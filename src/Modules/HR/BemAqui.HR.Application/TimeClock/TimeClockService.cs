using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Time;
using BemAqui.HR.Contracts;
using BemAqui.HR.Domain;
using BemAqui.Identity.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.HR.Application.TimeClock;

/// <summary>
/// Ponto do app como <b>controle interno de jornada</b> (RH-01a, ADR 0035 §2 e §3): "Meu ponto" do funcionário da sessão (hoje, marcar,
/// ciência da geolocalização e "Minhas marcações") e a consulta do RH por funcionário e por dia. A hora é sempre a do servidor; a
/// localização é <c>[PersonalData]</c>, só gravada com a geolocalização ligada, a ciência dada e fora do aparelho registrado, e só sai
/// pela rota <c>/sensitive</c> (ou para o próprio funcionário).
/// </summary>
public sealed class TimeClockService(
    IHrDb db,
    HrParametersService parameters,
    IShopfloorDevices devices,
    IUserDirectory users,
    IAuditWriter audit,
    ICurrentUser currentUser,
    TimeProvider clock)
{
    /// <summary>Entidade das leituras sensíveis da localização (CA 11) e das próprias marcações (CA 13).</summary>
    public const string LocationReadEntity = "TimeEntry.Location";
    public const string MineReadEntity = "TimeEntry.Mine";

    /// <summary>Teto de linhas da consulta do RH.</summary>
    public const int MaxRows = 3000;

    public async Task<MyTimeClock> TodayAsync(EmployeeSummary employee, TimeClockOrigin origin, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(employee);
        ArgumentNullException.ThrowIfNull(origin);
        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(BusinessTime.ToBusiness(now).DateTime);
        var entries = await db.TimeEntries.AsNoTracking()
            .Where(e => e.EmployeeId == employee.Id && e.Day == today && e.Status == TimeEntryStatus.Valid)
            .OrderBy(e => e.At)
            .Select(e => new { e.Id, e.At, e.Type, e.Source, e.Location })
            .ToListAsync(cancellationToken);
        var schedule = await ScheduleDaysAsync(employee.Id, cancellationToken);
        var last = entries.Count > 0 ? entries[^1].Type : (TimeEntryType?)null;
        var outOfSequence = entries.Select((e, i) => TimeClockRules.IsOutOfSequence(i == 0 ? null : entries[i - 1].Type, e.Type)).Any(x => x);
        var geolocation = origin.DeviceId is null ? await parameters.TimeClockGeolocationAsync(cancellationToken) : TimeClockGeolocation.Off;
        var consentNeeded = geolocation == TimeClockGeolocation.Ask && !await HasConsentAsync(employee.Id, cancellationToken);
        var suggested = TimeClockRules.Suggest(last);
        return new MyTimeClock(
            TimeClockRules.InternalControlBanner,
            TimeClockRules.FirstName(employee.Name),
            now,
            today,
            TimeClockRules.ScheduleText(schedule, today),
            entries.Select(e => new MyTimeEntryToday(e.Id, e.At, e.Type, TimeClockRules.Label(e.Type), e.Source, e.Location)).ToList(),
            suggested,
            TimeClockRules.Verb(suggested),
            outOfSequence,
            outOfSequence ? TimeClockRules.OutOfSequenceHint : null,
            geolocation,
            consentNeeded,
            TimeClockRules.GeoConsentText,
            TimeClockRules.GeoConsentVersion,
            origin.DeviceId is not null);
    }

    /// <summary>
    /// Marcar (CA 3 a 5 e 9): hora do servidor, sequência livre (fora de ordem vira inconsistência no espelho), duplo toque do mesmo tipo
    /// em menos de 1 minuto recusado com 409. Uma marcação por vez por funcionário (trava na transação). <paramref name="withinTransaction"/>
    /// roda antes do commit: no tablet, o fim da sessão por PIN grava junto com a marcação (adendo à ADR 0026).
    /// </summary>
    public async Task<Result<TimeEntryReceipt>> RecordAsync(
        EmployeeSummary employee, TimeEntryInput input, TimeClockOrigin origin, Func<CancellationToken, Task<bool>>? withinTransaction = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(employee);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(origin);
        if (!Enum.IsDefined(input.Type))
        {
            return Error.Validation("time_entry_type", "Escolha entrada, saída para o intervalo, volta do intervalo ou saída.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var lockKey = "hr.time_entry:" + employee.Id.ToString("N");
        await db.Database.SqlQuery<int>($"SELECT 1 AS \"Value\" FROM (SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))) AS l").ToListAsync(cancellationToken);

        var now = clock.GetUtcNow();
        var local = BusinessTime.ToBusiness(now);
        var day = DateOnly.FromDateTime(local.DateTime);
        var since = now - TimeClockRules.DuplicateWindow;
        var previous = await db.TimeEntries.AsNoTracking()
            .Where(e => e.EmployeeId == employee.Id && e.Type == input.Type && e.Status == TimeEntryStatus.Valid && e.At > since)
            .OrderByDescending(e => e.At).Select(e => (DateTimeOffset?)e.At).FirstOrDefaultAsync(cancellationToken);
        if (TimeClockRules.IsDuplicate(previous, now))
        {
            return Error.Conflict(TimeClockRules.DuplicateCode, TimeClockRules.DuplicateMessage(input.Type, BusinessTime.ToBusiness(previous!.Value)));
        }

        var last = await db.TimeEntries.AsNoTracking()
            .Where(e => e.EmployeeId == employee.Id && e.Day == day && e.Status == TimeEntryStatus.Valid)
            .OrderByDescending(e => e.At).Select(e => (TimeEntryType?)e.Type).FirstOrDefaultAsync(cancellationToken);

        // Localização (CA 9 a 11): só com a geolocalização ligada, fora do aparelho registrado e com a ciência da versão atual; senão é
        // ignorada. Pedida e sem coordenada válida (negada ou com erro), "Sem localização": nunca bloqueia a marcação.
        var location = TimeEntryLocation.NotAsked;
        (decimal Latitude, decimal Longitude, int? AccuracyM)? coordinates = null;
        if (origin.DeviceId is null && await parameters.TimeClockGeolocationAsync(cancellationToken) == TimeClockGeolocation.Ask)
        {
            coordinates = await HasConsentAsync(employee.Id, cancellationToken) ? TimeClockRules.Normalize(input.Latitude, input.Longitude, input.AccuracyM) : null;
            location = coordinates is null ? TimeEntryLocation.Missing : TimeEntryLocation.Captured;
        }

        var entry = new TimeEntry
        {
            EmployeeId = employee.Id,
            At = now,
            Day = day,
            Type = input.Type,
            Source = origin.Pin ? TimeEntrySource.Pin : TimeEntrySource.App,
            RecordedByUserId = currentUser.UserId,
            DeviceId = origin.DeviceId,
            Location = location,
            Latitude = coordinates?.Latitude,
            Longitude = coordinates?.Longitude,
            AccuracyM = coordinates?.AccuracyM,
            CreatedAt = now,
        };
        db.TimeEntries.Add(entry);
        await db.SaveChangesAsync(cancellationToken);
        var ended = withinTransaction is not null && await withinTransaction(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var outOfSequence = TimeClockRules.IsOutOfSequence(last, input.Type);
        return new TimeEntryReceipt(entry.Id, entry.Type, entry.At, entry.Day, entry.Source, entry.Location, TimeClockRules.ReceiptTitle,
            TimeClockRules.ReceiptText(entry.Type, local), TimeClockRules.Done(entry.Type), outOfSequence, outOfSequence ? TimeClockRules.OutOfSequenceHint : null, ended);
    }

    /// <summary>"Entendi e continuar" (CA 10): grava a ciência da versão atual do texto (uma vez por versão).</summary>
    public async Task<GeoConsentResult> AcceptGeoConsentAsync(EmployeeSummary employee, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(employee);
        var existing = await db.TimeClockGeoConsents.AsNoTracking()
            .Where(c => c.EmployeeId == employee.Id && c.TextVersion == TimeClockRules.GeoConsentVersion)
            .OrderBy(c => c.AcceptedAt).Select(c => (DateTimeOffset?)c.AcceptedAt).FirstOrDefaultAsync(cancellationToken);
        if (existing is { } at)
        {
            return new GeoConsentResult(TimeClockRules.GeoConsentVersion, at);
        }

        var consent = new TimeClockGeoConsent { EmployeeId = employee.Id, UserId = currentUser.UserId, AcceptedAt = clock.GetUtcNow(), TextVersion = TimeClockRules.GeoConsentVersion };
        db.TimeClockGeoConsents.Add(consent);
        await db.SaveChangesAsync(cancellationToken);
        return new GeoConsentResult(consent.TextVersion, consent.AcceptedAt);
    }

    /// <summary>"Minhas marcações" (CA 13): as do funcionário da sessão nos últimos 60 dias, com a própria localização; a leitura fica registrada.</summary>
    public async Task<IReadOnlyList<MyTimeEntry>> MineAsync(EmployeeSummary employee, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(employee);
        var from = BusinessTime.Today(clock).AddDays(-(TimeClockRules.MineDays - 1));
        var rows = await db.TimeEntries.AsNoTracking()
            .Where(e => e.EmployeeId == employee.Id && e.Day >= from)
            .OrderByDescending(e => e.At)
            .ToListAsync(cancellationToken);
        var names = await DeviceNamesAsync(rows.Select(r => r.DeviceId), cancellationToken);
        await audit.RecordReadAsync(MineReadEntity, employee.Id.ToString(), cancellationToken);
        return rows.Select(e => new MyTimeEntry(e.Id, e.At, e.Day, e.Type, e.Source, e.DeviceId is { } d ? names.GetValueOrDefault(d) : null, e.Location, e.Status, e.Latitude, e.Longitude, e.AccuracyM)).ToList();
    }

    /// <summary>
    /// Consulta do RH (CA 12), com <c>hr.view</c>: marcações do período (até 62 dias), por funcionário e por centro (o do cargo), sem a
    /// localização. Período invertido ou longo demais, 422.
    /// </summary>
    public async Task<Result<TimeEntryList>> ListAsync(DateOnly? from, DateOnly? to, Guid? employeeId, Guid? workCenterId, CancellationToken cancellationToken = default)
    {
        var range = Range(from, to);
        if (range.IsFailure)
        {
            return range.Error!;
        }

        var (start, end) = range.Value;
        var query = from e in db.TimeEntries.AsNoTracking()
                    join p in db.Employees.AsNoTracking() on e.EmployeeId equals p.Id
                    where e.Day >= start && e.Day <= end
                    select new { Entry = e, p.Name, p.PositionId };
        if (employeeId is { } id)
        {
            query = query.Where(x => x.Entry.EmployeeId == id);
        }

        if (workCenterId is { } center)
        {
            query = query.Where(x => db.JobPositions.Any(j => j.Id == x.PositionId && j.DefaultWorkCenterId == center));
        }

        var rows = await query.OrderByDescending(x => x.Entry.Day).ThenBy(x => x.Name).ThenBy(x => x.Entry.At).Take(MaxRows + 1).ToListAsync(cancellationToken);
        var names = await DeviceNamesAsync(rows.Select(r => r.Entry.DeviceId), cancellationToken);
        // RH-01b CA 13 e 19: a marca "autoaprovado" da marcação criada por ajuste e o texto da desconsiderada, com o aprovador e o motivo.
        var adjustmentIds = rows.SelectMany(r => new[] { r.Entry.AdjustmentId, r.Entry.VoidAdjustmentId }).OfType<Guid>().Distinct().ToList();
        var adjustments = adjustmentIds.Count == 0
            ? []
            : await db.TimeAdjustments.AsNoTracking().Where(a => adjustmentIds.Contains(a.Id)).Select(a => new { a.Id, a.SelfApproved, a.Justification }).ToListAsync(cancellationToken);
        var byId = adjustments.ToDictionary(a => a.Id);
        var approvers = new Dictionary<Guid, string?>();
        foreach (var approver in rows.Select(r => r.Entry.VoidedBy).OfType<Guid>().Distinct())
        {
            approvers[approver] = await users.DisplayNameAsync(approver, cancellationToken);
        }

        var items = rows.Take(MaxRows).Select(x => new TimeEntryRow(x.Entry.Id, x.Entry.EmployeeId, x.Name, x.Entry.Day, x.Entry.At, x.Entry.Type, x.Entry.Source,
            x.Entry.DeviceId is { } d ? names.GetValueOrDefault(d) : null, x.Entry.Location, x.Entry.Status,
            x.Entry.AdjustmentId, x.Entry.AdjustmentId is { } created && byId.TryGetValue(created, out var origin) && origin.SelfApproved, x.Entry.VoidAdjustmentId,
            x.Entry.Status == TimeEntryStatus.Voided && x.Entry.VoidedAt is { } voidedAt
                ? TimeAdjustmentRules.VoidedNote(x.Entry.VoidedBy is { } by ? approvers.GetValueOrDefault(by) : null, BusinessTime.ToBusiness(voidedAt),
                    x.Entry.VoidAdjustmentId is { } v && byId.TryGetValue(v, out var voiding) ? voiding.Justification : null)
                : null)).ToList();
        return new TimeEntryList(start, end, items, rows.Count > MaxRows);
    }

    /// <summary>Marcações de um funcionário (aba "Ponto", CA 12); funcionário inexistente, 404.</summary>
    public async Task<Result<TimeEntryList>> EmployeeAsync(Guid employeeId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default)
    {
        if (!await db.Employees.AnyAsync(e => e.Id == employeeId, cancellationToken))
        {
            return Error.NotFound("employee_not_found", "Funcionário não encontrado.");
        }

        return await ListAsync(from, to, employeeId, null, cancellationToken);
    }

    /// <summary>
    /// Localização das marcações de um funcionário (CA 11), com <c>hr.view_sensitive</c>: grava <c>SensitiveRead</c> (quem, quando e o IP,
    /// sem as coordenadas).
    /// </summary>
    public async Task<Result<IReadOnlyList<TimeEntryLocationRow>>> LocationsAsync(Guid employeeId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default)
    {
        if (!await db.Employees.AnyAsync(e => e.Id == employeeId, cancellationToken))
        {
            return Error.NotFound("employee_not_found", "Funcionário não encontrado.");
        }

        var range = Range(from, to);
        if (range.IsFailure)
        {
            return range.Error!;
        }

        var (start, end) = range.Value;
        var rows = await db.TimeEntries.AsNoTracking()
            .Where(e => e.EmployeeId == employeeId && e.Day >= start && e.Day <= end && e.Location != TimeEntryLocation.NotAsked)
            .OrderByDescending(e => e.At)
            .Select(e => new TimeEntryLocationRow(e.Id, e.At, e.Type, e.Location, e.Latitude, e.Longitude, e.AccuracyM))
            .ToListAsync(cancellationToken);
        await audit.RecordReadAsync(LocationReadEntity, employeeId.ToString(), cancellationToken);
        return rows;
    }

    private Result<(DateOnly From, DateOnly To)> Range(DateOnly? from, DateOnly? to)
    {
        var end = to ?? BusinessTime.Today(clock);
        var start = from ?? end.AddDays(-6);
        if (start > end)
        {
            return Error.Validation("time_entry_range", "A data inicial vem antes da final.", new Dictionary<string, string[]> { ["from"] = ["A data inicial vem antes da final."] });
        }

        if (end.DayNumber - start.DayNumber + 1 > TimeClockRules.MaxRangeDays)
        {
            return Error.Validation("time_entry_range", $"Escolha um período de até {TimeClockRules.MaxRangeDays} dias.", new Dictionary<string, string[]> { ["from"] = [$"Escolha um período de até {TimeClockRules.MaxRangeDays} dias."] });
        }

        return (start, end);
    }

    private async Task<IReadOnlyDictionary<Guid, string>> DeviceNamesAsync(IEnumerable<Guid?> ids, CancellationToken cancellationToken)
    {
        var list = ids.OfType<Guid>().Distinct().ToList();
        return list.Count == 0 ? new Dictionary<Guid, string>() : await devices.NamesAsync(list, cancellationToken);
    }

    private Task<bool> HasConsentAsync(Guid employeeId, CancellationToken cancellationToken) =>
        db.TimeClockGeoConsents.AnyAsync(c => c.EmployeeId == employeeId && c.TextVersion == TimeClockRules.GeoConsentVersion, cancellationToken);

    /// <summary>Dias da jornada do funcionário (a dele ou a do cargo).</summary>
    private async Task<IReadOnlyList<ScheduleDay>?> ScheduleDaysAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        var ids = await db.Employees.AsNoTracking().Where(e => e.Id == employeeId)
            .Select(e => new { e.WorkScheduleId, Position = db.JobPositions.Where(j => j.Id == e.PositionId).Select(j => j.WorkScheduleId).FirstOrDefault() })
            .FirstOrDefaultAsync(cancellationToken);
        var scheduleId = ids?.WorkScheduleId ?? ids?.Position;
        if (scheduleId is not { } sid)
        {
            return null;
        }

        return (await db.WorkSchedules.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sid, cancellationToken))?.Days;
    }
}
