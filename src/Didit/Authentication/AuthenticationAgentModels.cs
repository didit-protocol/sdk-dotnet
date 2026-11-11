using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Didit.Internal;

namespace Didit
{
    /// <summary>
    /// The JSON-serialized result of <c>navigator.credentials.get()</c> that proves a fresh passkey approval of an agent request.
    /// Every binary field is base64url text; never send an unconverted <c>PublicKeyCredential</c>.
    /// </summary>
    public sealed class AuthAgentPasskeyAssertion
    {
        /// <summary><c>id</c>: the base64url credential identifier.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary><c>rawId</c>: the base64url raw credential identifier.</summary>
        public string RawId { get; set; } = string.Empty;

        /// <summary><c>type</c>: always <c>public-key</c>.</summary>
        public string Type { get; set; } = "public-key";

        /// <summary><c>response.clientDataJSON</c>: base64url client data JSON.</summary>
        public string ClientDataJson { get; set; } = string.Empty;

        /// <summary><c>response.authenticatorData</c>: base64url authenticator data.</summary>
        public string AuthenticatorData { get; set; } = string.Empty;

        /// <summary><c>response.signature</c>: base64url signature.</summary>
        public string Signature { get; set; } = string.Empty;

        /// <summary><c>response.userHandle</c>: base64url user handle, when the authenticator supplied one.</summary>
        public string? UserHandle { get; set; }

        /// <summary><c>clientExtensionResults</c>: the browser's extension results, as a JSON object, when present.</summary>
        public JsonElement? ClientExtensionResults { get; set; }

        internal string?[] Secrets()
        {
            // Object keys and values, as in the request body.
            var secrets = new List<string?>
            {
                "id", "rawId", "type", "response", "clientDataJSON", "authenticatorData", "signature",
                Id, RawId, Type, ClientDataJson, AuthenticatorData, Signature, UserHandle,
            };
            if (UserHandle is not null)
            {
                secrets.Add("userHandle");
            }

            if (ClientExtensionResults is { } extensions)
            {
                secrets.Add("clientExtensionResults");
                secrets.AddRange(Strings(extensions));
            }

            return secrets.ToArray();
        }

        // GetString throws on a lone escaped surrogate: redact the raw JSON spelling (without its quotes) then,
        // since that is exactly what the request body carried.
        private static string? SafeString(JsonElement element)
        {
            try
            {
                return element.GetString();
            }
            catch (InvalidOperationException)
            {
                var raw = element.GetRawText();
                return raw.Length >= 2 ? raw.Substring(1, raw.Length - 2) : null;
            }
        }

        private static IEnumerable<string?> Strings(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    var value = SafeString(element);
                    if (value is not null)
                    {
                        yield return value;
                    }

                    break;
                case JsonValueKind.Array:
                    foreach (var text in element.EnumerateArray().SelectMany(Strings))
                    {
                        yield return text;
                    }

                    break;
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        yield return property.Name;
                        foreach (var text in Strings(property.Value))
                        {
                            yield return text;
                        }
                    }

                    break;
            }
        }

        internal void Write(Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            writer.WriteString("id", Id);
            writer.WriteString("rawId", RawId);
            writer.WriteString("type", Type);
            writer.WritePropertyName("response");
            writer.WriteStartObject();
            writer.WriteString("clientDataJSON", ClientDataJson);
            writer.WriteString("authenticatorData", AuthenticatorData);
            writer.WriteString("signature", Signature);
            if (UserHandle is not null)
            {
                writer.WriteString("userHandle", UserHandle);
            }

            writer.WriteEndObject();
            if (ClientExtensionResults is { } extensions)
            {
                writer.WritePropertyName("clientExtensionResults");
                extensions.WriteTo(writer);
            }

            writer.WriteEndObject();
        }
    }

    /// <summary>The known values of the consent decision of <see cref="Authentication.SubmitAgentConsentAsync"/>.</summary>
    public static class AuthAgentConsentDecision
    {
        /// <summary>Approve the agent request.</summary>
        public const string Approve = "approve";

        /// <summary>Deny the agent request.</summary>
        public const string Deny = "deny";
    }

    /// <summary>
    /// The answer to <see cref="Authentication.SubmitAgentConsentAsync"/>: either an approval (<see cref="Code"/>,
    /// <see cref="RedirectUri"/> and <see cref="State"/>) or a denial (<see cref="Status"/> is <c>denied</c>). The two shapes share
    /// no discriminator field, so use <see cref="IsApproved"/> and <see cref="IsDenied"/>.
    /// </summary>
    public sealed class AuthAgentConsentResult
    {
        private AuthAgentConsentResult(JsonElement raw)
        {
            Raw = raw;
            Code = AuthJson.GetString(raw, "code");
            RedirectUri = AuthJson.GetString(raw, "redirect_uri");
            State = AuthJson.GetString(raw, "state");
            Status = AuthJson.GetString(raw, "status");
        }

        /// <summary><c>code</c>: the single-use agent authorization code, valid for 60 seconds (approval only).</summary>
        public string? Code { get; }

        /// <summary><c>redirect_uri</c>: the registered redirect URI of the agent request (approval only).</summary>
        public string? RedirectUri { get; }

        /// <summary><c>state</c> of the agent request (approval only).</summary>
        public string? State { get; }

        /// <summary><c>status</c>: <c>denied</c> when the person declined (denial only).</summary>
        public string? Status { get; }

        /// <summary><c>true</c> when the request was approved and <see cref="Code"/> was issued.</summary>
        public bool IsApproved => Code is not null;

        /// <summary><c>true</c> when the request was denied and no code was issued.</summary>
        public bool IsDenied => Code is null && Status == "denied";

        /// <summary>The whole response, including fields this SDK version does not know.</summary>
        public JsonElement Raw { get; }

        internal static AuthAgentConsentResult FromJson(JsonElement raw) => new(raw);
    }

    /// <summary>A passkey registered to the approving person, as listed in <see cref="AuthAgentWebAuthnOptions.AllowCredentials"/>.</summary>
    public sealed class AuthAgentCredentialDescriptor
    {
        internal AuthAgentCredentialDescriptor(JsonElement raw)
        {
            Raw = raw;
            Id = AuthJson.GetString(raw, "id");
            Type = AuthJson.GetString(raw, "type");
            Transports = AuthJson.GetStringList(raw, "transports");
        }

        /// <summary><c>id</c>: the base64url credential identifier.</summary>
        public string? Id { get; }

        /// <summary><c>type</c>: <c>public-key</c>.</summary>
        public string? Type { get; }

        /// <summary><c>transports</c>: optional authenticator transport hints.</summary>
        public IReadOnlyList<string> Transports { get; }

        /// <summary>The object as sent, including fields this SDK version does not know.</summary>
        public JsonElement Raw { get; }
    }

    /// <summary>WebAuthn request options for a fresh approval; pass them as <c>publicKey</c> to <c>navigator.credentials.get()</c> after decoding the binary fields.</summary>
    public sealed class AuthAgentWebAuthnOptions
    {
        internal AuthAgentWebAuthnOptions(JsonElement raw)
        {
            Raw = raw;
            Challenge = AuthJson.GetString(raw, "challenge");
            RpId = AuthJson.GetString(raw, "rpId");
            Timeout = AuthJson.GetInt64(raw, "timeout");
            UserVerification = AuthJson.GetString(raw, "userVerification");
            var credentials = new List<AuthAgentCredentialDescriptor>();
            if (AuthJson.TryGet(raw, "allowCredentials", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                credentials.AddRange(list.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object).Select(item => new AuthAgentCredentialDescriptor(item)));
            }

            AllowCredentials = credentials;
        }

        /// <summary><c>challenge</c>: base64url, bound to this approval and valid for less than 300 seconds.</summary>
        public string? Challenge { get; }

        /// <summary><c>rpId</c>: the relying-party hostname.</summary>
        public string? RpId { get; }

        /// <summary><c>timeout</c>: the browser timeout in milliseconds; it does not extend the server expiry.</summary>
        public long? Timeout { get; }

        /// <summary><c>userVerification</c>: always <c>required</c>.</summary>
        public string? UserVerification { get; }

        /// <summary><c>allowCredentials</c>: only the approving person's enrolled passkeys.</summary>
        public IReadOnlyList<AuthAgentCredentialDescriptor> AllowCredentials { get; }

        /// <summary>The options as sent, ready to hand to the browser once the binary fields are decoded.</summary>
        public JsonElement Raw { get; }
    }

    /// <summary>The answer to <see cref="Authentication.CreateAgentApprovalOptionsAsync"/>.</summary>
    public sealed class AuthAgentApprovalOptions
    {
        private AuthAgentApprovalOptions(JsonElement raw)
        {
            Raw = raw;
            var options = AuthJson.GetElement(raw, "options");
            Options = new AuthAgentWebAuthnOptions(options is { ValueKind: JsonValueKind.Object } value ? value : AuthJson.EmptyObject);
        }

        /// <summary><c>options</c>: the WebAuthn request options.</summary>
        public AuthAgentWebAuthnOptions Options { get; }

        /// <summary>The whole response, including fields this SDK version does not know.</summary>
        public JsonElement Raw { get; }

        internal static AuthAgentApprovalOptions FromJson(JsonElement raw) => new(raw);
    }
}
