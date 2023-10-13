using Semver;

namespace Microsoft.OneFuzz.Service;

public class Versions {
    public static bool IsMinimumVersion(string versionStr, string minimumStr) {
        var version = SemVersion.Parse(versionStr, SemVersionStyles.Any);
        var minimum = SemVersion.Parse(minimumStr, SemVersionStyles.Any);

        return version.ComparePrecedenceTo(minimum) >= 0;
    }
}
