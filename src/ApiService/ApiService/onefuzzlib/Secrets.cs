using System.Text.Json;
using System.Threading.Tasks;
using Azure.Security.KeyVault.Secrets;
using Microsoft.OneFuzz.Service.OneFuzzLib.Orm;

namespace Microsoft.OneFuzz.Service;

public interface ISecretsOperations
{
    public (Uri, string) ParseSecretUrl(Uri secretsUrl);
    public Task<SecretData<SecretAddress>?> SaveToKeyvault<T>(SecretData<T> secretData);
    public Task<string?> GetSecretStringValue<T>(SecretData<T> data);

    public Task<KeyVaultSecret> StoreInKeyvault(Uri keyvaultUrl, string secretName, string secretValue);
    public Task<KeyVaultSecret> GetSecret(Uri secretUrl);
    public Task<T?> GetSecretObj<T>(Uri secretUrl);
    public Task<DeleteSecretOperation> DeleteSecret(Uri secretUrl);
    public Task<DeleteSecretOperation?> DeleteRemoteSecretData<T>(SecretData<T> data);
    public Uri GetKeyvaultAddress();

}

public class SecretsOperations : ISecretsOperations
{
    private readonly ICreds _creds;
    private readonly IServiceConfig _config;
    public SecretsOperations(ICreds creds, IServiceConfig config)
    {
        _creds = creds;
        _config = config;
    }

    public (Uri, string) ParseSecretUrl(Uri secretsUrl)
    {
        // format: https://{vault-name}.vault.azure.net/secrets/{secret-name}/{version}
        var vaultUrl = $"{secretsUrl.Scheme}://{secretsUrl.Host}";
        var secretName = secretsUrl.Segments[secretsUrl.Segments.Length - 2].Trim('/');
        return (new Uri(vaultUrl), secretName);
    }

    public async Task<SecretData<SecretAddress>?> SaveToKeyvault<T>(SecretData<T> secretData)
    {
        if (secretData == null || secretData.Secret is null)
            return null;

        if (secretData.Secret is SecretAddress)
        {
            return secretData as SecretData<SecretAddress>;
        }
        else
        {
            var secretName = Guid.NewGuid();
            string secretValue;
            if (secretData.Secret is string)
            {
                secretValue = (secretData.Secret as string)!.Trim();
            }
            else
            {
                secretValue = JsonSerializer.Serialize(secretData.Secret, EntityConverter.GetJsonSerializerOptions());
            }

            var kv = await StoreInKeyvault(GetKeyvaultAddress(), secretName.ToString(), secretValue);
            return new SecretData<SecretAddress>(new SecretAddress(kv.Id));
        }
    }

    public async Task<string?> GetSecretStringValue<T>(SecretData<T> data)
    {
        if (data.Secret is null)
        {
            return null;
        }

        if (data.Secret is SecretAddress)
        {
            var secret = await GetSecret((data.Secret as SecretAddress)!.Url);
            return secret.Value;
        }
        else
        {
            return data.Secret.ToString();
        }
    }

    public Uri GetKeyvaultAddress()
    {
        // https://docs.microsoft.com/en-us/azure/key-vault/general/about-keys-secrets-certificates#vault-name-and-object-name
        var keyvaultName = _config!.OneFuzzKeyvault;
        return new Uri($"https://{keyvaultName}.vault.azure.net");
    }


    public async Task<KeyVaultSecret> StoreInKeyvault(Uri keyvaultUrl, string secretName, string secretValue)
    {
        var keyvaultClient = new SecretClient(keyvaultUrl, _creds.GetIdentity());
        var r = await keyvaultClient.SetSecretAsync(secretName, secretValue);
        return r.Value;
    }

    public async Task<KeyVaultSecret> GetSecret(Uri secretUrl)
    {
        var (vaultUrl, secretName) = ParseSecretUrl(secretUrl);
        var keyvaultClient = new SecretClient(vaultUrl, _creds.GetIdentity());
        return await keyvaultClient.GetSecretAsync(secretName);
    }

    public async Task<T?> GetSecretObj<T>(Uri secretUrl)
    {
        var secret = await GetSecret(secretUrl);
        if (secret is null)
            return default(T);
        else
            return JsonSerializer.Deserialize<T>(secret.Value, EntityConverter.GetJsonSerializerOptions());
    }

    public async Task<DeleteSecretOperation> DeleteSecret(Uri secretUrl)
    {
        var (vaultUrl, secretName) = ParseSecretUrl(secretUrl);
        var keyvaultClient = new SecretClient(vaultUrl, _creds.GetIdentity());
        return await keyvaultClient.StartDeleteSecretAsync(secretName);
    }

    public async Task<DeleteSecretOperation?> DeleteRemoteSecretData<T>(SecretData<T> data)
    {
        if (data.Secret is SecretAddress)
        {
            if (data.Secret is not null)
                return await DeleteSecret((data.Secret as SecretAddress)!.Url);
            else
                return null;
        }
        else
        {
            return null;
        }
    }

}
