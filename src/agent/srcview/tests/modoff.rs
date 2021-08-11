use std::path::PathBuf;
use std::{env, fs};

use srcview::ModOff;

#[test]
fn parse_modoff() {
    let root = env::var("CARGO_MANIFEST_DIR").unwrap();
    let path: PathBuf = [&root, "res", "example.txt"].iter().collect();

    let modoff = fs::read_to_string(&path).unwrap();
    let modoffs = ModOff::parse(&modoff).unwrap();

    assert_eq!(
        modoffs,
        vec![
            ModOff::new("example.exe", 0x6f70),
            ModOff::new("example.exe", 0x6f75),
            ModOff::new("example.exe", 0x6f79),
            ModOff::new("example.exe", 0x6f7d),
            ModOff::new("example.exe", 0x6f81),
            ModOff::new("example.exe", 0x6f82),
            ModOff::new("example.exe", 0x6f85),
            ModOff::new("example.exe", 0x6f87),
            ModOff::new("example.exe", 0x6f89),
            ModOff::new("example.exe", 0x6f8b),
            ModOff::new("example.exe", 0x6f9b),
            ModOff::new("example.exe", 0x6fa2),
            ModOff::new("example.exe", 0x6fa7),
            ModOff::new("example.exe", 0x6fa9),
            ModOff::new("example.exe", 0x6fad),
        ]
    );
}
