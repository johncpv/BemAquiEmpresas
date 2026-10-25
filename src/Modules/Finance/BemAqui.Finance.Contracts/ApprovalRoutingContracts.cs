using BemAqui.SharedKernel;

namespace BemAqui.Finance.Contracts;

/// <summary>
/// Tipos de documento do motor de alçadas (<c>finance.approval_rules.document_type</c>, FIN-03b, ADR 0024 §4) e o mapa tipo →
/// permissão do aprovador, que fica no Finance: <c>PaymentOrder</c> → <c>finance.approve</c>; <c>PurchaseOrder</c> → <c>purchasing.approve</c>.
/// </summary>
public static class ApprovalDocumentTypes
{
    public const string PaymentOrder = "PaymentOrder";
    public const string PurchaseOrder = "PurchaseOrder";

    /// <summary>
    /// Ajuste de estoque (EST-01b CA 14): declarado só no mapa tipo → permissão (<c>inventory.approve</c>). Todo ajuste é aprovado por
    /// quem tem a ação, sem faixa de valor; não entra em <see cref="All"/> nem na tela de Alçadas enquanto não houver regra para ele.
    /// </summary>
    public const string StockAdjustment = "StockAdjustment";

    public static IReadOnlyList<string> All { get; } = [PaymentOrder, PurchaseOrder];

    public static bool IsKnown(string? documentType) => documentType is PaymentOrder or PurchaseOrder;

    /// <summary>Permissão efetiva de quem aprova o tipo (CMP-03a CA 10): quem tem <c>finance.approve</c> sem <c>purchasing.approve</c> não aprova pedido.</summary>
    public static string ApproverPermission(string documentType) => documentType switch
    {
        PaymentOrder => "finance.approve",
        PurchaseOrder => "purchasing.approve",
        StockAdjustment => "inventory.approve",
        _ => throw new ArgumentOutOfRangeException(nameof(documentType), documentType, "Tipo de documento sem alçada."),
    };
}

/// <summary>Nível resolvido (ADR 0024 §4): a regra aplicada, o nome do aprovador (grupo ou usuário) e quem pode decidir.</summary>
public sealed record ApprovalRoutingLevel(int Level, Guid RuleId, string ApproverName, IReadOnlyList<Guid> Eligible);

/// <summary>
/// Resolução da alçada de um documento: os níveis em ordem e se o valor de referência mudou a regra em relação ao valor do próprio
/// documento (<c>SplitDetected</c>: o chamador grava e mostra o aviso de fracionamento).
/// </summary>
public sealed record ApprovalRouting(decimal ReferenceAmount, bool SplitDetected, IReadOnlyList<ApprovalRoutingLevel> Levels);

/// <summary>
/// Motor de alçadas da FIN-03b para os outros módulos (ADR 0024 §4): o Finance só resolve a regra e confere a permissão do tipo; quem
/// chama guarda os próprios níveis e decisões e calcula o valor de referência contra o fracionamento. Regras do tipo editadas na tela
/// de Alçadas do Financeiro.
/// </summary>
public interface IApprovalRouting
{
    /// <summary>
    /// Níveis do documento pelo <paramref name="referenceAmount"/> (o maior entre o valor do documento e a soma do fornecedor em 30 dias),
    /// com a conta (regra por conta vence a faixa) e quem pode decidir cada nível: tem a permissão do tipo, não é o solicitante (exceto
    /// o Proprietário único, <c>SoleOwnerRule</c>) e o nível não repete o único aprovador do anterior. 422 <c>approval_not_viable</c>
    /// com o motivo quando algum nível fica sem aprovador ou nenhuma regra cobre o valor (ADR 0019 §6).
    /// </summary>
    /// <param name="documentAmount">Valor do próprio documento, para detectar o fracionamento (a regra pelo valor de referência é outra).</param>
    Task<Result<ApprovalRouting>> ResolveAsync(
        string documentType, decimal referenceAmount, decimal documentAmount, Guid? accountId, Guid? requesterId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Quem pode decidir o nível agora pela regra gravada no envio: está na regra, tem a permissão do tipo e não é o solicitante (exceto
    /// o Proprietário único). Quem já decidiu outro nível é tirado pelo chamador, que guarda as decisões.
    /// </summary>
    Task<IReadOnlyList<Guid>> EligibleAsync(string documentType, int level, Guid ruleId, Guid? requesterId, CancellationToken cancellationToken = default);

    /// <summary>Nome do aprovador da regra (grupo ou usuário), inclusive de regra excluída depois do envio.</summary>
    Task<string> ApproverNameAsync(Guid ruleId, CancellationToken cancellationToken = default);
}
