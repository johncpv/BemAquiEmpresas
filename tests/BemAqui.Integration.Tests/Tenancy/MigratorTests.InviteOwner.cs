using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Identity.Application;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Tenancy;

/// <summary>FND-10b: link do convite do proprietário pela CLI, sem e-mail; gerar de novo invalida o anterior.</summary>
public partial class MigratorTests
{
    private static string LinkToken(string output) =>
        Uri.UnescapeDataString(output.Split('\n').Select(l => l.Trim()).Single(l => l.StartsWith("https://", StringComparison.Ordinal)).Split("token=")[1]);

    private async Task<InviteState> InviteStateAsync(Guid tenantId, string token)
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenantId))!;
        await using var scope = await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
        return (await scope.ServiceProvider.GetRequiredService<Invites>().GetAsync(token)).State;
    }

    [Fact]
    public async Task Tenant_invite_owner_mostra_um_link_valido_e_o_novo_invalida_o_anterior()
    {
        var tenant = await tenancy.ProvisionAsync("empresa-convite", "Empresa Convite", "convite.localhost");
        var first = new StringWriter();
        var second = new StringWriter();

        (await RunCliAsync(first, "tenant", "invite-owner", "--key", "empresa-convite")).Should().Be(0, first.ToString());
        (await RunCliAsync(second, "tenant", "invite-owner", "--key", "empresa-convite")).Should().Be(0, second.ToString());

        first.ToString().Should().Contain("https://convite.localhost/convite?token=").And.Contain("dono@convite.localhost");
        (await InviteStateAsync(tenant.Id, LinkToken(second.ToString()))).Should().Be(InviteState.Valid);
        (await InviteStateAsync(tenant.Id, LinkToken(first.ToString()))).Should().Be(InviteState.NotFound, "o link novo invalida o anterior");
        (await RunCliAsync(new StringWriter(), "tenant", "invite-owner", "--key", "nao-existe")).Should().Be(1);
    }
}
