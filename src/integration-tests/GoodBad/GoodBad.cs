// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace GoodBad;

public class BinaryParser
{
    int count = 0;

    public void ProcessInput(ReadOnlySpan<byte> data) {
        if (data.Length < 4) {
            return;
        }

        if (data[0] == 'b') { count++; }
        if (data[1] == 'a') { count++; }
        if (data[2] == 'd') { count++; }
        if (data[3] == '!') { count++; }

        // Simulate an out-of-bounds access while parsing.
        if (count >= 4) {
            var _ = data[0xdead];
        }
    }
}

public class Fuzzer {
    /// Preferred test method.
    public static void TestInput(ReadOnlySpan<byte> data) {
            var parser = new BinaryParser();
            parser.ProcessInput(data);
    }

    /// Backwards-compatible test method for legacy code that can't use `Span` types.
    ///
    /// Incurs an extra copy of `data` per fuzzing iteration.
    public static void TestInputCompat(byte[] data) {
            var parser = new BinaryParser();
            parser.ProcessInput(data);
    }

    /// Invalid static method that has a fuzzing-incompatible signature.
    public static void BadSignature(ReadOnlySpan<int> data) {
        return;
    }
}
