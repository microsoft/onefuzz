ifeq ($(OS),Windows_NT)
	CFLAGS=/O2 /Zi /fsanitize=fuzzer /fsanitize=address
	CC=cl
else
	CFLAGS=-g3 -fsanitize=fuzzer -fsanitize=address
	CC=clang
endif

all: fuzz.exe

fuzz.exe: simple.c
	$(CC) $(CFLAGS) $< -o $@

test: fuzz.exe
	./fuzz.exe -runs=100 seeds

.PHONY: clean

clean:
	rm -f fuzz.exe
