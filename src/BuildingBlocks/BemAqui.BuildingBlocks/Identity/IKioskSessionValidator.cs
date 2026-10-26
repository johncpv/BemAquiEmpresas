using System.Net;

namespace BemAqui.BuildingBlocks.Identity;

/// <summary>Resultado da conferência da sessão de quiosque (ADR 0030 §2).</summary>
public enum KioskSessionStatus
{
    Valid,

    /// <summary>Cookie ausente, desconhecido, vencido (30 dias sem uso ou 180 absolutos) ou derrubado por "Gerar novo link".</summary>
    Invalid,

    /// <summary>A tela foi revogada (<c>kiosk.delete</c>).</summary>
    Revoked,

    /// <summary>O IP real não está nas redes permitidas da tela.</summary>
    NetworkDenied,
}

/// <summary>A tela da sessão e o escopo aplicado no servidor (linha e visões); o cliente nunca escolhe o filtro.</summary>
public sealed record KioskSessionInfo(Guid KioskId, Guid SessionId, string Name, string? Line, IReadOnlyList<string> Views, int RotationSeconds);

public sealed record KioskSessionCheck(KioskSessionStatus Status, KioskSessionInfo? Session)
{
    public static readonly KioskSessionCheck Invalid = new(KioskSessionStatus.Invalid, null);
}

/// <summary>
/// Confere a sessão de quiosque no banco do tenant do host (ADR 0030 §2): a cada request pelo cookie (com cache de 30 s invalidado na
/// revogação) e a cada 30 s no hub pela sessão. Grava o uso no máximo 1 vez por minuto. Implementado pelo Identity.
/// </summary>
public interface IKioskSessionValidator
{
    Task<KioskSessionCheck> CheckAsync(string? cookie, IPAddress? ip, CancellationToken cancellationToken = default);

    Task<KioskSessionCheck> CheckSessionAsync(Guid sessionId, IPAddress? ip, CancellationToken cancellationToken = default);
}

/// <summary>Por que a TV conectada deixa de mostrar o painel (mensagem de controle no hub do quiosque).</summary>
public enum KioskEndReason
{
    /// <summary>Tela revogada: "Esta tela foi desligada por {empresa}.".</summary>
    Revoked,

    /// <summary>"Gerar novo link" ou sessão vencida: "Esta tela precisa ser conectada de novo.".</summary>
    SessionEnded,

    /// <summary>Tenant suspenso ou módulo não liberado: "Painel indisponível nesta versão do sistema.".</summary>
    Unavailable,
}

/// <summary>
/// Mensagens de controle da TV (ADR 0030 §3): configuração nova (<c>KioskConfigChanged</c>) e fim da sessão (<c>KioskRevoked</c>), com
/// as conexões da tela fechadas. Implementado pelo Host (SignalR); fora dele, não faz nada.
/// </summary>
public interface IKioskRealtime
{
    Task ConfigChangedAsync(Guid tenantId, Guid kioskId, CancellationToken cancellationToken = default);

    Task EndedAsync(Guid tenantId, Guid kioskId, KioskEndReason reason, string companyName, CancellationToken cancellationToken = default);
}

/// <summary>Sem hub no processo (CLI, jobs isolados): nada a enviar.</summary>
public sealed class NoKioskRealtime : IKioskRealtime
{
    public Task ConfigChangedAsync(Guid tenantId, Guid kioskId, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task EndedAsync(Guid tenantId, Guid kioskId, KioskEndReason reason, string companyName, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
