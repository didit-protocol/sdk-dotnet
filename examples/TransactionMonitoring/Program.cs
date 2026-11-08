// Transaction monitoring, wallet screening and the Travel Rule.
//
//   export DIDIT_API_KEY=...
//   dotnet run --project examples/TransactionMonitoring
using System;
using System.Threading.Tasks;
using Didit;

using var client = new DiditClient();

// Screen a payment. The response carries the risk score, the severity and the status set by your rules.
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
    Subject = new TransactionsCreateParamsSubject
    {
        VendorData = "user-42",
        FullName = "Jane Example",
    },
});
Console.WriteLine($"Transaction {transaction.TxnId}: {transaction.Status}, score {transaction.Score}, severity {transaction.Severity}");

// Walk every transaction that needs a decision; pages are fetched as the loop advances.
await foreach (var item in await client.Transactions.ListAsync(new TransactionsListParams { Limit = 50 }))
{
    Console.WriteLine($"  {item.TxnId} {item.Status}");
}

// Screen a crypto wallet before paying out.
var wallet = await client.WalletScreening.ScreenAsync(new WalletScreeningScreenParams
{
    WalletAddress = "bc1qexampleexampleexampleexampleexample0",
    Blockchain = WalletScreeningScreenParamsBlockchain.Btc,
});
Console.WriteLine($"Wallet risk score {wallet.RiskScore}, status {wallet.Status}");

// Travel Rule: your settings and the VASP directory.
var settings = await client.TravelRule.RetrieveSettingsAsync();
Console.WriteLine($"Travel Rule enabled: {settings.IsEnabled} ({settings.Jurisdiction})");
var vasps = await client.TravelRule.ListVaspsAsync(new TravelRuleListVaspsParams { Limit = 5 });
foreach (var vasp in vasps.Results)
{
    Console.WriteLine($"  VASP {vasp.Name}");
}
