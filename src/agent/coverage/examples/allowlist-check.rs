use std::sync::Arc;

use anyhow::Result;
use clap::Parser;
use coverage::allowlist::AllowList;
use debuggable_module::load_module::LoadModule;
use debuggable_module::loader::Loader;
use debuggable_module::path::FilePath;
use debuggable_module::Module;
use symbolic::debuginfo::Object;
use symbolic::symcache::{SymCache, SymCacheConverter};

#[derive(Parser, Debug)]
struct Args {
    #[arg(short, long)]
    module: String,

    #[arg(short, long)]
    allowlist: Option<String>,

    #[arg(short, long)]
    verbose: bool,
}

fn main() -> Result<()> {
    env_logger::init();

    let args = Args::parse();

    let allowlist = match args.allowlist {
        Some(allowlist) => AllowList::load(allowlist)?,
        None => AllowList::default(),
    };

    let loader = Arc::new(Loader::new());
    let path = FilePath::new(&args.module)?;
    let module: Box<dyn Module> = LoadModule::load(&loader, path)?;
    let debuginfo = module.debuginfo()?;
    let mut symcache = vec![];
    let mut converter = SymCacheConverter::new();

    let exe = Object::parse(module.executable_data())?;
    converter.process_object(&exe)?;

    let di = Object::parse(module.debuginfo_data())?;
    converter.process_object(&di)?;

    converter.serialize(&mut std::io::Cursor::new(&mut symcache))?;
    let symcache = SymCache::parse(&symcache)?;

    let mut total_functions = 0;
    let mut allowed_functions = 0;

    for function in debuginfo.functions() {
        total_functions += 1;

        if let Some(location) = symcache.lookup(function.offset.0).next() {
            if let Some(file) = location.file() {
                let is_allowed = allowlist.is_allowed(&file.full_path());

                if is_allowed {
                    allowed_functions += 1;
                }

                if args.verbose {
                    if is_allowed {
                        println!("1\t{}\t{}", function.name, file.full_path());
                    } else {
                        println!("0\t{}\t{}", function.name, file.full_path());
                    }
                } else if is_allowed {
                    println!("{}\t{}", function.name, file.full_path());
                }
            }
        }
    }

    log::info!(
        "allowed {}/{} functions",
        allowed_functions,
        total_functions
    );

    Ok(())
}
