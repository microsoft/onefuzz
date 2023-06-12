namespace Microsoft.OneFuzz.Service;


public interface IRetentionPolicy {
    DateOnly GetExpiryDate();
}

public class RetentionPolicyUtils {
    public static KeyValuePair<string, string> CreateExpiryDateTag(DateOnly expiryDate) =>
        new("Expiry", expiryDate.ToString());


    // TODO: Make sure this query ONLY returns blobs with an expiry tag
    public static string CreateExpiredBlobTagFilter() => $@"""Expiry"" <= '{DateOnly.FromDateTime(DateTime.UtcNow)}'";
}
