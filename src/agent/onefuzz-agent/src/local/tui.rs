// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::local::common::UiEvent;
use anyhow::{Context, Result};
use crossterm::{
    event::{self, Event, KeyCode},
    execute,
    terminal::{disable_raw_mode, enable_raw_mode, EnterAlternateScreen, LeaveAlternateScreen},
};
use futures::{StreamExt, TryStreamExt};
use log::Level;
use onefuzz::utils::try_wait_all_join_handles;
use onefuzz_telemetry::{self, EventData};
use std::{
    collections::HashMap,
    io::{self, Stdout, Write},
    path::PathBuf,
    thread::{self, JoinHandle},
    time::Duration,
};
use tokio::{
    sync::{
        broadcast::{self, RecvError},
        mpsc::{self, UnboundedSender},
    },
    time::delay_for,
};
use tui::{
    backend::CrosstermBackend,
    layout::{Constraint, Corner, Direction, Layout},
    style::{Color, Modifier, Style},
    text::{Span, Spans},
    widgets::{Block, Borders},
    widgets::{Gauge, List, ListItem, ListState},
    Terminal,
};

use arraydeque::{ArrayDeque, Wrapping};

use super::common::wait_for_dir;

#[derive(Debug, thiserror::Error)]
enum UiLoopError {
    #[error("program exiting")]
    Exit,
    #[error("error")]
    Anyhow(anyhow::Error),
}

impl From<anyhow::Error> for UiLoopError {
    fn from(e: anyhow::Error) -> Self {
        Self::Anyhow(e)
    }
}

impl From<std::io::Error> for UiLoopError {
    fn from(e: std::io::Error) -> Self {
        Self::Anyhow(e.into())
    }
}

/// Maximum number of log message to display, arbitrarily chosen
const LOGS_BUFFER_SIZE: usize = 100;
const TICK_RATE: Duration = Duration::from_millis(250);

#[derive(Debug, Default)]
struct CoverageData {
    covered: Option<u64>,
    features: Option<u64>,
    rate: Option<f64>,
}

/// Event driving the refresh of the UI
#[derive(Debug)]
enum TerminalEvent {
    Input(Event),
    Tick,
    FileCount { dir: PathBuf, count: usize },
    Quit,
    MonitorDir(PathBuf),
    Coverage(CoverageData),
}

struct UiLoopState {
    pub logs: ArrayDeque<[(Level, String); LOGS_BUFFER_SIZE], Wrapping>,
    pub file_count: HashMap<PathBuf, usize>,
    pub file_count_state: ListState,
    pub file_monitors: HashMap<PathBuf, tokio::task::JoinHandle<Result<()>>>,
    pub log_event_receiver: mpsc::UnboundedReceiver<(Level, String)>,
    pub terminal: Terminal<CrosstermBackend<Stdout>>,
    pub coverage: CoverageData,
    pub cancellation_tx: broadcast::Sender<()>,
}

impl UiLoopState {
    fn new(
        terminal: Terminal<CrosstermBackend<Stdout>>,
        log_event_receiver: mpsc::UnboundedReceiver<(Level, String)>,
    ) -> Self {
        let (cancellation_tx, _) = broadcast::channel(1);
        Self {
            log_event_receiver,
            logs: Default::default(),
            file_count: Default::default(),
            file_count_state: Default::default(),
            file_monitors: Default::default(),
            terminal,
            coverage: Default::default(),
            cancellation_tx,
        }
    }
}

pub struct TerminalUi {
    pub task_events: mpsc::UnboundedSender<UiEvent>,
    task_event_receiver: mpsc::UnboundedReceiver<UiEvent>,
    ui_event_tx: mpsc::UnboundedSender<TerminalEvent>,
    ui_event_rx: mpsc::UnboundedReceiver<TerminalEvent>,
}

impl TerminalUi {
    pub fn init() -> Result<Self> {
        let (task_event_sender, task_event_receiver) = mpsc::unbounded_channel();
        let (ui_event_tx, ui_event_rx) = mpsc::unbounded_channel();
        Ok(Self {
            task_events: task_event_sender,
            task_event_receiver,
            ui_event_tx,
            ui_event_rx,
        })
    }

    pub async fn run(self, timeout: Option<Duration>) -> Result<()> {
        enable_raw_mode()?;
        let mut stdout = io::stdout();
        execute!(stdout, EnterAlternateScreen)?;

        let backend = CrosstermBackend::new(stdout);
        let mut terminal = Terminal::new(backend)?;
        terminal.clear()?;
        let (log_event_sender, log_event_receiver) = mpsc::unbounded_channel();
        let initial_state = UiLoopState::new(terminal, log_event_receiver);

        env_logger::Builder::from_env(env_logger::Env::default().default_filter_or("info"))
            .format(move |_buf, record| {
                let _r = log_event_sender.send((record.level(), format!("{}", record.args())));
                Ok(())
            })
            .init();

        let tick_event_tx_clone = self.ui_event_tx.clone();
        let tick_event_handle = tokio::spawn(Self::ticking(
            tick_event_tx_clone,
            initial_state.cancellation_tx.subscribe(),
        ));

        let keyboard_ui_event_tx = self.ui_event_tx.clone();
        let _keyboard_event_handle = Self::read_keyboard_events(
            keyboard_ui_event_tx,
            initial_state.cancellation_tx.subscribe(),
        );

        let task_event_receiver = self.task_event_receiver;
        let ui_event_tx = self.ui_event_tx.clone();

        let external_event_handle = tokio::spawn(Self::read_commands(
            ui_event_tx,
            task_event_receiver,
            initial_state.cancellation_tx.subscribe(),
        ));

        let mut task_handles = vec![tick_event_handle, external_event_handle];

        let ui_event_tx = self.ui_event_tx.clone();
        let telemetry = tokio::spawn(Self::listen_telemetry_event(
            ui_event_tx,
            initial_state.cancellation_tx.subscribe(),
        ));

        task_handles.push(telemetry);

        let ui_loop = tokio::spawn(Self::ui_loop(
            initial_state,
            self.ui_event_rx,
            self.ui_event_tx.clone(),
        ));

        task_handles.push(ui_loop);

        if let Some(timeout) = timeout {
            let ui_event_tx = self.ui_event_tx.clone();
            tokio::spawn(async move {
                tokio::time::delay_for(timeout).await;
                let _ = ui_event_tx.send(TerminalEvent::Quit);
            });
        }

        try_wait_all_join_handles(task_handles).await?;
        Ok(())
    }

    async fn listen_telemetry_event(
        ui_event_tx: UnboundedSender<TerminalEvent>,
        mut cancellation_rx: broadcast::Receiver<()>,
    ) -> Result<()> {
        let mut rx = onefuzz_telemetry::subscribe_to_events();

        while cancellation_rx.try_recv() == Err(broadcast::TryRecvError::Empty) {
            match rx.recv().await {
                Ok((event, data)) => {
                    if let onefuzz_telemetry::Event::coverage_data = event {
                        let (covered, features, rate) =
                            data.iter()
                                .cloned()
                                .fold((None, None, None), |(c, f, r), d| match d {
                                    EventData::Covered(value) => (Some(value), f, r),
                                    EventData::Features(value) => (c, Some(value), r),
                                    EventData::Rate(value) => (c, f, Some(value)),
                                    _ => (c, f, r),
                                });

                        let _ = ui_event_tx.send(TerminalEvent::Coverage(CoverageData {
                            covered,
                            features,
                            rate,
                        }));
                    }
                }

                Err(RecvError::Lagged(_)) => continue,
                Err(RecvError::Closed) => break,
            }
        }
        Ok(())
    }

    async fn ticking(
        ui_event_tx: mpsc::UnboundedSender<TerminalEvent>,
        mut cancellation_rx: broadcast::Receiver<()>,
    ) -> Result<()> {
        let mut interval = tokio::time::interval(TICK_RATE);
        while Err(broadcast::TryRecvError::Empty) == cancellation_rx.try_recv() {
            interval.tick().await;
            if let Err(_err) = ui_event_tx.send(TerminalEvent::Tick) {
                break;
            }
        }
        Ok(())
    }

    fn read_keyboard_events(
        ui_event_tx: mpsc::UnboundedSender<TerminalEvent>,
        mut cancellation_rx: broadcast::Receiver<()>,
    ) -> JoinHandle<Result<()>> {
        thread::spawn(move || {
            while Err(broadcast::TryRecvError::Empty) == cancellation_rx.try_recv() {
                if event::poll(Duration::from_secs(1))? {
                    let event = event::read()?;
                    if let Err(_err) = ui_event_tx.send(TerminalEvent::Input(event)) {
                        return Ok(());
                    }
                }
            }
            Ok(())
        })
    }

    async fn read_commands(
        ui_event_tx: mpsc::UnboundedSender<TerminalEvent>,
        mut external_event_rx: mpsc::UnboundedReceiver<UiEvent>,
        mut cancellation_rx: broadcast::Receiver<()>,
    ) -> Result<()> {
        while Err(broadcast::TryRecvError::Empty) == cancellation_rx.try_recv() {
            match external_event_rx.try_recv() {
                Ok(UiEvent::MonitorDir(dir)) => {
                    if ui_event_tx.send(TerminalEvent::MonitorDir(dir)).is_err() {
                        break;
                    }
                }
                Err(mpsc::error::TryRecvError::Empty) => delay_for(Duration::from_secs(1)).await,
                Err(mpsc::error::TryRecvError::Closed) => break,
            }
        }
        Ok(())
    }

    fn take_available_logs<T>(
        receiver: &mut mpsc::UnboundedReceiver<T>,
        size: usize,
        buffer: &mut ArrayDeque<[T; LOGS_BUFFER_SIZE], Wrapping>,
    ) {
        let mut count = 0;
        while let Ok(v) = receiver.try_recv() {
            count += 1;
            buffer.push_front(v);
            if count >= size {
                break;
            }
        }
    }

    async fn refresh_ui(ui_state: UiLoopState) -> Result<UiLoopState, UiLoopError> {
        let mut logs = ui_state.logs;
        let mut file_count_state = ui_state.file_count_state;
        let file_count = ui_state.file_count;
        let mut log_event_receiver = ui_state.log_event_receiver;
        let mut terminal = ui_state.terminal;
        let rate = ui_state.coverage.rate.unwrap_or(0.0);
        let features = ui_state.coverage.features.unwrap_or(0);
        let covered = ui_state.coverage.covered.unwrap_or(0);

        Self::take_available_logs(&mut log_event_receiver, 10, &mut logs);
        terminal.draw(|f| {
            let chunks = Layout::default()
                .direction(Direction::Vertical)
                .constraints([Constraint::Percentage(25), Constraint::Percentage(75)].as_ref())
                .split(f.size());

            let log_area = chunks[1];
            let top_area = Layout::default()
                .direction(Direction::Horizontal)
                .constraints([Constraint::Percentage(50), Constraint::Percentage(50)].as_ref())
                .split(chunks[0]);

            let file_count_area = top_area[0];
            let coverage_area = Layout::default()
                .direction(Direction::Vertical)
                .constraints([Constraint::Percentage(25), Constraint::Percentage(75)].as_ref())
                .margin(1)
                .split(top_area[1]);

            let label = format!("{:.2}%", rate * 100.0);

            let coverage_block = Block::default().borders(Borders::ALL).title("Coverage:");

            f.render_widget(coverage_block, top_area[1]);

            let gauge = Gauge::default()
                .gauge_style(
                    Style::default()
                        .fg(Color::Magenta)
                        .bg(Color::Black)
                        .add_modifier(Modifier::ITALIC | Modifier::BOLD),
                )
                .label(label)
                .ratio(rate);
            f.render_widget(gauge, coverage_area[0]);

            let coverage_info = List::new([
                ListItem::new(Spans::from(vec![
                    Span::raw("features: "),
                    Span::raw(format!("{}", features)),
                ])),
                ListItem::new(Spans::from(vec![
                    Span::raw("covered: "),
                    Span::raw(format!("{}", covered)),
                ])),
            ]);

            f.render_widget(coverage_info, coverage_area[1]);

            let mut sorted_file_count = file_count.iter().collect::<Vec<_>>();

            sorted_file_count.sort_by(|(p1, _), (p2, _)| p1.cmp(p2));

            let files = sorted_file_count
                .iter()
                .map(|(path, count)| {
                    ListItem::new(Spans::from(vec![
                        Span::raw(
                            path.file_name()
                                .map(|f| f.to_string_lossy())
                                .unwrap_or_default(),
                        ),
                        Span::raw(": "),
                        Span::raw(format!("{}", count)),
                    ]))
                })
                .collect::<Vec<_>>();

            let log_list = List::new(files)
                .block(Block::default().borders(Borders::ALL).title("files"))
                .highlight_style(Style::default().add_modifier(Modifier::BOLD))
                .start_corner(Corner::TopLeft);

            f.render_stateful_widget(log_list, file_count_area, &mut file_count_state);

            let log_items = logs
                .iter()
                .map(|(level, log)| {
                    let style = match level {
                        Level::Debug => Style::default().fg(Color::Magenta),
                        Level::Error => Style::default().fg(Color::Red),
                        Level::Warn => Style::default().fg(Color::Yellow),
                        Level::Info => Style::default().fg(Color::Blue),
                        Level::Trace => Style::default(),
                    };

                    ListItem::new(Spans::from(vec![
                        Span::styled(format!("{:<9}", level), style),
                        Span::raw(" "),
                        Span::raw(log),
                    ]))
                })
                .collect::<Vec<_>>();

            let log_list = List::new(log_items)
                .block(Block::default().borders(Borders::ALL).title("Logs"))
                .start_corner(Corner::BottomLeft);

            f.render_widget(log_list, log_area);
        })?;
        Ok(UiLoopState {
            logs,
            file_count_state,
            file_count,
            terminal,
            log_event_receiver,
            ..ui_state
        })
    }

    async fn on_key_down(ui_state: UiLoopState) -> Result<UiLoopState, UiLoopError> {
        let mut file_count_state = ui_state.file_count_state;
        let count = ui_state.file_count.len();
        let i = file_count_state
            .selected()
            .map(|i| {
                if count == 0 {
                    0
                } else {
                    (i + count + 1) % count
                }
            })
            .unwrap_or_default();

        file_count_state.select(Some(i));
        Ok(UiLoopState {
            file_count_state,
            ..ui_state
        })
    }

    async fn on_key_up(ui_state: UiLoopState) -> Result<UiLoopState, UiLoopError> {
        let mut file_count_state = ui_state.file_count_state;
        let count = ui_state.file_count.len();
        let i = file_count_state
            .selected()
            .map(|i| {
                if count == 0 {
                    0
                } else {
                    (i + count - 1) % count
                }
            })
            .unwrap_or_default();
        file_count_state.select(Some(i));
        Ok(UiLoopState {
            file_count_state,
            ..ui_state
        })
    }

    async fn on_quit(
        ui_state: UiLoopState,
        cancellation_tx: broadcast::Sender<()>,
    ) -> Result<UiLoopState, UiLoopError> {
        let _ = cancellation_tx.send(());
        let file_monitors = ui_state.file_monitors.into_iter().map(|(_, v)| v).collect();
        tokio::time::timeout(
            Duration::from_secs(10),
            try_wait_all_join_handles(file_monitors),
        )
        .await
        .context("failed to close file monitoring tasks")?
        .context("file monitoring task terminated with error")?;
        let mut terminal = ui_state.terminal;
        disable_raw_mode().map_err(|e| anyhow!("{:?}", e))?;
        execute!(terminal.backend_mut(), LeaveAlternateScreen).map_err(|e| anyhow!("{:?}", e))?;
        terminal.show_cursor()?;

        Err(UiLoopError::Exit)
    }

    async fn on_file_count(
        ui_state: UiLoopState,
        dir: PathBuf,
        count: usize,
    ) -> Result<UiLoopState, UiLoopError> {
        let mut file_count = ui_state.file_count;
        file_count.insert(dir, count);
        Ok(UiLoopState {
            file_count,
            ..ui_state
        })
    }

    async fn on_monitor_dir(
        ui_state: UiLoopState,
        path: PathBuf,
        ui_event_tx: mpsc::UnboundedSender<TerminalEvent>,
        cancellation_rx: broadcast::Receiver<()>,
    ) -> Result<UiLoopState, UiLoopError> {
        let mut file_monitors = ui_state.file_monitors;

        file_monitors.entry(path).or_insert_with_key(|path| {
            Self::spawn_file_count_monitor(path.clone(), ui_event_tx, cancellation_rx)
        });

        Ok(UiLoopState {
            file_monitors,
            ..ui_state
        })
    }

    async fn ui_loop(
        initial_state: UiLoopState,
        ui_event_rx: mpsc::UnboundedReceiver<TerminalEvent>,
        ui_event_tx: mpsc::UnboundedSender<TerminalEvent>,
    ) -> Result<()> {
        let loop_result = ui_event_rx
            .map(Ok)
            .try_fold(initial_state, |ui_state, event| async {
                let ui_event_tx = ui_event_tx.clone();
                let cancellation_tx = ui_state.cancellation_tx.clone();
                match event {
                    TerminalEvent::Tick => Self::refresh_ui(ui_state).await,
                    TerminalEvent::Input(Event::Key(k)) => match k.code {
                        KeyCode::Char('q') => Self::on_quit(ui_state, cancellation_tx).await,
                        KeyCode::Down => Self::on_key_down(ui_state).await,
                        KeyCode::Up => Self::on_key_up(ui_state).await,
                        _ => Ok(ui_state),
                    },
                    TerminalEvent::FileCount { dir, count } => {
                        Self::on_file_count(ui_state, dir, count).await
                    }
                    TerminalEvent::Quit => Self::on_quit(ui_state, cancellation_tx).await,
                    TerminalEvent::MonitorDir(path) => {
                        Self::on_monitor_dir(
                            ui_state,
                            path,
                            ui_event_tx,
                            cancellation_tx.subscribe(),
                        )
                        .await
                    }
                    TerminalEvent::Coverage(coverage) => Ok(UiLoopState {
                        coverage,
                        ..ui_state
                    }),
                    _ => Ok(ui_state),
                }
            })
            .await;

        match loop_result {
            Err(UiLoopError::Exit) | Ok(_) => Ok(()),
            Err(UiLoopError::Anyhow(e)) => Err(e),
        }
    }

    fn spawn_file_count_monitor(
        dir: PathBuf,
        sender: mpsc::UnboundedSender<TerminalEvent>,
        mut cancellation_rx: broadcast::Receiver<()>,
    ) -> tokio::task::JoinHandle<Result<()>> {
        tokio::spawn(async move {
            wait_for_dir(&dir).await?;
            while cancellation_rx.try_recv() == Err(broadcast::TryRecvError::Empty) {
                let mut rd = tokio::fs::read_dir(&dir).await?;
                let mut count: usize = 0;

                while let Some(Ok(entry)) = rd.next().await {
                    if entry.path().is_file() {
                        count += 1;
                    }
                }

                if sender
                    .send(TerminalEvent::FileCount {
                        dir: dir.clone(),
                        count,
                    })
                    .is_err()
                {
                    break;
                }
                delay_for(Duration::from_secs(5)).await;
            }
            Ok(())
        })
    }
}
