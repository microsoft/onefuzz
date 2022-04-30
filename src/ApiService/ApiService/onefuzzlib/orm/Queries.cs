namespace ApiService.OneFuzzLib.Orm {
    public static class Query {

        public static string Or(IEnumerable<string> queries) {
            return string.Join(" or ", queries.Select(x => $"({x})"));
        }

        public static string And(IEnumerable<string> queries) {
            return string.Join(" and ", queries.Select(x => $"({x})"));
        }

        public static string EqualAny(string property, IEnumerable<string> values) {
            return Or(values.Select(x => $"{property} eq '{x}'"));
        }

    }
}
