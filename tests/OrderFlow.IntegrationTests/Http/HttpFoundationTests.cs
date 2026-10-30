using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using OrderFlow.Application.Common.Errors;
using OrderFlow.Application.Common.Money;
using Xunit;

namespace OrderFlow.IntegrationTests.Http;

public class HttpFoundationTests
{
    [Theory]
    [InlineData(ApplicationError.ValidationFailed, 400, "validation_failed")]
    [InlineData(ApplicationError.NotFound, 404, "not_found")]
    [InlineData(ApplicationError.Forbidden, 403, "forbidden")]
    [InlineData(ApplicationError.StateConflict, 409, "state_conflict")]
    [InlineData(ApplicationError.IdempotencyConflict, 409, "idempotency_conflict")]
    [InlineData(ApplicationError.InsufficientStock, 409, "insufficient_stock")]
    [InlineData(ApplicationError.VersionConflict, 409, "version_conflict")]
    [InlineData(ApplicationError.OrderExpired, 409, "order_expired")]
    public async Task Expected_failures_have_explicit_status_codes(ApplicationError error, int status, string code)
    {
        await using var factory = new FoundationApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync($"/_tests/foundations/business/{error}");
        var problem = await AssertProblem(response, status, code);
        Assert.Equal(error == ApplicationError.ValidationFailed ? 1 : 0,
            problem.GetProperty("errors").EnumerateObject().Count());
        Assert.DoesNotContain(factory.Logs.Entries, entry => entry.Level >= LogLevel.Error);
    }

    [Theory]
    [InlineData(400, "validation_failed")]
    [InlineData(401, "unauthenticated")]
    [InlineData(403, "forbidden")]
    [InlineData(404, "not_found")]
    [InlineData(405, "method_not_allowed")]
    [InlineData(413, "request_too_large")]
    [InlineData(415, "unsupported_media_type")]
    [InlineData(429, "rate_limited")]
    [InlineData(503, "service_unavailable")]
    public async Task Bodyless_errors_use_the_same_contract(int status, string code)
    {
        await using var factory = new FoundationApiFactory();
        using var response = await factory.CreateClient().GetAsync($"/_tests/foundations/status/{status}");
        await AssertProblem(response, status, code);
    }

    [Fact]
    public async Task Validation_has_camel_case_field_errors_without_echoing_payload_values()
    {
        await using var factory = new FoundationApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/_tests/foundations/echo",
            new { name = "sensitive-test-marker", quantity = 0, amount = "12.50", ratio = 0.5m });
        var problem = await AssertProblem(response, 400, "validation_failed");

        Assert.True(problem.GetProperty("errors").TryGetProperty("quantity", out _));
        Assert.DoesNotContain("sensitive-test-marker", problem.GetRawText());
        Assert.DoesNotContain(factory.Logs.Entries, entry => entry.Message.Contains("sensitive-test-marker"));
    }

    [Theory]
    [InlineData("{", null)]
    [InlineData("{}", null)]
    [InlineData("{\"name\":\"x\",\"quantity\":1,\"amount\":\"1.00\",\"unexpected\":true}", null)]
    [InlineData("{\"name\":\"x\",\"quantity\":\"1\",\"amount\":\"1.00\"}", null)]
    [InlineData(null, "12.501")]
    [InlineData(null, "1.000")]
    [InlineData(null, "10000000000000000.00")]
    [InlineData(null, "1e2")]
    [InlineData(null, "-1.00")]
    [InlineData(null, "1,00")]
    public async Task Malformed_json_invalid_money_and_unknown_fields_are_400(string? json, string? money)
    {
        await using var factory = new FoundationApiFactory();
        using var client = factory.CreateClient();
        var body = json ?? JsonSerializer.Serialize(new { name = "x", quantity = 1, amount = money });
        using var response = await client.PostAsync("/_tests/foundations/echo",
            new StringContent(body, Encoding.UTF8, "application/json"));
        var problem = await AssertProblem(response, 400, "validation_failed");
        Assert.NotEmpty(problem.GetProperty("errors").EnumerateObject());
    }

    [Theory]
    [InlineData("12.50")]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("{}")]
    [InlineData("[]")]
    public async Task Money_cannot_be_a_json_number_null_boolean_or_object(string token)
    {
        await using var factory = new FoundationApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsync("/_tests/foundations/echo", new StringContent(
            "{\"name\":\"x\",\"quantity\":1,\"amount\":" + token + "}", Encoding.UTF8, "application/json"));
        await AssertProblem(response, 400, "validation_failed");
    }

    [Theory]
    [InlineData("0", "0.00")]
    [InlineData("12", "12.00")]
    [InlineData("12.5", "12.50")]
    [InlineData("9999999999999999.99", "9999999999999999.99")]
    public async Task Money_roundtrips_as_string_while_unrelated_numbers_stay_numeric(string input, string output)
    {
        await using var factory = new FoundationApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/_tests/foundations/echo",
            new { name = "x", quantity = 2, amount = input, ratio = 1.25m });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(output, json.GetProperty("amount").GetString());
        Assert.Equal(JsonValueKind.Number, json.GetProperty("quantity").ValueKind);
        Assert.Equal(1.25m, json.GetProperty("ratio").GetDecimal());

        // Minimal API JSON options have the same narrow converter as MVC options.
        var options = factory.Services.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions;
        using var minimalJson = JsonDocument.Parse(JsonSerializer.Serialize(
            new { amount = UsdMoney.FromDecimal(12.5m), count = 2, ratio = 1.25m }, options));
        Assert.Equal("12.50", minimalJson.RootElement.GetProperty("amount").GetString());
        Assert.Equal(2, minimalJson.RootElement.GetProperty("count").GetInt32());
        Assert.Equal(1.25m, minimalJson.RootElement.GetProperty("ratio").GetDecimal());
    }

    [Fact]
    public async Task Problem_trace_matches_response_header_and_structured_request_log()
    {
        await using var factory = new FoundationApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/_tests/foundations/business/NotFound?secret=sensitive-test-marker");
        request.Headers.Add("X-Trace-Id", "untrusted-client-marker");
        request.Headers.Add("Cookie", "test=sensitive-test-marker");
        request.Headers.Add("Authorization", "Bearer sensitive-test-marker");
        request.Headers.Add("traceparent", "00-11111111111111111111111111111111-2222222222222222-01");
        using var response = await client.SendAsync(request);
        var problem = await AssertProblem(response, 404, "not_found");
        var trace = problem.GetProperty("traceId").GetString();
        var log = await factory.Logs.RequestCompleted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(trace, log.Fields["TraceId"]);
        Assert.NotEqual("untrusted-client-marker", trace);
        Assert.Equal("11111111111111111111111111111111", trace);
        Assert.Equal("_tests/foundations/business/{error}", log.Fields["Route"]);
        Assert.DoesNotContain(factory.Logs.Entries, entry => entry.Message.Contains("sensitive-test-marker"));
    }

    [Fact]
    public async Task Configured_json_console_does_not_serialize_framework_request_scopes()
    {
        await using var factory = new FoundationApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/health/live");
        var formatter = Assert.Single(factory.Services.GetServices<ConsoleFormatter>(),
            formatter => formatter.Name == ConsoleFormatterNames.Json);
        var scopes = new LoggerExternalScopeProvider();
        using var scope = scopes.Push(new Dictionary<string, object>
        {
            ["RequestPath"] = "/sensitive-test-marker",
            ["Cookie"] = "sensitive-test-marker"
        });
        var entry = new LogEntry<string>(LogLevel.Information, "Test", new EventId(1000),
            "safe request event", null, (state, _) => state);
        using var writer = new StringWriter();

        formatter.Write(in entry, scopes, writer);

        Assert.Contains("safe request event", writer.ToString());
        Assert.DoesNotContain("sensitive-test-marker", writer.ToString());
        Assert.DoesNotContain("RequestPath", writer.ToString());
    }

    [Theory]
    [InlineData("unknown", "Production")]
    [InlineData("database", "Production")]
    [InlineData("cancellation", "Production")]
    [InlineData("unknown", "Development")]
    public async Task Unexpected_infrastructure_and_nonrequest_cancellation_are_sanitized_500(string kind, string environment)
    {
        await using var factory = new FoundationApiFactory(environment);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/_tests/foundations/unexpected/{kind}");
        request.Headers.Accept.ParseAdd("text/html");
        using var response = await client.SendAsync(request);
        var problem = await AssertProblem(response, 500, "internal_error");
        Assert.DoesNotContain("sensitive-test-marker", problem.GetRawText());
        Assert.False(problem.TryGetProperty("exception", out _));
        var log = Assert.Single(factory.Logs.Entries, entry => entry.Event.Id == 1001);
        Assert.Equal(LogLevel.Error, log.Level);
        Assert.Equal(problem.GetProperty("traceId").GetString(), log.Fields["TraceId"]);
        Assert.NotNull(log.Fields["ExceptionType"]);
        Assert.Null(log.Exception);
        Assert.DoesNotContain(factory.Logs.Entries, entry => entry.Message.Contains("sensitive-test-marker")
            || (entry.Exception?.ToString().Contains("sensitive-test-marker") ?? false));
    }

    [Fact]
    public async Task Request_abort_is_observed_deterministically_without_a_business_error()
    {
        await using var factory = new FoundationApiFactory();
        using var client = factory.CreateClient();
        using var cancellation = new CancellationTokenSource();
        var request = client.GetAsync("/_tests/foundations/cancel", cancellation.Token);
        await factory.Cancellation.Reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        var log = await factory.Logs.RequestCompleted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("Aborted", log.Fields["Outcome"]);
        Assert.Equal(499, log.Fields["StatusCode"]);
        Assert.DoesNotContain(factory.Logs.Entries, entry => entry.Event.Id == 1001);
    }

    [Fact]
    public async Task Pagination_defaults_limits_and_binding_errors_use_validation_contract()
    {
        await using var factory = new FoundationApiFactory();
        using var client = factory.CreateClient();
        var page = await client.GetFromJsonAsync<JsonElement>("/_tests/foundations/page");
        Assert.Equal(1, page.GetProperty("page").GetInt32());
        Assert.Equal(20, page.GetProperty("pageSize").GetInt32());
        foreach (var query in new[] { "page=0", "pageSize=101", "page=2147483647&pageSize=100", "page=abc" })
        {
            using var response = await client.GetAsync("/_tests/foundations/page?" + query);
            await AssertProblem(response, 400, "validation_failed");
        }
    }

    [Fact]
    public async Task Nonexistent_and_application_not_found_do_not_reveal_resource_details()
    {
        await using var factory = new FoundationApiFactory();
        using var client = factory.CreateClient();
        using var absentResponse = await client.GetAsync("/absent-sensitive-test-marker");
        using var applicationResponse = await client.GetAsync("/_tests/foundations/business/NotFound");
        var absent = await AssertProblem(absentResponse, 404, "not_found");
        var application = await AssertProblem(applicationResponse, 404, "not_found");
        Assert.Equal(absent.GetProperty("title").GetString(), application.GetProperty("title").GetString());
        Assert.False(application.TryGetProperty("instance", out _));
        Assert.DoesNotContain("sensitive-test-marker", absent.GetRawText());
    }

    [Fact]
    public async Task Openapi_is_development_only_and_documents_error_and_money_shapes()
    {
        await using var development = new FoundationApiFactory("Development");
        var document = await development.CreateClient().GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        Assert.StartsWith("3.1", document.GetProperty("openapi").GetString());
        var operation = document.GetProperty("paths").GetProperty("/api/health/live").GetProperty("get");
        Assert.Equal("GetLiveness", operation.GetProperty("operationId").GetString());
        Assert.True(operation.GetProperty("responses").GetProperty("500").GetProperty("content")
            .TryGetProperty("application/problem+json", out _));
        Assert.Contains("Nonnegative USD money", document.GetRawText());
        Assert.Contains("\"traceId\"", document.GetRawText());
        Assert.Contains("\"code\"", document.GetRawText());

        await using var production = new WebApplicationFactory<Api.Program>()
            .WithWebHostBuilder(builder => builder.UseSetting("environment", "Production"));
        using var productionClient = production.CreateClient();
        using var response = await productionClient.GetAsync("/openapi/v1.json");
        await AssertProblem(response, 404, "not_found");
        using var testRoute = await productionClient.GetAsync("/_tests/foundations/echo");
        await AssertProblem(testRoute, 404, "not_found");
        Assert.Same(TimeProvider.System, production.Services.GetRequiredService<TimeProvider>());
    }

    private static async Task<JsonElement> AssertProblem(HttpResponseMessage response, int status, string code)
    {
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(status, body.GetProperty("status").GetInt32());
        Assert.Equal(code, body.GetProperty("code").GetString());
        Assert.Equal($"urn:orderflow:error:{code}", body.GetProperty("type").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("title").GetString()));
        var trace = body.GetProperty("traceId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(trace));
        Assert.Equal(trace, Assert.Single(response.Headers.GetValues("X-Trace-Id")));
        Assert.Equal(JsonValueKind.Object, body.GetProperty("errors").ValueKind);
        return body;
    }
}
