using System.Text.RegularExpressions;

namespace BemAqui.BuildingBlocks.Modules;

/// <summary>Áreas do dock (CLAUDE.md, Interface): todo módulo pertence a uma.</summary>
public static class ModuleAreas
{
    public const string Overview = "Visão geral";
    public const string Commercial = "Comercial";
    public const string Factory = "Fábrica";
    public const string Financial = "Financeiro";
    public const string People = "Pessoas";
    public const string Dedicated = "Telas dedicadas";
    public const string System = "Sistema";

    public static readonly string[] All = [Overview, Commercial, Factory, Financial, People, Dedicated, System];
}

/// <summary>Ações padrão (CLAUDE.md, camada 3). Ações específicas são snake_case e só existem com necessidade real (ADR 0003).</summary>
public static class StandardActions
{
    public const string View = "view";
    public const string Create = "create";
    public const string Edit = "edit";
    public const string Approve = "approve";
    public const string Delete = "delete";

    public static readonly string[] All = [View, Create, Edit, Approve, Delete];

    public static string Label(string action) => action switch
    {
        View => "Ver",
        Create => "Criar",
        Edit => "Editar",
        Approve => "Aprovar",
        Delete => "Excluir",
        _ => action,
    };
}

public sealed record ModuleAction(string Code, string Label, string Description);

/// <summary>Um módulo do catálogo: o que o super admin libera, o grupo habilita e a ação marca.</summary>
public sealed record ModuleDefinition(
    string Code,
    string Name,
    string Area,
    int Phase,
    int Order,
    string Description,
    IReadOnlyList<string> Standard,
    IReadOnlyList<ModuleAction> Specific,
    string? ApproveMeaning = null)
{
    public IEnumerable<string> Actions => Standard.Concat(Specific.Select(s => s.Code));

    public bool HasAction(string action) => Actions.Contains(action, StringComparer.Ordinal);
}

/// <summary>
/// Fonte única dos módulos e ações (FND-06, ADR 0003 e ADR 0010 item 1). Lido pelo seed do tenant, pela policy de
/// permissão, pela matriz de grupos, pelo menu e pela ajuda. Permissão é sempre <c>modulo.acao</c>, em dois níveis.
/// </summary>
public static partial class ModuleCatalog
{
    private static readonly string[] Crud = [StandardActions.View, StandardActions.Create, StandardActions.Edit, StandardActions.Delete];
    private static readonly string[] CrudApprove = [StandardActions.View, StandardActions.Create, StandardActions.Edit, StandardActions.Approve, StandardActions.Delete];

    public static IReadOnlyList<ModuleDefinition> All { get; } =
    [
        new("dashboard", "Painel do proprietário", ModuleAreas.Overview, 4, 10, "Indicadores de caixa, margem e produção num só lugar.", [StandardActions.View], []),
        new("quotes", "Orçamentos", ModuleAreas.Commercial, 2, 20, "Orçamentos com custo e margem, até virar contrato.", CrudApprove, [], "gerar o contrato"),
        new("projects", "Projetos", ModuleAreas.Commercial, 2, 30, "Contratos e projetos, com etapas e aditivos.", CrudApprove, [], "aditivos de contrato"),
        new("parties", "Cadastros", ModuleAreas.Commercial, 1, 40, "Clientes, fornecedores e instaladores.", Crud, []),
        new("engineering", "Engenharia", ModuleAreas.Factory, 3, 50, "Estrutura de produto (BOM) e roteiros de fabricação.", [StandardActions.View, StandardActions.Create, StandardActions.Edit, StandardActions.Approve], [], "liberar a BOM"),
        new("production", "Produção", ModuleAreas.Factory, 3, 60, "Ordens de serviço, Kanban, apontamentos e qualidade.", [StandardActions.View, StandardActions.Create, StandardActions.Edit, StandardActions.Approve], [], "liberar na qualidade"),
        new("inventory", "Estoque", ModuleAreas.Factory, 3, 70, "Itens, saldos, reservas e movimentos.", [StandardActions.View, StandardActions.Create, StandardActions.Edit, StandardActions.Approve],
            [new("adjust", "Ajustar estoque", "Lançar ajuste de inventário, que muda o saldo sem documento de origem.")], "ajuste de inventário"),
        new("purchasing", "Compras", ModuleAreas.Factory, 3, 80, "Solicitações, cotações, pedidos e recebimento.", CrudApprove, [], "pedido de compra dentro da alçada"),
        new("assets", "Patrimônio", ModuleAreas.Factory, 6, 90, "Bens, QR code e manutenção.", Crud, []),
        new("finance", "Caixa e pagamentos", ModuleAreas.Financial, 2, 100, "Contas a receber e a pagar, ordens de pagamento e fluxo de caixa.", CrudApprove,
            [new("pay", "Pagar", "Dar baixa de pagamento: registra a saída de caixa de uma ordem aprovada.")], "ordem de pagamento, dentro da alçada"),
        new("fiscal", "Fiscal", ModuleAreas.Financial, 5, 110, "Notas fiscais de saída e de entrada.", [StandardActions.View, StandardActions.Create, StandardActions.Edit], []),
        new("hr", "RH", ModuleAreas.People, 6, 120, "Funcionários, cargos, ponto e folha.", CrudApprove,
            [new("view_sensitive", "Ver dados sensíveis", "Ver salário, documentos e dados pessoais protegidos pela LGPD.")], "ajustes de ponto"),
        new("kiosk", "Painel da TV", ModuleAreas.Dedicated, 4, 130, "Painéis de fábrica na TV, sem login. Criar = gerar o token de uma tela.", [StandardActions.View, StandardActions.Create], []),
        new("shopfloor", "App do operador", ModuleAreas.Dedicated, 3, 140, "Fila do operador e apontamento das etapas. Criar = apontar.", [StandardActions.View, StandardActions.Create], []),
        new("identity", "Usuários e acessos", ModuleAreas.System, 0, 150, "Usuários, grupos e permissões.", Crud, []),
        new("settings", "Configurações", ModuleAreas.System, 0, 160, "Parâmetros da empresa: segurança, numeração, IA e mais.", [StandardActions.View, StandardActions.Edit], []),
    ];

    public static ModuleDefinition? Find(string code) => All.FirstOrDefault(m => string.Equals(m.Code, code, StringComparison.Ordinal));

    /// <summary>Permissão válida: <c>modulo.acao</c> com módulo e ação do catálogo. Três níveis nunca valem.</summary>
    public static bool IsKnownPermission(string permission)
    {
        ArgumentNullException.ThrowIfNull(permission);
        var parts = permission.Split('.');
        return parts.Length == 2 && Find(parts[0]) is { } module && module.HasAction(parts[1]);
    }

    public static IEnumerable<string> AllPermissions => All.SelectMany(m => m.Actions.Select(a => m.Code + "." + a));

    [GeneratedRegex("^[a-z][a-z0-9]*(_[a-z0-9]+)*$")]
    public static partial Regex SnakeCase();
}
