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
use std::{
    collections::HashMap,
    io::{self, Stdout, Write},
    path::PathBuf,
    thread::{self, JoinHandle},
    time::Duration,
};
use tokio::{
    sync::mpsc::{self, UnboundedReceiver},
    time,
};
use tui::{
    backend::CrosstermBackend,
    layout::{Constraint, Corner, Direction, Layout},
    style::{Color, Modifier, Style},
    text::{Span, Spans},
    widgets::{Block, Borders},
    widgets::{List, ListItem, ListState},
    Terminal,
};

use arraydeque::{ArrayDeque, Wrapping};

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

/// Event driving the refresh of the UI
#[derive(Debug)]
enum TerminalEvent {
    Input(Event),
    Tick,
    FileCount { dir: PathBuf, count: usize },
    Quit,
}

struct UiLoopState {
    pub logs: ArrayDeque<[(Level, String); LOGS_BUFFER_SIZE], Wrapping>,
    pub file_count: HashMap<PathBuf, usize>,
    pub file_count_state: ListState,
    pub file_monitors: Vec<JoinHandle<Result<()>>>,
    pub log_event_receiver: mpsc::UnboundedReceiver<(Level, String)>,
    pub terminal: Terminal<CrosstermBackend<Stdout>>,
}

impl UiLoopState {
    fn new(
        terminal: Terminal<CrosstermBackend<Stdout>>,
        log_event_receiver: mpsc::UnboundedReceiver<(Level, String)>,
    ) -> Self {
        Self {
            log_event_receiver,
            logs: Default::default(),
            file_count: Default::default(),
            file_count_state: Default::default(),
            file_monitors: Default::default(),
            terminal,
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
        let tick_event_handle =
            tokio::spawn(async { Self::ticking(tick_event_tx_clone).await.context("ticking") });

        let keyboard_ui_event_tx = self.ui_event_tx.clone();
        let _keyboard_event_handle = Self::read_keyboard_events(keyboard_ui_event_tx);

        let task_event_receiver = self.task_event_receiver;
        let ui_event_tx = self.ui_event_tx.clone();
        let external_event_handle =
            tokio::spawn(Self::read_commands(ui_event_tx, task_event_receiver));

        let ui_loop = tokio::spawn(Self::ui_loop(initial_state, self.ui_event_rx));

        let mut task_handles = vec![tick_event_handle, ui_loop, external_event_handle];

        if let Some(timeout) = timeout {
            let ui_event_tx = self.ui_event_tx.clone();
            let timeout_task = tokio::spawn(async move {
                time::delay_for(timeout).await;
                let _ = ui_event_tx.send(TerminalEvent::Quit);
                Ok(())
            });
            task_handles.push(timeout_task);
        }

        try_wait_all_join_handles(task_handles)
            .await
            .context("ui_loop")?;
        Ok(())
    }

    async fn ticking(ui_event_tx: mpsc::UnboundedSender<TerminalEvent>) -> Result<()> {
        let mut interval = tokio::time::interval(TICK_RATE);
        loop {
            interval.tick().await;
            if let Err(_err) = ui_event_tx.send(TerminalEvent::Tick) {
                break;
            }
        }
        Ok(())
    }

    fn read_keyboard_events(
        ui_event_tx: mpsc::UnboundedSender<TerminalEvent>,
    ) -> JoinHandle<Result<()>> {
        thread::spawn(move || loop {
            if event::poll(Duration::from_secs(1))? {
                let event = event::read()?;
                if let Err(_err) = ui_event_tx.send(TerminalEvent::Input(event)) {
                    return Ok(());
                }
            }
        })
    }

    async fn read_commands(
        ui_event_tx: mpsc::UnboundedSender<TerminalEvent>,
        mut external_event_rx: mpsc::UnboundedReceiver<UiEvent>,
    ) -> Result<()> {
        while let Some(UiEvent::FileCount { dir, count }) = external_event_rx.recv().await {
            if ui_event_tx
                .send(TerminalEvent::FileCount { dir, count })
                .is_err()
            {
                break;
            }
        }
        Ok(())
    }

    fn take_available_logs<T>(
        receiver: &mut UnboundedReceiver<T>,
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

        Self::take_available_logs(&mut log_event_receiver, 10, &mut logs);
        terminal.draw(|f| {
            let chunks = Layout::default()
                .direction(Direction::Vertical)
                .constraints([Constraint::Percentage(25), Constraint::Percentage(75)].as_ref())
                .split(f.size());

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

            f.render_stateful_widget(log_list, chunks[0], &mut file_count_state);

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

            f.render_widget(log_list, chunks[1]);
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

    async fn on_quit(ui_state: UiLoopState) -> Result<UiLoopState, UiLoopError> {
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

    async fn ui_loop(
        initial_state: UiLoopState,
        ui_event_rx: mpsc::UnboundedReceiver<TerminalEvent>,
    ) -> Result<()> {
        let loop_result = ui_event_rx
            .map(Ok)
            .try_fold(initial_state, |ui_state, event| async {
                match event {
                    TerminalEvent::Tick => Self::refresh_ui(ui_state).await,
                    TerminalEvent::Input(Event::Key(k)) => match k.code {
                        KeyCode::Char('q') => Self::on_quit(ui_state).await,
                        KeyCode::Down => Self::on_key_down(ui_state).await,
                        KeyCode::Up => Self::on_key_up(ui_state).await,
                        _ => Ok(ui_state),
                    },
                    TerminalEvent::FileCount { dir, count } => {
                        Self::on_file_count(ui_state, dir, count).await
                    }
                    TerminalEvent::Quit => Self::on_quit(ui_state).await,
                    _ => Ok(ui_state),
                }
            })
            .await;

        match loop_result {
            Err(UiLoopError::Exit) | Ok(_) => Ok(()),
            Err(UiLoopError::Anyhow(e)) => Err(e),
        }
    }
}
