using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BemAqui.BuildingBlocks.Webhooks;

/// <summary>
/// Um tipo do catálogo de avisos v1: escopo e módulo exigidos no envio (CA 8), recurso para consultar de novo e os campos permitidos no <c>data</c>.
/// <paramref name="ResourceIdField"/>: campo do <c>data</c> cujo id entra no <c>resource</c> (INT-04 CA 1: o boleto aponta para o título); nulo, o id do aviso.
/// </summary>
public sealed record OutboundEventType(string Type, string? Scope, string? Module, string Resource, IReadOnlyList<string> Fields, string Label, string? ResourceIdField = null);

/// <summary>
/// Catálogo de avisos v1 (INT-02, Regras; contrato <c>1</c>): campo novo é compatível; remover ou mudar sentido exige contrato <c>2</c>. O
/// <c>data</c> só leva ids, número legível, situação, valor e datas: o publicador recusa campo fora da lista do tipo e o teste confere que nenhum
/// campo proibido (nome, documento, contato, endereço, custo, imposto, margem) está em lista alguma.
/// </summary>
public static class OutboundWebhookCatalog
{
    public const int Contract = 1;
    public const string Ping = "ping";
    public const string AppDisconnected = "app.disconnected";

    private static readonly string[] OrderFields = ["id", "number", "status", "total", "party_id"];
    public const string WhatsAppMessageRequested = "whatsapp_message.requested";

    // INT-04 CA 1: todo resource é uma rota do integration-v1 (o teste confere contra docs/integracoes/openapi-integration-v1.json).
    private const string OrderResource = "/api/v1/integration/orders/{0}";
    private const string ChargeResource = "/api/v1/integration/charges/{0}";
    private const string WhatsAppResource = "/api/v1/integration/whatsapp-messages/{0}";
    private const string InvoiceResource = "/api/v1/integration/invoices/{0}";
    private const string ConnectionResource = "/api/v1/integration/connection";

    public static readonly IReadOnlyList<OutboundEventType> All =
    [
        Order("sales_order.created", "Pedido criado"),
        Order("sales_order.confirmed", "Pedido confirmado"),
        Order("sales_order.paid", "Pedido pago"),
        Order("sales_order.cancelled", "Pedido cancelado"),
        Order("sales_order.picked", "Pedido separado"),
        Order("sales_order.invoiced", "Pedido faturado"),
        Order("sales_order.shipped", "Pedido enviado"),
        Order("sales_order.delivered", "Pedido entregue"),
        new("billing_slip.issued", "finance.view", "finance", ChargeResource, ["id", "receivable_id", "amount", "due_date", "party_id"], "Boleto emitido", "receivable_id"),
        new("billing_slip.cancelled", "finance.view", "finance", ChargeResource, ["id", "receivable_id", "reason", "expired", "party_id"], "Boleto cancelado ou vencido", "receivable_id"),
        new("billing_slip.paid", "finance.view", "finance", ChargeResource, ["id", "receivable_id", "paid_amount", "credit_date"], "Boleto pago", "receivable_id"),
        new("invoice.authorized", "fiscal.view", "fiscal", InvoiceResource, ["id", "type", "issued_on", "total", "party_id"], "Nota autorizada"),
        new("invoice.cancelled", "fiscal.view", "fiscal", InvoiceResource, ["id", "type", "cancelled_on", "total", "party_id"], "Nota cancelada"),
        new("invoice.rejected", "fiscal.view", "fiscal", InvoiceResource, ["id", "type", "party_id"], "Nota rejeitada"),
        // INT-04 CA 7 (ADR 0053 §5): o ERP avisa "há mensagem"; o Conecta lê o destino na rota protegida pelo mesmo escopo (ATD-06a). Sem telefone,
        // nome ou variáveis no aviso.
        new(WhatsAppMessageRequested, "conecta.send_whatsapp", "conecta", WhatsAppResource, ["id", "template", "not_after"], "Mensagem de WhatsApp pedida"),
        new(AppDisconnected, null, null, ConnectionResource, ["connection_id"], "Aplicativo desconectado"),
        new(Ping, null, null, ConnectionResource, ["connection_id"], "Teste"),
    ];

    /// <summary>Nomes que nunca podem sair no corpo (CA 5).</summary>
    public static readonly string[] ForbiddenFields =
    [
        "name", "nome", "document", "documento", "cpf", "cnpj", "tax_id", "phone", "telefone", "email", "e_mail", "address", "endereco", "street", "zip",
        "cep", "cost", "custo", "unit_cost", "taxes", "tax", "imposto", "cost_tax_amount", "margin", "margem", "customer_name", "party_name",
    ];

    private static OutboundEventType Order(string type, string label) => new(type, "sales.view", "sales", OrderResource, OrderFields, label);

    public static OutboundEventType? Find(string type) => All.FirstOrDefault(t => string.Equals(t.Type, type, StringComparison.Ordinal));

    /// <summary>Tipos que a inscrição pode assinar com a concessão: sem escopo (sistema) ou com o escopo concedido.</summary>
    public static bool Allowed(OutboundEventType type, IReadOnlyCollection<string> grantedScopes)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(grantedScopes);
        return type.Scope is null || grantedScopes.Contains(type.Scope, StringComparer.Ordinal);
    }

    public static string ResourceFor(OutboundEventType type, Guid id, IReadOnlyDictionary<string, object?>? data = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (type.ResourceIdField is { } field && data is not null && data.TryGetValue(field, out var value) && value is Guid other)
        {
            id = other;
        }

        return type.Resource.Contains("{0}", StringComparison.Ordinal) ? string.Format(CultureInfo.InvariantCulture, type.Resource, id) : type.Resource;
    }

    /// <summary>Motivo do <c>Skipped</c> quando a conexão perdeu o escopo (CA 8).</summary>
    public static string MissingScopeReason(string scope) => scope switch
    {
        "sales.view" => "Sem permissão para ver pedidos",
        "finance.view" => "Sem permissão para ver cobranças",
        "fiscal.view" => "Sem permissão para ver notas",
        "conecta.send_whatsapp" => "Sem permissão para mandar WhatsApp",
        _ => "Sem permissão para " + scope,
    };
}

/// <summary>
/// Corpo do aviso (CA 5), em bytes determinísticos: JSON UTF-8 com as chaves na ordem do contrato e o <c>data</c> gravado no evento copiado como
/// está. Montado de novo a cada tentativa a partir do evento, o resultado é byte a byte o mesmo (CA 9).
/// </summary>
public static class OutboundWebhookBody
{
    public const int MaxBytes = 64 * 1024;

    public static byte[] Build(Guid eventId, string type, DateTimeOffset occurredAt, string tenant, Guid connectionId, string data, string resource)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(data);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("id", eventId);
            writer.WriteString("type", type);
            writer.WriteNumber("contract", OutboundWebhookCatalog.Contract);
            writer.WriteString("occurred_at", Utc(occurredAt));
            writer.WriteString("tenant", tenant);
            writer.WriteString("connection_id", connectionId);
            writer.WritePropertyName("data");
            writer.WriteRawValue(data, skipInputValidation: false);
            writer.WriteString("resource", resource);
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    /// <summary><c>data</c> em JSON compacto, com as chaves na ordem dada.</summary>
    public static string Data(IReadOnlyDictionary<string, object?> data) => JsonSerializer.Serialize(data);

    public static string Utc(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
}

/// <summary>
/// Assinatura dos avisos (CA 5 e 6, ADR 0053): <c>v1=</c> + hex minúsculo do HMAC-SHA256 de <c>"{timestamp}.{corpo}"</c>, com a chave = bytes UTF-8
/// do segredo. Na sobreposição da troca, <c>v1=&lt;nova&gt;,v1=&lt;antiga&gt;</c>. O recebedor confere em tempo constante e com janela de 5 min.
/// </summary>
public static class OutboundWebhookSigner
{
    public const string EventIdHeader = "BemAqui-Event-Id";
    public const string EventTypeHeader = "BemAqui-Event-Type";
    public const string TimestampHeader = "BemAqui-Timestamp";
    public const string SignatureHeader = "BemAqui-Signature";

    /// <summary>INT-04 CA 4: id da conexão, fora da assinatura, para o recebedor achar o segredo sem ler corpo ainda não autenticado.</summary>
    public const string ConnectionHeader = "BemAqui-Connection";
    public const string UserAgent = "BemAqui-Webhooks/1";

    public static string Sign(string secret, long timestamp, ReadOnlySpan<byte> body)
    {
        ArgumentNullException.ThrowIfNull(secret);
        var prefix = Encoding.UTF8.GetBytes(timestamp.ToString(CultureInfo.InvariantCulture) + ".");
        var payload = new byte[prefix.Length + body.Length];
        prefix.CopyTo(payload, 0);
        body.CopyTo(payload.AsSpan(prefix.Length));
        return "v1=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), payload));
    }

    /// <summary>Cabeçalho com a atual e, se houver, a anterior.</summary>
    public static string Header(string current, string? previous, long timestamp, ReadOnlySpan<byte> body) =>
        previous is null ? Sign(current, timestamp, body) : Sign(current, timestamp, body) + "," + Sign(previous, timestamp, body);

    /// <summary>Segredo novo: 32 bytes aleatórios em base64url (43 caracteres).</summary>
    public static string NewSecret() => Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
}

/// <summary>
/// Novas tentativas (CA 10): depois da 1ª falha, 1 min, 5 min, 30 min, 2 h, 6 h, 12 h e 24 h (8 envios no total), com ±10%; <c>Retry-After</c> do
/// 429 e do 503 é respeitado até 1 h. Depois da 8ª falha, null (<c>DeadLettered</c>).
/// </summary>
public static class OutboundRetryPolicy
{
    public const int MaxAttempts = 8;
    public static readonly TimeSpan MaxRetryAfter = TimeSpan.FromHours(1);

    private static readonly TimeSpan[] Delays =
    [
        TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), TimeSpan.FromHours(2), TimeSpan.FromHours(6), TimeSpan.FromHours(12),
        TimeSpan.FromHours(24),
    ];

    /// <param name="attemptsDone">Envios já feitos, contando o que acabou de falhar.</param>
    /// <param name="jitter">Entre -1 e 1 (fração de 10%).</param>
    public static TimeSpan? NextDelay(int attemptsDone, TimeSpan? retryAfter, double jitter)
    {
        if (attemptsDone >= MaxAttempts)
        {
            return null;
        }

        if (retryAfter is { } wait && wait > TimeSpan.Zero)
        {
            return wait > MaxRetryAfter ? MaxRetryAfter : wait;
        }

        var baseDelay = Delays[Math.Clamp(attemptsDone - 1, 0, Delays.Length - 1)];
        return TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * (1 + (Math.Clamp(jitter, -1, 1) * 0.1)));
    }
}
