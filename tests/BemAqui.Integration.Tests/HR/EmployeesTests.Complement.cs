using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Web;
using BemAqui.HR.Application;
using BemAqui.HR.Contracts;
using BemAqui.HR.Domain;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Attachments;
using BemAqui.Integration.Tests.Observability;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Platform.Domain;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;

namespace BemAqui.Integration.Tests.HR;

/// <summary>
/// CAD-04c2: dependentes, conta para pagamento (2FA de novo, segunda confirmação, aviso, quarentena e revisão), adicionais,
/// vale-transporte e documentos, tudo como dado pessoal (LGPD).
/// </summary>
public partial class EmployeesTests
{
    private const string BankCanary = "CANARIO-CONTA-5519";
    private const string AccountNumber = "7766554-3";
    private const string OtherCpf = "11144477735";

    /// <summary>Códigos do aplicativo autenticador em sequência (o servidor recusa o mesmo intervalo duas vezes).</summary>
    private sealed class Authenticator(byte[] secret)
    {
        private long _last = long.MinValue;

        public string Next()
        {
            // O servidor aceita o passo anterior, o atual e o próximo. O código do passo anterior vence quando o relógio vira o passo: nos
            // últimos 10 s do passo ele podia vencer no meio da requisição (2FA "inválido" intermitente), então ali já vai o código do atual.
            var now = DateTimeOffset.UtcNow;
            var late = now.ToUnixTimeSeconds() % Totp.StepSeconds >= Totp.StepSeconds - 10;
            var step = Math.Max(_last + 1, Totp.StepAt(now) - (late ? 0 : 1));
            _last = step;
            return Totp.Code(secret, step);
        }
    }

    /// <summary>Liga o 2FA (gira o security stamp): o cliente ganha o token novo.</summary>
    private async Task<Authenticator> TotpAsync(Guid userId, HttpClient client, Tenant? tenant = null)
    {
        var target = tenant ?? tenancy.TenantA;
        var secret = Totp.NewSecret();
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(target.Id))!;
        await using var scope = await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
        var user = await db.Users.SingleAsync(u => u.Id == userId);
        user.BeginTotpSetup(tenancy.Factory.Services.GetRequiredService<ISecretProtector>().Protect(SecretScope.ForTenant(target.Id), SecretKind.TotpSecret, new Secret(Base32.Encode(secret))));
        user.EnableTotp(Totp.StepAt(DateTimeOffset.UtcNow) - 10);
        await db.SaveChangesAsync();
        var host = target.Id == tenancy.TenantB.Id ? "b.localhost" : Host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(target.Id, host, userId: userId, stamp: user.SecurityStamp));
        return new Authenticator(secret);
    }

    private static object Bank(uint version, string? code, string holderCpf, string pixType = "Cpf", string? pixKey = null, bool checkedWith = false, string? reason = null, string account = AccountNumber) =>
        new
        {
            bankCode = "077", agency = "0001", account, accountType = "Checking", holderName = "Titular da Conta", holderCpf,
            pixKeyType = pixType, pixKey = pixKey ?? holderCpf, checkedWithEmployee = checkedWith, mismatchReason = reason, twoFactorCode = code, version,
        };

    private static async Task SetEmailAsync(HttpClient client, Guid id, string cpf, string? email) =>
        (await client.PutAsJsonAsync(Emp(id, "/personal"), new
        {
            cpf, birthDate = "1990-05-10", rg = (string?)null, pis = (string?)null, ctpsNumber = (string?)null, ctpsSeries = (string?)null, voterId = (string?)null,
            zipCode = (string?)null, street = (string?)null, number = (string?)null, complement = (string?)null, district = (string?)null, city = (string?)null, state = (string?)null, phone = (string?)null, email,
        })).EnsureSuccessStatusCode();

    private async Task<T> InTenantAsync<T>(Func<IServiceProvider, Task<T>> query)
    {
        await using var scope = await ScopeAsync();
        return await query(scope.ServiceProvider);
    }

    private Task<int> ReadsAsync(string entity, Guid id) =>
        InTenantAsync(sp => sp.GetRequiredService<BemAquiDbContext>().AuditEntries.CountAsync(a => a.Entity == entity && a.EntityId == id.ToString() && a.Action == AuditActions.SensitiveRead));

    [Fact]
    public async Task Dependentes_com_CPF_so_no_de_IR_IDOR_e_so_pela_rota_sensivel()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var (_, viewer) = await ActorAsync("hr:view,edit");
        var position = await PositionAsync(owner);
        var id = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/hr/employees", NewEmployee(position, NewCpf())));
        var other = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/hr/employees", NewEmployee(position, NewCpf())));
        var dependents = Emp(id, "/dependents");
        object Dependent(bool ir, string? cpf, string name = "Ana") => new { name, birthDate = "2015-02-03", kinship = "Child", irDependent = ir, familyAllowance = true, cpf };

        (await viewer.PostAsJsonAsync(dependents, Dependent(false, null))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "exige hr.view_sensitive (CA 1)");
        (await viewer.GetAsync(dependents + "/sensitive")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "conteúdo só por rota com hr.view_sensitive (CA 3)");

        var withoutIr = await owner.PostAsJsonAsync(dependents, Dependent(false, "529.982.247-25"));
        withoutIr.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "sem IR, o CPF não é aceito (CA 2)");
        (await JsonAsync(withoutIr)).GetProperty("errors").TryGetProperty("cpf", out _).Should().BeTrue();
        (await owner.PostAsJsonAsync(dependents, Dependent(true, null))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "com IR, o CPF é obrigatório");
        (await owner.PostAsJsonAsync(dependents, Dependent(true, "111.111.111-11"))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "CPF inválido");

        var plain = await owner.PostAsJsonAsync(dependents, Dependent(false, null, "Bia"));
        plain.StatusCode.Should().Be(HttpStatusCode.Created, await plain.Content.ReadAsStringAsync());
        var cpf = NewCpf();
        var ir = await JsonAsync(await owner.PostAsJsonAsync(dependents, Dependent(true, cpf)));
        var irId = ir.GetProperty("id").GetGuid();
        var taken = await owner.PostAsJsonAsync(dependents, Dependent(true, cpf, "Outra"));
        taken.StatusCode.Should().Be(HttpStatusCode.Conflict, "o mesmo CPF não se repete entre os dependentes do funcionário (CA 2)");
        (await CodeAsync(taken)).Should().Be(EmployeeComplementService.DependentCpfTakenCode);
        (await owner.PostAsJsonAsync(Emp(other, "/dependents"), Dependent(true, cpf))).StatusCode.Should().Be(HttpStatusCode.Created, "outro funcionário pode ter o mesmo dependente");

        var before = await ReadsAsync("EmployeeDependent", id);
        var list = await owner.GetAsync(dependents + "/sensitive");
        list.Headers.CacheControl!.NoStore.Should().BeTrue();
        var body = await list.Content.ReadAsStringAsync();
        body.Should().NotContain(cpf, "o CPF do dependente sai mascarado").And.Contain("***." + cpf[3..6] + "." + cpf[6..9] + "-**");
        (await ReadsAsync("EmployeeDependent", id)).Should().Be(before + 1, "a leitura vai para a auditoria");

        (await owner.PutAsJsonAsync(Emp(other, "/dependents/" + irId), Dependent(true, null))).StatusCode.Should().Be(HttpStatusCode.NotFound, "dependente de outro funcionário (IDOR, CA 3)");
        (await owner.DeleteAsync(Emp(other, "/dependents/" + irId))).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var edited = await owner.PutAsJsonAsync(Emp(id, "/dependents/" + irId), new { name = "Ana Maria", birthDate = "2015-02-03", kinship = "Child", irDependent = true, familyAllowance = false, cpf = (string?)null, version = ir.GetProperty("version").GetUInt32() });
        edited.StatusCode.Should().Be(HttpStatusCode.OK, "CPF vazio no dependente de IR mantém o gravado");
        await using (var scope = await ScopeAsync())
        {
            (await scope.ServiceProvider.GetRequiredService<IHrDb>().EmployeeDependents.SingleAsync(d => d.Id == irId)).Cpf.Should().Be(cpf);
            var audit = string.Join("\n", await scope.ServiceProvider.GetRequiredService<BemAquiDbContext>().AuditEntries.Where(a => a.EntityId == irId.ToString()).Select(a => a.Changes).ToListAsync());
            audit.Should().NotContain(cpf, "CPF do dependente mascarado na auditoria (CA 15)").And.NotContain("Ana Maria");
        }

        (await owner.DeleteAsync(Emp(id, "/dependents/" + (await JsonAsync(plain)).GetProperty("id").GetGuid()))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await JsonAsync(await owner.GetAsync(dependents + "/sensitive"))).GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Conta_exige_2FA_de_novo_e_segunda_confirmacao_e_sai_mascarada()
    {
        var (ownerId, owner) = await ActorAsync(Group.OwnerSeedKey);
        var (_, noTotp) = await ActorAsync(Group.OwnerSeedKey);
        var totp = await TotpAsync(ownerId, owner);
        var position = await PositionAsync(owner);
        var cpf = NewCpf();
        var id = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/hr/employees", NewEmployee(position, cpf)));
        var bank = Emp(id, "/bank-account");

        var noCode = await owner.PutAsJsonAsync(bank, Bank(await VersionAsync(owner, id), null, cpf));
        noCode.StatusCode.Should().Be(HttpStatusCode.Forbidden, "sem o 2FA de novo, nada é gravado (CA 4)");
        (await CodeAsync(noCode)).Should().Be("mfa_step_up_required");
        var withoutTotp = await noTotp.PutAsJsonAsync(bank, Bank(await VersionAsync(owner, id), "123456", cpf));
        (await CodeAsync(withoutTotp)).Should().Be("mfa_step_up_required", "quem não tem 2FA ativo não troca a conta");
        (await owner.PutAsJsonAsync(bank, Bank(await VersionAsync(owner, id), "000000", cpf))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "código errado");
        (await JsonAsync(await owner.GetAsync(Emp(id, "/payment/sensitive")))).GetProperty("account").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);

        var badPix = await owner.PutAsJsonAsync(bank, Bank(await VersionAsync(owner, id), null, cpf, "Email", "maria@empresa"));
        badPix.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "chave validada pelo tipo (CA 4)");
        (await JsonAsync(badPix)).GetProperty("errors").TryGetProperty("pixKey", out _).Should().BeTrue();
        var mismatch = await owner.PutAsJsonAsync(bank, Bank(await VersionAsync(owner, id), null, OtherCpf, "Cpf", cpf));
        mismatch.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "titular diferente do CPF sem a segunda confirmação (CA 5)");
        (await CodeAsync(mismatch)).Should().Be("bank_owner_mismatch_unconfirmed");
        (await CodeAsync(await owner.PutAsJsonAsync(bank, Bank(await VersionAsync(owner, id), null, OtherCpf, checkedWith: true, reason: "curto")))).Should().Be("bank_owner_mismatch_unconfirmed", "motivo de 10 a 200");

        var saved = await owner.PutAsJsonAsync(bank, Bank(await VersionAsync(owner, id), totp.Next(), cpf));
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        var status = (await JsonAsync(saved)).GetProperty("status");
        status.GetProperty("isFirstAccount").GetBoolean().Should().BeTrue();
        status.GetProperty("inQuarantine").GetBoolean().Should().BeFalse("a primeira conta não entra em quarentena (CA 7)");
        (await InTenantAsync(sp => sp.GetRequiredService<BemAquiDbContext>().Notifications.AnyAsync(n => n.Type == EmployeeComplementService.BankChangedNotification && n.Link.Contains(id.ToString()))))
            .Should().BeFalse("a primeira conta não é troca");

        var before = await ReadsAsync("Employee.Payment", id);
        var payment = await owner.GetAsync(Emp(id, "/payment/sensitive"));
        payment.Headers.CacheControl!.NoStore.Should().BeTrue("CA 9");
        var masked = await payment.Content.ReadAsStringAsync();
        masked.Should().NotContain(AccountNumber).And.NotContain(cpf, "conta, titular e chave mascarados também para quem tem a permissão (CA 9)").And.Contain("-3");
        (await ReadsAsync("Employee.Payment", id)).Should().Be(before + 1);

        var reveals = await ReadsAsync("Employee.BankAccount", id);
        var revealed = await owner.GetAsync(Emp(id, "/bank-account/sensitive"));
        revealed.Headers.CacheControl!.NoStore.Should().BeTrue();
        var clear = await JsonAsync(revealed);
        clear.GetProperty("account").GetString().Should().Be(AccountNumber);
        clear.GetProperty("employeeCpf").GetString().Should().Be(cpf, "o titular vem preenchido com o CPF do funcionário");
        (await ReadsAsync("Employee.BankAccount", id)).Should().Be(reveals + 1, "\"Mostrar\" registra a leitura (CA 9)");

        var (_, viewer) = await ActorAsync("hr:view,edit");
        (await viewer.GetAsync(Emp(id, "/payment/sensitive"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await viewer.GetAsync(Emp(id, "/bank-account/sensitive"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await viewer.PutAsJsonAsync(bank, Bank(await VersionAsync(owner, id), "123456", cpf))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Troca_avisa_Proprietario_e_funcionario_entra_em_quarentena_e_nada_vaza()
    {
        var sink = new MemorySink();
        await using var factory = tenancy.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<ILogEventSink>(sink)));
        var (ownerId, ownerClient) = await ActorAsync(Group.OwnerSeedKey);
        var owner = factory.CreateClient();
        owner.DefaultRequestHeaders.Host = Host;
        var totp = await TotpAsync(ownerId, ownerClient);
        owner.DefaultRequestHeaders.Authorization = ownerClient.DefaultRequestHeaders.Authorization;
        var position = await PositionAsync(owner);
        var cpf = NewCpf();
        var id = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/hr/employees", NewEmployee(position, cpf, name: "Rui Conta " + Guid.NewGuid().ToString("N")[..5])));
        await SetEmailAsync(owner, id, cpf, "antigo@pessoal.com");
        await SetEmailAsync(owner, id, cpf, "novo@pessoal.com");
        (await owner.PutAsJsonAsync(Emp(id, "/bank-account"), Bank(await VersionAsync(owner, id), totp.Next(), cpf))).EnsureSuccessStatusCode();

        var changed = await owner.PutAsJsonAsync(Emp(id, "/bank-account"), Bank(await VersionAsync(owner, id), totp.Next(), cpf, "Cpf", OtherCpf, true, BankCanary + " conferido", "99887766-1"));
        changed.StatusCode.Should().Be(HttpStatusCode.OK, await changed.Content.ReadAsStringAsync());
        var status = (await JsonAsync(changed)).GetProperty("status");
        status.GetProperty("inQuarantine").GetBoolean().Should().BeTrue("troca entra em quarentena (CA 7)");
        status.GetProperty("canReview").GetBoolean().Should().BeFalse("quem trocou não revisa");
        var changedAt = status.GetProperty("changedAt").GetDateTimeOffset();
        status.GetProperty("quarantineUntil").GetDateTimeOffset().Should().Be(changedAt.AddDays(30));

        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<BemAquiDbContext>();
        var link = "/app/rh/funcionarios/" + id + "?aba=pagamento";
        var notes = await db.Notifications.AsNoTracking().Where(n => n.Link == link && n.Type == EmployeeComplementService.BankChangedNotification).ToListAsync();
        notes.Should().Contain(n => n.UserId == ownerId, "todos os membros do Proprietário pelo sino (CA 6)");
        notes.Should().OnlyContain(n => n.Title.Contains("alterada por", StringComparison.Ordinal) && n.Title.Contains("chave ou titular diferente do CPF", StringComparison.Ordinal));
        var mails = await db.EmailLogs.AsNoTracking().Where(m => m.RelatedId == id && m.Template == EmployeeComplementService.BankChangedTemplate).ToListAsync();
        mails.Select(m => m.To).Should().BeEquivalentTo(["novo@pessoal.com", "antigo@pessoal.com"], "o atual e o anterior, trocado há menos de 30 dias (CA 6)");
        mails.Should().OnlyContain(m => !m.Model.Contains("99887766", StringComparison.Ordinal) && !m.Model.Contains(OtherCpf, StringComparison.Ordinal) && !m.Model.Contains(BankCanary, StringComparison.Ordinal) && !m.Model.Contains("077", StringComparison.Ordinal),
            "sem banco, conta nem chave no e-mail (CA 6 e 15)");

        var statuses = await scope.ServiceProvider.GetRequiredService<IEmployees>().BankAccountStatusAsync([id]);
        statuses.Single().Should().BeEquivalentTo(new { EmployeeId = id, IsFirstAccount = false, InQuarantine = true, Version = 2, ReviewedAt = (DateTimeOffset?)null }, "o IEmployees expõe a quarentena sem dado bancário (CA 7)");

        var audit = string.Join("\n", await db.AuditEntries.AsNoTracking().Where(a => a.EntityId == id.ToString()).Select(a => a.Changes).ToListAsync());
        audit.Should().Contain("***").And.NotContain("99887766").And.NotContain(OtherCpf).And.NotContain(BankCanary, "auditoria mascarada (CA 15)");
        var outbox = string.Join("\n", (await db.OutboxMessages.AsNoTracking().OrderByDescending(m => m.OccurredAt).Take(50).ToListAsync()).Select(m => m.Payload));
        outbox.Should().NotContain("99887766").And.NotContain(OtherCpf).And.NotContain(BankCanary, "argumento de job só com ids (CA 15)");
        string events;
        lock (sink.Events)
        {
            events = string.Join("\n", sink.Events.Select(e => e.RenderMessage(CultureInfo.InvariantCulture) + " " + string.Join(" ", e.Properties.Select(p => p.Value.ToString()))));
        }

        events.Should().NotContain("99887766").And.NotContain(OtherCpf).And.NotContain(BankCanary, "nada da conta no log (CA 15)");

        // Sem e-mail pessoal: o Proprietário é avisado para falar pessoalmente.
        var (secondId, second) = await ActorAsync(Group.OwnerSeedKey);
        var secondCodes = await TotpAsync(secondId, second);
        var cpf2 = NewCpf();
        var silent = await CreatedAsync(await second.PostAsJsonAsync("/api/v1/hr/employees", NewEmployee(position, cpf2)));
        (await second.PutAsJsonAsync(Emp(silent, "/bank-account"), Bank(await VersionAsync(second, silent), secondCodes.Next(), cpf2))).EnsureSuccessStatusCode();
        (await second.PutAsJsonAsync(Emp(silent, "/bank-account"), Bank(await VersionAsync(second, silent), secondCodes.Next(), cpf2, account: "123-4"))).EnsureSuccessStatusCode();
        (await db.Notifications.AsNoTracking().Where(n => n.UserId == ownerId && n.Link.Contains(silent.ToString())).Select(n => n.Title).SingleAsync())
            .Should().Contain("O funcionário não tem e-mail pessoal: avise pessoalmente.");
    }

    [Fact]
    public async Task Revisao_por_outro_usuario_libera_a_conta_e_recusa_quem_trocou()
    {
        var (authorId, author) = await ActorAsync(Group.OwnerSeedKey);
        var (reviewerId, reviewer) = await ActorAsync(Group.OwnerSeedKey);
        var (_, sensitiveOnly) = await ActorAsync("hr:view,edit,view_sensitive");
        var authorCodes = await TotpAsync(authorId, author);
        var reviewerCodes = await TotpAsync(reviewerId, reviewer);
        var position = await PositionAsync(author);
        var cpf = NewCpf();
        var id = await CreatedAsync(await author.PostAsJsonAsync("/api/v1/hr/employees", NewEmployee(position, cpf)));
        var review = Emp(id, "/bank-account/review");
        (await author.PutAsJsonAsync(Emp(id, "/bank-account"), Bank(await VersionAsync(author, id), authorCodes.Next(), cpf))).EnsureSuccessStatusCode();
        (await CodeAsync(await reviewer.PostAsJsonAsync(review, new { note = "Conferi", twoFactorCode = reviewerCodes.Next(), version = await VersionAsync(author, id) })))
            .Should().Be(EmployeeBankRules.NotInQuarantineCode, "a primeira conta não tem o que revisar");
        (await author.PutAsJsonAsync(Emp(id, "/bank-account"), Bank(await VersionAsync(author, id), authorCodes.Next(), cpf, account: "4455-6"))).EnsureSuccessStatusCode();

        var self = await author.PostAsJsonAsync(review, new { note = "Conferi com ele", twoFactorCode = authorCodes.Next(), version = await VersionAsync(author, id) });
        self.StatusCode.Should().Be(HttpStatusCode.Forbidden, "quem fez a troca não revisa (CA 8)");
        (await JsonAsync(self)).GetProperty("detail").GetString().Should().Be("Você não pode revisar uma troca feita por você.");
        (await sensitiveOnly.PostAsJsonAsync(review, new { note = "x", twoFactorCode = "123456", version = 0 })).StatusCode.Should().Be(HttpStatusCode.Forbidden, "revisar exige hr.approve");
        (await CodeAsync(await reviewer.PostAsJsonAsync(review, new { note = "Liguei para o funcionário", twoFactorCode = (string?)null, version = await VersionAsync(author, id) }))).Should().Be("mfa_step_up_required");
        (await reviewer.PostAsJsonAsync(review, new { note = new string('x', 201), twoFactorCode = reviewerCodes.Next(), version = await VersionAsync(author, id) })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(await reviewer.GetAsync(Emp(id, "/bank-account/status")))).GetProperty("canReview").GetBoolean().Should().BeTrue();

        var released = await reviewer.PostAsJsonAsync(review, new { note = "Liguei para o funcionário", twoFactorCode = reviewerCodes.Next(), version = await VersionAsync(author, id) });
        released.StatusCode.Should().Be(HttpStatusCode.OK, await released.Content.ReadAsStringAsync());
        var status = (await JsonAsync(released)).GetProperty("status");
        status.GetProperty("inQuarantine").GetBoolean().Should().BeFalse("a revisão termina a quarentena (CA 8)");
        status.GetProperty("selfReviewed").GetBoolean().Should().BeFalse();
        status.GetProperty("reviewedAt").ValueKind.Should().NotBe(System.Text.Json.JsonValueKind.Null);
        (await InTenantAsync(sp => sp.GetRequiredService<BemAquiDbContext>().Notifications.AnyAsync(n => n.UserId == authorId && n.Type == EmployeeComplementService.BankReviewedNotification && n.Link.Contains(id.ToString()))))
            .Should().BeTrue("os membros do Proprietário são avisados da revisão");
        (await InTenantAsync(sp => sp.GetRequiredService<IEmployees>().BankAccountStatusAsync([id]))).Single().InQuarantine.Should().BeFalse();
    }

    [Fact]
    public async Task Proprietario_unico_autorrevisa_com_a_marca()
    {
        // O tenant B só tem este Proprietário com hr.approve e hr.view_sensitive: a exceção do Proprietário único vale (CA 8).
        var (soleId, stamp) = await tenancy.SeedUserAsync(tenancy.TenantB, "unico" + Guid.NewGuid().ToString("N")[..8] + "@teste.com");
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantB.Id))!;
        await using (var scope = await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info))
        {
            var identity = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            var group = await identity.Groups.Where(g => g.SeedKey == Group.OwnerSeedKey).Select(g => g.Id).SingleAsync();
            identity.UserGroups.Add(new UserGroup { UserId = soleId, GroupId = group });
            await identity.SaveChangesAsync();
        }

        var sole = tenancy.Factory.CreateClient();
        sole.DefaultRequestHeaders.Host = "b.localhost";
        sole.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenancy.TenantB.Id, "b.localhost", userId: soleId, stamp: stamp));
        var codes = await TotpAsync(soleId, sole, tenancy.TenantB);
        var position = await PositionAsync(sole);
        var cpf = NewCpf();
        var id = await CreatedAsync(await sole.PostAsJsonAsync("/api/v1/hr/employees", NewEmployee(position, cpf)));
        (await sole.PutAsJsonAsync(Emp(id, "/bank-account"), Bank(await VersionAsync(sole, id), codes.Next(), cpf))).EnsureSuccessStatusCode();
        (await sole.PutAsJsonAsync(Emp(id, "/bank-account"), Bank(await VersionAsync(sole, id), codes.Next(), cpf, account: "1-0"))).EnsureSuccessStatusCode();

        var reviewed = await sole.PostAsJsonAsync(Emp(id, "/bank-account/review"), new { note = "Sou o único revisor", twoFactorCode = codes.Next(), version = await VersionAsync(sole, id) });
        reviewed.StatusCode.Should().Be(HttpStatusCode.OK, await reviewed.Content.ReadAsStringAsync());
        (await JsonAsync(reviewed)).GetProperty("status").GetProperty("selfReviewed").GetBoolean().Should().BeTrue("marca \"autorrevisado\"");
    }

    [Fact]
    public async Task Adicionais_e_vale_transporte_com_versao()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var (_, viewer) = await ActorAsync("hr:view,edit");
        var position = await PositionAsync(owner);
        var id = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/hr/employees", NewEmployee(position, NewCpf())));
        var version = await VersionAsync(owner, id);
        var additional = Emp(id, "/additional");

        (await viewer.PutAsJsonAsync(additional, new { insalubrityPct = 20, hazardPay = false, usesTransportVoucher = true, version })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await owner.PutAsJsonAsync(additional, new { insalubrityPct = 15, hazardPay = false, usesTransportVoucher = true, version })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "grau 10, 20 ou 40 (CA 10)");
        var saved = await owner.PutAsJsonAsync(additional, new { insalubrityPct = 20, hazardPay = true, usesTransportVoucher = true, version });
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        var payment = await JsonAsync(await owner.GetAsync(Emp(id, "/payment/sensitive")));
        payment.GetProperty("insalubrityPct").GetInt32().Should().Be(20);
        payment.GetProperty("hazardPay").GetBoolean().Should().BeTrue();
        payment.GetProperty("usesTransportVoucher").GetBoolean().Should().BeTrue();
        var stale = await owner.PutAsJsonAsync(additional, new { insalubrityPct = (int?)null, hazardPay = false, usesTransportVoucher = false, version });
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict, "xmin do funcionário (ADR 0019 §2)");
        (await CodeAsync(stale)).Should().Be("concurrency_conflict");
    }

    [Fact]
    public async Task Documentos_e_anexos_so_com_a_permissao_sensivel_e_download_auditado()
    {
        var storage = Path.Combine(Path.GetTempPath(), "bqe-cad04c2-" + Guid.NewGuid().ToString("N"));
        await using var factory = tenancy.Factory.WithWebHostBuilder(b => b.UseSetting("Storage:Root", storage).ConfigureTestServices(s => s.AddSingleton<BemAqui.BuildingBlocks.Attachments.IVirusScanner>(new FakeScanner())));
        HttpClient Clone(HttpClient source)
        {
            var client = factory.CreateClient();
            client.DefaultRequestHeaders.Host = Host;
            client.DefaultRequestHeaders.Authorization = source.DefaultRequestHeaders.Authorization;
            return client;
        }

        try
        {
            var (ownerId, ownerBase) = await ActorAsync(Group.OwnerSeedKey);
            var (_, viewerBase) = await ActorAsync("hr:view,edit");
            var owner = Clone(ownerBase);
            var viewer = Clone(viewerBase);
            var position = await PositionAsync(owner);
            var id = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/hr/employees", NewEmployee(position, NewCpf())));
            var other = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/hr/employees", NewEmployee(position, NewCpf())));

            (await owner.PutAsJsonAsync(Emp(id, "/documents"), new { cnhNumber = "123", cnhCategory = "B", cnhExpiry = "2030-01-01", militaryCertificate = (string?)null, version = await VersionAsync(owner, id) }))
                .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "CNH com 11 dígitos");
            var docs = await owner.PutAsJsonAsync(Emp(id, "/documents"), new { cnhNumber = "12345678901", cnhCategory = "ab", cnhExpiry = "2030-01-01", militaryCertificate = "RA-123", version = await VersionAsync(owner, id) });
            docs.StatusCode.Should().Be(HttpStatusCode.OK, await docs.Content.ReadAsStringAsync());
            var stored = await JsonAsync(await owner.GetAsync(Emp(id, "/documents/sensitive")));
            stored.GetProperty("cnhCategory").GetString().Should().Be("AB");
            (await viewer.GetAsync(Emp(id, "/documents/sensitive"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

            MultipartFormDataContent File(string name, byte[] bytes, string type) => new() { { new ByteArrayContent(bytes) { Headers = { ContentType = new(type) } }, "file", name } };
            var pdf = "%PDF-1.4\n%%EOF"u8.ToArray();
            using (var denied = File("rg.pdf", pdf, "application/pdf"))
            {
                (await viewer.PostAsync("/api/v1/attachments/EmployeeDocument/" + id, denied)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "anexar exige hr.view_sensitive (CA 11)");
            }

            using (var zip = File("docs.zip", [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00], "application/zip"))
            {
                (await owner.PostAsync("/api/v1/attachments/EmployeeDocument/" + id, zip)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "só pdf, png e jpg");
            }

            using (var orphan = File("rg.pdf", pdf, "application/pdf"))
            {
                (await owner.PostAsync("/api/v1/attachments/EmployeeDocument/" + Guid.CreateVersion7(), orphan)).StatusCode.Should().Be(HttpStatusCode.NotFound, "OwnerExists");
            }

            Guid attachmentId;
            using (var content = File("rg.pdf", pdf, "application/pdf"))
            {
                var uploaded = await owner.PostAsync("/api/v1/attachments/EmployeeDocument/" + id, content);
                uploaded.StatusCode.Should().Be(HttpStatusCode.Created, await uploaded.Content.ReadAsStringAsync());
                attachmentId = (await JsonAsync(uploaded)).GetProperty("id").GetGuid();
            }

            (await viewer.GetAsync(Emp(id, "/documents/files"))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "sem a permissão, 403 sem revelar quantos são (CA 12)");
            (await viewer.GetAsync("/api/v1/attachments/EmployeeDocument/" + id)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await viewer.GetAsync("/api/v1/attachments/" + attachmentId + "/content")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await JsonAsync(await owner.GetAsync(Emp(id, "/documents/files")))).GetArrayLength().Should().Be(1);
            (await JsonAsync(await owner.GetAsync(Emp(other, "/documents/files")))).GetArrayLength().Should().Be(0);
            (await owner.GetAsync(Emp(other, "/documents/files/" + attachmentId + "/content"))).StatusCode.Should().Be(HttpStatusCode.NotFound, "anexo de outro funcionário (CA 12)");
            (await owner.GetAsync(Emp(Guid.CreateVersion7(), "/documents/files/" + attachmentId + "/content"))).StatusCode.Should().Be(HttpStatusCode.NotFound);

            var before = await ReadsAsync("EmployeeDocument", id);
            var download = await owner.GetAsync(Emp(id, "/documents/files/" + attachmentId + "/content"));
            download.StatusCode.Should().Be(HttpStatusCode.OK);
            download.Headers.CacheControl!.NoStore.Should().BeTrue();
            (await owner.GetAsync("/api/v1/attachments/" + attachmentId + "/content")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await ReadsAsync("EmployeeDocument", id)).Should().Be(before + 2, "cada download grava a leitura sensível (AuditReads, CA 12)");
            var read = await InTenantAsync(sp => sp.GetRequiredService<BemAquiDbContext>().AuditEntries.AsNoTracking().Where(a => a.Entity == "EmployeeDocument" && a.EntityId == id.ToString()).OrderByDescending(a => a.At).FirstAsync());
            read.UserId.Should().Be(ownerId);
            read.Changes.Should().Contain(attachmentId.ToString(), "qual documento").And.NotContain("rg.pdf");
        }
        finally
        {
            if (Directory.Exists(storage))
            {
                Directory.Delete(storage, true);
            }
        }
    }

    [Fact]
    public async Task Tres_camadas_nos_endpoints_do_complemento()
    {
        var (_, nobody) = await ActorAsync();
        var noGroup = await nobody.GetAsync(Emp(Guid.NewGuid(), "/payment/sensitive"));
        noGroup.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(noGroup)).Should().Be("forbidden");
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        await SetHrReleasedAsync(false);
        try
        {
            var notReleased = await owner.GetAsync(Emp(Guid.NewGuid(), "/dependents/sensitive"));
            notReleased.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await CodeAsync(notReleased)).Should().Be("module_not_released");
        }
        finally
        {
            await SetHrReleasedAsync(true);
        }

        string[] parts = ["/dependents", "/payment", "/bank-account", "/additional", "/documents"];
        var endpoints = tenancy.Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/v1/hr/employees", StringComparison.Ordinal) == true && parts.Any(p => e.RoutePattern.RawText.Contains(p, StringComparison.Ordinal))
                && !e.RoutePattern.RawText.Contains("/time-adjustments", StringComparison.Ordinal)).ToList();
        endpoints.Should().HaveCount(14);
        foreach (var endpoint in endpoints)
        {
            endpoint.Metadata.GetMetadata<RequiresModuleMetadata>()!.ModuleCode.Should().Be("hr", endpoint.DisplayName);
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Should().Contain(a => a.Policy != null && a.Policy.StartsWith(ModuleEndpoints.PermissionPolicyPrefix + "hr.", StringComparison.Ordinal), endpoint.DisplayName);
        }
    }
}
