using System;

namespace Microsoft.OneFuzz.Service;

public class Reports {
    private LogTracer _loggerTracer;
    private IContainers _containers;
    public Reports(ILogTracerFactory loggerFactory, IContainers containers)
    {
        _loggerTracer = loggerFactory.MakeLogTracer(Guid.NewGuid());
        _containers = containers;
    }

    public async void GetReportOrRegression(Container container, string fileName, bool ExactReports = false, params string[] args)
    {
        var filePath = String.Join("/", new [] {container.ContainerName, fileName});
        if (!fileName.EndsWith(".json"))
        {
            if (ExactReports)
            {
                _loggerTracer.Error($"get_report invalid extension: {filePath}");
            }
            return;
        }

        var blob = await _containers.GetBlob(container, fileName, StorageType.Corpus);
    }
}
