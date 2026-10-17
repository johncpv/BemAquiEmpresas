using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace BemAqui.Host.Security;

/// <summary>
/// Hub mínimo (FND-02): prova que a conexão exige token do próprio tenant e host. O tenant vem da claim
/// <c>tid</c>, já conferida contra o host na conexão; os grupos são sempre <c>{tenantId}:{painel}</c>, montados
/// no servidor (o cliente nunca escolhe o grupo).
/// </summary>
[Authorize]
public sealed class PingHub : Hub
{
    public const string Path = "/hubs/ping";

    public static string Group(Guid tenantId, string panel) => $"{tenantId:N}:{panel}";

    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, Group(TenantId(), "all"));
        await base.OnConnectedAsync();
    }

    public string Ping() => TenantId().ToString();

    private Guid TenantId() => Guid.Parse(Context.User!.FindFirstValue(TenantJwtAuthentication.TenantClaim)!);
}
