using BemAqui.BuildingBlocks.Pdf;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Time;
using BemAqui.SharedKernel;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace BemAqui.Pos.Application;

/// <summary>
/// Relatório do fechamento de caixa em PDF (CX-02 CA 9, modelo único da ADR 0045): vendas, quantidade, cancelamentos, sangrias, suprimentos,
/// recolhimento, vendas feitas sem rede e o contado por forma. A <b>via do operador</b> não traz esperado, diferença nem o total das vendas (que
/// revelaria o esperado); a <b>via do gerente</b> traz o quadro completo e a quebra.
/// </summary>
public sealed class CashSessionPdfDocument : PdfDocumentFrame
{
    private readonly CashSessionDetail _session;
    private readonly bool _manager;

    public CashSessionPdfDocument(PdfFrame frame, CashSessionDetail session, bool managerCopy)
        : base(frame, new PdfDocumentHeader(
            managerCopy ? "Fechamento de caixa — via do gerente" : "Fechamento de caixa — via do operador",
            $"{session?.StoreName} · Caixa {session?.RegisterNumber:00}",
            session is null ? null : DateOnly.FromDateTime(BusinessTime.ToBusiness(session.OpenedAt).DateTime)))
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _manager = managerCopy;
    }

    protected override void ComposeContent(IContainer container) => container.Column(col =>
    {
        var s = _session;
        col.Spacing(8);
        col.Item().Element(c => Fields(c, 3,
            ("Operador", s.OperatorName),
            ("Situação", s.StatusLabel),
            ("Abertura", PdfTheme.DateTime(s.OpenedAt)),
            ("Contagem", s.CountedAt is { } counted ? PdfTheme.DateTime(counted) : null),
            ("Fechamento", s.ClosedAt is { } closed ? PdfTheme.DateTime(closed) : null),
            ("Fundo de troco", PdfTheme.Money(s.OpeningAmount)),
            ("Vendas", s.Sales.Sales.ToString(PdfTheme.PtBr)),
            ("Cancelamentos", s.Sales.Cancelled.ToString(PdfTheme.PtBr)),
            ("Vendas feitas sem rede", s.Sales.Offline.ToString(PdfTheme.PtBr)),
            ("Recolhimento ao Cofre", s.DepositAmount is { } deposit ? PdfTheme.Money(deposit) : null),
            ("Fundo que fica", s.KeptAmount is { } kept ? PdfTheme.Money(kept) : null),
            ("Reaberturas", s.ReopenCount.ToString(PdfTheme.PtBr))));
        if (_manager)
        {
            col.Item().Element(c => Fields(c, 3,
                ("Total das vendas", s.Sales.Total is { } total ? PdfTheme.Money(total) : null),
                ("Descontos", s.Sales.Discounts is { } discounts ? PdfTheme.Money(discounts) : null),
                ("Canceladas (valor)", s.Sales.CancelledTotal is { } cancelled ? PdfTheme.Money(cancelled) : null)));
        }

        col.Item().Element(c => Section(c, _manager ? "Quebra por forma de pagamento" : "Contado por forma de pagamento"));
        col.Item().Element(Lines);
        col.Item().Element(c => Section(c, "Fundo de troco, sangrias, suprimentos e recolhimento"));
        col.Item().Element(Movements);
        if (_manager && s.ApprovalReason is { Length: > 0 } approval)
        {
            col.Item().Text($"Aprovado por {s.ApprovedByName} em {(s.ApprovedAt is { } at ? PdfTheme.DateTime(at) : "—")}: {approval}");
        }

        if (_manager && s.ReviewReasons.Count > 0)
        {
            col.Item().Text("Foi para conferência: " + string.Join("; ", s.ReviewReasons) + ".");
        }

        col.Item().PaddingTop(24).Row(row =>
        {
            row.RelativeItem().Element(e => Signature(e, "Operador (nome e assinatura)"));
            row.ConstantItem(24);
            row.RelativeItem().Element(e => Signature(e, "Conferido por (nome e assinatura)"));
        });
    });

    private void Lines(IContainer c) => c.Table(table =>
    {
        string[] columns = _manager ? ["Forma", "Esperado", "Contado", "Pix não localizado", "Diferença"] : ["Forma", "Contado"];
        table.ColumnsDefinition(cols =>
        {
            cols.RelativeColumn(4);
            for (var i = 1; i < columns.Length; i++)
            {
                cols.RelativeColumn(2);
            }
        });
        table.Header(h =>
        {
            for (var i = 0; i < columns.Length; i++)
            {
                var cell = HeaderCell(h.Cell());
                (i > 0 ? cell.AlignRight() : cell).Text(columns[i]).SemiBold();
            }
        });
        foreach (var line in _session.Lines)
        {
            BodyCell(table.Cell()).Text(line.MethodName);
            if (_manager)
            {
                BodyCell(table.Cell()).AlignRight().Text(PdfTheme.Money(line.Expected ?? 0m));
                BodyCell(table.Cell()).AlignRight().Text(PdfTheme.Money(line.Counted));
                BodyCell(table.Cell()).AlignRight().Text(PdfTheme.Money(line.PixNotFound ?? 0m));
                var difference = line.Difference ?? 0m;
                var text = BodyCell(table.Cell()).AlignRight().Text(PdfTheme.Money(difference) + (difference > 0 ? " (sobra)" : difference < 0 ? " (falta)" : string.Empty));
                if (line.AboveTolerance == true)
                {
                    text.FontColor(PdfTheme.WarnInk).SemiBold();
                }
            }
            else
            {
                BodyCell(table.Cell()).AlignRight().Text(PdfTheme.Money(line.Counted));
            }
        }
    });

    private void Movements(IContainer c) => c.Table(table =>
    {
        table.ColumnsDefinition(cols =>
        {
            cols.RelativeColumn(3);
            cols.RelativeColumn(2);
            cols.RelativeColumn(5);
            cols.RelativeColumn(3);
            cols.RelativeColumn(2);
        });
        table.Header(h =>
        {
            string[] columns = ["Movimento", "Quando", "Motivo", "Quem", "Valor"];
            for (var i = 0; i < columns.Length; i++)
            {
                var cell = HeaderCell(h.Cell());
                (i == 4 ? cell.AlignRight() : cell).Text(columns[i]).SemiBold();
            }
        });
        foreach (var m in _session.Movements)
        {
            BodyCell(table.Cell()).Text(m.KindLabel + (m.AboveExpected == true ? " (acima do calculado)" : string.Empty));
            BodyCell(table.Cell()).Text(PdfTheme.DateTime(m.OccurredAt));
            BodyCell(table.Cell()).Text(m.Reason ?? "—");
            BodyCell(table.Cell()).Text(m.OperatorName);
            BodyCell(table.Cell()).AlignRight().Text(PdfTheme.Money(m.Amount));
        }
    });
}

/// <summary>Gera o PDF do fechamento (CA 9): a via do gerente exige <c>sales.approve</c>; a exportação fica na auditoria.</summary>
public sealed class CashSessionReportService(CashSessionQueries queries, PdfFrameFactory frames, IPdfRenderer renderer, IAuditWriter audit)
{
    public async Task<Result<(byte[] Content, string FileName)>> PdfAsync(Guid id, bool managerCopy, CancellationToken cancellationToken = default)
    {
        if (managerCopy && !await queries.ExpectedVisibleAsync(cancellationToken))
        {
            return Error.Forbidden("forbidden", "A via do gerente, com o esperado e a quebra, é de quem aprova vendas.");
        }

        var detail = await queries.GetAsync(id, cancellationToken);
        if (detail.IsFailure)
        {
            return detail.Error!;
        }

        var frame = await frames.CreateAsync(cancellationToken);
        var bytes = await renderer.RenderAsync(new CashSessionPdfDocument(frame, detail.Value, managerCopy), cancellationToken);
        await audit.RecordExportAsync("CashSession", id.ToString(), managerCopy ? "pdf-gerente" : "pdf-operador", detail.Value.Lines.Count, cancellationToken);
        var date = BusinessTime.ToBusiness(detail.Value.OpenedAt).ToString("yyyy-MM-dd", PdfTheme.PtBr);
        return (bytes, $"fechamento-caixa-{detail.Value.RegisterNumber:00}-{date}{(managerCopy ? "-gerente" : string.Empty)}.pdf");
    }
}
