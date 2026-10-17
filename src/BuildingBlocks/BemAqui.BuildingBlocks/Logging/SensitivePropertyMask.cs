using BemAqui.SharedKernel;
using Serilog.Core;
using Serilog.Events;

namespace BemAqui.BuildingBlocks.Logging;

/// <summary>
/// Mascara no log toda propriedade cujo nome indica segredo (password, token, authorization, cookie, secret,
/// apikey), inclusive dentro de objetos e listas (FND-12 CA 3). Complementa a política do tipo Secret (FND-11).
/// </summary>
public sealed class SensitivePropertyMask : ILogEventEnricher
{
    private static readonly string[] Markers = ["password", "senha", "token", "authorization", "cookie", "secret", "apikey", "api_key", "connectionstring"];

    public static bool IsSensitive(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var normalized = name.Replace("-", string.Empty, StringComparison.Ordinal).Replace("_", string.Empty, StringComparison.Ordinal);
        return Markers.Any(m => normalized.Contains(m.Replace("_", string.Empty, StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase));
    }

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        foreach (var (name, value) in logEvent.Properties.ToList())
        {
            var masked = IsSensitive(name) ? new ScalarValue(Secret.Mask) : Mask(value);
            if (!ReferenceEquals(masked, value))
            {
                logEvent.AddOrUpdateProperty(new LogEventProperty(name, masked));
            }
        }
    }

    private static LogEventPropertyValue Mask(LogEventPropertyValue value) => value switch
    {
        StructureValue structure when structure.Properties.Any(p => IsSensitive(p.Name) || p.Value is not ScalarValue) =>
            new StructureValue(structure.Properties.Select(p => new LogEventProperty(p.Name, IsSensitive(p.Name) ? new ScalarValue(Secret.Mask) : Mask(p.Value))), structure.TypeTag),
        SequenceValue sequence => new SequenceValue(sequence.Elements.Select(Mask)),
        DictionaryValue dictionary => new DictionaryValue(dictionary.Elements.Select(e =>
            new KeyValuePair<ScalarValue, LogEventPropertyValue>(e.Key, e.Key.Value is string k && IsSensitive(k) ? new ScalarValue(Secret.Mask) : Mask(e.Value)))),
        _ => value,
    };
}
