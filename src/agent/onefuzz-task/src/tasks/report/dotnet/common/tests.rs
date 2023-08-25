use anyhow::Result;

use super::parse_sos_print_exception_output;

const PRINT_EXCEPTION_STDOUT: &str = include_str!("data/print-exception.stdout");

#[test]
fn test_parse_sos_print_exception() -> Result<()> {
    let ei = parse_sos_print_exception_output(PRINT_EXCEPTION_STDOUT)?;

    assert_eq!(ei.exception, "System.IndexOutOfRangeException");
    assert_eq!(ei.message, "Index was outside the bounds of the array.");
    assert_eq!(ei.inner_exception, None);
    assert_eq!(ei.call_stack, vec![
        "System.Private.CoreLib.dll!System.ThrowHelper.ThrowIndexOutOfRangeException()+0x35 [/_/src/libraries/System.Private.CoreLib/src/System/ThrowHelper.cs @ 65]",
        "System.Private.CoreLib.dll!System.ReadOnlySpan`1[[System.Byte, System.Private.CoreLib]].get_Item(Int32)+0x19 [/_/src/libraries/System.Private.CoreLib/src/System/ReadOnlySpan.cs @ 136]",
        "GoodBad.dll!GoodBad.Parser.ParseInput(System.ReadOnlySpan`1<Byte>)+0x39f",
        "GoodBad.dll!GoodBad.ParserLibFuzzer.TestOneInput(System.ReadOnlySpan`1<Byte>)+0x82",
        "SharpFuzz.dll!SharpFuzz.Fuzzer+LibFuzzer.RunWithoutLibFuzzer(SharpFuzz.ReadOnlySpanAction)+0x135",
        "SharpFuzz.dll!SharpFuzz.Fuzzer+LibFuzzer.Run(SharpFuzz.ReadOnlySpanAction)+0x8b",
        "LibFuzzerSharp.dll!LibFuzzerSharp.Program.TryTestOne[[System.__Canon, System.Private.CoreLib]](System.Reflection.MethodInfo, System.Func`2<System.__Canon,SharpFuzz.ReadOnlySpanAction>)+0xd1",
        "LibFuzzerSharp.dll!LibFuzzerSharp.Program.TryTestOneSpan(System.Reflection.MethodInfo)+0x112",
        "LibFuzzerSharp.dll!LibFuzzerSharp.Program.Main(System.String[])+0x39f",
    ]);
    assert_eq!(ei.hresult, "80131508");

    Ok(())
}
