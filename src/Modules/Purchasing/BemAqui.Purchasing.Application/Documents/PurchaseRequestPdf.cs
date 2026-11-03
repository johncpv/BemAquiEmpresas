using BemAqui.BuildingBlocks.Pdf;
using BemAqui.Purchasing.Contracts;
using BemAqui.Purchasing.Domain;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace BemAqui.Purchasing.Application.Documents;

/// <summary>
/// O que o PDF da solicitação de compra mostra (EXP-05 CA 6): número, origem, destino, OS, quem pediu, necessidade, itens com a situação da
/// linha e os vínculos (cotações e pedidos que atendem as linhas). Sem preço: a SC não tem valor.
/// </summary>
public sealed record PurchaseRequestPdfModel(
    string Number, PurchaseRequestStatus Status, string StatusLabel, string Origin, string Destination, string? OrderNumber, string RequestedBy, DateTimeOffset CreatedAt,
    DateOnly? NearestNeededBy, string? Notes, string? CancelReason, IReadOnlyList<PurchaseRequestPdfLine> Lines, IReadOnlyList<PurchaseRequestPdfLink> Links, DateOnly IssueDate);

public sealed record PurchaseRequestPdfLine(int Sequence, string Code, string Description, decimal Quantity, string Unit, DateOnly NeededBy, string Status, string? OrderNumber);

/// <param name="Kind">"Cotação" ou "Pedido".</param>
public sealed record PurchaseRequestPdfLink(string Kind, string Number, string Status);

/// <summary>Montagem pura do PDF da solicitação (EXP-05 CA 6): rótulos da tela, marca "Cancelado" e limite de linhas.</summary>
public static class PurchaseRequestPdf
{
    public const int MaxLines = 300;

    public static string StatusLabel(PurchaseRequestStatus status) => status switch
    {
        PurchaseRequestStatus.Open => "Aberta",
        PurchaseRequestStatus.Quoting => "Em cotação",
        PurchaseRequestStatus.Ordered => "Pedida",
        _ => "Cancelada",
    };

    public static string OriginLabel(PurchaseRequestOrigin origin) => origin switch
    {
        PurchaseRequestOrigin.BomShortage => "Falta da BOM",
        PurchaseRequestOrigin.MinStock => "Estoque mínimo",
        _ => "Manual",
    };

    public static string RfqStatusLabel(RfqStatus status) => status switch
    {
        RfqStatus.Open => "Aberta",
        RfqStatus.Closed => "Concluída",
        _ => "Cancelada",
    };

    public static string OrderStatusLabel(PurchaseOrderStatus status) => status switch
    {
        PurchaseOrderStatus.Draft => "Rascunho",
        PurchaseOrderStatus.PendingApproval => "Aguardando aprovação",
        PurchaseOrderStatus.Approved => "Aprovado",
        PurchaseOrderStatus.Sent => "Enviado",
        PurchaseOrderStatus.PartiallyReceived => "Recebido em parte",
        PurchaseOrderStatus.Received => "Recebido",
        _ => "Cancelado",
    };

    /// <summary>"Cancelado" com o motivo; as demais situações, sem marca.</summary>
    public static (PdfMark Mark, string? Notice) Mark(PurchaseRequestStatus status, string? reason) => status == PurchaseRequestStatus.Cancelled
        ? (PdfMark.Cancelled, string.IsNullOrWhiteSpace(reason) ? null : "Motivo: " + reason.Trim())
        : (PdfMark.None, null);

    /// <summary>Quem pediu, como a tela: a pessoa na manual; "Sistema · …" nas automáticas.</summary>
    public static string Requester(PurchaseRequestDetail r)
    {
        ArgumentNullException.ThrowIfNull(r);
        return r.Origin switch
        {
            PurchaseRequestOrigin.Manual => r.RequestedByName ?? "—",
            PurchaseRequestOrigin.MinStock => "Sistema · Estoque mínimo",
            _ => $"Sistema · Falta da BOM {r.OrderNumber}".TrimEnd(),
        };
    }

    public static PurchaseRequestPdfModel Build(PurchaseRequestDetail r, IReadOnlyList<PurchaseRequestPdfLink> links, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(r);
        ArgumentNullException.ThrowIfNull(links);
        var destination = r.ProjectNumber is { } p ? (r.ProjectName is { Length: > 0 } n ? $"{p} — {n}" : p) : "Estoque geral";
        var lines = r.Lines.Select(l => new PurchaseRequestPdfLine(l.Sequence, l.ItemCode, l.ItemName, l.Quantity, l.UnitCode, l.NeededBy, StatusLabel(l.Status), l.OrderNumber)).ToList();
        return new PurchaseRequestPdfModel(r.Number, r.Status, StatusLabel(r.Status), OriginLabel(r.Origin), destination, r.OrderNumber, Requester(r), r.CreatedAt, r.NearestNeededBy,
            r.Notes, r.CancelReason, lines, links, today);
    }
}

/// <summary>Solicitação de compra no modelo único (EXP-05 CA 6): dados, itens com a situação de cada linha e os vínculos; cancelada com "Cancelado".</summary>
public sealed class PurchaseRequestPdfDocument : PdfDocumentFrame
{
    private static readonly string[] Columns = ["#", "Código", "Descrição", "Qtd.", "Un.", "Necessidade", "OS", "Situação"];

    private readonly PurchaseRequestPdfModel _model;

    public PurchaseRequestPdfDocument(PdfFrame frame, PurchaseRequestPdfModel model)
        : base(frame, Header(model)) => _model = model;

    private static PdfDocumentHeader Header(PurchaseRequestPdfModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var (mark, notice) = PurchaseRequestPdf.Mark(model.Status, model.CancelReason);
        return new PdfDocumentHeader("Solicitação de compra", model.Number, model.IssueDate, mark, Notice: notice);
    }

    protected override void ComposeContent(IContainer container) => container.Column(col =>
    {
        var m = _model;
        col.Spacing(8);
        col.Item().Element(c => Fields(c, 3,
            ("Situação", m.StatusLabel),
            ("Origem", m.Origin),
            ("Destino", m.Destination),
            ("OS", m.OrderNumber),
            ("Pedida por", $"{m.RequestedBy} · {PdfTheme.DateTime(m.CreatedAt)}"),
            ("Necessidade mais próxima", m.NearestNeededBy is { } d ? PdfTheme.Date(d) : null)));
        if (m.Notes is { Length: > 0 } notes)
        {
            col.Item().Text("Observação: " + notes);
        }

        col.Item().Element(c => Section(c, "Itens"));
        col.Item().Element(Lines);
        col.Item().Element(c => Section(c, "Vínculos"));
        col.Item().Element(Links);
    });

    private void Lines(IContainer c) => c.Table(table =>
    {
        table.ColumnsDefinition(cols =>
        {
            cols.ConstantColumn(20);
            cols.ConstantColumn(64);
            cols.RelativeColumn(4);
            cols.ConstantColumn(52);
            cols.ConstantColumn(26);
            cols.ConstantColumn(62);
            cols.ConstantColumn(70);
            cols.ConstantColumn(64);
        });
        table.Header(h =>
        {
            for (var i = 0; i < Columns.Length; i++)
            {
                var cell = HeaderCell(h.Cell());
                (i == 3 ? cell.AlignRight() : cell).Text(Columns[i]).SemiBold();
            }
        });
        foreach (var l in _model.Lines)
        {
            BodyCell(table.Cell()).Text(l.Sequence.ToString(PdfTheme.PtBr));
            BodyCell(table.Cell()).Text(l.Code);
            BodyCell(table.Cell()).Text(l.Description);
            BodyCell(table.Cell()).AlignRight().Text(PdfTheme.Quantity(l.Quantity));
            BodyCell(table.Cell()).PaddingLeft(3).Text(l.Unit);
            BodyCell(table.Cell()).Text(PdfTheme.Date(l.NeededBy));
            BodyCell(table.Cell()).Text(l.OrderNumber ?? "—");
            BodyCell(table.Cell()).Text(l.Status);
        }
    });

    private void Links(IContainer c)
    {
        if (_model.Links.Count == 0)
        {
            c.Text("Nenhuma cotação ou pedido atende esta solicitação ainda.").FontColor(PdfTheme.Muted);
            return;
        }

        c.Column(col =>
        {
            foreach (var link in _model.Links)
            {
                col.Item().Text(t =>
                {
                    t.Span(link.Kind + " ").FontColor(PdfTheme.Muted);
                    t.Span(link.Number).SemiBold();
                    t.Span(" · " + link.Status);
                });
            }
        });
    }
}
