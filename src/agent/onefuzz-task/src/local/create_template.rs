use crate::local::template::CommonProperties;

use super::template::{TaskConfig, TaskConfigDiscriminants, TaskGroup};
use anyhow::Result;
use clap::Command;
use std::str::FromStr;
use std::{
    io,
    path::{Path, PathBuf},
};

use strum::VariantNames;

use crate::local::{
    coverage::Coverage, generic_analysis::Analysis, generic_crash_report::CrashReport,
    generic_generator::Generator, libfuzzer::LibFuzzer,
    libfuzzer_crash_report::LibfuzzerCrashReport, libfuzzer_merge::LibfuzzerMerge,
    libfuzzer_regression::LibfuzzerRegression, libfuzzer_test_input::LibfuzzerTestInput,
    template::Template, test_input::TestInput,
};

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

    match res {
        Ok(None) => { /* user quit, do nothing */ }
        Ok(Some(path)) => match path.canonicalize() {
            Ok(canonical_path) => println!("Wrote the template to: {:?}", canonical_path),
            _ => println!("Wrote the template to: {:?}", path),
        },
        Err(e) => println!("Failed to write template due to {}", e),
    }

    Ok(())
}

fn run_app<B: Backend>(terminal: &mut Terminal<B>, mut app: App) -> Result<Option<PathBuf>> {
    loop {
        terminal.draw(|f| ui(f, &mut app))?;
        if let Event::Key(key) = event::read()? {
            if key.kind == KeyEventKind::Press {
                match key.code {
                    KeyCode::Char('q') => return Ok(None),
                    KeyCode::Char(' ') => app.items.toggle(),
                    KeyCode::Down => app.items.next(),
                    KeyCode::Up => app.items.previous(),
                    KeyCode::Enter => {
                        return match generate_template(app.items.items) {
                            Ok(p) => Ok(Some(p)),
                            Err(e) => Err(e),
                        }
                    }
                    _ => {}
                }
            }
        }
    }
}

fn generate_template(items: Vec<ListElement>) -> Result<PathBuf> {
    let tasks: Vec<TaskConfig> = items
        .iter()
        .filter(|item| item.is_included)
        .filter_map(|list_element| {
            match TaskConfigDiscriminants::from_str(list_element.task_type) {
                Err(e) => {
                    error!(
                        "Failed to match task config {:?} - {}",
                        list_element.task_type, e
                    );
                    None
                }
                Ok(t) => match t {
                    TaskConfigDiscriminants::LibFuzzer => {
                        Some(TaskConfig::LibFuzzer(LibFuzzer::example_values()))
                    }
                    TaskConfigDiscriminants::Analysis => {
                        Some(TaskConfig::Analysis(Analysis::example_values()))
                    }
                    TaskConfigDiscriminants::Coverage => {
                        Some(TaskConfig::Coverage(Coverage::example_values()))
                    }
                    TaskConfigDiscriminants::CrashReport => {
                        Some(TaskConfig::CrashReport(CrashReport::example_values()))
                    }
                    TaskConfigDiscriminants::Generator => {
                        Some(TaskConfig::Generator(Generator::example_values()))
                    }
                    TaskConfigDiscriminants::LibfuzzerCrashReport => Some(
                        TaskConfig::LibfuzzerCrashReport(LibfuzzerCrashReport::example_values()),
                    ),
                    TaskConfigDiscriminants::LibfuzzerMerge => {
                        Some(TaskConfig::LibfuzzerMerge(LibfuzzerMerge::example_values()))
                    }
                    TaskConfigDiscriminants::LibfuzzerRegression => Some(
                        TaskConfig::LibfuzzerRegression(LibfuzzerRegression::example_values()),
                    ),
                    TaskConfigDiscriminants::LibfuzzerTestInput => Some(
                        TaskConfig::LibfuzzerTestInput(LibfuzzerTestInput::example_values()),
                    ),
                    TaskConfigDiscriminants::TestInput => {
                        Some(TaskConfig::TestInput(TestInput::example_values()))
                    }
                    TaskConfigDiscriminants::Radamsa => Some(TaskConfig::Radamsa),
                },
            }
        })
        .collect();

    let definition = TaskGroup {
        common: CommonProperties {
            setup_dir: None,
            extra_setup_dir: None,
            extra_dir: None,
            create_job_dir: false,
        },
        tasks,
    };

    let filename = "template";
    let mut filepath = format!("./{}.yaml", filename);
    let mut output_file = Path::new(&filepath);
    let mut counter = 0;
    while output_file.exists() {
        filepath = format!("./{}-{}.yaml", filename, counter);
        output_file = Path::new(&filepath);
        counter += 1;
    }

    std::fs::write(output_file, serde_yaml::to_string(&definition)?)?;

    Ok(output_file.into())
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
                .title("Select which tasks you want to include in the template. Use ⬆/⬇ to navigate and <space> to select. Press <enter> when you're done."),
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

    fn toggle(&mut self) {
        if let Some(index) = self.state.selected() {
            if let Some(element) = self.items.get_mut(index) {
                element.toggle()
            }
        }
    }
}
