using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.HR.Domain;

/// <summary>Tipo da tabela legal da folha (FLH-01 CA 1 a 4). Cada tipo tem o esquema próprio, versionado no código (ADR 0039 §1).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PayrollTaxTableKind>))]
public enum PayrollTaxTableKind
{
    Inss,
    Irrf,
    FamilyAllowance,
    MinimumWage,
    Fgts,
    TransportVoucher,
    EmployerCharges,
}

/// <summary>
/// Situação da versão (CA 5, 7 e 13): <c>Unconfirmed</c> ("A confirmar com a contadora", editável e já usada na simulação),
/// <c>Active</c> (conferida, imutável) e <c>Superseded</c> (substituída por uma retificação conferida).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PayrollTaxTableStatus>))]
public enum PayrollTaxTableStatus
{
    Unconfirmed,
    Active,
    Superseded,
}

/// <summary>Regra de arredondamento do resultado de cada imposto (CA 9): 2 casas; padrão <c>AwayFromZero</c>.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PayrollRounding>))]
public enum PayrollRounding
{
    AwayFromZero,
    ToEven,
    Truncate,
}

/// <summary>Faixa progressiva: limite superior (nulo = "acima de", só na última do IRRF), alíquota em % e parcela a deduzir (IRRF).</summary>
public sealed record TaxBracket(decimal? UpTo, decimal Rate, decimal Deduction = 0m);

/// <summary>INSS do empregado (esquema 1): faixas progressivas; o limite da última é o teto do salário de contribuição.</summary>
public sealed record InssContent(IReadOnlyList<TaxBracket> Brackets);

/// <summary>
/// Redução adicional do IRRF (esquema 1, formato da norma vigente): para o rendimento tributável do mês até <c>UpTo</c> (nulo = sem
/// limite), a redução é <c>FixedAmount − Rate% × rendimento</c>, nunca menor que zero nem maior que o imposto das faixas. Com
/// <c>Rate</c> zero, é a redução de valor fixo (o imposto zera até o valor).
/// </summary>
public sealed record IrrfReduction(decimal? UpTo, decimal FixedAmount, decimal Rate);

/// <summary>IRRF mensal (esquema 1): faixas com parcela a deduzir, dedução por dependente, desconto simplificado e a redução adicional.</summary>
public sealed record IrrfContent(IReadOnlyList<TaxBracket> Brackets, decimal DependentDeduction, decimal SimplifiedDiscount, IReadOnlyList<IrrfReduction> Reductions);

/// <summary>Salário-família (esquema 1): remuneração máxima para ter direito e a cota por filho.</summary>
public sealed record FamilyAllowanceContent(decimal MaxRemuneration, decimal QuotaPerChild);

/// <summary>Salário mínimo nacional (esquema 1): valor mensal.</summary>
public sealed record MinimumWageContent(decimal Monthly);

/// <summary>FGTS (esquema 1): alíquota mensal, a da multa da rescisão e a do aprendiz, em %.</summary>
public sealed record FgtsContent(decimal MonthlyRate, decimal TerminationRate, decimal ApprenticeRate);

/// <summary>Vale-transporte (esquema 1): percentual máximo de desconto sobre o salário base.</summary>
public sealed record TransportVoucherContent(decimal MaxDiscountRate);

/// <summary>
/// Encargos da empresa (esquema 1), pelo regime (P-01: Lucro Presumido): contribuição patronal, RAT (pelo CNAE preponderante) × FAP e
/// terceiros (pelo FPAS), em %.
/// </summary>
public sealed record EmployerChargesContent(string TaxRegime, decimal EmployerRate, decimal RatRate, decimal Fap, decimal ThirdPartiesRate);

/// <summary>Conteúdo da versão: só a seção do tipo vem preenchida (as outras nulas).</summary>
public sealed record PayrollTaxContent(
    InssContent? Inss = null,
    IrrfContent? Irrf = null,
    FamilyAllowanceContent? FamilyAllowance = null,
    MinimumWageContent? MinimumWage = null,
    FgtsContent? Fgts = null,
    TransportVoucherContent? TransportVoucher = null,
    EmployerChargesContent? EmployerCharges = null);

/// <summary>
/// Versão de uma tabela legal da folha (<c>hr.payroll_tax_tables</c>, FLH-01, ADR 0039 §1): tipo, "vale a partir de" (primeiro dia do
/// mês), situação, conteúdo validado pelo esquema do tipo (<see cref="SchemaVersion"/>), arredondamento e fonte. Vale até a versão
/// seguinte do mesmo tipo; nenhum valor legal fica no código (vem do pacote do ano ou do cadastro). Tabela legal não é dado pessoal.
/// </summary>
[DocumentName("A tabela da folha")]
public sealed class PayrollTaxTable : Entity, IAuditable, IConcurrencyVersioned
{
    public PayrollTaxTableKind Kind { get; init; }

    public DateOnly ValidFrom { get; set; }

    public PayrollTaxTableStatus Status { get; private set; } = PayrollTaxTableStatus.Unconfirmed;

    /// <summary>Retificação (CA 13): a versão <c>Active</c> que esta corrige, com a mesma vigência.</summary>
    public Guid? RectifiesId { get; init; }

    public string? RectifyReason { get; init; }

    /// <summary>JSON do conteúdo do tipo (jsonb), no esquema <see cref="SchemaVersion"/>.</summary>
    public string Content { get; private set; } = "{}";

    public int SchemaVersion { get; private set; } = PayrollTaxSchema.Current;

    public PayrollRounding Rounding { get; set; } = PayrollRounding.AwayFromZero;

    /// <summary>Norma e data de publicação (obrigatória).</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Ano do pacote do seed; nulo quando cadastrada à mão.</summary>
    public int? SeedPackage { get; init; }

    /// <summary>Quem cadastrou; nulo nas versões do seed (criadas pelo sistema).</summary>
    public Guid? CreatedBy { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Todos que editaram (CA 5: quem cadastrou ou editou não confere).</summary>
    public List<Guid> EditedBy { get; private set; } = [];

    public DateTimeOffset? EditedAt { get; private set; }

    public Guid? CheckedBy { get; private set; }

    public DateTimeOffset? CheckedAt { get; private set; }

    public string? CheckedNote { get; private set; }

    /// <summary>Dataset da contadora usado na conferência pelo Proprietário único (CA 6).</summary>
    public Guid? CheckedWithDatasetId { get; private set; }

    public DateTimeOffset? SupersededAt { get; private set; }

    public uint Version { get; set; }

    /// <summary>Entra na escolha por competência (CA 8): a conferida, ou a "A confirmar" que não é retificação pendente.</summary>
    public bool IsCurrent => Status == PayrollTaxTableStatus.Active || (Status == PayrollTaxTableStatus.Unconfirmed && RectifiesId is null);

    public PayrollTaxContent ReadContent() => PayrollTaxSchema.Read(Kind, Content);

    public void SetContent(PayrollTaxContent content)
    {
        Content = PayrollTaxSchema.Write(Kind, content);
        SchemaVersion = PayrollTaxSchema.Current;
    }

    public bool CreatedOrEditedBy(Guid user) => CreatedBy == user || EditedBy.Contains(user);

    public void MarkEdited(Guid? by, DateTimeOffset at)
    {
        if (by is { } user && !EditedBy.Contains(user))
        {
            EditedBy = [.. EditedBy, user];
        }

        EditedAt = at;
    }

    public void Check(Guid by, DateTimeOffset at, string note, Guid? datasetId)
    {
        Status = PayrollTaxTableStatus.Active;
        CheckedBy = by;
        CheckedAt = at;
        CheckedNote = note;
        CheckedWithDatasetId = datasetId;
    }

    public void Supersede(DateTimeOffset at)
    {
        Status = PayrollTaxTableStatus.Superseded;
        SupersededAt = at;
    }
}

/// <summary>
/// Dataset de referência da contadora (CA 6): os casos "base → resultado" de uma vigência, conferidos pelo simulador antes da
/// conferência pelo Proprietário único. Casos montados ou anonimizados pela contadora, nunca holerite real.
/// </summary>
public sealed class PayrollTaxTableDataset : Entity, IAuditable
{
    public Guid TableId { get; init; }

    /// <summary>JSON da lista de <see cref="PayrollTaxCase"/> (jsonb).</summary>
    public string Cases { get; init; } = "[]";

    public int CaseCount { get; init; }

    public Guid? CreatedBy { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>Caso do dataset (CA 6 e 10): base, dependentes (ou filhos, no salário-família) e o resultado esperado da tabela.</summary>
public sealed record PayrollTaxCase(decimal Base, int Dependents, decimal Expected);

/// <summary>Esquema por tipo (ADR 0039 §1): leitura e gravação do JSON do conteúdo. Forma nova = versão nova, nunca edição da antiga.</summary>
public static class PayrollTaxSchema
{
    public const int Current = 1;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public static string Write(PayrollTaxTableKind kind, PayrollTaxContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        object? section = kind switch
        {
            PayrollTaxTableKind.Inss => content.Inss,
            PayrollTaxTableKind.Irrf => content.Irrf,
            PayrollTaxTableKind.FamilyAllowance => content.FamilyAllowance,
            PayrollTaxTableKind.MinimumWage => content.MinimumWage,
            PayrollTaxTableKind.Fgts => content.Fgts,
            PayrollTaxTableKind.TransportVoucher => content.TransportVoucher,
            PayrollTaxTableKind.EmployerCharges => content.EmployerCharges,
            _ => null,
        };
        return JsonSerializer.Serialize(section ?? new object(), section?.GetType() ?? typeof(object), Json);
    }

    public static PayrollTaxContent Read(PayrollTaxTableKind kind, string json) => kind switch
    {
        PayrollTaxTableKind.Inss => new(Inss: JsonSerializer.Deserialize<InssContent>(json, Json)),
        PayrollTaxTableKind.Irrf => new(Irrf: JsonSerializer.Deserialize<IrrfContent>(json, Json)),
        PayrollTaxTableKind.FamilyAllowance => new(FamilyAllowance: JsonSerializer.Deserialize<FamilyAllowanceContent>(json, Json)),
        PayrollTaxTableKind.MinimumWage => new(MinimumWage: JsonSerializer.Deserialize<MinimumWageContent>(json, Json)),
        PayrollTaxTableKind.Fgts => new(Fgts: JsonSerializer.Deserialize<FgtsContent>(json, Json)),
        PayrollTaxTableKind.TransportVoucher => new(TransportVoucher: JsonSerializer.Deserialize<TransportVoucherContent>(json, Json)),
        PayrollTaxTableKind.EmployerCharges => new(EmployerCharges: JsonSerializer.Deserialize<EmployerChargesContent>(json, Json)),
        _ => new(),
    };

    public static string WriteCases(IReadOnlyList<PayrollTaxCase> cases) => JsonSerializer.Serialize(cases, Json);
}

/// <summary>Resultado de uma tabela com a memória do cálculo (CA 10).</summary>
public sealed record PayrollTaxResult(decimal Amount, IReadOnlyList<string> Memory);

/// <summary>
/// Regras das tabelas legais (FLH-01): validação pelo esquema do tipo (CA 9), escolha da versão por competência (CA 8 e 11), cálculo
/// para o simulador e o dataset (CA 6 e 10) e arredondamento parametrizado. Funções puras, sem banco nem relógio; nenhum valor legal.
/// </summary>
public static class PayrollTaxRules
{
    public const int SourceMaxLength = 300;
    public const int ReasonMaxLength = 200;
    public const int MaxBrackets = 20;
    public const int MaxCases = 500;
    public const decimal MaxMoney = 9_999_999_999_999_999.99m;

    public const string NotFoundCode = "payroll_tax_table_not_found";
    public const string MissingCode = "payroll_tax_table_missing";
    public const string NotConfirmedCode = "payroll_tax_table_not_confirmed";
    public const string ImmutableCode = "payroll_tax_table_immutable";
    public const string DuplicateCode = "payroll_tax_table_exists";
    public const string SelfCheckCode = "payroll_tax_table_self_check";
    public const string DatasetRequiredCode = "payroll_tax_table_dataset_required";
    public const string DatasetMismatchCode = "payroll_tax_table_dataset_mismatch";
    public const string RectifyPendingCode = "payroll_tax_table_rectify_pending";

    public const string ImmutableMessage = "Tabela conferida não muda. Crie uma nova versão com a vigência certa ou retifique.";
    public const string SelfCheckMessage = "Quem cadastrou a tabela não confere. Peça a outra pessoa com permissão de aprovar.";
    public const string SingleOwnerNote = "Conferência pelo Proprietário único, com dataset da contadora";

    public static string KindLabel(PayrollTaxTableKind kind) => kind switch
    {
        PayrollTaxTableKind.Inss => "INSS",
        PayrollTaxTableKind.Irrf => "IRRF",
        PayrollTaxTableKind.FamilyAllowance => "Salário-família",
        PayrollTaxTableKind.MinimumWage => "Salário mínimo",
        PayrollTaxTableKind.Fgts => "FGTS",
        PayrollTaxTableKind.TransportVoucher => "Vale-transporte",
        PayrollTaxTableKind.EmployerCharges => "Encargos da empresa",
        _ => kind.ToString(),
    };

    /// <summary>"abril/2026".</summary>
    public static string MonthLabel(DateOnly month) => month.ToString("MMMM'/'yyyy", CultureInfo.GetCultureInfo("pt-BR"));

    public static string MissingMessage(PayrollTaxTableKind kind, DateOnly competence) => $"Falta a tabela {KindLabel(kind)} para {MonthLabel(competence)}.";

    /// <summary>Arredondamento do resultado de cada imposto (CA 9): 2 casas, pela regra da versão.</summary>
    public static decimal Round(decimal value, PayrollRounding rounding) => rounding switch
    {
        PayrollRounding.ToEven => decimal.Round(value, 2, MidpointRounding.ToEven),
        PayrollRounding.Truncate => decimal.Round(value, 2, MidpointRounding.ToZero),
        _ => decimal.Round(value, 2, MidpointRounding.AwayFromZero),
    };

    public static string RoundingLabel(PayrollRounding rounding) => rounding switch
    {
        PayrollRounding.ToEven => "2 casas, metade para o par",
        PayrollRounding.Truncate => "2 casas, truncado",
        _ => "2 casas, metade para cima",
    };

    /// <summary>
    /// Escolha por competência (CA 8): a versão vigente (conferida, ou "A confirmar" que não é retificação pendente) de maior "vale a
    /// partir de" até o primeiro dia da competência; nula se não houver.
    /// </summary>
    public static PayrollTaxTable? Pick(IEnumerable<PayrollTaxTable> versions, PayrollTaxTableKind kind, DateOnly competence)
    {
        ArgumentNullException.ThrowIfNull(versions);
        var first = new DateOnly(competence.Year, competence.Month, 1);
        return versions.Where(v => v.Kind == kind && v.IsCurrent && v.ValidFrom <= first)
            .OrderByDescending(v => v.ValidFrom).ThenByDescending(v => v.Status == PayrollTaxTableStatus.Active).FirstOrDefault();
    }

    /// <summary>Uma vigente por tipo e mês (CA 11): já existe outra versão vigente do tipo com o mesmo "vale a partir de".</summary>
    public static bool Conflicts(IEnumerable<PayrollTaxTable> versions, PayrollTaxTableKind kind, DateOnly validFrom, Guid? except) =>
        versions.Any(v => v.Kind == kind && v.ValidFrom == validFrom && v.Id != except && v.IsCurrent);

    /// <summary>Uma retificação pendente por versão (CA 13).</summary>
    public static bool HasPendingRectification(IEnumerable<PayrollTaxTable> versions, Guid id) =>
        versions.Any(v => v.RectifiesId == id && v.Status == PayrollTaxTableStatus.Unconfirmed);

    /// <summary>Validação do cadastro (CA 2 a 4 e 9): erros por campo e os avisos que não bloqueiam (continuidade do IRRF).</summary>
    public static (Dictionary<string, string[]> Errors, List<string> Warnings) Validate(PayrollTaxTableKind kind, DateOnly? validFrom, PayrollTaxContent? content, PayrollRounding? rounding, string? source)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var warnings = new List<string>();
        void Add(string field, string message)
        {
            if (!errors.TryGetValue(field, out var list))
            {
                errors[field] = list = [];
            }

            list.Add(message);
        }

        if (!Enum.IsDefined(kind))
        {
            Add("kind", "Escolha o tipo da tabela.");
        }

        if (validFrom is not { } from || from.Day != 1)
        {
            Add("validFrom", "Informe \"Vale a partir de\" com o primeiro dia do mês.");
        }

        if (rounding is not { } r || !Enum.IsDefined(r))
        {
            Add("rounding", "Escolha a regra de arredondamento.");
        }

        var text = (source ?? string.Empty).Trim();
        if (text.Length is 0 or > SourceMaxLength)
        {
            Add("source", "Informe a fonte: a norma e a data de publicação (até 300 caracteres).");
        }

        if (content is null)
        {
            Add("content", "Informe os valores da tabela.");
        }
        else
        {
            switch (kind)
            {
                case PayrollTaxTableKind.Inss:
                    ValidateInss(content.Inss, Add);
                    break;
                case PayrollTaxTableKind.Irrf:
                    ValidateIrrf(content.Irrf, Add, warnings);
                    break;
                case PayrollTaxTableKind.FamilyAllowance:
                    if (content.FamilyAllowance is not { } fa)
                    {
                        Add("content", "Informe a remuneração máxima e a cota por filho.");
                    }
                    else
                    {
                        Money(fa.MaxRemuneration, "content.maxRemuneration", "A remuneração máxima", Add, positive: true);
                        Money(fa.QuotaPerChild, "content.quotaPerChild", "A cota por filho", Add);
                    }

                    break;
                case PayrollTaxTableKind.MinimumWage:
                    if (content.MinimumWage is not { } mw)
                    {
                        Add("content", "Informe o valor do salário mínimo.");
                    }
                    else
                    {
                        Money(mw.Monthly, "content.monthly", "O salário mínimo", Add, positive: true);
                    }

                    break;
                case PayrollTaxTableKind.Fgts:
                    if (content.Fgts is not { } fgts)
                    {
                        Add("content", "Informe as alíquotas do FGTS.");
                    }
                    else
                    {
                        Rate(fgts.MonthlyRate, "content.monthlyRate", "A alíquota mensal", Add);
                        Rate(fgts.TerminationRate, "content.terminationRate", "A alíquota da rescisão", Add);
                        Rate(fgts.ApprenticeRate, "content.apprenticeRate", "A alíquota do aprendiz", Add);
                    }

                    break;
                case PayrollTaxTableKind.TransportVoucher:
                    if (content.TransportVoucher is not { } vt)
                    {
                        Add("content", "Informe o percentual máximo de desconto.");
                    }
                    else
                    {
                        Rate(vt.MaxDiscountRate, "content.maxDiscountRate", "O percentual máximo", Add);
                    }

                    break;
                case PayrollTaxTableKind.EmployerCharges:
                    if (content.EmployerCharges is not { } ec)
                    {
                        Add("content", "Informe os encargos da empresa.");
                    }
                    else
                    {
                        if ((ec.TaxRegime ?? string.Empty).Trim().Length is 0 or > 40)
                        {
                            Add("content.taxRegime", "Informe o regime tributário (até 40 caracteres).");
                        }

                        Rate(ec.EmployerRate, "content.employerRate", "A contribuição patronal", Add);
                        Rate(ec.RatRate, "content.ratRate", "O RAT", Add);
                        Rate(ec.ThirdPartiesRate, "content.thirdPartiesRate", "Terceiros", Add);
                        if (ec.Fap <= 0m || ec.Fap > 10m || decimal.Round(ec.Fap, 4) != ec.Fap)
                        {
                            Add("content.fap", "Informe o FAP maior que zero, com até 4 casas.");
                        }
                    }

                    break;
            }
        }

        return (errors.ToDictionary(e => e.Key, e => e.Value.ToArray(), StringComparer.Ordinal), warnings);
    }

    private static void ValidateInss(InssContent? inss, Action<string, string> add)
    {
        if (inss is null || inss.Brackets is not { Count: > 0 } brackets)
        {
            add("content.brackets", "Informe ao menos uma faixa do INSS.");
            return;
        }

        if (brackets.Count > MaxBrackets)
        {
            add("content.brackets", $"No máximo {MaxBrackets} faixas.");
            return;
        }

        decimal previous = 0m;
        for (var i = 0; i < brackets.Count; i++)
        {
            var b = brackets[i];
            if (b.UpTo is not { } upTo)
            {
                add($"content.brackets[{i}]", "Informe o limite da faixa: no INSS a última faixa termina no teto do salário de contribuição.");
                continue;
            }

            if (!ValidMoney(upTo) || upTo <= previous)
            {
                add($"content.brackets[{i}]", "As faixas precisam ser crescentes, sem lacuna nem sobreposição (limite maior que o da faixa anterior, com até 2 casas).");
            }

            Rate(b.Rate, $"content.brackets[{i}]", "A alíquota", add);
            previous = upTo;
        }
    }

    private static void ValidateIrrf(IrrfContent? irrf, Action<string, string> add, List<string> warnings)
    {
        if (irrf is null || irrf.Brackets is not { Count: > 0 } brackets)
        {
            add("content.brackets", "Informe ao menos uma faixa do IRRF.");
            return;
        }

        if (brackets.Count > MaxBrackets)
        {
            add("content.brackets", $"No máximo {MaxBrackets} faixas.");
            return;
        }

        decimal previous = 0m;
        for (var i = 0; i < brackets.Count; i++)
        {
            var b = brackets[i];
            var last = i == brackets.Count - 1;
            if (b.UpTo is { } upTo)
            {
                if (last)
                {
                    add($"content.brackets[{i}]", "A última faixa do IRRF é \"acima de\": deixe o limite em branco.");
                }
                else if (!ValidMoney(upTo) || upTo <= previous)
                {
                    add($"content.brackets[{i}]", "As faixas precisam ser crescentes, sem lacuna nem sobreposição (limite maior que o da faixa anterior, com até 2 casas).");
                }

                previous = upTo;
            }
            else if (!last)
            {
                add($"content.brackets[{i}]", "Informe o limite da faixa: só a última fica sem limite.");
            }

            Rate(b.Rate, $"content.brackets[{i}]", "A alíquota", add);
            Money(b.Deduction, $"content.brackets[{i}]", "A parcela a deduzir", add);
        }

        Money(irrf.DependentDeduction, "content.dependentDeduction", "A dedução por dependente", add);
        Money(irrf.SimplifiedDiscount, "content.simplifiedDiscount", "O desconto simplificado", add);
        var reductions = irrf.Reductions ?? [];
        if (reductions.Count > MaxBrackets)
        {
            add("content.reductions", $"No máximo {MaxBrackets} faixas de redução.");
            return;
        }

        decimal previousReduction = 0m;
        for (var i = 0; i < reductions.Count; i++)
        {
            var r = reductions[i];
            if (r.UpTo is { } upTo)
            {
                if (!ValidMoney(upTo) || upTo <= previousReduction)
                {
                    add($"content.reductions[{i}]", "As faixas da redução precisam ser crescentes (limite maior que o da anterior, com até 2 casas).");
                }

                previousReduction = upTo;
            }
            else if (i != reductions.Count - 1)
            {
                add($"content.reductions[{i}]", "Informe o limite da faixa da redução: só a última fica sem limite.");
            }

            Money(r.FixedAmount, $"content.reductions[{i}]", "O valor fixo da redução", add);
            Rate(r.Rate, $"content.reductions[{i}]", "O percentual da redução", add);
        }

        // Continuidade na fronteira (CA 9): o imposto pela faixa de baixo e pela de cima precisa bater no limite; aviso, sem bloquear.
        for (var i = 0; i < brackets.Count - 1; i++)
        {
            if (brackets[i].UpTo is not { } edge)
            {
                continue;
            }

            var below = (edge * brackets[i].Rate / 100m) - brackets[i].Deduction;
            var above = (edge * brackets[i + 1].Rate / 100m) - brackets[i + 1].Deduction;
            if (Math.Abs(below - above) > 0.01m)
            {
                warnings.Add($"O IRRF não é contínuo em R$ {edge.ToString("N2", CultureInfo.GetCultureInfo("pt-BR"))}: dá R$ {Math.Max(0m, below).ToString("N2", CultureInfo.GetCultureInfo("pt-BR"))} pela faixa {i + 1} e R$ {Math.Max(0m, above).ToString("N2", CultureInfo.GetCultureInfo("pt-BR"))} pela faixa {i + 2}. Confira a parcela a deduzir.");
            }
        }
    }

    public static bool ValidMoney(decimal value) => value >= 0m && value <= MaxMoney && decimal.Round(value, 2) == value;

    public static bool ValidRate(decimal value) => value is >= 0m and <= 100m && decimal.Round(value, 4) == value;

    private static void Money(decimal value, string field, string label, Action<string, string> add, bool positive = false)
    {
        if (!ValidMoney(value) || (positive && value == 0m))
        {
            add(field, $"{label} precisa ser {(positive ? "maior que zero" : "zero ou mais")}, com até 2 casas.");
        }
    }

    private static void Rate(decimal value, string field, string label, Action<string, string> add)
    {
        if (!ValidRate(value))
        {
            add(field, $"{label} precisa ficar entre 0 e 100%, com até 4 casas.");
        }
    }

    private static string Brl(decimal value) => "R$ " + value.ToString("N2", CultureInfo.GetCultureInfo("pt-BR"));

    private static string Pct(decimal value) => value.ToString("0.####", CultureInfo.GetCultureInfo("pt-BR")) + "%";

    /// <summary>INSS progressivo (CA 10): cada faixa sobre a parte da base dentro dela, até o teto; arredonda o total.</summary>
    public static PayrollTaxResult Inss(InssContent inss, PayrollRounding rounding, decimal salary)
    {
        ArgumentNullException.ThrowIfNull(inss);
        var memory = new List<string>();
        decimal total = 0m, lower = 0m;
        var ceiling = inss.Brackets[^1].UpTo ?? decimal.MaxValue;
        var basis = Math.Min(Math.Max(salary, 0m), ceiling);
        if (salary > ceiling)
        {
            memory.Add($"Base limitada ao teto de {Brl(ceiling)}.");
        }

        for (var i = 0; i < inss.Brackets.Count && basis > lower; i++)
        {
            var b = inss.Brackets[i];
            var upper = b.UpTo ?? ceiling;
            var slice = Math.Min(basis, upper) - lower;
            var part = slice * b.Rate / 100m;
            memory.Add($"Faixa {i + 1}: {Brl(slice)} × {Pct(b.Rate)} = {Brl(decimal.Round(part, 2, MidpointRounding.AwayFromZero))}");
            total += part;
            lower = upper;
        }

        var amount = Round(total, rounding);
        memory.Add($"Arredondamento: {RoundingLabel(rounding)} → {Brl(amount)}");
        return new PayrollTaxResult(amount, memory);
    }

    /// <summary>
    /// IRRF mensal (CA 10): rendimento − (INSS + dependentes × dedução) ou − desconto simplificado (o que for melhor), faixa com a
    /// parcela a deduzir, a redução adicional pela norma (sobre o rendimento tributável) e o arredondamento no resultado.
    /// </summary>
    public static PayrollTaxResult Irrf(IrrfContent irrf, PayrollRounding rounding, decimal income, decimal inss, int dependents)
    {
        ArgumentNullException.ThrowIfNull(irrf);
        var memory = new List<string>();
        var legal = inss + (Math.Max(dependents, 0) * irrf.DependentDeduction);
        var simplified = irrf.SimplifiedDiscount;
        var useSimplified = simplified > legal;
        var deductions = useSimplified ? simplified : legal;
        memory.Add(useSimplified
            ? $"Desconto simplificado de {Brl(simplified)} (maior que INSS + dependentes = {Brl(legal)})."
            : $"Deduções: INSS {Brl(inss)} + {Math.Max(dependents, 0)} dependente(s) × {Brl(irrf.DependentDeduction)} = {Brl(legal)}.");
        var basis = Math.Max(income - deductions, 0m);
        memory.Add($"Base de cálculo: {Brl(income)} − {Brl(deductions)} = {Brl(basis)}.");
        var index = 0;
        while (index < irrf.Brackets.Count - 1 && irrf.Brackets[index].UpTo is { } upTo && basis > upTo)
        {
            index++;
        }

        var bracket = irrf.Brackets[index];
        var tax = Math.Max((basis * bracket.Rate / 100m) - bracket.Deduction, 0m);
        memory.Add($"Faixa {index + 1}: {Brl(basis)} × {Pct(bracket.Rate)} − parcela a deduzir {Brl(bracket.Deduction)} = {Brl(decimal.Round(tax, 2, MidpointRounding.AwayFromZero))}.");
        var reduction = 0m;
        foreach (var r in irrf.Reductions ?? [])
        {
            if (r.UpTo is null || income <= r.UpTo)
            {
                reduction = Math.Clamp(r.FixedAmount - (r.Rate / 100m * income), 0m, tax);
                memory.Add(r.Rate == 0m
                    ? $"Redução adicional (rendimento até {Brl(r.UpTo ?? income)}): até {Brl(r.FixedAmount)} → {Brl(decimal.Round(reduction, 2, MidpointRounding.AwayFromZero))}."
                    : $"Redução adicional: {Brl(r.FixedAmount)} − {Pct(r.Rate)} × {Brl(income)} → {Brl(decimal.Round(reduction, 2, MidpointRounding.AwayFromZero))}.");
                break;
            }
        }

        var amount = Round(tax - reduction, rounding);
        memory.Add($"Arredondamento: {RoundingLabel(rounding)} → {Brl(amount)}");
        return new PayrollTaxResult(amount, memory);
    }

    /// <summary>Salário-família (CA 10): a cota por filho, só com a remuneração até o limite.</summary>
    public static PayrollTaxResult FamilyAllowance(FamilyAllowanceContent fa, PayrollRounding rounding, decimal salary, int children)
    {
        ArgumentNullException.ThrowIfNull(fa);
        if (salary > fa.MaxRemuneration)
        {
            return new PayrollTaxResult(0m, [$"Remuneração acima de {Brl(fa.MaxRemuneration)}: sem salário-família."]);
        }

        var amount = Round(Math.Max(children, 0) * fa.QuotaPerChild, rounding);
        return new PayrollTaxResult(amount, [$"{Math.Max(children, 0)} filho(s) × {Brl(fa.QuotaPerChild)} = {Brl(amount)} (remuneração até {Brl(fa.MaxRemuneration)})."]);
    }

    /// <summary>Percentual simples sobre a base (FGTS, vale-transporte), com o arredondamento da versão.</summary>
    public static PayrollTaxResult Percent(string label, decimal rate, PayrollRounding rounding, decimal salary)
    {
        var amount = Round(Math.Max(salary, 0m) * rate / 100m, rounding);
        return new PayrollTaxResult(amount, [$"{label}: {Brl(Math.Max(salary, 0m))} × {Pct(rate)} = {Brl(amount)} ({RoundingLabel(rounding)})."]);
    }

    /// <summary>Encargos da empresa (CA 4 e 10): patronal + RAT × FAP + terceiros, sobre a base.</summary>
    public static PayrollTaxResult EmployerCharges(EmployerChargesContent ec, PayrollRounding rounding, decimal salary)
    {
        ArgumentNullException.ThrowIfNull(ec);
        var rat = ec.RatRate * ec.Fap;
        var rate = ec.EmployerRate + rat + ec.ThirdPartiesRate;
        var amount = Round(Math.Max(salary, 0m) * rate / 100m, rounding);
        return new PayrollTaxResult(amount, [$"{ec.TaxRegime}: patronal {Pct(ec.EmployerRate)} + RAT {Pct(ec.RatRate)} × FAP {ec.Fap.ToString("0.0000", CultureInfo.GetCultureInfo("pt-BR"))} + terceiros {Pct(ec.ThirdPartiesRate)} = {Pct(decimal.Round(rate, 4))} sobre {Brl(Math.Max(salary, 0m))} = {Brl(amount)}."]);
    }

    /// <summary>
    /// Resultado da tabela para um caso do dataset (CA 6): INSS, IRRF (com o INSS da competência), salário-família (dependentes = filhos),
    /// salário mínimo, FGTS mensal, desconto máximo do vale-transporte e encargos.
    /// </summary>
    public static decimal Evaluate(PayrollTaxTable table, PayrollTaxCase taxCase, Func<decimal, decimal>? inssOf)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(taxCase);
        var c = table.ReadContent();
        return table.Kind switch
        {
            PayrollTaxTableKind.Inss => Inss(c.Inss!, table.Rounding, taxCase.Base).Amount,
            PayrollTaxTableKind.Irrf => Irrf(c.Irrf!, table.Rounding, taxCase.Base, inssOf?.Invoke(taxCase.Base) ?? 0m, taxCase.Dependents).Amount,
            PayrollTaxTableKind.FamilyAllowance => FamilyAllowance(c.FamilyAllowance!, table.Rounding, taxCase.Base, taxCase.Dependents).Amount,
            PayrollTaxTableKind.MinimumWage => c.MinimumWage!.Monthly,
            PayrollTaxTableKind.Fgts => Percent("FGTS", c.Fgts!.MonthlyRate, table.Rounding, taxCase.Base).Amount,
            PayrollTaxTableKind.TransportVoucher => Percent("Vale-transporte", c.TransportVoucher!.MaxDiscountRate, table.Rounding, taxCase.Base).Amount,
            PayrollTaxTableKind.EmployerCharges => EmployerCharges(c.EmployerCharges!, table.Rounding, taxCase.Base).Amount,
            _ => 0m,
        };
    }

    /// <summary>
    /// Dataset colado ou enviado (CA 6): uma linha por caso, "base;dependentes;esperado" (cabeçalho opcional, vírgula ou ponto decimal).
    /// Devolve os casos ou a primeira linha inválida.
    /// </summary>
    public static (List<PayrollTaxCase> Cases, string? Error) ParseCases(string? text)
    {
        var cases = new List<PayrollTaxCase>();
        var lines = (text ?? string.Empty).Split('\n').Select(l => l.Trim().TrimEnd('\r')).Where(l => l.Length > 0).ToList();
        for (var i = 0; i < lines.Count; i++)
        {
            var parts = lines[i].Split(';');
            if (i == 0 && parts.Length > 0 && !parts[0].Any(char.IsAsciiDigit))
            {
                continue;
            }

            if (parts.Length != 3 || ParseDecimal(parts[0]) is not { } basis || !int.TryParse(parts[1].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var dependents) || ParseDecimal(parts[2]) is not { } expected)
            {
                return ([], $"Linha {i + 1}: use \"base;dependentes;esperado\" (ex.: 3.000,00;1;123,45).");
            }

            cases.Add(new PayrollTaxCase(basis, dependents, expected));
        }

        if (cases.Count == 0)
        {
            return ([], "O dataset não tem nenhum caso.");
        }

        return cases.Count > MaxCases ? ([], $"No máximo {MaxCases} casos por dataset.") : (cases, null);
    }

    private static decimal? ParseDecimal(string text)
    {
        var t = text.Trim().Replace("R$", string.Empty, StringComparison.Ordinal).Trim();
        if (t.Contains(',', StringComparison.Ordinal))
        {
            t = t.Replace(".", string.Empty, StringComparison.Ordinal).Replace(',', '.');
        }

        return decimal.TryParse(t, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) && ValidMoney(value) ? value : null;
    }
}
