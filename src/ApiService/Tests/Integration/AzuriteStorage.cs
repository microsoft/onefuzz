using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.OneFuzz.Service;

using Async = System.Threading.Tasks;

namespace Tests.Integration;

sealed class AzuriteStorage : IStorage {
    public Uri GetBlobEndpoint(string accountId)
        => new($"http://127.0.0.1:10000/{accountId}");

    public Uri GetQueueEndpoint(string accountId)
        => new($"http://127.0.0.1:10001/{accountId}");

    public Uri GetTableEndpoint(string accountId)
        => new($"http://127.0.0.1:10002/{accountId}");
    
    // This is the fixed account key used by Azurite (derived from devstorage emulator);
    // https://docs.microsoft.com/en-us/azure/storage/common/storage-configure-connection-string#configure-a-connection-string-for-azurite
    const string AccountKey = "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";

    public Task<(string?, string?)> GetStorageAccountNameAndKey(string accountId)
        => Async.Task.FromResult<(string?, string?)>((accountId, AccountKey));

    public Task<string?> GetStorageAccountNameAndKeyByName(string accountName) {
        throw new System.NotImplementedException();
    }

    public IEnumerable<string> CorpusAccounts() {
        throw new System.NotImplementedException();
    }

    public IEnumerable<string> GetAccounts(StorageType storageType) {
        throw new System.NotImplementedException();
    }

    public string GetPrimaryAccount(StorageType storageType) {
        throw new System.NotImplementedException();
    }
}
