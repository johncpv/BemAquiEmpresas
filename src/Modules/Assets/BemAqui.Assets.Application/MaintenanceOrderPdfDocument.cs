using BemAqui.Assets.Contracts;
using BemAqui.BuildingBlocks.Pdf;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace BemAqui.Assets.Application;

/// <summary>Item do checklist impresso: o texto e o resultado (vazio = caixa em branco para o executor marcar).</summary>
public sealed record MaintenancePdfCheck(int Sequence, string Text, MaintenanceChecklistResult? Result, string? MarkedBy);

/// <summary>Custo por natureza (peças, mão de obra, prestador, outros), como a aba Custo mostra a quem tem <c>assets.edit</c>.</summary>
public sealed record MaintenancePdfCost(string Label, decimal Amount);

/// <summary>
/// O que o PDF da OS de manutenção mostra (EXP-05 CA 8): número, bem (patrimônio, nome, local, centro), tipo, plano de origem, agendamento,
/// executor (equipe interna ou prestador), o problema, o checklist, o horímetro, as observações e, só com <c>assets.edit</c> (como a tela),
/// o valor combinado e o custo por natureza. <c>Costs</c> nulo = o usuário não vê custo; vazio = a OS ainda não tem custo lançado.
/// </summary>
public sealed record MaintenanceOrderPdfModel(
    string Number, MaintenanceOrderStatus Status, MaintenanceOrderType Type, string AssetTag, string AssetName, string? SiteName, string? WorkCenterName, string? PlanDescription,
    DateOnly ScheduledOn, string Executor, string Problem, bool MachineStopped, string? Meter, string? StartedAt, string? DoneAt, string? WorkDone, string? CancelledAt,
    string? CancelReason, IReadOnlyList<MaintenancePdfCheck> Checklist, decimal? ContractValue, IReadOnlyList<MaintenancePdfCost>? Costs, decimal? CostTotal)
{
    /// <summary>Linhas que o PDF desenha (o teto de 300 da ADR 0045 vale para o checklist).</summary>
    public int Lines => Checklist.Count + (Costs?.Count ?? 0);
}

/// <summary>Regras puras do PDF da OS de manutenção: rótulos, marca e o teto.</summary>
public static class MaintenanceOrderPdf
{
    public const int MaxLines = 300;
    public const string Title = "OS de manutenção";
    public const string InternalTeam = "Equipe interna";

    public static string StatusLabel(MaintenanceOrderStatus status) => status switch
    {
        MaintenanceOrderStatus.Open => "Aberta",
        MaintenanceOrderStatus.InProgress => "Em andamento",
        MaintenanceOrderStatus.Done => "Concluída",
        _ => "Cancelada",
    };

    public static string TypeLabel(MaintenanceOrderType type) => type == MaintenanceOrderType.Preventive ? "Preventiva" : "Corretiva";

    public static string CostLabel(MaintenanceCostType type) => type switch
    {
        MaintenanceCostType.Labor => "Mão de obra interna",
        MaintenanceCostType.Service => "Prestador",
        MaintenanceCostType.Parts => "Peças",
        _ => "Outros",
    };

    /// <summary>Aberta e em andamento sem marca; cancelada com "Cancelado" e o motivo na faixa; concluída sem marca.</summary>
    public static (PdfMark Mark, string? Notice) Mark(MaintenanceOrderStatus status, string? cancelReason) => status == MaintenanceOrderStatus.Cancelled
        ? (PdfMark.Cancelled, cancelReason is { Length: > 0 } reason ? "Motivo: " + reason : null)
        : (PdfMark.None, null);
}

/// <summary>
/// PDF da OS de manutenção no modelo único (EXP-05 CA 8): os dados da OS, o checklist com as caixas (marcadas quando feitas; "N/A" quando não
/// se aplica), o horímetro, as observações, o custo como a tela mostra e as assinaturas "Executado por" e "Conferido por".
/// </summary>
public sealed class MaintenanceOrderPdfDocument : PdfDocumentFrame
{
    private readonly MaintenanceOrderPdfModel _model;

    public MaintenanceOrderPdfDocument(PdfFrame frame, MaintenanceOrderPdfModel model, DateOnly issuedOn)
        : base(frame, Header(model, issuedOn)) => _model = model;

    private static PdfDocumentHeader Header(MaintenanceOrderPdfModel model, DateOnly issuedOn)
    {
        ArgumentNullException.ThrowIfNull(model);
        var (mark, notice) = MaintenanceOrderPdf.Mark(model.Status, model.CancelReason);
        return new PdfDocumentHeader(MaintenanceOrderPdf.Title, model.Number, issuedOn, mark, Notice: notice);
    }

    protected override void ComposeContent(IContainer container) => container.Column(col =>
    {
        var m = _model;
        col.Spacing(8);
        col.Item().Element(c => Fields(c, 3,
            ("Situação", MaintenanceOrderPdf.StatusLabel(m.Status)),
            ("Tipo", MaintenanceOrderPdf.TypeLabel(m.Type)),
            ("Plano de origem", m.PlanDescription ?? (m.Type == MaintenanceOrderType.Preventive ? "Preventiva avulsa" : null)),
            ("Patrimônio", m.AssetTag),
            ("Bem", m.AssetName),
            ("Local", m.SiteName),
            ("Centro de trabalho", m.WorkCenterName),
            ("Agendada para", PdfTheme.Date(m.ScheduledOn)),
            ("Executor", m.Executor),
            ("Máquina parada", m.MachineStopped ? "Sim" : "Não"),
            ("Horímetro", m.Meter),
            ("Iniciada", m.StartedAt)));
        col.Item().Element(c => Section(c, m.Type == MaintenanceOrderType.Corrective ? "Problema" : "O que será feito"));
        col.Item().Text(m.Problem);
        col.Item().Element(c => Section(c, "Checklist"));
        col.Item().Element(Checklist);
        col.Item().Element(c => Section(c, "Observações"));
        if (m.WorkDone is { Length: > 0 } done)
        {
            col.Item().Text(done);
        }
        else
        {
            col.Item().Column(lines =>
            {
                for (var i = 0; i < 3; i++)
                {
                    lines.Item().Height(18).BorderBottom(0.5f).BorderColor(PdfTheme.Line);
                }
            });
        }

        if (m.DoneAt is { } doneAt)
        {
            col.Item().Text("Concluída em " + doneAt).FontColor(PdfTheme.Muted);
        }

        if (m.CancelledAt is { } cancelledAt)
        {
            col.Item().Text($"Cancelada em {cancelledAt}{(m.CancelReason is { Length: > 0 } r ? " · Motivo: " + r : string.Empty)}").FontColor(PdfTheme.Muted);
        }

        if (m.Costs is not null)
        {
            col.Item().Element(c => Section(c, "Custo"));
            col.Item().Element(Costs);
        }

        col.Item().PaddingTop(24).Row(row =>
        {
            row.RelativeItem().Element(e => Signature(e, "Executado por (nome, assinatura e data)"));
            row.ConstantItem(24);
            row.RelativeItem().Element(e => Signature(e, "Conferido por (nome, assinatura e data)"));
        });
    });

    private void Checklist(IContainer c) => c.Column(list =>
    {
        if (_model.Checklist.Count == 0)
        {
            list.Item().Text("Esta OS não tem checklist. Descreva o que foi feito nas observações.").FontColor(PdfTheme.Muted);
            return;
        }

        foreach (var item in _model.Checklist)
        {
            list.Item().PaddingTop(2).Row(check =>
            {
                var mark = item.Result switch { MaintenanceChecklistResult.Done => "X", MaintenanceChecklistResult.NotApplicable => "-", _ => string.Empty };
                check.ConstantItem(9).Height(9).Border(1).BorderColor(PdfTheme.Ink).AlignCenter().AlignMiddle().Text(mark).FontSize(7).Bold();
                check.ConstantItem(5);
                check.RelativeItem().Text(t =>
                {
                    t.Span(item.Text);
                    if (item.Result == MaintenanceChecklistResult.NotApplicable)
                    {
                        t.Span(" (não se aplica)").FontColor(PdfTheme.Muted);
                    }

                    if (item.MarkedBy is { Length: > 0 } by)
                    {
                        t.Span(" · " + by).FontSize(PdfTheme.SmallFontSize).FontColor(PdfTheme.Muted);
                    }
                });
            });
        }
    });

    private void Costs(IContainer c) => c.Table(table =>
    {
        table.ColumnsDefinition(cols =>
        {
            cols.RelativeColumn(3);
            cols.RelativeColumn(1);
        });
        if (_model.ContractValue is { } contract)
        {
            table.Cell().Element(BodyCell).Text("Valor combinado com o prestador");
            table.Cell().Element(BodyCell).AlignRight().Text(PdfTheme.Money(contract));
        }

        if (_model.Costs!.Count == 0)
        {
            table.Cell().ColumnSpan(2).Element(BodyCell).Text("Nenhum custo lançado: o custo entra na conclusão (prestador) e na aba Custo da OS concluída.").FontColor(PdfTheme.Muted);
            return;
        }

        foreach (var cost in _model.Costs)
        {
            table.Cell().Element(BodyCell).Text(cost.Label);
            table.Cell().Element(BodyCell).AlignRight().Text(PdfTheme.Money(cost.Amount));
        }

        table.Cell().Element(BodyCell).Text("Total").SemiBold();
        table.Cell().Element(BodyCell).AlignRight().Text(PdfTheme.Money(_model.CostTotal ?? 0m)).SemiBold();
    });
}
