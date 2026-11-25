using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>
/// Contas do caixa do PDV que o Financeiro cria quando faltam (como o Cofre e a Quebra de caixa): PDV-06, "Vales-troca" (o passivo dos vales em aberto,
/// saldo negativo) e "Devoluções de vendas" (o que voltou ao cliente em vale ou dinheiro). Tipo Caixa; a trava impede duas iguais em paralelo.
/// </summary>
public static class PosSpecialAccounts
{
    public const string StoreCreditSeedKey = "vales-troca";
    public const string StoreCreditName = "Vales-troca";
    public const string ReturnsSeedKey = "devolucoes-de-vendas";
    public const string ReturnsName = "Devoluções de vendas";

    public static async Task<Guid> EnsureAsync(IFinanceDb db, string seedKey, string name, TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(clock);
        var lockKey = "finance.pos-account:" + seedKey;
        await db.Database.SqlQuery<int>($"SELECT 1 AS \"Value\" FROM (SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))) AS l").ToListAsync(cancellationToken);
        var account = await db.BankAccounts.Where(a => a.IsActive && a.Kind == BankAccountKind.Cash && (a.SeedKey == seedKey || a.Name == name))
            .OrderByDescending(a => a.SeedKey == seedKey).ThenBy(a => a.CreatedAt).Select(a => (Guid?)a.Id).FirstOrDefaultAsync(cancellationToken);
        if (account is not null)
        {
            return account.Value;
        }

        var local = db.BankAccounts.Local.FirstOrDefault(a => a.IsActive && a.Kind == BankAccountKind.Cash && a.Name == name);
        if (local is not null)
        {
            return local.Id;
        }

        var created = new BankAccount { Name = name, Kind = BankAccountKind.Cash, CreatedAt = clock.GetUtcNow() };
        db.BankAccounts.Add(created);
        return created.Id;
    }
}
