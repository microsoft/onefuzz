using System.Text.Json;

namespace FunctionalTests;

public class Error : IComparable<Error>, IFromJsonElement<Error> {
    private readonly JsonElement _e;

    public Error(JsonElement e) {
        _e = e;
    }

    public int StatusCode => _e.GetIntProperty("status");

    public string Title => _e.GetStringProperty("title");

    public string Detail => _e.GetStringProperty("detail");

    public static Error Convert(JsonElement e) => new(e);

    public static bool IsError(JsonElement res) {
        return res.ValueKind == JsonValueKind.Object
            && res.TryGetProperty("title", out _)
            && res.TryGetProperty("detail", out _);
    }

    public int CompareTo(Error? other) {
        if (other is null) {
            return -1;
        }

        var statusCompare = StatusCode.CompareTo(other.StatusCode);
        if (statusCompare != 0) {
            return statusCompare;
        }

        var titleCompare = Title.CompareTo(other.Title);
        if (titleCompare != 0) {
            return titleCompare;
        }

        var detailCompare = Detail.CompareTo(other.Detail);
        if (detailCompare != 0) {
            return detailCompare;
        }

        return 0;
    }

    public override string ToString() {
        return _e.ToString();
    }

    public bool IsWrongSizeError =>
        Title == "INVALID_REQUEST" && Detail.Contains("The field Size must be between 1 and 9.223372036854776E+18.");

    public bool UnableToFindPoolError => Title == "INVALID_REQUEST" && Detail.Contains("unable to find pool");

    public bool UnableToFindScalesetError => Title == "INVALID_REQUEST" && Detail.Contains("unable to find scaleset");

    public bool UnableToFindNode => Title == "UNABLE_TO_FIND" && Detail.Contains("unable to find node");

    public bool ShouldBeProvided(string p) => Title == "INVALID_REQUEST" && Detail.Contains($"'{p}' query parameter must be provided");

    public bool UnableToFindTask => Title == "INVALID_REQUEST" && Detail.Contains("unable to find task");
}
