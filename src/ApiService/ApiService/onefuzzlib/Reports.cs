using System.Text.Json;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;

public interface IReports {
    public Async.Task<RegressionReportOrReport?> GetReportOrRegression(Container container, string fileName, bool expectReports = false, params string[] args);
}

public class Reports : IReports {
    private ILogTracer _log;
    private IContainers _containers;
    public Reports(ILogTracer log, IContainers containers) {
        _log = log;
        _containers = containers;
    }

    public async Async.Task<RegressionReportOrReport?> GetReportOrRegression(Container container, string fileName, bool expectReports = false, params string[] args) {
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

    private RegressionReportOrReport? ParseReportOrRegression(string content, string? filePath, bool expectReports = false) {
        try {
            return new RegressionReportOrReport {
                RegressionReport = JsonSerializer.Deserialize<RegressionReport>(content, EntityConverter.GetJsonSerializerOptions())
            };
        } catch (JsonException e) {
            try {
                return new RegressionReportOrReport {
                    Report = JsonSerializer.Deserialize<Report>(content, EntityConverter.GetJsonSerializerOptions())
                };
            } catch (JsonException e2) {
                if (expectReports) {
                    _log.Error($"unable to parse report ({filePath}) as a report or regression. regression error: {e.Message} report error: {e2.Message}");
                }
                return null;
            }
        }
    }

    private RegressionReportOrReport? ParseReportOrRegression(IEnumerable<byte> content, string? filePath, bool expectReports = false) {
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

public class RegressionReportOrReport {
    public RegressionReport? RegressionReport { get; set; }
    public Report? Report { get; set; }
}
