using BemAqui.BuildingBlocks.Persistence;
using BemAqui.Commercial.Contracts;
using BemAqui.Inventory.Contracts;
using BemAqui.Parties.Contracts;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Production.Application;

public sealed partial class ShipmentService
{
    /// <summary>Linha conferida: o pedido, o item e as séries prontas para gravar.</summary>
    private sealed record CheckedLine(ShipmentLineInput Input, ItemSummary Item, string? LotNumber, IReadOnlyList<SerialInfo> Serials);

    /// <summary>Cabeçalho conferido.</summary>
    private sealed record CheckedShipment(ProjectSummary Project, string Address, string? Plate, string? Driver, IReadOnlyList<CheckedLine> Lines);

    /// <summary>
    /// "Salvar romaneio" (CA 1 e 2): nasce o rascunho <c>ROM-AAAA-0001</c> (D15) com o projeto, o cliente, o endereço de entrega, o transporte e
    /// as linhas (com <c>Idempotency-Key</c> no endpoint). Série em outro romaneio não cancelado, de outro projeto ou fora do estoque: 422.
    /// </summary>
    public async Task<Result<ShipmentDetail>> CreateAsync(ShipmentInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var checkedInput = await ValidateAsync(input, null, cancellationToken);
        if (checkedInput.IsFailure)
        {
            return checkedInput.Error!;
        }

        var c = checkedInput.Value;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var shipment = new Shipment
        {
            Number = await numbers.NextAsync(ShipmentRules.DocType, cancellationToken), ProjectId = c.Project.Id, PartyId = c.Project.PartyId, CreatedBy = currentUser.UserId,
            CreatedAt = clock.GetUtcNow(),
        };
        Apply(shipment, input, c);
        db.Shipments.Add(shipment);
        AddLines(shipment.Id, c.Lines);
        var saved = await SaveSerialsAsync(cancellationToken);
        if (saved.IsFailure)
        {
            await transaction.RollbackAsync(cancellationToken);
            return saved.Error!;
        }

        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(shipment.Id, cancellationToken);
    }

    /// <summary>Alterar o rascunho (CA 1): o cabeçalho e as linhas, com <c>production.create</c>; fora do rascunho, 409.</summary>
    public async Task<Result<ShipmentDetail>> UpdateAsync(Guid id, ShipmentInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var shipment = await db.Shipments.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (shipment is null)
        {
            return NotFound();
        }

        if (input.Version is not { } version || shipment.Version != version)
        {
            return Error.Conflict("concurrency_conflict", "O romaneio mudou desde que você abriu. Recarregue.");
        }

        if (shipment.Status != ShipmentState.Draft)
        {
            return Error.Conflict("shipment_not_draft", "Só romaneio em rascunho é alterado.");
        }

        if (input.ProjectId != shipment.ProjectId)
        {
            return Invalid("projectId", "O projeto do romaneio não muda. Cancele e monte outro romaneio.");
        }

        var checkedInput = await ValidateAsync(input, id, cancellationToken);
        if (checkedInput.IsFailure)
        {
            return checkedInput.Error!;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.Entry(shipment).Property(s => s.Version).OriginalValue = version;
        Apply(shipment, input, checkedInput.Value);
        // As linhas antigas saem (ficam na auditoria) e entram as novas; a série da linha antiga é liberada antes de entrar de novo.
        db.ShipmentLineSerials.RemoveRange(await db.ShipmentLineSerials.Where(s => s.ShipmentId == id).ToListAsync(cancellationToken));
        db.ShipmentLines.RemoveRange(await db.ShipmentLines.Where(l => l.ShipmentId == id).ToListAsync(cancellationToken));
        await db.SaveChangesAsync(cancellationToken);
        AddLines(id, checkedInput.Value.Lines);
        var saved = await SaveSerialsAsync(cancellationToken);
        if (saved.IsFailure)
        {
            await transaction.RollbackAsync(cancellationToken);
            return saved.Error!;
        }

        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    private static void Apply(Shipment shipment, ShipmentInput input, CheckedShipment c)
    {
        shipment.DeliveryAddress = c.Address;
        shipment.CarrierPartyId = input.CarrierPartyId;
        shipment.VehiclePlate = c.Plate;
        shipment.DriverName = c.Driver;
        shipment.Volumes = input.Volumes;
        shipment.GrossWeight = input.GrossWeight;
        shipment.PlannedDate = input.PlannedDate;
    }

    private void AddLines(Guid shipmentId, IReadOnlyList<CheckedLine> lines)
    {
        var sequence = 0;
        // A tabela das séries do romaneio é da Produção (as séries do estoque mudam só pelo IStockSerials).
        var held = db.ShipmentLineSerials;
        foreach (var l in lines)
        {
            var line = new ShipmentLine
            {
                ShipmentId = shipmentId, Sequence = ++sequence, ItemId = l.Item.Id, Quantity = l.Input.Quantity, LocationId = l.Input.LocationId, LotId = l.Input.LotId,
                LotNumber = l.LotNumber, FromGeneralStock = l.Input.FromGeneralStock,
            };
            db.ShipmentLines.Add(line);
            foreach (var s in l.Serials)
            {
                held.Add(new ShipmentLineSerial { ShipmentId = shipmentId, LineId = line.Id, SerialId = s.Id, SerialNumber = s.SerialNumber });
            }
        }
    }

    /// <summary>O índice único parcial segura a mesma série em dois romaneios montados ao mesmo tempo (CA 16): o segundo recebe 409.</summary>
    private async Task<Result> SaveSerialsAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return Result.Success();
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, "ux_shipment_line_serials_active"))
        {
            return Error.Conflict("serial_in_shipment", "Outro romaneio acabou de incluir uma destas séries. Recarregue e confira as séries.");
        }
    }

    /// <summary>
    /// Confere o pedido (CA 1, 2 e 15): o projeto pelo <see cref="IProjects"/>, a transportadora pelo <see cref="IParties"/>, os limites do
    /// transporte e cada linha pelo que está pronto para expedir (<see cref="IFinishedGoodsStock"/>): do projeto ou, marcada, do estoque geral.
    /// </summary>
    private async Task<Result<CheckedShipment>> ValidateAsync(ShipmentInput input, Guid? current, CancellationToken ct)
    {
        var project = await projects.FindAsync(input.ProjectId, ct);
        if (project is null)
        {
            return Invalid("projectId", "Projeto não encontrado.");
        }

        if (project.Status is "Cancelled" or "Closed")
        {
            return Invalid("projectId", "O projeto está encerrado ou cancelado: não recebe romaneio.");
        }

        var address = input.DeliveryAddress?.Trim();
        if (string.IsNullOrEmpty(address) || address.Length > ShipmentRules.MaxAddress)
        {
            return Invalid("deliveryAddress", $"Informe o endereço de entrega, com até {ShipmentRules.MaxAddress} caracteres.");
        }

        if (input.CarrierPartyId is { } carrier && (await parties.FindAsync(carrier, ct) is not { IsActive: true, IsDeleted: false } || !await parties.HasRoleAsync(carrier, PartyRoleKind.Carrier, ct)))
        {
            return Invalid("carrierPartyId", "Escolha uma transportadora ativa (terceiro com o papel Transportadora).");
        }

        var plate = ShipmentRules.Plate(input.VehiclePlate);
        if (plate is { Length: > ShipmentRules.MaxPlate })
        {
            return Invalid("vehiclePlate", $"A placa tem até {ShipmentRules.MaxPlate} caracteres.");
        }

        var driver = string.IsNullOrWhiteSpace(input.DriverName) ? null : input.DriverName.Trim();
        if (driver is { Length: > ShipmentRules.MaxDriver })
        {
            return Invalid("driverName", $"O nome do motorista tem até {ShipmentRules.MaxDriver} caracteres.");
        }

        if (input.Volumes is < 1 or > ShipmentRules.MaxVolumes)
        {
            return Invalid("volumes", $"Volumes de 1 a {ShipmentRules.MaxVolumes}.");
        }

        if (input.GrossWeight is < 0 or > ShipmentRules.MaxWeight)
        {
            return Invalid("grossWeight", "Peso bruto de zero para cima, em kg.");
        }

        if (input.Lines is not { Count: > 0 } || input.Lines.Count > ShipmentRules.MaxLines)
        {
            return Invalid("lines", $"Inclua de 1 a {ShipmentRules.MaxLines} itens no romaneio.");
        }

        var lines = await ValidateLinesAsync(project, input.Lines, current, ct);
        return lines.IsFailure ? lines.Error! : new CheckedShipment(project, address, plate, driver, lines.Value);
    }

    private async Task<Result<IReadOnlyList<CheckedLine>>> ValidateLinesAsync(ProjectSummary project, IReadOnlyList<ShipmentLineInput> lines, Guid? current, CancellationToken ct)
    {
        var itemIds = lines.Select(l => l.ItemId).Distinct().ToList();
        var found = (await items.FindAsync(itemIds, ct)).ToDictionary(i => i.Id);
        var serialIds = lines.SelectMany(l => l.SerialIds ?? []).ToList();
        if (serialIds.Count != serialIds.Distinct().Count())
        {
            return Invalid("lines", "A mesma série está em duas linhas.");
        }

        var serialInfo = (await serials.FindAsync(serialIds, ct)).ToDictionary(s => s.Id);
        var held = await HeldSerialsAsync(serialIds, current, ct);
        var ofProject = await finished.ForProjectAsync(project.Id, ct);
        IReadOnlyList<FinishedGoodsStockLine> general = lines.Any(l => l.FromGeneralStock) ? await finished.GeneralStockAsync(null, 200, ct) : [];
        var open = await OpenQuantitiesAsync(itemIds, current, ct);
        var result = new List<CheckedLine>();
        var keys = new HashSet<(Guid, Guid, Guid?, bool)>();
        foreach (var line in lines)
        {
            if (!found.TryGetValue(line.ItemId, out var item) || item.Type == ItemType.Service)
            {
                return Invalid("lines", "Item não encontrado no estoque.");
            }

            if (line.Quantity <= 0)
            {
                return Invalid("lines", $"Informe a quantidade de {item.Code}, maior que zero.");
            }

            if (item.TracksLot && line.LotId is null)
            {
                return Invalid("lines", $"{item.Code} controla lote: informe o lote.");
            }

            var pool = line.FromGeneralStock ? general : ofProject;
            var stockLine = pool.FirstOrDefault(s => s.ItemId == item.Id && s.LocationId == line.LocationId && s.LotId == line.LotId);
            if (item.TracksSerial)
            {
                var ids = line.SerialIds ?? [];
                if (SerialRules.Units(line.Quantity) is not { } units || units != ids.Count)
                {
                    return Invalid("lines", $"{item.Code} controla série: escolha uma série por unidade.");
                }

                var chosen = new List<SerialInfo>();
                foreach (var serialId in ids)
                {
                    if (!serialInfo.TryGetValue(serialId, out var serial))
                    {
                        return Invalid("lines", "Série não encontrada.");
                    }

                    if (ShipmentRules.SerialProblem(new ShipmentSerialCandidate(serial, held.GetValueOrDefault(serialId)), project.Id, item.Id, line.LocationId, line.FromGeneralStock) is { } problem)
                    {
                        return Invalid("lines", problem);
                    }

                    chosen.Add(serial);
                }

                result.Add(new CheckedLine(line, item, stockLine?.LotNumber, chosen));
                continue;
            }

            if (line.SerialIds is { Count: > 0 })
            {
                return Invalid("lines", $"{item.Code} não controla série: a saída é por quantidade.");
            }

            if (!keys.Add((item.Id, line.LocationId, line.LotId, line.FromGeneralStock)))
            {
                return Invalid("lines", $"{item.Code} aparece duas vezes no mesmo local. Junte as quantidades numa linha.");
            }

            if (stockLine is null)
            {
                return Invalid("lines", line.FromGeneralStock
                    ? $"{item.Code} não tem produto acabado sem projeto neste local."
                    : $"{item.Code} não tem produto acabado do projeto {project.Number} neste local. Produto de outro projeto não entra; o produto acabado sem projeto entra por \"Incluir produto do estoque geral\".");
            }

            var inOpen = open.Where(o => o.ItemId == item.Id && o.LocationId == line.LocationId && o.LotId == line.LotId && o.FromGeneralStock == line.FromGeneralStock).Sum(o => o.Quantity);
            if (line.Quantity > stockLine.Quantity - inOpen)
            {
                return Invalid("lines", $"{item.Code}: há {ShipmentRules.Quantity(Math.Max(stockLine.Quantity - inOpen, 0m))} {item.UnitCode} para expedir neste local{(inOpen > 0 ? $" ({ShipmentRules.Quantity(inOpen)} já em outros romaneios)" : string.Empty)}.");
            }

            result.Add(new CheckedLine(line, item, stockLine.LotNumber, []));
        }

        return result;
    }

    internal static Error Invalid(string field, string message) =>
        Error.Validation(Error.Codes.Validation, message, new Dictionary<string, string[]> { [field] = [message] });
}
