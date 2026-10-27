using BemAqui.BuildingBlocks.Identity;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>
/// Reações do boleto às mudanças do título (BNK-01b CA 17), dentro do Finance e na mesma gravação do comando: título cancelado, valor
/// alterado (aditivo, COM-07), vencimento alterado (D14) ou quitado por outro meio (baixa manual da FIN-01) pedem o cancelamento do
/// boleto no Inter (job, com o motivo); o aviso a quem tem <c>finance.edit</c> sai na confirmação do banco.
/// </summary>
public sealed class BillingSlipReactions(IFinanceDb db, IBillingSlipJobs jobs, ICurrentUser currentUser, TimeProvider clock)
{
    public const string OpenCode = "billing_slip_open";
    public const string OpenMessage = "Este título tem boleto no Inter. Cancelar o boleto e registrar a baixa?";

    /// <summary>Boleto que ainda ocupa o título (pedido, emitido ou com falha), sem cancelamento pendente.</summary>
    public async Task<BillingSlip?> OpenSlipAsync(Receivable receivable, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receivable);
        if (receivable.BillingSlipId is not { } id)
        {
            return null;
        }

        var slip = await db.BillingSlips.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        return slip is { HoldsReceivable: true, CancelPending: false } ? slip : null;
    }

    /// <summary>Baixa manual de título com boleto aberto (CA 17): sem a confirmação do usuário, 409 com a pergunta.</summary>
    public async Task<Error?> SettlementGuardAsync(Receivable receivable, bool confirmed, CancellationToken cancellationToken = default)
    {
        if (await OpenSlipAsync(receivable, cancellationToken) is null)
        {
            return null;
        }

        if (!confirmed)
        {
            return Error.Conflict(OpenCode, OpenMessage);
        }

        await RequestCancelAsync(receivable, BillingSlipCancelReason.SettledOtherMeans, cancellationToken);
        return null;
    }

    /// <summary>Pede o cancelamento do boleto aberto do título, se houver (o chamador grava).</summary>
    public async Task RequestCancelAsync(Receivable receivable, BillingSlipCancelReason reason, CancellationToken cancellationToken = default)
    {
        if (await OpenSlipAsync(receivable, cancellationToken) is not { } slip)
        {
            return;
        }

        slip.RequestCancel(reason, currentUser.UserId, clock.GetUtcNow());
        await jobs.EnqueueCancelAsync(slip.Id, cancellationToken);
    }
}
