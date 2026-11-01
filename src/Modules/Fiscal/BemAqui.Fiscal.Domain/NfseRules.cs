using System.Globalization;

namespace BemAqui.Fiscal.Domain;

/// <summary>
/// Regras puras da NFS-e da instalação (FIS-06): a discriminação sugerida, o valor do serviço pelos itens <c>Service</c> ainda não faturados,
/// o valor líquido, quando o Emissor emite a NFS-e do município (lacuna L-m), a conferência do registro do portal e a comparação do ISS
/// digitado com o calculado. Sem banco.
/// </summary>
public static class NfseRules
{
    /// <summary>Portal da NFS-e de Caçapava (e-Transparência), para o processo provisório (D5).</summary>
    public const string PortalUrl = "https://nfe.etransparencia.com.br/sp.cacapava/";

    public const string PortalNotice = "Até o Emissor Fiscal emitir a NFS-e de Caçapava, emita a nota no portal da prefeitura e registre aqui.";
    public const string CancelInPortal = "Cancele no portal da prefeitura e registre aqui.";

    /// <summary>Escopo da chave do Emissor que emite NFS-e.</summary>
    public const string EmitScope = "nfse:emitir";

    /// <summary>Série do RPS na prefeitura (a confirmar com a contadora; o exemplo do contrato do Emissor usa "1").</summary>
    public const string Series = "1";

    /// <summary>
    /// Indicador da operação do IBS/CBS (<c>IndCodOpe</c>, anexo VII da NFS-e Nacional), o do exemplo do contrato do Emissor; a confirmar com a
    /// contadora antes do provider de Caçapava (pendência registrada na story).
    /// </summary>
    public const string IbsCbsOperationCode = "030101";

    public const int DescriptionMaxLength = 2000;

    /// <summary>Diferença tolerada entre o ISS digitado da nota da prefeitura e o calculado pela regra (CA 6).</summary>
    public const decimal IssTolerance = 0.01m;

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>"Instalação de {equipamento}, projeto {número}, OS {número}", saneada como a FIS-03a CA 4 (sem controle, espaços normalizados).</summary>
    public static string Discrimination(string? equipment, string projectNumber, string orderNumber)
    {
        var what = NfeTextRules.Clean(equipment) ?? "equipamento";
        var text = NfeTextRules.Clean($"Instalação de {what}, projeto {projectNumber}, OS {orderNumber}") ?? string.Empty;
        return text.Length > DescriptionMaxLength ? text[..DescriptionMaxLength] : text;
    }

    /// <summary>Código de verificação ou protocolo: sem espaços nem caracteres de controle; vazio vira nulo.</summary>
    public static string? CleanCode(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var clean = new string(value.Where(c => !char.IsWhiteSpace(c) && !char.IsControl(c)).ToArray());
        return clean.Length == 0 ? null : clean;
    }

    /// <summary>Número da NFS-e da prefeitura: só dígitos, de 1 a 15; senão nulo.</summary>
    public static long? ParseNumber(string? value)
    {
        var digits = new string((value ?? string.Empty).Where(char.IsAsciiDigit).ToArray()).TrimStart('0');
        return digits.Length is > 0 and <= 15 && long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : null;
    }

    /// <summary>Valor líquido da NFS-e: o serviço menos o ISS retido pelo tomador.</summary>
    public static decimal NetAmount(decimal services, decimal iss, bool withheld) => TaxCalculator.Round(services - (withheld ? iss : 0m));

    /// <summary>
    /// O Emissor emite a NFS-e deste emitente? (lacuna L-m) Só com a chave com <c>nfse:emitir</c> e o município do emitente entre os da NFS-e
    /// habilitada no <c>GET /me</c>; sem a lista (o Emissor de hoje), não: vale o processo provisório pelo portal (D5).
    /// </summary>
    public static bool EmitterEnabled(IReadOnlyCollection<string> scopes, IReadOnlyCollection<string>? nfseCities, string? issuerCity)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        return scopes.Contains(EmitScope, StringComparer.Ordinal) && nfseCities is { Count: > 0 } && issuerCity is { Length: 7 } && nfseCities.Contains(issuerCity, StringComparer.Ordinal);
    }

    /// <summary>Aviso da diferença do ISS (CA 6): acima de R$ 0,01 mostra o calculado, sem bloquear; nulo quando bate.</summary>
    public static string? IssDifference(decimal typed, decimal calculated) =>
        Math.Abs(TaxCalculator.Round(typed) - TaxCalculator.Round(calculated)) > IssTolerance
            ? $"O ISS da nota é diferente do calculado ({calculated.ToString("C", PtBr)}). Confira com a contadora."
            : null;

    /// <summary>Aviso do valor do serviço diferente do rascunho (vale o da nota da prefeitura para a receita).</summary>
    public static string? AmountDifference(decimal typed, decimal draft) =>
        Math.Abs(TaxCalculator.Round(typed) - TaxCalculator.Round(draft)) > IssTolerance
            ? $"O valor da nota é diferente do rascunho ({draft.ToString("C", PtBr)}). A receita conta pelo valor da nota da prefeitura."
            : null;

    /// <summary>
    /// Conferência do registro do portal (CA 6), campo a campo: número só com dígitos, código de verificação, data até hoje e não antes do
    /// aceite, valor maior que zero e ISS de zero até o valor. Vazio, pode registrar.
    /// </summary>
    public static Dictionary<string, string[]> RegistrationErrors(long? number, string? verificationCode, DateOnly? issuedOn, decimal servicesAmount, decimal iss, DateOnly today, DateOnly? acceptedOn)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (number is null)
        {
            errors["number"] = ["Informe o número da NFS-e como saiu na nota da prefeitura (só números)."];
        }

        if (CleanCode(verificationCode) is not { } code || code.Length > OutgoingInvoice.VerificationCodeMaxLength)
        {
            errors["verificationCode"] = [$"Informe o código de verificação da nota da prefeitura (até {OutgoingInvoice.VerificationCodeMaxLength} caracteres)."];
        }

        if (issuedOn is not { } on || on > today || (acceptedOn is { } accepted && on < accepted))
        {
            errors["issuedOn"] = ["Informe a data de emissão da nota: do aceite técnico até hoje."];
        }

        if (servicesAmount <= 0)
        {
            errors["servicesAmount"] = ["Informe o valor do serviço da nota, maior que zero."];
        }

        if (iss < 0 || iss > servicesAmount)
        {
            errors["issAmount"] = ["Informe o ISS da nota: de zero até o valor do serviço."];
        }

        return errors;
    }

    /// <summary>Conferência do registro do cancelamento feito na prefeitura (CA 9): motivo de 15 a 500 caracteres e o protocolo.</summary>
    public static Dictionary<string, string[]> CancellationErrors(string? reason, string? protocol)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var cleanReason = NfeTextRules.Clean(reason);
        if (cleanReason is null || cleanReason.Length < 15 || cleanReason.Length > OutgoingInvoice.CancellationReasonMaxLength)
        {
            errors["reason"] = ["Informe o motivo do cancelamento, de 15 a 500 caracteres."];
        }

        if (CleanCode(protocol) is not { } p || p.Length > OutgoingInvoice.CancellationProtocolMaxLength)
        {
            errors["protocol"] = ["Informe o número do protocolo do cancelamento na prefeitura."];
        }

        return errors;
    }
}
