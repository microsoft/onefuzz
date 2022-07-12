using System.Threading.Tasks;

namespace Microsoft.OneFuzz.Service;

public interface IImageOperations {
    public Async.Task<OneFuzzResult<Os>> GetOs(string region, string image);
}

public class ImageOperations : IImageOperations {
    public Task<OneFuzzResult<Os>> GetOs(string region, string image) {
        throw new NotImplementedException();
    }
}
