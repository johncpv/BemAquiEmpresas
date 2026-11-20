namespace BemAqui.Identity.Contracts;

/// <summary>
/// Histórico do operador do caixa para a subida dos eventos (OFF-02, requisitos de segurança da PDV-02, PDV-03 e CX-01): o servidor não confia
/// no operador nem na permissão que o aparelho diz ter. O Identity é o dono das sessões por PIN e das permissões; o <c>Pos</c> pergunta por aqui.
/// </summary>
public interface IPosOperatorHistory
{
    /// <summary>
    /// O usuário tinha sessão por PIN aberta neste aparelho de caixa no instante (com a folga de relógio de 2 min da ADR 0026): a sessão nasceu
    /// antes e não tinha sido encerrada, revogada nem vencida.
    /// </summary>
    Task<bool> HadPinSessionAsync(Guid userId, Guid deviceId, DateTimeOffset at, CancellationToken cancellationToken = default);

    /// <summary>
    /// O usuário tinha a ação (<c>modulo.acao</c>) no instante, pelo retrato das permissões que valia naquele momento (ou, sem retrato anterior, o
    /// mais antigo que se conhece).
    /// </summary>
    Task<bool> HadPermissionAsync(Guid userId, string permission, DateTimeOffset at, CancellationToken cancellationToken = default);
}
