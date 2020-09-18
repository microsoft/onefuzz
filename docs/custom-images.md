# Fuzzing using Custom OS Images

In order to use custom OS images in OneFzuz, the image _must_ run the
[Azure VM Agent](https://docs.microsoft.com/en-us/azure/virtual-machines/extensions/overview).

Building custom images can be automated using the
[Linux](https://docs.microsoft.com/en-us/azure/virtual-machines/linux/image-builder)
or
[Windows](https://docs.microsoft.com/en-us/azure/virtual-machines/windows/image-builder)
image builders for Azure.

If you have a custom Windows VHD, you should follow the
[Guide to prepare a VHD for Azure](https://docs.microsoft.com/en-us/azure/virtual-machines/windows/prepare-for-upload-vhd-image).

From there, rather than using Image SKUs such as
`Canonical:UbuntuServer:18.04-LTS:latest`, use the full resource ID to the
shared image, such as
`/subscriptions/MYSUBSCRIPTION/resourceGroups/MYGROUP/providers/Microsoft.Compute/galleries/MYGALLERY/images/MYDEFINITION/versions/MYVERSION`

The images must be hosted in a
[Shared Image Gallery](https://docs.microsoft.com/en-us/azure/virtual-machines/windows/shared-image-galleries).
The Service Principal for the OneFuzz instance must have RBAC to the shared
image gallery sufficient to deploy the images.
