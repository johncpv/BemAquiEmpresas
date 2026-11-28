# FIN-10a — Autoaprovação configurável da ordem de pagamento: quem solicitou a OP pode aprová-la, conforme o parâmetro

| Campo | Valor |
|---|---|
| ID | FIN-10a |
| Título | Parâmetro do Financeiro com três opções (Nunca / Só o Proprietário / Qualquer aprovador, só no 1º nível) para a autoaprovação da ordem de pagamento; padrão "Só o Proprietário" só para tenants provisionados daqui em diante; selo, auditoria e histórico visíveis; troca só pelo Proprietário, com o código do autenticador e concorrência otimista |
| Tamanho | P |
| Status | Draft (27/11/2026, revisada) |
| Requisitos | PRD R-FIN-5 (alçadas) · R-SEC-5 · regra do produto "regras de controle de terceiros sempre configuráveis" · FIN-03a D6 (CA 9 e 10) · ADR 0008 (step-up, adendo de 21/10/2026) · ADR 0016 adendo M6 (`finance.parameters`) · ADR 0019 (concorrência pelo `xmin`) |
| Dependências | FIN-03a, FIN-03b (alçada multinível), FND-05 (2FA TOTP), FND-03a/03b (provisionamento) |
| Bloqueia | FIN-10b (autoconfirmação da conciliação, mesma codificação de modo) |
| Ordem de execução | Pós-go-live, logo depois da EXE-01a e antes da EXE-01b (pedido do PO de 27/11/2026) |

## User story
Como **dono de uma empresa pequena (às vezes a única pessoa dela)**, quero **escolher se quem solicitou uma ordem de pagamento pode também aprová-la**, para **não ficar travado por uma regra de controle pensada para empresas com várias pessoas, sem esconder de ninguém que eu mesmo aprovei**.

## Contexto e referências
- **Hoje** (`PaymentOrderService.Decide.cs`, `PaymentOrderRules.CanDecide`, `ApprovalResolver.SelfAllowedAsync`): quem solicitou a OP não a decide (403 `own_payment_order`, "Quem solicitou a ordem não pode aprová-la."), com as exceções da FIN-03a D6: (a) o grupo Proprietário tem **um único membro ativo** e é ele o solicitante; (b) o Proprietário tem mais membros e o parâmetro booleano `finance.owner_can_approve_own` (padrão desligado) está ligado. A OP e a decisão já guardam `self_approved` (selo "Aprovada pelo próprio solicitante" e filtro na lista, FIN-03a CA 9 e 10). Na alçada multinível o solicitante só decide o **nível 1** e quem já decidiu um nível não decide outro (`EligibleByRuleAsync`, `EligibleAsync`, `LevelErrorAsync`).
- O booleano `owner_can_approve_own` também é lido pelo aditivo de contrato (`AmendmentService.Decide.cs`, `DecisionRuleAsync`) e pela pauta do Comercial (`CommercialAgendaSources.MayDecideOwnAsync`). Os dois exigem que o autor seja **do Proprietário** e usam o booleano só para o caso de mais de um membro.
- Armazenamento: `finance.parameters` (chave única, `value numeric`, auditada pelo interceptor), lida por `FinanceParametersService.LimitsAsync`, editada em **Configurações → Parâmetros** (`FinanceLimitsSection.tsx`, permissão `settings.edit`, rota `PUT /api/v1/finance/parameters`). Hoje a linha **não tem versão** (`FinanceParameter` não é `IConcurrencyVersioned`). O `FinanceParametersSeeder` (um `ITenantSeeder`) grava os padrões que faltam em **todos** os tenants a cada deploy, então todo tenant já tem a linha `finance.owner_can_approve_own = 0`.
- Step-up: `IStepUpTwoFactor.VerifyAsync` (uso único, anti-replay do login) devolve só verdadeiro/falso; não distingue "sem 2FA ativo" de "código errado".
- **Causa provável do bloqueio no BemAqui** (lida no código, sem acesso ao banco; ver Change Log): a exceção (a) conta os membros **ativos** do grupo do sistema Proprietário (`UserDirectory.OwnerIdsAsync`); com dois ou mais (por exemplo, um segundo usuário convidado como Proprietário) ela não vale, e sem o parâmetro ligado a OP do John fica sem quem aprove. Se o John não estiver no grupo do sistema Proprietário, nenhuma exceção vale. **Até a FIN-10a chegar, o PO destrava marcando "Proprietário aprova a própria ordem" em Configurações → Parâmetros → Financeiro** (vale para o caso de mais de um Proprietário).

## Decisões do PO (27/11/2026)
1. Parâmetro em Configurações → Parâmetros → bloco Financeiro, com três opções: **Nunca** (regra atual) / **Só o Proprietário** / **Qualquer aprovador**.
2. **Padrão:** tenants existentes ficam como estão (a Konebras não muda). Tenants provisionados daqui em diante nascem com **"Só o Proprietário"**.
3. A autoaprovação **fica visível** (selo, auditoria, histórico de decisões), continua exigindo o 2FA atual para pagar, e as alçadas multinível continuam valendo.
4. Trocar o parâmetro: **só o Proprietário**, com o código do autenticador, auditado, avisando os outros Proprietários.
5. (Revisão de 27/11) A story foi dividida: esta, só a ordem de pagamento; a conciliação foi para a **FIN-10b** (Draft, a revisar).

## Decisões de desenho (registradas na ADR 0054)
- **Codificação sem migration de dados:** `finance.owner_can_approve_own` passa de booleano a **modo** (`0` Nunca, `1` Só o Proprietário, `2` Qualquer aprovador). O valor antigo `0` é "Nunca" e o `1` já significava "Proprietário aprova a própria" (D6 b): **nenhum tenant existente muda de comportamento** e nenhum UPDATE é necessário. Valor fora de 0 a 2 lido do banco vale "Nunca" (falha fechada).
- **"Nunca" mantém a exceção D6 (a)** (Proprietário com um único membro ativo) como hoje, para não mudar a Konebras nem o fluxo entregue; a `<Dica>` diz isso.
- **"Só o Proprietário"** = o solicitante que é membro do grupo Proprietário aprova a própria OP, qualquer que seja o número de membros. **"Qualquer aprovador (só no 1º nível)"** = o solicitante com `finance.approve` que seja elegível no **nível 1** da alçada aprova a própria OP. `finance.approve` continua obrigatória nos dois modos.
- **Alçada multinível:** a autoaprovação vale **só no nível 1**; o mesmo usuário **nunca** decide dois níveis da mesma OP, em nenhum modo (já é o código: `self && level == 1`, `already_decided_level` e `EligibleAsync` sem quem decidiu). Regra de 2 níveis com uma só pessoa elegível já é barrada pela "alçada viável" (FIN-03b).
- **Aditivo e pauta do Comercial continuam "só Proprietário":** o booleano derivado do contrato `FinanceLimits.OwnerCanApproveOwn` é `modo != Never` e os dois leitores já exigem que o autor seja do Proprietário. Logo "Qualquer aprovador" **nunca** libera um não Proprietário a aprovar o próprio aditivo; nada muda no código deles, só ganham testes nos três modos.
- **Padrão por provisionamento, não por seed:** os `ITenantSeeder` rodam também no `TenantMigrationRunner` em todos os tenants a cada deploy; por isso "Só o Proprietário" vem de um passo próprio do provisionamento (`ProvisioningSteps.FinanceDefaults`, depois de `Seed` e antes de `CreateOwner`), executado por uma porta `ITenantProvisioningDefaults` (nova, em BuildingBlocks) implementada no Finance. O passo **só grava quando o tenant está em `Provisioning`**; numa retomada (`--resume`) de tenant já ativo, ou de tenant provisionado antes do deploy (que não tem o passo no `provisioning_log`), ele é registrado como concluído **sem gravar nada** ("Pulada: tenant já ativo."). Num tenant novo, o seeder já gravou `0`; o passo troca para `1` uma única vez (o `provisioning_log` impede repetir).
- **Concorrência:** `FinanceParameter` passa a ser `IConcurrencyVersioned` (`xmin`, ADR 0019) e o PUT traz `selfApprovalModeSeen` (o modo que a tela mostrou). Se o modo gravado já não é o visto, ou se a gravação perde a corrida do `xmin`, 409 `parameters_changed` "Outra pessoa mudou os parâmetros. Recarregue e tente de novo."; nada é gravado (rastreamento limpo, sem notificação).
- **PUT compatível:** todos os campos novos são **opcionais** (ausente = não mudou). O booleano legado `ownerCanApproveOwn` vira opcional e **só alterna Nunca ↔ Só o Proprietário**: `true` em "Nunca" vira "Só o Proprietário"; `false` em "Só o Proprietário" vira "Nunca"; com "Qualquer aprovador", o booleano **nunca rebaixa** o modo. Com `selfApprovalMode` e o booleano juntos, vale o modo.
- **Ordem das checagens no PUT:** (1) `settings.edit` (403 `forbidden`); validações de valor; (2) se o modo muda: é do Proprietário (403 `forbidden`, "Só o Proprietário muda esta regra."); modo visto confere (409); tem 2FA ativo (`IStepUpTwoFactor.IsEnrolledAsync`, nova; sem 2FA: 403 `two_factor_required` com "Para continuar, ative a verificação em duas etapas." e `enrolled: false`); código presente (403 `two_factor_required`, "Confirme com o código do autenticador."); código válido e não usado (`VerifyAsync`, uso único; 403 `two_factor_invalid`, "Código inválido ou já usado. Peça um novo."). O código **só é pedido quando o modo muda**: "Salvar limites" sem trocar o modo não pede código nem exige ser Proprietário.
- **Auditoria e aviso:** a troca grava a auditoria antes/depois da linha **sempre** (interceptor `IAuditable`, mesmo sem nenhum destinatário de aviso). O aviso vai para **todos os Proprietários menos o autor** (mudança: hoje vai a todos, inclusive quem trocou). As notificações entram no mesmo `SaveChanges` da troca: se a troca falha (409), nada é avisado.

## Critérios de aceite
**Parâmetro e padrão**
1. **Dado** um tenant que existe antes do deploy (inclusive a Konebras), **quando** o deploy roda (migrations e seeders), **então** nenhuma linha de `finance.parameters` é alterada; o parâmetro mostra "Nunca" (ou "Só o Proprietário", se o booleano antigo estava ligado); aprovar OP é idêntico ao anterior.
2. **Dado** um tenant provisionado depois do deploy, **quando** a provisão termina, **então** o parâmetro está em "Só o Proprietário" e o passo `FinanceDefaults` aparece no `provisioning_log` (e na tela de detalhe do tenant no admin da plataforma, rótulo "Padrões do financeiro"). **Dado** a retomada (`--resume`) de um tenant já ativo, inclusive um provisionado antes do deploy, **quando** roda, **então** o passo não grava nada (o parâmetro continua como estava) e fica registrado como concluído; o `TenantMigrationRunner` não grava o padrão em nenhum tenant (teste com dois tenants: um novo e um antigo).
3. **Dado** a tela Configurações → Parâmetros, **quando** abro o bloco Financeiro, **então** vejo "Quem solicitou a ordem pode aprová-la?" com as três opções ("Nunca", "Só o Proprietário", "Qualquer aprovador (só no 1º nível)"), a atual marcada e uma `<Dica>` em "Nunca" que explica que, com um único Proprietário, ele continua aprovando as próprias ordens.

**Autoaprovação da ordem de pagamento**
4. **Dado** o modo "Nunca" e o Proprietário com mais de um membro, **quando** o solicitante tenta aprovar a própria OP, **então** 403 `own_payment_order` "Quem solicitou a ordem não pode aprová-la." (como hoje); com um único Proprietário que é o solicitante, a exceção D6 (a) continua valendo.
5. **Dado** o modo "Só o Proprietário", **quando** o solicitante é membro do grupo Proprietário e tem `finance.approve`, **então** aprova a própria OP; um solicitante de outro grupo, mesmo com `finance.approve`, recebe 403 `own_payment_order`.
6. **Dado** o modo "Qualquer aprovador", **quando** o solicitante tem `finance.approve` e é elegível na regra de alçada do nível 1, **então** aprova a própria OP; sem `finance.approve`, 403 `forbidden`; fora da regra do nível, o erro de alçada da FIN-03b.
7. **[Segurança]** **Dado** uma OP com alçada de 2 ou mais níveis, **quando** o solicitante aprova o nível 1 em qualquer modo, **então** o nível 2 só é decidido por outra pessoa; a mesma pessoa não decide dois níveis (403 `already_decided_level`), e quem já decidiu um nível não aparece como elegível no seguinte.
8. **[Segurança]** **Dado** uma OP autoaprovada, **quando** a decisão grava, **então** `payment_order_approvals.self_approved` e `payment_orders.self_approved` ficam verdadeiros, o modo vigente na hora é gravado na decisão (`self_approval_mode`), a auditoria da decisão registra quem, quando e o modo, e o aviso "OP {número} aprovada. Pode pagar." segue para quem tem `finance.pay`.
9. **[Segurança]** **Dado** uma OP autoaprovada, **quando** vou pagá-la, **então** o 2FA do pagamento continua exigido como hoje (a autoaprovação não relaxa o pagar).
10. **Dado** uma OP autoaprovada, **quando** abro a lista, o detalhe, o histórico de decisões, o PDF da OP ou a exportação da lista, **então** vejo o selo "Aprovada pelo próprio solicitante" (chip informativo com texto, não só cor; no PDF e na exportação, em texto); o filtro "Aprovadas pelo próprio solicitante" continua funcionando.
11. **[Segurança]** **Dado** uma OP autoaprovada que é **reenviada** (valor acima do aprovado) ou **estornada**, **quando** o reenvio ou o estorno grava, **então** a decisão histórica continua com `self_approved` e o modo (nada é apagado); no reenvio, só o selo corrente da OP (`payment_orders.self_approved`) é zerado até a nova decisão; no estorno, a OP mantém o selo (ela foi aprovada pelo próprio solicitante).
12. **Dado** que sou o solicitante e o modo permite, **quando** abro "Aprovar", **então** o diálogo mostra o aviso "Você solicitou esta ordem. A aprovação ficará registrada como feita pelo próprio solicitante."; quando o modo não permite, "Aprovar" não aparece e o texto diz "Peça a outra pessoa que aprove."; se eu for do Proprietário, há o atalho "Permitir que eu aprove minhas ordens" para Parâmetros.

**Mudar o parâmetro**
13. **[Segurança]** **Dado** um usuário que não é do grupo Proprietário, mesmo com `settings.edit`, **quando** tenta mudar o modo, **então** 403 `forbidden` "Só o Proprietário muda esta regra." e nada é gravado; ele continua salvando os demais limites (sem mudar o modo) e vê o modo só em leitura, com a nota "Só o Proprietário muda esta regra.".
14. **[Segurança]** **Dado** um Proprietário que muda o modo, **quando** confirma, **então** a tela pede o código do autenticador; sem 2FA ativo, 403 `two_factor_required` com "Para continuar, ative a verificação em duas etapas." e o atalho "Ativar agora"; sem o código, 403 `two_factor_required`; com código errado ou repetido, 403 `two_factor_invalid` "Código inválido ou já usado. Peça um novo."; em todos, nada é gravado. "Salvar limites" **sem** mudar o modo não pede código.
15. **[Segurança]** **Dado** o PUT antigo (só o booleano `ownerCanApproveOwn`), **quando** chega, **então** ele só alterna Nunca ↔ Só o Proprietário (pedindo o código quando muda) e nunca rebaixa "Qualquer aprovador"; campos novos ausentes ficam como estão.
16. **[Segurança]** **Dado** a troca confirmada, **quando** grava, **então** a auditoria guarda o valor anterior e o novo (antes/depois), quem trocou e quando, **sempre**; **os outros Proprietários** (todos menos quem trocou) recebem "{usuário} mudou \"Quem solicitou a ordem pode aprová-la?\" de {antes} para {depois}." com link para Parâmetros; quando quem troca é o único Proprietário, nenhum aviso é enviado (a auditoria grava igual). A notificação antiga "ligou/desligou a aprovação da própria ordem" é substituída por esta. O sucesso diz "Regra de aprovação salva".
17. **[Segurança]** **Dado** duas trocas simultâneas do modo, **quando** chegam, **então** a segunda recebe 409 `parameters_changed` com "Recarregar" (modo visto diferente do gravado, ou `xmin` da linha), sem gravar nem avisar; uma troca não tem efeito sobre OP já decidida.

**Comercial (regressão)**
18. **[Segurança]** **Dado** os três modos, **quando** o autor de um aditivo de contrato tenta aprová-lo, ou a pauta do Comercial monta "aditivos aguardando aprovação", **então** a regra continua "só o Proprietário": em "Nunca", só o Proprietário único; em "Só o Proprietário" e em "Qualquer aprovador", qualquer Proprietário; um não Proprietário nunca aprova o próprio aditivo nem o vê como seu na pauta.

**Permissões**
19. **Dado** o módulo `finance` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`. Sem permissão nova (`settings.edit` e a regra de Proprietário já existem); sem seed patch.

## Regras de negócio e técnicas
- **Modo** como enum `SelfApprovalMode` (`Never`, `OwnerOnly`, `AnyApprover`) em `BemAqui.Finance.Contracts`; `FinanceLimits` ganha `SelfApprovalMode`; o booleano `OwnerCanApproveOwn` continua, derivado (`modo != Never`), para o Comercial.
- `PaymentOrderRules.CanDecide(isRequester, requesterIsOwner, ownerMembers, mode)` (função pura, teste de unidade da tabela modo × Proprietário × membros); `PaymentOrderRules.ResolveMode(current, requestedMode, legacyBool)` (pura, testada). `ApprovalResolver.SelfAllowedAsync` usa a mesma função e continua restringindo o solicitante ao nível 1.
- **Migration aditiva** (`FinanceSelfApprovalMode`): `payment_order_approvals.self_approval_mode smallint null` e o `xmin` como versão de `finance.parameters` (coluna de sistema: nada é criado). Sem UPDATE em dados existentes. Decisões antigas ficam sem modo (nulo).
- **Provisionamento:** passo `FinanceDefaults` em `ProvisioningSteps.Ordered` (depois de `Seed`); porta `ITenantProvisioningDefaults` (BuildingBlocks) com `FinanceProvisioningDefaults` (Finance.Infrastructure); só roda com o tenant em `Provisioning`. Os testes que contam os passos (`ProvisioningTests` usa `ProvisioningSteps.Ordered`; `ProvisioningTests.Resume` lista as etapas "Já concluída") seguem passando; a tela do admin (`src/Web/src/platform/status.ts`, `STEP_LABEL`) ganha o rótulo.
- **Step-up:** o PUT recebe `code` e `selfApprovalModeSeen`; `IStepUpTwoFactor` ganha `IsEnrolledAsync` (Identity: `TotpEnabled`; `DenyStepUpTwoFactor`: falso). Código de uso único, sem reaproveitar a janela de "2FA recente".
- **Dinheiro e tempo:** nada novo; datas UTC.

## Permissões, eventos e tabelas
- **Permissões:** nenhuma nova. Mudar o modo: `settings.edit` **e** grupo Proprietário **e** código do autenticador; aprovar OP: `finance.approve`.
- **Eventos:** nenhum novo (`PaymentOrderApproved` igual).
- **Tabelas:** `finance.parameters` (`finance.owner_can_approve_own` com nova codificação 0/1/2; `xmin` como versão); `payment_order_approvals.self_approval_mode`. Atualizar `docs/03-modelo-dados.md` e `docs/04-fluxos-estados.md` (OP).

## Telas e estados
- **Configurações → Parâmetros → Financeiro:** grupo de rádios "Quem solicitou a ordem pode aprová-la?" com descrição de uma linha cada ("Nunca — outra pessoa precisa aprovar", "Só o Proprietário — quem é do Proprietário aprova a própria ordem, e fica registrado", "Qualquer aprovador (só no 1º nível) — quem tem alçada no 1º nível aprova a própria ordem, e fica registrado"); `<Dica>` em "Nunca": "Com um único Proprietário, ele continua aprovando as próprias ordens (sem ele a empresa travaria)." Os rádios ficam **fora** do `fieldset` de `canEdit`: só o Proprietário os edita; os demais veem em leitura com "Só o Proprietário muda esta regra.". "Salvar limites" com o modo trocado abre o diálogo "Confirme com o código do autenticador"; sucesso: "Regra de aprovação salva" (ou "Limites do financeiro salvos" sem troca de modo). Estados: carregando; código errado ("Código inválido ou já usado. Peça um novo."); sem 2FA ("Para continuar, ative a verificação em duas etapas." + "Ativar agora"); 409 com "Recarregar".
- **Ordem de pagamento (lista, detalhe, histórico, PDF, exportação):** selo "Aprovada pelo próprio solicitante"; aviso âmbar no diálogo "Aprovar" quando sou o solicitante; "Peça a outra pessoa que aprove." quando não posso, com o atalho para Parâmetros se sou do Proprietário.
- **400 px:** rádios empilhados; o diálogo de código ocupa a tela.
- **Ajuda (mesmo commit):** `Ajuda/configuracoes-parametros.md` (bloco "Quem aprova a própria ordem": três opções, padrão de tenant novo, código do autenticador, aviso aos outros Proprietários) e `Ajuda/financeiro-ordens-de-pagamento.md` (selo e o que muda para quem solicita).

## Plano de testes
- **Unidade:** tabela de `CanDecide` (modo × Proprietário × membros) e de `ResolveMode` (modo pedido × booleano legado × modo atual), CA 4 a 6 e 15; leitura do valor (0/1/2 e fora da faixa).
- **Integração (Testcontainers):** aprovar a própria OP nos três modos, com 1 e 2 Proprietários, com alçada de 2 níveis (CA 4 a 7); `self_approved` e `self_approval_mode` gravados (CA 8); reenvio preserva a decisão histórica e zera só o selo corrente; PDF e exportação com o selo (CA 10, 11); troca por não Proprietário (403), sem 2FA, sem código, código errado, código repetido, código certo, antes/depois na auditoria e aviso só aos outros Proprietários, PUT legado (CA 13 a 16); troca simultânea e modo visto desatualizado (CA 17); aditivo e pauta nos três modos (CA 18); provisionar tenant novo grava "Só o Proprietário", retomada de tenant ativo não grava, tenant antigo intacto (CA 1, 2).
- **Front (Vitest):** rádios com as três opções, leitura para não Proprietário, diálogo de código só quando o modo muda, aviso do diálogo "Aprovar".
- **Sem E2E** (por feature); sem teste de padrão repetido.

## Fora do escopo
- **Conciliação** (quem importou o OFX confirma): FIN-10b.
- Estender o modo ao aditivo e aos documentos com `SoleOwnerRule` (ADR 0024 §4): decisão à parte.
- 2FA por ação para aprovar ou pagar (adiado, P-19) e estorno com aprovação.
- Tela própria de histórico de trocas do parâmetro (a auditoria geral já mostra).

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração e front passando.
- [ ] Migration aditiva criada; nenhum dado existente alterado.
- [ ] Passo de provisionamento criado, só para tenant em `Provisioning`; nenhum `ITenantSeeder` muda parâmetro.
- [ ] Tela com estados; ajudas atualizadas; teste de cobertura passando.
- [ ] Auditoria e aviso aos outros Proprietários ligados; step-up e concorrência testados.
- [ ] OpenAPI e cliente Orval regenerados; `docs/03`, `docs/04` e ADR 0054 atualizados.
- [ ] Story marcada no backlog.

## Tarefas técnicas
- [ ] 1. Causa do bloqueio no BemAqui pela leitura do código (sem acesso à VPS nem a bancos), registrada no Change Log.
- [ ] 2. `SelfApprovalMode` + `FinanceLimits` + `PaymentOrderRules.CanDecide`/`ResolveMode` + `ApprovalResolver`; testes de unidade.
- [ ] 3. Decisão grava `self_approval_mode`; migration aditiva (`self_approval_mode` e `xmin` na `parameters`).
- [ ] 4. `FinanceParametersService.UpdateAsync`: campos opcionais, booleano legado, Proprietário, 2FA (`IsEnrolledAsync`, `VerifyAsync`), modo visto e `xmin` (409), aviso aos outros Proprietários no mesmo `SaveChanges`.
- [ ] 5. Passo `FinanceDefaults` no provisionamento (porta `ITenantProvisioningDefaults`), rótulo no admin; testes de tenant novo, retomada de tenant ativo e tenant antigo.
- [ ] 6. Testes do aditivo e da pauta nos três modos.
- [ ] 7. Front: rádios em `FinanceLimitsSection.tsx` (fora do `fieldset` de `canEdit`), diálogo de código, aviso no diálogo "Aprovar"; `pnpm gen:api`.
- [ ] 8. Ajudas, `docs/03`, `docs/04`, ADR 0054, backlog.

## Dev Notes
- **Backend (Finance):** `BemAqui.Finance.Domain/PaymentOrderRules.cs`; `BemAqui.Finance.Application/PaymentOrderService.Decide.cs` (`DecisionRuleAsync`, `DecideAsync`), `PaymentOrderService.cs` (`canDecide` do detalhe), `PaymentOrderService.Reverse.cs` (reenvio zera o selo corrente); `ApprovalResolver.cs` (`SelfAllowedAsync`); `FinanceParametersService.cs`; `ReceivableDtos.cs` (`FinanceLimitsInput`) e `BemAqui.Finance.Contracts/ReceivablesContracts.cs` (`FinanceLimits`); `PaymentOrderPdfDocument.cs` e `Exports/FinanceExports.cs` (selo já existe: provar por teste).
- **Step-up:** `BuildingBlocks/Identity/IStepUpTwoFactor.cs`; implementação em `Identity.Application/TwoFactorService.StepUp.cs`.
- **Provisionamento:** `Platform.Application/Provisioning/ProvisioningService.cs` (`RunStepAsync`), `ProvisioningResult.cs` (`ProvisioningSteps`); admin em `Platform.Application/Tenants/TenantAdminService.cs` e `src/Web/src/platform/status.ts`; testes `tests/.../Tenancy/ProvisioningTests*.cs`.
- **Comercial (só testes):** `AmendmentService.Decide.cs` (`DecisionRuleAsync`) e `CommercialAgendaSources.cs` (`MayDecideOwnAsync`).
- **Front:** `src/Web/src/pages/settings/parameters/FinanceLimitsSection.tsx`; `pages/finance/payment-orders/PaymentOrderPage.tsx`.
- **Riscos:** (1) o booleano é lido pelo Comercial: derivação testada nos três modos; (2) "Só o Proprietário" nos tenants novos afrouxa o controle por padrão: selo, auditoria e aviso são obrigatórios e a ajuda explica como voltar para "Nunca"; (3) `xmin` na `parameters` passa a valer para as outras telas que gravam parâmetros (DRE, cobrança): só muda algo em gravação simultânea real.

## Quality gates
- Agentes previstos: @dev, @qa, cybersecurity (step-up, segregação de função, auditoria).
- Gate: CA 1, 2, 7, 14, 16 e 17 obrigatórios.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 27/11/2026 | 0.1 | Story escrita a partir do pedido do PO (BemAqui, empresa de uma pessoa): dois parâmetros de três opções, padrão por provisionamento, selo, auditoria e troca com código do autenticador. Encontrado no código que a exceção D6 e o selo da OP já existem; a story generaliza o parâmetro, estende à conciliação e fecha a causa do bloqueio | @sm |
| 27/11/2026 | 0.2 | **Revisão de 27/11/2026.** Dividida: esta (FIN-10a) fica só com a ordem de pagamento; a conciliação foi para a FIN-10b (Draft). Achados aplicados, conferidos no código: (3) auditoria antes/depois sempre, aviso a todos os Proprietários **menos o autor** (mudança explícita), notificação no mesmo `SaveChanges`; (4) `FinanceParameter` não tinha versão: `xmin` (`IConcurrencyVersioned`) + `selfApprovalModeSeen`, 409 `parameters_changed`, na migration e nos testes; (5) PUT com campos novos opcionais, booleano legado só alterna Nunca ↔ Só o Proprietário e nunca rebaixa "Qualquer aprovador", ordem das checagens (Proprietário, modo visto, 2FA ativo via `IStepUpTwoFactor.IsEnrolledAsync`, código, uso único), código só quando o modo muda; (6) passo de provisionamento só para tenant em `Provisioning`, nunca numa retomada de tenant ativo, com testes de retomada de tenant antigo e de dois tenants, tela do admin e testes que contam os passos citados; (7) aditivo e pauta continuam "só Proprietário" (o booleano derivado `modo != Never` só vale para quem já é Proprietário), com testes nos três modos; (8) reenvio e estorno preservam a decisão histórica, o reenvio zera só o selo corrente, PDF e exportação mostram o selo (CA 10 e 11); (9) textos: "Qualquer aprovador (só no 1º nível)", `<Dica>` de "Nunca" citando o Proprietário único, rádios só editáveis pelo Proprietário (fora do `fieldset` de `canEdit`), textos do aviso e do toast. **Causa provável do bloqueio no BemAqui** (leitura do código; VPS e bancos não acessados): a exceção D6 (a) só vale com **um** membro ativo no grupo do sistema Proprietário (`UserDirectory.OwnerIdsAsync`: `is_system`, `seed_key = owner`, usuário `Active`); com um segundo Proprietário ativo, ou com o John fora do grupo do sistema, a OP que ele solicitou não tem quem aprove (`own_payment_order`, e `EligibleAsync` o tira do nível 1). Destrave imediato para o PO: marcar **"Proprietário aprova a própria ordem"** em Configurações → Parâmetros → Financeiro (vale com mais de um Proprietário, se o John for do grupo); se o John não for do Proprietário, incluí-lo no grupo. | @po |
