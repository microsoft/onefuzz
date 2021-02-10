all: check

libfuzzer-dotnet:
	mkdir -p my-fuzzer
	# direct url to a known-good instance of libfuzzer-dotnet.cc
	curl -o libfuzzer-dotnet.cc https://raw.githubusercontent.com/Metalnem/libfuzzer-dotnet/543b170a67bdd39e9ba260fe54bc93c77b877c24/libfuzzer-dotnet.cc
	clang -fsanitize=fuzzer libfuzzer-dotnet.cc -o my-fuzzer/libfuzzer-dotnet
	rm -f libfuzzer-dotnet.cc

build-harness: libfuzzer-dotnet
	dotnet tool install --global SharpFuzz.CommandLine || echo already installed
	dotnet publish ./wrapper/wrapper.csproj -c release -o my-fuzzer
	sharpfuzz my-fuzzer/problems.dll || echo already instrumented

check: build-harness
	./my-fuzzer/libfuzzer-dotnet --target_path=./my-fuzzer/wrapper -runs=1

fuzz: check
	./my-fuzzer/libfuzzer-dotnet --target_path=./my-fuzzer/wrapper

.PHONY: clean

clean:
	rm -rf fuzz.exe libfuzzer-dotnet my-fuzzer wrapper/bin wrapper/obj problems/bin problems/obj
