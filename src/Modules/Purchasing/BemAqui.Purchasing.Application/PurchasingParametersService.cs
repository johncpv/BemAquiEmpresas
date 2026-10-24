using BemAqui.BuildingBlocks.Authorization;
using BemAqui.Purchasing.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Purchasing.Application;

/// <summary>
/// Parâmetros de Compras (ADR 0016 adendo M6), em <c>purchasing.parameters</c>: o prazo padrão de compra (<c>default_lead_days</c>,
/// 15 dias, de 0 a 365; ponto 31) e a tolerância do recebimento (CMP-04, 0% a 50%), editados com <c>settings.edit</c> e auditados; e a marca da recomposição da implantação (D3).
/// </summary>
public sealed class PurchasingParametersService(IPurchasingDb db, IPermissionChecker permissions, BemAqui.Finance.Contracts.IChartOfAccounts chart, TimeProvider clock)
{
    public const string DefaultLeadDays = "default_lead_days";

    /// <summary>Conta e natureza dos títulos conferidas com a contadora (P-17); enquanto não, a tela mostra "A confirmar com a contadora".</summary>
    public const string AccountingConfirmed = "accounting_confirmed";
    public const string ShortagesBackfilled = "shortages_backfilled";

    /// <summary>Conta do plano dos títulos do pedido (CMP-03a CA 19, ponto 39): código, padrão "2.1" (Materiais).</summary>
    public const string PayableAccountCode = "payable_account_code";

    /// <summary>Natureza de custo dos itens do pedido: padrão <c>Material</c>.</summary>
    public const string PayableCostType = "payable_cost_type";

    /// <summary>Conta do título de frete de outro fornecedor (ADR 0024 §2): padrão "2.4" (Fretes); o frete do próprio fornecedor vai nas parcelas do pedido.</summary>
    public const string FreightAccountCode = "freight_account_code";

    /// <summary>Natureza do frete: padrão <c>Freight</c>.</summary>
    public const string FreightCostType = "freight_cost_type";

    /// <summary>Tolerância do recebimento (CMP-04 CA 3 e 4, D8): % acima da quantidade da linha e do total aprovado; padrão 0%, de 0 a 50.</summary>
    public const string ReceiptTolerancePct = "receipt_tolerance_pct";

    /// <summary>Cotação (CMP-02 CA 6, D8): custo total da resposta acima deste valor exige o anexo da proposta; padrão R$ 5.000,00.</summary>
    public const string QuoteAttachmentAmount = "quote_attachment_amount";

    public const string DefaultPayableAccount = "2.1";
    public const string DefaultFreightAccount = "2.4";

    /// <summary>
    /// Padrões dos títulos do pedido (CA 19): a conta e a natureza dos itens e do frete, vindos de <c>purchasing.parameters</c> (vazio =
    /// padrão). A natureza inválida volta ao padrão.
    /// </summary>
    public async Task<PurchasePayableDefaults> PayableDefaultsAsync(CancellationToken cancellationToken = default)
    {
        var texts = await db.PurchasingParameters.AsNoTracking().Where(p => p.Text != null).ToDictionaryAsync(p => p.Key, p => p.Text!, cancellationToken);
        return new PurchasePayableDefaults(
            texts.GetValueOrDefault(PayableAccountCode) ?? DefaultPayableAccount, CostOf(texts.GetValueOrDefault(PayableCostType), CostType.Material),
            texts.GetValueOrDefault(FreightAccountCode) ?? DefaultFreightAccount, CostOf(texts.GetValueOrDefault(FreightCostType), CostType.Freight));
    }

    private static CostType CostOf(string? text, CostType fallback) => Enum.TryParse<CostType>(text, out var parsed) && Enum.IsDefined(parsed) ? parsed : fallback;

    public async Task<int> LeadDaysAsync(CancellationToken cancellationToken = default) =>
        await ValueAsync(DefaultLeadDays, cancellationToken) is { } days && days >= PurchaseRequestRules.MinLeadDays && days <= PurchaseRequestRules.MaxLeadDays
            ? (int)days
            : PurchaseRequestRules.DefaultLeadDays;

    /// <summary>Tolerância do recebimento em % (CA 3, 4): padrão 0; valor fora de 0 a 50 volta ao padrão.</summary>
    public async Task<decimal> ReceiptToleranceAsync(CancellationToken cancellationToken = default) =>
        await ValueAsync(ReceiptTolerancePct, cancellationToken) is { } pct && pct >= 0m && pct <= GoodsReceiptRules.MaxTolerancePct ? pct : 0m;

    /// <summary>Limite do anexo da proposta (CA 6): padrão R$ 5.000,00; valor fora de 0 a 1.000.000 volta ao padrão.</summary>
    public async Task<decimal> QuoteAttachmentAmountAsync(CancellationToken cancellationToken = default) =>
        await ValueAsync(QuoteAttachmentAmount, cancellationToken) is { } amount && amount >= 0m && amount <= RfqRules.MaxAttachmentAmount ? amount : RfqRules.DefaultAttachmentAmount;

    public async Task<PurchasingParametersView> GetAsync(CancellationToken cancellationToken = default)
    {
        var id = await db.PurchasingParameters.AsNoTracking().Where(p => p.Key == DefaultLeadDays).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(cancellationToken);
        var defaults = await PayableDefaultsAsync(cancellationToken);
        var accounts = (await chart.PostableAsync(null, cancellationToken)).OrderBy(a => a.Code, StringComparer.Ordinal)
            .Select(a => new PurchasingAccountOption(a.Code, a.Name)).ToList();
        return new PurchasingParametersView(await LeadDaysAsync(cancellationToken), id, defaults.AccountCode, defaults.CostType, defaults.FreightAccountCode, defaults.FreightCostType,
            await ValueAsync(AccountingConfirmed, cancellationToken) is 1m, accounts, await ReceiptToleranceAsync(cancellationToken), await QuoteAttachmentAmountAsync(cancellationToken));
    }

    public async Task<Result<PurchasingParametersView>> UpdateAsync(PurchasingParametersInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await permissions.HasPermissionAsync("settings", "edit", cancellationToken))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Você não tem acesso a esta funcionalidade. Fale com o administrador.");
        }

        if (input.DefaultLeadDays is < PurchaseRequestRules.MinLeadDays or > PurchaseRequestRules.MaxLeadDays)
        {
            return Invalid("defaultLeadDays", "Informe de 0 a 365 dias.");
        }

        if (input.ReceiptTolerancePct is < 0m or > GoodsReceiptRules.MaxTolerancePct || (input.ReceiptTolerancePct is { } tol && decimal.Round(tol, 2) != tol))
        {
            return Invalid("receiptTolerancePct", "Informe a tolerância de 0% a 50%, com até 2 casas.");
        }

        if (input.QuoteAttachmentAmount is < 0m or > RfqRules.MaxAttachmentAmount || (input.QuoteAttachmentAmount is { } limit && decimal.Round(limit, 2) != limit))
        {
            return Invalid("quoteAttachmentAmount", "Informe o valor de R$ 0,00 a R$ 1.000.000,00, com até 2 casas.");
        }

        // CMP-03a (decisão do PO de 21/10): conta e natureza dos títulos do pedido e do frete, editáveis aqui até a contadora confirmar.
        var postable = (await chart.PostableAsync(null, cancellationToken)).Select(a => a.Code).ToHashSet(StringComparer.Ordinal);
        foreach (var (field, code) in new[] { ("payableAccountCode", input.PayableAccountCode), ("freightAccountCode", input.FreightAccountCode) })
        {
            if (code is not null && !postable.Contains(code.Trim()))
            {
                return Invalid(field, "Escolha uma conta analítica e ativa do plano de contas.");
            }
        }

        await SetAsync(DefaultLeadDays, input.DefaultLeadDays, cancellationToken);
        await SetTextAsync(PayableAccountCode, input.PayableAccountCode?.Trim(), cancellationToken);
        await SetTextAsync(PayableCostType, input.PayableCostType?.ToString(), cancellationToken);
        await SetTextAsync(FreightAccountCode, input.FreightAccountCode?.Trim(), cancellationToken);
        await SetTextAsync(FreightCostType, input.FreightCostType?.ToString(), cancellationToken);
        if (input.ReceiptTolerancePct is { } tolerance)
        {
            await SetAsync(ReceiptTolerancePct, tolerance, cancellationToken);
        }

        if (input.QuoteAttachmentAmount is { } attachmentAmount)
        {
            await SetAsync(QuoteAttachmentAmount, attachmentAmount, cancellationToken);
        }

        if (input.AccountingConfirmed is { } confirmed)
        {
            await SetAsync(AccountingConfirmed, confirmed ? 1m : 0m, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(cancellationToken);
    }

    private static Error Invalid(string field, string message) => Error.Validation(Error.Codes.Validation, message, new Dictionary<string, string[]> { [field] = [message] });

    private async Task SetTextAsync(string key, string? text, CancellationToken ct)
    {
        if (text is null)
        {
            return;
        }

        var parameter = await db.PurchasingParameters.FirstOrDefaultAsync(p => p.Key == key, ct);
        if (parameter is null)
        {
            db.PurchasingParameters.Add(new PurchasingParameter { Key = key, Text = text, UpdatedAt = clock.GetUtcNow() });
        }
        else if (parameter.Text != text)
        {
            parameter.Text = text;
            parameter.UpdatedAt = clock.GetUtcNow();
        }
    }

    /// <summary>A recomposição da implantação já rodou neste tenant (D3)?</summary>
    public async Task<bool> ShortagesBackfilledAsync(CancellationToken cancellationToken = default) => await ValueAsync(ShortagesBackfilled, cancellationToken) is 1m;

    /// <summary>Marca a recomposição da implantação como feita (grava no próximo <c>SaveChanges</c>).</summary>
    public Task MarkShortagesBackfilledAsync(CancellationToken cancellationToken = default) => SetAsync(ShortagesBackfilled, 1m, cancellationToken);

    private async Task<decimal?> ValueAsync(string key, CancellationToken ct) =>
        await db.PurchasingParameters.AsNoTracking().Where(p => p.Key == key).Select(p => p.Value).FirstOrDefaultAsync(ct);

    private async Task SetAsync(string key, decimal value, CancellationToken ct)
    {
        var parameter = await db.PurchasingParameters.FirstOrDefaultAsync(p => p.Key == key, ct);
        if (parameter is null)
        {
            db.PurchasingParameters.Add(new PurchasingParameter { Key = key, Value = value, UpdatedAt = clock.GetUtcNow() });
        }
        else if (parameter.Value != value)
        {
            parameter.Value = value;
            parameter.UpdatedAt = clock.GetUtcNow();
        }
    }
}
