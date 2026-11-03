using BemAqui.BuildingBlocks.Export;
using BemAqui.Purchasing.Contracts;
using BemAqui.Purchasing.Domain;

namespace BemAqui.Purchasing.Application.Exports;

/// <summary>Linha da planilha de solicitações (EXP-04): as colunas da tabela "Listas" da story.</summary>
public sealed record PurchaseRequestExportRow(string Number, string Origin, string Destination, string? Order, int Items, DateOnly? NearestNeededBy, string Status, DateOnly CreatedOn);

public sealed record RfqExportRow(string Number, int Items, int Suppliers, int Responded, DateOnly CreatedOn, string Status);

/// <param name="Total">Valor do pedido; a última linha ("Total") traz a soma e nada mais.</param>
public sealed record PurchaseOrderExportRow(string Number, string? Supplier, string? Destination, decimal Total, DateOnly? ExpectedDate, string? Status, DateOnly? ApprovedOn, DateOnly? SentOn);

public sealed record ToReceiveExportRow(string Order, string Supplier, DateOnly ExpectedDate, decimal OpenAmount, string Status);

public sealed record ReceivedExportRow(string Number, string Order, string Supplier, DateOnly ReceivedOn, int Items, decimal Total, string? ReceivedBy, bool Reversed);

/// <summary>
/// Definições das planilhas das listas de compras (EXP-04 CA 15, ADR 0045 §3): mesmas colunas e rótulos da tela, só xlsx. Nenhuma coluna
/// sensível: o valor do pedido e do recebimento já aparece para quem tem <c>purchasing.view</c>.
/// </summary>
public static class PurchasingExports
{
    public const string TotalLabel = "Total";

    public static readonly IReadOnlyDictionary<PurchaseRequestOrigin, string> RequestOrigins = new Dictionary<PurchaseRequestOrigin, string>
    {
        [PurchaseRequestOrigin.Manual] = "Manual",
        [PurchaseRequestOrigin.BomShortage] = "Falta da BOM",
        [PurchaseRequestOrigin.MinStock] = "Estoque mínimo",
    };

    public static readonly IReadOnlyDictionary<PurchaseRequestStatus, string> RequestStatuses = new Dictionary<PurchaseRequestStatus, string>
    {
        [PurchaseRequestStatus.Open] = "Aberta",
        [PurchaseRequestStatus.Quoting] = "Em cotação",
        [PurchaseRequestStatus.Ordered] = "Pedida",
        [PurchaseRequestStatus.Cancelled] = "Cancelada",
    };

    public static readonly IReadOnlyDictionary<RfqStatus, string> RfqStatuses = new Dictionary<RfqStatus, string>
    {
        [RfqStatus.Open] = "Aberta",
        [RfqStatus.Closed] = "Concluída",
        [RfqStatus.Cancelled] = "Cancelada",
    };

    public static readonly IReadOnlyDictionary<PurchaseOrderStatus, string> OrderStatuses = new Dictionary<PurchaseOrderStatus, string>
    {
        [PurchaseOrderStatus.Draft] = "Rascunho",
        [PurchaseOrderStatus.PendingApproval] = "Aguardando aprovação",
        [PurchaseOrderStatus.Approved] = "Aprovado",
        [PurchaseOrderStatus.Sent] = "Enviado",
        [PurchaseOrderStatus.PartiallyReceived] = "Parcialmente recebido",
        [PurchaseOrderStatus.Received] = "Recebido",
        [PurchaseOrderStatus.Cancelled] = "Cancelado",
    };

    public static readonly IReadOnlyDictionary<GoodsReceiptStatus, string> ReceiptStatuses = new Dictionary<GoodsReceiptStatus, string>
    {
        [GoodsReceiptStatus.Completed] = "Registrado",
        [GoodsReceiptStatus.Reversed] = "Estornado",
    };

    /// <summary>Os rótulos do "Mostrar" das três listas, como na tela.</summary>
    public static readonly IReadOnlyDictionary<string, string> Shows = new Dictionary<string, string>
    {
        ["aguardando-voce"] = "Aguardando você",
        ["abertas"] = "Abertas",
        ["todas"] = "Todas",
        ["abertos"] = "Abertos",
        ["todos"] = "Todos",
        ["a-receber"] = "A receber",
    };

    public static readonly ExportSpec<PurchaseRequestExportRow> Requests = new ExportSpec<PurchaseRequestExportRow>("PurchaseRequest", "solicitacoes-de-compra", "Solicitações de compra")
        .Text("Número", r => r.Number)
        .Text("Origem", r => r.Origin)
        .Text("Destino", r => r.Destination)
        .Text("OS", r => r.Order)
        .Quantity("Itens", r => r.Items)
        .Date("Necessidade mais próxima", r => r.NearestNeededBy)
        .Text("Situação", r => r.Status)
        .Date("Criada em", r => r.CreatedOn);

    public static readonly ExportSpec<RfqExportRow> Rfqs = new ExportSpec<RfqExportRow>("Rfq", "cotacoes", "Cotações")
        .Text("Número", r => r.Number)
        .Quantity("Itens", r => r.Items)
        .Quantity("Fornecedores", r => r.Suppliers)
        .Quantity("Responderam", r => r.Responded)
        .Date("Criada em", r => r.CreatedOn)
        .Text("Situação", r => r.Status);

    public static readonly ExportSpec<PurchaseOrderExportRow> Orders = new ExportSpec<PurchaseOrderExportRow>("PurchaseOrder", "pedidos-de-compra", "Pedidos de compra")
        .Text("Número", r => r.Number)
        .Text("Fornecedor", r => r.Supplier)
        .Text("Destino", r => r.Destination)
        .Money("Total", r => r.Total)
        .Date("Previsão de entrega", r => r.ExpectedDate)
        .Text("Situação", r => r.Status)
        .Date("Aprovado em", r => r.ApprovedOn)
        .Date("Enviado em", r => r.SentOn)
        .Style(r => r.Number == TotalLabel ? new ExportRowStyle(Bold: true) : null);

    public static readonly ExportSpec<ToReceiveExportRow> ToReceive = new ExportSpec<ToReceiveExportRow>("PurchaseOrder", "pedidos-a-receber", "Pedidos a receber")
        .Text("Pedido", r => r.Order)
        .Text("Fornecedor", r => r.Supplier)
        .Date("Previsão", r => r.ExpectedDate)
        .Money("Saldo a receber", r => r.OpenAmount)
        .Text("Situação", r => r.Status);

    public static readonly ExportSpec<ReceivedExportRow> Received = new ExportSpec<ReceivedExportRow>("GoodsReceipt", "recebimentos", "Recebimentos")
        .Text("Número", r => r.Number)
        .Text("Pedido", r => r.Order)
        .Text("Fornecedor", r => r.Supplier)
        .Date("Data", r => r.ReceivedOn)
        .Quantity("Itens", r => r.Items)
        .Money("Total", r => r.Total)
        .Text("Recebido por", r => r.ReceivedBy)
        .Text("Estornado", r => r.Reversed ? "Sim" : "Não");

    /// <summary>Destino do pedido e da SC como a tela: o projeto ("PRJ-… — Nome") ou "Estoque geral".</summary>
    public static string Destination(string? projectNumber, string? projectName) =>
        string.IsNullOrEmpty(projectNumber) ? "Estoque geral" : string.IsNullOrWhiteSpace(projectName) ? projectNumber : $"{projectNumber} — {projectName}";

    /// <summary>Saldo a receber do pedido: o que falta de cada linha pelo preço dela, em centavos.</summary>
    public static decimal OpenAmount(IEnumerable<(decimal Quantity, decimal Received, decimal UnitPrice)> lines) =>
        Math.Round(lines.Sum(l => Math.Max(0m, l.Quantity - l.Received) * l.UnitPrice), 2, MidpointRounding.AwayFromZero);
}
