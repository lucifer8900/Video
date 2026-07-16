using System.Net;
using System.Text;
using System.Text.Json;
using Lingmai.RedMist.Api.Ledger;
using Lingmai.RedMist.Contracts.Ledger;
using Lingmai.RedMist.Generation.Ledger;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Lingmai.RedMist.Api.Tests;

[Collection(ApiProcessCollection.Name)]
public sealed class LedgerEndpointTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 7, 16, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AppendAndReplayReturnExactAcceptedThenDuplicateAcknowledgements()
    {
        var repository = new InMemoryLedgerRepository();
        var service = Service(repository);
        var request = new LedgerEventBatchRequest("1.0.0", [Event("led.ack")]);

        (DefaultHttpContext acceptedContext, JsonDocument accepted) =
            await Append(request, service);
        (DefaultHttpContext duplicateContext, JsonDocument duplicate) =
            await Append(request, service);
        using (accepted)
        using (duplicate)
        {
            Assert.Equal(StatusCodes.Status200OK, acceptedContext.Response.StatusCode);
            Assert.Equal(StatusCodes.Status200OK, duplicateContext.Response.StatusCode);
            Assert.Equal("no-store", acceptedContext.Response.Headers.CacheControl);
            Assert.Equal("no-store", duplicateContext.Response.Headers.CacheControl);
            Assert.Equal(
                ["schemaVersion", "acknowledgements"],
                accepted.RootElement.EnumerateObject().Select(property => property.Name));
            JsonElement acceptedAck = Assert.Single(
                accepted.RootElement.GetProperty("acknowledgements").EnumerateArray());
            JsonElement duplicateAck = Assert.Single(
                duplicate.RootElement.GetProperty("acknowledgements").EnumerateArray());
            Assert.Equal(
                ["playerId", "entryId", "status"],
                acceptedAck.EnumerateObject().Select(property => property.Name));
            Assert.Equal("p.8842", acceptedAck.GetProperty("playerId").GetString());
            Assert.Equal("led.ack", acceptedAck.GetProperty("entryId").GetString());
            Assert.Equal("accepted", acceptedAck.GetProperty("status").GetString());
            Assert.Equal("duplicate", duplicateAck.GetProperty("status").GetString());
        }

        Assert.Equal(1, await repository.CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ConflictingReplayIsSanitized409AndDoesNotPartiallyAppend()
    {
        var repository = new InMemoryLedgerRepository();
        var service = Service(repository);
        await Append(
            new LedgerEventBatchRequest("1.0.0", [Event("led.private", "{\"debtKind\":\"spared_life\"}")]),
            service);
        var conflict = new LedgerEventBatchRequest(
            "1.0.0",
            [
                Event("led.uncommitted"),
                Event("led.private", "{\"debtKind\":\"material_debt\"}")
            ]);

        (DefaultHttpContext context, JsonDocument response) = await Append(conflict, service);
        using (response)
        {
            string raw = response.RootElement.GetRawText();
            Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
            Assert.Equal("ledger.idempotency_conflict", response.RootElement.GetProperty("code").GetString());
            Assert.DoesNotContain("p.8842", raw, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("led.private", raw, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("led.uncommitted", raw, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("material_debt", raw, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("fingerprint", raw, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(1, await repository.CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task InvalidPrivacyPayloadReturnsSanitized400WithoutStorage()
    {
        var repository = new InMemoryLedgerRepository();
        var service = Service(repository);
        var invalid = new LedgerEventBatchRequest(
            "1.0.0",
            [Event("led.invalid", "{\"Display_Text\":\"private dialogue\"}")]);

        (DefaultHttpContext context, JsonDocument response) = await Append(invalid, service);
        using (response)
        {
            string raw = response.RootElement.GetRawText();
            Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
            Assert.Equal("ledger.invalid_request", response.RootElement.GetProperty("code").GetString());
            Assert.DoesNotContain("private dialogue", raw, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Display_Text", raw, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Equal(0, await repository.CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task UndefinedPayloadReturns400InsteadOfEscapingAsServerError()
    {
        var repository = new InMemoryLedgerRepository();
        var service = Service(repository);
        LedgerEventContract missingPayload = Event("led.missing-payload") with
        {
            Payload = default,
        };

        (DefaultHttpContext context, JsonDocument response) = await Append(
            new LedgerEventBatchRequest("1.0.0", [missingPayload]),
            service);
        using (response)
        {
            Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
            Assert.Equal("ledger.invalid_request", response.RootElement.GetProperty("code").GetString());
        }
        Assert.Equal(0, await repository.CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task QueryIsFilteredOrderedAndBoundedByCursor()
    {
        var repository = new InMemoryLedgerRepository();
        var service = Service(repository);
        await Append(
            new LedgerEventBatchRequest(
                "1.0.0",
                [Event("led.c", worldClock: 3), Event("led.a", worldClock: 1), Event("led.b", worldClock: 2)]),
            service);
        await Append(
            new LedgerEventBatchRequest(
                "1.0.0",
                [Event("led.other", playerId: "p.9900", worldClock: 0)]),
            service);

        (DefaultHttpContext firstContext, JsonDocument first) = await Query(
            service,
            "p.8842",
            "chapter.red_mist",
            limit: 2,
            cursor: null);
        string cursor;
        using (first)
        {
            Assert.Equal(StatusCodes.Status200OK, firstContext.Response.StatusCode);
            Assert.Equal("no-store", firstContext.Response.Headers.CacheControl);
            Assert.Equal(
                ["led.c", "led.a"],
                first.RootElement.GetProperty("entries")
                    .EnumerateArray()
                    .Select(entry => entry.GetProperty("event").GetProperty("entryId").GetString()));
            cursor = first.RootElement.GetProperty("nextCursor").GetString()!;
            Assert.NotEmpty(cursor);
        }

        (DefaultHttpContext secondContext, JsonDocument second) = await Query(
            service,
            "p.8842",
            "chapter.red_mist",
            limit: 2,
            cursor);
        using (second)
        {
            Assert.Equal(StatusCodes.Status200OK, secondContext.Response.StatusCode);
            Assert.Equal("no-store", secondContext.Response.Headers.CacheControl);
            Assert.Equal(
                "led.b",
                Assert.Single(second.RootElement.GetProperty("entries").EnumerateArray())
                    .GetProperty("event").GetProperty("entryId").GetString());
            Assert.Equal(JsonValueKind.Null, second.RootElement.GetProperty("nextCursor").ValueKind);
        }
    }

    [Fact]
    public async Task RealHttpRoutesAreWiredWithStrictDefaultLocalStorage()
    {
        await using ApiProcessHarness server = await ApiProcessHarness.StartAsync(
            new Dictionary<string, string> { ["Ledger__Enabled"] = "true" });
        var request = new LedgerEventBatchRequest("1.0.0", [Event("led.http")]);
        string json = JsonSerializer.Serialize(request, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        using HttpResponseMessage append = await server.Client.PostAsync(
            "/api/v1/ledger/events",
            new StringContent(json, Encoding.UTF8, "application/json"));
        string appendJson = await append.Content.ReadAsStringAsync();
        using JsonDocument appendBody = JsonDocument.Parse(appendJson);
        Assert.Equal(HttpStatusCode.OK, append.StatusCode);
        Assert.Equal("no-store", append.Headers.CacheControl?.ToString());
        Assert.Equal(
            "accepted",
            Assert.Single(appendBody.RootElement.GetProperty("acknowledgements").EnumerateArray())
                .GetProperty("status").GetString());

        string unmapped = json[..^1] + ",\"providerApiKey\":\"must-not-leak\"}";
        using HttpResponseMessage rejected = await server.Client.PostAsync(
            "/api/v1/ledger/events",
            new StringContent(unmapped, Encoding.UTF8, "application/json"));
        string rejectedBody = await rejected.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.DoesNotContain("must-not-leak", rejectedBody, StringComparison.Ordinal);

        using HttpResponseMessage query = await server.Client.GetAsync(
            "/api/v1/ledger/events?playerId=p.8842&chapter=chapter.red_mist&limit=10");
        using JsonDocument queryBody = JsonDocument.Parse(await query.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, query.StatusCode);
        Assert.Equal("led.http", Assert.Single(queryBody.RootElement.GetProperty("entries").EnumerateArray())
            .GetProperty("event").GetProperty("entryId").GetString());
    }

    [Fact]
    public async Task DefaultConfigurationDoesNotMapPublicLedgerRoutes()
    {
        await using ApiProcessHarness server = await ApiProcessHarness.StartAsync();
        var request = new LedgerEventBatchRequest("1.0.0", [Event("led.disabled")]);
        string json = JsonSerializer.Serialize(request, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        using HttpResponseMessage append = await server.Client.PostAsync(
            "/api/v1/ledger/events",
            new StringContent(json, Encoding.UTF8, "application/json"));
        using HttpResponseMessage query = await server.Client.GetAsync(
            "/api/v1/ledger/events?playerId=p.8842&chapter=chapter.red_mist");

        Assert.Equal(HttpStatusCode.NotFound, append.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, query.StatusCode);
    }

    [Fact]
    public async Task DisabledPostgresConfigurationDoesNotInitializeDatabaseOrMapRoutes()
    {
        await using ApiProcessHarness server = await ApiProcessHarness.StartAsync(
            new Dictionary<string, string>
            {
                ["Ledger__Enabled"] = "false",
                ["Ledger__Provider"] = "Postgres",
                ["Ledger__ConnectionString"] =
                    "Host=127.0.0.1;Port=1;Database=unreachable;Username=none;Timeout=1",
            });

        using HttpResponseMessage response = await server.Client.GetAsync(
            "/api/v1/ledger/events?playerId=p.8842&chapter=chapter.red_mist");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task TombstonedPlayerOfflineRetryReturnsSanitizedGone()
    {
        var repository = new InMemoryLedgerRepository();
        var service = Service(repository);
        await repository.DeletePlayerAsync("p.8842", CancellationToken.None);

        (DefaultHttpContext context, JsonDocument response) = await Append(
            new LedgerEventBatchRequest("1.0.0", [Event("led.offline-private")]),
            service);
        using (response)
        {
            string raw = response.RootElement.GetRawText();
            Assert.Equal(StatusCodes.Status410Gone, context.Response.StatusCode);
            Assert.Equal("ledger.player_deleted", response.RootElement.GetProperty("code").GetString());
            Assert.DoesNotContain("p.8842", raw, StringComparison.Ordinal);
            Assert.DoesNotContain("led.offline-private", raw, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task UnknownLedgerProviderStopsBeforeListening()
    {
        ApiProcessExitResult result = await ApiProcessHarness.RunToExitAsync(
            new Dictionary<string, string>
            {
                ["Ledger__Enabled"] = "false",
                ["Ledger__Provider"] = "CloudMagic",
            },
            TimeSpan.FromSeconds(5));

        Assert.False(result.TimedOut);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Ledger:Provider", result.Logs, StringComparison.Ordinal);
        Assert.DoesNotContain("Now listening", result.Logs, StringComparison.OrdinalIgnoreCase);
    }

    private static LedgerService Service(ILedgerRepository repository) =>
        new(repository, new FixedTimeProvider(FixedNow));

    private static async Task<(DefaultHttpContext Context, JsonDocument Response)> Append(
        LedgerEventBatchRequest request,
        LedgerService service)
    {
        DefaultHttpContext context = Context();
        IResult result = await LedgerEndpoints.HandleAppendAsync(
            context,
            request,
            service,
            CancellationToken.None);
        await result.ExecuteAsync(context);
        return (context, Response(context));
    }

    private static async Task<(DefaultHttpContext Context, JsonDocument Response)> Query(
        LedgerService service,
        string playerId,
        string chapter,
        int limit,
        string? cursor)
    {
        DefaultHttpContext context = Context();
        IResult result = await LedgerEndpoints.HandleQueryAsync(
            context,
            playerId,
            chapter,
            limit,
            cursor,
            service,
            CancellationToken.None);
        await result.ExecuteAsync(context);
        return (context, Response(context));
    }

    private static LedgerEventContract Event(
        string entryId,
        string payload = "{\"debtKind\":\"spared_life\"}",
        string playerId = "p.8842",
        long worldClock = 1440)
    {
        using JsonDocument document = JsonDocument.Parse(payload);
        return new LedgerEventContract(
            "1.0.0",
            entryId,
            playerId,
            "debt_incurred",
            ["char.shen_yan", "npc.shi_jun"],
            3,
            "chapter.red_mist",
            worldClock,
            "rescue",
            ["fact.shijun_alive"],
            document.RootElement.Clone());
    }

    private static DefaultHttpContext Context()
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        };
        context.TraceIdentifier = "cx401-ledger-test";
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static JsonDocument Response(DefaultHttpContext context)
    {
        context.Response.Body.Position = 0;
        return JsonDocument.Parse(context.Response.Body);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
