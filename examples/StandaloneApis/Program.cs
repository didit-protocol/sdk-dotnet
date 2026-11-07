// Standalone APIs: verify an ID document from images, then screen the person against AML lists.
//
//   export DIDIT_API_KEY=...
//   dotnet run --project examples/StandaloneApis -- front.jpg back.jpg
using System;
using System.Threading.Tasks;
using Didit;

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: StandaloneApis <front-image> [back-image]");
    return 1;
}

using var client = new DiditClient();

var idCheck = await client.IdVerification.VerifyAsync(new IdVerificationVerifyParams
{
    FrontImage = FileUpload.FromPath(args[0]),
    BackImage = args.Length > 1 ? FileUpload.FromPath(args[1]) : Optional<FileUpload>.Unset,
    VendorData = "user-42",
});
var document = idCheck.IdVerification;
Console.WriteLine($"ID verification: {document?.Status} ({document?.DocumentType}, {document?.IssuingState})");

var fullName = string.Join(" ", document?.FirstName, document?.LastName).Trim();
var screening = await client.Aml.ScreenAsync(new AmlScreenParams
{
    FullName = fullName.Length > 0 ? fullName : "Jane Example",
    EntityType = AmlScreenParamsEntityType.Person,
    VendorData = "user-42",
});
Console.WriteLine($"AML screening: {screening.Aml?.Status}, {screening.Aml?.TotalHits ?? 0} hits");

return 0;
