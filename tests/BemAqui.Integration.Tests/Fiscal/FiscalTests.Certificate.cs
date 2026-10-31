using System.Net;
using System.Net.Http.Json;
using BemAqui.BuildingBlocks.Agenda;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.Fiscal.Application;
using BemAqui.Fiscal.Application.Certificates;
using BemAqui.Fiscal.Application.Emitter;
using BemAqui.Fiscal.Domain;
using BemAqui.Platform.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Fiscal;

/// <summary>
/// FIS-02a CA 11 a 15 (Testcontainers + <see cref="FakeFiscalEmitter"/>): envio do A1 com 2FA de novo direto ao cofre do Emissor, as
/// recusas (CNPJ, vencido, sem chave, não A1, senha fora do formulário), o canário do <c>.pfx</c> e da senha (log, auditoria, outbox e
/// resposta), a troca fora do BemAqui pelo <c>GET /me</c>, o aviso de vencimento por limiar com a pauta e o rate limit.
/// </summary>
public partial class FiscalTests
{
    private const string Certificate = "/api/v1/fiscal/settings/certificate";

    private static MultipartFormDataContent CertificateForm(byte[] pfx, string? password = TestCertificates.Password, string? code = null, string fileName = "empresa.pfx")
    {
        var form = new MultipartFormDataContent { { new ByteArrayContent(pfx), "pfx", fileName } };
        if (password is not null)
        {
            form.Add(new StringContent(password), "password");
        }

        if (code is not null)
        {
            form.Add(new StringContent(code), "code");
        }

        return form;
    }

    private static async Task<FiscalCertificateView> CertificateViewAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<FiscalCertificateView>(Web))!;
    }

    private async Task ClearCertificateAsync(Tenant tenant)
    {
        await fixture.QueryAsync(tenant, db => db.Database.ExecuteSqlRawAsync(
            "UPDATE fiscal.fiscal_settings SET certificate_not_after = NULL, certificate_subject = NULL, certificate_document = NULL, certificate_thumbprint = NULL, certificate_uploaded_at = NULL, certificate_uploaded_by = NULL, certificate_changed_outside_at = NULL; DELETE FROM fiscal.certificate_uploads;"));
        fixture.Factory.Services.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>().Remove("fiscal-certificate-me:" + tenant.Id.ToString("N"));
    }

    [Fact]
    public async Task Enviar_certificado_exige_2FA_confere_e_repassa_ao_Emissor_guardando_so_os_metadados()
    {
        var tenant = fixture.Tenancy.TenantA;
        await ResetSettingsAsync(tenant);
        await fixture.ConfigureAsync(tenant);
        await ClearCertificateAsync(tenant);
        var fake = fixture.Fake(tenant);
        var (_, owner, code) = await OwnerWithTotpAsync();
        var pfx = TestCertificates.Pfx();
        var canary = Convert.ToBase64String(pfx)[..40];

        var noCode = await owner.PostAsync(Certificate, CertificateForm(pfx));
        noCode.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await JsonAsync(noCode)).GetProperty("code").GetString().Should().Be("two_factor_required");
        fake.Count("certificado").Should().Be(0, "sem o código nada é enviado");

        var inQuery = await owner.PostAsync(Certificate + "?password=" + TestCertificates.Password, CertificateForm(pfx, password: null, code: await code()));
        inQuery.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "a senha só vale no corpo do formulário");
        fake.Count("certificado").Should().Be(0);

        var response = await owner.PostAsync(Certificate, CertificateForm(pfx, code: await code()));
        var body = await response.Content.ReadAsStringAsync();
        var view = await CertificateViewAsync(response);
        fake.Count("certificado").Should().Be(1);
        fake.LastUpload.Should().Be((pfx.Length, "47225005000186"));
        view.Status.Should().Be("Valid");
        view.Document.Should().Be("47225005000186");
        view.Subject.Should().Be("EMPRESA A LTDA:47225005000186");
        view.Thumbprint.Should().Be(TestCertificates.Thumbprint(pfx));
        view.NotAfter.Should().BeCloseTo(DateTimeOffset.UtcNow.AddYears(1), TimeSpan.FromMinutes(5));
        view.UploadedAt.Should().NotBeNull();
        view.History.Should().ContainSingle(h => h.Source == FiscalCertificateSources.BemAqui);
        body.Should().NotContain(TestCertificates.Password).And.NotContain(canary);

        // CA 12: o .pfx e a senha nunca vão para log (inclusive o de requisições), auditoria, outbox ou banco.
        fixture.AllLogs().Should().NotContain(TestCertificates.Password).And.NotContain(canary);
        var audit = await fixture.QueryAsync(tenant, db => db.AuditEntries.AsNoTracking().Where(a => a.Entity == nameof(FiscalSettings)).Select(a => a.Changes).ToListAsync());
        var auditText = string.Join("\n", audit);
        auditText.Should().NotContain(TestCertificates.Password).And.NotContain(canary).And.NotContain(view.Thumbprint!);
        auditText.Should().Contain("CertificateNotAfter", "a troca aparece como \"válido até\"");
        var outbox = await fixture.QueryAsync(tenant, db => db.OutboxMessages.AsNoTracking().Select(m => m.Payload).ToListAsync());
        string.Join("\n", outbox).Should().NotContain(TestCertificates.Password).And.NotContain(canary);
        var stored = await fixture.QueryAsync(tenant, db => db.Database.SqlQueryRaw<string>("SELECT row_to_json(s)::text AS \"Value\" FROM fiscal.fiscal_settings s").ToListAsync());
        string.Join("\n", stored).Should().NotContain(TestCertificates.Password).And.NotContain(canary);
    }

    [Fact]
    public async Task Recusa_outro_CNPJ_vencido_sem_chave_nao_A1_e_o_que_o_Emissor_recusar()
    {
        var tenant = fixture.Tenancy.TenantA;
        await fixture.ConfigureAsync(tenant);
        await ClearCertificateAsync(tenant);
        var fake = fixture.Fake(tenant);
        var (_, owner, code) = await OwnerWithTotpAsync();

        async Task<string> RefusedAsync(byte[] pfx)
        {
            var response = await owner.PostAsync(Certificate, CertificateForm(pfx, code: await code()));
            response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, await response.Content.ReadAsStringAsync());
            return (await JsonAsync(response)).GetProperty("detail").GetString()!;
        }

        (await RefusedAsync(TestCertificates.Pfx(cnpj: "11222333000181"))).Should().Be("O certificado é de outro CNPJ: 11.222.***/****-81.");
        (await RefusedAsync(TestCertificates.Pfx(notBefore: DateTimeOffset.UtcNow.AddYears(-2), notAfter: DateTimeOffset.UtcNow.AddDays(-1)))).Should().StartWith("O certificado venceu em ");
        (await RefusedAsync(TestCertificates.Pfx(privateKey: false))).Should().Be(FiscalCertificateInspector.NoPrivateKey);
        (await RefusedAsync(TestCertificates.Pfx(policy: TestCertificates.A3Policy))).Should().Be(FiscalCertificateInspector.NotA1);
        (await RefusedAsync(TestCertificates.Pfx(password: "outra"))).Should().Be(FiscalCertificateInspector.OpenFailed);
        fake.Count("certificado").Should().Be(0, "o BemAqui confere antes de repassar");

        fake.RejectCertificate = true;
        (await RefusedAsync(TestCertificates.Pfx())).Should().StartWith("O Emissor Fiscal recusou o certificado:");
        fake.RejectCertificate = false;
        (await fixture.FiscalAsync(tenant, db => db.FiscalSettings.Select(s => s.CertificateThumbprint).SingleAsync())).Should().BeNull("recusado, nada é gravado");

        var big = await owner.PostAsync(Certificate, CertificateForm(new byte[FiscalCertificateRules.MaxBytes + 1], code: await code()));
        big.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var wrongType = await owner.PostAsync(Certificate, CertificateForm(TestCertificates.Pfx(), code: await code(), fileName: "certificado.txt"));
        wrongType.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Sem_integracao_testada_a_tela_pede_para_conectar_o_Emissor()
    {
        var tenant = fixture.Tenancy.TenantA;
        await fixture.RemoveIntegrationAsync(tenant);
        try
        {
            var (_, owner, code) = await OwnerWithTotpAsync();
            var view = await CertificateViewAsync(await owner.GetAsync(Certificate));
            view.IntegrationReady.Should().BeFalse();
            view.IntegrationMessage.Should().Be("Conecte o Emissor Fiscal antes de enviar o certificado.");
            var upload = await owner.PostAsync(Certificate, CertificateForm(TestCertificates.Pfx(), code: await code()));
            upload.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await JsonAsync(upload)).GetProperty("detail").GetString().Should().Be("Conecte o Emissor Fiscal antes de enviar o certificado.");
        }
        finally
        {
            await fixture.ConfigureAsync(tenant);
        }
    }

    [Fact]
    public async Task Certificado_trocado_fora_do_BemAqui_aparece_pelo_GET_me()
    {
        var tenant = fixture.Tenancy.TenantA;
        await fixture.ConfigureAsync(tenant);
        await ClearCertificateAsync(tenant);
        var fake = fixture.Fake(tenant);
        var (_, owner, code) = await OwnerWithTotpAsync();
        await CertificateViewAsync(await owner.PostAsync(Certificate, CertificateForm(TestCertificates.Pfx(), code: await code())));

        var until = DateTimeOffset.UtcNow.AddDays(200);
        fake.Certificate = new EmitterCertificate(true, "FFEE00112233445566778899AABBCCDDEEFF0011", "OUTRO TITULAR:47225005000186", DateTimeOffset.UtcNow.AddDays(-1), until, 200, false);
        var cached = await CertificateViewAsync(await owner.GetAsync(Certificate));
        cached.ChangedOutsideAt.Should().BeNull("o GET /me fica em cache por 1 h");

        var view = await CertificateViewAsync(await owner.GetAsync(Certificate + "?refresh=true"));
        view.ChangedOutsideAt.Should().NotBeNull();
        view.Thumbprint.Should().Be("FFEE00112233445566778899AABBCCDDEEFF0011");
        view.NotAfter.Should().BeCloseTo(until, TimeSpan.FromSeconds(1));
        view.History.Should().Contain(h => h.Source == FiscalCertificateSources.Emissor);
        fake.Count("me").Should().BeGreaterThan(0);
        await ClearCertificateAsync(tenant);
    }

    [Fact]
    public async Task Vencimento_avisa_o_Proprietario_por_limiar_uma_vez_entra_na_pauta_e_vencido_alerta()
    {
        var tenant = fixture.Tenancy.TenantA;
        var other = fixture.Tenancy.TenantB;
        await ResetSettingsAsync(tenant);
        var (ownerId, _) = await fixture.ActorAsync(tenant, FiscalFixture.HostA, owner: true);

        async Task SetExpiryAsync(int days) => await fixture.QueryAsync(tenant, db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE fiscal.fiscal_settings SET certificate_not_after = {BemAqui.BuildingBlocks.Time.BusinessTime.StartOfDayUtc(BemAqui.BuildingBlocks.Time.BusinessTime.Today(TimeProvider.System).AddDays(days)).AddHours(12)}, certificate_thumbprint = {"AA11BB22"}, certificate_subject = {"EMPRESA A LTDA"}"));
        async Task<List<string>> NoticesAsync() => await fixture.QueryAsync(tenant, db => db.Notifications.AsNoTracking()
            .Where(n => n.UserId == ownerId && n.Type == FiscalCertificateExpiryChecker.NotificationType).Select(n => n.Title + "|" + n.Link).ToListAsync());
        async Task RunAsync(Tenant t)
        {
            var job = ActivatorUtilities.CreateInstance<FiscalCertificateExpiryJob>(fixture.Factory.Services);
            await job.ExecuteAsync(t.Id, new FiscalCertificateExpiryArgs(), CancellationToken.None);
        }

        try
        {
            await SetExpiryAsync(20);
            await RunAsync(tenant);
            await RunAsync(tenant);
            await RunAsync(other);
            var notices = await NoticesAsync();
            notices.Should().ContainSingle("execução dupla não repete no mesmo limiar");
            notices[0].Should().Be("O certificado digital da empresa vence em 20 dias. Sem ele, nenhuma nota é emitida.|/app/configuracoes/fiscal/certificado");
            (await fixture.QueryAsync(other, db => db.Notifications.CountAsync(n => n.Type == FiscalCertificateExpiryChecker.NotificationType))).Should().Be(0, "o outro tenant não tem certificado");

            await using (var scope = await fixture.ScopeAsync(tenant))
            {
                var source = scope.ServiceProvider.GetServices<IAgendaSource>().OfType<FiscalAgendaSource>().Single();
                var items = await source.ItemsAsync(new AgendaRequest(ownerId, (await scope.ServiceProvider.GetRequiredService<IPermissionService>().GetAsync(ownerId)),
                    new HashSet<string> { FiscalAgendaSource.CertificateExpiring }, BemAqui.BuildingBlocks.Time.BusinessTime.Today(TimeProvider.System)));
                items.Should().ContainSingle().Which.Title.Should().Be("Certificado digital vencendo");
                items[0].Link.Should().Be("/app/configuracoes/fiscal/certificado");
            }

            await SetExpiryAsync(10);
            await RunAsync(tenant);
            (await NoticesAsync()).Should().HaveCount(2, "limiar novo (15 dias), aviso novo");

            fixture.Alerts.Raised.Clear();
            await SetExpiryAsync(-1);
            await RunAsync(tenant);
            fixture.Alerts.Raised.Should().Contain(a => a.Kind == FiscalAlerts.Kind && a.Link == FiscalLinks.Certificate);
            var readiness = await FiscalServiceAsync(tenant, s => s.ReadinessAsync(FiscalDocumentType.Nfe));
            readiness.Error!.Code.Should().Be("fiscal_certificate_expired");
        }
        finally
        {
            await ClearCertificateAsync(tenant);
        }
    }

    [Fact]
    public async Task Certificado_sem_settings_edit_some_e_o_envio_tem_rate_limit_por_tenant()
    {
        var tenant = fixture.Tenancy.TenantA;
        var (_, viewer) = await fixture.ActorAsync(tenant, FiscalFixture.HostA, grants: ["settings.view", "fiscal.view"]);
        var read = await CertificateViewAsync(await viewer.GetAsync(Certificate));
        read.CanEdit.Should().BeFalse();
        (await viewer.PostAsync(Certificate, CertificateForm(TestCertificates.Pfx(), code: "123456"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Tenant B (sem ambiente fiscal): o limite vale antes do serviço (5 por hora em produção, o valor da fixture aqui).
        new FiscalIntegrationOptions().CertificateUploadsPerHour.Should().Be(5);
        var (_, owner) = await fixture.ActorAsync(fixture.Tenancy.TenantB, FiscalFixture.HostB, owner: true);
        var pfx = TestCertificates.Pfx();
        for (var i = 0; i < FiscalFixture.CertificateUploadsPerHour; i++)
        {
            (await owner.PostAsync(Certificate, CertificateForm(pfx))).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        }

        var limited = await owner.PostAsync(Certificate, CertificateForm(pfx));
        limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        limited.Headers.RetryAfter.Should().NotBeNull();
    }
}
