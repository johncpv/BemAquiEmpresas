using BemAqui.BuildingBlocks.Messaging;
using BemAqui.Fiscal.Contracts;
using BemAqui.Fiscal.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Fiscal.Application.TaxRules;

/// <summary>
/// Resolução da regra de cada item (FIS-02b CA 5, 7 e 9): só regra <c>Active</c> do regime atual, vigente na data de emissão, cujas
/// condições casam com a UF, o tipo de destinatário e o NCM; a mais específica por NCM ganha, depois a sem NCM. Em produção, só regra
/// conferida (CA 4). Sem regra, recusa com a mensagem do CA 9: nunca usa padrão silencioso. A emissão (FIS-03a, FIS-06) e o "Simular" usam.
/// </summary>
public sealed class TaxRuleResolver(IFiscalDb db)
{
    public async Task<Result<IReadOnlyList<TaxRule>>> ResolveAsync(
        TaxOperation operation, int crt, TaxRecipient recipient, IReadOnlyList<TaxItem> items, DateOnly date, bool requireConfirmed, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        ArgumentNullException.ThrowIfNull(items);
        var candidates = await db.TaxRules.AsNoTracking()
            .Where(r => r.Status == TaxRuleStatus.Active && r.Operation == operation && r.CrtScope == crt)
            .ToListAsync(cancellationToken);
        var rules = new List<TaxRule>(items.Count);
        foreach (var item in items)
        {
            var rule = Pick(candidates, recipient.State, recipient.Type, item.Ncm, date, requireConfirmed);
            if (rule is null)
            {
                return NoRule(operation, recipient, item.Ncm);
            }

            rules.Add(rule);
        }

        return rules;
    }

    /// <summary>Sem banco: a regra que vale para o item, ou null.</summary>
    public static TaxRule? Pick(IEnumerable<TaxRule> candidates, string state, RecipientType recipient, string? ncm, DateOnly date, bool requireConfirmed)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        return candidates
            .Where(r => r.Status == TaxRuleStatus.Active && (!requireConfirmed || r.IsConfirmed) && r.AppliesOn(date) && r.Conditions.Matches(state, recipient, ncm))
            .OrderByDescending(r => r.Conditions.Specificity(ncm))
            .ThenByDescending(r => r.Conditions.States.Count > 0)
            .ThenByDescending(r => r.Conditions.RecipientTypes.Count > 0)
            .ThenByDescending(r => r.ActivatedAt)
            .FirstOrDefault();
    }

    /// <summary>CA 9: "Não há regra fiscal para {operação} para {UF}/{tipo de cliente} e NCM {ncm}."</summary>
    public static Error NoRule(TaxOperation operation, TaxRecipient recipient, string? ncm)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        var message = $"Não há regra fiscal para {TaxRuleRules.Label(operation)} para {recipient.State}/{TaxRuleRules.Label(recipient.Type)} e NCM {(string.IsNullOrEmpty(ncm) ? "não informado" : ncm)}.";
        return new Error(TaxRuleRules.NoRuleCode, message, ErrorType.Validation) { Data = new Dictionary<string, object?> { ["link"] = "/app/fiscal/regras" } };
    }
}

/// <summary>
/// <c>FiscalSettingsChanged</c> (FIS-02a) → aviso de revisão (FIS-02b CA 6): as regras não inativas que usam um valor que mudou ganham o
/// aviso "Os valores padrão mudaram em Configurações → Fiscal. Revise esta regra."; nenhuma regra muda sozinha. Roda na transação da inbox.
/// </summary>
public sealed class TaxRuleReviewConsumer(IFiscalDb db) : IIntegrationEventConsumer<FiscalSettingsChanged>
{
    public async Task HandleAsync(FiscalSettingsChanged integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var rules = await db.TaxRules.Where(r => r.Status != TaxRuleStatus.Inactive && !r.NeedsReview).ToListAsync(cancellationToken);
        var changed = false;
        foreach (var rule in rules.Where(r => TaxRuleRules.AffectedBy(r, integrationEvent.ChangedKeys, integrationEvent.RegimeChanged)))
        {
            rule.FlagForReview();
            changed = true;
        }

        if (changed)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
