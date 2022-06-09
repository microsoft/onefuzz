using System.Text.Json;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace ApiService.OneFuzzLib.Orm {
    public static class Query {

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
            return Query.EqualAny(property, convertedEnums);
        }
    }
}
