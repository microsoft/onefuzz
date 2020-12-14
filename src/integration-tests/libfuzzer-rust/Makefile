.PHONY: all clean

all: 
	rustup install nightly
	cargo install cargo-fuzz 
	cargo +nightly fuzz build --release

clean:
	cargo clean
	(cd fuzz; cargo clean)
