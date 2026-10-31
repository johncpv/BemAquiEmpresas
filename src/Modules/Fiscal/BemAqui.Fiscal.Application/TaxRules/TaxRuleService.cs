using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.Fiscal.Application.Settings;
using BemAqui.Fiscal.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Fiscal.Application.TaxRules;

/// <summary>
/// Fiscal → Regras fiscais (FIS-02b CA 1 a 6, 11 e 13): lista, detalhe por versão, cadastro e edição em rascunho (editar a ativa cria nova
/// versão), ativação por outra pessoa com <c>fiscal.approve</c> (exceto o Proprietário único), inativação com motivo e "Marcar como
/// conferida". Permissões conferidas no endpoint; a auditoria grava cada versão (entidade <c>TaxRule</c>).
/// </summary>
public sealed class TaxRuleService(
    IFiscalDb db,
    IFiscalSettings settings,
    IPermissionChecker permissions,
    ICurrentUser currentUser,
    IUserDirectory users,
    INotificationService notifications,
    TimeProvider clock)
{
    public const string Link = "/app/fiscal/regras/";
    public const string SelfActivatedType = "fiscal-tax-rule-self-activated";
    public const string NotFoundCode = "tax_rule_not_found";
    public const string NotDraftCode = "tax_rule_not_draft";
    public const string FilterToConfirm = "a-confirmar";
    public const string FilterToReview = "a-revisar";

    public async Task<TaxRuleList> ListAsync(string? filter, CancellationToken cancellationToken = default)
    {
        var all = await db.TaxRules.AsNoTracking().ToListAsync(cancellationToken);
        var names = new Dictionary<Guid, string?>();
        var rows = new List<TaxRuleSummary>();
        foreach (var group in all.GroupBy(r => r.RuleId))
        {
            var head = Head(group);
            var draft = group.FirstOrDefault(r => r.Status == TaxRuleStatus.Draft && r.Id != head.Id);
            rows.Add(new TaxRuleSummary(head.RuleId, head.Id, head.RuleVersion, head.Operation, head.Name, head.Status, head.ValidFrom, head.ValidTo, head.CfopInternal, head.CfopInterstate,
                State(head), group.Any(r => r.Status != TaxRuleStatus.Inactive && r.NeedsReview), head.Difal is not null,
                await NameAsync(head.ActivatedBy, names, cancellationToken), head.ActivatedAt, head.IsSeed, draft?.Id, draft?.RuleVersion));
        }

        var toConfirm = rows.Count(r => r.Status != TaxRuleStatus.Inactive && r.ReviewState == FiscalReviewState.ToConfirm);
        var toReview = rows.Count(r => r.NeedsReview);
        var shown = filter switch
        {
            FilterToConfirm => rows.Where(r => r.Status != TaxRuleStatus.Inactive && r.ReviewState == FiscalReviewState.ToConfirm),
            FilterToReview => rows.Where(r => r.NeedsReview),
            _ => rows,
        };
        var ordered = shown.OrderBy(r => r.Operation).ThenBy(r => r.Name, StringComparer.CurrentCulture).ToList();
        var seedDraftsOnly = rows.Count > 0 && all.All(r => r.IsSeed && r.Status == TaxRuleStatus.Draft);
        return new TaxRuleList(ordered, toConfirm, toReview, rows.Count(r => r.Status == TaxRuleStatus.Active), seedDraftsOnly,
            await permissions.HasPermissionAsync("fiscal", "edit", cancellationToken), await permissions.HasPermissionAsync("fiscal", "approve", cancellationToken));
    }

    /// <summary>A regra aberta: o rascunho pendente, se houver; senão a versão que vale.</summary>
    public async Task<Result<TaxRuleView>> GetAsync(Guid ruleId, CancellationToken cancellationToken = default)
    {
        var versions = await VersionsAsync(ruleId, false, cancellationToken);
        if (versions.Count == 0)
        {
            return NotFound();
        }

        var open = versions.FirstOrDefault(v => v.Status == TaxRuleStatus.Draft) ?? Head(versions);
        return await ViewAsync(open, versions, cancellationToken);
    }

    /// <summary>Uma versão (CA 12, IDOR): a versão tem de ser da regra do caminho.</summary>
    public async Task<Result<TaxRuleView>> GetVersionAsync(Guid ruleId, Guid versionId, CancellationToken cancellationToken = default)
    {
        var versions = await VersionsAsync(ruleId, false, cancellationToken);
        var version = versions.FirstOrDefault(v => v.Id == versionId);
        return version is null ? NotFound() : await ViewAsync(version, versions, cancellationToken);
    }

    /// <summary>"Salvar regra fiscal" de uma regra nova (CA 2): nasce <c>Draft</c>, "A confirmar com a contadora".</summary>
    public async Task<Result<TaxRuleView>> CreateAsync(TaxRuleValues values, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);
        var rule = TaxRule.New(currentUser.UserId, clock.GetUtcNow());
        var check = await ApplyAsync(rule, values, cancellationToken);
        if (check is { } error)
        {
            return error;
        }

        db.TaxRules.Add(rule);
        await db.SaveChangesAsync(cancellationToken);
        return await GetVersionAsync(rule.RuleId, rule.Id, cancellationToken);
    }

    /// <summary>
    /// "Salvar regra fiscal" (CA 2): edita o rascunho; numa regra ativa (ou inativa) cria a nova versão <c>Draft</c>, e a ativa continua
    /// valendo até a nova ser ativada. O chip volta para "A confirmar com a contadora".
    /// </summary>
    public async Task<Result<TaxRuleView>> UpdateAsync(Guid ruleId, TaxRuleValues values, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);
        var versions = await VersionsAsync(ruleId, true, cancellationToken);
        if (versions.Count == 0)
        {
            return NotFound();
        }

        var now = clock.GetUtcNow();
        var draft = versions.FirstOrDefault(v => v.Status == TaxRuleStatus.Draft);
        var isNew = draft is null;
        draft ??= versions.MaxBy(v => v.RuleVersion)!.NextVersion(currentUser.UserId, now);
        var check = await ApplyAsync(draft, values, cancellationToken);
        if (check is { } error)
        {
            db.ChangeTracker.Clear();
            return error;
        }

        if (isNew)
        {
            db.TaxRules.Add(draft);
        }
        else
        {
            draft.Touch(currentUser.UserId, now);
        }

        await db.SaveChangesAsync(cancellationToken);
        return await GetVersionAsync(ruleId, draft.Id, cancellationToken);
    }

    /// <summary>
    /// "Ativar regra" (CA 3 e 5): só o rascunho; quem editou não ativa (403 <c>segregation_of_duties</c>), exceto o Proprietário único
    /// (marca "Autoaprovado" e aviso aos Proprietários); a do seed (autor sistema) qualquer um com <c>fiscal.approve</c> ativa. A versão
    /// ativa anterior deixa de valer.
    /// </summary>
    public async Task<Result<TaxRuleView>> ActivateAsync(Guid ruleId, Guid versionId, CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not { } me)
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Entre de novo para ativar a regra.");
        }

        var versions = await VersionsAsync(ruleId, true, cancellationToken);
        var version = versions.FirstOrDefault(v => v.Id == versionId);
        if (version is null)
        {
            return NotFound();
        }

        if (version.Status != TaxRuleStatus.Draft)
        {
            return Error.Conflict(NotDraftCode, "Só a versão em rascunho é ativada. Edite a regra para criar uma nova versão.");
        }

        var errors = TaxRuleRules.Validate(version);
        if (errors.Count > 0)
        {
            return Invalid(errors);
        }

        if (await OverlapAsync(version, cancellationToken) is { } overlap)
        {
            return overlap;
        }

        var self = version.Editors.Contains(me);
        if (self && !await SoleOwnerRule.NoOtherEligibleAsync(users, "fiscal.approve", me, version.Editors, cancellationToken))
        {
            return Error.Forbidden(TaxRuleRules.SegregationCode, TaxRuleRules.SegregationMessage);
        }

        var now = clock.GetUtcNow();
        foreach (var previous in versions.Where(v => v.Status == TaxRuleStatus.Active))
        {
            previous.Supersede(me, now, version.RuleVersion);
        }

        version.Activate(me, now, self);
        if (self)
        {
            var name = await users.DisplayNameAsync(me, cancellationToken) ?? "Um usuário";
            foreach (var owner in await users.OwnerIdsAsync(cancellationToken))
            {
                notifications.Notify(owner, SelfActivatedType, $"{name} ativou a própria regra fiscal {version.Name} ({SoleOwnerRule.SelfApprovedLabel}).", Link + ruleId);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return await GetVersionAsync(ruleId, version.Id, cancellationToken);
    }

    /// <summary>"Inativar regra" (CA 3), com o motivo: a ativa deixa de valer; o rascunho é descartado (fica no histórico).</summary>
    public async Task<Result<TaxRuleView>> DeactivateAsync(Guid ruleId, Guid versionId, DeactivateTaxRuleRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length < 3)
        {
            return Invalid(new Dictionary<string, string[]> { ["reason"] = ["Informe o motivo da inativação."] });
        }

        var version = await db.TaxRules.FirstOrDefaultAsync(r => r.Id == versionId && r.RuleId == ruleId, cancellationToken);
        if (version is null)
        {
            return NotFound();
        }

        if (version.Status == TaxRuleStatus.Inactive)
        {
            return Error.Conflict("tax_rule_inactive", "Esta versão já está inativa.");
        }

        version.Deactivate(currentUser.UserId ?? Guid.Empty, clock.GetUtcNow(), request.Reason.Trim());
        await db.SaveChangesAsync(cancellationToken);
        return await GetVersionAsync(ruleId, versionId, cancellationToken);
    }

    /// <summary>"Marcar como conferida" (CA 4): a versão fica conferida com quem e quando (auditado).</summary>
    public async Task<Result<TaxRuleView>> ConfirmAsync(Guid ruleId, Guid versionId, CancellationToken cancellationToken = default)
    {
        var version = await db.TaxRules.FirstOrDefaultAsync(r => r.Id == versionId && r.RuleId == ruleId, cancellationToken);
        if (version is null)
        {
            return NotFound();
        }

        if (!version.IsConfirmed)
        {
            version.Confirm(currentUser.UserId ?? Guid.Empty, clock.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
        }

        return await GetVersionAsync(ruleId, versionId, cancellationToken);
    }

    /// <summary>Copia os valores, fixa o CRT de Configurações → Fiscal, valida e confere a sobreposição; devolve o erro ou null.</summary>
    private async Task<Error?> ApplyAsync(TaxRule rule, TaxRuleValues v, CancellationToken ct)
    {
        rule.Operation = v.Operation;
        rule.Name = (v.Name ?? string.Empty).Trim();
        rule.NatureOfOperation = (v.NatureOfOperation ?? string.Empty).Trim();
        rule.ValidFrom = v.ValidFrom;
        rule.ValidTo = v.ValidTo;
        rule.CfopInternal = Clean(v.CfopInternal);
        rule.CfopInterstate = Clean(v.CfopInterstate);
        rule.Icms = v.Icms;
        rule.IcmsSt = v.IcmsSt;
        rule.Fcp = v.Fcp;
        rule.Ipi = v.Ipi;
        rule.Pis = v.Pis;
        rule.Cofins = v.Cofins;
        rule.Iss = v.Iss;
        rule.IbsCbs = v.IbsCbs;
        rule.Difal = v.Difal;
        rule.CostTaxes = [.. (v.CostTaxes ?? []).Distinct(StringComparer.Ordinal)];
        rule.ComplementaryInfo = Clean(v.ComplementaryInfo);
        var c = v.Conditions ?? TaxRuleConditions.Any;
        rule.Conditions = new TaxRuleConditions(
            [.. (c.States ?? []).Select(s => s.Trim().ToUpperInvariant()).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
            [.. (c.RecipientTypes ?? []).Distinct().Order()],
            [.. (c.NcmPrefixes ?? []).Select(FiscalSettingsRules.Digits).Where(p => p.Length > 0).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)]);
        rule.CrtScope = FiscalSettings.CrtOf((await settings.CurrentAsync(ct)).Regime);
        var errors = TaxRuleRules.Validate(rule);
        if (errors.Count > 0)
        {
            return Invalid(errors);
        }

        return await OverlapAsync(rule, ct);
    }

    /// <summary>CA 5: 422 "Esta regra se sobrepõe à regra {nome}." contra as ativas das outras regras.</summary>
    private async Task<Error?> OverlapAsync(TaxRule rule, CancellationToken ct)
    {
        var active = await db.TaxRules.AsNoTracking().Where(r => r.Status == TaxRuleStatus.Active && r.RuleId != rule.RuleId && r.Operation == rule.Operation).ToListAsync(ct);
        var other = active.FirstOrDefault(a => TaxRuleRules.Overlaps(rule, a));
        return other is null ? null : new Error(TaxRuleRules.OverlapCode, $"Esta regra se sobrepõe à regra {other.Name}.", ErrorType.Validation)
        {
            Data = new Dictionary<string, object?> { ["ruleId"] = other.RuleId },
        };
    }

    private async Task<List<TaxRule>> VersionsAsync(Guid ruleId, bool track, CancellationToken ct)
    {
        var query = track ? db.TaxRules : db.TaxRules.AsNoTracking();
        return await query.Where(r => r.RuleId == ruleId).OrderBy(r => r.RuleVersion).ToListAsync(ct);
    }

    private async Task<TaxRuleView> ViewAsync(TaxRule v, IReadOnlyList<TaxRule> versions, CancellationToken ct)
    {
        var names = new Dictionary<Guid, string?>();
        var canEdit = await permissions.HasPermissionAsync("fiscal", "edit", ct);
        var canApprove = await permissions.HasPermissionAsync("fiscal", "approve", ct);
        string? blocked = null;
        if (v.Status == TaxRuleStatus.Draft && canApprove && currentUser.UserId is { } me && v.Editors.Contains(me)
            && !await SoleOwnerRule.NoOtherEligibleAsync(users, "fiscal.approve", me, v.Editors, ct))
        {
            blocked = TaxRuleRules.SegregationMessage + " Peça a outra pessoa com permissão de ativar.";
        }

        var summaries = new List<TaxRuleVersionSummary>();
        foreach (var x in versions.OrderByDescending(x => x.RuleVersion))
        {
            summaries.Add(new TaxRuleVersionSummary(x.Id, x.RuleVersion, x.Status, State(x), x.UpdatedAt, await NameAsync(x.ActivatedBy, names, ct), x.ActivatedAt));
        }

        return new TaxRuleView(v.RuleId, v.Id, v.RuleVersion, v.Status, ToValues(v), v.CrtScope, State(v),
            await NameAsync(v.ConfirmedBy, names, ct), v.ConfirmedAt, await NameAsync(v.ActivatedBy, names, ct), v.ActivatedAt, v.SelfActivated, v.DeactivationReason,
            v.NeedsReview, v.IsSeed, v.Editors.Length == 0, summaries, canEdit, canApprove, blocked, FiscalSettingsRules.IcmsCstFor(v.CrtScope));
    }

    public static TaxRuleValues ToValues(TaxRule r)
    {
        ArgumentNullException.ThrowIfNull(r);
        return new TaxRuleValues(r.Operation, r.Name, r.NatureOfOperation, r.ValidFrom, r.ValidTo, r.CfopInternal, r.CfopInterstate, r.Icms, r.IcmsSt, r.Fcp, r.Ipi, r.Pis, r.Cofins,
            r.Iss, r.IbsCbs, r.Difal, r.CostTaxes, r.ComplementaryInfo, r.Conditions);
    }

    /// <summary>A versão que vale: a ativa; sem ativa, a última.</summary>
    private static TaxRule Head(IEnumerable<TaxRule> versions)
    {
        var list = versions.ToList();
        return list.FirstOrDefault(v => v.Status == TaxRuleStatus.Active) ?? list.MaxBy(v => v.RuleVersion)!;
    }

    private static FiscalReviewState State(TaxRule r) => r.IsConfirmed ? FiscalReviewState.Confirmed : FiscalReviewState.ToConfirm;

    private async Task<string?> NameAsync(Guid? id, Dictionary<Guid, string?> cache, CancellationToken ct)
    {
        if (id is not { } user || user == Guid.Empty)
        {
            return null;
        }

        if (!cache.TryGetValue(user, out var name))
        {
            name = await users.DisplayNameAsync(user, ct);
            cache[user] = name;
        }

        return name;
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static Error Invalid(Dictionary<string, string[]> errors) => Error.Validation(Error.Codes.Validation, errors.Values.First()[0], errors);

    private static Error NotFound() => Error.NotFound(NotFoundCode, "Regra fiscal não encontrada.");
}
