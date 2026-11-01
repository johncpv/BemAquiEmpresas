using BemAqui.BuildingBlocks.Web;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Platform.Endpoints;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Security;

/// <summary>
/// D19: a política padrão do app é só "autenticado" (fallback do <c>TenantJwtAuthentication</c>), então endpoint novo sem
/// <c>RequirePermission</c> ficaria aberto a qualquer usuário do tenant. Este teste percorre todos os endpoints do app e
/// reprova quem não tem permissão do catálogo (policy <c>perm:</c>), não é da plataforma (policy <c>platform</c>, super
/// admin, FND-13) nem está numa das listas versionadas abaixo. Mudou uma lista? Explique o motivo na própria linha.
/// A chave é a rota (RawText, sem barra final); o método não entra porque todas as rotas listadas têm o mesmo tratamento
/// em todos os métodos.
/// </summary>
public class EndpointAuthorizationTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    /// <summary>Rotas anônimas legítimas (CLAUDE.md, ADR 0009).</summary>
    private static readonly Dictionary<string, string> Anonymous = new(StringComparer.Ordinal)
    {
        ["/api/v1/auth/login"] = "login",
        ["/api/v1/auth/refresh"] = "renova a sessão pelo cookie de refresh (HttpOnly), sem JWT válido",
        ["/api/v1/auth/logout"] = "encerra a sessão do cookie de refresh",
        ["/api/v1/auth/2fa/verify"] = "2FA do login, pelo token do desafio (5 min)",
        ["/api/v1/auth/2fa/setup"] = "2FA obrigatório no primeiro login, pelo token do desafio",
        ["/api/v1/auth/2fa/confirm"] = "2FA obrigatório no primeiro login, pelo token do desafio",
        ["/api/v1/auth/forgot-password"] = "recuperação de senha (resposta igual para e-mail existente ou não)",
        ["/api/v1/auth/reset-password"] = "redefinição pelo token de uso único",
        ["/api/v1/auth/invites/inspect"] = "convite: mostra a quem é o convite, pelo token",
        ["/api/v1/auth/invites/accept"] = "convite: aceite pelo token",
        ["/api/v1/auth/pin"] = "entrar por PIN, só com o cookie do aparelho registrado válido; sem ele, 404 (ADR 0009 adendo de 21/10/2026, ADR 0026)",
        ["/api/v1/auth/pin/users"] = "nomes de quem tem PIN no aparelho, só com o cookie do aparelho válido; sem ele, 404 (ADR 0009 adendo, ADR 0026)",
        ["/api/v1/kiosk/session"] = "Painel da TV: troca o token de uso único (do fragmento /tv#token, no corpo) pelo cookie __Host-bq_kiosk; Origin e X-CSRF, rate limit kiosk-session, 401 uniforme (ADR 0009 adendo de 21/10/2026, ADR 0030)",
        ["/api/v1/public/tenant-context"] = "nome e logo para a tela de login (ADR 0009)",
        ["/api/v1/public/tenant-logo"] = "imagem do logo da tela de login (complemento do tenant-context)",
        ["/api/v1/platform/auth/login"] = "login do super admin (sessão própria da plataforma)",
        ["/api/v1/platform/auth/set-password"] = "primeira senha do super admin, pelo token",
        ["/api/v1/platform/auth/2fa/setup"] = "2FA do super admin, pelo desafio",
        ["/api/v1/platform/auth/2fa/confirm"] = "2FA do super admin, pelo desafio",
        ["/api/v1/platform/auth/2fa/verify"] = "2FA do super admin, pelo desafio",
        ["/api/v1/platform/auth/logout"] = "encerra a sessão da plataforma",
        // FND-16: única rota anônima de provedor externo (POST e o GET de verificação de quem declara; outro método, 405). Sem usuário,
        // a origem é provada antes de qualquer gravação: HMAC do corpo cru com timestamp ou token na URL (só o hash guardado), em tempo
        // constante; tenant só pelo tenantKey opaco, só no host da plataforma (ou no interno, para quem declara), fora do tenant por host;
        // corpo de até 64 KB, Content-Type do provedor, política de rate limit "webhooks"; recusa uniforme 401 sem corpo.
        // CLAUDE.md ("webhooks, assinatura validada"), ADR 0009 adendo de 21/10/2026, ADR 0031 §1 e §2, ADR 0007 adendo (webhook-secret).
        ["/webhooks/{provider}/{tenantKey}/{pathToken?}"] = "webhooks de provedores externos com origem validada (ADR 0009 adendo de 21/10/2026, ADR 0031)",
        ["/health/live"] = "healthcheck do Docker",
        ["/health/ready"] = "healthcheck do Docker",
        ["/health"] = "healthcheck do Docker (compatibilidade)",
        ["/openapi/{documentName}.json"] = "só em Development (ADR 0009)",
        ["/openapi/{**rest}"] = "fora de Development: responde 404",
        ["/swagger/{**rest}"] = "fora de Development: responde 404",
    };

    /// <summary>Só aparecem num dos ambientes (Development ou não): não contam na checagem de rota que sumiu.</summary>
    private static readonly HashSet<string> EnvironmentDependent = new(StringComparer.Ordinal)
    {
        "/openapi/{documentName}.json", "/openapi/{**rest}", "/swagger/{**rest}",
    };

    /// <summary>
    /// Rotas que valem para qualquer usuário autenticado do tenant: dado da própria pessoa, menu, ajuda filtrada pelos
    /// módulos dela, ou checagem de permissão feita no serviço pelo dono do registro (anexos e histórico).
    /// Não entram aqui rotas que leem ou gravam dado de negócio sem essa checagem.
    /// </summary>
    private static readonly Dictionary<string, string> AuthenticatedOnly = new(StringComparer.Ordinal)
    {
        ["/api/v1/me"] = "perfil da própria pessoa",
        ["/api/v1/me/permissions"] = "permissões efetivas da própria pessoa (monta o menu)",
        ["/api/v1/me/agenda"] = "a pauta da própria pessoa; cada categoria confere a permissão (DSH-04)",
        ["/api/v1/me/preferences"] = "tema e preferências da própria pessoa",
        ["/api/v1/me/2fa"] = "2FA da própria pessoa",
        ["/api/v1/me/2fa/setup"] = "2FA da própria pessoa",
        ["/api/v1/me/2fa/confirm"] = "2FA da própria pessoa",
        ["/api/v1/me/2fa/disable"] = "2FA da própria pessoa (exige senha/código no serviço)",
        ["/api/v1/me/2fa/step-up"] = "2FA de novo da própria pessoa (BNK-01a): confere o código e troca só o access token; rate limit por usuário",
        ["/api/v1/me/pins"] = "PINs da própria pessoa nos aparelhos da fábrica (PRD-03b)",
        ["/api/v1/me/pins/{deviceId:guid}"] = "apaga o PIN da própria pessoa num aparelho (PRD-03b)",
        ["/api/v1/modules"] = "catálogo de módulos com o que está liberado no tenant (menu e tela não liberada)",
        ["/api/v1/notifications"] = "só as notificações da própria pessoa (ListMineAsync)",
        ["/api/v1/notifications/{id:guid}/read"] = "marca como lida só notificação da própria pessoa",
        ["/api/v1/ajuda"] = "índice da ajuda filtrado pelos módulos liberados e pelas permissões da pessoa (HelpAccess)",
        ["/api/v1/ajuda/vistas"] = "ajudas já vistas pela própria pessoa",
        ["/api/v1/ajuda/perguntar"] = "responde só com a ajuda que a pessoa pode ver; limite por usuário",
        ["/api/v1/ajuda/{chave}"] = "ajuda de uma tela; HelpAccess recusa módulo sem permissão",
        ["/api/v1/ajuda/{chave}/vista"] = "marca a ajuda como vista pela própria pessoa",
        ["/api/v1/attachments/{ownerType}/{ownerId:guid}"] = "AttachmentService confere modulo.acao do dono (registro de anexos)",
        ["/api/v1/attachments/{id:guid}/content"] = "AttachmentService confere modulo.acao do dono antes do download",
        ["/api/v1/audit/{entity}/{id}"] = "AuditHistory confere a ação de leitura do módulo dono da entidade",
        ["/hubs/ping"] = "hub de tempo real só com o ping de conexão, sem dado",
        ["/hubs/ping/negotiate"] = "negociação do hub acima",
    };

    /// <summary>
    /// Autoatendimento do funcionário (ADR 0035 §1, D19): rotas de <c>/api/v1/me/hr/*</c> sem <c>perm:</c>, liberadas pela camada 1
    /// (<c>RequireModule("hr")</c>) e pelo filtro de vínculo com funcionário ativo (<c>RequireActiveEmployeeLink</c>). Rota nova entra aqui
    /// com o motivo; sem as duas marcas, reprova.
    /// </summary>
    private static readonly Dictionary<string, string> SelfService = new(StringComparer.Ordinal)
    {
        ["/api/v1/me/hr/link"] = "vínculo da própria pessoa com o funcionário (menu de \"Meus holerites\"), sem dado sensível (RH-02b)",
        ["/api/v1/me/hr/payslips"] = "os próprios holerites das folhas fechadas (RH-02b CA 5)",
        ["/api/v1/me/hr/payslips/{id:guid}/pdf"] = "o PDF do próprio holerite; o de outro funcionário responde 404 (RH-02b CA 5)",
        ["/api/v1/me/hr/time-entries"] = "Meu ponto: marcar (POST, hora do servidor) e Minhas marcações dos últimos 60 dias (GET), só do funcionário da sessão (RH-01a CA 3 e 13)",
        ["/api/v1/me/hr/time-entries/today"] = "Meu ponto: hoje, a jornada e a próxima marcação sugerida (RH-01a CA 2)",
        ["/api/v1/me/hr/time-entries/geo-consent"] = "ciência do aviso da geolocalização do próprio funcionário (RH-01a CA 10)",
        ["/api/v1/me/hr/absence-reasons"] = "a lista única de ausências para o pedido de abono, sem dado pessoal (RH-01b CA 7)",
        ["/api/v1/me/hr/time-adjustments"] = "Pedir ajuste (POST) e Meus pedidos (GET), só do funcionário da sessão; desconsiderar só marcação dele (RH-01b CA 5 e 6)",
        ["/api/v1/me/hr/time-adjustments/{id:guid}/cancel"] = "cancelar o próprio pedido pendente; o de outro funcionário responde 404 (RH-01b CA 5)",
        ["/api/v1/me/hr/time-adjustments/{id:guid}/documents"] = "anexar o documento ao próprio abono; ClamAV e prefixo do tenant (RH-01b CA 8)",
        ["/api/v1/me/hr/time-adjustments/{id:guid}/documents/{attachmentId:guid}"] = "baixar só o documento que o próprio funcionário enviou ao pedido dele (RH-01b CA 8)",
        ["/api/v1/me/hr/timesheets"] = "Meu espelho: os períodos (o em aberto e os fechados) só do funcionário da sessão (RH-01c CA 8)",
        ["/api/v1/me/hr/timesheets/{period}"] = "Meu espelho do período, só do funcionário da sessão; employeeId na query responde 400 (RH-01c CA 8)",
        ["/api/v1/me/hr/timesheets/{period}/ack"] = "\"Li e confirmo\" do próprio espelho fechado, ciência sem valor legal (RH-01c CA 15)",
        ["/api/v1/me/hr/timesheets/{period}/pdf"] = "o próprio espelho fechado em PDF, sem CPF, salário, motivo de saúde nem localização (RH-01c CA 18)",
    };

    private static bool IsSelfService(RouteEndpoint e) =>
        e.Metadata.GetMetadata<SelfServiceMetadata>() is { ModuleCode: "hr" }
        && e.Metadata.GetMetadata<BemAqui.HR.Endpoints.ActiveEmployeeLinkAttribute>() is not null
        && e.Metadata.GetMetadata<RequiresModuleMetadata>()?.ModuleCode == "hr"
        && e.RoutePattern.RawText!.StartsWith("/api/v1/me/hr/", StringComparison.Ordinal)
        && SelfService.ContainsKey(Key(e));

    [Fact]
    public void Rotas_de_autoatendimento_so_com_a_marca_o_filtro_de_vinculo_e_na_lista()
    {
        var endpoints = Endpoints();
        var me = endpoints.Where(e => e.RoutePattern.RawText!.StartsWith("/api/v1/me/hr", StringComparison.Ordinal)).ToList();

        me.Should().NotBeEmpty();
        me.Should().OnlyContain(e => IsSelfService(e) && !HasPermission(e), "rota /api/v1/me/hr/* só pelo módulo e pelo vínculo, sem ação no grupo (ADR 0035)");
        endpoints.Where(e => e.Metadata.GetMetadata<SelfServiceMetadata>() != null).Should()
            .OnlyContain(e => e.RoutePattern.RawText!.StartsWith("/api/v1/me/", StringComparison.Ordinal), "a marca de autoatendimento só vale em /api/v1/me/*");
        // ADR 0035 §4 e §5: holerite, "Minhas marcações" e o resto exigem login completo; só ver hoje e marcar o ponto entram na sessão por PIN.
        me.Where(e => e.Metadata.GetMetadata<PinSessionAllowedAttribute>() != null).Select(e => e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.IEndpointNameMetadata>()?.EndpointName).Should()
            .BeEquivalentTo(["GetMyTimeClock", "RecordMyTimeEntry"], "só o ponto no tablet (adendo de 21/10/2026 à ADR 0026)");
        SelfService.Keys.Except(me.Select(Key)).Should().BeEmpty("rota removida sai da lista");
    }

    private static string Key(RouteEndpoint endpoint)
    {
        var raw = endpoint.RoutePattern.RawText ?? string.Empty;
        return raw.Length > 1 ? raw.TrimEnd('/') : raw;
    }

    private List<RouteEndpoint> Endpoints() =>
        tenancy.Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();

    private static bool HasPermission(RouteEndpoint e) =>
        e.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(a => a.Policy?.StartsWith(ModuleEndpoints.PermissionPolicyPrefix, StringComparison.Ordinal) == true);

    /// <summary>
    /// Hub de negócio (PRD-02): <c>RequireHubAccess</c> põe o módulo no metadado e a policy <c>hub:{modulo}:{acao}|{acao}</c>, com
    /// módulo e ações do catálogo (filtro de endpoint não roda em hub).
    /// </summary>
    private static bool HasHubAccess(RouteEndpoint e)
    {
        var module = e.Metadata.GetMetadata<RequiresModuleMetadata>()?.ModuleCode;
        return module is not null && e.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(a =>
            a.Policy?.StartsWith(BemAqui.Host.Realtime.HubAccess.PolicyPrefix + module + ":", StringComparison.Ordinal) == true
            && a.Policy[(BemAqui.Host.Realtime.HubAccess.PolicyPrefix.Length + module.Length + 1)..].Split('|').All(BemAqui.BuildingBlocks.Modules.ModuleCatalog.IsKnownPermission));
    }

    /// <summary>
    /// Painel da TV (ADR 0030 §2, ADR 0009 adendo de 21/10/2026): só o principal de quiosque (policy <c>kiosk</c>, esquema <c>Kiosk</c>),
    /// em <c>GET /api/v1/kiosk/*</c> e no hub <c>/hubs/kiosk</c>; nenhum outro endpoint usa a policy.
    /// </summary>
    private static bool IsKiosk(RouteEndpoint e) =>
        e.Metadata.GetMetadata<KioskEndpointAttribute>() is not null
        && e.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(a => a.Policy == BemAqui.BuildingBlocks.Security.KioskClaims.Policy)
        && (e.RoutePattern.RawText!.StartsWith(KioskEndpoints.HubPath, StringComparison.Ordinal)
            || (e.RoutePattern.RawText.StartsWith(KioskEndpoints.PathPrefix + "/", StringComparison.Ordinal)
                && e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods.SequenceEqual(["GET"]) == true));

    [Fact]
    public void Policy_do_quiosque_so_nos_endpoints_do_painel()
    {
        var kiosk = Endpoints().Where(e => e.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(a => a.Policy == BemAqui.BuildingBlocks.Security.KioskClaims.Policy)).ToList();

        kiosk.Should().NotBeEmpty();
        kiosk.Should().OnlyContain(e => IsKiosk(e), "só GET /api/v1/kiosk/* e o /hubs/kiosk");
        kiosk.Should().OnlyContain(e => !HasPermission(e), "endpoint do painel não é de usuário");
    }

    /// <summary>Super admin: sessão própria da plataforma (esquema e policy <c>platform</c>), fora do tenant (FND-13).</summary>
    private static bool IsPlatform(RouteEndpoint e) =>
        e.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(a => a.Policy == PlatformSessionAuthenticationHandler.Policy);

    private static bool IsAnonymous(RouteEndpoint e) => e.Metadata.GetMetadata<IAllowAnonymous>() is not null;

    [Fact]
    public void Todo_endpoint_exige_permissao_do_catalogo_ou_esta_numa_lista_versionada()
    {
        var problems = new List<string>();
        foreach (var endpoint in Endpoints())
        {
            var key = Key(endpoint);
            if (IsAnonymous(endpoint))
            {
                if (!Anonymous.ContainsKey(key))
                {
                    problems.Add("anônima fora da lista: " + key + " (" + endpoint.DisplayName + ")");
                }
            }
            else if (!HasPermission(endpoint) && !HasHubAccess(endpoint) && !IsKiosk(endpoint) && !IsPlatform(endpoint) && !IsSelfService(endpoint) && !AuthenticatedOnly.ContainsKey(key))
            {
                problems.Add("só autenticado, sem RequirePermission: " + key + " (" + endpoint.DisplayName + ")");
            }
        }

        problems.Should().BeEmpty("endpoint precisa de RequirePermission (ou entrar numa lista com motivo):\n" + string.Join("\n", problems.Order(StringComparer.Ordinal)));
    }

    /// <summary>
    /// Camadas 1 e 2 em todo endpoint de negócio: a permissão <c>modulo.acao</c> é do catálogo e o endpoint declara o módulo
    /// (é ele que responde <c>module_not_released</c>). A permissão pode ser de outro módulo (ex.: <c>settings.edit</c> na
    /// configuração do módulo, aprovações nos cards do painel). O comportamento HTTP das três camadas é testado uma vez em
    /// <c>AuthTests</c>; módulo novo não precisa repetir, só testa regra própria (ação específica, seed patch, IDOR).
    /// </summary>
    [Fact]
    public void Endpoint_com_permissao_declara_o_modulo_e_a_permissao_e_do_catalogo()
    {
        var problems = new List<string>();
        foreach (var endpoint in Endpoints().Where(HasPermission))
        {
            var module = endpoint.Metadata.GetMetadata<RequiresModuleMetadata>()?.ModuleCode;
            foreach (var permission in endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
                         .Select(a => a.Policy)
                         .Where(p => p?.StartsWith(ModuleEndpoints.PermissionPolicyPrefix, StringComparison.Ordinal) == true)
                         .SelectMany(p => p![ModuleEndpoints.PermissionPolicyPrefix.Length..].Split('|')))
            {
                if (!BemAqui.BuildingBlocks.Modules.ModuleCatalog.IsKnownPermission(permission))
                {
                    problems.Add("permissão fora do catálogo: " + permission + " em " + Key(endpoint));
                }
                else if (module is null)
                {
                    problems.Add("sem RequireModule (" + permission + "): " + Key(endpoint));
                }
            }
        }

        problems.Should().BeEmpty(string.Join("\n", problems.Distinct().Order(StringComparer.Ordinal)));
    }

    [Fact]
    public void Listas_nao_guardam_rota_que_nao_existe_mais()
    {
        var keys = Endpoints().Select(Key).ToHashSet(StringComparer.Ordinal);

        Anonymous.Keys.Concat(AuthenticatedOnly.Keys)
            .Where(k => !EnvironmentDependent.Contains(k) && !keys.Contains(k))
            .Should().BeEmpty("rota removida sai da lista");
    }

    [Fact]
    public void Rota_anonima_nao_e_tambem_so_autenticada()
    {
        Anonymous.Keys.Intersect(AuthenticatedOnly.Keys).Should().BeEmpty();
    }
}
