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

        public static bool GetBoolValue(string key, IDictionary<string, string> query, bool defaultValue = false) {
            bool v;
            if (query.ContainsKey(key)) {
                if (!bool.TryParse(query[key], out v)) {
                    v = defaultValue;
                }
            } else {
                v = defaultValue;
            }
            return v;
        }


    }
}
