using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>
/// Pacote do Comércio no Financeiro (PERF-04): contas, conta bancária quando falta e formas de pagamento. Fica fora do
/// <see cref="PaymentMethodService"/> porque roda no seed (provisionamento, <c>migrate</c> e aba Perfis), sem usuário.
/// </summary>
public sealed class RetailFinanceSeed(IFinanceDb db, BemAqui.BuildingBlocks.Messaging.IEventPublisher events, TimeProvider clock)
{
    /// <summary>Contas do pacote do Comércio (PERF-04 CA 1): dois caixas e a conta que recebe a sobra e a falta do fechamento (CX-03).</summary>
    public static readonly IReadOnlyList<(string Key, string Name)> RetailCashAccounts =
        [("caixa-pdv-01", "Caixa PDV 01"), ("cofre", "Cofre"), ("quebra-de-caixa", "Quebra de caixa")];

    /// <summary>Conta bancária criada só quando o tenant não tem nenhuma: Pix e cartões precisam cair numa conta do tipo Banco.</summary>
    public const string RetailBankKey = "banco-principal";

    public const string RetailBankName = "Conta bancária principal";

    /// <summary>Formas do pacote do Comércio: as sugeridas da PAG-01 e o Vale.</summary>
    public static readonly IReadOnlyList<(string Key, string Name, PaymentMethodType Type)> RetailMethods =
        [.. PaymentMethodService.Suggested, ("vale", "Vale", PaymentMethodType.Voucher)];

    /// <summary>
    /// Pacote do Comércio no Financeiro (PERF-04 CA 1 a 3). Cria o que falta pela <c>seed_key</c>, sem recriar o que o tenant excluiu e sem
    /// alterar nada que já existe (nome, conta, adquirente e conferência ficam como o tenant deixou). Conta com o mesmo nome de uma ativa não
    /// é duplicada. As formas nascem "A confirmar" (<c>ConfirmedAt</c> nulo) e sem taxa: taxa e prazo zerados são a ausência de taxa, como na
    /// PAG-01 (a venda entra pelo valor cheio até o lojista cadastrar a taxa da maquininha). Cartões e Vale apontam para a adquirente
    /// <paramref name="acquirerPartyId"/> ("Maquininha" do pacote dos Terceiros); sem ela, nascem sem adquirente, como as sugeridas.
    /// </summary>
    public async Task ApplyRetailPackageAsync(Guid? acquirerPartyId, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var seededAccounts = (await db.BankAccounts.IgnoreQueryFilters().Where(a => a.SeedKey != null).Select(a => a.SeedKey!).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);
        var activeAccounts = await db.BankAccounts.Where(a => a.IsActive).Select(a => new { a.Id, a.Name, a.Kind, a.IsDefault, a.SeedKey }).ToListAsync(cancellationToken);
        var names = activeAccounts.Select(a => a.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var created = new List<BankAccount>();
        foreach (var (key, name) in RetailCashAccounts.Where(a => !seededAccounts.Contains(a.Key) && !names.Contains(a.Name)))
        {
            var account = new BankAccount { SeedKey = key, Name = name, Kind = BankAccountKind.Cash, CreatedAt = now };
            db.BankAccounts.Add(account);
            created.Add(account);
        }

        // Dinheiro cai no "Caixa PDV 01" (o do seed ou um com o mesmo nome); sem ele, no primeiro caixa ativo.
        var cashName = RetailCashAccounts[0].Name;
        var cash = created.FirstOrDefault(a => a.SeedKey == RetailCashAccounts[0].Key)?.Id
            ?? activeAccounts.Where(a => a.Kind == BankAccountKind.Cash).OrderByDescending(a => a.SeedKey == RetailCashAccounts[0].Key || string.Equals(a.Name, cashName, StringComparison.OrdinalIgnoreCase))
                .ThenBy(a => a.Name, StringComparer.Ordinal).Select(a => (Guid?)a.Id).FirstOrDefault();

        var bank = activeAccounts.Where(a => a.Kind == BankAccountKind.Bank).OrderByDescending(a => a.IsDefault).ThenBy(a => a.Name, StringComparer.Ordinal)
            .Select(a => (Guid?)a.Id).FirstOrDefault();
        if (bank is null && !seededAccounts.Contains(RetailBankKey) && !names.Contains(RetailBankName))
        {
            var account = new BankAccount { SeedKey = RetailBankKey, Name = RetailBankName, Kind = BankAccountKind.Bank, IsDefault = !activeAccounts.Any(a => a.IsDefault), CreatedAt = now };
            db.BankAccounts.Add(account);
            bank = account.Id;
        }

        var seededMethods = (await db.PaymentMethods.IgnoreQueryFilters().Where(m => m.SeedKey != null).Select(m => m.SeedKey!).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var (key, name, type) in RetailMethods.Where(m => !seededMethods.Contains(m.Key)))
        {
            var destination = type == PaymentMethodType.Cash ? cash : bank;
            if (destination is null)
            {
                continue; // o tenant excluiu a conta do seed e não tem outra do tipo: a forma fica para o cadastro manual
            }

            var method = new PaymentMethod
            {
                SeedKey = key, Name = name, Type = type, DestinationAccountId = destination.Value,
                AcquirerPartyId = PaymentMethod.AcceptsAcquirer(type) ? acquirerPartyId : null,
                NfePaymentCode = PaymentMethod.DefaultNfeCode(type), AllowsChange = type == PaymentMethodType.Cash, CreatedAt = now,
            };
            db.PaymentMethods.Add(method);
            events.Publish(new PaymentMethodChanged(method.Id));
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
