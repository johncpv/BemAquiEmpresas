using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Inventory.Application;

/// <summary>
/// Reserva genérica de documento para o pedido de venda (VND-02a, <see cref="ISalesReservations"/>): pede ao <see cref="ReservationAllocator"/>,
/// o único que escreve reserva (EST-02), na transação de quem chama, e grava. Exige transação aberta, como o motor.
/// </summary>
public sealed class SalesReservationsContract(IInventoryDb db, ReservationAllocator allocator) : ISalesReservations
{
    public async Task<SourceReservationOutcome> ReserveAsync(SourceReservationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireTransaction();
        if (string.IsNullOrWhiteSpace(request.SourceType) || request.SourceId == Guid.Empty || request.Lines.Any(l => l.Quantity < 0))
        {
            throw new ArgumentException("Reserva de documento precisa de tipo, id e quantidades não negativas.", nameof(request));
        }

        var outcome = await allocator.ReserveForSourceAsync(request, cancellationToken);
        if (outcome.Reserved)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return outcome;
    }

    public async Task ReleaseAsync(string sourceType, Guid sourceId, string origin, CancellationToken cancellationToken = default)
    {
        RequireTransaction();
        await allocator.ReleaseForSourceAsync(sourceType, sourceId, origin, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SetExpiryAsync(string sourceType, Guid sourceId, DateTimeOffset? expiresAt, CancellationToken cancellationToken = default)
    {
        await allocator.SetSourceExpiryAsync(sourceType, sourceId, expiresAt, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SourceReservationInfo>> ForSourceAsync(string sourceType, Guid sourceId, CancellationToken cancellationToken = default) =>
        await db.Reservations.IgnoreQueryFilters().AsNoTracking().Where(r => r.SourceType == sourceType && r.SourceId == sourceId).OrderBy(r => r.ItemId)
            .Select(r => new SourceReservationInfo(r.ItemId, r.LocationId, r.Quantity, r.QuantityIssued, r.Status, r.ExpiresAt)).ToListAsync(cancellationToken);

    private void RequireTransaction()
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("ISalesReservations exige transação aberta: o chamador é dono da transação (ADR 0023 §3).");
        }
    }
}
