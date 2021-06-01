import os
import shutil
import subprocess  # nosec


def azcopy_sync(src: str, dst: str) -> None:
    """Expose azcopy for uploading/downloading files"""

    azcopy = os.environ.get("AZCOPY") or shutil.which("azcopy")
    if not azcopy:
        raise Exception(
            "unable to find 'azcopy' in path or AZCOPY environment variable"
        )

    # security note: callers need to understand the src/dst for this.
    subprocess.check_output([azcopy, "sync", src, dst, "--recursive=true"])  # nosec
