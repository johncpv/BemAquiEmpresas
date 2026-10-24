using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Finance.Contracts;
using BemAqui.Host.Persistence;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BemAqui.Integration.Tests.Finance;

/// <summary>
/// CMP-03a CA 17 a 19 (ADR 0024 Â§2): <c>IPayables</c> por parcela. A migraÃ§Ã£o do Ã­ndice Ã© testada sobre um banco no esquema anterior com
/// tÃ­tulos que imitam os do tenant <c>konebras</c> (instalador da COM-06, recorrente e manual da FIN-02); os mÃ©todos novos e os antigos,
/// sobre o banco do tenant de teste. Banco desta classe.
/// </summary>
public class PayablesInstallmentTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string BeforeOrders = "20261023223150_PurchasingRequests";
    private const string StepOne = "20261024023142_PurchaseOrders";

    private async Task<AsyncServiceScope> ScopeAsync()
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        return await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
    }

    private static TenantDbContext Context(string connection) =>
        new(new DbContextOptionsBuilder<TenantDbContext>().UseNpgsql(connection).UseSnakeCaseNamingConvention().Options);

    private static async Task InsertLegacyAsync(NpgsqlConnection connection, Guid id, string number, string origin, Guid? originId, string status, decimal amount, DateOnly? due, bool estimate,
        Guid? recurring = null, DateOnly? competence = null)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO finance.payables (id, number, party_id, description, account_id, amount, retention_iss, retention_inss, retention_ir, retention_pcc, paid_amount, issue_date,
                due_date, is_estimate, status, origin, origin_id, recurring_expense_id, competence, created_at)
            VALUES (@id, @number, @party, 'TÃ­tulo legado', @account, @amount, 0, 0, 0, 0, 0, @issue, @due, @estimate, @status, @origin, @originId, @recurring, @competence, now())
            """, connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("number", number);
        command.Parameters.AddWithValue("party", Guid.CreateVersion7());
        command.Parameters.AddWithValue("account", Guid.CreateVersion7());
        command.Parameters.AddWithValue("amount", amount);
        command.Parameters.AddWithValue("issue", new DateOnly(2026, 9, 1));
        command.Parameters.AddWithValue("due", (object?)due ?? DBNull.Value);
        command.Parameters.AddWithValue("estimate", estimate);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("origin", origin);
        command.Parameters.AddWithValue("originId", (object?)originId ?? DBNull.Value);
        command.Parameters.AddWithValue("recurring", (object?)recurring ?? DBNull.Value);
        command.Parameters.AddWithValue("competence", (object?)competence ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// CA 17 e 18: no esquema anterior, tÃ­tulos de origem existentes; o passo 1 cria <c>installment = 1</c> e o Ã­ndice novo mantendo o antigo
    /// (a versÃ£o anterior continua valendo: um segundo tÃ­tulo da mesma origem ainda Ã© recusado); o passo 2 remove o antigo e libera as
    /// parcelas, sem duplicar nem mudar valores, situaÃ§Ãµes e vencimentos.
    /// </summary>
    [Fact]
    public async Task Migracao_em_dois_passos_preserva_os_titulos_de_origem()
    {
        // Banco novo com o usuÃ¡rio do container (o mesmo do catÃ¡logo), migrado atÃ© antes da CMP-03a.
        var name = $"cmp03a_{Guid.CreateVersion7():N}";
        await using (var admin = new NpgsqlConnection(tenancy.CatalogConnection))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin);
            await create.ExecuteNonQueryAsync();
        }

        var connection = new NpgsqlConnectionStringBuilder(tenancy.CatalogConnection) { Database = name }.ConnectionString;
        await using var db = Context(connection);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(BeforeOrders);

        var installer = (Id: Guid.CreateVersion7(), Origin: Guid.CreateVersion7());
        var recurring = (Id: Guid.CreateVersion7(), Expense: Guid.CreateVersion7());
        var manual = Guid.CreateVersion7();
        await using (var raw = new NpgsqlConnection(connection))
        {
            await raw.OpenAsync();
            await InsertLegacyAsync(raw, installer.Id, "CP-2026-00001", "Installer", installer.Origin, "Planned", 16_500m, new DateOnly(2026, 12, 10), true);
            await InsertLegacyAsync(raw, recurring.Id, "CP-2026-00002", "Recurring", null, "Open", 1_200m, new DateOnly(2026, 11, 5), false, recurring.Expense, new DateOnly(2026, 11, 1));
            await InsertLegacyAsync(raw, manual, "CP-2026-00003", "Manual", null, "Paid", 300m, new DateOnly(2026, 10, 1), false);
        }

        var before = new List<LegacyRow>();
        await using (var raw = new NpgsqlConnection(connection))
        {
            await raw.OpenAsync();
            await using var select = new NpgsqlCommand("SELECT id, number, status, amount, due_date, is_estimate FROM finance.payables ORDER BY number", raw);
            await using var reader = await select.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                before.Add(new LegacyRow(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetDecimal(3), reader.IsDBNull(4) ? null : reader.GetFieldValue<DateOnly>(4), reader.GetBoolean(5)));
            }
        }

        before.Should().HaveCount(3);

        await migrator.MigrateAsync(StepOne);
        var duplicate = async () =>
        {
            await using var raw = new NpgsqlConnection(connection);
            await raw.OpenAsync();
            await using var insert = new NpgsqlCommand(
                "INSERT INTO finance.payables (id, number, party_id, description, account_id, amount, retention_iss, retention_inss, retention_ir, retention_pcc, paid_amount, issue_date, is_estimate, status, origin, origin_id, installment, created_at) " +
                "VALUES (@id, 'CP-2026-00004', @p, 'Parcela 2', @a, 10, 0, 0, 0, 0, 0, '2026-10-01', true, 'Planned', 'Installer', @o, 2, now())", raw);
            insert.Parameters.AddWithValue("id", Guid.CreateVersion7());
            insert.Parameters.AddWithValue("p", Guid.CreateVersion7());
            insert.Parameters.AddWithValue("a", Guid.CreateVersion7());
            insert.Parameters.AddWithValue("o", installer.Origin);
            await insert.ExecuteNonQueryAsync();
        };
        await duplicate.Should().ThrowAsync<PostgresException>("entre os dois passos o Ã­ndice antigo (origin, origin_id) continua valendo");

        await db.Database.MigrateAsync();

        var after = await db.Payables.AsNoTracking().OrderBy(p => p.Number).ToListAsync();
        after.Should().HaveCount(3, "a migraÃ§Ã£o nÃ£o duplica");
        after.Should().OnlyContain(p => p.Installment == 1);
        after.Select(p => (p.Id, p.Number, p.Status.ToString(), p.Amount, p.DueDate, p.IsEstimate))
            .Should().Equal(before.Select(b => (b.Id, b.Number, b.Status, b.Amount, b.DueDate, b.IsEstimate)), "mesmos valores, situaÃ§Ãµes e vencimentos");
        await duplicate.Should().NotThrowAsync("com o Ã­ndice antigo removido, a parcela 2 da mesma origem entra");
    }

    private sealed record LegacyRow(Guid Id, string Number, string Status, decimal Amount, DateOnly? DueDate, bool IsEstimate);

    /// <summary>CA 17 e 18: os mÃ©todos de uma parcela continuam valendo com installment = 1 (idempotente, busca pela origem, cancelar).</summary>
    [Fact]
    public async Task Metodos_de_uma_parcela_continuam_com_installment_1()
    {
        await using var scope = await ScopeAsync();
        var payables = scope.ServiceProvider.GetRequiredService<IPayables>();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var origin = Guid.CreateVersion7();
        var party = Guid.CreateVersion7();

        var first = await payables.CreateFromOriginAsync(DocumentOrigin.Installer, origin, party, null, 900m, Today().AddDays(30), true, "Instalador", planned: true);
        await db.SaveChangesAsync();
        var again = await payables.CreateFromOriginAsync(DocumentOrigin.Installer, origin, party, null, 900m, Today().AddDays(30), true, "Instalador", planned: true);

        again.Value.Id.Should().Be(first.Value.Id, "idempotente por (origin, originId, 1)");
        first.Value.Installment.Should().Be(1);
        (await payables.FindByOriginAsync(DocumentOrigin.Installer, origin))!.Id.Should().Be(first.Value.Id);
        var installer = await db.Payables.AsNoTracking().SingleAsync(p => p.Id == first.Value.Id);
        installer.CostType.Should().BeNull("sem projeto nÃ£o hÃ¡ natureza");
        (await db.Accounts.AsNoTracking().SingleAsync(a => a.Id == installer.AccountId)).Code.Should().Be("2.3", "a origem antiga mantÃ©m a conta de hoje (CA 19)");
        (await payables.CancelFromOriginAsync(DocumentOrigin.Installer, origin, "OS cancelada")).IsSuccess.Should().BeTrue();
        await db.SaveChangesAsync();
        (await db.Payables.AsNoTracking().SingleAsync(p => p.Id == first.Value.Id)).Status.Should().Be(PayableStatus.Cancelled);
    }

    /// <summary>
    /// CA 19: criar por parcela (conta e natureza do chamador, "Estimado", idempotente), trocar as parcelas e cancelar tudo pela origem; tÃ­tulo
    /// pago ou em OP recusa a troca e o cancelamento.
    /// </summary>
    [Fact]
    public async Task Parcelas_previstas_criar_trocar_e_cancelar_pela_origem()
    {
        await using var scope = await ScopeAsync();
        var payables = scope.ServiceProvider.GetRequiredService<IPayables>();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var origin = Guid.CreateVersion7();
        var party = Guid.CreateVersion7();
        var project = Guid.CreateVersion7();
        var account = await db.Accounts.Where(a => a.Code == "2.4").Select(a => a.Id).SingleAsync();
        var plan = new PlannedPayables(party, project, account, CostType.Freight,
            [new PlannedInstallment(1, 100m, Today().AddDays(30), "Pedido X â€” parcela 1/2"), new PlannedInstallment(2, 100.01m, Today().AddDays(60), "Pedido X â€” parcela 2/2")]);

        (await payables.CreatePlannedInstallmentsAsync(DocumentOrigin.Purchase, origin, plan)).IsSuccess.Should().BeTrue();
        await db.SaveChangesAsync();
        (await payables.CreatePlannedInstallmentsAsync(DocumentOrigin.Purchase, origin, plan)).Value.Should().HaveCount(2);
        await db.SaveChangesAsync();

        var created = await db.Payables.AsNoTracking().Where(p => p.Origin == DocumentOrigin.Purchase && p.OriginId == origin).OrderBy(p => p.Installment).ToListAsync();
        created.Should().HaveCount(2, "idempotente por parcela");
        created.Should().OnlyContain(p => p.Status == PayableStatus.Planned && p.IsEstimate && p.AccountId == account && p.CostType == CostType.Freight && p.ProjectId == project);

        var replaced = await payables.ReplacePlannedAsync(DocumentOrigin.Purchase, origin, plan with { Installments = [new PlannedInstallment(1, 200.01m, Today().AddDays(10), "Pedido X â€” parcela 1/1")] });
        replaced.IsSuccess.Should().BeTrue();
        await db.SaveChangesAsync();
        var afterReplace = await db.Payables.AsNoTracking().Where(p => p.Origin == DocumentOrigin.Purchase && p.OriginId == origin).OrderBy(p => p.Installment).ToListAsync();
        afterReplace.Single(p => p.Installment == 1).Amount.Should().Be(200.01m);
        afterReplace.Single(p => p.Installment == 1).Id.Should().Be(created[0].Id);
        afterReplace.Single(p => p.Installment == 2).Status.Should().Be(PayableStatus.Cancelled);

        var listed = await payables.ListByOriginAsync(DocumentOrigin.Purchase, origin);
        listed.Select(p => p.Installment).Should().Equal((short)1, (short)2);

        // TÃ­tulo numa OP: a troca e o cancelamento sÃ£o recusados (409 payable_in_payment_order), nada muda.
        var first = await db.Payables.SingleAsync(p => p.Id == created[0].Id);
        first.Status = PayableStatus.AwaitingApproval;
        await db.SaveChangesAsync();
        (await payables.CancelAllFromOriginAsync(DocumentOrigin.Purchase, origin, "Pedido cancelado")).Error!.Code.Should().Be("payable_in_payment_order");
        (await payables.ReplacePlannedAsync(DocumentOrigin.Purchase, origin, plan)).Error!.Code.Should().Be("payable_in_payment_order");
        first.Status = PayableStatus.Planned;
        await db.SaveChangesAsync();

        (await payables.CancelAllFromOriginAsync(DocumentOrigin.Purchase, origin, "Pedido cancelado")).IsSuccess.Should().BeTrue();
        await db.SaveChangesAsync();
        (await db.Payables.AsNoTracking().Where(p => p.Origin == DocumentOrigin.Purchase && p.OriginId == origin).ToListAsync()).Should().OnlyContain(p => p.Status == PayableStatus.Cancelled);

        // Cancelado nÃ£o ocupa a chave: a parcela 1 pode nascer de novo (replanejamento depois do estorno, ADR 0024 Â§2).
        (await payables.CreatePlannedInstallmentsAsync(DocumentOrigin.Purchase, origin, plan)).IsSuccess.Should().BeTrue();
        await db.SaveChangesAsync();
        (await db.Payables.AsNoTracking().CountAsync(p => p.Origin == DocumentOrigin.Purchase && p.OriginId == origin && p.Status != PayableStatus.Cancelled)).Should().Be(2);
    }

    private static DateOnly Today() => BemAqui.BuildingBlocks.Time.BusinessTime.Today(TimeProvider.System);
}
