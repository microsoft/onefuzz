using System.Text.Json;
using Xunit;

namespace FunctionalTests;

public class Error : IComparable<Error>, IFromJsonElement<Error> {
    JsonElement _e;

    public Error(JsonElement e) {
        _e = e;
        Assert.True(_e.EnumerateObject().Count() == 2);
    }

    public int Code => _e.GetProperty("code").GetInt32();

    public IEnumerable<string> Errors => _e.GetProperty("errors").EnumerateArray().Select(e => e.GetString()!);

    public Error Convert(JsonElement e) => new Error(e);

    public static bool IsError(JsonElement res) {
        return res.ValueKind == JsonValueKind.Object && res.TryGetProperty("code", out _) && res.TryGetProperty("errors", out _);
    }

    public int CompareTo(Error? other) {
        if (other is null) {
            return -1;
        }

        var sameErrorMessages = Errors.Count() == other.Errors.Count();
        foreach (var s in other.Errors) {
            if (!sameErrorMessages) break;
            sameErrorMessages = Errors.Contains(s);
        }

        if (other.Code == this.Code && sameErrorMessages) {
            return 0;
        } else
            return 1;
    }

    public override string ToString() {
        return _e.ToString();
    }

    public bool IsWrongSizeError =>
        Code == 450 && Errors.First() == "The field Size must be between 1 and 9.223372036854776E+18.";

    public bool UnableToFindPoolError => Code == 450 && Errors.First() == "unable to find pool";

    public bool UnableToFindScalesetError => Code == 450 && Errors.First() == "unable to find scaleset";

    public bool UnableToFindNode => Code == 467 && Errors.First() == "unable to find node ";
}
