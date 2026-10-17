using System.Diagnostics.CodeAnalysis;
using BemAqui.SharedKernel;
using Serilog.Core;
using Serilog.Events;

namespace BemAqui.BuildingBlocks.Logging;

/// <summary>Serilog: todo <see cref="Secret"/> (inclusive dentro de objetos com <c>{@Obj}</c>) vira <c>***</c>.</summary>
public sealed class SecretDestructuringPolicy : IDestructuringPolicy
{
    public bool TryDestructure(object value, ILogEventPropertyValueFactory propertyValueFactory, [NotNullWhen(true)] out LogEventPropertyValue? result)
    {
        if (value is Secret)
        {
            result = new ScalarValue(Secret.Mask);
            return true;
        }

        result = null;
        return false;
    }
}
