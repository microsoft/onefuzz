namespace ApiService.TestHooks {
    public class UriExtension {

        public static IDictionary<string, string> GetQueryComponents(System.Uri uri) {
            var queryComponents = uri.GetComponents(UriComponents.Query, UriFormat.UriEscaped).Split("&");

            var q =
                from cs in queryComponents
                where !string.IsNullOrEmpty(cs)
                let i = cs.IndexOf('=')
                select new KeyValuePair<string, string>(Uri.UnescapeDataString(cs.Substring(0, i)), Uri.UnescapeDataString(cs.Substring(i + 1)));

            return new Dictionary<string, string>(q);
        }

        public static bool GetBool(string key, IDictionary<string, string> query, bool defaultValue = false) {
            bool v;
            if (query.TryGetValue(key, out var value)) {
                v = bool.Parse(value);
            } else {
                v = defaultValue;
            }
            return v;
        }

        public static int? GetInt(string key, IDictionary<string, string> query, int? defaultValue = null) {
            int? v;
            if (query.TryGetValue(key, out var value)) {
                v = int.Parse(value);
            } else {
                v = defaultValue;
            }
            return v;
        }


        public static string? GetString(string key, IDictionary<string, string> query, string? defaultValue = null) {
            if (query.TryGetValue(key, out var value)) {
                return value;
            } else {
                return defaultValue;
            }
        }

        public static Guid? GetGuid(string key, IDictionary<string, string> query, Guid? defaultValue = null) {
            if (query.TryGetValue(key, out var value)) {
                return Guid.Parse(value);
            } else {
                return defaultValue;
            }
        }


    }
}
