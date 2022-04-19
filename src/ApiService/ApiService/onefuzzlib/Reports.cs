namespace Microsoft.OneFuzz.Service;

public class Reports
{
    private ILogTracer _log;
    private IContainers _containers;
    public Reports(ILogTracer log, IContainers containers)
    {
        _log = log;
        _containers = containers;
    }

    public async void GetReportOrRegression(Container container, string fileName, bool ExactReports = false, params string[] args)
    {
        var filePath = String.Join("/", new[] { container.ContainerName, fileName });
        if (!fileName.EndsWith(".json"))
        {
            if (ExactReports)
            {
                _log.Error($"get_report invalid extension: {filePath}");
            }
            return;
        }

        var blob = await _containers.GetBlob(container, fileName, StorageType.Corpus);

        if (blob == null)
        {
            if (ExactReports)
            {
                _log.Error($"get_report invalid blob: {filePath}");
            }
            return;
        }

        //return parse_report_or_regression(blob, file_path=file_path, expect_reports=expect_reports)
    }

    private void ParseReportOrRegression(string content, string? filePath, bool expectReports = false)
    {
        // var data = JsonSerializer.Deserialize<Dictionary
    }

    private void ParseReportOrRegression(IEnumerable<byte> content, string? filePath, bool expectReports = false)
    {
        try
        {
            var str = System.Text.Encoding.UTF8.GetString(content.ToArray());
            ParseReportOrRegression(str, filePath, expectReports);
        }
        catch (Exception e)
        {
            if (expectReports)
            {
                _log.Error($"unable to parse report ({filePath}): unicode decode of report failed - {e.Message} {e.StackTrace}");
            }
            return;
        }
    }
}
