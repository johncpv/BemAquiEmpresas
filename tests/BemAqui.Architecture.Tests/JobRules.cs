using BemAqui.BuildingBlocks.Jobs;

namespace BemAqui.Architecture.Tests;

/// <summary>FND-07 CA 15: todo job deriva de TenantJob ou é um GlobalRecurringJob (nunca agenda sem tenant).</summary>
public static class JobRules
{
    public static IReadOnlyList<string> JobsOutsideBase(IEnumerable<Type> types) =>
        types
            .Where(t => t is { IsClass: true, IsAbstract: false } && t.Name.EndsWith("Job", StringComparison.Ordinal))
            .Where(t => !DerivesFromGeneric(t, typeof(TenantJob<>)) && !DerivesFromGeneric(t, typeof(GlobalRecurringJob<,>)))
            .Select(t => $"{t.FullName} não deriva de TenantJob<T> nem de GlobalRecurringJob<,>")
            .ToList();

    private static bool DerivesFromGeneric(Type type, Type generic)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == generic)
            {
                return true;
            }
        }

        return false;
    }
}
