# Introduction 

 This tool allows users to token-authenticate with a Symbol Server and serve symbol files. 
 Optionally supports automation & Blob Storage symbol-caching.
 Supports local users (yourself) and automation with a given ClientId/Secret. Optionally supports a token audience.

# Getting Started

You will need .NET 8 Runtime/SDK in order to code / build / use as a dotnet tool.

- [Install .NET 8 SDK/Runtime Interactively](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)

OR

- `winget install dotnet-runtime-8`
- `winget install dotnet-sdk-8`

### Update your Symbol Path:

`SETX _NT_SYMBOL_PATH SRV*http://localhost:XXXX`

### Running the proxy

Authentication as the local user (yourself) takes a few moments sometimes. If the proxy is configured with `IsNoisy = true`, you'll see:
- `Access token acquired successfully.`
- `Local Symbol Proxy started at http://localhost:XXXX/`

The proxy is then ready for use and listening for requests on the port designated in the AppSettings file. 

# AppSettings explained

## Mandatory Settings
- `SymbolServerURI`: The URI of the symbol server you'd like to use. e.g. https://somesymbolserver.com/
- `LocalPort`: The `http://localhost/` port you'd like to use in your symbol path. e.g. `1234`. Defaults to 5000. Ensure you `.SYMPATH+` this value, or update the _NT_SYMBOL_PATH_ environment variable.

## Optional Settings

- `BlobConnectionString`: A connection string to optionally cache symbols in a Blob Storage account.
- `BlobContainerName`: The name of the container in the storage account above, which you'd like to cache symbols in.
- `ClientId`: Used for automation - ClientID of the application you're requesting a token for.
- `ClientSecret`: Used for automation - ClientSecret of the application you're requesting a token for.
- `IsNoisy`: Used for local development - 'True' is essentially !symnoisy for the proxy's console window.
- `SymDownloadRetryCount`: Number of times to retry the symbol server if a symbol isn't found. Defaults to 2.
- `TenantId`: Used for automation - TenantID of the application you're requesting a token for.
- `TokenAudience`: If your endpoint requires a [Token Audience](https://learn.microsoft.com/en-us/entra/identity-platform/claims-validation#validate-the-audience) for authentication, include that here. 
- Please note that if your Symbol Server requires an [Authentication Scope](https://learn.microsoft.com/en-us/entra/identity-platform/scopes-oidc#the-default-scope) besides `./default`, you'll need to edit `ProxyHandler.cs` to accommodate.

## Contributing

This project welcomes contributions and suggestions.  Most contributions require you to agree to a
Contributor License Agreement (CLA) declaring that you have the right to, and actually do, grant us
the rights to use your contribution. For details, visit https://cla.opensource.microsoft.com.

When you submit a pull request, a CLA bot will automatically determine whether you need to provide
a CLA and decorate the PR appropriately (e.g., status check, comment). Simply follow the instructions
provided by the bot. You will only need to do this once across all repos using our CLA.

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/).
For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or
contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.

## Trademarks

This project may contain trademarks or logos for projects, products, or services. Authorized use of Microsoft 
trademarks or logos is subject to and must follow 
[Microsoft's Trademark & Brand Guidelines](https://www.microsoft.com/en-us/legal/intellectualproperty/trademarks/usage/general).
Use of Microsoft trademarks or logos in modified versions of this project must not cause confusion or imply Microsoft sponsorship.
Any use of third-party trademarks or logos are subject to those third-party's policies.
