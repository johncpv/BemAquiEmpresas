using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Production.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Production.Endpoints;

internal static partial class ProductionEndpoints
{
    /// <summary>OS de instalação (COM-06): criar com production.create, executar com production.edit e aceitar com production.approve.</summary>
    private static void MapInstallations(RouteGroupBuilder installations)
    {
        installations.MapGet(string.Empty, async (string? situation, Guid? projectId, int? page, int? pageSize, InstallationOrderService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(situation, projectId, new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize).Normalize(), ct)))
            .RequirePermission("production.view").WithName("ListInstallations").Produces<InstallationPage>();
        installations.MapGet("/defaults", async (Guid projectId, InstallationOrderService service, CancellationToken ct) => (await service.DefaultsAsync(projectId, ct)).ToHttpResult())
            .RequirePermission("production.create").WithName("GetInstallationDefaults").Produces<InstallationDefaults>();
        installations.MapGet("/installers", async (string? q, InstallationOrderService service, CancellationToken ct) => Results.Ok(await service.InstallersAsync(q, ct)))
            .RequirePermission("production.create").WithName("ListInstallers").Produces<IReadOnlyList<InstallerOption>>();
        installations.MapGet("/{id:guid}", async (Guid id, InstallationOrderService service, CancellationToken ct) => (await service.GetAsync(id, ct)).ToHttpResult())
            .RequirePermission("production.view").WithName("GetInstallation").Produces<InstallationDetail>();
        installations.MapPost(string.Empty, async (InstallationCreateInput input, InstallationOrderService service, CancellationToken ct) =>
                (await service.CreateAsync(input, ct)).ToHttpResult(saved => Results.Created("/api/v1/production/installations/" + saved.Id, saved)))
            .RequirePermission("production.create").RequireIdempotencyKey().WithName("CreateInstallation").Produces<InstallationDetail>(StatusCodes.Status201Created);
        installations.MapPut("/{id:guid}", async (Guid id, InstallationUpdateInput input, InstallationOrderService service, CancellationToken ct) => (await service.UpdateAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("production.edit").WithName("UpdateInstallation").Produces<InstallationDetail>();
        installations.MapPost("/{id:guid}/start", async (Guid id, InstallationDateInput input, InstallationOrderService service, CancellationToken ct) => (await service.StartAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("production.edit").WithName("StartInstallation").Produces<InstallationDetail>();
        installations.MapPost("/{id:guid}/steps/{stepId:guid}/complete", async (Guid id, Guid stepId, InstallationVersionInput input, InstallationOrderService service, CancellationToken ct) =>
                (await service.CompleteStepAsync(id, stepId, input.Version, ct)).ToHttpResult())
            .RequirePermission("production.edit").WithName("CompleteInstallationStep").Produces<InstallationDetail>();
        installations.MapPut("/{id:guid}/checklist/{itemId:guid}", async (Guid id, Guid itemId, InstallationChecklistInput input, InstallationOrderService service, CancellationToken ct) =>
                (await service.MarkChecklistAsync(id, itemId, input, ct)).ToHttpResult())
            .RequirePermission("production.edit").WithName("MarkInstallationChecklist").Produces<InstallationDetail>();
        installations.MapPost("/{id:guid}/accept", async (Guid id, InstallationAcceptInput input, InstallationOrderService service, CancellationToken ct) => (await service.AcceptAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("production.approve").RequireIdempotencyKey().WithName("AcceptInstallation").Produces<InstallationAcceptResult>();
        installations.MapPost("/{id:guid}/cancel", async (Guid id, InstallationCancelInput input, InstallationOrderService service, CancellationToken ct) => (await service.CancelAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("production.edit").WithName("CancelInstallation").Produces<InstallationDetail>();
    }

    /// <summary>Modelo da OS de instalação (COM-06 CA 4, P-22).</summary>
    private static void MapInstallationTemplate(RouteGroupBuilder template)
    {
        template.MapGet(string.Empty, async (InstallationTemplateService service, CancellationToken ct) => Results.Ok(await service.GetAsync(ct)))
            .RequirePermission("production.view").WithName("GetInstallationTemplate").Produces<InstallationTemplateView>();
        template.MapPut(string.Empty, async (InstallationTemplateInput input, InstallationTemplateService service, CancellationToken ct) => (await service.SaveAsync(input, ct)).ToHttpResult())
            .RequirePermission("production.edit").WithName("SaveInstallationTemplate").Produces<InstallationTemplateView>();
    }
}
