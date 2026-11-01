using BemAqui.BuildingBlocks.Privacy;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>
/// Onde o titular aparece no Financeiro (LGPD-01a CA 9, ADR 0037 §2): os títulos a receber e a pagar do terceiro PF (número, valor, datas e
/// situação), as despesas recorrentes, os boletos com envio agendado ao e-mail dele, as linhas de extrato em que ele é a contraparte (pelo CPF)
/// e, do funcionário, os títulos de salário e o retrato do favorecido na OP (<c>finance.payment_order_items.payee_snapshot</c>). A forma de
/// pagamento sai mascarada, como no "A pagar".
/// </summary>
[PersonalDataCovers(typeof(Payable), typeof(RecurringExpense), typeof(PaymentOrderItem), typeof(BillingSlip), typeof(BankStatementLine))]
public sealed class FinancePersonalDataProvider(IFinanceDb db) : IPersonalDataProvider
{
    public string Module => "finance";

    public string Title => "Financeiro";

    public async Task<PersonalDataSection?> ExportAsync(PersonalDataExportContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var subject = context.Subject;
        var blocks = new List<PersonalDataBlock>();
        var payableIds = new List<Guid>();
        if (subject.Kind == PersonalDataSubjectKind.Party && subject.PartyId is { } party)
        {
            var receivables = await db.Receivables.AsNoTracking().Where(r => r.PartyId == party).OrderBy(r => r.IssueDate)
                .Select(r => new { r.Id, r.Number, r.Amount, r.IssueDate, r.DueDate, r.Status }).ToListAsync(cancellationToken);
            blocks.Add(new PersonalDataBlock("Contas a receber", "finance.receivables", receivables.Select(r => PersonalDataRecord.Of(
                ("Número", r.Number), ("Valor", PersonalDataMask.Money(r.Amount)), ("Emissão", PersonalDataMask.Date(r.IssueDate)), ("Vencimento", PersonalDataMask.Date(r.DueDate)),
                ("Situação", r.Status.ToString()))).ToList()));
            var receivableIds = receivables.Select(r => r.Id).ToList();
            var slips = await db.BillingSlips.AsNoTracking().Where(b => receivableIds.Contains(b.ReceivableId)).OrderBy(b => b.DueDate)
                .Select(b => new { b.Amount, b.DueDate, b.Status, b.ScheduledEmailTo, b.ScheduledEmailCc }).ToListAsync(cancellationToken);
            blocks.Add(new PersonalDataBlock("Boletos", "finance.billing_slips", slips.Select(b => PersonalDataRecord.Of(
                ("Valor", PersonalDataMask.Money(b.Amount)), ("Vencimento", PersonalDataMask.Date(b.DueDate)), ("Situação", b.Status.ToString()),
                ("Envio agendado para", PersonalDataMask.Recipients(b.ScheduledEmailTo, subject.Emails)), ("Cópia", PersonalDataMask.Recipients(b.ScheduledEmailCc, subject.Emails)))).ToList()));
            var recurring = await db.RecurringExpenses.IgnoreQueryFilters().AsNoTracking().Where(r => r.PartyId == party)
                .Select(r => new { r.Description, r.Amount, r.DueDay, r.PaymentMethod }).ToListAsync(cancellationToken);
            blocks.Add(new PersonalDataBlock("Despesas recorrentes", "finance.recurring_expenses", recurring.Select(r => PersonalDataRecord.Of(
                ("Descrição", r.Description), ("Valor", PersonalDataMask.Money(r.Amount)), ("Dia do vencimento", r.DueDay.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("Forma de pagamento", r.PaymentMethod?.ToString()))).ToList()));
            blocks.Add(await PayablesAsync(p => p.PayeeType == PayeeType.Party && p.PayeeId == party, payableIds, cancellationToken));
        }
        else if (subject.Kind == PersonalDataSubjectKind.Employee && subject.EmployeeId is { } employee)
        {
            blocks.Add(await PayablesAsync(p => p.PayeeType == PayeeType.Employee && p.PayeeId == employee, payableIds, cancellationToken));
        }

        if (payableIds.Count > 0)
        {
            var items = await (from i in db.PaymentOrderItems.AsNoTracking()
                               join o in db.PaymentOrders.AsNoTracking() on i.PaymentOrderId equals o.Id
                               where payableIds.Contains(i.PayableId)
                               orderby o.Number
                               select new { o.Number, i.ApprovedTotal, i.PayeeSnapshot }).ToListAsync(cancellationToken);
            blocks.Add(new PersonalDataBlock("Ordens de pagamento (retrato do favorecido)", "finance.payment_order_items", items.Select(i => PersonalDataRecord.Of(
                ("Ordem de pagamento", i.Number), ("Total aprovado", PersonalDataMask.Money(i.ApprovedTotal)), ("Retrato do favorecido", i.PayeeSnapshot))).ToList()));
        }

        var document = BrazilianDocuments.Normalize(subject.Document);
        if (document.Length == 11)
        {
            var lines = await db.BankStatementLines.AsNoTracking().Where(l => l.CounterpartyDocument == document).OrderBy(l => l.Date)
                .Select(l => new { l.Date, l.Amount, l.CounterpartyName, l.CounterpartyDocument }).ToListAsync(cancellationToken);
            blocks.Add(new PersonalDataBlock("Extrato bancário (como contraparte)", "finance.bank_statement_lines", lines.Select(l => PersonalDataRecord.Of(
                ("Data", PersonalDataMask.Date(l.Date)), ("Valor", PersonalDataMask.Money(l.Amount)), ("Nome", l.CounterpartyName),
                ("CPF", BrazilianDocuments.Format(l.CounterpartyDocument)))).ToList()));
        }

        return blocks.Count == 0 ? null : new PersonalDataSection(Module, Title, blocks);
    }

    private async Task<PersonalDataBlock> PayablesAsync(System.Linq.Expressions.Expression<Func<Payable, bool>> filter, List<Guid> ids, CancellationToken ct)
    {
        var payables = await db.Payables.AsNoTracking().Where(filter).OrderBy(p => p.IssueDate)
            .Select(p => new { p.Id, p.Number, p.Description, p.Amount, p.IssueDate, p.DueDate, p.Status, p.PaymentDetails }).ToListAsync(ct);
        ids.AddRange(payables.Select(p => p.Id));
        return new PersonalDataBlock("Contas a pagar", "finance.payables", payables.Select(p => PersonalDataRecord.Of(
            ("Número", p.Number), ("Descrição", p.Description), ("Valor", PersonalDataMask.Money(p.Amount)), ("Emissão", PersonalDataMask.Date(p.IssueDate)),
            ("Vencimento", PersonalDataMask.Date(p.DueDate)), ("Situação", p.Status.ToString()), ("Forma de pagamento", Method(p.PaymentDetails)))).ToList());
    }

    private static string? Method(string? json) => PaymentDetails.Deserialize(json) is { } d
        ? d.Kind + (d.HolderDocument is { } doc ? " · titular " + PaymentDetails.Mask(doc) : string.Empty)
        : null;
}
