# Contributing

There are many ways to contribute to the OneFuzz project: logging bugs,
submitting pull requests, reporting issues, and creating suggestions.

Please read our [project values](docs/values.md).

## Reporting Security Issues

**Please do not report security vulnerabilities through public GitHub issues.**
Instead, please report them to the Microsoft Security Response Center (MSRC).
See [SECURITY.md](./SECURITY.md) for more information.

## Before you start, file an issue

Please follow this simple rule to help us eliminate any unnecessary wasted
effort & frustration, and ensure an efficient and effective use of everyone's
time - yours, ours, and other community members':

> 👉 If you have a question, think you've discovered an issue, would like to
> propose a new feature, etc., then find/file an issue **BEFORE** starting work
> to fix/implement it.

### Search existing issues first

Before filing a new issue, search existing open and closed issues first: This
project is moving fast! It is likely someone else has found the problem you're
seeing, and someone may be working on or have already contributed a fix!

If no existing item describes your issue/feature, great - please file a new
issue:

### File a new Issue

* Don't know whether you're reporting an issue or requesting a feature? File an
  issue
* Have a question that you don't see answered in docs, videos, etc.? File an
  issue
* Want to know if we're planning on building a particular feature? File an issue
* Got a great idea for a new feature? File an issue/request/idea
* Found an existing issue that describes yours? Great - up-vote and add
  additional commentary / info / repro-steps / etc.

When you hit "New Issue", select the type of issue closest to what you want to
report/ask/request.

### Complete the template

**Complete the information requested in the issue template, providing as much
information as possible**. The more information you provide, the more likely
your issue/ask will be understood and implemented. Helpful information
includes:

* What tools and apps you're using (e.g. VS 2019, VSCode, etc.)
* Don't assume we're experts in setting up YOUR environment and don't assume we
  are experts in `<your distro/tool of choice>`. Teach us to help you!
* **We LOVE detailed repro steps!** What steps do we need to take to reproduce
  the issue? Assume we love to read repro steps. As much detail as you can
  stand is probably _barely_ enough detail for us.
* Prefer error message text where possible or screenshots of errors if text
  cannot be captured
* We MUCH prefer text command-line script than screenshots of command-line
  script.

> 👉 If you don't have any additional info/context to add but would like to
> indicate that you're affected by the issue, upvote the original issue by
> clicking its [+😊] button and hitting 👍 (+1) icon. This way we can actually
> measure how impactful an issue is.

## Contributing fixes / features

For those able & willing to help fix issues and/or implement features ...

### To Spec or not to Spec

Some issues/features may be quick and simple to describe and understand. For
such scenarios, once a team member has agreed with your approach, skip ahead to
the section headed "Fork, Branch, and Create your PR", below.

Small issues that do not require a spec will be labelled Issue-Bug or
Issue-Task.

However, some issues/features will require careful thought & formal design
before implementation. For these scenarios, we'll request that a spec is
written and the associated issue will be labeled Issue-Feature.

Specs help collaborators discuss different approaches to solve a problem,
describe how the feature will behave, how the feature will impact the user,
what happens if something goes wrong, etc. Driving towards agreement in a spec,
before any code is written, often results in simpler code, and less wasted
effort in the long run.

Specs will be managed in a very similar manner as code contributions so please
follow the "Fork, Branch and Create your PR" below.

### Writing / Contributing-to a Spec

To write/contribute to a spec: fork, branch and commit via PRs, as you would
with any code changes.

Specs are written in markdown, stored under the `\doc\spec` folder and named
`[issue id] - [spec description].md`.

> 👉 **It is important to follow the spec templates and complete the requested
> information**. The available spec templates will help ensure that specs
> contain the minimum information & decisions necessary to permit development
> to begin.  In particular, specs require you to confirm that you've already
> discussed the issue/idea with the team in an issue and that you provide the
> issue ID for reference.

Team members will be happy to help review specs and guide them to completion.

### Help Wanted

Once the team have approved an issue/spec, development can proceed. If no
developers are immediately available, the spec can be parked ready for a
developer to get started. Parked specs' issues will be labeled "Help Wanted".
To find a list of development opportunities waiting for developer involvement,
visit the Issues and filter on [the Help-Wanted
label](https://github.com/microsoft/onefuzz/labels/Help%20Wanted).

## Development

### Fork, Clone, Branch and Create your PR

Once you've discussed your proposed feature/fix/etc. with a team member, and
you've agreed an approach or a spec has been written and approved, it's time to
start development:

1. Fork the repo if you haven't already
1. Clone your fork locally
1. Create & push a feature branch
1. Create a [Draft Pull Request (PR)](https://github.blog/2019-02-14-introducing-draft-pull-requests/)
1. Work on your changes
1. Try to follow the existing style of the related code as closely as possible.

#### Python Specific

1. Provide as much context in typing variables as possible.  Example:
   `Dict[UUID, int]` is better than `Any`.
1. For a complex set of data, consider using objects (such as
   [pydantic](https://pydantic-docs.helpmanual.io/) typed data classes) rather
   than nested Dicts, Tuples, or Lists.

### Local Build Prerequisites

OneFuzz is built with multiple components and runs on Linux and Windows:

* [Rust](https://www.rust-lang.org/) (latest stable)
* [Python](https://www.python.org) (at least 3.7 or later)
* [Azure Functions Core
  Tools](https://docs.microsoft.com/en-us/azure/azure-functions/functions-run-local)
(latest version of the 4.x)

While local builds are possible for every component of the system, new
contributors may find [automatic builds for
PRs](https://github.com/microsoft/onefuzz/actions?query=workflow%3ABuild)
beneficial.

### Getting the sources

First, fork the OneFuzz repository so that you can make a pull request. Then,
clone your fork locally:

```
git clone https://github.com/<<<your-github-account>>>/onefuzz.git
```

Occasionally you will want to merge changes in the upstream repository (the
official code repo) with your fork.

```
cd onefuzz
git checkout main
git pull https://github.com/microsoft/onefuzz.git main
```

Manage any merge conflicts, commit them, and then push them to your fork.

> 👉 The `microsoft/onefuzz` repository contains GitHub Actions that
> automatically build OneFuzz as well as triage components during our
> development. As you may not want these running on your fork, you can disable
> Actions for your fork by via 
> `https://github.com/YOUR_USERNAME/onefuzz/settings/actions`.

### Deploying from a build

These instructions assume a working python 3.7 (or later) install and a
logged-in Azure CLI session.

1. Download the `release-artifacts` from your CICD build.
2. Create a new directory for the release artifacts
3. Unzip the `release-artifacts.zip` into this new directory
4. Unzip the resulting zip file that starts with `onefuzz-deployment`
5. Setup a python virtual environment.  example: `python3 -m venv onefuzz-deploy-venv`
6. Activate the virtual environment. example: `. onefuzz-deploy-venv/bin/activate`
7. Install python-wheel. example: `pip install wheel`
8. Install the deployment prerequisites.  Example: `pip install -r requirements.txt`
9. Run the deployment script: Example: `python deploy.py ${REGION} ${GROUP} ${INSTANCE} ${OWNER}`

### Code Review

When you'd like the team to take a look, (even if the work is not yet
fully-complete), mark the PR as 'Ready For Review' so that the team can review
your work and provide comments, suggestions, and request changes. It may take
several cycles, but the end result will be solid, testable, conformant code
that is safe for us to merge.

We will treat community PR's with the same level of scrutiny and rigor as
commits submitted by our internal team.

### Merge

Once your code has been reviewed and approved by the requisite number of team
members, it will be merged into the master branch. Once merged, your PR will be
automatically closed.

## Thank you

Thank you in advance for your contribution!
