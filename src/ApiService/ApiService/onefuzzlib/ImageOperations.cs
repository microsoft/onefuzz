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
        string? name = null;
        try {
            var parsed = _context.Creds.ParseResourceId(image);
            parsed = await _context.Creds.GetData(parsed);
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
        } catch (FormatException) {
            var imageParts = image.Split(":");

            // The python code would throw if more than 4 parts are found in the split
            System.Diagnostics.Trace.Assert(imageParts.Length == 4, $"Expected 4 ':' separated parts in {image}");

            var publisher = imageParts[0];
            var offer = imageParts[1];
            var sku = imageParts[2];
            var version = imageParts[3];

            try {
                var subscription = await _context.Creds.ArmClient.GetDefaultSubscriptionAsync();
                if (string.Equals(version, "latest", StringComparison.Ordinal)) {
                    version = (await subscription.GetVirtualMachineImagesAsync(
                        region,
                        publisher,
                        offer,
                        sku,
                        top: 1
                    ).FirstAsync()).Name;
                }

                name = (await subscription.GetVirtualMachineImageAsync(
                    region,
                    publisher,
                    offer,
                    sku
                    , version
                )).Value.OSDiskImageOperatingSystem.ToString().ToLower();
            } catch (RequestFailedException ex) {
                return OneFuzzResult<Os>.Error(
                    ErrorCode.INVALID_IMAGE,
                    ex.ToString()
                );
            }
        }

        if (name != null) {
            name = string.Concat(name[0].ToString().ToUpper(), name.AsSpan(1));
            if (Enum.TryParse(name, out Os os)) {
                return OneFuzzResult<Os>.Ok(os);
            }
        }

        return OneFuzzResult<Os>.Error(
            ErrorCode.INVALID_IMAGE,
            $"Unexpected image os type: {name}"
        );
    }
}
