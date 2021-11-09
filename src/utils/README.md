# lint.sh

Before you run:

Install [direnv](https://direnv.net/) and [hook it into your shell](https://direnv.net/docs/hook.html)

Restart your shell to apply direnv hook.

## From inside `src/cli` and run following shell commands

- `echo "layout python3" >> .envrc`
- `direnv allow`
- `pip install -e ../pytypes`
- `pip install -e .`


## From inside `src/pytypes` and run following shell commands
- `echo "layout python3" >> .envrc`
- `direnv allow`
- `pip install -e .`


## From inside `src/api-service` and run following shell commands
- `echo "layout python3" >> .envrc`
- `direnv allow`
- `pip install -e ../pytypes`
- `pip install -r requirements-dev.txt`
- `cd \_\_app\_\_; pip install -r requirements.txt`

Now your environment is setup and you are ready to run `bash lint.sh` to reformat your code and make OneFuzz CI linter happy.