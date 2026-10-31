using System.Globalization;
using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using BemAqui.HR.Contracts;
using BemAqui.HR.Domain;
using BemAqui.Identity.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.HR.Application.TimeClock;

/// <summary>
/// Ajustes do ponto (RH-01b): lançamento manual pelo RH, pedido do funcionário ("Meu ponto") e abono de ausência, todos com aprovação. O
/// pedido nasce <c>Pending</c> e nada muda no ponto até a aprovação (<see cref="ApproveAsync"/>), que cria a marcação <c>Manual</c>,
/// desconsidera a original (<c>Voided</c>, nunca apagada) ou faz valer o abono. Quem pede não aprova, nem o dono das marcações (salvo o
/// Proprietário único, com "autoaprovado"). A justificativa, o motivo e o documento nunca vão para log, evento ou notificação.
/// </summary>
public sealed partial class TimeAdjustmentService(
    IHrDb db,
    ITimesheetPeriodLock periods,
    IPermissionChecker permissions,
    IUserDirectory users,
    IUserEmployeeLink links,
    INotificationService notifications,
    AttachmentService attachments,
    ITenantContext tenant,
    ICurrentUser currentUser,
    TimeProvider clock)
{
    /// <summary>Dono de anexo do documento do abono (CA 8): leitura e escrita com <c>hr.view_health</c>, ou o próprio funcionário pela rota dele.</summary>
    public const string DocumentOwner = "TimeAdjustmentDocument";
    public const string HealthAction = "view_health";
    public const string PendingType = "hr.time_adjustment_pending";
    public const string DecidedType = "hr.time_adjustment_decided";
    public const string ApproverLink = "/app/rh/ponto/ajustes?pedido=";
    public const string MineLink = "/app/meu-ponto?aba=pedidos";
    public const string NotFoundCode = "time_adjustment_not_found";
    public const string DecidedCode = "time_adjustment_decided";
    public const string OwnCode = "own_time_adjustment";
    public const int MaxRows = 500;

    /// <summary>A lista única de ausências (CA 7).</summary>
    public static IReadOnlyList<AbsenceReasonOption> Reasons() =>
        AbsenceReasons.All.Select(r => new AbsenceReasonOption(r.Code, r.Label, r.AppliesTo, r.IsHealth, r.Justifies)).ToList();

    /// <summary>"Lançar marcação" e "Abonar ausência" do RH (CA 2 e 7), com <c>hr.create</c>: o lançador é o solicitante e o pedido também passa por aprovação.</summary>
    public async Task<Result<TimeAdjustmentView>> CreateForEmployeeAsync(Guid employeeId, TimeAdjustmentInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var created = await CreateManyAsync(employeeId, [input], TimeAdjustmentSource.HR, self: false, cancellationToken);
        if (created.IsFailure)
        {
            return created.Error!;
        }

        return created.Value[0];
    }

    /// <summary>Pedido do próprio funcionário (CA 5 e 7, ADR 0035): o funcionário vem da sessão; "Desconsiderar" só numa marcação dele.</summary>
    public async Task<Result<TimeAdjustmentView>> CreateMineAsync(EmployeeSummary employee, TimeAdjustmentInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(employee);
        ArgumentNullException.ThrowIfNull(input);
        var created = await CreateManyAsync(employee.Id, [input], TimeAdjustmentSource.Employee, self: true, cancellationToken);
        if (created.IsFailure)
        {
            return created.Error!;
        }

        return created.Value[0];
    }

    /// <summary>"Lançar o dia pela jornada" (CA 3): um pedido "Incluir marcação" por marcação, com a mesma justificativa, no mesmo comando.</summary>
    public async Task<Result<IReadOnlyList<TimeAdjustmentView>>> CreateDayAsync(Guid employeeId, TimeAdjustmentDayInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Entries is not { Count: > 0 } entries || entries.Count > 8)
        {
            return Invalid("entries", "Informe de 1 a 8 marcações do dia.");
        }

        var inputs = entries.Select(e => new TimeAdjustmentInput(TimeAdjustmentKind.AddEntry, input.Day, e.Time, e.Type, Justification: input.Justification)).ToList();
        return await CreateManyAsync(employeeId, inputs, TimeAdjustmentSource.HR, self: false, cancellationToken);
    }

    /// <summary>O diálogo do "Lançar o dia pela jornada" preenchido com a jornada do funcionário naquele dia (CA 3).</summary>
    public async Task<Result<ScheduleDayProposal>> ScheduleDayAsync(Guid employeeId, DateOnly day, CancellationToken cancellationToken = default)
    {
        if (!await db.Employees.AnyAsync(e => e.Id == employeeId, cancellationToken))
        {
            return EmployeeNotFound();
        }

        var days = await ScheduleDaysAsync(employeeId, cancellationToken);
        var existing = await db.TimeEntries.CountAsync(e => e.EmployeeId == employeeId && e.Day == day && e.Status == TimeEntryStatus.Valid, cancellationToken);
        var entries = TimeAdjustmentRules.ScheduleEntries(days, day).Select(e => new TimeAdjustmentDayEntry(e.Type, e.Time.ToString("HH:mm", CultureInfo.InvariantCulture))).ToList();
        return new ScheduleDayProposal(day, TimeClockRules.ScheduleText(days, day), entries, existing);
    }

    /// <summary>
    /// Valida todos e grava todos ou nenhum: data (CA 4), período fechado (CA 16), marcação do próprio funcionário (CA 6), motivo da lista
    /// única e dado de saúde (CA 7). Cada pedido avisa quem aprova (CA 17).
    /// </summary>
    private async Task<Result<IReadOnlyList<TimeAdjustmentView>>> CreateManyAsync(
        Guid employeeId, IReadOnlyList<TimeAdjustmentInput> inputs, TimeAdjustmentSource source, bool self, CancellationToken ct)
    {
        var employee = await db.Employees.AsNoTracking().Where(e => e.Id == employeeId)
            .Select(e => new { e.Id, e.Name, e.AdmissionDate, e.TerminationDate }).FirstOrDefaultAsync(ct);
        if (employee is null)
        {
            return EmployeeNotFound();
        }

        var now = clock.GetUtcNow();
        var today = BusinessTime.Today(clock);
        var oldestOpen = await periods.OldestOpenStartAsync(ct);
        var canHealth = self || await permissions.HasPermissionAsync("hr", HealthAction, ct);
        var built = new List<TimeAdjustment>();
        foreach (var input in inputs)
        {
            var justification = (input.Justification ?? string.Empty).Trim();
            if (TimeAdjustmentRules.JustificationError(justification) is { } bad)
            {
                return Invalid("justification", bad);
            }

            DateOnly day;
            DateTimeOffset? at = null;
            TimeEntryType? type = null;
            Guid? entryId = null;
            int? minutes = null;
            AbsenceReason? reason = null;
            switch (input.Kind)
            {
                case TimeAdjustmentKind.AddEntry:
                    if (input.Day is not { } addDay)
                    {
                        return Invalid("day", "Informe a data da marcação.");
                    }

                    if (!TimeOnly.TryParseExact(input.Time ?? string.Empty, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
                    {
                        return Invalid("time", "Informe a hora no formato 00:00.");
                    }

                    if (input.EntryType is not { } entryType || !Enum.IsDefined(entryType))
                    {
                        return Invalid("entryType", "Escolha entrada, saída para o intervalo, volta do intervalo ou saída.");
                    }

                    day = addDay;
                    at = ToUtc(addDay, time);
                    type = entryType;
                    if (TimeAdjustmentRules.IsFuture(at.Value, now))
                    {
                        return Invalid("time", TimeAdjustmentRules.FutureMessage);
                    }

                    break;
                case TimeAdjustmentKind.VoidEntry:
                    // CA 6: só marcação do próprio funcionário da rota (ou da sessão); a de outro responde como inexistente.
                    var entry = input.EntryId is { } id
                        ? await db.TimeEntries.AsNoTracking().Where(e => e.Id == id && e.EmployeeId == employeeId).Select(e => new { e.Id, e.Day, e.At, e.Type, e.Status }).FirstOrDefaultAsync(ct)
                        : null;
                    if (entry is null)
                    {
                        return Error.NotFound("time_entry_not_found", "Marcação não encontrada.");
                    }

                    if (entry.Status != TimeEntryStatus.Valid)
                    {
                        return Error.Conflict("time_entry_voided", "Esta marcação já foi desconsiderada.");
                    }

                    if (await db.TimeAdjustments.AnyAsync(a => a.EntryId == entry.Id && a.Status == TimeAdjustmentStatus.Pending, ct))
                    {
                        return Error.Conflict("time_adjustment_pending", "Já existe um pedido aguardando aprovação para esta marcação.");
                    }

                    day = entry.Day;
                    entryId = entry.Id;
                    type = entry.Type;
                    at = entry.At;
                    break;
                case TimeAdjustmentKind.Absence:
                    if (input.Day is not { } absenceDay)
                    {
                        return Invalid("day", "Informe o dia da ausência.");
                    }

                    if (input.AbsenceReason is not { } chosen || !AbsenceReasons.ForAbsence(chosen))
                    {
                        return Invalid("absenceReason", "Escolha o motivo na lista de ausências.");
                    }

                    if (AbsenceReasons.IsHealth(chosen) && !canHealth)
                    {
                        return Error.Forbidden(Error.Codes.Forbidden, "Só quem tem a permissão Ver dados de saúde registra abono com motivo de saúde.");
                    }

                    if (input.AbsenceMinutes is { } m && (m < 1 || m > TimeAdjustmentRules.MaxAbsenceMinutes))
                    {
                        return Invalid("absenceMinutes", "Informe as horas abonadas (até 24 h) ou deixe em branco para o dia inteiro.");
                    }

                    day = absenceDay;
                    minutes = input.AbsenceMinutes;
                    reason = chosen;
                    break;
                default:
                    return Invalid("kind", "Escolha incluir marcação, desconsiderar marcação ou abonar ausência.");
            }

            if (TimeAdjustmentRules.DateError(day, today, oldestOpen, employee.AdmissionDate, employee.TerminationDate) is { } dateError)
            {
                return Invalid("day", dateError);
            }

            if (await periods.CheckAsync(day, ct) is { } closed)
            {
                return closed;
            }

            built.Add(new TimeAdjustment
            {
                EmployeeId = employeeId, Kind = input.Kind, Day = day, EntryId = entryId, At = input.Kind == TimeAdjustmentKind.AddEntry ? at : null,
                EntryType = input.Kind == TimeAdjustmentKind.AddEntry ? type : null, AbsenceMinutes = minutes, AbsenceReason = reason, Justification = justification,
                Source = source, RequestedBy = currentUser.UserId, RequestedAt = now,
            });
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        db.TimeAdjustments.AddRange(built);
        await db.SaveChangesAsync(ct);
        foreach (var adjustment in built)
        {
            await NotifyApproversAsync(adjustment, employee.Name, ct);
        }

        await transaction.CommitAsync(ct);
        var ids = built.Select(b => b.Id).ToList();
        var views = await ViewsAsync(db.TimeAdjustments.AsNoTracking().Where(a => ids.Contains(a.Id)), self ? employeeId : null, ct);
        return Result<IReadOnlyList<TimeAdjustmentView>>.Success(views.OrderBy(v => ids.IndexOf(v.Id)).ToList());
    }

    /// <summary>Pedidos para a aprovação (CA 10, <c>hr.view</c>): por situação e funcionário; os pendentes primeiro, do mais antigo.</summary>
    public async Task<TimeAdjustmentList> ListAsync(TimeAdjustmentStatus? status, Guid? employeeId, CancellationToken cancellationToken = default)
    {
        var query = db.TimeAdjustments.AsNoTracking();
        if (status is { } s)
        {
            query = query.Where(a => a.Status == s);
        }

        if (employeeId is { } id)
        {
            query = query.Where(a => a.EmployeeId == id);
        }

        var pendingFirst = status == TimeAdjustmentStatus.Pending;
        var ordered = pendingFirst ? query.OrderBy(a => a.RequestedAt) : query.OrderByDescending(a => a.RequestedAt);
        var views = await ViewsAsync(ordered.Take(MaxRows + 1), null, cancellationToken);
        var items = (pendingFirst ? views.OrderBy(v => v.RequestedAt) : views.OrderBy(v => v.Status == TimeAdjustmentStatus.Pending ? 0 : 1).ThenByDescending(v => v.RequestedAt)).ToList();
        return new TimeAdjustmentList(items.Take(MaxRows).ToList(), items.Count > MaxRows);
    }

    /// <summary>Pedidos de um funcionário (aba Ponto); funcionário inexistente, 404.</summary>
    public async Task<Result<TimeAdjustmentList>> EmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        if (!await db.Employees.AnyAsync(e => e.Id == employeeId, cancellationToken))
        {
            return EmployeeNotFound();
        }

        return await ListAsync(null, employeeId, cancellationToken);
    }

    /// <summary>"Meus pedidos" (CA 5): os do funcionário da sessão, com tudo o que ele mesmo informou.</summary>
    public async Task<IReadOnlyList<TimeAdjustmentView>> MineAsync(EmployeeSummary employee, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(employee);
        var views = await ViewsAsync(db.TimeAdjustments.AsNoTracking().Where(a => a.EmployeeId == employee.Id).OrderByDescending(a => a.RequestedAt).Take(MaxRows), employee.Id, cancellationToken);
        return views.OrderByDescending(v => v.RequestedAt).ToList();
    }

    /// <summary>Detalhe com o dia antes e depois (CA 10). <paramref name="employeeId"/> da rota: o pedido precisa ser dele (IDOR, CA 18).</summary>
    public async Task<Result<TimeAdjustmentDetail>> DetailAsync(Guid id, Guid? employeeId = null, CancellationToken cancellationToken = default)
    {
        var query = db.TimeAdjustments.AsNoTracking().Where(a => a.Id == id);
        if (employeeId is { } owner)
        {
            query = query.Where(a => a.EmployeeId == owner);
        }

        var views = await ViewsAsync(query, null, cancellationToken);
        if (views.Count == 0)
        {
            return NotFound();
        }

        var view = views[0];
        return new TimeAdjustmentDetail(view, await ImpactAsync(view, cancellationToken));
    }

    /// <summary>
    /// Pedidos vistos por quem consulta (CA 9 e 13). <paramref name="selfEmployeeId"/>: o próprio funcionário vê tudo o que informou. Sem
    /// <c>hr.view_health</c>, o abono sai protegido (de qualquer motivo, para não deixar inferir qual é de saúde, como os afastamentos,
    /// PO 22/10/2026).
    /// </summary>
    private async Task<IReadOnlyList<TimeAdjustmentView>> ViewsAsync(IQueryable<TimeAdjustment> query, Guid? selfEmployeeId, CancellationToken ct)
    {
        var rows = await (from a in query
                          join e in db.Employees.AsNoTracking() on a.EmployeeId equals e.Id
                          select new { Adjustment = a, e.Name }).ToListAsync(ct);
        if (rows.Count == 0)
        {
            return [];
        }

        var canHealth = selfEmployeeId is not null || await permissions.HasPermissionAsync("hr", HealthAction, ct);
        var me = currentUser.UserId;
        var canApprove = selfEmployeeId is null && await permissions.HasPermissionAsync("hr", "approve", ct);
        var myEmployee = me is { } user && canApprove ? await links.EmployeeOfUserAsync(user, ct) : null;
        var sole = canApprove && me is { } actor && await SoleApproverAsync(actor, ct);
        var entryIds = rows.Where(r => r.Adjustment.EntryId is not null).Select(r => r.Adjustment.EntryId!.Value).Distinct().ToList();
        var entries = entryIds.Count == 0
            ? new Dictionary<Guid, (DateTimeOffset At, TimeEntryType Type)>()
            : (await db.TimeEntries.AsNoTracking().Where(t => entryIds.Contains(t.Id)).Select(t => new { t.Id, t.At, t.Type }).ToListAsync(ct)).ToDictionary(t => t.Id, t => (t.At, t.Type));
        var absences = rows.Where(r => r.Adjustment.Kind == TimeAdjustmentKind.Absence).Select(r => r.Adjustment.Id).ToList();
        var withDocument = await attachments.OwnersWithAttachmentsAsync(DocumentOwner, absences, ct);
        var names = await NamesAsync(rows.SelectMany(r => new[] { r.Adjustment.RequestedBy, r.Adjustment.DecidedBy }), ct);
        var result = new List<TimeAdjustmentView>(rows.Count);
        foreach (var row in rows)
        {
            var a = row.Adjustment;
            var hidden = a.Kind == TimeAdjustmentKind.Absence && !canHealth;
            var (at, type) = a.EntryId is { } eid && entries.TryGetValue(eid, out var original) ? (original.At, original.Type) : (a.At ?? default, a.EntryType ?? TimeEntryType.In);
            var local = a.Kind == TimeAdjustmentKind.Absence ? (DateTimeOffset?)null : BusinessTime.ToBusiness(at);
            var documents = a.Kind == TimeAdjustmentKind.Absence && !hidden && withDocument.Contains(a.Id) ? await attachments.ListCheckedAsync(DocumentOwner, a.Id, ct) : [];
            var involved = a.RequestedBy == me || (myEmployee is { } mine && mine == a.EmployeeId);
            result.Add(new TimeAdjustmentView(
                a.Id, a.EmployeeId, row.Name, a.Kind, a.Status, TimeAdjustmentRules.StatusLabel(a.Status), a.Day,
                TimeAdjustmentRules.Summary(a.Kind, type, local, a.AbsenceMinutes, a.AbsenceReason, !hidden),
                a.Kind == TimeAdjustmentKind.Absence ? null : at, a.Kind == TimeAdjustmentKind.Absence ? null : type, a.EntryId, a.AbsenceMinutes,
                hidden ? null : a.AbsenceReason, hidden || a.AbsenceReason is not { } reason ? (hidden ? TimeAdjustmentRules.ProtectedLabel : null) : AbsenceReasons.Label(reason),
                hidden ? null : a.Justification, hidden, withDocument.Contains(a.Id), documents, a.Source, a.RequestedBy, Name(names, a.RequestedBy), a.RequestedAt,
                a.DecidedBy, Name(names, a.DecidedBy), a.DecidedAt, hidden ? null : a.DecisionComment, a.SelfApproved, a.CreatedEntryId,
                a.Status == TimeAdjustmentStatus.Pending && canApprove && (!involved || sole),
                a.Status == TimeAdjustmentStatus.Pending && me is { } u && a.RequestedBy == u, a.Version));
        }

        return result;
    }

    /// <summary>O dia antes e depois (CA 10): as marcações válidas de agora, com a incluída ou sem a desconsiderada, e as horas pelos pares fechados.</summary>
    private async Task<TimeAdjustmentDayImpact> ImpactAsync(TimeAdjustmentView view, CancellationToken ct)
    {
        var all = await db.TimeEntries.AsNoTracking().Where(e => e.EmployeeId == view.EmployeeId && e.Day == view.Day)
            .OrderBy(e => e.At).Select(e => new { e.Id, e.At, e.Type, e.Source, e.Status, e.AdjustmentId, e.VoidAdjustmentId }).ToListAsync(ct);
        var approved = view.Status == TimeAdjustmentStatus.Approved;
        // Antes: o dia sem o efeito do ajuste; depois: com ele (já aprovado ou, no pendente, como ficaria).
        var before = all.Where(e => (e.Status == TimeEntryStatus.Valid || e.VoidAdjustmentId == view.Id) && e.AdjustmentId != view.Id)
            .Select(e => new TimeAdjustmentDayEntryView(e.Id, e.At, e.Type, TimeClockRules.Label(e.Type), e.Source, e.Id == view.EntryId)).ToList();
        var after = before.Where(e => !(view.Kind == TimeAdjustmentKind.VoidEntry && e.Id == view.EntryId)).Select(e => e with { Changed = false }).ToList();
        if (view.Kind == TimeAdjustmentKind.AddEntry && view.At is { } at && view.EntryType is { } type && view.Status is TimeAdjustmentStatus.Pending or TimeAdjustmentStatus.Approved)
        {
            after.Add(new TimeAdjustmentDayEntryView(approved ? view.CreatedEntryId : null, at, type, TimeClockRules.Label(type), TimeEntrySource.Manual, true));
        }
        else if (view.Kind == TimeAdjustmentKind.VoidEntry && view.Status is TimeAdjustmentStatus.Rejected or TimeAdjustmentStatus.Cancelled)
        {
            after = before.Select(e => e with { Changed = false }).ToList();
        }

        after = after.OrderBy(e => e.At).ToList();
        var absence = view.Kind == TimeAdjustmentKind.Absence && view.Status is TimeAdjustmentStatus.Pending or TimeAdjustmentStatus.Approved;
        return new TimeAdjustmentDayImpact(before, after, TimeAdjustmentRules.WorkedMinutes(before.Select(e => (e.At, e.Type))), TimeAdjustmentRules.WorkedMinutes(after.Select(e => (e.At, e.Type))),
            absence ? view.AbsenceMinutes : null, absence && view.AbsenceMinutes is null);
    }

    /// <summary>
    /// CA 17: quem tem <c>hr.approve</c> recebe "Ajuste de ponto de {funcionário} para aprovar" com o link do pedido, uma vez por pedido
    /// (<c>dedup_key</c>), na transação do pedido; nunca a justificativa nem o motivo. Quem pediu não recebe, salvo se for o único aprovador.
    /// </summary>
    private async Task NotifyApproversAsync(TimeAdjustment adjustment, string employeeName, CancellationToken ct)
    {
        var approvers = await users.UsersWithPermissionAsync(TimeAdjustmentRules.ApprovePermission, ct);
        var targets = approvers.Where(a => a != adjustment.RequestedBy).ToList();
        if (targets.Count == 0)
        {
            targets = [.. approvers];
        }

        foreach (var user in targets)
        {
            await notifications.NotifyOnceAsync(user, PendingType, TimeAdjustmentRules.ApproverNotice(employeeName), ApproverLink + adjustment.Id, TimeAdjustmentRules.DedupKey(adjustment.Id), ct);
        }
    }

    /// <summary>Proprietário único (CA 13): nenhum outro usuário ativo com <c>hr.approve</c>.</summary>
    private async Task<bool> SoleApproverAsync(Guid actor, CancellationToken ct) =>
        await SoleOwnerRule.NoOtherEligibleAsync(users, TimeAdjustmentRules.ApprovePermission, actor, [actor], ct);

    private async Task<Dictionary<Guid, string?>> NamesAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var names = new Dictionary<Guid, string?>();
        foreach (var id in ids.OfType<Guid>().Distinct())
        {
            names[id] = await users.DisplayNameAsync(id, ct);
        }

        return names;
    }

    private static string? Name(Dictionary<Guid, string?> names, Guid? id) => id is { } key ? names.GetValueOrDefault(key) : null;

    private async Task<IReadOnlyList<ScheduleDay>?> ScheduleDaysAsync(Guid employeeId, CancellationToken ct)
    {
        var ids = await db.Employees.AsNoTracking().Where(e => e.Id == employeeId)
            .Select(e => new { e.WorkScheduleId, Position = db.JobPositions.Where(j => j.Id == e.PositionId).Select(j => j.WorkScheduleId).FirstOrDefault() })
            .FirstOrDefaultAsync(ct);
        var scheduleId = ids?.WorkScheduleId ?? ids?.Position;
        return scheduleId is { } sid ? (await db.WorkSchedules.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sid, ct))?.Days : null;
    }

    /// <summary>Hora local (São Paulo) do dia para UTC.</summary>
    internal static DateTimeOffset ToUtc(DateOnly day, TimeOnly time)
    {
        var local = day.ToDateTime(time, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, BusinessTime.Zone.GetUtcOffset(local)).ToUniversalTime();
    }

    private string StoragePrefix() => (tenant.Tenant?.Id ?? Guid.Empty).ToString("N");

    internal static Error Invalid(string field, string message) =>
        Error.Validation(Error.Codes.Validation, message, new Dictionary<string, string[]> { [field] = [message] });

    internal static Error NotFound() => Error.NotFound(NotFoundCode, "Pedido de ajuste não encontrado.");

    private static Error EmployeeNotFound() => Error.NotFound("employee_not_found", "Funcionário não encontrado.");
}
