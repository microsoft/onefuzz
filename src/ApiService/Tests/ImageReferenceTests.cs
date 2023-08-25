using System;
using System.Text.Json;
using Azure.ResourceManager.Compute;
using Microsoft.OneFuzz.Service;
using Xunit;

namespace Tests;

public class ImageReferenceTests {

    [Fact]
    public void CanParseImageGalleryReference() {
        var subId = Guid.NewGuid();
        var fakeId = GalleryImageResource.CreateResourceIdentifier(
            subId.ToString(), "resource-group", "gallery", "imageName");

        var result = ImageReference.MustParse(fakeId.ToString());
        var galleryImage = Assert.IsType<ImageReference.LatestGalleryImage>(result);
        Assert.Equal("imageName", galleryImage.Identifier.Name);
        Assert.Equal("gallery", galleryImage.Identifier.Parent?.Name);
        Assert.Equal("resource-group", galleryImage.Identifier.ResourceGroupName);
    }

    [Fact]
    public void CanParseImageReference() {
        var subId = Guid.NewGuid();
        var fakeId = ImageResource.CreateResourceIdentifier(
            subId.ToString(), "resource-group", "imageName");

        var result = ImageReference.MustParse(fakeId.ToString());
        var image = Assert.IsType<ImageReference.Image>(result);
        Assert.Equal("imageName", image.Identifier.Name);
        Assert.Equal("resource-group", image.Identifier.ResourceGroupName);
    }

    [Fact]
    public void CanParseMarketplaceReference() {
        var input = "Canonical:UbuntuServer:20.04-LTS:latest";
        var result = ImageReference.MustParse(input);
        var marketplace = Assert.IsType<ImageReference.Marketplace>(result);

        Assert.Equal("Canonical", marketplace.Publisher);
        Assert.Equal("UbuntuServer", marketplace.Offer);
        Assert.Equal("20.04-LTS", marketplace.Sku);
        Assert.Equal("latest", marketplace.Version);
    }

    [Fact]
    public void CanParseSpecificVersionGalleryImage() {
        var subId = Guid.NewGuid();
        var fakeId = GalleryImageVersionResource.CreateResourceIdentifier(
            subId.ToString(), "resource-group", "gallery", "imageName", "latest");

        var result = ImageReference.MustParse(fakeId.ToString());
        var galleryImage = Assert.IsType<ImageReference.GalleryImage>(result);
        Assert.Equal("latest", galleryImage.Identifier.Name);
        Assert.Equal("imageName", galleryImage.Identifier.Parent?.Name);
        Assert.Equal("gallery", galleryImage.Identifier.Parent?.Parent?.Name);
        Assert.Equal("resource-group", galleryImage.Identifier.ResourceGroupName);
    }

    [Fact]
    public void CanParseSharedGalleryImage() {
        var subId = Guid.NewGuid();
        var fakeId = SharedGalleryImageResource.CreateResourceIdentifier(
            subId.ToString(), "location", "gallery", "imageName");

        var result = ImageReference.MustParse(fakeId.ToString());
        var galleryImage = Assert.IsType<ImageReference.LatestSharedGalleryImage>(result);
        Assert.Equal("imageName", galleryImage.Identifier.Name);
        Assert.Equal("gallery", galleryImage.Identifier.Parent?.Name);
        Assert.Null(galleryImage.Identifier.ResourceGroupName);
    }

    [Fact]
    public void CanParseSpecificVersionSharedGalleryImage() {
        var subId = Guid.NewGuid();
        var fakeId = SharedGalleryImageVersionResource.CreateResourceIdentifier(
            subId.ToString(), "location", "gallery", "imageName", "latest");

        var result = ImageReference.MustParse(fakeId.ToString());
        var galleryImage = Assert.IsType<ImageReference.SharedGalleryImage>(result);
        Assert.Equal("latest", galleryImage.Identifier.Name);
        Assert.Equal("imageName", galleryImage.Identifier.Parent?.Name);
        Assert.Equal("gallery", galleryImage.Identifier.Parent?.Parent?.Name);
        Assert.Null(galleryImage.Identifier.ResourceGroupName);
    }

    [Fact]
    public void UnknownResourceTypeGeneratesError() {
        var subId = Guid.NewGuid();
        var fakeId = VirtualMachineResource.CreateResourceIdentifier(
            subId.ToString(), "resource-group", "vmName");

        var ex = Assert.Throws<ArgumentException>(() => ImageReference.MustParse(fakeId.ToString()));
        Assert.Equal("Unknown image resource type: Microsoft.Compute/virtualMachines (Parameter 'image')", ex.Message);
    }

    static readonly string _expected = @$"{{
  ""latestGalleryId"": ""/subscriptions/{Guid.Empty}/resourceGroups/resource-group/providers/Microsoft.Compute/galleries/gallery/images/imageName"",
  ""imageId"": ""/subscriptions/{Guid.Empty}/resourceGroups/resource-group/providers/Microsoft.Compute/images/imageName"",
  ""marketplaceId"": ""Canonical:UbuntuServer:20.04-LTS:latest"",
  ""galleryId"": ""/subscriptions/{Guid.Empty}/resourceGroups/resource-group/providers/Microsoft.Compute/galleries/gallery/images/imageName/versions/latest"",
  ""latestSharedGalleryId"": ""/subscriptions/{Guid.Empty}/providers/Microsoft.Compute/locations/location/sharedGalleries/gallery/images/imageName"",
  ""sharedGalleryId"": ""/subscriptions/{Guid.Empty}/providers/Microsoft.Compute/locations/location/sharedGalleries/gallery/images/imageName/versions/latest""
}}";

    private sealed record Holder(
        ImageReference latestGalleryId,
        ImageReference imageId,
        ImageReference marketplaceId,
        ImageReference galleryId,
        ImageReference.LatestSharedGalleryImage latestSharedGalleryId,
        ImageReference.SharedGalleryImage sharedGalleryId);

    [Fact]
    public void SerializesToStringAndDeserializesFromString() {
        var subId = Guid.Empty;

        var galleryId = new ImageReference.GalleryImage(
            GalleryImageVersionResource.CreateResourceIdentifier(
                subId.ToString(), "resource-group", "gallery", "imageName", "latest"));

        var latestGalleryId = new ImageReference.LatestGalleryImage(
            GalleryImageResource.CreateResourceIdentifier(
                subId.ToString(), "resource-group", "gallery", "imageName"));

        var imageId = new ImageReference.Image(
            ImageResource.CreateResourceIdentifier(
                subId.ToString(), "resource-group", "imageName"));

        var marketplaceId = new ImageReference.Marketplace(
            "Canonical", "UbuntuServer", "20.04-LTS", "latest");

        var latestSharedGalleryId = new ImageReference.LatestSharedGalleryImage(
            SharedGalleryImageResource.CreateResourceIdentifier(
                subId.ToString(), "location", "gallery", "imageName"));

        var sharedGalleryId = new ImageReference.SharedGalleryImage(
            SharedGalleryImageVersionResource.CreateResourceIdentifier(
                subId.ToString(), "location", "gallery", "imageName", "latest"));

        var result = JsonSerializer.Serialize(
            new Holder(
                latestGalleryId,
                imageId,
                marketplaceId,
                galleryId,
                latestSharedGalleryId,
                sharedGalleryId),
            new JsonSerializerOptions { WriteIndented = true });

        Assert.Equal(_expected, result);

        var deserialized = JsonSerializer.Deserialize<Holder>(result);
        Assert.NotNull(deserialized);
        Assert.Equal(latestGalleryId, deserialized!.latestGalleryId);
        Assert.Equal(galleryId, deserialized.galleryId);
        Assert.Equal(imageId, deserialized.imageId);
        Assert.Equal(marketplaceId, deserialized.marketplaceId);
        Assert.Equal(latestSharedGalleryId, deserialized.latestSharedGalleryId);
        Assert.Equal(sharedGalleryId, deserialized.sharedGalleryId);
    }
}
