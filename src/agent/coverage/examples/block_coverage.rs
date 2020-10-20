use std::env;
use std::process::Command;


fn main() {
    env_logger::init();

    let mut args = env::args().skip(1);
    let exe = args.next().unwrap();
    let args: Vec<_> = args.collect();

    let mut cmd = Command::new(exe);
    cmd.args(&args);

    let coverage = coverage::block::windows::record(cmd).unwrap();
    let hit = coverage.count_blocks_hit();
    println!("blocks_hit = {}", hit);
}
