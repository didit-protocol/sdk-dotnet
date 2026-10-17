using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Didit.Internal;
using Didit.Tests.Support;
using Xunit;

namespace Didit.Tests.Core
{
    public sealed class JsonTests
    {
        [Fact]
        public void Optional_fields_are_omitted_when_unset_sent_when_set_and_null_when_set_to_null()
        {
            var unset = new SessionsCreateParams { WorkflowId = "wf" };
            Assert.Equal("{\"workflow_id\":\"wf\"}", JsonSupport.Serialize(unset));

            var set = new SessionsCreateParams { WorkflowId = "wf", VendorData = "user-1", Language = "es" };
            JsonAssert.Equivalent("{\"workflow_id\":\"wf\",\"vendor_data\":\"user-1\",\"language\":\"es\"}", JsonSupport.Serialize(set));

            var withNull = new SessionsDeleteParams { FaceRetentionDays = null };
            Assert.Equal("{\"face_retention_days\":null}", JsonSupport.Serialize(withNull));
        }

        [Fact]
        public void Additional_properties_are_sent_with_requests()
        {
            var request = new SessionsCreateParams
            {
                WorkflowId = "wf",
                AdditionalProperties = new Dictionary<string, JsonElement> { ["new_field"] = JsonDocument.Parse("42").RootElement },
            };
            JsonAssert.Equivalent("{\"workflow_id\":\"wf\",\"new_field\":42}", JsonSupport.Serialize(request));
        }

        [Fact]
        public void Responses_tolerate_unknown_enum_values_unknown_fields_and_missing_fields()
        {
            var json = "{\"session_id\":\"s\",\"status\":\"Brand New Status\",\"session_number\":\"17\",\"future_field\":{\"x\":1}}";
            var parsed = JsonSupport.Deserialize<SessionsCreateResponse>(json)!;

            Assert.Equal("Brand New Status", parsed.Status!.Value.Value);
            Assert.NotEqual(SessionsCreateResponseStatus.Approved, parsed.Status.Value);
            Assert.Equal(17, parsed.SessionNumber);
            Assert.Equal(1, parsed.AdditionalProperties!["future_field"].GetProperty("x").GetInt32());
            Assert.Null(parsed.Url);
            Assert.Contains("Brand New Status", JsonSupport.Serialize(parsed));
        }

        [Fact]
        public void Open_enums_compare_by_value()
        {
            SessionsCreateResponseStatus approved = "Approved";
            Assert.Equal(SessionsCreateResponseStatus.Approved, approved);
            Assert.True(approved == SessionsCreateResponseStatus.Approved);
            Assert.False(approved != SessionsCreateResponseStatus.Approved);
            Assert.Equal("Approved", approved.ToString());
            Assert.Equal(approved.GetHashCode(), new SessionsCreateResponseStatus("Approved").GetHashCode());
            Assert.True(approved.Equals((object)SessionsCreateResponseStatus.Approved));
            Assert.Equal(string.Empty, default(SessionsCreateResponseStatus).Value);
            Assert.Throws<ArgumentNullException>(() => new SessionsCreateResponseStatus(null!));
        }

        [Fact]
        public void Date_times_parse_leniently()
        {
            Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), JsonSupport.Deserialize<DateTimeOffset>("\"2026-01-02T03:04:05Z\""));
            Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), JsonSupport.Deserialize<DateTimeOffset>("\"2026-01-02 03:04:05\""));
            Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1760702400), JsonSupport.Deserialize<DateTimeOffset>("1760702400"));
            Assert.Throws<JsonException>(() => JsonSupport.Deserialize<DateTimeOffset>("\"not a date\""));
            Assert.Equal("\"2026-01-02T03:04:05+00:00\"", JsonSupport.Serialize(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero)));
        }

        [Fact]
        public void Strings_accept_numbers_and_booleans()
        {
            Assert.Equal("12", JsonSupport.Deserialize<string>("12"));
            Assert.Equal("true", JsonSupport.Deserialize<string>("true"));
            Assert.Null(JsonSupport.Deserialize<string>("null"));
        }

        [Fact]
        public void Optional_value_semantics()
        {
            Optional<string?> unset = default;
            Optional<string?> nulled = null;
            Optional<string?> value = "v";

            Assert.False(unset.IsSet);
            Assert.True(nulled.IsSet);
            Assert.Null(nulled.Value);
            Assert.Equal("v", value.Value);
            Assert.Throws<InvalidOperationException>(() => unset.Value);
            Assert.Equal("fallback", unset.GetValueOrDefault("fallback"));
            Assert.True(value.TryGetValue(out var got) && got == "v");
            Assert.Equal(Optional<string?>.Unset, unset);
            Assert.True(value == Optional<string?>.FromValue("v"));
            Assert.True(value != nulled);
            Assert.Equal("(unset)", unset.ToString());
            Assert.Equal("null", nulled.ToString());
            Assert.NotEqual(unset.GetHashCode(), nulled.GetHashCode());
            Assert.False(value.Equals((object)"v"));
        }

        [Fact]
        public async Task Optional_query_and_body_together()
        {
            var handler = new MockHttpHandler();
            handler.Enqueue(200, "{}");
            using var client = TestClients.Create(handler);

            await client.Sessions.UpdateAmlHitStatusAsync("s-1", new SessionsUpdateAmlHitStatusParams { HitId = "h-1", ReviewStatus = "False Positive", NodeId = "node-7" });

            var request = Assert.Single(handler.Requests);
            Assert.Equal("?node_id=node-7", request.Query);
            Assert.DoesNotContain("node", request.BodyText);
            JsonAssert.Equivalent("{\"hit_id\":\"h-1\",\"review_status\":\"False Positive\"}", request.BodyText);
        }
    }
}
