using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Azure;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.Compute;
using Azure.ResourceManager.Compute.Models;
using Microsoft.Extensions.Caching.Memory;
using Compute = Azure.ResourceManager.Compute;

namespace Microsoft.OneFuzz.Service;

public static class DefaultImages {
    public static readonly ImageReference Windows = ImageReference.MustParse("MicrosoftWindowsDesktop:Windows-11:win11-22h2-pro:latest");
    public static readonly ImageReference Linux = ImageReference.MustParse("Canonical:0001-com-ubuntu-server-jammy:22_04-lts:latest");
}

[JsonConverter(typeof(Converter<ImageReference>))]
public abstract record ImageReference {
    public static ImageReference MustParse(string image) {
        var result = TryParse(image);
        if (!result.IsOk) {
            var msg = result.ErrorV.Errors != null ? string.Join(", ", result.ErrorV.Errors) : string.Empty;
            throw new ArgumentException(msg, nameof(image));
        }

        return result.OkV;
    }

    public static OneFuzzResult<ImageReference> TryParse(string image) {
        ResourceIdentifier identifier;
        ImageReference result;
        try {
            // see if it is a valid ARM resource identifier:
            identifier = new ResourceIdentifier(image);
            if (identifier.ResourceType == SharedGalleryImageResource.ResourceType) {
                result = new LatestSharedGalleryImage(identifier);
            } else if (identifier.ResourceType == SharedGalleryImageVersionResource.ResourceType) {
                result = new SharedGalleryImage(identifier);
            } else if (identifier.ResourceType == GalleryImageVersionResource.ResourceType) {
                result = new GalleryImage(identifier);
            } else if (identifier.ResourceType == GalleryImageResource.ResourceType) {
                result = new LatestGalleryImage(identifier);
            } else if (identifier.ResourceType == ImageResource.ResourceType) {
                result = new Image(identifier);
            } else {
                return Error.Create(
                    ErrorCode.INVALID_IMAGE,
                    $"Unknown image resource type: {identifier.ResourceType}");
            }
        } catch (FormatException) {
            // not an ARM identifier, try to parse a marketplace image:
            var imageParts = image.Split(":");
            // The python code would throw if more than 4 parts are found in the split
            if (imageParts.Length != 4) {
                return Error.Create(
                    ErrorCode.INVALID_IMAGE, $"Expected 4 ':' separated parts in '{image}'");
            }

            result = new Marketplace(
                    Publisher: imageParts[0],
                    Offer: imageParts[1],
                    Sku: imageParts[2],
                    Version: imageParts[3]);
        }

        return OneFuzzResult.Ok(result);
    }

    // region is not part of the key as it should not make a difference to the OS type
    // it is only used for marketplace images
    private sealed record CacheKey(string image);
    public Task<OneFuzzResult<Os>> GetOs(IMemoryCache cache, ArmClient armClient, Region region) {
        return cache.GetOrCreateAsync(new CacheKey(ToString()), entry => {
            // this should essentially never change
            // the user would have to delete the image and recreate it with the same name but
            // a different OS, which would be very unusual
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(1);
            return GetOsUncached(armClient, region);
        });
    }

    protected abstract Task<OneFuzzResult<Os>> GetOsUncached(ArmClient armClient, Region region);

    public abstract Compute.Models.ImageReference ToArm();

    public abstract long MaximumVmCount { get; }

    // Documented here: https://docs.microsoft.com/en-us/azure/virtual-machine-scale-sets/virtual-machine-scale-sets-placement-groups#checklist-for-using-large-scale-sets
    protected const long CustomImageMaximumVmCount = 600;
    protected const long MarketplaceImageMaximumVmCount = 1000;

    public abstract override string ToString();

    public abstract record ArmImageReference(ResourceIdentifier Identifier) : ImageReference {
        public sealed override long MaximumVmCount => CustomImageMaximumVmCount;

        public sealed override Compute.Models.ImageReference ToArm()
            => new() { Id = Identifier };

        public sealed override string ToString() => Identifier.ToString();
    }

    [JsonConverter(typeof(Converter<LatestGalleryImage>))]
    public sealed record LatestGalleryImage(ResourceIdentifier Identifier) : ArmImageReference(Identifier) {
        protected override async Task<OneFuzzResult<Os>> GetOsUncached(ArmClient armClient, Region region) {
            try {
                var resource = await armClient.GetGalleryImageResource(Identifier).GetAsync();
                if (resource.Value.Data.OSType is OperatingSystemTypes os) {
                    return OneFuzzResult.Ok(Enum.Parse<Os>(os.ToString(), ignoreCase: true));
                } else {
                    return Error.Create(ErrorCode.INVALID_IMAGE, "Specified image had no OSType");
                }
            } catch (Exception ex) when (ex is RequestFailedException) {
                return Error.Create(ErrorCode.INVALID_IMAGE, ex.ToString());
            }
        }
    }

    [JsonConverter(typeof(Converter<GalleryImage>))]
    public sealed record GalleryImage(ResourceIdentifier Identifier) : ArmImageReference(Identifier) {
        protected override async Task<OneFuzzResult<Os>> GetOsUncached(ArmClient armClient, Region region) {
            try {
                // need to access parent of versioned resource to get the OS data
                var resource = await armClient.GetGalleryImageResource(Identifier.Parent!).GetAsync();
                if (resource.Value.Data.OSType is OperatingSystemTypes os) {
                    return OneFuzzResult.Ok(Enum.Parse<Os>(os.ToString(), ignoreCase: true));
                } else {
                    return Error.Create(ErrorCode.INVALID_IMAGE, "Specified image had no OSType");
                }
            } catch (Exception ex) when (ex is RequestFailedException) {
                return Error.Create(ErrorCode.INVALID_IMAGE, ex.ToString());
            }
        }
    }

    [JsonConverter(typeof(Converter<LatestSharedGalleryImage>))]
    public sealed record LatestSharedGalleryImage(ResourceIdentifier Identifier) : ArmImageReference(Identifier) {
        protected override async Task<OneFuzzResult<Os>> GetOsUncached(ArmClient armClient, Region region) {
            try {
                var resource = await armClient.GetSharedGalleryImageResource(Identifier).GetAsync();
                if (resource.Value.Data.OSType is OperatingSystemTypes os) {
                    return OneFuzzResult.Ok(Enum.Parse<Os>(os.ToString(), ignoreCase: true));
                } else {
                    return Error.Create(ErrorCode.INVALID_IMAGE, "Specified image had no OSType");
                }
            } catch (Exception ex) when (ex is RequestFailedException) {
                return Error.Create(ErrorCode.INVALID_IMAGE, ex.ToString());
            }
        }
    }

    [JsonConverter(typeof(Converter<SharedGalleryImage>))]
    public sealed record SharedGalleryImage(ResourceIdentifier Identifier) : ArmImageReference(Identifier) {
        protected override async Task<OneFuzzResult<Os>> GetOsUncached(ArmClient armClient, Region region) {
            try {
                // need to access parent of versioned resource to get OS info
                var resource = await armClient.GetSharedGalleryImageResource(Identifier.Parent!).GetAsync();
                if (resource.Value.Data.OSType is OperatingSystemTypes os) {
                    return OneFuzzResult.Ok(Enum.Parse<Os>(os.ToString(), ignoreCase: true));
                } else {
                    return Error.Create(ErrorCode.INVALID_IMAGE, "Specified image had no OSType");
                }
            } catch (Exception ex) when (ex is RequestFailedException) {
                return Error.Create(ErrorCode.INVALID_IMAGE, ex.ToString());
            }
        }
    }

    [JsonConverter(typeof(Converter<Image>))]
    public sealed record Image(ResourceIdentifier Identifier) : ArmImageReference(Identifier) {
        protected override async Task<OneFuzzResult<Os>> GetOsUncached(ArmClient armClient, Region region) {
            try {
                var resource = await armClient.GetImageResource(Identifier).GetAsync();
                var os = resource.Value.Data.StorageProfile.OSDisk.OSType.ToString();
                return OneFuzzResult.Ok(Enum.Parse<Os>(os.ToString(), ignoreCase: true));
            } catch (Exception ex) when (ex is RequestFailedException) {
                return Error.Create(ErrorCode.INVALID_IMAGE, ex.ToString());
            }
        }
    }

    [JsonConverter(typeof(Converter<Marketplace>))]
    public sealed record Marketplace(
        string Publisher,
        string Offer,
        string Sku,
        string Version) : ImageReference {
        public override long MaximumVmCount => MarketplaceImageMaximumVmCount;

        protected override async Task<OneFuzzResult<Os>> GetOsUncached(ArmClient armClient, Region region) {
            try {
                var subscription = await armClient.GetDefaultSubscriptionAsync();
                string version;
                if (string.Equals(Version, "latest", StringComparison.Ordinal)) {
                    version =
                        (await subscription.GetVirtualMachineImagesAsync(
                            region.String,
                            Publisher,
                            Offer,
                            Sku,
                            top: 1
                        ).FirstAsync()).Name;
                } else {
                    version = Version;
                }

                var vm = await subscription.GetVirtualMachineImageAsync(
                    region.String,
                    Publisher,
                    Offer,
                    Sku,
                    version);

                var os = vm.Value.OSDiskImageOperatingSystem.ToString();
                return OneFuzzResult.Ok(Enum.Parse<Os>(os, ignoreCase: true));
            } catch (RequestFailedException ex) {
                return OneFuzzResult<Os>.Error(
                    ErrorCode.INVALID_IMAGE,
                    ex.ToString()
                );
            }
        }

        public override Compute.Models.ImageReference ToArm() {
            return new() {
                Publisher = Publisher,
                Offer = Offer,
                Sku = Sku,
                Version = Version
            };
        }

        public override string ToString() => string.Join(":", Publisher, Offer, Sku, Version);
    }

    // ImageReference serializes to and from JSON as a string.
    public sealed class Converter<T> : JsonConverter<T> where T : ImageReference {
        public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
            Debug.Assert(typeToConvert.IsAssignableTo(typeof(ImageReference)));

            var value = reader.GetString();
            if (value is null) {
                return null;
            }

            var result = TryParse(value);
            if (!result.IsOk) {
                throw new JsonException(result.ErrorV.Errors?.First());
            }

            return (T)(object)result.OkV;
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.ToString());
    }
}
