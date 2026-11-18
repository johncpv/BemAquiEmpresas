namespace BemAqui.BuildingBlocks.Identity;

/// <summary>Resultado da conferência da sessão por PIN a cada request (ADR 0026 §1 e §3).</summary>
public enum PinSessionCheck
{
    Valid,

    /// <summary>Aparelho revogado, vencido, cookie ausente ou de outro aparelho: o tablet volta a "Este aparelho não está registrado.".</summary>
    DeviceInvalid,

    /// <summary>Sessão por PIN revogada, passada das 12 h, sem <c>shopfloor.create</c>, perfil sensível ou funcionário inativo.</summary>
    SessionEnded,
}

/// <summary>
/// Confere a sessão aberta por PIN em todo request (PRD-03b CA 10, 15, 17 e 20): aparelho válido e com o segredo do cookie, sessão
/// viva dentro das 12 h, <c>shopfloor.create</c>, perfil sem ação reservada e funcionário ativo. Implementado pelo Identity.
/// </summary>
public interface IPinSessionValidator
{
    Task<PinSessionCheck> CheckAsync(Guid userId, string? deviceSecret, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sessão por PIN no aparelho de caixa do PDV (PDV-01 CA 11, ADR 0048): o aparelho do cookie <c>__Host-bq_pos</c> continua ativo, a sessão por PIN
    /// dele está viva dentro das 12 h, o usuário continua ativo, com <c>pos.create</c>, sem ação reservada e, se for usuário de operação, com a loja do
    /// aparelho entre as dele. Implementado pelo Identity.
    /// </summary>
    Task<PinSessionCheck> CheckRegisterAsync(Guid userId, string? posDeviceSecret, CancellationToken cancellationToken = default);
}