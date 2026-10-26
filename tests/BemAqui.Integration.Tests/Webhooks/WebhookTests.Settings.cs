using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Webhooks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.Webhooks;

/// <summary>FND-16 CA 7, 15 e 16: rotação com 2FA de novo e 24 h de sobreposição; "Eventos recebidos" com as três camadas e o corpo mascarado.</summary>
public partial class WebhookTests
{
    private const string TenantHost = "a.localhost";
    private const string Events = "/api/v1/settings/integrations/events";

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task Rotacao_exige_2FA_de_novo_mostra_o_segredo_uma_vez_e_audita_sem_o_valor()
    {
        var (ownerId, owner) = await fixture.ActorAsync(fixture.Tenancy.TenantA, TenantHost, owner: true);

        var noCode = await owner.PostAsJsonAsync("/api/v1/settings/integrations/webhooks/test/rotate", new { code = (string?)null });
        noCode.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await JsonAsync(noCode)).GetProperty("code").GetString().Should().Be("two_factor_required");

        var code = await fixture.EnableTotpAsync(fixture.Tenancy.TenantA, ownerId);
        var rotated = await owner.PostAsJsonAsync("/api/v1/settings/integrations/webhooks/test/rotate", new { code });
        rotated.StatusCode.Should().Be(HttpStatusCode.OK, await rotated.Content.ReadAsStringAsync());
        rotated.Headers.CacheControl!.NoStore.Should().BeTrue();
        var body = await JsonAsync(rotated);
        var secret = body.GetProperty("secret").GetString()!;
        var url = body.GetProperty("url").GetString()!;
        var key = await fixture.KeyAsync(fixture.Tenancy.TenantA);
        url.Should().EndWith("/webhooks/test/" + key);

        (await owner.PostAsJsonAsync("/api/v1/settings/integrations/webhooks/test/rotate", new { code })).StatusCode
            .Should().Be(HttpStatusCode.Forbidden, "o mesmo código não vale duas vezes");

        (await fixture.Platform().SendAsync(WebhookFixture.SignedPost(Path("test", key), secret, WebhookFixture.Body()))).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var status = await owner.GetFromJsonAsync<JsonElement>("/api/v1/settings/integrations/webhooks");
        status.EnumerateArray().Single(s => s.GetProperty("provider").GetString() == "test").GetProperty("configured").GetBoolean().Should().BeTrue();
        status.GetRawText().Should().NotContain(secret, "o segredo aparece uma única vez");

        var audit = await fixture.QueryAsync(fixture.Tenancy.TenantA, db => db.AuditEntries.AsNoTracking().Where(a => a.Entity == nameof(WebhookCredential)).Select(a => a.Changes).ToListAsync());
        audit.Should().NotBeEmpty();
        string.Join("\n", audit).Should().NotContain(secret);
        (await fixture.QueryAsync(fixture.Tenancy.TenantA, db => db.Notifications.AnyAsync(n => n.UserId == ownerId && n.Type == WebhookCredentialService.RotatedNotification))).Should().BeTrue();
    }

    [Fact]
    public async Task Segredo_anterior_vale_24_horas_com_aviso_ao_Proprietario_e_depois_e_recusado()
    {
        var key = await fixture.KeyAsync(fixture.Tenancy.TenantB);
        var old = await fixture.ConfigureAsync(fixture.Tenancy.TenantB, WebhookFixture.Hmac);
        var current = await fixture.ConfigureAsync(fixture.Tenancy.TenantB, WebhookFixture.Hmac);
        var (ownerId, _) = await fixture.ActorAsync(fixture.Tenancy.TenantB, "b.localhost", owner: true);

        (await fixture.Platform().SendAsync(WebhookFixture.SignedPost(Path("test", key), current, WebhookFixture.Body()))).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await fixture.Platform().SendAsync(WebhookFixture.SignedPost(Path("test", key), old, WebhookFixture.Body()))).StatusCode.Should().Be(HttpStatusCode.Accepted, "dentro das 24 h");
        (await fixture.Platform().SendAsync(WebhookFixture.SignedPost(Path("test", key), old, WebhookFixture.Body()))).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await fixture.QueryAsync(fixture.Tenancy.TenantB, db => db.Notifications.CountAsync(n => n.UserId == ownerId && n.Type == WebhookReceiver.PreviousSecretNotification)))
            .Should().Be(1, "o aviso sai uma vez por dia");
        fixture.Alerts.Raised.Should().Contain(a => a.Kind == WebhookAlerts.PreviousSecretUsed && a.Tenant == fixture.Tenancy.TenantB.Key);

        await fixture.QueryAsync(fixture.Tenancy.TenantB, db => db.WebhookCredentials.Where(c => c.Provider == "test")
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.PreviousValidUntil, DateTimeOffset.UtcNow.AddSeconds(-1))));
        (await fixture.Platform().SendAsync(WebhookFixture.SignedPost(Path("test", key), old, WebhookFixture.Body()))).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "passadas as 24 h");
    }

    [Fact]
    public async Task Rotacao_sem_settings_edit_da_403_forbidden()
    {
        var (_, viewer) = await fixture.ActorAsync(fixture.Tenancy.TenantA, TenantHost, grants: ["settings.view", "finance.view"]);

        var response = await viewer.PostAsJsonAsync("/api/v1/settings/integrations/webhooks/test/rotate", new { code = "123456" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await JsonAsync(response)).GetProperty("code").GetString().Should().Be("forbidden");
    }

    [Fact]
    public async Task Lista_mostra_os_eventos_sem_o_corpo_e_o_conteudo_mascarado_exige_a_aprovacao_e_fica_na_auditoria()
    {
        var id = await ReceiveAsync(fixture.Tenancy.TenantA);
        var (_, viewer) = await fixture.ActorAsync(fixture.Tenancy.TenantA, TenantHost, grants: ["settings.view", "finance.view"]);
        var (approverId, approver) = await fixture.ActorAsync(fixture.Tenancy.TenantA, TenantHost, grants: ["settings.view", "finance.view", "finance.approve"]);

        var list = await viewer.GetAsync(Events + "?provider=test&pageSize=100");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await list.Content.ReadAsStringAsync();
        json.Should().NotContain("Maria", "a lista não traz o corpo");
        var row = JsonDocument.Parse(json).RootElement.GetProperty("page").GetProperty("items").EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == id);
        row.GetProperty("canViewContent").GetBoolean().Should().BeFalse();
        row.GetProperty("status").GetString().Should().Be("Received");

        (await viewer.GetAsync(Events + "/" + id + "/content")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var content = await approver.GetAsync(Events + "/" + id + "/content");
        content.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Headers.CacheControl!.NoStore.Should().BeTrue();
        var masked = (await JsonAsync(content)).GetProperty("maskedJson").GetString()!;
        masked.Should().NotContain("Maria").And.Contain(JsonPersonalDataMask.Mask).And.Contain("10.5", "valores ficam visíveis");
        (await fixture.QueryAsync(fixture.Tenancy.TenantA, db => db.AuditEntries.AnyAsync(a => a.Entity == "WebhookEvent" && a.EntityId == id.ToString() && a.Action == AuditActions.SensitiveRead && a.UserId == approverId)))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Reprocessar_exige_a_edicao_do_dono_vale_so_para_parado_e_e_auditado()
    {
        var id = await ReceiveAsync(fixture.Tenancy.TenantA, "falhar");
        var (_, viewer) = await fixture.ActorAsync(fixture.Tenancy.TenantA, TenantHost, grants: ["settings.view", "finance.view"]);
        var (editorId, editor) = await fixture.ActorAsync(fixture.Tenancy.TenantA, TenantHost, grants: ["settings.view", "finance.view", "finance.edit"]);

        (await editor.PostAsync(Events + "/" + id + "/reprocess", null)).StatusCode.Should().Be(HttpStatusCode.Conflict, "ainda não está parado");
        for (var i = 0; i < ProcessWebhookEventJob.MaxAttempts; i++)
        {
            await fixture.RunJobAsync(fixture.Tenancy.TenantA, id);
        }

        (await viewer.PostAsync(Events + "/" + id + "/reprocess", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var responses = await Task.WhenAll(editor.PostAsync(Events + "/" + id + "/reprocess", null), editor.PostAsync(Events + "/" + id + "/reprocess", null));
        responses.Select(r => r.StatusCode).Should().Contain(HttpStatusCode.NoContent).And.OnlyContain(s => s == HttpStatusCode.NoContent || s == HttpStatusCode.Conflict, "concorrência dá 409, não 500");

        var evt = await fixture.EventAsync(fixture.Tenancy.TenantA, id);
        evt.Status.Should().Be(WebhookEventStatus.Received);
        evt.Attempts.Should().Be(0);
        (await fixture.QueryAsync(fixture.Tenancy.TenantA, db => db.AuditEntries.AnyAsync(a => a.Entity == "WebhookEvent" && a.EntityId == id.ToString() && a.UserId == editorId)))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Tres_camadas_e_IDOR()
    {
        var idB = await ReceiveAsync(fixture.Tenancy.TenantB);
        var (_, noGroup) = await fixture.ActorAsync(fixture.Tenancy.TenantA, TenantHost);
        var (_, financeOnly) = await fixture.ActorAsync(fixture.Tenancy.TenantA, TenantHost, grants: ["finance.view"]);
        var (_, settingsOnly) = await fixture.ActorAsync(fixture.Tenancy.TenantA, TenantHost, grants: ["settings.view"]);
        var (_, approver) = await fixture.ActorAsync(fixture.Tenancy.TenantA, TenantHost, grants: ["settings.view", "finance.view", "finance.approve", "finance.edit"]);

        (await noGroup.GetAsync(Events)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await financeOnly.GetAsync(Events)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "a tela é de Configurações");
        var settingsOnlyResponse = await settingsOnly.GetAsync(Events);
        settingsOnlyResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden, "sem ver o módulo dono, nenhum provedor");
        (await JsonAsync(settingsOnlyResponse)).GetProperty("code").GetString().Should().Be("forbidden");

        (await approver.GetAsync(Events + "/" + idB + "/content")).StatusCode.Should().Be(HttpStatusCode.NotFound, "evento de outro tenant não existe aqui");
        (await approver.PostAsync(Events + "/" + idB + "/reprocess", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await fixture.SetModuleAsync(fixture.Tenancy.TenantA, "settings", released: false);
        try
        {
            var notReleased = await approver.GetAsync(Events);
            notReleased.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await JsonAsync(notReleased)).GetProperty("code").GetString().Should().Be("module_not_released");
        }
        finally
        {
            await fixture.SetModuleAsync(fixture.Tenancy.TenantA, "settings", released: true);
        }

        await fixture.SetModuleAsync(fixture.Tenancy.TenantA, "finance", released: false);
        await fixture.SetModuleAsync(fixture.Tenancy.TenantA, "fiscal", released: false);
        try
        {
            var ownerModules = await approver.GetAsync(Events);
            ownerModules.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await JsonAsync(ownerModules)).GetProperty("code").GetString().Should().Be("module_not_released");
        }
        finally
        {
            await fixture.SetModuleAsync(fixture.Tenancy.TenantA, "finance", released: true);
            await fixture.SetModuleAsync(fixture.Tenancy.TenantA, "fiscal", released: true);
        }
    }
}
