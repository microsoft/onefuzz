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
        var galleryImage = Assert.IsType<ImageReference.GalleryImage>(result);
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
        var input = "Canonical:UbuntuServer:18.04-LTS:latest";
        var result = ImageReference.MustParse(input);
        var marketplace = Assert.IsType<ImageReference.Marketplace>(result);

        Assert.Equal("Canonical", marketplace.Publisher);
        Assert.Equal("UbuntuServer", marketplace.Offer);
        Assert.Equal("18.04-LTS", marketplace.Sku);
        Assert.Equal("latest", marketplace.Version);
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
  ""galleryImageId"": ""/subscriptions/{Guid.Empty}/resourceGroups/resource-group/providers/Microsoft.Compute/galleries/gallery/images/imageName"",
  ""imageId"": ""/subscriptions/{Guid.Empty}/resourceGroups/resource-group/providers/Microsoft.Compute/images/imageName"",
  ""marketplaceId"": ""Canonical:UbuntuServer:18.04-LTS:latest""
}}";

    private record Holder(
        ImageReference galleryImageId,
        ImageReference imageId,
        ImageReference marketplaceId);

    [Fact]
    public void SerializesToStringAndDeserializesFromString() {
        var subId = Guid.Empty;

        var galleryImageId = new ImageReference.GalleryImage(
            GalleryImageResource.CreateResourceIdentifier(
                subId.ToString(), "resource-group", "gallery", "imageName"));

        var imageId = new ImageReference.Image(
            ImageResource.CreateResourceIdentifier(
                subId.ToString(), "resource-group", "imageName"));

        var marketplaceId = new ImageReference.Marketplace(
            "Canonical", "UbuntuServer", "18.04-LTS", "latest");

        var result = JsonSerializer.Serialize(
            new Holder(galleryImageId, imageId, marketplaceId),
            new JsonSerializerOptions { WriteIndented = true });

        Assert.Equal(_expected, result);

        var deserialized = JsonSerializer.Deserialize<Holder>(result);
        Assert.NotNull(deserialized);
        Assert.Equal(galleryImageId, deserialized!.galleryImageId);
        Assert.Equal(imageId, deserialized.imageId);
        Assert.Equal(marketplaceId, deserialized.marketplaceId);
    }
}
