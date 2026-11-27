using BemAqui.BuildingBlocks.Integration;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Time;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Commercial.Application.Documents;
using BemAqui.Commercial.Contracts;
using BemAqui.Commercial.Domain;
using BemAqui.Parties.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Commercial.Endpoints;

/// <summary>
/// Status do projeto para o aplicativo (INT-03 CA 19): DTO <b>próprio e mínimo</b>, número, nome, etapa atual (letra do display e nome), situação
/// ("No prazo", "Atrasado", "Entregue" ou "Cancelado") e data prevista de entrega. Nenhum campo de custo, orçado, margem ou valor: o teste de campos
/// proibidos confere por reflexão.
/// </summary>
public sealed record IntegrationProjectStatus(string Number, string Name, string? StageLetter, string StageName, string Situation, DateOnly? PromisedDate);

/// <summary><c>GET /api/v1/integration/customers/{id}/projects</c> com <c>projects.view</c> (perfil fábrica), montado a partir do <see cref="IProjects"/>.</summary>
public static class IntegrationProjectEndpoints
{
    public static void MapIntegrationProjects(this IEndpointRouteBuilder api)
    {
        api.MapIntegrationGroup().MapGet("/customers/{id:guid}/projects", async (Guid id, IParties parties, IProjects projects, IAuditWriter audit, TimeProvider clock,
                CancellationToken ct) =>
            {
                if (await parties.FindAsync(id, ct) is not { IsDeleted: false } party || !party.Roles.Contains(PartyRoleKind.Customer))
                {
                    return IntegrationApi.NotFound();
                }

                var today = BusinessTime.Today(clock);
                var list = (await projects.ListByPartyAsync(id, ct)).Select(p => Map(p, today)).ToList();
                await audit.RecordReadAsync("Party", id.ToString(), "projects", ct);
                return Results.Ok(list);
            })
            .RequirePermission("projects.view").WithName("IntegrationListCustomerProjects").Produces<IReadOnlyList<IntegrationProjectStatus>>();
    }

    public static IntegrationProjectStatus Map(ProjectSummary project, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(project);
        var status = Enum.TryParse<ProjectStatus>(project.Status, out var parsed) ? parsed : ProjectStatus.Planned;
        var index = ProjectSummaryPdf.StageIndex(status);
        var situation = status switch
        {
            ProjectStatus.Cancelled => "Cancelado",
            ProjectStatus.Delivered or ProjectStatus.Closed => "Entregue",
            _ when project.PromisedDate is { } promised && promised < today => "Atrasado",
            _ => "No prazo",
        };
        return new IntegrationProjectStatus(project.Number, project.Name, index >= 0 ? ProjectSummaryPdf.Letters[index] : null, ProjectSummaryPdf.StatusLabel(status), situation,
            project.PromisedDate);
    }
}
