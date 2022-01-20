#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import unittest
from uuid import uuid4

from onefuzztypes.models import BlobRef, Report
from onefuzztypes.primitives import Container

from __app__.onefuzzlib.sarif import generate_sarif

# import logging


example_report = {
    "input_sha256": "4bdd0bbfe3f4c52cc2c8ff02f1fef29663dd9938f230304915805af1fa71e968",
    "input_blob": {
        "account": "fuzzrabkn4vd3e2gy",
        "container": "oft-crashes-2b72ce5bb0055954a4006004fd9233f6",
        "name": "crash-229d028063a11904f846c91224abaa99113f3a15"
    },
    "executable": "setup/fuzz.exe",
    "crash_type": "stack-buffer-overflow",
    "crash_site": "AddressSanitizer: stack-buffer-overflow D:\\a\\onefuzz\\onefuzz\\src\\integration-tests\\libfuzzer-linked-library\\bad2.c:38 in func2",
    "call_stack": ["#0 0x7ffbfec41855 in func2 D:\\a\\onefuzz\\onefuzz\\src\\integration-tests\\libfuzzer-linked-library\\bad2.c:38", "#1 0x7ff7b5351059 in LLVMFuzzerTestOneInput D:\\a\\onefuzz\\onefuzz\\src\\integration-tests\\libfuzzer-linked-library\\main.c:12", "#2 0x7ff7b53bdcf8 in fuzzer::Fuzzer::ExecuteCallback C:\\src\\llvm_package_1300-final\\llvm-project\\compiler-rt\\lib\\fuzzer\\FuzzerLoop.cpp:611", "#3 0x7ff7b53d27e5 in fuzzer::RunOneTest C:\\src\\llvm_package_1300-final\\llvm-project\\compiler-rt\\lib\\fuzzer\\FuzzerDriver.cpp:323", "#4 0x7ff7b53d812c in fuzzer::FuzzerDriver C:\\src\\llvm_package_1300-final\\llvm-project\\compiler-rt\\lib\\fuzzer\\FuzzerDriver.cpp:856", "#5 0x7ff7b5395d32 in main C:\\src\\llvm_package_1300-final\\llvm-project\\compiler-rt\\lib\\fuzzer\\FuzzerMain.cpp:20", "#6 0x7ff7b53df8f7 in __scrt_common_main_seh d:\\a01\\_work\\6\\s\\src\\vctools\\crt\\vcstartup\\src\\startup\\exe_common.inl:288", "#7 0x7ffc06557033 in BaseThreadInitThunk+0x13 (C:\\Windows\\System32\\KERNEL32.DLL+0x180017033)", "#8 0x7ffc07922650 in RtlUserThreadStart+0x20 (C:\\Windows\\SYSTEM32\\ntdll.dll+0x180052650)"],
    "call_stack_sha256": "874ede7e372ce42241019e49699acf7085db77491106019d9064531b3c931e99",
    "minimized_stack": ["#0 0x7ffbfec41855 in func2 D:\\a\\onefuzz\\onefuzz\\src\\integration-tests\\libfuzzer-linked-library\\bad2.c:38", "#1 0x7ff7b5351059 in LLVMFuzzerTestOneInput D:\\a\\onefuzz\\onefuzz\\src\\integration-tests\\libfuzzer-linked-library\\main.c:12"],
    "minimized_stack_sha256": "1c3e435cdbc5e6f73480ed4bff3ac707822ae112c038191db6b9d8e6a6361029",
    "minimized_stack_function_names": ["func2", "main.c"],
    "minimized_stack_function_names_sha256": "5e1a2c94af1f7bd42b3184250d1ae03f6e8d552ca336d00d9d2756b7226489f5",
    "minimized_stack_function_lines": ["func2 bad2.c:38", "main.c main.c:12"],
    "minimized_stack_function_lines_sha256": "c21d40603b609f91ed2f1116ab229a991394582de48117daf4185d527e2faca7",
    "asan_log": "INFO: Running with entropic power schedule (0xFF, 100).\r\nINFO: Seed: 3215303615\r\nINFO: Loaded 3 modules   (43 inline 8-bit counters): 21 [00007FFBFED44088, 00007FFBFED4409D), 21 [00007FFBFECB4088, 00007FFBFECB409D), 1 [00007FF7B54C1088, 00007FF7B54C1089), \r\nINFO: Loaded 3 PC tables (43 PCs): 21 [00007FFBFED30398,00007FFBFED304E8), 21 [00007FFBFECA0398,00007FFBFECA04E8), 1 [00007FF7B547F790,00007FF7B547F7A0), \r\nsetup/fuzz.exe: Running 1 inputs 1 time(s) each.\r\nRunning: C:\\Windows\\Temp\\.tmpm8JTA8\\crash-229d028063a11904f846c91224abaa99113f3a15\r\n=================================================================\n==4220==ERROR: AddressSanitizer: stack-buffer-overflow on address 0x0064241eeefc at pc 0x7ffbfec41856 bp 0x0064241eee40 sp 0x0064241eee88\nWRITE of size 4 at 0x0064241eeefc thread T0\n==4220==WARNING: Failed to use and restart external symbolizer!\n    #0 0x7ffbfec41855 in func2 D:\\a\\onefuzz\\onefuzz\\src\\integration-tests\\libfuzzer-linked-library\\bad2.c:38\n    #1 0x7ff7b5351059 in LLVMFuzzerTestOneInput D:\\a\\onefuzz\\onefuzz\\src\\integration-tests\\libfuzzer-linked-library\\main.c:12\n    #2 0x7ff7b53bdcf8 in fuzzer::Fuzzer::ExecuteCallback C:\\src\\llvm_package_1300-final\\llvm-project\\compiler-rt\\lib\\fuzzer\\FuzzerLoop.cpp:611\n    #3 0x7ff7b53d27e5 in fuzzer::RunOneTest C:\\src\\llvm_package_1300-final\\llvm-project\\compiler-rt\\lib\\fuzzer\\FuzzerDriver.cpp:323\n    #4 0x7ff7b53d812c in fuzzer::FuzzerDriver C:\\src\\llvm_package_1300-final\\llvm-project\\compiler-rt\\lib\\fuzzer\\FuzzerDriver.cpp:856\n    #5 0x7ff7b5395d32 in main C:\\src\\llvm_package_1300-final\\llvm-project\\compiler-rt\\lib\\fuzzer\\FuzzerMain.cpp:20\n    #6 0x7ff7b53df8f7 in __scrt_common_main_seh d:\\a01\\_work\\6\\s\\src\\vctools\\crt\\vcstartup\\src\\startup\\exe_common.inl:288\n    #7 0x7ffc06557033 in BaseThreadInitThunk+0x13 (C:\\Windows\\System32\\KERNEL32.DLL+0x180017033)\n    #8 0x7ffc07922650 in RtlUserThreadStart+0x20 (C:\\Windows\\SYSTEM32\\ntdll.dll+0x180052650)\n\nAddress 0x0064241eeefc is located in stack of thread T0 at offset 60 in frame\n    #0 0x7ffbfec4102f in func2 D:\\a\\onefuzz\\onefuzz\\src\\integration-tests\\libfuzzer-linked-library\\bad2.c:13\n\n  This frame has 1 object(s):\n    [32, 36) 'cnt' (line 14) <== Memory access at offset 60 overflows this variable\nHINT: this may be a false positive if your program uses some custom stack unwind mechanism, swapcontext or vfork\n      (longjmp, SEH and C++ exceptions *are* supported)\nSUMMARY: AddressSanitizer: stack-buffer-overflow D:\\a\\onefuzz\\onefuzz\\src\\integration-tests\\libfuzzer-linked-library\\bad2.c:38 in func2\nShadow bytes around the buggy address:\n  0x02096d23dd80: 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00\n  0x02096d23dd90: 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00\n  0x02096d23dda0: 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00\n  0x02096d23ddb0: 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00\n  0x02096d23ddc0: 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00\n=>0x02096d23ddd0: 00 00 00 00 00 00 00 00 f1 f1 f1 f1 04 f3 f3[f3]\n  0x02096d23dde0: 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00\n  0x02096d23ddf0: 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00\n  0x02096d23de00: 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00\n  0x02096d23de10: 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00\n  0x02096d23de20: 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00\nShadow byte legend (one shadow byte represents 8 application bytes):\n  Addressable:           00\n  Partially addressable: 01 02 03 04 05 06 07 \n  Heap left redzone:       fa\n  Freed heap region:       fd\n  Stack left redzone:      f1\n  Stack mid redzone:       f2\n  Stack right redzone:     f3\n  Stack after return:      f5\n  Stack use after scope:   f8\n  Global redzone:          f9\n  Global init order:       f6\n  Poisoned by user:        f7\n  Container overflow:      fc\n  Array cookie:            ac\n  Intra object redzone:    bb\n  ASan internal:           fe\n  Left alloca redzone:     ca\n  Right alloca redzone:    cb\n==4220==ABORTING\n",
    "task_id": "45407f33-6323-4682-9dd6-147ed345769e",
    "job_id": "f76bae27-0322-42b1-9cfb-6947be52240f"
}


class TestSarif(unittest.TestCase):
    def test_basic(self) -> None:

        test_report = Report.parse_obj(example_report)

        sarif = generate_sarif(test_report)

        print(f"sarif report : {sarif}")
