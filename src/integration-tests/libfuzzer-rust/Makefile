.PHONY: all clean

all: 
	rustup install nightly
	# temporary fix for cargo-fuzz on latest nightly: https://github.com/rust-fuzz/cargo-fuzz/issues/276
	cargo install --git https://github.com/rust-fuzz/cargo-fuzz --rev b4df3e58f767b5cad8d1aa6753961003f56f3609
	cargo +nightly fuzz build --release

clean:
	cargo clean
	(cd fuzz; cargo clean)
