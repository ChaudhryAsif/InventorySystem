using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace InventorySystem.Data;

// Nested application services participate in the caller's transaction.
public sealed class AtomicTransaction : IDisposable, IAsyncDisposable
{
    private readonly IDbContextTransaction transaction;
    private readonly bool ownsTransaction;
    public AtomicTransaction(IDbContextTransaction transaction, bool ownsTransaction)
        => (this.transaction, this.ownsTransaction) = (transaction, ownsTransaction);
    public void Commit() { if (ownsTransaction) transaction.Commit(); }
    public void Rollback() { if (ownsTransaction) transaction.Rollback(); }
    public Task CommitAsync(CancellationToken ct = default) => ownsTransaction ? transaction.CommitAsync(ct) : Task.CompletedTask;
    public Task RollbackAsync(CancellationToken ct = default) => ownsTransaction ? transaction.RollbackAsync(ct) : Task.CompletedTask;
    public void Dispose() { if (ownsTransaction) transaction.Dispose(); }
    public ValueTask DisposeAsync() => ownsTransaction ? transaction.DisposeAsync() : ValueTask.CompletedTask;
}
public static class AtomicTransactionExtensions
{
    public static AtomicTransaction BeginAtomicTransaction(this DatabaseFacade database)
        => database.CurrentTransaction is { } current ? new(current, false) : new(database.BeginTransaction(), true);
    public static async Task<AtomicTransaction> BeginAtomicTransactionAsync(this DatabaseFacade database)
        => database.CurrentTransaction is { } current ? new(current, false) : new(await database.BeginTransactionAsync(), true);
}
