namespace BemAqui.BuildingBlocks.Security;

/// <summary>Escopo do segredo: a plataforma ou um tenant. Entra no propósito do Data Protection.</summary>
public readonly record struct SecretScope
{
    private SecretScope(string value) => Value = value;

    public string Value { get; }

    public static SecretScope Platform { get; } = new("platform");

    public static SecretScope ForTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant inválido para escopo de segredo.", nameof(tenantId));
        }

        return new SecretScope($"tenant:{tenantId:N}");
    }

    public override string ToString() => Value;
}
