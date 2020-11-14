from .afl import afl_windows, afl_linux
from .libfuzzer import libfuzzer_linux, libfuzzer_windows

TEMPLATES = {
    "afl_windows": afl_windows,
    "afl_linux": afl_linux,
    "libfuzzer_linux": libfuzzer_linux,
    "libfuzzer_windows": libfuzzer_windows,
}
