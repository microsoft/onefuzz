// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::{env, fs, process};

use srcview::{ModOff, SrcView};

fn main() {
    let args = env::args().collect::<Vec<String>>();

    if args.len() != 3 {
        eprintln!("Usage: {} <pdb> <modoff.txt>", args[0]);
        process::exit(1);
    }

    let pdb_path = &args[1];
    let modoff_path = &args[2];

    let modoff_data = fs::read_to_string(&modoff_path).unwrap();
    let modoffs = ModOff::parse(&modoff_data).unwrap();
    let mut srcview = SrcView::new();
    srcview.insert("example.exe", pdb_path).unwrap();

    for modoff in &modoffs {
        print!(" +{:04x} ", modoff.offset);
        match srcview.modoff(modoff) {
            Some(srcloc) => println!("{}", srcloc),
            None => println!(),
        }
    }
}
