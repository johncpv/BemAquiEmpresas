using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Security;
using BemAqui.Finance.Application;
using BemAqui.Finance.Contracts;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Finance;

/// <summary>FIN-10a: autoaprovação da OP por modo (Nunca, Só o Proprietário, Qualquer aprovador), selo e troca do parâmetro.</summary>
public partial class PaymentOrderTests
{
    private const string Parameters = "/api/v1/finance/parameters";

    /// <summary>Grava o modo direto no banco (como estaria num tenant antigo ou já configurado), sem passar pela troca com código.</summary>
    private Task<int> SetModeAsync(SelfApprovalMode mode) => WithDbAsync(async db =>
    {
        var parameter = await db.FinanceParameters.SingleAsync(p => p.Key == FinanceParametersService.OwnerCanApproveOwn);
        parameter.Value = (int)mode;
        return await db.SaveChangesAsync();
    });

    private Task<decimal> StoredModeAsync() =>
        WithDbAsync(db => db.FinanceParameters.Where(p => p.Key == FinanceParametersService.OwnerCanApproveOwn).Select(p => p.Value).SingleAsync());

    /// <summary>Proprietário com o 2FA ligado e um gerador de código (cada chamada libera o passo atual de novo; o anti-replay aceita uma vez).</summary>
    private async Task<(Guid Id, HttpClient Client, Func<Task<string>> Code)> OwnerWithTotpAsync()
    {
        var (id, _) = await ActorAsync(Group.OwnerSeedKey);
        var secret = Totp.NewSecret();
        var protector = tenancy.Factory.Services.GetRequiredService<ISecretProtector>();
        var stamp = await WithDbAsync(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Id == id);
            user.BeginTotpSetup(protector.Protect(SecretScope.ForTenant(tenancy.TenantA.Id), SecretKind.TotpSecret, new Secret(Base32.Encode(secret))));
            user.EnableTotp(Totp.StepAt(DateTimeOffset.UtcNow) - 10);
            await db.SaveChangesAsync();
            return user.SecurityStamp;
        });
        var client = tenancy.Factory.CreateClient();
        client.DefaultRequestHeaders.Host = Host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenancy.TenantA.Id, Host, userId: id, stamp: stamp, otpAt: DateTimeOffset.UtcNow));
        async Task<string> Code()
        {
            var step = Totp.StepAt(DateTimeOffset.UtcNow);
            await WithDbAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE identity.users SET totp_last_step = {step - 10} WHERE id = {id}"));
            return Totp.Code(secret, step);
        }

        return (id, client, Code);
    }

    /// <summary>
    /// Usuário fora do Proprietário num grupo próprio com Caixa e pagamentos ver, criar, editar e aprovar (o "Qualquer aprovador") e,
    /// com <paramref name="settingsEdit"/>, Configurações ver e editar.
    /// </summary>
    private async Task<(Guid Id, HttpClient Client, Guid GroupId)> ApproverOutsideOwnerAsync(bool settingsEdit = false)
    {
        var (id, client) = await ActorAsync(null);
        var groupId = await WithDbAsync(async db =>
        {
            var group = new Group { Name = "Aprovadores " + Guid.NewGuid().ToString("N")[..6], Description = "FIN-10a" };
            db.Groups.Add(group);
            db.GroupModules.Add(new GroupModule { GroupId = group.Id, ModuleCode = "finance", Enabled = true });
            foreach (var action in new[] { "view", "create", "edit", "approve" })
            {
                db.GroupPermissions.Add(new GroupGrant { GroupId = group.Id, ModuleCode = "finance", Action = action });
            }

            if (settingsEdit)
            {
                db.GroupModules.Add(new GroupModule { GroupId = group.Id, ModuleCode = "settings", Enabled = true });
                db.GroupPermissions.Add(new GroupGrant { GroupId = group.Id, ModuleCode = "settings", Action = "view" });
                db.GroupPermissions.Add(new GroupGrant { GroupId = group.Id, ModuleCode = "settings", Action = "edit" });
            }

            db.UserGroups.Add(new UserGroup { UserId = id, GroupId = group.Id });
            await db.SaveChangesAsync();
            return group.Id;
        });
        await BumpAsync(id);
        return (id, client, groupId);
    }

    private async Task BumpAsync(Guid userId)
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<BemAqui.BuildingBlocks.Tenancy.ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        await using var scope = await tenancy.Factory.Services.GetRequiredService<BemAqui.BuildingBlocks.Tenancy.ITenantScopeFactory>().CreateScopeAsync(info);
        await scope.ServiceProvider.GetRequiredService<IPermissionService>().BumpAsync([userId]);
    }

    private static Dictionary<string, object?> LimitsBody(JsonElement limits, params (string Key, object? Value)[] extra)
    {
        var body = new Dictionary<string, object?>
        {
            ["discountApprovalPct"] = limits.GetProperty("discountApprovalPct").GetDecimal(),
            ["discountApprovalAmount"] = limits.GetProperty("discountApprovalAmount").GetDecimal(),
            ["cancelApprovalAmount"] = limits.GetProperty("cancelApprovalAmount").GetDecimal(),
            ["backdateDays"] = limits.GetProperty("backdateDays").GetInt32(),
            ["paymentTolerance"] = limits.GetProperty("paymentTolerance").GetDecimal(),
            ["transferNotifyAmount"] = limits.GetProperty("transferNotifyAmount").GetDecimal(),
        };
        foreach (var (key, value) in extra)
        {
            body[key] = value;
        }

        return body;
    }

    private async Task<(Guid Id, JsonElement Order)> OwnOrderAsync(HttpClient requester, HttpClient owner, int payables = 1)
    {
        var supplier = await SupplierAsync(owner);
        var items = new List<(Guid, decimal)>();
        for (var i = 0; i < payables; i++)
        {
            items.Add(((await PayableAsync(requester, supplier)).GetProperty("id").GetGuid(), 950m));
        }

        var submitted = await requester.SendAsync(Post(Base, Items([.. items])));
        submitted.StatusCode.Should().Be(HttpStatusCode.Created, await submitted.Content.ReadAsStringAsync());
        var order = await JsonAsync(submitted);
        return (order.GetProperty("summary").GetProperty("id").GetGuid(), order);
    }

    private static Task<HttpResponseMessage> ApproveAsync(HttpClient client, Guid id, JsonElement order) =>
        client.PostAsJsonAsync($"{Base}/{id}/approve", new { comment = (string?)null, version = Version(order) });

    [Fact]
    public async Task Autoaprovacao_da_OP_segue_o_modo_e_grava_selo_e_modo()
    {
        var (ownerId, owner) = await ActorAsync(Group.OwnerSeedKey);
        await ActorAsync(Group.OwnerSeedKey); // Proprietário com mais de um membro
        var (_, financeNoApprove) = await ActorAsync("finance");
        var (outsiderId, outsider, outsiderGroup) = await ApproverOutsideOwnerAsync();
        try
        {
            // CA 4: Nunca, com mais de um Proprietário, recusa o solicitante.
            await SetModeAsync(SelfApprovalMode.Never);
            var (neverId, never) = await OwnOrderAsync(owner, owner);
            never.GetProperty("canDecide").GetBoolean().Should().BeFalse();
            Code(await JsonAsync(await ApproveAsync(owner, neverId, never))).Should().Be("own_payment_order");

            // CA 5: Só o Proprietário libera quem é do grupo; fora dele, mesmo com finance.approve, não.
            await SetModeAsync(SelfApprovalMode.OwnerOnly);
            (await JsonAsync(await owner.GetAsync($"{Base}/{neverId}"))).GetProperty("canDecide").GetBoolean().Should().BeTrue();
            var approved = await JsonAsync(await ApproveAsync(owner, neverId, never));
            approved.GetProperty("summary").GetProperty("selfApproved").GetBoolean().Should().BeTrue();
            approved.GetProperty("decisions").EnumerateArray().Last().GetProperty("selfApproved").GetBoolean().Should().BeTrue();
            var decision = await WithDbAsync(db => db.PaymentOrderApprovals.SingleAsync(a => a.PaymentOrderId == neverId && a.Kind == BemAqui.Finance.Domain.PaymentOrderDecisionKind.Decision));
            decision.SelfApprovalMode.Should().Be(SelfApprovalMode.OwnerOnly, "CA 8: o modo vigente fica na decisão");
            decision.DecidedBy.Should().Be(ownerId);
            (await WithDbAsync(db => db.AuditEntries.AsNoTracking().Where(a => a.Entity == "PaymentOrderApproval" && a.EntityId == decision.Id.ToString() && a.UserId == ownerId).ToListAsync()))
                .Should().Contain(a => a.Changes.Contains("selfApprovalMode", StringComparison.OrdinalIgnoreCase), "a auditoria da decisão registra quem e o modo");
            var (outsiderOrderId, outsiderOrder) = await OwnOrderAsync(outsider, owner);
            Code(await JsonAsync(await ApproveAsync(outsider, outsiderOrderId, outsiderOrder))).Should().Be("own_payment_order");

            // CA 6: Qualquer aprovador; fora da regra do nível 1, o erro de alçada; sem finance.approve, forbidden.
            await SetModeAsync(SelfApprovalMode.AnyApprover);
            Code(await JsonAsync(await ApproveAsync(outsider, outsiderOrderId, outsiderOrder))).Should().Be("outside_approval_rule");
            var (financeOrderId, financeOrder) = await OwnOrderAsync(financeNoApprove, owner);
            var noPermission = await ApproveAsync(financeNoApprove, financeOrderId, financeOrder);
            noPermission.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            Code(await JsonAsync(noPermission)).Should().Be(Error.Codes.Forbidden);

            // Com o grupo do solicitante na regra do nível 1, ele aprova a própria (o mesmo nível de qualquer aprovador da regra).
            var rules = await JsonAsync(await owner.GetAsync("/api/v1/finance/approval-rules"));
            var rule = rules.EnumerateArray().Single(r => r.GetProperty("isDefault").GetBoolean());
            var ruleId = rule.GetProperty("id").GetGuid();
            object RuleBody(Guid group) => new { minAmount = rule.GetProperty("minAmount").GetDecimal(), maxAmount = (decimal?)null, accountId = (Guid?)null, approverGroupId = group, approverUserId = (Guid?)null, level = 1 };
            var ownerGroup = rule.GetProperty("approverGroupId").GetGuid();
            (await owner.PutAsJsonAsync($"/api/v1/finance/approval-rules/{ruleId}", RuleBody(outsiderGroup))).StatusCode.Should().Be(HttpStatusCode.OK);
            try
            {
                var (anyId, any) = await OwnOrderAsync(outsider, owner);
                var self = await ApproveAsync(outsider, anyId, any);
                self.StatusCode.Should().Be(HttpStatusCode.OK, await self.Content.ReadAsStringAsync());
                (await JsonAsync(self)).GetProperty("summary").GetProperty("selfApproved").GetBoolean().Should().BeTrue();
                (await WithDbAsync(db => db.PaymentOrderApprovals.Where(a => a.PaymentOrderId == anyId && a.Kind == BemAqui.Finance.Domain.PaymentOrderDecisionKind.Decision).Select(a => a.SelfApprovalMode).SingleAsync()))
                    .Should().Be(SelfApprovalMode.AnyApprover);
            }
            finally
            {
                (await owner.PutAsJsonAsync($"/api/v1/finance/approval-rules/{ruleId}", RuleBody(ownerGroup))).StatusCode.Should().Be(HttpStatusCode.OK);
            }

            // CA 9: pagar a autoaprovada continua pedindo o 2FA recente.
            var bank = await BankAsync(owner);
            var (_, stale) = await ActorAsync(Group.OwnerSeedKey, recentTwoFactor: false);
            var pay = await stale.SendAsync(Post($"{Base}/{neverId}/pay", new { bankAccountId = bank, date = Iso(Today), items = Array.Empty<object>(), backdateReason = (string?)null, version = Version(approved) }));
            pay.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            // CA 10: PDF e exportação mostram o selo; o filtro continua.
            (await PdfAsync(owner, neverId)).Should().Contain(PayablesTests.Flat("Aprovada pelo próprio solicitante"));
            var number = approved.GetProperty("summary").GetProperty("number").GetString()!;
            var exported = await owner.GetAsync($"{Base}/export?format=pdf&selfApproved=true");
            exported.StatusCode.Should().Be(HttpStatusCode.OK, await exported.Content.ReadAsStringAsync());
            PdfText(await exported.Content.ReadAsByteArrayAsync()).Should().Contain("Autoaprovada").And.Contain(number).And.Contain("Sim");
            (await JsonAsync(await owner.GetAsync($"{Base}?selfApproved=true"))).GetProperty("items").EnumerateArray()
                .Should().Contain(i => i.GetProperty("id").GetGuid() == neverId);

            // CA 11: reenviar zera só o selo corrente da OP; a decisão histórica continua com o selo e o modo.
            var item = approved.GetProperty("items")[0].GetProperty("id").GetGuid();
            var resubmitted = await JsonAsync(await owner.PostAsJsonAsync($"{Base}/{neverId}/resubmit",
                new { items = new[] { new { itemId = item, interest = 10m, fine = 0m, discount = 0m } }, version = Version(approved) }));
            resubmitted.GetProperty("summary").GetProperty("status").GetString().Should().Be("Pending");
            resubmitted.GetProperty("summary").GetProperty("selfApproved").GetBoolean().Should().BeFalse();
            resubmitted.GetProperty("decisions").EnumerateArray().Should().Contain(d => d.GetProperty("selfApproved").GetBoolean());
            (await WithDbAsync(db => db.PaymentOrderApprovals.CountAsync(a => a.PaymentOrderId == neverId && a.SelfApproved && a.SelfApprovalMode == SelfApprovalMode.OwnerOnly))).Should().Be(1);
            _ = outsiderId;
        }
        finally
        {
            await SetModeAsync(SelfApprovalMode.Never);
        }
    }

    [Fact]
    public async Task Alcada_de_dois_niveis_autoaprova_so_o_nivel_1_e_ninguem_decide_dois_niveis()
    {
        var (_, requester) = await ActorAsync(Group.OwnerSeedKey);
        var (level2Id, level2) = await ActorAsync(Group.OwnerSeedKey);
        await SetModeAsync(SelfApprovalMode.AnyApprover);
        var created = await requester.PostAsJsonAsync("/api/v1/finance/approval-rules",
            new { minAmount = 4_000.01m, maxAmount = (decimal?)null, accountId = (Guid?)null, approverGroupId = (Guid?)null, approverUserId = level2Id, level = 2 });
        created.StatusCode.Should().Be(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var ruleId = (await JsonAsync(created)).GetProperty("id").GetGuid();
        try
        {
            var (id, order) = await OwnOrderAsync(requester, requester, payables: 5); // 4.750,00: dois níveis
            order.GetProperty("levels").GetArrayLength().Should().Be(2);
            var first = await ApproveAsync(requester, id, order);
            first.StatusCode.Should().Be(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
            order = await JsonAsync(first);
            order.GetProperty("summary").GetProperty("status").GetString().Should().Be("Pending");
            order.GetProperty("canDecide").GetBoolean().Should().BeFalse("quem decidiu o nível 1 não decide o 2");
            var again = await ApproveAsync(requester, id, order);
            again.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            Code(await JsonAsync(again)).Should().Be("already_decided_level");

            var last = await JsonAsync(await ApproveAsync(level2, id, order));
            last.GetProperty("summary").GetProperty("status").GetString().Should().Be("Approved");
            last.GetProperty("summary").GetProperty("selfApproved").GetBoolean().Should().BeTrue("o nível 1 foi do próprio solicitante");
        }
        finally
        {
            await requester.DeleteAsync($"/api/v1/finance/approval-rules/{ruleId}");
            await SetModeAsync(SelfApprovalMode.Never);
        }
    }

    [Fact]
    public async Task Trocar_o_modo_pede_Proprietario_2FA_e_codigo_de_uso_unico_e_avisa_os_outros()
    {
        await SetModeAsync(SelfApprovalMode.Never);
        var (authorId, author, code) = await OwnerWithTotpAsync();
        var (otherOwnerId, _) = await ActorAsync(Group.OwnerSeedKey);
        var (_, noTotpOwner) = await ActorAsync(Group.OwnerSeedKey);
        var (_, outsider, _) = await ApproverOutsideOwnerAsync(settingsEdit: true); // edita Configurações, mas não é do Proprietário
        var limits = await JsonAsync(await author.GetAsync(Parameters));
        limits.GetProperty("selfApprovalMode").GetString().Should().Be("Never");
        try
        {
            // CA 13: com settings.edit, mas fora do Proprietário, não muda o modo; salva os demais limites.
            var notOwner = await outsider.PutAsJsonAsync(Parameters, LimitsBody(limits, ("selfApprovalMode", "OwnerOnly"), ("code", await code())));
            notOwner.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await JsonAsync(notOwner)).GetProperty("detail").GetString().Should().Be("Só o Proprietário muda esta regra.");
            (await outsider.PutAsJsonAsync(Parameters, LimitsBody(limits))).StatusCode.Should().Be(HttpStatusCode.OK);

            // "Salvar limites" sem trocar o modo não pede código.
            var plain = await author.PutAsJsonAsync(Parameters, LimitsBody(limits, ("backdateDays", 8)));
            plain.StatusCode.Should().Be(HttpStatusCode.OK, await plain.Content.ReadAsStringAsync());

            // CA 14: sem 2FA ativo, sem código, código errado; nada gravado.
            var noTotp = await noTotpOwner.PutAsJsonAsync(Parameters, LimitsBody(limits, ("selfApprovalMode", "OwnerOnly"), ("code", "123456")));
            var noTotpBody = await JsonAsync(noTotp);
            noTotp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            Code(noTotpBody).Should().Be("two_factor_required");
            noTotpBody.GetProperty("detail").GetString().Should().Be("Para continuar, ative a verificação em duas etapas.");
            Code(await JsonAsync(await author.PutAsJsonAsync(Parameters, LimitsBody(limits, ("selfApprovalMode", "OwnerOnly"))))).Should().Be("two_factor_required");
            Code(await JsonAsync(await author.PutAsJsonAsync(Parameters, LimitsBody(limits, ("selfApprovalMode", "OwnerOnly"), ("code", "000000"))))).Should().Be("two_factor_invalid");
            (await StoredModeAsync()).Should().Be(0m);

            // CA 15 e 16: o booleano legado liga "Só o Proprietário" com o código; auditoria antes/depois e aviso só aos outros.
            var once = await code();
            var on = await author.PutAsJsonAsync(Parameters, LimitsBody(limits, ("ownerCanApproveOwn", true), ("selfApprovalModeSeen", "Never"), ("code", once)));
            on.StatusCode.Should().Be(HttpStatusCode.OK, await on.Content.ReadAsStringAsync());
            (await JsonAsync(on)).GetProperty("selfApprovalMode").GetString().Should().Be("OwnerOnly");
            const string title = "mudou \"Quem solicitou a ordem pode aprová-la?\" de Nunca para Só o Proprietário.";
            (await WithDbAsync(db => db.Notifications.CountAsync(n => n.UserId == otherOwnerId && n.Title.EndsWith(title)))).Should().Be(1);
            (await WithDbAsync(db => db.Notifications.CountAsync(n => n.UserId == authorId && n.Title.Contains("Quem solicitou a ordem")))).Should().Be(0, "quem trocou não se avisa");
            var parameterId = await WithDbAsync(db => db.FinanceParameters.Where(p => p.Key == FinanceParametersService.OwnerCanApproveOwn).Select(p => p.Id).SingleAsync());
            (await WithDbAsync(db => db.AuditEntries.CountAsync(a => a.Entity == "FinanceParameter" && a.EntityId == parameterId.ToString() && a.Action == AuditActions.Updated && a.UserId == authorId)))
                .Should().BeGreaterThan(0);

            // Código repetido: recusado.
            var reused = await author.PutAsJsonAsync(Parameters, LimitsBody(limits, ("selfApprovalMode", "AnyApprover"), ("code", once)));
            Code(await JsonAsync(reused)).Should().Be("two_factor_invalid");

            // O booleano legado nunca rebaixa "Qualquer aprovador".
            (await author.PutAsJsonAsync(Parameters, LimitsBody(limits, ("selfApprovalMode", "AnyApprover"), ("code", await code())))).StatusCode.Should().Be(HttpStatusCode.OK);
            var legacy = await author.PutAsJsonAsync(Parameters, LimitsBody(limits, ("ownerCanApproveOwn", false)));
            legacy.StatusCode.Should().Be(HttpStatusCode.OK, await legacy.Content.ReadAsStringAsync());
            (await StoredModeAsync()).Should().Be(2m);
        }
        finally
        {
            await SetModeAsync(SelfApprovalMode.Never);
        }
    }

    [Fact]
    public async Task Troca_simultanea_ou_com_modo_visto_desatualizado_da_409()
    {
        await SetModeAsync(SelfApprovalMode.OwnerOnly);
        var (_, first, firstCode) = await OwnerWithTotpAsync();
        var (_, second, secondCode) = await OwnerWithTotpAsync();
        var limits = await JsonAsync(await first.GetAsync(Parameters));
        try
        {
            var stale = await first.PutAsJsonAsync(Parameters, LimitsBody(limits, ("selfApprovalMode", "AnyApprover"), ("selfApprovalModeSeen", "Never"), ("code", await firstCode())));
            stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
            Code(await JsonAsync(stale)).Should().Be("parameters_changed");

            var codes = (await firstCode(), await secondCode());
            var results = await Task.WhenAll(
                first.PutAsJsonAsync(Parameters, LimitsBody(limits, ("selfApprovalMode", "AnyApprover"), ("selfApprovalModeSeen", "OwnerOnly"), ("code", codes.Item1))),
                second.PutAsJsonAsync(Parameters, LimitsBody(limits, ("selfApprovalMode", "Never"), ("selfApprovalModeSeen", "OwnerOnly"), ("code", codes.Item2))));
            results.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.Conflict]);
            (await WithDbAsync(db => db.Notifications.CountAsync(n => n.Title.Contains("de Só o Proprietário para")))).Should().BeGreaterThan(0);
        }
        finally
        {
            await SetModeAsync(SelfApprovalMode.Never);
        }
    }
}
