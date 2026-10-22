// Quickstart: create a verification session, read its decision, verify a webhook.
//
//   export DIDIT_API_KEY=...            # Business Console > Developers > API keys
//   export DIDIT_WORKFLOW_ID=...        # a published workflow
//   export DIDIT_WEBHOOK_SECRET=...     # Business Console > Developers > Webhooks
//   dotnet run --project examples/Quickstart
//
// DIDIT_BASE_URL (optional, this example only) points the client at another server, such as a local mock.
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Didit;

var options = new DiditClientOptions();
if (Uri.TryCreate(Environment.GetEnvironmentVariable("DIDIT_BASE_URL"), UriKind.Absolute, out var baseUrl))
{
    options.BaseUrl = baseUrl;
}

using var client = new DiditClient(options);

// 1. Create a session and send the user to its hosted verification URL.
var session = await client.Sessions.CreateAsync(new SessionsCreateParams
{
    WorkflowId = Environment.GetEnvironmentVariable("DIDIT_WORKFLOW_ID") ?? "11111111-2222-3333-4444-555555555555",
    VendorData = "user-42",
    Callback = "https://example.com/verification/done",
});
Console.WriteLine($"Session {session.SessionId} created: send the user to {session.Url}");

// 2. Read the decision (poll, or better, wait for the webhook below).
var decision = await client.Sessions.RetrieveDecisionAsync(session.SessionId!);
Console.WriteLine($"Decision: {decision.Status}");
foreach (var check in decision.IdVerifications ?? Array.Empty<SessionsRetrieveDecisionResponseIdVerifications>())
{
    Console.WriteLine($"  ID verification {check.Status}: {check.FirstName} {check.LastName}, {check.DocumentType}");
}

// 3. Verify a webhook. In your server, pass the raw request body and its headers (see examples/WebhookMinimalApi).
var secret = Environment.GetEnvironmentVariable("DIDIT_WEBHOOK_SECRET") ?? "whsec_test_quickstart_example";
var (body, headers) = SampleDelivery(session.SessionId!, secret);
var evt = DiditWebhooks.Verify(body, headers, secret);
switch (evt)
{
    case SessionStatusUpdatedEvent statusUpdated:
        Console.WriteLine($"Webhook verified: session {statusUpdated.SessionId} is {statusUpdated.Status}");
        break;
    default:
        Console.WriteLine($"Webhook verified: {evt.WebhookType}");
        break;
}

// A delivery signed the way Didit signs it, so the example runs without a real webhook.
static (string Body, Dictionary<string, string> Headers) SampleDelivery(string sessionId, string secret)
{
    var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    var body = $"{{\"created_at\":{timestamp},\"session_id\":\"{sessionId}\",\"status\":\"Approved\",\"timestamp\":{timestamp},\"vendor_data\":\"user-42\",\"webhook_type\":\"status.updated\"}}";
    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
    var signature = BitConverter.ToString(hmac.ComputeHash(Encoding.UTF8.GetBytes(body))).Replace("-", string.Empty).ToLowerInvariant();
    return (body, new Dictionary<string, string> { ["X-Signature"] = signature, ["X-Timestamp"] = timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture) });
}
