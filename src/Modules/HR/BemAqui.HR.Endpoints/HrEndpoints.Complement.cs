using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Web;
using BemAqui.HR.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.HR.Endpoints;

internal static partial class HrEndpoints
{
    /// <summary>
    /// Complemento do cadastro (CAD-04c2): leitura só por rotas <c>/sensitive</c> com <c>hr.view_sensitive</c>; os comandos exigem
    /// <c>hr.edit</c> na rota e <c>hr.view_sensitive</c> no serviço; a revisão da conta, <c>hr.approve</c>. Todo dependente e anexo
    /// precisa ser do funcionário da rota (IDOR, 404).
    /// </summary>
    private static void MapEmployeeComplement(RouteGroupBuilder employees)
    {
        employees.MapGet("/{id:guid}/dependents/sensitive", async (Guid id, EmployeeComplementService service, CancellationToken ct) => (await service.DependentsAsync(id, ct)).ToHttpResult())
            .RequirePermission("hr.view_sensitive").WithName("ListEmployeeDependentsSensitive").Produces<IReadOnlyList<DependentDetail>>();
        employees.MapPost("/{id:guid}/dependents", async (Guid id, DependentInput input, EmployeeComplementService service, CancellationToken ct) =>
                (await service.AddDependentAsync(id, input, ct)).ToHttpResult(saved => Results.Created($"/api/v1/hr/employees/{id}/dependents/{saved.Id}", saved)))
            .RequirePermission("hr.edit").WithName("AddEmployeeDependent").Produces<DependentDetail>(StatusCodes.Status201Created);
        employees.MapPut("/{id:guid}/dependents/{dependentId:guid}", async (Guid id, Guid dependentId, DependentInput input, EmployeeComplementService service, CancellationToken ct) =>
                (await service.UpdateDependentAsync(id, dependentId, input, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("UpdateEmployeeDependent").Produces<DependentDetail>();
        employees.MapDelete("/{id:guid}/dependents/{dependentId:guid}", async (Guid id, Guid dependentId, EmployeeComplementService service, CancellationToken ct) =>
                (await service.DeleteDependentAsync(id, dependentId, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("DeleteEmployeeDependent");

        employees.MapGet("/{id:guid}/payment/sensitive", async (Guid id, EmployeeComplementService service, CancellationToken ct) => (await service.PaymentAsync(id, ct)).ToHttpResult())
            .RequirePermission("hr.view_sensitive").WithName("GetEmployeePaymentSensitive").Produces<EmployeePaymentDetail>();
        employees.MapGet("/{id:guid}/bank-account/status", async (Guid id, EmployeeComplementService service, CancellationToken ct) => (await service.BankStatusAsync(id, ct)).ToHttpResult())
            .RequirePermission("hr.view_sensitive").WithName("GetEmployeeBankStatus").Produces<BankAccountStatusView>();
        employees.MapGet("/{id:guid}/bank-account/sensitive", async (Guid id, EmployeeComplementService service, CancellationToken ct) => (await service.RevealBankAsync(id, ct)).ToHttpResult())
            .RequirePermission("hr.view_sensitive").WithName("RevealEmployeeBankAccount").Produces<BankAccountRevealed>();
        employees.MapPut("/{id:guid}/bank-account", async (Guid id, BankAccountInput input, EmployeeComplementService service, CancellationToken ct) => (await service.SaveBankAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("SaveEmployeeBankAccount").Produces<EmployeePaymentDetail>();
        employees.MapPost("/{id:guid}/bank-account/review", async (Guid id, BankReviewInput input, EmployeeComplementService service, CancellationToken ct) => (await service.ReviewBankAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("hr.approve").WithName("ReviewEmployeeBankAccount").Produces<EmployeePaymentDetail>();
        employees.MapPut("/{id:guid}/additional", async (Guid id, AdditionalInput input, EmployeeComplementService service, CancellationToken ct) => (await service.SaveAdditionalAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("SaveEmployeeAdditional").Produces<EmployeePaymentDetail>();

        employees.MapGet("/{id:guid}/documents/sensitive", async (Guid id, EmployeeComplementService service, CancellationToken ct) => (await service.DocumentsAsync(id, ct)).ToHttpResult())
            .RequirePermission("hr.view_sensitive").WithName("GetEmployeeDocumentsSensitive").Produces<EmployeeDocumentsDetail>();
        employees.MapPut("/{id:guid}/documents", async (Guid id, EmployeeDocumentsInput input, EmployeeComplementService service, CancellationToken ct) => (await service.SaveDocumentsAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("SaveEmployeeDocuments").Produces<EmployeeDocumentsDetail>();
        employees.MapGet("/{id:guid}/documents/files", async (Guid id, EmployeeComplementService service, CancellationToken ct) => (await service.DocumentFilesAsync(id, ct)).ToHttpResult())
            .RequirePermission("hr.view_sensitive").WithName("ListEmployeeDocumentFiles").Produces<IReadOnlyList<AttachmentDto>>();
        employees.MapGet("/{id:guid}/documents/files/{attachmentId:guid}/content", async (Guid id, Guid attachmentId, EmployeeComplementService service, HttpContext http, CancellationToken ct) =>
            {
                var result = await service.DocumentFileAsync(id, attachmentId, ct);
                if (result.IsFailure)
                {
                    return result.Error!.ToProblem();
                }

                AttachmentEndpoints.ApplyDownloadHeaders(http.Response);
                return Results.File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
            })
            .RequirePermission("hr.view_sensitive").WithName("DownloadEmployeeDocumentFile");
    }
}
