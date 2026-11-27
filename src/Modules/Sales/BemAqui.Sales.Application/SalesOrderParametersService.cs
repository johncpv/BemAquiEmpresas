using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Contracts;
using BemAqui.Sales.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Sales.Application;

/// <summary>
/// Parâmetros do pedido de venda (VND-02a, Configurações → Parâmetros → Pedidos de venda) em <c>sales.order_parameters</c> (uma linha, auditada). Sem a
/// linha, valem os padrões; salvar exige <c>settings.edit</c>. A forma do boleto só aceita forma ativa do tipo <c>BankSlip</c> (CA 16).
/// </summary>
public sealed class SalesOrderParametersService(ISalesDb db, IPaymentMethods methods, IPermissionChecker permissions, TimeProvider clock)
{
    /// <summary>Os parâmetros vigentes (os padrões quando ninguém salvou).</summary>
    public async Task<SalesOrderParameters> CurrentAsync(CancellationToken cancellationToken = default) =>
        await db.SalesOrderParameters.AsNoTracking().OrderBy(p => p.Id).FirstOrDefaultAsync(cancellationToken) ?? new SalesOrderParameters();

    /// <summary>A forma de pagamento do boleto, se configurada e ainda ativa do tipo <c>BankSlip</c>.</summary>
    public async Task<PaymentMethodInfo?> BankSlipMethodAsync(CancellationToken cancellationToken = default)
    {
        var current = await CurrentAsync(cancellationToken);
        return current.BankSlipPaymentMethodId is { } id
            ? (await methods.ActiveAsync(BusinessTime.Today(clock), cancellationToken)).FirstOrDefault(m => m.Id == id && m.Type == PaymentMethodType.BankSlip)
            : null;
    }

    public async Task<SalesOrderParametersView> GetAsync(CancellationToken cancellationToken = default)
    {
        var stored = await db.SalesOrderParameters.AsNoTracking().OrderBy(p => p.Id).FirstOrDefaultAsync(cancellationToken);
        var p = stored ?? new SalesOrderParameters();
        var options = (await methods.ActiveAsync(BusinessTime.Today(clock), cancellationToken)).Where(m => m.Type == PaymentMethodType.BankSlip)
            .Select(m => new SalesOrderMethodOption(m.Id, m.Name)).OrderBy(m => m.Name, StringComparer.CurrentCulture).ToList();
        return new SalesOrderParametersView(p.DiscountLimitPct, p.AppDiscountLimitPct, p.HoldHours, p.AppHoldHours, p.ChargeDays, p.AppChargeDays, p.AppMaxLineQty, p.AppMaxOrderQty,
            p.AppUnpaidAlertCount, p.BankSlipPaymentMethodId, stored?.Id, options);
    }

    public async Task<Result<SalesOrderParametersView>> UpdateAsync(SalesOrderParametersInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await permissions.HasPermissionAsync("settings", "edit", cancellationToken))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Você não tem acesso a esta funcionalidade. Fale com o administrador.");
        }

        if (Validate(input) is { } invalid)
        {
            return invalid;
        }

        if (input.BankSlipPaymentMethodId is { } methodId
            && !(await methods.ActiveAsync(BusinessTime.Today(clock), cancellationToken)).Any(m => m.Id == methodId && m.Type == PaymentMethodType.BankSlip))
        {
            return Invalid("bankSlipPaymentMethodId", "Escolha uma forma de pagamento ativa do tipo \"Boleto com Pix\".");
        }

        var row = await db.SalesOrderParameters.OrderBy(p => p.Id).FirstOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            row = new SalesOrderParameters();
            db.SalesOrderParameters.Add(row);
        }

        row.DiscountLimitPct = input.DiscountLimitPct;
        row.AppDiscountLimitPct = input.AppDiscountLimitPct;
        row.HoldHours = input.HoldHours;
        row.AppHoldHours = input.AppHoldHours;
        row.ChargeDays = input.ChargeDays;
        row.AppChargeDays = input.AppChargeDays;
        row.AppMaxLineQty = input.AppMaxLineQty;
        row.AppMaxOrderQty = input.AppMaxOrderQty;
        row.AppUnpaidAlertCount = input.AppUnpaidAlertCount;
        row.BankSlipPaymentMethodId = input.BankSlipPaymentMethodId;
        row.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(cancellationToken);
    }

    /// <summary>Faixas aceitas de cada parâmetro (puro, testado na unidade).</summary>
    public static Error? Validate(SalesOrderParametersInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.DiscountLimitPct is < 0 or > 100 || input.AppDiscountLimitPct is < 0 or > 100)
        {
            return Invalid("discountLimitPct", "Os limites de desconto vão de 0% a 100%.");
        }

        if (input.HoldHours is < 1 or > 720 || input.AppHoldHours is < 1 or > 72)
        {
            return Invalid("holdHours", "O prazo da reserva vai de 1 a 720 horas (aplicativo: até 72 horas).");
        }

        if (input.ChargeDays is < 0 or > 30 || input.AppChargeDays is < 0 or > SalesOrderRules.MaxAppChargeDays)
        {
            return Invalid("chargeDays", $"O vencimento da cobrança vai de 0 a 30 dias úteis (aplicativo: até {SalesOrderRules.MaxAppChargeDays}).");
        }

        if (input.AppMaxLineQty <= 0 || input.AppMaxOrderQty < input.AppMaxLineQty || input.AppMaxOrderQty > 1_000_000)
        {
            return Invalid("appMaxLineQty", "A quantidade máxima por linha precisa ser maior que zero e não passar da quantidade máxima do pedido.");
        }

        return input.AppUnpaidAlertCount is < 1 or > 1000 ? Invalid("appUnpaidAlertCount", "O alerta de pedidos sem pagamento vai de 1 a 1.000.") : null;
    }

    private static Error Invalid(string field, string message) => Error.Validation(Error.Codes.Validation, message, new Dictionary<string, string[]> { [field] = [message] });
}
