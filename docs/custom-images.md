# Fuzzing using Custom OS Images

In order to use custom OS images in OneFuzz, the image _must_ run the [Azure VM
Agent](https://docs.microsoft.com/en-us/azure/virtual-machines/extensions/overview).

Building custom images can be automated using the
[Linux](https://docs.microsoft.com/en-us/azure/virtual-machines/linux/image-builder)
or
[Windows](https://docs.microsoft.com/en-us/azure/virtual-machines/windows/image-builder)
image builders for Azure.

If you have a custom Windows VHD, you should follow the [Guide to prepare a VHD
for
Azure](https://docs.microsoft.com/en-us/azure/virtual-machines/windows/prepare-for-upload-vhd-image).

From there, rather than using Image SKUs such as
`Canonical:0001-com-ubuntu-server-focal:20_04-lts:latest`, use the full resource ID for the
shared image. Supported ID formats are:

- VM image:<br/>
  `/subscriptions/{subscription}/resourceGroups/{resourceGroup}/providers/Microsoft.Compute/images/{image}`
- gallery image (latest):<br/>
  `/subscriptions/{subscription}/resourceGroups/{resourceGroup}/providers/Microsoft.Compute/galleries/{gallery}/images/{image}`
- gallery image (specific version):<br/>
  `/subscriptions/{subscription}/resourceGroups/{resourceGroup}/providers/Microsoft.Compute/galleries/{gallery}/images/{image}/versions/{version}`
- shared gallery image (latest):<br/>
  `/subscriptions/{subscription}/providers/Microsoft.Compute/locations/{location}/sharedGalleries/{gallery}/images/{image}`,
- shared gallery image (specific version):<br/>
  `/subscriptions/{subscription}/providers/Microsoft.Compute/locations/{location}/sharedGalleries/{gallery}/images/{image}/versions/{version}`

The Service Principal for the OneFuzz instance must have RBAC to the image
sufficient to read and deploy the images, and the image must be replicated into
the region of the scaleset.
