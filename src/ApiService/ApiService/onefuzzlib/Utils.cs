using System.Diagnostics.CodeAnalysis;

namespace Microsoft.OneFuzz.Service;

public static class ObjectExtention {
    public static T EnsureNotNull<T>(this T? thisObject, string message) {
        if (thisObject == null) {
            throw new ArgumentException(message);
        }

        return thisObject;
    }

    // Explicitly discards the result value.
    // In general we should not do this; eventually all call-sites should
    // be updated.
    public static Async.Task IgnoreResult<T>(this Async.Task<T> task)
        => task;
}

public static class IAsyncEnumerableExtension {
    public static async IAsyncEnumerable<List<TSource>> Chunk<TSource>(this IAsyncEnumerable<TSource> source, int size) {

        if (size <= 0) {
            throw new ArgumentException("size must be greater than 0");
        }

        var enumerator = source.GetAsyncEnumerator();
        List<TSource> result = new List<TSource>(size);
        while (await enumerator.MoveNextAsync()) {
            result.Add(enumerator.Current);

            if (result.Count == size) {
                yield return result;
                result = new List<TSource>(size);
            }
        }

        if (result.Count > 0) {
            yield return result;
        }
    }
}

public static class TruncateUtils {
    public static List<string> TruncateList(List<string> data, int maxLength) {
        int currentLength = 0;
        return data.TakeWhile(curr => (currentLength += curr.Length) <= maxLength).ToList();
    }

    [return: NotNullIfNotNull(nameof(data))]
    public static string? TruncateString(string? data, int maxLength) {
        return data?[..Math.Min(data.Length, maxLength)];
    }
}
