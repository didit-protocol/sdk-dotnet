# Examples

Runnable examples of the Didit .NET library. They reference the library project; in your own project, `dotnet add package Didit`.

| Example | What it shows |
|---|---|
| [Quickstart](Quickstart) | create a verification session, read its decision, verify a webhook |
| [StandaloneApis](StandaloneApis) | ID verification from images, AML screening |
| [TransactionMonitoring](TransactionMonitoring) | transaction screening, auto-pagination, wallet screening, Travel Rule |
| [WebhookMinimalApi](WebhookMinimalApi) | webhook receiver with ASP.NET Core minimal APIs, secret rotation |
| [WebhookMvc](WebhookMvc) | webhook receiver with an ASP.NET Core MVC controller and `services.AddDidit()` |
| [AuthenticationApi](AuthenticationApi) | Didit Authentication: protect an API with access tokens, server-side login with PKCE |

```sh
export DIDIT_API_KEY=...
dotnet run --project examples/Quickstart
```
