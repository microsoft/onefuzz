namespace Tests;
using System;
using Microsoft.OneFuzz.Service;
using Xunit;

public class ReportTests {

    [Fact]
    void TestParseReport() {

        var testReport = """
{
    "call_stack_sha256": "972a371a291ed5668a77576368ead0c46c2bac9f9a16b7fa7c0b48aec5b059b1",
    "input_url": "https://fuzzxkbh6uhuuke4m.blob.core.windows.net/oft-asan-crashes/crash",
    "executable": "setup/fuzz.exe",
    "crash_type": "double-free",
    "crash_site": "double-free (/onefuzz/setup/fuzz.exe+0x4f72e2)",
    "call_stack": [
        "#0 0x4f72e2  (/onefuzz/setup/fuzz.exe+0x4f72e2)",
        "#1 0x5273f0  (/onefuzz/setup/fuzz.exe+0x5273f0)",
        "#2 0x42fb3a  (/onefuzz/setup/fuzz.exe+0x42fb3a)",
        "#3 0x41ef87  (/onefuzz/setup/fuzz.exe+0x41ef87)",
        "#4 0x424ba1  (/onefuzz/setup/fuzz.exe+0x424ba1)",
        "#5 0x44bd72  (/onefuzz/setup/fuzz.exe+0x44bd72)",
        "#6 0x7ffff6a9bb96  (/lib/x86_64-linux-gnu/libc.so.6+0x21b96)",
        "#7 0x41d879  (/onefuzz/setup/fuzz.exe+0x41d879)"
    ],
    "asan_log": "INFO: Seed: 1720627312\nINFO: Loaded 1 modules   (21 inline 8-bit counters): 21 [0x766ef0, 0x766f05), \nINFO: Loaded 1 PC tables (21 PCs): 21 [0x542fd0,0x543120), \nsetup/fuzz.exe: Running 1 inputs 1 time(s) each.\nRunning: ./tmp/crash-66e9fe527ddb160d75f8c2cc373479e841f7999c\n=================================================================\n==16771==ERROR: AddressSanitizer: attempting double-free on 0x602000000050 in thread T0:\n==16771==WARNING: invalid path to external symbolizer!\n==16771==WARNING: Failed to use and restart external symbolizer!\n    #0 0x4f72e2  (/onefuzz/setup/fuzz.exe+0x4f72e2)\n    #1 0x5273f0  (/onefuzz/setup/fuzz.exe+0x5273f0)\n    #2 0x42fb3a  (/onefuzz/setup/fuzz.exe+0x42fb3a)\n    #3 0x41ef87  (/onefuzz/setup/fuzz.exe+0x41ef87)\n    #4 0x424ba1  (/onefuzz/setup/fuzz.exe+0x424ba1)\n    #5 0x44bd72  (/onefuzz/setup/fuzz.exe+0x44bd72)\n    #6 0x7ffff6a9bb96  (/lib/x86_64-linux-gnu/libc.so.6+0x21b96)\n    #7 0x41d879  (/onefuzz/setup/fuzz.exe+0x41d879)\n\n0x602000000050 is located 0 bytes inside of 4-byte region [0x602000000050,0x602000000054)\nfreed by thread T0 here:\n    #0 0x4f72e2  (/onefuzz/setup/fuzz.exe+0x4f72e2)\n    #1 0x5273e1  (/onefuzz/setup/fuzz.exe+0x5273e1)\n    #2 0x42fb3a  (/onefuzz/setup/fuzz.exe+0x42fb3a)\n    #3 0x41ef87  (/onefuzz/setup/fuzz.exe+0x41ef87)\n    #4 0x424ba1  (/onefuzz/setup/fuzz.exe+0x424ba1)\n    #5 0x44bd72  (/onefuzz/setup/fuzz.exe+0x44bd72)\n    #6 0x7ffff6a9bb96  (/lib/x86_64-linux-gnu/libc.so.6+0x21b96)\n\npreviously allocated by thread T0 here:\n    #0 0x4f7663  (/onefuzz/setup/fuzz.exe+0x4f7663)\n    #1 0x5273cb  (/onefuzz/setup/fuzz.exe+0x5273cb)\n    #2 0x42fb3a  (/onefuzz/setup/fuzz.exe+0x42fb3a)\n    #3 0x41ef87  (/onefuzz/setup/fuzz.exe+0x41ef87)\n    #4 0x424ba1  (/onefuzz/setup/fuzz.exe+0x424ba1)\n    #5 0x44bd72  (/onefuzz/setup/fuzz.exe+0x44bd72)\n    #6 0x7ffff6a9bb96  (/lib/x86_64-linux-gnu/libc.so.6+0x21b96)\n\nSUMMARY: AddressSanitizer: double-free (/onefuzz/setup/fuzz.exe+0x4f72e2) \n==16771==ABORTING\n",
    "task_id": "218e1cdb-529a-45dd-b45b-1966d42b652c",
    "job_id": "218e1cdb-529a-45dd-b45b-1966d42b652c",
    "input_sha256": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
    "input_blob": {
        "account": "fuzzxkbh6uhuuke4m",
        "container": "oft-asn-crashes",
        "name": "crash"
    },
    "tool_name": "libfuzzer",
    "tool_version": "1.2.3",
    "onefuzz_version": "1.2.3",
    "extra_property1": "test",
    "extra_property2": 5
}
""";

        var testRegresion = """
{
    "crash_test_result": {
        "no_repro": {
            "input_sha256": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            "executable": "/onefuzz/blob-containers/fuzzdn52wmq2aaxny/fuzz.exe",
            "task_id": "f032970b-3de2-4d52-897f-4c83715f840d",
            "job_id": "f3d4821e-3fd8-47a1-aecb-97d2418555d5",
            "tries": 1
        }
    },
    "original_crash_test_result": {
        "crash_report": {
            "input_sha256": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            "input_blob": {
                "account": "fuzzdn52wmq2aaxny",
                "container": "oft-crashes-cc3ebdad463e52c1a540b8efb631c1ed",
                "name": "fake-crash-sample"
            },
            "executable": "fuzz.exe",
            "crash_type": "fake crash report",
            "crash_site": "fake crash site",
            "call_stack": ["#0 fake", "#1 call", "#2 stack"],
            "call_stack_sha256": "0000000000000000000000000000000000000000000000000000000000000000",
            "minimized_stack": [],
            "minimized_stack_function_names": [],
            "asan_log": "fake asan log",
            "task_id": "3e345aa4-8399-45fd-8e10-8d953f1802b0",
            "job_id": "f3d4821e-3fd8-47a1-aecb-97d2418555d5",
            "onefuzz_version": "1.2.3",
            "tool_name": "libfuzzer",
            "tool_version": "1.2.3"
        }
    }
}
""";

        var report = Reports.ParseReportOrRegression(testReport, new Uri("http://test"));
        var reportInstance = Assert.IsType<Report>(report);

        Assert.Equal("test", reportInstance?.ExtensionData?["extra_property1"].GetString());
        Assert.Equal(5, reportInstance?.ExtensionData?["extra_property2"].GetInt32());


        var regression = Reports.ParseReportOrRegression(testRegresion, new Uri("http://test"));
        _ = Assert.IsType<RegressionReport>(regression);

        var noReport = Reports.ParseReportOrRegression("{}", new Uri("http://test"));
        _ = Assert.IsType<UnknownReportType>(noReport);



    }

}
