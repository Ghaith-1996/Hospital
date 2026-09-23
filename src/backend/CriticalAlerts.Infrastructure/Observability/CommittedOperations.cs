using System.Data.Common;
using CriticalAlerts.Domain.Reliability;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace CriticalAlerts.Infrastructure.Observability;

// Scoped to one DbContext. The output boundaries validate captured operations/correlations at emission.
public sealed class CommittedOperations(ILogger logger, PlatformMetrics? metrics = null)
{
    private readonly Dictionary<Guid, List<(string Action, string Correlation)>> pending = [];
    private List<(string Action, string Correlation)> saving = [];

    public void Capture(DbContext? context)
    {
        saving = context?.ChangeTracker.Entries<AuditEvent>().Where(e => e.State == EntityState.Added)
            .Select(e => (e.Entity.Action, e.Entity.CorrelationId)).ToList() ?? [];
    }

    public void Saved(DbContext? context)
    {
        var values = saving;
        saving = [];
        if (context?.Database.CurrentTransaction is { } transaction)
        {
            if (!pending.TryGetValue(transaction.TransactionId, out var list)) pending[transaction.TransactionId] = list = [];
            list.AddRange(values);
        }
        else Emit(values);
    }

    public void Committed(Guid transactionId)
    {
        if (pending.Remove(transactionId, out var values)) Emit(values);
    }

    public void Discard(Guid transactionId) => pending.Remove(transactionId);
    public void SaveFailed() => saving = [];

    private void Emit(IEnumerable<(string Action, string Correlation)> values)
    {
        foreach (var value in values)
        {
            CriticalAlertsOperationalLog.Completed(logger, value.Action, value.Correlation);
            metrics?.Record(value.Action);
        }
    }
}

public sealed class OperationSaveInterceptor(CommittedOperations observer) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    { observer.Capture(eventData.Context); return result; }
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    { observer.Capture(eventData.Context); return ValueTask.FromResult(result); }
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    { observer.Saved(eventData.Context); return result; }
    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    { observer.Saved(eventData.Context); return ValueTask.FromResult(result); }
    public override void SaveChangesFailed(DbContextErrorEventData eventData) => observer.SaveFailed();
    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    { observer.SaveFailed(); return Task.CompletedTask; }
}

public sealed class OperationTransactionInterceptor(CommittedOperations observer) : DbTransactionInterceptor
{
    public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) => observer.Committed(eventData.TransactionId);
    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    { observer.Committed(eventData.TransactionId); return Task.CompletedTask; }
    public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) => observer.Discard(eventData.TransactionId);
    public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    { observer.Discard(eventData.TransactionId); return Task.CompletedTask; }
    public override void TransactionFailed(DbTransaction transaction, TransactionErrorEventData eventData) => observer.Discard(eventData.TransactionId);
    public override Task TransactionFailedAsync(DbTransaction transaction, TransactionErrorEventData eventData, CancellationToken cancellationToken = default)
    { observer.Discard(eventData.TransactionId); return Task.CompletedTask; }
}
