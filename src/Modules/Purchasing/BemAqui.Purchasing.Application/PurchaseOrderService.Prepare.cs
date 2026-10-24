using BemAqui.Inventory.Contracts;
using BemAqui.Parties.Contracts;
using BemAqui.Purchasing.Contracts;
using BemAqui.Purchasing.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Purchasing.Application;

public sealed partial class PurchaseOrderService
{
    /// <summary>O que veio da tela ou da API, antes de validar.</summary>
    private sealed record Draft(
        Guid PartyId, Guid? ProjectId, Guid? PaymentTermId, DateOnly ExpectedDate, decimal Freight, decimal IpiPct, string? Notes, IReadOnlyList<PurchaseOrderLineInput>? Lines);

    private sealed record PreparedLine(ItemSummary Item, decimal Quantity, decimal? EnteredQuantity, Guid? EnteredUnitId, decimal UnitPrice, CostType CostType, IReadOnlyList<PurchaseRequestLine> RequestLines);

    private sealed record Prepared(
        Guid PartyId, Guid? ProjectId, Guid TermId, DateOnly ExpectedDate, decimal Freight, decimal IpiPct, string? Notes, IReadOnlyList<PreparedLine> Lines, PurchaseOrderTotals Totals);

    /// <summary>
    /// Validação comum de criar e editar (CA 1 a 4): fornecedor com papel Fornecedor e ativo, condição ativa (padrão a do fornecedor),
    /// itens ativos fora Serviço, quantidade convertida para a unidade de estoque, preço com até 6 casas, linhas de SC em aberto do mesmo
    /// item, destino (o das SC quando há SC; senão o projeto em andamento ou o estoque geral), frete, IPI e o total recalculado. 422 no campo.
    /// </summary>
    /// <remarks>
    /// <paramref name="fromRfq"/> (CMP-02 CA 9): as linhas de SC da cotação de origem, que podem estar <c>Quoting</c> (ou de novo
    /// <c>Open</c>, quando o pedido gerado antes foi cancelado).
    /// </remarks>
    private async Task<Result<Prepared>> PrepareAsync(Draft draft, PurchaseOrder? order, HashSet<Guid> alreadyLinked, CancellationToken ct, IReadOnlySet<Guid>? fromRfq = null)
    {
        var lines = draft.Lines ?? [];
        if (lines.Count == 0)
        {
            return Invalid("lines", "Inclua pelo menos um item.");
        }

        if (lines.Count > PurchaseOrderRules.MaxLines)
        {
            return Invalid("lines", $"Um pedido tem até {PurchaseOrderRules.MaxLines} itens.");
        }

        var notes = Clean(draft.Notes);
        if (notes is { Length: > PurchaseOrderRules.MaxNotes })
        {
            return Invalid("notes", $"Escreva a observação com até {PurchaseOrderRules.MaxNotes} caracteres.");
        }

        if (draft.Freight < 0m || draft.Freight > PurchaseOrderRules.MaxMoney || decimal.Round(draft.Freight, 2) != draft.Freight)
        {
            return Invalid("freight", "Informe o frete em reais, de zero para cima.");
        }

        if (draft.IpiPct < 0m || draft.IpiPct > PurchaseOrderRules.MaxIpiPct || decimal.Round(draft.IpiPct, 4) != draft.IpiPct)
        {
            return Invalid("ipiPct", "Informe o IPI de 0% a 100%, com até 4 casas.");
        }

        if (order?.ExpectedDate != draft.ExpectedDate && draft.ExpectedDate < Today)
        {
            return Invalid("expectedDate", "A data prevista de entrega não pode ser antes de hoje.");
        }

        var party = await parties.FindAsync(draft.PartyId, ct);
        if (party is null || party.IsDeleted || !party.IsActive)
        {
            return Invalid("partyId", "Escolha um fornecedor ativo.");
        }

        if (!party.Roles.Contains(PartyRoleKind.Supplier))
        {
            return Invalid("partyId", "Este terceiro não tem o papel Fornecedor. Marque o papel no cadastro dele.");
        }

        var termId = draft.PaymentTermId ?? party.DefaultPaymentTermId;
        var term = termId is { } t ? await terms.FindAsync(t, ct) : null;
        if (term is null || (!term.IsActive && term.Id != order?.PaymentTermId))
        {
            return Invalid("paymentTermId", "Escolha uma condição de pagamento ativa.");
        }

        var found = (await items.FindAsync(lines.Select(l => l.ItemId).Distinct().ToList(), ct)).ToDictionary(i => i.Id);
        var requestedIds = lines.SelectMany(l => l.RequestLineIds ?? []).ToList();
        if (requestedIds.Count != requestedIds.Distinct().Count())
        {
            return Invalid("lines", "Cada linha de solicitação entra uma vez no pedido.");
        }

        var loadIds = requestedIds.Union(alreadyLinked).ToList();
        var requestLines = loadIds.Count == 0 ? [] : await db.PurchaseRequestLines.Where(l => loadIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct);
        var prepared = new List<PreparedLine>();
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var prefix = $"lines[{i}].";
            if (!found.TryGetValue(line.ItemId, out var item) || item.IsDeleted || !item.IsActive)
            {
                return Invalid(prefix + "itemId", "Escolha um item ativo.");
            }

            if (item.Type == ItemType.Service)
            {
                return Error.Validation(PurchaseRequestService.ServiceNotAllowedCode, PurchaseRequestService.ServiceNotAllowed,
                    new Dictionary<string, string[]> { [prefix + "itemId"] = [PurchaseRequestService.ServiceNotAllowed] });
            }

            if (prepared.Any(p => p.Item.Id == item.Id))
            {
                return Invalid(prefix + "itemId", "Este item já está em outra linha. Some as quantidades numa linha só.");
            }

            if (line.Quantity is <= 0m or > PurchaseOrderRules.MaxQuantity)
            {
                return Invalid(prefix + "quantity", "Informe a quantidade, maior que zero.");
            }

            if (line.UnitPrice is <= 0m or > PurchaseOrderRules.MaxUnitPrice || decimal.Round(line.UnitPrice, 6) != line.UnitPrice)
            {
                return Invalid(prefix + "unitPrice", "Informe o preço unitário, maior que zero, com até 6 casas.");
            }

            var quantity = decimal.Round(line.Quantity, 4, MidpointRounding.AwayFromZero);
            decimal? entered = null;
            Guid? enteredUnit = null;
            if (line.UnitId is { } unit && unit != item.UnitId)
            {
                var converted = await conversion.ConvertAsync(item.Id, line.Quantity, unit, item.UnitId, ct);
                if (converted.IsFailure)
                {
                    return Invalid(prefix + "unitId", converted.Error!.Message);
                }

                (entered, enteredUnit, quantity) = (line.Quantity, unit, converted.Value);
            }

            var linkedHere = new List<PurchaseRequestLine>();
            foreach (var requestLineId in line.RequestLineIds ?? [])
            {
                if (!requestLines.TryGetValue(requestLineId, out var requestLine) || requestLine.ItemId != item.Id)
                {
                    return Invalid(prefix + "requestLineIds", "A linha de solicitação não é deste item.");
                }

                var quotedHere = fromRfq?.Contains(requestLine.Id) == true && requestLine.Status == PurchaseRequestStatus.Quoting;
                if (requestLine.Status != PurchaseRequestStatus.Open && !alreadyLinked.Contains(requestLine.Id) && !quotedHere)
                {
                    return Error.Conflict(PurchaseRequestService.LineNotOpenCode, "Uma linha de solicitação deste pedido já virou pedido, está em cotação ou foi cancelada. Recarregue.");
                }

                linkedHere.Add(requestLine);
            }

            prepared.Add(new PreparedLine(item, quantity, entered, enteredUnit, line.UnitPrice, CostType.Material, linkedHere));
        }

        var fromRequests = prepared.SelectMany(l => l.RequestLines).ToList();
        var projectId = fromRequests.Count > 0 ? PurchaseOrderRules.DestinationOf(fromRequests.Select(r => r.ProjectId).ToList()) : draft.ProjectId;
        if (projectId is { } pid && pid != order?.ProjectId)
        {
            var project = await projects.FindAsync(pid, ct);
            if (project is null)
            {
                return Invalid("projectId", "Projeto não encontrado.");
            }

            if (project.Status is "Closed" or "Cancelled")
            {
                return Invalid("projectId", "O projeto precisa estar em andamento (não encerrado nem cancelado).");
            }
        }

        var totals = PurchaseOrderRules.Totals(prepared.Select(l => (l.Quantity, l.UnitPrice)), draft.Freight, draft.IpiPct);
        if (totals.Total <= 0m || totals.Total > PurchaseOrderRules.MaxMoney)
        {
            return Invalid("lines", "O total do pedido precisa ser maior que zero.");
        }

        return new Prepared(party.Id, projectId, term.Id, draft.ExpectedDate, draft.Freight, draft.IpiPct, notes, prepared, totals);
    }
}
