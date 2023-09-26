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
    public const string CONTAINER_RETENTION_KEY = "onefuzz_retentionperiod";

    public static OneFuzzResult<TimeSpan?> GetContainerRetentionPeriodFromMetadata(IDictionary<string, string>? containerMetadata) {
        if (containerMetadata is not null &&
            containerMetadata.TryGetValue(CONTAINER_RETENTION_KEY, out var retentionString) &&
            !string.IsNullOrWhiteSpace(retentionString)) {
            try {
                return Result.Ok<TimeSpan?>(XmlConvert.ToTimeSpan(retentionString));
            } catch (Exception ex) {
                return Error.Create(ErrorCode.INVALID_RETENTION_PERIOD, ex.Message);
            }
        }

        return Result.Ok<TimeSpan?>(null);
    }
}
