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
        var filePath = String.Join("/", new[] { container.ContainerName, fileName });
        if (!fileName.EndsWith(".json", StringComparison.Ordinal)) {
            if (expectReports) {
                _log.Error($"get_report invalid extension: {filePath}");
            }
            return null;
        }

        var blob = await _containers.GetBlob(container, fileName, StorageType.Corpus);

        if (blob == null) {
            if (expectReports) {
                _log.Error($"get_report invalid blob: {filePath}");
            }
            return null;
        }

        return ParseReportOrRegression(blob.ToString(), filePath, expectReports);
    }

    private IReport? ParseReportOrRegression(string content, string? filePath, bool expectReports = false) {
        var regressionReport = JsonSerializer.Deserialize<RegressionReport>(content, EntityConverter.GetJsonSerializerOptions());
        if (regressionReport == null || regressionReport.CrashTestResult == null) {
            var report = JsonSerializer.Deserialize<Report>(content, EntityConverter.GetJsonSerializerOptions());
            if (expectReports && report == null) {
                _log.Error($"unable to parse report ({filePath}) as a report or regression");
                return null;
            }
            return report;
        }
        return regressionReport;
    }

    private IReport? ParseReportOrRegression(IEnumerable<byte> content, string? filePath, bool expectReports = false) {
        try {
            var str = System.Text.Encoding.UTF8.GetString(content.ToArray());
            return ParseReportOrRegression(str, filePath, expectReports);
        } catch (Exception e) {
            if (expectReports) {
                _log.Error($"unable to parse report ({filePath}): unicode decode of report failed - {e.Message} {e.StackTrace}");
            }
            return null;
        }
    }
}

public interface IReport { };
