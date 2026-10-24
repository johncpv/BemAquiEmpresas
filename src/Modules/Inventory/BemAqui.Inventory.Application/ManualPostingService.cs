using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Persistence.Numbering;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Commercial.Contracts;
using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Inventory.Application;

/// <summary>
/// Lançamentos manuais do estoque (EST-01a, D7, ADR 0023 §5): saldo inicial na janela de implantação, entrada manual, saída avulsa e
/// transferência. Abaixo dos limites grava na hora pelo motor; acima, vira documento pendente sem mexer no saldo, decidido por outra
/// pessoa com <c>inventory.approve</c> (exceção do Proprietário único pelo <see cref="SoleOwnerRule"/>).
/// </summary>
public sealed partial class ManualPostingService(
    IInventoryDb db, IStockMovements ledger, INumberSequence numbers, IUnitConversion conversion, IProjects projects, IUserDirectory users,
    IPermissionChecker permissions, INotificationService notifications, ICurrentUser currentUser, TimeProvider clock, ReversePolicy policy, StockQueryService queries,
    ILocationCountLock locks)
{
    public const string Link = "/app/estoque/lancamentos/";
    public const string MovementLink = "/app/estoque/movimentos/";
    public const string ApprovalType = "inventory.manual_pending";
    public const string DecisionType = "inventory.manual_decided";
    public const string SelfApprovedType = "inventory.self_approved";
    public const int MaxLotNumber = 40;
    public const int MaxComment = 500;

    /// <summary>Entrada manual, saída avulsa ou transferência (CA 6 a 9).</summary>
    public async Task<Result<ManualPostingDetail>> CreateAsync(ManualPostingInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Kind is not (ManualPostingKind.Receipt or ManualPostingKind.Issue or ManualPostingKind.Transfer))
        {
            return Invalid("kind", "Saldo inicial tem lançamento próprio (Lançar saldo inicial).");
        }

        var item = await ActiveItemAsync(input.ItemId, "itemId", cancellationToken);
        if (item.IsFailure)
        {
            return item.Error!;
        }

        var origin = await ActiveLocationAsync(input.LocationId, "locationId", cancellationToken);
        if (origin.IsFailure)
        {
            return origin.Error!;
        }

        var location = origin.Value;
        Location? destination = null;
        if (input.Kind == ManualPostingKind.Transfer)
        {
            if (input.ToLocationId is not { } to || to == input.LocationId)
            {
                return Invalid("toLocationId", "Escolha o local de destino, diferente do de origem.");
            }

            var target = await ActiveLocationAsync(to, "toLocationId", cancellationToken);
            if (target.IsFailure)
            {
                return target.Error!;
            }

            destination = target.Value;
        }

        var quantity = await StockQuantityAsync(item.Value, input.Quantity, input.UnitId, "quantity", cancellationToken);
        if (quantity.IsFailure)
        {
            return quantity.Error!;
        }

        var reason = Clean(input.Reason);
        if ((input.Kind != ManualPostingKind.Transfer && reason is null) || reason is { Length: > ManualPostingRules.MaxReason })
        {
            return Invalid("reason", $"Informe o motivo, com até {ManualPostingRules.MaxReason} caracteres.");
        }

        decimal? unitCost = null;
        if (input.Kind == ManualPostingKind.Receipt)
        {
            if (input.UnitCost is not > 0)
            {
                return Invalid("unitCost", "Informe o custo unitário da entrada, maior que zero.");
            }

            unitCost = AverageCostCalculator.RoundCost(input.UnitCost.Value);
        }

        var lot = await LotForRequestAsync(item.Value, input.Kind == ManualPostingKind.Receipt, input.LotId, input.LotNumber, cancellationToken);
        if (lot.IsFailure)
        {
            return lot.Error!;
        }

        if (input.Kind == ManualPostingKind.Transfer && input.ProjectId is not null)
        {
            return Invalid("projectId", "Transferência entre locais não tem projeto.");
        }

        var project = await ProjectAsync(input.ProjectId, cancellationToken);
        if (project.IsFailure)
        {
            return project.Error!;
        }

        var parameters = await ParametersAsync(cancellationToken);
        var average = item.Value.AverageCost;
        var value = AverageCostCalculator.LineTotal(quantity.Value, unitCost ?? average);
        var approval = ManualPostingRules.ApprovalReason(input.Kind, value, unitCost, average, parameters.ManualApprovalAmount, parameters.ManualCostDeviationPct);
        var now = clock.GetUtcNow();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var posting = new ManualPosting
        {
            Number = await numbers.NextAsync(StockLedger.DocType, cancellationToken),
            Kind = input.Kind,
            Status = approval is null ? ManualPostingStatus.Posted : ManualPostingStatus.Pending,
            ApprovalReason = approval,
            EstimatedValue = value,
            Reason = reason,
            ProjectId = project.Value,
            RequestedBy = currentUser.UserId,
            RequestedAt = now,
        };
        var line = new ManualPostingLine
        {
            PostingId = posting.Id, Sequence = 1, ItemId = item.Value.Id, LocationId = location.Id, ToLocationId = destination?.Id, LotId = lot.Value.LotId, LotNumber = lot.Value.LotNumber,
            Quantity = quantity.Value, EnteredQuantity = input.Quantity, EnteredUnitId = input.UnitId ?? item.Value.UnitId, UnitCost = unitCost,
        };
        db.ManualPostings.Add(posting);
        db.ManualPostingLines.Add(line);
        if (approval is null)
        {
            var posted = await PostLinesAsync(posting, [line], cancellationToken);
            if (posted.IsFailure)
            {
                await transaction.RollbackAsync(cancellationToken);
                return posted.Error!;
            }
        }
        else
        {
            await db.SaveChangesAsync(cancellationToken);
            await NotifyApproversAsync(posting, location.Name, value, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return await DetailAsync(posting.Id, cancellationToken);
    }

    /// <summary>
    /// Grava os movimentos das linhas pelo motor, na transação aberta: o lote nasce aqui, a saída sai pelo custo médio do momento e
    /// a transferência tem duas pernas com o mesmo número.
    /// </summary>
    private async Task<Result> PostLinesAsync(ManualPosting posting, IReadOnlyList<ManualPostingLine> lines, CancellationToken ct)
    {
        var at = posting.CutoffDate is { } cutoff ? OpeningAt(cutoff) : (DateTimeOffset?)null;
        var postings = new List<(ManualPostingLine Line, bool InLeg, StockPosting Posting)>();
        foreach (var line in lines.OrderBy(l => l.Sequence))
        {
            if (line.LotId is null && line.LotNumber is { } lotNumber)
            {
                line.LotId = await ledger.EnsureLotAsync(line.ItemId, lotNumber, cancellationToken: ct);
            }

            const string source = StockSources.ManualPosting;
            switch (posting.Kind)
            {
                case ManualPostingKind.OpeningBalance:
                    postings.Add((line, false, new StockPosting(StockMovementType.OpeningBalance, line.ItemId, line.LocationId, line.LotId, line.Quantity, line.UnitCost, source, posting.Id,
                        line.Sequence, posting.Number, At: at, Reason: posting.Reason)));
                    break;
                case ManualPostingKind.Receipt:
                    postings.Add((line, false, new StockPosting(StockMovementType.Receipt, line.ItemId, line.LocationId, line.LotId, line.Quantity, line.UnitCost, source, posting.Id,
                        line.Sequence, posting.Number, posting.ProjectId, Reason: posting.Reason)));
                    break;
                case ManualPostingKind.Issue:
                    postings.Add((line, false, new StockPosting(StockMovementType.Issue, line.ItemId, line.LocationId, line.LotId, -line.Quantity, null, source, posting.Id,
                        line.Sequence, posting.Number, posting.ProjectId, Reason: posting.Reason)));
                    break;
                default:
                    postings.Add((line, false, new StockPosting(StockMovementType.Transfer, line.ItemId, line.LocationId, line.LotId, -line.Quantity, null, source, posting.Id,
                        (line.Sequence * 2) - 1, posting.Number, Reason: posting.Reason)));
                    postings.Add((line, true, new StockPosting(StockMovementType.Transfer, line.ItemId, line.ToLocationId!.Value, line.LotId, line.Quantity, null, source, posting.Id,
                        line.Sequence * 2, posting.Number, Reason: posting.Reason)));
                    break;
            }
        }

        var results = await ledger.PostManyAsync(postings.Select(p => p.Posting).ToList(), ct);
        if (results.IsFailure)
        {
            return results.Error!;
        }

        for (var i = 0; i < postings.Count; i++)
        {
            if (postings[i].InLeg)
            {
                postings[i].Line.InMovementId = results.Value[i].MovementId;
            }
            else
            {
                postings[i].Line.MovementId = results.Value[i].MovementId;
            }
        }

        posting.Status = ManualPostingStatus.Posted;
        posting.PostedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>Aviso a quem pode aprovar, menos o autor (CA 9, R-PRP-5), uma vez por lançamento.</summary>
    private async Task NotifyApproversAsync(ManualPosting posting, string locationName, decimal value, CancellationToken ct)
    {
        var title = $"Lançamento {posting.Number} de {locationName} aguardando aprovação: {KindLabel(posting.Kind)}, {ManualPostingRules.Money(value)}.";
        foreach (var user in (await users.UsersWithPermissionAsync("inventory.approve", ct)).Where(u => u != posting.RequestedBy))
        {
            await notifications.NotifyOnceAsync(user, ApprovalType, title, Link + posting.Id, $"inventory-manual:{posting.Id}", ct);
        }
    }

    public static string KindLabel(ManualPostingKind kind) => kind switch
    {
        ManualPostingKind.OpeningBalance => "saldo inicial",
        ManualPostingKind.Receipt => "entrada",
        ManualPostingKind.Issue => "saída",
        _ => "transferência",
    };

    /// <summary>Data de corte D gravada em D 23:59:59 de São Paulo, em UTC (ADR 0016).</summary>
    public static DateTimeOffset OpeningAt(DateOnly cutoff) => BusinessTime.StartOfDayUtc(cutoff.AddDays(1)).AddSeconds(-1);

    private async Task<Result<Item>> ActiveItemAsync(Guid id, string field, CancellationToken ct)
    {
        var item = await db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item is null)
        {
            return Invalid(field, "Item não encontrado.");
        }

        if (!item.IsActive)
        {
            return Invalid(field, "Item inativo não movimenta estoque.");
        }

        return item.Type == ItemType.Service ? Invalid(field, "Serviço não tem estoque.") : item;
    }

    private async Task<Result<Location>> ActiveLocationAsync(Guid id, string field, CancellationToken ct)
    {
        var location = await db.Locations.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, ct);
        if (location is null)
        {
            return Invalid(field, "Local não encontrado.");
        }

        return location.IsActive ? location : Invalid(field, "Local inativo não recebe movimento.");
    }

    /// <summary>Quantidade na unidade de estoque (R-EST-3): outra unidade passa pelo <see cref="IUnitConversion"/>, nunca por fator 1.</summary>
    private async Task<Result<decimal>> StockQuantityAsync(Item item, decimal quantity, Guid? unitId, string field, CancellationToken ct)
    {
        if (quantity <= 0)
        {
            return Invalid(field, "Informe uma quantidade maior que zero.");
        }

        decimal converted;
        if (unitId is null || unitId == item.UnitId)
        {
            converted = Math.Round(quantity, 4, MidpointRounding.AwayFromZero);
        }
        else
        {
            var result = await conversion.ConvertAsync(item.Id, quantity, unitId.Value, item.UnitId, ct);
            if (result.IsFailure)
            {
                return Invalid(field, result.Error!.Message);
            }

            converted = result.Value;
        }

        return converted > 0 ? converted : Invalid(field, "A quantidade convertida fica zero. Aumente a quantidade.");
    }

    /// <summary>
    /// Lote da linha (CA 6, 7, 31): a entrada informa o número (o lote nasce quando grava); a saída e a transferência escolhem um lote
    /// que já existe neste item. Lote de outro item: 422.
    /// </summary>
    private async Task<Result<(Guid? LotId, string? LotNumber)>> LotForRequestAsync(Item item, bool entry, Guid? lotId, string? lotNumber, CancellationToken ct)
    {
        var number = Clean(lotNumber);
        if (!item.TracksLot)
        {
            return lotId is null && number is null ? ((Guid?)null, (string?)null) : Invalid(lotId is null ? "lotNumber" : "lotId", "Este item não controla lote.");
        }

        if (lotId is { } id)
        {
            var found = await db.Lots.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id && l.ItemId == item.Id, ct);
            return found is null ? Invalid("lotId", "Lote não encontrado neste item.") : (found.Id, found.LotNumber);
        }

        if (number is null)
        {
            return Invalid("lotNumber", "Este item controla lote: informe o lote.");
        }

        if (number.Length > MaxLotNumber)
        {
            return Invalid("lotNumber", $"Número do lote com até {MaxLotNumber} caracteres.");
        }

        var existing = await db.Lots.AsNoTracking().FirstOrDefaultAsync(l => l.ItemId == item.Id && l.LotNumber == number, ct);
        if (existing is not null)
        {
            return (existing.Id, existing.LotNumber);
        }

        return entry ? ((Guid?)null, number) : Invalid("lotNumber", "Lote não encontrado neste item.");
    }

    /// <summary>Projeto em andamento (CA 31), validado pelo <see cref="IProjects"/>: encerrado ou cancelado, 422.</summary>
    private async Task<Result<Guid?>> ProjectAsync(Guid? projectId, CancellationToken ct)
    {
        if (projectId is not { } id)
        {
            return (Guid?)null;
        }

        var project = await projects.FindAsync(id, ct);
        if (project is null)
        {
            return Invalid("projectId", "Projeto não encontrado.");
        }

        return project.Status is "Closed" or "Cancelled" ? Invalid("projectId", "O projeto precisa estar em andamento (não encerrado nem cancelado).") : project.Id;
    }

    /// <summary>Parâmetros do estoque (linha única); o seed cria, e na falta valem os padrões (R$ 1.000,00 e 20%).</summary>
    internal async Task<InventoryParameters> ParametersAsync(CancellationToken ct) =>
        await db.InventoryParameters.FirstOrDefaultAsync(ct) ?? AddParameters();

    private InventoryParameters AddParameters()
    {
        var parameters = new InventoryParameters();
        db.InventoryParameters.Add(parameters);
        return parameters;
    }

    private async Task<bool> IsDecidingAloneAsync(Guid me, CancellationToken ct) =>
        await SoleOwnerRule.NoOtherEligibleAsync(users, "inventory.approve", me, [me], ct);

    private async Task NotifySelfApprovedAsync(string what, string link, CancellationToken ct)
    {
        var name = currentUser.UserId is { } me ? await users.DisplayNameAsync(me, ct) ?? "Um usuário" : "Um usuário";
        foreach (var owner in await users.OwnerIdsAsync(ct))
        {
            notifications.Notify(owner, SelfApprovedType, $"{name} {what} ({SoleOwnerRule.SelfApprovedLabel}).", link);
        }
    }

    internal static Error Invalid(string field, string message) =>
        Error.Validation(Error.Codes.Validation, message, new Dictionary<string, string[]> { [field] = [message] });

    internal static Error NotFound() => Error.NotFound("manual_posting_not_found", "Lançamento não encontrado.");

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
