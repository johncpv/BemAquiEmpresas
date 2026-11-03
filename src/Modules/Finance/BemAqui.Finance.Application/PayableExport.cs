using BemAqui.BuildingBlocks.Export;
using BemAqui.Finance.Contracts;

namespace BemAqui.Finance.Application;

/// <summary>
/// Linha exportada de Contas a pagar (EXP-01, ADR 0045): o título como na tela, ou a folha agrupada (RH-02c CA 7) sem nome nem valor por
/// funcionário, só o total e a quantidade.
/// </summary>
public sealed record PayableExportRow(
    string Number, string Payee, string Description, string? Account, string? Project, string? Method, DateOnly? IssueDate, DateOnly? DueDate,
    decimal? Amount, decimal? Retentions, decimal? NetAmount, decimal? PaidAmount, decimal Balance, string Situation)
{
    public static PayableExportRow FromDetail(PayableDetail p)
    {
        ArgumentNullException.ThrowIfNull(p);
        return new PayableExportRow(p.Number, p.PartyName, p.IsEstimate ? p.Description + " (estimado)" : p.Description, p.AccountName, p.ProjectNumber,
            p.PaymentMethod is { } m ? PayableExport.MethodLabel(m) : null, p.IssueDate, p.DueDate, p.Amount, p.RetentionIss + p.RetentionInss + p.RetentionIr + p.RetentionPcc,
            p.NetAmount, p.PaidAmount, p.Balance, PayableExport.Situation(p));
    }

    public static PayableExportRow FromGroup(PayablePayrollGroup g)
    {
        ArgumentNullException.ThrowIfNull(g);
        var count = g.Count == 1 ? "1 salário" : $"{g.Count} salários";
        return new PayableExportRow("—", g.Label, g.IsEstimate ? count + " (estimado)" : count, null, null, PayableExport.MethodLabel(PaymentMethodKind.EmployeeAccount), null, g.DueDate,
            null, null, null, null, g.Total, "Folha agrupada");
    }
}

/// <summary>Colunas e rótulos da exportação de Contas a pagar (mesmos textos da tela, <c>payables.ts</c>).</summary>
public static class PayableExport
{
    public static readonly ExportSpec<PayableExportRow> Spec = new ExportSpec<PayableExportRow>("Payable", "contas-a-pagar", "Contas a pagar")
        .Text("Número", r => r.Number)
        .Text("Fornecedor", r => r.Payee)
        .Text("Descrição", r => r.Description)
        .Text("Conta", r => r.Account).NotInPdf()
        .Text("Projeto", r => r.Project).NotInPdf()
        .Text("Forma", r => r.Method)
        .Date("Emissão", r => r.IssueDate).NotInPdf()
        .Date("Vencimento", r => r.DueDate)
        .Money("Valor", r => r.Amount).NotInPdf()
        .Money("Retenções", r => r.Retentions).NotInPdf()
        .Money("Líquido", r => r.NetAmount)
        .Money("Pago", r => r.PaidAmount).NotInPdf()
        .Money("Saldo", r => r.Balance)
        .Text("Situação", r => r.Situation);

    /// <summary>Filtro "situacao" da tela → rótulo.</summary>
    public static readonly IReadOnlyDictionary<string, string> Situations = new Dictionary<string, string>
    {
        ["aberto"] = "Em aberto",
        ["vencido"] = "Vencidos",
        ["previsto"] = "Previstos",
        ["em-op"] = "Em ordem de pagamento",
        ["pago"] = "Pagos",
        ["cancelado"] = "Cancelados",
    };

    public static string MethodLabel(PaymentMethodKind kind) => kind switch
    {
        PaymentMethodKind.Pix => "Pix",
        PaymentMethodKind.Boleto => "Boleto",
        PaymentMethodKind.Ted => "TED",
        PaymentMethodKind.EmployeeAccount => "Conta do funcionário",
        _ => "Outro",
    };

    /// <summary>Situação exibida, igual ao chip da tela (<c>payableSituation</c>).</summary>
    public static string Situation(PayableDetail p)
    {
        ArgumentNullException.ThrowIfNull(p);
        return p.Status switch
        {
            PayableStatus.Cancelled => "Cancelado",
            PayableStatus.Paid => "Pago",
            _ when p.IsOverdue => "Vencido",
            _ when p.IsDueToday => "Vence hoje",
            PayableStatus.Planned => "Previsto",
            PayableStatus.AwaitingApproval => "Em OP",
            PayableStatus.Approved => "Aprovado",
            PayableStatus.PartiallyPaid => "Parcial",
            _ => "Em aberto",
        };
    }
}
