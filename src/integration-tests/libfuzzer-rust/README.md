# Fuzzing Rust in OneFuzz

OneFuzz can orchastrate fuzzing of Rust using
[cargo-fuzz](https://crates.io/crates/cargo-fuzz) to build libfuzzer based
fuzzing targets.

Included in this directory is a simple example to demonstrate rust based
fuzzing.  For more examples, check out the libfuzzer examples in the [rust
fuzzing trophy case](https://github.com/rust-fuzz/trophy-case).

## Example command

```bash
# ensure the latest cargo-fuzz is installed
cargo install cargo-fuzz --force     
# build your fuzzing targets
cargo +nightly fuzz build --release  
# Launch a fuzz job for each of the targets provided by cargo-fuzz
for target in $(cargo fuzz list); do
    onefuzz template libfuzzer basic $PROJECT_NAME $target $BUILD_NUMBER $POOL_NAME --target_exe ./fuzz/target/x86_64-unknown-linux-gnu/release/$target --inputs ./fuzz/corpus/$target
done
```