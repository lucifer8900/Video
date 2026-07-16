using System.Text.Json;
using Lingmai.RedMist.Generation.Ledger;

namespace Lingmai.RedMist.Generation.Tests;

public sealed class LedgerRepositoryTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 7, 16, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task SamePlayerAndEntryWithSameCanonicalPayloadIsIdempotent()
    {
        var repository = new InMemoryLedgerRepository();
        var service = Service(repository);
        LedgerEventData first = Event(
            "led.same",
            type: "item_gained",
            payload: "{\"itemRef\":\"item.spirit_herb\",\"quantity\":1}");
        LedgerEventData reordered = Event(
            "led.same",
            type: "item_gained",
            payload: "{\"quantity\":1,\"itemRef\":\"item.spirit_herb\"}");

        IReadOnlyList<LedgerAppendResult> accepted =
            await service.AppendAsync([first], CancellationToken.None);
        IReadOnlyList<LedgerAppendResult> duplicate =
            await service.AppendAsync([reordered], CancellationToken.None);

        Assert.Equal(LedgerAppendDisposition.Accepted, Assert.Single(accepted).Disposition);
        Assert.Equal(LedgerAppendDisposition.Duplicate, Assert.Single(duplicate).Disposition);
        Assert.Equal(1, await repository.CountAsync(CancellationToken.None));
        Assert.Equal(FixedNow, Assert.Single(duplicate).Entry.ReceivedAtUtc);
    }

    [Fact]
    public async Task DifferentPayloadForSamePlayerAndEntryIsConflictAndBatchIsAtomic()
    {
        var repository = new InMemoryLedgerRepository();
        var service = Service(repository);
        await service.AppendAsync(
            [Event("led.existing", payload: "{\"debtKind\":\"spared_life\"}")],
            CancellationToken.None);

        await Assert.ThrowsAsync<LedgerIdempotencyConflictException>(() => service.AppendAsync(
            [
                Event("led.new"),
                Event("led.existing", payload: "{\"debtKind\":\"material_debt\"}"),
            ],
            CancellationToken.None));

        Assert.Equal(1, await repository.CountAsync(CancellationToken.None));
        NarrativeLedgerPage page = await service.QueryAsync(
            "p.8842",
            "chapter.red_mist",
            100,
            cursor: null,
            CancellationToken.None);
        Assert.Equal("led.existing", Assert.Single(page.Entries).Event.EntryId);
    }

    [Fact]
    public async Task ConcurrentReplayAcceptsExactlyOnce()
    {
        var repository = new InMemoryLedgerRepository();
        var service = Service(repository);

        LedgerAppendResult[] results = await Task.WhenAll(
            Enumerable.Range(0, 24).Select(async _ => Assert.Single(
                await service.AppendAsync([Event("led.concurrent")], CancellationToken.None))));

        Assert.Equal(1, results.Count(result => result.Disposition == LedgerAppendDisposition.Accepted));
        Assert.Equal(23, results.Count(result => result.Disposition == LedgerAppendDisposition.Duplicate));
        Assert.Equal(1, await repository.CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task QueryFiltersAndUsesServerIngestOrderWithBoundedOpaqueCursor()
    {
        var repository = new InMemoryLedgerRepository();
        var service = Service(repository);
        await service.AppendAsync(
            [
                Event("led.c", worldClock: 30),
                Event("led.a", worldClock: 10),
                Event("led.b", worldClock: 10),
                Event("led.other-chapter", chapter: "chapter.qifeng_valley", worldClock: 5),
            ],
            CancellationToken.None);
        await service.AppendAsync(
            [Event("led.other-player", playerId: "p.9900", worldClock: 1)],
            CancellationToken.None);

        NarrativeLedgerPage first = await service.QueryAsync(
            "p.8842", "chapter.red_mist", 2, cursor: null, CancellationToken.None);
        Assert.Equal(["led.c", "led.a"], first.Entries.Select(entry => entry.Event.EntryId));
        Assert.NotNull(first.NextCursor);
        Assert.DoesNotContain("led.b", first.NextCursor, StringComparison.Ordinal);

        NarrativeLedgerPage second = await service.QueryAsync(
            "p.8842", "chapter.red_mist", 2, first.NextCursor, CancellationToken.None);
        Assert.Equal("led.b", Assert.Single(second.Entries).Event.EntryId);
        Assert.Null(second.NextCursor);
    }

    [Fact]
    public async Task LateOfflineEventWithLowerWorldClockAppearsAfterExistingCursor()
    {
        var repository = new InMemoryLedgerRepository();
        var service = Service(repository);
        await service.AppendAsync(
            [Event("led.first", worldClock: 100), Event("led.second", worldClock: 200)],
            CancellationToken.None);
        NarrativeLedgerPage first = await service.QueryAsync(
            "p.8842", "chapter.red_mist", 1, null, CancellationToken.None);
        Assert.Equal("led.first", Assert.Single(first.Entries).Event.EntryId);
        Assert.NotNull(first.NextCursor);

        await service.AppendAsync([Event("led.late", worldClock: 1)], CancellationToken.None);
        NarrativeLedgerPage next = await service.QueryAsync(
            "p.8842", "chapter.red_mist", 10, first.NextCursor, CancellationToken.None);

        Assert.Equal(
            ["led.second", "led.late"],
            next.Entries.Select(entry => entry.Event.EntryId));
    }

    [Fact]
    public async Task ServiceRejectsMixedPlayersDuplicateKeysAndNestedPayloads()
    {
        var repository = new InMemoryLedgerRepository();
        var service = Service(repository);

        await Assert.ThrowsAsync<LedgerValidationException>(() => service.AppendAsync(
            [Event("led.one"), Event("led.two", playerId: "p.9900")],
            CancellationToken.None));
        await Assert.ThrowsAsync<LedgerValidationException>(() => service.AppendAsync(
            [Event("led.one"), Event("led.one")],
            CancellationToken.None));
        await Assert.ThrowsAsync<LedgerValidationException>(() => service.AppendAsync(
            [Event("led.nested", payload: "{\"unsafe\":{\"recording\":true}}")],
            CancellationToken.None));

        Assert.Equal(0, await repository.CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ServiceRejectsPrivacyKeysAndOversizedPayloadIndependentlyOfSchema()
    {
        var repository = new InMemoryLedgerRepository();
        var service = Service(repository);
        string oversized = "{" + string.Join(
            ",",
            Enumerable.Range(0, 10).Select(index =>
                $"\"safe{index}\":\"{new string('x', 500)}\"")) + "}";

        await Assert.ThrowsAsync<LedgerValidationException>(() => service.AppendAsync(
            [Event("led.transcript", payload: "{\"TrAnScRiPt\":\"private words\"}")],
            CancellationToken.None));
        await Assert.ThrowsAsync<LedgerValidationException>(() => service.AppendAsync(
            [Event("led.device", payload: "{\"deviceInfo\":\"private\"}")],
            CancellationToken.None));
        await Assert.ThrowsAsync<LedgerValidationException>(() => service.AppendAsync(
            [Event("led.oversized", payload: oversized)],
            CancellationToken.None));

        Assert.Equal(0, await repository.CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ActorAndFactIdentityUniquenessUsesOrdinalSchemaSemantics()
    {
        var repository = new InMemoryLedgerRepository();
        var service = Service(repository);
        LedgerEventData actorDuplicate = Event("led.actor") with
        {
            Actors = ["npc.Shi_Jun", "npc.shi_jun"],
        };
        LedgerEventData factDuplicate = Event("led.fact") with
        {
            FactRefs = ["fact.ShiJun_Alive", "fact.shijun_alive"],
        };

        await service.AppendAsync([actorDuplicate], CancellationToken.None);
        await service.AppendAsync([factDuplicate], CancellationToken.None);
        Assert.Equal(2, await repository.CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task PayloadPolicyAllowsOnlyPerEventTypedKeysAndNeverFreeText()
    {
        var repository = new InMemoryLedgerRepository();
        var service = Service(repository);
        LedgerEventData[] valid =
        [
            Event("led.debt-in", type: "debt_incurred", payload: "{\"debtKind\":\"spared_life\"}"),
            Event("led.debt-out", type: "debt_repaid", payload: "{\"debtRef\":\"led.debt-in\"}"),
            Event("led.secret", type: "secret_exposed", payload: "{\"secretRef\":\"secret.hidden_gate\"}"),
            Event("led.promise-made", type: "promise_made", payload: "{\"promiseRef\":\"promise.return\"}"),
            Event("led.promise-broken", type: "promise_broken", payload: "{\"promiseRef\":\"promise.return\"}"),
            Event("led.rescued", type: "npc_rescued", payload: "{\"npcRef\":\"npc.shi_jun\"}"),
            Event("led.abandoned", type: "npc_abandoned", payload: "{\"npcRef\":\"npc.shi_jun\"}"),
            Event("led.spared", type: "enemy_spared", payload: "{\"npcRef\":\"npc.shi_jun\"}"),
            Event("led.item", type: "item_gained", payload: "{\"itemRef\":\"item.spirit_herb\",\"quantity\":2}"),
            Event("led.quest", type: "quest_expired", payload: "{\"questRef\":\"quest.mist_gate\"}"),
            Event("led.trump", type: "trump_card_revealed", payload: "{\"abilityRef\":\"ability.sword_light\"}"),
        ];

        await service.AppendAsync(valid, CancellationToken.None);
        await Assert.ThrowsAsync<LedgerValidationException>(() => service.AppendAsync(
            [Event("led.note", payload: "{\"note\":\"full transcript hidden here\"}")],
            CancellationToken.None));
        await Assert.ThrowsAsync<LedgerValidationException>(() => service.AppendAsync(
            [Event("led.wrong-key", type: "item_gained", payload: "{\"debtKind\":\"spared_life\"}")],
            CancellationToken.None));
        await Assert.ThrowsAsync<LedgerValidationException>(() => service.AppendAsync(
            [Event("led.free-text", type: "secret_exposed", payload: "{\"secretRef\":\"secrets contain spaces\"}")],
            CancellationToken.None));
        Assert.Equal(11, await repository.CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task IntegerPayloadFingerprintCanonicalizesEquivalentJsonNumbers()
    {
        var repository = new InMemoryLedgerRepository();
        var service = Service(repository);
        LedgerEventData integer = Event(
            "led.number",
            type: "item_gained",
            payload: "{\"quantity\":1}");
        LedgerEventData decimalForm = Event(
            "led.number",
            type: "item_gained",
            payload: "{\"quantity\":1.0}");
        LedgerEventData exponentForm = Event(
            "led.number",
            type: "item_gained",
            payload: "{\"quantity\":1e0}");

        Assert.Equal(
            LedgerAppendDisposition.Accepted,
            Assert.Single(await service.AppendAsync([integer], CancellationToken.None)).Disposition);
        Assert.Equal(
            LedgerAppendDisposition.Duplicate,
            Assert.Single(await service.AppendAsync([decimalForm], CancellationToken.None)).Disposition);
        Assert.Equal(
            LedgerAppendDisposition.Duplicate,
            Assert.Single(await service.AppendAsync([exponentForm], CancellationToken.None)).Disposition);
    }

    [Fact]
    public async Task CursorCannotBeReusedForAnotherPlayerOrChapter()
    {
        var repository = new InMemoryLedgerRepository();
        var service = Service(repository);
        await service.AppendAsync(
            [Event("led.a", worldClock: 1), Event("led.b", worldClock: 2)],
            CancellationToken.None);
        NarrativeLedgerPage page = await service.QueryAsync(
            "p.8842", "chapter.red_mist", 1, null, CancellationToken.None);

        await Assert.ThrowsAsync<LedgerValidationException>(() => service.QueryAsync(
            "p.9900", "chapter.red_mist", 1, page.NextCursor, CancellationToken.None));
        await Assert.ThrowsAsync<LedgerValidationException>(() => service.QueryAsync(
            "p.8842", "chapter.qifeng_valley", 1, page.NextCursor, CancellationToken.None));
        await Assert.ThrowsAsync<LedgerValidationException>(() => service.QueryAsync(
            "p.8842", "chapter.red_mist", 101, null, CancellationToken.None));
    }

    [Fact]
    public async Task MaximumLegalPlayerAndChapterProduceCursorWithinContractBound()
    {
        string playerId = "p." + new string('a', 94);
        string chapter = "c" + new string('b', 63);
        var repository = new InMemoryLedgerRepository();
        var service = Service(repository);
        await service.AppendAsync(
            [
                Event("led." + new string('c', 92), playerId, chapter, 1),
                Event("led." + new string('d', 92), playerId, chapter, 2),
            ],
            CancellationToken.None);

        NarrativeLedgerPage first = await service.QueryAsync(
            playerId, chapter, 1, null, CancellationToken.None);
        Assert.NotNull(first.NextCursor);
        Assert.InRange(first.NextCursor!.Length, 1, 256);
        NarrativeLedgerPage second = await service.QueryAsync(
            playerId, chapter, 1, first.NextCursor, CancellationToken.None);
        Assert.Single(second.Entries);
    }

    [Fact]
    public async Task DeletePlayerTombstonesTargetAndOfflineRetryCannotResurrect()
    {
        var repository = new InMemoryLedgerRepository();
        var service = Service(repository);
        await service.AppendAsync([Event("led.first")], CancellationToken.None);
        await service.AppendAsync(
            [Event("led.other", playerId: "p.9900")],
            CancellationToken.None);

        await repository.DeletePlayerAsync("p.8842", CancellationToken.None);

        Assert.Equal(1, await repository.CountAsync(CancellationToken.None));
        NarrativeLedgerPage deleted = await service.QueryAsync(
            "p.8842", "chapter.red_mist", 100, null, CancellationToken.None);
        NarrativeLedgerPage retained = await service.QueryAsync(
            "p.9900", "chapter.red_mist", 100, null, CancellationToken.None);
        Assert.Empty(deleted.Entries);
        Assert.Equal("led.other", Assert.Single(retained.Entries).Event.EntryId);
        await Assert.ThrowsAsync<LedgerPlayerDeletedException>(() => service.AppendAsync(
            [Event("led.offline-retry")],
            CancellationToken.None));
        Assert.Equal(1, await repository.CountAsync(CancellationToken.None));
    }

    private static LedgerService Service(ILedgerRepository repository) =>
        new(repository, new FixedTimeProvider(FixedNow));

    private static LedgerEventData Event(
        string entryId,
        string playerId = "p.8842",
        string chapter = "chapter.red_mist",
        long worldClock = 1440,
        string type = "debt_incurred",
        string payload = "{\"debtKind\":\"spared_life\"}") =>
        new(
            "1.0.0",
            entryId,
            playerId,
            type,
            ["char.shen_yan", "npc.shi_jun"],
            3,
            chapter,
            worldClock,
            "rescue",
            ["fact.shijun_alive"],
            Parse(payload));

    private static JsonElement Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
