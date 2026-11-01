using BemAqui.BuildingBlocks.Time;
using BemAqui.Fiscal.Application.Settings;
using BemAqui.Fiscal.Application.TaxRules;
using BemAqui.Fiscal.Domain;
using BemAqui.Identity.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Fiscal.Application.Invoices;

/// <summary>Regras candidatas do cálculo: as ativas do regime atual e se a emissão exige regra conferida (produção, FIS-02b CA 4).</summary>
public sealed record TaxRuleCandidates(IReadOnlyList<TaxRule> Rules, TaxIssuer Issuer, bool RequireConfirmed, DateOnly Date);

/// <summary>
/// Cálculo da nota (FIS-03a CA 1 e 3): resolve a regra de cada item como a FIS-02b (só ativa do regime, vigente, pelas condições; em
/// produção, só conferida) ou usa a escolhida na tela entre as que se aplicam; rateia frete e outras despesas pelo valor; aplica o
/// <see cref="TaxCalculator"/> item a item e soma os totais. Nenhum valor de imposto vem da tela. Sem regra ou com dado faltando, o item fica
/// sem tributo e a nota guarda o motivo (a emissão espera).
/// </summary>
public sealed class OutgoingInvoiceCalculator(IFiscalDb db, IFiscalSettings settings, ICompanyFiscalProfile company, TimeProvider clock)
{
    public async Task<TaxRuleCandidates> CandidatesAsync(CancellationToken cancellationToken = default)
    {
        var current = await settings.CurrentAsync(cancellationToken);
        var crt = FiscalSettings.CrtOf(current.Regime);
        var profile = await company.GetFiscalProfileAsync(cancellationToken);
        var rules = await db.TaxRules.AsNoTracking().Where(r => r.Status == TaxRuleStatus.Active && r.CrtScope == crt).ToListAsync(cancellationToken);
        return new TaxRuleCandidates(rules, new TaxIssuer((profile.State ?? string.Empty).Trim().ToUpperInvariant(), crt), current.Environment == FiscalEnvironment.Production,
            BusinessTime.Today(clock));
    }

    /// <summary>Recalcula a nota inteira (rascunho criado, salvo e conferido antes de emitir).</summary>
    public async Task RecalculateAsync(OutgoingInvoice invoice, IReadOnlyList<OutgoingInvoiceItem> items, CancellationToken cancellationToken = default) =>
        Recalculate(invoice, items, await CandidatesAsync(cancellationToken));

    public static void Recalculate(OutgoingInvoice invoice, IReadOnlyList<OutgoingInvoiceItem> items, TaxRuleCandidates candidates)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(candidates);
        var ordered = items.OrderBy(i => i.Sequence).ToList();
        var gross = ordered.Select(i => TaxCalculator.Round(i.Quantity * i.UnitPrice) - i.Discount).Select(v => Math.Max(0m, v)).ToList();
        var freight = OutgoingInvoiceRules.Apportion(gross, TaxCalculator.Round(invoice.Freight));
        var other = OutgoingInvoiceRules.Apportion(gross, TaxCalculator.Round(invoice.OtherExpenses));
        invoice.FinalConsumer = invoice.RecipientType != RecipientType.Contributor;
        var recipient = new TaxRecipient((invoice.RecipientState ?? string.Empty).Trim().ToUpperInvariant(), invoice.RecipientType, invoice.FinalConsumer);
        string? error = null;
        if (!TaxRuleRules.States.Contains(candidates.Issuer.State, StringComparer.Ordinal))
        {
            error = "Informe a UF da empresa em Dados da empresa.";
        }
        else if (!TaxRuleRules.States.Contains(recipient.State, StringComparer.Ordinal))
        {
            error = "Informe o endereço do cliente com a UF no cadastro.";
        }

        for (var i = 0; i < ordered.Count; i++)
        {
            var item = ordered[i];
            item.Freight = freight[i];
            item.OtherExpenses = other[i];
            if (error is not null)
            {
                item.ApplyResult(null, null);
                continue;
            }

            var rule = RuleFor(item, recipient, candidates);
            if (rule is null)
            {
                error = item.ChosenRuleId is not null
                    ? $"A regra escolhida para o item {item.Sequence} não vale mais para este cliente. Escolha outra ou volte para a automática."
                    : TaxRuleResolver.NoRule(item.Operation, recipient, item.Ncm).Message;
                item.ApplyResult(null, null);
                continue;
            }

            var result = TaxCalculator.CalculateItem(i, candidates.Issuer, recipient, item.ToTaxItem(), rule);
            if (result.IsFailure)
            {
                error = result.Error!.Code == TaxCalculator.IpiRateMissingCode
                    ? $"Informe a alíquota do IPI na TIPI para o NCM {item.Ncm ?? "do item"} (item {item.Sequence})."
                    : $"Item {item.Sequence}: {result.Error.Message}";
                item.ApplyResult(null, rule);
                continue;
            }

            item.ApplyResult(result.Value, rule);
        }

        var first = ordered.Count > 0 ? ordered[0] : null;
        invoice.Operation = first?.Operation ?? invoice.Operation;
        invoice.NatureOfOperation = first?.TaxRuleVersionId is { } versionId ? candidates.Rules.FirstOrDefault(r => r.Id == versionId)?.NatureOfOperation ?? invoice.NatureOfOperation : invoice.NatureOfOperation;
        invoice.ApplyTotals(Totals(ordered), error);
    }

    /// <summary>Soma dos itens (sem novo arredondamento), tributo a tributo na ordem do <see cref="TaxCodes.Order"/>.</summary>
    public static InvoiceTotals Totals(IReadOnlyList<OutgoingInvoiceItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var lines = items.SelectMany(i => i.Taxes).ToList();
        var taxes = TaxCodes.Order.Select(code => (code, list: lines.Where(l => l.Code == code).ToList())).Where(x => x.list.Count > 0)
            .Select(x => new TaxTotal(x.code, x.list.Sum(l => l.Base), x.list.Sum(l => l.Amount))).ToList();
        return new InvoiceTotals(items.Sum(i => i.GrossAmount), items.Sum(i => i.Discount), items.Sum(i => i.Freight), items.Sum(i => i.OtherExpenses), taxes,
            items.Sum(i => i.Total), items.Sum(i => i.CostTaxAmount));
    }

    /// <summary>A regra do item: a escolhida (se ainda vale) ou a da resolução automática.</summary>
    public static TaxRule? RuleFor(OutgoingInvoiceItem item, TaxRecipient recipient, TaxRuleCandidates candidates)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(candidates);
        var applicable = Applicable(item, recipient, candidates).ToList();
        return item.ChosenRuleId is { } chosen
            ? applicable.FirstOrDefault(r => r.RuleId == chosen)
            : TaxRuleResolver.Pick(applicable, recipient.State, recipient.Type, item.Ncm, candidates.Date, candidates.RequireConfirmed);
    }

    /// <summary>As regras que podem ser escolhidas para o item (CA 3): ativas, da operação, vigentes e com as condições do destinatário e do NCM.</summary>
    public static IEnumerable<TaxRule> Applicable(OutgoingInvoiceItem item, TaxRecipient recipient, TaxRuleCandidates candidates)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(recipient);
        ArgumentNullException.ThrowIfNull(candidates);
        return candidates.Rules.Where(r => r.Operation == item.Operation && r.Status == TaxRuleStatus.Active && r.AppliesOn(candidates.Date)
            && (!candidates.RequireConfirmed || r.IsConfirmed) && r.Conditions.Matches(recipient.State, recipient.Type, item.Ncm));
    }
}
