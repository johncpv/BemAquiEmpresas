using Microsoft.EntityFrameworkCore;

namespace BemAqui.Migrations.Application;

public sealed partial class ImportRunner
{
    /// <summary>Tudo numa transação desfeita no fim: os destinos ficam como estavam e o relatório é o mesmo do real (CA 14).</summary>
    private async Task<string?> DryRunAsync(ImportSession session, IReadOnlyList<IDolibarrLoader> selected, CancellationToken ct)
    {
        string? failure = null;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        foreach (var loader in selected)
        {
            session.Begin(loader.Entity, transaction);
            failure = await TryLoadAsync(session, loader, ct);
            if (failure is not null)
            {
                break;
            }
        }

        await transaction.RollbackAsync(CancellationToken.None);
        db.ChangeTracker.Clear();
        return failure;
    }

    /// <summary>Uma transação por entidade: a staging da entidade entra junto com os destinos; a falha desfaz só a entidade em curso.</summary>
    private async Task<string?> RealAsync(ImportSession session, IReadOnlyList<IDolibarrLoader> selected, CancellationToken ct)
    {
        foreach (var loader in selected)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            session.Begin(loader.Entity, transaction);
            var failure = await TryLoadAsync(session, loader, ct);
            if (failure is null)
            {
                db.ImportItems.AddRange(session.Items.Where(i => i.Entity == loader.Entity));
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                db.ChangeTracker.Clear();
                continue;
            }

            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            session.Discard(loader.Entity);
            return failure;
        }

        return null;
    }

    private static async Task<string?> TryLoadAsync(ImportSession session, IDolibarrLoader loader, CancellationToken ct)
    {
        try
        {
            await loader.LoadAsync(session, ct);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return "Falha de infraestrutura em " + loader.Entity + ", entidade desfeita: " + ex.GetType().Name + ": " + ex.Message;
        }
    }
}
