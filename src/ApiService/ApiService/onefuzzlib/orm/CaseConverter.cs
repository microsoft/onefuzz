namespace Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

public class CaseConverter {
    /// get the start indices of each word and the lat indice
    static IEnumerable<int> getIndices(string input) {

        yield return 0;
        for (var i = 1; i < input.Length; i++) {

            if (Char.IsDigit(input[i])) {
                continue;
            }

            if (i < input.Length - 1 && Char.IsDigit(input[i + 1])) {
                continue;
            }

            // is the current letter uppercase
            if (Char.IsUpper(input[i])) {
                if (input[i - 1] == '_') {
                    continue;
                }

                if (i < input.Length - 1 && !Char.IsUpper(input[i + 1])) {
                    yield return i;
                } else if (!Char.IsUpper(input[i - 1])) {
                    yield return i;
                }
            }
        }
        yield return input.Length;
    }

    public static string PascalToSnake(string input) {
        var indices = getIndices(input).ToArray();
        return string.Join("_", Enumerable.Zip(indices, indices.Skip(1)).Select(x => input.Substring(x.First, x.Second - x.First).ToLowerInvariant()));

    }
    public static string SnakeToPascal(string input) {
        return string.Join("", input.Split('_', StringSplitOptions.RemoveEmptyEntries).Select(x => $"{Char.ToUpper(x[0])}{x.Substring(1)}"));
    }
}
