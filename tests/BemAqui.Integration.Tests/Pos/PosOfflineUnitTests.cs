using BemAqui.Identity.Contracts;
using BemAqui.Pos.Application;
using BemAqui.Pos.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Pos;

/// <summary>OFF-03 sem banco: os limites da aprovação sem rede (CA 13, os mesmos do aparelho) e as mensagens assinadas (iguais às do Vitest).</summary>
public class PosOfflineUnitTests
{
    [Fact]
    public void Limites_da_aprovacao_sem_rede_seguem_os_padroes_conservadores_da_loja()
    {
        var store = new Store();
        (store.OfflineApprovalMaxAmount, store.OfflineApprovalSessionAmount, store.OfflineApprovalSessionCount, store.OfflineApprovalReviewAmount, store.OfflineCancelSaleAllowed)
            .Should().Be((50m, 150m, 5, 100m, true));

        PosEventIngestion.OfflineLimitProblem(store, SupervisorAction.Discount, 50m, (0, 0m)).Should().BeNull();
        PosEventIngestion.OfflineLimitProblem(store, SupervisorAction.Discount, 50.01m, (0, 0m)).Should().Contain("teto");
        PosEventIngestion.OfflineLimitProblem(store, SupervisorAction.CancelItem, 10m, (5, 40m)).Should().Contain("5 aprovações");
        PosEventIngestion.OfflineLimitProblem(store, SupervisorAction.CancelItem, 10m, (2, 145m)).Should().Contain("soma");
        store.OfflineCancelSaleAllowed = false;
        PosEventIngestion.OfflineLimitProblem(store, SupervisorAction.CancelSale, 1m, (0, 0m)).Should().Contain("cancelamento de venda");
        store.OfflineApprovalMaxAmount = 0m;
        PosEventIngestion.OfflineLimitProblem(store, SupervisorAction.Discount, 0.01m, (0, 0m)).Should().Contain("não aceita");
    }

    [Fact]
    public void Mensagens_assinadas_sao_as_mesmas_do_aparelho()
    {
        var device = Guid.Parse("019a0000-0000-7000-8000-0000000000d1");
        var user = Guid.Parse("019a0000-0000-7000-8000-0000000000aa");
        var other = Guid.Parse("019a0000-0000-7000-8000-0000000000bb");
        var id = Guid.Parse("019a0000-0000-7000-8000-0000000000e1");
        PosOfflineProof.VerifierMessage("operator", device, user, "bm9uY2U=")
            .Should().Be("bq-pos-offline-verifier|v1|operator|019a0000-0000-7000-8000-0000000000d1|019a0000-0000-7000-8000-0000000000aa|bm9uY2U=");
        PosOfflineProof.LoginMessage(device, user, id, "2026-11-24T12:00:00.000Z")
            .Should().Be("bq-pos-offline-login|v1|019a0000-0000-7000-8000-0000000000d1|019a0000-0000-7000-8000-0000000000aa|019a0000-0000-7000-8000-0000000000e1|2026-11-24T12:00:00.000Z");
        PosOfflineProof.ApprovalMessage(device, id, "Discount", user, other, id, null, 2000, 1500, device, "2026-11-24T12:00:00.000Z", "Cliente fiel")
            .Should().Be("bq-pos-offline-approval|v2|019a0000-0000-7000-8000-0000000000d1|019a0000-0000-7000-8000-0000000000e1|Discount|019a0000-0000-7000-8000-0000000000aa"
                + "|019a0000-0000-7000-8000-0000000000bb|019a0000-0000-7000-8000-0000000000e1||2000|1500|019a0000-0000-7000-8000-0000000000d1|2026-11-24T12:00:00.000Z|Cliente fiel");
        new PosOfflineVerifier(user, "Ana", "operator", "PBKDF2-SHA512", 100_000, "c2FsdA==", 32, "bm9uY2U=", "segredo", []).ToString()
            .Should().NotContain("c2FsdA==").And.NotContain("bm9uY2U=").And.NotContain("segredo");
    }
}
