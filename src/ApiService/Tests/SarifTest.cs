using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.OneFuzz.Service;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;
using Xunit;
using Xunit.Abstractions;

namespace Tests;

public class SarifTests {

    ITestOutputHelper _output;

    public SarifTests(ITestOutputHelper output) {
        _output = output;
    }

    [Fact]
    public async System.Threading.Tasks.Task TestParseRport() {
        var test2 =
            @"
            {
                ""input_sha256"": ""e2c07e18f1796a5e6cd4f198ebd2c0ac2657f17d0b4a65b916400c31bc21c493"",
                ""input_blob"": {
                    ""account"": ""fuzzpremium01"",
                    ""container"": ""oft-crashes-8e71519a47f954d38187b8e912db578e"",
                    ""name"": ""crash-04084f93ef033a4ba23ad2948caa8f90f8801f71""
                },
                ""executable"": ""setup/h265fuzzer.exe"",
                ""crash_type"": ""access-violation"",
                ""crash_site"": ""AddressSanitizer: access-violation avcore\\codecdsp\\video\\h265dec\\shared\\umc\\codec\\h265_dec\\src\\umc_h265_segment_decoder_deblocking.cpp:486 in H265SegmentDecoder::SetupCTUDeblockInfo"",
                ""call_stack"": [
                    ""#0 0x7ff77bbefe52 in H265SegmentDecoder::SetupCTUDeblockInfo avcore\\codecdsp\\video\\h265dec\\shared\\umc\\codec\\h265_dec\\src\\umc_h265_segment_decoder_deblocking.cpp:486"",
                    ""#1 0x7ff77bbda5e0 in H265SegmentDecoderMultiThreaded::DeblockEdgeInfoSectionTask avcore\\codecdsp\\video\\h265dec\\shared\\umc\\codec\\h265_dec\\src\\ms_h265_segment_decoder_mt.cpp:1838"",
                    ""#2 0x7ff77bb1c406 in H265SegmentDecoderMultiThreaded::ProcessSegment avcore\\codecdsp\\video\\h265dec\\shared\\umc\\codec\\h265_dec\\src\\umc_h265_segment_decoder_mt.cpp:211"",
                    ""#3 0x7ff77ba9d465 in MFXTaskSupplier_H265::RunThread avcore\\codecdsp\\video\\h265dec\\shared\\umc\\codec\\h265_dec\\src\\umc_h265_mfx_supplier.cpp:299"",
                    ""#4 0x7ff77ba7eda9 in VideoDECODEH265::RunThread avcore\\codecdsp\\video\\h265dec\\shared\\umc\\codec\\h265_dec\\src\\h265_dec_decode.cpp:539"",
                    ""#5 0x7ff77bd7959a in mfxSchedulerCore::scheduler_thread_proc avcore\\codecdsp\\video\\h265dec\\mfx_lib\\scheduler\\src\\mfx_scheduler_core_thread.cpp:41"",
                    ""#6 0x7ff827eabf23 in _asan_wrap_GlobalSize+0x59349 (C:\\onefuzz\\9a41e716-80e1-4fb1-afaf-b929e0ab838d\\setup\\clang_rt.asan_dynamic-x86_64.dll+0x18005bf23)"",
                    ""#7 0x7ff844f57033 in BaseThreadInitThunk+0x13 (C:\\Windows\\System32\\KERNEL32.DLL+0x180017033)"",
                    ""#8 0x7ff8457a2650 in RtlUserThreadStart+0x20 (C:\\Windows\\SYSTEM32\\ntdll.dll+0x180052650)""
                ],
                ""call_stack_sha256"": ""cf4f1dab7002f9a31165fd0137222ee227d0e091cad168954376d1ad0334ac85"",
                ""minimized_stack"": [
                    ""#0 0x7ff77bbefe52 in H265SegmentDecoder::SetupCTUDeblockInfo avcore\\codecdsp\\video\\h265dec\\shared\\umc\\codec\\h265_dec\\src\\umc_h265_segment_decoder_deblocking.cpp:486"",
                    ""#1 0x7ff77bbda5e0 in H265SegmentDecoderMultiThreaded::DeblockEdgeInfoSectionTask avcore\\codecdsp\\video\\h265dec\\shared\\umc\\codec\\h265_dec\\src\\ms_h265_segment_decoder_mt.cpp:1838"",
                    ""#2 0x7ff77bb1c406 in H265SegmentDecoderMultiThreaded::ProcessSegment avcore\\codecdsp\\video\\h265dec\\shared\\umc\\codec\\h265_dec\\src\\umc_h265_segment_decoder_mt.cpp:211"",
                    ""#3 0x7ff77ba9d465 in MFXTaskSupplier_H265::RunThread avcore\\codecdsp\\video\\h265dec\\shared\\umc\\codec\\h265_dec\\src\\umc_h265_mfx_supplier.cpp:299"",
                    ""#4 0x7ff77ba7eda9 in VideoDECODEH265::RunThread avcore\\codecdsp\\video\\h265dec\\shared\\umc\\codec\\h265_dec\\src\\h265_dec_decode.cpp:539"",
                    ""#5 0x7ff77bd7959a in mfxSchedulerCore::scheduler_thread_proc avcore\\codecdsp\\video\\h265dec\\mfx_lib\\scheduler\\src\\mfx_scheduler_core_thread.cpp:41""
                ],
                ""minimized_stack_sha256"": ""56f4dd4beed124b8ca8555ef8b80fc63bec3b2b4625e1e75b8cf4395800e9d41"",
                ""minimized_stack_function_names"": [
                    ""H265SegmentDecoder::SetupCTUDeblockInfo"",
                    ""H265SegmentDecoderMultiThreaded::DeblockEdgeInfoSectionTask"",
                    ""H265SegmentDecoderMultiThreaded::ProcessSegment"",
                    ""MFXTaskSupplier_H265::RunThread"",
                    ""VideoDECODEH265::RunThread"",
                    ""mfxSchedulerCore::scheduler_thread_proc""
                ],
                ""minimized_stack_function_names_sha256"": ""0b7016821c7dfdc50748fc009f5e3149035607deefb28607d2e5d7c64b20aa72"",
                ""minimized_stack_function_lines"": [
                    ""H265SegmentDecoder::SetupCTUDeblockInfo umc_h265_segment_decoder_deblocking.cpp:486"",
                    ""H265SegmentDecoderMultiThreaded::DeblockEdgeInfoSectionTask ms_h265_segment_decoder_mt.cpp:1838"",
                    ""H265SegmentDecoderMultiThreaded::ProcessSegment umc_h265_segment_decoder_mt.cpp:211"",
                    ""MFXTaskSupplier_H265::RunThread umc_h265_mfx_supplier.cpp:299"",
                    ""VideoDECODEH265::RunThread h265_dec_decode.cpp:539"",
                    ""mfxSchedulerCore::scheduler_thread_proc mfx_scheduler_core_thread.cpp:41""
                ],
                ""minimized_stack_function_lines_sha256"": ""084d244b6dc9a67618bdd8e385c940c47efb54303fd8759ba1b0c135a28962ed"",
                ""asan_log"": ""INFO: Seed: 1578328312\r\nINFO: Loaded 1 modules   (78016 inline 8-bit counters): 78016 [00007FF77BD99000, 00007FF77BDAC0C0), \r\nsetup/h265fuzzer.exe: Running 1 inputs 10000 time(s) each.\r\nRunning: C:\\onefuzz\\9a41e716-80e1-4fb1-afaf-b929e0ab838d\\task_crashes_1\\crash-04084f93ef033a4ba23ad2948caa8f90f8801f71\r\n=================================================================\n==6912==ERROR: AddressSanitizer: access-violation on unknown address 0x7ff77fdd7650 (pc 0x7ff77bbefe53 bp 0x00f2d2eff2e0 sp 0x00f2d2eff1e0 T3)\n==6912==The signal is caused by a READ memory access.\n==6912==WARNING: Failed to use and restart external symbolizer!\n    #0 0x7ff77bbefe52 in H265SegmentDecoder::SetupCTUDeblockInfo avcore\\codecdsp\\video\\h265dec\\shared\\umc\\codec\\h265_dec\\src\\umc_h265_segment_decoder_deblocking.cpp:486\n    #1 0x7ff77bbda5e0 in H265SegmentDecoderMultiThreaded::DeblockEdgeInfoSectionTask avcore\\codecdsp\\video\\h265dec\\shared\\umc\\codec\\h265_dec\\src\\ms_h265_segment_decoder_mt.cpp:1838\n    #2 0x7ff77bb1c406 in H265SegmentDecoderMultiThreaded::ProcessSegment avcore\\codecdsp\\video\\h265dec\\shared\\umc\\codec\\h265_dec\\src\\umc_h265_segment_decoder_mt.cpp:211\n    #3 0x7ff77ba9d465 in MFXTaskSupplier_H265::RunThread avcore\\codecdsp\\video\\h265dec\\shared\\umc\\codec\\h265_dec\\src\\umc_h265_mfx_supplier.cpp:299\n    #4 0x7ff77ba7eda9 in VideoDECODEH265::RunThread avcore\\codecdsp\\video\\h265dec\\shared\\umc\\codec\\h265_dec\\src\\h265_dec_decode.cpp:539\n    #5 0x7ff77bd7959a in mfxSchedulerCore::scheduler_thread_proc avcore\\codecdsp\\video\\h265dec\\mfx_lib\\scheduler\\src\\mfx_scheduler_core_thread.cpp:41\n    #6 0x7ff827eabf23 in _asan_wrap_GlobalSize+0x59349 (C:\\onefuzz\\9a41e716-80e1-4fb1-afaf-b929e0ab838d\\setup\\clang_rt.asan_dynamic-x86_64.dll+0x18005bf23)\n    #7 0x7ff844f57033 in BaseThreadInitThunk+0x13 (C:\\Windows\\System32\\KERNEL32.DLL+0x180017033)\n    #8 0x7ff8457a2650 in RtlUserThreadStart+0x20 (C:\\Windows\\SYSTEM32\\ntdll.dll+0x180052650)\n\nAddressSanitizer can not provide additional info.\nSUMMARY: AddressSanitizer: access-violation avcore\\codecdsp\\video\\h265dec\\shared\\umc\\codec\\h265_dec\\src\\umc_h265_segment_decoder_deblocking.cpp:486 in H265SegmentDecoder::SetupCTUDeblockInfo\nThread T3 created by T0 here:\n    #0 0x7ff827eacd28 in _asan_wrap_GlobalSize+0x5a14e (C:\\onefuzz\\9a41e716-80e1-4fb1-afaf-b929e0ab838d\\setup\\clang_rt.asan_dynamic-x86_64.dll+0x18005cd28)\n    #1 0x7ff77bd74e11 in mfxSchedulerCore::Initialize avcore\\codecdsp\\video\\h265dec\\mfx_lib\\scheduler\\src\\mfx_scheduler_core_ischeduler.cpp:107\n    #2 0x7ff77ba6f2ba in CMFXHEVCDecoder::InitializeMFXHEVCDecoder avcore\\codecdsp\\video\\h265dec\\wrapper\\ms_mfx_hevc_decoder.cpp:219\n    #3 0x7ff77b9d2d5d in CH265DecoderTransform::xOnBeginStreaming avcore\\codecdsp\\video\\h265dec\\syncmft\\h265decodertransform.cpp:3682\n    #4 0x7ff77b9da6f6 in CH265DecoderTransform::ProcessInput avcore\\codecdsp\\video\\h265dec\\syncmft\\h265decodertransform.cpp:4670\n    #5 0x7ff77b97df93 in LLVMFuzzerTestOneInput avcore\\codecdsp\\video\\h265dec\\test\\fuzzing\\h265fuzzer.cpp:88\n    #6 0x7ff77b9a5ad0 in fuzzer::Fuzzer::ExecuteCallback OSS\\libfuzzer\\10.0.0\\src\\compiler-rt\\lib\\fuzzer\\fuzzerloop.cpp:556\n    #7 0x7ff77b98ac70 in fuzzer::RunOneTest OSS\\libfuzzer\\10.0.0\\src\\compiler-rt\\lib\\fuzzer\\fuzzerdriver.cpp:292\n    #8 0x7ff77b98d54c in fuzzer::FuzzerDriver OSS\\libfuzzer\\10.0.0\\src\\compiler-rt\\lib\\fuzzer\\fuzzerdriver.cpp:778\n    #9 0x7ff77b988fa1 in main OSS\\libfuzzer\\10.0.0\\src\\compiler-rt\\lib\\fuzzer\\fuzzermain.cpp:19\n    #10 0x7ff77bd8ec35 in __scrt_common_main_seh VCCRT\\vcstartup\\src\\startup\\exe_common.inl:288\n    #11 0x7ff844f57033 in BaseThreadInitThunk+0x13 (C:\\Windows\\System32\\KERNEL32.DLL+0x180017033)\n    #12 0x7ff8457a2650 in RtlUserThreadStart+0x20 (C:\\Windows\\SYSTEM32\\ntdll.dll+0x180052650)\n\n==6912==ABORTING\n"",
                ""task_id"": ""9a41e716-80e1-4fb1-afaf-b929e0ab838d"",
                ""job_id"": ""748b4cca-b23e-447c-b45b-0ecd40295288"",
                ""tool_name"": ""libfuzzer"",
                ""onefuzz_version"": ""5.0.0""
            }";

        var rootpath = @"D:\a\onefuzz\onefuzz";
        var report = JsonSerializer.Deserialize<Report>(test2, EntityConverter.GetJsonSerializerOptions());
        Assert.NotNull(report);

        var sarif = SarifGenerator.ToSarif(rootpath, report!);

        Assert.Equal(1, sarif.Runs.Count);
        var run = sarif.Runs[0];

        Assert.Equal(report!.ToolName, run.Tool.Driver.Name);
        Assert.Equal(report.OnefuzzVersion, run.Tool.Driver.SemanticVersion);

        Assert.Equal(1, run.Tool.Driver.Rules.Count);
        var rule = run.Tool.Driver.Rules[0];
        Assert.Equal(AsanHelper.GetAsantErrorCode(report.CrashType), rule.Id);

        Assert.Equal(1, run.Results.Count);
        var result = run.Results[0];

        Assert.Equal(1, run.Results.Count);






    }


    //[Fact]
    async System.Threading.Tasks.Task ValidateSarifFile() {

        var reportDir = @"./sample_reports";

        var reports =
            Directory.EnumerateFiles(reportDir, "*.json")
            .Select(x => (fileName: x, report: JsonSerializer.Deserialize<Report>(File.ReadAllText(x), EntityConverter.GetJsonSerializerOptions()) ?? throw new Exception()))
            .Select(x => (x.fileName, sarif: SarifGenerator.ToSarif("/home/runner/work/onefuzz/onefuzz", x.report)));

        // foreach (var (fileName, report) in reports) {
        //     _output.WriteLine($"writing {fileName}");
        //     await using var file = File.OpenWrite(Path.Combine(reportDir, Path.GetFileNameWithoutExtension(fileName) + ".sarif"));
        //     await using var writer = new StreamWriter(file);
        //     using var jsonWriter = new Newtonsoft.Json.JsonTextWriter(writer) { Formatting = Newtonsoft.Json.Formatting.Indented };
        //     var jsonSerializer = new Newtonsoft.Json.JsonSerializer();
        //     jsonSerializer.Serialize(jsonWriter, report);
        // }

        foreach (var (_, report) in reports) {

            var validationResult = await report.Validate();
            var results =
            validationResult.Runs.SelectMany(
                run => run.Results.Select(
                    result => new {
                        MessageId = result.Message.Id,
                        Arguments = string.Join("\n", result.Message.Arguments),
                        Location = string.Join(",", result.Locations.Select(location => $"{location.PhysicalLocation.Region.StartLine}:{location.PhysicalLocation.Region.StartColumn}"))
                    }
                )
            ).ToList();

            if (results.Any()) {

                _output.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(report, Newtonsoft.Json.Formatting.Indented));
                foreach (var result in results) {
                    _output.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented));
                }
            }

        }

    }
}



