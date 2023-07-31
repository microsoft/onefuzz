// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::io::{stdout, BufWriter};
use std::path::Path;
use std::{env, fs, process};

use srcview::{ModOff, Report, SrcLine, SrcView};

fn main() {
    let args = env::args().collect::<Vec<String>>();

    if args.len() != 3 {
        eprintln!("Usage: {} <pdb> <modoff.txt>", args[0]);
        process::exit(1);
    }

    let pdb_path = Path::new(&args[1]);
    let modoff_path = Path::new(&args[2]);

    // read our modoff file and parse it to a vector
    let modoff_data = fs::read_to_string(modoff_path).unwrap();
    let modoffs = ModOff::parse(&modoff_data).unwrap();

    // create all the likely module base names -- do we care about mixed case
    // here?
    let bare = pdb_path.file_stem().unwrap().to_string_lossy();
    let exe = format!("{bare}.exe");
    let dll = format!("{bare}.dll");
    let sys = format!("{bare}.sys");

    // create our new SrcView and insert our only pdb into it
    // we don't know what the modoff module will be, so create a mapping from
    // all likely names to the pdb

    let mut srcview = SrcView::new();

    // in theory we could refcount the pdbcache's to save resources here, but
    // Im not sure thats necesary...
    srcview.insert(&bare, pdb_path).unwrap();
    srcview.insert(&exe, pdb_path).unwrap();
    srcview.insert(&dll, pdb_path).unwrap();
    srcview.insert(&sys, pdb_path).unwrap();

    // Convert our ModOffs to SrcLine so we can draw it
    let coverage: Vec<SrcLine> = modoffs
        .into_iter()
        .filter_map(|m| srcview.modoff(&m))
        .flat_map(|sl| sl.cloned())
        .collect();

    // Generate our report, filtering on our example path
    let r = Report::new(&coverage, &srcview, Some(r"E:\\1f\\coverage\\example")).unwrap();

    // Format it as cobertura and display it
    // output is built incrementally so buffer it
    let mut buffered_stdout = BufWriter::new(stdout().lock());
    r.cobertura(Some(r"E:\\1f\\coverage\\"), &mut buffered_stdout)
        .unwrap();
}
