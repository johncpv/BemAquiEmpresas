namespace BemAqui.BuildingBlocks.Identity;

/// <summary>Quem está agindo (auditoria). Sem usuário autenticado (jobs, sistema), <see cref="UserId"/> é null.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }

    /// <summary>IP real do cliente (forwarded headers confiáveis, FND-02).</summary>
    string? IpAddress { get; }
}

/// <summary>Usuário "sistema": usado por jobs e até a autenticação existir (FND-04).</summary>
public sealed class SystemCurrentUser : ICurrentUser
{
    public Guid? UserId => null;

    public string? IpAddress => null;
}
