using System.Xml;

namespace Microsoft.OneFuzz.Service;


public interface IRetentionPolicy {
    DateOnly GetExpiryDate();
}

public class RetentionPolicyUtils {
    public const string EXPIRY_TAG = "Expiry";
    public static KeyValuePair<string, string> CreateExpiryDateTag(DateOnly expiryDate) =>
        new(EXPIRY_TAG, expiryDate.ToString());

    public static DateOnly? GetExpiryDateTagFromTags(IDictionary<string, string>? blobTags) {
        if (blobTags != null &&
            blobTags.TryGetValue(EXPIRY_TAG, out var expiryTag) &&
            !string.IsNullOrWhiteSpace(expiryTag) &&
            DateOnly.TryParse(expiryTag, out var expiryDate)) {
            return expiryDate;
        }
        return null;
    }

    public static string CreateExpiredBlobTagFilter() => $@"""{EXPIRY_TAG}"" <= '{DateOnly.FromDateTime(DateTime.UtcNow)}'";

    // NB: this must match the value used on the CLI side
    public const string RETENTION_KEY = "onefuzz_retentionperiod";

    public static TimeSpan? GetRetentionPeriodFromMetadata(IDictionary<string, string>? containerMetadata) {
        if (containerMetadata is not null &&
            containerMetadata.TryGetValue(RETENTION_KEY, out var retentionString) &&
            !string.IsNullOrWhiteSpace(retentionString)) {
            try {
                return XmlConvert.ToTimeSpan(retentionString);
            } catch {
                // Log error: unable to convert xxx 
                return null;
            }
        }

        return null;
    }
}
