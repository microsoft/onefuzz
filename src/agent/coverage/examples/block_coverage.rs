use std::env;

use anyhow::Result;

#[cfg(target_os = "windows")]
fn main() -> Result<()> {
    use std::process::Command;

    env_logger::init();

    let mut args = env::args().skip(1);
    let exe = args.next().unwrap();
    let args: Vec<_> = args.collect();

    let mut cmd = Command::new(exe);
    cmd.args(&args);

    let coverage = coverage::block::windows::record(cmd)?;
    let hit = coverage.count_blocks_hit();
    let found = coverage.count_blocks();
    let percent = 100.0 * (hit as f64) / (found as f64);

    log::info!("block coverage = {}/{} ({:.2}%)", hit, found, percent);

    Ok(())
}

#[cfg(target_os = "linux")]
fn main() -> Result<()> {
    use coverage::block::linux::record;
    use pete::Command;

    env_logger::init();

    let argv = env::args().skip(1).collect();
    let cmd = Command::new(argv)?;

    let coverage = record(cmd)?;

    for m in coverage.modules.values() {
        let mut hit = 0;
        let mut found = 0;

        let name = m.module.file_name().unwrap().to_string_lossy();

        log::info!("{}", m.module.display());

        for b in m.blocks.values() {
            found += 1;

            if b.count > 0 {
                hit += 1;
            };

            let marker = if b.count == 0 {
                " "
            } else {
                "x"
            };

            log::debug!("  [{}] {}+{:x} ({} bytes)", marker, name, b.offset, b.size);
        }

        let percent = 100.0 * (hit as f64) / (found as f64);
        log::info!("block coverage = {}/{} ({:.2}%)", hit, found, percent);
    }

    Ok(())
}