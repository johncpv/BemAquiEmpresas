# ADR 0054 — Autoaprovação configurável da ordem de pagamento

- **Status:** Aceita (27/11/2026)
- **Stories:** [FIN-10a](../stories/FIN-10a-autoaprovacao-da-op.md) (ordem de pagamento), [FIN-10b](../stories/FIN-10b-autoconfirmacao-da-conciliacao.md) (conciliação, Draft)
- **Relaciona:** ADR 0008 (step-up), 0016 adendo M6 (`finance.parameters`), 0019 (concorrência pelo `xmin`), 0024 §4 (`SoleOwnerRule`)

## Contexto

A FIN-03a (D6) proibiu quem solicitou a OP de aprová-la, com duas exceções: o Proprietário único e o booleano `finance.owner_can_approve_own`. Empresas de uma pessoa (o próprio BemAqui) e empresas pequenas pediram para escolher a regra, sem esconder a autoaprovação. A regra é "de controle de terceiros" e por isso vira configuração.

## Decisão

1. **Modo no lugar do booleano, sem migration de dados.** `finance.owner_can_approve_own` vale `0` Nunca, `1` Só o Proprietário, `2` Qualquer aprovador (enum `SelfApprovalMode`, em `Finance.Contracts`). O `1` antigo já era "Proprietário aprova a própria", então nenhum tenant muda. Valor fora da faixa vale Nunca.
2. **Regra (`PaymentOrderRules.CanDecide`):** Nunca = só o Proprietário único (D6 a); Só o Proprietário = quem é do grupo, com qualquer número de membros; Qualquer aprovador = quem tem `finance.approve` e está na regra do nível 1. A autoaprovação vale só no nível 1 e ninguém decide dois níveis da mesma OP, em nenhum modo. A decisão grava `self_approved` e `self_approval_mode`.
3. **Comercial continua "só Proprietário".** `FinanceLimits.OwnerCanApproveOwn` é derivado (`modo != Never`); o aditivo e a pauta só o consultam para quem já é do Proprietário. Estender o modo a eles é decisão à parte.
4. **Troca do modo:** `settings.edit`, grupo Proprietário e o código do autenticador (`IStepUpTwoFactor`, uso único; `IsEnrolledAsync` separa "sem 2FA" de "código errado"), só quando o modo muda. PUT compatível: campos novos opcionais; o booleano legado só alterna Nunca e Só o Proprietário e nunca rebaixa Qualquer aprovador. Concorrência: `FinanceParameter` é `IConcurrencyVersioned` (`xmin`) e o PUT leva `selfApprovalModeSeen`; 409 `parameters_changed`. Auditoria antes/depois sempre; aviso aos outros Proprietários no mesmo `SaveChanges`.
5. **Padrão de tenant novo pelo provisionamento, nunca por seeder.** Os `ITenantSeeder` rodam em todo deploy em todos os tenants (`TenantMigrationRunner`), então não podem trocar padrão. Nova porta `ITenantProvisioningDefaults` (BuildingBlocks), executada no passo `NewTenantDefaults` (depois de `Seed`), só com o tenant em `Provisioning`; numa retomada de tenant ativo o passo é registrado sem gravar. O Finance grava "Só o Proprietário" (decisão do PO de 27/11/2026). A FIN-10b usará a mesma porta.

## Consequências

- Tenants novos afrouxam o controle por padrão; o selo (lista, detalhe, decisão, PDF, exportação), a auditoria e o aviso compensam, e a ajuda explica como voltar para Nunca.
- `xmin` na `finance.parameters` vale também para as outras telas que gravam parâmetros do Finance: só muda algo em gravação simultânea real (409 padrão).
- Outros módulos podem registrar padrões de tenant novo pela mesma porta, sem tocar nos tenants existentes.
