using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Pos.Domain;

/// <summary>Como o servidor tratou o evento do aparelho (OFF-02 CA 5): aceito, aceito com divergência ou posto em quarentena (nunca descartado).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DeviceEventOutcome>))]
public enum DeviceEventOutcome
{
    Accepted,
    AcceptedWithDivergence,
    Quarantined,
}

/// <summary>
/// Evento do aparelho de caixa como chegou (<c>pos.device_events</c>, OFF-02 CA 2 e 3): o registro imutável da trilha do caixa. Gravado por SQL com
/// <c>ON CONFLICT (event_id) DO NOTHING</c>; UPDATE e DELETE falham por trigger. Não é auditado: ele é a trilha. Nunca leva dado pessoal (o CPF
/// sobe num bloco à parte e vai cifrado para o <c>Sales</c>), nem dado de cartão (o texto que parece número de cartão sai do conteúdo antes de
/// gravar). Loja, caixa e época vêm do principal do aparelho, nunca do corpo. <see cref="Outcome"/> é o tratamento na chegada; liberar da
/// quarentena fica na divergência.
/// </summary>
public sealed class DeviceEvent
{
    public Guid EventId { get; init; }

    public Guid DeviceId { get; init; }

    public Guid StoreId { get; init; }

    public Guid RegisterId { get; init; }

    public int DeviceEpoch { get; init; }

    /// <summary>Sequência do evento no caixa e na época (1, 2, 3...).</summary>
    public long Seq { get; init; }

    public int SchemaVersion { get; init; }

    /// <summary><c>pos.sale</c>, <c>pos.cash</c> ou <c>pos.print</c>.</summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>Instante do aparelho (o que ele diz).</summary>
    public DateTimeOffset OccurredAt { get; init; }

    /// <summary>Instante do servidor ao receber.</summary>
    public DateTimeOffset ReceivedAt { get; init; }

    /// <summary>Relógio do aparelho menos o do servidor no envio, em milissegundos (CA 10).</summary>
    public long ClockSkewMs { get; init; }

    public string? PrevHash { get; init; }

    public string Hash { get; init; } = string.Empty;

    /// <summary>A sequência e o hash fecham com o evento anterior (falso: quarentena "Sequência quebrada", CA 11).</summary>
    public bool ChainOk { get; init; }

    /// <summary>O conteúdo como o aparelho mandou (texto exato, o hash confere), sem dado de cartão.</summary>
    public string Payload { get; init; } = "{}";

    public DeviceEventOutcome Outcome { get; init; }

    /// <summary>O aparelho já estava revogado ou substituído quando enviou (CA 12).</summary>
    public bool DeviceRevoked { get; init; }
}

/// <summary>Tipo da divergência da sincronização (CA 9 a 13 e os requisitos de segurança herdados).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SyncDivergenceKind>))]
public enum SyncDivergenceKind
{
    /// <summary>Preço desatualizado: o aparelho vendeu com uma tabela mais antiga que a vigente (vale o preço do aparelho).</summary>
    PriceOutdated,

    /// <summary>Preço divergente: diferente do catálogo na versão vigente, versão nula ou futura, ou produto fora do catálogo da loja (vale o do aparelho).</summary>
    PriceMismatch,

    /// <summary>Relógio do aparelho fora da tolerância (as duas datas ficam gravadas).</summary>
    DeviceClock,

    /// <summary>Lacuna de sequência ou hash que não fecha com o anterior: quarentena a partir dali.</summary>
    BrokenSequence,

    /// <summary>Aparelho revogado ou substituído enviando o que tinha gravado: quarentena para revisão.</summary>
    ReplacedDevice,

    /// <summary>Versão do formato do evento fora do prazo de suporte.</summary>
    SchemaVersion,

    /// <summary>Venda ou evento que não passou na conferência do servidor (operador, permissão, autorização, pagamento, dado de cartão).</summary>
    Validation,

    /// <summary>
    /// Autorização sem supervisor conferido (revisão de segurança M2, até a PDV-04): quem autorizou não tinha sessão por PIN neste aparelho no instante,
    /// ou é o próprio operador numa ação que pede supervisor. A venda entra; a pendência fica para o gerente. Desde a revisão de segurança da PDV-04 não é
    /// mais gerada (autorização de outra pessoa sem a aprovação por PIN vai para a quarentena); fica para as pendências antigas.
    /// </summary>
    Authorization,

    /// <summary>Soma dos pagamentos diferente do total, ou pagamento sem confirmação (precisa de alguém olhar).</summary>
    Payment,

    /// <summary>Sessão de caixa desconhecida, fechada, de outro caixa ou segunda abertura.</summary>
    CashSession,

    /// <summary>A loja não tinha documento fiscal para a venda (modo não fiscal não aceito na versão vigente): pendência do Proprietário.</summary>
    FiscalDocument,

    /// <summary>
    /// OFF-03 CA 4, 7 e 13: entrada ou aprovação sem rede a revisar (pessoa desativada, PIN ou código trocado, prova que não confere, aprovação acima
    /// dos limites da loja). O que a pessoa fez naquela entrada fica na quarentena até o Proprietário liberar.
    /// </summary>
    OfflineAccess,
}

[JsonConverter(typeof(JsonStringEnumConverter<SyncDivergenceStatus>))]
public enum SyncDivergenceStatus
{
    Open,
    Resolved,
}

/// <summary>Resolução escolhida por quem tem <c>sales.approve</c> (CA 15).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SyncResolution>))]
public enum SyncResolution
{
    /// <summary>Aceitar como está (a venda já entrou; só a divergência fecha).</summary>
    AcceptAsIs,

    /// <summary>Liberar da quarentena: projeta o evento (a venda entra, a sessão de caixa anda, a impressão fica registrada).</summary>
    ReleaseQuarantine,

    /// <summary>Manter fora: o evento continua só no registro, sem efeito (fica a justificativa).</summary>
    KeepOut,
}

/// <summary>
/// Divergência da sincronização (<c>pos.sync_divergences</c>, tela "Pendências do PDV", CA 14 e 15): o que divergiu, em qual loja, caixa, venda e
/// com qual operador. Quarentena = o evento ficou só no registro, sem efeito, até alguém liberar. Auditada (a resolução fica no histórico).
/// </summary>
[DocumentName("A pendência do PDV")]
public sealed class SyncDivergence : Entity, IAuditable, IConcurrencyVersioned
{
    public const int MaxSummaryLength = 300;
    public const int MaxReasonLength = 300;

    public SyncDivergenceKind Kind { get; init; }

    public SyncDivergenceStatus Status { get; private set; } = SyncDivergenceStatus.Open;

    /// <summary>O evento ficou sem efeito (só no registro) por causa desta divergência.</summary>
    public bool Quarantined { get; init; }

    public Guid EventId { get; init; }

    public string EventKind { get; init; } = string.Empty;

    public Guid StoreId { get; init; }

    public Guid RegisterId { get; init; }

    public Guid DeviceId { get; init; }

    public int DeviceEpoch { get; init; }

    public long Seq { get; init; }

    public Guid? SaleId { get; init; }

    public string? SaleNumber { get; init; }

    public Guid? OperatorId { get; init; }

    /// <summary>O que divergiu, em português, para a tela ("Arroz 5 kg: vendido a R$ 21,90; vigente R$ 23,50.").</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>Detalhe estruturado (jsonb, camelCase): os valores que divergiram. Nunca dado pessoal nem de cartão.</summary>
    public string? Details { get; init; }

    /// <summary>Instante do evento no aparelho.</summary>
    public DateTimeOffset OccurredAt { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public SyncResolution? Resolution { get; private set; }

    public string? ResolutionReason { get; private set; }

    public Guid? ResolvedBy { get; private set; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    public uint Version { get; set; }

    public void Resolve(SyncResolution resolution, string reason, Guid? by, DateTimeOffset at)
    {
        Status = SyncDivergenceStatus.Resolved;
        Resolution = resolution;
        ResolutionReason = reason;
        ResolvedBy = by;
        ResolvedAt = at;
    }
}

/// <summary>Exceção por operador (requisito da PDV-02): remoção, redução, descarte e evento recusado na conferência.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OperatorIncidentKind>))]
public enum OperatorIncidentKind
{
    ItemRemoved,
    QuantityReduced,
    SaleDiscarded,
    EventRejected,
}

/// <summary>
/// Linha do relatório de exceções por operador (<c>pos.operator_exceptions</c>): gravada na subida, uma por remoção, redução, descarte ou evento
/// posto em quarentena pela conferência. <see cref="OperatorId"/> nulo = o aparelho não disse quem (o que já é uma exceção).
/// </summary>
public sealed class OperatorIncident
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid StoreId { get; init; }

    public Guid RegisterId { get; init; }

    public Guid? OperatorId { get; init; }

    public OperatorIncidentKind Kind { get; init; }

    public Guid EventId { get; init; }

    public Guid? SaleId { get; init; }

    public string? SaleNumber { get; init; }

    /// <summary>Quem autorizou (remoção ou redução de linha que não era o último lançamento).</summary>
    public Guid? AuthorizedBy { get; init; }

    public DateTimeOffset OccurredAt { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// Dinheiro que a venda deixou na gaveta da sessão (<c>pos.session_sales</c>): o servidor recalcula o dinheiro esperado da sessão para marcar a
/// sangria acima do calculado (requisito da CX-01: o <c>aboveExpected</c> do aparelho é só indício). Uma linha por venda.
/// </summary>
public sealed class SessionSale
{
    public Guid SaleId { get; init; }

    public Guid SessionId { get; init; }

    /// <summary>PDV-04 CA 7: a venda cancelada na sessão tira o dinheiro dela da gaveta (volta a zero).</summary>
    public decimal CashAmount { get; set; }

    /// <summary>OFF-03 CA 8: a venda foi concluída sem rede (o relatório de fechamento lista as vendas feitas sem rede).</summary>
    public bool Offline { get; init; }

    /// <summary>OFF-03 CA 8: o número da venda no caixa, para a lista das vendas feitas sem rede (nulo nas que subiram antes da OFF-03).</summary>
    public string? SaleNumber { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}
