using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Webhooks;
using BemAqui.Finance.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>
/// Provedor <c>inter</c> da FND-16 (BNK-02, ADR 0031 e 0032): o Inter não assina o corpo, então a origem é o token de 256 bits na URL
/// (só o hash guardado, por integração: <c>scope_id</c> = id da integração). O modo do token é configuração (<c>Inter:WebhookMode</c>):
/// <c>Query</c> (<c>?t=</c>, padrão) ou <c>PathToken</c> (último segmento), conforme o sandbox aceitar ou não a query (CA 2). Sem id de
/// entrega conhecido, o <c>external_id</c> é a chave composta (cobrança + situação + data do evento) e, junto, o SHA-256 do corpo (CA 6);
/// se o sandbox mostrar um cabeçalho com o id da entrega, <c>Inter:WebhookDeliveryIdHeader</c> o liga, e a chave composta continua como
/// segunda verificação no handler (o boleto já pago termina <c>Ignored</c>).
/// </summary>
public static class InterWebhook
{
    public const string ProviderCode = "inter";
    public const string Name = "Banco Inter";
    public const int MaxItems = 50;

    public static WebhookProvider Provider(UrlTokenMode mode = UrlTokenMode.Query, string? deliveryIdHeader = null) => new()
    {
        Code = ProviderCode,
        Name = Name,
        OwnerModule = "finance",
        Validator = new UrlTokenValidator(mode),
        HandlerType = typeof(InterWebhookHandler),
        Queue = JobQueues.Default,
        DeliveryIdHeader = string.IsNullOrWhiteSpace(deliveryIdHeader) ? null : deliveryIdHeader,
        CompositeKey = CompositeKey,
    };

    public static UrlTokenMode ParseMode(string? value) =>
        Enum.TryParse<UrlTokenMode>(value, ignoreCase: true, out var mode) ? mode : UrlTokenMode.Query;

    /// <summary>Um aviso: os identificadores da cobrança (nada mais é lido do corpo, CA 5) e, só para a chave composta, situação e data.</summary>
    public sealed record Notice(string? RequestId, string? OurReference, string? OurNumber, string? Situation, string? At)
    {
        public bool Identified => RequestId is not null || OurReference is not null || OurNumber is not null;
    }

    /// <summary>
    /// Chave composta (CA 6): <c>cobranca:{código}:{situação}:{data}</c> para um aviso; para um lote, <c>lote:</c> + SHA-256 das chaves em
    /// ordem. Corpo fora do formato: nulo (fica o SHA-256 do corpo).
    /// </summary>
    public static string? CompositeKey(WebhookRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!TryRead(request.Body, out var notices) || notices.Count == 0)
        {
            return null;
        }

        var keys = notices.Select(n => string.Join(':', n.RequestId ?? n.OurReference ?? n.OurNumber, n.Situation ?? "-", n.At ?? "-")).Order(StringComparer.Ordinal).ToList();
        return keys.Count == 1
            ? "cobranca:" + keys[0]
            : "lote:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', keys))))[..40];
    }

    /// <summary>Lê o aviso (objeto ou lista, como o Inter manda), só os campos de texto curtos; qualquer outra coisa é recusada.</summary>
    public static bool TryRead(ReadOnlyMemory<byte> body, out IReadOnlyList<Notice> notices)
    {
        notices = [];
        try
        {
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            var items = root.ValueKind switch
            {
                JsonValueKind.Array => root.EnumerateArray().ToList(),
                JsonValueKind.Object => [root],
                _ => null,
            };
            if (items is null || items.Count == 0 || items.Count > MaxItems || items.Any(i => i.ValueKind != JsonValueKind.Object))
            {
                return false;
            }

            var read = items.Select(i => new Notice(Text(i, "codigoSolicitacao"), Text(i, "seuNumero"), Text(i, "nossoNumero"), Text(i, "situacao"), Text(i, "dataHoraSituacao") ?? Text(i, "dataSituacao")))
                .ToList();
            if (read.Any(n => !n.Identified))
            {
                return false;
            }

            notices = read;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? Text(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value))
        {
            return null;
        }

        var text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
        return text is { Length: > 0 and <= 64 } && text.All(c => c is >= (char)0x20 and <= (char)0x7E) ? text.Trim() : null;
    }
}

/// <summary>
/// Aviso de pagamento do Inter (BNK-02 CA 5 a 10): o corpo só identifica a cobrança. Para cada aviso, acha o boleto (código da solicitação,
/// "seu número" ou "nosso número", na conta da integração que validou o token) e reconsulta a cobrança no Inter pelo mTLS
/// (<see cref="IBankBillingProvider.GetAsync"/>) antes de qualquer efeito; o efeito é o do <see cref="BankPaymentService"/>. Cobrança que o
/// BemAqui não emitiu: <c>Ignored</c> "Cobrança fora do sistema" (fica para a conciliação, BNK-03b). Inter fora do ar, token recusado ou
/// certificado vencido: falha, o Hangfire tenta de novo e nenhum título é alterado (CA 12).
/// </summary>
public sealed class InterWebhookHandler(IFinanceDb db, BankIntegrationService integrations, IBankBillingProvider bank, BankPaymentService payments) : IWebhookHandler
{
    public const string Malformed = "Aviso fora do formato esperado";
    public const string IntegrationOff = "Integração do Inter desligada nesta conta";
    public const string Unknown = "Cobrança fora do sistema";
    public const string Mismatch = "A reconsulta não confere com o boleto";

    public async Task<WebhookHandling> HandleAsync(WebhookEventContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!InterWebhook.TryRead(context.Payload, out var notices))
        {
            return WebhookHandling.Ignored(Malformed);
        }

        var integration = await db.BankIntegrations.AsNoTracking().FirstOrDefaultAsync(i => i.Id == context.ScopeId, cancellationToken);
        if (integration is not { IsActive: true })
        {
            return WebhookHandling.Ignored(IntegrationOff);
        }

        var allowed = await integrations.EffectsAllowedAsync(integration.BankAccountId, cancellationToken);
        if (allowed.IsFailure)
        {
            throw new WebhookHandlerException("Integração do Inter indisponível (" + allowed.Error!.Code + ").");
        }

        var results = new List<WebhookHandling>();
        foreach (var notice in notices)
        {
            results.Add(await HandleAsync(allowed.Value, notice, context.EventId, cancellationToken));
        }

        return results.FirstOrDefault(r => r.Status == WebhookEventStatus.Processed) ?? results[0];
    }

    private async Task<WebhookHandling> HandleAsync(BankIntegration integration, InterWebhook.Notice notice, Guid eventId, CancellationToken ct)
    {
        var account = integration.BankAccountId;
        var slip = await db.BillingSlips.AsNoTracking()
            .Where(s => s.BankAccountId == account && ((notice.RequestId != null && s.ProviderRequestId == notice.RequestId)
                || (notice.OurReference != null && s.OurReference == notice.OurReference) || (notice.OurNumber != null && s.OurNumber == notice.OurNumber)))
            .OrderByDescending(s => s.RequestedAt).Select(s => new { s.Id, s.ProviderRequestId, s.OurReference }).FirstOrDefaultAsync(ct);
        var requestId = slip?.ProviderRequestId ?? notice.RequestId;
        if (requestId is null)
        {
            return WebhookHandling.Ignored(slip is null ? Unknown : Malformed);
        }

        var remote = await bank.GetAsync(integration, requestId, ct);
        if (!remote.Ok)
        {
            if (slip is null && remote.Failure!.HttpStatus == 404)
            {
                return WebhookHandling.Ignored(Unknown);
            }

            throw new WebhookHandlerException("O Inter não respondeu à reconsulta (" + remote.Failure!.Kind + ").");
        }

        if (slip is null)
        {
            return WebhookHandling.Ignored(Unknown);
        }

        if (remote.Value!.OurReference is { } reference && !string.Equals(reference, slip.OurReference, StringComparison.OrdinalIgnoreCase))
        {
            return WebhookHandling.Ignored(Mismatch);
        }

        var result = await payments.ApplyAsync(slip.Id, remote.Value, new BankPaymentOrigin(eventId), ct);
        return result.Outcome switch
        {
            BankPaymentOutcome.Settled or BankPaymentOutcome.PendingReview or BankPaymentOutcome.Cancelled or BankPaymentOutcome.Expired => WebhookHandling.Processed(),
            _ => WebhookHandling.Ignored(result.Reason ?? BankPaymentService.StillOpen),
        };
    }
}
