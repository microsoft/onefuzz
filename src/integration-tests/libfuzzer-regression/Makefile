CC=clang

CFLAGS=-g3 -fsanitize=fuzzer -fsanitize=address

all: broken.exe fixed.exe

broken.exe: simple.c
	$(CC) $(CFLAGS) simple.c -o broken.exe

fixed.exe: simple.c
	$(CC) $(CFLAGS) simple.c -o fixed.exe -DFIXED

.PHONY: clean

clean:
	rm -f broken.exe fixed.exe
