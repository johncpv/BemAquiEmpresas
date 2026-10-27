using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.HR.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.HR.Application;

/// <summary>Rubrica na lista e no painel (RH-02a CA 1 e 2): nenhum valor; <c>InUse</c> diz se já foi usada em holerite (excluir vira inativar).</summary>
public sealed record PayrollItemDetail(
    Guid Id,
    string Code,
    string Name,
    PayrollItemKind Kind,
    bool InssBase,
    bool FgtsBase,
    bool IrrfBase,
    bool IsActive,
    bool IsStandard,
    bool Confirmed,
    string? ConfirmedByName,
    DateTimeOffset? ConfirmedAt,
    bool InUse,
    uint Version);

public sealed record PayrollItemInput(string? Code, string? Name, PayrollItemKind? Kind, bool InssBase, bool FgtsBase, bool IrrfBase, uint? Version);

/// <summary>
/// Rubricas (RH-02a CA 1 e 2): a lista padrão nasce do seed "A confirmar com a contadora"; criar, editar, inativar, excluir (só sem
/// uso) e "Marcar como conferido" com <c>hr.edit</c>, tudo auditado. O código pode ser trocado pelo da contadora: as linhas dos
/// holerites guardam a rubrica pelo id, com o código e o nome do momento.
/// </summary>
public sealed class PayrollItemService(IHrDb db, ICurrentUser currentUser, IUserDirectory users, TimeProvider clock)
{
    public const string CodeIndex = "ix_payroll_items_code";
    public const string SalarySeedKey = "salario";

    /// <summary>Rubrica do seed: chave, código, nome, tipo e as marcas de base (propostas, a confirmar com a contadora).</summary>
    public sealed record Standard(string SeedKey, string Code, string Name, PayrollItemKind Kind, bool Inss, bool Fgts, bool Irrf);

    /// <summary>A lista padrão (CA 1). As marcas de base são informativas (a folha calculada da Fase 7 usa); nada é calculado aqui.</summary>
    public static IReadOnlyList<Standard> Standards { get; } =
    [
        new(SalarySeedKey, "SAL", "Salário", PayrollItemKind.Earning, true, true, true),
        new("horas_extras_50", "HE50", "Horas extras 50%", PayrollItemKind.Earning, true, true, true),
        new("horas_extras_100", "HE100", "Horas extras 100%", PayrollItemKind.Earning, true, true, true),
        new("dsr", "DSR", "DSR", PayrollItemKind.Earning, true, true, true),
        new("insalubridade", "INSAL", "Adicional de insalubridade", PayrollItemKind.Earning, true, true, true),
        new("periculosidade", "PERIC", "Adicional de periculosidade", PayrollItemKind.Earning, true, true, true),
        new("adicional_noturno", "ADNOT", "Adicional noturno", PayrollItemKind.Earning, true, true, true),
        new("ferias", "FERIAS", "Férias", PayrollItemKind.Earning, true, true, true),
        new("terco_ferias", "FER13", "1/3 de férias", PayrollItemKind.Earning, true, true, true),
        new("decimo_terceiro", "13SAL", "13º salário", PayrollItemKind.Earning, true, true, true),
        new("salario_familia", "SALFAM", "Salário-família", PayrollItemKind.Earning, false, false, false),
        new("faltas", "FALTAS", "Faltas", PayrollItemKind.Deduction, true, true, true),
        new("atrasos", "ATRASOS", "Atrasos", PayrollItemKind.Deduction, true, true, true),
        new("inss", "INSS", "INSS", PayrollItemKind.Deduction, false, false, true),
        new("irrf", "IRRF", "IRRF", PayrollItemKind.Deduction, false, false, false),
        new("vale_transporte", "VT", "Vale-transporte", PayrollItemKind.Deduction, false, false, false),
        new("adiantamento", "ADIANT", "Adiantamento", PayrollItemKind.Deduction, false, false, false),
        new("pensao_alimenticia", "PENSAO", "Pensão alimentícia", PayrollItemKind.Deduction, false, false, true),
        new("fgts", "FGTS", "FGTS", PayrollItemKind.Information, false, false, false),
    ];

    public async Task<IReadOnlyList<PayrollItemDetail>> ListAsync(CancellationToken cancellationToken = default)
    {
        var items = await db.PayrollItems.AsNoTracking().OrderBy(i => i.Kind).ThenBy(i => i.Code).ToListAsync(cancellationToken);
        var used = (await db.PayslipLines.AsNoTracking().Select(l => l.PayrollItemId).Distinct().ToListAsync(cancellationToken)).ToHashSet();
        return await DetailsAsync(items, used, cancellationToken);
    }

    public async Task<Result<PayrollItemDetail>> CreateAsync(PayrollItemInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var item = new PayrollItem { CreatedAt = clock.GetUtcNow() };
        if (Apply(item, input) is { } invalid)
        {
            return invalid;
        }

        db.PayrollItems.Add(item);
        return await SaveAsync(item, cancellationToken);
    }

    public async Task<Result<PayrollItemDetail>> UpdateAsync(Guid id, PayrollItemInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var item = await db.PayrollItems.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (item is null)
        {
            return NotFound();
        }

        var before = (item.Code, item.Kind, item.InssBase, item.FgtsBase, item.IrrfBase);
        if (Apply(item, input) is { } invalid)
        {
            return invalid;
        }

        if (before != (item.Code, item.Kind, item.InssBase, item.FgtsBase, item.IrrfBase))
        {
            item.ResetConfirmation();
        }

        if (input.Version is { } version)
        {
            db.Entry(item).Property(i => i.Version).OriginalValue = version;
        }

        return await SaveAsync(item, cancellationToken);
    }

    public async Task<Result<PayrollItemDetail>> SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken = default)
    {
        var item = await db.PayrollItems.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (item is null)
        {
            return NotFound();
        }

        item.IsActive = active;
        return await SaveAsync(item, cancellationToken);
    }

    /// <summary>"Marcar como conferido" (ADR 0042): quem e quando, auditado; vale para o código, o tipo e as bases de agora.</summary>
    public async Task<Result<PayrollItemDetail>> ConfirmAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var item = await db.PayrollItems.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (item is null)
        {
            return NotFound();
        }

        if (item.ConfirmedAt is null)
        {
            item.Confirm(currentUser.UserId, clock.GetUtcNow());
        }

        return await SaveAsync(item, cancellationToken);
    }

    /// <summary>Excluir só sem uso (CA 2, D10): usada em holerite, 409 com a alternativa "Inativar".</summary>
    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var item = await db.PayrollItems.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (item is null)
        {
            return NotFound();
        }

        if (await db.PayslipLines.AnyAsync(l => l.PayrollItemId == id, cancellationToken))
        {
            return Error.Conflict(PayrollRules.ItemInUseCode, "A rubrica já foi usada em holerite. Inative em vez de excluir: ela some das escolhas e o histórico continua.");
        }

        db.PayrollItems.Remove(item);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>Seed (também nos tenants existentes): cria a rubrica padrão que falta, sem recriar a excluída nem repetir o código.</summary>
    public static async Task SeedAsync(IHrDb db, TimeProvider clock, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(clock);
        var keys = (await db.PayrollItems.IgnoreQueryFilters().Where(i => i.SeedKey != null).Select(i => i.SeedKey!).ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
        var codes = (await db.PayrollItems.Select(i => i.Code).ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
        foreach (var s in Standards.Where(s => !keys.Contains(s.SeedKey) && !codes.Contains(s.Code)))
        {
            db.PayrollItems.Add(new PayrollItem { SeedKey = s.SeedKey, Code = s.Code, Name = s.Name, Kind = s.Kind, InssBase = s.Inss, FgtsBase = s.Fgts, IrrfBase = s.Irrf, CreatedAt = clock.GetUtcNow() });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static Error? Apply(PayrollItem item, PayrollItemInput input)
    {
        if (PayrollRules.NormalizeCode(input.Code) is not { } code)
        {
            return PayrollRules.Field("code", "Informe o código com até 10 letras ou números (pode ter _ e -), começando por letra ou número.");
        }

        var name = (input.Name ?? string.Empty).Trim();
        if (name.Length is 0 or > PayrollRules.NameMaxLength)
        {
            return PayrollRules.Field("name", "Informe o nome da rubrica, com até 100 caracteres.");
        }

        if (input.Kind is not { } kind || !Enum.IsDefined(kind))
        {
            return PayrollRules.Field("kind", "Escolha o tipo: Provento, Desconto ou Informativa.");
        }

        item.Code = code;
        item.Name = name;
        item.Kind = kind;
        item.InssBase = input.InssBase;
        item.FgtsBase = input.FgtsBase;
        item.IrrfBase = input.IrrfBase;
        return null;
    }

    private async Task<Result<PayrollItemDetail>> SaveAsync(PayrollItem item, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, CodeIndex))
        {
            return Error.Conflict(PayrollRules.ItemCodeTakenCode, "Já existe uma rubrica com este código.");
        }

        var used = await db.PayslipLines.AnyAsync(l => l.PayrollItemId == item.Id, ct);
        return (await DetailsAsync([item], used ? [item.Id] : [], ct))[0];
    }

    private async Task<IReadOnlyList<PayrollItemDetail>> DetailsAsync(IReadOnlyList<PayrollItem> items, HashSet<Guid> used, CancellationToken ct)
    {
        var names = new Dictionary<Guid, string?>();
        foreach (var by in items.Select(i => i.ConfirmedBy).OfType<Guid>().Distinct())
        {
            names[by] = await users.DisplayNameAsync(by, ct);
        }

        return items.Select(i => new PayrollItemDetail(i.Id, i.Code, i.Name, i.Kind, i.InssBase, i.FgtsBase, i.IrrfBase, i.IsActive, i.SeedKey is not null, i.ConfirmedAt is not null,
            i.ConfirmedBy is { } by ? names.GetValueOrDefault(by) : null, i.ConfirmedAt, used.Contains(i.Id), i.Version)).ToList();
    }

    private static Error NotFound() => Error.NotFound("payroll_item_not_found", "Rubrica não encontrada.");
}
