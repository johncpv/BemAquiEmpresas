using BemAqui.HR.Domain;

namespace BemAqui.HR.Application;

/// <summary>
/// Versão de tabela legal na tela (FLH-01 CA 14): conteúdo completo (tabela legal não é dado pessoal), quem cadastrou, editou e conferiu,
/// a fonte, a vigência (<c>InForceToday</c>, <c>IsFuture</c> para o chip "A partir de {data}"), os avisos da validação (continuidade do
/// IRRF) e <c>MineToCheck</c> (o usuário cadastrou ou editou: não confere, CA 5).
/// </summary>
public sealed record PayrollTaxTableDetail(
    Guid Id,
    PayrollTaxTableKind Kind,
    string KindLabel,
    DateOnly ValidFrom,
    PayrollTaxTableStatus Status,
    Guid? RectifiesId,
    string? RectifyReason,
    PayrollTaxContent Content,
    int SchemaVersion,
    PayrollRounding Rounding,
    string Source,
    int? SeedPackage,
    string? CreatedByName,
    DateTimeOffset CreatedAt,
    IReadOnlyList<string> EditedByNames,
    DateTimeOffset? EditedAt,
    string? CheckedByName,
    DateTimeOffset? CheckedAt,
    string? CheckedNote,
    bool CheckedWithDataset,
    bool InForceToday,
    bool IsFuture,
    bool MineToCheck,
    IReadOnlyList<string> Warnings,
    uint Version);

/// <summary>Seção de um tipo (CA 14): a vigente hoje, as versões (futuras, vigente e antigas) e, sem versão, a nota do pacote do ano.</summary>
public sealed record PayrollTaxTableSection(PayrollTaxTableKind Kind, string Label, Guid? InForceId, IReadOnlyList<PayrollTaxTableDetail> Versions, string? PendingNote);

/// <summary>
/// RH → Tabelas da folha: as seções, quantas versões "A confirmar com a contadora", se o usuário é o único que aprova (CA 6: conferir a
/// própria versão pede o dataset) e a base sugerida do simulador (o salário médio, só com <c>hr.view_sensitive</c>).
/// </summary>
public sealed record PayrollTaxTablesView(IReadOnlyList<PayrollTaxTableSection> Sections, int Unconfirmed, bool SingleApprover, decimal? SuggestedBase);

/// <summary>Cadastro e edição (CA 2 a 4): tipo (só no cadastro), "vale a partir de", conteúdo do tipo, arredondamento, fonte e a versão.</summary>
public sealed record PayrollTaxTableInput(PayrollTaxTableKind? Kind, DateOnly? ValidFrom, PayrollTaxContent? Content, PayrollRounding? Rounding, string? Source, uint? Version);

/// <summary>"Marcar como conferido" (CA 5 e 6): com quem e quando foi conferido e, para o Proprietário único, o dataset da contadora.</summary>
public sealed record PayrollTaxTableCheckInput(string? Accountant, DateOnly? CheckedOn, string? Dataset, uint Version);

/// <summary>Retificar (CA 13): motivo obrigatório e o conteúdo corrigido (vazio copia o da versão retificada); a vigência é a mesma.</summary>
public sealed record PayrollTaxTableRectifyInput(string? Reason, PayrollTaxContent? Content, PayrollRounding? Rounding, string? Source, uint Version);

/// <summary>Folha afetada por uma versão (padrão 7): só as calculadas (FLH-02a em diante) na vigência da versão.</summary>
public sealed record PayrollTaxImpactRun(Guid RunId, DateOnly Period, PayrollRunType Type, PayrollRunStatus Status);

/// <summary>Impacto (CA 13, padrão 7): as folhas em rascunho (ganham "Recalcular") e as fechadas (nunca recalculadas: "Ver diferença").</summary>
public sealed record PayrollTaxTableImpact(IReadOnlyList<PayrollTaxImpactRun> DraftRuns, IReadOnlyList<PayrollTaxImpactRun> ClosedRuns);

/// <summary>Simulador (CA 10): competência "2026-04", base, dependentes (IRRF) e filhos (salário-família).</summary>
public sealed record PayrollTaxSimulationInput(string? Competence, decimal Base, int Dependents, int Children);

/// <summary>Linha do simulador: a versão que o motor usaria, o valor e a memória (faixa, alíquota, parcela, redução, arredondamento).</summary>
public sealed record PayrollTaxSimulationLine(PayrollTaxTableKind Kind, string Label, Guid TableId, PayrollTaxTableStatus Status, DateOnly ValidFrom, decimal Amount, IReadOnlyList<string> Memory);

/// <summary>Resultado do simulador, com as tabelas que faltam na competência ("Falta a tabela {tipo} para {mês}.").</summary>
public sealed record PayrollTaxSimulation(DateOnly Competence, decimal Base, int Dependents, int Children, IReadOnlyList<PayrollTaxSimulationLine> Lines, IReadOnlyList<string> Missing);

/// <summary>Versão escolhida para uma competência (CA 8): id, situação, esquema e conteúdo, para o motor gravar no holerite (ADR 0039).</summary>
public sealed record PayrollTaxTableInForce(Guid Id, PayrollTaxTableKind Kind, PayrollTaxTableStatus Status, DateOnly ValidFrom, int SchemaVersion, PayrollTaxContent Content, PayrollRounding Rounding);

/// <summary>Mudança listada no fechamento (CA 12): o quê, antes → depois por campo, quem e quando.</summary>
public sealed record PayrollChangeNotice(string What, string Action, IReadOnlyList<PayrollChangeField> Fields, string? ByName, DateTimeOffset At);

public sealed record PayrollChangeField(string Field, string? Before, string? After);

/// <summary>Leitura das tabelas pelo cálculo (FLH-02a em diante), dentro do HR.</summary>
public interface IPayrollTaxTables
{
    /// <summary>
    /// A versão vigente do tipo na competência (CA 8); sem versão, 422 <c>payroll_tax_table_missing</c> "Falta a tabela {tipo} para
    /// {mês}." com o link para cadastrar.
    /// </summary>
    Task<BemAqui.SharedKernel.Result<PayrollTaxTableInForce>> ForCompetenceAsync(PayrollTaxTableKind kind, DateOnly competence, CancellationToken cancellationToken = default);

    /// <summary>Fechamento oficial da folha calculada (FLH-02a CA 16): todas as versões usadas precisam estar conferidas.</summary>
    Task<BemAqui.SharedKernel.Result> RequireActiveAsync(IReadOnlyCollection<Guid> tableIds, CancellationToken cancellationToken = default);
}
