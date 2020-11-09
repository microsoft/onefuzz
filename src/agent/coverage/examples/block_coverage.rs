#[cfg(target_os = "windows")]
fn main() {
    use std::env;
    use std::process::Command;

    env_logger::init();

    let mut args = env::args().skip(1);
    let exe = args.next().unwrap();
    let args: Vec<_> = args.collect();

    let mut cmd = Command::new(exe);
    cmd.args(&args);

    let coverage = coverage::block::windows::record(cmd).unwrap();
    let hit = coverage.count_blocks_hit();
    let found = coverage.count_blocks();
    let percent = 100.0 * (hit as f64) / (found as f64);

    println!("block coverage = {}/{} ({:.2}%)", hit, found, percent);
}

#[cfg(target_os = "linux")]
fn main() {}