using Lingmai.RedMist.Generation.Budget;

namespace Lingmai.RedMist.Generation.Tests;

public sealed class BudgetReservationCoreTests
{
    [Fact]
    public void MoneyAcceptsKnownIso4217CurrenciesAndUsesValueEquality()
    {
        Assert.Equal(new Money(1_000_000, "CNY"), new Money(1_000_000, "CNY"));
        Assert.NotEqual(new Money(1_000_000, "CNY"), new Money(1_000_000, "USD"));
        Assert.Equal(0, Money.Zero("CNY").Micros);
    }

    [Theory]
    [InlineData("cny")]
    [InlineData("CN")]
    [InlineData("CNY ")]
    [InlineData("ZZZ")]
    [InlineData("")]
    public void MoneyRejectsNonIsoCurrencyCodes(string currency)
    {
        Assert.Throws<ArgumentException>(() => new Money(1, currency));
    }

    [Fact]
    public void MoneyRejectsNegativeValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Money(-1, "CNY"));
    }

    [Fact]
    public void MoneyArithmeticIsCheckedAndCurrencySafe()
    {
        Assert.Equal(
            new Money(7, "CNY"),
            Money.Add(new Money(3, "CNY"), new Money(4, "CNY")));
        Assert.Throws<OverflowException>(
            () => Money.Add(new Money(long.MaxValue, "CNY"), new Money(1, "CNY")));
        Assert.Throws<CurrencyMismatchException>(
            () => Money.Add(new Money(1, "CNY"), new Money(1, "USD")));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Money.Subtract(new Money(1, "CNY"), new Money(2, "CNY")));
    }

    [Fact]
    public void ContextResolvesAllFiveServerScopeKeysIncludingUtcDay()
    {
        BudgetContext context = BudgetTestData.Context;

        Assert.Equal(
            new BudgetScope(BudgetScopeKind.Account, "account:test-account"),
            BudgetScope.Resolve(BudgetScopeKind.Account, context));
        Assert.Equal(
            new BudgetScope(BudgetScopeKind.Device, "device:test-device"),
            BudgetScope.Resolve(BudgetScopeKind.Device, context));
        Assert.Equal(
            new BudgetScope(BudgetScopeKind.Chapter, "chapter:test-chapter"),
            BudgetScope.Resolve(BudgetScopeKind.Chapter, context));
        Assert.Equal(
            new BudgetScope(BudgetScopeKind.Daily, "daily:test-project:2026-07-16"),
            BudgetScope.Resolve(BudgetScopeKind.Daily, context));
        Assert.Equal(
            new BudgetScope(BudgetScopeKind.Project, "project:test-project"),
            BudgetScope.Resolve(BudgetScopeKind.Project, context));
    }

    [Theory]
    [InlineData("", "device", "chapter", "project")]
    [InlineData("account", " ", "chapter", "project")]
    [InlineData("account", "device", "", "project")]
    [InlineData("account", "device", "chapter", "\t")]
    public void ContextRejectsBlankServerKeys(
        string account,
        string device,
        string chapter,
        string project)
    {
        Assert.Throws<ArgumentException>(
            () => new BudgetContext(
                account,
                device,
                chapter,
                project,
                new DateOnly(2026, 7, 16)));
    }
}

internal static class BudgetTestData
{
    public static readonly BudgetContext Context = new(
        "test-account",
        "test-device",
        "test-chapter",
        "test-project",
        new DateOnly(2026, 7, 16));

    public static readonly BudgetScopeKind[] AllScopeKinds =
        Enum.GetValues<BudgetScopeKind>();

    public static InMemoryBudgetRepository RepositoryWithLimits(
        long limitMicros,
        BudgetScopeKind? omitted = null,
        BudgetScopeKind? constrained = null,
        long? constrainedLimitMicros = null,
        string currency = "CNY")
    {
        var repository = new InMemoryBudgetRepository();
        foreach (BudgetScopeKind kind in AllScopeKinds)
        {
            if (kind == omitted) continue;
            long limit = kind == constrained
                ? constrainedLimitMicros ?? limitMicros
                : limitMicros;
            repository.ConfigureLimit(
                BudgetScope.Resolve(kind, Context),
                new Money(limit, currency));
        }

        return repository;
    }

    public static BudgetReservationRequest Request(
        string idempotencyKey = "budget-request-1",
        string? fingerprint = null,
        long maximumCostMicros = 10,
        string currency = "CNY") =>
        new(
            idempotencyKey,
            fingerprint ?? Fingerprint(1),
            Context,
            new Money(maximumCostMicros, currency),
            "test.price.v1",
            "test.provider.video",
            "test.model.snapshot.v1");

    public static string Fingerprint(int value) =>
        "sha256:" + value.ToString("x64", System.Globalization.CultureInfo.InvariantCulture);
}
