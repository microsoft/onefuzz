
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.ResourceManager.Compute;
using Microsoft.OneFuzz.Service;

using Async = System.Threading.Tasks;

namespace IntegrationTests.Fakes;


sealed class TestVmssOperations : IVmssOperations {
    public Task<IReadOnlyList<string>> ListAvailableSkus(Region region)
        => Async.Task.FromResult(TestSkus);

    public static IReadOnlyList<string> TestSkus = new[] { TestSku };
    public const string TestSku = "Test_Sku";

    /* below not implemented */

    public Task<OneFuzzResultVoid> CreateVmss(Region location, Guid name, string vmSku, long vmCount, string image, string networkId, bool? spotInstance, bool ephemeralOsDisks, IList<VirtualMachineScaleSetExtensionData>? extensions, string password, string sshPublicKey, IDictionary<string, string> tags) {
        throw new NotImplementedException();
    }

    public Task<bool> DeleteVmss(Guid name, bool? forceDeletion = null) {
        throw new NotImplementedException();
    }

    public Task<OneFuzzResult<string>> GetInstanceId(Guid name, Guid vmId) {
        throw new NotImplementedException();
    }

    public Task<VirtualMachineScaleSetData?> GetVmss(Guid name) {
        throw new NotImplementedException();
    }

    public Task<long?> GetVmssSize(Guid name) {
        throw new NotImplementedException();
    }


    public Task<IDictionary<Guid, string>?> ListInstanceIds(Guid name) {
        throw new NotImplementedException();
    }

    public IAsyncEnumerable<VirtualMachineScaleSetVmResource> ListVmss(Guid name) {
        throw new NotImplementedException();
    }

    public Task<OneFuzzResultVoid> ResizeVmss(Guid name, long capacity) {
        throw new NotImplementedException();
    }

    public Task<OneFuzzResultVoid> UpdateExtensions(Guid name, IList<VirtualMachineScaleSetExtensionData> extensions) {
        throw new NotImplementedException();
    }

    public Task<OneFuzzResultVoid> UpdateScaleInProtection(Scaleset scaleset, string instanceId, bool protectFromScaleIn) {
        throw new NotImplementedException();
    }

    public Task<OneFuzzResultVoid> ReimageNodes(Guid scalesetId, IEnumerable<Node> nodes) {
        throw new NotImplementedException();
    }

    public Async.Task DeleteNodes(Guid scalesetId, IEnumerable<Node> nodes) {
        throw new NotImplementedException();
    }

    public Task<VirtualMachineScaleSetVmData?> GetVmssVm(Guid scaleset, string instanceId) {
        throw new NotImplementedException();
    }
}
