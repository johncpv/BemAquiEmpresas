using BemAqui.BuildingBlocks.Email;
using BemAqui.BuildingBlocks.Observability;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Platform.Domain;
using BemAqui.Platform.Infrastructure.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BemAqui.Platform.Infrastructure.Alerts;

/// <summary>
/// Alertas aos super admins por e-mail, deduplicados no catálogo: no máximo um envio por tipo e tenant a cada
/// janela (30 min por padrão; FND-12 CA 6). Toda ocorrência vai para o log; falha ao alertar nunca propaga.
/// </summary>
public sealed partial class CatalogAlertSink(IServiceScopeFactory scopes, IEmailTransport transport, IOptions<AlertOptions> options, TimeProvider clock, ILogger<CatalogAlertSink> logger) : IAlertSink
{
    public async Task RaiseAsync(Alert alert, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(alert);
        AlertLog.Raised(logger, alert.Kind, alert.Tenant, alert.Summary);
        try
        {
            if (!await ShouldSendAsync(alert, cancellationToken) || options.Value.Recipients.Length == 0)
            {
                return;
            }

            var when = BusinessTime.ToBusiness(clock.GetUtcNow()).ToString("dd/MM/yyyy HH:mm", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
            var text = $"Alerta: {alert.Summary}\nTipo: {alert.Kind}\nTenant: {alert.Tenant ?? "(plataforma)"}\nQuando: {when} (horário de Brasília)\n\n{alert.Details}\n{(alert.Link is null ? string.Empty : "Detalhes: " + alert.Link)}";
            var content = new RenderedEmail("[BemAqui Empresas] " + alert.Summary, "<pre>" + System.Net.WebUtility.HtmlEncode(text) + "</pre>", text);
            foreach (var to in options.Value.Recipients)
            {
                await transport.SendAsync(new OutgoingEmail(to, content), cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogAlertFailed(logger, ex, alert.Kind);
        }
    }

    private async Task<bool> ShouldSendAsync(Alert alert, CancellationToken cancellationToken)
    {
        var key = alert.Kind + ":" + (alert.Tenant ?? "-");
        var now = clock.GetUtcNow();
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var existing = await db.PlatformAlerts.FirstOrDefaultAsync(a => a.Key == key, cancellationToken);
        if (existing is not null && now - existing.LastSentAt < options.Value.DedupWindow)
        {
            existing.Occurrences++;
            await db.SaveChangesAsync(cancellationToken);
            return false;
        }

        if (existing is null)
        {
            db.PlatformAlerts.Add(new PlatformAlert { Key = key, LastSentAt = now, Occurrences = 1, LastSummary = Truncate(alert.Summary) });
        }
        else
        {
            existing.LastSentAt = now;
            existing.Occurrences = 1;
            existing.LastSummary = Truncate(alert.Summary);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            return false; // outra instância registrou o mesmo alerta ao mesmo tempo: só uma envia
        }
    }

    private static string Truncate(string value) => value.Length > 500 ? value[..500] : value;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Não foi possível enviar o alerta {Kind} por e-mail.")]
    private static partial void LogAlertFailed(ILogger logger, Exception exception, string kind);
}
