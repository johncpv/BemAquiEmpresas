using BemAqui.SharedKernel;

namespace BemAqui.BuildingBlocks.Privacy;

/// <summary>
/// Titular já identificado pelo módulo dono do cadastro (ADR 0037 §1): o tipo e o id, o nome, os e-mails (em minúsculas) e o documento
/// (CPF só com dígitos) que os outros módulos usam para achar o que guardam dele, sem ler a tabela do dono (ADR 0006). <c>PartyId</c> é o
/// terceiro (no contato, o terceiro do contato); <c>EmployeeId</c> e <c>UserId</c>, o vínculo entre funcionário e usuário, quando houver.
/// </summary>
public sealed record PersonalDataSubject(
    PersonalDataSubjectKind Kind,
    Guid Id,
    string Name,
    IReadOnlyList<string> Emails,
    string? Document = null,
    Guid? PartyId = null,
    Guid? EmployeeId = null,
    Guid? UserId = null);

/// <summary>Resultado do seletor de titular (CA 1): nome e um detalhe mascarado que ajuda a escolher (e-mail, CPF ou o terceiro do contato).</summary>
public sealed record PersonalDataSubjectMatch(PersonalDataSubjectKind Kind, Guid Id, string Name, string? Detail);

/// <summary>Um campo exportado: o nome em português e o valor já formatado (ou nulo).</summary>
public sealed record PersonalDataField(string Name, string? Value);

/// <summary>Um registro (uma linha da origem) com a data dele, quando houver.</summary>
public sealed record PersonalDataRecord(IReadOnlyList<PersonalDataField> Fields, DateTimeOffset? At = null)
{
    public static PersonalDataRecord Of(DateTimeOffset? at, params (string Name, string? Value)[] fields) =>
        new(fields.Select(f => new PersonalDataField(f.Name, f.Value)).ToList(), at);

    public static PersonalDataRecord Of(params (string Name, string? Value)[] fields) => Of(null, fields);
}

/// <summary>Um bloco do módulo: o título ("Endereços"), a origem (a tabela) e os registros.</summary>
public sealed record PersonalDataBlock(string Title, string Source, IReadOnlyList<PersonalDataRecord> Records);

/// <summary>Arquivo do titular que vai na pasta <c>anexos/</c> do ZIP (CA 9): só os <c>Clean</c>, lidos pelo dono.</summary>
public sealed record PersonalDataFile(Guid AttachmentId, string OwnerType, Guid OwnerId, string FileName);

/// <summary>
/// O que um módulo tem do titular (CA 8): um bloco por origem, os arquivos e uma observação (ex.: "Dados de saúde não incluídos: sem a
/// ação hr.view_health").
/// </summary>
public sealed record PersonalDataSection(string Module, string Title, IReadOnlyList<PersonalDataBlock> Blocks, IReadOnlyList<PersonalDataFile>? Files = null, string? Note = null)
{
    public bool IsEmpty => Blocks.All(b => b.Records.Count == 0) && (Files is null || Files.Count == 0);
}

/// <summary>
/// Pedido de exportação para um provedor: o titular, o número do pedido e as permissões efetivas de quem pediu (o Proprietário). Dado
/// sensível só sai com a ação específica do módulo (ex.: <c>hr.view_sensitive</c>, <c>hr.view_health</c>), como na tela.
/// </summary>
public sealed record PersonalDataExportContext(PersonalDataSubject Subject, string RequestNumber, IReadOnlySet<string> Permissions)
{
    public bool Can(string permission) => Permissions.Contains(permission);
}

/// <summary>
/// Provedor de dado pessoal de um módulo (ADR 0037 §1, LGPD-01a CA 8): cada módulo exporta só o que é dele, sem ler tabela de outro. O
/// módulo dono do cadastro também identifica o titular (<see cref="ResolveAsync"/>) e responde ao seletor (<see cref="SearchAsync"/>). A
/// anonimização (<c>PlanAnonymizationAsync</c>, <c>AnonymizeAsync</c>) entra na LGPD-01b. Toda entidade com <c>[PersonalData]</c> pertence a
/// um provedor (<see cref="PersonalDataCoversAttribute"/>) ou a um expurgo (<see cref="PersonalDataPurgeAttribute"/>): o teste de
/// arquitetura <c>PersonalDataTests</c> reprova a que não tem nenhum dos dois.
/// </summary>
public interface IPersonalDataProvider
{
    /// <summary>Código do módulo (o mesmo do catálogo, ou <c>shared</c> para o que é da base).</summary>
    string Module { get; }

    /// <summary>Título do bloco no arquivo ("Terceiros", "RH").</summary>
    string Title { get; }

    /// <summary>Identifica o titular se o cadastro é deste módulo; null se não é dono do tipo ou o id não existe.</summary>
    Task<PersonalDataSubject?> ResolveAsync(PersonalDataSubjectKind kind, Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult<PersonalDataSubject?>(null);

    /// <summary>Busca do seletor de titular, só nos tipos de que o módulo é dono.</summary>
    Task<IReadOnlyList<PersonalDataSubjectMatch>> SearchAsync(PersonalDataSubjectKind kind, string term, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PersonalDataSubjectMatch>>([]);

    /// <summary>O que o módulo guarda do titular; null quando não guarda nada.</summary>
    Task<PersonalDataSection?> ExportAsync(PersonalDataExportContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Datas base da retenção que este módulo conhece do titular (LGPD-01b CA 1 e 3): ex.: o RH informa o desligamento e o último pagamento; o
    /// Financeiro, a data do último título. Chaves em <see cref="RetentionAnchors"/>; o orquestrador fica com a maior data de cada chave.
    /// </summary>
    Task<IReadOnlyDictionary<string, DateOnly>> RetentionAnchorsAsync(PersonalDataSubject subject, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<string, DateOnly>>(new Dictionary<string, DateOnly>());

    /// <summary>
    /// Prévia (LGPD-01b CA 2 e 3): o que o módulo troca pelo marcador, o que fica retido (com o motivo e a data) e os documentos que continuam
    /// existindo. Não grava nada; null quando o módulo não guarda nada do titular.
    /// </summary>
    Task<PersonalDataAnonymizationPlan?> PlanAnonymizationAsync(PersonalDataAnonymizationContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult<PersonalDataAnonymizationPlan?>(null);

    /// <summary>
    /// Anonimiza o que a prévia listou (CA 4 a 9): o orquestrador abre uma transação por módulo, e o provedor só grava as tabelas dele.
    /// Idempotente pelo número do pedido: rodar de novo dá o mesmo resultado. Devolve o que foi feito (os mesmos itens da prévia).
    /// </summary>
    Task<PersonalDataAnonymizationPlan?> AnonymizeAsync(PersonalDataAnonymizationContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult<PersonalDataAnonymizationPlan?>(null);
}

/// <summary>As entidades com <c>[PersonalData]</c> que o provedor exporta (e que a LGPD-01b anonimiza).</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class PersonalDataCoversAttribute(params Type[] entities) : Attribute
{
    public IReadOnlyList<Type> Entities { get; } = entities;
}

/// <summary>
/// Expurgo de um lugar com dado pessoal sem provedor (ADR 0037 §2): a entidade, em quantos dias o dado sai e como (ex.: "corpo apagado"). Vai
/// na classe do job que expurga.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class PersonalDataPurgeAttribute(Type entity, int days, string treatment) : Attribute
{
    public Type Entity { get; } = entity;

    public int Days { get; } = days;

    public string Treatment { get; } = treatment;
}

/// <summary>Padrões do <c>ILIKE</c> com o texto escapado: igual (sem caixa) ou contendo.</summary>
public static class PersonalDataQuery
{
    public static string Exact(string text) => Escape(text.Trim());

    public static string Contains(string text) => "%" + Escape(text.Trim()) + "%";

    private static string Escape(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
}

/// <summary>Máscaras do arquivo do titular (CA 9): o CPF de outra pessoa e o e-mail de quem não é o titular.</summary>
public static class PersonalDataMask
{
    public static string? Cpf(string? cpf) => string.IsNullOrWhiteSpace(cpf) ? null : SharedKernel.BrazilianDocuments.MaskCpf(cpf);

    public static string? Email(string? email) => string.IsNullOrWhiteSpace(email) ? null : BemAqui.BuildingBlocks.Email.EmailAddresses.Mask(email.Trim());

    /// <summary>Telefone de outra pessoa: só os 2 últimos dígitos ("(**) *****-**34").</summary>
    public static string? Phone(string? phone)
    {
        var digits = new string((phone ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        return digits.Length == 0 ? null : "***" + digits[^Math.Min(2, digits.Length)..];
    }

    /// <summary>Nome de outra pessoa: o primeiro nome e a inicial do último ("Maria S.").</summary>
    public static string? Name(string? name)
    {
        var parts = (name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length switch { 0 => null, 1 => parts[0], _ => parts[0] + " " + parts[^1][0] + "." };
    }

    /// <summary>Lista de e-mails separados por vírgula: o do titular aparece inteiro, o dos outros mascarado.</summary>
    public static string? Recipients(string? list, IReadOnlyCollection<string> subjectEmails)
    {
        ArgumentNullException.ThrowIfNull(subjectEmails);
        if (string.IsNullOrWhiteSpace(list))
        {
            return null;
        }

        return string.Join(", ", list.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(a => subjectEmails.Contains(a.ToLowerInvariant()) ? a : Email(a)));
    }

    public static string Date(DateOnly? date) => date is { } d ? d.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture) : string.Empty;

    public static string? Money(decimal? value) => value?.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));

    public static string YesNo(bool value) => value ? "Sim" : "Não";

    /// <summary>Data e hora em São Paulo (dd/MM/yyyy HH:mm).</summary>
    public static string? At(DateTimeOffset? at) => at is { } a ? Time.BusinessTime.ToBusiness(a).ToString("dd/MM/yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture) : null;
}
