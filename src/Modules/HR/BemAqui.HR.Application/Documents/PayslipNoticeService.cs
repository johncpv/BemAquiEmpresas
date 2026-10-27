using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Email;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.HR.Domain;
using BemAqui.Identity.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.HR.Application.Documents;

/// <summary>
/// Aviso do holerite por e-mail (RH-02b CA 7 a 10; D15): só com a folha fechada, um e-mail por funcionário, só para o e-mail do usuário
/// ligado e ativo (nunca cópia nem endereço digitado), template <c>payslip-available</c> sem anexo e sem valor; o modelo guarda só o
/// nome, o mês, a empresa e o link. Avisar a folha de novo não repete quem já foi avisado: o "Avisar de novo" é por holerite.
/// </summary>
public sealed class PayslipNoticeService(
    IHrDb db,
    IUserEmployeeLink links,
    IUserDirectory users,
    IEmailSender email,
    EmailLogReader emailLogs,
    TenantLinks tenantLinks,
    ICompanyIdentity company,
    IPermissionChecker permissions,
    ICurrentUser currentUser,
    TimeProvider clock)
{
    private sealed record Target(Payslip Payslip, string Name, string? Registration, string? PersonalEmail, bool Terminated, string? UserEmail, bool HasUser);

    /// <summary>Diálogo e coluna de aviso (CA 7 e 9), pela rota <c>/sensitive</c>: quem recebe, quem não tem usuário, os desligados e a situação.</summary>
    public async Task<Result<PayslipNotices>> NoticesAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        var run = await db.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        if (run is null)
        {
            return RunNotFound();
        }

        var targets = await TargetsAsync(runId, null, cancellationToken);
        var logs = (await emailLogs.StatusAsync(targets.Select(t => t.Payslip.NoticeEmailLogId).OfType<Guid>().ToList(), cancellationToken)).ToDictionary(l => l.Id);
        var rows = targets.Select(t =>
        {
            var log = t.Payslip.NoticeEmailLogId is { } id ? logs.GetValueOrDefault(id) : null;
            var failed = log?.Status is EmailStatus.Failed or EmailStatus.Skipped;
            var status = PayslipDocumentRules.Status(t.Terminated, t.UserEmail is not null, t.Payslip.NotifiedAt is not null, log?.Status == EmailStatus.Sent, failed);
            return new PayslipNoticeRow(t.Payslip.Id, t.Payslip.EmployeeId, t.Name, t.Registration, status, t.Payslip.NotifiedAt, log?.SentAt, failed ? log!.Error ?? "Envio não concluído." : null,
                t.Payslip.FirstViewedAt, t.Payslip.ChangedAfterNotice);
        }).ToList();
        return new PayslipNotices(
            run.Status != PayrollRunStatus.Draft,
            targets.Count(t => Receives(t) && t.Payslip.NotifiedAt is null),
            targets.Count(t => t.Payslip.NotifiedAt is not null),
            targets.Count(t => Receives(t) && t.Payslip.ChangedAfterNotice),
            targets.Where(t => !t.Terminated && t.UserEmail is null).Select(t => new PayslipNoticePerson(t.Payslip.EmployeeId, t.Name, t.Registration, t.PersonalEmail)).ToList(),
            targets.Where(t => t.Terminated).Select(t => new PayslipNoticePerson(t.Payslip.EmployeeId, t.Name, t.Registration, null)).ToList(),
            rows);
    }

    /// <summary>
    /// "Avisar funcionários" (CA 7, 8 e 10): <c>hr.edit</c> na rota e <c>hr.view_sensitive</c> aqui; folha em rascunho, 409. Enfileira um
    /// e-mail por funcionário com usuário ligado e ativo ainda não avisado (ou, com <c>OnlyChanged</c>, os alterados depois do aviso).
    /// </summary>
    public async Task<Result<PayslipNotifyResult>> NotifyAsync(Guid runId, PayslipNotifyInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (await GuardAsync(runId, cancellationToken) is { } refused)
        {
            return refused;
        }

        var targets = (await TargetsAsync(runId, null, cancellationToken))
            .Where(t => Receives(t) && (input.OnlyChanged ? t.Payslip.ChangedAfterNotice : t.Payslip.NotifiedAt is null)).ToList();
        if (targets.Count == 0)
        {
            return Error.Validation("payslip_notice_none", input.OnlyChanged ? "Nenhum holerite alterado depois do aviso." : "Todos os funcionários com usuário já foram avisados. Use \"Avisar de novo\" no holerite.");
        }

        return await EnqueueAsync(runId, targets, cancellationToken);
    }

    /// <summary>"Avisar de novo" (CA 8 e 9): um holerite da folha da rota (IDOR, 404), com usuário ligado e ativo e sem desligamento.</summary>
    public async Task<Result<PayslipNotifyResult>> NotifyOneAsync(Guid runId, Guid payslipId, CancellationToken cancellationToken = default)
    {
        if (await GuardAsync(runId, cancellationToken) is { } refused)
        {
            return refused;
        }

        var target = (await TargetsAsync(runId, payslipId, cancellationToken)).FirstOrDefault();
        if (target is null)
        {
            return PayslipDocumentService.PayslipNotFound();
        }

        if (target.Terminated)
        {
            return Error.Validation("payslip_notice_terminated", "Funcionário desligado: baixe o holerite e entregue pessoalmente.");
        }

        return target.UserEmail is null
            ? Error.Validation("payslip_notice_no_user", "O funcionário não tem usuário ativo ligado. Convide o usuário ou entregue o holerite pessoalmente.")
            : await EnqueueAsync(runId, [target], cancellationToken);
    }

    private static bool Receives(Target t) => !t.Terminated && t.UserEmail is not null;

    private async Task<Error?> GuardAsync(Guid runId, CancellationToken ct)
    {
        if (!await permissions.HasPermissionAsync("hr", PositionService.SensitiveAction, ct))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Você não tem acesso aos valores da folha. Fale com o administrador.");
        }

        var run = await db.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, ct);
        return run is null ? RunNotFound()
            : run.Status == PayrollRunStatus.Draft ? Error.Conflict(PayslipDocumentRules.NotClosedCode, "Avise os funcionários depois de fechar a folha.")
            : null;
    }

    private async Task<Result<PayslipNotifyResult>> EnqueueAsync(Guid runId, List<Target> targets, CancellationToken ct)
    {
        var run = await db.PayrollRuns.AsNoTracking().FirstAsync(r => r.Id == runId, ct);
        var info = await company.GetAsync(ct);
        var name = EmailAddresses.OneLine(info.TradeName ?? info.LegalName);
        var link = tenantLinks.Absolute(PayslipDocumentRules.MyPayslipsPath);
        var now = clock.GetUtcNow();
        foreach (var t in targets)
        {
            // Modelo mínimo (CA 8): nome, mês, empresa e link. Nada de valor, CPF ou anexo; o destinatário é só o do usuário ligado.
            var model = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["employee_name"] = EmailAddresses.OneLine(t.Name), ["month"] = PayslipDocumentRules.MonthYear(run.Period), ["tenant_name"] = name, ["link"] = link,
            };
            var logId = email.Enqueue(new EmailRequest(PayslipDocumentRules.NoticeTemplate, t.UserEmail!, model, PayslipDocumentRules.OwnerType, t.Payslip.Id, RequestedBy: currentUser.UserId));
            var tracked = await db.Payslips.FirstAsync(p => p.Id == t.Payslip.Id, ct);
            tracked.NotifiedAt = now;
            tracked.NoticeEmailLogId = logId;
            tracked.ChangedAfterNotice = false;
        }

        await db.SaveChangesAsync(ct);
        return new PayslipNotifyResult(targets.Count, targets.Count == 1 ? "1 aviso na fila de envio" : $"{targets.Count} avisos na fila de envio");
    }

    private async Task<List<Target>> TargetsAsync(Guid runId, Guid? payslipId, CancellationToken ct)
    {
        var rows = await (from p in db.Payslips.AsNoTracking()
                          join e in db.Employees.IgnoreQueryFilters().AsNoTracking() on p.EmployeeId equals e.Id
                          where p.PayrollRunId == runId && (payslipId == null || p.Id == payslipId)
                          orderby e.Name
                          select new { Payslip = p, e.Name, e.Registration, e.Email, Terminated = e.Status == EmployeeStatus.Terminated || e.IsDeleted }).ToListAsync(ct);
        var result = new List<Target>(rows.Count);
        foreach (var r in rows)
        {
            var user = await links.UserOfEmployeeAsync(r.Payslip.EmployeeId, ct);
            var active = user is null ? null : await users.EmailOfAsync(user.UserId, ct);
            result.Add(new Target(r.Payslip, r.Name, r.Registration, r.Email, r.Terminated, active, user is not null));
        }

        return result;
    }

    private static Error RunNotFound() => Error.NotFound("payroll_run_not_found", "Folha não encontrada.");
}
