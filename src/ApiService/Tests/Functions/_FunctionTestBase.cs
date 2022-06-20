using System;
using System.IO;
using ApiService.OneFuzzLib.Orm;
using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.OneFuzz.Service;
using Tests.Fakes;
using Xunit.Abstractions;

namespace Tests.Functions;

// FunctionTestBase contains shared implementations for running
// functions against live Azure Storage or the Azurite emulator.
// 
// To use this base class, derive an abstract class from this
// with all the tests defined in it. Then, from that class
// derive two non-abstract classes for XUnit to find:
// - one for Azurite
// - one for Azure Storage (marked with [Trait("Category", "Integration")])
//
// See AgentEventsTests for an example.
public abstract class FunctionTestBase : IDisposable {
    private readonly IStorage _storage;

    // each test will use a different table prefix so they don't interfere
    // with each other - generate a prefix like t12345678 (table names must start with letter)
    private readonly string _tablePrefix = "t" + Guid.NewGuid().ToString()[..8];

    protected ILogTracer Logger { get; }

    protected TestContext Context { get; }

    public FunctionTestBase(ITestOutputHelper output, IStorage storage, string accountId) {
        Logger = new TestLogTracer(output);
        _storage = storage;

        Context = new TestContext(Logger, _storage, _tablePrefix, accountId);
    }

    protected static string BodyAsString(HttpResponseData data) {
        data.Body.Seek(0, SeekOrigin.Begin);
        using var sr = new StreamReader(data.Body);
        return sr.ReadToEnd();
    }

    public void Dispose() {
        // TODO, a bit ugly, tidy this up:
        // delete any tables we created during the run
        if (_storage is Integration.AzureStorage storage) {
            var accountName = storage.AccountName;
            var accountKey = storage.AccountKey;
            if (accountName is not null && accountKey is not null) {
                // we are running against live storage
                var tableClient = new TableServiceClient(
                    _storage.GetTableEndpoint(accountName),
                    new TableSharedKeyCredential(accountName, accountKey));

                var tablesToDelete = tableClient.Query(filter: Query.StartsWith("TableName", _tablePrefix));
                foreach (var table in tablesToDelete) {
                    try {
                        tableClient.DeleteTable(table.Name);
                        Logger.Info($"cleaned up table {table.Name}");
                    } catch (Exception ex) {
                        // swallow any exceptions: this is a best-effort attempt to cleanup
                        Logger.Exception(ex, "error deleting table at end of test");
                    }
                }
            }
        }
    }
}
