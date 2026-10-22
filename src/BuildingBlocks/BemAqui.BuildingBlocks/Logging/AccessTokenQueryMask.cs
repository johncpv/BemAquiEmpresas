using System.Text.RegularExpressions;
using BemAqui.SharedKernel;
using Serilog.Core;
using Serilog.Events;

namespace BemAqui.BuildingBlocks.Logging;

/// <summary>
/// O token do SignalR chega na query <c>access_token</c> (o WebSocket não manda Authorization): em qualquer propriedade de texto do
/// log (URL, query string, mensagem do ASP.NET Core), o valor vira <c>***</c> (PRD-02 CA 18). O nginx grava a URL sem a query.
/// </summary>
public sealed partial class AccessTokenQueryMask : ILogEventEnricher
{
    [GeneratedRegex("(access_token=)[^&\\s\"']*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Token();

    public static string Mask(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Contains("access_token", StringComparison.OrdinalIgnoreCase) ? Token().Replace(value, "$1" + Secret.Mask) : value;
    }

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        foreach (var (name, value) in logEvent.Properties.ToList())
        {
            if (value is ScalarValue { Value: string text } && text.Contains("access_token", StringComparison.OrdinalIgnoreCase))
            {
                logEvent.AddOrUpdateProperty(new LogEventProperty(name, new ScalarValue(Mask(text))));
            }
        }
    }
}
