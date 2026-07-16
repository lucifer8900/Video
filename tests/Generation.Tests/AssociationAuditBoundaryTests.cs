using System.Text.RegularExpressions;
using Lingmai.RedMist.Contracts.Associations;
using Lingmai.RedMist.Generation.Associations;

namespace Lingmai.RedMist.Generation.Tests;

public sealed class AssociationAuditBoundaryTests
{
    private const string RelativeMigrationPath =
        "server/Generation/Persistence/Migrations/006_association_generation_audits.sql";

    [Fact]
    public void InputHashDistinguishesLedgerActorOrder()
    {
        AssociationDecisionRequest original = AssociationDecisionServiceTests.Request();
        AssociationDecisionRequest reordered = original with
        {
            LedgerEntries = original.LedgerEntries
                .Select(entry => entry with
                {
                    Event = entry.Event with
                    {
                        Actors = entry.Event.Actors.Reverse().ToArray(),
                    },
                })
                .ToArray(),
        };
        ApprovedAssociationTemplate[] templates =
            [AssociationDecisionServiceTests.ApprovedTemplate()];

        string originalHash = AssociationAuditHash.ComputeInputHash(original, templates);
        string reorderedHash = AssociationAuditHash.ComputeInputHash(reordered, templates);

        Assert.NotEqual(originalHash, reorderedHash);
    }

    [Fact]
    public void InputHashDistinguishesRuntimeGenerationPolicy()
    {
        AssociationDecisionRequest reuseOnly = AssociationDecisionServiceTests.Request() with
        {
            RuntimePolicy = AssociationRuntimePolicy.CreateForTests(false),
        };
        AssociationDecisionRequest runtimeGenerationAllowed = reuseOnly with
        {
            RuntimePolicy = AssociationRuntimePolicy.CreateForTests(true),
        };
        ApprovedAssociationTemplate[] templates =
            [AssociationDecisionServiceTests.ApprovedTemplate()];

        string reuseOnlyHash = AssociationAuditHash.ComputeInputHash(reuseOnly, templates);
        string runtimeAllowedHash = AssociationAuditHash.ComputeInputHash(
            runtimeGenerationAllowed,
            templates);

        Assert.NotEqual(reuseOnlyHash, runtimeAllowedHash);
    }

    [Fact]
    public async Task DeletePlayerPermanentlySuppressesLaterInMemoryAuditStores()
    {
        const string playerId = "p.audit_deleted";
        var repository = new InMemoryAssociationDecisionAuditRepository();
        AssociationDecisionAuditContract original = Audit(
            "genjob.audit.deleted.1",
            "thr.audit.deleted.1",
            playerId);
        AssociationDecisionAuditContract attemptedResurrection = Audit(
            "genjob.audit.deleted.2",
            "thr.audit.deleted.2",
            playerId);

        await repository.StoreAsync(original, CancellationToken.None);
        Assert.Equal(1, await repository.DeletePlayerAsync(playerId, CancellationToken.None));

        Exception? exception = await Record.ExceptionAsync(async () =>
            await repository.StoreAsync(attemptedResurrection, CancellationToken.None));

        Assert.NotNull(exception);
        Assert.Equal("AssociationDecisionAuditSuppressedException", exception!.GetType().Name);
        Assert.Equal(0, repository.Count);
        Assert.Null(await repository.GetAsync(
            attemptedResurrection.AuditRef,
            CancellationToken.None));
    }

    [Fact]
    public void MigrationRetainsPlayerTombstonesUnderEquivalentPerPlayerSerialization()
    {
        string sql = ReadMigration();
        string repository = ReadRepositoryFile(
            "server/Generation/Persistence/PostgresAssociationDecisionAuditRepository.cs");
        bool hasPerPlayerSerialization =
            repository.Contains("pg_advisory_xact_lock", StringComparison.OrdinalIgnoreCase) ||
            sql.Contains(
                "lock table association_audit_player_tombstones",
                StringComparison.OrdinalIgnoreCase) ||
            Regex.IsMatch(
                sql,
                @"association_audit_player_tombstones[\s\S]*?for\s+(?:no\s+key\s+)?update",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        Assert.Contains(
            "association_audit_player_tombstones",
            sql,
            StringComparison.OrdinalIgnoreCase);
        Assert.Matches(
            new Regex(
                @"player_ref_hash\s+text\s+(?:not\s+null\s+)?primary\s+key",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            sql);
        Assert.True(
            hasPerPlayerSerialization,
            "Player deletion and audit storage must serialize on the same player hash.");
    }

    [Fact]
    public void MigrationUsesTheSame64KiBTotalAuditJsonBudgetAsRuntime()
    {
        string sql = ReadMigration();

        Assert.Contains("ck_association_audit_json_size", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("octet_length(guard_results::text)", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<= 65536", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "octet_length(guard_results::text) <= 32768",
            sql,
            StringComparison.OrdinalIgnoreCase);
    }

    private static AssociationDecisionAuditContract Audit(
        string auditRef,
        string threadId,
        string playerId) => new(
        "1.0.0",
        auditRef,
        threadId,
        $"sha256:{new string('a', 64)}",
        AssociationAuditHash.ComputePlayerRefHash(playerId),
        "chapter.red_mist",
        "assoc.enemy.echo.v1",
        new Dictionary<string, string>(StringComparer.Ordinal),
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
                "snapshot.audit.boundary.1"),
        },
        1,
        false,
        null,
        5,
        new DateTimeOffset(2026, 7, 16, 12, 0, 0, TimeSpan.Zero));

    private static string ReadMigration()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null &&
               !File.Exists(Path.Combine(current.FullName, "Video.sln")))
        {
            current = current.Parent;
        }

        if (current is null)
            throw new DirectoryNotFoundException("Could not locate the Video repository root.");
        return File.ReadAllText(Path.Combine(
            current.FullName,
            RelativeMigrationPath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null &&
               !File.Exists(Path.Combine(current.FullName, "Video.sln")))
        {
            current = current.Parent;
        }

        if (current is null)
            throw new DirectoryNotFoundException("Could not locate the Video repository root.");
        return File.ReadAllText(Path.Combine(
            current.FullName,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }
}
