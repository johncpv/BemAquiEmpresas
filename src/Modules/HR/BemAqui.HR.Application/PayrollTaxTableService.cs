using System.Globalization;
using System.Text.Json;
using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Time;
using BemAqui.HR.Contracts;
using BemAqui.HR.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.HR.Application;

/// <summary>
/// Tabelas legais da folha por vigência (FLH-01, ADR 0039 §1): ver com <c>hr.view</c>; cadastrar, editar e retificar com <c>hr.edit</c>
/// (na rota) e <c>hr.view_sensitive</c> (aqui); conferir com <c>hr.approve</c> (na rota), por quem não cadastrou nem editou (exceção do
/// Proprietário único com o dataset da contadora). Versão conferida não muda: corrige-se por retificação, com nova conferência.
/// </summary>
public sealed class PayrollTaxTableService(
    IHrDb db,
    IPermissionChecker permissions,
    IUserDirectory users,
    IEventPublisher events,
    ICurrentUser currentUser,
    TimeProvider clock) : IPayrollTaxTables
{
    public const string CurrentIndex = "ix_payroll_tax_tables_current";
    public const string PendingRectifyIndex = "ix_payroll_tax_tables_pending_rectify";
    public const string SeedIndex = "ix_payroll_tax_tables_seed";
    public const string Link = "/app/rh/tabelas-folha";

    private static readonly PayrollTaxTableKind[] Kinds = Enum.GetValues<PayrollTaxTableKind>();

    public async Task<PayrollTaxTablesView> ListAsync(CancellationToken cancellationToken = default)
    {
        var all = await db.PayrollTaxTables.AsNoTracking().ToListAsync(cancellationToken);
        var today = BusinessTime.Today(clock);
        var names = await NamesAsync(all, cancellationToken);
        var pending = PayrollTaxPackages.PendingNotes();
        var sections = Kinds.Select(kind =>
        {
            var versions = all.Where(t => t.Kind == kind).OrderByDescending(t => t.ValidFrom).ThenBy(t => t.CreatedAt).ToList();
            var inForce = PayrollTaxRules.Pick(versions, kind, today);
            return new PayrollTaxTableSection(kind, PayrollTaxRules.KindLabel(kind), inForce?.Id,
                versions.Select(v => Detail(v, names, today, inForce?.Id)).ToList(), versions.Count == 0 ? pending.GetValueOrDefault(kind) : null);
        }).ToList();
        var approvers = await users.UsersWithPermissionAsync("hr.approve", cancellationToken);
        var single = approvers.Count == 1 && approvers[0] == currentUser.UserId;
        decimal? suggested = null;
        if (await CanSensitiveAsync(cancellationToken))
        {
            var salaries = await db.Employees.AsNoTracking().Where(e => e.Status == EmployeeStatus.Active && e.BaseSalary > 0m).Select(e => e.BaseSalary).ToListAsync(cancellationToken);
            suggested = salaries.Count == 0 ? null : PayrollRules.Round(salaries.Average());
        }

        return new PayrollTaxTablesView(sections, all.Count(t => t.Status == PayrollTaxTableStatus.Unconfirmed), single, suggested);
    }

    public async Task<Result<PayrollTaxTableDetail>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var table = await db.PayrollTaxTables.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        return table is null ? NotFound() : await DetailAsync(table, cancellationToken);
    }

    /// <summary>Cadastrar (CA 2 a 4): nasce <c>Unconfirmed</c> ("A confirmar com a contadora"); uma vigente por tipo e mês.</summary>
    public async Task<Result<PayrollTaxTableDetail>> CreateAsync(PayrollTaxTableInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await CanSensitiveAsync(cancellationToken))
        {
            return Forbidden();
        }

        if (input.Kind is not { } kind || !Enum.IsDefined(kind))
        {
            return Error.Validation(Error.Codes.Validation, "Escolha o tipo da tabela.", new Dictionary<string, string[]> { ["kind"] = ["Escolha o tipo da tabela."] });
        }

        if (Invalid(kind, input.ValidFrom, input.Content, input.Rounding, input.Source) is { } invalid)
        {
            return invalid;
        }

        if (await DuplicateAsync(kind, input.ValidFrom!.Value, null, cancellationToken))
        {
            return Duplicate(kind, input.ValidFrom.Value);
        }

        var table = new PayrollTaxTable
        {
            Kind = kind, ValidFrom = input.ValidFrom.Value, Rounding = input.Rounding!.Value, Source = input.Source!.Trim(),
            CreatedBy = currentUser.UserId, CreatedAt = clock.GetUtcNow(),
        };
        table.SetContent(input.Content!);
        db.PayrollTaxTables.Add(table);
        return await SaveAsync(table, cancellationToken);
    }

    /// <summary>
    /// Editar (CA 7): só a versão <c>Unconfirmed</c>, com auditoria antes/depois; continua "A confirmar" e quem editou passa a não conferir.
    /// A conferida (e a substituída) não muda: 409 com a saída (nova versão ou retificação).
    /// </summary>
    public async Task<Result<PayrollTaxTableDetail>> UpdateAsync(Guid id, PayrollTaxTableInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await CanSensitiveAsync(cancellationToken))
        {
            return Forbidden();
        }

        var table = await db.PayrollTaxTables.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (table is null)
        {
            return NotFound();
        }

        if (table.Status != PayrollTaxTableStatus.Unconfirmed)
        {
            return Error.Conflict(PayrollTaxRules.ImmutableCode, PayrollTaxRules.ImmutableMessage);
        }

        var validFrom = input.ValidFrom ?? table.ValidFrom;
        if (table.RectifiesId is not null && validFrom != table.ValidFrom)
        {
            return Field("validFrom", "A retificação mantém a vigência da versão retificada.");
        }

        if (Invalid(table.Kind, validFrom, input.Content, input.Rounding, input.Source) is { } invalid)
        {
            return invalid;
        }

        if (table.RectifiesId is null && validFrom != table.ValidFrom && await DuplicateAsync(table.Kind, validFrom, table.Id, cancellationToken))
        {
            return Duplicate(table.Kind, validFrom);
        }

        table.ValidFrom = validFrom;
        table.SetContent(input.Content!);
        table.Rounding = input.Rounding!.Value;
        table.Source = input.Source!.Trim();
        table.MarkEdited(currentUser.UserId, clock.GetUtcNow());
        if (input.Version is { } version)
        {
            db.Entry(table).Property(t => t.Version).OriginalValue = version;
        }

        return await SaveAsync(table, cancellationToken);
    }

    /// <summary>
    /// "Marcar como conferido" (CA 5, 6 e 13): quem cadastrou ou editou recebe 403, menos o Proprietário único, que confere só com o
    /// dataset da contadora batendo no simulador em todos os casos. A retificação conferida substitui a anterior (<c>Superseded</c>).
    /// </summary>
    public async Task<Result<PayrollTaxTableDetail>> CheckAsync(Guid id, PayrollTaxTableCheckInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (currentUser.UserId is not { } me)
        {
            return Forbidden();
        }

        var table = await db.PayrollTaxTables.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (table is null)
        {
            return NotFound();
        }

        if (table.Status != PayrollTaxTableStatus.Unconfirmed)
        {
            return Error.Conflict(PayrollTaxRules.ImmutableCode, table.Status == PayrollTaxTableStatus.Active ? "Esta tabela já foi conferida." : "Esta versão foi substituída por uma retificação.");
        }

        var accountant = (input.Accountant ?? string.Empty).Trim();
        if (accountant.Length is 0 or > 100)
        {
            return Field("accountant", "Informe com quem a tabela foi conferida (até 100 caracteres).");
        }

        if (input.CheckedOn is not { } on || on > BusinessTime.Today(clock))
        {
            return Field("checkedOn", "Informe a data da conferência (hoje ou antes).");
        }

        var note = $"Conferido com {accountant} em {on.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}";
        Guid? datasetId = null;
        if (table.CreatedOrEditedBy(me))
        {
            var approvers = await users.UsersWithPermissionAsync("hr.approve", cancellationToken);
            if (approvers.Count != 1 || approvers[0] != me)
            {
                return Error.Forbidden(PayrollTaxRules.SelfCheckCode, PayrollTaxRules.SelfCheckMessage);
            }

            var dataset = await DatasetAsync(table, input.Dataset, me, cancellationToken);
            if (dataset.IsFailure)
            {
                return dataset.Error!;
            }

            datasetId = dataset.Value;
            note = PayrollTaxRules.SingleOwnerNote + ". " + note;
        }

        db.Entry(table).Property(t => t.Version).OriginalValue = input.Version;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (table.RectifiesId is { } rectified && await db.PayrollTaxTables.FirstOrDefaultAsync(t => t.Id == rectified, cancellationToken) is { } previous)
        {
            // A anterior sai do índice da vigente antes de a retificação entrar (o índice único confere linha a linha).
            previous.Supersede(clock.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
        }

        table.Check(me, clock.GetUtcNow(), note, datasetId);
        events.Publish(new PayrollTaxTableActivated(table.Kind.ToString(), table.ValidFrom, table.Id));
        var saved = await SaveAsync(table, cancellationToken);
        if (saved.IsSuccess)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return saved;
    }

    /// <summary>
    /// Retificar (CA 13): só a versão <c>Active</c>, com motivo; nasce uma versão <c>Unconfirmed</c> com a mesma vigência, que passa por
    /// nova conferência. Até lá o cálculo continua com a conferida. Uma retificação pendente por versão.
    /// </summary>
    public async Task<Result<PayrollTaxTableDetail>> RectifyAsync(Guid id, PayrollTaxTableRectifyInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await CanSensitiveAsync(cancellationToken))
        {
            return Forbidden();
        }

        var source = await db.PayrollTaxTables.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (source is null)
        {
            return NotFound();
        }

        if (source.Status != PayrollTaxTableStatus.Active)
        {
            return Error.Conflict(PayrollTaxRules.ImmutableCode, source.Status == PayrollTaxTableStatus.Unconfirmed
                ? "Tabela a confirmar não precisa de retificação: edite os valores."
                : "Esta versão já foi substituída. Retifique a vigente.");
        }

        if (source.Version != input.Version)
        {
            return Error.Conflict("concurrency_conflict", "A tabela da folha mudou desde que você abriu. Recarregue.");
        }

        var reason = (input.Reason ?? string.Empty).Trim();
        if (reason.Length is < 5 or > PayrollTaxRules.ReasonMaxLength)
        {
            return Field("reason", "Informe o motivo da retificação, de 5 a 200 caracteres.");
        }

        if (PayrollTaxRules.HasPendingRectification(await db.PayrollTaxTables.AsNoTracking().Where(t => t.RectifiesId == id).ToListAsync(cancellationToken), id))
        {
            return Error.Conflict(PayrollTaxRules.RectifyPendingCode, "Já existe uma retificação desta tabela a conferir. Edite ou confira a retificação.");
        }

        var content = input.Content ?? source.ReadContent();
        var rounding = input.Rounding ?? source.Rounding;
        var text = string.IsNullOrWhiteSpace(input.Source) ? source.Source : input.Source;
        if (Invalid(source.Kind, source.ValidFrom, content, rounding, text) is { } invalid)
        {
            return invalid;
        }

        var table = new PayrollTaxTable
        {
            Kind = source.Kind, ValidFrom = source.ValidFrom, RectifiesId = source.Id, RectifyReason = reason, Rounding = rounding, Source = text!.Trim(),
            CreatedBy = currentUser.UserId, CreatedAt = clock.GetUtcNow(),
        };
        table.SetContent(content);
        db.PayrollTaxTables.Add(table);
        return await SaveAsync(table, cancellationToken);
    }

    /// <summary>Folhas calculadas na vigência da versão (padrão 7): as em rascunho e as fechadas (estas nunca são recalculadas).</summary>
    public async Task<Result<PayrollTaxTableImpact>> ImpactAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var table = await db.PayrollTaxTables.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (table is null)
        {
            return NotFound();
        }

        var next = await db.PayrollTaxTables.AsNoTracking()
            .Where(t => t.Kind == table.Kind && t.ValidFrom > table.ValidFrom && t.Status != PayrollTaxTableStatus.Superseded && !(t.Status == PayrollTaxTableStatus.Unconfirmed && t.RectifiesId != null))
            .OrderBy(t => t.ValidFrom).Select(t => (DateOnly?)t.ValidFrom).FirstOrDefaultAsync(cancellationToken);
        var runs = await db.PayrollRuns.AsNoTracking()
            .Where(r => r.Mode == PayrollRunMode.Calculated && r.Period >= table.ValidFrom && (next == null || r.Period < next))
            .OrderBy(r => r.Period).Select(r => new PayrollTaxImpactRun(r.Id, r.Period, r.Type, r.Status)).ToListAsync(cancellationToken);
        return new PayrollTaxTableImpact(runs.Where(r => r.Status == PayrollRunStatus.Draft).ToList(), runs.Where(r => r.Status != PayrollRunStatus.Draft).ToList());
    }

    /// <summary>Simulador (CA 10): pelas versões que o motor usaria na competência, com a memória; as que faltam vêm listadas.</summary>
    public async Task<Result<PayrollTaxSimulation>> SimulateAsync(PayrollTaxSimulationInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (PayrollRules.ParsePeriod(input.Competence) is not { } competence)
        {
            return Field("competence", "Informe a competência (mês e ano).");
        }

        if (!PayrollTaxRules.ValidMoney(input.Base))
        {
            return Field("base", "Informe a base: zero ou mais, com até 2 casas.");
        }

        if (input.Dependents is < 0 or > 99 || input.Children is < 0 or > 99)
        {
            return Field("dependents", "Dependentes e filhos: de 0 a 99.");
        }

        var all = await db.PayrollTaxTables.AsNoTracking().ToListAsync(cancellationToken);
        var lines = new List<PayrollTaxSimulationLine>();
        var missing = new List<string>();
        decimal? inss = null;
        foreach (var kind in Kinds)
        {
            if (PayrollTaxRules.Pick(all, kind, competence) is not { } table)
            {
                missing.Add(PayrollTaxRules.MissingMessage(kind, competence));
                continue;
            }

            var c = table.ReadContent();
            var result = kind switch
            {
                PayrollTaxTableKind.Inss => PayrollTaxRules.Inss(c.Inss!, table.Rounding, input.Base),
                PayrollTaxTableKind.Irrf => PayrollTaxRules.Irrf(c.Irrf!, table.Rounding, input.Base, inss ?? 0m, input.Dependents),
                PayrollTaxTableKind.FamilyAllowance => PayrollTaxRules.FamilyAllowance(c.FamilyAllowance!, table.Rounding, input.Base, input.Children),
                PayrollTaxTableKind.MinimumWage => new PayrollTaxResult(c.MinimumWage!.Monthly, [$"Salário mínimo de {PayrollTaxRules.MonthLabel(competence)}."]),
                PayrollTaxTableKind.Fgts => PayrollTaxRules.Percent("FGTS mensal", c.Fgts!.MonthlyRate, table.Rounding, input.Base),
                PayrollTaxTableKind.TransportVoucher => PayrollTaxRules.Percent("Desconto máximo do vale-transporte", c.TransportVoucher!.MaxDiscountRate, table.Rounding, input.Base),
                _ => PayrollTaxRules.EmployerCharges(c.EmployerCharges!, table.Rounding, input.Base),
            };
            if (kind == PayrollTaxTableKind.Inss)
            {
                inss = result.Amount;
            }
            else if (kind == PayrollTaxTableKind.Irrf && inss is null)
            {
                result = result with { Memory = [.. result.Memory, "Sem tabela de INSS na competência: o IRRF foi simulado sem a dedução do INSS."] };
            }

            lines.Add(new PayrollTaxSimulationLine(kind, PayrollTaxRules.KindLabel(kind), table.Id, table.Status, table.ValidFrom, result.Amount, result.Memory));
        }

        return new PayrollTaxSimulation(competence, input.Base, input.Dependents, input.Children, lines, missing);
    }

    public async Task<Result<PayrollTaxTableInForce>> ForCompetenceAsync(PayrollTaxTableKind kind, DateOnly competence, CancellationToken cancellationToken = default)
    {
        var first = new DateOnly(competence.Year, competence.Month, 1);
        var candidates = await db.PayrollTaxTables.AsNoTracking().Where(t => t.Kind == kind && t.ValidFrom <= first && t.Status != PayrollTaxTableStatus.Superseded).ToListAsync(cancellationToken);
        if (PayrollTaxRules.Pick(candidates, kind, first) is not { } table)
        {
            return Error.Validation(PayrollTaxRules.MissingCode, PayrollTaxRules.MissingMessage(kind, first)) with
            {
                Data = new Dictionary<string, object?> { ["kind"] = kind.ToString(), ["link"] = Link + "?tipo=" + kind },
            };
        }

        return new PayrollTaxTableInForce(table.Id, table.Kind, table.Status, table.ValidFrom, table.SchemaVersion, table.ReadContent(), table.Rounding);
    }

    public async Task<Result> RequireActiveAsync(IReadOnlyCollection<Guid> tableIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tableIds);
        var found = await db.PayrollTaxTables.AsNoTracking().Where(t => tableIds.Contains(t.Id)).OrderBy(t => t.Kind).ToListAsync(cancellationToken);
        var pending = found.Where(t => t.Status != PayrollTaxTableStatus.Active).ToList();
        if (pending.Count == 0 && found.Count == tableIds.Distinct().Count())
        {
            return Result.Success();
        }

        var first = pending.FirstOrDefault();
        return Error.Validation(PayrollTaxRules.NotConfirmedCode, first is null
            ? "A folha oficial usa só tabelas conferidas: uma das tabelas usadas não existe mais."
            : $"A folha oficial usa só tabelas conferidas: confira a tabela {PayrollTaxRules.KindLabel(first.Kind)} de {PayrollTaxRules.MonthLabel(first.ValidFrom)} com a contadora.") with
        {
            Data = new Dictionary<string, object?> { ["tables"] = pending.Select(p => p.Id).ToList(), ["link"] = Link },
        };
    }

    /// <summary>
    /// Pacote do ano (CA 1), também nos tenants existentes no próximo deploy: cria cada versão do pacote uma vez (pelo ano e o tipo), como
    /// "A confirmar com a contadora" e criada pelo sistema; nunca sobrescreve nem recria. Pula a entrada sem valores (pendente) e o mês em
    /// que o tenant já cadastrou a própria versão.
    /// </summary>
    public static async Task SeedAsync(IHrDb db, TimeProvider clock, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(clock);
        var existing = await db.PayrollTaxTables.AsNoTracking().Select(t => new { t.Kind, t.ValidFrom, t.SeedPackage, t.Status, t.RectifiesId }).ToListAsync(cancellationToken);
        foreach (var package in PayrollTaxPackages.All)
        {
            foreach (var entry in package.Tables.Where(t => t.Pending is null))
            {
                var seeded = existing.Any(e => e.SeedPackage == package.Package && e.Kind == entry.Kind);
                var taken = existing.Any(e => e.Kind == entry.Kind && e.ValidFrom == entry.ValidFrom && (e.Status == PayrollTaxTableStatus.Active || (e.Status == PayrollTaxTableStatus.Unconfirmed && e.RectifiesId == null)));
                if (seeded || taken)
                {
                    continue;
                }

                var table = new PayrollTaxTable
                {
                    Kind = entry.Kind, ValidFrom = entry.ValidFrom!.Value, Rounding = entry.Rounding ?? PayrollRounding.AwayFromZero, Source = entry.Source!.Trim(),
                    SeedPackage = package.Package, CreatedAt = clock.GetUtcNow(),
                };
                table.SetContent(entry.Content!);
                db.PayrollTaxTables.Add(table);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<Result<Guid>> DatasetAsync(PayrollTaxTable table, string? text, Guid me, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Error.Validation(PayrollTaxRules.DatasetRequiredCode,
                "Você é a única pessoa que aprova e cadastrou ou editou esta tabela: anexe o dataset de referência da contadora desta vigência para conferir.");
        }

        var (cases, error) = PayrollTaxRules.ParseCases(text);
        if (error is not null)
        {
            return Field("dataset", error);
        }

        Func<decimal, decimal>? inssOf = null;
        if (table.Kind == PayrollTaxTableKind.Irrf)
        {
            var inssTables = await db.PayrollTaxTables.AsNoTracking().Where(t => t.Kind == PayrollTaxTableKind.Inss && t.ValidFrom <= table.ValidFrom).ToListAsync(ct);
            if (PayrollTaxRules.Pick(inssTables, PayrollTaxTableKind.Inss, table.ValidFrom) is not { } inss)
            {
                return Error.Validation(PayrollTaxRules.MissingCode, PayrollTaxRules.MissingMessage(PayrollTaxTableKind.Inss, table.ValidFrom) + " O IRRF do dataset usa o INSS da mesma vigência.");
            }

            var content = inss.ReadContent().Inss!;
            inssOf = salary => PayrollTaxRules.Inss(content, inss.Rounding, salary).Amount;
        }

        var misses = new List<string>();
        for (var i = 0; i < cases.Count; i++)
        {
            var simulated = PayrollTaxRules.Evaluate(table, cases[i], inssOf);
            if (simulated != cases[i].Expected)
            {
                misses.Add(string.Create(CultureInfo.GetCultureInfo("pt-BR"), $"Caso {i + 1} (base {cases[i].Base:N2}, {cases[i].Dependents}): esperado {cases[i].Expected:N2}, simulado {simulated:N2}"));
            }
        }

        if (misses.Count > 0)
        {
            return Error.Validation(PayrollTaxRules.DatasetMismatchCode, $"O simulador não bateu em {misses.Count} {(misses.Count == 1 ? "caso" : "casos")} do dataset. Corrija a tabela ou confira o dataset com a contadora.") with
            {
                Data = new Dictionary<string, object?> { ["cases"] = misses.Take(20).ToList() },
            };
        }

        var dataset = new PayrollTaxTableDataset { TableId = table.Id, Cases = PayrollTaxSchema.WriteCases(cases), CaseCount = cases.Count, CreatedBy = me, CreatedAt = clock.GetUtcNow() };
        db.PayrollTaxTableDatasets.Add(dataset);
        return dataset.Id;
    }

    private static Error? Invalid(PayrollTaxTableKind kind, DateOnly? validFrom, PayrollTaxContent? content, PayrollRounding? rounding, string? source)
    {
        var (errors, _) = PayrollTaxRules.Validate(kind, validFrom, content, rounding, source);
        return errors.Count == 0 ? null : Error.Validation(Error.Codes.Validation, errors.First().Value[0], errors);
    }

    private async Task<bool> DuplicateAsync(PayrollTaxTableKind kind, DateOnly validFrom, Guid? except, CancellationToken ct) =>
        PayrollTaxRules.Conflicts(await db.PayrollTaxTables.AsNoTracking().Where(t => t.Kind == kind && t.ValidFrom == validFrom).ToListAsync(ct), kind, validFrom, except);

    private static Error Duplicate(PayrollTaxTableKind kind, DateOnly validFrom) =>
        Error.Conflict(PayrollTaxRules.DuplicateCode, $"Já existe a tabela {PayrollTaxRules.KindLabel(kind)} que vale a partir de {PayrollTaxRules.MonthLabel(validFrom)}. Edite a que está a confirmar ou retifique a conferida.");

    private async Task<Result<PayrollTaxTableDetail>> SaveAsync(PayrollTaxTable table, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, CurrentIndex))
        {
            return Duplicate(table.Kind, table.ValidFrom);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, PendingRectifyIndex))
        {
            return Error.Conflict(PayrollTaxRules.RectifyPendingCode, "Já existe uma retificação desta tabela a conferir. Edite ou confira a retificação.");
        }

        return await DetailAsync(table, ct);
    }

    private async Task<PayrollTaxTableDetail> DetailAsync(PayrollTaxTable table, CancellationToken ct)
    {
        var today = BusinessTime.Today(clock);
        var same = await db.PayrollTaxTables.AsNoTracking().Where(t => t.Kind == table.Kind && t.ValidFrom <= today).ToListAsync(ct);
        return Detail(table, await NamesAsync([table], ct), today, PayrollTaxRules.Pick(same, table.Kind, today)?.Id);
    }

    private PayrollTaxTableDetail Detail(PayrollTaxTable t, Dictionary<Guid, string?> names, DateOnly today, Guid? inForceId)
    {
        var content = t.ReadContent();
        var (_, warnings) = PayrollTaxRules.Validate(t.Kind, t.ValidFrom, content, t.Rounding, t.Source);
        var mine = currentUser.UserId is { } me && t.CreatedOrEditedBy(me);
        return new PayrollTaxTableDetail(t.Id, t.Kind, PayrollTaxRules.KindLabel(t.Kind), t.ValidFrom, t.Status, t.RectifiesId, t.RectifyReason, content, t.SchemaVersion, t.Rounding, t.Source,
            t.SeedPackage, t.CreatedBy is { } by ? names.GetValueOrDefault(by) : null, t.CreatedAt, t.EditedBy.Select(e => names.GetValueOrDefault(e) ?? "Usuário removido").ToList(), t.EditedAt,
            t.CheckedBy is { } c ? names.GetValueOrDefault(c) : null, t.CheckedAt, t.CheckedNote, t.CheckedWithDatasetId is not null, t.Id == inForceId, t.ValidFrom > today && t.IsCurrent, mine,
            warnings, t.Version);
    }

    private async Task<Dictionary<Guid, string?>> NamesAsync(IEnumerable<PayrollTaxTable> tables, CancellationToken ct)
    {
        var names = new Dictionary<Guid, string?>();
        foreach (var id in tables.SelectMany(t => t.EditedBy.Append(t.CreatedBy ?? Guid.Empty).Append(t.CheckedBy ?? Guid.Empty)).Where(i => i != Guid.Empty).Distinct())
        {
            names[id] = await users.DisplayNameAsync(id, ct);
        }

        return names;
    }

    private ValueTask<bool> CanSensitiveAsync(CancellationToken ct) => permissions.HasPermissionAsync("hr", PositionService.SensitiveAction, ct);

    private static Error Field(string field, string message) => Error.Validation(Error.Codes.Validation, message, new Dictionary<string, string[]> { [field] = [message] });

    private static Error NotFound() => Error.NotFound(PayrollTaxRules.NotFoundCode, "Tabela da folha não encontrada.");

    private static Error Forbidden() => Error.Forbidden(Error.Codes.Forbidden, "Você não tem acesso a esta funcionalidade. Fale com o administrador.");
}

/// <summary>Pacote de dados do ano (CA 1): os arquivos <c>PayrollTaxPackages/hr-payroll-tables-{ano}.json</c>, embutidos no assembly.</summary>
public static class PayrollTaxPackages
{
    public const string ResourcePrefix = "BemAqui.HR.PayrollTaxPackages.";

    public sealed record Entry(PayrollTaxTableKind Kind, string? Pending, DateOnly? ValidFrom, PayrollRounding? Rounding, string? Source, PayrollTaxContent? Content);

    public sealed record PackageFile(int Package, IReadOnlyList<Entry> Tables);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly Lazy<List<PackageFile>> Loaded = new(Load);

    /// <summary>Os pacotes, do ano mais antigo ao mais novo. Pacote inválido derruba o seed (e o teste do pacote no CI).</summary>
    public static IReadOnlyList<PackageFile> All => Loaded.Value;

    /// <summary>A nota da entrada pendente do pacote mais novo, por tipo (a seção vazia da tela explica por que não veio pré-cadastrada).</summary>
    public static IReadOnlyDictionary<PayrollTaxTableKind, string> PendingNotes() =>
        All.Count == 0 ? new Dictionary<PayrollTaxTableKind, string>() : All[^1].Tables.Where(t => t.Pending is not null).ToDictionary(t => t.Kind, t => $"Pacote {All[^1].Package}: {t.Pending}");

    /// <summary>Confere um pacote: entrada com valores precisa passar pela mesma validação do cadastro.</summary>
    public static IReadOnlyList<string> Problems(PackageFile package)
    {
        ArgumentNullException.ThrowIfNull(package);
        var problems = new List<string>();
        foreach (var entry in package.Tables)
        {
            if (entry.Pending is not null)
            {
                continue;
            }

            var (errors, _) = PayrollTaxRules.Validate(entry.Kind, entry.ValidFrom, entry.Content, entry.Rounding ?? PayrollRounding.AwayFromZero, entry.Source);
            problems.AddRange(errors.SelectMany(e => e.Value.Select(m => $"{package.Package} {entry.Kind} {e.Key}: {m}")));
        }

        if (package.Tables.GroupBy(t => t.Kind).Any(g => g.Count() > 1))
        {
            problems.Add($"{package.Package}: um tipo aparece mais de uma vez no pacote.");
        }

        return problems;
    }

    public static PackageFile Parse(string json) => JsonSerializer.Deserialize<PackageFile>(json, Json) ?? throw new InvalidOperationException("Pacote de tabelas vazio.");

    private static List<PackageFile> Load()
    {
        var assembly = typeof(PayrollTaxPackages).Assembly;
        var packages = new List<PackageFile>();
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            var package = Parse(reader.ReadToEnd());
            if (Problems(package) is { Count: > 0 } problems)
            {
                throw new InvalidOperationException("Pacote de tabelas da folha inválido: " + string.Join("; ", problems));
            }

            packages.Add(package);
        }

        return packages.OrderBy(p => p.Package).ToList();
    }
}
