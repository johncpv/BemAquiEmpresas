namespace BemAqui.BuildingBlocks.Modules;

/// <summary>Resultado da concessão: o que entra e o que fica de fora porque quem autoriza não tem.</summary>
public sealed record AppScopeGrant(IReadOnlyList<string> Granted, IReadOnlyList<string> NotHeld, IReadOnlyList<string> Unknown);

/// <summary>
/// Escopos de aplicativo conectado (INT-01, ADR 0053 §5 e §6): códigos <c>modulo.acao</c> do <see cref="ModuleCatalog"/>. O texto mostrado no
/// consentimento é o mesmo da ajuda; ações reservadas do ADR 0008 e as que pedem 2FA de novo nunca são concedidas.
/// </summary>
public static class ConnectedAppScopes
{
    /// <summary>Fonte única dos escopos do Conecta (ADR 0053 §5).</summary>
    public static readonly string[] Conecta =
    [
        "catalog.view", "inventory.view", "parties.view", "parties.create", "parties.edit", "sales.view", "sales.create", "sales.edit",
        "finance.view", "fiscal.view", "projects.view",
    ];

    private static readonly string[] NeverGranted =
        ["finance.pay", "hr.view_sensitive", "hr.view_health", "identity.edit", "settings.edit", "pos.manage_devices"];

    private static readonly Dictionary<string, string> Texts = new(StringComparer.Ordinal)
    {
        ["catalog.view"] = "Ver produtos e preços",
        ["inventory.view"] = "Ver o saldo em estoque",
        ["parties.view"] = "Ver clientes (nome, telefone, e-mail, documento)",
        ["parties.create"] = "Cadastrar clientes",
        ["parties.edit"] = "Completar o contato de clientes (o documento nunca muda)",
        ["sales.view"] = "Ver pedidos de venda",
        ["sales.create"] = "Criar pedidos de venda",
        ["sales.edit"] = "Alterar pedidos de venda em aberto",
        ["finance.view"] = "Ver cobranças",
        ["fiscal.view"] = "Ver notas fiscais",
        ["projects.view"] = "Ver projetos e a etapa em que estão",
    };

    /// <summary>Ação que um aplicativo nunca recebe: <c>*.approve</c>, as reservadas do ADR 0008 e as que pedem 2FA de novo.</summary>
    public static bool IsNeverGranted(string scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return scope.EndsWith(".approve", StringComparison.Ordinal) || NeverGranted.Contains(scope, StringComparer.Ordinal);
    }

    /// <summary>Texto em português simples para o consentimento e a ajuda.</summary>
    public static string Text(string scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (Texts.TryGetValue(scope, out var text))
        {
            return text;
        }

        var parts = scope.Split('.');
        var module = ModuleCatalog.Find(parts[0]);
        return module is null || parts.Length != 2 ? scope : StandardActions.Label(parts[1]) + " em " + module.Name;
    }

    /// <summary>Lista do <c>scope</c> (separada por espaço), sem repetição e na ordem pedida.</summary>
    public static IReadOnlyList<string> Parse(string? scope) =>
        (scope ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>
    /// Concessão = pedidos ∩ permitidos do aplicativo ∩ permissões efetivas de quem autoriza, sem as nunca concedidas (INT-01 CA 9). Pedido fora do
    /// catálogo ou dos permitidos volta em <see cref="AppScopeGrant.Unknown"/> (<c>invalid_scope</c>); o que a pessoa não tem, em <c>NotHeld</c>.
    /// </summary>
    public static AppScopeGrant Compute(IEnumerable<string> requested, IReadOnlyCollection<string> allowed, Func<string, bool> holds)
    {
        ArgumentNullException.ThrowIfNull(requested);
        ArgumentNullException.ThrowIfNull(allowed);
        ArgumentNullException.ThrowIfNull(holds);
        var granted = new List<string>();
        var notHeld = new List<string>();
        var unknown = new List<string>();
        foreach (var scope in requested.Distinct(StringComparer.Ordinal))
        {
            if (!ModuleCatalog.IsKnownPermission(scope) || !allowed.Contains(scope, StringComparer.Ordinal) || IsNeverGranted(scope))
            {
                unknown.Add(scope);
            }
            else if (holds(scope))
            {
                granted.Add(scope);
            }
            else
            {
                notHeld.Add(scope);
            }
        }

        return new AppScopeGrant(granted, notHeld, unknown);
    }
}
