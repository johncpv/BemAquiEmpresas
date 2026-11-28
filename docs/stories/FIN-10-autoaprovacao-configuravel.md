# FIN-10 — Autoaprovação configurável: quem solicitou a ordem de pagamento pode aprová-la, e quem importou o extrato pode confirmar a conciliação

| Campo | Valor |
|---|---|
| ID | FIN-10 |
| Título | Dois parâmetros do Financeiro com três opções cada (Nunca / Só o Proprietário / Qualquer aprovador) para a autoaprovação da ordem de pagamento e a autoconfirmação da conciliação; padrão "Só o Proprietário" só para tenants provisionados daqui em diante; selo, auditoria e histórico visíveis; troca só pelo Proprietário, com o código do autenticador |
| Tamanho | P |
| Status | Draft (27/11/2026) |
| Requisitos | PRD R-FIN-5 (alçadas) · R-SEC-5 · regra do produto "regras de controle de terceiros sempre configuráveis" · FIN-03a D6 (CA 9 e 10) · BNK-03b D13 (CA 5) · ADR 0008 (step-up, adendo de 21/10/2026) · ADR 0016 adendo M6 (`finance.parameters`) |
| Dependências | FIN-03a, FIN-03b (alçada multinível), BNK-03b, BNK-01a (padrão do `RequireRecentTwoFactor`), FND-05 (2FA TOTP) |
| Bloqueia | — (destrava o BemAqui, empresa de uma pessoa) |
| Ordem de execução | Pós-go-live, logo depois da EXE-01a e antes da EXE-01b (pedido do PO de 27/11/2026) |

## User story
Como **dono de uma empresa pequena (às vezes a única pessoa dela)**, quero **escolher se quem solicitou uma ordem de pagamento, ou importou o extrato, pode também aprová-la ou confirmá-la**, para **não ficar travado por uma regra de controle pensada para empresas com várias pessoas, sem esconder de ninguém que eu mesmo aprovei**.

## Contexto e referências
- **Hoje** (`PaymentOrderService.Decide.cs`, `PaymentOrderRules.CanDecide`, `ApprovalResolver.SelfAllowedAsync`): quem solicitou a OP não a aprova (403 `own_payment_order`, "Quem solicitou a ordem não pode aprová-la."), com as exceções da FIN-03a D6: (a) o grupo Proprietário tem **um único membro** e é ele o solicitante; (b) o Proprietário tem mais membros e o parâmetro booleano `finance.owner_can_approve_own` (padrão desligado) está ligado. A OP e a decisão já guardam `self_approved` (selo "Aprovada pelo próprio solicitante" e filtro na lista, FIN-03a CA 9 e 10). Na alçada multinível o solicitante só decide o **nível 1** e quem já decidiu um nível não decide outro (`EligibleByRuleAsync`, `decided`).
- **Conciliação** (`ReconciliationService.Commands.SegregationAsync`, BNK-03b D13/CA 5): quem importou o OFX não confirma a conciliação dele (403 `segregation_required`); "o Proprietário também não". Linhas vindas da API não têm a restrição. Hoje **não há** parâmetro nem selo na conciliação.
- O parâmetro booleano `owner_can_approve_own` também é lido pelo aditivo de contrato (`AmendmentService.Decide.cs`) e pela pauta do Comercial (`CommercialAgendaSources`). Esta story **não muda** o aditivo (ver "Fora do escopo"); ele continua lendo o mesmo valor, interpretado como "liga" quando for 1 ou 2.
- **Por que o BemAqui trava** se a exceção (a) existe: não está esclarecido. A primeira tarefa é reproduzir no tenant do BemAqui (ver `OwnerIdsAsync`: pode haver mais de um membro no Proprietário, ou o John pode não estar no grupo do sistema). O parâmetro novo resolve o caso de qualquer forma, mas a causa precisa ser registrada no Change Log.
- Armazenamento dos parâmetros: `finance.parameters` (chave única, `value numeric`, auditada), lida por `FinanceParametersService.LimitsAsync`, editada em **Configurações → Parâmetros** (`FinanceLimitsSection.tsx`, permissão `settings.edit`). O step-up é o `IStepUpTwoFactor` (já usado no certificado do Fiscal e no Banco Inter).
- Decisões do PO de 27/11/2026 (abaixo).

## Decisões do PO (27/11/2026)
1. **Dois parâmetros**, em Configurações → Parâmetros → bloco Financeiro, cada um com três opções: **Nunca** (regra atual) / **Só o Proprietário** / **Qualquer aprovador**.
2. **Padrão:** tenants existentes ficam como estão ("Nunca"; a Konebras não muda). Tenants provisionados daqui em diante nascem com **"Só o Proprietário"**.
3. A autoaprovação **fica visível** (selo, auditoria, histórico de decisões), continua exigindo o 2FA atual para pagar, e as alçadas multinível continuam valendo.
4. Trocar o parâmetro: **só o Proprietário**, com o código do autenticador, auditado, avisando os outros Proprietários.

## Decisões de desenho desta story (registradas na ADR 0054)
- **Codificação sem migration de dados:** `finance.owner_can_approve_own` passa de booleano a **modo** (`0` Nunca, `1` Só o Proprietário, `2` Qualquer aprovador). O valor antigo `0` é "Nunca" e o `1` já significava "Proprietário" (D6 b): **nenhum tenant existente muda de comportamento** e nenhum UPDATE é necessário. Chave nova para a conciliação: `finance.reconciliation_self_confirm` (mesma codificação).
- **"Nunca" mantém a exceção D6 (a)** (Proprietário com um único membro) como hoje, para não mudar a Konebras nem o fluxo já entregue; a `<Dica>` diz isso. Se o BemAqui já se enquadra em (a), a story prova por teste que ele aprova com "Nunca" e a investigação da tarefa 1 explica o bloqueio.
- **"Só o Proprietário"** = o solicitante que é membro do grupo Proprietário aprova a própria OP, qualquer que seja o número de membros. **"Qualquer aprovador"** = o solicitante com `finance.approve` que seja elegível na alçada (membro do grupo ou usuário da regra do nível) aprova a própria OP. A permissão `finance.approve` continua obrigatória nos dois modos.
- **Alçada multinível (escolha mais segura):** a autoaprovação vale **só no nível 1**; o mesmo usuário **nunca** decide dois níveis da mesma OP, em nenhum modo. Os níveis seguintes exigem outra pessoa. É o que o código já faz (`self && level == 1` e `decided`); a story fixa isso em teste. Uma regra de 2 níveis que só tenha uma pessoa elegível já é barrada pela "alçada viável" (FIN-03b), então nada fica preso sem aviso.
- **Conciliação:** "Só o Proprietário" libera quem é do grupo Proprietário; "Qualquer aprovador" libera qualquer usuário que já tenha a permissão de confirmar conciliação. Em todos os modos linhas da API continuam livres e a data de fechamento de período segue exigindo `finance.approve` (BNK-03b CA 5).
- **Padrão por provisionamento, não por seed:** os `ITenantSeeder` rodam também no `TenantMigrationRunner` em **todos** os tenants a cada deploy; por isso o valor "Só o Proprietário" **não** pode vir de seeder. Ele é gravado por um passo exclusivo do provisionamento (`ProvisioningService`, depois do passo de seeds e antes de `CreateOwner`), uma única vez, e nunca sobrescreve valor existente.

## Critérios de aceite
**Parâmetros e padrão**
1. **Dado** um tenant que existe antes do deploy (inclusive a Konebras), **quando** o deploy roda, **então** nenhuma linha de `finance.parameters` é criada nem alterada; o parâmetro da OP mostra "Nunca" (ou "Só o Proprietário", se o booleano antigo estava ligado) e o da conciliação mostra "Nunca"; o comportamento de aprovar OP e confirmar conciliação é idêntico ao anterior (regressão na cópia do `konebras`).
2. **Dado** um tenant provisionado depois do deploy, **quando** a provisão termina, **então** os dois parâmetros estão gravados com "Só o Proprietário"; reexecutar o passo, ou reprovisionar um tenant já provisionado, não sobrescreve valor que o Proprietário tenha trocado, e o `TenantMigrationRunner` não grava o padrão em nenhum tenant (teste com dois tenants: um novo e um antigo).
3. **Dado** a tela Configurações → Parâmetros, **quando** abro o bloco Financeiro, **então** vejo "Quem solicitou a ordem pode aprová-la?" e "Quem importou o extrato pode confirmar a conciliação?", cada um com as três opções, a opção atual marcada e uma `<Dica>` que explica o risco da opção mais aberta.

**Autoaprovação da ordem de pagamento**
4. **Dado** o modo "Nunca" e o Proprietário com mais de um membro, **quando** o solicitante tenta aprovar a própria OP, **então** 403 `own_payment_order` "Quem solicitou a ordem não pode aprová-la." (como hoje); com um único Proprietário que é o solicitante, a exceção D6 (a) continua valendo.
5. **Dado** o modo "Só o Proprietário", **quando** o solicitante é membro do grupo Proprietário e tem `finance.approve`, **então** aprova a própria OP; um solicitante de outro grupo, mesmo com `finance.approve`, recebe 403 `own_payment_order`.
6. **Dado** o modo "Qualquer aprovador", **quando** o solicitante tem `finance.approve` e é elegível na regra de alçada do nível 1, **então** aprova a própria OP; sem `finance.approve`, 403 `forbidden`; fora da regra do nível, o erro de alçada da FIN-03b.
7. **[Segurança]** **Dado** uma OP com alçada de 2 ou mais níveis, **quando** o solicitante aprova o nível 1 em qualquer modo, **então** o nível 2 só é decidido por outra pessoa; a mesma pessoa não decide dois níveis (403 com a mensagem de alçada), e quem já decidiu um nível não aparece como elegível no seguinte.
8. **[Segurança]** **Dado** uma OP autoaprovada, **quando** a decisão grava, **então** `payment_order_approvals.self_approved` e `payment_orders.self_approved` ficam verdadeiros, o modo vigente na hora é gravado na decisão (`self_approval_mode`), a auditoria registra quem, quando e o modo, e o aviso "OP {número} aprovada. Pode pagar." segue para quem tem `finance.pay`.
9. **[Segurança]** **Dado** uma OP autoaprovada, **quando** vou pagá-la, **então** o 2FA do pagamento continua exigido como hoje (a autoaprovação não relaxa o pagar), e a data de pagamento retroativa e a tolerância seguem as regras da FIN-03a.
10. **Dado** uma OP autoaprovada, **quando** abro a lista, o detalhe ou o histórico de decisões, **então** vejo o selo "Aprovada pelo próprio solicitante" (chip informativo com texto, não só cor) na lista, no cabeçalho do detalhe e na linha da decisão; o filtro "Aprovadas pelo próprio solicitante" da lista continua funcionando.

**Autoconfirmação da conciliação**
11. **Dado** o modo "Nunca" da conciliação, **quando** quem importou o OFX tenta confirmar, **então** 403 `segregation_required` como hoje.
12. **Dado** o modo "Só o Proprietário", **quando** quem importou o OFX é do grupo Proprietário e confirma, **então** a conciliação é gravada; quem importou e não é Proprietário continua com 403 `segregation_required`. No modo "Qualquer aprovador", qualquer usuário com a permissão de confirmar conciliação pode confirmar o OFX que ele mesmo importou.
13. **[Segurança]** **Dado** uma conciliação confirmada por quem importou o extrato, **quando** grava, **então** `bank_reconciliations.self_confirmed` fica verdadeiro, a auditoria registra o modo vigente, e a tela de conciliação e o histórico da linha mostram o selo "Confirmada por quem importou o extrato". Nada muda no desfazer nem no fechamento de período.

**Mudar o parâmetro**
14. **[Segurança]** **Dado** um usuário que não é do grupo Proprietário, mesmo com `settings.edit`, **quando** tenta alterar qualquer um dos dois parâmetros, **então** 403 `forbidden`; a leitura continua com a permissão atual da tela.
15. **[Segurança]** **Dado** um Proprietário que altera um dos parâmetros, **quando** confirma, **então** a tela pede o código do autenticador (`IStepUpTwoFactor`, uso único, sem 2FA ativo: "Para continuar, ative a verificação em duas etapas." com "Ativar agora"); sem o código 403 `two_factor_required`, com código errado ou repetido 403 `two_factor_invalid`, e nada é gravado. A troca de qualquer um dos dois parâmetros é a única exigência extra: salvar os demais limites da tela não pede código.
16. **[Segurança]** **Dado** a troca confirmada, **quando** grava, **então** a auditoria guarda o valor anterior e o novo (antes/depois), quem trocou e quando, e **os outros Proprietários** (todos menos quem trocou) recebem a notificação "{usuário} mudou \"{parâmetro}\" de {antes} para {depois}." com link para Parâmetros; quando quem troca é o único Proprietário, nada é enviado. A notificação existente de "ligou/desligou a aprovação da própria ordem" é substituída por esta.
17. **[Segurança]** **Dado** duas trocas simultâneas do mesmo parâmetro, **quando** chegam, **então** a segunda recebe 409 com "Recarregar" (concorrência otimista na linha do parâmetro); uma troca não tem efeito sobre OP já decidida nem sobre conciliação já confirmada.

**Permissões**
18. **Dado** o módulo `finance` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`. Sem permissão nova (`settings.edit` e a regra de Proprietário já existem); sem seed patch.

## Regras de negócio e técnicas
- **Modo** como enum (`SelfApprovalMode`: `Never`, `OwnerOnly`, `AnyApprover`) em `BemAqui.Finance.Domain`; `FinanceParametersService.LimitsAsync` devolve `OwnerCanApproveOwnMode` e `ReconciliationSelfConfirmMode`; o campo booleano `OwnerCanApproveOwn` do contrato continua, derivado (`modo != Never`), para o aditivo e a pauta do Comercial não mudarem.
- `PaymentOrderRules.CanDecide` e `ApprovalResolver.SelfAllowedAsync` passam a receber o modo (função pura, com teste de unidade da tabela completa: modo × solicitante é Proprietário × nº de membros × tem `finance.approve` × nível). `SelfAllowedAsync` continua restringindo o solicitante ao nível 1.
- `ReconciliationService.SegregationAsync` passa a consultar o modo e a ser do Proprietário; devolve o `self` para gravar `self_confirmed`.
- **Migrations aditivas:** `payment_order_approvals.self_approval_mode smallint null`; `bank_reconciliations.self_confirmed boolean not null default false` e `self_approval_mode smallint null`. Sem UPDATE em dados existentes. Linhas de OP e de conciliação antigas ficam sem selo novo (a OP antiga mantém o `self_approved` que já tem).
- **Provisionamento:** novo passo em `ProvisioningSteps` (idempotente, registrado em `catalog.provisioning_log`), `SeedFinanceDefaults`, que grava os dois parâmetros só quando a chave não existe. Nenhum `ITenantSeeder` é alterado.
- **Step-up:** o endpoint de limites recebe `code` (como `FiscalSettingsService`); o código é verificado só quando um dos dois modos muda; erros `two_factor_required` e `two_factor_invalid`. Código de uso único, sem reaproveitar a janela de "2FA recente" de outras telas.
- **Notificação aos outros Proprietários:** `users.OwnerIdsAsync` menos o autor, tipo `finance.parameters` (já usado).
- **Dinheiro e tempo:** nada novo; datas UTC.

## Permissões, eventos e tabelas
- **Permissões:** nenhuma nova. Trocar o parâmetro: grupo Proprietário **e** `settings.edit`; aprovar OP: `finance.approve`; confirmar conciliação: a permissão atual.
- **Eventos:** nenhum novo (`PaymentOrderApproved` igual).
- **Tabelas:** `finance.parameters` (chave `finance.reconciliation_self_confirm` nova; `finance.owner_can_approve_own` com nova codificação 0/1/2 e `updated_by`, já existente); colunas aditivas em `payment_order_approvals` e `bank_reconciliations`. Atualizar `docs/03-modelo-dados.md` e `docs/04-fluxos-estados.md` §3.6/§3.7.

## Telas e estados
- **Configurações → Parâmetros → Financeiro:** dois grupos de opção (rádios com descrição de uma linha cada: "Nunca — outra pessoa precisa aprovar", "Só o Proprietário — você aprova a própria ordem, e fica registrado", "Qualquer aprovador — quem tem alçada aprova a própria ordem, e fica registrado"). Ao escolher uma opção diferente, o botão "Salvar" abre o diálogo de código do autenticador ("Confirme com o código do autenticador"), e o sucesso diz "Salva". Quem não é Proprietário vê o valor em leitura com a nota "Só o Proprietário muda esta regra." Estados: carregando; erro ao salvar (código errado: "Código inválido ou já usado. Peça um novo."); sem 2FA: "Para continuar, ative a verificação em duas etapas." + "Ativar agora".
- **Ordem de pagamento (lista, detalhe, histórico de decisões):** chip informativo "Aprovada pelo próprio solicitante"; o diálogo "Aprovar", quando o usuário é o solicitante e o modo permite, mostra o aviso âmbar "Você solicitou esta ordem. A aprovação ficará registrada como feita pelo próprio solicitante." Quando o modo não permite, o botão "Aprovar" fica oculto e o texto diz quem pode ("Peça a outra pessoa que aprove.") com link para os parâmetros se o usuário é Proprietário (propositivo: "Permitir que eu aprove minhas ordens").
- **Conciliação:** o aviso do 403 `segregation_required` ganha, para o Proprietário, o atalho para o parâmetro; o selo "Confirmada por quem importou o extrato" aparece na conciliação e no histórico da linha.
- **400 px:** rádios empilhados; o diálogo de código ocupa a tela; o selo vai para a linha de baixo do cartão.
- **Ajuda a atualizar (mesmo commit):** `Ajuda/configuracoes-parametros.md` (bloco "Quem aprova a própria ordem" com as três opções, o padrão por tenant novo, o código do autenticador e o aviso aos outros Proprietários), `Ajuda/financeiro-ordens-de-pagamento.md` (selo e o que muda para quem solicita) e `Ajuda/financeiro-conciliacao.md` (quem importou e o novo parâmetro). O teste de cobertura da ajuda segue passando.

## Plano de testes
- **Unidade:** tabela de `CanDecide`/`SelfAllowedAsync` (modo × Proprietário × membros × permissão × nível), CA 4 a 7; modo da conciliação (CA 11 a 12); codificação 0/1/2 e compatibilidade do booleano antigo (CA 1).
- **Integração (Testcontainers):** aprovar a própria OP nos três modos, com 1 e 2 Proprietários, com alçada de 1 e de 2 níveis (CA 4 a 7); `self_approved`, `self_approval_mode` e auditoria gravados e pagar ainda exigindo 2FA (CA 8, 9); confirmar OFX próprio nos três modos e `self_confirmed` (CA 11 a 13); troca do parâmetro por não Proprietário (403), sem código, código errado, código repetido, código certo, antes/depois na auditoria e aviso aos outros Proprietários (CA 14 a 16); troca simultânea (CA 17); provisionar tenant novo grava "Só o Proprietário", não sobrescreve em nova execução, e o `TenantMigrationRunner` não grava nada em tenant antigo (CA 1, 2); regressão com a cópia do `konebras`.
- **Front (Vitest):** bloco de parâmetros com as três opções, diálogo de código, leitura para não Proprietário, aviso do diálogo "Aprovar" por modo, selos.
- **Sem E2E** (por feature, não por story); sem teste de padrão repetido (`Tres_camadas_*`, ajuda e arquitetura já varridos).

## Fora do escopo
- Aditivo de contrato e demais documentos com `SoleOwnerRule` (ADR 0024 §4: estoque, compras): continuam como estão; o aditivo apenas lê o booleano derivado. Estender o modo a eles é decisão à parte.
- 2FA por ação para aprovar ou pagar (adiado, P-19) e estorno com aprovação.
- Relatório de "tudo que foi autoaprovado no mês" (o filtro da lista já cobre a OP; a conciliação ganha o selo, sem relatório).
- Tela própria de histórico de trocas do parâmetro (a auditoria geral já mostra).

## Pontos em aberto
- **Causa do bloqueio no BemAqui** (a exceção D6 (a) deveria liberar o único Proprietário): reproduzir na tarefa 1 antes de codar; se for dado do tenant (por exemplo, dois membros no Proprietário), não é bug de código.
- Nenhuma decisão de produto pendente: as quatro do PO e as de desenho acima fecham a story. Registrar a ADR 0054.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração e front passando; regressão da Konebras verde.
- [ ] Migrations aditivas criadas; nenhum dado existente alterado.
- [ ] Passo de provisionamento criado e idempotente; nenhum `ITenantSeeder` muda parâmetro de tenant existente.
- [ ] Tela com estados; ajuda das três telas atualizada; teste de cobertura passando.
- [ ] Auditoria e aviso aos Proprietários ligados; step-up testado.
- [ ] OpenAPI e cliente Orval regenerados; `docs/03`, `docs/04` e ADR 0054 atualizados.
- [ ] Story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] 1. Reproduzir o bloqueio no BemAqui (contar `OwnerIdsAsync`, conferir o grupo do John) e registrar a causa.
- [ ] 2. `SelfApprovalMode` + `FinanceLimits` (dois modos, booleano derivado) + `PaymentOrderRules.CanDecide` e `ApprovalResolver.SelfAllowedAsync` com o modo; testes de unidade.
- [ ] 3. `ReconciliationService.SegregationAsync` com o modo; gravar `self_confirmed` e `self_approval_mode`; migration aditiva.
- [ ] 4. `FinanceParametersService.UpdateAsync`: só Proprietário, step-up quando um dos modos muda, auditoria antes/depois, aviso aos outros Proprietários, concorrência.
- [ ] 5. Passo `SeedFinanceDefaults` no `ProvisioningService` (e `ProvisioningSteps`), com teste de dois tenants.
- [ ] 6. Front: bloco em `FinanceLimitsSection.tsx`, diálogo de código, selos em `PaymentOrdersPage`/`PaymentOrderPage` e na conciliação; `pnpm gen:api`.
- [ ] 7. Ajudas, `docs/03`, `docs/04` §3.6/§3.7, ADR 0054, backlog.

## Dev Notes
- **Backend (Finance):** `src/Modules/Finance/BemAqui.Finance.Domain/PaymentOrderRules.cs`; `.../BemAqui.Finance.Application/PaymentOrderService.Decide.cs` (`DecisionRuleAsync`, `DecideAsync`); `ApprovalResolver.cs` (`SelfAllowedAsync`, `EligibleByRuleAsync`); `FinanceParametersService.cs` (chaves, `Defaults`, `LimitsAsync`, `UpdateAsync`); `ReceivableDtos.cs` (`FinanceLimitsInput`) e `BemAqui.Finance.Contracts/ReceivablesContracts.cs`; `Reconciliation/ReconciliationService.cs` e `.Commands.cs` (`SegregationAsync`), `.History.cs`; `BemAqui.Finance.Domain/BankReconciliation.cs`; migrations em `BemAqui.Finance.Infrastructure`.
- **Step-up:** `src/BuildingBlocks/.../Identity/IStepUpTwoFactor.cs`; exemplo em `Fiscal/.../Settings/FiscalSettingsService.cs` (erros de código obrigatório e inválido).
- **Provisionamento:** `src/Modules/Platform/BemAqui.Platform.Application/Provisioning/ProvisioningService.cs` (`RunStepAsync`, `ProvisioningSteps.Ordered`). **Não** usar `ITenantSeeder` (roda em todos os tenants no `TenantMigrationRunner`).
- **Consumidores do booleano:** `Commercial/.../AmendmentService.Decide.cs` (linha do `DecisionRuleAsync`) e `CommercialAgendaSources.cs`; não alterar o comportamento deles.
- **Front:** `src/Web/src/pages/settings/parameters/FinanceLimitsSection.tsx` e `ParametersPage.tsx`; `pages/finance/payment-orders/PaymentOrderPage.tsx` e `PaymentOrdersPage.tsx`; tela de conciliação; `api/generated` (Orval).
- **Ajuda:** `Ajuda/configuracoes-parametros.md`, `Ajuda/financeiro-ordens-de-pagamento.md`, `Ajuda/financeiro-conciliacao.md`.
- **Riscos:** (1) o booleano antigo é lido por outros módulos, então a derivação precisa ser testada; (2) padrão "Só o Proprietário" nos tenants novos afrouxa o controle por padrão: por isso o selo, a auditoria e o aviso são obrigatórios e a ajuda explica como voltar para "Nunca"; (3) alçada de 2 níveis com uma só pessoa elegível já é recusada pela FIN-03b, mas confirmar no teste do CA 7.

## Quality gates
- Agentes previstos: @dev, @qa, cybersecurity (step-up, segregação de função, auditoria).
- Gate: CA 1, 2, 7, 15 e 16 obrigatórios.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 27/11/2026 | 0.1 | Story escrita a partir do pedido do PO (BemAqui, empresa de uma pessoa): dois parâmetros de três opções, padrão por provisionamento, selo, auditoria e troca com código do autenticador. Encontrado no código que a exceção D6 e o selo da OP já existem; a story generaliza o parâmetro, estende à conciliação e fecha a causa do bloqueio | @sm |
