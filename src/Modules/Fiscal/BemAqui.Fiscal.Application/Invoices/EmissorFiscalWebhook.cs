using System.Text.Json;
using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Webhooks;
using BemAqui.Fiscal.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Fiscal.Application.Invoices;

/// <summary>
/// Provedor <c>emissor-fiscal</c> da FND-16 (FIS-01 CA 11, ADR 0031): HMAC-SHA256 com timestamp no formato do Emissor
/// (<c>X-BemAqui-Signature: t=…,v1=…</c>, janela de 5 min), id da entrega em <c>X-BemAqui-Delivery-Id</c>, tipo em <c>X-BemAqui-Event</c>,
/// fila <c>fiscal</c>. O Emissor entrega só em HTTPS pública: o aviso chega pelo stack-nginx, no host da plataforma (ADR 0027, adendo de
/// 27/10/2026; ADR 0031 §1), como qualquer outro provedor. Responde 2xx só depois de gravar o evento e a mensagem do outbox; a entrega
/// repetida (mesmo <c>X-BemAqui-Delivery-Id</c>, ou mesmo corpo) responde 200 sem gravar de novo.
/// </summary>
public static class EmissorFiscalWebhook
{
    public const string ProviderCode = "emissor-fiscal";

    public static readonly string[] NfeEvents = ["nfe.autorizada", "nfe.rejeitada", "nfe.cancelada", "nfe.denegada"];

    public static WebhookProvider Provider { get; } = new()
    {
        Code = ProviderCode,
        Name = "Emissor Fiscal",
        OwnerModule = FiscalModuleInfo.Code,
        Validator = new TimestampedSignatureValidator("X-BemAqui-Signature", "X-BemAqui-Timestamp"),
        HandlerType = typeof(EmissorFiscalWebhookHandler),
        Queue = JobQueues.Fiscal,
        DeliveryIdHeader = "X-BemAqui-Delivery-Id",
        EventTypeHeader = "X-BemAqui-Event",
    };
}

/// <summary>
/// Aviso do Emissor (CA 11): o corpo só dispara a reconsulta. Confere o tipo, o tenant do Emissor (o da integração), acha a nota pelo
/// id do documento ou pela chave, reconsulta <c>GET /api/v1/nfe/{chave}</c> e aplica a transição dela. Nota desconhecida, outro tenant
/// ou NFS-e (FIS-06): <c>Ignored</c> com o motivo. Emissor fora do ar na reconsulta: falha e o Hangfire tenta de novo.
/// </summary>
public sealed class EmissorFiscalWebhookHandler(IFiscalDb db, FiscalEmitterAccess access, OutgoingInvoiceReconciler reconciler) : IWebhookHandler
{
    public const string Malformed = "Aviso fora do formato esperado";
    public const string OtherTenant = "Aviso de outro tenant do Emissor Fiscal";
    public const string NotTested = "Integração fiscal ainda não testada";
    public const string Unknown = "Nota desconhecida";
    public const string NotHandled = "Evento sem efeito nesta versão";
    public const string Mismatch = "A reconsulta não confere com o aviso";

    public async Task<WebhookHandling> HandleAsync(WebhookEventContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!TryRead(context.Payload, out var payload) || !string.Equals(payload.Type, context.EventType, StringComparison.Ordinal))
        {
            return WebhookHandling.Ignored(Malformed);
        }

        if (!EmissorFiscalWebhook.NfeEvents.Contains(payload.Type, StringComparer.Ordinal))
        {
            return WebhookHandling.Ignored(NotHandled);
        }

        var settings = await db.FiscalIntegrationSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (settings?.EmitterTenantId is not { } emitterTenant)
        {
            return WebhookHandling.Ignored(NotTested);
        }

        if (payload.TenantId != emitterTenant)
        {
            return WebhookHandling.Ignored(OtherTenant);
        }

        var documentId = payload.DocumentId;
        var key = payload.AccessKey;
        var invoice = await db.OutgoingInvoices.FirstOrDefaultAsync(i => i.EmitterDocumentId == documentId || (key != null && i.AccessKey == key), cancellationToken);
        if (invoice is null)
        {
            return WebhookHandling.Ignored(Unknown);
        }

        var accessKey = invoice.AccessKey ?? key;
        if (accessKey is null)
        {
            return WebhookHandling.Ignored(Malformed);
        }

        var emitter = await access.CurrentAsync(requireActive: true, cancellationToken);
        if (emitter.IsFailure)
        {
            throw new WebhookHandlerException("Integração fiscal indisponível (" + emitter.Error!.Code + ").");
        }

        var remote = await emitter.Value.GetNfeAsync(accessKey, cancellationToken);
        if (remote.IsFailure)
        {
            if (remote.Error!.Code == FiscalErrorCodes.NotFound)
            {
                return WebhookHandling.Ignored(Unknown);
            }

            throw new WebhookHandlerException("O Emissor Fiscal não respondeu à reconsulta (" + remote.Error.Code + ").");
        }

        if (remote.Value.DocumentId != documentId)
        {
            return WebhookHandling.Ignored(Mismatch);
        }

        await reconciler.ApplyAsync(invoice, remote.Value, emitter.Value, cancellationToken);
        return WebhookHandling.Processed();
    }

    private sealed record Payload(string Type, Guid TenantId, Guid DocumentId, string? AccessKey);

    /// <summary>Lê só os ids e o tipo; o resto do corpo (situação, protocolo) nunca é fonte de verdade.</summary>
    private static bool TryRead(ReadOnlyMemory<byte> body, out Payload payload)
    {
        payload = null!;
        try
        {
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("tipo", out var type) || type.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("tenantId", out var tenant) || !tenant.TryGetGuid(out var tenantId)
                || !root.TryGetProperty("documentoId", out var document) || !document.TryGetGuid(out var documentId))
            {
                return false;
            }

            string? key = null;
            if (root.TryGetProperty("chaveDeAcesso", out var chave) && chave.ValueKind == JsonValueKind.String)
            {
                key = chave.GetString();
                if (key is not { Length: 44 } || !key.All(char.IsAsciiDigit))
                {
                    return false;
                }
            }

            payload = new Payload(type.GetString()!, tenantId, documentId, key);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
