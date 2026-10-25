using System.Text.Json.Serialization;

namespace BemAqui.SharedKernel;

/// <summary>
/// Origem de um título (ADR 0019 §14): vocabulário comum entre Finance e Commercial, no SharedKernel para nenhum Contracts
/// referenciar outro.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<DocumentOrigin>))]
public enum DocumentOrigin
{
    /// <summary>Lançado à mão no Financeiro.</summary>
    Manual,

    /// <summary>Implantação financeira (FIN-00): em aberto na data de corte, fora do DRE por competência e da pontualidade.</summary>
    Opening,

    /// <summary>Parcela do contrato (COM-04b).</summary>
    Contract,

    /// <summary>Despesa recorrente (FIN-02).</summary>
    Recurring,

    /// <summary>OS de instalação, título a pagar do instalador (COM-06).</summary>
    Installer,

    /// <summary>Pedido de compra (Fase 3).</summary>
    Purchase,

    /// <summary>Folha (Fase 4).</summary>
    Payroll,

    /// <summary>Manutenção de patrimônio (Fase 3).</summary>
    Maintenance,

    /// <summary>Recebimento de compra (CMP-04, ADR 0024 §3): os títulos em aberto do próprio recebimento, um por parcela da condição do pedido.</summary>
    GoodsReceipt,
}
