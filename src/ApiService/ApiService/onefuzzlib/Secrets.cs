using System.Text.Json;
using System.Threading.Tasks;
using Azure.Security.KeyVault.Secrets;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;

public interface ISecretsOperations {
    public async Task<SecretData<T>> StoreSecretData<T>(SecretData<T> secretData) {
        if (secretData.Secret.IsHIddden) {
            return secretData;
        }
        var address = await StoreSecret(secretData.Secret);
        return new SecretData<T>(new SecretAddress<T>(address));
    }

    public Task<T?> GetSecretValue<T>(ISecret<T> data);

    Task<Uri> StoreSecret(ISecret secret);
}

public class SecretsOperations : ISecretsOperations {
    private readonly ICreds _creds;
    private readonly IServiceConfig _config;
    public SecretsOperations(ICreds creds, IServiceConfig config) {
        _creds = creds;
        _config = config;
    }

    public static (Uri, string) ParseSecretUrl(Uri secretsUrl) {
        // format: https://{vault-name}.vault.azure.net/secrets/{secret-name}/{version}
        var vaultUrl = $"{secretsUrl.Scheme}://{secretsUrl.Host}";
        var secretName = secretsUrl.Segments[^2].Trim('/');
        return (new Uri(vaultUrl), secretName);
    }

    public virtual async Task<SecretData<T>> StoreSecretData<T>(SecretData<T> secretData) {
        if (secretData.Secret.IsHIddden) {
            return secretData;
        }
        var kv = await StoreSecret(secretData.Secret);
        return new SecretData<T>(new SecretAddress<T>(kv));
    }

    public async Task<Uri> StoreSecret(ISecret secret) {
        var secretValue = secret.GetValue();
        var secretName = Guid.NewGuid();
        var kv = await StoreInKeyvault(GetKeyvaultAddress(), secretName.ToString(), secretValue ?? "");
        return kv.Id;
    }

    public async Task<string?> GetSecretStringValue<T>(SecretData<T> data) {
        return (data.Secret) switch {
            SecretAddress<T> secretAddress => (await GetSecret(secretAddress.Url)).Value,
            SecretValue<T> sValue => sValue.Value?.ToString(),
            _ => data.Secret.ToString(),
        };
    }

    public async Task<T?> GetSecretValue<T>(ISecret<T> data) {
        switch ((data)) {
            case SecretAddress<T> secretAddress:
                var secretValue = (await GetSecret(secretAddress.Url)).Value;
                if (secretValue is null)
                    return default;
                return JsonSerializer.Deserialize<T>(secretValue, EntityConverter.GetJsonSerializerOptions());

            case SecretValue<T> sValue:
                return sValue.Value;

        }
        return default;
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
