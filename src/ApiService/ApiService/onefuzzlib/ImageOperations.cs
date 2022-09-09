using System.Threading.Tasks;
using Azure;
using Azure.Core;
using Azure.ResourceManager.Compute;

namespace Microsoft.OneFuzz.Service;

public record ImageInfo(string Publisher, string Offer, string Sku, string Version);

public interface IImageOperations {
    public Async.Task<OneFuzzResult<Os>> GetOs(Region region, string image);

    public static ImageInfo GetImageInfo(string image) {
        var imageParts = image.Split(":");
        // The python code would throw if more than 4 parts are found in the split
        System.Diagnostics.Trace.Assert(imageParts.Length == 4, $"Expected 4 ':' separated parts in {image}");

        var publisher = imageParts[0];
        var offer = imageParts[1];
        var sku = imageParts[2];
        var version = imageParts[3];

        return new ImageInfo(Publisher: publisher, Offer: offer, Sku: sku, Version: version);
    }
}

public class ImageOperations : IImageOperations {
    private readonly IOnefuzzContext _context;

    public ImageOperations(IOnefuzzContext context) {
        _context = context;
    }

    public async Task<OneFuzzResult<Os>> GetOs(Region region, string image) {
        string? name;
        var client = _context.Creds.ArmClient;
        try {
            var imageId = new ResourceIdentifier(image);
            if (imageId.ResourceType == GalleryImageResource.ResourceType) {
                try {
                    var resource = await client.GetGalleryImageResource(imageId).GetAsync();
                    name = resource.Value.Data.OSType?.ToString();
                } catch (Exception ex) when (ex is RequestFailedException) {
                    return OneFuzzResult<Os>.Error(
                        ErrorCode.INVALID_IMAGE,
                        ex.ToString());
                }
            } else if (imageId.ResourceType == ImageResource.ResourceType) {
                try {
                    var resource = await client.GetImageResource(imageId).GetAsync();
                    name = resource.Value.Data.StorageProfile.OSDisk.OSType.ToString();
                } catch (Exception ex) when (ex is RequestFailedException) {
                    return OneFuzzResult<Os>.Error(
                        ErrorCode.INVALID_IMAGE,
                        ex.ToString());
                }
            } else {
                return OneFuzzResult<Os>.Error(
                    ErrorCode.INVALID_IMAGE,
                    $"Unknown image resource type: {imageId.ResourceType}");
            }
        } catch (FormatException) {
            var imageInfo = IImageOperations.GetImageInfo(image);
            try {
                var subscription = await client.GetDefaultSubscriptionAsync();
                string version;
                if (string.Equals(imageInfo.Version, "latest", StringComparison.Ordinal)) {
                    version =
                        (await subscription.GetVirtualMachineImagesAsync(
                            region.String,
                            imageInfo.Publisher,
                            imageInfo.Offer,
                            imageInfo.Sku,
                            top: 1
                        ).FirstAsync()).Name;
                } else {
                    version = imageInfo.Version;
                }

                name = (await subscription.GetVirtualMachineImageAsync(
                    region.String,
                    imageInfo.Publisher,
                    imageInfo.Offer,
                    imageInfo.Sku,
                    version
                )).Value.OSDiskImageOperatingSystem.ToString();
            } catch (RequestFailedException ex) {
                return OneFuzzResult<Os>.Error(
                    ErrorCode.INVALID_IMAGE,
                    ex.ToString()
                );
            }
        }

        if (name != null) {
            if (Enum.TryParse(name, ignoreCase: true, out Os os)) {
                return OneFuzzResult<Os>.Ok(os);
            }
        }

        return OneFuzzResult<Os>.Error(
            ErrorCode.INVALID_IMAGE,
            $"Unexpected image os type: {name}"
        );
    }
}
