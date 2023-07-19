# Integration Tests

The integration tests in this project allow specific Functions to be run against
Azure Storage. They can be run in two modes:

- **Against the [Azurite](https://github.com/Azure/Azurite) storage emulator**:
  these tests are run by default. `azurite` must be started and running (e.g.
  with `azurite -s &`).

- **Against a real Azure Storage account**: to use this, the environment
  variables `AZURE_ACCOUNT_NAME` and `AZURE_ACCOUNT_KEY` must be set.

  These tests can be excluded by running `dotnet test` with the arguments
  `--filter "Category!=Live"`.

The same tests are used in each case. The way this is achieved in Xunit is by
writing the tests in an (abstract) base class and then deriving two
implementations from this base class, one for each “run configuration”. 
