using System.Net;
using System.Text.Json;
using BemAqui.Identity.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Production;

/// <summary>
/// DSH-04 CA 2 e 13 na Produção: "Qualidade para decidir" segue a fila "aguardando" da PRD-05 (quem apontou etapa da tentativa não vê,
/// salvo a "Autoinspeção") e some depois da decisão. O banco desta classe tem dados de outros testes: as conferências são por contagem
/// relativa entre dois usuários criados aqui, não pelas 5 primeiras linhas.
/// </summary>
public partial class FabricationTests
{
    private const string QualityCategory = "production.quality_pending";

    private static async Task<(int Count, string Why)> QualityAgendaAsync(HttpClient client)
    {
        var view = await OkAsync(await client.GetAsync("/api/v1/me/agenda?categories=" + QualityCategory));
        view.GetProperty("failures").GetArrayLength().Should().Be(0);
        var item = view.GetProperty("items").EnumerateArray().Where(i => i.GetProperty("category").GetString() == QualityCategory).Select(i => (JsonElement?)i).FirstOrDefault();
        return item is { } found ? (found.GetProperty("count").GetInt32(), found.GetProperty("why").GetString()!) : (0, string.Empty);
    }

    [Fact]
    public async Task Pauta_qualidade_segrega_quem_apontou_mostra_autoinspecao_e_some_depois_de_decidir()
    {
        var (_, center, order) = await QualityDatasetAsync();
        var (inspectorId, _, inspector, _) = await OperatorAsync(center, group: "production_supervision");
        await WorkUntilQualityAsync(inspector, order, 1, 2);
        var q = await StepIdAsync(order, 3);
        var (_, other) = await ActorAsync("production:view,approve_quality");

        var (otherCount, _) = await QualityAgendaAsync(other);
        var (inspectorCount, _) = await QualityAgendaAsync(inspector);
        otherCount.Should().Be(inspectorCount + 1, "o inspetor que apontou etapa da tentativa não vê esta OS; o outro aprovador vê");
        var (_, _, operatorClient, _) = await OperatorAsync(center);
        (await QualityAgendaAsync(operatorClient)).Count.Should().Be(0, "o Operador não tem production.approve_quality");

        // Sem outro aprovador elegível (o mesmo cenário do teste da PRD-05): a OS aparece para o inspetor com a marca "Autoinspeção".
        var others = await WithDbAsync(db => db.Users.Where(u => u.Id != inspectorId && u.Status == UserStatus.Active).Select(u => u.Id).ToListAsync());
        var ownerGroup = await WithDbAsync(db => db.Groups.Where(g => g.SeedKey == Group.OwnerSeedKey).Select(g => g.Id).SingleAsync());
        await WithDbAsync(db => db.Users.Where(u => others.Contains(u.Id)).ExecuteUpdateAsync(s => s.SetProperty(u => u.Status, UserStatus.Inactive)));
        await WithDbAsync(async db => { db.UserGroups.Add(new UserGroup { UserId = inspectorId, GroupId = ownerGroup }); return await db.SaveChangesAsync(); });
        try
        {
            await using (var scope = await ScopeAsync())
            {
                await scope.ServiceProvider.GetRequiredService<BemAqui.BuildingBlocks.Identity.IPermissionService>().BumpAsync([inspectorId]);
            }

            var (selfCount, why) = await QualityAgendaAsync(inspector);
            selfCount.Should().Be(inspectorCount + 1);
            why.Should().Contain("1 seria Autoinspeção");

            await MarkQualityAsync(inspector, q);
            (await ApproveQualityAsync(inspector, q, await OrderVersionAsync(order))).StatusCode.Should().Be(HttpStatusCode.OK);
            (await QualityAgendaAsync(inspector)).Count.Should().Be(inspectorCount, "CA 6: decidida, a OS sai da pauta");
        }
        finally
        {
            await WithDbAsync(db => db.Users.Where(u => others.Contains(u.Id)).ExecuteUpdateAsync(s => s.SetProperty(u => u.Status, UserStatus.Active)));
            await WithDbAsync(db => db.UserGroups.Where(ug => ug.UserId == inspectorId && ug.GroupId == ownerGroup).ExecuteDeleteAsync());
        }
    }
}
