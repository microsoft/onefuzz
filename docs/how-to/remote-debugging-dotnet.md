## How to setup remote debugging of dotnet Azure Functions on new deployments

1) when running `deploy.py` use `--host_dotnet_on_windows` flag as part of the command line. This will deploy `dotnet` Azure Function on Windows Server Farm, which supports functinoality for remote debugging of `dotnet` code.

2) Follow instructions on how to connect Visual Studio 2022 to newly deployed Azure Function: [https://docs.microsoft.com/en-us/azure/azure-functions/functions-develop-vs?tabs=in-process#remote-debugging](https://docs.microsoft.com/en-us/azure/azure-functions/functions-develop-vs?tabs=in-process#remote-debugging)