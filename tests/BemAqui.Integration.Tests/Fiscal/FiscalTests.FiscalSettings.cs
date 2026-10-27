using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.Fiscal.Application;
using BemAqui.Fiscal.Application.Settings;
using BemAqui.Fiscal.Domain;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Identity.Infrastructure;
using BemAqui.Fiscal.Infrastructure;
using BemAqui.Platform.Contracts;
using BemAqui.Platform.Domain;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Fiscal;

/// <summary>
/// FIS-02a CA 1 a 10, 15 e 16 (Testcontainers): catálogo e seed patch <c>fiscal-permissions</c>, seed <c>fiscal-defaults</c> sem
/// sobrescrever, a tela pré-configurada, salvar e auditar, conferir e desfazer, a prontidão em produção e em homologação, as
/// pré-condições, a troca para produção (marca do catálogo, 2FA e campos críticos), a troca de regime e de série, o PDF e as três camadas.
/// </summary>
public partial class FiscalTests
{
    private const string Settings = "/api/v1/fiscal/settings";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    /// <summary>Volta a configuração aos valores do seed, sem conferências, e o perfil da empresa com IE e IBGE.</summary>
    private async Task ResetSettingsAsync(Tenant tenant)
    {
        await using var scope = await fixture.ScopeAsync(tenant);
        var db = scope.ServiceProvider.GetRequiredService<IFiscalDb>();
        db.SettingConfirmations.RemoveRange(await db.SettingConfirmations.ToListAsync());
        db.FiscalSettings.RemoveRange(await db.FiscalSettings.ToListAsync());
        await db.SaveChangesAsync();
        await new FiscalSettingsSeeder().SeedAsync(scope.ServiceProvider, CancellationToken.None);
        await SetCompanyAsync(tenant, "123456789012", "3508504");
    }

    private async Task SetCompanyAsync(Tenant tenant, string? stateRegistration, string? cityIbge)
    {
        await using var scope = await fixture.ScopeAsync(tenant);
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
        var profile = await db.CompanyProfiles.FirstOrDefaultAsync();
        if (profile is null)
        {
            profile = new CompanyProfile { LegalName = "Empresa A Ltda", Street = "Rua A", Number = "10", City = "Caçapava", State = "SP", ZipCode = "12280000" };
            db.CompanyProfiles.Add(profile);
        }

        profile.StateRegistration = stateRegistration;
        profile.CityIbge = cityIbge;
        profile.MunicipalRegistration = "98765";
        await db.SaveChangesAsync();
    }

    private Task<T> FiscalServiceAsync<T>(Tenant tenant, Func<FiscalSettingsService, Task<T>> action) => fixture.WithAsync(tenant, action);

    private static async Task<FiscalSettingsView> ViewAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<FiscalSettingsView>(Web))!;
    }

    private static async Task<FiscalSettingsSaved> SavedAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<FiscalSettingsSaved>(Web))!;
    }

    private static Task<HttpResponseMessage> PutSettingsAsync(HttpClient client, FiscalSettingsValues values, string? code = null, bool confirmSeries = false) =>
        client.PutAsJsonAsync(Settings, new SaveFiscalSettingsRequest(values, code, confirmSeries), Web);

    private static Task<HttpResponseMessage> ConfirmAsync(HttpClient client, params string[] keys) =>
        client.PostAsJsonAsync(Settings + "/confirmations", new ConfirmFiscalSettingsRequest(keys), Web);

    [Fact]
    public async Task Catalogo_tem_fiscal_approve_e_import_incoming_e_o_seed_patch_roda_uma_vez()
    {
        var tenant = fixture.Tenancy.TenantA;
        async Task<(List<string> Catalog, int Patches, List<string> Finance, List<string> Warehouse, int ApproveGrants)> ReadAsync()
        {
            await using var scope = await fixture.ScopeAsync(tenant);
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            List<string> Of(string seedKey) => [.. db.GroupPermissions.Where(p => p.ModuleCode == "fiscal" && db.Groups.Any(g => g.Id == p.GroupId && g.SeedKey == seedKey)).Select(p => p.Action)];
            return (await db.CatalogModuleActions.Where(a => a.ModuleCode == "fiscal").Select(a => a.Action).ToListAsync(),
                await db.SeedPatches.CountAsync(p => p.Key == "fiscal-permissions"),
                Of("finance"), Of("warehouse"),
                await db.GroupPermissions.CountAsync(p => p.ModuleCode == "fiscal" && p.Action == "approve" && db.Groups.Any(g => g.Id == p.GroupId && g.SeedKey != Group.OwnerSeedKey)));
        }

        var first = await ReadAsync();
        first.Catalog.Should().BeEquivalentTo(["view", "create", "edit", "approve", "import_incoming"]);
        first.Patches.Should().Be(1);
        first.Finance.Should().BeEquivalentTo(["view", "create", "edit"]);
        first.Warehouse.Should().BeEquivalentTo(["view", "import_incoming"]);
        first.ApproveGrants.Should().Be(0, "nenhum grupo do seed além do Proprietário recebe fiscal.approve");

        await using (var scope = await fixture.ScopeAsync(tenant))
        {
            await new SeedPatchSeeder().SeedAsync(scope.ServiceProvider, CancellationToken.None);
        }

        var again = await ReadAsync();
        again.Patches.Should().Be(1);
        again.Finance.Should().HaveCount(3, "rodar de novo não duplica");

        var (ownerId, _) = await fixture.ActorAsync(tenant, FiscalFixture.HostA, owner: true);
        await using var permissionScope = await fixture.ScopeAsync(tenant);
        var effective = await permissionScope.ServiceProvider.GetRequiredService<IPermissionService>().GetAsync(ownerId);
        effective.Has("fiscal.approve").Should().BeTrue("o Proprietário tem tudo");
        effective.Has("fiscal.import_incoming").Should().BeTrue();
    }

    [Fact]
    public async Task Seed_fiscal_defaults_cria_uma_vez_e_nunca_sobrescreve_valor_salvo()
    {
        var tenant = fixture.Tenancy.TenantA;
        await ResetSettingsAsync(tenant);
        (await fixture.FiscalAsync(tenant, db => db.FiscalSettings.CountAsync())).Should().Be(1, "o provisionamento já gravou a linha");
        await fixture.QueryAsync(tenant, db => db.Database.ExecuteSqlRawAsync("UPDATE fiscal.fiscal_settings SET pis_rate = 1.65"));

        await using (var scope = await fixture.ScopeAsync(tenant))
        {
            await new FiscalSettingsSeeder().SeedAsync(scope.ServiceProvider, CancellationToken.None);
        }

        (await fixture.FiscalAsync(tenant, db => db.FiscalSettings.CountAsync())).Should().Be(1);
        (await fixture.FiscalAsync(tenant, db => db.FiscalSettings.Select(s => s.PisRate).SingleAsync())).Should().Be(1.65m);
        await ResetSettingsAsync(tenant);
    }

    [Fact]
    public async Task Primeira_abertura_vem_pre_configurada_com_tudo_a_confirmar_e_a_empresa_so_leitura()
    {
        var tenant = fixture.Tenancy.TenantA;
        await ResetSettingsAsync(tenant);
        var (_, owner) = await fixture.ActorAsync(tenant, FiscalFixture.HostA, owner: true);

        var response = await owner.GetAsync(Settings);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var view = await ViewAsync(response);

        view.Values.Regime.Should().Be(TaxRegime.LucroPresumido);
        view.Crt.Should().Be(3);
        view.Values.NfeSeries.Should().Be(2);
        view.Values.CfopSaleInterstateFinal.Should().Be("6107");
        view.Reviews.Should().HaveCount(FiscalSettingKeys.All.Count);
        view.Reviews.All(r => r.ReviewState == FiscalReviewState.ToConfirm).Should().BeTrue();
        view.ConfirmedCount.Should().Be(0);
        view.TotalCount.Should().Be(FiscalSettingKeys.All.Count);
        view.FirstConfiguration.Should().BeTrue();
        view.Sections.Select(s => s.Code).Should().Equal(FiscalSettingKeys.Sections.Select(s => s.Code));
        view.Sections[0].FirstPendingKey.Should().Be("regime");
        view.Company.Document.Should().Be("47225005000186");
        view.Company.StateRegistration.Should().Be("123456789012");
        view.Company.MunicipalRegistration.Should().Be("98765");
        view.Company.CityIbge.Should().Be("3508504");
        view.Preconditions.Should().BeEmpty();
        view.CanEdit.Should().BeTrue();
        view.AllowedEnvironment.Should().Be("Homologation");
    }

    [Fact]
    public async Task Salvar_valida_grava_audita_e_mudar_valor_conferido_volta_o_chip()
    {
        var tenant = fixture.Tenancy.TenantA;
        await ResetSettingsAsync(tenant);
        var (_, owner) = await fixture.ActorAsync(tenant, FiscalFixture.HostA, owner: true);
        var view = await ViewAsync(await owner.GetAsync(Settings));

        var invalid = await PutSettingsAsync(owner, view.Values with { CnaeMain = "123", NfeSeries = 1000, CfopSaleInStateContributor = "6101", PisRate = 120m, CancellationWindowHours = 200, NfseMunicipalCode = "07.02" });
        invalid.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var fields = (await JsonAsync(invalid)).GetProperty("errors");
        foreach (var field in new[] { "cnaeMain", "nfeSeries", "cfopSaleInStateContributor", "pisRate", "cancellationWindowHours", "nfseMunicipalCode" })
        {
            fields.TryGetProperty(field, out _).Should().BeTrue(field);
        }

        (await fixture.FiscalAsync(tenant, db => db.FiscalSettings.Select(s => s.PisRate).SingleAsync())).Should().Be(0.65m, "recusado, nada muda");

        (await ViewAsync(await ConfirmAsync(owner, "pisRate", "cofinsRate"))).Reviews.Single(r => r.Key == "pisRate").ReviewState.Should().Be(FiscalReviewState.Confirmed);
        var saved = await SavedAsync(await PutSettingsAsync(owner, view.Values with { PisRate = 1.65m, CnaeMain = "4329-1/03", CnaeSecondary = ["2822-4/01"] }));
        saved.ChangedKeys.Should().BeEquivalentTo(["pisRate", "cnaeSecondary"]);
        saved.Settings.Values.PisRate.Should().Be(1.65m);
        saved.Settings.Values.CnaeSecondary.Should().Equal("2822401");
        saved.Settings.Reviews.Single(r => r.Key == "pisRate").ReviewState.Should().Be(FiscalReviewState.ToConfirm, "mudou o valor conferido, o chip volta");
        saved.Settings.Reviews.Single(r => r.Key == "cofinsRate").ReviewState.Should().Be(FiscalReviewState.Confirmed, "o que não mudou continua conferido");
        (await fixture.FiscalAsync(tenant, db => db.SettingConfirmations.AnyAsync(c => c.SettingKey == "pisRate"))).Should().BeFalse("a conferência daquele campo é limpa");

        var audit = await fixture.QueryAsync(tenant, db => db.AuditEntries.AsNoTracking().Where(a => a.Entity == nameof(FiscalSettings) && a.Action == "Updated").Select(a => a.Changes).ToListAsync());
        string.Join("\n", audit).Should().Contain("PisRate").And.Contain("1.65");
        var outbox = await fixture.QueryAsync(tenant, db => db.OutboxMessages.AsNoTracking().Where(m => m.Type == typeof(BemAqui.Fiscal.Contracts.FiscalSettingsChanged).FullName).Select(m => m.Payload).ToListAsync());
        outbox.Should().Contain(p => p.Contains("pisRate") && !p.Contains("1.65"), "o aviso leva só as chaves, nunca os valores");
        await ResetSettingsAsync(tenant);
    }

    [Fact]
    public async Task Conferir_campo_e_secao_grava_quem_quando_e_o_hash_e_desfazer_volta_auditado()
    {
        var tenant = fixture.Tenancy.TenantA;
        await ResetSettingsAsync(tenant);
        var (ownerId, owner) = await fixture.ActorAsync(tenant, FiscalFixture.HostA, owner: true);
        var section = FiscalSettingKeys.All.Where(k => k.Section == "cst").Select(k => k.Key).ToArray();

        var confirmed = await ViewAsync(await ConfirmAsync(owner, section));
        confirmed.Sections.Single(s => s.Code == "cst").Pending.Should().Be(0);
        confirmed.ConfirmedCount.Should().Be(section.Length);
        var review = confirmed.Reviews.Single(r => r.Key == "icmsInternalRate");
        review.ReviewedAt.Should().NotBeNull();
        var stored = await fixture.FiscalAsync(tenant, db => db.SettingConfirmations.AsNoTracking().SingleAsync(c => c.SettingKey == "icmsInternalRate"));
        stored.ConfirmedBy.Should().Be(ownerId);
        stored.ValueHash.Should().Be(FiscalSettingKeys.Find("icmsInternalRate")!.HashOf(FiscalSettingsService.NewDefaults(DateTimeOffset.UtcNow)));

        (await ConfirmAsync(owner, "naoExiste")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var undone = await ViewAsync(await owner.DeleteAsync(Settings + "/confirmations/icmsInternalRate"));
        undone.Reviews.Single(r => r.Key == "icmsInternalRate").ReviewState.Should().Be(FiscalReviewState.ToConfirm);
        var audit = await fixture.QueryAsync(tenant, db => db.AuditEntries.AsNoTracking().Where(a => a.Entity == nameof(SettingConfirmation)).Select(a => a.Action).ToListAsync());
        audit.Should().Contain("Created").And.Contain("Deleted");
        await ResetSettingsAsync(tenant);
    }

    [Fact]
    public async Task Prontidao_em_homologacao_so_avisa_e_em_producao_recusa_com_a_lista_dos_criticos()
    {
        var tenant = fixture.Tenancy.TenantA;
        await ResetSettingsAsync(tenant);

        var homolog = await FiscalServiceAsync(tenant, s => s.ReadinessAsync(FiscalDocumentType.Nfe));
        homolog.IsSuccess.Should().BeTrue();
        homolog.Value.PendingKeys.Should().Contain("cstIcms").And.NotContain("nfseIssRate");
        homolog.Value.Warnings.Should().NotBeEmpty();

        await fixture.QueryAsync(tenant, db => db.Database.ExecuteSqlRawAsync("UPDATE fiscal.fiscal_settings SET environment = 'Production'"));
        try
        {
            var blocked = await FiscalServiceAsync(tenant, s => s.ReadinessAsync(FiscalDocumentType.Nfe));
            blocked.Error!.Code.Should().Be(FiscalSettingsErrors.NotConfirmed);
            blocked.Error.Type.Should().Be(ErrorType.Conflict);
            var pending = (IEnumerable<string>)blocked.Error.Data!["pending"]!;
            pending.Should().Contain("regime").And.Contain("cfopSaleInStateContributor").And.NotContain("nfseServiceItem");
            ((string)blocked.Error.Data["link"]!).Should().Be("/app/configuracoes/fiscal#campo-regime");

            await FiscalServiceAsync(tenant, s => s.ConfirmAsync(new ConfirmFiscalSettingsRequest([.. FiscalSettingKeys.CriticalFor(FiscalDocumentType.Nfe).Select(k => k.Key)])));
            (await FiscalServiceAsync(tenant, s => s.ReadinessAsync(FiscalDocumentType.Nfe))).IsSuccess.Should().BeTrue("NF-e com tudo conferido");
            var nfse = await FiscalServiceAsync(tenant, s => s.ReadinessAsync(FiscalDocumentType.Nfse));
            nfse.Error!.Code.Should().Be(FiscalSettingsErrors.NotConfirmed, "os da NFS-e só valem para a NFS-e, e ainda faltam");
            ((IEnumerable<string>)nfse.Error.Data!["pending"]!).Should().OnlyContain(k => FiscalSettingKeys.Find(k)!.Critical == FiscalCriticality.Nfse);
        }
        finally
        {
            await ResetSettingsAsync(tenant);
        }
    }

    [Fact]
    public async Task Sem_IE_IBGE_ou_serie_a_tela_mostra_a_pre_condicao_e_a_emissao_fica_bloqueada()
    {
        var tenant = fixture.Tenancy.TenantA;
        await ResetSettingsAsync(tenant);
        await SetCompanyAsync(tenant, null, null);
        await fixture.QueryAsync(tenant, db => db.Database.ExecuteSqlRawAsync("UPDATE fiscal.fiscal_settings SET nfe_series = NULL"));
        try
        {
            var (_, owner) = await fixture.ActorAsync(tenant, FiscalFixture.HostA, owner: true);
            var view = await ViewAsync(await owner.GetAsync(Settings));
            view.Preconditions.Select(p => p.Message).Should().Equal(
                "Informe a inscrição estadual em Dados da empresa.", "Informe o código IBGE do município em Dados da empresa.", "Informe a série exclusiva da NF-e.");
            var readiness = await FiscalServiceAsync(tenant, s => s.ReadinessAsync(FiscalDocumentType.Nfe));
            readiness.Error!.Code.Should().Be(FiscalSettingsErrors.Incomplete);
            readiness.Error.Message.Should().Be("Informe a inscrição estadual em Dados da empresa.");
        }
        finally
        {
            await ResetSettingsAsync(tenant);
        }
    }

    [Fact]
    public async Task Producao_so_com_a_marca_do_catalogo_2FA_de_novo_e_os_criticos_conferidos()
    {
        var tenant = fixture.Tenancy.TenantA;
        await ResetSettingsAsync(tenant);
        var (_, owner, code) = await OwnerWithTotpAsync();
        var values = (await ViewAsync(await owner.GetAsync(Settings))).Values with { Environment = FiscalEnvironment.Production };
        try
        {
            var noFlag = await PutSettingsAsync(owner, values, await code());
            noFlag.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await JsonAsync(noFlag)).GetProperty("detail").GetString().Should().Be("A emissão desta empresa só liga na virada para a produção.");

            await fixture.SetAllowanceAsync(tenant, FiscalEnvironmentAllowance.Production);
            var noCode = await PutSettingsAsync(owner, values);
            noCode.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await JsonAsync(noCode)).GetProperty("code").GetString().Should().Be("two_factor_required");
            var wrong = await PutSettingsAsync(owner, values, "000000");
            (await JsonAsync(wrong)).GetProperty("code").GetString().Should().Be("two_factor_invalid");

            var pending = await PutSettingsAsync(owner, values, await code());
            pending.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await JsonAsync(pending)).GetProperty("code").GetString().Should().Be(FiscalSettingsErrors.NotConfirmed);
            (await fixture.FiscalAsync(tenant, db => db.FiscalSettings.Select(s => s.Environment).SingleAsync())).Should().Be(FiscalEnvironment.Homologation);

            await ConfirmAsync(owner, [.. FiscalSettingKeys.CriticalFor(FiscalDocumentType.Nfe).Select(k => k.Key)]);
            var on = await SavedAsync(await PutSettingsAsync(owner, values, await code()));
            on.Settings.Values.Environment.Should().Be(FiscalEnvironment.Production);
        }
        finally
        {
            await fixture.SetAllowanceAsync(tenant, FiscalEnvironmentAllowance.Homologation);
            await ResetSettingsAsync(tenant);
        }
    }

    [Fact]
    public async Task Troca_de_regime_avisa_das_regras_e_troca_de_serie_com_nota_emitida_pede_confirmacao()
    {
        var tenant = fixture.Tenancy.TenantA;
        await ResetSettingsAsync(tenant);
        await fixture.ConfigureAsync(tenant);
        var (_, owner) = await fixture.ActorAsync(tenant, FiscalFixture.HostA, owner: true);
        var values = (await ViewAsync(await owner.GetAsync(Settings))).Values;
        await ConfirmAsync(owner, "regime", "crt");

        var regime = await SavedAsync(await PutSettingsAsync(owner, values with { Regime = TaxRegime.LucroReal }));
        regime.Notice.Should().Be(FiscalSettingsService.RegimeChangedNotice);
        regime.ChangedKeys.Should().Contain("regime");
        regime.Settings.Reviews.Single(r => r.Key == "regime").ReviewState.Should().Be(FiscalReviewState.ToConfirm);
        var simples = await PutSettingsAsync(owner, values with { Regime = TaxRegime.SimplesNacional });
        simples.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "no Simples o CST 00 não vale (CSOSN)");

        var invoiceId = await fixture.InvoiceAsync(tenant);
        (await fixture.SubmitAsync(tenant, invoiceId)).IsSuccess.Should().BeTrue();
        var series = (await fixture.InvoiceOfAsync(tenant, invoiceId)).Series!.Value;
        await fixture.QueryAsync(tenant, db => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE fiscal.fiscal_settings SET nfe_series = {series}"));
        var change = await PutSettingsAsync(owner, values with { Regime = TaxRegime.LucroReal, NfeSeries = series + 1 });
        change.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await JsonAsync(change);
        problem.GetProperty("code").GetString().Should().Be(FiscalSettingsErrors.SeriesInUse);
        problem.GetProperty("detail").GetString().Should().StartWith($"Já há nota emitida na série {series} (a última é a nº ");
        var confirmed = await SavedAsync(await PutSettingsAsync(owner, values with { Regime = TaxRegime.LucroReal, NfeSeries = series + 1 }, confirmSeries: true));
        confirmed.Settings.Values.NfeSeries.Should().Be(series + 1);
        await ResetSettingsAsync(tenant);
    }

    [Fact]
    public async Task Baixar_para_a_contadora_gera_o_PDF_so_com_settings_edit()
    {
        var tenant = fixture.Tenancy.TenantA;
        await ResetSettingsAsync(tenant);
        var (_, owner) = await fixture.ActorAsync(tenant, FiscalFixture.HostA, owner: true);
        var (_, viewer) = await fixture.ActorAsync(tenant, FiscalFixture.HostA, grants: ["settings.view", "fiscal.view"]);

        var pdf = await owner.GetAsync(Settings + "/export-pdf");
        pdf.StatusCode.Should().Be(HttpStatusCode.OK);
        pdf.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        var bytes = await pdf.Content.ReadAsByteArrayAsync();
        System.Text.Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
        (await viewer.GetAsync(Settings + "/export-pdf")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Tres_camadas_da_configuracao_fiscal()
    {
        var tenant = fixture.Tenancy.TenantA;
        await ResetSettingsAsync(tenant);
        var (_, viewer) = await fixture.ActorAsync(tenant, FiscalFixture.HostA, grants: ["fiscal.view"]);
        var (_, nobody) = await fixture.ActorAsync(tenant, FiscalFixture.HostA);
        var (_, owner) = await fixture.ActorAsync(tenant, FiscalFixture.HostA, owner: true);

        var read = await ViewAsync(await viewer.GetAsync(Settings));
        read.CanEdit.Should().BeFalse("fiscal.view só lê");
        (await PutSettingsAsync(viewer, read.Values)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ConfirmAsync(viewer, "regime")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await viewer.DeleteAsync(Settings + "/confirmations/regime")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var none = await nobody.GetAsync(Settings);
        none.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await JsonAsync(none)).GetProperty("code").GetString().Should().Be("forbidden");

        await fixture.SetModuleAsync(tenant, "fiscal", released: false);
        try
        {
            foreach (var path in new[] { Settings, Settings + "/certificate" })
            {
                var blocked = await owner.GetAsync(path);
                blocked.StatusCode.Should().Be(HttpStatusCode.Forbidden);
                (await JsonAsync(blocked)).GetProperty("code").GetString().Should().Be("module_not_released");
            }
        }
        finally
        {
            await fixture.SetModuleAsync(tenant, "fiscal", released: true);
        }
    }
}
