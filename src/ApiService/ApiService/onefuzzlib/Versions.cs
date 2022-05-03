using Semver;

namespace Microsoft.OneFuzz.Service;

public class versions {
    public static bool IsMinimumVersion(string versionStr, string minimumStr) {
        var version = SemVersion.Parse(versionStr, SemVersionStyles.Any);
        var minimum = SemVersion.Parse(minimumStr, SemVersionStyles.Any);

        return version >= minimum;
    }
}
