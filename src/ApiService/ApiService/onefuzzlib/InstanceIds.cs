using System.Text.RegularExpressions;

namespace Microsoft.OneFuzz.Service;

public static class InstanceIds {
    // Each VM in a scale set also gets a computer name assigned to it. This
    // computer name is the hostname of the VM in the Azure-provided DNS name
    // resolution within the virtual network. This computer name is of the form
    // "{computer-name-prefix}{base-36-instance-id}". The {base-36-instance-id}
    // is in base 36 and is always six digits in length. If the base 36
    // representation of the number takes fewer than six digits, the
    // {base-36-instance-id} is padded with zeros to make it six digits in
    // length. For example, an instance with {computer-name-prefix} "nsgvmss"
    // and instance ID 85 will have computer name "nsgvmss00002D".
    // https://learn.microsoft.com/en-us/azure/virtual-machine-scale-sets/virtual-machine-scale-sets-instance-ids#scale-set-vm-computer-name
    private static readonly Regex _instanceIdSuffix = new(@"[0-9a-zA-Z]{6}\z");
    public static long? InstanceIdFromComputerName(string computerName) {
        var match = _instanceIdSuffix.Match(computerName);
        if (!match.Success) {
            return null;
        }

        try {
            return ReadNumberInBase(match.ValueSpan, Base36);
        } catch (FormatException) {
            return null;
        }
    }

    private static readonly char[] _base36 = new[] {
        '0', '1', '2', '3', '4', '5', '6', '7', '8', '9',
        'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J',
        'K', 'L', 'M', 'N', 'O', 'P', 'Q', 'R', 'S', 'T',
        'U', 'V', 'W', 'X', 'Y', 'Z',
    };

    public static ReadOnlySpan<char> Base36 => _base36;

    public static long ReadNumberInBase(ReadOnlySpan<char> span, ReadOnlySpan<char> baseValues) {
        // this can be made faster by optimizing for the particular base 
        // but this is simple and it will not be invoked very often
        long result = 0;
        for (int i = 0; i < span.Length; ++i) {
            var value = baseValues.IndexOf(span.Slice(i, 1), StringComparison.OrdinalIgnoreCase);
            if (value < 0) {
                throw new FormatException($"Character '{span[i]}' not valid in base {baseValues.Length}");
            }

            result *= baseValues.Length;
            result += value;
        }

        return result;
    }
}
