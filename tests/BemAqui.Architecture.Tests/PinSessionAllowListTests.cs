using System.Text.RegularExpressions;
using FluentAssertions;

namespace BemAqui.Architecture.Tests;

/// <summary>
/// ADR 0026 §4 (PRD-03b CA 16): a sessão por PIN só alcança os endpoints marcados com <c>[PinSessionAllowed]</c>
/// (<c>.AllowPinSession()</c>), e a marca só entra com revisão. Este teste lê o código e compara cada uso com a lista revisada abaixo:
/// endpoint novo marcado sem entrar aqui reprova, e endpoint da lista que perdeu a marca também. Mudou a lista? Explique na linha.
/// </summary>
public static partial class PinSessionAllowList
{
    /// <summary>Lista revisada (nome do endpoint, ou <c>hub:</c> + classe do hub) e o motivo de a sessão por PIN poder usar.</summary>
    public static readonly IReadOnlyDictionary<string, string> Reviewed = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // App do operador (/api/v1/shopfloor/*, PRD-03a): fila, etapa, apontar, checklist, buscar OS e documentos pela fila.
        ["GetShopfloorQueue"] = "fila do operador",
        ["SearchShopfloorOrder"] = "buscar OS pelo número",
        ["GetShopfloorStep"] = "etapa",
        ["StartShopfloorStep"] = "iniciar",
        ["PauseShopfloorStep"] = "pausar",
        ["ResumeShopfloorStep"] = "retomar",
        ["LeaveShopfloorStep"] = "sair da etapa",
        ["FinishShopfloorStep"] = "finalizar",
        ["MarkShopfloorChecklist"] = "checklist",
        ["ListShopfloorAttachments"] = "documentos da OS pelo endpoint que confere a fila do operador",
        ["DownloadShopfloorAttachment"] = "documentos da OS pelo endpoint que confere a fila do operador",
        // EST-03 CA 5 e 6 (ADR 0026 §4): material da etapa e "Pedir material"; o pedido só cria a requisição Requested, nunca baixa.
        ["GetShopfloorStepMaterial"] = "itens da BOM e pedidos da etapa, sem custo",
        ["RequestShopfloorMaterial"] = "pedir material ao almoxarifado (shopfloor.create), sem baixa",
        // Aparelho no /fab (PRD-03b): ver o aparelho, tema por aparelho (CA 11) e "Apagar meu PIN deste aparelho" (CA 9).
        ["GetShopfloorDevice"] = "o /fab sabe se o aparelho está registrado e qual o tema",
        ["SetShopfloorDeviceTheme"] = "tema do /fab no aparelho (ponto 95)",
        ["DeleteMyShopfloorPin"] = "o próprio usuário apaga o próprio PIN no aparelho",
        // Qualidade no tablet (PRD-05, ADR 0026 adendo de 22/10/2026): só com production.approve_quality.
        ["GetQualityQueue"] = "fila de qualidade",
        ["GetQualityStep"] = "inspeção",
        ["MarkQualityChecklist"] = "checklist da inspeção",
        ["StartQualityInspection"] = "iniciar a inspeção",
        ["ApproveQuality"] = "aprovar a qualidade (production.approve_quality, D6)",
        ["RejectQuality"] = "reprovar a qualidade e abrir a NC (production.approve_quality, D6)",
        ["UploadNonConformityAttachment"] = "foto da NC na reprovação",
        // "Meu ponto" no tablet (RH-01a, adendo de 21/10/2026 à ADR 0026): só ver hoje e marcar; marcar encerra a sessão por PIN.
        ["GetMyTimeClock"] = "Meu ponto: hoje e a próxima marcação sugerida, do funcionário da sessão",
        ["RecordMyTimeEntry"] = "Meu ponto: marcar com a hora do servidor; encerra a sessão por PIN no servidor",
        // Sessão, permissões, saída e ajuda contextual.
        ["GetCurrentSession"] = "/api/v1/me",
        ["GetMyPermissions"] = "/api/v1/me/permissions (menu e <Can>)",
        ["Logout"] = "/api/v1/auth/logout",
        ["ListHelp"] = "ajuda contextual",
        ["ListSeenHelp"] = "ajuda contextual",
        ["GetHelp"] = "ajuda contextual",
        ["MarkHelpSeen"] = "ajuda contextual",
        ["AskHelp"] = "Pergunte ao BemAqui, só com os textos da ajuda",
        // Tempo real: grupo do chão de fábrica, com shopfloor.view (o hub põe a sessão por PIN só nesse grupo).
        ["hub:ProductionHub"] = "ProductionHub, grupo do chão de fábrica",
    };

    [GeneratedRegex("""\.AllowPinSession\(\)""")]
    private static partial Regex Mark();

    [GeneratedRegex("""\.WithName\("([^"]+)"\)""")]
    private static partial Regex Name();

    [GeneratedRegex("""MapHub<[\w.]*?(\w+)>""")]
    private static partial Regex Hub();

    /// <summary>Chaves dos usos de <c>.AllowPinSession()</c>: o <c>WithName</c> da mesma instrução, ou o hub do <c>MapHub</c>.</summary>
    public static IReadOnlyList<string> Uses(IEnumerable<(string File, string Content)> sources) =>
        sources.SelectMany(s => Mark().Matches(s.Content).Select(m => Key(s.File, s.Content, m.Index))).ToList();

    private static string Key(string file, string content, int index)
    {
        var start = content.LastIndexOf(';', index) + 1;
        var end = content.IndexOf(';', index);
        var statement = content[start..(end < 0 ? content.Length : end)];
        if (Name().Match(statement) is { Success: true } name)
        {
            return name.Groups[1].Value;
        }

        return Hub().Match(statement) is { Success: true } hub ? "hub:" + hub.Groups[1].Value : file + ": AllowPinSession() sem WithName";
    }
}

public class PinSessionAllowListTests
{
    [Fact]
    public void So_os_endpoints_revisados_tem_a_marca_da_sessao_por_PIN()
    {
        var uses = PinSessionAllowList.Uses(PermissionRules.SolutionSources());

        uses.Should().OnlyHaveUniqueItems();
        uses.Except(PinSessionAllowList.Reviewed.Keys).Should().BeEmpty("endpoint novo com [PinSessionAllowed] precisa de revisão e entrar na lista");
        PinSessionAllowList.Reviewed.Keys.Except(uses).Should().BeEmpty("endpoint da lista revisada perdeu a marca ou foi removido");
    }

    [Fact]
    public void Registrar_aparelho_criar_PIN_e_o_backoffice_nunca_entram_na_lista()
    {
        PinSessionAllowList.Reviewed.Keys.Should().NotContain(["RegisterShopfloorDevice", "CreateShopfloorPin", "ListMyPins", "RevokeShopfloorDevice", "ListShopfloorDevices"]);
        PinSessionAllowList.Reviewed.Keys.Should().NotContain(k => k.Contains("Order", StringComparison.Ordinal) && !k.Contains("Shopfloor", StringComparison.Ordinal),
            "a conclusão da OS e o resto da produção exigem login completo (PRD-06a1)");
    }

    [Fact]
    public void Regra_acusa_endpoint_marcado_fora_da_lista_e_entende_o_hub()
    {
        const string source = """
            api.MapGet("/x", () => 1).RequirePermission("finance.view").AllowPinSession().WithName("ListPayables");
            app.MapHub<BemAqui.Host.Realtime.ProductionHub>("/hubs/production").RequireHubAccess("production", "shopfloor.view").AllowPinSession();
            """;

        PinSessionAllowList.Uses([("X.cs", source)]).Should().Equal("ListPayables", "hub:ProductionHub");
        PinSessionAllowList.Uses([("X.cs", source)]).Except(PinSessionAllowList.Reviewed.Keys).Should().Equal("ListPayables");
    }
}