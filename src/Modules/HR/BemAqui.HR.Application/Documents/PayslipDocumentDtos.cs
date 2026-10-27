using BemAqui.HR.Domain;

namespace BemAqui.HR.Application.Documents;

/// <summary>Arquivo para o download (PDF do holerite ou ZIP), já com o nome sem CPF nem nome do funcionário.</summary>
public sealed record PayslipFile(Stream Content, string FileName, string ContentType);

/// <summary>
/// "Baixar todos os holerites" (CA 2): até 20, o ZIP sai na hora (<c>AttachmentId</c> para baixar); acima, vai para o segundo plano
/// (<c>Queued</c>) com a mensagem "Gerando {n} holerites…" e o aviso no sino quando pronto.
/// </summary>
public sealed record PayslipBatchResult(bool Queued, int Count, Guid? AttachmentId, string FileName, string Message);

/// <summary>Holerite em "Meus holerites" (CA 5): competência, tipo, líquido e quando o próprio funcionário abriu pela primeira vez.</summary>
public sealed record MyPayslip(Guid Id, DateOnly Period, PayrollRunType Type, decimal Net, DateOnly? PaymentDate, DateTimeOffset? FirstViewedAt);

/// <summary>Vínculo do usuário da sessão com o funcionário (menu e "Meus holerites"): nome e situação, sem nada sensível.</summary>
public sealed record MyEmployeeLink(string Name, string Situation);

/// <summary>Situação do aviso de um holerite (CA 9), sem valor.</summary>
public sealed record PayslipNoticeRow(
    Guid PayslipId,
    Guid EmployeeId,
    string EmployeeName,
    string? Registration,
    PayslipNoticeStatus Status,
    DateTimeOffset? NotifiedAt,
    DateTimeOffset? SentAt,
    string? Error,
    DateTimeOffset? FirstViewedAt,
    bool ChangedAfterNotice);

/// <summary>Funcionário sem usuário (CA 7): o e-mail pessoal do cadastro, sugerido para o convite.</summary>
public sealed record PayslipNoticePerson(Guid EmployeeId, string Name, string? Registration, string? SuggestedEmail);

/// <summary>
/// Diálogo "Avisar funcionários" e a coluna de aviso (CA 7 a 10): quantos recebem agora, quantos já foram avisados, os sem usuário (com o
/// e-mail sugerido para o convite), os desligados (entregar o PDF pessoalmente) e os alterados depois do aviso.
/// </summary>
public sealed record PayslipNotices(
    bool RunClosed,
    int Pending,
    int AlreadyNotified,
    int Changed,
    IReadOnlyList<PayslipNoticePerson> WithoutUser,
    IReadOnlyList<PayslipNoticePerson> Terminated,
    IReadOnlyList<PayslipNoticeRow> Rows);

/// <summary>Avisar (CA 7 e 10): todos os ainda não avisados, ou só os alterados depois do aviso.</summary>
public sealed record PayslipNotifyInput(bool OnlyChanged);

/// <summary>Resultado: quantos avisos entraram na fila e "{n} avisos na fila de envio".</summary>
public sealed record PayslipNotifyResult(int Queued, string Message);
