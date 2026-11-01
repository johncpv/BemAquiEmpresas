using BemAqui.Finance.Application;
using BemAqui.Finance.Domain;
using BemAqui.Fiscal.Domain;
using BemAqui.Host.Persistence;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Finance;

/// <summary>
/// A regra pela nota (FIS-03a CA 13, FIN-08 CA 4) cumpre o mesmo contrato da regra pelo título: notas autorizadas de produção na data da
/// nota, na conta dos títulos do projeto (sem título, a 1.1); a de homologação e a fora do intervalo ficam de fora.
/// </summary>
public class InvoiceRevenueRecognitionContractTests(TenancyFixture tenancy) : RevenueRecognitionContractTests(tenancy), IClassFixture<TenancyFixture>
{
    protected override RevenueBasis ExpectedBasis => RevenueBasis.Invoice;

    protected override IRevenueRecognition Resolve(IServiceProvider tenantScope) => tenantScope.GetRequiredService<IEnumerable<IRevenueRecognition>>().Single(r => r.Basis == RevenueBasis.Invoice);

    protected override async Task<IReadOnlyList<Guid>> SeedAsync(TenantDbContext db, DateOnly from, DateOnly to)
    {
        var a12 = await db.Accounts.Where(a => a.Code == "1.2").Select(a => a.Id).SingleAsync();
        var project = Guid.CreateVersion7();
        db.Receivables.Add(new Receivable
        {
            Number = "RC-" + Guid.NewGuid().ToString("N")[..10], PartyId = Guid.CreateVersion7(), AccountId = a12, Description = "Parcela", Amount = 1m, IssueDate = from.AddYears(-1),
            DueDate = from.AddYears(-1), ProjectId = project, Origin = DocumentOrigin.Manual, CreatedAt = DateTimeOffset.UtcNow,
        });
        db.OutgoingInvoices.AddRange(
            Note(project, from.AddDays(5), 2_000m), Note(null, from.AddMonths(2), 750.25m), Note(project, from.AddMonths(4), 1_000m),
            Note(project, from.AddDays(6), 9_999m, FiscalEnvironment.Homologation), Note(project, to.AddDays(1), 333m));
        await db.SaveChangesAsync();
        return [project];
    }

    private static OutgoingInvoice Note(Guid? project, DateOnly issuedOn, decimal total, FiscalEnvironment environment = FiscalEnvironment.Production)
    {
        var invoice = OutgoingInvoice.NewDraft(Guid.CreateVersion7(), "ROM-C", project ?? Guid.Empty, Guid.CreateVersion7(), Guid.CreateVersion7(), null, DateTimeOffset.UtcNow);
        if (project is null)
        {
            typeof(OutgoingInvoice).GetProperty(nameof(OutgoingInvoice.ProjectId))!.SetValue(invoice, null);
        }

        invoice.Environment = environment;
        invoice.ApplyTotals(new InvoiceTotals(total, 0, 0, 0, [], total, 0), null);
        invoice.BeginSubmission(new string('a', 64), DateTimeOffset.UtcNow);
        var number = Random.Shared.NextInt64(1, long.MaxValue / 2);
        invoice.Apply(new EmitterNfeState(Guid.CreateVersion7(), FiscalDocumentStatus.Authorized, number.ToString("D44", System.Globalization.CultureInfo.InvariantCulture), 2, number, "1",
            issuedOn.ToDateTime(new TimeOnly(15, 0), DateTimeKind.Utc), null, null, null), DateTimeOffset.UtcNow);
        invoice.MarkAuthorizationPublished(issuedOn, DateTimeOffset.UtcNow);
        invoice.AttachAuthorizedXml(Guid.CreateVersion7(), new string('b', 64));
        invoice.AttachDanfe(Guid.CreateVersion7());
        return invoice;
    }
}
