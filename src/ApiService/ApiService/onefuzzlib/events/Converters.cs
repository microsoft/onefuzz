using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.OneFuzz.Service {
    public class RemoveUserInfo : JsonConverter<UserInfo> {
        public override UserInfo? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
            throw new NotSupportedException("reading UserInfo is not supported");
        }

        public override void Write(Utf8JsonWriter writer, UserInfo value, JsonSerializerOptions options) {
            writer.WriteStartObject();
            writer.WriteEndObject();
        }
    }

    /// <summary>
    /// <b>THIS IS A WRITE ONLY JSON CONVERTER</b>
    /// <br/>
    /// It should only be used when serializing event messages to send via queue/webhooks
    /// </summary>
    public class EventExportConverter : JsonConverter<DownloadableEventMessage> {
        private static HashSet<Type> boundedTypes = new HashSet<Type>{
            typeof(Guid),
            typeof(DateTime),
            typeof(int),
            typeof(bool),
            typeof(float),
            typeof(double),
            typeof(long),
            typeof(char),
            typeof(Uri)
        };

        public override DownloadableEventMessage? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
            throw new NotSupportedException("This converter should only be used when serializing event messages to send via queue/webhooks");
        }

        public override void Write(Utf8JsonWriter writer, DownloadableEventMessage value, JsonSerializerOptions options) {
            WriteInternal(writer, value, options);
        }

        private static void WriteInternal(Utf8JsonWriter writer, object type, JsonSerializerOptions options) {
            writer.WriteStartObject();
            var properties = type.GetType().GetProperties();
            foreach (var property in properties) {
                if (property.GetValue(type, null) == null
                    || typeof(IEnumerable).IsAssignableFrom(property.PropertyType)
                    || type.GetType() == property.PropertyType) {
                    continue;
                }
                if (HasBoundedSerialization(property)) {
                    var serialized = JsonSerializer.Serialize(property.GetValue(type, null), property.PropertyType, options);
                    if (!string.IsNullOrEmpty(serialized)) {
                        writer.WritePropertyName(property.Name);
                        writer.WriteRawValue(serialized);
                    }
                } else if (property.PropertyType.IsClass) {
                    writer.WritePropertyName(property.Name);
                    WriteInternal(writer, property.GetValue(type, null)!, options);
                }
            }
            writer.WriteEndObject();
        }

        public static bool HasBoundedSerialization(PropertyInfo propertyInfo) {
            return propertyInfo.PropertyType.IsEnum ||
                boundedTypes.Contains(propertyInfo.PropertyType) ||
                typeof(IValidatedString).IsAssignableFrom(propertyInfo.PropertyType);
        }

    }
}
