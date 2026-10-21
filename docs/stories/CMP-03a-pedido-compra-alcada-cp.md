# CMP-03a — Pedido de compra: criação, aprovação por alçada e CP previsto

| Campo | Valor |
|---|---|
| ID | CMP-03a |
| Título | Pedido de compra a partir da cotação, da solicitação ou manual, vinculado a projeto ou ao estoque geral; envio para aprovação com alçada (motor da FIN-03b para `PurchaseOrder`) e segregação solicitante × aprovador; na aprovação, os títulos a pagar previstos (`Planned`, data estimada) por parcela da condição entram no fluxo de caixa; cancelamento antes de receber cancela a previsão |
| Tamanho | G (quebra da CMP-03: o PDF e o envio ao fornecedor foram para a CMP-03b) |
| Status | Draft |
| Requisitos | R-CMP-3 (pedido com aprovação por alçada) · R-CMP-4 (pedido gera previsão de CP no fluxo de caixa) · R-CMP-6 (projeto ou estoque geral) · R-FIN-4 (CP originado de compras) · R-FIN-10 (pedidos de compra no projetado) · R-FIN-5 (alçadas) · R-COM-4 (numeração PC) · R-COM-2 · R-PRP-1 · R-PRP-7 · R-SEC-5 |
| Dependências | CMP-02 (cotação concluída), CMP-01 (SC), FIN-03b (motor de alçadas, `approval_rules.document_type` reservado para `PurchaseOrder`), FIN-02 (`IPayables`), FIN-05 (fluxo de caixa com `Planned` estimado), CAD-05b (`IPaymentTerms`), FND-09e |
| Bloqueia | CMP-03b (PDF e envio), CMP-04 (recebimento contra o pedido), EST-04 (lote do fornecedor) |
| Ordem de execução | 10 (Marco 3.3 "Comprar o que falta") |

## User story
Como **comprador**, quero **gerar o pedido a partir da cotação e mandá-lo para aprovação**, e como **proprietário**, quero **aprovar dentro da alçada e já ver o pagamento previsto no fluxo de caixa**, para **nenhuma compra sair sem aprovação e o caixa mostrar o compromisso antes da nota chegar**.

## Contexto e referências
- PRD §5.10 R-CMP-3, R-CMP-4, R-CMP-6; §5.11 R-FIN-4, R-FIN-5, R-FIN-10 ("base = títulos em aberto + recorrentes + pedidos de compra + folha prevista").
- `04-fluxos-estados` §1 "Pedido de compra": `Draft → PendingApproval → Approved → Sent → PartiallyReceived → Received`; `Cancelled` antes de receber. §1 "Título a pagar": `Planned (previsão: pedido de compra/folha/instalador; pode ter data estimada) → Open (confirmado)`. §2: `PurchaseOrderApproved` → Finance cria payables `Planned`; `GoodsReceived` → Finance confirma (CMP-04). §3.6 (alçadas: regra mais específica, valor de referência contra fracionamento, multinível, solicitante não aprova o próprio, D6).
- `03-modelo-dados` → `purchasing.purchase_orders` (`number`, `party_id`, `rfq_id`, `project_id`, `status`, `payment_term_id`, `expected_date`, `total`), `purchase_order_lines` (`order_id`, `item_id`, `quantity`, `unit_price`, `quantity_received`); `finance.approval_rules.document_type` (PaymentOrder/**PurchaseOrder**, "reservado para a CMP-03", FIN-03b).
- **Código atual:** o motor de alçadas é interno do Finance (`ApprovalResolver`, `ApprovalRuleService`, ambos filtrando `PaymentOrder`); `IPayables.CreateFromOriginAsync` cria **um** título por `(origin, originId)`, com conta `3.9` e `CostType.Other` para qualquer origem que não seja `Installer`.
- ADR 0019 §17 (CP estimado com `is_estimate`, `Tag` "Estimado" no fluxo de caixa; confirmação grava o vencimento real), §3 (idempotência), §6 (alçada viável). ADR 0008 (`purchasing.approve` reservada ao Proprietário para conceder).
- Plano de contas do seed: `2.1 Materiais`, `2.4 Fretes`.

## Critérios de aceite
**Criar**
1. **Dado** uma cotação `Closed`, **quando** clico "Gerar pedidos de compra" (CMP-02 CA 7) com `purchasing.create`, **então** nasce um pedido `Draft` por fornecedor vencedor, com número `PC-2026-0001`, as linhas vencedoras (item, quantidade, preço unitário `numeric(18,6)`), frete e IPI do fornecedor, condição de pagamento, data prevista de entrega (hoje + prazo da resposta), o destino (projeto da SC quando todas as linhas são do mesmo projeto, senão "Estoque geral"; R-CMP-6) e o vínculo com a cotação e as linhas de SC, que passam a `Ordered`; com `Idempotency-Key`.
2. **Dado** `purchasing.create`, **quando** crio um pedido manual (sem cotação; a partir de linhas de SC ou de itens digitados), **então** o fornecedor vem sugerido pelo último fornecedor do item e o preço pelo último preço pago (R-PRP-1), com a `Tag` "Sugerido".
3. **Dado** um pedido `Draft`, **quando** edito, **então** o total = Σ linhas (qtd × preço, 2 casas `AwayFromZero`) + frete + IPI aparece ao vivo no rodapé, e o servidor recalcula tudo ao salvar (valores do cliente nunca são confiados); fornecedor sem papel Fornecedor, item inativo ou projeto fora de andamento: 422 no campo.
4. **Dado** o pedido com destino em projeto, **quando** grava, **então** guarda `project_id` e a natureza de custo por linha (proposta: Material para itens; Frete para o frete), validada por `IProjects`.

**Aprovação por alçada**
5. **Dado** um pedido `Draft` válido, **quando** clico "Enviar para aprovação", **então** passa a `PendingApproval`; a alçada é resolvida no envio pelo motor da FIN-03b com `document_type = PurchaseOrder` (pelo contrato novo `IApprovalRouting` em `Finance.Contracts`, Pontos em aberto 1), comparando o **valor de referência** (o maior entre o total do pedido e a soma dos pedidos não cancelados do mesmo fornecedor nos últimos 30 dias, incluindo este), e a resolução por nível fica gravada no pedido; sem regra de `PurchaseOrder`, vale a padrão "Proprietário aprova tudo" (seed, como a FIN-03b CA 1).
6. **[Segurança]** **Dado** um nível sem aprovador elegível (com `purchasing.approve` efetivo, diferente do solicitante e dos outros níveis), **quando** envio, **então** 422 com o motivo (ADR 0019 §6), e o pedido continua `Draft`.
7. **Dado** o envio, **quando** grava, **então** os aprovadores do nível 1 recebem a notificação e o e-mail "Pedido de compra {número} aguardando aprovação" **só com** número, fornecedor, total e o link autenticado para o pedido (mesma regra da FIN-03b CA 16 e 17), pelo outbox (`PurchaseOrderSubmitted`).
8. **[Segurança]** **Dado** um aprovador, **quando** decide, **então**: o solicitante não aprova o próprio pedido (exceção do Proprietário único, D6); quem está fora da regra do nível recebe 403 `outside_approval_rule` "Este pedido não está na sua alçada.", mesmo com `purchasing.approve`; o mesmo aprovador não decide dois níveis; todas as aprovações do nível N antes do N+1 (`04-fluxos` §3.6).
9. **Dado** a aprovação do último nível, **quando** grava, **então** na mesma transação o pedido fica `Approved` e o evento `PurchaseOrderApproved` (`{ purchaseOrderId, partyId, projectId, total, paymentTermId, expectedDate }`) vai para o outbox; a mensagem diz "Pedido aprovado. Lançando a previsão no caixa…".
10. **Dado** "Reprovar" com comentário obrigatório, **quando** grava, **então** o pedido volta a `Draft` com o comentário e o solicitante é notificado (proposta: volta a rascunho para corrigir; Pontos em aberto 6).

**Previsão no caixa (CP `Planned`)**
11. **Dado** o `PurchaseOrderApproved`, **quando** o consumidor do Finance processa, **então** cria um título a pagar `Planned` por parcela da condição de pagamento (`IPaymentTerms.SplitAsync` sobre o total, a partir da data prevista de entrega), com `is_estimate = true`, origem `Purchase`, fornecedor, projeto (quando houver), conta `2.1 Materiais` e natureza `Material` (proposta, Pontos em aberto 2), descrição "Pedido {número} — parcela {n}/{m}"; idempotente por `(Purchase, pedidoId, parcela)` (extensão do `IPayables`, Pontos em aberto 3); os títulos aparecem no fluxo de caixa com a `Tag` "Estimado" (FIN-05).
12. **Dado** um pedido aprovado com projeto, **quando** abro o Projeto 360 → Custos (COM-05), **então** o valor do pedido **não** entra no realizado de Material como título a pagar (para não contar duas vezes com a baixa do estoque, EST-03), e aparece na coluna "Comprometido" (proposta, Pontos em aberto 4).

**Mudanças e cancelamento**
13. **Dado** um pedido `Approved` ainda sem recebimento, **quando** edito quantidade, preço, frete ou condição, **então** ele volta a `PendingApproval` (nova resolução de alçada) e, na reaprovação, o Finance atualiza os títulos `Planned` (`IPayables.UpdatePlannedAsync` por parcela; parcelas a mais criadas e a menos canceladas); título já em OP recusa (409 `payable_in_payment_order`, que o diálogo mostra com o link).
14. **Dado** `purchasing.edit` e um pedido `Draft`, `PendingApproval`, `Approved` ou `Sent` **sem recebimento**, **quando** clico "Cancelar pedido" com motivo e confirmo no diálogo com o impacto ("Cancela {n} títulos previstos de {total} no fluxo de caixa"), **então** o pedido fica `Cancelled`, o evento `PurchaseOrderCancelled` cancela os títulos `Planned` (`IPayables.CancelFromOriginAsync`), e as linhas de SC voltam a `Open`; pedido com recebimento não é cancelado (409 "Já houve recebimento: encerre o saldo do pedido." — encerramento do saldo na CMP-04).

**Consulta, permissões e auditoria**
15. **Dado** `purchasing.view`, **quando** abro "Pedidos" (`/app/compras/pedidos`), **então** vejo número, fornecedor, destino, total, previsão de entrega, situação (Rascunho neutro, Aguardando aprovação info, Aprovado ok, Enviado ok, Parcialmente recebido info, Recebido ok, Cancelado neutro) e "Aguardando você" com só os pedidos que o usuário pode aprovar; atraso de entrega (previsão < hoje e não recebido) com faixa lateral vermelha.
16. **Dado** o módulo `purchasing` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; criar sem `purchasing.create`, editar/cancelar sem `purchasing.edit`, aprovar sem `purchasing.approve` (e fora da alçada): 403. `RequireModule` e `perm:`; IDOR em `/purchase-orders/{id}/lines/{lineId}`; fornecedor, projeto e condição validados pelos Contracts dos donos.
17. **[Segurança]** **Dado** aprovação e edição, ou duas aprovações, simultâneas, **quando** chegam, **então** só uma grava (`xmin`), e os títulos não duplicam (consumidor idempotente pela inbox e por parcela).
18. **Dado** qualquer alteração ou decisão, **quando** abro "Histórico de alterações" e "Decisões", **então** vejo antes/depois, usuário, data, o nível, a regra aplicada, o valor de referência e o aviso de fracionamento.

## Regras de negócio e técnicas
- **Tabelas** (03 + proposta): `purchase_orders` (+ `freight`, `ipi_pct`, `ipi_amount`, `subtotal`, `requested_by`, `submitted_at`, `approved_at`, `cancel_reason`, `cancelled_at`, `cancelled_by`, `xmin`); `purchase_order_lines` (+ `request_line_ids` por vínculo, `cost_type`, `quantity_received` da CMP-04); `purchase_order_levels` e `purchase_order_approvals` (espelho de `payment_order_levels`/`payment_order_approvals`, propostas).
- **Alçada:** mesma semântica da FIN-03b (faixa, conta não se aplica ao pedido, multinível, valor de referência, alçada viável); o Purchasing não lê tabela do Finance (`IApprovalRouting` em `Finance.Contracts`, Pontos em aberto 1). As regras de `PurchaseOrder` são editadas na tela de Alçadas do Financeiro, com um seletor de tipo de documento.
- **Previsão:** a soma das parcelas fecha com o total (`PaymentTermSplitter`, a última absorve a diferença); data estimada = data prevista de entrega + dias da parcela (como a ADR 0019 §17 faz com o instalador).
- **Nunca apagar:** pedido com efeito (aprovado) só é cancelado; os títulos são cancelados pela origem.
- Guid v7; numeração `PC`; `xmin`; auditoria `IAuditable`; dinheiro `numeric(18,2)`, preço `numeric(18,6)`, quantidade `numeric(18,4)`.

## Permissões, eventos e tabelas
- **Permissões:** `purchasing.view`, `purchasing.create`, `purchasing.edit`, `purchasing.approve` (reservada ao Proprietário para conceder).
- **Eventos (`Purchasing.Contracts`):** `PurchaseOrderSubmitted`, `PurchaseOrderLevelReleased`, `PurchaseOrderApproved`, `PurchaseOrderChanged` (reaprovação com valores novos), `PurchaseOrderCancelled`. **Consumidor no Finance:** cria, atualiza e cancela os títulos `Planned`.
- **Contratos:** `IApprovalRouting` (novo, `Finance.Contracts`: resolver, conferir elegibilidade e registrar o nível para `PurchaseOrder`); `IPayables` estendido por parcela, conta e natureza (Pontos em aberto 3); novo `IPurchaseOrders` em `Purchasing.Contracts` (situação, saldo a receber, comprometido por projeto).
- **Tabelas:** `purchasing.purchase_orders`, `purchase_order_lines`, `purchase_order_levels`, `purchase_order_approvals`; seed da regra padrão de `PurchaseOrder` em `finance.approval_rules`.

## Telas e estados
- **Fábrica → Compras → Pedidos** (`/app/compras/pedidos`, detalhe `/app/compras/pedidos/$id`): cabeçalho com número, chip, fornecedor e destino; "facts" (total, previsão de entrega, condição, recebido); abas na URL: Itens, Decisões, Financeiro (títulos previstos, com `finance.view`), Documentos, Histórico de alterações.
- **Verbos:** "Gerar pedidos de compra" / "Pedidos gerados"; "Enviar para aprovação" / "Pedido enviado para aprovação"; "Aprovar pedido" / "Pedido aprovado"; "Reprovar pedido" / "Pedido reprovado"; "Cancelar pedido" / "Pedido cancelado".
- **Diálogo de aprovação** (`size="lg"`) com o impacto: total, parcelas previstas e datas, saldo projetado do caixa no dia de cada parcela (pelo `FIN-05`, com `finance.view`), aviso de fracionamento.
- **Estados:** carregando; vazio ("Nenhum pedido." + "Cotar itens" e "Novo pedido"); erro; modo leitura com a faixa do estado; processamento assíncrono da previsão ("Lançando a previsão no caixa…", depois de cerca de 30 s o estado de demora).
- **Propositivo:** padrões do CA 1 e 2; depois de aprovado, `NextSteps` "Enviar ao fornecedor" (CMP-03b) e "Ver previsão no fluxo de caixa".
- **400 px:** lista com número, fornecedor, total e situação; itens em cartões; diálogo de aprovação em tela cheia.
- **Ajuda:** `Ajuda/compras-pedidos.md`, `Ajuda/compras-pedido.md` (abas `itens` padrão, `decisoes`, `financeiro`, `documentos`, `historico`); a `Ajuda/financeiro-alcadas.md` ganha "Alçadas de pedido de compra"; `<Dica>` em "Valor de referência", "Estimado" e "Comprometido".

## Plano de testes
- **Unidade:** totais (CA 3); parcelas com a última absorvendo a diferença e datas estimadas (CA 11); valor de referência e fracionamento (CA 5).
- **Integração (Testcontainers, obrigatória: gera CP):** gerar a partir da cotação com idempotência (CA 1); padrões (CA 2); projeto e natureza (CA 4); alçada com o motor da FIN-03b (casos A–D da FIN-03b adaptados para `PurchaseOrder`), alçada viável e um Proprietário só (CA 5, 6); e-mail só com número, fornecedor, total e link (CA 7); segregação e fora da alçada (CA 8); aprovação gera N títulos `Planned` estimados no fluxo de caixa, sem duplicar em reentrega (CA 9, 11, 17); Custos do projeto sem contar o pedido como realizado (CA 12); reaprovação atualiza parcelas, título em OP recusa (CA 13); cancelamento cancela os títulos (CA 14); três camadas e IDOR (CA 16); auditoria e decisões (CA 18).
- **Arquitetura:** Purchasing só referencia `Finance.Contracts`, `Inventory.Contracts`, `Parties.Contracts`, `Commercial.Contracts`; nada do Purchasing lê `finance.*`.
- **E2E (Playwright):** gerar pedido da cotação, enviar, aprovar pelo link do e-mail depois do login e ver as parcelas "Estimado" no fluxo de caixa.

## Fora do escopo
- PDF e envio ao fornecedor: CMP-03b. Recebimento e confirmação do CP: CMP-04. NF de entrada: Fase 5.
- Aprovação em lote (R-PRP-6) e aprovador substituto: fora do MVP.

## Pontos em aberto
1. **Motor de alçada para o pedido** (arquiteto/PO): proposta: o Finance expõe `IApprovalRouting` em `Finance.Contracts` (resolver para `PurchaseOrder`, elegibilidade, gravação dos níveis no Purchasing) e as regras ficam em `finance.approval_rules`, como a FIN-03b reservou; alternativa: tabela de alçadas própria do Purchasing. Pede ADR.
2. **Conta e natureza do título do pedido** (PO/contadora): o `IPayables` grava conta `3.9` e natureza `Other` para origens que não são de instalador. Proposta: `2.1 Materiais` e natureza `Material`, e `2.4 Fretes` para a parcela do frete quando o PO quiser separar; com a contadora, a compra para estoque pode ir para conta de estoque fora do DRE (P-17: DRE por competência).
3. **Um título por parcela** (arquiteto): o `IPayables` é idempotente por `(origin, originId)` e cria um título só. Proposta: `CreateInstallmentsFromOriginAsync(origin, originId, parcelas, conta, natureza, …)`, idempotente por `(origin, originId, parcela)`, e as variantes de confirmar, atualizar e cancelar por parcela.
4. **Contar duas vezes no custo do projeto** (PO): compra para projeto vira título a pagar com projeto (que a COM-05 conta como realizado) e o material, ao ser baixado para a OS (EST-03), também. Proposta: título de origem `Purchase` de item estocável **não** conta como realizado (aparece como "Comprometido"); o realizado de material vem só da baixa para a OS e da saída avulsa com projeto; serviço ou material entregue direto na obra (sem entrada em estoque) fica fora do MVP ou entra como título manual com projeto. Alternativa: contar o título e não contar a baixa.
5. **Fracionamento e mínimo de cotações** (PO): proposta: o valor de referência do CA 5 (como a FIN-03b); o mínimo de cotações da CMP-02 (ponto 2) não entra.
6. **Reprovação** (PO): proposta: volta a `Draft` com o comentário (CA 10); alternativa: estado `Rejected` final, como a OP.
7. **Seed da regra padrão** (PO): proposta: "Proprietário aprova tudo" também para `PurchaseOrder`.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers), arquitetura e E2E passando.
- [ ] Migration criada (não destrutiva), testada sobre cópia do banco do tenant `konebras`; seed da regra padrão idempotente.
- [ ] Permissões só do `ModuleCatalog`; template de e-mail registrado.
- [ ] Telas com estados, inclusive a 400 px.
- [ ] Ajuda da lista e de cada aba (e a das alçadas atualizada); teste de cobertura passando.
- [ ] Confirmação com impacto no caixa, alertas acionáveis e próximos passos aplicados.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] ADR do ponto 1 registrada; `docs/03-modelo-dados.md` e `docs/04-fluxos-estados.md` atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `PurchaseOrder`, `PurchaseOrderLine`, níveis e decisões (Domain) com a máquina de estados.
- [ ] `IApprovalRouting` no Finance (reaproveitando o `ApprovalResolver` sem duplicar a regra) e o seletor de tipo na tela de Alçadas.
- [ ] Extensão do `IPayables` por parcela, conta e natureza; consumidor `PurchaseOrderApproved/Changed/Cancelled` no Finance.
- [ ] `IPurchaseOrders` em `Purchasing.Contracts`; "Comprometido" na aba Custos do projeto.
- [ ] Endpoints `/api/v1/purchasing/purchase-orders`; telas, e-mail, ajudas.

## Quality gates
- Agentes previstos: @dev, @architect (alçada entre módulos e `IPayables` por parcela), cybersecurity (segregação, alçada e link do e-mail), @qa.
- Gate: CA 5, 6, 8, 11, 12, 14 e 17 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026.

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

- **Correções feitas na validação:** quebra da CMP-03 (PDF e envio na CMP-03b); alçada reaproveitando o motor da FIN-03b por contrato, sem ler tabela do Finance (ponto 1, pede ADR); um título por parcela (ponto 3) em vez de um título pelo total; risco de contar a compra duas vezes no custo do projeto registrado (CA 12, ponto 4), sem decidir.
- **Pré-condições para `Ready`:** pontos em aberto 1, 2, 3 e 4 (ADR do ponto 1).
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da CMP-03) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
