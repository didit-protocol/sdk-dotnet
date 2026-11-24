using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Didit.Tests.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Didit.Tests.DependencyInjection
{
    public sealed class AddDiditTests
    {
        [Fact]
        public async Task Registers_a_singleton_client_using_the_http_client_factory()
        {
            var handler = new MockHttpHandler();
            handler.Enqueue(200, "{\"count\":0,\"next\":null,\"previous\":null,\"results\":[]}");
            var services = new ServiceCollection();
            services.AddDidit(o =>
            {
                o.ApiKey = "di_key";
                o.MaxRetries = 0;
            }).ConfigurePrimaryHttpMessageHandler(() => handler);

            using var provider = services.BuildServiceProvider();
            var client = provider.GetRequiredService<DiditClient>();
            Assert.Same(client, provider.GetRequiredService<DiditClient>());

            await client.Sessions.ListAsync();

            var request = Assert.Single(handler.Requests);
            Assert.Equal("di_key", request.Header("x-api-key"));
            Assert.Equal(0, client.Options.MaxRetries);
        }

        [Fact]
        public void Binds_options_from_configuration()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Didit:ApiKey"] = "config_key",
                    ["Didit:OrganizationId"] = "org-1",
                    ["Didit:ApplicationId"] = "app-1",
                    ["Didit:BaseUrl"] = "http://localhost:9000",
                    ["Didit:Timeout"] = "00:00:10",
                    ["Didit:MaxRetries"] = "5",
                })
                .Build();
            var services = new ServiceCollection();
            services.AddDidit(configuration.GetSection("Didit"));

            using var provider = services.BuildServiceProvider();
            var options = provider.GetRequiredService<DiditClient>().Options;

            Assert.Equal("config_key", options.ApiKey);
            Assert.Equal("org-1", options.OrganizationId);
            Assert.Equal("app-1", options.ApplicationId);
            Assert.Equal(new Uri("http://localhost:9000"), options.BaseUrl);
            Assert.Equal(TimeSpan.FromSeconds(10), options.Timeout);
            Assert.Equal(5, options.MaxRetries);
        }

        [Fact]
        public async Task Debug_lines_go_to_ilogger_when_debug_is_enabled()
        {
            var lines = new List<string>();
            var handler = new MockHttpHandler();
            handler.Enqueue(200, "{\"count\":0,\"next\":null,\"previous\":null,\"results\":[]}");
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Debug).AddProvider(new ListLoggerProvider(lines)));
            services.AddDidit(o => o.ApiKey = "k").ConfigurePrimaryHttpMessageHandler(() => handler);

            using var provider = services.BuildServiceProvider();
            await provider.GetRequiredService<DiditClient>().Sessions.ListAsync();

            Assert.Contains(lines, line => line.IndexOf("GET /v3/sessions/ -> 200", StringComparison.Ordinal) >= 0);
        }

        [Fact]
        public void Null_arguments_are_rejected()
        {
            Assert.Throws<ArgumentNullException>(() => DiditServiceCollectionExtensions.AddDidit(null!));
            Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddDidit((IConfiguration)null!));
        }

        private sealed class ListLoggerProvider : ILoggerProvider
        {
            private readonly List<string> _lines;

            public ListLoggerProvider(List<string> lines) => _lines = lines;

            public ILogger CreateLogger(string categoryName) => new ListLogger(_lines);

            public void Dispose()
            {
            }

            private sealed class ListLogger : ILogger
            {
                private readonly List<string> _lines;

                public ListLogger(List<string> lines) => _lines = lines;

                public IDisposable? BeginScope<TState>(TState state)
                    where TState : notnull => null;

                public bool IsEnabled(LogLevel logLevel) => true;

                public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                    _lines.Add(formatter(state, exception));
            }
        }
    }
}
