use crate::local::template::CommonProperties;

use super::template::{TaskConfig, TaskGroup};
use anyhow::{Error, Result};
use clap::Command;
use std::{
    io,
    time::{Duration, Instant},
};
use strum::VariantNames;

use crossterm::{
    event::{self, DisableMouseCapture, EnableMouseCapture, Event, KeyCode, KeyEventKind},
    execute,
    terminal::{disable_raw_mode, enable_raw_mode, EnterAlternateScreen, LeaveAlternateScreen},
};
use tui::{prelude::*, widgets::*};

pub fn args(name: &'static str) -> Command {
    Command::new(name).about("interactively create a template")
}

pub fn run() -> Result<()> {
    // setup terminal
    enable_raw_mode()?;
    let mut stdout = io::stdout();
    execute!(stdout, EnterAlternateScreen, EnableMouseCapture)?;
    let backend = CrosstermBackend::new(stdout);
    let mut terminal = Terminal::new(backend)?;

    // create app and run it
    let app = App::new();
    let res = run_app(&mut terminal, app);

    // restore terminal
    disable_raw_mode()?;
    execute!(
        terminal.backend_mut(),
        LeaveAlternateScreen,
        DisableMouseCapture
    )?;
    terminal.show_cursor()?;

    if let Err(err) = res {
        println!("{err:?}");
    }

    Ok(())
}

fn run_app<B: Backend>(terminal: &mut Terminal<B>, mut app: App) -> io::Result<()> {
    loop {
        terminal.draw(|f| ui(f, &mut app))?;
        if let Event::Key(key) = event::read()? {
            if key.kind == KeyEventKind::Press {
                match key.code {
                    KeyCode::Char('q') => return Ok(()),
                    KeyCode::Char(' ') => app.items.toggle(),
                    KeyCode::Down => app.items.next(),
                    KeyCode::Up => app.items.previous(),
                    _ => {}
                }
            }
        }
    }
}

fn ui<B: Backend>(f: &mut Frame<B>, app: &mut App) {
    let areas = Layout::default()
        .direction(Direction::Vertical)
        .constraints([Constraint::Percentage(100)])
        .split(f.size());
    // Iterate through all elements in the `items` app and append some debug text to it.
    let items: Vec<ListItem> = app
        .items
        .items
        .iter()
        .map(|list_element| {
            let title = if list_element.is_included {
                format!("✅ {}", list_element.task_type)
            } else {
                list_element.task_type.to_string()
            };
            ListItem::new(title).style(Style::default().fg(Color::Black).bg(Color::White))
        })
        .collect();

    // Create a List from all list items and highlight the currently selected one
    let items = List::new(items)
        .block(
            Block::default()
                .borders(Borders::ALL)
                .title("Select which tasks you want to include in the template. Use ⬆/⬇ to navigate and <space> to select."),
        )
        .highlight_style(
            Style::default()
                .bg(Color::LightGreen)
                .add_modifier(Modifier::BOLD),
        )
        .highlight_symbol(">> ");

    // We can now render the item list
    f.render_stateful_widget(items, areas[0], &mut app.items.state);
}

// type ListElement<'a> = (&'a str, bool);

struct ListElement<'a> {
    pub task_type: &'a str,
    pub is_included: bool,
}

pub trait Toggle {
    fn toggle(&mut self) {}
}

impl<'a> Toggle for ListElement<'a> {
    fn toggle(&mut self) {
        self.is_included = !self.is_included
    }
}

struct App<'a> {
    items: StatefulList<ListElement<'a>>,
}

impl<'a> App<'a> {
    fn new() -> App<'a> {
        App {
            items: StatefulList::with_items(
                TaskConfig::VARIANTS
                    .iter()
                    .map(|name| ListElement {
                        task_type: name,
                        is_included: false,
                    })
                    .collect(),
            ),
        }
    }
}

struct StatefulList<ListElement> {
    state: ListState,
    items: Vec<ListElement>,
}

impl<T: Toggle> StatefulList<T> {
    fn with_items(items: Vec<T>) -> StatefulList<T> {
        StatefulList {
            state: ListState::default(),
            items,
        }
    }

    fn next(&mut self) {
        let i = match self.state.selected() {
            Some(i) => {
                if self.items.first().is_some() {
                    (i + 1) % self.items.len()
                } else {
                    0
                }
            }
            None => 0,
        };
        self.state.select(Some(i));
    }

    fn previous(&mut self) {
        let i = match self.state.selected() {
            Some(i) => {
                if i == 0 {
                    self.items.len() - 1
                } else {
                    i - 1
                }
            }
            None => 0,
        };
        self.state.select(Some(i));
    }

    fn unselect(&mut self) {
        self.state.select(None);
    }

    fn toggle(&mut self) {
        if let Some(index) = self.state.selected() {
            if let Some(element) = self.items.get_mut(index) {
                element.toggle()
            }
        }
    }
}

// pub fn run() -> Result<()> {
//     println!("Please type which task you would like to generate: ");
//     let task_type = get_input();
//     let definition = TaskGroup {
//         common: CommonProperties {
//             setup_dir: None,
//             extra_setup_dir: None,
//             extra_dir: None,
//             create_job_dir: false,
//         },
//         tasks: Vec::new(),
//     };

//     // Do a bunch of work creating tasks and adding them to the definition

//     Ok(())
// }

// fn get_input() -> String {
//     let mut s = String::new();
//     stdin()
//         .read_line(&mut s)
//         .expect("Did not enter a correct string");

//     if let Some('\n') = s.chars().next_back() {
//         s.pop();
//     }
//     if let Some('\r') = s.chars().next_back() {
//         s.pop();
//     }
//     s
// }
