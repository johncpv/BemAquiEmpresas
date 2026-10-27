using System.Text.Json.Serialization;

namespace BemAqui.HR.Domain;

/// <summary>Situação do aviso do holerite na aba Holerites (RH-02b CA 9).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PayslipNoticeStatus>))]
public enum PayslipNoticeStatus
{
    /// <summary>Tem usuário ligado e ativo, ainda não avisado.</summary>
    NotNotified,
    Queued,
    Sent,
    Failed,
    NoUser,
    Terminated,
}

/// <summary>Regras do holerite em PDF e do aviso, sem banco (RH-02b CA 1, 2, 4 e 7).</summary>
public static class PayslipDocumentRules
{
    /// <summary>Dono dos PDFs guardados (ADR 0019 §7): o holerite.</summary>
    public const string OwnerType = "Payslip";

    /// <summary>Dono do ZIP de "Baixar todos" (o id é o da folha).</summary>
    public const string BatchOwnerType = "PayslipBatch";

    /// <summary>Template do aviso (FND-08): só nome, mês, empresa e o link.</summary>
    public const string NoticeTemplate = "payslip-available";

    /// <summary>Rota do canal do funcionário (ADR 0035 §4).</summary>
    public const string MyPayslipsPath = "/app/meus-holerites";

    /// <summary>Limite do PDF (ADR 0022): acima disso, 422 antes de gerar.</summary>
    public const int MaxPdfLines = 300;

    /// <summary>Acima disso, "Baixar todos" vai para o segundo plano (CA 2).</summary>
    public const int InlineBatchLimit = 20;

    public const string TooManyLinesCode = "payslip_pdf_too_many_lines";
    public const string NotClosedCode = "payroll_run_not_closed";
    public const string EmployeeNotActiveCode = "employee_not_active";
    public const string EmployeeNotActiveMessage = "Seu usuário não está ligado a um funcionário ativo. Fale com o RH.";
    public const string Watermark = "RASCUNHO — folha não fechada";

    /// <summary>CPF mascarado 3+2 (D15, CA 1): "123.***.***-45". Sem os 11 dígitos, tudo mascarado.</summary>
    public static string MaskCpf(string? cpf)
    {
        var digits = new string((cpf ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        return digits.Length == 11 ? $"{digits[..3]}.***.***-{digits[9..]}" : "***.***.***-**";
    }

    /// <summary>
    /// Nome do arquivo (CA 2): <c>holerite-{aaaa-mm}-{matrícula ou id curto}.pdf</c>, sem CPF nem nome. A matrícula fica só com letras,
    /// dígitos, "-" e "_"; vazia, entra o final do id (a parte aleatória do Guid v7).
    /// </summary>
    public static string FileName(DateOnly period, string? registration, Guid payslipId)
    {
        var safe = new string((registration ?? string.Empty).Trim().Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_').ToArray());
        var key = safe.Length > 0 ? safe[..Math.Min(safe.Length, 30)] : ShortId(payslipId);
        return $"holerite-{period:yyyy-MM}-{key}.pdf";
    }

    public static string ShortId(Guid id) => id.ToString("N")[^8..];

    /// <summary>Nome do ZIP de "Baixar todos".</summary>
    public static string BatchFileName(DateOnly period, PayrollRunType type) => $"holerites-{period:yyyy-MM}-{TypeSlug(type)}.zip";

    /// <summary>"10/2026" para o assunto do aviso (CA 7).</summary>
    public static string MonthYear(DateOnly period) => period.ToString("MM'/'yyyy", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Situação do aviso (CA 9): com aviso, a do e-mail (falhou, enviado ou na fila); sem aviso, desligado, sem usuário ou "não avisado".
    /// </summary>
    public static PayslipNoticeStatus Status(bool terminated, bool hasUser, bool notified, bool? sent, bool failed) =>
        !notified ? (terminated ? PayslipNoticeStatus.Terminated : hasUser ? PayslipNoticeStatus.NotNotified : PayslipNoticeStatus.NoUser)
        : failed ? PayslipNoticeStatus.Failed
        : sent == true ? PayslipNoticeStatus.Sent
        : PayslipNoticeStatus.Queued;

    private static string TypeSlug(PayrollRunType type) => type switch
    {
        PayrollRunType.Monthly => "mensal",
        PayrollRunType.Advance => "adiantamento",
        PayrollRunType.Vacation => "ferias",
        PayrollRunType.Thirteenth1 => "13-1",
        PayrollRunType.Thirteenth2 => "13-2",
        _ => "rescisao",
    };
}
