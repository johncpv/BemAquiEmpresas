using BemAqui.SharedKernel;
using FluentAssertions;

namespace BemAqui.Architecture.Tests;

public class SecretExposureTests
{
    private sealed record SmtpSettingsResponse(string Host, Secret Password);

    private sealed record AiSettingsResponse(string Provider, IReadOnlyList<Secret> Keys);

    private sealed record SafeResponse(string Host, bool Configured, DateTimeOffset? UpdatedAt);

    [Fact]
    public void Nenhum_tipo_dos_Endpoints_expoe_Secret()
    {
        var endpoints = Solution.Assemblies.Where(a => a.GetName().Name!.EndsWith(".Endpoints", StringComparison.Ordinal));

        SecretExposureRules.SecretProperties(endpoints.SelectMany(a => a.GetTypes())).Should().BeEmpty();
    }

    [Fact]
    public void Regra_acusa_Secret_direto_e_em_colecao() =>
        SecretExposureRules.SecretProperties([typeof(SmtpSettingsResponse), typeof(AiSettingsResponse)]).Should().HaveCount(2);

    [Fact]
    public void Resposta_com_configured_e_updatedAt_e_permitida() =>
        SecretExposureRules.SecretProperties([typeof(SafeResponse)]).Should().BeEmpty();
}
