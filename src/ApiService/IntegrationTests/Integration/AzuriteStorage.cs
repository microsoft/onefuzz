using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.OneFuzz.Service;

using Async = System.Threading.Tasks;

namespace IntegrationTests.Integration;

// An implementation of IStorage for communicating with the Azurite Storage emulator.
sealed class AzuriteStorage : IStorage {
    static AzuriteStorage() {
        try {
            using var client = new HttpClient();
            client.GetAsync(new Uri("http://127.0.0.1:10000")).Wait();
        } catch {
            Console.Error.WriteLine("'azurite' must be running to run integration tests.");
            Environment.Exit(1);
        }
    }

    public Uri GetBlobEndpoint(string _accountId)
        => new($"http://127.0.0.1:10000/devstoreaccount1");

    public Uri GetQueueEndpoint(string _accountId)
        => new($"http://127.0.0.1:10001/devstoreaccount1");

    public Uri GetTableEndpoint(string _accountId)
        => new($"http://127.0.0.1:10002/devstoreaccount1");

    // This is the fixed name & account key used by Azurite (derived from devstorage emulator);
    // https://docs.microsoft.com/en-us/azure/storage/common/storage-configure-connection-string#configure-a-connection-string-for-azurite
    const string AccountName = "devstoreaccount1";
    const string AccountKey = "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";

    public Task<(string, string)> GetStorageAccountNameAndKey(string accountId)
        => Async.Task.FromResult((AccountName, AccountKey));

    public Task<string?> GetStorageAccountNameKeyByName(string accountName) {
        return Async.Task.FromResult(AccountName)!;
    }

    public IEnumerable<string> CorpusAccounts() {
        throw new System.NotImplementedException();
    }

    public string GetPrimaryAccount(StorageType storageType) {
        throw new System.NotImplementedException();
    }

    public IEnumerable<string> GetAccounts(StorageType storageType) {
        yield return AccountName;
    }
}
