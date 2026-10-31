using System.Globalization;
using BemAqui.BuildingBlocks.Email;
using BemAqui.Finance.Domain;

namespace BemAqui.Finance.Application;

/// <summary>
/// E-mail <c>collection-reminder</c> (FIN-09 CA 2 e 7): template fixo do sistema que recebe o texto do passo já montado (texto puro, os campos
/// trocados pelos valores) e o escapa no HTML; nenhum texto do usuário é compilado como template. Só o título em questão (nunca a lista de
/// outros títulos do cliente), com a linha digitável e o PDF do boleto emitido quando houver e o Reply-To do financeiro.
/// </summary>
public sealed class CollectionReminderMailer(IEmailSender email)
{
    /// <summary>Valores dos campos da lista fixa (CA 2).</summary>
    public static IReadOnlyDictionary<string, string> Values(string company, string customer, string number, string amount, DateOnly dueDate, string? digitableLine, DateOnly today) =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [CollectionRules.Company] = company,
            [CollectionRules.Customer] = customer,
            [CollectionRules.Number] = number,
            [CollectionRules.Amount] = amount,
            [CollectionRules.DueDate] = dueDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            [CollectionRules.DigitableLine] = digitableLine ?? string.Empty,
            [CollectionRules.DaysOverdue] = CollectionRules.DaysLate(dueDate, today).ToString(CultureInfo.InvariantCulture),
        };

    /// <summary>Modelo do template: só valores simples, todos escapados pelo renderizador no HTML.</summary>
    public static Dictionary<string, object?> Model(string company, string number, DateOnly dueDate, DateOnly today, string message, string? digitableLine, string senderName) =>
        new(StringComparer.Ordinal)
        {
            ["tenant_name"] = company,
            ["headline"] = CollectionRules.Headline(number, dueDate, today),
            ["message"] = message,
            ["digitable_line"] = digitableLine ?? string.Empty,
            ["sender_name"] = senderName,
        };

    /// <summary>Enfileira o lembrete (na transação da régua) e devolve o id do e-mail.</summary>
    public Guid Enqueue(CollectionReminderMail mail)
    {
        ArgumentNullException.ThrowIfNull(mail);
        var receivable = mail.Receivable;
        var due = receivable.DueDate!.Value;
        var line = mail.Slip?.DigitableLine;
        var values = Values(mail.Company, mail.Customer, receivable.Number, ReceivableService.Money(receivable.Balance), due, line, mail.Today);
        var model = Model(mail.Company, receivable.Number, due, mail.Today, CollectionRules.Render(mail.Text, values), line, mail.Company);
        var request = mail.Slip is { PdfAttachmentId: { } pdf } slip
            ? new EmailRequest(EmailDispatchLimiter.CollectionReminderTemplate, mail.To, model, BillingSlipService.AttachmentOwner, slip.Id, [pdf], null, mail.ReplyTo, mail.Company + " via BemAqui")
            : new EmailRequest(EmailDispatchLimiter.CollectionReminderTemplate, mail.To, model, CollectionReminderRunner.RelatedType, receivable.Id, null, null, mail.ReplyTo, mail.Company + " via BemAqui");
        return email.Enqueue(request);
    }
}

/// <summary>O que vai num lembrete: o título, o passo (texto literal), o destinatário do cliente e o boleto emitido, se houver.</summary>
public sealed record CollectionReminderMail(Receivable Receivable, string Text, string To, string Company, string Customer, string? ReplyTo, BillingSlip? Slip, DateOnly Today);
