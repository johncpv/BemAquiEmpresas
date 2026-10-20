using BemAqui.BuildingBlocks.Attachments;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Finance;

public partial class PaymentOrderTests
{
    /// <summary>ADR 0019 §7: todo dono de anexo inscrito confere que o documento existe.</summary>
    [Fact]
    public void Todo_dono_de_anexo_confere_que_o_documento_existe() =>
        tenancy.Factory.Services.GetServices<AttachmentOwnerRegistration>().Should().NotBeEmpty().And.OnlyContain(r => r.Rules.OwnerExists != null);
}
