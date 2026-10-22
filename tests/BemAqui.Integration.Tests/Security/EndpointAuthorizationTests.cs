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
        ["/api/v1/public/tenant-context"] = "nome e logo para a tela de login (ADR 0009)",
        ["/api/v1/public/tenant-logo"] = "imagem do logo da tela de login (complemento do tenant-context)",
        ["/api/v1/platform/auth/login"] = "login do super admin (sessão própria da plataforma)",
        ["/api/v1/platform/auth/set-password"] = "primeira senha do super admin, pelo token",
        ["/api/v1/platform/auth/2fa/setup"] = "2FA do super admin, pelo desafio",
        ["/api/v1/platform/auth/2fa/confirm"] = "2FA do super admin, pelo desafio",
        ["/api/v1/platform/auth/2fa/verify"] = "2FA do super admin, pelo desafio",
        ["/api/v1/platform/auth/logout"] = "encerra a sessão da plataforma",
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
        ["/api/v1/me/preferences"] = "tema e preferências da própria pessoa",
        ["/api/v1/me/2fa"] = "2FA da própria pessoa",
        ["/api/v1/me/2fa/setup"] = "2FA da própria pessoa",
        ["/api/v1/me/2fa/confirm"] = "2FA da própria pessoa",
        ["/api/v1/me/2fa/disable"] = "2FA da própria pessoa (exige senha/código no serviço)",
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

    private static string Key(RouteEndpoint endpoint)
    {
        var raw = endpoint.RoutePattern.RawText ?? string.Empty;
        return raw.Length > 1 ? raw.TrimEnd('/') : raw;
    }

    private List<RouteEndpoint> Endpoints() =>
        tenancy.Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();

    private static bool HasPermission(RouteEndpoint e) =>
        e.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(a => a.Policy?.StartsWith(ModuleEndpoints.PermissionPolicyPrefix, StringComparison.Ordinal) == true);

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
            else if (!HasPermission(endpoint) && !IsPlatform(endpoint) && !AuthenticatedOnly.ContainsKey(key))
            {
                problems.Add("só autenticado, sem RequirePermission: " + key + " (" + endpoint.DisplayName + ")");
            }
        }

        problems.Should().BeEmpty("endpoint precisa de RequirePermission (ou entrar numa lista com motivo):\n" + string.Join("\n", problems.Order(StringComparer.Ordinal)));
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
