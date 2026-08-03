using System.Collections.ObjectModel;
using Lingmai.RedMist.Contracts.Associations;
using Lingmai.RedMist.Generation.Associations;

namespace Lingmai.RedMist.Generation.Tests;

public sealed class AssociationAuditRepositoryTests
{
    [Fact]
    public async Task StoreDeepFreezesDataAndDeletePlayerOnlyRemovesMatchingAudits()
    {
        var repository = new InMemoryAssociationDecisionAuditRepository();
        var mutableParameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["target"] = "npc.known",
        };
        AssociationDecisionAuditContract first = Audit(
            "genjob.audit.player_one.1",
            "thr.audit.player_one.1",
            "p.player_one",
            mutableParameters);
        AssociationDecisionAuditContract second = Audit(
            "genjob.audit.player_two.1",
            "thr.audit.player_two.1",
            "p.player_two",
            new Dictionary<string, string>(StringComparer.Ordinal));

        await repository.StoreAsync(first, CancellationToken.None);
        await repository.StoreAsync(second, CancellationToken.None);
        mutableParameters["target"] = "npc.mutated";

        AssociationDecisionAuditContract? persisted = await repository.GetAsync(
            first.AuditRef,
            CancellationToken.None);
        Assert.NotNull(persisted);
        Assert.Equal("npc.known", persisted!.ResolvedParams["target"]);
        Assert.IsType<ReadOnlyDictionary<string, string>>(persisted.ResolvedParams);

        Assert.Equal(1, await repository.DeletePlayerAsync(
            "p.player_one",
            CancellationToken.None));
        Assert.Null(await repository.GetAsync(first.AuditRef, CancellationToken.None));
        Assert.NotNull(await repository.GetAsync(second.AuditRef, CancellationToken.None));
    }

    [Fact]
    public async Task ExactReplayIsIdempotentButConflictingAuditRefFailsClosed()
    {
        var repository = new InMemoryAssociationDecisionAuditRepository();
        AssociationDecisionAuditContract original = Audit(
            "genjob.audit.same.1",
            "thr.audit.same.1",
            "p.same",
            new Dictionary<string, string>());
        AssociationDecisionAuditContract conflict = original with
        {
            ThreadId = "thr.audit.conflict.1",
        };

        await repository.StoreAsync(original, CancellationToken.None);
        await repository.StoreAsync(original, CancellationToken.None);

        await Assert.ThrowsAsync<AssociationDecisionAuditConflictException>(async () =>
            await repository.StoreAsync(conflict, CancellationToken.None));
        Assert.Equal(1, repository.Count);
    }

    private static AssociationDecisionAuditContract Audit(
        string auditRef,
        string threadId,
        string playerId,
        IReadOnlyDictionary<string, string> parameters) => new(
        "1.0.0",
        auditRef,
        threadId,
        $"sha256:{new string('a', 64)}",
        AssociationAuditHash.ComputePlayerRefHash(playerId),
        "chapter.red_mist",
        "assoc.enemy.echo.v1",
        parameters,
        new[]
        {
            new AssociationProviderAuditContract(
                "ranker",
                "mock.association-ranker",
                "prompt.association-ranker.mock.v1",
                "model.association-ranker.mock.v1",
                true),
        },
        new[]
        {
            new AssociationGuardResultAuditContract(
                "deterministic",
                "passed",
                "none",
                "snapshot.audit.1"),
        },
        1,
        false,
        null,
        5,
        new DateTimeOffset(2026, 7, 16, 12, 0, 0, TimeSpan.Zero));
}
