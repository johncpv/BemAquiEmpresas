using BemAqui.BuildingBlocks.Pdf;
using BemAqui.Identity.Contracts;

namespace BemAqui.Identity.Application;

/// <summary>Empresa do cabeçalho do modelo único de PDF (ADR 0045 §1): o mesmo perfil e o mesmo logo publicado do <see cref="ICompanyIdentity"/>.</summary>
public sealed partial class CompanySettings : IPdfCompanySource
{
    async Task<PdfCompanyInfo> IPdfCompanySource.GetAsync(CancellationToken cancellationToken)
    {
        var info = await ((ICompanyIdentity)this).GetAsync(cancellationToken);
        return new PdfCompanyInfo(info.LegalName, info.TradeName, info.Document, info.Address, info.Phone, info.Email, info.Complete, info.LogoStoragePath);
    }
}
