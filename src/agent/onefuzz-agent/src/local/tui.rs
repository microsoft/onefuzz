// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::local::common::UiEvent;
use anyhow::{Context, Result};
use crossterm::{
    event::{self, DisableMouseCapture, EnableMouseCapture, Event, KeyCode},
    execute,
    terminal::{disable_raw_mode, enable_raw_mode, EnterAlternateScreen, LeaveAlternateScreen},
};
use futures::{StreamExt, TryStreamExt};
use log::Level;
use onefuzz::utils::try_wait_all_join_handles;
use std::{
    cmp::Ordering,
    collections::HashMap,
    error::Error,
    io::{self, Stdout, Write},
    path::PathBuf,
    sync::Arc,
    time::Duration,
};
use tokio::{
    sync::{
        mpsc::{self, UnboundedSender},
        Mutex,
    },
    task::JoinHandle,
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
use async_trait::async_trait;
use thiserror;

#[derive(Debug, thiserror::Error)]
enum UILoopError {
    #[error("program exiting")]
    Exit,
    #[error("error")]
    Anyhow(anyhow::Error),
}

impl From<anyhow::Error> for UILoopError {
    fn from(e: anyhow::Error) -> Self {
        Self::Anyhow(e)
    }
}

impl From<std::io::Error> for UILoopError {
    fn from(e: std::io::Error) -> Self {
        Self::Anyhow(e.into())
    }
}

const BUFFER_SIZE: usize = 100;
const TICK_RATE: Duration = Duration::from_millis(250);

pub struct TerminalUi {
    pub task_events: UnboundedSender<UiEvent>,
    task_event_receiver: mpsc::UnboundedReceiver<UiEvent>,
    log_event_receiver: mpsc::UnboundedReceiver<(Level, String)>,
}

/// Event driving the refresh of the UI
#[derive(Debug)]
enum TerminalEvent {
    Input(Event),
    Tick,
    FileCount { dir: PathBuf, count: usize },
}

trait TakeAvailable<T> {
    fn take_available(&mut self, size: usize) -> Result<Vec<T>>;
}

#[async_trait]
impl<T: Send + Sync> TakeAvailable<T> for mpsc::UnboundedReceiver<T> {
    fn take_available(&mut self, size: usize) -> Result<Vec<T>> {
        let mut result = vec![];
        while let Ok(v) = self.try_recv() {
            result.push(v);
            if result.len() >= size {
                break;
            }
        }
        Ok(result)
    }
}

impl TerminalUi {
    pub fn init() -> Result<Self> {
        let (task_event_sender, task_event_receiver) = mpsc::unbounded_channel();
        let (log_event_sender, log_event_receiver) = mpsc::unbounded_channel();
        let log_event_sender = Arc::new(Mutex::new(log_event_sender));
        env_logger::Builder::from_env(env_logger::Env::default().default_filter_or("info"))
            .format(move |_buf, record| {
                let sender_lock = log_event_sender.try_lock().map_err(|err| {
                    std::io::Error::new(std::io::ErrorKind::Other, err.to_string())
                })?;
                let level = record.level();
                sender_lock
                    .send((level, format!("{}", record.args())))
                    .map_err(|err| std::io::Error::new(std::io::ErrorKind::Other, err.to_string()))
            })
            .init();

        Ok(Self {
            task_events: task_event_sender,
            task_event_receiver,
            log_event_receiver,
        })
    }

    async fn read_ui_events(ui_event_tx: UnboundedSender<TerminalEvent>) -> Result<()> {
        let mut interval = tokio::time::interval(TICK_RATE);
        loop {
            if event::poll(Duration::from_secs(0))? {
                let event = event::read()?;
                ui_event_tx.send(TerminalEvent::Input(event))?;
            } else {
                ui_event_tx.send(TerminalEvent::Tick)?;
                interval.tick().await;
            }
        }
    }

    async fn read_commands(
        ui_event_tx: mpsc::UnboundedSender<TerminalEvent>,
        mut external_event_rx: mpsc::UnboundedReceiver<UiEvent>,
    ) -> Result<()> {
        loop {
            match external_event_rx.recv().await {
                Some(UiEvent::FileCount { dir, count }) => {
                    ui_event_tx.send(TerminalEvent::FileCount { dir, count })?
                }
                None => break,
            }
        }
        Ok(())
    }

    async fn ui_loop(
        initial_state: UILoopState,
        ui_event_rx: mpsc::UnboundedReceiver<TerminalEvent>,
    ) -> Result<()> {
        let loop_result = ui_event_rx
            .map(Ok)
            .try_fold(initial_state, |ui_state, event| async {
                match event {
                    TerminalEvent::Tick => {
                        let mut logs = ui_state.logs;
                        let mut file_count_state = ui_state.file_count_state;
                        let file_count = ui_state.file_count;
                        let mut log_event_receiver = ui_state.log_event_receiver;
                        let mut terminal = ui_state.terminal;

                        logs.extend_front(log_event_receiver.take_available(10)?.into_iter().rev());
                        terminal.draw(|f| {
                            let chunks = Layout::default()
                                .direction(Direction::Vertical)
                                .constraints(
                                    [Constraint::Percentage(25), Constraint::Percentage(75)]
                                        .as_ref(),
                                )
                                .split(f.size());

                            let mut sorted_file_count = file_count.iter().collect::<Vec<_>>();

                            sorted_file_count.sort_by(|(p1, _), (p2, _)| {
                                if p1 == p2 {
                                    Ordering::Equal
                                } else if p1 > p2 {
                                    Ordering::Greater
                                } else {
                                    Ordering::Less
                                }
                            });

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
                        Ok(UILoopState {
                            logs,
                            file_count_state,
                            file_count,
                            terminal,
                            log_event_receiver,
                            ..ui_state
                        })
                    }
                    TerminalEvent::Input(Event::Key(k)) if k.code == KeyCode::Char('q') => {
                        let mut terminal = ui_state.terminal;
                        disable_raw_mode().map_err(|e| anyhow!("{:?}", e))?;
                        execute!(
                            terminal.backend_mut(),
                            LeaveAlternateScreen,
                            DisableMouseCapture
                        )
                        .map_err(|e| anyhow!("{:?}", e))?;
                        terminal.show_cursor()?;
                        Err(UILoopError::Exit)
                    }
                    TerminalEvent::Input(Event::Key(k)) if k.code == KeyCode::Down => {
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
                        Ok(UILoopState {
                            file_count_state,
                            ..ui_state
                        })
                    }
                    TerminalEvent::Input(Event::Key(k)) if k.code == KeyCode::Up => {
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
                        Ok(UILoopState {
                            file_count_state,
                            ..ui_state
                        })
                    }

                    TerminalEvent::FileCount { dir, count } => {
                        let mut file_count = ui_state.file_count;
                        file_count.insert(dir, count);
                        Ok(UILoopState {
                            file_count,
                            ..ui_state
                        })
                    }
                    _ => Ok(ui_state),
                }
            })
            .await;

        match loop_result {
            Err(UILoopError::Exit) | Ok(_) => Ok(()),
            Err(UILoopError::Anyhow(e)) => Err(e),
        }
    }

    pub async fn run(self) -> Result<()> {
        enable_raw_mode()?;

        let mut stdout = io::stdout();
        execute!(stdout, EnterAlternateScreen, EnableMouseCapture)?;

        let backend = CrosstermBackend::new(stdout);
        let mut terminal = Terminal::new(backend)?;

        let (ui_event_tx, ui_event_rx) = mpsc::unbounded_channel();
        let ui_event_tx_clone = ui_event_tx.clone();
        let ui_event_handle = tokio::spawn(async {
            Self::read_ui_events(ui_event_tx_clone)
                .await
                .context("reading Events")?;
            Ok(())
        });

        let task_event_receiver = self.task_event_receiver;
        let external_event_handle =
            tokio::spawn(Self::read_commands(ui_event_tx, task_event_receiver));
        terminal.clear()?;
        let initial_state = UILoopState::new(terminal, self.log_event_receiver);
        let ui_loop = tokio::spawn(Self::ui_loop(initial_state, ui_event_rx));
        try_wait_all_join_handles(vec![ui_event_handle, ui_loop, external_event_handle])
            .await
            .context("***** run handle")
    }
}

struct UILoopState {
    pub logs: ArrayDeque<[(Level, String); BUFFER_SIZE], Wrapping>,
    pub file_count: HashMap<PathBuf, usize>,
    pub file_count_state: ListState,
    pub file_monitors: Vec<JoinHandle<Result<()>>>,
    pub log_event_receiver: mpsc::UnboundedReceiver<(Level, String)>,
    pub terminal: Terminal<CrosstermBackend<Stdout>>,
}

impl UILoopState {
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
