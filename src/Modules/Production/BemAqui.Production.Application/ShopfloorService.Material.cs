using BemAqui.Inventory.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Production.Application;

public sealed partial class ShopfloorService
{
    /// <summary>
    /// Material da etapa no app (EST-03 CA 5): os itens da BOM liberada da OS (necessidade, reservado e baixado) e os pedidos do operador
    /// nesta etapa, com a situação ("Pedido enviado ao almoxarifado", "Material separado", "Recusado: {motivo}"). Só a etapa da fila do
    /// operador (fora dela, 404). Sem custo, valor nem lote (CA 6).
    /// </summary>
    public async Task<Result<ShopfloorMaterialView>> MaterialAsync(Guid stepId, CancellationToken cancellationToken = default)
    {
        var me = await OperatorAsync(cancellationToken);
        if (me.IsFailure)
        {
            return me.Error!;
        }

        if (await VisibleOrderAsync(stepId, me.Value.Id, cancellationToken) is not { } orderId)
        {
            return StepNotFound();
        }

        var options = await requisitions.OptionsAsync(orderId, stepId, cancellationToken);
        if (options.IsFailure)
        {
            return options.Error!;
        }

        var number = await db.ProductionOrders.AsNoTracking().Where(o => o.Id == orderId).Select(o => o.Number).SingleAsync(cancellationToken);
        return new ShopfloorMaterialView(
            stepId, number, options.Value.BomReleased,
            options.Value.Items.Select(i => new ShopfloorMaterialItem(i.ItemId, i.ItemCode, i.ItemName, i.UnitCode, i.Required, i.Reserved, i.Issued)).ToList(),
            options.Value.Requests.Select(Map).ToList());
    }

    /// <summary>
    /// "Pedir material" (EST-03 CA 5 e 6): o servidor confere a etapa (pronta, em andamento ou pausada da fila do operador, OS não
    /// cancelada nem concluída; outra, 404) e o funcionário ativo do usuário logado, e pede ao Inventory a requisição <c>Requested</c>.
    /// Este caminho <b>nunca</b> baixa estoque: não conhece o motor (teste de arquitetura). O corpo só aceita item e quantidade.
    /// </summary>
    public async Task<Result<ShopfloorMaterialRequest>> RequestMaterialAsync(Guid stepId, ShopfloorMaterialInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var me = await OperatorAsync(cancellationToken);
        if (me.IsFailure)
        {
            return me.Error!;
        }

        if (await VisibleOrderAsync(stepId, me.Value.Id, cancellationToken) is not { } orderId)
        {
            return StepNotFound();
        }

        var stepName = await db.ProductionOrderSteps.AsNoTracking().Where(s => s.Id == stepId).Select(s => s.Name).SingleAsync(cancellationToken);
        var result = await requisitions.RequestAsync(
            new MaterialRequestCommand(orderId, stepId, stepName, me.Value.Name, (input.Lines ?? []).Select(l => new MaterialRequestLine(l.ItemId, l.Quantity)).ToList()), cancellationToken);
        return result.IsFailure ? result.Error! : Map(result.Value);
    }

    private static ShopfloorMaterialRequest Map(MaterialRequestInfo r) => new(
        r.Id, r.Number, r.State, r.DeclineReason, r.RequestedAt, r.DecidedAt,
        r.Lines.Select(l => new ShopfloorMaterialRequestLine(l.ItemId, l.ItemCode, l.ItemName, l.UnitCode, l.Quantity)).ToList());
}
