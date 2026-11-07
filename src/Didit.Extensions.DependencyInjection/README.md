# Didit.Extensions.DependencyInjection

Registers the official [Didit](https://didit.me) .NET client (`Didit` on NuGet) with `Microsoft.Extensions.DependencyInjection` and `IHttpClientFactory`.

```csharp
builder.Services.AddDidit(options =>
{
    options.ApiKey = builder.Configuration["Didit:ApiKey"];
});

// or bind a configuration section (ApiKey, OrganizationId, ApplicationId, BaseUrl, Timeout, MaxRetries)
builder.Services.AddDidit(builder.Configuration.GetSection("Didit"));
```

Then inject `DiditClient`. `AddDidit` returns the `IHttpClientBuilder` of the named client `Didit`, so you can add your own handlers.
Debug lines (method, path, status, duration, request id; never bodies or headers) go to `ILogger<DiditClient>` at Debug level.

Documentation: https://github.com/didit-protocol/sdk-dotnet and https://docs.didit.me
