// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::{env, fs, process};

use srcview::ModOff;

fn main() {
    let args = env::args().collect::<Vec<String>>();

    if args.len() != 2 {
        eprintln!("Usage: {} <modoff.txt>", args[0]);
        process::exit(1);
    }

    let modoff = fs::read_to_string(&args[1]).unwrap();

    println!("{:#?}", ModOff::parse(&modoff).unwrap());
}
