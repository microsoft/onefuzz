using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Azure.Core;

namespace Microsoft.OneFuzz.Service;

static partial class Check {
    [GeneratedRegex("\\A[a-zA-Z0-9]+\\z")]
    private static partial Regex IsAlnumRegex();
    public static bool IsAlnum(string input) => IsAlnumRegex().IsMatch(input);

    [GeneratedRegex("\\A[a-zA-Z0-9\\-]+\\z")]
    private static partial Regex IsAlnumDashRegex();
    public static bool IsAlnumDash(string input) => IsAlnumDashRegex().IsMatch(input);

    // Permits 1-64 characters: alphanumeric, underscore, period, or dash.
    [GeneratedRegex("\\A[._a-zA-Z0-9\\-]{1,64}\\z")]
    private static partial Regex IsNameLikeRegex();
    public static bool IsNameLike(string input) => IsNameLikeRegex().IsMatch(input);
}

public interface IValidatedString<T> where T : IValidatedString<T> {
    public static abstract T Parse(string input);
    public static abstract bool IsValid(string input);
    public static abstract string Requirements { get; }
    public string String { get; }
}

public abstract record ValidatedStringBase<T> where T : IValidatedString<T> {
    protected ValidatedStringBase(string value) {
        if (!T.IsValid(value)) {
            throw new ArgumentException(T.Requirements);
        }

        String = value;
    }
    public string String { get; }

    public override string ToString() => String;

    public static bool TryParse(string input, [NotNullWhen(returnValue: true)] out T? result) {
        try {
            result = T.Parse(input);
            return true;
        } catch (ArgumentException) {
            result = default;
            return false;
        }
    }
}

// JSON converter for types that are wrappers around a validated string.
public sealed class ValidatedStringConverter<T> : JsonConverter<T> where T : IValidatedString<T> {
    public sealed override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        var value = reader.GetString();
        if (value is null) {
            throw new JsonException("expected a string");
        }

        if (ValidatedStringBase<T>.TryParse(value, out var result)) {
            return result;
        } else {
            throw new JsonException($"unable to parse input as a {typeof(T).Name}: {T.Requirements}");
        }
    }

    public sealed override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.String);
}

[JsonConverter(typeof(ValidatedStringConverter<PoolName>))]
public sealed record PoolName : ValidatedStringBase<PoolName>, IValidatedString<PoolName> {
    private PoolName(string value) : base(value) { }
    public static PoolName Parse(string input) => new(input);
    public static string Requirements => "Pool name must have only numbers, letters, underscores, periods, or dashes";

    // NOTE: PoolName is currently _not_ validated, since this 
    // can break existing users. When CSHARP-RELEASE happens, we can
    // try to synchronize other breaking changes with that.
    public static bool IsValid(string input) => true;
}

[JsonConverter(typeof(ValidatedStringConverter<Region>))]
public sealed record Region : ValidatedStringBase<Region>, IValidatedString<Region> {
    private Region(string value) : base(value.ToLowerInvariant()) { }
    public static Region Parse(string input) => new(input);
    public static bool IsValid(string input) => Check.IsAlnum(input);
    public static string Requirements => "Region name must have only numbers or letters";
    public static implicit operator AzureLocation(Region me) => new(me.String);
    public static implicit operator Region(AzureLocation it) => new(it.Name);
}

[JsonConverter(typeof(ValidatedStringConverter<Container>))]
public sealed record Container : ValidatedStringBase<Container>, IValidatedString<Container> {
    private Container(string value) : base(value) { }
    public static Container Parse(string input) => new(input);

    // See: https://docs.microsoft.com/en-us/azure/azure-resource-manager/management/resource-name-rules#microsoftstorage
    // - 3-63
    // - Lowercase letters, numbers, and hyphens.
    // - Start with lowercase letter or number. Can't use consecutive hyphens.
    private static readonly Regex _containerRegex = new(@"\A(?!-)(?!.*--)[a-z0-9\-]{3,63}\z", RegexOptions.Compiled);
    public static bool IsValid(string input) => _containerRegex.IsMatch(input);
    public static string Requirements => "Container name must be 3-63 lowercase letters, numbers, or hyphens";
}
