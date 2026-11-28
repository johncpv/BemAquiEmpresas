using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Finance.Application;
using BemAqui.Finance.Contracts;
using BemAqui.Identity.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Commercial;

/// <summary>
/// FIN-10a CA 18: o aditivo e a pauta do Comercial continuam "só o Proprietário" nos três modos da autoaprovação da OP; "Qualquer
/// aprovador" nunca libera quem não é do Proprietário a aprovar o próprio aditivo.
/// </summary>
public partial class QuoteTests
{
    private const string AgendaPath = "/api/v1/me/agenda?categories=projects.amendments_pending";

    private Task<int> SetModeAsync(SelfApprovalMode mode) => WithDbAsync(async db =>
    {
        var parameter = await db.FinanceParameters.SingleAsync(p => p.Key == FinanceParametersService.OwnerCanApproveOwn);
        parameter.Value = (int)mode;
        return await db.SaveChangesAsync();
    });

    /// <summary>Fora do Proprietário, num grupo próprio que registra e aprova aditivos (projetos ver, editar e aprovar).</summary>
    private async Task<(Guid Id, HttpClient Client)> AmendmentApproverOutsideOwnerAsync()
    {
        var (id, client) = await ActorAsync(null);
        await WithDbAsync(async db =>
        {
            var group = new Group { Name = "Projetos " + Guid.NewGuid().ToString("N")[..6], Description = "FIN-10a" };
            db.Groups.Add(group);
            db.GroupModules.Add(new GroupModule { GroupId = group.Id, ModuleCode = "projects", Enabled = true });
            foreach (var action in new[] { "view", "edit", "approve" })
            {
                db.GroupPermissions.Add(new GroupGrant { GroupId = group.Id, ModuleCode = "projects", Action = action });
            }

            db.UserGroups.Add(new UserGroup { UserId = id, GroupId = group.Id });
            return await db.SaveChangesAsync();
        });
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        await using var scope = await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
        await scope.ServiceProvider.GetRequiredService<IPermissionService>().BumpAsync([id]);
        return (id, client);
    }

    private async Task<(string Path, Guid Id, JsonElement Amendment)> SubmittedAmendmentAsync(HttpClient owner, HttpClient author)
    {
        var (id, quote, _) = await SentQuoteAsync(owner, canary: false);
        var (contractId, _) = await ApproveAsync(owner, id, quote, Today);
        var path = $"/api/v1/contracts/{contractId}/amendments";
        var created = await author.SendAsync(Post(path, new { description = "Mais uma parada", valueDelta = 1_000m, daysDelta = 5, milestones = new[] { Milestone("Sinal do aditivo", 100m) }, reductions = (object[]?)null, budgetDeltas = (object?)null, version = (uint?)null }));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var amendment = await JsonAsync(created);
        var amendmentId = amendment.GetProperty("id").GetGuid();
        var submitted = await author.PostAsJsonAsync($"{path}/{amendmentId}/submit", new { version = Version(amendment) });
        submitted.StatusCode.Should().Be(HttpStatusCode.OK, await submitted.Content.ReadAsStringAsync());
        return (path, amendmentId, await JsonAsync(submitted));
    }

    private static async Task<List<string>> AgendaLinksAsync(HttpClient client)
    {
        var response = await client.GetAsync(AgendaPath);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var view = await JsonAsync(response);
        return [.. view.GetProperty("items").EnumerateArray().Where(i => i.GetProperty("category").GetString() == "projects.amendments_pending")
            .SelectMany(i => i.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("link").GetString()!))];
    }

    [Fact]
    public async Task Aditivo_e_pauta_continuam_so_Proprietario_nos_tres_modos()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        await ActorAsync(Group.OwnerSeedKey); // Proprietário com mais de um membro
        var (_, outsider) = await AmendmentApproverOutsideOwnerAsync();
        var (ownerPath, ownerAmendmentId, ownerAmendment) = await SubmittedAmendmentAsync(owner, owner);
        var (outsiderPath, outsiderAmendmentId, outsiderAmendment) = await SubmittedAmendmentAsync(owner, outsider);
        try
        {
            foreach (var mode in new[] { SelfApprovalMode.Never, SelfApprovalMode.OwnerOnly, SelfApprovalMode.AnyApprover })
            {
                await SetModeAsync(mode);
                (await AgendaLinksAsync(owner)).Any(l => l.EndsWith(ownerAmendmentId.ToString(), StringComparison.Ordinal))
                    .Should().Be(mode != SelfApprovalMode.Never, $"no modo {mode}, o Proprietário (com outros membros) vê o próprio aditivo só se o modo deixa");
                (await AgendaLinksAsync(outsider)).Should().NotContain(l => l.EndsWith(outsiderAmendmentId.ToString(), StringComparison.Ordinal), $"modo {mode}: quem não é do Proprietário nunca");
                var refused = await outsider.PostAsJsonAsync($"{outsiderPath}/{outsiderAmendmentId}/approve", new { version = Version(outsiderAmendment) });
                refused.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"modo {mode}");
                (await refused.Content.ReadAsStringAsync()).Should().Contain("Quem registrou o aditivo não pode aprová-lo.");
                if (mode == SelfApprovalMode.Never)
                {
                    (await owner.PostAsJsonAsync($"{ownerPath}/{ownerAmendmentId}/approve", new { version = Version(ownerAmendment) })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
                }
            }

            // "Qualquer aprovador" vale para o Proprietário como "Só o Proprietário": aprova o próprio, com o selo.
            var approved = await owner.PostAsJsonAsync($"{ownerPath}/{ownerAmendmentId}/approve", new { version = Version(ownerAmendment) });
            approved.StatusCode.Should().Be(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());
            (await JsonAsync(approved)).GetProperty("selfApproved").GetBoolean().Should().BeTrue();
        }
        finally
        {
            await SetModeAsync(SelfApprovalMode.Never);
        }
    }
}
