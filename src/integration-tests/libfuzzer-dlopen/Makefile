CC=clang
CFLAGS=-fsanitize=address,fuzzer -fPIC -O0 -ggdb3

.PHONY: all clean test

all: libbad.so fuzz.exe

fuzz.exe: main.o
	$(CC) $(CFLAGS) -o $@ $<

libbad.so: bad.o
	$(CC) -shared -o $@ $<
	
test: all
	LD_LIBRARY_PATH=. ./fuzz.exe

clean:
	rm -rf fuzz.exe *.o *.so crash-*
