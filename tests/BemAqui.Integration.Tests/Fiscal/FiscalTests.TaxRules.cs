using System.Net;
using System.Net.Http.Json;
using BemAqui.Fiscal.Application;
using BemAqui.Fiscal.Application.Settings;
using BemAqui.Fiscal.Application.TaxRules;
using BemAqui.Fiscal.Contracts;
using BemAqui.Fiscal.Domain;
using BemAqui.Fiscal.Infrastructure;
using BemAqui.Platform.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Fiscal;

/// <summary>
/// FIS-02b (Testcontainers): seed <c>fiscal-tax-rules</c> uma vez e dos valores da FIS-02a (CA 1); rascunho → ativa com segregação, autor
/// sistema e nova versão (CA 2 e 3); conferência e prontidão em produção e homologação (CA 4); sobreposição (CA 5); aviso de revisão pelo
/// <c>FiscalSettingsChanged</c> (CA 6); simulação sem gravar, sem regra, DIFAL e rate limit (CA 8 a 10); marca de custo (CA 11); permissões
/// próprias e IDOR (CA 12); auditoria (CA 13).
/// </summary>
public partial class FiscalTests
{
    private const string Rules = "/api/v1/fiscal/tax-rules";

    /// <summary>Apaga as regras e roda o seed de novo (com a configuração do seed).</summary>
    private async Task ResetRulesAsync(Tenant tenant)
    {
        await ResetSettingsAsync(tenant);
        await using var scope = await fixture.ScopeAsync(tenant);
        var db = scope.ServiceProvider.GetRequiredService<IFiscalDb>();
        db.TaxRules.RemoveRange(await db.TaxRules.ToListAsync());
        await db.SaveChangesAsync();
        await new TaxRuleSeeder().SeedAsync(scope.ServiceProvider, CancellationToken.None);
    }

    private static async Task<T> OkAsync<T>(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>(Web))!;
    }

    private static string VersionPath(TaxRuleView v, string action = "") => $"{Rules}/{v.RuleId}/versions/{v.VersionId}{action}";

    private static async Task<TaxRuleView> SeedRuleAsync(HttpClient client, string name)
    {
        var list = await OkAsync<TaxRuleList>(await client.GetAsync(Rules));
        return await OkAsync<TaxRuleView>(await client.GetAsync($"{Rules}/{list.Rules.Single(r => r.Name == name).RuleId}"));
    }

    private static SimulateTaxRequest Simulation(string state = "SP", RecipientType type = RecipientType.Contributor, Guid? version = null, TaxOperation operation = TaxOperation.OwnProductionSale) =>
        new(operation, state, type, type != RecipientType.Contributor, [new TaxItem("Elevador", "8428.10.00", 0, null, 1, 1000m, IpiRate: 5m)], RuleVersionId: version);

    [Fact]
    public async Task Seed_das_regras_roda_uma_vez_com_os_valores_da_configuracao_fiscal()
    {
        var tenant = fixture.Tenancy.TenantA;
        await ResetRulesAsync(tenant);
        try
        {
            (await fixture.FiscalAsync(tenant, db => db.TaxRules.CountAsync())).Should().Be(4);
            await using (var scope = await fixture.ScopeAsync(tenant))
            {
                await new TaxRuleSeeder().SeedAsync(scope.ServiceProvider, CancellationToken.None);
            }

            (await fixture.FiscalAsync(tenant, db => db.TaxRules.CountAsync())).Should().Be(4, "uma vez e só se o tenant não tiver regra");

            // Os valores vêm de Configurações → Fiscal (FIS-02a), não de outro lugar.
            await fixture.QueryAsync(tenant, db => db.Database.ExecuteSqlRawAsync("UPDATE fiscal.fiscal_settings SET pis_rate = 1.65; DELETE FROM fiscal.tax_rules"));
            await using (var scope = await fixture.ScopeAsync(tenant))
            {
                await new TaxRuleSeeder().SeedAsync(scope.ServiceProvider, CancellationToken.None);
            }

            var (_, owner) = await fixture.ActorAsync(tenant, FiscalFixture.HostA, owner: true);
            var list = await OkAsync<TaxRuleList>(await owner.GetAsync(Rules));
            list.SeedDraftsOnly.Should().BeTrue();
            list.Rules.Should().OnlyContain(r => r.Status == TaxRuleStatus.Draft && r.ReviewState == FiscalReviewState.ToConfirm && r.IsSeed);
            list.ToConfirm.Should().Be(4);
            var rule = await SeedRuleAsync(owner, "Venda de produção própria a contribuinte");
            rule.Values.Pis!.Rate.Should().Be(1.65m);
            rule.Values.CfopInternal.Should().Be("5101");
            rule.SystemAuthored.Should().BeTrue();
        }
        finally
        {
            await ResetRulesAsync(tenant);
        }
    }

    [Fact]
    public async Task Rascunho_ativado_por_outra_pessoa_e_editar_a_ativa_cria_nova_versao()
    {
        var tenant = fixture.Tenancy.TenantA;
        await ResetRulesAsync(tenant);
        try
        {
            var (authorId, author) = await fixture.ActorAsync(tenant, FiscalFixture.HostA, owner: true);
            var (otherId, other) = await fixture.ActorAsync(tenant, FiscalFixture.HostA, owner: true);
            var seed = await SeedRuleAsync(author, "Revenda de peça");

            // Regra do seed: autor sistema, qualquer um com fiscal.approve ativa.
            var active = await OkAsync<TaxRuleView>(await author.PostAsync(VersionPath(seed, "/activate"), null));
            active.Status.Should().Be(TaxRuleStatus.Active);
            active.ActivatedByName.Should().NotBeNull();

            // Editar a ativa cria a versão 2 em rascunho; a 1 continua valendo.
            var edited = await OkAsync<TaxRuleView>(await author.PutAsJsonAsync($"{Rules}/{seed.RuleId}", active.Values with { NatureOfOperation = "Revenda de mercadoria" }, Web));
            edited.RuleVersion.Should().Be(2);
            edited.Status.Should().Be(TaxRuleStatus.Draft);
            edited.ReviewState.Should().Be(FiscalReviewState.ToConfirm);
            edited.ActivateBlockedReason.Should().StartWith(TaxRuleRules.SegregationMessage);
            edited.Versions.Select(v => (v.RuleVersion, v.Status)).Should().Equal((2, TaxRuleStatus.Draft), (1, TaxRuleStatus.Active));

            var denied = await author.PostAsync(VersionPath(edited, "/activate"), null);
            denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            var problem = await JsonAsync(denied);
            problem.GetProperty("code").GetString().Should().Be("segregation_of_duties");
            problem.GetProperty("detail").GetString().Should().Be("Quem editou a regra não a ativa.");

            var second = await OkAsync<TaxRuleView>(await other.PostAsync(VersionPath(edited, "/activate"), null));
            second.Status.Should().Be(TaxRuleStatus.Active);
            second.Values.NatureOfOperation.Should().Be("Revenda de mercadoria");
            second.Versions.Single(v => v.RuleVersion == 1).Status.Should().Be(TaxRuleStatus.Inactive, "a versão anterior deixa de valer");
            (await fixture.FiscalAsync(tenant, db => db.TaxRules.SingleAsync(r => r.Id == second.VersionId))).ActivatedBy.Should().Be(otherId);

            (await other.PostAsync(VersionPath(second, "/activate"), null)).StatusCode.Should().Be(HttpStatusCode.Conflict, "só rascunho é ativado");

            // Inativar com motivo.
            (await other.PostAsJsonAsync(VersionPath(second, "/deactivate"), new DeactivateTaxRuleRequest(""), Web)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            var off = await OkAsync<TaxRuleView>(await other.PostAsJsonAsync(VersionPath(second, "/deactivate"), new DeactivateTaxRuleRequest("Revenda sai pela contadora"), Web));
            off.Status.Should().Be(TaxRuleStatus.Inactive);
            off.DeactivationReason.Should().Be("Revenda sai pela contadora");

            // CA 13: histórico com antes e depois, a versão e quem ativou.
            var audit = await fixture.QueryAsync(tenant, db => db.AuditEntries.AsNoTracking().Where(a => a.Entity == nameof(TaxRule) && a.EntityId == second.VersionId.ToString()).ToListAsync());
            audit.Select(a => a.Action).Should().Contain("Created").And.Contain("Updated");
            string.Join("\n", audit.Select(a => a.Changes)).Should().Contain("natureOfOperation").And.Contain("activatedBy").And.Contain("ruleVersion").And.Contain(authorId.ToString());
        }
        finally
        {
            await ResetRulesAsync(tenant);
        }
    }

    [Fact]
    public async Task Conferida_por_quem_tem_approve_e_producao_exige_regra_ativa_conferida()
    {
        var tenant = fixture.Tenancy.TenantA;
        await ResetRulesAsync(tenant);
        var (_, owner) = await fixture.ActorAsync(tenant, FiscalFixture.HostA, owner: true);
        try
        {
            var seed = await SeedRuleAsync(owner, "Venda de produção própria a contribuinte");
            var active = await OkAsync<TaxRuleView>(await owner.PostAsync(VersionPath(seed, "/activate"), null));

            var homolog = await FiscalServiceAsync(tenant, s => s.ReadinessAsync(FiscalDocumentType.Nfe));
            homolog.IsSuccess.Should().BeTrue("em homologação a regra ativa não conferida emite com o aviso");
            homolog.Value.PendingRules.Should().Equal("Venda de produção própria a contribuinte");
            homolog.Value.Warnings.Should().Contain(w => w.Contains("regras fiscais ativas"));

            await FiscalServiceAsync(tenant, s => s.ConfirmAsync(new ConfirmFiscalSettingsRequest([.. FiscalSettingKeys.CriticalFor(FiscalDocumentType.Nfe).Select(k => k.Key)])));
            await fixture.QueryAsync(tenant, db => db.Database.ExecuteSqlRawAsync("UPDATE fiscal.fiscal_settings SET environment = 'Production'"));
            var blocked = await FiscalServiceAsync(tenant, s => s.ReadinessAsync(FiscalDocumentType.Nfe));
            blocked.Error!.Code.Should().Be(FiscalSettingsErrors.TaxRulesNotConfirmed);
            ((IEnumerable<string>)blocked.Error.Data!["pendingRules"]!).Should().Equal("Venda de produção própria a contribuinte");
            (await owner.PostAsJsonAsync(Rules + "/simulate", Simulation(), Web)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "em produção só regra conferida resolve");

            var confirmed = await OkAsync<TaxRuleView>(await owner.PostAsync(VersionPath(active, "/confirm"), null));
            confirmed.ReviewState.Should().Be(FiscalReviewState.Confirmed);
            confirmed.ConfirmedByName.Should().NotBeNull();
            (await FiscalServiceAsync(tenant, s => s.ReadinessAsync(FiscalDocumentType.Nfe))).IsSuccess.Should().BeTrue();
            (await owner.PostAsJsonAsync(Rules + "/simulate", Simulation(), Web)).StatusCode.Should().Be(HttpStatusCode.OK);
            var audit = await fixture.QueryAsync(tenant, db => db.AuditEntries.AsNoTracking().Where(a => a.Entity == nameof(TaxRule) && a.EntityId == active.VersionId.ToString()).Select(a => a.Changes).ToListAsync());
            string.Join("\n", audit).Should().Contain("confirmedBy");
        }
        finally
        {
            await ResetRulesAsync(tenant);
        }
    }

    [Fact]
    public async Task Regra_que_se_sobrepoe_a_uma_ativa_e_recusada_ao_salvar_e_ao_ativar()
    {
        var tenant = fixture.Tenancy.TenantA;
        await ResetRulesAsync(tenant);
        try
        {
            var (_, owner) = await fixture.ActorAsync(tenant, FiscalFixture.HostA, owner: true);
            var (_, other) = await fixture.ActorAsync(tenant, FiscalFixture.HostA, owner: true);
            var seed = await SeedRuleAsync(owner, "Venda de produção própria a contribuinte");
            await OkAsync<TaxRuleView>(await owner.PostAsync(VersionPath(seed, "/activate"), null));

            var overlap = await owner.PostAsJsonAsync(Rules, seed.Values with { Name = "Outra venda" }, Web);
            overlap.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            var problem = await JsonAsync(overlap);
            problem.GetProperty("code").GetString().Should().Be(TaxRuleRules.OverlapCode);
            problem.GetProperty("detail").GetString().Should().Be("Esta regra se sobrepõe à regra Venda de produção própria a contribuinte.");

            // A mais específica por NCM é aceita contra a sem NCM.
            var specific = await OkAsync<TaxRuleView>(await owner.PostAsJsonAsync(Rules, seed.Values with { Name = "Venda de cabine", Conditions = seed.Values.Conditions with { NcmPrefixes = ["8428"] } }, Web));
            specific.Status.Should().Be(TaxRuleStatus.Draft);
            await OkAsync<TaxRuleView>(await other.PostAsync(VersionPath(specific, "/activate"), null));
            var sim = await OkAsync<TaxSimulationView>(await owner.PostAsJsonAsync(Rules + "/simulate", Simulation(), Web));
            sim.Calculation.Items[0].RuleName.Should().Be("Venda de cabine", "a mais específica por NCM ganha");

            var invalid = await owner.PostAsJsonAsync(Rules, seed.Values with { Name = "", CfopInternal = "6101" }, Web);
            invalid.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            var errors = (await JsonAsync(invalid)).GetProperty("errors");
            errors.TryGetProperty("name", out _).Should().BeTrue();
            errors.TryGetProperty("cfopInternal", out _).Should().BeTrue();
        }
        finally
        {
            await ResetRulesAsync(tenant);
        }
    }

    [Fact]
    public async Task Mudanca_nos_valores_padrao_marca_as_regras_afetadas_para_revisar_sem_muda_las()
    {
        var tenant = fixture.Tenancy.TenantA;
        await ResetRulesAsync(tenant);
        try
        {
            var (_, owner) = await fixture.ActorAsync(tenant, FiscalFixture.HostA, owner: true);
            var values = (await ViewAsync(await owner.GetAsync(Settings))).Values;
            var saved = await SavedAsync(await PutSettingsAsync(owner, values with { NfseIssRate = 3m }));
            saved.RulesToReview.Should().Equal("Prestação de serviço de instalação");

            await fixture.WithAsync<bool, TaxRuleReviewConsumer>(tenant, async c =>
            {
                await c.HandleAsync(new FiscalSettingsChanged(saved.ChangedKeys, false), CancellationToken.None);
                return true;
            });

            var review = await OkAsync<TaxRuleList>(await owner.GetAsync(Rules + "?filter=" + TaxRuleService.FilterToReview));
            review.Rules.Select(r => r.Name).Should().Equal("Prestação de serviço de instalação");
            review.ToReview.Should().Be(1);
            var service = await OkAsync<TaxRuleView>(await owner.GetAsync($"{Rules}/{review.Rules[0].RuleId}"));
            service.NeedsReview.Should().BeTrue();
            service.Values.Iss!.Rate.Should().Be(5m, "nenhuma regra muda sozinha");
        }
        finally
        {
            await ResetRulesAsync(tenant);
        }
    }

    [Fact]
    public async Task Simular_nao_grava_nem_chama_o_Emissor_e_sem_regra_recusa()
    {
        var tenant = fixture.Tenancy.TenantA;
        await ResetRulesAsync(tenant);
        try
        {
            var (_, viewer) = await fixture.ActorAsync(tenant, FiscalFixture.HostA, grants: ["fiscal.view"]);
            var (_, owner) = await fixture.ActorAsync(tenant, FiscalFixture.HostA, owner: true);
            var draft = await SeedRuleAsync(viewer, "Venda de produção própria a não contribuinte");
            var emitterCalls = fixture.Fake(tenant).Calls.Count;
            var audits = await fixture.QueryAsync(tenant, db => db.AuditEntries.CountAsync());

            // Simular a versão aberta (rascunho) com o DIFAL: calcula, mostra e bloqueia.
            var sim = await OkAsync<TaxSimulationView>(await viewer.PostAsJsonAsync(Rules + "/simulate", Simulation("BA", RecipientType.NonContributor, draft.VersionId), Web));
            sim.Calculation.Blocks.Should().Equal(TaxCalculator.DifalBlockedMessage);
            sim.Calculation.Totals.Single(t => t.Code == TaxCodes.Difal).Amount.Should().Be(115.50m, "(18% − 7%) × 1.050");
            sim.Calculation.Items[0].Cfop.Should().Be("6107");
            sim.Calculation.ProjectCost.Should().Be(73.50m + 6.50m + 30m + 9m + 1m, "ICMS 73,50 + PIS + COFINS + IBS/CBS; IPI e DIFAL fora (CA 11)");
            sim.Warnings.Should().Contain(w => w.Contains("rascunho"));
            sim.IssuerState.Should().Be("SP");

            // Sem regra ativa para a combinação: recusa com a mensagem do CA 9.
            var none = await viewer.PostAsJsonAsync(Rules + "/simulate", Simulation(), Web);
            none.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await JsonAsync(none)).GetProperty("detail").GetString().Should().Be("Não há regra fiscal para Venda de produção própria para SP/contribuinte e NCM 84281000.");

            (await fixture.QueryAsync(tenant, db => db.AuditEntries.CountAsync())).Should().Be(audits, "simular não grava nada");
            fixture.Fake(tenant).Calls.Count.Should().Be(emitterCalls, "nem chama o EmissorFiscal");

            // Permissões próprias do módulo: ver não edita nem ativa; IDOR na versão.
            (await viewer.PostAsJsonAsync(Rules, draft.Values, Web)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await viewer.PostAsync(VersionPath(draft, "/activate"), null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await viewer.PostAsync(VersionPath(draft, "/confirm"), null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            var list = await OkAsync<TaxRuleList>(await viewer.GetAsync(Rules));
            list.CanEdit.Should().BeFalse();
            list.CanApprove.Should().BeFalse();
            var otherRule = list.Rules.First(r => r.RuleId != draft.RuleId).RuleId;
            (await owner.GetAsync($"{Rules}/{otherRule}/versions/{draft.VersionId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await owner.PostAsync($"{Rules}/{otherRule}/versions/{draft.VersionId}/activate", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await fixture.FiscalAsync(tenant, db => db.TaxRules.SingleAsync(r => r.Id == draft.VersionId))).Status.Should().Be(TaxRuleStatus.Draft);
        }
        finally
        {
            await ResetRulesAsync(tenant);
        }
    }

    [Fact]
    public async Task Simular_tem_o_limite_de_30_por_minuto_por_usuario()
    {
        var tenant = fixture.Tenancy.TenantB;
        var (_, viewer) = await fixture.ActorAsync(tenant, FiscalFixture.HostB, grants: ["fiscal.view"]);
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 31; i++)
        {
            statuses.Add((await viewer.PostAsJsonAsync(Rules + "/simulate", Simulation(), Web)).StatusCode);
        }

        statuses.Take(30).Should().NotContain(HttpStatusCode.TooManyRequests);
        statuses[30].Should().Be(HttpStatusCode.TooManyRequests);
    }
}
