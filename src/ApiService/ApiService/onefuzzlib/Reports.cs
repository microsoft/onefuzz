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

        return ParseReportOrRegression(blob.ToString(), filePath, reportUrl, expectReports);
    }

    private IReport? ParseReportOrRegression(string content, string? filePath, Uri? reportUrl, bool expectReports = false) {
        var regressionReport = JsonSerializer.Deserialize<RegressionReport>(content, EntityConverter.GetJsonSerializerOptions());
        if (regressionReport == null || regressionReport.CrashTestResult == null) {
            try {
                var report = JsonSerializer.Deserialize<Report>(content, EntityConverter.GetJsonSerializerOptions());
                return report != null ? report with { ReportUrl = reportUrl } : report;
            } catch (JsonException e) {
                if (expectReports) {
                    _log.Error($"unable to parse report ({filePath:Tag:FilePath}) as a report or regression - {e}");
                }
                return null;
            }
        }
        return regressionReport != null ? regressionReport with { ReportUrl = reportUrl } : regressionReport;
    }
}

public interface IReport {
    Uri? ReportUrl {
        init;
    }
};
