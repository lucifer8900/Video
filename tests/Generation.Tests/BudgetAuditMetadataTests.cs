using Lingmai.RedMist.Generation.Budget;
using Lingmai.RedMist.Generation.Tests.TestDoubles;

namespace Lingmai.RedMist.Generation.Tests;

public sealed class BudgetAuditMetadataTests
{
    [Theory]
    [InlineData("", "test.provider", "test.model")]
    [InlineData("test.price.v1", "", "test.model")]
    [InlineData("test.price.v1", "test.provider", "")]
    public async Task ReservationRejectsMissingCostAuditMetadata(
        string priceVersion,
        string providerKey,
        string modelSnapshot)
    {
        var repository = new InMemoryBudgetRepository();
        await repository.ConfigureLimitsAsync(
            SettlementBudgetTestData.Context(),
            SettlementBudgetTestData.UniformPolicy(1000),
            SettlementBudgetTestData.FixedNow,
            CancellationToken.None);
        BudgetReservationRequest request = SettlementBudgetTestData.Request(
            "audit-metadata-required",
            100,
            200) with
        {
            PriceVersion = priceVersion,
            ProviderKey = providerKey,
            ModelSnapshot = modelSnapshot,
        };

        await Assert.ThrowsAsync<ArgumentException>(
            () => repository.ReserveAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task ReservationPreservesCostAuditMetadata()
    {
        var repository = new InMemoryBudgetRepository();
        await repository.ConfigureLimitsAsync(
            SettlementBudgetTestData.Context(),
            SettlementBudgetTestData.UniformPolicy(1000),
            SettlementBudgetTestData.FixedNow,
            CancellationToken.None);
        BudgetReservationRequest request = SettlementBudgetTestData.Request(
            "audit-metadata-roundtrip",
            100,
            200);

        BudgetReserveDecision decision = await repository.ReserveAsync(
            request,
            CancellationToken.None);

        BudgetReservation reservation = Assert.IsType<BudgetReservation>(decision.Reservation);
        Assert.Equal(request.PriceVersion, reservation.PriceVersion);
        Assert.Equal(request.ProviderKey, reservation.ProviderKey);
        Assert.Equal(request.ModelSnapshot, reservation.ModelSnapshot);
    }
}
