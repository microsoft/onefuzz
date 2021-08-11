# srcview

A library for mapping module+offset to source:line. Note that you'll get
significantly better results if you use instruction level coverage as
opposed to branch. Alternatively you're welcome to post-process branch
coverage into instruction, but this project does not assist with that.

## Docs

`cargo doc --open`

## Usage

See [`examples/dump_cobertura.rs`](examples/dump_cobertura.rs).

This can be run with `cargo run --example dump_cobertura res\example.pdb res\example.txt`.

The biggest challenge when making this work is likely getting the absolute PDB
paths to match relative paths inside your repo. To make this a bit easier, you
can dump the PDB paths with: `cargo run --example dump_paths res\example.pdb`

## ADO Integration

See [`azure-pipelines.yml`](azure-pipelines.yml).

## VSCode Integration

Install [Coverage Gutters](https://marketplace.visualstudio.com/items?itemName=ryanluker.vscode-coverage-gutters),
then place a file named `coverage.xml` at a location such that the relative
file paths match your repo. Note that this is a fille you should generate
yourself, and that the `coverage.xml` included in this repo will probably not
help you. From there, navigate to any file you expect to see coverage in. If
the paths and coverage file correctly line up, you should see red or green bars
next to the source lines.