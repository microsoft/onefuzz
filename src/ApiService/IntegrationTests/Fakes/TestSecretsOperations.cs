using System;
using System.Collections.Generic;
using Async = System.Threading.Tasks;
using Azure.Security.KeyVault.Secrets;
using Microsoft.OneFuzz.Service;
using System.Text.Json;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace IntegrationTests.Fakes;
sealed class TestSecretsOperations : SecretsOperations {
    private readonly Dictionary<Uri, Dictionary<string, string>> _fakeKeyvault = new();
    public TestSecretsOperations(ICreds creds, IServiceConfig config)
        : base(creds, config) { }

    // Since these are integration tests, we're just going to store this in memory
    public override Async.Task<KeyVaultSecret> StoreInKeyvault(Uri keyvaultUrl, string secretName, string secretValue) {
        if (!_fakeKeyvault.ContainsKey(keyvaultUrl)) {
            _fakeKeyvault[keyvaultUrl] = new();
        }

        _fakeKeyvault[keyvaultUrl][secretName] = secretValue;
        return Async.Task.FromResult(new KeyVaultSecret(secretName, secretValue));
    }

    public override async Async.Task<SecretData<T>> SaveToKeyvault<T>(SecretData<T> secretData) {
        if (secretData.Secret is SecretAddress<T> secretAddress) {
            return secretData;
        } else if (secretData.Secret is SecretValue<T> sValue) {
            var secretName = Guid.NewGuid();
            string secretValue;
            if (sValue.Value is string secretString) {
                secretValue = secretString.Trim();
            } else {
                secretValue = JsonSerializer.Serialize(sValue.Value, EntityConverter.GetJsonSerializerOptions());
            }

            var kv = await StoreInKeyvault(GetKeyvaultAddress(), secretName.ToString(), secretValue);
            return new SecretData<T>(new SecretAddress<T>(new Uri($"http://example.com/{kv.Name}")));
        }

        throw new Exception("Invalid secret value");
    }

}
