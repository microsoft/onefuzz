
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
    public static readonly ImageReference TestImage = ImageReference.MustParse("Canonical:UbuntuServer:20.04-LTS:latest");

    /* below not implemented */

    public Task<OneFuzzResultVoid> CreateVmss(Region location, ScalesetId name, string vmSku, long vmCount, ImageReference image, string networkId, bool? spotInstance, bool ephemeralOsDisks, IList<VirtualMachineScaleSetExtensionData>? extensions, string password, string sshPublicKey, IDictionary<string, string> tags) {
        throw new NotImplementedException();
    }

    public Task<bool> DeleteVmss(ScalesetId name, bool? forceDeletion = null) {
        throw new NotImplementedException();
    }

    public Task<OneFuzzResult<string>> GetInstanceId(ScalesetId name, Guid vmId) {
        throw new NotImplementedException();
    }

    public Task<VirtualMachineScaleSetData?> GetVmss(ScalesetId name) {
        throw new NotImplementedException();
    }

    public Task<long?> GetVmssSize(ScalesetId name) {
        throw new NotImplementedException();
    }


    public Task<IDictionary<Guid, string>> ListInstanceIds(ScalesetId name) {
        throw new NotImplementedException();
    }

    public IAsyncEnumerable<VirtualMachineScaleSetVmResource> ListVmss(ScalesetId name) {
        throw new NotImplementedException();
    }

    public Task<OneFuzzResultVoid> ResizeVmss(ScalesetId name, long capacity) {
        throw new NotImplementedException();
    }

    public Task<OneFuzzResultVoid> UpdateExtensions(ScalesetId name, IList<VirtualMachineScaleSetExtensionData> extensions) {
        throw new NotImplementedException();
    }

    public Task<OneFuzzResultVoid> UpdateScaleInProtection(Scaleset scaleset, string instanceId, bool protectFromScaleIn) {
        throw new NotImplementedException();
    }

    public Task<OneFuzzResultVoid> ReimageNodes(ScalesetId scalesetId, IEnumerable<Node> nodes) {
        throw new NotImplementedException();
    }

    public Async.Task<OneFuzzResultVoid> DeleteNodes(ScalesetId scalesetId, IEnumerable<Node> nodes) {
        throw new NotImplementedException();
    }
}
