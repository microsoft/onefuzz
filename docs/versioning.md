# Versioning

OneFuzz follows [Semantic Versioning 2.0](https://semver.org/).

At a high level, the summary from [semver.org](semver.org), says:

> Given a version number MAJOR.MINOR.PATCH, increment the:
>
> 1. MAJOR version when you make incompatible API changes,
> 1. MINOR version when you add functionality in a backwards compatible manner,
>    and
> 1. PATCH version when you make backwards compatible bug fixes. Additional
>    labels for pre-release and build metadata are available as extensions to
>    the MAJOR.MINOR.PATCH format.

## Versioning Focus

Our focus for compatibility is the CLI and Service. We will work towards
automated compatibility testing of the CLI and Service for one minor version
back.

## Additional Care for Versioning

A user should _always_ be able to access any artifact related to a crash report
or third-party integration for crash reports such as Azure Devops Work Items or
Microsoft Teams messages. As such, special care should be taken to ensure
compatibility for Crash Reports and Task Configurations, and accessing said
information, above and beyond the compatibility for the rest of OneFuzz.