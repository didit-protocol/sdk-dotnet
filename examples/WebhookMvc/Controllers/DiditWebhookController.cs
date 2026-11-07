using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Didit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace WebhookMvc.Controllers
{
    [ApiController]
    [Route("webhooks/didit")]
    public sealed class DiditWebhookController : ControllerBase
    {
        private static readonly string Secret = Environment.GetEnvironmentVariable("DIDIT_WEBHOOK_SECRET") ?? string.Empty;
        private readonly DiditClient _didit;
        private readonly ILogger<DiditWebhookController> _logger;

        public DiditWebhookController(DiditClient didit, ILogger<DiditWebhookController> logger)
        {
            _didit = didit;
            _logger = logger;
        }

        [HttpPost]
        public async Task<IActionResult> Receive()
        {
            // Read the raw body yourself: model binding would re-encode it and break X-Signature.
            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();

            DiditEvent evt;
            try
            {
                evt = DiditWebhooks.Verify(body, name => Request.Headers[name].FirstOrDefault(), Secret);
            }
            catch (WebhookVerificationException error)
            {
                _logger.LogWarning("Rejected a Didit webhook: {Reason}", error.Reason);
                return Unauthorized();
            }

            if (evt is SessionStatusUpdatedEvent session && session.SessionId is not null)
            {
                // The event carries the decision; re-fetching shows how to call the API from a receiver.
                var decision = await _didit.Sessions.RetrieveDecisionAsync(session.SessionId);
                _logger.LogInformation("Session {SessionId} is {Status}", decision.SessionId, decision.Status);
            }

            return Ok();
        }
    }
}
