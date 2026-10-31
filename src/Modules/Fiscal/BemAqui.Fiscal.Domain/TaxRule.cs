using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Fiscal.Domain;

/// <summary>Operação da regra fiscal (FIS-02b CA 2): define quais tributos se aplicam e o CFOP.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TaxOperation>))]
public enum TaxOperation
{
    OwnProductionSale,
    Resale,
    ServiceProvision,
    ShipmentToSite,
    ReturnFromSite,
    PurchaseReturn,
    OtherOutbound,
}

/// <summary>Situação de uma versão da regra: rascunho, ativa (vale para a emissão) ou inativa.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TaxRuleStatus>))]
public enum TaxRuleStatus
{
    Draft,
    Active,
    Inactive,
}

/// <summary>Tipo de destinatário (<c>indIEDest</c> da NF-e): contribuinte do ICMS, contribuinte isento ou não contribuinte.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RecipientType>))]
public enum RecipientType
{
    Contributor,
    Exempt,
    NonContributor,
}

/// <summary>
/// ICMS próprio: CST (ou CSOSN no Simples), modalidade da base (<c>modBC</c>, 3 = valor da operação), alíquota interna, as interestaduais
/// por grupo de UF (12% e 7%) e a de mercadoria importada (4%), e a redução da base (%).
/// </summary>
public sealed record IcmsParameters(string Cst, int BaseModality, decimal InternalRate, decimal InterstateSouthSoutheastRate, decimal InterstateOtherRate, decimal ImportedRate, decimal BaseReduction);

/// <summary>ICMS-ST: modalidade (<c>modBCST</c>, 4 = MVA), MVA (%), alíquota interna do destino e redução da base (%).</summary>
public sealed record IcmsStParameters(int BaseModality, decimal Mva, decimal InternalRate, decimal BaseReduction);

/// <summary>Fundo de Combate à Pobreza da operação interna (%).</summary>
public sealed record FcpParameters(decimal Rate);

/// <summary>IPI: CST, código de enquadramento (<c>cEnq</c>), "pela TIPI do NCM" ou alíquota fixa.</summary>
public sealed record IpiParameters(string Cst, string Framework, bool FromNcm, decimal Rate);

/// <summary>PIS ou COFINS: CST e alíquota (%).</summary>
public sealed record PisCofinsParameters(string Cst, decimal Rate);

/// <summary>ISS da prestação de serviço (NFS-e, FIS-06): item da LC 116, alíquota, local de incidência e retenção pelo tomador.</summary>
public sealed record IssParameters(string ServiceItem, decimal Rate, IssLocation Location, bool Withheld);

/// <summary>IBS e CBS (Reforma Tributária, P-03): CST, <c>cClassTrib</c>, CBS, IBS estadual e IBS municipal (%).</summary>
public sealed record IbsCbsParameters(string Cst, string Classification, decimal CbsRate, decimal IbsStateRate, decimal IbsCityRate);

/// <summary>Marca DIFAL (CA 8): o motor calcula a diferença para a alíquota interna do destino, mas a emissão fica bloqueada.</summary>
public sealed record DifalParameters(decimal DestinationInternalRate);

/// <summary>
/// Quando a regra se aplica (CA 2): UFs de destino, tipos de destinatário e prefixos de NCM; lista vazia vale para todos. Uma regra com
/// NCM é mais específica que a sem NCM (CA 5).
/// </summary>
public sealed record TaxRuleConditions(IReadOnlyList<string> States, IReadOnlyList<RecipientType> RecipientTypes, IReadOnlyList<string> NcmPrefixes)
{
    public static TaxRuleConditions Any { get; } = new([], [], []);

    public bool Matches(string state, RecipientType recipient, string? ncm) =>
        (States.Count == 0 || States.Contains(state, StringComparer.Ordinal))
        && (RecipientTypes.Count == 0 || RecipientTypes.Contains(recipient))
        && (NcmPrefixes.Count == 0 || (ncm is { Length: > 0 } && NcmPrefixes.Any(p => ncm.StartsWith(p, StringComparison.Ordinal))));

    /// <summary>Tamanho do prefixo de NCM que casou (0 sem NCM): a regra mais específica ganha.</summary>
    public int Specificity(string? ncm) => ncm is null ? 0 : NcmPrefixes.Where(p => ncm.StartsWith(p, StringComparison.Ordinal)).Select(p => p.Length).DefaultIfEmpty(0).Max();
}

/// <summary>
/// Regra fiscal por operação (<c>fiscal.tax_rules</c>, FIS-02b): cada linha é uma <b>versão</b> (<see cref="RuleVersion"/>) da regra
/// <see cref="RuleId"/>. Nasce <c>Draft</c>; vira <c>Active</c> quando alguém com <c>fiscal.approve</c> que não a editou ativa (CA 3);
/// editar uma ativa cria nova versão <c>Draft</c> e a ativa vale até a nova ser ativada (CA 2). A conferência da contadora vale para a
/// versão (CA 4): editar limpa. As do seed (<see cref="IsSeed"/>) têm o sistema como autor (<see cref="Editors"/> vazio).
/// </summary>
[DocumentName("A regra fiscal")]
public sealed class TaxRule : Entity, IAuditable, IConcurrencyVersioned
{
    public const int NameMaxLength = 120;
    public const int NatureMaxLength = 60;
    public const int InfoMaxLength = 2000;
    public const int ReasonMaxLength = 500;

    /// <summary>A regra (todas as versões têm o mesmo id).</summary>
    public Guid RuleId { get; init; }

    public int RuleVersion { get; private set; } = 1;

    public TaxOperation Operation { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Natureza da operação (<c>natOp</c>, texto da nota).</summary>
    public string NatureOfOperation { get; set; } = string.Empty;

    public TaxRuleStatus Status { get; private set; } = TaxRuleStatus.Draft;

    public DateOnly ValidFrom { get; set; }

    public DateOnly? ValidTo { get; set; }

    public string? CfopInternal { get; set; }

    public string? CfopInterstate { get; set; }

    /// <summary>CRT para o qual a regra foi montada (FIS-02a): regra de outro regime não se aplica (CA 6 e FIS-02a CA 9).</summary>
    public int CrtScope { get; set; } = 3;

    public IcmsParameters? Icms { get; set; }

    public IcmsStParameters? IcmsSt { get; set; }

    public FcpParameters? Fcp { get; set; }

    public IpiParameters? Ipi { get; set; }

    public PisCofinsParameters? Pis { get; set; }

    public PisCofinsParameters? Cofins { get; set; }

    public IssParameters? Iss { get; set; }

    public IbsCbsParameters? IbsCbs { get; set; }

    public DifalParameters? Difal { get; set; }

    /// <summary>Tributos que entram no custo do projeto (CA 11), da lista de <see cref="FiscalSettingsRules.CostTaxOptions"/>.</summary>
    public string[] CostTaxes { get; set; } = [.. FiscalSettingsRules.DefaultCostTaxes];

    public string? ComplementaryInfo { get; set; }

    public TaxRuleConditions Conditions { get; set; } = TaxRuleConditions.Any;

    public bool IsSeed { get; init; }

    public string? SeedKey { get; init; }

    /// <summary>Os valores padrão de Configurações → Fiscal mudaram depois desta versão (CA 6): só aviso, nada muda sozinho.</summary>
    public bool NeedsReview { get; private set; }

    /// <summary>Quem criou ou editou esta versão (CA 3: não a ativa). Vazio: autor é o sistema (seed).</summary>
    public Guid[] Editors { get; private set; } = [];

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public Guid? ActivatedBy { get; private set; }

    public DateTimeOffset? ActivatedAt { get; private set; }

    /// <summary>Ativada por quem a editou, como Proprietário único (<c>SoleOwnerRule</c>, marca "Autoaprovado").</summary>
    public bool SelfActivated { get; private set; }

    public Guid? ConfirmedBy { get; private set; }

    public DateTimeOffset? ConfirmedAt { get; private set; }

    public Guid? DeactivatedBy { get; private set; }

    public DateTimeOffset? DeactivatedAt { get; private set; }

    public string? DeactivationReason { get; private set; }

    public uint Version { get; set; }

    public bool IsConfirmed => ConfirmedAt is not null;

    public bool AppliesOn(DateOnly date) => ValidFrom <= date && (ValidTo is null || ValidTo >= date);

    public static TaxRule New(Guid? by, DateTimeOffset at, bool seed = false, string? seedKey = null) => new()
    {
        RuleId = Guid.CreateVersion7(),
        IsSeed = seed,
        SeedKey = seedKey,
        CreatedAt = at,
        UpdatedAt = at,
        UpdatedBy = by,
        Editors = by is { } id ? [id] : [],
    };

    /// <summary>Gravou a versão (CA 2): quem editou entra nos autores, a conferência e o aviso de revisão saem.</summary>
    public void Touch(Guid? by, DateTimeOffset at)
    {
        if (Status != TaxRuleStatus.Draft)
        {
            throw new InvalidOperationException("Só a versão em rascunho é editada; a ativa ganha nova versão.");
        }

        UpdatedAt = at;
        UpdatedBy = by;
        if (by is { } id && !Editors.Contains(id))
        {
            Editors = [.. Editors, id];
        }

        ConfirmedBy = null;
        ConfirmedAt = null;
        NeedsReview = false;
    }

    /// <summary>Nova versão <c>Draft</c> a partir desta (editar a ativa ou reabrir a inativa, CA 2).</summary>
    public TaxRule NextVersion(Guid? by, DateTimeOffset at) => new()
    {
        RuleId = RuleId,
        RuleVersion = RuleVersion + 1,
        Operation = Operation,
        Name = Name,
        NatureOfOperation = NatureOfOperation,
        ValidFrom = ValidFrom,
        ValidTo = ValidTo,
        CfopInternal = CfopInternal,
        CfopInterstate = CfopInterstate,
        CrtScope = CrtScope,
        Icms = Icms,
        IcmsSt = IcmsSt,
        Fcp = Fcp,
        Ipi = Ipi,
        Pis = Pis,
        Cofins = Cofins,
        Iss = Iss,
        IbsCbs = IbsCbs,
        Difal = Difal,
        CostTaxes = [.. CostTaxes],
        ComplementaryInfo = ComplementaryInfo,
        Conditions = Conditions,
        IsSeed = IsSeed,
        SeedKey = SeedKey,
        CreatedAt = at,
        UpdatedAt = at,
        UpdatedBy = by,
        Editors = by is { } id ? [id] : [],
    };

    /// <summary>"Ativar regra" (CA 3): quem ativou e quando; <paramref name="self"/> marca o Proprietário único.</summary>
    public void Activate(Guid by, DateTimeOffset at, bool self)
    {
        Status = TaxRuleStatus.Active;
        ActivatedBy = by;
        ActivatedAt = at;
        SelfActivated = self;
    }

    /// <summary>A nova versão foi ativada: esta deixa de valer (o histórico guarda quem e quando).</summary>
    public void Supersede(Guid by, DateTimeOffset at, int newVersion) => Deactivate(by, at, $"Substituída pela versão {newVersion}.");

    /// <summary>"Inativar regra" (CA 3), com o motivo.</summary>
    public void Deactivate(Guid by, DateTimeOffset at, string reason)
    {
        Status = TaxRuleStatus.Inactive;
        DeactivatedBy = by;
        DeactivatedAt = at;
        DeactivationReason = reason.Length > ReasonMaxLength ? reason[..ReasonMaxLength] : reason;
    }

    /// <summary>"Marcar como conferida" (CA 4): vale para esta versão; tira o aviso de revisão.</summary>
    public void Confirm(Guid by, DateTimeOffset at)
    {
        ConfirmedBy = by;
        ConfirmedAt = at;
        NeedsReview = false;
    }

    /// <summary>Os padrões de Configurações → Fiscal mudaram (CA 6): só o aviso.</summary>
    public void FlagForReview() => NeedsReview = true;
}
