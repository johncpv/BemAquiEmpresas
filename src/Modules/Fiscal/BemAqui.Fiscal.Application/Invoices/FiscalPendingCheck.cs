using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Fiscal.Application.Emitter;
using BemAqui.Fiscal.Domain;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BemAqui.Fiscal.Application.Invoices;

/// <summary>O que a conferência fez num tenant.</summary>
public sealed record FiscalPendingCheckResult(int Checked, int Changed, int XmlStored);

/// <summary>
/// Conferência de pendentes (FIS-01 CA 14, ADR 0027 §4): a reserva do webhook. Toda nota local em <c>Processing</c> ou
/// <c>ContingencyPending</c> há mais de 5 min passa pela <b>consulta forçada</b> do Emissor (<c>POST /api/v1/nfe/{chave}/consultar</c>,
/// lacuna L-a) e recebe a mesma transição do webhook. Cada nota é travada (<c>FOR UPDATE SKIP LOCKED</c>) e gravada com o <c>xmin</c>:
/// webhook e conferência ao mesmo tempo aplicam a transição uma vez. Nunca reemite: nota sem resposta (sem chave) só é reenviada com a
/// mesma chave e o mesmo payload, quando a FIS-03a souber montá-lo; até lá, alerta. Também guarda o XML de autorizada que ficou sem.
/// </summary>
public sealed partial class FiscalPendingChecker(
    IFiscalDb db,
    FiscalEmitterAccess access,
    OutgoingInvoiceReconciler reconciler,
    NfeSubmissionService submissions,
    IOutgoingNfePayloads payloads,
    FiscalAlerts alerts,
    TenantContextHolder tenant,
    IOptions<FiscalIntegrationOptions> options,
    TimeProvider clock,
    ILogger<FiscalPendingChecker> logger)
{
    public const int BatchSize = 200;

    private static readonly OutgoingInvoiceStatus[] Pending = [OutgoingInvoiceStatus.Processing, OutgoingInvoiceStatus.ContingencyPending];

    public async Task<FiscalPendingCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (tenant.Resolved?.IsReleased(FiscalModuleInfo.Code) != true)
        {
            return new FiscalPendingCheckResult(0, 0, 0);
        }

        var now = clock.GetUtcNow();
        var cutoff = now - options.Value.PendingAfter;
        var ids = await db.OutgoingInvoices.AsNoTracking()
            .Where(i => (Pending.Contains(i.Status) && i.ProcessingSince <= cutoff) || (i.Status == OutgoingInvoiceStatus.Authorized && i.XmlAttachmentId == null))
            .OrderBy(i => i.ProcessingSince).Select(i => i.Id).Take(BatchSize).ToListAsync(cancellationToken);
        if (ids.Count == 0)
        {
            return new FiscalPendingCheckResult(0, 0, 0);
        }

        var emitter = await access.CurrentAsync(requireActive: true, cancellationToken);
        if (emitter.IsFailure)
        {
            var skippedTenant = TenantId();
            LogSkipped(logger, skippedTenant, emitter.Error!.Code, ids.Count);
            return new FiscalPendingCheckResult(0, 0, 0);
        }

        await CheckSefazAsync(emitter.Value, cancellationToken);
        int checkedCount = 0, changed = 0, xml = 0;
        foreach (var id in ids)
        {
            var step = await CheckOneAsync(id, emitter.Value, cancellationToken);
            checkedCount += step.Checked ? 1 : 0;
            changed += step.Changed ? 1 : 0;
            xml += step.Xml ? 1 : 0;
            if (step.Stop)
            {
                break; // chave recusada ou Emissor fora do ar: as outras esperam a próxima rodada
            }
        }

        var tenantId = TenantId();
        LogChecked(logger, tenantId, checkedCount, changed, xml);
        return new FiscalPendingCheckResult(checkedCount, changed, xml);
    }

    private sealed record Step(bool Checked, bool Changed, bool Xml, bool Stop);

    private async Task<Step> CheckOneAsync(Guid id, IFiscalEmitter emitter, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var invoice = await db.OutgoingInvoices
            .FromSql($"SELECT i.*, i.xmin FROM fiscal.outgoing_invoices i WHERE i.id = {id} FOR UPDATE SKIP LOCKED")
            .FirstOrDefaultAsync(cancellationToken);
        if (invoice is null)
        {
            return new Step(false, false, false, false); // outra execução (ou o webhook) está com a nota
        }

        var now = clock.GetUtcNow();
        if (invoice.Status == OutgoingInvoiceStatus.Authorized && invoice.XmlAttachmentId is null && invoice.AccessKey is { } authorizedKey)
        {
            var stored = await reconciler.StoreXmlAsync(invoice, authorizedKey, emitter, cancellationToken);
            return await SaveAsync(transaction, new Step(true, false, stored, false), cancellationToken);
        }

        if (!invoice.IsPending)
        {
            return new Step(false, false, false, false);
        }

        if (invoice.AccessKey is not { } key)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return await ResendWithoutAnswerAsync(invoice, cancellationToken);
        }

        var remote = await emitter.ForceConsultAsync(key, cancellationToken);
        if (remote.IsFailure)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return await FailedAsync(invoice, remote.Error!.Code, cancellationToken);
        }

        var before = invoice.Status;
        var applied = await reconciler.ApplyAsync(invoice, remote.Value, emitter, cancellationToken);
        if (invoice.Status == OutgoingInvoiceStatus.ContingencyPending && invoice.ContingencySince <= now - options.Value.ContingencyAlertAfter)
        {
            await alerts.RaiseAsync(FiscalAlertReason.ContingencyTooLong, invoice.Id, cancellationToken);
        }

        return await SaveAsync(transaction, new Step(true, applied && invoice.Status != before, invoice.XmlAttachmentId is not null && before != OutgoingInvoiceStatus.Authorized, false), cancellationToken);
    }

    private async Task<Step> SaveAsync(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction, Step step, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return step;
        }
        catch (DbUpdateConcurrencyException)
        {
            // O webhook gravou a mesma nota primeiro: vale o dele (a transição é a mesma).
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            return step with { Changed = false, Xml = false };
        }
    }

    /// <summary>Nota que nunca recebeu resposta (queda depois do POST): só a mesma chave e o mesmo payload; sem payload, alerta.</summary>
    private async Task<Step> ResendWithoutAnswerAsync(OutgoingInvoice invoice, CancellationToken cancellationToken)
    {
        var payload = await payloads.BuildAsync(invoice.Id, cancellationToken);
        if (payload is null)
        {
            await alerts.RaiseAsync(FiscalAlertReason.Unreachable, invoice.Id, cancellationToken);
            return new Step(true, false, false, false);
        }

        db.ChangeTracker.Clear();
        var resent = await submissions.SubmitAsync(invoice.Id, payload, cancellationToken);
        return new Step(true, resent.IsSuccess && resent.Value.Status != OutgoingInvoiceStatus.Processing, false, resent.IsSuccess && resent.Value.Outcome == SubmissionOutcome.Uncertain);
    }

    private async Task<Step> FailedAsync(OutgoingInvoice invoice, string code, CancellationToken cancellationToken)
    {
        switch (code)
        {
            case FiscalErrorCodes.IntegrationMisconfigured:
                await alerts.RaiseAsync(FiscalAlertReason.KeyRejected, null, cancellationToken);
                return new Step(true, false, false, true);
            case FiscalErrorCodes.NotFound:
                // Sem a consulta forçada no Emissor (L-a) ou nota que ele não conhece: fica Processing, demorando, nunca reemitida.
                await alerts.RaiseAsync(FiscalAlertReason.ForceConsultMissing, invoice.Id, cancellationToken);
                return new Step(true, false, false, false);
            case FiscalErrorCodes.RateLimited:
                return new Step(true, false, false, true);
            default:
                await alerts.RaiseAsync(FiscalAlertReason.Unreachable, null, cancellationToken);
                return new Step(true, false, false, true);
        }
    }

    /// <summary>SEFAZ fora do ar (CA 12): alerta uma vez por janela; a conferência segue (o Emissor responde pela contingência).</summary>
    private async Task CheckSefazAsync(IFiscalEmitter emitter, CancellationToken cancellationToken)
    {
        var settings = await db.FiscalIntegrationSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var status = await emitter.SefazStatusAsync(settings?.IssuerDocument, cancellationToken);
        if (status.IsSuccess && !status.Value.Online)
        {
            await alerts.RaiseAsync(FiscalAlertReason.SefazDown, null, cancellationToken);
        }
    }

    private Guid TenantId() => tenant.Resolved?.Tenant.Id ?? Guid.Empty;

    [LoggerMessage(Level = LogLevel.Information, Message = "Conferência fiscal do tenant {TenantId}: {Checked} conferidas, {Changed} mudaram, {Xml} XML guardados.")]
    private static partial void LogChecked(ILogger logger, Guid tenantId, int @checked, int changed, int xml);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Conferência fiscal do tenant {TenantId} não rodou ({Code}); {Pending} notas esperando.")]
    private static partial void LogSkipped(ILogger logger, Guid tenantId, string code, int pending);
}

public sealed record FiscalPendingCheckArgs;

/// <summary>Execução por tenant da conferência (só com o <c>tenantId</c>).</summary>
public sealed class FiscalPendingCheckJob(ITenantDirectory directory, ITenantScopeFactory scopes, ILogger<FiscalPendingCheckJob> logger)
    : TenantJob<FiscalPendingCheckArgs>(directory, scopes, logger)
{
    protected override async Task RunAsync(IServiceProvider services, TenantInfo tenant, FiscalPendingCheckArgs args, CancellationToken cancellationToken) =>
        await services.GetRequiredService<FiscalPendingChecker>().CheckAsync(cancellationToken);
}

/// <summary>
/// Recorrente global <c>fiscal-pending-check</c> (a cada 10 min): enfileira a conferência na fila <c>fiscal</c> para cada tenant ativo com o
/// módulo <c>fiscal</c> liberado (o job confere de novo no início).
/// </summary>
public sealed class FiscalPendingCheckRecurringJob(ITenantDirectory directory, ITenantResolver resolver, IBackgroundJobClient jobs)
    : GlobalRecurringJob<FiscalPendingCheckJob, FiscalPendingCheckArgs>(directory, jobs)
{
    public const string Id = "fiscal-pending-check";
    public const string Cron = "*/10 * * * *";

    protected override string Queue => JobQueues.Fiscal;

    protected override FiscalPendingCheckArgs ArgsFor(TenantInfo tenant) => new();

    protected override async Task<bool> ShouldEnqueueAsync(TenantInfo tenant, CancellationToken cancellationToken) =>
        (await resolver.FindByIdAsync(tenant.Id, cancellationToken))?.IsReleased(FiscalModuleInfo.Code) == true;
}
