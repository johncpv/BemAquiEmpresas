using System.Globalization;
using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Email;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Domain;
using BemAqui.Identity.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>
/// Seção "Régua de cobrança" de Configurações → Cobrança (FIN-09 CA 1 e 2, ADR 0042): pré-configurada (−3, 0, +3 e +10 dias, textos
/// sugeridos, 09:00) e desligada; editar com <c>settings.edit</c>, auditado (antes e depois). O texto é literal: só os campos da lista fixa.
/// </summary>
public sealed class CollectionRuleService(
    IFinanceDb db, CollectionPlanner planner, CollectionEligibility eligibility, EmailTemplateRenderer renderer, ICompanyIdentity company, IPermissionChecker permissions, ICurrentUser currentUser, TimeProvider clock)
{
    public const string TimeFormat = "HH:mm";

    public async Task<CollectionRuleView> GetAsync(CancellationToken cancellationToken = default)
    {
        var rule = await db.CollectionRules.AsNoTracking().OrderBy(r => r.Id).FirstOrDefaultAsync(cancellationToken);
        return await ViewAsync(rule, cancellationToken);
    }

    private async Task<CollectionRuleView> ViewAsync(CollectionRule? rule, CancellationToken ct)
    {
        var steps = rule?.Steps ?? [.. CollectionRules.DefaultSteps];
        var sendTime = rule?.SendTime ?? CollectionRules.DefaultSendTime;
        var next = await planner.NextRunDateAsync(sendTime, ct);
        // Propositivo (ao ligar): quantos títulos recebem lembrete no próximo dia de envio, já sem os pausados e os pagos.
        var due = await planner.DueOnAsync(steps, next, tracking: false, ct);
        var blocked = await eligibility.BlockedAsync([.. due.Select(d => d.Receivable)], ct);
        var count = due.Count(d => !blocked.ContainsKey(d.Receivable.Id));
        var email = (await company.GetAsync(ct)).Email;
        return new CollectionRuleView(rule?.Id, rule?.IsActive ?? false, sendTime.ToString(TimeFormat, CultureInfo.InvariantCulture),
            steps.Select(s => new CollectionStepView(s.Days, CollectionRules.StepLabel(s.Days), s.Text)).ToList(), rule?.ReplyTo,
            email is { Length: > 0 } e && EmailAddresses.IsPlain(e) ? e : null, CollectionRules.Placeholders, CollectionRules.MaxSteps, count, next,
            await permissions.HasPermissionAsync("settings", "edit", ct), rule?.Version ?? 0);
    }

    /// <summary>Validação do "Salvar régua" (CA 1 e 2), com o campo e a mensagem em PT-BR.</summary>
    public static Error? Validate(CollectionRuleInput input, out TimeOnly sendTime, out List<CollectionStep> steps)
    {
        ArgumentNullException.ThrowIfNull(input);
        sendTime = default;
        steps = (input.Steps ?? []).Select(s => new CollectionStep(s?.Days ?? 0, s?.Text?.Trim() ?? string.Empty)).ToList();
        if (!TimeOnly.TryParseExact(input.SendTime, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out sendTime) || !CollectionRules.SendTimeValid(sendTime))
        {
            return ReceivableService.Invalid("sendTime", "Escolha um horário de envio entre 07:00 e 20:00.");
        }

        if (CollectionRules.StepsError(steps) is { } error)
        {
            return ReceivableService.Invalid(error.Field, error.Message);
        }

        var replyTo = input.ReplyTo?.Trim();
        return !string.IsNullOrEmpty(replyTo) && !EmailAddresses.IsPlain(replyTo)
            ? ReceivableService.Invalid("replyTo", "Informe um e-mail válido para as respostas, sem nome nem separador.")
            : null;
    }

    public async Task<Result<CollectionRuleView>> SaveAsync(CollectionRuleInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await permissions.HasPermissionAsync("settings", "edit", cancellationToken))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Você não tem acesso a esta funcionalidade. Fale com o administrador.");
        }

        if (Validate(input, out var sendTime, out var steps) is { } invalid)
        {
            return invalid;
        }

        var rule = await db.CollectionRules.OrderBy(r => r.Id).FirstOrDefaultAsync(cancellationToken);
        if (rule is null)
        {
            rule = new CollectionRule();
            db.CollectionRules.Add(rule);
        }
        else if (input.Version is { } version)
        {
            db.Entry(rule).Property(r => r.Version).OriginalValue = version;
        }

        rule.Steps = [.. steps.OrderBy(s => s.Days)];
        rule.SendTime = sendTime;
        rule.IsActive = input.IsActive;
        rule.ReplyTo = string.IsNullOrWhiteSpace(input.ReplyTo) ? null : input.ReplyTo.Trim();
        rule.UpdatedAt = clock.GetUtcNow();
        rule.UpdatedBy = currentUser.UserId;
        await db.SaveChangesAsync(cancellationToken);
        return await ViewAsync(rule, cancellationToken);
    }

    /// <summary>Prévia (CA 2): o e-mail montado como no envio, com um título de exemplo (nenhum dado de cliente real).</summary>
    public async Task<Result<CollectionPreview>> PreviewAsync(CollectionPreviewInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Days is < CollectionRules.MinDays or > CollectionRules.MaxDays)
        {
            return ReceivableService.Invalid("days", "Os dias do passo vão de 30 antes a 90 depois do vencimento.");
        }

        if (CollectionRules.TextError(input.Text) is { } error)
        {
            return ReceivableService.Invalid("text", error);
        }

        var info = await company.GetAsync(cancellationToken);
        var name = EmailAddresses.OneLine(info.TradeName ?? info.LegalName);
        var today = BusinessTime.Today(clock);
        var due = today.AddDays(-input.Days);
        const string line = "07790.00116 12345.678901 23456.789012 1 00000000012345";
        var values = CollectionReminderMailer.Values(name, "Cliente Exemplo Ltda", "REC-0001", ReceivableService.Money(1234.56m), due, line, today);
        var model = CollectionReminderMailer.Model(name, "REC-0001", due, today, CollectionRules.Render(input.Text!.Trim(), values), line, name);
        var rendered = renderer.Render(EmailDispatchLimiter.CollectionReminderTemplate, model);
        return new CollectionPreview(rendered.Subject, rendered.Text.Trim(), line, "Exemplo com um título fictício de R$ 1.234,56 e boleto emitido.");
    }

    /// <summary>Seed (também nos tenants existentes, ADR 0016 e 0042): a régua sugerida, desligada, só se ainda não existe.</summary>
    public static async Task SeedAsync(IFinanceDb db, TimeProvider clock, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(clock);
        if (await db.CollectionRules.AnyAsync(cancellationToken))
        {
            return;
        }

        db.CollectionRules.Add(new CollectionRule { Steps = [.. CollectionRules.DefaultSteps], SendTime = CollectionRules.DefaultSendTime, IsActive = false, UpdatedAt = clock.GetUtcNow() });
        await db.SaveChangesAsync(cancellationToken);
    }
}
