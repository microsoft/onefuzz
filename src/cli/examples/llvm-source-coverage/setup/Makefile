CC=clang

CFLAGS=-g3 -fsanitize=fuzzer -fsanitize=address
CFLAGS_COV=-g3 -fsanitize=fuzzer -fprofile-instr-generate -fcoverage-mapping

all: fuzz.exe fuzz-coverage.exe

fuzz.exe: simple.c
	$(CC) $(CFLAGS) $< -o $@

fuzz-coverage.exe: simple.c
	$(CC) $(CFLAGS_COV) $< -o $@

test: fuzz.exe
	./fuzz.exe -runs=100 ../inputs

.PHONY: clean

clean:
	rm -f fuzz.exe fuzz-coverage.exe
