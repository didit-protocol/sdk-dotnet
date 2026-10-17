# Didit .NET library

The official Didit .NET library.
Identity verification (KYC), business verification (KYB), AML screening, liveness and biometrics, transaction monitoring, wallet screening, the Travel Rule and customer Authentication, from your C#, F# or VB backend.

> In development. Not released yet: nothing is published to NuGet before the first release.

```sh
dotnet add package Didit
# optional: services.AddDidit() with IHttpClientFactory
dotnet add package Didit.Extensions.DependencyInjection
```

Supported runtimes: .NET 8 and later, and every runtime that implements .NET Standard 2.0 (.NET Framework 4.7.2 or later, Mono, Unity). On .NET Framework, set `<LangVersion>latest</LangVersion>` (C# 11 or later) to construct the request types, which use `required` members.

- API reference: https://docs.didit.me
- Every operation of the API has a method; the full tree is in the [API overview](#api-overview).
- Design contract shared by the seven Didit server SDKs: [docs/DESIGN.md](docs/DESIGN.md)

## Quickstart

Create a verification session, send the user to its URL, then read the decision.

```csharp
using Didit;

using var client = new DiditClient(new DiditClientOptions
{
    ApiKey = Environment.GetEnvironmentVariable("DIDIT_API_KEY"), // the default; shown for clarity
});

var session = await client.Sessions.CreateAsync(new SessionsCreateParams
{
    WorkflowId = "11111111-2222-3333-4444-555555555555",
    VendorData = "user-42",
    Callback = "https://example.com/verification/done",
});
Console.WriteLine($"Send the user to {session.Url}");

var decision = await client.Sessions.RetrieveDecisionAsync(session.SessionId!);
Console.WriteLine(decision.Status); // Approved, Declined, In Review, ...
```

Then receive the result as a webhook. Verify it with the raw body and the headers (ASP.NET Core minimal API):

```csharp
app.MapPost("/webhooks/didit", async (HttpRequest request) =>
{
    using var reader = new StreamReader(request.Body);
    var body = await reader.ReadToEndAsync();
    try
    {
        var evt = DiditWebhooks.Verify(body, name => request.Headers[name].FirstOrDefault(), webhookSecret);
        if (evt is SessionStatusUpdatedEvent { Status: "Approved" } approved)
        {
            // approved.SessionId, approved.VendorData, approved.Decision
        }

        return Results.Ok();
    }
    catch (WebhookVerificationException error)
    {
        return Results.Unauthorized(); // error.Reason says why
    }
});
```

Runnable versions: [examples/Quickstart](examples/Quickstart) and [examples/WebhookMinimalApi](examples/WebhookMinimalApi).

## Configuration

| Option | Default | Environment fallback |
|---|---|---|
| `ApiKey` | none | `DIDIT_API_KEY` |
| `OrganizationId`, `ApplicationId` (application-scoped operations: cases, networks, regulatory reports, report templates, user and business monitoring) | none | `DIDIT_ORGANIZATION_ID`, `DIDIT_APPLICATION_ID` |
| `BaseUrl` | `https://verification.didit.me` | |
| `AccountBaseUrl` | `https://apx.didit.me/auth/v2` | |
| `AccountToken` (account management) | none | |
| `Timeout` (per attempt) | 60 seconds | |
| `MaxRetries` | 2 | |
| `DefaultHeaders` | none | |
| `HttpClient` | one owned by the client | |
| `Logger` | none | `DIDIT_LOG=debug` writes to standard error |

`DiditClient` is thread-safe: create one per process and share it.
A missing API key, or a missing organization or application id for an application-scoped operation, throws `ConfigurationException` before any request is sent.

Override options for one call with `RequestOptions`, or derive a client with `WithOptions` (it shares the connection pool):

```csharp
var page = await client.Sessions.ListAsync(
    new SessionsListParams { Limit = 100 },
    new RequestOptions { Timeout = TimeSpan.FromSeconds(10), MaxRetries = 0 });

var otherApp = client.WithOptions(o => o.ApplicationId = "another-application-id");
```

With dependency injection (`Didit.Extensions.DependencyInjection`), the client's `HttpClient` comes from `IHttpClientFactory`:

```csharp
builder.Services.AddDidit(builder.Configuration.GetSection("Didit")); // ApiKey, OrganizationId, ApplicationId, ...
// then inject DiditClient
```

Every request carries `x-api-key`, `User-Agent: didit-dotnet/<version> (<runtime>; <os>)`, `X-Didit-Client: dotnet/<version>` and `Accept: application/json`.
The API key is only ever sent to `BaseUrl`: it never appears in logs or exception messages, a pagination `next` URL on another host is refused, and the client's own `HttpClient` does not follow redirects.

## Errors

Every exception derives from `DiditException`.

| Exception | When |
|---|---|
| `ApiConnectionException` | no response: DNS, connection, TLS |
| `ApiTimeoutException` (an `ApiConnectionException`) | the attempt exceeded `Timeout` |
| `ApiStatusException` | any non-2xx: `StatusCode`, `Body` (parsed JSON), `RawBody`, `Headers`, `RequestId` |
| `BadRequestException` 400, `AuthenticationException` 401, `PermissionDeniedException` 403, `NotFoundException` 404, `ConflictException` 409, `UnprocessableEntityException` 422, `RateLimitException` 429, `InternalServerException` 5xx | subclasses of `ApiStatusException` |
| `WebhookVerificationException` | a webhook failed verification; `Reason` is machine-readable |
| `TokenVerificationException` | an Authentication token failed verification; `Reason` is machine-readable |
| `ConfigurationException` | missing API key, organization or application id, account token, or webhook secret |

```csharp
try
{
    await client.Sessions.RetrieveDecisionAsync(sessionId);
}
catch (NotFoundException)
{
    // unknown session, or outside the key's workflows
}
catch (ApiStatusException error)
{
    logger.LogWarning("Didit {Status}: {Message} (request id {RequestId})", error.StatusCode, error.Message, error.RequestId);
}
```

## Retries and timeouts

Failed requests are retried up to `MaxRetries` times (default 2) with exponential backoff (0.5 s, doubling, capped at 8 s, full jitter), honouring `Retry-After` up to 60 s.
`GET`, `HEAD`, `PUT` and `DELETE` are retried on connection errors, timeouts, 408, 429 and 5xx.
`POST` and `PATCH` are never retried once the request may have reached the server: only when the connection failed before sending, or on 429.
Operations that accept an idempotency key take it from `RequestOptions.IdempotencyKey`, so your own code can retry a `POST` safely.

## Pagination

List operations return a `Page<T>` with `Count`, `Next`, `Previous` and `Results`. Enumerate it with `await foreach` to walk every item of every page; the next pages are fetched as the loop advances.

```csharp
await foreach (var session in await client.Sessions.ListAsync(new SessionsListParams { Limit = 100, Status = "Approved" }))
{
    Console.WriteLine(session.SessionId);
}

var page = await client.Users.ListAsync();
while (page is not null)
{
    Handle(page.Results);
    page = await page.GetNextPageAsync();
}
```

## Files and binary responses

Upload parameters take a `FileUpload` from a path, bytes or a stream, with an optional file name and content type. Streams are rewound (or buffered once) so retries resend the same bytes.

```csharp
var result = await client.IdVerification.VerifyAsync(new IdVerificationVerifyParams
{
    FrontImage = FileUpload.FromPath("front.jpg"),
    BackImage = FileUpload.FromStream(backStream, "back.jpg", "image/jpeg"),
});
```

Reports come back as `BinaryResponse` with `Content` (bytes), `ContentType`, `FileName`, `OpenRead()` and `SaveToAsync(path)`:

```csharp
var pdf = await client.Sessions.GeneratePdfAsync(sessionId);
await pdf.SaveToAsync("decision.pdf");
```

## Raw responses

`client.WithRawResponse` mirrors every resource and returns `DiditResponse<T>` with `StatusCode`, `Headers`, `RequestId`, the raw `Body` and the parsed `Data`:

```csharp
var response = await client.WithRawResponse.Sessions.CreateAsync(parameters);
Console.WriteLine($"{response.StatusCode} {response.RequestId} {response.GetHeader("X-RateLimit-Remaining")}");
var session = response.Data;
```

## Webhooks

`DiditWebhooks.Verify(rawBody, headers, secret)` checks the signature and the timestamp and returns a typed event; anything else throws `WebhookVerificationException` with a `Reason` (`missing_signature`, `invalid_signature`, `timestamp_mismatch`, `timestamp_out_of_tolerance`, ...).

- Pass the raw body as received (string or bytes). `X-Signature` signs the exact bytes; `X-Signature-V2` signs a canonical form of the JSON and survives middleware that re-encodes the body.
- Headers: a dictionary or any `name => value` lookup, such as `name => request.Headers[name].FirstOrDefault()` in ASP.NET Core.
- Secret rotation: pass several secrets (`new[] { current, previous }`). Didit also signs with the previous secret during a rotation (`X-Signature-Previous`, `X-Signature-V2-Previous`), so receivers that still hold only the old secret keep working.
- `tolerance` defaults to 5 minutes.
- `DiditWebhooks.VerifyParsed(JsonElement payload, ...)` is for frameworks that only expose the parsed JSON; it checks `X-Signature-V2`.
- `X-Signature-Simple` is deprecated and counts only with `allowSimple: true`; even then the event says `IsBodyAuthenticated == false`, so re-fetch the record from the API before acting on it.

Events: `SessionStatusUpdatedEvent`, `SessionDataUpdatedEvent`, `UserStatusUpdatedEvent`, `UserDataUpdatedEvent`, `BusinessStatusUpdatedEvent`, `BusinessDataUpdatedEvent`, `ActivityCreatedEvent`, `TransactionCreatedEvent`, `TransactionStatusUpdatedEvent`, `TravelRuleStatusUpdatedEvent`, `WorkflowComplianceChangedEvent`, `KybRegistrySearchResolvedEvent`, and `UnknownEvent` for any other type. Every event has `EventId`, `WebhookType`, `Timestamp`, `CreatedAt`, `ApplicationId`, `Environment` and the raw `Payload`.

```csharp
switch (DiditWebhooks.Verify(body, headers, secret))
{
    case SessionStatusUpdatedEvent e:
        await sessions.UpdateAsync(e.SessionId, e.Status, e.Decision);
        break;
    case TransactionStatusUpdatedEvent e:
        await payments.ApplyAsync(e.TxnId, e.Status, e.Score);
        break;
    case UnknownEvent e:
        logger.LogInformation("Unhandled Didit event {Type}", e.WebhookType);
        break;
}
```

Receivers: [ASP.NET Core minimal API](examples/WebhookMinimalApi) and [ASP.NET Core MVC](examples/WebhookMvc).

## Transactions, wallet screening and the Travel Rule

```csharp
var transaction = await client.Transactions.CreateAsync(new TransactionsCreateParams
{
    TransactionId = "txn_2026_0001",
    TransactionCategory = TransactionsCreateParamsTransactionCategory.Finance,
    TransactionDetails = new TransactionsCreateParamsTransactionDetails
    {
        Direction = TransactionsCreateParamsTransactionDetailsDirection.Outbound,
        Amount = 1500.00,
        Currency = "EUR",
    },
    Subject = new TransactionsCreateParamsSubject { VendorData = "user-42", FullName = "Jane Example" },
});
// transaction.Status, transaction.Score, transaction.Severity

var wallet = await client.WalletScreening.ScreenAsync(new WalletScreeningScreenParams
{
    WalletAddress = "bc1q...",
    Blockchain = WalletScreeningScreenParamsBlockchain.Btc,
});
```

Rules live under `client.Transactions.Rules` (CRUD, backtest, library), Travel Rule transfers under `client.Transactions.TravelRule`, and settings, the VASP directory, inbound registration and widget sessions under `client.TravelRule` (wallet addresses: `client.TravelRule.WalletAddresses`). See [examples/TransactionMonitoring](examples/TransactionMonitoring).

## Authentication

Didit Authentication gives each application its own OpenID Connect issuer for its end users. The `Authentication` class is for your backend: it verifies the tokens your frontend sends, exchanges authorization codes and manages sessions. It talks to your issuer and never takes the API key.

```csharp
var auth = Authentication.FromPublishableKey("pk_live_...");   // or new Authentication("https://acme.auth.didit.me")

// In an API: verify the access token from the Authorization header (ES256, issuer, audience, expiry).
var claims = await auth.VerifyAccessTokenAsync(accessToken, audience: "your-application-id");
Console.WriteLine($"{claims.Subject} {claims.VendorData}");

// Instant revocation check, and sign-out.
var current = await auth.GetSessionAsync(accessToken);
await auth.RevokeSessionAsync(accessToken);
```

Server-side login with PKCE:

```csharp
var pkce = Authentication.CreatePkce();
var state = Authentication.CreateState();
var nonce = Authentication.CreateNonce();
var authorize = (await auth.GetDiscoveryDocumentAsync()).AuthorizationEndpoint;
// redirect to authorize with response_type=code, redirect_uri, code_challenge=pkce.CodeChallenge, code_challenge_method=S256, state and nonce
var tokens = await auth.ExchangeCodeAsync(code, pkce.CodeVerifier, redirectUri);
var idClaims = await auth.VerifyIdTokenAsync(tokens.IdToken!, nonce, audience: "your-application-id");
var refreshed = await auth.RefreshAsync(tokens.RefreshToken!); // serialise refreshes per session: a reused refresh token revokes it
```

Step-up verification before a sensitive action: `CreateStepUpAsync(accessToken, action, returnTo, amount, currency)` and `RetrieveStepUpAsync(accessToken, id)`; act only when the status is `approved`. See [examples/AuthenticationApi](examples/AuthenticationApi).

## Account management

The account API creates organizations' applications and API keys programmatically. It authenticates with a bearer token from `client.Account.LoginAsync` instead of the API key:

```csharp
var login = await client.Account.LoginAsync(new LoginRequest { Email = email, Password = password });
var account = client.WithOptions(o => o.AccountToken = login.AccessToken);
var organizations = await account.Account.Organizations.ListAsync();
```

## Debug logging

Set `DIDIT_LOG=debug`, or `DiditClientOptions.Logger`, to log one line per attempt with method, path, status, duration and request id. Bodies and headers are never logged.

## API overview

Resources follow the API's naming (`sessions.imports.create` is `client.Sessions.Imports.CreateAsync`). Every method is `async`, takes optional `RequestOptions` and a `CancellationToken`, and has XML documentation from the API reference.

| Resource | Operations |
|---|---|
| `Sessions` (`Imports`, `Reviews`) | create, list, retrieve decision, update status and data, share and import, PDF reports, delete |
| Standalone APIs | `IdVerification`, `FaceMatch`, `FaceSearch`, `PassiveLiveness`, `AgeEstimation`, `ProofOfAddress`, `DocumentAi`, `Aml`, `DatabaseValidation`, `Phone`, `Email`, `Ip`, `Kyb` |
| `IdWallets`, `FraudChecks` | digital ID wallets, fraud checks |
| `Users`, `Businesses`, `BusinessSessions`, `AmlMonitoring` | entities, monitoring, faces |
| `Transactions` (`Rules`, `TravelRule`), `WalletScreening`, `TravelRule` (`WalletAddresses`) | transaction monitoring, wallet screening, Travel Rule |
| `Workflows`, `Questionnaires`, `Customization`, `Billing`, `BiometricTemplates` | configuration |
| `Webhooks.Destinations` | webhook destinations |
| `Cases` (`Notes`, `Links`, `Checklist`, `Requests`, `Teams`, `RoutingRules`, `Blueprints`, `RegulatoryReports`), `RegulatoryReports`, `ReportTemplates`, `Networks` (`Notes`) | case management and investigations (application-scoped) |
| `Account` (`Organizations`, `Applications`) | account management |

## Language notes

Choices DESIGN.md leaves to each language, made the .NET way:

- The client is `DiditClient` (a class cannot share the `Didit` namespace's name). Resources are properties, methods end in `Async`, and every method takes `RequestOptions? requestOptions = null, CancellationToken cancellationToken = default` last.
- Exceptions use the .NET `Exception` suffix: DESIGN.md's `DiditError` is `DiditException`, `APIStatusError` is `ApiStatusException`, `RateLimitError` is `RateLimitException`, and so on.
- Request types are records with settable properties. Required fields use C# `required`. Optional fields are `Optional<T>`: unset fields are not sent, `= null` sends JSON `null`, and a value converts implicitly (`VendorData = "user-42"`). A field typed as an interface (`IReadOnlyList<T>`) accepts arrays and lists; from a variable typed as the interface, write `Optional<IReadOnlyList<string>>.FromValue(list)`.
- Response types are records with nullable init-only properties: a field the API leaves out reads as `null` and never throws, so older SDK versions keep working when the API grows. Fields the SDK does not know yet are kept in `AdditionalProperties` (raw JSON), which request types also send.
- Enums are open: readonly structs with a static property per known value (`SessionsCreateResponseStatus.Approved`), any other string is accepted and kept, values compare with `==`, and a string converts implicitly (`NewStatus = "Approved"`). For an optional enum field, use the static property or `new T("value")`.
- Integers are `long`, numbers `double`, date-times `DateTimeOffset`, UUIDs and string decimals `string`, free-form JSON `JsonElement` in responses and `object` in requests (dictionaries and anonymous objects work).
- Type names follow DESIGN.md 5.1 literally, so deeply nested inline objects have long names (`SessionsRetrieveDecisionResponseIdVerifications`); `var` keeps code short.
- A query parameter whose name clashes with a body field of the same operation gets a `Query` suffix (`NodeIdQuery`).
- JSON uses System.Text.Json source generation. Free-form request values are serialised by reflection, except in trimmed or Native AOT apps that disable reflection.
- Raw responses: `client.WithRawResponse`, the C# form of `with_raw_response`.
- Pagination: `Page<T>` implements `IAsyncEnumerable<T>`; `AsPagesAsync()` walks pages.
- Webhooks: the static `DiditWebhooks` class, usable without a client. Canonical JSON is computed from the raw JSON text, so integers above 2^53 keep their exact value.
- Authentication verifies ES256 with the BCL's `ECDsa`, so the package needs no JWT library (DESIGN.md allows `Microsoft.IdentityModel.Tokens`; it is not needed).
- Debug logging is a delegate (`DiditClientOptions.Logger`); the dependency injection package routes it to `ILogger<DiditClient>`.

## Versioning

Semantic versioning. Until 1.0.0, breaking changes bump the minor version. Releases are cut by release-please from conventional commits, and `CHANGELOG.md` is generated.
The SDK is regenerated from the OpenAPI documents published at docs.didit.me; a removed API operation stays in the SDK, marked `[Obsolete]`, for one minor version.

## Contributing and security

See [CONTRIBUTING.md](CONTRIBUTING.md) for how the SDK is generated, tested and released, and [SECURITY.md](SECURITY.md) to report a vulnerability.

## License

MIT
