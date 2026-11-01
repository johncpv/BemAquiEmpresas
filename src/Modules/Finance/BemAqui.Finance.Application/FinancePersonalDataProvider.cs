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

    /// <summary>Data do último documento com movimento do titular no Financeiro (CA 3): títulos e linhas de extrato.</summary>
    public async Task<IReadOnlyDictionary<string, DateOnly>> RetentionAnchorsAsync(PersonalDataSubject subject, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        var dates = new List<DateOnly>();
        if (subject.Kind == PersonalDataSubjectKind.Party && subject.PartyId is { } party)
        {
            dates.AddRange(await db.Receivables.Where(r => r.PartyId == party).Select(r => r.IssueDate).ToListAsync(cancellationToken));
            dates.AddRange(await db.Payables.Where(p => p.PayeeType == PayeeType.Party && p.PayeeId == party).Select(p => p.IssueDate).ToListAsync(cancellationToken));
        }
        else if (subject.Kind == PersonalDataSubjectKind.Employee && subject.EmployeeId is { } employee)
        {
            dates.AddRange(await db.Payables.Where(p => p.PayeeType == PayeeType.Employee && p.PayeeId == employee).Select(p => p.IssueDate).ToListAsync(cancellationToken));
        }

        var document = BrazilianDocuments.Normalize(subject.Document);
        if (document.Length == 11)
        {
            dates.AddRange(await db.BankStatementLines.Where(l => l.CounterpartyDocument == document).Select(l => l.Date).ToListAsync(cancellationToken));
        }

        return dates.Count == 0 ? new Dictionary<string, DateOnly>() : new Dictionary<string, DateOnly> { [RetentionAnchors.LastDocument] = dates.Max() };
    }

    public Task<PersonalDataAnonymizationPlan?> PlanAnonymizationAsync(PersonalDataAnonymizationContext context, CancellationToken cancellationToken = default) =>
        RunAsync(context, false, cancellationToken);

    public Task<PersonalDataAnonymizationPlan?> AnonymizeAsync(PersonalDataAnonymizationContext context, CancellationToken cancellationToken = default) =>
        RunAsync(context, true, cancellationToken);

    /// <summary>
    /// LGPD-01b CA 2, 4 e 9: títulos a receber e a pagar ficam (valores, datas, ids e situação; o nome não mora aqui, vem do cadastro). A forma
    /// de pagamento (chave Pix, conta, titular) sai dos títulos pagos ou cancelados e das despesas recorrentes encerradas, e fica só o tipo;
    /// com título em aberto, fica retida até o pagamento. O envio de boleto agendado ao e-mail do titular é desfeito. A contraparte do extrato
    /// (nome e CPF) segue o nome nas notas e títulos: fim do exercício da linha + 5 anos. O retrato do favorecido nas OPs troca pelo
    /// <c>SubjectAnonymized</c> (<see cref="SubjectAnonymizedConsumer"/>).
    /// </summary>
    private async Task<PersonalDataAnonymizationPlan?> RunAsync(PersonalDataAnonymizationContext context, bool apply, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        var subject = context.Subject;
        var plan = new PersonalDataPlanBuilder(Module, Title);
        var payables = new List<Payable>();
        if (subject.Kind == PersonalDataSubjectKind.Party && subject.PartyId is { } party)
        {
            var receivables = await db.Receivables.Where(r => r.PartyId == party).Select(r => r.Id).ToListAsync(ct);
            plan.Keep("Títulos a receber: mantidos, com valores, datas e situação", "finance.receivables", receivables.Count);
            var slips = await db.BillingSlips.Where(b => receivables.Contains(b.ReceivableId) && (b.ScheduledEmailTo != null || b.ScheduledEmailCc != null)).ToListAsync(ct);
            slips = slips.Where(b => PersonalDataMarker.HasAny(b.ScheduledEmailTo, subject.Emails) || PersonalDataMarker.HasAny(b.ScheduledEmailCc, subject.Emails)).ToList();
            var contact = context.Decide(RetentionGroups.Contact);
            if (apply && !contact.Retained)
            {
                slips.ForEach(b => b.CancelScheduledEmail());
            }

            plan.Fields(contact, "Envio de boleto agendado ao e-mail do titular", "finance.billing_slips", slips.Count);
            payables = await db.Payables.Where(p => p.PayeeType == PayeeType.Party && p.PayeeId == party).ToListAsync(ct);
            var recurring = await db.RecurringExpenses.IgnoreQueryFilters().Where(r => r.PartyId == party && r.PaymentDetails != null).ToListAsync(ct);
            var activeRecurring = recurring.Where(r => r.IsActive && !r.IsDeleted).ToList();
            var bank = activeRecurring.Count > 0 ? new RetentionDecision(true, null, RetentionGroups.ReasonOf(RetentionBase.LastPayment)) : RetentionDecision.Free;
            if (apply && !bank.Retained)
            {
                recurring.ForEach(r => r.PaymentDetails = OnlyKind(r.PaymentDetails));
            }

            plan.Fields(bank, "Forma de pagamento das despesas recorrentes", "finance.recurring_expenses", recurring.Count);
        }
        else if (subject.Kind == PersonalDataSubjectKind.Employee && subject.EmployeeId is { } employee)
        {
            payables = await db.Payables.Where(p => p.PayeeType == PayeeType.Employee && p.PayeeId == employee).ToListAsync(ct);
        }

        plan.Keep("Títulos a pagar: mantidos, com valores, datas e situação", "finance.payables", payables.Count);
        var withDetails = payables.Where(p => p.PaymentDetails != null && !IsKindOnly(p.PaymentDetails)).ToList();
        var closed = withDetails.Where(p => p.Status is PayableStatus.Paid or PayableStatus.Cancelled).ToList();
        var open = withDetails.Count - closed.Count;
        if (apply)
        {
            closed.ForEach(p => p.PaymentDetails = OnlyKind(p.PaymentDetails));
        }

        plan.Anonymize("Forma de pagamento dos títulos pagos ou cancelados (fica só o tipo)", "finance.payables", closed.Count);
        plan.Fields(new RetentionDecision(true, null, RetentionGroups.ReasonOf(RetentionBase.LastPayment)), "Forma de pagamento dos títulos em aberto", "finance.payables", open);

        var payableIds = payables.Select(p => p.Id).ToList();
        var snapshots = payableIds.Count == 0 ? 0 : await db.PaymentOrderItems.CountAsync(i => payableIds.Contains(i.PayableId), ct);
        plan.Anonymize("Retrato do favorecido nas ordens de pagamento (forma de pagamento; o nome segue o cadastro)", "finance.payment_order_items", snapshots);

        var document = BrazilianDocuments.Normalize(subject.Document);
        if (document.Length == 11)
        {
            var lines = await db.BankStatementLines.Where(l => l.CounterpartyDocument == document).ToListAsync(ct);
            foreach (var group in lines.GroupBy(l => context.Decide(RetentionGroups.Identification, l.Date)))
            {
                if (apply && !group.Key.Retained)
                {
                    foreach (var line in group)
                    {
                        db.Entry(line).Property(l => l.CounterpartyName).CurrentValue = context.Marker;
                        db.Entry(line).Property(l => l.CounterpartyDocument).CurrentValue = null;
                    }
                }

                plan.Fields(group.Key, "Nome e CPF da contraparte no extrato bancário", "finance.bank_statement_lines", group.Count());
            }
        }

        if (apply)
        {
            await db.SaveChangesAsync(ct);
        }

        var built = plan.Build();
        return built.Items.Count == 0 ? null : built;
    }

    /// <summary>Forma de pagamento sem dado pessoal: só o tipo (Pix, boleto, transferência).</summary>
    internal static string? OnlyKind(string? json) => PaymentDetails.Deserialize(json) is { } d ? PaymentDetails.Serialize(new PaymentDetailsInput(d.Kind)) : null;

    private static bool IsKindOnly(string? json) => PaymentDetails.Deserialize(json) is { } d && d == new PaymentDetailsInput(d.Kind);

    private static string? Method(string? json) => PaymentDetails.Deserialize(json) is { } d
        ? d.Kind + (d.HolderDocument is { } doc ? " · titular " + PaymentDetails.Mask(doc) : string.Empty)
        : null;
}
