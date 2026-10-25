using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Time;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.BuildingBlocks.Email;

/// <summary>
/// Limite diário dos envios de documento por e-mail (COM-03 CA 11 e CMP-03b CA 5): no tenant, 50 por dia somando orçamentos
/// (<c>quote-sent</c>) e pedidos de compra (<c>purchase-order-sent</c>); por usuário, o parâmetro <c>email.dispatch_max_per_user_day</c>
/// (padrão 20), também somado. A contagem é sobre <c>shared.email_logs</c> do dia em São Paulo e roda com a trava do tenant
/// (<c>pg_advisory_xact_lock</c>) na transação do envio: dois envios simultâneos no último lugar, só um passa.
/// </summary>
public sealed class EmailDispatchLimiter(BemAquiDbContext db, TimeProvider clock)
{
    public const string QuoteTemplate = "quote-sent";
    public const string PurchaseOrderTemplate = "purchase-order-sent";
    public const int TenantDailyLimit = 50;
    public const int DefaultPerUserDailyLimit = 20;
    public const string TenantLimitCode = "email_dispatch_limit";
    public const string UserLimitCode = "email_dispatch_user_limit";

    /// <summary>Chave da trava; cada tenant tem o próprio banco, então a chave fixa já é por tenant.</summary>
    public const string LockKey = "email.document_dispatch";

    public static IReadOnlyList<string> DispatchTemplates { get; } = [QuoteTemplate, PurchaseOrderTemplate];

    /// <summary>
    /// Trava o tenant e confere os dois limites. Exige a transação do envio aberta (a trava vale até o commit); devolve o erro 429
    /// ou null quando o envio pode entrar na fila.
    /// </summary>
    public async Task<Error?> CheckAsync(Guid? userId, int perUserLimit, CancellationToken cancellationToken = default)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("O limite diário de envios precisa da transação do envio aberta.");
        }

        await db.Database.SqlQuery<int>($"SELECT 1 AS \"Value\" FROM (SELECT pg_advisory_xact_lock(hashtextextended({LockKey}, 0))) AS l").ToListAsync(cancellationToken);
        var since = BusinessTime.StartOfDayUtc(BusinessTime.Today(clock));
        var templates = DispatchTemplates.ToList();
        var today = db.EmailLogs.AsNoTracking().Where(l => templates.Contains(l.Template) && l.CreatedAt >= since);
        if (await today.CountAsync(cancellationToken) >= TenantDailyLimit)
        {
            return Error.TooManyRequests(TenantLimitCode, "Limite diário de envios atingido. Tente amanhã ou baixe o PDF.");
        }

        return userId is { } user && await today.CountAsync(l => l.RequestedBy == user, cancellationToken) >= perUserLimit
            ? Error.TooManyRequests(UserLimitCode, "Você atingiu o seu limite diário de envios. Tente amanhã ou baixe o PDF.")
            : null;
    }
}

/// <summary>Regras dos endereços e do texto de cabeçalho dos envios de documento (COM-03 CA 9 e 10, CMP-03b CA 6 e 7).</summary>
public static class EmailAddresses
{
    public const int MaxLength = 254;

    private static readonly char[] Forbidden = [',', ';', '<', '>', '"', ' ', '\t', (char)13, (char)10];

    /// <summary>Endereço puro: sem nome de exibição, separadores nem quebra de linha; formato válido; até 254 caracteres.</summary>
    public static bool IsPlain(string? address)
    {
        var value = address?.Trim() ?? string.Empty;
        return value.Length is > 0 and <= MaxLength && value.IndexOfAny(Forbidden) < 0
            && System.Net.Mail.MailAddress.TryCreate(value, out var parsed) && parsed.Address == value && string.IsNullOrEmpty(parsed.DisplayName);
    }

    /// <summary>Sem CR/LF: o nome da empresa vai no assunto e no remetente.</summary>
    public static string OneLine(string? text) =>
        string.Join(' ', (text ?? string.Empty).Split([(char)13, (char)10], StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).Where(p => p.Length > 0)).Trim();

    /// <summary>Endereço mascarado para quem não vê dado pessoal de terceiros (CMP-03b CA 11): "m***@aurora.com.br".</summary>
    public static string Mask(string address)
    {
        ArgumentNullException.ThrowIfNull(address);
        var at = address.IndexOf('@', StringComparison.Ordinal);
        return at <= 0 ? "***" : address[0] + "***" + address[at..];
    }
}
