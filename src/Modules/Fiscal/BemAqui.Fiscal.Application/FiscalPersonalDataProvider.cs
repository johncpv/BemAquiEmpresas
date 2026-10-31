using BemAqui.BuildingBlocks.Privacy;

namespace BemAqui.Fiscal.Application;

/// <summary>
/// Fiscal no arquivo do titular (LGPD-01a CA 8): a NF-e de saída ainda não guarda o destinatário (FIS-01: só ids, totais e situação), então
/// nada sai daqui por enquanto. A FIS-03a, ao gravar o destinatário e o vínculo com o terceiro, exporta as notas em que o titular aparece
/// (número, série, data e situação) e cobre a entidade nova neste provedor; sem isso, o <c>PersonalDataTests</c> reprova.
/// </summary>
public sealed class FiscalPersonalDataProvider : IPersonalDataProvider
{
    public string Module => "fiscal";

    public string Title => "Fiscal";

    public Task<PersonalDataSection?> ExportAsync(PersonalDataExportContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult<PersonalDataSection?>(null);
}
