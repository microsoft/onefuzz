using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Azure.Core;

namespace Microsoft.OneFuzz.Service;

static class Check {
    private static readonly Regex _isAlnum = new(@"\A[a-zA-Z0-9]+\z", RegexOptions.Compiled);
    public static bool IsAlnum(string input) => _isAlnum.IsMatch(input);

    private static readonly Regex _isAlnumDash = new(@"\A[a-zA-Z0-9\-]+\z", RegexOptions.Compiled);
    public static bool IsAlnumDash(string input) => _isAlnumDash.IsMatch(input);

    // Permits 1-64 characters: alphanumeric, underscore, period, or dash.
    private static readonly Regex _isNameLike = new(@"\A[._a-zA-Z0-9\-]{1,64}\z", RegexOptions.Compiled);
    public static bool IsNameLike(string input) => _isNameLike.IsMatch(input);
}

// Base class for types that are wrappers around a validated string.
public abstract record ValidatedString(string String) {
    public sealed override string ToString() => String;
}

// JSON converter for types that are wrappers around a validated string.
public abstract class ValidatedStringConverter<T> : JsonConverter<T> where T : ValidatedString {
    protected abstract bool TryParse(string input, out T? output);

    public sealed override bool CanConvert(Type typeToConvert)
        => typeToConvert == typeof(T);

    public sealed override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        if (reader.TokenType != JsonTokenType.String) {
            throw new JsonException("expected a string");
        }

        var value = reader.GetString();
        if (value is null) {
            throw new JsonException("expected a string");
        }

        if (TryParse(value, out var result)) {
            return result;
        } else {
            throw new JsonException($"unable to parse input as a {typeof(T).Name}");
        }
    }

    public sealed override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.String);
}

[JsonConverter(typeof(Converter))]
public sealed record PoolName : ValidatedString {
    // NOTE: PoolName is currently _not_ validated, since this 
    // can break existing users. When CSHARP-RELEASE happens, we can
    // try to synchronize other breaking changes with that.
    private static bool IsValid(string input) => true;

    private PoolName(string value) : base(value) {
        Debug.Assert(IsValid(value));
    }

    public static PoolName Parse(string input) {
        if (TryParse(input, out var result)) {
            return result;
        }

        throw new ArgumentException("Pool name must have only numbers, letters, underscores, periods, or dashes");
    }

    public static bool TryParse(string input, [NotNullWhen(returnValue: true)] out PoolName? result) {
        if (!IsValid(input)) {
            result = default;
            return false;
        }

        result = new PoolName(input);
        return true;
    }

    public sealed class Converter : ValidatedStringConverter<PoolName> {
        protected override bool TryParse(string input, out PoolName? output)
            => PoolName.TryParse(input, out output);
    }
}

[JsonConverter(typeof(Converter))]
public record Region : ValidatedString {
    private static bool IsValid(string input) => Check.IsAlnum(input);

    private Region(string value) : base(value.ToLowerInvariant()) {
        Debug.Assert(IsValid(value));
    }

    public static Region Parse(string input) {
        if (TryParse(input, out var result)) {
            return result;
        }

        throw new ArgumentException("Region name must have only numbers or letters");
    }

    public static bool TryParse(string input, [NotNullWhen(returnValue: true)] out Region? result) {
        if (!IsValid(input)) {
            result = default;
            return false;
        }

        result = new Region(input);
        return true;
    }

    public static implicit operator AzureLocation(Region me) => new(me.String);
    public static implicit operator Region(AzureLocation it) => new(it.Name);

    public sealed class Converter : ValidatedStringConverter<Region> {
        protected override bool TryParse(string input, out Region? output)
            => Region.TryParse(input, out output);
    }
}

[JsonConverter(typeof(Converter))]
public record Container : ValidatedString {
    // See: https://docs.microsoft.com/en-us/azure/azure-resource-manager/management/resource-name-rules#microsoftstorage
    // - 3-63
    // - Lowercase letters, numbers, and hyphens.
    // - Start with lowercase letter or number. Can't use consecutive hyphens.
    private static readonly Regex _containerRegex = new(@"\A(?!-)(?!.*--)[a-z0-9\-]{3,63}\z", RegexOptions.Compiled);

    private static bool IsValid(string input) => _containerRegex.IsMatch(input);

    private Container(string value) : base(value) {
        Debug.Assert(IsValid(value));
    }

    public static Container Parse(string input) {
        if (TryParse(input, out var result)) {
            return result;
        }

        throw new ArgumentException("Container name must have only numbers, letters or dashes");
    }

    public static bool TryParse(string input, [NotNullWhen(returnValue: true)] out Container? result) {
        if (!IsValid(input)) {
            result = default;
            return false;
        }

        result = new Container(input);
        return true;
    }

    public sealed class Converter : ValidatedStringConverter<Container> {
        protected override bool TryParse(string input, out Container? output)
            => Container.TryParse(input, out output);
    }
}
