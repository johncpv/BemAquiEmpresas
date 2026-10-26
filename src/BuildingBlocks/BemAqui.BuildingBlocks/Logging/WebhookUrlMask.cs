using System.Text.RegularExpressions;
using BemAqui.SharedKernel;
using Serilog.Core;
using Serilog.Events;

namespace BemAqui.BuildingBlocks.Logging;

/// <summary>
/// URL de webhook no log (FND-16 CA 13): o caminho fica só com o provedor (<c>/webhooks/{provider}/***</c>: some o
/// <c>tenantKey</c> e o token do modo <c>PathToken</c>) e o token da query (<c>t=</c>) vira <c>***</c>, em qualquer propriedade
/// de texto (caminho do request, URL do ASP.NET Core, mensagem).
/// </summary>
public sealed partial class WebhookUrlMask : ILogEventEnricher
{
    private const string Marker = "/webhooks/";

    [GeneratedRegex(@"(/webhooks/[A-Za-z0-9\-]{1,32})/[^\s?#""']*", RegexOptions.CultureInvariant)]
    private static partial Regex PathSegments();

    [GeneratedRegex(@"([?&](?:t|hub\.verify_token|hub\.challenge)=)[^&\s""'#]*", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex QueryToken();

    public static string Mask(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!value.Contains(Marker, StringComparison.OrdinalIgnoreCase) && !value.Contains("t=", StringComparison.Ordinal))
        {
            return value;
        }

        var masked = PathSegments().Replace(value, "$1/" + Secret.Mask);
        return masked.Contains(Marker, StringComparison.OrdinalIgnoreCase) || masked.StartsWith('?') ? QueryToken().Replace(masked, "$1" + Secret.Mask) : masked;
    }

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        var touchesWebhook = logEvent.Properties.Values.Any(v => v is ScalarValue { Value: string s } && s.Contains(Marker, StringComparison.OrdinalIgnoreCase));
        if (!touchesWebhook)
        {
            return;
        }

        foreach (var (name, value) in logEvent.Properties.ToList())
        {
            if (value is ScalarValue { Value: string text })
            {
                var masked = name == "QueryString" ? (text.Length == 0 ? text : Secret.Mask) : Mask(text);
                if (!string.Equals(masked, text, StringComparison.Ordinal))
                {
                    logEvent.AddOrUpdateProperty(new LogEventProperty(name, new ScalarValue(masked)));
                }
            }
        }
    }
}
