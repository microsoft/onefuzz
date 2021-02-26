all: check


fuzz-libs:
	sudo apt update
	sudo apt install -y qemu-user g++-aarch64-linux-gnu
	git clone https://github.com/llvm-mirror/compiler-rt

	# last version that supports pc-guard instrumentation, required by GCC
	(cd compiler-rt; git checkout daa6759576548a2f3825faddaa6811cabbfb45eb)
	
	# These *must* be built without ASAN
	mkdir -p fuzz-libs
	(cd fuzz-libs; aarch64-linux-gnu-g++ -c ../compiler-rt/lib/fuzzer/*.cpp)

fuzz.exe: fuzz-libs fuzz.c
	aarch64-linux-gnu-g++ -pthread -lasan -o fuzz.exe fuzz-libs/*.o fuzz.c -fsanitize=address -fsanitize-coverage=trace-pc

check: fuzz.exe
	ASAN_OPTIONS=:detect_leaks=0 qemu-aarch64 -L /usr/aarch64-linux-gnu ./fuzz.exe -runs=1

fuzz: check
	ASAN_OPTIONS=:detect_leaks=0 qemu-aarch64 -L /usr/aarch64-linux-gnu ./fuzz.exe

.PHONY: clean

clean:
	rm -rf fuzz.exe fuzz-libs compiler-rt
