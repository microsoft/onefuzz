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
