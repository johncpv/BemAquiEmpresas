using BemAqui.HR.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.HR.Application;

public sealed partial class EmployeeService
{
    /// <summary>Dados pessoais (CA 6 e 10): exige <c>hr.view_sensitive</c>; PIS e e-mail validados.</summary>
    public async Task<Result<EmployeeDetail>> UpdatePersonalAsync(Guid id, EmployeePersonalInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await CanSensitiveAsync(cancellationToken))
        {
            return Forbidden();
        }

        var e = await db.Employees.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (e is null)
        {
            return NotFound();
        }

        if (await CpfAsync(e, input.Cpf, cancellationToken) is { } badCpf)
        {
            return badCpf;
        }

        var pis = Digits(input.Pis);
        if (pis is not null && !Pis.IsValid(pis))
        {
            return PositionService.Invalid("pis", "PIS inválido.");
        }

        var email = Trim(input.Email, 150);
        if (email is not null && (!email.Contains('@', StringComparison.Ordinal) || email.Contains(' ', StringComparison.Ordinal) || !email.Split('@')[^1].Contains('.', StringComparison.Ordinal)))
        {
            return PositionService.Invalid("email", "E-mail inválido.");
        }

        e.BirthDate = input.BirthDate;
        e.Rg = Trim(input.Rg, 20);
        e.Pis = pis;
        e.CtpsNumber = Digits(input.CtpsNumber);
        e.CtpsSeries = Trim(input.CtpsSeries, 10);
        e.VoterId = Digits(input.VoterId);
        e.ZipCode = Digits(input.ZipCode);
        e.Street = Trim(input.Street, 150);
        e.Number = Trim(input.Number, 20);
        e.Complement = Trim(input.Complement, 100);
        e.District = Trim(input.District, 100);
        e.City = Trim(input.City, 100);
        e.State = Trim(input.State, 2)?.ToUpperInvariant();
        e.Phone = Digits(input.Phone);
        e.Email = email;
        await db.SaveChangesAsync(cancellationToken);
        return (await DetailsAsync([e], cancellationToken))[0];
    }
}
