using Microsoft.Azure.Functions.Worker;
using System.Text.Json;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;

public class QueueFileChanges
{
    // The number of time the function will be retried if an error occurs
    // https://docs.microsoft.com/en-us/azure/azure-functions/functions-bindings-storage-queue-trigger?tabs=csharp#poison-messages
    const int MAX_DEQUEUE_COUNT = 5;

    private readonly ILogTracer _log;

    private readonly IStorage _storage;

    public QueueFileChanges(ILogTracer log, IStorage storage)
    {
        _log = log;
        _storage = storage;
    }

    [Function("QueueFileChanges")]
    public Async.Task Run(
        [QueueTrigger("file-changes-refactored", Connection = "AzureWebJobsStorage")] string msg,
        int dequeueCount)
    {
        var fileChangeEvent = JsonSerializer.Deserialize<Dictionary<string, string>>(msg, EntityConverter.GetJsonSerializerOptions());
        var lastTry = dequeueCount == MAX_DEQUEUE_COUNT;

        var _ = fileChangeEvent ?? throw new ArgumentException("Unable to parse queue trigger as JSON");

        // check type first before calling Azure APIs
        const string eventType = "eventType";
        if (!fileChangeEvent.ContainsKey(eventType)
            || fileChangeEvent[eventType] != "Microsoft.Storage.BlobCreated")
        {
            return Async.Task.CompletedTask;
        }

        const string topic = "topic";
        if (!fileChangeEvent.ContainsKey(topic)
            || !_storage.CorpusAccounts().Contains(fileChangeEvent[topic]))
        {
            return Async.Task.CompletedTask;
        }

        file_added(_log, fileChangeEvent, lastTry);
        return Async.Task.CompletedTask;
    }

    private void file_added(ILogTracer log, Dictionary<string, string> fileChangeEvent, bool failTaskOnTransientError)
    {
        var data = JsonSerializer.Deserialize<Dictionary<string, string>>(fileChangeEvent["data"])!;
        var url = data["url"];
        var parts = url.Split("/").Skip(3).ToList();

        var container = parts[0];
        var path = string.Join('/', parts.Skip(1));

        log.Info($"file added container: {container} - path: {path}");
        // TODO: new_files(container, path, fail_task_on_transient_error)
    }
}
