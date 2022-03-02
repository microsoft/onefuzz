// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::local::common::UiEvent;
use anyhow::Result;
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
    io::{self, Stdout},
    iter::once,
    mem::{discriminant, Discriminant},
    path::PathBuf,
    thread::{self, JoinHandle},
    time::Duration,
};

use flume::{Receiver, Sender};
use tokio::{
    sync::broadcast::{self, error::TryRecvError},
    time::sleep,
};
use tui::{
    backend::CrosstermBackend,
    layout::{Alignment, Constraint, Corner, Direction, Layout},
    style::{Color, Modifier, Style},
    text::{Span, Spans},
    widgets::{Block, Borders},
    widgets::{Gauge, List, ListItem, ListState, Paragraph, Wrap},
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
const FILE_MONITOR_POLLING_PERIOD: Duration = Duration::from_secs(5);
const EVENT_POLLING_PERIOD: Duration = Duration::from_secs(1);

#[derive(Debug, Default)]
#[allow(dead_code)]
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
    Telemetry(Vec<EventData>),
}

struct UiLoopState {
    pub logs: ArrayDeque<[(Level, String); LOGS_BUFFER_SIZE], Wrapping>,
    pub file_count: HashMap<PathBuf, usize>,
    pub file_count_state: ListState,
    pub file_monitors: HashMap<PathBuf, tokio::task::JoinHandle<Result<()>>>,
    pub log_event_receiver: Receiver<(Level, String)>,
    pub terminal: Terminal<CrosstermBackend<Stdout>>,
    pub cancellation_tx: broadcast::Sender<()>,
    pub events: HashMap<Discriminant<EventData>, EventData>,
}

impl UiLoopState {
    fn new(
        terminal: Terminal<CrosstermBackend<Stdout>>,
        log_event_receiver: Receiver<(Level, String)>,
    ) -> Self {
        let (cancellation_tx, _) = broadcast::channel(1);
        let events = HashMap::new();
        Self {
            log_event_receiver,
            logs: Default::default(),
            file_count: Default::default(),
            file_count_state: Default::default(),
            file_monitors: Default::default(),
            terminal,
            cancellation_tx,
            events,
        }
    }
}

pub struct TerminalUi {
    pub task_events: Sender<UiEvent>,
    task_event_receiver: Receiver<UiEvent>,
    ui_event_tx: Sender<TerminalEvent>,
    ui_event_rx: Receiver<TerminalEvent>,
}

impl TerminalUi {
    pub fn init() -> Result<Self> {
        let (task_event_sender, task_event_receiver) = flume::unbounded();
        let (ui_event_tx, ui_event_rx) = flume::unbounded();
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
        let (log_event_sender, log_event_receiver) = flume::unbounded();
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
                tokio::time::sleep(timeout).await;
                let _ = ui_event_tx.send(TerminalEvent::Quit);
            });
        }

        try_wait_all_join_handles(task_handles).await?;
        Ok(())
    }

    fn filter_event(event: &EventData) -> bool {
        matches!(
            event,
            EventData::Features(_)
                | EventData::Covered(_)
                | EventData::Rate(_)
                | EventData::Count(_)
                | EventData::ExecsSecond(_)
                | EventData::VirtualMemory(_)
                | EventData::PhysicalMemory(_)
                | EventData::CpuUsage(_)
                | EventData::Coverage(_)
                | EventData::CoveragePaths(_)
                | EventData::CoveragePathsFavored(_)
                | EventData::CoveragePathsFound(_)
                | EventData::CoveragePathsImported(_)
                | EventData::CoverageMaxDepth(_)
        )
    }

    async fn listen_telemetry_event(
        ui_event_tx: Sender<TerminalEvent>,
        mut cancellation_rx: broadcast::Receiver<()>,
    ) -> Result<()> {
        let mut rx = onefuzz_telemetry::subscribe_to_events();

        while cancellation_rx.try_recv() == Err(broadcast::error::TryRecvError::Empty) {
            match rx.try_recv() {
                Ok((_event, data)) => {
                    let data = data
                        .into_iter()
                        .filter(Self::filter_event)
                        .collect::<Vec<_>>();
                    let _ = ui_event_tx.send(TerminalEvent::Telemetry(data));
                }
                Err(TryRecvError::Empty) => sleep(EVENT_POLLING_PERIOD).await,
                Err(TryRecvError::Lagged(_)) => continue,
                Err(TryRecvError::Closed) => break,
            }
        }
        Ok(())
    }

    async fn ticking(
        ui_event_tx: Sender<TerminalEvent>,
        mut cancellation_rx: broadcast::Receiver<()>,
    ) -> Result<()> {
        let mut interval = tokio::time::interval(TICK_RATE);
        while Err(broadcast::error::TryRecvError::Empty) == cancellation_rx.try_recv() {
            interval.tick().await;
            if let Err(_err) = ui_event_tx.send(TerminalEvent::Tick) {
                break;
            }
        }
        Ok(())
    }

    fn read_keyboard_events(
        ui_event_tx: Sender<TerminalEvent>,
        mut cancellation_rx: broadcast::Receiver<()>,
    ) -> JoinHandle<Result<()>> {
        thread::spawn(move || {
            while Err(broadcast::error::TryRecvError::Empty) == cancellation_rx.try_recv() {
                if event::poll(EVENT_POLLING_PERIOD)? {
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
        ui_event_tx: Sender<TerminalEvent>,
        external_event_rx: Receiver<UiEvent>,
        mut cancellation_rx: broadcast::Receiver<()>,
    ) -> Result<()> {
        while Err(broadcast::error::TryRecvError::Empty) == cancellation_rx.try_recv() {
            match external_event_rx.try_recv() {
                Ok(UiEvent::MonitorDir(dir)) => {
                    if ui_event_tx.send(TerminalEvent::MonitorDir(dir)).is_err() {
                        break;
                    }
                }
                Err(flume::TryRecvError::Empty) => sleep(EVENT_POLLING_PERIOD).await,
                Err(flume::TryRecvError::Disconnected) => break,
            }
        }
        Ok(())
    }

    fn take_available_logs<T>(
        receiver: &mut Receiver<T>,
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

    fn create_coverage_gauge<'a>(rate: f64) -> Gauge<'a> {
        let label = format!("coverage {:.2}%", rate * 100.0);
        Gauge::default()
            .gauge_style(
                Style::default()
                    .fg(Color::White)
                    .bg(Color::Black)
                    .add_modifier(Modifier::ITALIC | Modifier::BOLD),
            )
            .label(label)
            .ratio(rate)
    }

    fn create_stats_paragraph(
        events: &HashMap<Discriminant<EventData>, EventData>,
    ) -> Paragraph<'_> {
        let mut event_values = events.values().map(|v| v.as_values()).collect::<Vec<_>>();

        event_values.sort_by(|(a, _), (b, _)| a.cmp(b));

        let mut stats_spans = once(Span::styled(
            "Stats: ",
            Style::default().add_modifier(Modifier::BOLD),
        ))
        .chain(event_values.into_iter().flat_map(|(name, value)| {
            vec![
                Span::raw(name),
                Span::raw(" "),
                Span::styled(value, Style::default().add_modifier(Modifier::BOLD)),
                Span::raw(", "),
            ]
        }))
        .collect::<Vec<_>>();

        if stats_spans.len() > 1 {
            // removing the last ","
            stats_spans.pop();
        }

        Paragraph::new(Spans::from(stats_spans))
            .style(Style::default())
            .alignment(Alignment::Left)
            .wrap(Wrap { trim: true })
    }

    fn create_file_count_paragraph(file_count: &HashMap<PathBuf, usize>) -> Paragraph<'_> {
        let mut sorted_file_count = file_count.iter().collect::<Vec<_>>();

        sorted_file_count.sort_by(|(p1, _), (p2, _)| p1.cmp(p2));

        let mut files_spans = once(Span::styled(
            "Files: ",
            Style::default().add_modifier(Modifier::BOLD),
        ))
        .chain(sorted_file_count.iter().flat_map(|(path, count)| {
            vec![
                Span::raw(
                    path.file_name()
                        .map(|f| f.to_string_lossy())
                        .unwrap_or_default(),
                ),
                Span::raw(" "),
                Span::styled(
                    format!("{}", count),
                    Style::default().add_modifier(Modifier::BOLD),
                ),
                Span::raw(", "),
            ]
        }))
        .collect::<Vec<_>>();

        if files_spans.len() > 1 {
            files_spans.pop();
        } // removing the last ","

        Paragraph::new(Spans::from(files_spans))
            .style(Style::default())
            .alignment(Alignment::Left)
            .wrap(Wrap { trim: true })
    }

    fn create_log_list(
        logs: &ArrayDeque<[(Level, String); LOGS_BUFFER_SIZE], Wrapping>,
    ) -> List<'_> {
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

        List::new(log_items)
            .block(Block::default().borders(Borders::TOP).title("Logs"))
            .start_corner(Corner::BottomLeft)
    }

    async fn refresh_ui(ui_state: UiLoopState) -> Result<UiLoopState, UiLoopError> {
        let mut logs = ui_state.logs;
        let file_count = ui_state.file_count;
        let mut log_event_receiver = ui_state.log_event_receiver;
        let mut terminal = ui_state.terminal;
        let rate = ui_state
            .events
            .get(&discriminant(&EventData::Rate(0.0)))
            .and_then(|x| {
                if let EventData::Rate(r) = x {
                    Some(*r)
                } else {
                    None
                }
            });

        let events = ui_state.events;

        Self::take_available_logs(&mut log_event_receiver, 10, &mut logs);
        terminal.draw(|f| {
            let chunks = Layout::default()
                .direction(Direction::Vertical)
                .constraints([Constraint::Percentage(25), Constraint::Percentage(75)].as_ref())
                .split(f.size());

            let log_area = chunks[1];
            let top_area = Layout::default()
                .direction(Direction::Vertical)
                .constraints([Constraint::Percentage(50), Constraint::Percentage(50)].as_ref())
                .split(chunks[0]);

            let file_count_area = top_area[0];

            if let Some(rate) = rate {
                let coverage_area = Layout::default()
                    .direction(Direction::Vertical)
                    .constraints([Constraint::Percentage(25), Constraint::Percentage(75)].as_ref())
                    .split(top_area[1]);

                let gauge = Self::create_coverage_gauge(rate);
                f.render_widget(gauge, coverage_area[0]);
                let stats_paragraph = Self::create_stats_paragraph(&events);
                f.render_widget(stats_paragraph, coverage_area[1]);
            } else {
                let stats_paragraph = Self::create_stats_paragraph(&events);
                f.render_widget(stats_paragraph, top_area[1]);
            }

            let file_count_paragraph = Self::create_file_count_paragraph(&file_count);
            f.render_widget(file_count_paragraph, file_count_area);

            let log_list = Self::create_log_list(&logs);
            f.render_widget(log_list, log_area);
        })?;
        Ok(UiLoopState {
            logs,
            file_count,
            log_event_receiver,
            terminal,
            events,
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
        ui_event_tx: Sender<TerminalEvent>,
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
        ui_event_rx: Receiver<TerminalEvent>,
        ui_event_tx: Sender<TerminalEvent>,
    ) -> Result<()> {
        let loop_result = ui_event_rx
            .stream()
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
                    TerminalEvent::Telemetry(event_data) => {
                        let mut events = ui_state.events;
                        for e in event_data {
                            events.insert(discriminant(&e), e);
                        }

                        Ok(UiLoopState { events, ..ui_state })
                    }
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
        sender: Sender<TerminalEvent>,
        mut cancellation_rx: broadcast::Receiver<()>,
    ) -> tokio::task::JoinHandle<Result<()>> {
        tokio::spawn(async move {
            wait_for_dir(&dir).await?;
            while cancellation_rx.try_recv() == Err(broadcast::error::TryRecvError::Empty) {
                let mut rd = tokio::fs::read_dir(&dir).await?;
                let mut count: usize = 0;

                while let Ok(Some(entry)) = rd.next_entry().await {
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

                sleep(FILE_MONITOR_POLLING_PERIOD).await;
            }
            Ok(())
        })
    }
}
