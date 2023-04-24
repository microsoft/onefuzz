using System.Text.Json;
using System.Threading.Tasks;
using Azure.Security.KeyVault.Secrets;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;

public interface ISecretsOperations {
    public (Uri, string) ParseSecretUrl(Uri secretsUrl);
    public Task<SecretData<T>> SaveToKeyvault<T>(SecretData<T> secretData);

    public Task<string?> GetSecretStringValue<T>(SecretData<T> data);

    public Task<KeyVaultSecret> StoreInKeyvault(Uri keyvaultUrl, string secretName, string secretValue);
    public Task<KeyVaultSecret> GetSecret(Uri secretUrl);
    public Task<T?> GetSecretObj<T>(Uri secretUrl);
    public Task<DeleteSecretOperation> DeleteSecret(Uri secretUrl);
    public Task<DeleteSecretOperation?> DeleteRemoteSecretData<T>(SecretData<T> data);
    public Uri GetKeyvaultAddress();

}

public class SecretsOperations : ISecretsOperations {
    private readonly ICreds _creds;
    private readonly IServiceConfig _config;
    public SecretsOperations(ICreds creds, IServiceConfig config) {
        _creds = creds;
        _config = config;
    }

    public (Uri, string) ParseSecretUrl(Uri secretsUrl) {
        // format: https://{vault-name}.vault.azure.net/secrets/{secret-name}/{version}
        var vaultUrl = $"{secretsUrl.Scheme}://{secretsUrl.Host}";
        var secretName = secretsUrl.Segments[^2].Trim('/');
        return (new Uri(vaultUrl), secretName);
    }

    public virtual async Task<SecretData<T>> SaveToKeyvault<T>(SecretData<T> secretData) {

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
            return new SecretData<T>(new SecretAddress<T>(kv.Id));
        }

        throw new Exception("Invalid secret value");
    }

    public async Task<string?> GetSecretStringValue<T>(SecretData<T> data) {
        return (data.Secret) switch {
            SecretAddress<T> secretAddress => (await GetSecret(secretAddress.Url)).Value,
            SecretValue<T> sValue => sValue.Value?.ToString(),
            _ => data.Secret.ToString(),
        };
    }

    public Uri GetKeyvaultAddress() {
        // https://docs.microsoft.com/en-us/azure/key-vault/general/about-keys-secrets-certificates#vault-name-and-object-name
        var keyvaultName = _config!.OneFuzzKeyvault;
        return new Uri($"https://{keyvaultName}.vault.azure.net");
    }


    public virtual async Task<KeyVaultSecret> StoreInKeyvault(Uri keyvaultUrl, string secretName, string secretValue) {
        var keyvaultClient = new SecretClient(keyvaultUrl, _creds.GetIdentity());
        var r = await keyvaultClient.SetSecretAsync(secretName, secretValue);
        return r.Value;
    }

    public async Task<KeyVaultSecret> GetSecret(Uri secretUrl) {
        var (vaultUrl, secretName) = ParseSecretUrl(secretUrl);
        var keyvaultClient = new SecretClient(vaultUrl, _creds.GetIdentity());
        return await keyvaultClient.GetSecretAsync(secretName);
    }

    public async Task<T?> GetSecretObj<T>(Uri secretUrl) {
        var secret = await GetSecret(secretUrl);
        if (secret is null)
            return default(T);
        else
            return JsonSerializer.Deserialize<T>(secret.Value, EntityConverter.GetJsonSerializerOptions());
    }

    public async Task<DeleteSecretOperation> DeleteSecret(Uri secretUrl) {
        var (vaultUrl, secretName) = ParseSecretUrl(secretUrl);
        var keyvaultClient = new SecretClient(vaultUrl, _creds.GetIdentity());
        return await keyvaultClient.StartDeleteSecretAsync(secretName);
    }

    public async Task<DeleteSecretOperation?> DeleteRemoteSecretData<T>(SecretData<T> data) {
        if (data.Secret is SecretAddress<T> secretAddress) {
            return await DeleteSecret(secretAddress.Url);
        } else {
            return null;
        }
    }

}
