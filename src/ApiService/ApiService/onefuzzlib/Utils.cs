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

        var enumerator = source.GetAsyncEnumerator();
        List<TSource> result = new List<TSource>(size);
        while (await enumerator.MoveNextAsync()) {
            if (result.Count == size) {
                yield return result;
                result = new List<TSource>(size);
            }
            result.Add(enumerator.Current);
        }

        if (result.Count > 0) {
            yield return result;
        }
    }
}
