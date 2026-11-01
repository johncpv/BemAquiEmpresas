using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.BuildingBlocks.Privacy;

/// <summary>O que acontece com um item da prévia (CA 2): trocado pelo marcador, retido por obrigação legal ou documento que continua existindo.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AnonymizationAction>))]
public enum AnonymizationAction
{
    Anonymize,
    Retain,
    Keep,
}

/// <summary>Uma regra da tabela de retenção (<c>settings.retention_rules</c>) como os módulos a leem.</summary>
public sealed record PersonalDataRetentionRule(PersonalDataSubjectKind SubjectType, string FieldGroup, RetentionRuleKind Rule, int Years, RetentionBase BaseDate);

/// <summary>Decisão de retenção de um grupo de campos: retido (até quando e por quê) ou livre para anonimizar.</summary>
public sealed record RetentionDecision(bool Retained, DateOnly? Until, string? Reason)
{
    public static readonly RetentionDecision Free = new(false, null, null);
}

/// <summary>
/// Grupos de campos da tabela de retenção (CA 1) e o valor pré-configurado pela ADR 0018 ("a confirmar com a contadora e com o jurídico").
/// A chave é o <c>seed_key</c> da linha: <c>{tipo}.{grupo}</c>.
/// </summary>
public static class RetentionGroups
{
    public const string Contact = "contact";
    public const string Address = "address";
    public const string Identification = "identification";
    public const string Bank = "bank";
    public const string Geolocation = "geolocation";
    public const string Dependents = "dependents";
    public const string Labor = "labor";
    public const string Account = "account";

    /// <summary>Valores sugeridos (CA 1). Mudou a lista, mude a ajuda <c>configuracoes-privacidade-retencao.md</c>.</summary>
    public static readonly IReadOnlyList<(PersonalDataRetentionRule Rule, string Label)> Defaults =
    [
        (new(PersonalDataSubjectKind.Party, Contact, RetentionRuleKind.Anytime, 0, RetentionBase.None), "Telefone, e-mail e observações"),
        (new(PersonalDataSubjectKind.Party, Bank, RetentionRuleKind.AfterYears, 0, RetentionBase.LastPayment), "Dados bancários e chave Pix nos títulos"),
        (new(PersonalDataSubjectKind.Party, Address, RetentionRuleKind.AfterYears, 5, RetentionBase.FiscalYearEnd), "Endereços"),
        (new(PersonalDataSubjectKind.Party, Identification, RetentionRuleKind.AfterYears, 5, RetentionBase.FiscalYearEnd), "Nome e CPF nas notas, títulos e documentos com movimento"),
        (new(PersonalDataSubjectKind.Contact, Contact, RetentionRuleKind.Anytime, 0, RetentionBase.None), "Nome, função, e-mail e telefone do contato"),
        (new(PersonalDataSubjectKind.Employee, Contact, RetentionRuleKind.Anytime, 0, RetentionBase.None), "Telefone e e-mail pessoal"),
        (new(PersonalDataSubjectKind.Employee, Geolocation, RetentionRuleKind.Anytime, 0, RetentionBase.None), "Localização do ponto e ciência da geolocalização"),
        (new(PersonalDataSubjectKind.Employee, Dependents, RetentionRuleKind.Anytime, 0, RetentionBase.None), "Dependentes sem obrigação legal (fora do IR e do salário-família)"),
        (new(PersonalDataSubjectKind.Employee, Bank, RetentionRuleKind.AfterYears, 0, RetentionBase.LastPayment), "Conta de salário e chave Pix"),
        (new(PersonalDataSubjectKind.Employee, Address, RetentionRuleKind.AfterYears, 5, RetentionBase.Termination), "Endereço"),
        (new(PersonalDataSubjectKind.Employee, Labor, RetentionRuleKind.AfterYears, 5, RetentionBase.Termination), "Dados trabalhistas e previdenciários: nome, documentos, ponto, ajustes, afastamentos, holerites e anexos"),
        (new(PersonalDataSubjectKind.User, Account, RetentionRuleKind.Anytime, 0, RetentionBase.None), "Nome, e-mail e acessos do usuário do sistema"),
    ];

    public static string SeedKey(PersonalDataSubjectKind kind, string group) => kind.ToString().ToLowerInvariant() + "." + group;

    public static string ReasonOf(RetentionBase basis) => basis switch
    {
        RetentionBase.Termination => "obrigação trabalhista e previdenciária",
        RetentionBase.FiscalYearEnd => "obrigação fiscal e contábil",
        RetentionBase.LastPayment => "pagamento em aberto",
        _ => "obrigação legal",
    };
}

/// <summary>
/// Datas base da retenção do titular (CA 1 e 3), informadas por cada módulo pelo contrato (<see cref="IPersonalDataProvider.RetentionAnchorsAsync"/>),
/// sem ler tabela de outro: o desligamento e o último pagamento (RH) e a data do último documento com movimento (Financeiro, Comercial,
/// Compras, Produção). Ausente quer dizer "não aconteceu" (sem desligamento, pagamento ainda em curso, nenhum documento).
/// </summary>
public static class RetentionAnchors
{
    public const string Termination = "termination";
    public const string LastPayment = "last_payment";
    public const string LastDocument = "last_document";

    public static string Of(RetentionBase basis) => basis switch
    {
        RetentionBase.Termination => Termination,
        RetentionBase.LastPayment => LastPayment,
        RetentionBase.FiscalYearEnd => LastDocument,
        _ => string.Empty,
    };
}

/// <summary>A tabela de retenção do tenant (CA 1), com o valor sugerido para o grupo que ainda não tem linha.</summary>
public sealed class PersonalDataRetention(IReadOnlyList<PersonalDataRetentionRule> rules)
{
    public static readonly PersonalDataRetention Defaults = new(RetentionGroups.Defaults.Select(d => d.Rule).ToList());

    public IReadOnlyList<PersonalDataRetentionRule> Rules { get; } = rules;

    public PersonalDataRetentionRule RuleOf(PersonalDataSubjectKind kind, string group) =>
        Rules.FirstOrDefault(r => r.SubjectType == kind && r.FieldGroup == group)
        ?? RetentionGroups.Defaults.Select(d => d.Rule).FirstOrDefault(r => r.SubjectType == kind && r.FieldGroup == group)
        ?? new PersonalDataRetentionRule(kind, group, RetentionRuleKind.AfterYears, 5, RetentionBase.FiscalYearEnd);

    /// <summary>
    /// Decide um grupo (CA 1 e 3, função pura): <c>Anytime</c> nunca retém. <c>AfterYears</c> retém até a data base mais os anos; sem a data
    /// base, o desligamento e o último pagamento retêm sem prazo (o vínculo ou o pagamento ainda corre) e o fim do exercício não retém (nenhum
    /// documento com movimento). O fim do exercício é 31/12 do ano da data base.
    /// </summary>
    public static RetentionDecision Decide(PersonalDataRetentionRule rule, DateOnly? baseDate, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (rule.Rule == RetentionRuleKind.Anytime)
        {
            return RetentionDecision.Free;
        }

        var reason = RetentionGroups.ReasonOf(rule.BaseDate);
        if (baseDate is not { } basis)
        {
            return rule.BaseDate is RetentionBase.Termination or RetentionBase.LastPayment ? new RetentionDecision(true, null, reason) : RetentionDecision.Free;
        }

        var start = rule.BaseDate == RetentionBase.FiscalYearEnd ? new DateOnly(basis.Year, 12, 31) : basis;
        var until = start.AddYears(Math.Max(0, rule.Years));
        return today <= until ? new RetentionDecision(true, until, reason) : RetentionDecision.Free;
    }
}

/// <summary>
/// O que o orquestrador entrega a cada módulo para planejar e anonimizar (ADR 0037 §1): o titular, o número do pedido (o marcador), a data de
/// hoje, a tabela de retenção e as datas base reunidas de todos os módulos.
/// </summary>
public sealed record PersonalDataAnonymizationContext(
    PersonalDataSubject Subject,
    string RequestNumber,
    DateOnly Today,
    PersonalDataRetention Retention,
    IReadOnlyDictionary<string, DateOnly> Anchors)
{
    /// <summary>Texto que entra no lugar do dado pessoal: <c>Titular anonimizado {número do pedido}</c> (CA 4).</summary>
    public string Marker => PersonalDataMarker.Text(RequestNumber);

    /// <summary>Grupo do contato do titular (e-mail, telefone, cópias de e-mail): <c>account</c> no usuário do sistema, <c>contact</c> nos demais.</summary>
    public string ContactGroup => Subject.Kind == PersonalDataSubjectKind.User ? RetentionGroups.Account : RetentionGroups.Contact;

    /// <summary>
    /// Grupo do nome e do documento do titular: <c>identification</c> no terceiro PF (notas e títulos), <c>labor</c> no funcionário, e o
    /// contato e o usuário pelo próprio grupo.
    /// </summary>
    public string IdentityGroup => Subject.Kind switch
    {
        PersonalDataSubjectKind.Party => RetentionGroups.Identification,
        PersonalDataSubjectKind.Employee => RetentionGroups.Labor,
        PersonalDataSubjectKind.User => RetentionGroups.Account,
        _ => RetentionGroups.Contact,
    };

    /// <summary>Nome completo do titular (duas palavras ou mais) para achar cópias em texto livre; null quando curto demais para não pegar outra pessoa.</summary>
    public string? FullName => Subject.Name.Trim() is { } n && n.Contains(' ', StringComparison.Ordinal) && !PersonalDataMarker.IsMarker(n) ? n : null;

    /// <summary>Decide um grupo do tipo do titular, com a data base reunida (ou uma data própria do registro, ex.: a data do título).</summary>
    public RetentionDecision Decide(string group, DateOnly? ownBaseDate = null) => Decide(Subject.Kind, group, ownBaseDate);

    public RetentionDecision Decide(PersonalDataSubjectKind kind, string group, DateOnly? ownBaseDate = null)
    {
        var rule = Retention.RuleOf(kind, group);
        var anchor = Anchors.TryGetValue(RetentionAnchors.Of(rule.BaseDate), out var a) ? a : (DateOnly?)null;
        return PersonalDataRetention.Decide(rule, ownBaseDate ?? anchor, Today);
    }
}

/// <summary>Um item da prévia (CA 2): o que é, de onde, quantos e, quando retido, até quando e por quê. Nunca leva o valor do dado.</summary>
public sealed record PersonalDataPlanItem(AnonymizationAction Action, string Label, string Source, int Count, DateOnly? RetainedUntil = null, string? Reason = null);

/// <summary>
/// Prévia (e resultado) de um módulo: os itens em três blocos — anonimizado, retido e documentos mantidos. <see cref="Blocker"/>: o que impede
/// anonimizar agora (ex.: o último membro do Proprietário), com o caminho para resolver.
/// </summary>
public sealed record PersonalDataAnonymizationPlan(string Module, string Title, IReadOnlyList<PersonalDataPlanItem> Items, string? Blocker = null)
{
    public bool IsEmpty => Items.All(i => i.Count == 0);
}

/// <summary>Monta a prévia de um módulo: só entram itens com algo (contagem maior que zero).</summary>
public sealed class PersonalDataPlanBuilder(string module, string title)
{
    private readonly List<PersonalDataPlanItem> _items = [];

    /// <summary>Um grupo de campos: anonimizado ou retido pela decisão.</summary>
    public PersonalDataPlanBuilder Fields(RetentionDecision decision, string label, string source, int count)
    {
        ArgumentNullException.ThrowIfNull(decision);
        if (count > 0)
        {
            _items.Add(decision.Retained
                ? new PersonalDataPlanItem(AnonymizationAction.Retain, label, source, count, decision.Until, decision.Reason)
                : new PersonalDataPlanItem(AnonymizationAction.Anonymize, label, source, count));
        }

        return this;
    }

    public PersonalDataPlanBuilder Anonymize(string label, string source, int count) => Fields(RetentionDecision.Free, label, source, count);

    /// <summary>Documento com movimento que continua existindo (ex.: "12 títulos a receber: mantidos, com o nome trocado").</summary>
    public PersonalDataPlanBuilder Keep(string label, string source, int count)
    {
        if (count > 0)
        {
            _items.Add(new PersonalDataPlanItem(AnonymizationAction.Keep, label, source, count));
        }

        return this;
    }

    private string? _blocker;

    /// <summary>Impede a anonimização enquanto não for resolvido (o texto diz como).</summary>
    public PersonalDataPlanBuilder Block(string? reason)
    {
        _blocker ??= reason;
        return this;
    }

    public PersonalDataAnonymizationPlan Build() => new(module, title, _items, _blocker);
}

/// <summary>Marcador do dado anonimizado (CA 4): único por pedido; e-mail obrigatório e único vira um endereço inválido por construção.</summary>
public static class PersonalDataMarker
{
    public const string Prefix = "Titular anonimizado";

    public static string Text(string requestNumber) => $"{Prefix} {requestNumber}";

    /// <summary>E-mail que não pode receber nada (domínio <c>.invalid</c>, RFC 2606), único pelo id do registro.</summary>
    public static string Email(string requestNumber, Guid id) =>
        $"anonimizado.{requestNumber.ToLowerInvariant()}.{id.ToString("N")[^12..]}@anonimizado.invalid";

    /// <summary>
    /// Documento obrigatório e único (ex.: o CPF do funcionário, 11 posições): letras e dígitos que nunca formam um CPF válido, únicos pelo id
    /// (a parte aleatória do Guid v7).
    /// </summary>
    public static string Document(Guid id) => "X" + id.ToString("N")[^10..].ToUpperInvariant();

    /// <summary>Data obrigatória sem valor (ex.: nascimento de quem foi anonimizado): 01/01/1900, que não identifica ninguém.</summary>
    public static readonly DateOnly NoDate = new(1900, 1, 1);

    public static bool IsMarker(string? text) => text is not null && text.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>
    /// Troca, numa lista de endereços separados por vírgula, os do titular pelo marcador (CA 9); devolve null quando não sobra nenhum outro.
    /// </summary>
    public static string? Recipients(string? list, IReadOnlyCollection<string> subjectEmails, string marker)
    {
        ArgumentNullException.ThrowIfNull(subjectEmails);
        if (string.IsNullOrWhiteSpace(list))
        {
            return list;
        }

        var parts = list.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(a => subjectEmails.Contains(a.ToLowerInvariant()) ? marker : a).Distinct(StringComparer.Ordinal).ToList();
        return string.Join(", ", parts);
    }

    /// <summary>A lista tem algum endereço do titular?</summary>
    public static bool HasAny(string? list, IReadOnlyCollection<string> subjectEmails)
    {
        ArgumentNullException.ThrowIfNull(subjectEmails);
        return !string.IsNullOrWhiteSpace(list)
            && list.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(a => subjectEmails.Contains(a.ToLowerInvariant()));
    }
}

/// <summary>
/// Titular anonimizado (ADR 0037 §4, LGPD-01b CA 9): só ids e o número do pedido, nunca dado pessoal; os módulos que guardam cópia (ex.: o
/// retrato do favorecido na OP) trocam o dado pelo marcador. <see cref="IdentityRemoved"/>: o nome e o documento do cadastro saíram (não estavam
/// retidos); sem isso, as cópias ficam só sem o contato e a forma de pagamento.
/// </summary>
public sealed record SubjectAnonymized(PersonalDataSubjectKind SubjectType, Guid SubjectId, Guid? PartyId, Guid? EmployeeId, string RequestNumber, bool IdentityRemoved) : IIntegrationEvent;
