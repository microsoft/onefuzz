CC=clang

CFLAGS=-g3 -fsanitize=fuzzer -fsanitize=address

all: fuzz.exe

fuzz.exe: simple.c
	$(CC) $(CFLAGS) $< -o $@

test: fuzz.exe
	./fuzz.exe -runs=100 seeds

.PHONY: clean

clean:
	rm -f fuzz.exe
