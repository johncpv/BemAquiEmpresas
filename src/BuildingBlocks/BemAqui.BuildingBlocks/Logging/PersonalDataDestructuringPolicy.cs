using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using BemAqui.SharedKernel;
using Serilog.Core;
using Serilog.Events;

namespace BemAqui.BuildingBlocks.Logging;

/// <summary>
/// Objeto logado com <c>{@Objeto}</c>: propriedade marcada com <see cref="PersonalDataAttribute"/> sai como "***"
/// (CAD-01a CA 25, LGPD). Só atua em tipos que têm alguma propriedade marcada; os demais seguem o padrão do Serilog.
/// </summary>
public sealed class PersonalDataDestructuringPolicy : IDestructuringPolicy
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]?> Cache = new();

    public bool TryDestructure(object value, ILogEventPropertyValueFactory propertyValueFactory, [NotNullWhen(true)] out LogEventPropertyValue? result)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(propertyValueFactory);
        var properties = Cache.GetOrAdd(value.GetType(), type =>
        {
            var all = type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead && p.GetIndexParameters().Length == 0).ToArray();
            return all.Any(p => p.GetCustomAttribute<PersonalDataAttribute>() is not null) ? all : null;
        });
        if (properties is null)
        {
            result = null;
            return false;
        }

        result = new StructureValue(properties.Select(p => new LogEventProperty(p.Name, p.GetCustomAttribute<PersonalDataAttribute>() is not null
            ? new ScalarValue(Secret.Mask)
            : propertyValueFactory.CreatePropertyValue(p.GetValue(value), destructureObjects: true))), value.GetType().Name);
        return true;
    }
}
