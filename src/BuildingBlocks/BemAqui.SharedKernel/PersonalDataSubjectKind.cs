using System.Text.Json.Serialization;

namespace BemAqui.SharedKernel;

/// <summary>Quem é o titular do pedido (LGPD-01a CA 1): terceiro PF, contato de terceiro, funcionário, usuário ou "não encontrado".</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PersonalDataSubjectKind>))]
public enum PersonalDataSubjectKind
{
    Party,
    Contact,
    Employee,
    User,
    Unknown,
}
