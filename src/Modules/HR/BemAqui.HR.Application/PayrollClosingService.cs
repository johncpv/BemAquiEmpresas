using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.Finance.Contracts;
using BemAqui.HR.Contracts;
using BemAqui.HR.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.HR.Application;

/// <summary>
/// Fechamento da folha (RH-02c CA 1 a 5, 12 e 13; ADR 0036 §2 e §4): pré-condições com link, data de pagamento pelo tipo, 2FA de novo
/// e, na mesma transação, a folha <c>Closed</c> com os totais congelados, um título <c>Open</c> por holerite (favorecido funcionário) e
/// um por guia (o órgão), a folha prevista refeita e o <c>PayrollClosed</c> no outbox (só aviso, sem valor). Reabrir cancela os títulos
/// pela origem; a folha fica <c>Paid</c> quando todos os títulos estão pagos (pela fonte, no consumidor do <c>PaymentOrderPaid</c>).
/// </summary>
public sealed class PayrollClosingService(
    IHrDb db,
    IPermissionChecker permissions,
    IStepUpTwoFactor stepUp,
    IPayables payables,
    IEmployees employees,
    PayrollCostTargets targets,
    PayrollForecastService forecast,
    PayrollService runs,
    Documents.PayslipDocumentService documents,
    PayrollChangeNotices changeNotices,
    PayrollCalculationService calculation,
    IEventPublisher events,
    INotificationService notifications,
    IUserDirectory users,
    BemAqui.BuildingBlocks.Persistence.Auditing.IAuditWriter audit,
    ICurrentUser currentUser,
    TimeProvider clock)
{
    public const string NotificationType = "hr.payroll";
    public const string StepUpMessage = "Confirme com o código do aplicativo autenticador para fechar a folha.";

    private sealed record Slip(Guid Id, Guid EmployeeId, string Name, string? Registration, decimal Net, decimal FgtsBase, bool HasLines, PayrollCostGroup Group);

    private sealed record Snapshot(PayrollRun Run, List<Slip> Slips, List<PayrollRunCharge> Charges, Dictionary<Guid, EmployeeBankAccountStatus> Accounts, HashSet<Guid> Veterans);

    /// <summary>Prévia do fechamento (CA 1): pendências, data sugerida, avisos e impacto. Leitura sensível auditada.</summary>
    public async Task<Result<PayrollClosePreview>> PreviewAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        var run = await db.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        if (run is null)
        {
            return RunNotFound();
        }

        var snapshot = await SnapshotAsync(run, cancellationToken);
        var preview = await BuildPreviewAsync(snapshot, cancellationToken);
        await audit.RecordReadAsync(nameof(PayrollRun) + ".Close", runId.ToString(), cancellationToken);
        return preview;
    }

    /// <summary>
    /// Fechar (CA 2 e 3): <c>hr.approve</c> (na rota) com <c>hr.view_sensitive</c>, sem pendência, com a data de pagamento e o 2FA de novo
    /// (403 <c>mfa_step_up_required</c>). Folha que não está em <c>Draft</c> ou versão antiga: 409. Qualquer título que falhe desfaz tudo.
    /// </summary>
    public async Task<Result<PayrollCloseResult>> CloseAsync(Guid runId, PayrollCloseInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await CanSensitiveAsync(cancellationToken))
        {
            return Forbidden();
        }

        var run = await db.PayrollRuns.FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        if (run is null)
        {
            return RunNotFound();
        }

        if (run.Status != PayrollRunStatus.Draft)
        {
            return PayrollRules.Closed(run.Period);
        }

        if (run.Version != input.Version)
        {
            return Stale();
        }

        // FLH-02a CA 15 e 16: a calculada antes da virada não fecha (409); depois, só calculada, atualizada e com as tabelas conferidas (422).
        if (await calculation.CloseErrorAsync(run, cancellationToken) is { } calculated)
        {
            return calculated;
        }

        var snapshot = await SnapshotAsync(run, cancellationToken);
        var preview = await BuildPreviewAsync(snapshot, cancellationToken);
        if (preview.Blockers.Count > 0)
        {
            return PayrollClosingRules.Blocked(preview.Blockers[0].Message) with { Data = new Dictionary<string, object?> { ["blockers"] = preview.Blockers.Select(b => b.Message).ToList() } };
        }

        // FLH-01 CA 12: mudança de tabela legal ou de parâmetro desde o último fechamento pede ciência (vem da auditoria, não dá para omitir).
        if (preview.Changes.Count > 0 && !input.AcknowledgedChanges)
        {
            return Error.Validation(PayrollChangeNotices.NotAcknowledgedCode, PayrollChangeNotices.NotAcknowledgedMessage);
        }

        if ((input.PaymentDate ?? preview.SuggestedPaymentDate) is not { } paymentDate)
        {
            return PayrollRules.Field("paymentDate", "Informe a data de pagamento da folha.");
        }

        if (paymentDate < run.Period)
        {
            return PayrollRules.Field("paymentDate", "A data de pagamento não pode ser antes da competência.");
        }

        if (currentUser.UserId is not { } me || !await stepUp.VerifyAsync(me, input.TwoFactorCode, cancellationToken))
        {
            return Error.Forbidden(EmployeeBankRules.StepUpCode, StepUpMessage);
        }

        var cost = await targets.ResolveAsync(cancellationToken);
        if (cost.IsFailure)
        {
            return cost.Error!;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var created = 0;
        foreach (var slip in snapshot.Slips.Where(s => s.Net > 0m))
        {
            var target = cost.Value[slip.Group];
            var plan = OpenPayables.For(Payee.Employee(slip.EmployeeId), target.AccountId, target.CostCenterId,
                [new OpenInstallment(1, slip.Net, paymentDate, PayrollClosingRules.SalaryDescription(run.Type, run.Period, slip.Registration, slip.Name))]);
            var made = await payables.CreateOpenInstallmentsAsync(DocumentOrigin.Payroll, slip.Id, plan, cancellationToken);
            if (made.IsFailure)
            {
                return made.Error!;
            }

            created += made.Value.Count;
        }

        var chargeTarget = cost.Value[PayrollClosingRules.ChargeGroup(snapshot.Slips.Select(s => (s.Group, s.FgtsBase)))];
        foreach (var charge in snapshot.Charges)
        {
            var plan = OpenPayables.For(Payee.Party(charge.PayeePartyId!.Value), chargeTarget.AccountId, chargeTarget.CostCenterId,
                [new OpenInstallment(1, charge.Amount, charge.DueDate, PayrollClosingRules.ChargeDescription(charge.Kind, run.Period))]);
            var made = await payables.CreateOpenInstallmentsAsync(DocumentOrigin.Payroll, charge.Id, plan, cancellationToken);
            if (made.IsFailure)
            {
                return made.Error!;
            }

            created += made.Value.Count;
        }

        run.Status = PayrollRunStatus.Closed;
        run.ClosedAt = clock.GetUtcNow();
        run.ClosedBy = me;
        run.PaymentDate = paymentDate;
        run.ClosedNet = snapshot.Slips.Sum(s => s.Net);
        run.ClosedCharges = snapshot.Charges.Sum(c => c.Amount);
        run.ClosedPayables = created;
        db.Entry(run).Property(r => r.Version).OriginalValue = input.Version;

        // A previsão do mês e do tipo é substituída pela folha real, e a dos meses seguintes é refeita (CA 15), na mesma transação.
        // RH-02b CA 10: holerite já avisado que mudou depois do aviso ganha "Alterado depois do aviso".
        var changedAfterNotice = await documents.MarkChangedAfterNoticeAsync(run.Id, cancellationToken);
        var refreshed = await forecast.RefreshAsync(cancellationToken);
        if (refreshed.IsFailure)
        {
            return refreshed.Error!;
        }

        events.Publish(new PayrollClosed(run.Id, run.Period));
        foreach (var payer in await users.UsersWithPermissionAsync("finance.pay", cancellationToken))
        {
            notifications.Notify(payer, NotificationType, $"Folha de {PayrollRules.PeriodLabel(run.Period)} fechada: {created} {(created == 1 ? "título" : "títulos")} para pagar.", RunLink(run.Id, "pagamentos"));
        }

        if (await SaveAsync(cancellationToken) is { } conflict)
        {
            return conflict;
        }

        await transaction.CommitAsync(cancellationToken);
        var summary = (await runs.GetAsync(run.Id, cancellationToken)).Value;
        return new PayrollCloseResult(summary, created, PayrollClosingRules.ClosedMessage(run.Period, created), changedAfterNotice);
    }

    /// <summary>
    /// Reabrir (CA 13): só a folha <c>Closed</c> sem título em OP não finalizada nem pago (409 com o link para a OP); na mesma transação, a
    /// folha volta a <c>Draft</c>, os títulos são cancelados pela origem com o motivo, a previsão é refeita e o Proprietário e quem tem
    /// <c>finance.edit</c> são avisados.
    /// </summary>
    public async Task<Result<PayrollRunSummary>> ReopenAsync(Guid runId, PayrollReopenInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await CanSensitiveAsync(cancellationToken))
        {
            return Forbidden();
        }

        var reason = (input.Reason ?? string.Empty).Trim();
        if (reason.Length is < 5 or > 200)
        {
            return PayrollRules.Field("reason", "Informe o motivo da reabertura, de 5 a 200 caracteres.");
        }

        var run = await db.PayrollRuns.FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        if (run is null)
        {
            return RunNotFound();
        }

        if (run.Version != input.Version)
        {
            return Stale();
        }

        var origins = await OriginIdsAsync(run.Id, cancellationToken);
        var titles = await payables.StatusByOriginAsync(DocumentOrigin.Payroll, origins, cancellationToken);
        var busy = titles.FirstOrDefault(t => t.PaymentOrderId is not null || t.Status is PayableStatus.AwaitingApproval or PayableStatus.Approved or PayableStatus.PartiallyPaid or PayableStatus.Paid);
        if (run.Status == PayrollRunStatus.Paid || busy is not null)
        {
            return Error.Conflict(PayrollClosingRules.ReopenInPaymentCode, "Há pagamentos desta folha em andamento. Estorne ou cancele a ordem de pagamento antes.")
                with { Data = new Dictionary<string, object?> { ["paymentOrderId"] = busy?.PaymentOrderId } };
        }

        if (run.Status != PayrollRunStatus.Closed)
        {
            return Error.Conflict("payroll_run_not_closed", $"A folha de {PayrollRules.PeriodLabel(run.Period)} não está fechada.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var origin in origins)
        {
            var cancelled = await payables.CancelAllFromOriginAsync(DocumentOrigin.Payroll, origin, "Folha reaberta: " + reason, cancellationToken);
            if (cancelled.IsFailure)
            {
                return cancelled.Error!;
            }
        }

        run.Status = PayrollRunStatus.Draft;
        run.ClosedAt = null;
        run.ClosedBy = null;
        run.ClosedNet = null;
        run.ClosedCharges = null;
        run.ClosedPayables = null;
        run.ReopenReason = reason;
        run.ReopenedAt = clock.GetUtcNow();
        run.ReopenedBy = currentUser.UserId;
        // RH-02b CA 10: os PDFs guardados deixam de valer; fechada de novo, o próximo download refaz.
        await documents.InvalidateAsync(run.Id, cancellationToken);
        db.Entry(run).Property(r => r.Version).OriginalValue = input.Version;
        var refreshed = await forecast.RefreshAsync(cancellationToken);
        if (refreshed.IsFailure)
        {
            return refreshed.Error!;
        }

        events.Publish(new PayrollReopened(run.Id));
        var who = currentUser.UserId is { } me ? await users.DisplayNameAsync(me, cancellationToken) ?? "um usuário" : "o sistema";
        var notify = (await users.OwnerIdsAsync(cancellationToken)).Concat(await users.UsersWithPermissionAsync("finance.edit", cancellationToken)).Distinct();
        foreach (var user in notify)
        {
            notifications.Notify(user, NotificationType, $"Folha de {PayrollRules.PeriodLabel(run.Period)} reaberta por {who}: {Cut(reason, 100)}", RunLink(run.Id, "historico"));
        }

        if (await SaveAsync(cancellationToken) is { } conflict)
        {
            return conflict;
        }

        await transaction.CommitAsync(cancellationToken);
        return (await runs.GetAsync(run.Id, cancellationToken)).Value;
    }

    /// <summary>
    /// Folha paga pela fonte (CA 12): a fechada com todos os títulos <c>Paid</c> passa a <c>Paid</c>; a paga com algum título fora de
    /// <c>Paid</c> (estorno) volta a <c>Closed</c>. Idempotente: o consumidor do <c>PaymentOrderPaid</c> e do <c>PaymentOrderReversed</c> chama.
    /// </summary>
    public async Task ReconcileAsync(CancellationToken cancellationToken = default)
    {
        // Os consumidores rodam em paralelo: se outra conciliação gravou a mesma folha antes, recarrega e confere de novo.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await ReconcileOnceAsync(cancellationToken);
                return;
            }
            catch (DbUpdateConcurrencyException ex) when (attempt < 3)
            {
                foreach (var entry in ex.Entries)
                {
                    await entry.ReloadAsync(cancellationToken);
                }
            }
        }
    }

    private async Task ReconcileOnceAsync(CancellationToken cancellationToken)
    {
        var candidates = await db.PayrollRuns.Where(r => r.Status == PayrollRunStatus.Closed || r.Status == PayrollRunStatus.Paid).ToListAsync(cancellationToken);
        foreach (var run in candidates)
        {
            var origins = await OriginIdsAsync(run.Id, cancellationToken);
            var titles = await payables.StatusByOriginAsync(DocumentOrigin.Payroll, origins, cancellationToken);
            var allPaid = titles.Count > 0 && titles.All(t => t.Status == PayableStatus.Paid);
            if (run.Status == PayrollRunStatus.Closed && allPaid)
            {
                run.Status = PayrollRunStatus.Paid;
                run.PaidAt = clock.GetUtcNow();
            }
            else if (run.Status == PayrollRunStatus.Paid && !allPaid)
            {
                run.Status = PayrollRunStatus.Closed;
                run.PaidAt = null;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Aba Pagamentos (CA 5): os títulos da folha pelo <c>IPayables</c>, a situação só com <c>finance.view</c> (sem ela, "Título gerado"), e
    /// as pré-seleções das OPs: salários em aberto sem conta em quarentena, e guias em aberto. Leitura sensível auditada.
    /// </summary>
    public async Task<Result<PayrollPayments>> PaymentsAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        var run = await db.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        if (run is null)
        {
            return RunNotFound();
        }

        var slips = await (from p in db.Payslips.AsNoTracking()
                           join e in db.Employees.IgnoreQueryFilters().AsNoTracking() on p.EmployeeId equals e.Id
                           where p.PayrollRunId == runId
                           select new { p.Id, p.EmployeeId, e.Name, e.Registration }).ToDictionaryAsync(s => s.Id, cancellationToken);
        var charges = await db.PayrollRunCharges.AsNoTracking().Where(c => c.PayrollRunId == runId).ToDictionaryAsync(c => c.Id, cancellationToken);
        var titles = await payables.StatusByOriginAsync(DocumentOrigin.Payroll, [.. slips.Keys, .. charges.Keys], cancellationToken);
        var canSee = await permissions.HasPermissionAsync("finance", "view", cancellationToken);
        var accounts = (await employees.BankAccountStatusAsync(slips.Values.Select(s => s.EmployeeId).Distinct().ToList(), cancellationToken)).ToDictionary(a => a.EmployeeId);
        var rows = titles.Select(t =>
        {
            if (slips.TryGetValue(t.OriginId, out var s))
            {
                var account = accounts.GetValueOrDefault(s.EmployeeId);
                return new PayrollPaymentRow(t.PayableId, t.Number, true, t.OriginId, s.EmployeeId, string.IsNullOrWhiteSpace(s.Registration) ? s.Name : $"{s.Registration} — {s.Name}", t.Amount,
                    t.DueDate, canSee ? t.Status : null, canSee ? StatusLabel(t.Status) : "Título gerado", canSee ? t.PaymentOrderId : null, account?.InQuarantine == true, account?.QuarantineUntil);
            }

            var c = charges[t.OriginId];
            return new PayrollPaymentRow(t.PayableId, t.Number, false, t.OriginId, null, $"{PayrollRules.ChargeLabel(c.Kind)} — {c.Description}", t.Amount, t.DueDate,
                canSee ? t.Status : null, canSee ? StatusLabel(t.Status) : "Título gerado", canSee ? t.PaymentOrderId : null, false, null);
        }).OrderBy(r => !r.IsSalary).ThenBy(r => r.PayeeName, StringComparer.CurrentCulture).ToList();
        var open = rows.Where(r => titles.First(t => t.PayableId == r.PayableId).Status == PayableStatus.Open).ToList();
        await audit.RecordReadAsync(nameof(PayrollRun) + ".Payments", runId.ToString(), cancellationToken);
        return new PayrollPayments(rows, rows.Where(r => r.IsSalary).Sum(r => r.Amount), rows.Where(r => !r.IsSalary).Sum(r => r.Amount),
            open.Where(r => r.IsSalary && !r.InQuarantine).Select(r => r.PayableId).ToList(), open.Where(r => r.IsSalary && r.InQuarantine).ToList(),
            open.Where(r => !r.IsSalary).Select(r => r.PayableId).ToList(), canSee);
    }

    /// <summary>"Recalcular previsão" (CA 14, <c>hr.edit</c>): refaz a folha prevista numa transação.</summary>
    public async Task<Result<PayrollForecastResult>> RecalculateForecastAsync(CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var refreshed = await forecast.RefreshAsync(cancellationToken);
        if (refreshed.IsFailure)
        {
            return refreshed.Error!;
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return refreshed.Value;
    }

    private async Task<Snapshot> SnapshotAsync(PayrollRun run, CancellationToken ct)
    {
        var slips = await (from p in db.Payslips.AsNoTracking()
                           join e in db.Employees.IgnoreQueryFilters().AsNoTracking() on p.EmployeeId equals e.Id
                           join pos in db.JobPositions.IgnoreQueryFilters().AsNoTracking() on e.PositionId equals pos.Id
                           where p.PayrollRunId == run.Id
                           orderby e.Name
                           select new Slip(p.Id, p.EmployeeId, e.Name, e.Registration, p.Net, p.FgtsBase, db.PayslipLines.Any(l => l.PayslipId == p.Id),
                               pos.DefaultWorkCenterId != null ? PayrollCostGroup.Direct : PayrollCostGroup.Admin)).ToListAsync(ct);
        var charges = await db.PayrollRunCharges.AsNoTracking().Where(c => c.PayrollRunId == run.Id).OrderBy(c => c.Kind).ThenBy(c => c.CreatedAt).ToListAsync(ct);
        var ids = slips.Select(s => s.EmployeeId).ToList();
        var accounts = (await employees.BankAccountStatusAsync(ids, ct)).ToDictionary(a => a.EmployeeId);
        var veterans = (await (from p in db.Payslips.AsNoTracking()
                               join r in db.PayrollRuns.AsNoTracking() on p.PayrollRunId equals r.Id
                               where ids.Contains(p.EmployeeId) && r.Id != run.Id && r.Period < run.Period
                               select p.EmployeeId).Distinct().ToListAsync(ct)).ToHashSet();
        return new Snapshot(run, slips, charges, accounts, veterans);
    }

    private async Task<PayrollClosePreview> BuildPreviewAsync(Snapshot s, CancellationToken ct)
    {
        var run = s.Run;
        var blockers = new List<PayrollCloseIssue>();
        foreach (var slip in s.Slips.Where(x => !x.HasLines))
        {
            blockers.Add(new PayrollCloseIssue("payslip_empty", $"Holerite de {slip.Name} sem lançamento.", RunLink(run.Id, "holerites")));
        }

        foreach (var slip in s.Slips.Where(x => x.Net < 0m))
        {
            blockers.Add(new PayrollCloseIssue("payslip_negative_net", $"Holerite de {slip.Name} com líquido negativo.", RunLink(run.Id, "holerites")));
        }

        foreach (var slip in s.Slips.Where(x => x.Net > 0m && !s.Accounts.ContainsKey(x.EmployeeId)))
        {
            blockers.Add(new PayrollCloseIssue("employee_without_account", $"{slip.Name} sem conta para pagamento.", $"/app/rh/funcionarios/{slip.EmployeeId}?aba=pagamento"));
        }

        foreach (var charge in s.Charges.Where(c => c.PayeePartyId is null))
        {
            blockers.Add(new PayrollCloseIssue("charge_without_payee", $"Guia {PayrollRules.ChargeLabel(charge.Kind)} sem órgão favorecido.", RunLink(run.Id, "encargos")));
        }

        if (s.Slips.All(x => x.Net <= 0m) && s.Charges.Count == 0)
        {
            blockers.Add(new PayrollCloseIssue("payroll_empty", "A folha não tem nada a pagar: lance os holerites ou os encargos.", RunLink(run.Id, "holerites")));
        }

        blockers.AddRange(await calculation.CloseBlockersAsync(run, ct));
        var cost = await targets.ResolveAsync(ct);
        if (cost.IsFailure)
        {
            blockers.Add(new PayrollCloseIssue("payroll_account_missing", cost.Error!.Message, "/app/configuracoes/parametros?secao=rh"));
        }

        var rule = PayrollClosingRules.Rule(run.Type, run.Period, await targets.DueParametersAsync(ct));
        var suggested = run.PaymentDate ?? await targets.PaymentDateAsync(rule, ct);
        var newcomers = s.Slips.Where(x => !s.Veterans.Contains(x.EmployeeId)).Select(x => new PayrollClosePerson(x.EmployeeId, x.Name, x.Registration, null)).ToList();
        var changed = s.Slips.Where(x => s.Accounts.GetValueOrDefault(x.EmployeeId)?.InQuarantine == true)
            .Select(x => new PayrollClosePerson(x.EmployeeId, x.Name, x.Registration, s.Accounts[x.EmployeeId].QuarantineUntil)).ToList();
        var paid = s.Slips.Where(x => x.Net > 0m).ToList();
        return new PayrollClosePreview(run.Id, run.Period, run.Type, blockers, suggested, rule is PaymentDateRule.Informed, await targets.DueConfirmedAsync(run.Type, ct),
            newcomers, changed, paid.Count, paid.Sum(x => x.Net), s.Charges.Count, s.Charges.Sum(c => c.Amount), run.Version, await changeNotices.SinceLastClosingAsync(ct));
    }

    /// <summary>Ids de origem dos títulos da folha: os holerites e as guias.</summary>
    private async Task<List<Guid>> OriginIdsAsync(Guid runId, CancellationToken ct)
    {
        var slips = await db.Payslips.AsNoTracking().Where(p => p.PayrollRunId == runId).Select(p => p.Id).ToListAsync(ct);
        var charges = await db.PayrollRunCharges.AsNoTracking().Where(c => c.PayrollRunId == runId).Select(c => c.Id).ToListAsync(ct);
        return [.. slips, .. charges];
    }

    private async Task<Error?> SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateConcurrencyException)
        {
            return Stale();
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            // Dois fechamentos ao mesmo tempo: o índice único (origin, origin_id, installment) segura o segundo (CA 3).
            return Stale();
        }
    }

    private static string StatusLabel(PayableStatus status) => status switch
    {
        PayableStatus.Planned => "Previsto",
        PayableStatus.Open => "Em aberto",
        PayableStatus.AwaitingApproval => "Aguardando aprovação",
        PayableStatus.Approved => "Aprovado",
        PayableStatus.PartiallyPaid => "Pago em parte",
        PayableStatus.Paid => "Pago",
        _ => "Cancelado",
    };

    private ValueTask<bool> CanSensitiveAsync(CancellationToken ct) => permissions.HasPermissionAsync("hr", PositionService.SensitiveAction, ct);

    internal static string RunLink(Guid runId, string tab) => $"/app/rh/folha/{runId}?aba={tab}";

    private static string Cut(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    private static Error Stale() => Error.Conflict("concurrency_conflict", "A folha mudou enquanto você trabalhava. Recarregue.");

    private static Error RunNotFound() => Error.NotFound("payroll_run_not_found", "Folha não encontrada.");

    private static Error Forbidden() => Error.Forbidden(Error.Codes.Forbidden, "Você não tem acesso aos valores da folha. Fale com o administrador.");
}
