using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.Finance.Contracts;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.Commercial;

public partial class QuoteTests
{
    private async Task AcceptFlowAsync(HttpClient owner, JsonElement os, Guid osId, Guid projectId, Guid contractId)
    {
        object Accept(JsonElement o) => new { date = Iso(Today), acceptedByName = "Carlos (síndico)", version = Version(o) };

        // CA 11: checklist incompleto.
        var incomplete = await owner.SendAsync(Post($"{Installations}/{osId}/accept", Accept(os)));
        incomplete.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await incomplete.Content.ReadAsStringAsync()).Should().Contain("Conclua as etapas e o checklist antes do aceite.");
        foreach (var item in os.GetProperty("checklist").EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).ToList())
        {
            os = await JsonAsync(await owner.PutAsJsonAsync($"{Installations}/{osId}/checklist/{item}", new { result = "Done", version = Version(os) }));
        }

        // CA 16: quem não aprova na produção não aceita.
        var (_, commercial) = await ActorAsync("commercial");
        (await commercial.SendAsync(Post($"{Installations}/{osId}/accept", Accept(os)))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // CA 9 e 17: dois aceites juntos, um vale; o título do instalador é confirmado.
        var results = await Task.WhenAll(owner.SendAsync(Post($"{Installations}/{osId}/accept", Accept(os))), owner.SendAsync(Post($"{Installations}/{osId}/accept", Accept(os))));
        results.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1, string.Join(" | ", await Task.WhenAll(results.Select(r => r.Content.ReadAsStringAsync()))));
        var payable = await WithDbAsync(db => db.Payables.AsNoTracking().SingleAsync(p => p.OriginId == osId));
        payable.Status.Should().Be(PayableStatus.Open);
        payable.IsEstimate.Should().BeFalse();
        (await JsonAsync(await owner.GetAsync($"{Installations}/{osId}"))).GetProperty("status").GetString().Should().Be("Completed");

        // CA 10: com todas as OS aceitas, o projeto é entregue e a parcela do aceite é liberada.
        await DrainAsync();
        (await JsonAsync(await owner.GetAsync($"/api/v1/projects/{projectId}"))).GetProperty("status").GetString().Should().Be("Delivered");
        var aceite = (await ParcelsAsync(contractId)).Single(p => p.Description.Contains("Aceite"));
        aceite.Status.Should().Be(ReceivableStatus.Open);
        aceite.DueDate.Should().Be(Today.AddDays(10));
        var done = await JsonAsync(await owner.GetAsync($"{Installations}/{osId}"));
        (await owner.PostAsJsonAsync($"{Installations}/{osId}/cancel", new { reason = "x", version = Version(done) })).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
