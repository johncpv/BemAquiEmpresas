using BemAqui.BuildingBlocks.Attachments;
using BemAqui.HR.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.HR.Application;

/// <summary>
/// Documentos extras (CAD-04c2 CA 11 e 12): CNH e reservista no funcionário; os digitalizados no dono de anexo
/// <c>EmployeeDocument</c> (leitura e escrita com <c>hr.view_sensitive</c>, pdf, png e jpg até 10 MB, <c>AuditReads</c>).
/// </summary>
public sealed partial class EmployeeComplementService
{
    public const string DocumentOwner = "EmployeeDocument";

    private static readonly string[] CnhCategories = ["A", "B", "C", "D", "E", "AB", "AC", "AD", "AE", "ACC"];

    public async Task<Result<EmployeeDocumentsDetail>> DocumentsAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        var e = await db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == employeeId, cancellationToken);
        if (e is null)
        {
            return NotFound();
        }

        await audit.RecordReadAsync(nameof(Employee) + ".Documents", employeeId.ToString(), cancellationToken);
        return new EmployeeDocumentsDetail(e.CnhNumber, e.CnhCategory, e.CnhExpiry, e.MilitaryCertificate, e.Version);
    }

    /// <summary>CNH (11 dígitos, categoria e validade) e reservista (opcional), com <c>hr.view_sensitive</c>.</summary>
    public async Task<Result<EmployeeDocumentsDetail>> SaveDocumentsAsync(Guid employeeId, EmployeeDocumentsInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await CanSensitiveAsync(cancellationToken))
        {
            return Forbidden();
        }

        var e = await db.Employees.FirstOrDefaultAsync(x => x.Id == employeeId, cancellationToken);
        if (e is null)
        {
            return NotFound();
        }

        var cnh = Digits(input.CnhNumber);
        if (cnh is not null && cnh.Length != 11)
        {
            return DependentRules.Field("cnhNumber", "O número da CNH tem 11 dígitos.");
        }

        var category = Clean(input.CnhCategory);
        if (category is not null && !CnhCategories.Contains(category))
        {
            return DependentRules.Field("cnhCategory", "Escolha a categoria da CNH: A, B, C, D, E ou a combinação (AB, AC, AD, AE).");
        }

        if (cnh is null && (category is not null || input.CnhExpiry is not null))
        {
            return DependentRules.Field("cnhNumber", "Informe o número da CNH antes da categoria e da validade.");
        }

        if (input.CnhExpiry is { Year: < 1950 })
        {
            return DependentRules.Field("cnhExpiry", "Informe a validade da CNH.");
        }

        var military = (input.MilitaryCertificate ?? string.Empty).Trim();
        if (military.Length > 20)
        {
            return DependentRules.Field("militaryCertificate", "O certificado de reservista aceita até 20 caracteres.");
        }

        e.CnhNumber = cnh;
        e.CnhCategory = category;
        e.CnhExpiry = input.CnhExpiry;
        e.MilitaryCertificate = military.Length == 0 ? null : military;
        await SaveEmployeeAsync(e, input.Version, cancellationToken);
        return new EmployeeDocumentsDetail(e.CnhNumber, e.CnhCategory, e.CnhExpiry, e.MilitaryCertificate, e.Version);
    }

    /// <summary>Anexos do funcionário (CA 12): a rota exige <c>hr.view_sensitive</c> antes de qualquer consulta (sem revelar quantos são).</summary>
    public async Task<Result<IReadOnlyList<AttachmentDto>>> DocumentFilesAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        if (!await db.Employees.AnyAsync(x => x.Id == employeeId, cancellationToken))
        {
            return NotFound();
        }

        return (await attachments.ListCheckedAsync(DocumentOwner, employeeId, cancellationToken)).ToList();
    }

    /// <summary>
    /// Download (CA 12): o funcionário precisa existir e o anexo ser dele (de outro, 404); a leitura sensível vai para a auditoria
    /// pelo <c>AuditReads</c> do dono, com quem, quando, qual documento e o IP.
    /// </summary>
    public async Task<Result<AttachmentContent>> DocumentFileAsync(Guid employeeId, Guid attachmentId, CancellationToken cancellationToken = default)
    {
        if (!await db.Employees.AnyAsync(x => x.Id == employeeId, cancellationToken))
        {
            return NotFound();
        }

        return await attachments.DownloadCheckedAsync(DocumentOwner, employeeId, attachmentId, cancellationToken);
    }
}
