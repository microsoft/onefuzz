import os
import shutil
import subprocess  # nosec


def find_azcopy() -> str:
    azcopy = os.environ.get("AZCOPY")
    binary_name = "azcopy" if os.name == "posix" else "azcopy.exe"

    if azcopy:
        if not os.path.exists(azcopy):
            raise Exception(f"AZCOPY environment variable is invalid: {azcopy}")
        elif os.path.isdir(azcopy):
            contains_azcopy = os.path.isfile(os.path.join(azcopy, binary_name))

            if contains_azcopy:
                azcopy = os.path.join(azcopy, binary_name)
            else:
                raise Exception(
                    f"The directory specified by AZCOPY doesn't contain the file '{binary_name}': {azcopy}"
                )
    else:
        azcopy = shutil.which("azcopy")

    if not azcopy:
        raise Exception(
            "unable to find 'azcopy' in path or AZCOPY environment variable"
        )

    return azcopy


def azcopy_sync(src: str, dst: str) -> None:
    """Expose azcopy for syncing existing files"""

    azcopy = find_azcopy()

    # security note: callers need to understand the src/dst for this.
    subprocess.check_output([azcopy, "sync", src, dst, "--recursive=true"])  # nosec


def azcopy_copy(src: str, dst: str) -> None:
    """Expose azcopy for uploading/downloading files"""

    azcopy = find_azcopy()

    # security note: callers need to understand the src/dst for this.
    subprocess.check_output([azcopy, "cp", src, dst, "--recursive=true"])  # nosec
