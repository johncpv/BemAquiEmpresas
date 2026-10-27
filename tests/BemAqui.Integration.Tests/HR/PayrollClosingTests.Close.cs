using System.Net;
using System.Net.Http.Json;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.Finance.Contracts;
using BemAqui.HR.Contracts;
using BemAqui.HR.Domain;
using BemAqui.Identity.Domain;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.HR;

public partial class PayrollClosingTests
{
    /// <summary>CA 1, 2, 4 e 17: pré-condições, 2FA de novo, títulos Employee e Party na mesma transação, conta e centro, evento sem valor.</summary>
    [Fact]
    public async Task Fecha_com_2FA_e_gera_um_titulo_por_funcionario_e_um_por_guia()
    {
        var owner = await ActorAsync(Group.OwnerSeedKey, totp: true);
        var hrOnly = await ActorAsync("hr:view,create,edit,view_sensitive", totp: true);
        var position = await PositionAsync(owner.Client);
        var a = await EmployeeAsync(owner.Client, position);
        var b = await EmployeeAsync(owner.Client, position);
        await SetAccountAsync(a.Id);
        var run = await RunAsync(owner.Client, "2035-01", "Vacation", [a.Id, b.Id]);
        await AddChargeAsync(owner.Client, run, null);

        // Pendências: holerite sem lançamento, funcionário sem conta (o b, depois de lançado) e guia sem órgão; data obrigatória em Férias.
        var preview = await JsonAsync(await owner.Client.GetAsync($"{Runs}/{run}/close/sensitive"));
        var codes = preview.GetProperty("blockers").EnumerateArray().Select(x => x.GetProperty("code").GetString()).ToList();
        codes.Should().Contain(["payslip_empty", "charge_without_payee"]);
        preview.GetProperty("paymentDateRequired").GetBoolean().Should().BeTrue();
        (await CodeAsync(await CloseAsync(owner, run, "2035-01-30"))).Should().Be(PayrollClosingRules.PreconditionCode);

        await FillAsync(owner.Client, run, 1_500m);
        var organ = await PartyAsync(owner.Client);
        var charges = await JsonAsync(await owner.Client.GetAsync($"{Runs}/{run}/charges/sensitive"));
        var charge = charges.GetProperty("items")[0];
        (await owner.Client.PutAsJsonAsync($"{Runs}/{run}/charges/{charge.GetProperty("id").GetGuid()}",
            new { kind = "Fgts", description = (string?)null, amount = 240m, dueDate = (string?)null, payeePartyId = organ, version = charge.GetProperty("version").GetUInt32() })).StatusCode.Should().Be(HttpStatusCode.OK);
        preview = await JsonAsync(await owner.Client.GetAsync($"{Runs}/{run}/close/sensitive"));
        preview.GetProperty("blockers").EnumerateArray().Select(x => x.GetProperty("code").GetString()).Should().Equal("employee_without_account");
        await SetAccountAsync(b.Id, 2, quarantine: true);
        preview = await JsonAsync(await owner.Client.GetAsync($"{Runs}/{run}/close/sensitive"));
        preview.GetProperty("blockers").GetArrayLength().Should().Be(0);
        preview.GetProperty("salaryCount").GetInt32().Should().Be(2);
        preview.GetProperty("salaryTotal").GetDecimal().Should().Be(3_000m);
        preview.GetProperty("chargeTotal").GetDecimal().Should().Be(240m);
        preview.GetProperty("accountChanged").EnumerateArray().Select(x => x.GetProperty("employeeId").GetGuid()).Should().Equal(b.Id);
        preview.GetProperty("newcomers").GetArrayLength().Should().Be(2, "primeira folha dos dois");

        // Sem hr.approve: 403 forbidden. Sem o código: 403 mfa_step_up_required e nada grava.
        (await CloseAsync(hrOnly, run, "2035-01-30")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var noCode = await CloseAsync(owner, run, "2035-01-30", code: false);
        noCode.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(noCode)).Should().Be("mfa_step_up_required");
        (await PayrollPayablesAsync(run)).Should().BeEmpty();

        var closed = await CloseAsync(owner, run, "2035-01-30");
        closed.StatusCode.Should().Be(HttpStatusCode.OK, await closed.Content.ReadAsStringAsync());
        var body = await JsonAsync(closed);
        body.GetProperty("message").GetString().Should().Be("Folha de janeiro de 2035 fechada. 3 títulos a pagar gerados.");
        body.GetProperty("run").GetProperty("status").GetString().Should().Be("Closed");

        var titles = await PayrollPayablesAsync(run);
        titles.Should().HaveCount(3);
        var salaries = titles.Where(t => t.PayeeType == PayeeType.Employee).ToList();
        salaries.Select(t => t.PayeeId).Should().BeEquivalentTo([a.Id, b.Id]);
        salaries.Should().OnlyContain(t => t.PartyId == null && t.Status == PayableStatus.Open && t.DueDate == new DateOnly(2035, 1, 30) && t.PaymentMethod == PaymentMethodKind.EmployeeAccount
            && t.CreatedBy == owner.Id && t.ProjectId == null && t.Amount == 1_500m);
        salaries.Single(t => t.PayeeId == a.Id).Description.Should().Be($"Salário Férias janeiro de 2035 — {a.Registration}");
        var guide = titles.Single(t => t.PayeeType == PayeeType.Party);
        guide.PartyId.Should().Be(organ);
        guide.Description.Should().Be("Guia FGTS janeiro de 2035");
        // CA 4: cargo sem centro de trabalho → 3.1 "Pessoal administrativo" e centro "Administrativo" (padrão dos parâmetros).
        var (admin, adminCenter) = await DbAsync(async db => (await db.Accounts.Where(x => x.Code == "3.1").Select(x => x.Id).SingleAsync(), await db.CostCenters.Where(c => c.Code == "1").Select(c => (Guid?)c.Id).SingleAsync()));
        titles.Should().OnlyContain(t => t.AccountId == admin && t.CostCenterId == adminCenter);

        // Evento só para aviso, sem valor nem dado pessoal (CA 2); auditoria sem dado bancário (canário, CA 17).
        var outbox = await DbAsync(db => db.OutboxMessages.Where(m => m.Type.EndsWith(nameof(PayrollClosed))).Select(m => m.Payload).ToListAsync());
        outbox.Should().ContainSingle(p => p.Contains(run.ToString(), StringComparison.Ordinal));
        outbox.Single(p => p.Contains(run.ToString(), StringComparison.Ordinal)).Should().NotContain("1500").And.NotContain(a.Name);
        var audit = await DbAsync(db => db.AuditEntries.Where(e => e.Entity == nameof(PayrollRun) && e.EntityId == run.ToString()).Select(e => e.Changes).ToListAsync());
        audit.Should().NotBeEmpty();
        audit.Should().OnlyContain(c => !c.Contains(AccountCanary, StringComparison.Ordinal) && !c.Contains("3000", StringComparison.Ordinal));

        // Fechada: não fecha de novo, o holerite não muda.
        (await CodeAsync(await CloseAsync(owner, run, "2035-01-30"))).Should().Be(PayrollRules.ClosedCode);
    }

    /// <summary>CA 3: dois fechamentos simultâneos e o reenvio com a mesma Idempotency-Key: um grava, os títulos não duplicam, um evento; dois tenants.</summary>
    [Fact]
    public async Task Fechamento_concorrente_grava_uma_vez_so_em_dois_tenants()
    {
        var owner = await ActorAsync(Group.OwnerSeedKey, totp: true);
        var owner2 = await ActorAsync(Group.OwnerSeedKey, totp: true);
        var (run, _, _, _) = await ReadyRunAsync(owner, "2035-02");
        var version = await RunVersionAsync(owner.Client, run);
        var first = owner.Client.SendAsync(Post($"{Runs}/{run}/close", new { paymentDate = "2035-02-27", twoFactorCode = owner.Codes!.Next(), version }));
        // Outro membro do Proprietário ao mesmo tempo (cada um com o próprio 2FA): a disputa é na versão da folha e no índice único dos títulos.
        var second = owner2.Client.SendAsync(Post($"{Runs}/{run}/close", new { paymentDate = "2035-02-27", twoFactorCode = owner2.Codes!.Next(), version }));
        var results = await Task.WhenAll(first, second);
        results.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1, string.Join(" | ", await Task.WhenAll(results.Select(r => r.Content.ReadAsStringAsync()))));
        // O segundo recebe 409 (versão da folha ou índice único dos títulos) e não grava nada.
        results.Single(r => r.StatusCode != HttpStatusCode.OK).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await PayrollPayablesAsync(run)).Should().HaveCount(3);
        (await DbAsync(db => OutboxCountAsync(db, nameof(PayrollClosed), run))).Should().Be(1);

        // Reenvio com a mesma chave: a resposta guardada, nada novo.
        var key = Guid.NewGuid();
        var (runB, _, _, _) = await ReadyRunAsync(owner, "2035-03");
        var v = await RunVersionAsync(owner.Client, runB);
        // O reenvio leva o mesmo corpo (mesmo código 2FA): corpo diferente com a mesma chave é 422 idempotency_key_reused.
        var body = new { paymentDate = "2035-03-30", twoFactorCode = owner.Codes!.Next(), version = v };
        var once = await owner.Client.SendAsync(Post($"{Runs}/{runB}/close", body, key));
        once.StatusCode.Should().Be(HttpStatusCode.OK, await once.Content.ReadAsStringAsync());
        var again = await owner.Client.SendAsync(Post($"{Runs}/{runB}/close", body, key));
        again.StatusCode.Should().Be(HttpStatusCode.OK);
        (await PayrollPayablesAsync(runB)).Should().HaveCount(3);

        // Tenant B: o mesmo fluxo no banco dele, sem enxergar o A.
        var ownerB = await ActorAsync(Group.OwnerSeedKey, totp: true, tenant: tenancy.TenantB);
        var positionB = await PositionAsync(ownerB.Client);
        var eB = await EmployeeAsync(ownerB.Client, positionB);
        await SetAccountAsync(eB.Id, tenant: tenancy.TenantB);
        var runOnB = await RunAsync(ownerB.Client, "2035-02", "Vacation", [eB.Id]);
        await FillAsync(ownerB.Client, runOnB, 900m);
        (await CloseAsync(ownerB, runOnB, "2035-02-27")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PayrollPayablesAsync(runOnB, tenancy.TenantB)).Should().ContainSingle(t => t.PayeeId == eB.Id);
        (await PayrollPayablesAsync(runOnB)).Should().BeEmpty("a folha do B não existe no A");
        (await owner.Client.GetAsync($"{Runs}/{runOnB}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>CA 6: expansão do payables — títulos de terceiro com payee Party = party_id, CHECK, N-1 tolerante e regressão do título manual.</summary>
    [Fact]
    public async Task Payables_ganha_favorecido_com_check_e_codigo_antigo_continua_gravando()
    {
        var owner = await ActorAsync(Group.OwnerSeedKey);
        var supplier = await PartyAsync(owner.Client);
        // O segundo terceiro é do próprio teste: rodando primeiro na classe, o tenant não tem outro.
        var other = await PartyAsync(owner.Client);
        var account = await DbAsync(db => db.Accounts.Where(a => a.Code == "3.3").Select(a => a.Id).SingleAsync());
        var created = await owner.Client.SendAsync(Post("/api/v1/finance/payables", new
        {
            partyId = supplier, description = "Regressão", accountId = account, costCenterId = (Guid?)null, amount = 100m, issueDate = Iso(Today), dueDate = Iso(Today.AddDays(5)),
            retentionIss = 0m, retentionInss = 0m, retentionIr = 0m, retentionPcc = 0m, paymentDetails = (object?)null,
        }));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var id = (await JsonAsync(created)).GetProperty("items")[0].GetProperty("id").GetGuid();
        var stored = await DbAsync(db => db.Payables.AsNoTracking().SingleAsync(p => p.Id == id));
        (stored.PayeeType, stored.PayeeId, stored.PartyId).Should().Be((PayeeType.Party, supplier, (Guid?)supplier));

        var violations = await DbAsync(async db =>
        {
            var errors = new List<string>();
            foreach (var sql in new[]
            {
                // Funcionário com party_id preenchido e terceiro sem party_id: o CHECK recusa.
                $"UPDATE finance.payables SET payee_type = 'Employee' WHERE id = '{id}'",
                $"UPDATE finance.payables SET party_id = NULL WHERE id = '{id}'",
            })
            {
                try
                {
                    await db.Database.ExecuteSqlRawAsync(sql);
                    errors.Add("aceitou: " + sql);
                }
                catch (Npgsql.PostgresException ex) when (ex.SqlState == "23514")
                {
                }
            }

            // Código N-1: grava e troca só party_id; o favorecido acompanha.
            await db.Database.ExecuteSqlAsync($"UPDATE finance.payables SET party_id = {other} WHERE id = {id}");
            var after = await db.Payables.AsNoTracking().SingleAsync(p => p.Id == id);
            if (after.PayeeId != other)
            {
                errors.Add("payee_id não acompanhou o party_id");
            }

            return errors;
        });
        violations.Should().BeEmpty();
    }

    /// <summary>CA 16: o contrato da conta de salário só atende com finance.pay no request.</summary>
    [Fact]
    public async Task Conta_de_salario_so_sai_com_finance_pay()
    {
        var owner = await ActorAsync(Group.OwnerSeedKey);
        var position = await PositionAsync(owner.Client);
        var e = await EmployeeAsync(owner.Client, position);
        await SetAccountAsync(e.Id);
        var result = await InTenantAsync(tenancy.TenantA, sp => sp.GetRequiredService<IEmployeeBankAccounts>().GetForPaymentAsync(e.Id, 1, Guid.NewGuid(), "OP-TESTE"));
        result.IsFailure.Should().BeTrue();
        result.Error!.Type.Should().Be(ErrorType.Forbidden);
        var summary = (await InTenantAsync(tenancy.TenantA, sp => sp.GetRequiredService<IEmployeeBankAccounts>().SummaryAsync([e.Id]))).Single();
        summary.AccountTail.Should().Be("****-71");
        summary.Version.Should().Be(1);
        summary.Should().NotBeNull();
        System.Text.Json.JsonSerializer.Serialize(summary).Should().NotContain(AccountCanary);
    }
}
