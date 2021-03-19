CC=clang
CFLAGS=-fsanitize=address,fuzzer -O0

.PHONY: all clean

all: fuzz.exe

fuzz.exe: main.o bad.dll
	$(CC) $(CFLAGS) -o $@ $<

bad.dll: bad.o
	$(CC) $(CFLAGS) -shared -o $@ $<

clean:
	rm -rf fuzz.exe *.o *.dll crash-* *.pdb *.exp *.lib
