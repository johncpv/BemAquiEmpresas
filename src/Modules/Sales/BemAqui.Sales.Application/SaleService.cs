using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Contracts;
using BemAqui.Inventory.Contracts;
using BemAqui.Sales.Contracts;
using BemAqui.Sales.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Sales.Application;

/// <summary>
/// Núcleo de vendas (VND-01): ingestão da venda concluída (<see cref="ISaleIngestion"/>), pagamento de pedido (<see cref="IOrderPayments"/>) e
/// cancelamento. Grava só as tabelas <c>sales.*</c>, na transação de quem chama, e publica <see cref="SaleCompleted"/> e
/// <see cref="SaleCancelled"/> no outbox; estoque e financeiro reagem por consumidores idempotentes.
/// </summary>
public sealed partial class SaleService(
    ISalesDb db,
    ISaleProductResolver products,
    IPaymentMethods paymentMethods,
    ILocations locations,
    IEnumerable<IPaymentProvider> providers,
    IEventPublisher events,
    ICurrentUser currentUser,
    TimeProvider clock) : ISaleIngestion, IOrderPayments
{
    public const string Module = "sales";
    public const string NumberIndex = "ux_sales_channel_number";
    public const string DefaultPartLabel = "Única";

    public async Task<Result<SaleIngestionResult>> IngestAsync(SaleIngestionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await db.Sales.AsNoTracking().FirstOrDefaultAsync(s => s.Id == request.SaleId, cancellationToken) is { } existing)
        {
            return await ReplayAsync(existing, cancellationToken);
        }

        if (ValidateHeader(request.SaleId, request.Number, request.SourceType, request.Lines, request.ServiceFee) is { } header)
        {
            return header;
        }

        var channelErrors = SaleRules.ChannelErrors(request.Channel, request.RegisterId, request.CashSessionId);
        if (channelErrors.Count > 0)
        {
            return Error.Validation("sale_channel_invalid", "Caixa e sessão de caixa não batem com o canal da venda.", channelErrors);
        }

        if (ValidatePayments(request.Payments.Select(p => (p.Id, p.Amount, p.ChangeAmount, p.Installments, Texts(p.Provider, p.CardBrand, p.Nsu, p.AuthorizationCode))).ToList()) is { } invalid)
        {
            return invalid;
        }

        var parts = request.Parts ?? [];
        if (parts.Any(p => string.IsNullOrWhiteSpace(p.Label) || p.Label.Length > 60 || SaleRules.LooksLikeCardNumber(p.Label) || p.Amount < 0))
        {
            return Field("parts", "Cada parte precisa de um nome de até 60 caracteres e valor não negativo.");
        }

        if (request.Payments.FirstOrDefault(p => p.PartId is { } part && parts.All(x => x.Id != part)) is { } orphan)
        {
            return Field("payments", "O pagamento " + orphan.Id + " aponta para uma parte que não está na venda.");
        }

        var methods = await MethodsAsync(cancellationToken);
        foreach (var payment in request.Payments)
        {
            if (CheckMethod(methods, payment.PaymentMethodId, payment.ChangeAmount, request.CustomerId, request.FromDevice) is { } methodError)
            {
                return methodError;
            }
        }

        var built = await BuildSaleAsync(request.SaleId, request.Channel, request.Number, request.SourceType, request.SourceId, request.StoreId, request.RegisterId, request.CashSessionId,
            request.StockLocationId, request.CustomerId, request.SellerUserId, request.FromDevice, request.PriceListVersion, request.CompletedAtDevice, request.Lines, request.ServiceFee, cancellationToken);
        if (built.IsFailure)
        {
            return built.Error!;
        }

        var (sale, lines) = built.Value;
        var now = clock.GetUtcNow();
        List<SalePart> saleParts = parts.Count == 0
            ? [SalePart.Create(Guid.CreateVersion7(), sale.Id, DefaultPartLabel, SalePartKind.Equal, sale.Total)]
            : parts.Select(p => SalePart.Create(p.Id, sale.Id, p.Label.Trim(), p.Kind, SaleRules.Money(p.Amount))).ToList();
        var payments = new List<SalePayment>();
        foreach (var p in request.Payments)
        {
            var payment = SalePayment.Create(p.Id, sale.Id, p.PartId ?? saleParts[0].Id, p.PaymentMethodId, SaleRules.Money(p.Amount), SaleRules.Money(p.ChangeAmount), p.Integration,
                p.Installments, Clean(p.Provider), p.AcquirerPartyId, Clean(p.CardBrand), Clean(p.Nsu), Clean(p.AuthorizationCode), Clean(p.Txid), Clean(p.EndToEndId), now);
            if (!payment.Arrive(p.Status, currentUser.UserId, now))
            {
                return Field("payments", "Situação de pagamento inválida: " + p.Status + ".");
            }

            payments.Add(payment);
        }

        // CA 5: no canal sem caixa, quem chama ainda pode corrigir; a venda do aparelho do PDV nunca é recusada (vira divergência, OFF-02).
        var captured = SaleRules.CapturedNet(payments.Select(p => (p.Status, p.Amount, p.ChangeAmount)));
        var difference = SaleRules.Money(captured - sale.Total);
        if (difference != 0 && !request.FromDevice)
        {
            return Error.Validation("payment_total_mismatch",
                $"A soma dos pagamentos capturados ({Money(captured)}) é diferente do total da venda ({Money(sale.Total)}).");
        }

        sale.PaymentDifference = difference == 0 ? null : difference;
        sale.Complete(request.CompletedAt, BusinessDateOf(request.CompletedAt));
        foreach (var part in saleParts)
        {
            part.Paid = true;
        }

        return await SaveNewAsync(sale, lines, saleParts, payments, cancellationToken) is { } saveError
            ? saveError
            : ToResult(sale, captured, replayed: false);
    }

    /// <summary>A segunda entrega da mesma venda (CA 4): nada é gravado e a resposta é a da primeira.</summary>
    private async Task<Result<SaleIngestionResult>> ReplayAsync(Sale existing, CancellationToken ct)
    {
        var payments = await db.SalePayments.AsNoTracking().Where(p => p.SaleId == existing.Id).Select(p => new { p.Status, p.Amount, p.ChangeAmount }).ToListAsync(ct);
        return ToResult(existing, SaleRules.CapturedNet(payments.Select(p => (p.Status, p.Amount, p.ChangeAmount))), replayed: true);
    }

    private static SaleIngestionResult ToResult(Sale sale, decimal captured, bool replayed) =>
        new(sale.Id, sale.Number, sale.Status, sale.Total, captured, sale.PaymentDifference ?? 0,
            sale.PaymentDifference is null ? SaleIngestionOutcome.Accepted : SaleIngestionOutcome.AcceptedWithDivergence, replayed);

    /// <summary>
    /// Venda nova com linhas, partes e pagamentos, mais o <see cref="SaleCompleted"/> quando já conclui. O <c>SaveChanges</c> entra na transação de
    /// quem chama, se houver (CA 3): um erro depois desfaz tudo junto.
    /// </summary>
    private async Task<Error?> SaveNewAsync(Sale sale, IReadOnlyList<SaleLine> lines, IReadOnlyList<SalePart> parts, IReadOnlyList<SalePayment> payments, CancellationToken ct)
    {
        if (await db.Sales.AnyAsync(s => s.Channel == sale.Channel && s.Number == sale.Number, ct))
        {
            return Error.Conflict("sale_number_taken", $"Já existe venda {sale.Number} neste canal.");
        }

        db.Sales.Add(sale);
        db.SaleLines.AddRange(lines);
        db.SaleParts.AddRange(parts);
        db.SalePayments.AddRange(payments);
        if (sale.Status == SaleStatus.Completed)
        {
            PublishCompleted(sale);
        }

        try
        {
            await db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            // Duas entregas da mesma venda ao mesmo tempo: a outra gravou primeiro. Quem chama reenvia e recebe a resposta da primeira (CA 4).
            return Error.Conflict("sale_conflict", "A venda acabou de ser gravada por outra entrega. Envie de novo para receber a resposta.");
        }
    }

    private void PublishCompleted(Sale sale) =>
        events.Publish(new SaleCompleted(sale.Id, sale.Number, sale.Channel, sale.StoreId, sale.CashSessionId, sale.BusinessDate, sale.Total, sale.CompletedAt!.Value));

    /// <summary>Venda com as linhas resolvidas no catálogo (pela porta <see cref="ISaleProductResolver"/>) e os totais.</summary>
    private async Task<Result<(Sale Sale, List<SaleLine> Lines)>> BuildSaleAsync(Guid saleId, SaleChannel channel, string number, string sourceType, Guid? sourceId, Guid? storeId,
        Guid? registerId, Guid? cashSessionId, Guid? stockLocationId, Guid? customerId, Guid? sellerUserId, bool fromDevice, long? priceListVersion, DateTimeOffset? completedAtDevice,
        IReadOnlyList<SaleLineInput> inputs, decimal serviceFee, CancellationToken ct)
    {
        var byKey = (await products.FindManyAsync(inputs.Select(l => l.ProductId).Distinct().ToList(), ct)).ToDictionary(p => p.ProductId);
        if (inputs.FirstOrDefault(l => !byKey.ContainsKey(l.ProductId)) is { } unknown)
        {
            return Field("lines", "Produto não encontrado: " + unknown.ProductId + ".");
        }

        // A venda feita no aparelho já aconteceu: produto desativado depois não a recusa (CA 5).
        if (!fromDevice && inputs.FirstOrDefault(l => !byKey[l.ProductId].IsActive) is { } inactive)
        {
            return Field("lines", "Produto inativo: " + byKey[inactive.ProductId].Description + ".");
        }

        // Todo produto vendável tem item de estoque; o serviço (item do tipo Serviço) é tirado da baixa pelo consumidor do estoque.
        var needsStock = inputs.Count > 0;
        var location = stockLocationId;
        if (location is null && needsStock)
        {
            location = (await locations.DefaultAsync(ct))?.Id;
            if (location is null && !fromDevice)
            {
                return Field("stockLocationId", "Informe o local de estoque da venda ou cadastre o local padrão do estoque.");
            }
        }

        var sale = Sale.Create(saleId, channel, number.Trim(), sourceType.Trim(), sourceId, storeId, registerId, cashSessionId, location, customerId, sellerUserId, fromDevice,
            priceListVersion, completedAtDevice, clock.GetUtcNow(), currentUser.UserId);
        var lines = inputs.Select((l, i) =>
        {
            var product = byKey[l.ProductId];
            return SaleLine.Create(l.Id == Guid.Empty ? Guid.CreateVersion7() : l.Id, saleId, i + 1, l.ProductId, product.ParentProductId, product.ItemId, Cut(product.Description, 200),
                Cut(product.UnitCode, 10), SaleRules.Quantity(l.Quantity), SaleRules.Money(l.UnitPrice), SaleRules.Money(l.Discount), Clean(l.Note), Clean(l.Seat));
        }).ToList();
        sale.ApplyTotals(lines, serviceFee);
        sale.BusinessDate = BusinessDateOf(clock.GetUtcNow());
        sale.StockStatus = needsStock ? SaleStockStatus.Pending : SaleStockStatus.NotRequired;
        return (sale, lines);
    }

    private static Error? ValidateHeader(Guid saleId, string number, string sourceType, IReadOnlyList<SaleLineInput> lines, decimal serviceFee)
    {
        if (saleId == Guid.Empty)
        {
            return Field("saleId", "Informe o id da venda.");
        }

        if (string.IsNullOrWhiteSpace(number) || number.Trim().Length > 40)
        {
            return Field("number", "Informe o número da venda (até 40 caracteres).");
        }

        if (string.IsNullOrWhiteSpace(sourceType) || sourceType.Trim().Length > 40)
        {
            return Field("sourceType", "Informe a origem da venda (até 40 caracteres).");
        }

        if (lines is null || lines.Count == 0 || lines.Count > SaleRules.MaxLines)
        {
            return Field("lines", $"A venda precisa de 1 a {SaleRules.MaxLines} linhas.");
        }

        if (serviceFee < 0)
        {
            return Field("serviceFee", "Taxa de serviço não pode ser negativa.");
        }

        foreach (var line in lines)
        {
            if (line.Quantity <= 0 || line.UnitPrice < 0 || line.Discount < 0 || line.Discount > SaleRules.Money(line.Quantity * line.UnitPrice))
            {
                return Field("lines", "Cada linha precisa de quantidade maior que zero, preço não negativo e desconto até o valor da linha.");
            }

            if (SaleRules.LooksLikeCardNumber(line.Note) || SaleRules.LooksLikeCardNumber(line.Seat) || line.Note?.Length > 200 || line.Seat?.Length > 20)
            {
                return CardOrLength("lines");
            }
        }

        return null;
    }

    /// <summary>Valor, troco, parcelas e os campos livres do pagamento (CA 8: nada que pareça número de cartão).</summary>
    private static Error? ValidatePayments(List<(Guid Id, decimal Amount, decimal Change, int Installments, string?[] Texts)> payments)
    {
        if (payments.Count > SaleRules.MaxPayments)
        {
            return Field("payments", $"No máximo {SaleRules.MaxPayments} pagamentos por venda.");
        }

        if (payments.Select(p => p.Id).Distinct().Count() != payments.Count || payments.Any(p => p.Id == Guid.Empty))
        {
            return Field("payments", "Cada pagamento precisa de um id próprio.");
        }

        foreach (var p in payments)
        {
            if (p.Amount <= 0 || p.Change < 0 || p.Change >= p.Amount)
            {
                return Field("payments", "Valor do pagamento maior que zero e troco menor que o valor entregue.");
            }

            if (p.Installments is < 1 or > 24)
            {
                return Field("payments", "Parcelas de 1 a 24.");
            }

            if (p.Texts.Any(t => SaleRules.LooksLikeCardNumber(t) || t?.Length > 60))
            {
                return CardOrLength("payments");
            }
        }

        return null;
    }

    private static string?[] Texts(params string?[] texts) => texts;

    private async Task<IReadOnlyDictionary<Guid, PaymentMethodInfo>> MethodsAsync(CancellationToken ct) =>
        (await paymentMethods.ActiveAsync(BusinessTime.Today(clock), ct)).ToDictionary(m => m.Id);

    /// <summary>Forma ativa do Financeiro, troco só em forma que dá troco (rascunho C §6) e fiado só com cliente.</summary>
    private static Error? CheckMethod(IReadOnlyDictionary<Guid, PaymentMethodInfo> methods, Guid methodId, decimal change, Guid? customerId, bool fromDevice)
    {
        if (!methods.TryGetValue(methodId, out var method))
        {
            // A venda do aparelho usou a forma que valia na hora; se ela foi desativada depois, a venda entra igual (CA 5).
            return fromDevice ? null : Field("payments", "Forma de pagamento não encontrada ou inativa.");
        }

        if (change > 0 && !method.AllowsChange)
        {
            return Field("payments", $"A forma {method.Name} não dá troco: troco só em dinheiro.");
        }

        return method.Type == PaymentMethodType.OnAccount && customerId is null ? Field("customerId", "Venda fiado exige o cliente.") : null;
    }

    private static DateOnly BusinessDateOf(DateTimeOffset at) => DateOnly.FromDateTime(BusinessTime.ToBusiness(at).DateTime);

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static string Cut(string text, int max) => text.Length <= max ? text : text[..max];

    internal static string Money(decimal value) => value.ToString("C", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));

    private static Error CardOrLength(string field) =>
        Field(field, "Campo com texto longo demais ou com sequência que parece número de cartão. O sistema nunca guarda número de cartão.");

    private static Error Field(string field, string message) =>
        Error.Validation(Error.Codes.Validation, message, new Dictionary<string, string[]> { [field] = [message] });
}
