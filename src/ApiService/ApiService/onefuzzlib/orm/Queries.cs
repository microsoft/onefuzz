using System.Text.Json;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace ApiService.OneFuzzLib.Orm {
    public static class Query {
        public static string PartitionKey(string partitionKey) {
            // TODO: need to escape
            return $"PartitionKey eq '{partitionKey}'";
        }

        public static string RowKey(string rowKey) {
            // TODO: need to escape
            return $"RowKey eq '{rowKey}'";
        }

        public static string SingleEntity(string partitionKey, string rowKey) {
            // TODO: need to escape
            return $"(PartitionKey eq '{partitionKey}') and (RowKey eq '{rowKey}')";
        }

        public static string Or(IEnumerable<string> queries) {
            return string.Join(" or ", queries.Select(x => $"({x})"));
        }

        public static string Or(string q1, string q2) {
            return Or(new[] { q1, q2 });
        }

        public static string And(IEnumerable<string> queries) {
            return string.Join(" and ", queries.Select(x => $"({x})"));
        }

        public static string And(string q1, string q2) {
            return And(new[] { q1, q2 });
        }


        public static string EqualAny(string property, IEnumerable<string> values) {
            return Or(values.Select(x => $"{property} eq '{x}'"));
        }


        public static string EqualAnyEnum<T>(string property, IEnumerable<T> enums) where T : Enum {
            IEnumerable<string> convertedEnums = enums.Select(x => JsonSerializer.Serialize(x, EntityConverter.GetJsonSerializerOptions()).Trim('"'));
            return EqualAny(property, convertedEnums);
        }

        public static string TimeRange(DateTimeOffset min, DateTimeOffset max) {
            // NB: this uses the auto-populated Timestamp property, and will result in scanning
            // TODO: should this be inclusive at the endpoints?
            return $"Timestamp lt datetime'{max:o}' and Timestamp gt datetime'{min:o}'";
        }

        public static string StartsWith(string property, string prefix) {
            var upperBound = prefix[..(prefix.Length - 1)] + (char)(prefix.Last() + 1);
            // TODO: escaping
            return $"{property} ge '{prefix}' and {property} lt '{upperBound}'";
        }
    }
}
