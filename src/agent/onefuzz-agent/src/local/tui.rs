// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crossterm::{
    event::{self, Event, KeyCode},
    terminal::enable_raw_mode,
};
use log::Level;
use onefuzz::utils::try_wait_all_join_handles;
use std::{
    io::{self, Stdout},
    sync::Arc,
    time::Duration,
};
use tokio::{
    sync::{mpsc, Mutex},
    task::JoinHandle,
};
use tui::{
    backend::CrosstermBackend,
    layout::{Constraint, Corner, Direction, Layout},
    style::{Color, Style},
    text::{Span, Spans},
    widgets::{Block, Borders},
    widgets::{List, ListItem},
    Terminal,
};

use anyhow::Result;

use arraydeque::{ArrayDeque, Wrapping};
use async_trait::async_trait;

const BUFFER_SIZE: usize = 100;
const TICK_RATE: Duration = Duration::from_millis(250);

pub struct TerminalUi {
    pub task_events: Arc<Mutex<mpsc::UnboundedSender<String>>>,
    task_event_receiver: Arc<Mutex<mpsc::UnboundedReceiver<String>>>,
    log_event_receiver: Arc<Mutex<mpsc::UnboundedReceiver<(Level, String)>>>,
    terminal: Mutex<Terminal<CrosstermBackend<Stdout>>>,
}

#[derive(Debug)]
enum TerminalEvent {
    Input(Event),
    Tick,
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
        let stdout = io::stdout();
        let backend = CrosstermBackend::new(stdout);
        let terminal = Mutex::new(Terminal::new(backend)?);

        Ok(Self {
            task_events: Arc::new(Mutex::new(task_event_sender)),
            task_event_receiver: Arc::new(Mutex::new(task_event_receiver)),
            log_event_receiver: Arc::new(Mutex::new(log_event_receiver)),
            terminal,
        })
    }

    fn read_events() -> (
        mpsc::UnboundedReceiver<TerminalEvent>,
        JoinHandle<Result<()>>,
    ) {
        //let mut log_data: ArrayDeque<[String; BUFFER_SIZE], Wrapping> = ArrayDeque::new();
        let (ui_event_tx, ui_event_rx) = mpsc::unbounded_channel();
        let join_handle: JoinHandle<Result<()>> = tokio::spawn(async move {
            let mut interval = tokio::time::interval(TICK_RATE);

            loop {
                if event::poll(Duration::from_secs(0))? {
                    let event = event::read()?;
                    ui_event_tx.send(TerminalEvent::Input(event))?;
                }
                ui_event_tx.send(TerminalEvent::Tick)?;
                interval.tick().await;
            }
        });

        (ui_event_rx, join_handle)
    }

    async fn ui_loop(self, mut ui_event_rx: mpsc::UnboundedReceiver<TerminalEvent>) -> Result<()> {
        let mut log_data: ArrayDeque<[(Level, String); BUFFER_SIZE], Wrapping> = ArrayDeque::new();
        loop {
            match ui_event_rx.recv().await {
                Some(TerminalEvent::Tick) => {
                    log_data.extend_front({
                        let mut log_receiver = self.log_event_receiver.lock().await;
                        log_receiver.take_available(10)?.into_iter().rev()
                    });
                    let _event_data = {
                        let mut event_receiver = self.task_event_receiver.lock().await;
                        event_receiver.take_available(10)?
                    };
                    self.terminal.lock().await.draw(|f| {
                        let chunks = Layout::default()
                            .direction(Direction::Vertical)
                            .constraints(
                                [Constraint::Percentage(25), Constraint::Percentage(75)].as_ref(),
                            )
                            .split(f.size());

                        let log_items = log_data
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
                }
                Some(TerminalEvent::Input(Event::Key(k))) => {
                    if k.code == KeyCode::Char('q') {
                        break;
                    }
                }

                _ => break,
            }
        }
        Ok(())
    }

    pub async fn run(self) -> Result<()> {
        enable_raw_mode()?;
        let (ui_event_rx, ui_event_handle) = Self::read_events();
        //self.terminal.lock().await.clear()?;
        let ui_loop = tokio::spawn(self.ui_loop(ui_event_rx));

        try_wait_all_join_handles(vec![ui_event_handle, ui_loop]).await
    }
}
