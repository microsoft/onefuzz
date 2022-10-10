namespace Microsoft.OneFuzz.Service;

public class JinjaTemplateAdapter {
    public static bool IsJinjaTemplate(string jinjaTemplate) {
        return jinjaTemplate.Contains("{% endfor %}")
            || jinjaTemplate.Contains("{% endif %}");
    }
    public static string AdaptForScriban(string jinjaTemplate) {
        return jinjaTemplate.Replace("endfor", "end")
            .Replace("endif", "end")
            .Replace("{%", "{{")
            .Replace("%}", "}}");
    }
}
