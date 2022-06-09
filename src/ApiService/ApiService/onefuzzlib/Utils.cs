namespace Microsoft.OneFuzz.Service;

public static class ObjectExtention {
    public static T EnsureNotNull<T>(this T? thisObject, string message) {
        if (thisObject == null) {
            throw new ArgumentNullException(message);
        }
        return thisObject;
    }
}
