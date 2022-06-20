using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Faithlife.Utility;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;

// SubclassConverter allows serializing and deserializing a set of subclasses
// of the given T abstract base class, as long as all their properties are disjoint.
//
// It identifies which subclass to deserialize based upon the properties provided in the JSON.
public sealed class SubclassConverter<T> : JsonConverter<T> {
    private static readonly IReadOnlyList<(HashSet<string> props, Type type)> ChildTypes = FindChildTypes(typeof(T));

    private static List<(HashSet<string>, Type)> FindChildTypes(Type t) {
        if (!t.IsAbstract) {
            throw new ArgumentException("SubclassConverter can only be applied to abstract base classes");
        }

        // NB: assumes that the naming converter will always be the same, so we don’t need to regenerate the names each time
        var namer = new OnefuzzNamingPolicy();

        var result = new List<(HashSet<string> props, Type type)>();
        foreach (var type in t.Assembly.ExportedTypes) {
            if (type == t) {
                // skip the type itself
                continue;
            }

            if (type.IsAssignableTo(t)) {
                var props = type.GetProperties().Select(p => namer.ConvertName(p.Name)).ToHashSet();
                result.Add((props, type));
            }
        }

        // ensure that property names are all distinct
        for (int i = 0; i < result.Count; ++i) {
            for (int j = 0; j < result.Count; ++j) {
                if (i == j) {
                    continue;
                }

                var intersection = result[i].props.Intersect(result[j].props);
                if (intersection.Any()) {
                    throw new ArgumentException(
                        "Cannot use SubclassConverter on types with overlapping property names: "
                        + $" {result[i].type} and {result[j].type} share properties: {intersection.Join(", ")}");
                }
            }
        }

        return result;
    }

    private static Type FindType(Utf8JsonReader reader) {
        // note that this takes the reader by value instead of by 'ref'
        // this means it won't affect the reader passed in, which can be 
        // used to deserialize the whole object

        if (reader.TokenType != JsonTokenType.StartObject) {
            throw new JsonException($"Expected to be reading object, not {reader.TokenType}");
        }

        if (!reader.Read() || reader.TokenType != JsonTokenType.PropertyName) {
            throw new JsonException("Unable to read object property name");
        }

        var propertyName = reader.GetString();
        if (propertyName is null) {
            throw new JsonException("Unable to get property name");
        }

        foreach (var (props, type) in ChildTypes) {
            if (props.Contains(propertyName)) {
                return type;
            }
        }

        throw new JsonException($"No subclass found with property '{propertyName}'");
    }

    public override bool CanConvert(Type typeToConvert) {
        return typeToConvert == typeof(T) || ChildTypes.Any(x => x.type == typeToConvert);
    }

    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        Debug.Assert(options.PropertyNamingPolicy?.GetType() == typeof(OnefuzzNamingPolicy)); // see NB above

        var type = FindType(reader);
        return (T?)JsonSerializer.Deserialize(ref reader, type, options);
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) {
        Debug.Assert(options.PropertyNamingPolicy?.GetType() == typeof(OnefuzzNamingPolicy)); // see NB above
        Debug.Assert(value != null);

        // Note: we invoke GetType to get the derived type to serialize:
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }
}
