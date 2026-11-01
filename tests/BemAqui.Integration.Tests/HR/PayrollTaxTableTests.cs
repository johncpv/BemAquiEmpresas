using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Host.Persistence;
using BemAqui.HR.Application;
using BemAqui.HR.Domain;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Platform.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.HR;

/// <summary>
/// FLH-01: tabelas legais da folha por vigência, com <b>tabelas sintéticas</b> (inventadas para o teste, ADR 0039 §4). Cada teste usa anos
/// próprios (a classe compartilha o TenantA). O TenantB é o do Proprietário único (um só membro com <c>hr.approve</c>) e do seed.
/// </summary>
public class PayrollTaxTableTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string Tables = "/api/v1/hr/payroll-tax-tables";

    private static readonly object SyntheticInss = new { inss = new { brackets = new object[] { new { upTo = 1000m, rate = 10m }, new { upTo = 2000m, rate = 15m }, new { upTo = 4000m, rate = 20m } } } };

    private static readonly object SyntheticIrrf = new
    {
        irrf = new
        {
            brackets = new object[] { new { upTo = 2000m, rate = 0m, deduction = 0m }, new { upTo = 3000m, rate = 10m, deduction = 200m }, new { upTo = 5000m, rate = 20m, deduction = 500m }, new { upTo = (decimal?)null, rate = 30m, deduction = 1000m } },
            dependentDeduction = 100m,
            simplifiedDiscount = 400m,
            reductions = new object[] { new { upTo = 4000m, fixedAmount = 150m, rate = 0m }, new { upTo = 6000m, fixedAmount = 600m, rate = 10m } },
        },
    };

    private sealed record Actor(Guid Id, HttpClient Client);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<JsonElement> OkAsync(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        response.StatusCode.Should().Be(status, await response.Content.ReadAsStringAsync());
        return await JsonAsync(response);
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response) => (await JsonAsync(response)).TryGetProperty("code", out var c) ? c.GetString() : null;

    private async Task<T> InTenantAsync<T>(Tenant tenant, Func<IServiceProvider, Task<T>> action)
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenant.Id))!;
        await using var scope = await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
        return await action(scope.ServiceProvider);
    }

    private Task<T> DbAsync<T>(Func<TenantDbContext, Task<T>> action, Tenant? tenant = null) =>
        InTenantAsync(tenant ?? tenancy.TenantA, sp => action(sp.GetRequiredService<TenantDbContext>()));

    /// <summary>Usuário num grupo próprio ("hr:view,edit,view_sensitive").</summary>
    private async Task<Actor> ActorAsync(string grants, Tenant? tenant = null)
    {
        var target = tenant ?? tenancy.TenantA;
        var host = target.Id == tenancy.TenantB.Id ? "b.localhost" : "a.localhost";
        var (id, _) = await tenancy.SeedUserAsync(target, "t" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        var stamp = await InTenantAsync(target, async sp =>
        {
            var db = sp.GetRequiredService<IIdentityDb>();
            var group = new Group { Name = "Tabelas " + Guid.NewGuid().ToString("N")[..6], Description = "Teste" };
            db.Groups.Add(group);
            foreach (var part in grants.Split(';'))
            {
                var module = part[..part.IndexOf(':', StringComparison.Ordinal)];
                db.GroupModules.Add(new GroupModule { GroupId = group.Id, ModuleCode = module, Enabled = true });
                foreach (var action in part[(module.Length + 1)..].Split(','))
                {
                    db.GroupPermissions.Add(new GroupGrant { GroupId = group.Id, ModuleCode = module, Action = action });
                }
            }

            db.UserGroups.Add(new UserGroup { UserId = id, GroupId = group.Id });
            await db.SaveChangesAsync();
            await sp.GetRequiredService<BemAqui.BuildingBlocks.Identity.IPermissionService>().BumpAsync([id]);
            return (await db.Users.SingleAsync(u => u.Id == id)).SecurityStamp;
        });
        var client = tenancy.Factory.CreateClient();
        client.DefaultRequestHeaders.Host = host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(target.Id, host, userId: id, stamp: stamp));
        return new Actor(id, client);
    }

    private static Task<HttpResponseMessage> CreateAsync(Actor actor, string kind, string validFrom, object content, string rounding = "AwayFromZero", string source = "Tabela sintética de teste") =>
        actor.Client.PostAsJsonAsync(Tables, new { kind, validFrom, content, rounding, source, version = (uint?)null });

    private static Task<HttpResponseMessage> CheckAsync(Actor actor, JsonElement table, string? dataset = null) =>
        actor.Client.PostAsJsonAsync($"{Tables}/{table.GetProperty("id").GetGuid()}/check",
            new { accountant = "Contadora Sintética", checkedOn = "2026-10-01", dataset, version = table.GetProperty("version").GetUInt32() });

    /// <summary>CA 1: o pacote do ano entra uma vez, "A confirmar" e criado pelo sistema; rodar de novo não duplica nem sobrescreve o editado.</summary>
    [Fact]
    public async Task Pacote_do_ano_entra_uma_vez_e_nunca_sobrescreve()
    {
        var b = tenancy.TenantB;
        var seeded = await DbAsync(db => db.PayrollTaxTables.AsNoTracking().Where(t => t.SeedPackage == 2026).ToListAsync(), b);
        var charges = seeded.Should().ContainSingle(t => t.Kind == PayrollTaxTableKind.EmployerCharges).Which;
        charges.Status.Should().Be(PayrollTaxTableStatus.Unconfirmed);
        charges.CreatedBy.Should().BeNull("as versões do seed contam como criadas pelo sistema");
        charges.Source.Should().Contain("P-01");
        charges.ValidFrom.Should().Be(new DateOnly(2026, 1, 1));
        charges.ReadContent().EmployerCharges!.EmployerRate.Should().Be(20m);
        // FLH-02a: o pacote de 2026 traz todos os tipos com a fonte oficial, todos "A confirmar com a contadora".
        seeded.Select(t => t.Kind).Should().BeEquivalentTo(Enum.GetValues<PayrollTaxTableKind>());
        seeded.Should().OnlyContain(t => t.Status == PayrollTaxTableStatus.Unconfirmed && t.Source.Contains("A confirmar com a contadora"));
        seeded.Single(t => t.Kind == PayrollTaxTableKind.Inss).ReadContent().Inss!.Brackets[^1].UpTo.Should().Be(8475.55m, "o teto de 2026");

        // O tenant edita a versão do seed; o seed roda de novo (próximo deploy) e não duplica nem desfaz.
        var editor = await ActorAsync("hr:view,edit,view_sensitive", b);
        var detail = await OkAsync(await editor.Client.GetAsync($"{Tables}/{charges.Id}"));
        var edited = await OkAsync(await editor.Client.PutAsJsonAsync($"{Tables}/{charges.Id}", new
        {
            kind = (string?)null, validFrom = "2026-01-01", rounding = "AwayFromZero", source = "Conferido o RAT do CNAE com a contadora",
            content = new { employerCharges = new { taxRegime = "Lucro Presumido", employerRate = 20m, ratRate = 2m, fap = 1m, thirdPartiesRate = 5.8m } },
            version = detail.GetProperty("version").GetUInt32(),
        }));
        edited.GetProperty("status").GetString().Should().Be("Unconfirmed");
        await InTenantAsync(b, async sp =>
        {
            await PayrollTaxTableService.SeedAsync(sp.GetRequiredService<IHrDb>(), TimeProvider.System);
            await PayrollTaxTableService.SeedAsync(sp.GetRequiredService<IHrDb>(), TimeProvider.System);
            return 0;
        });
        var after = await DbAsync(db => db.PayrollTaxTables.AsNoTracking().Where(t => t.SeedPackage == 2026).ToListAsync(), b);
        after.Should().HaveCount(seeded.Count);
        after.Single(t => t.Id == charges.Id).ReadContent().EmployerCharges!.RatRate.Should().Be(2m);

        // Sem entrada pendente no pacote, nenhuma seção traz a nota.
        var view = await OkAsync(await editor.Client.GetAsync(Tables));
        view.GetProperty("sections").EnumerateArray().Where(s => s.TryGetProperty("pendingNote", out var note) && note.ValueKind != JsonValueKind.Null).Should().BeEmpty();
    }

    /// <summary>CA 2 a 4, 9 e 15: cadastro de cada tipo com validação no servidor; nasce "A confirmar"; sem hr.view_sensitive, 403.</summary>
    [Fact]
    public async Task Cadastro_de_cada_tipo_valida_e_nasce_a_confirmar()
    {
        var editor = await ActorAsync("hr:view,edit,view_sensitive");
        var noSensitive = await ActorAsync("hr:view,edit");
        var viewer = await ActorAsync("hr:view");
        (await CreateAsync(noSensitive, "Inss", "2041-01-01", SyntheticInss)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CreateAsync(viewer, "Inss", "2041-01-01", SyntheticInss)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var gap = await CreateAsync(editor, "Inss", "2041-01-01", new { inss = new { brackets = new object[] { new { upTo = 2000m, rate = 10m }, new { upTo = 1000m, rate = 15m } } } });
        gap.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(gap)).GetProperty("errors").TryGetProperty("content.brackets[1]", out _).Should().BeTrue();
        (await CreateAsync(editor, "Inss", "2041-01-15", SyntheticInss)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "vale a partir do primeiro dia do mês");
        (await CreateAsync(editor, "Inss", "2041-01-01", SyntheticInss, source: " ")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "a fonte é obrigatória");

        var inss = await OkAsync(await CreateAsync(editor, "Inss", "2041-01-01", SyntheticInss), HttpStatusCode.Created);
        inss.GetProperty("status").GetString().Should().Be("Unconfirmed");
        inss.GetProperty("kindLabel").GetString().Should().Be("INSS");
        inss.GetProperty("mineToCheck").GetBoolean().Should().BeTrue();
        inss.GetProperty("content").GetProperty("inss").GetProperty("brackets").GetArrayLength().Should().Be(3);
        var dup = await CreateAsync(editor, "Inss", "2041-01-01", SyntheticInss);
        dup.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(dup)).Should().Be(PayrollTaxRules.DuplicateCode);

        var irrf = await OkAsync(await CreateAsync(editor, "Irrf", "2041-01-01", SyntheticIrrf, rounding: "ToEven"), HttpStatusCode.Created);
        irrf.GetProperty("content").GetProperty("irrf").GetProperty("reductions").GetArrayLength().Should().Be(2);
        irrf.GetProperty("rounding").GetString().Should().Be("ToEven");
        irrf.GetProperty("warnings").GetArrayLength().Should().Be(0);
        var broken = new { irrf = new { brackets = new object[] { new { upTo = 2000m, rate = 0m, deduction = 0m }, new { upTo = (decimal?)null, rate = 10m, deduction = 150m } }, dependentDeduction = 0m, simplifiedDiscount = 0m, reductions = Array.Empty<object>() } };
        var warned = await OkAsync(await CreateAsync(editor, "Irrf", "2041-02-01", broken), HttpStatusCode.Created);
        warned.GetProperty("warnings").GetArrayLength().Should().Be(1, "descontínuo em 2.000: avisa sem bloquear");

        await OkAsync(await CreateAsync(editor, "FamilyAllowance", "2041-01-01", new { familyAllowance = new { maxRemuneration = 1500m, quotaPerChild = 50m } }), HttpStatusCode.Created);
        await OkAsync(await CreateAsync(editor, "MinimumWage", "2041-01-01", new { minimumWage = new { monthly = 1234.56m } }), HttpStatusCode.Created);
        await OkAsync(await CreateAsync(editor, "Fgts", "2041-01-01", new { fgts = new { monthlyRate = 7.5m, terminationRate = 30m, apprenticeRate = 1.5m } }), HttpStatusCode.Created);
        await OkAsync(await CreateAsync(editor, "TransportVoucher", "2041-01-01", new { transportVoucher = new { maxDiscountRate = 5m } }), HttpStatusCode.Created);
        await OkAsync(await CreateAsync(editor, "EmployerCharges", "2041-01-01", new { employerCharges = new { taxRegime = "Regime sintético", employerRate = 10m, ratRate = 2m, fap = 1.5m, thirdPartiesRate = 4m } }), HttpStatusCode.Created);

        // Simulador (CA 10) pelas versões da competência, com a memória.
        var sim = await OkAsync(await viewer.Client.PostAsJsonAsync($"{Tables}/simulate", new { competence = "2041-03", @base = 1500m, dependents = 0, children = 2 }));
        var lines = sim.GetProperty("lines").EnumerateArray().ToDictionary(l => l.GetProperty("kind").GetString()!, l => l);
        lines["Inss"].GetProperty("amount").GetDecimal().Should().Be(175m);
        lines["Irrf"].GetProperty("tableId").GetGuid().Should().Be(warned.GetProperty("id").GetGuid(), "fevereiro vale a partir de fevereiro");
        lines["FamilyAllowance"].GetProperty("amount").GetDecimal().Should().Be(100m);
        lines["Fgts"].GetProperty("amount").GetDecimal().Should().Be(112.5m);
        lines["EmployerCharges"].GetProperty("amount").GetDecimal().Should().Be(255m);
        lines["Inss"].GetProperty("memory").GetArrayLength().Should().BeGreaterThan(1);
        sim.GetProperty("missing").GetArrayLength().Should().Be(0);
    }

    /// <summary>CA 5: quem cadastrou ou editou não confere (403); outra pessoa com hr.approve confere e grava quem, quando e a nota; o seed é do sistema.</summary>
    [Fact]
    public async Task Conferencia_segrega_quem_cadastrou_ou_editou()
    {
        var maker = await ActorAsync("hr:view,edit,view_sensitive,approve");
        var editor = await ActorAsync("hr:view,edit,view_sensitive,approve");
        var checker = await ActorAsync("hr:view,approve");
        var noApprove = await ActorAsync("hr:view,edit,view_sensitive");
        var table = await OkAsync(await CreateAsync(maker, "Inss", "2042-01-01", SyntheticInss), HttpStatusCode.Created);
        var self = await CheckAsync(maker, table);
        self.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await JsonAsync(self)).GetProperty("detail").GetString().Should().Be(PayrollTaxRules.SelfCheckMessage);
        (await CheckAsync(noApprove, table)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var put = await OkAsync(await editor.Client.PutAsJsonAsync($"{Tables}/{table.GetProperty("id").GetGuid()}", new
        {
            kind = (string?)null, validFrom = "2042-01-01", rounding = "AwayFromZero", source = "Tabela sintética corrigida", content = SyntheticInss, version = table.GetProperty("version").GetUInt32(),
        }));
        (await CheckAsync(editor, put)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "quem editou também não confere");
        var noNote = await checker.Client.PostAsJsonAsync($"{Tables}/{put.GetProperty("id").GetGuid()}/check", new { accountant = " ", checkedOn = "2026-10-01", dataset = (string?)null, version = put.GetProperty("version").GetUInt32() });
        noNote.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var done = await OkAsync(await CheckAsync(checker, put));
        done.GetProperty("status").GetString().Should().Be("Active");
        done.GetProperty("checkedNote").GetString().Should().Be("Conferido com Contadora Sintética em 01/10/2026");
        done.GetProperty("checkedAt").ValueKind.Should().Be(JsonValueKind.String);
        done.GetProperty("editedByNames").GetArrayLength().Should().Be(1);
        var outbox = await DbAsync(db => db.OutboxMessages.AsNoTracking().Where(m => m.Type.EndsWith("PayrollTaxTableActivated")).Select(m => m.Payload).ToListAsync());
        outbox.Should().Contain(p => p.Contains(done.GetProperty("id").GetGuid().ToString(), StringComparison.Ordinal));

        // Versão do seed: criada pelo sistema, qualquer um com hr.approve confere.
        var seed = await DbAsync(db => db.PayrollTaxTables.AsNoTracking().SingleAsync(t => t.SeedPackage == 2026 && t.Kind == PayrollTaxTableKind.EmployerCharges));
        var seedDetail = await OkAsync(await maker.Client.GetAsync($"{Tables}/{seed.Id}"));
        (await OkAsync(await CheckAsync(maker, seedDetail))).GetProperty("status").GetString().Should().Be("Active");
    }

    /// <summary>CA 6: o Proprietário único só confere a própria versão com o dataset da contadora batendo em todos os casos.</summary>
    [Fact]
    public async Task Proprietario_unico_confere_a_propria_so_com_dataset()
    {
        var b = tenancy.TenantB;
        var owner = await ActorAsync("hr:view,edit,view_sensitive,approve", b);
        var approvers = await InTenantAsync(b, sp => sp.GetRequiredService<BemAqui.BuildingBlocks.Identity.IUserDirectory>().UsersWithPermissionAsync("hr.approve"));
        approvers.Should().Equal([owner.Id], "o TenantB tem um único membro com hr.approve");
        var inss = await OkAsync(await CreateAsync(owner, "Inss", "2043-01-01", SyntheticInss), HttpStatusCode.Created);
        (await OkAsync(await owner.Client.GetAsync(Tables))).GetProperty("singleApprover").GetBoolean().Should().BeTrue();

        var none = await CheckAsync(owner, inss);
        none.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodeAsync(none)).Should().Be(PayrollTaxRules.DatasetRequiredCode);
        var wrong = await CheckAsync(owner, inss, "base;dependentes;esperado\n1500,00;0;175,00\n3000,00;0;451,00");
        wrong.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var mismatch = await JsonAsync(wrong);
        mismatch.GetProperty("code").GetString().Should().Be(PayrollTaxRules.DatasetMismatchCode);
        mismatch.GetProperty("cases").GetArrayLength().Should().Be(1);

        var done = await OkAsync(await CheckAsync(owner, inss, "base;dependentes;esperado\n1500,00;0;175,00\n3000,00;0;450,00\n5000,00;0;650,00"));
        done.GetProperty("status").GetString().Should().Be("Active");
        done.GetProperty("checkedNote").GetString().Should().StartWith(PayrollTaxRules.SingleOwnerNote);
        done.GetProperty("checkedWithDataset").GetBoolean().Should().BeTrue();
        (await DbAsync(db => db.PayrollTaxTableDatasets.AsNoTracking().CountAsync(d => d.TableId == inss.GetProperty("id").GetGuid()), b)).Should().Be(1);

        // IRRF do dataset usa o INSS da mesma vigência (o conferido acima).
        var irrf = await OkAsync(await CreateAsync(owner, "Irrf", "2043-01-01", SyntheticIrrf), HttpStatusCode.Created);
        (await OkAsync(await CheckAsync(owner, irrf, "4500,00;0;120,00\n5000,00;2;230,00"))).GetProperty("status").GetString().Should().Be("Active");
    }

    /// <summary>CA 7 e 14: edita a "A confirmar" com auditoria antes/depois; a conferida não muda (409); o histórico mostra tudo.</summary>
    [Fact]
    public async Task Edita_antes_da_conferencia_e_imutavel_depois()
    {
        var editor = await ActorAsync("hr:view,edit,view_sensitive");
        var checker = await ActorAsync("hr:view,approve");
        var table = await OkAsync(await CreateAsync(editor, "MinimumWage", "2044-01-01", new { minimumWage = new { monthly = 1000m } }), HttpStatusCode.Created);
        var id = table.GetProperty("id").GetGuid();
        object Input(decimal monthly, uint version) => new { kind = (string?)null, validFrom = "2044-01-01", rounding = "AwayFromZero", source = "Tabela sintética", content = new { minimumWage = new { monthly } }, version };
        var edited = await OkAsync(await editor.Client.PutAsJsonAsync($"{Tables}/{id}", Input(1100m, table.GetProperty("version").GetUInt32())));
        edited.GetProperty("status").GetString().Should().Be("Unconfirmed");
        edited.GetProperty("content").GetProperty("minimumWage").GetProperty("monthly").GetDecimal().Should().Be(1100m);
        (await editor.Client.PutAsJsonAsync($"{Tables}/{id}", Input(1200m, table.GetProperty("version").GetUInt32()))).StatusCode.Should().Be(HttpStatusCode.Conflict, "versão antiga");

        var active = await OkAsync(await CheckAsync(checker, edited));
        var locked = await editor.Client.PutAsJsonAsync($"{Tables}/{id}", Input(1300m, active.GetProperty("version").GetUInt32()));
        locked.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(locked)).GetProperty("detail").GetString().Should().Be(PayrollTaxRules.ImmutableMessage);

        var history = await OkAsync(await checker.Client.GetAsync($"/api/v1/audit/PayrollTaxTable/{id}"));
        var changes = history.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("changes").GetString()!).ToList();
        changes.Should().Contain(c => c.Contains("\"content\"", StringComparison.Ordinal) && c.Contains("1100", StringComparison.Ordinal), "antes e depois do conteúdo");
        changes.Should().Contain(c => c.Contains("\"checkedBy\"", StringComparison.Ordinal), "a conferência com quem e quando");
    }

    /// <summary>CA 8 e 11: a competência recebe a versão de maior vigência até ela; sem versão, 422; o fechamento oficial exige Active.</summary>
    [Fact]
    public async Task Leitura_pela_competencia_e_falta_de_tabela()
    {
        var editor = await ActorAsync("hr:view,edit,view_sensitive");
        var checker = await ActorAsync("hr:view,approve");
        var jan = await OkAsync(await CreateAsync(editor, "Inss", "2045-01-01", SyntheticInss), HttpStatusCode.Created);
        var may = await OkAsync(await CreateAsync(editor, "Inss", "2045-05-01", new { inss = new { brackets = new object[] { new { upTo = 5000m, rate = 11m } } } }), HttpStatusCode.Created);
        await CheckAsync(checker, jan);

        async Task<JsonElement> ForAsync(string competence) => await OkAsync(await checker.Client.GetAsync($"{Tables}/for-competence?kind=Inss&competence={competence}"));
        (await ForAsync("2045-04")).GetProperty("id").GetGuid().Should().Be(jan.GetProperty("id").GetGuid());
        (await ForAsync("2045-04")).GetProperty("status").GetString().Should().Be("Active");
        var june = await ForAsync("2045-06");
        june.GetProperty("id").GetGuid().Should().Be(may.GetProperty("id").GetGuid());
        june.GetProperty("status").GetString().Should().Be("Unconfirmed");

        // Antes da primeira versão: 422 com a mensagem e o link (o pacote do seed começa em 2026: sem tabela de FGTS em 2025).
        var missing = await checker.Client.GetAsync($"{Tables}/for-competence?kind=Fgts&competence=2025-03");
        missing.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await JsonAsync(missing);
        body.GetProperty("code").GetString().Should().Be(PayrollTaxRules.MissingCode);
        body.GetProperty("detail").GetString().Should().Be("Falta a tabela FGTS para março/2025.");
        body.GetProperty("link").GetString().Should().StartWith(PayrollTaxTableService.Link);

        // Fechamento oficial (FLH-02a CA 16): todas as versões usadas conferidas.
        var ids = new[] { jan.GetProperty("id").GetGuid(), may.GetProperty("id").GetGuid() };
        var required = await InTenantAsync(tenancy.TenantA, async sp => await sp.GetRequiredService<IPayrollTaxTables>().RequireActiveAsync(ids));
        required.IsFailure.Should().BeTrue();
        required.Error!.Code.Should().Be(PayrollTaxRules.NotConfirmedCode);
        (await InTenantAsync(tenancy.TenantA, async sp => await sp.GetRequiredService<IPayrollTaxTables>().RequireActiveAsync([ids[0]]))).IsSuccess.Should().BeTrue();
    }

    /// <summary>CA 13 e 14: retificação com motivo nasce "A confirmar", o cálculo segue com a conferida; conferida, a anterior fica Superseded.</summary>
    [Fact]
    public async Task Retificacao_passa_por_nova_conferencia_e_substitui_a_anterior()
    {
        var editor = await ActorAsync("hr:view,edit,view_sensitive");
        var checker = await ActorAsync("hr:view,approve");
        var original = await OkAsync(await CheckAsync(checker, await OkAsync(await CreateAsync(editor, "TransportVoucher", "2046-01-01", new { transportVoucher = new { maxDiscountRate = 5m } }), HttpStatusCode.Created)));
        var id = original.GetProperty("id").GetGuid();
        var version = original.GetProperty("version").GetUInt32();
        (await editor.Client.PostAsJsonAsync($"{Tables}/{id}/rectify", new { reason = "x", content = (object?)null, rounding = (string?)null, source = (string?)null, version })).StatusCode
            .Should().Be(HttpStatusCode.UnprocessableEntity, "motivo obrigatório");
        var fix = await OkAsync(await editor.Client.PostAsJsonAsync($"{Tables}/{id}/rectify",
            new { reason = "Percentual digitado errado", content = new { transportVoucher = new { maxDiscountRate = 4m } }, rounding = (string?)null, source = (string?)null, version }), HttpStatusCode.Created);
        fix.GetProperty("status").GetString().Should().Be("Unconfirmed");
        fix.GetProperty("rectifiesId").GetGuid().Should().Be(id);
        fix.GetProperty("validFrom").GetString().Should().Be("2046-01-01");
        var again = await editor.Client.PostAsJsonAsync($"{Tables}/{id}/rectify", new { reason = "Outra correção", content = (object?)null, rounding = (string?)null, source = (string?)null, version });
        (await CodeAsync(again)).Should().Be(PayrollTaxRules.RectifyPendingCode);

        async Task<Guid> InForceAsync() => (await OkAsync(await checker.Client.GetAsync($"{Tables}/for-competence?kind=TransportVoucher&competence=2046-02"))).GetProperty("id").GetGuid();
        (await InForceAsync()).Should().Be(id, "até a nova conferência, vale a conferida");
        var impact = await OkAsync(await checker.Client.GetAsync($"{Tables}/{id}/impact"));
        impact.GetProperty("closedRuns").GetArrayLength().Should().Be(0, "não há folha calculada antes da FLH-02a");

        (await CheckAsync(editor, fix)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await OkAsync(await CheckAsync(checker, fix));
        (await InForceAsync()).Should().Be(fix.GetProperty("id").GetGuid());
        var old = await OkAsync(await checker.Client.GetAsync($"{Tables}/{id}"));
        old.GetProperty("status").GetString().Should().Be("Superseded");
        (await editor.Client.PostAsJsonAsync($"{Tables}/{id}/rectify", new { reason = "Corrigir a antiga", content = (object?)null, rounding = (string?)null, source = (string?)null, version = old.GetProperty("version").GetUInt32() }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict, "a substituída não é retificada");

        // CA 14: o histórico mostra quem cadastrou, conferiu e a fonte.
        var list = await OkAsync(await checker.Client.GetAsync(Tables));
        var versions = list.GetProperty("sections").EnumerateArray().Single(s => s.GetProperty("kind").GetString() == "TransportVoucher").GetProperty("versions").EnumerateArray().ToList();
        var shown = versions.Single(v => v.GetProperty("id").GetGuid() == fix.GetProperty("id").GetGuid());
        shown.GetProperty("rectifyReason").GetString().Should().Be("Percentual digitado errado");
        shown.GetProperty("checkedByName").GetString().Should().NotBeNullOrEmpty();
        shown.GetProperty("createdByName").GetString().Should().NotBeNullOrEmpty();
        shown.GetProperty("isFuture").GetBoolean().Should().BeTrue("2046 é futuro: o chip \"A partir de\"");
    }
}
