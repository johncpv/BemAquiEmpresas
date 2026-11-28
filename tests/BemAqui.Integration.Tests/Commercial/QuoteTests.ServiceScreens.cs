using System.Net;
using System.Text.Json;
using BemAqui.Identity.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.Commercial;

/// <summary>EXE-01b: prévia do efeito nas parcelas por passo, pauta "Aceite do cliente pendente" e o filtro "Tipo" da lista.</summary>
public partial class QuoteTests
{
    private static async Task<JsonElement> PreviewAsync(HttpClient client, Guid projectId, string step)
    {
        var response = await client.GetAsync($"/api/v1/projects/{projectId}/milestone-preview?step={step}&date={Iso(Today)}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await JsonAsync(response);
    }

    private static string[] Descriptions(JsonElement preview) => [.. preview.GetProperty("lines").EnumerateArray().Select(l => l.GetProperty("description").GetString()!)];

    private static async Task<JsonElement[]> AcceptanceAgendaAsync(HttpClient client)
    {
        var agenda = await JsonAsync(await client.GetAsync("/api/v1/me/agenda?categories=projects.acceptance_pending"));
        return [.. agenda.GetProperty("items").EnumerateArray().Where(i => i.GetProperty("category").GetString() == "projects.acceptance_pending")];
    }

    private static bool Lists(JsonElement[] items, string number) =>
        items.SelectMany(i => i.GetProperty("entries").EnumerateArray()).Any(e => e.GetProperty("title").GetString()!.Contains(number, StringComparison.Ordinal));

    [Fact]
    public async Task Previa_das_parcelas_por_passo_vem_do_servidor()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        await ServicesAsync(true);
        try
        {
            var (id, quote) = await ServiceQuoteAsync(owner, ServiceMilestones);
            var (_, projectId) = await ApproveAsync(owner, id, quote, Today);

            // Iniciar não libera nada; passo inválido, 422.
            var start = await PreviewAsync(owner, projectId, "start");
            start.GetProperty("lines").GetArrayLength().Should().Be(0);
            start.GetProperty("hasAcceptanceMilestone").GetBoolean().Should().BeTrue();
            (await owner.GetAsync($"/api/v1/projects/{projectId}/milestone-preview?step=outro")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

            // Entrega: libera a parcela de entrega (valor e vencimento com finance.view) e diz que a de aceite continua aguardando.
            var delivery = await PreviewAsync(owner, projectId, "delivery");
            Descriptions(delivery).Should().Equal("Entrega");
            var line = delivery.GetProperty("lines")[0];
            line.GetProperty("amount").GetDecimal().Should().Be(3_000m);
            line.GetProperty("dueDate").GetString().Should().Be(Iso(Today.AddDays(5)));
            delivery.GetProperty("total").GetDecimal().Should().Be(3_000m);
            delivery.GetProperty("stillWaiting").EnumerateArray().Select(s => s.GetString()).Should().Equal("Aceite");

            // Sem finance.view: só a descrição do marco.
            var (_, viewer) = await ActorAsync("commercial");
            var masked = await PreviewAsync(viewer, projectId, "delivery");
            masked.GetProperty("amountsVisible").GetBoolean().Should().BeFalse();
            masked.GetProperty("lines")[0].GetProperty("amount").ValueKind.Should().Be(JsonValueKind.Null);
            masked.GetProperty("total").ValueKind.Should().Be(JsonValueKind.Null);

            // Entregue: o aceite libera a parcela de aceite; depois do aceite, o desfazer devolve a mesma e não está travado.
            var project = await ProjectAsync(owner, projectId);
            (await owner.SendAsync(Post($"/api/v1/projects/{projectId}/delivery", new { date = Iso(Today), version = Version(project), expectedStatus = "Planned" })))
                .StatusCode.Should().Be(HttpStatusCode.OK);
            await DrainAsync();
            Descriptions(await PreviewAsync(owner, projectId, "acceptance")).Should().Equal("Aceite");
            project = await ProjectAsync(owner, projectId);
            (await owner.SendAsync(Post($"/api/v1/projects/{projectId}/acceptance", Accept(project, Today)))).StatusCode.Should().Be(HttpStatusCode.OK);
            await DrainAsync();
            var revert = await PreviewAsync(owner, projectId, "revert");
            Descriptions(revert).Should().Equal("Aceite");
            revert.GetProperty("lockedReason").ValueKind.Should().Be(JsonValueKind.Null);
        }
        finally
        {
            await ServicesAsync(false);
        }
    }

    [Fact]
    public async Task Pauta_do_aceite_pendente_com_parcela_desde_a_entrega_e_sem_parcela_no_setimo_dia()
    {
        var (ownerId, owner) = await ActorAsync(Group.OwnerSeedKey);
        await ServicesAsync(true);
        try
        {
            var (withId, withQuote) = await ServiceQuoteAsync(owner, ServiceMilestones);
            var (_, withParcel) = await ApproveAsync(owner, withId, withQuote, Today);
            var (withoutId, withoutQuote) = await ServiceQuoteAsync(owner, [Milestone("Sinal", 50m), Milestone("Entrega", 50m, triggerEvent: "Delivered")]);
            var (_, withoutParcel) = await ApproveAsync(owner, withoutId, withoutQuote, Today);
            foreach (var projectId in new[] { withParcel, withoutParcel })
            {
                var project = await ProjectAsync(owner, projectId);
                (await owner.SendAsync(Post($"/api/v1/projects/{projectId}/delivery", new { date = Iso(Today), version = Version(project), expectedStatus = "Planned" })))
                    .StatusCode.Should().Be(HttpStatusCode.OK);
            }

            var numbers = await WithDbAsync(db => db.Projects.AsNoTracking().Where(p => p.Id == withParcel || p.Id == withoutParcel).ToDictionaryAsync(p => p.Id, p => p.Number));

            // Sem responsável, quem tem projects.register_acceptance (o Proprietário) vê; com parcela, desde o dia da entrega e com o valor.
            var items = await AcceptanceAgendaAsync(owner);
            Lists(items, numbers[withParcel]).Should().BeTrue();
            items.SelectMany(i => i.GetProperty("entries").EnumerateArray()).Single(e => e.GetProperty("title").GetString()!.Contains(numbers[withParcel], StringComparison.Ordinal))
                .GetProperty("title").GetString().Should().Contain("libera R$");
            Lists(items, numbers[withoutParcel]).Should().BeFalse("sem parcela, só a partir de 7 dias corridos");

            // Sem parcela: no 6º dia ainda não; no 7º, sim (sem valor).
            await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE commercial.projects SET delivered_on = {Today.AddDays(-6)} WHERE id = {withoutParcel}"));
            Lists(await AcceptanceAgendaAsync(owner), numbers[withoutParcel]).Should().BeFalse("6º dia");
            await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE commercial.projects SET delivered_on = {Today.AddDays(-7)} WHERE id = {withoutParcel}"));
            items = await AcceptanceAgendaAsync(owner);
            Lists(items, numbers[withoutParcel]).Should().BeTrue("7º dia");
            items.SelectMany(i => i.GetProperty("entries").EnumerateArray()).Single(e => e.GetProperty("title").GetString()!.Contains(numbers[withoutParcel], StringComparison.Ordinal))
                .GetProperty("title").GetString().Should().NotContain("libera");

            // Destinatário: com responsável, só ele (o Proprietário deixa de ver o de outro responsável).
            var (otherId, other) = await ActorAsync("commercial");
            await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE commercial.projects SET manager_user_id = {otherId} WHERE id = {withParcel}"));
            Lists(await AcceptanceAgendaAsync(owner), numbers[withParcel]).Should().BeFalse("o responsável é outro");
            Lists(await AcceptanceAgendaAsync(other), numbers[withParcel]).Should().BeTrue("o responsável vê, mesmo sem registrar o aceite");

            // Some quando o aceite é registrado.
            await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE commercial.projects SET manager_user_id = {ownerId} WHERE id = {withParcel}"));
            var accepted = await ProjectAsync(owner, withParcel);
            (await owner.SendAsync(Post($"/api/v1/projects/{withParcel}/acceptance", Accept(accepted, Today)))).StatusCode.Should().Be(HttpStatusCode.OK);
            Lists(await AcceptanceAgendaAsync(owner), numbers[withParcel]).Should().BeFalse("aceite registrado");

            // CA 12: filtro "Tipo" na lista.
            var services = await JsonAsync(await owner.GetAsync("/api/v1/projects?kind=servico&pageSize=100"));
            services.GetProperty("items").EnumerateArray().All(p => p.GetProperty("executionKind").GetString() == "Service").Should().BeTrue();
            services.GetProperty("items").EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).Should().Contain([withParcel, withoutParcel]);
            var factory = await JsonAsync(await owner.GetAsync("/api/v1/projects?kind=fabricacao&pageSize=100"));
            factory.GetProperty("items").EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).Should().NotContain(withParcel);
        }
        finally
        {
            await ServicesAsync(false);
        }
    }
}
