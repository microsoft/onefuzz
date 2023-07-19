using System.Runtime.CompilerServices;
using System.Text.Json;
using Azure.Data.Tables;
using Microsoft.OneFuzz.Service;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace ApiService.OneFuzzLib.Orm {
    public static class Query {
        // For all queries below, note that TableClient.CreateQueryFilter takes a FormattableString
        // and handles escaping the interpolated values properly. It also handles quoting the values
        // where needed, so use {string} and not '{string}'.

        public static string CreateQueryFilter(FormattableString input) {
            var args = input.GetArguments();

            for (int i = 0; i < args.Length; i++) {
                if (args[i] is Guid g) {
                    args[i] = g.ToString();
                } else if (args[i] is IValidatedString s) {
                    args[i] = s.String;
                }
            }

            return TableClient.CreateQueryFilter(FormattableStringFactory.Create(input.Format, args));
        }
        public static string PartitionKey(string partitionKey)
            => CreateQueryFilter($"PartitionKey eq {partitionKey}");

        public static string RowKey(string rowKey)
            => CreateQueryFilter($"RowKey eq {rowKey}");

        public static string PartitionKeys(IEnumerable<string> partitionKeys)
            => Or(partitionKeys.Select(PartitionKey));

        public static string RowKeys(IEnumerable<string> rowKeys)
            => Or(rowKeys.Select(RowKey));

        public static string SingleEntity(string partitionKey, string rowKey)
            => CreateQueryFilter($"(PartitionKey eq {partitionKey}) and (RowKey eq {rowKey})");

        public static string Or(IEnumerable<string> queries)
            // subqueries should already be properly escaped
            => string.Join(" or ", queries.Select(x => $"({x})"));

        public static string Or(string q1, string q2) => Or(new[] { q1, q2 });

        public static string And(IEnumerable<string> queries)
            // subqueries should already be properly escaped
            => string.Join(" and ", queries.Select(x => $"({x})"));

        public static string And(string q1, string q2) => And(new[] { q1, q2 });


        public static string EqualAny(string property, IEnumerable<string> values) {
            // property should not be escaped, but the string should be:
            return Or(values.Select(x => $"{property} eq '{EscapeString(x)}'"));
        }

        public static string EqualAnyEnum<T>(string property, IEnumerable<T> enums) where T : Enum {
            IEnumerable<string> convertedEnums = enums.Select(x => JsonSerializer.Serialize(x, EntityConverter.GetJsonSerializerOptions()).Trim('"'));
            return EqualAny(property, convertedEnums);
        }

        public static string EqualEnum<T>(string property, T e) where T : Enum {
            string convertedEnum = JsonSerializer.Serialize(e, EntityConverter.GetJsonSerializerOptions()).Trim('"');
            return $"{property} eq '{EscapeString(convertedEnum)}'";
        }

        public static string TimeRange(DateTimeOffset min, DateTimeOffset max) {
            // NB: this uses the auto-populated Timestamp property, and will result in a table scan
            // TODO: should this be inclusive at the endpoints?
            return CreateQueryFilter($"Timestamp lt {max} and Timestamp gt {min}");
        }

        public static string TimestampNewerThan(DateTimeOffset t) {
            return CreateQueryFilter($"Timestamp gt {t}");
        }
        public static string NewerThan(string field, DateTimeOffset t) {
            return $"{field} gt {CreateQueryFilter($"{t}")}";
        }
        public static string TimestampOlderThan(DateTimeOffset t) {
            return CreateQueryFilter($"Timestamp lt {t}");
        }

        public static string OlderThan(string field, DateTimeOffset t) {
            return $"{field} lt {CreateQueryFilter($"{t}")}";
        }

        public static string StartsWith(string property, string prefix) {
            var upperBound = prefix[..(prefix.Length - 1)] + (char)(prefix.Last() + 1);
            // property name should not be escaped, but strings should be:
            return $"{property} ge '{EscapeString(prefix)}' and {property} lt '{EscapeString(upperBound)}'";
        }

        // makes a string safe for interpolation between '…'
        private static string EscapeString(string s) => s.Replace("'", "''");
    }
}
