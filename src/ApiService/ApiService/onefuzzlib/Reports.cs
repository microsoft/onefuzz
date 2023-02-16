using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;

public interface IReports {
    public Async.Task<IReport?> GetReportOrRegression(Container container, string fileName, bool expectReports = false, params string[] args);
    public Async.Task<Report?> GetReport(Container container, string fileName);
}

public class Reports : IReports {
    private ILogTracer _log;
    private IContainers _containers;
    public Reports(ILogTracer log, IContainers containers) {
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
        if (!fileName.EndsWith(".json", StringComparison.Ordinal)) {
            if (expectReports) {
                _log.Error($"get_report invalid extension: {filePath:Tag:FilePath}");
            }
            return null;
        }

        var blob = await _containers.GetBlob(container, fileName, StorageType.Corpus);

        if (blob == null) {
            if (expectReports) {
                _log.Error($"get_report invalid blob: {filePath:Tag:FilePath}");
            }
            return null;
        }

        var reportUrl = await _containers.GetFileUrl(container, fileName, StorageType.Corpus);

        var reportOrRegression = ParseReportOrRegression(blob.ToString(), reportUrl);

        if (reportOrRegression == null && expectReports) {
            _log.Error($"unable to parse report ({filePath:Tag:FilePath}) as a report or regression");
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

    public static IReport? ParseReportOrRegression(string content, Uri? reportUrl) {
        var regressionReport = TryDeserialize<RegressionReport>(content);
        if (regressionReport is { CrashTestResult: { } }) {
            return regressionReport with { ReportUrl = reportUrl };
        }
        var report = TryDeserialize<Report>(content);
        if (report is { CrashType: { } }) {
            return report with { ReportUrl = reportUrl };
        }
        return null;
    }
}

public interface IReport {
    Uri? ReportUrl {
        init;
    }
};
