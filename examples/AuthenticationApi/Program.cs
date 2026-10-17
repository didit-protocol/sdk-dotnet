// Didit Authentication in an ASP.NET Core backend: protect an API with Didit access tokens, sign users in with
// PKCE on the server, and require a step-up before a payout.
//
//   export DIDIT_PUBLISHABLE_KEY=pk_test_...    # or DIDIT_AUTH_ISSUER=https://<slug>.auth.didit.me
//   export DIDIT_APPLICATION_ID=...              # the client_id and token audience
//   dotnet run --project examples/AuthenticationApi --urls http://localhost:5082
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Didit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

var issuer = Environment.GetEnvironmentVariable("DIDIT_AUTH_ISSUER");
var auth = string.IsNullOrEmpty(issuer)
    ? Authentication.FromPublishableKey(Environment.GetEnvironmentVariable("DIDIT_PUBLISHABLE_KEY") ?? throw new InvalidOperationException("Set DIDIT_PUBLISHABLE_KEY or DIDIT_AUTH_ISSUER."))
    : new Authentication(issuer);
var applicationId = Environment.GetEnvironmentVariable("DIDIT_APPLICATION_ID") ?? throw new InvalidOperationException("Set DIDIT_APPLICATION_ID.");
const string RedirectUri = "http://localhost:5082/callback";

// Demo storage for pending sign-ins; use your session store in production.
var pending = new ConcurrentDictionary<string, (string Verifier, string Nonce)>();

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(auth);
var app = builder.Build();

// 1. An API endpoint that accepts Didit access tokens.
app.MapGet("/me", async (HttpRequest request) =>
{
    var claims = await Authorize(request);
    return claims is null
        ? Results.Unauthorized()
        : Results.Ok(new { claims.Subject, claims.VendorData, claims.Amr, Organization = claims.Organization?.Id });
});

// 2. Server-side sign-in with PKCE.
app.MapGet("/login", async () =>
{
    var pkce = Authentication.CreatePkce();
    var state = Authentication.CreateState();
    var nonce = Authentication.CreateNonce();
    pending[state] = (pkce.CodeVerifier, nonce);

    var discovery = await auth.GetDiscoveryDocumentAsync();
    var url = $"{discovery.AuthorizationEndpoint}?response_type=code&client_id={Uri.EscapeDataString(applicationId)}"
        + $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}&scope=openid%20email&state={state}&nonce={nonce}"
        + $"&code_challenge={pkce.CodeChallenge}&code_challenge_method={pkce.CodeChallengeMethod}";
    return Results.Redirect(url);
});

app.MapGet("/callback", async (string code, string state) =>
{
    if (!pending.TryRemove(state, out var signIn))
    {
        return Results.BadRequest("Unknown or reused state.");
    }

    var tokens = await auth.ExchangeCodeAsync(code, signIn.Verifier, RedirectUri);
    var user = await auth.VerifyIdTokenAsync(tokens.IdToken!, signIn.Nonce, applicationId);
    // Store tokens.RefreshToken server-side; refresh one session at a time (a reused refresh token revokes it).
    return Results.Ok(new { user.Subject, user.Email, user.EmailVerified });
});

// 3. A sensitive action behind a step-up: proceed only when the step-up is approved.
app.MapPost("/payouts", async (HttpRequest request, string amount, string? stepUpId) =>
{
    var claims = await Authorize(request);
    if (claims is null)
    {
        return Results.Unauthorized();
    }

    var accessToken = BearerToken(request)!;
    if (stepUpId is null)
    {
        var stepUp = await auth.CreateStepUpAsync(accessToken, "payout", "https://example.com/payouts/confirm", amount, "EUR");
        return stepUp.Required
            ? Results.Json(new { step_up_id = stepUp.Id, verification_url = stepUp.VerificationUrl }, statusCode: StatusCodes.Status403Forbidden)
            : Results.Ok(new { paid = amount });
    }

    var result = await auth.RetrieveStepUpAsync(accessToken, stepUpId);
    return result.IsApproved ? Results.Ok(new { paid = amount }) : Results.Json(new { status = result.Status }, statusCode: StatusCodes.Status403Forbidden);
});

app.Run();

async Task<AccessTokenClaims?> Authorize(HttpRequest request)
{
    var token = BearerToken(request);
    if (token is null)
    {
        return null;
    }

    try
    {
        return await auth.VerifyAccessTokenAsync(token, audience: applicationId);
    }
    catch (TokenVerificationException)
    {
        return null; // Reason says why: expired, invalid_signature, invalid_audience, ...
    }
}

static string? BearerToken(HttpRequest request)
{
    var header = request.Headers.Authorization.ToString();
    return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header.Substring("Bearer ".Length).Trim() : null;
}
