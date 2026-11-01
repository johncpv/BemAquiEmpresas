using System.Globalization;
using System.Text;
using BemAqui.SharedKernel;

namespace BemAqui.Fiscal.Domain;

/// <summary>Totais da nota (jsonb, CA 3): soma dos itens, sem novo arredondamento; <see cref="CostTax"/> é o que entra no custo do projeto.</summary>
public sealed record InvoiceTotals(decimal Products, decimal Discount, decimal Freight, decimal OtherExpenses, IReadOnlyList<TaxTotal> Taxes, decimal InvoiceTotal, decimal CostTax)
{
    public static InvoiceTotals Empty { get; } = new(0m, 0m, 0m, 0m, [], 0m, 0m);

    /// <summary>Soma dos valores dos tributos (o "impostos" da lista e dos totais do filtro, CA 17), sem DIFAL (bloqueado).</summary>
    public decimal TaxAmount => Taxes.Where(t => t.Code != TaxCodes.Difal).Sum(t => t.Amount);
}

/// <summary>
/// Item da NF-e de saída (<c>fiscal.outgoing_invoice_items</c>, CA 1 e 3): a linha do romaneio com descrição, NCM, CEST, origem, unidade,
/// quantidade (até o expedido), valor unitário do contrato, desconto, o frete rateado, as séries (copiadas, nas informações do item) e o
/// resultado do <c>TaxCalculator</c> (regra e versão usadas, CFOP e tributos em jsonb). Nenhum valor de imposto é digitado (ponto 47): só a
/// alíquota do IPI da TIPI do NCM, quando a regra manda usar a TIPI (a tabela não é importada no MVP).
/// </summary>
public sealed class OutgoingInvoiceItem : Entity, IAuditable
{
    public const int DescriptionMaxLength = NfeTextRules.ProductDescription;
    public const int InfoMaxLength = NfeTextRules.ItemInformation;

    public Guid InvoiceId { get; init; }

    public int Sequence { get; init; }

    public Guid? ShipmentLineId { get; init; }

    public Guid? ProjectItemId { get; init; }

    public Guid ItemId { get; init; }

    public string Code { get; init; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string? Ncm { get; init; }

    public string? Cest { get; init; }

    public int Origin { get; init; }

    public string Unit { get; init; } = "UN";

    /// <summary><c>Equipment</c> ou <c>Product</c> (o tipo do item do orçamento, D4): <c>Service</c> nunca entra na NF-e.</summary>
    public string QuoteItemKind { get; init; } = "Equipment";

    /// <summary>Operação do item: venda de produção própria ou revenda (pelo tipo do item).</summary>
    public TaxOperation Operation { get; init; } = TaxOperation.OwnProductionSale;

    /// <summary>Quantidade expedida no romaneio: a nota não passa dela (CA 3).</summary>
    public decimal ShippedQuantity { get; init; }

    public decimal Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal Discount { get; set; }

    /// <summary>Frete e outras despesas da nota rateados neste item (pelo valor).</summary>
    public decimal Freight { get; set; }

    public decimal OtherExpenses { get; set; }

    /// <summary>Alíquota do IPI da TIPI do NCM (só quando a regra diz "pela TIPI"); vem da última nota com o mesmo NCM.</summary>
    public decimal? IpiRate { get; set; }

    /// <summary>Informações do item (<c>infAdProd</c>, até 500): as séries vêm preenchidas.</summary>
    public string? AdditionalInfo { get; set; }

    public Guid[] SerialIds { get; init; } = [];

    public string[] SerialNumbers { get; init; } = [];

    /// <summary>Regra escolhida pelo usuário entre as ativas que se aplicam (CA 3); nula, a resolução automática da FIS-02b.</summary>
    public Guid? ChosenRuleId { get; set; }

    public Guid? TaxRuleId { get; set; }

    public Guid? TaxRuleVersionId { get; set; }

    public int? TaxRuleVersion { get; set; }

    public string? TaxRuleName { get; set; }

    public string? Cfop { get; set; }

    /// <summary>CST e <c>cClassTrib</c> do IBS/CBS e o enquadramento do IPI, da regra (para o pedido ao Emissor).</summary>
    public string? IbsCbsClassification { get; set; }

    public string? IpiFramework { get; set; }

    public IReadOnlyList<TaxLine> Taxes { get; set; } = [];

    public decimal GrossAmount { get; set; }

    public decimal Total { get; set; }

    /// <summary>Tributos do item que entram no custo do projeto (coluna <c>cost_tax_amount</c>); o Commercial lança na razão pelo evento <c>InvoiceAuthorized</c>.</summary>
    public decimal CostTaxAmount { get; set; }

    /// <summary>Aplica o resultado do motor (CA 3): regra e versão, CFOP, tributos e totais; sem resultado, limpa.</summary>
    public void ApplyResult(TaxItemResult? result, TaxRule? rule)
    {
        TaxRuleId = rule?.RuleId;
        TaxRuleVersionId = rule?.Id;
        TaxRuleVersion = rule?.RuleVersion;
        TaxRuleName = rule?.Name;
        IbsCbsClassification = rule?.IbsCbs?.Classification;
        IpiFramework = rule?.Ipi?.Framework;
        Cfop = result?.Cfop;
        Taxes = result?.Taxes ?? [];
        GrossAmount = result?.GrossAmount ?? TaxCalculator.Round(Quantity * UnitPrice);
        Total = result?.ItemTotal ?? 0m;
        CostTaxAmount = result?.CostTax ?? 0m;
    }

    public TaxItem ToTaxItem() => new(Description, Ncm, Origin, Cest, Quantity, UnitPrice, Discount, Freight, OtherExpenses, IpiRate);
}

/// <summary>Parcela do contrato vista na nota (<c>fiscal.outgoing_invoice_receivables</c>): só referência, a nota não cria nem muda título (D4).</summary>
public sealed class OutgoingInvoiceReceivable : Entity
{
    public Guid InvoiceId { get; init; }

    public Guid ReceivableId { get; init; }
}

/// <summary>Por que um número de NF-e ficou sem nota válida.</summary>
public static class NumberGapReasons
{
    /// <summary>Rejeitada: o número foi gasto e vai para "Números a inutilizar" (FIS-03b).</summary>
    public const string Rejected = "Rejected";

    /// <summary>Denegada: o número fica registrado como denegado (não se inutiliza).</summary>
    public const string Denied = "Denied";
}

/// <summary>
/// Número de NF-e gasto sem nota válida (<c>fiscal.number_gaps</c>, CA 8 e 9): a rejeitada entra em "Números a inutilizar" e a denegada fica
/// registrada. A inutilização (FIS-03b) marca <see cref="InvalidatedAt"/>. Uma linha por série e número.
/// </summary>
public sealed class NumberGap : Entity, IAuditable, IConcurrencyVersioned
{
    public int Series { get; init; }

    public long Number { get; init; }

    public Guid InvoiceId { get; init; }

    public int Attempt { get; init; }

    public string Reason { get; init; } = NumberGapReasons.Rejected;

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? InvalidatedAt { get; private set; }

    /// <summary>A inutilizar (só a rejeitada) ou inutilizado com o evento homologado (FIS-03b CA 10).</summary>
    public NumberGapStatus Status { get; private set; } = NumberGapStatus.Pending;

    public Guid? InvalidationEventId { get; private set; }

    /// <summary>Entra em "A inutilizar": rejeitada e ainda não inutilizada (a denegada fica só registrada).</summary>
    public bool ToInvalidate => Reason == NumberGapReasons.Rejected && Status == NumberGapStatus.Pending;

    public uint Version { get; set; }

    /// <summary>
    /// Inutilização enviada (CA 10 e 15): o número fica preso ao evento até o desfecho; com o <c>xmin</c>, dois pedidos juntos da mesma faixa
    /// deixam só um ir ao Emissor.
    /// </summary>
    public Error? BeginInvalidation(Guid eventId, DateTimeOffset? sentAt, DateTimeOffset now)
    {
        if (!ToInvalidate)
        {
            return Error.Conflict(FiscalErrorCodes.NumberNotInvalidable, $"O número {Number.ToString(System.Globalization.CultureInfo.InvariantCulture)} não está em Números a inutilizar.");
        }

        if (InvalidationEventId is not null && sentAt is { } sent && now - sent < FiscalEventRules.ResendAfter)
        {
            return Error.Conflict(FiscalErrorCodes.EventInProgress, "A inutilização desta faixa já foi enviada. Aguarde a resposta da SEFAZ.");
        }

        InvalidationEventId = eventId;
        return null;
    }

    /// <summary>Recusada: o número volta a ficar só a inutilizar.</summary>
    public void InvalidationRefused(Guid eventId)
    {
        if (Status == NumberGapStatus.Pending && InvalidationEventId == eventId)
        {
            InvalidationEventId = null;
        }
    }

    public void Invalidate(Guid eventId, DateTimeOffset at)
    {
        if (Status == NumberGapStatus.Invalidated)
        {
            return;
        }

        Status = NumberGapStatus.Invalidated;
        InvalidationEventId = eventId;
        InvalidatedAt = at;
    }
}

/// <summary>
/// Saneamento dos textos livres da NF-e 4.00 (CA 4, revisão §4.3): sem caracteres de controle, espaços normalizados e dentro dos limites
/// do esquema, conferidos antes de reservar qualquer número. O escape para o XML é do serializador do Emissor; nada é montado como HTML.
/// </summary>
public static class NfeTextRules
{
    public const int ProductDescription = 120;
    public const int ItemInformation = 500;
    public const int ComplementaryInformation = 5000;
    public const int Name = 60;
    public const int Street = 60;
    public const int Number = 60;
    public const int Complement = 60;
    public const int District = 60;
    public const int City = 60;

    /// <summary>Sem caracteres de controle e com os espaços normalizados (um só, sem nas pontas); vazio vira nulo.</summary>
    public static string? Clean(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var builder = new StringBuilder(value.Length);
        var space = false;
        foreach (var c in value.Normalize(NormalizationForm.FormC))
        {
            if (char.IsWhiteSpace(c) || char.IsControl(c) || CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
            {
                space = builder.Length > 0;
                continue;
            }

            if (space)
            {
                builder.Append(' ');
                space = false;
            }

            builder.Append(c);
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    /// <summary>O texto cabe no limite? (contado depois do saneamento).</summary>
    public static bool Fits(string? cleaned, int max) => cleaned is null || cleaned.Length <= max;
}
