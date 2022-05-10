using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using Microsoft.CodeAnalysis.Sarif;
using Microsoft.OneFuzz.Service;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;
using Xunit;
using Xunit.Abstractions;

namespace Tests;

public class SarifTests{

    ITestOutputHelper _output;

    public SarifTests(ITestOutputHelper output) {
        _output = output;
    }





    [Fact]
    public async System.Threading.Tasks.Task TestParseRport() {
        var testReport =
            @"
            {
                ""input_sha256"": ""4bdd0bbfe3f4c52cc2c8ff02f1fef29663dd9938f230304915805af1fa71e968"",
                ""input_blob"": {
                    ""account"": ""fuzzrabkn4vd3e2gy"",
                    ""container"": ""oft-crashes-2b72ce5bb0055954a4006004fd9233f6"",
                    ""name"": ""crash-229d028063a11904f846c91224abaa99113f3a15""
                },
                ""executable"": ""setup/fuzz.exe"",
                ""crash_type"": ""stack-buffer-overflow"",
                ""crash_site"": ""AddressSanitizer: stack-buffer-overflow D:\\a\\onefuzz\\onefuzz\\src\\integration-tests\\libfuzzer-linked-library\\bad2.c:38 in func2"",
                ""call_stack"": [
                    ""#0 0x7ffbfec41855 in func2 D:\\a\\onefuzz\\onefuzz\\src\\integration-tests\\libfuzzer-linked-library\\bad2.c:38"",
                    ""#1 0x7ff7b5351059 in LLVMFuzzerTestOneInput D:\\a\\onefuzz\\onefuzz\\src\\integration-tests\\libfuzzer-linked-library\\main.c:12"",
                    ""#2 0x7ff7b53bdcf8 in fuzzer::Fuzzer::ExecuteCallback C:\\src\\llvm_package_1300-final\\llvm-project\\compiler-rt\\lib\\fuzzer\\FuzzerLoop.cpp:611"",
                    ""#3 0x7ff7b53d27e5 in fuzzer::RunOneTest C:\\src\\llvm_package_1300-final\\llvm-project\\compiler-rt\\lib\\fuzzer\\FuzzerDriver.cpp:323"",
                    ""#4 0x7ff7b53d812c in fuzzer::FuzzerDriver C:\\src\\llvm_package_1300-final\\llvm-project\\compiler-rt\\lib\\fuzzer\\FuzzerDriver.cpp:856"",
                    ""#5 0x7ff7b5395d32 in main C:\\src\\llvm_package_1300-final\\llvm-project\\compiler-rt\\lib\\fuzzer\\FuzzerMain.cpp:20"",
                    ""#6 0x7ff7b53df8f7 in __scrt_common_main_seh d:\\a01\\_work\\6\\s\\src\\vctools\\crt\\vcstartup\\src\\startup\\exe_common.inl:288"",
                    ""#7 0x7ffc06557033 in BaseThreadInitThunk+0x13 (C:\\Windows\\System32\\KERNEL32.DLL+0x180017033)"",
                    ""#8 0x7ffc07922650 in RtlUserThreadStart+0x20 (C:\\Windows\\SYSTEM32\\ntdll.dll+0x180052650)""
                ],
                ""call_stack_sha256"": ""874ede7e372ce42241019e49699acf7085db77491106019d9064531b3c931e99"",
                ""minimized_stack"": [
                    ""#0 0x7ffbfec41855 in func2 D:\\a\\onefuzz\\onefuzz\\src\\integration-tests\\libfuzzer-linked-library\\bad2.c:38"",
                    ""#1 0x7ff7b5351059 in LLVMFuzzerTestOneInput D:\\a\\onefuzz\\onefuzz\\src\\integration-tests\\libfuzzer-linked-library\\main.c:12""
                ],
                ""minimized_stack_sha256"": ""1c3e435cdbc5e6f73480ed4bff3ac707822ae112c038191db6b9d8e6a6361029"",
                ""minimized_stack_function_names"": [
                    ""func2"",
                    ""main.c""
                ],
                ""minimized_stack_function_names_sha256"": ""5e1a2c94af1f7bd42b3184250d1ae03f6e8d552ca336d00d9d2756b7226489f5"",
                ""minimized_stack_function_lines"": [
                    ""func2 bad2.c:38"",
                    ""main.c main.c:12""
                ],
                ""minimized_stack_function_lines_sha256"": ""c21d40603b609f91ed2f1116ab229a991394582de48117daf4185d527e2faca7"",
                ""asan_log"": ""INFO: Running with entropic power schedule (0xFF,100).\r\nINFO: Seed: 3215303615\r\nINFO: Loaded 3 modules   (43 inline 8-bit counters): 21 [00007FFBFED44088,00007FFBFED4409D),21 [00007FFBFECB4088,00007FFBFECB409D),1 [00007FF7B54C1088,00007FF7B54C1089),\r\nINFO: Loaded 3 PC tables (43 PCs): 21 [00007FFBFED30398,00007FFBFED304E8),21 [00007FFBFECA0398,00007FFBFECA04E8),1 [00007FF7B547F790,00007FF7B547F7A0),\r\nsetup/fuzz.exe: Running 1 inputs 1 time(s) each.\r\nRunning: C:\\Windows\\Temp\\.tmpm8JTA8\\crash-229d028063a11904f846c91224abaa99113f3a15\r\n=================================================================\n==4220==ERROR: AddressSanitizer: stack-buffer-overflow on address 0x0064241eeefc at pc 0x7ffbfec41856 bp 0x0064241eee40 sp 0x0064241eee88\nWRITE of size 4 at 0x0064241eeefc thread T0\n==4220==WARNING: Failed to use and restart external symbolizer!\n    #0 0x7ffbfec41855 in func2 D:\\a\\onefuzz\\onefuzz\\src\\integration-tests\\libfuzzer-linked-library\\bad2.c:38\n    #1 0x7ff7b5351059 in LLVMFuzzerTestOneInput D:\\a\\onefuzz\\onefuzz\\src\\integration-tests\\libfuzzer-linked-library\\main.c:12\n    #2 0x7ff7b53bdcf8 in fuzzer::Fuzzer::ExecuteCallback C:\\src\\llvm_package_1300-final\\llvm-project\\compiler-rt\\lib\\fuzzer\\FuzzerLoop.cpp:611\n    #3 0x7ff7b53d27e5 in fuzzer::RunOneTest C:\\src\\llvm_package_1300-final\\llvm-project\\compiler-rt\\lib\\fuzzer\\FuzzerDriver.cpp:323\n    #4 0x7ff7b53d812c in fuzzer::FuzzerDriver C:\\src\\llvm_package_1300-final\\llvm-project\\compiler-rt\\lib\\fuzzer\\FuzzerDriver.cpp:856\n    #5 0x7ff7b5395d32 in main C:\\src\\llvm_package_1300-final\\llvm-project\\compiler-rt\\lib\\fuzzer\\FuzzerMain.cpp:20\n    #6 0x7ff7b53df8f7 in __scrt_common_main_seh d:\\a01\\_work\\6\\s\\src\\vctools\\crt\\vcstartup\\src\\startup\\exe_common.inl:288\n    #7 0x7ffc06557033 in BaseThreadInitThunk+0x13 (C:\\Windows\\System32\\KERNEL32.DLL+0x180017033)\n    #8 0x7ffc07922650 in RtlUserThreadStart+0x20 (C:\\Windows\\SYSTEM32\\ntdll.dll+0x180052650)\n\nAddress 0x0064241eeefc is located in stack of thread T0 at offset 60 in frame\n    #0 0x7ffbfec4102f in func2 D:\\a\\onefuzz\\onefuzz\\src\\integration-tests\\libfuzzer-linked-library\\bad2.c:13\n\n  This frame has 1 object(s):\n    [32,36) 'cnt' (line 14) <== Memory access at offset 60 overflows this variable\nHINT: this may be a false positive if your program uses some custom stack unwind mechanism,swapcontext or vfork\n      (longjmp,SEH and C++ exceptions *are* supported)\nSUMMARY: AddressSanitizer: stack-buffer-overflow D:\\a\\onefuzz\\onefuzz\\src\\integration-tests\\libfuzzer-linked-library\\bad2.c:38 in func2\nShadow bytes around the buggy address:\n  0x02096d23dd80: 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00\n  0x02096d23dd90: 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00\n  0x02096d23dda0: 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00\n  0x02096d23ddb0: 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00\n  0x02096d23ddc0: 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00\n=>0x02096d23ddd0: 00 00 00 00 00 00 00 00 f1 f1 f1 f1 04 f3 f3[f3]\n  0x02096d23dde0: 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00\n  0x02096d23ddf0: 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00\n  0x02096d23de00: 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00\n  0x02096d23de10: 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00\n  0x02096d23de20: 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00\nShadow byte legend (one shadow byte represents 8 application bytes):\n  Addressable:           00\n  Partially addressable: 01 02 03 04 05 06 07 \n  Heap left redzone:       fa\n  Freed heap region:       fd\n  Stack left redzone:      f1\n  Stack mid redzone:       f2\n  Stack right redzone:     f3\n  Stack after return:      f5\n  Stack use after scope:   f8\n  Global redzone:          f9\n  Global init order:       f6\n  Poisoned by user:        f7\n  Container overflow:      fc\n  Array cookie:            ac\n  Intra object redzone:    bb\n  ASan internal:           fe\n  Left alloca redzone:     ca\n  Right alloca redzone:    cb\n==4220==ABORTING\n"",
                ""task_id"": ""45407f33-6323-4682-9dd6-147ed345769e"",
                ""job_id"": ""f76bae27-0322-42b1-9cfb-6947be52240f""
            }";

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
                ""job_id"": ""748b4cca-b23e-447c-b45b-0ecd40295288""
            }";

        var rootpath = @"D:\a\onefuzz\onefuzz";
        var report = JsonSerializer.Deserialize<Report>(test2, EntityConverter.GetJsonSerializerOptions());
        Assert.NotNull(report);

        var sarif = SarifGenerator.ToSarif(rootpath, report!);
        var sarifJson = sarif.ToJsonString();


        var client = new HttpClient();

        var multiForm = new MultipartFormDataContent();

        using var mem = new MemoryStream();
        using var content = new StreamContent(mem);
        sarif.Save(mem);
        mem.Position = 0;
        multiForm.Add(content, "postedFiles", "test.sarif");
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, new Uri("https://sarifweb.azurewebsites.net/Validation/ValidateFiles"))
        {
            Content = multiForm,
        };

        var response = await client.SendAsync(httpRequest);
        Assert.True(response.IsSuccessStatusCode);
        // JsonDocument.ParseAsync()

        _output.WriteLine(await response.Content.ReadAsStringAsync());

    }


    [Fact]
    async System.Threading.Tasks.Task ValidateSarifFile() {

        var reportDir = @"./sample_reports";

        var reports =
            Directory.EnumerateFiles(reportDir, "*.json")
            .Select(x => JsonSerializer.Deserialize<Report>(File.ReadAllText(x), EntityConverter.GetJsonSerializerOptions()) ?? throw new Exception())
            .Select(x => SarifGenerator.ToSarif("/home/runner/work/onefuzz/onefuzz", x));



        foreach (var report in reports) { 
        
            var validationResult = await report.Validate();
            var results =
            validationResult.Runs.SelectMany(
                run => run.Results.Select(
                    result => new {
                        MessageId = result.Message.Id,
                        Arguments = string.Join("\n", result.Message.Arguments) ,
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



