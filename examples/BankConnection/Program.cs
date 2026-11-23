// Single-user sandbox demo. The developer running the server also plays the customer.
// Forward your sandbox webhook destination to /webhooks/didit on this receiver.
// Set DIDIT_API_KEY (bank-data permission) and DIDIT_WEBHOOK_SECRET, then run:
// dotnet run --project examples/BankConnection --urls http://localhost:5080
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Didit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(server => server.Limits.MaxRequestBodySize = 1024 * 1024);
var app = builder.Build();
var secret = Environment.GetEnvironmentVariable("DIDIT_WEBHOOK_SECRET");
ArgumentException.ThrowIfNullOrWhiteSpace(secret);

var options = new DiditClientOptions();
if (Uri.TryCreate(Environment.GetEnvironmentVariable("DIDIT_BASE_URL"), UriKind.Absolute, out var baseUrl))
{
    options.BaseUrl = baseUrl;
}

using var client = new DiditClient(options);
var link = await client.Bank.CreateLinkAsync(new BankLinkCreateRequest
{
    Mode = BankLinkCreateRequestMode.Connect,
    Access = BankLinkCreateRequestAccess.OneTime,
    Country = "ESP",
    Data = new[] { BankLinkCreateRequestData.Accounts },
});
var linkId = link.Id ?? throw new InvalidOperationException("Bank Link response is missing its ID.");
if (!Uri.TryCreate(link.Url, UriKind.Absolute, out var hostedUrl) || hostedUrl.Scheme != Uri.UriSchemeHttps)
{
    throw new InvalidOperationException("Bank Link response is missing its hosted HTTPS URL.");
}

app.MapPost("/webhooks/didit", async (HttpRequest request) =>
{
    using var body = new MemoryStream();
    await request.Body.CopyToAsync(body, request.HttpContext.RequestAborted);
    DiditEvent delivery;
    try
    {
        delivery = DiditWebhooks.Verify(body.ToArray(), name => request.Headers[name].FirstOrDefault(), secret);
    }
    catch (WebhookVerificationException)
    {
        return Results.Unauthorized();
    }

    if (delivery is not BankConnectionCreatedEvent created || string.IsNullOrEmpty(created.ConnectionId))
    {
        return Results.NoContent();
    }

    var cancellation = request.HttpContext.RequestAborted;
    var currentLink = await client.Bank.GetLinkAsync(linkId, cancellationToken: cancellation);
    if (currentLink.ConnectionId is null && currentLink.Status != BankLinkStatus.Cancelled
        && currentLink.Status != BankLinkStatus.Expired && currentLink.Status != BankLinkStatus.Failed)
    {
        // The delivery can arrive before the completed link is readable. Ask for redelivery.
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }

    if (currentLink.ConnectionId == created.ConnectionId)
    {
        var accounts = await client.Bank.ListAccountsAsync(created.ConnectionId, cancellationToken: cancellation);
        Console.WriteLine($"Read {accounts.Items?.Count ?? 0} accounts on the first page.");
    }

    return Results.NoContent();
});

await app.StartAsync();
// Deliver the hosted URL directly to the local user. Only the webhook route is served.
using var browser = Process.Start(new ProcessStartInfo(hostedUrl.AbsoluteUri) { UseShellExecute = true });
await app.WaitForShutdownAsync();
