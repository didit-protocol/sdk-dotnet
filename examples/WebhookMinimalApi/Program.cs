// Didit webhook receiver with ASP.NET Core minimal APIs.
//
//   export DIDIT_WEBHOOK_SECRET=...   # Business Console > Developers > Webhooks
//   dotnet run --project examples/WebhookMinimalApi --urls http://localhost:5080
//
// Point the webhook destination at https://<your-host>/webhooks/didit.
using System;
using System.IO;
using System.Linq;
using Didit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// During a secret rotation, list both secrets: new first, then old.
var secrets = (Environment.GetEnvironmentVariable("DIDIT_WEBHOOK_SECRET") ?? string.Empty)
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

app.MapPost("/webhooks/didit", async (HttpRequest request, ILogger<Program> logger) =>
{
    // Verify the raw body: X-Signature signs these exact bytes.
    using var reader = new StreamReader(request.Body);
    var body = await reader.ReadToEndAsync();

    DiditEvent evt;
    try
    {
        evt = DiditWebhooks.Verify(body, name => request.Headers[name].FirstOrDefault(), secrets);
    }
    catch (WebhookVerificationException error)
    {
        logger.LogWarning("Rejected a Didit webhook: {Reason}", error.Reason);
        return Results.Unauthorized();
    }

    switch (evt)
    {
        case SessionStatusUpdatedEvent session:
            logger.LogInformation("Session {SessionId} ({VendorData}) is {Status}", session.SessionId, session.VendorData, session.Status);
            break;
        case TransactionStatusUpdatedEvent transaction:
            logger.LogInformation("Transaction {TxnId} is {Status}, score {Score}", transaction.TxnId, transaction.Status, transaction.Score);
            break;
        case TravelRuleStatusUpdatedEvent travelRule:
            logger.LogInformation("Travel Rule for {TxnId} is {Status}", travelRule.TxnId, travelRule.TravelRuleStatus);
            break;
        default:
            logger.LogInformation("Didit event {Type} ({EventId})", evt.WebhookType, evt.EventId);
            break;
    }

    // Answer quickly; do slow work in the background. Didit retries deliveries that fail.
    return Results.Ok();
});

app.Run();
