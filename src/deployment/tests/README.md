# Functional tests.

- `nsg_tst.py` 

    Requires an existing OneFuzz deployment running in Azure.
    The OneFuzz deployment has to be already pre-configured using OneFuzz CLI or deployment `config_path` can be passed as command line argument to `nsg_test.py`.

    `nsg_test.py` validates that OneFuzz configures NSGs correctly to allow or block access to debug proxy.
