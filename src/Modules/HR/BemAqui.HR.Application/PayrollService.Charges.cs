using BemAqui.HR.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.HR.Application;

/// <summary>
/// Encargos da empresa (RH-02a CA 7): uma linha por guia que a contadora calculou (FGTS, INSS, IRRF retido, contribuição sindical e
/// "Outros"), com vencimento e órgão preenchidos pelo padrão de Configurações → Parâmetros → RH. A RH-02c gera um título a pagar por
/// guia (ADR 0036). O órgão é validado pelo <c>IParties</c>; a guia precisa ser da folha da rota (IDOR, 404).
/// </summary>
public sealed partial class PayrollService
{
    public async Task<Result<PayrollChargesView>> ChargesAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        var run = await db.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        if (run is null)
        {
            return RunNotFound();
        }

        await audit.RecordReadAsync(nameof(PayrollRun) + ".Charges", runId.ToString(), cancellationToken);
        return await ChargesViewAsync(run, cancellationToken);
    }

    public async Task<Result<PayrollChargesView>> AddChargeAsync(Guid runId, PayrollChargeInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await CanSensitiveAsync(cancellationToken))
        {
            return Forbidden();
        }

        var run = await DraftRunAsync(runId, cancellationToken);
        if (run.IsFailure)
        {
            return run.Error!;
        }

        var charge = new PayrollRunCharge { PayrollRunId = runId, CreatedAt = clock.GetUtcNow() };
        if (await ApplyChargeAsync(charge, run.Value, input, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        db.PayrollRunCharges.Add(charge);
        await db.SaveChangesAsync(cancellationToken);
        return await ChargesViewAsync(run.Value, cancellationToken);
    }

    public async Task<Result<PayrollChargesView>> UpdateChargeAsync(Guid runId, Guid chargeId, PayrollChargeInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await CanSensitiveAsync(cancellationToken))
        {
            return Forbidden();
        }

        var charge = await db.PayrollRunCharges.FirstOrDefaultAsync(c => c.Id == chargeId && c.PayrollRunId == runId, cancellationToken);
        if (charge is null)
        {
            return ChargeNotFound();
        }

        var run = await DraftRunAsync(runId, cancellationToken);
        if (run.IsFailure)
        {
            return run.Error!;
        }

        if (await ApplyChargeAsync(charge, run.Value, input, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        if (input.Version is { } version)
        {
            db.Entry(charge).Property(c => c.Version).OriginalValue = version;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await ChargesViewAsync(run.Value, cancellationToken);
    }

    public async Task<Result<PayrollChargesView>> DeleteChargeAsync(Guid runId, Guid chargeId, CancellationToken cancellationToken = default)
    {
        if (!await CanSensitiveAsync(cancellationToken))
        {
            return Forbidden();
        }

        var charge = await db.PayrollRunCharges.FirstOrDefaultAsync(c => c.Id == chargeId && c.PayrollRunId == runId, cancellationToken);
        if (charge is null)
        {
            return ChargeNotFound();
        }

        var run = await DraftRunAsync(runId, cancellationToken);
        if (run.IsFailure)
        {
            return run.Error!;
        }

        db.PayrollRunCharges.Remove(charge);
        await db.SaveChangesAsync(cancellationToken);
        return await ChargesViewAsync(run.Value, cancellationToken);
    }

    private async Task<Error?> ApplyChargeAsync(PayrollRunCharge charge, PayrollRun run, PayrollChargeInput input, CancellationToken ct)
    {
        if (input.Kind is not { } kind || !Enum.IsDefined(kind))
        {
            return PayrollRules.Field("kind", "Escolha a guia: FGTS, INSS, IRRF retido, contribuição sindical ou outros.");
        }

        var description = (input.Description ?? string.Empty).Trim();
        if (description.Length > PayrollRules.ChargeDescriptionMaxLength)
        {
            return PayrollRules.Field("description", "A descrição aceita até 100 caracteres.");
        }

        if (kind == PayrollChargeKind.Other && description.Length == 0)
        {
            return PayrollRules.Field("description", "Descreva a guia (ex.: \"Seguro de vida\").");
        }

        if (!PayrollRules.ValidAmount(input.Amount))
        {
            return PayrollRules.Field("amount", "Informe o valor da guia maior que zero, com até 2 casas.");
        }

        var defaults = (await parameters.ChargeDefaultsAsync(run.Period, ct)).Single(d => d.Kind == kind);
        if ((input.DueDate ?? defaults.DueDate) is not { } due)
        {
            return PayrollRules.Field("dueDate", "Informe o vencimento da guia.");
        }

        if (due < run.Period)
        {
            return PayrollRules.Field("dueDate", "O vencimento não pode ser antes da competência.");
        }

        var payee = input.PayeePartyId ?? defaults.PayeePartyId;
        if (payee is { } partyId && await parties.FindAsync(partyId, ct) is not { IsActive: true, IsDeleted: false })
        {
            return PayrollRules.Field("payeePartyId", "Escolha um terceiro ativo como órgão da guia.");
        }

        charge.Kind = kind;
        charge.Description = description.Length == 0 ? $"{PayrollRules.ChargeLabel(kind)} {PayrollRules.PeriodLabel(run.Period)}" : description;
        charge.Amount = input.Amount;
        charge.DueDate = due;
        charge.PayeePartyId = payee;
        return null;
    }

    private async Task<PayrollChargesView> ChargesViewAsync(PayrollRun run, CancellationToken ct)
    {
        var charges = await db.PayrollRunCharges.AsNoTracking().Where(c => c.PayrollRunId == run.Id).OrderBy(c => c.Kind).ThenBy(c => c.CreatedAt).ToListAsync(ct);
        var payeeIds = charges.Select(c => c.PayeePartyId).OfType<Guid>().Distinct().ToList();
        var names = (await parties.FindManyAsync(payeeIds, ct)).ToDictionary(p => p.Id, p => p.TradeName ?? p.LegalName);
        var fgts = await db.Payslips.AsNoTracking().Where(p => p.PayrollRunId == run.Id).SumAsync(p => p.FgtsAmount, ct);
        var fgtsCharges = charges.Where(c => c.Kind == PayrollChargeKind.Fgts).Select(c => c.Amount).DefaultIfEmpty().Sum();
        var hasFgts = charges.Any(c => c.Kind == PayrollChargeKind.Fgts);
        var items = charges.Select(c => new PayrollChargeDetail(c.Id, c.Kind, c.Description, c.Amount, c.DueDate, c.PayeePartyId, c.PayeePartyId is { } id ? names.GetValueOrDefault(id) : null, c.Version)).ToList();
        return new PayrollChargesView(items, await parameters.ChargeDefaultsAsync(run.Period, ct), fgts, hasFgts ? fgtsCharges : null,
            hasFgts && fgtsCharges != fgts, run.Status == PayrollRunStatus.Draft);
    }

    private static Error ChargeNotFound() => Error.NotFound("payroll_charge_not_found", "Guia não encontrada.");
}
