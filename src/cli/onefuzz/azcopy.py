import os
import shutil
import subprocess  # nosec


def find_azcopy() -> str:
    azcopy = os.environ.get("AZCOPY")

    if azcopy:
        if not os.path.exists(azcopy):
            raise Exception(f"AZCOPY environment variable is invalid: {azcopy}")
    else:
        azcopy = shutil.which("azcopy")

    if not azcopy:
        raise Exception(
            "unable to find 'azcopy' in path or AZCOPY environment variable"
        )

    return azcopy


def azcopy_sync(src: str, dst: str) -> None:
    """Expose azcopy for uploading/downloading files"""

    azcopy = find_azcopy()

    # security note: callers need to understand the src/dst for this.
    subprocess.check_output([azcopy, "sync", src, dst, "--recursive=true"])  # nosec
