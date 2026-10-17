using System.Reflection;
using BemAqui.SharedKernel;

namespace BemAqui.Architecture.Tests;

/// <summary>FND-11 CA 5: tipos expostos pelos Endpoints (DTOs de resposta) nunca têm <see cref="Secret"/>.</summary>
public static class SecretExposureRules
{
    public static IReadOnlyList<string> SecretProperties(IEnumerable<Type> types) =>
        types
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => (Type: t, Property: p)))
            .Where(x => ExposesSecret(x.Property.PropertyType))
            .Select(x => $"{x.Type.FullName}.{x.Property.Name} expõe Secret (segredo é write-only, ADR 0007)")
            .ToList();

    private static bool ExposesSecret(Type type) =>
        type == typeof(Secret)
        || (type.IsArray && ExposesSecret(type.GetElementType()!))
        || (type.IsGenericType && type.GetGenericArguments().Any(ExposesSecret));
}
