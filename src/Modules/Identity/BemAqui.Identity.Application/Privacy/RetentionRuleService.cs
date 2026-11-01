using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Privacy;
using BemAqui.Identity.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Identity.Application.Privacy;

/// <summary>
/// Configurações → Privacidade → Retenção (LGPD-01b CA 1, ADR 0018): a tabela por tipo de titular e grupo de campos, pré-configurada pela ADR
/// 0018 (semeada no provisionamento e, se faltar alguma linha, na primeira abertura) e editável só pelo Proprietário. Cada linha tem o chip "A
/// confirmar com a contadora e com o jurídico" até "Marcar como conferido" (quem e quando). Toda mudança passa pela auditoria da entidade, com
/// antes e depois. A data base é fixa pelo grupo; muda só a regra e os anos.
/// </summary>
public sealed class RetentionRuleService(IIdentityDb db, PrivacyRequestService requests, IUserDirectory users, ICurrentUser currentUser, TimeProvider clock)
{
    public async Task<Result<IReadOnlyList<RetentionRuleRow>>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (await requests.OwnerOnlyAsync(cancellationToken) is { } denied)
        {
            return denied;
        }

        if (await SeedMissingAsync(db, clock, cancellationToken) > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        var rows = await db.RetentionRules.AsNoTracking().ToListAsync(cancellationToken);
        var result = new List<RetentionRuleRow>();
        foreach (var r in rows.OrderBy(r => r.SubjectType).ThenBy(r => Order(r)))
        {
            result.Add(await RowAsync(r, cancellationToken));
        }

        return result;
    }

    /// <summary>Editar a regra (CA 1): a qualquer momento, ou de 0 a 30 anos depois da data base. A conferência volta a "A confirmar".</summary>
    public async Task<Result<RetentionRuleRow>> SaveAsync(Guid id, SaveRetentionRuleInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var (rule, error) = await LoadAsync(id, input.Version, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        if (input.Rule == RetentionRuleKind.AfterYears && (input.Years < 0 || input.Years > RetentionRule.MaxYears))
        {
            const string message = "Informe de 0 a 30 anos.";
            return Error.Validation("validation", message, new Dictionary<string, string[]> { ["years"] = [message] });
        }

        if (input.Rule == RetentionRuleKind.AfterYears && rule!.BaseDate == RetentionBase.None)
        {
            const string message = "Este grupo não tem data base: use \"A qualquer momento\".";
            return Error.Validation("validation", message, new Dictionary<string, string[]> { ["rule"] = [message] });
        }

        rule!.Rule = input.Rule;
        rule.Years = input.Rule == RetentionRuleKind.Anytime ? 0 : input.Years;
        rule.ConfirmedAt = null;
        rule.ConfirmedBy = null;
        rule.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return await RowAsync(rule, cancellationToken);
    }

    /// <summary>"Marcar como conferido" (CA 1): quem e quando ficam na linha e na auditoria.</summary>
    public async Task<Result<RetentionRuleRow>> ConfirmAsync(Guid id, ConfirmRetentionRuleInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var (rule, error) = await LoadAsync(id, input.Version, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        rule!.ConfirmedAt = clock.GetUtcNow();
        rule.ConfirmedBy = currentUser.UserId;
        rule.UpdatedAt = rule.ConfirmedAt.Value;
        await db.SaveChangesAsync(cancellationToken);
        return await RowAsync(rule, cancellationToken);
    }

    /// <summary>A tabela como os módulos a leem (o valor sugerido no lugar da linha que falta).</summary>
    public async Task<PersonalDataRetention> RetentionAsync(CancellationToken cancellationToken = default)
    {
        var rows = await db.RetentionRules.AsNoTracking().ToListAsync(cancellationToken);
        return new PersonalDataRetention(rows.Select(r => new PersonalDataRetentionRule(r.SubjectType, r.FieldGroup, r.Rule, r.Years, r.BaseDate)).ToList());
    }

    /// <summary>Semeia o que falta pelo <c>seed_key</c> (provisionamento, <c>migrate</c> e primeira abertura); nunca sobrescreve. O chamador salva.</summary>
    public static async Task<int> SeedMissingAsync(IIdentityDb db, TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(clock);
        var existing = (await db.RetentionRules.Select(r => r.SeedKey).ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
        var added = 0;
        foreach (var (rule, _) in RetentionGroups.Defaults)
        {
            var key = RetentionGroups.SeedKey(rule.SubjectType, rule.FieldGroup);
            if (existing.Contains(key))
            {
                continue;
            }

            db.RetentionRules.Add(new RetentionRule
            {
                SubjectType = rule.SubjectType, FieldGroup = rule.FieldGroup, Rule = rule.Rule, Years = rule.Years, BaseDate = rule.BaseDate, SeedKey = key,
                UpdatedAt = clock.GetUtcNow(),
            });
            added++;
        }

        return added;
    }

    private async Task<(RetentionRule? Rule, Error? Error)> LoadAsync(Guid id, uint version, CancellationToken ct)
    {
        if (await requests.OwnerOnlyAsync(ct) is { } denied)
        {
            return (null, denied);
        }

        var rule = await db.RetentionRules.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rule is null)
        {
            return (null, Error.NotFound("retention_rule_not_found", "Regra de retenção não encontrada."));
        }

        db.Entry(rule).Property(r => r.Version).OriginalValue = version;
        return (rule, null);
    }

    private async Task<RetentionRuleRow> RowAsync(RetentionRule r, CancellationToken ct) => new(
        r.Id, r.SubjectType, r.FieldGroup, Label(r), r.Rule, r.Years, r.BaseDate, r.ConfirmedAt is not null, r.ConfirmedAt,
        r.ConfirmedBy is { } by ? await users.DisplayNameAsync(by, ct) : null, r.Version);

    private static string Label(RetentionRule r) =>
        RetentionGroups.Defaults.FirstOrDefault(d => d.Rule.SubjectType == r.SubjectType && d.Rule.FieldGroup == r.FieldGroup).Label ?? r.FieldGroup;

    private static int Order(RetentionRule r)
    {
        var index = RetentionGroups.Defaults.Select((d, i) => (d, i)).FirstOrDefault(x => x.d.Rule.SubjectType == r.SubjectType && x.d.Rule.FieldGroup == r.FieldGroup);
        return index.d.Label is null ? int.MaxValue : index.i;
    }
}
