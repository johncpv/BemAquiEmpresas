using BemAqui.BuildingBlocks.Email;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.Identity.Contracts;

namespace BemAqui.Host;

/// <summary>Eventos de integração conhecidos pelo processo (API e CLI usam a mesma lista).</summary>
public static class HostEvents
{
    public static void Register(IntegrationEventRegistry events)
    {
        ArgumentNullException.ThrowIfNull(events);
        events.AddEmailEvents().AddEvent<UserActivated>()
            // CAD-06 → CAD-04a: o % de encargos recalcula o custo-hora dos cargos.
            .AddHandler<PayrollChargesChanged, BemAqui.HR.Application.PayrollChargesChangedHandler>()
            .AddEvent<BemAqui.HR.Contracts.HourlyCostChanged>();
    }
}
