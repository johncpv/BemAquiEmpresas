using System.Net;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Identity.Contracts;
using BemAqui.Identity.Domain;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Identity;

public partial class ShopfloorPinTests
{
    /// <summary>A mesma lista revisada do teste de arquitetura (<c>PinSessionAllowListTests</c>), conferida nos endpoints do app.</summary>
    private static readonly string[] Allowed =
    [
        "GetShopfloorQueue", "SearchShopfloorOrder", "GetShopfloorStep", "StartShopfloorStep", "PauseShopfloorStep", "ResumeShopfloorStep", "LeaveShopfloorStep",
        "FinishShopfloorStep", "MarkShopfloorChecklist", "ListShopfloorAttachments", "DownloadShopfloorAttachment", "GetShopfloorDevice", "SetShopfloorDeviceTheme",
        "DeleteMyShopfloorPin", "GetQualityQueue", "GetQualityStep", "MarkQualityChecklist", "StartQualityInspection", "ApproveQuality", "RejectQuality",
        "UploadNonConformityAttachment", "GetCurrentSession", "GetMyPermissions", "Logout", "ListHelp", "ListSeenHelp", "GetHelp", "MarkHelpSeen", "AskHelp",
    ];

    private List<RouteEndpoint> Endpoints() =>
        tenancy.Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();

    [Fact]
    public void Endpoints_com_a_marca_da_sessao_por_PIN_sao_so_os_da_lista_revisada()
    {
        var marked = Endpoints().Where(e => e.Metadata.GetMetadata<PinSessionAllowedAttribute>() is not null).ToList();

        marked.Select(e => e.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName).OfType<string>().Distinct().Should().BeEquivalentTo(Allowed);
        marked.Where(e => e.Metadata.GetMetadata<IEndpointNameMetadata>() is null).Should().OnlyContain(e => e.RoutePattern.RawText!.StartsWith("/hubs/production", StringComparison.Ordinal),
            "sem nome, só o ProductionHub (negociação e conexão)");
    }

    /// <summary>
    /// CA 16 (teste parametrizado sobre o app inteiro): a sessão por PIN chama cada endpoint do backoffice (todos os que exigem usuário e
    /// não têm a marca) e recebe 403 <c>pin_session_scope</c>, antes de qualquer regra do endpoint.
    /// </summary>
    [Fact]
    public async Task Sessao_por_PIN_nao_alcanca_nenhum_endpoint_do_backoffice()
    {
        var (device, _) = await DeviceAsync();
        var (userId, _) = await OperatorWithPinAsync(device);
        var (token, rotated, _) = await PinLoginAsync(device, userId);
        var outside = Endpoints()
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null && e.Metadata.GetMetadata<PinSessionAllowedAttribute>() is null)
            .Where(e => e.RoutePattern.RawText!.StartsWith("/api/v1/", StringComparison.Ordinal) && !e.RoutePattern.RawText.StartsWith("/api/v1/platform", StringComparison.Ordinal))
            .ToList();
        outside.Should().HaveCountGreaterThan(100);
        var failures = new List<string>();
        foreach (var endpoint in outside)
        {
            var method = endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods is { Count: > 0 } methods ? methods[0] : "GET";
            var url = string.Concat(endpoint.RoutePattern.PathSegments.Select(s => "/" + string.Concat(s.Parts.Select(p => p switch
            {
                Microsoft.AspNetCore.Routing.Patterns.RoutePatternLiteralPart literal => literal.Content,
                Microsoft.AspNetCore.Routing.Patterns.RoutePatternParameterPart parameter => parameter.ParameterPolicies.Any(x => x.Content == "int") ? "2026" : Guid.Empty.ToString(),
                _ => string.Empty,
            }))));
            var request = Authorized(new HttpMethod(method), url, token, rotated, method is "GET" or "DELETE" ? null : new { });
            var response = await Client().SendAsync(request);
            if (response.StatusCode != HttpStatusCode.Forbidden || Code(await JsonAsync(response)) != PinSessionEndpoints.ScopeCode)
            {
                failures.Add($"{method} {endpoint.RoutePattern.RawText}: {(int)response.StatusCode}");
            }
        }

        failures.Should().BeEmpty("a sessão por PIN só alcança a lista permitida (ADR 0026 §4)");
        (await Client().SendAsync(Authorized(HttpMethod.Get, "/api/v1/shopfloor/queue", token, rotated))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Client().SendAsync(Authorized(HttpMethod.Get, "/api/v1/me/permissions", token, rotated))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Revogar_o_aparelho_derruba_a_sessao_por_PIN_na_proxima_request_e_avisa_o_Proprietario()
    {
        var (device, deviceId) = await DeviceAsync("Tablet revogado");
        var (userId, _) = await OperatorWithPinAsync(device);
        var (token, rotated, refresh) = await PinLoginAsync(device, userId);
        (await Client().SendAsync(Authorized(HttpMethod.Get, "/api/v1/shopfloor/queue", token, rotated))).StatusCode.Should().Be(HttpStatusCode.OK);
        var (_, admin) = await UserAsync("settings:view,edit", employee: false, name: "Paula Lima");
        var (adminToken, _) = await FullLoginAsync(admin, ephemeral: false);

        var revoked = await Client().SendAsync(Authorized(HttpMethod.Post, $"/api/v1/settings/security/shopfloor-devices/{deviceId}/revoke", adminToken));

        revoked.StatusCode.Should().Be(HttpStatusCode.OK);
        var next = await Client().SendAsync(Authorized(HttpMethod.Get, "/api/v1/shopfloor/queue", token, rotated));
        next.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "o aparelho é conferido a cada request, não só no próximo login (CA 10)");
        Code(await JsonAsync(next)).Should().Be("device_not_registered");
        (await Client().SendAsync(RefreshRequest(refresh, rotated))).IsSuccessStatusCode.Should().BeFalse();
        (await Client().SendAsync(PinRequest(rotated, userId, GoodPin))).StatusCode.Should().Be(HttpStatusCode.NotFound, "o PIN deixa de funcionar (CA 9)");
        (await WithDbAsync(db => db.UserPins.CountAsync(p => p.DeviceId == deviceId))).Should().Be(0);
        (await NotificationsAsync("Aparelho Tablet revogado revogado por Paula Lima")).Should().NotBeEmpty();

        var (_, supervisor) = await UserAsync("production:view,approve", employee: false);
        var (other, otherId) = await DeviceAsync("Outro tablet");
        var (supervisorToken, _) = await FullLoginAsync(supervisor, ephemeral: false);
        (await Client().SendAsync(Authorized(HttpMethod.Post, $"/api/v1/settings/security/shopfloor-devices/{otherId}/revoke", supervisorToken))).StatusCode
            .Should().Be(HttpStatusCode.OK, "production.approve também revoga");
        (await Client().SendAsync(PinUsersRequest(other))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Apagar_meu_PIN_no_aparelho_encerra_a_sessao_e_tira_o_nome_da_lista()
    {
        var (device, _) = await DeviceAsync();
        var (userId, _) = await OperatorWithPinAsync(device);
        var (token, rotated, _) = await PinLoginAsync(device, userId);

        (await Client().SendAsync(Authorized(HttpMethod.Delete, "/api/v1/shopfloor/device/pin", token, rotated))).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await Client().SendAsync(Authorized(HttpMethod.Get, "/api/v1/shopfloor/queue", token, rotated))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await JsonAsync(await Client().SendAsync(PinUsersRequest(rotated)))).GetProperty("users").EnumerateArray().Should().NotContain(u => u.GetProperty("userId").GetGuid() == userId);
    }

    [Fact]
    public async Task Perfil_com_acao_reservada_nao_cria_PIN_e_quem_ganhou_uma_perde_o_PIN_no_proximo_login()
    {
        var (device, deviceId) = await DeviceAsync();
        var (_, reserved) = await UserAsync(Operator + "|hr:view,view_sensitive");
        var (reservedToken, _) = await FullLoginAsync(reserved, device: device);
        var refused = await Client().SendAsync(Authorized(HttpMethod.Post, "/api/v1/shopfloor/device/pin", reservedToken, device, new { pin = GoodPin, confirmation = GoodPin }));
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await JsonAsync(refused)).GetProperty("detail").GetString().Should().Be("Seu perfil não pode usar PIN.");

        var (userId, _) = await OperatorWithPinAsync(device);
        await GrantAsync(userId, "settings", "edit");

        var changed = await Client().SendAsync(PinRequest(device, userId, GoodPin));

        changed.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var problem = await JsonAsync(changed);
        Code(problem).Should().Be("auth.pin_profile_changed");
        problem.GetProperty("detail").GetString().Should().Be("Seu perfil mudou e não pode usar PIN. Entre com e-mail e senha.");
        (await WithDbAsync(db => db.UserPins.CountAsync(p => p.UserId == userId && p.DeviceId == deviceId))).Should().Be(0, "o PIN é apagado (CA 17)");
    }

    [Fact]
    public async Task Inspetor_com_a_acao_da_qualidade_entra_por_PIN_e_ve_a_fila_mas_nao_conclui_a_OS()
    {
        var (device, _) = await DeviceAsync();
        var (userId, _) = await OperatorWithPinAsync(device, Operator + "|production:view,approve_quality");

        var (token, rotated, _) = await PinLoginAsync(device, userId);

        (await Client().SendAsync(Authorized(HttpMethod.Get, "/api/v1/production/quality/queue", token, rotated))).StatusCode.Should().Be(HttpStatusCode.OK, "D6 (CA 18)");
        var complete = await Client().SendAsync(Authorized(HttpMethod.Post, $"/api/v1/production/production-orders/{Guid.NewGuid()}/complete", token, rotated, new { }));
        complete.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(complete)).Should().Be("pin_session_scope", "a conclusão da OS continua com production.approve e login completo");
    }

    [Fact]
    public async Task Perder_shopfloor_create_derruba_a_sessao_por_PIN_no_proximo_request()
    {
        var (device, _) = await DeviceAsync();
        var (userId, _) = await OperatorWithPinAsync(device);
        var (token, rotated, _) = await PinLoginAsync(device, userId);
        await WithDbAsync(async db =>
        {
            await db.UserGroups.Where(g => g.UserId == userId).ExecuteDeleteAsync();
        });
        await using (var scope = await ScopeAsync())
        {
            await scope.ServiceProvider.GetRequiredService<IPermissionService>().BumpAsync([userId]);
        }

        var next = await Client().SendAsync(Authorized(HttpMethod.Get, "/api/v1/shopfloor/queue", token, rotated));

        next.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "CA 15 e 20");
        Code(await JsonAsync(next)).Should().Be("pin_session_ended");
    }

    [Fact]
    public async Task Usuario_desativado_ou_funcionario_afastado_e_recusado()
    {
        var (device, _) = await DeviceAsync();
        var (userId, _) = await OperatorWithPinAsync(device);
        var (token, rotated, _) = await PinLoginAsync(device, userId);
        await using (var scope = await ScopeAsync())
        {
            (await scope.ServiceProvider.GetRequiredService<BemAqui.Identity.Application.UserLifecycle>().DeactivateAsync(userId, null)).IsSuccess.Should().BeTrue();
        }

        (await Client().SendAsync(Authorized(HttpMethod.Get, "/api/v1/shopfloor/queue", token, rotated))).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "security stamp a cada request");
        (await Client().SendAsync(PinRequest(rotated, userId, GoodPin))).IsSuccessStatusCode.Should().BeFalse();

        var (other, _) = await OperatorWithPinAsync(rotated);
        var employeeId = await WithDbAsync(db => db.Users.Where(u => u.Id == other).Select(u => u.EmployeeId!.Value).SingleAsync());
        await WithDbAsync(async db =>
        {
            db.EmployeeHistory.Add(new BemAqui.HR.Domain.EmployeeHistoryEntry
            {
                EmployeeId = employeeId, Kind = BemAqui.HR.Domain.EmployeeHistoryKind.Leave, EffectiveDate = Today, LeaveEndDate = Today.AddDays(3),
                LeaveType = BemAqui.HR.Domain.EmployeeLeaveType.Illness, NewValue = "Afastado", Reason = "Atestado", CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        });

        var onLeave = await Client().SendAsync(PinRequest(rotated, other, GoodPin));
        onLeave.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(onLeave)).Should().Be("auth.pin_employee_inactive", "afastado não entra por PIN (CA 20)");
    }

    [Fact]
    public async Task Trocar_o_vinculo_com_o_funcionario_apaga_o_PIN_e_revoga_as_sessoes_por_PIN()
    {
        var (device, _) = await DeviceAsync();
        var (userId, _) = await OperatorWithPinAsync(device);
        var (token, rotated, _) = await PinLoginAsync(device, userId);
        var employeeId = await WithDbAsync(db => db.Users.Where(u => u.Id == userId).Select(u => u.EmployeeId!.Value).SingleAsync());

        await using (var scope = await ScopeAsync())
        {
            (await scope.ServiceProvider.GetRequiredService<IUserEmployeeLink>().UnlinkAsync(employeeId)).IsSuccess.Should().BeTrue();
        }

        (await WithDbAsync(db => db.UserPins.CountAsync(p => p.UserId == userId))).Should().Be(0, "CA 19");
        (await WithDbAsync(db => db.RefreshTokens.CountAsync(t => t.UserId == userId && t.SessionKind == SessionKind.Pin && t.RevokedAt == null))).Should().Be(0);
        (await Client().SendAsync(Authorized(HttpMethod.Get, "/api/v1/shopfloor/queue", token, rotated))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await WithDbAsync(db => db.AuditEntries.CountAsync(a => a.EntityId == userId.ToString() && a.Action == "PinErased"))).Should().Be(1);
    }

    [Fact]
    public async Task Meus_PINs_listam_os_aparelhos_e_apagam_um()
    {
        var (device, deviceId) = await DeviceAsync("Tablet do perfil");
        var (userId, email) = await OperatorWithPinAsync(device);
        var (token, _) = await FullLoginAsync(email, ephemeral: false);

        var mine = await JsonAsync(await Client().SendAsync(Authorized(HttpMethod.Get, "/api/v1/me/pins", token)));
        mine.EnumerateArray().Should().ContainSingle(p => p.GetProperty("deviceId").GetGuid() == deviceId && p.GetProperty("deviceName").GetString() == "Tablet do perfil");

        (await Client().SendAsync(Authorized(HttpMethod.Delete, $"/api/v1/me/pins/{deviceId}", token))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await WithDbAsync(db => db.UserPins.CountAsync(p => p.UserId == userId))).Should().Be(0);
    }

    private async Task GrantAsync(Guid userId, string module, string action)
    {
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<BemAqui.Host.Persistence.TenantDbContext>();
        var group = new Group { Name = "Ganhou " + Guid.NewGuid().ToString("N")[..8], Description = "Teste" };
        db.Groups.Add(group);
        db.GroupModules.Add(new GroupModule { GroupId = group.Id, ModuleCode = module, Enabled = true });
        db.GroupPermissions.Add(new GroupGrant { GroupId = group.Id, ModuleCode = module, Action = action });
        db.UserGroups.Add(new UserGroup { UserId = userId, GroupId = group.Id });
        await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<IPermissionService>().BumpAsync([userId]);
    }
}