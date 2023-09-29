using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;


namespace Microsoft.OneFuzz.Service;

public interface IReports {
    public Async.Task<IReport?> GetReportOrRegression(Container container, string fileName, bool expectReports = false, params string[] args);
    public Async.Task<Report?> GetReport(Container container, string fileName);
}

public class Reports : IReports {
    private ILogger _log;
    private IContainers _containers;
    public Reports(ILogger<Reports> log, IContainers containers) {
        _log = log;
        _containers = containers;
    }

    public async Task<Report?> GetReport(Container container, string fileName) {
        var result = await GetReportOrRegression(container, fileName);
        if (result != null && result is Report) {
            return result as Report;
        }

        return null;
    }

    public async Async.Task<IReport?> GetReportOrRegression(Container container, string fileName, bool expectReports = false, params string[] args) {
        var filePath = string.Join("/", new[] { container.String, fileName });
        if (!fileName.EndsWith(".json", StringComparison.Ordinal) || fileName.Contains("source-coverage", StringComparison.InvariantCultureIgnoreCase)) {
            if (expectReports) {
                _log.LogError("get_report invalid extension or filename: {FilePath}", filePath);
            }
            return null;
        }

        var containerClient = await _containers.FindContainer(container, StorageType.Corpus);
        if (containerClient == null) {
            if (expectReports) {
                _log.LogError("get_report invalid container: {FilePath}", filePath);
            }
            return null;
        }

        Uri reportUrl = containerClient.GetBlobClient(fileName).Uri;

        var blob = (await containerClient.GetBlobClient(fileName).DownloadContentAsync()).Value.Content;

        if (blob == null) {
            if (expectReports) {
                _log.LogError("get_report invalid blob: {FilePath}", filePath);
            }
            return null;
        }

        var reportOrRegression = ParseReportOrRegression(blob.ToString(), reportUrl);

        if (reportOrRegression is UnknownReportType && expectReports) {
            _log.LogError("unable to parse report ({FilePath}) as a report or regression", filePath);
        }

        return reportOrRegression;
    }

    private static T? TryDeserialize<T>(string content) where T : class {

        try {
            return JsonSerializer.Deserialize<T>(content, EntityConverter.GetJsonSerializerOptions());
        } catch (JsonException) {
            return null;
        }
    }

    public static IReport ParseReportOrRegression(string content, Uri reportUrl) {
        var regressionReport = TryDeserialize<RegressionReport>(content);
        if (regressionReport is { CrashTestResult: { } }) {
            return regressionReport with { ReportUrl = reportUrl };
        }
        var report = TryDeserialize<Report>(content);
        if (report is { CrashType: { } }) {
            return report with { ReportUrl = reportUrl };
        }
        return new UnknownReportType(reportUrl);
    }
}

[JsonConverter(typeof(ReportConverter))]
public interface IReport {
    Uri? ReportUrl {
        init;
        get;
    }
    public string FileName() {
        var segments = (this.ReportUrl ?? throw new ArgumentException()).Segments.Skip(2);
        return string.Concat(segments);
    }
};

public class ReportConverter : JsonConverter<IReport> {

    public override IReport? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        using var templateJson = JsonDocument.ParseValue(ref reader);

        if (templateJson.RootElement.TryGetProperty("crash_test_result", out _)) {
            return templateJson.Deserialize<RegressionReport>(options);
        }
        return templateJson.Deserialize<Report>(options);
    }

    public override void Write(Utf8JsonWriter writer, IReport value, JsonSerializerOptions options) {
        throw new NotImplementedException();
    }
}
