# CMP-03a — Pedido de compra: criação, aprovação por alçada e CP previsto

| Campo | Valor |
|---|---|
| ID | CMP-03a |
| Título | Pedido de compra manual ou a partir da solicitação, vinculado a projeto ou ao estoque geral, com a API de criação que a CMP-02 usa para gerar o pedido do vencedor da cotação; envio para aprovação com alçada (`IApprovalRouting`, motor da FIN-03b para `PurchaseOrder`) e segregação solicitante × aprovador; na aprovação, os títulos a pagar previstos (`Planned`, data estimada) por parcela da condição nascem na mesma transação e entram no fluxo de caixa; cancelamento antes de receber cancela a previsão |
| Tamanho | G (quebra da CMP-03: o PDF e o envio ao fornecedor foram para a CMP-03b) |
| Status | Ready |
| Requisitos | R-CMP-3 (pedido com aprovação por alçada) · R-CMP-4 (pedido gera previsão de CP no fluxo de caixa) · R-CMP-6 (projeto ou estoque geral) · R-FIN-4 (CP originado de compras) · R-FIN-10 (pedidos de compra no projetado) · R-FIN-5 (alçadas) · R-COM-4 (numeração PC) · R-COM-2 · R-PRP-1 · R-PRP-7 · R-SEC-5 |
| Dependências | CMP-01 (SC), FIN-03b (motor de alçadas), FIN-02 (`IPayables`), FIN-05 (fluxo de caixa com `Planned` estimado); **ADR 0024** |
| Bloqueia | CMP-04 (recebimento contra o pedido), CMP-02 (pedido a partir do vencedor da cotação), CMP-03b (PDF e envio) |
| Ordem de execução | 15 (Marco 3.3 "Comprar o que falta") |

## User story
Como **comprador**, quero **criar o pedido a partir da solicitação (ou à mão) e mandá-lo para aprovação**, e como **proprietário**, quero **aprovar dentro da alçada e já ver o pagamento previsto no fluxo de caixa**, para **nenhuma compra sair sem aprovação e o caixa mostrar o compromisso antes da nota chegar**.

## Contexto e referências
- PRD §5.10 R-CMP-3, R-CMP-4, R-CMP-6; §5.11 R-FIN-4, R-FIN-5, R-FIN-10 ("base = títulos em aberto + recorrentes + pedidos de compra + folha prevista").
- `04-fluxos-estados` §1 "Pedido de compra": `Draft → PendingApproval → Approved → Sent → PartiallyReceived → Received`; `Cancelled` antes de receber. §1 "Título a pagar": `Planned (previsão: pedido de compra/folha/instalador; pode ter data estimada) → Open (confirmado)`. §3.6 (alçadas: regra mais específica, valor de referência contra fracionamento, multinível, solicitante não aprova o próprio, D6). O §2 passa a dizer que o título previsto do pedido nasce por chamada síncrona, não pelo `PurchaseOrderApproved` (ADR 0024 §1; atualizar no DoD).
- `03-modelo-dados` → `purchasing.purchase_orders` (`number`, `party_id`, `rfq_id`, `project_id`, `status`, `payment_term_id`, `expected_date`, `total`), `purchase_order_lines` (`order_id`, `item_id`, `quantity`, `unit_price`, `quantity_received`); `finance.approval_rules.document_type` (PaymentOrder/**PurchaseOrder**, reservado pela FIN-03b).
- **Código atual:** o motor de alçadas é interno do Finance (`ApprovalResolver.ResolveAsync`/`EligibleAsync` e `ApprovalRuleService`, filtrando `PaymentOrder`); `IPayables.CreateFromOriginAsync` cria **um** título por `(origin, originId)`, com conta `3.9` e `CostType.Other` para qualquer origem que não seja `Installer`; `DocumentOrigin.Purchase` já existe (passa a ser o título previsto do pedido).
- **ADR 0024** (compras e financeiro): §1 chamada síncrona, §2 `IPayables` por parcela, §4 `IApprovalRouting`, §5 aviso "1 cotação". **ADR 0025** §1 (compra para o projeto como "Comprometido", D2). ADR 0023 §5 (`SoleOwnerRule`). ADR 0019 §17 (CP estimado com `is_estimate`, `Tag` "Estimado"), §3 (idempotência), §6 (alçada viável). ADR 0008 (`purchasing.approve` reservada ao Proprietário para conceder). ADR 0019 §7, adendo de 21/10/2026 (dono de anexo `PurchaseOrder`).
- Revisão `docs/revisoes/2026-10-21-revisao-pre-fase3.md`: D2, D8, tabela 1.1 itens 38–44, §2.1 "Compras ↔ Financeiro", §2.2 CMP-03a, §2.3, §4 (nova ordem: a CMP-03a vem antes da CMP-02).
- `IPaymentTerms` (CAD-05b, entregue). Plano de contas do seed: `2.1 Materiais`, `2.4 Fretes`.

## Critérios de aceite
**Criar**
1. **Dado** `purchasing.create`, **quando** crio um pedido manual (itens digitados) ou a partir de linhas de SC `Open` (CMP-01), **então** nasce um pedido `Draft` com número `PC-2026-0001`, as linhas (item, quantidade, preço unitário `numeric(18,6)`), frete e IPI, a condição de pagamento (padrão do fornecedor), a data prevista de entrega (padrão: hoje + o prazo padrão de compra de `purchasing.parameters`, CMP-01), o destino (projeto da SC quando todas as linhas são do mesmo projeto, senão "Estoque geral"; R-CMP-6) e o vínculo com as linhas de SC, que passam a `Ordered`; o fornecedor vem sugerido pelo último fornecedor do item e o preço pelo último preço pago (R-PRP-1), com a `Tag` "Sugerido"; com `Idempotency-Key`.
2. **Dado** o caso de uso de criação (`POST /api/v1/purchasing/purchase-orders`), **quando** é chamado com `rfqId`, as respostas vencedoras e o número de respostas recebidas na cotação, **então** cria o pedido `Draft` com `rfq_id`, `quotes_count` e a data prevista pelo prazo da resposta, pelas mesmas regras do CA 1 e 3; a tela "Gerar pedidos de compra" e o "Enviar todos para aprovação" são da CMP-02, que chama esta API.
3. **Dado** um pedido `Draft`, **quando** edito, **então** o total = Σ linhas (qtd × preço, 2 casas `AwayFromZero`) + frete + IPI aparece ao vivo no rodapé, e o servidor recalcula tudo ao salvar (valores do cliente nunca são confiados); fornecedor sem papel Fornecedor, item inativo ou projeto fora de andamento: 422 no campo.
4. **Dado** o pedido com destino em projeto, **quando** grava, **então** guarda `project_id` (validado por `IProjects`) e a natureza de custo por linha: `Material` para os itens e `Freight` para o frete.

**Aprovação por alçada (ADR 0024 §4)**
5. **Dado** um pedido `Draft` válido, **quando** clico "Enviar para aprovação", **então** passa a `PendingApproval`; o Purchasing chama `IApprovalRouting.ResolveAsync(PurchaseOrder, referenceAmount, accountId, requesterId)` com a conta do título (CA 19) e o **valor de referência** = o maior entre o total do pedido e a soma dos pedidos não cancelados do mesmo fornecedor nos últimos 30 dias (aprovados ou pendentes, incluindo este), calculado pelo Purchasing; os níveis resolvidos (regra, aprovadores, aviso de fracionamento) ficam em `purchase_order_levels`, no Purchasing; sem regra específica de `PurchaseOrder`, vale a regra padrão própria do tipo, "Proprietário aprova tudo" (seed das `approval_rules`).
6. **[Segurança]** **Dado** dois pedidos de R$ 3.000,00 ao mesmo fornecedor em 30 dias e uma regra até R$ 5.000,00 para um aprovador e acima disso para o Proprietário, **quando** envio o segundo, **então** a alçada é resolvida por R$ 6.000,00 (fracionamento calculado sobre os pedidos, nunca sobre os títulos), com o aviso de fracionamento gravado no nível e mostrado na aprovação.
7. **[Segurança]** **Dado** um nível sem aprovador elegível (com `purchasing.approve` efetivo, diferente do solicitante e dos outros níveis), **quando** envio, **então** 422 `approval_not_viable` com o motivo (ADR 0019 §6), e o pedido continua `Draft`.
8. **Dado** o envio, **quando** grava, **então** os aprovadores do nível 1 recebem a notificação e o e-mail "Pedido de compra {número} aguardando aprovação" **só com** número, fornecedor, total e o link autenticado para o pedido (mesma regra da FIN-03b CA 16 e 17), pelo outbox (`PurchaseOrderSubmitted`).
9. **[Segurança]** **Dado** um aprovador, **quando** decide, **então**: quem pode decidir vem de `IApprovalRouting.EligibleAsync(PurchaseOrder, level, ruleId, requesterId)`; o solicitante não aprova o próprio pedido, com a exceção do Proprietário único decidida pelo helper `SoleOwnerRule` (BuildingBlocks), que grava a marca "Autoaprovado" e notifica; quem está fora da regra do nível recebe 403 `outside_approval_rule` "Este pedido não está na sua alçada.", mesmo com `purchasing.approve`; o mesmo aprovador não decide dois níveis (403 `already_decided_level`); todas as aprovações do nível N antes do N+1 (`04-fluxos` §3.6).
10. **[Segurança]** **Dado** um usuário com `finance.approve` e sem `purchasing.approve`, **quando** tenta aprovar ou reprovar um pedido, **então** 403 (o mapa tipo de documento → permissão do aprovador fica no Finance: `PaymentOrder` → `finance.approve`, `PurchaseOrder` → `purchasing.approve`); teste de integração obrigatório.
11. **Dado** um pedido com `rfq_id` cuja cotação teve uma resposta só (`quotes_count = 1`), **quando** abro o diálogo de aprovação, **então** aparece o aviso âmbar "1 cotação: este pedido veio de uma cotação com uma resposta só.", que não bloqueia a aprovação (D8).
12. **Dado** a aprovação do último nível, **quando** grava, **então** na **mesma transação** (ADR 0024 §1): o pedido fica `Approved`; o Purchasing chama `IPayables.CreatePlannedInstallmentsAsync(Purchase, pedidoId, fornecedor, projeto, conta, natureza, parcelas, descrição)`, com um título `Planned` por parcela da condição (`IPaymentTerms.SplitAsync` sobre o total, a partir da data prevista de entrega), `is_estimate = true` e a descrição "Pedido {número} — parcela {n}/{m}", idempotente por parcela; e o `PurchaseOrderApproved` (`{ purchaseOrderId, partyId, projectId, total, paymentTermId, expectedDate }`) vai para o outbox só para aviso e projeção. Falha do `IPayables` desfaz tudo (o pedido continua `PendingApproval`). A mensagem diz "Pedido aprovado. Previsão lançada no caixa."; os títulos aparecem no fluxo de caixa com a `Tag` "Estimado" (FIN-05).
13. **Dado** um pedido aprovado com projeto, **quando** abro o Projeto 360 → Custos, **então** os títulos de origem `Purchase` de item estocável **não** entram no realizado e aparecem em "Comprometido" (D2, ADR 0025 §1; a coluna e o cálculo são da PRD-04); o material conta como realizado só na baixa para a OS (EST-03).
14. **Dado** "Reprovar pedido" com comentário obrigatório, **quando** grava, **então** o pedido volta a `Draft` com o comentário, os níveis e as decisões da tentativa ficam no histórico, imutáveis (numerados por tentativa), e o solicitante é notificado; o reenvio resolve a alçada de novo (CA 5).

**Mudanças e cancelamento**
15. **[Segurança]** **Dado** um pedido `PendingApproval`, `Approved` ou `Sent` sem recebimento, **quando** edito quantidade, preço, frete, IPI, fornecedor ou condição e confirmo no diálogo de impacto, **então** ele volta a `PendingApproval` com nova resolução de alçada (as decisões anteriores ficam no histórico); os previstos de antes continuam até a reaprovação, que os troca na mesma transação por `IPayables.ReplacePlannedAsync(Purchase, pedidoId, parcelas)`; algum previsto em OP recusa a edição com 409 `payable_in_payment_order` (o diálogo mostra o link "Ver OP"); pedido que estava `Sent` volta aprovado com a faixa "Alterado depois do envio: reenvie ao fornecedor." e o `NextSteps` "Reenviar ao fornecedor" (CMP-03b).
16. **Dado** `purchasing.edit` e um pedido `Draft`, `PendingApproval`, `Approved` ou `Sent` **sem recebimento**, **quando** clico "Cancelar pedido" com motivo e confirmo no diálogo com o impacto ("Cancela {n} títulos previstos de {total} no fluxo de caixa"), **então** na mesma transação o pedido fica `Cancelled`, os títulos são cancelados por `IPayables.CancelAllFromOriginAsync(Purchase, pedidoId, motivo)` (409 se algum estiver em OP ou pago) e as linhas de SC voltam a `Open`; `PurchaseOrderCancelled` vai para o outbox só para aviso; pedido com recebimento não é cancelado (409 "Já houve recebimento: encerre o saldo do pedido.", encerramento na CMP-04).

**`IPayables` por parcela (ADR 0024 §2)**
17. **Dado** a migração do Finance, **quando** roda, **então** `finance.payables` ganha `installment` (`smallint`, 1 para os títulos de hoje), o índice único `(origin, origin_id, installment)` é criado **antes** de remover o índice atual `(origin, origin_id)`, sem nada destrutivo, e a versão anterior do código continua funcionando entre os dois passos; `CreateFromOriginAsync`, `ConfirmAsync`, `UpdatePlannedAsync`, `CancelFromOriginAsync` e `FindByOriginAsync` continuam valendo com `installment = 1`.
18. **Dado** uma cópia do banco do tenant `konebras`, **quando** a migração roda, **então** o teste de regressão confirma que os títulos de origem já existentes (instalador da COM-06, recorrentes e manuais da FIN-02) ficam com `installment = 1`, sem duplicar, com os mesmos valores, situações e vencimentos, e que a criação idempotente e a busca por origem devolvem o mesmo título de antes.
19. **Dado** `CreatePlannedInstallmentsAsync`, `ReplacePlannedAsync` e `CancelAllFromOriginAsync`, **quando** são chamados, **então** conta e natureza vêm como parâmetros (as origens antigas mantêm a conta e a natureza de hoje); os padrões do pedido ficam em `purchasing.parameters`: conta `2.1 Materiais` e natureza `Material`; frete `2.4 Fretes` e natureza `Freight` (ver Pontos 2).

**Consulta, permissões e auditoria**
20. **Dado** `purchasing.view`, **quando** abro "Pedidos" (`/app/compras/pedidos`), **então** vejo número, fornecedor, destino, total, previsão de entrega e situação (Rascunho neutro, Aguardando aprovação info, Aprovado ok, Enviado ok, Parcialmente recebido info, Recebido ok, Cancelado neutro); para quem aprova, o filtro padrão é "Aguardando você", com só os pedidos que o usuário pode decidir agora; atraso de entrega (previsão < hoje e não recebido) com faixa lateral vermelha e o texto "Atrasado {n} d".
21. **Dado** o módulo `purchasing` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; criar sem `purchasing.create`, editar/cancelar sem `purchasing.edit`, aprovar sem `purchasing.approve` (e fora da alçada): 403. `RequireModule` e `perm:`; IDOR em `/purchase-orders/{id}/lines/{lineId}`; fornecedor, projeto e condição validados pelos Contracts dos donos; anexos do pedido pelo dono `PurchaseOrder` (leitura `purchasing.view`, escrita `purchasing.edit`, pdf, png e jpg até 10 MB, `OwnerExists`; adendo à ADR 0019 §7).
22. **[Segurança]** **Dado** aprovação e edição, ou duas aprovações, simultâneas, **quando** chegam, **então** só uma grava (`xmin`), e os títulos não duplicam (idempotência por parcela no `IPayables`).
23. **Dado** qualquer alteração ou decisão, **quando** abro "Histórico de alterações" e "Decisões", **então** vejo antes/depois, usuário, data, a tentativa, o nível, a regra aplicada, o valor de referência, o aviso de fracionamento e a marca "Autoaprovado" quando houver.

**Saldo a receber por item (contrato de leitura)**
24. **Dado** o contrato `IPurchaseOrders.OpenQuantityByItemAsync(itemIds, destination)` em `Purchasing.Contracts`, **quando** outro módulo consulta, **então** recebe, por item, o saldo a receber na unidade de estoque (quantidade pedida − recebida, convertida pelo `IUnitConversion`) dos pedidos `Approved`, `Sent` e `PartiallyReceived`; pedido em rascunho, aguardando aprovação, cancelado, recebido ou com o saldo encerrado não entra; `destination` filtra "Estoque geral", um projeto ou todos. É leitura, sem permissão própria (quem chama confere a sua). Nesta story o contrato passa a alimentar o job do mínimo da CMP-01 (CA 9 dela, com destino "Estoque geral") e fica disponível para a EST-06.

## Regras de negócio e técnicas
- **Tabelas:** `purchase_orders` (+ `freight`, `ipi_pct`, `ipi_amount`, `subtotal`, `quotes_count`, `requested_by`, `submitted_at`, `approved_at`, `attempt`, `changed_after_sent`, `cancel_reason`, `cancelled_at`, `cancelled_by`, `xmin`); `purchase_order_lines` (+ `request_line_ids` por vínculo, `cost_type`, `quantity_received` da CMP-04); `purchase_order_levels` e `purchase_order_approvals` (espelho de `payment_order_levels`/`payment_order_approvals`, por tentativa, imutáveis depois de decididos).
- **Alçada:** mesma semântica da FIN-03b (faixa, multinível, valor de referência, alçada viável). O Purchasing não lê tabela do Finance: resolve pelo `IApprovalRouting` e guarda os próprios níveis e aprovações; o Finance só resolve a regra e confere a permissão do tipo. As regras de `PurchaseOrder` são editadas na tela de Alçadas do Financeiro, com um seletor de tipo de documento.
- **Previsão:** a soma das parcelas fecha com o total (`PaymentTermSplitter`, a última absorve a diferença); data estimada = data prevista de entrega + dias da parcela (ADR 0019 §17). O frete e o IPI do próprio fornecedor vão nas mesmas parcelas do pedido (um título por parcela; no estoque eles entram rateados no custo de entrada, P-16).
- **Comprometido (ADR 0025 §1):** previstos com projeto e títulos de compra de item estocável com projeto ficam fora do realizado; `IPurchaseOrders` expõe o comprometido por projeto para a PRD-04.
- **Nunca apagar:** pedido com efeito (aprovado) só é cancelado; os títulos são cancelados pela origem.
- Guid v7; numeração `PC`; `xmin`; auditoria `IAuditable`; dinheiro `numeric(18,2)`, preço `numeric(18,6)`, quantidade `numeric(18,4)`.

## Permissões, eventos e tabelas
- **Permissões:** `purchasing.view`, `purchasing.create`, `purchasing.edit`, `purchasing.approve` (reservada ao Proprietário para conceder, ADR 0008).
- **Eventos (`Purchasing.Contracts`, só aviso e projeção):** `PurchaseOrderSubmitted`, `PurchaseOrderLevelReleased`, `PurchaseOrderApproved`, `PurchaseOrderRejected`, `PurchaseOrderChanged`, `PurchaseOrderCancelled`. Nenhum consumidor do Finance cria ou muda título por evento.
- **Contratos:** `IApprovalRouting` (novo, `Finance.Contracts`: `ResolveAsync`, `EligibleAsync`, sobre o `ApprovalResolver` da FIN-03b); `IPayables` com `installment`, conta e natureza como parâmetros e os métodos `CreatePlannedInstallmentsAsync`, `ReplacePlannedAsync`, `CancelAllFromOriginAsync`; `SoleOwnerRule` (BuildingBlocks); novo `IPurchaseOrders` em `Purchasing.Contracts` (situação, saldo a receber por item com `OpenQuantityByItemAsync`, CA 24, comprometido por projeto).
- **Tabelas:** `purchasing.purchase_orders`, `purchase_order_lines`, `purchase_order_levels`, `purchase_order_approvals`; `finance.payables.installment` e o índice novo; seed da regra padrão de `PurchaseOrder` em `finance.approval_rules`.

## Telas e estados
- Padrões de tela da Fase 3: 2, 4, 5, 6.
- **Fábrica → Compras → Pedidos** (`/app/compras/pedidos`, detalhe `/app/compras/pedidos/$id`): cabeçalho com número, chip, fornecedor e destino; "facts" (total, previsão de entrega, condição, recebido); abas na URL: Itens, Decisões, Financeiro (títulos previstos, com `finance.view`), Documentos, Histórico de alterações.
- **Verbos:** "Novo pedido" / "Pedido criado"; "Enviar para aprovação" / "Pedido enviado para aprovação"; "Aprovar pedido" / "Pedido aprovado"; "Reprovar pedido" / "Pedido reprovado"; "Cancelar pedido" / "Pedido cancelado".
- **Diálogo de aprovação** (`size="lg"`) com o impacto: total, parcelas previstas e datas, saldo projetado do caixa no dia de cada parcela (FIN-05, com `finance.view`), aviso de fracionamento e aviso "1 cotação" (CA 11).
- **Estados:** carregando; vazio ("Nenhum pedido." + "Novo pedido" e "Ver solicitações"); erro; modo leitura com a faixa do estado.
- **Propositivo:** padrões do CA 1; depois de aprovado, `NextSteps` "Ver previsão no fluxo de caixa", "Registrar recebimento" (CMP-04) e "Enviar ao fornecedor" (CMP-03b, quando entregue).
- **400 px:** lista com número, fornecedor, total e situação; itens em cartões; diálogo de aprovação em tela cheia.
- **Ajuda:** `Ajuda/compras-pedidos.md`, `Ajuda/compras-pedido.md` (abas `itens` padrão, `decisoes`, `financeiro`, `documentos`, `historico`); a `Ajuda/financeiro-alcadas.md` ganha "Alçadas de pedido de compra"; `<Dica>` em "Valor de referência", "Estimado" e "Comprometido".

## Plano de testes
- **Unidade:** totais (CA 3); parcelas com a última absorvendo a diferença e datas estimadas (CA 12); valor de referência e fracionamento (CA 5, 6); mapa tipo de documento → permissão (CA 10).
- **Integração (Testcontainers, obrigatória: gera CP):** criação manual e da SC com idempotência e padrões (CA 1); criação pela API com `rfqId` (CA 2); projeto e natureza (CA 4); alçada com o motor da FIN-03b (casos A–D da FIN-03b adaptados para `PurchaseOrder`), fracionamento sobre os pedidos, alçada viável e Proprietário único com `SoleOwnerRule` (CA 5–7, 9); e-mail só com número, fornecedor, total e link (CA 8); `finance.approve` sem `purchasing.approve` recebe 403 (CA 10); aviso "1 cotação" (CA 11); aprovação gera N títulos `Planned` estimados na mesma transação, rollback se o `IPayables` falha (CA 12); Custos do projeto sem contar o pedido como realizado (CA 13); reprovação e reenvio com nova resolução (CA 14); editar pedido enviado volta à aprovação, troca as parcelas e pede reenvio; título em OP recusa (CA 15); cancelamento cancela os títulos (CA 16); migração do índice em dois passos (CA 17); regressão sobre cópia do banco do tenant `konebras` (CA 18); conta e natureza por parâmetro (CA 19); três camadas, IDOR e dono de anexo (CA 21); concorrência (CA 22); auditoria e decisões (CA 23); saldo a receber por item, por situação, destino e conversão de unidade, e o job do mínimo da CMP-01 descontando esse saldo (CA 24).
- **Arquitetura:** Purchasing só referencia `Finance.Contracts`, `Inventory.Contracts`, `Parties.Contracts`, `Commercial.Contracts`; nada do Purchasing lê `finance.*`; o `IApprovalRouting` não duplica a regra do `ApprovalResolver`.
- **E2E (Playwright):** criar pedido a partir da SC, enviar, aprovar pelo link do e-mail depois do login e ver as parcelas "Estimado" no fluxo de caixa.

## Fora do escopo
- Tela "Gerar pedidos de compra" a partir da cotação e "Enviar todos para aprovação": CMP-02 (usa a API do CA 2).
- PDF e envio ao fornecedor: CMP-03b. Recebimento, títulos do recebimento e replanejamento do saldo: CMP-04. NF de entrada: Fase 5.
- Aprovação em lote (R-PRP-6) e aprovador substituto: fora do MVP.

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fase 3, tabela 1.1, itens 38–44):
1. **Alçada do pedido (38):** `IApprovalRouting` em `Finance.Contracts`, parametrizado por tipo de documento; regras em `finance.approval_rules`; níveis e aprovações no Purchasing (ADR 0024 §4; CA 5, 9, 10).
2. **Conta e natureza (39):** padrão `2.1 Materiais` / natureza `Material`, frete `2.4 Fretes` / `Freight`, como parâmetros (CA 19).
3. **Um título por parcela (40):** `installment` e índice `(origin, origin_id, installment)` (ADR 0024 §2; CA 17, 18).
4. **Contar duas vezes (41):** D2; compra para o projeto como "Comprometido" (ADR 0025 §1; CA 13).
5. **Fracionamento (42):** valor de referência pelos pedidos do fornecedor em 30 dias, vindo do Purchasing (CA 5, 6).
6. **Reprovação (43):** volta a rascunho, histórico imutável, nova resolução no reenvio (CA 14).
7. **"Proprietário aprova tudo" (44):** regra padrão própria do tipo `PurchaseOrder` no seed (CA 5).

**Pré-condição externa:** conferir a conta e a natureza do título de compra com a contadora (P-17, DRE por competência; a compra para estoque pode ir para conta de estoque fora do DRE). Não bloqueia: o padrão está definido e é parâmetro.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers), arquitetura e E2E passando.
- [ ] ADR 0024 aplicada (§1, §2, §4); migration criada (não destrutiva, índice novo antes de remover o antigo), testada sobre cópia do banco do tenant `konebras` com a regressão do CA 18; seed da regra padrão idempotente.
- [ ] Permissões só do `ModuleCatalog`; dono de anexo `PurchaseOrder` registrado; template de e-mail registrado.
- [ ] Telas com estados, inclusive a 400 px.
- [ ] Ajuda da lista e de cada aba (e a das alçadas atualizada); teste de cobertura passando.
- [ ] Confirmação com impacto no caixa, alertas acionáveis e próximos passos aplicados.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` e `docs/04-fluxos-estados.md` (§2: previsto síncrono) atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `PurchaseOrder`, `PurchaseOrderLine`, níveis e decisões por tentativa (Domain) com a máquina de estados.
- [ ] `IApprovalRouting` no Finance sobre o `ApprovalResolver` (mapa tipo → permissão) e o seletor de tipo na tela de Alçadas; `SoleOwnerRule` usado na autoaprovação.
- [ ] `installment`, índice em dois passos e os métodos novos do `IPayables`; teste de regressão sobre cópia do `konebras`.
- [ ] `IPurchaseOrders` em `Purchasing.Contracts` (comprometido por projeto, `OpenQuantityByItemAsync`); ligar o saldo a receber no `MinStockPurchaseRequestJob` da CMP-01.
- [ ] Endpoints `/api/v1/purchasing/purchase-orders`; telas, e-mail, ajudas.

## Quality gates
- Agentes previstos: @dev, @architect (alçada entre módulos, `IPayables` por parcela, migração do índice), @data-engineer (regressão sobre a cópia do `konebras`), cybersecurity (segregação, fracionamento, permissão do tipo, link do e-mail), @qa.
- Gate: CA 5, 6, 7, 9, 10, 12, 13, 15, 16, 17, 18 e 22 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fase 3).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK (ajustada: o pedido nasce da SC ou à mão) |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK (renumerados, 23) |
| 5 | CAs de segurança e de permissão (3 camadas) | OK (ajustado: CA 6, 10, 15) |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: antes da CMP-02) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK |

- **Correções feitas na validação (21/10/2026, primeira validação):** quebra da CMP-03 (PDF e envio na CMP-03b); alçada reaproveitando o motor da FIN-03b por contrato, sem ler tabela do Finance (ponto 1, pede ADR); um título por parcela (ponto 3) em vez de um título pelo total; risco de contar a compra duas vezes no custo do projeto registrado (CA 12, ponto 4), sem decidir.
- **Correções da revalidação (21/10/2026):** ADR 0024 aplicada: previsto por chamada síncrona na transação da aprovação, sem consumidor no Finance (CA 12, 15, 16); `installment`, índice em dois passos e regressão sobre o `konebras` (CA 17, 18); conta e natureza como parâmetros (CA 19); `IApprovalRouting` com mapa tipo → permissão e teste de 403 (CA 5, 9, 10); fracionamento sobre os pedidos (CA 6); reprovação com histórico por tentativa (CA 14); edição de pedido enviado volta à aprovação e pede reenvio (CA 15); "Comprometido" pela D2/ADR 0025 (CA 13); nova ordem: pedido manual ou da SC, API para a CMP-02 e aviso "1 cotação" (CA 1, 2, 11); dono de anexo `PurchaseOrder`; padrões de tela 2, 4, 5, 6. CAs renumerados. Saldo a receber por item (`OpenQuantityByItemAsync`, CA 24) para o job do mínimo da CMP-01 e a EST-06.
- **Pré-condições para `Ready`:** nenhuma de decisão de story. Pré-condição externa explícita: conta e natureza com a contadora (P-17), sem bloquear.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da CMP-03) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO (D2, D8, tabela 1.1 itens 38–44) e correções da revisão pré-Fase 3 (§2.1 Compras ↔ Financeiro, §2.2 CMP-03a, §2.3 padrões 2, 4, 5, 6); ADR 0024 §1, §2, §4; nova ordem (Marco 3.3, antes da CMP-02); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
