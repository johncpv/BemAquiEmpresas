using System.Globalization;
using BemAqui.BuildingBlocks.Email;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Domain;
using BemAqui.Identity.Contracts;

namespace BemAqui.Finance.Application;

/// <summary>
/// E-mail <c>billing-slip-issued</c> (BNK-01b CA 13): pela fila, com o PDF do banco anexado (o <c>SendEmailJob</c> só anexa depois do
/// <c>Clean</c>), corpo com só empresa, número do título, valor, vencimento e linha digitável (nenhum outro título do cliente),
/// Reply-To de quem pediu. O limite diário de Configurações → Cobrança soma orçamentos, pedidos e boletos; acima dele, o envio fica
/// para o dia seguinte às 08:00 (São Paulo). Chame dentro da transação do comando (a trava do limite vale até o commit).
/// </summary>
public sealed class BillingSlipMailer(IEmailSender email, EmailDispatchLimiter limiter, BillingSettingsService settings, ICompanyIdentity company, IUserDirectory users, IBillingSlipJobs jobs, TimeProvider clock)
{
    public const string LimitMessage = "Limite diário de e-mails atingido. O boleto será enviado amanhã às 08:00.";

    /// <summary>Enfileira o e-mail (true) ou agenda para amanhã às 08:00 (false).</summary>
    public async Task<bool> SendOrScheduleAsync(BillingSlip slip, Receivable receivable, string to, IReadOnlyList<string> cc, Guid? requestedBy, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(slip);
        ArgumentNullException.ThrowIfNull(receivable);
        var limit = (await settings.DefaultsAsync(cancellationToken)).DailyEmailLimit;
        if (!await limiter.BillingWithinLimitAsync(limit, cancellationToken))
        {
            var at = NextMorning(clock);
            slip.ScheduleEmail(at, to, cc);
            await jobs.ScheduleEmailAsync(slip.Id, at, cancellationToken);
            return false;
        }

        await EnqueueAsync(slip, receivable, to, cc, requestedBy, cancellationToken);
        return true;
    }

    /// <summary>Envio sem a conferência do limite (o adiado, já no dia seguinte).</summary>
    public async Task EnqueueAsync(BillingSlip slip, Receivable receivable, string to, IReadOnlyList<string> cc, Guid? requestedBy, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(slip);
        ArgumentNullException.ThrowIfNull(receivable);
        var info = await company.GetAsync(cancellationToken);
        var name = EmailAddresses.OneLine(info.TradeName ?? info.LegalName);
        var sender = requestedBy is { } u ? await users.DisplayNameAsync(u, cancellationToken) : null;
        var replyTo = requestedBy is { } r ? await users.EmailOfAsync(r, cancellationToken) : null;
        var model = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["tenant_name"] = name,
            ["receivable_number"] = receivable.Number,
            ["amount"] = ReceivableService.Money(slip.Amount),
            ["due_date"] = slip.DueDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            ["digitable_line"] = slip.DigitableLine ?? string.Empty,
            ["sender_name"] = EmailAddresses.OneLine(sender ?? name),
        };
        email.Enqueue(new EmailRequest(EmailDispatchLimiter.BillingSlipTemplate, to, model, BillingSlipService.AttachmentOwner, slip.Id, [slip.PdfAttachmentId!.Value], cc,
            replyTo, $"{name} via BemAqui", requestedBy));
        slip.EmailSent(clock.GetUtcNow());
    }

    /// <summary>Amanhã às 08:00 de São Paulo, em UTC.</summary>
    public static DateTimeOffset NextMorning(TimeProvider clock) => BusinessTime.StartOfDayUtc(BusinessTime.Today(clock).AddDays(1)).AddHours(8);
}
