using System;
using System.Threading.Tasks;
using Xunit;

namespace Didit.SmokeTests
{
    /// <summary>Skips unless <c>DIDIT_SANDBOX_API_KEY</c> is set.</summary>
    public sealed class SandboxFactAttribute : FactAttribute
    {
        public SandboxFactAttribute(string? extraVariable = null)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DIDIT_SANDBOX_API_KEY")))
            {
                Skip = "Set DIDIT_SANDBOX_API_KEY to run live sandbox tests.";
            }
            else if (extraVariable is not null && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(extraVariable)))
            {
                Skip = $"Set {extraVariable} to run this sandbox test.";
            }
        }
    }

    /// <summary>
    /// Read-mostly checks against a sandbox application. They create nothing that costs money: at most one sandbox
    /// session, and only when <c>DIDIT_SANDBOX_WORKFLOW_ID</c> is set.
    /// </summary>
    public sealed class SandboxTests
    {
        private static DiditClient Client() => new(new DiditClientOptions
        {
            ApiKey = Environment.GetEnvironmentVariable("DIDIT_SANDBOX_API_KEY"),
            OrganizationId = Environment.GetEnvironmentVariable("DIDIT_SANDBOX_ORGANIZATION_ID"),
            ApplicationId = Environment.GetEnvironmentVariable("DIDIT_SANDBOX_APPLICATION_ID"),
            BaseUrl = Uri.TryCreate(Environment.GetEnvironmentVariable("DIDIT_SANDBOX_BASE_URL"), UriKind.Absolute, out var url) ? url : DiditClientOptions.DefaultBaseUrl,
        });

        [SandboxFact]
        public async Task Lists_sessions_and_workflows()
        {
            using var client = Client();
            var sessions = await client.Sessions.ListAsync(new SessionsListParams { Limit = 1 });
            Assert.NotNull(sessions.Results);
            var workflows = await client.Workflows.ListAsync(new WorkflowsListParams { Limit = 1 });
            Assert.NotNull(workflows.Results);
        }

        [SandboxFact]
        public async Task Wrong_api_key_is_rejected_with_a_typed_error()
        {
            using var client = Client().WithOptions(o => o.ApiKey = "invalid-key-for-smoke-test");
            var error = await Assert.ThrowsAnyAsync<ApiStatusException>(() => client.Sessions.ListAsync(new SessionsListParams { Limit = 1 }));
            Assert.True(error.StatusCode is 401 or 403, $"status {error.StatusCode}");
        }

        [SandboxFact("DIDIT_SANDBOX_WORKFLOW_ID")]
        public async Task Creates_a_sandbox_session_and_reads_its_decision()
        {
            using var client = Client();
            var session = await client.Sessions.CreateAsync(new SessionsCreateParams
            {
                WorkflowId = Environment.GetEnvironmentVariable("DIDIT_SANDBOX_WORKFLOW_ID")!,
                VendorData = "sdk-dotnet-smoke-test",
            });
            Assert.False(string.IsNullOrEmpty(session.SessionId));
            Assert.False(string.IsNullOrEmpty(session.Url));

            var decision = await client.Sessions.RetrieveDecisionAsync(session.SessionId!);
            Assert.Equal(session.SessionId, decision.SessionId);
        }
    }
}
