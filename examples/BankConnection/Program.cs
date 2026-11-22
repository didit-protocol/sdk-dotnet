// Bank verification: create a Bank Link, verify bank.connection.created, read the accounts.
//
//   export DIDIT_API_KEY=...            # Business Console > Developers > API keys (with the bank-data permission)
//   export DIDIT_WEBHOOK_SECRET=...     # Business Console > Developers > Webhooks
//   dotnet run --project examples/BankConnection
//
// DIDIT_CONNECTION_ID (optional) skips waiting for the person and reads that connection.
// DIDIT_BASE_URL (optional, this example only) points the client at another server, such as a local mock.
using System;
using System.Collections.Generic;
using System.Globalization;
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

// 1. Create a link. Send the person to link.Url; they connect their bank there.
var link = await client.Bank.CreateLinkAsync(new BankLinkCreateRequest
{
    Mode = BankLinkCreateRequestMode.Connect,
    Access = BankLinkCreateRequestAccess.Ongoing,
    Country = "ES",
    Segment = BankLinkCreateRequestSegment.Personal,
    Data = new[] { BankLinkCreateRequestData.Accounts, BankLinkCreateRequestData.Balances, BankLinkCreateRequestData.Transactions },
    VendorData = "user-42",
});
Console.WriteLine($"Bank Link {link.Id} created: send the person to {link.Url}");

// 2. In your server the connection arrives as a webhook. Verify the raw body and headers first.
//    This sample delivery is signed the way Didit signs it, so the example runs without a real webhook.
var secret = Environment.GetEnvironmentVariable("DIDIT_WEBHOOK_SECRET") ?? "whsec_test_bank_example";
var (body, headers) = SampleConnectionCreated("connection-1", secret);
switch (DiditWebhooks.Verify(body, headers, secret))
{
    case BankConnectionCreatedEvent created:
        Console.WriteLine($"Webhook verified: connection {created.ConnectionId}, first sync {created.InitialStatus}");
        break;
    default:
        Console.WriteLine("Webhook verified: not a bank.connection.created event");
        break;
}

// 3. Read the connection and its accounts once it is created. The webhook gives you the connection id;
//    here we poll the link so the example can continue without a webhook receiver.
var connectionId = Environment.GetEnvironmentVariable("DIDIT_CONNECTION_ID") ?? link.ConnectionId;
var deadline = DateTimeOffset.UtcNow.AddMinutes(5);
while (connectionId is null && DateTimeOffset.UtcNow < deadline
    && link.Status != BankLinkStatus.Cancelled && link.Status != BankLinkStatus.Expired && link.Status != BankLinkStatus.Failed)
{
    await Task.Delay(TimeSpan.FromSeconds(2));
    link = await client.Bank.GetLinkAsync(link.Id!);
    connectionId = link.ConnectionId;
}

if (connectionId is null)
{
    Console.WriteLine($"The link is {link.Status?.Value}: no connection to read. Set DIDIT_CONNECTION_ID to read an existing one.");
    return;
}

var connection = await client.Bank.GetConnectionAsync(connectionId);
Console.WriteLine($"Connection {connection.Id}: {connection.Status?.Value}, access {connection.Access?.Value}");
var accounts = await client.Bank.ListAccountsAsync(connectionId, new BankListAccountsParams { Limit = 100 });
foreach (var account in accounts.Items ?? Array.Empty<BankAccount>())
{
    Console.WriteLine($"  Account {account.Name} ({account.Currency}): {account.Status?.Value}");
}

// A delivery signed the way Didit signs it, so the example runs without a real webhook.
static (string Body, Dictionary<string, string> Headers) SampleConnectionCreated(string connectionId, string webhookSecret)
{
    var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    var body = "{\"application_id\":\"app-1\",\"connection_id\":\"" + connectionId + "\",\"created_at\":" + timestamp
        + ",\"environment\":\"sandbox\",\"event_id\":\"evt-bank-1\",\"initial_status\":\"succeeded\",\"timestamp\":" + timestamp
        + ",\"webhook_type\":\"bank.connection.created\"}";
    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(webhookSecret));
    var signature = BitConverter.ToString(hmac.ComputeHash(Encoding.UTF8.GetBytes(body))).Replace("-", string.Empty).ToLowerInvariant();
    return (body, new Dictionary<string, string>
    {
        ["X-Signature"] = signature,
        ["X-Timestamp"] = timestamp.ToString(CultureInfo.InvariantCulture),
    });
}
