using System.Text.Json.Serialization;
using BemAqui.Assets.Contracts;

namespace BemAqui.Assets.Application;

/// <summary>
/// "Registrar movimentação" (PAT-02 CA 1): o local, o responsável e o centro de destino (o estado completo: o formulário vem com os atuais e
/// o vazio tira), a data (padrão hoje, até hoje) e o motivo (até 200). <c>Version</c>: o <c>xmin</c> do bem lido.
/// </summary>
public sealed record AssetMovementInput(Guid? SiteId, Guid? ResponsibleEmployeeId, Guid? WorkCenterId, DateOnly? MovedOn, string? Reason, uint? Version = null);

/// <summary>
/// "Transferir selecionados" (CA 4): os bens e o destino; o que não foi escolhido (local, responsável ou centro) fica como está em cada bem.
/// </summary>
public sealed record AssetTransferInput(IReadOnlyList<Guid>? AssetIds, Guid? SiteId, Guid? ResponsibleEmployeeId, Guid? WorkCenterId, DateOnly? MovedOn, string? Reason);

/// <summary>Onde o bem está (ou estava): ids e nomes de local, responsável e centro.</summary>
public sealed record AssetPlaceView(Guid? SiteId, string? SiteName, Guid? ResponsibleEmployeeId, string? ResponsibleName, Guid? WorkCenterId, string? WorkCenterName);

/// <summary>Linha do impacto em lista (CA 4, padrão 7): cada bem, de → para.</summary>
public sealed record AssetTransferLine(Guid AssetId, string Tag, string Name, AssetPlaceView From, AssetPlaceView To);

/// <summary>Bem que impede o lote (CA 4), com o motivo.</summary>
public sealed record AssetTransferRefusal(Guid AssetId, string Tag, string Name, string Reason);

/// <summary>Prévia do lote: o que vai mudar e o que impede (com recusa, o lote não grava nada).</summary>
public sealed record AssetTransferPreview(IReadOnlyList<AssetTransferLine> Items, IReadOnlyList<AssetTransferRefusal> Refused);

/// <summary>Lote gravado: quantos movimentos nasceram, um por bem, na mesma transação.</summary>
public sealed record AssetTransferResult(int Moved, string Message);

/// <summary>
/// "Baixar bem" (CA 6): o motivo, a data (padrão hoje, até hoje), a observação (obrigatória em "Outro") e, na venda, o valor e o comprador
/// opcional (<c>IParties</c>, papel Cliente).
/// </summary>
public sealed record AssetDisposalInput(AssetDisposalReason? DisposalReason, DateOnly? DisposedOn, string? Reason = null, decimal? Value = null, Guid? BuyerPartyId = null, uint? Version = null);

/// <summary>"Reativar bem" (CA 8): o motivo (obrigatório) e a data (padrão hoje).</summary>
public sealed record AssetReactivationInput(string? Reason, DateOnly? ReactivatedOn = null, uint? Version = null);

/// <summary>
/// Título a receber da venda já preenchido (CA 7), só para quem tem <c>finance.create</c>: abre o título manual da FIN-01, que grava só com a
/// confirmação de lá.
/// </summary>
public sealed record AssetReceivableDraft(Guid? PartyId, string? PartyName, decimal Amount, DateOnly IssueDate, string Description);

/// <summary>
/// Movimento na aba Movimentações (CA 3 e 10): data, de → para, motivo, quem registrou e quando. O valor da venda só para quem tem
/// <c>assets.edit</c> (fora do JSON sem a ação).
/// </summary>
public sealed record AssetMovementView(
    Guid Id,
    Guid AssetId,
    AssetMovementKind Kind,
    DateOnly MovedOn,
    AssetPlaceView From,
    AssetPlaceView To,
    string? Reason,
    AssetDisposalReason? DisposalReason,
    Guid? BuyerPartyId,
    string? BuyerName,
    Guid? CreatedBy,
    string? CreatedByName,
    DateTimeOffset CreatedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? DisposalValue);

/// <summary>
/// Resultado de movimentar, baixar ou reativar: o bem atualizado, o movimento e, na venda, o título a receber pré-preenchido (só com
/// <c>finance.create</c>). <c>SaleReverted</c>: a reativação desfez uma baixa por venda ("Cancele o título da venda no Financeiro, se for o caso.").
/// </summary>
public sealed record AssetMovementResult(AssetDetail Asset, AssetMovementView Movement, AssetReceivableDraft? Receivable = null, bool SaleReverted = false);

/// <summary>Comprador no seletor da venda (CA 6, papel Cliente do <c>IParties</c>).</summary>
public sealed record AssetBuyerChoice(Guid Id, string Name, string? Document);
