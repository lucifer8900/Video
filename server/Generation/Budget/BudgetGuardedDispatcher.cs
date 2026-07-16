using System.Collections.Concurrent;

namespace Lingmai.RedMist.Generation.Budget;

public sealed class BudgetGuardedDispatcher
{
    private readonly IBudgetRepository _repository;
    private readonly string _fallbackMediaRef;
    private readonly ConcurrentDictionary<Guid, Lazy<Task<object>>> _dispatches = new();

    public BudgetGuardedDispatcher(IBudgetRepository repository, string fallbackMediaRef)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        ArgumentException.ThrowIfNullOrWhiteSpace(fallbackMediaRef);
        _fallbackMediaRef = fallbackMediaRef;
    }

    public async Task<BudgetDispatchResult<T>> DispatchAsync<T>(
        BudgetReservationRequest request,
        Func<BudgetReservation, CancellationToken, Task<BudgetProviderResult<T>>> providerCall,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(providerCall);

        BudgetReserveDecision decision;
        try
        {
            decision = await _repository
                .ReserveAsync(request, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Fallback<T>("budget.unavailable");
        }

        if (!decision.Admitted)
            return Fallback<T>("budget.exceeded");
        if (decision.Reservation is null)
            return Fallback<T>("budget.unavailable");

        BudgetReservation reservation = decision.Reservation;
        var candidate = new Lazy<Task<object>>(
            () => DispatchOnceAsync(
                reservation,
                providerCall,
                cancellationToken),
            LazyThreadSafetyMode.ExecutionAndPublication);
        Lazy<Task<object>> shared = _dispatches.GetOrAdd(reservation.Id, candidate);

        try
        {
            object result = await shared.Value.ConfigureAwait(false);
            return result is BudgetDispatchResult<T> typed
                ? typed
                : Fallback<T>("budget.unavailable");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Fallback<T>("budget.unavailable");
        }
    }

    private async Task<object> DispatchOnceAsync<T>(
        BudgetReservation reservation,
        Func<BudgetReservation, CancellationToken, Task<BudgetProviderResult<T>>> providerCall,
        CancellationToken cancellationToken)
    {
        bool claimed;
        try
        {
            claimed = await _repository
                .TryClaimDispatchAsync(reservation.Id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Fallback<T>("budget.unavailable");
        }

        if (!claimed) return Fallback<T>("budget.unavailable");

        BudgetProviderResult<T> providerResult;
        try
        {
            providerResult = await providerCall(reservation, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await TryMarkReconciliationPendingAsync(reservation.Id).ConfigureAwait(false);
            throw;
        }
        catch
        {
            await TryMarkReconciliationPendingAsync(reservation.Id).ConfigureAwait(false);
            return Fallback<T>("budget.provider_unavailable");
        }

        try
        {
            await _repository
                .SettleAsync(
                    reservation.Id,
                    providerResult.SettlementEventId,
                    providerResult.ActualCostMicros,
                    cancellationToken)
                .ConfigureAwait(false);
            return new BudgetDispatchResult<T>(
                BudgetDispatchDisposition.Dispatched,
                providerResult.Value,
                fallbackReason: null,
                fallbackMediaRef: null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await TryMarkReconciliationPendingAsync(reservation.Id).ConfigureAwait(false);
            throw;
        }
        catch
        {
            await TryMarkReconciliationPendingAsync(reservation.Id).ConfigureAwait(false);
            return Fallback<T>("budget.reconciliation_pending");
        }
    }

    private async Task TryMarkReconciliationPendingAsync(Guid reservationId)
    {
        try
        {
            await _repository.MarkReconciliationPendingAsync(
                reservationId,
                $"reconcile:{reservationId:N}",
                CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Fail closed. A provider is never re-invoked from this dispatcher entry.
        }
    }

    private BudgetDispatchResult<T> Fallback<T>(string reason) =>
        new(
            BudgetDispatchDisposition.Fallback,
            value: default,
            fallbackReason: reason,
            fallbackMediaRef: _fallbackMediaRef);
}
