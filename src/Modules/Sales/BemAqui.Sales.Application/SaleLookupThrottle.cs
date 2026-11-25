using System.Collections.Concurrent;

namespace BemAqui.Sales.Application;

/// <summary>
/// Revisão de segurança da PDV-06 (achado 8): teto de buscas de venda por CPF por caixa, além do rate limit da rota (por usuário e aparelho). Janela
/// fixa em memória, como o rate limiter do ASP.NET (uma instância da API); trocar de operador no mesmo caixa não zera o teto. Singleton.
/// </summary>
public sealed class SaleLookupThrottle(TimeProvider clock)
{
    public const int PermitsPerWindow = 20;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<Guid, (DateTimeOffset Start, int Count)> windows = new();

    public bool TryAcquire(Guid registerId)
    {
        var now = clock.GetUtcNow();
        var window = windows.AddOrUpdate(registerId, _ => (now, 1), (_, current) => now - current.Start >= Window ? (now, 1) : (current.Start, current.Count + 1));
        return window.Count <= PermitsPerWindow;
    }
}
