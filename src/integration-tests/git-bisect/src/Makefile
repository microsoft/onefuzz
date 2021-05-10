CC=clang

CFLAGS=-g3 -fsanitize=fuzzer -fsanitize=address

all: fuzz.exe

fuzz.exe: fuzz.c
	$(CC) $(CFLAGS) fuzz.c -o fuzz.exe

.PHONY: clean

clean:
	@rm -f fuzz.exe
