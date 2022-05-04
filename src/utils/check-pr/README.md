# PR integration test tool

## Requirements

* Python >= 3.7, and dependencies (see `requirements.txt`)
* [az-cli](https://docs.microsoft.com/en-us/cli/azure/install-azure-cli).
* [azcopy](https://docs.microsoft.com/en-us/azure/storage/common/storage-use-azcopy-v10)
* [azure-functions-core-tools](https://docs.microsoft.com/en-us/azure/azure-functions/functions-run-local?tabs=v4%2Clinux%2Ccsharp%2Cportal%2Cbash#install-the-azure-functions-core-tools) (ver. 4.x required)

## Setup

1. In GitHub, generate a personal access token (PAT) with the `public_repo` scope.
   You may need to enable SSO for the token, depending on the org that your OneFuzz fork belongs to.
1. Set `GITHUB_ISSUE_TOKEN` equal to the above PAT in your shell session.
   [`direnv`](https://direnv.net/) can help here.
1. Create and activate a virtualenv, e.g. `python -m venv venv` followed by `. ./venv/bin/activate` (e.g. on Linux).
1. Install dependencies with `pip install -r requirements.txt`

You can now invoke `check-pr.py`.
See `./check-pr.py -h` for available commands.
