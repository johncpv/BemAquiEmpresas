namespace BemAqui.BuildingBlocks.Identity;

/// <summary>
/// "2FA de novo" (D6, ADR 0008 adendo de 21/10/2026): antes de uma ação sensível (ex.: gerar o segredo de um webhook, FND-16 CA 7),
/// a pessoa confirma com o código do aplicativo autenticador. Sem 2FA ativo, ou com código errado ou já usado, recusa. A BNK-01a
/// reaproveita este contrato no <c>RequireRecentTwoFactor</c>.
/// </summary>
public interface IStepUpTwoFactor
{
    Task<bool> VerifyAsync(Guid userId, string? code, CancellationToken cancellationToken = default);
}

/// <summary>Sem o Identity no processo: falha fechada.</summary>
public sealed class DenyStepUpTwoFactor : IStepUpTwoFactor
{
    public Task<bool> VerifyAsync(Guid userId, string? code, CancellationToken cancellationToken = default) => Task.FromResult(false);
}
