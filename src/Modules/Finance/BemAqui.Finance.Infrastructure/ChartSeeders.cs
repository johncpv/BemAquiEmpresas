using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Finance.Application;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Finance.Infrastructure;

/// <summary>
/// Plano de contas v1 (CAD-05a CA 1 e 2, decidido em 18/10/2026): editável, sem esperar a contadora. Idempotente pela
/// <c>seed_key</c> (o código do seed), inclusive nos excluídos; o que o tenant mudou não volta ao padrão.
/// </summary>
public sealed class ChartOfAccountsSeeder : ITenantSeeder
{
    public static readonly IReadOnlyList<(string Code, string Name, AccountType Type, DreGroup Dre)> Seeds =
    [
        ("1", "Receitas", AccountType.Revenue, DreGroup.GrossRevenue),
        ("1.1", "Venda de equipamentos", AccountType.Revenue, DreGroup.GrossRevenue),
        ("1.2", "Serviços de instalação e assistência", AccountType.Revenue, DreGroup.GrossRevenue),
        ("1.3", "Receitas financeiras", AccountType.Revenue, DreGroup.FinancialResult),
        ("1.4", "Outras receitas", AccountType.Revenue, DreGroup.OtherResults),
        ("1.9", "Receitas a classificar", AccountType.Revenue, DreGroup.GrossRevenue),
        ("2", "Custos", AccountType.Expense, DreGroup.Costs),
        ("2.1", "Materiais", AccountType.Expense, DreGroup.Costs),
        ("2.2", "Mão de obra direta", AccountType.Expense, DreGroup.Costs),
        ("2.3", "Instalação terceirizada", AccountType.Expense, DreGroup.Costs),
        ("2.4", "Fretes", AccountType.Expense, DreGroup.Costs),
        ("2.5", "Impostos sobre vendas", AccountType.Expense, DreGroup.Deductions),
        ("2.6", "Outros custos", AccountType.Expense, DreGroup.Costs),
        ("3", "Despesas", AccountType.Expense, DreGroup.OperatingExpenses),
        ("3.1", "Pessoal administrativo", AccountType.Expense, DreGroup.OperatingExpenses),
        ("3.2", "Ocupação", AccountType.Expense, DreGroup.OperatingExpenses),
        ("3.3", "Administrativas", AccountType.Expense, DreGroup.OperatingExpenses),
        ("3.4", "Comerciais", AccountType.Expense, DreGroup.OperatingExpenses),
        ("3.5", "Financeiras", AccountType.Expense, DreGroup.FinancialResult),
        ("3.6", "Impostos e taxas", AccountType.Expense, DreGroup.OperatingExpenses),
        ("3.9", "Despesas a classificar", AccountType.Expense, DreGroup.OperatingExpenses),
        ("4", "Investimentos", AccountType.Asset, DreGroup.OutsideDre),
        ("4.1", "Investimentos e imobilizado", AccountType.Asset, DreGroup.OutsideDre),
        ("5", "Financiamentos e sócios", AccountType.Liability, DreGroup.OutsideDre),
        ("5.1", "Empréstimos e financiamentos", AccountType.Liability, DreGroup.OutsideDre),
        ("5.2", "Aportes e retiradas dos sócios", AccountType.Liability, DreGroup.OutsideDre),
    ];

    public int Order => 40;

    public async Task SeedAsync(IServiceProvider tenantScope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenantScope);
        var db = tenantScope.GetRequiredService<IFinanceDb>();
        var clock = tenantScope.GetRequiredService<TimeProvider>();
        var all = await db.Accounts.IgnoreQueryFilters().ToListAsync(cancellationToken);
        var seeded = all.Where(a => a.SeedKey != null).ToDictionary(a => a.SeedKey!, StringComparer.Ordinal);
        var codes = all.Where(a => !a.IsDeleted).Select(a => a.Code).ToHashSet(StringComparer.Ordinal);
        foreach (var (code, name, type, dre) in Seeds.Where(s => !seeded.ContainsKey(s.Code) && !codes.Contains(s.Code)))
        {
            var parentCode = TreeCode.ParentCode(code);
            var parent = parentCode is null ? null : seeded.GetValueOrDefault(parentCode);
            if (parentCode is not null && (parent is null || parent.IsDeleted))
            {
                // A raiz foi excluída (ou o código foi tomado à mão): as filhas do seed não voltam soltas.
                continue;
            }

            var account = new Account
            {
                SeedKey = code, Code = code, Name = name, Type = type, DreGroup = dre, ParentId = parent?.Id,
                AcceptsEntries = !Seeds.Any(s => TreeCode.ParentCode(s.Code) == code), CreatedAt = clock.GetUtcNow(),
            };
            db.Accounts.Add(account);
            seeded[code] = account;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Centros de custo do seed (CA 3): Administrativo, Comercial, Produção e Instalação, com as mesmas regras.</summary>
public sealed class CostCentersSeeder : ITenantSeeder
{
    public static readonly IReadOnlyList<(string Code, string Name)> Seeds = [("1", "Administrativo"), ("2", "Comercial"), ("3", "Produção"), ("4", "Instalação")];

    public int Order => 40;

    public async Task SeedAsync(IServiceProvider tenantScope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenantScope);
        var db = tenantScope.GetRequiredService<IFinanceDb>();
        var clock = tenantScope.GetRequiredService<TimeProvider>();
        var all = await db.CostCenters.IgnoreQueryFilters().ToListAsync(cancellationToken);
        var seeded = all.Where(c => c.SeedKey != null).Select(c => c.SeedKey!).ToHashSet(StringComparer.Ordinal);
        var codes = all.Where(c => !c.IsDeleted).Select(c => c.Code).ToHashSet(StringComparer.Ordinal);
        foreach (var (code, name) in Seeds.Where(s => !seeded.Contains(s.Code) && !codes.Contains(s.Code)))
        {
            db.CostCenters.Add(new CostCenter { SeedKey = code, Code = code, Name = name, CreatedAt = clock.GetUtcNow() });
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
