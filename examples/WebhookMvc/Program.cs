// Didit webhook receiver with an ASP.NET Core MVC controller.
//
//   export DIDIT_WEBHOOK_SECRET=...
//   export DIDIT_API_KEY=...          # used to re-fetch the decision
//   dotnet run --project examples/WebhookMvc --urls http://localhost:5081
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddDidit();

var app = builder.Build();
app.MapControllers();
app.Run();
