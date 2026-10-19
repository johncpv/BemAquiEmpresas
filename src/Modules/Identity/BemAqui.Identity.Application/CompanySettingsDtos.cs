using System.Text.Json.Serialization;

namespace BemAqui.Identity.Application;

/// <summary>Situação do logo (CAD-06 CA 5): sem logo, só o novo aguardando o antivírus, ou publicado.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LogoStatus>))]
public enum LogoStatus
{
    None,
    Pending,
    Clean,
}

/// <param name="Version">Muda a cada logo publicado: vai na URL pública para o navegador não usar o antigo.</param>
/// <param name="PendingNew">Há um logo novo aguardando a verificação; o anterior continua valendo até lá.</param>
public sealed record CompanyLogo(LogoStatus Status, string? Version, bool PendingNew);

/// <summary>
/// Perfil da empresa (CA 1 a 3). <c>Exists</c> falso: ainda não foi salvo, e os dados vêm do catálogo para o usuário só
/// confirmar. O CNPJ é só leitura (do catálogo).
/// </summary>
public sealed record CompanyProfileDto(
    bool Exists,
    string? Document,
    string LegalName,
    string? TradeName,
    string? StateRegistration,
    string? MunicipalRegistration,
    string? ZipCode,
    string? Street,
    string? Number,
    string? Complement,
    string? District,
    string? City,
    string? CityIbge,
    string? State,
    string? Phone,
    string? Email,
    Guid? Id,
    CompanyLogo Logo);

/// <summary>O que a tela grava. Campo desconhecido (ex.: <c>document</c>) recusa a requisição (CA 3).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CompanyProfileInput(
    string LegalName,
    string? TradeName,
    string? StateRegistration,
    string? MunicipalRegistration,
    string? ZipCode,
    string? Street,
    string? Number,
    string? Complement,
    string? District,
    string? City,
    string? CityIbge,
    string? State,
    string? Phone,
    string? Email);
