using MailAddress = System.Net.Mail.MailAddress;
using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Email;
using BemAqui.Commercial.Domain;
using BemAqui.SharedKernel;

namespace BemAqui.Commercial.Application.Documents;

public sealed partial class QuoteDocumentService
{
    private static readonly char[] Forbidden = [',', ';', '<', '>', '"', ' ', (char)13, (char)10];

    private async Task EnqueueAsync(Quote quote, Attachment pdf, string to, List<string> cc, string message, CancellationToken ct)
    {
        var info = await company.GetAsync(ct);
        var name = OneLine(info.TradeName ?? info.LegalName);
        var me = currentUser.UserId;
        var sender = me is { } u ? await users.DisplayNameAsync(u, ct) : null;
        var replyTo = me is { } r ? await users.EmailOfAsync(r, ct) : null;
        var model = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["tenant_name"] = name, ["quote_number"] = quote.Number, ["revision"] = quote.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["message"] = message, ["valid_until"] = quote.ValidUntil?.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture) ?? "a combinar",
            ["sender_name"] = sender ?? name,
        };
        var logId = email.Enqueue(new EmailRequest("quote-sent", to, model, OwnerType, quote.Id, [pdf.Id], cc, replyTo, $"{name} via BemAqui"));
        db.QuoteDispatches.Add(new QuoteDispatch
        {
            QuoteId = quote.Id, EmailLogId = logId, AttachmentId = pdf.Id, To = to, Cc = cc.Count > 0 ? string.Join(",", cc) : null, SentBy = me, CreatedAt = clock.GetUtcNow(),
        });
    }

    /// <summary>Endereço puro (CA 9): sem nome de exibição, separadores nem quebra de linha; formato válido; até 254 caracteres.</summary>
    private static Error? AddressError(string field, string? address)
    {
        var value = address?.Trim() ?? string.Empty;
        var ok = value.Length is > 0 and <= 254 && value.IndexOfAny(Forbidden) < 0
            && MailAddress.TryCreate(value, out var parsed) && parsed.Address == value && string.IsNullOrEmpty(parsed.DisplayName);
        return ok ? null : QuoteService.Invalid(field, "Informe um e-mail válido, só o endereço.");
    }

    /// <summary>Sem quebra de linha (CA 10): o nome vai no assunto e no remetente.</summary>
    private static string OneLine(string text) => string.Join(' ', text.Split([(char)13, (char)10], StringSplitOptions.RemoveEmptyEntries)).Trim();
}
