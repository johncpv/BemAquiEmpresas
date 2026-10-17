namespace BemAqui.SharedKernel;

/// <summary>Entidade com id Guid v7 gerado na aplicação (ordenável no tempo).</summary>
public abstract class Entity
{
    public Guid Id { get; protected init; } = Guid.CreateVersion7();
}
