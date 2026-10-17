using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Didit.Internal;
using Xunit;

namespace Didit.Tests.Core
{
    public sealed class RequestBuildingTests
    {
        [Fact]
        public void Query_strings_encode_every_supported_type()
        {
            var query = new QueryString();
            query.Add("s", "a b&c");
            query.Add("n", (long?)5);
            query.Add("d", (double?)1.5);
            query.Add("t", (bool?)true);
            query.Add("f", (bool?)false);
            query.Add("when", (DateTimeOffset?)new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));
            query.Add("list", new[] { "x", "y" });
            query.Add("ids", new long[] { 1, 2 });
            query.Add("skipped", (string?)null);
            query.Add("skipped", (IEnumerable<string>?)null);
            query.Add("skipped", (IEnumerable<long>?)null);

            Assert.Equal("?s=a%20b%26c&n=5&d=1.5&t=true&f=false&when=2026-01-02T03%3A04%3A05%2B00%3A00&list=x&list=y&ids=1&ids=2", query.Encode());
            Assert.Equal(string.Empty, new QueryString().Encode());
        }

        [Fact]
        public async Task Multipart_bodies_encode_scalars_files_lists_json_and_free_form_values()
        {
            var form = new MultipartRequestBody();
            form.Add("text", "hello");
            form.Add("none", (string?)null);
            form.Add("long", (long?)7);
            form.Add("double", (double?)2.5);
            form.Add("bool", (bool?)false);
            form.Add("date", (DateTimeOffset?)new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero));
            form.Add("file", FileUpload.FromBytes(new byte[] { 1 }, "a.png"));
            form.Add("nofile", (FileUpload?)null);
            form.AddEach("tags", new[] { "a", "b" });
            form.AddEach("tags", (IEnumerable<string>?)null);
            form.AddEach("files", new[] { FileUpload.FromBytes(new byte[] { 2 }, "b.jpg") });
            form.AddEach("files", (IEnumerable<FileUpload>?)null);
            form.AddJson("object", new Dictionary<string, int> { ["k"] = 1 });
            form.AddJson<string?>("nulljson", null);
            form.AddAny("any-null", null);
            form.AddAny("any-string", "s");
            form.AddAny("any-bool", true);
            form.AddAny("any-int", 3);
            form.AddAny("any-decimal", 1.25m);
            form.AddAny("any-file", FileUpload.FromBytes(new byte[] { 3 }, "c.pdf"));
            form.AddAny("any-enum", new SessionsCreateResponseStatus("Approved"));
            form.AddAny("any-object", new Dictionary<string, string> { ["x"] = "y" });

            var parts = form.Parts.ToDictionary(p => p.Name + ":" + (p.Text ?? p.File!.FileName), p => p);
            Assert.Equal(new[]
            {
                "text:hello", "long:7", "double:2.5", "bool:false", "date:2026-01-02T00:00:00+00:00", "file:a.png", "tags:a", "tags:b",
                "files:b.jpg", "object:{\"k\":1}", "any-string:s", "any-bool:true", "any-int:3", "any-decimal:1.25", "any-file:c.pdf",
                "any-enum:Approved", "any-object:{\"x\":\"y\"}",
            }, parts.Keys);

            using var content = await form.CreateContentAsync(CancellationToken.None);
            var text = await content.ReadAsStringAsync();
            Assert.Contains("name=\"file\"; filename=\"a.png\"", text);
            Assert.Contains("Content-Type: image/png", text);
        }

        [Fact]
        public void Open_enum_converter_accepts_numbers_and_works_as_dictionary_keys()
        {
            var fromNumber = JsonSupport.Deserialize<SessionsCreateResponseStatus>("42");
            Assert.Equal("42", fromNumber.Value);
            Assert.Throws<JsonException>(() => JsonSupport.Deserialize<SessionsCreateResponseStatus>("{}"));

            var map = JsonSupport.Deserialize<Dictionary<SessionsCreateResponseStatus, int>>("{\"Approved\":1}")!;
            Assert.Equal(1, map[SessionsCreateResponseStatus.Approved]);
            Assert.Equal("{\"Approved\":1}", JsonSupport.Serialize(map));
        }

        [Fact]
        public void Path_parameters_reject_empty_values_and_format_numbers()
        {
            Assert.Equal("a%2Fb", PathParameter.Encode("a/b", "id"));
            Assert.Equal("12", PathParameter.Encode(12L, "id"));
            Assert.Throws<ArgumentException>(() => PathParameter.Encode((string?)null, "id"));
        }

        [Fact]
        public async Task Json_bodies_are_utf8_json()
        {
            var body = JsonRequestBody.Create(new Dictionary<string, string> { ["name"] = "José" });
            using var content = await body.CreateContentAsync(CancellationToken.None);
            Assert.Equal("application/json; charset=utf-8", content.Headers.ContentType!.ToString());
            Assert.Equal("{\"name\":\"José\"}", await content.ReadAsStringAsync());
            Assert.Equal("PATCH", HttpMethods.Patch.Method);
            Assert.Same(HttpMethod.Get, HttpMethods.Get);
        }
    }
}
