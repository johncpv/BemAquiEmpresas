using System.Net;
using System.Net.Http.Json;
using BemAqui.Finance.Contracts;
using BemAqui.Production.Application;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.Commercial;

public partial class QuoteTests
{
    [Fact]
    public async Task Cancelar_OS_aberta_cancela_o_titulo_e_a_iniciada_deixa_para_o_financeiro()
    {
        var (_, owner) = await ActorAsync(BemAqui.Identity.Domain.Group.OwnerSeedKey);
        var installer = await CustomerAsync(owner, "Installer");
        var (id, quote, _) = await SentQuoteAsync(owner, canary: false, milestones: AcceptanceMilestones);
        var (_, projectId) = await ApproveAsync(owner, id, quote, Today);
        object New(decimal value) => new { projectId, projectItemIds = (Guid[]?)null, installerId = installer, contractValue = value, plannedStart = Iso(Today), plannedEnd = Iso(Today) };
        var os = await JsonAsync(await owner.SendAsync(Post(Installations, New(800m))));
        var osId = os.GetProperty("id").GetGuid();

        // CA 12: o valor muda o título previsto.
        os = await JsonAsync(await owner.PutAsJsonAsync($"{Installations}/{osId}", new { contractValue = 900m, plannedStart = Iso(Today), plannedEnd = Iso(Today.AddDays(2)), version = Version(os) }));
        (await WithDbAsync(db => db.Payables.AsNoTracking().SingleAsync(p => p.OriginId == osId))).Amount.Should().Be(900m);

        // CA 13: iniciada, cancelar deixa o título previsto; o financeiro cancela como origem cancelada.
        os = await JsonAsync(await owner.PostAsJsonAsync($"{Installations}/{osId}/start", new { date = Iso(Today), version = Version(os) }));
        var cancelled = await owner.PostAsJsonAsync($"{Installations}/{osId}/cancel", new { reason = "Cliente adiou a obra", version = Version(os) });
        cancelled.StatusCode.Should().Be(HttpStatusCode.OK, await cancelled.Content.ReadAsStringAsync());
        var payable = await WithDbAsync(db => db.Payables.AsNoTracking().SingleAsync(p => p.OriginId == osId));
        payable.Status.Should().Be(PayableStatus.Planned);
        var detail = await JsonAsync(await owner.GetAsync($"/api/v1/finance/payables/{payable.Id}"));
        var orphan = await owner.PostAsJsonAsync($"/api/v1/finance/payables/{payable.Id}/cancel", new { reason = "Obra adiada", version = Version(detail) });
        orphan.StatusCode.Should().Be(HttpStatusCode.OK, await orphan.Content.ReadAsStringAsync());
        (await WithDbAsync(db => db.Payables.AsNoTracking().SingleAsync(p => p.Id == payable.Id))).CancelReason.Should().StartWith("Origem cancelada:");

        // CA 4 e P-22: modelo com a entrega técnica no fim; OS nova copia o modelo.
        (await owner.PutAsJsonAsync("/api/v1/production/installation-template", new { steps = new[] { "Montagem" }, checklist = new[] { "Ok" } })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        try
        {
            var template = await owner.PutAsJsonAsync("/api/v1/production/installation-template", new { steps = new[] { "Montagem", "Entrega técnica" }, checklist = new[] { "Cliente orientado" } });
            template.StatusCode.Should().Be(HttpStatusCode.OK, await template.Content.ReadAsStringAsync());
            var os2 = await JsonAsync(await owner.SendAsync(Post(Installations, New(100m))));
            os2.GetProperty("steps").GetArrayLength().Should().Be(2);

            // CA 13: aberta, cancelar leva o título junto.
            var os2Id = os2.GetProperty("id").GetGuid();
            (await owner.PostAsJsonAsync($"{Installations}/{os2Id}/cancel", new { reason = "Duplicada", version = Version(os2) })).StatusCode.Should().Be(HttpStatusCode.OK);
            (await WithDbAsync(db => db.Payables.AsNoTracking().SingleAsync(p => p.OriginId == os2Id))).Status.Should().Be(PayableStatus.Cancelled);
        }
        finally
        {
            await owner.PutAsJsonAsync("/api/v1/production/installation-template", new { steps = InstallationTemplateService.DefaultSteps, checklist = InstallationTemplateService.DefaultChecklist });
        }
    }
}
