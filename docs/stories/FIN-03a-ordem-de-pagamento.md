# FIN-03a — Ordem de pagamento: criar, aprovar ou reprovar e pagar

| Campo | Valor |
|---|---|
| ID | FIN-03a |
| Título | Ordem de pagamento com um ou mais títulos a pagar, aprovação pela alçada padrão (quem tem `finance.approve`, por padrão só o Proprietário), reprovação com comentário, solicitante que não aprova a própria OP, pagamento com `finance.pay` que dá baixa e cria a transação, estorno e notificações |
| Tamanho | G (quebra da FIN-03: a alçada configurável, o multinível e o link no e-mail foram para a FIN-03b) |
| Status | Draft |
| Requisitos | R-FIN-5 (todo pagamento exige OP aprovada; padrão: proprietário aprova tudo; aprovação/reprovação com comentário) · R-COM-2 (notificação "OP aguardando aprovação") · R-COM-4 (numeração OP) · R-PRP-2 · R-PRP-5 · R-PRP-7 · R-SEC-5 · R-SEC-2 (2FA para quem aprova ou paga) |
| Dependências | FIN-02 (títulos a pagar), FIN-01 (`financial_transactions`), CAD-05b (contas), FND-06c (2FA obrigatório para quem aprova ou paga), FND-07 (notificações, outbox); Fase 0 (FND-09c, FND-09d, FND-14) |
| Bloqueia | FIN-03b, FIN-04 (pagamentos no extrato), FIN-05 (realizado), DSH-01 ("Pendências do dono"), DSH-04 |
| Ordem de execução | 3 (Marco 2.1 — caixa e pagamentos; entrega o "primeiro ganho financeiro" do PRD §7) |

## User story
Como **proprietário**, quero **que todo pagamento passe por uma ordem que eu aprovo ou reprovo com um comentário**, e como **financeiro**, quero **pagar só o que foi aprovado e ver a baixa refletida na conta**, para **nenhum dinheiro sair da empresa sem autorização formal**.

## Contexto e referências
- PRD R-FIN-5, §1.1 problema 5 ("pagamentos saem sem aprovação formal"), §1.2 métrica "100% dos pagamentos passando por OP aprovada".
- `04-fluxos-estados` §1 (`PaymentOrder: Pending → Approved → Paid`, `Rejected` volta os títulos para `Open` com comentário obrigatório; regra dura "nenhum pagamento é baixado sem OP aprovada"); §3.6 (quem solicita não aprova a própria OP, exceto o proprietário, configurável); §3.7 (`finance.approve` não dispensa a alçada).
- `03-modelo-dados` → `payment_orders`, `payment_order_approvals`; revisão pré-Fase 0 §2.1 (`payable_ids` vira tabela de junção; status do título derivado da OP).
- ADR 0008 (só o Proprietário concede `*.approve` e `finance.pay`); ADR 0010 item 3 (o Financeiro do seed não tem `finance.approve`); FND-06c CA 12 (2FA obrigatório para quem tem `*.approve` ou `*.pay`).
- `ModuleCatalog`: `finance.approve` = "ordem de pagamento, dentro da alçada"; `finance.pay` = "registra a saída de caixa de uma ordem aprovada".
- Protótipo: tela "Caixa e pagamentos" com a lista de OPs; o texto "até R$ 5.000 o financeiro aprova" é ilustrativo e não vale (o padrão do PRD é "proprietário aprova tudo"; faixas configuráveis na FIN-03b).

## Critérios de aceite
**Criar a OP**
1. **Dado** `finance.create` e títulos `Open` selecionados na lista "A pagar", **quando** clico "Criar ordem de pagamento", **então** a OP abre com os títulos, o valor a pagar de cada um vem com o saldo líquido em aberto (editável para menos, pagamento parcial), a data prevista vem com o menor vencimento e "Enviar para aprovação" grava a OP `Pending` com número `OP-2026-0001`; os títulos passam a `AwaitingApproval` na mesma transação.
2. **Dado** um título que já está em outra OP não finalizada, `Planned`, `Cancelled` ou `Paid`, **quando** tento incluí-lo, **então** 422 "O título {número} não está disponível para pagamento."; valor a pagar maior que o saldo líquido ou ≤ 0 responde 422.
3. **Dado** a OP enviada, **quando** grava, **então** cada aprovador elegível (quem tem `finance.approve`; na FIN-03b, os da alçada) recebe no sino "OP {número} de {total} aguarda sua aprovação", com link direto para a OP (R-PRP-5), e o solicitante não recebe.

**Aprovar e reprovar**
4. **Dado** `finance.approve` e uma OP `Pending`, **quando** clico "Aprovar", escrevo um comentário opcional e confirmo "Aprovar", **então** a OP passa a `Approved`, a decisão (aprovador, nível 1, decisão, comentário, data) é gravada em `payment_order_approvals`, os títulos passam a `Approved` e o evento `PaymentOrderApproved` notifica quem tem `finance.pay` ("OP {número} aprovada. Pode pagar."); o toast diz "Aprovada".
5. **Dado** `finance.approve` e uma OP `Pending`, **quando** clico "Reprovar", **então** o comentário é obrigatório (422 "Escreva o motivo da reprovação."), a OP passa a `Rejected`, os títulos voltam a `Open` e o solicitante é notificado com o comentário; o toast diz "Reprovada".
6. **[Segurança]** **Dado** um usuário que criou a OP, **quando** tenta aprová-la, **então** a API responde 403 `own_payment_order` "Quem solicitou a ordem não pode aprová-la."; a exceção é o membro do Proprietário quando o parâmetro `finance.owner_can_approve_own` estiver ligado (ver Pontos em aberto 1), e nesse caso a aprovação fica marcada "Aprovada pelo próprio solicitante" no detalhe e na auditoria.
7. **[Segurança]** **Dado** duas aprovações (ou aprovação e reprovação) enviadas ao mesmo tempo, **quando** chegam, **então** só a primeira muda a OP (transição condicional `WHERE status = 'Pending'`) e a segunda recebe 409 "A ordem já foi decidida."
8. **[Segurança]** **Dado** um usuário com `finance.approve` ou `finance.pay` sem 2FA, **quando** tenta aprovar ou pagar, **então** é levado à configuração do 2FA (FND-06c CA 12) e a API recusa a ação até o 2FA estar ativo.

**Pagar**
9. **Dado** `finance.pay` e uma OP `Approved`, **quando** clico "Pagar", escolho a conta bancária (padrão: a conta padrão), a data do pagamento (padrão: hoje), informo por título juros, multa e desconto (opcionais) e confirmo "Pagar", **então** na mesma transação: para cada título, uma baixa (`payable_settlements`) e uma `financial_transaction` `Payment` com valor negativo (principal + juros + multa − desconto), o título fica `Paid` (saldo zero) ou `PartiallyPaid` (pagamento parcial) e a OP fica `Paid`; o toast diz "Pagamento registrado" e a faixa "Próximos passos" oferece "Ver extrato da conta" e "Anexar comprovante".
10. **[Segurança]** **Dado** uma OP `Pending`, `Rejected`, `Cancelled` ou `Paid`, **quando** chamo o pagamento pela API, **então** a resposta é 409 `payment_order_not_approved` "Só é possível pagar uma ordem aprovada." e nenhuma transação é criada; um teste de integração prova que não há outro caminho que crie transação `Payment`.
11. **[Segurança]** **Dado** dois pagamentos da mesma OP enviados ao mesmo tempo, **quando** chegam, **então** só um grava (transição condicional `WHERE status = 'Approved'`) e o outro recebe 409.
12. **Dado** o diálogo "Pagar", **quando** abre, **então** mostra o total que vai sair da conta, por título e somado, ao vivo em `aria-live`, a conta e o saldo atual dela (FIN-04, quando existir) e o aviso âmbar "O saldo da conta fica negativo." quando for o caso (não bloqueia).

**Cancelar e estornar**
13. **Dado** `finance.edit` e uma OP `Pending`, **quando** o solicitante (ou quem tem `finance.approve`) clica "Cancelar ordem" e confirma com motivo, **então** a OP fica `Cancelled` e os títulos voltam a `Open`; OP `Approved` só é cancelada com `finance.approve`; OP `Paid` não é cancelada (409 "Estorne o pagamento.").
14. **Dado** `finance.pay` e uma OP `Paid`, **quando** clico "Estornar pagamento", informo o motivo e confirmo "Estornar pagamento", **então** as baixas ficam estornadas, transações inversas são criadas, os títulos voltam a `Open` (fora de OP), a OP fica `Cancelled` com o motivo, e os membros do Proprietário são notificados; nada é apagado.

**Consulta, permissões e auditoria**
15. **Dado** `finance.view`, **quando** abro a lista de OPs, **então** vejo número, solicitante, total, data prevista, títulos, situação (Aguardando aprovação alerta, Aprovada ok, Reprovada crítico, Paga ok, Cancelada neutro) e "Aguardando você" filtrado para quem pode aprovar; a OP mostra a linha do tempo das decisões.
16. **Dado** o módulo `finance` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; sem `finance.approve`, aprovar e reprovar respondem 403 e os botões não aparecem; sem `finance.pay`, pagar e estornar respondem 403. Todo endpoint tem `RequireModule` e `perm:`; `/{opId}/items/{itemId}` confere o vínculo (IDOR).
17. **Dado** qualquer ação na OP, **quando** abro "Histórico de alterações", **então** vejo criação, decisões, pagamento, cancelamento e estorno com usuário, data e comentário (auditoria completa, CA do backlog).

## Regras de negócio e técnicas
- **Junção** `payment_order_items` (proposta, revisão pré-Fase 0 §2.1): `payment_order_id`, `payable_id`, `amount` (`numeric(18,2)`, ≤ saldo líquido). `payment_orders.total` = Σ itens. Um título está em no máximo uma OP não finalizada (índice único parcial).
- **Situação do título derivada da OP:** `AwaitingApproval` (OP `Pending`), `Approved` (OP `Approved`), `PartiallyPaid`/`Paid` (baixas), `Open` (reprovada, cancelada ou estornada). Só os casos de uso da OP gravam esses estados.
- **Pagamento parcial:** o título fica `PartiallyPaid` (estado **proposto**, não está no 03 para `payables`) e pode entrar numa nova OP pelo saldo.
- **Baixa** `payable_settlements` (proposta, simétrica à FIN-01): `payable_id`, `payment_order_id`, `bank_account_id`, `date`, `principal`, `interest`, `fine`, `discount`, `total`, `transaction_id`, colunas de estorno. Invariante `paid_amount = Σ principal das baixas não estornadas`.
- **Alçada nesta story:** regra padrão única "quem tem `finance.approve` aprova qualquer valor, em um nível" (`approval_rule_id` nulo). A FIN-03b substitui pelo motor de regras sem mudar os casos de uso.
- **Transições** atômicas com `UPDATE … WHERE status = …` e `xmin`; datas `date`; dinheiro `numeric(18,2)`; Guid v7; numeração `OP`; auditoria `IAuditable` em OP, itens, decisões e baixas.

## Permissões, eventos e tabelas
- **Permissões (já no `ModuleCatalog`):** `finance.create` (criar e enviar OP), `finance.edit` (cancelar a própria OP pendente), `finance.approve` (aprovar, reprovar, cancelar aprovada), `finance.pay` (pagar, estornar pagamento).
- **Parâmetro (proposta):** `finance.owner_can_approve_own` em `identity.tenant_settings` (via `ITenantParameters`), editado em Configurações → Parâmetros com `settings.edit`.
- **Eventos (outbox, `Finance.Contracts`):** `PaymentOrderSubmitted` (`{ paymentOrderId, total, requestedBy }`) → notificação aos aprovadores; `PaymentOrderApproved` (`{ paymentOrderId }`) → notificação a quem paga; `PaymentOrderPaid` (`{ paymentOrderId, paidOn }`), sem consumidor nesta fase.
- **Tabelas:** `finance.payment_orders` (como no 03, sem `payable_ids`; mais `cancel_reason`, `submitted_at`, `self_approved`); `finance.payment_order_items` (**proposta**); `finance.payment_order_approvals` (como no 03); `finance.payable_settlements` (**proposta**); `finance.financial_transactions` (FIN-01). Anexo de comprovante: dono `PaymentOrder` no `AttachmentOwnerRegistry` (leitura `finance.view`, escrita `finance.pay`).
- **Destinatários das notificações:** `IUserDirectory` ganha `UsersWithPermissionAsync(permission)` (proposta), calculado pela permissão efetiva, sem acesso às tabelas do Identity.

## Telas e estados
- **Financeiro → Ordens de pagamento** (`/app/financeiro/ordens-de-pagamento`): h1 "Nenhum pagamento sai sem ordem aprovada."; DataTable com filtros (situação, "Aguardando você", período).
- **Ordem de pagamento** (`/app/financeiro/ordens-de-pagamento/$id`), abas: Títulos, Decisões, Comprovantes, Histórico de alterações. Botões conforme situação e permissão: "Aprovar", "Reprovar", "Pagar", "Cancelar ordem", "Estornar pagamento".
- **Diálogos:** "Enviar para aprovação" / "Enviada para aprovação"; "Aprovar" / "Aprovada"; "Reprovar" / "Reprovada"; "Pagar" / "Pagamento registrado"; "Cancelar ordem" / "Ordem cancelada"; "Estornar pagamento" / "Pagamento estornado".
- **Estados:** carregando; vazio ("Selecione títulos em A pagar e crie a primeira ordem de pagamento." + "Ir para A pagar"); vazio em "Aguardando você" ("Nada para aprovar agora."); erro; modo leitura.
- **Propositivo:** títulos vencidos e que vencem nos próximos 7 dias aparecem pré-selecionáveis com o atalho "Selecionar os que vencem até {data}"; conta padrão e hoje no pagamento; notificação leva direto ao botão "Aprovar"; depois de aprovar, "Próxima OP aguardando você".
- **400 px e teclado:** E2E do diálogo "Pagar" e da aprovação.
- **Ajuda:** `Ajuda/financeiro-ordens-de-pagamento.md` (`padrao: true`) e `Ajuda/financeiro-ordem-de-pagamento.md` (abas `titulos`, `decisoes`, `comprovantes`, `historico`); `Ajuda/configuracoes-parametros.md` ganha "Proprietário aprova a própria ordem"; `<Dica>` em "Alçada", "Pagamento parcial" e "Aprovada pelo próprio solicitante".

## Plano de testes
- **Unidade:** máquina de estados da OP e do título derivado (CA 1, 4, 5, 9, 13, 14); regra do solicitante (CA 6); total ao vivo (CA 12).
- **Integração (Testcontainers, obrigatória):** criar OP com títulos disponíveis e indisponíveis (CA 1, 2); notificações a aprovadores e pagadores (CA 3, 4, 5); solicitante não aprova, com e sem o parâmetro (CA 6); concorrência de decisão e de pagamento (CA 7, 11); 2FA exigido (CA 8); pagamento total e parcial com transações e baixas na mesma transação (CA 9); pagamento sem aprovação bloqueado em todos os caminhos (CA 10); cancelar e estornar (CA 13, 14); três camadas e IDOR (CA 16); auditoria completa (CA 17).
- **Arquitetura:** só `PayPaymentOrderHandler` cria transação `Payment`; títulos não mudam para `AwaitingApproval`/`Approved`/`Paid` fora dos casos de uso da OP.
- **E2E (Playwright):** financeiro cria OP, proprietário aprova pelo link da notificação, financeiro paga; solicitante não vê "Aprovar" na própria OP; diálogo a 400 px.

## Fora do escopo
- Regras de alçada por valor e conta, multinível e aprovação pelo link do e-mail: FIN-03b.
- Ações em lote (aprovar várias OPs, R-PRP-6, D).
- Pagamento pelo banco (Pix/TED pela API do Inter): fora do MVP; o pagamento aqui é o registro da saída feita no banco. A BNK-03 concilia.
- Débitos automáticos e tarifas "sem OP" (exceção do `04-fluxos` §1): BNK-03, junto da conciliação.

## Pontos em aberto
1. **Proprietário aprovando a própria OP.** O `04-fluxos` §3.6 diz "exceto o proprietário, configurável", sem padrão. **Recomendação:** parâmetro `finance.owner_can_approve_own` ligado por padrão (na Konebras o dono aprova tudo e às vezes lança), com a marca "Aprovada pelo próprio solicitante" e a lista dessas OPs visível ao Proprietário; o membro de outro grupo nunca aprova a própria.
2. **Segregação entre quem aprova e quem paga.** Os docs só proíbem solicitante = aprovador. **Recomendação:** não exigir pagador ≠ aprovador no MVP (equipe pequena), registrando os dois na OP.
3. **Comprovante obrigatório.** **Recomendação:** opcional, com o próximo passo "Anexar comprovante".
4. **Financeiro do seed sem `finance.pay`.** O seed dá ao Financeiro só `view`, `create` e `edit` (ADR 0010 item 3), e `finance.pay` é ação reservada ao Proprietário (ADR 0008). Sem mudança, só o Proprietário registra pagamentos. **Recomendação:** *seed patch* (ADR 0016) que concede `finance.pay` ao grupo Financeiro, mantendo `finance.approve` só com o Proprietário (quem aprova não é quem registra o pagamento); alternativa: o Proprietário concede à mão.
5. **P-18:** ver FIN-01, ponto 5.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers), arquitetura e E2E passando.
- [ ] Migration criada (não destrutiva); parâmetro com seed idempotente.
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Telas com estados de carregando, vazio e erro; modo leitura.
- [ ] Ajuda da lista e de cada aba; teste de cobertura passando.
- [ ] Padrões comuns da Fase 2, próximos passos e confirmação de efeito financeiro aplicados.
- [ ] Auditoria completa ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` e `docs/04-fluxos-estados.md` (estado `PartiallyPaid` do título a pagar) atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Entidades `PaymentOrder`, `PaymentOrderItem`, `PaymentOrderApproval`, `PayableSettlement`; máquina de estados.
- [ ] `IApprovalPolicy` com a implementação padrão (substituída na FIN-03b).
- [ ] `IUserDirectory.UsersWithPermissionAsync` (Identity).
- [ ] Endpoints `/api/v1/finance/payment-orders` (+ `/submit`, `/approve`, `/reject`, `/pay`, `/cancel`, `/reverse`); telas e ajuda.

## Quality gates
- Agentes previstos: @dev, cybersecurity (segregação, concorrência, caminho único de pagamento), @qa.
- Gate: CA 6, 7, 8, 10 e 11 obrigatórios e nunca ignorados.

## Validação do PO
Checklist do @po (Pax), 19/10/2026.

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto registrados sem decidir | OK |

- **Correções feitas na validação (19/10/2026):** quebra da FIN-03 (G+) em 03a e 03b; o texto de alçada do protótipo marcado como ilustrativo; junção `payment_order_items` com valor por título (pagamento parcial); estado `PartiallyPaid` do título a pagar marcado como proposta; 2FA explícito (CA 8); destinatários por permissão efetiva sem tocar no Identity; ponto em aberto 4 (o Financeiro do seed não tem `finance.pay`, então hoje só o Proprietário pagaria); o parâmetro novo entra na ajuda de Configurações → Parâmetros.
- **Pré-condições para `Ready`:** Pontos em aberto 1 a 4 (com padrão recomendado) e P-18.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 19/10/2026 | 0.1 | Story detalhada (quebra da FIN-03) | River (SM) |
| 19/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
