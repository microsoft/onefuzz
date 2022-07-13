using System.Threading.Tasks;
using Azure;
using Azure.ResourceManager.Compute;

namespace Microsoft.OneFuzz.Service;

public interface IImageOperations {
    public Async.Task<OneFuzzResult<Os>> GetOs(string region, string image);
}

public class ImageOperations : IImageOperations {
    private IOnefuzzContext _context;
    private ILogTracer _logTracer;

    public ImageOperations(ILogTracer logTracer, IOnefuzzContext context) {
        _logTracer = logTracer;
        _context = context;
    }
    public async Task<OneFuzzResult<Os>> GetOs(string region, string image) {
        var parsed = _context.Creds.ParseResourceId(image);
        var _ = !parsed.HasData ? await parsed.GetAsync() : null;
        string? name = null;
        if (parsed.Id.ResourceGroupName != null) {
            if (string.Equals(parsed.Id.ResourceType, "galleries", StringComparison.OrdinalIgnoreCase)) {
                try {
                    // This is not _exactly_ the same as the python code  
                    // because in C# we don't have access to child_name_1
                    var gallery = await _context.Creds.GetResourceGroupResource().GetGalleries().GetAsync(
                        parsed.Data.Name
                    );

                    var galleryImage = gallery.Value.GetGalleryImages()
                        .ToEnumerable()
                        .Where(galleryImage => string.Equals(galleryImage.Id, parsed.Id, StringComparison.OrdinalIgnoreCase))
                        .First();

                    galleryImage = await galleryImage.GetAsync();

                    name = galleryImage.Data?.OSType?.ToString().ToLowerInvariant()!;

                } catch (Exception ex) when (
                      ex is RequestFailedException ||
                      ex is NullReferenceException
                  ) {
                    return OneFuzzResult<Os>.Error(
                        ErrorCode.INVALID_IMAGE,
                        ex.ToString()
                    );
                }
            }
        } else {
            try {
                name = (await _context.Creds.GetResourceGroupResource().GetImages().GetAsync(
                    parsed.Data.Name
                )).Value.Data.StorageProfile.OSDisk.OSType.ToString().ToLowerInvariant();
            } catch (Exception ex) when (
                  ex is RequestFailedException ||
                  ex is NullReferenceException
              ) {
                return OneFuzzResult<Os>.Error(
                    ErrorCode.INVALID_IMAGE,
                    ex.ToString()
                );
            }
        }

        if (name != null && Enum.TryParse(name, out Os os)) {
            return OneFuzzResult<Os>.Ok(os);
        }

        return OneFuzzResult<Os>.Error(
            ErrorCode.INVALID_IMAGE,
            $"Unexpected image os type: {name}"
        );
    }
}
