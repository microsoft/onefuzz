## Supported Platforms

OneFuzz is cross-platform, and the actively-supported platforms vary by component.

### CLI

We continuously test the CLI on Windows 10 Pro and Ubuntu 18.04 LTS, both on the
x64 architecture.  The CLI client is written in Python 3, and targets Python 3.7
and up.  We distribute a self-contained executable CLI build for Windows which 
bundles a Python interpreter.

### Virtual Machine Scale Sets

OneFuzz deploys targets into Azure Virtual Machine Scale Sets for fuzzing (and
supporting tasks).  OneFuzz permits arbitrary choice of VM SKU and OS Image,
including custom images.  We continuously test on Window 10 Pro x64 (using the 
Azure OS image URN `MicrosoftWindowsDesktop:Windows-10:win10-21h2-pro:latest`)
and Ubuntu 18.04 LTS x64 (using the Azure OS image URN 
`Canonical:UbuntuServer:18.04-LTS:latest`).

### LibFuzzer Compilation

LibFuzzer targets are built by linking the libFuzzer runtime to a test function,
tied together with compiler-provided static instrumentation (sanitizers).
The resulting executable has runtime options and output that can vary with
the compiler and libFuzzer runtime used.

We actively support libFuzzer targets produced using the following compiler
toolchains:

* LLVM 8 and up, Windows and Linux, x86 and x64
* MSVC 16.8 and later that support x64 ASAN instrumentation
