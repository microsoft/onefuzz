// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::rc::Rc;

use anyhow::{bail, Result};
use clap::Parser;

use debuggable_module::{
    block,
    debuginfo::{DebugInfo, Function},
    linux::LinuxModule,
    loader::Loader,
    path::FilePath,
    windows::WindowsModule,
    {Module, Offset},
};
use iced_x86::{Decoder, Formatter, Instruction, NasmFormatter, SymbolResolver, SymbolResult};
use regex::Regex;

#[derive(Parser, Debug)]
struct Args {
    #[arg(short, long)]
    module: String,

    #[arg(short, long)]
    function: Option<String>,
}

fn main() -> Result<()> {
    let args = Args::parse();

    let loader = Loader::new();
    let path = FilePath::new(&args.module)?;
    let module = load_module(&loader, path)?;
    let debuginfo = Rc::new(module.debuginfo()?);

    let glob = args.function.unwrap_or(".*".to_owned());
    let regex = glob_to_regex(&glob)?;

    for function in debuginfo.functions() {
        if regex.is_match(&function.name) {
            dump_function(&*module, debuginfo.clone(), function)?;
        }
    }

    Ok(())
}

fn glob_to_regex(expr: &str) -> Result<Regex> {
    // Don't make users escape Windows path separators.
    let expr = expr.replace(r"\", r"\\");

    // Translate glob wildcards into quantified regexes.
    let expr = expr.replace("*", ".*");

    // Anchor to line start.
    let expr = format!("^{expr}");

    Ok(Regex::new(&expr)?)
}

fn load_module(loader: &Loader, exe_path: FilePath) -> Result<Box<dyn Module + '_>> {
    let exe_data = loader.load(&exe_path)?;

    if debuggable_module::is_linux_module(exe_data)? {
        let module = LinuxModule::new(exe_path, exe_data)?;
        return Ok(Box::new(module));
    }

    if debuggable_module::is_windows_module(exe_data)? {
        let dbg_path = exe_path.with_extension("pdb");
        let dbg_data = loader.load(&dbg_path)?;
        let module = WindowsModule::new(exe_path, exe_data, dbg_path, dbg_data)?;
        return Ok(Box::new(module));
    }

    bail!("unknown module file format");
}

fn dump_function(module: &dyn Module, debuginfo: Rc<DebugInfo>, function: &Function) -> Result<()> {
    println!("{}", function.name);

    let mut fmt = formatter(debuginfo.clone());

    let blocks = block::sweep_region(module, &*debuginfo, function.offset, function.size)?;

    for block in &blocks {
        let data = module.read(block.offset, block.size)?;
        dump(block.offset.0, data, &mut *fmt)?;
        println!()
    }

    Ok(())
}

fn dump(pc: u64, data: &[u8], fmt: &mut dyn Formatter) -> Result<()> {
    let mut decoder = Decoder::new(64, data, 0);
    decoder.set_ip(pc);

    while decoder.can_decode() {
        let inst = decoder.decode();

        let mut display = String::new();
        fmt.format(&inst, &mut display);
        println!("{:>12x}  {}", inst.ip(), display);
    }

    Ok(())
}

fn formatter(debuginfo: Rc<DebugInfo>) -> Box<dyn Formatter> {
    let resolver = Box::new(Resolver(debuginfo));
    let mut fmt = NasmFormatter::with_options(Some(resolver), None);

    let opts = fmt.options_mut();
    opts.set_add_leading_zero_to_hex_numbers(false);
    opts.set_branch_leading_zeros(false);
    opts.set_displacement_leading_zeros(false);
    opts.set_hex_prefix("0x");
    opts.set_hex_suffix("");
    opts.set_leading_zeros(false);
    opts.set_uppercase_all(false);
    opts.set_uppercase_hex(false);
    opts.set_space_after_operand_separator(true);
    opts.set_space_between_memory_add_operators(true);

    Box::new(fmt)
}

struct Resolver(Rc<DebugInfo>);

impl SymbolResolver for Resolver {
    fn symbol(
        &mut self,
        _instruction: &Instruction,
        _operand: u32,
        _instruction_operand: Option<u32>,
        address: u64,
        _address_size: u32,
    ) -> Option<SymbolResult> {
        let f = self.0.find_function(Offset(address))?;

        Some(SymbolResult::with_str(f.offset.0, &f.name))
    }
}
