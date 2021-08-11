use std::path::PathBuf;
use std::{env, process};

use srcview::SrcView;

fn main() {
    let args = env::args().collect::<Vec<String>>();

    if args.len() != 2 {
        eprintln!("Usage: {} <pdb>", args[0]);
        process::exit(1);
    }

    let pdb_path = PathBuf::from(&args[1]);

    let mut srcview = SrcView::new();
    srcview
        .insert(pdb_path.file_stem().unwrap().to_str().unwrap(), &pdb_path)
        .unwrap();

    for path in srcview.paths() {
        println!("{}", path.display());
    }
}
