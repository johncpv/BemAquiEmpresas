# 04 — Fluxos, estados e regras transversais

## 1. Máquinas de estado

### Orçamento
```
Draft ─enviar─► Sent ─aprovar─► Approved (gera Contrato)
  │               ├─rejeitar─► Rejected (motivo obrigatório)
  │               ├─nova revisão─► Superseded (a nova revisão nasce Draft)
  │               └─validade vencida (job diário)─► Expired ─reabrir─► nova revisão
```
- Só a revisão mais recente pode ser aprovada. Aprovar exige validade não vencida (ou reabertura com nova revisão).
- Aprovar exige ao menos um marco de pagamento e soma dos percentuais = 100%.
- `Sent ─prorrogar validade─► Sent` (COM-02, D16 de 20/10/2026): muda só `valid_until`, auditado, sem nova revisão. Orçamento `Expired` não é prorrogado: reabre com nova revisão.
- Revisões mantêm o número; só a revisão corrente (`is_current`) é editável e aprovável (ADR 0016 adendo M7).

### Contrato
`Active → Completed` (projeto entregue e recebíveis quitados ou cancelados; avaliado no `ReceivableSettled` e no `ProjectDelivered`, o que vier por último) · `Completed → Active` (estorno de baixa de um título do contrato; o projeto volta a `Delivered`) · `Active → Cancelled` (exige motivo, guardado no contrato; cancela recebíveis em aberto sem baixa e OS não iniciadas; títulos com baixa e OS iniciadas viram decisão manual; projeto já `Delivered` não cancela). Contrato de implantação (COM-04c): marcos "Recebido antes da implantação" não geram título e contam como quitados. ADR 0019 §16.

### Projeto
`Planned → InProgress` (primeira etapa de OS iniciada) `→ Fabricated` (todas OS de fabricação não canceladas concluídas, PRD-06a1: o consumidor do `ProductionOrderCompleted` confere por `IProductionOrders.ByProjectAsync`, as OS de instalação não contam, grava `fabricated_on` = data da conclusão e `fabricated_by_orders` e publica `ProjectFabricated`; também `Planned → Fabricated` direto. Projeto já `Fabricated` à mão ou além não muda nem publica de novo. O registro manual "Registrar fabricação concluída" fica só para projeto sem OS de fabricação não cancelada: com OS, 409 `fabrication_by_orders` "A fabricação deste projeto é concluída pelas OS ({n} abertas)." e o botão some) `→ InProgress` (estorno da conclusão de uma OS, PRD-06a1, só quando `fabricated_by_orders`; `fabricated_on` volta a nulo) `→ Installing` (OS de instalação iniciada) `→ Delivered` (entrega técnica aceita em todas as OS de instalação não canceladas; venda sem instalação: "Registrar entrega" manual, com `projects.approve`, só sem OS de instalação aberta, D15) `→ Closed` (financeiro quitado). `Closed → Delivered` quando uma baixa é estornada. `Cancelled` a partir de qualquer estado antes de Delivered.
- Etapas exibidas no `StageIndicator` (COM-04a): P Planejado · E Em produção · F Fabricado · I Instalando · T Entregue · C Encerrado.

### OS de instalação (COM-06)
`Open ─iniciar─► InProgress ─aceite técnico─► Completed`; `Open → Cancelled` (`production.edit`, cancela o CP previsto do instalador); `InProgress → Cancelled` só com `production.approve` (o CP previsto fica para o financeiro decidir). Etapas sequenciais; a última ("Entrega técnica") só conclui pelo aceite, com o checklist completo. Sem `Paused` nem `Quality`.

### OS (fabricação)
```
Open ─(primeira etapa iniciada; não exige BOM)─► InProgress ◄─► Paused
InProgress ─(etapa de qualidade)─► Quality ─aprovar─► InProgress (próxima etapa) / ReadyToComplete (se última)
InProgress ─(última etapa concluída)─► ReadyToComplete ("Pronta para concluir")
Quality ─reprovar─► abre NC + volta etapa escolhida (InProgress)
ReadyToComplete ─concluir (production.approve, login completo)─► Completed
Completed ─estornar a conclusão─► InProgress
Cancelled: libera reservas; materiais já baixados exigem devolução ou apropriação ao projeto como perda
```
Etapa da OS: `Waiting → Ready (anterior concluída) → InProgress ↔ Paused → Done` ou `Rejected`. Etapa só inicia se a anterior estiver Done (sequencial no MVP).
- **Nascimento e edição (PRD-01):** a OS nasce `Open` com as etapas copiadas do roteiro sugerido (a primeira `Ready`, as demais `Waiting`) ou sem etapas ("Sem roteiro"). Enquanto a OS está `Open` e nenhuma etapa saiu de `Waiting`/`Ready`, as etapas são editáveis na OS e o roteiro pode ser trocado; depois, 409 "A OS já começou: as etapas não mudam.".
- **Cancelamento (PRD-01):** `Open` sem etapa iniciada → `Cancelled` com `production.edit`; OS iniciada → `Cancelled` só com `production.approve`. Publica `ProductionOrderCancelled` (`{ productionOrderId, projectId }`). O `ContractCancelled` cancela as OS de fabricação não iniciadas do projeto e avisa o responsável das iniciadas (handler próprio da fabricação).
- **Etapas sem BOM (D12, revisão pré-Fase 3, 21/10/2026):** a primeira etapa do roteiro inicia sem BOM (a BOM nasce na etapa Projeto); as seguintes exigem BOM liberada. O bloqueio liga junto com a ENG-02; antes dela (Marco 3.1) nenhuma etapa exige BOM. *Aplicado na ENG-02 (23/10/2026):* iniciar (PRD-03a, `/fab` e "Apontar por funcionário") ou concluir pelo supervisor ("Concluir etapa", PRD-02) uma etapa depois da primeira que ainda está `Ready` sem BOM `Released` responde 409 `bom_not_released` "A BOM desta OS ainda não foi liberada. Fale com a Engenharia."; no backoffice o próximo passo é "Abrir BOM da OS" (com `engineering.view`). A etapa já `InProgress` ou `Paused` no deploy não é interrompida. Com o módulo `engineering` não liberado no tenant, o bloqueio não vale (ninguém conseguiria liberar a BOM). A lista Engenharia → BOMs abre no filtro "OS que vão parar sem BOM".

### BOM da OS (ENG-02)
```
(sem BOM) ─Criar BOM / Copiar BOM─► Draft (revisão 0)
Draft ─Salvar BOM─► Draft (sem efeito em estoque)
Draft ─Descartar rascunho (engineering.delete)─► (apagada; a liberada anterior continua valendo)
Draft ─Liberar BOM (engineering.approve)─► Released + BomReleased
Released ─Revisar BOM (engineering.edit)─► Released (vigente) + Draft (revisão n + 1)
Draft (n + 1) ─Liberar revisão─► Released; a anterior ─► Superseded + BomRevised
```
- Uma `Released` e um `Draft` por OS (índices únicos parciais); a liberação supersede a anterior e publica o evento na mesma transação, com a necessidade da OS já na unidade de estoque. BOM sem linhas também é liberada (OS só de mão de obra, `lines` vazio).
- OS `Cancelled` ou `Completed`: criar, salvar, copiar, revisar e liberar respondem 409 "A OS {número} está {situação}: a BOM não muda.".
- Duas liberações juntas: uma grava, a outra recebe 409 (`xmin` e o índice parcial); o evento sai uma vez.
- **Conclusão explícita (D12):** aprovar a qualidade da última etapa, ou concluir a última etapa sem qualidade, deixa a OS em `ReadyToComplete`; a conclusão é uma ação própria, com `production.approve` e login completo (PRD-06a1). Na PRD-06a1 a conclusão não mexe no estoque; a partir da PRD-06a2 ela dá entrada no produto acabado com série, registra os lotes consumidos e libera as reservas restantes, na mesma transação (ADR 0023 §1).
- **Concluir OS (PRD-06a1):** `production.approve` (fora da sessão por PIN), OS `ReadyToComplete` sem apontamento aberto. Na mesma transação a OS vai a `Completed` (`actual_end`, `completed_by`) com a quantidade inteira (sem conclusão parcial) e `ProductionOrderCompleted` vai para o outbox. Fora de `ReadyToComplete`: 409 `production_order_not_ready` com o motivo ("Falta aprovar a Qualidade.", "Há apontamento aberto na etapa {etapa}.", "Falta concluir a etapa {etapa}.", "A OS foi cancelada."). Duas conclusões juntas: só uma grava (`xmin`). O impacto do diálogo vem de `GET /production-orders/{id}/completion` (última OS do projeto, parcelas "fabricação concluída" com valor e vencimento só com `finance.view`).
- **Estorno da conclusão (ponto 83 da revisão, PRD-06a1):** `Completed → InProgress` com motivo (até 500): a última etapa volta a `Ready` e a OS a `InProgress` (ou `Quality`, quando a última etapa é de qualidade); `ProductionOrderCompletionReversed` vai para o outbox; o Commercial volta o projeto de `Fabricated` para `InProgress` quando ele foi fabricado pelas OS e avisa quem tem `finance.edit` ("Conclusão da {OS} estornada. A parcela {marco} já liberada continua em aberto."); o título a receber já liberado não é estornado e o registro de gatilho fica (a reconclusão não libera de novo). Recusado (409 `fabrication_reversal_blocked`), conferido de forma síncrona no Commercial (`IProjects.FabricationAsync`, que lê `IReceivables.ForContractAsync`) antes de gravar: parcela "fabricação concluída" do projeto fabricado pelas OS com baixa total ou parcial ("A parcela {marco} já foi recebida. Fale com o Financeiro antes de estornar."; título a receber não entra em OP) ou projeto em `Installing`, `Delivered` ou `Closed` ("O projeto já passou da fabricação."). Na PRD-06a2 também reverte a entrada e as séries ainda não expedidas.
- **Quais itens geram OS (D14):** `Equipment` gera OS automática; `Product` por "Gerar OS" manual; `Service` nunca; contrato de implantação nunca. Uma OS por item do projeto, com a quantidade do item.
- **Kanban (D10):** sem arraste; "Concluir etapa" no menu do cartão com `production.approve`; colunas pelo código da etapa (a letra do roteiro).
- **"Concluir etapa" (PRD-02):** só a etapa atual, fora a de qualidade (decidida na PRD-05), com motivo (até 200); na mesma transação a etapa vai a `Done` ("Concluída pelo supervisor"), a próxima a `Ready` e, se a OS estava `Open`, ela vai a `InProgress` (com `actual_start`) e publica `ProductionStepStarted`. Etapa que não é a atual, OS cancelada ou concluída: 409 `step_not_current`; duas conclusões juntas: só uma grava (`xmin`). Depois da última etapa, a OS aparece em "Pronta para concluir" (o estado `ReadyToComplete` é da PRD-06a1).
- **Tempo real (PRD-02):** toda mudança na OS de fabricação ou na etapa (criação, etapas, troca de roteiro, cancelamento, "Concluir etapa") envia `OrderBoardChanged` (`{ productionOrderId }`) aos grupos `{tenantId}:production` e `{tenantId}:shopfloor` do `ProductionHub`, só depois do commit, também nos consumidores do outbox; rollback não envia nada. Mudou a permissão efetiva (`perm_version`) ou o security stamp, as conexões do usuário fecham depois do commit.
- **Apontamento (PRD-03a):** "Iniciar etapa" abre o apontamento do funcionário do usuário (um aberto por funcionário) numa etapa `Ready`, ou entra numa etapa em andamento ou pausada (vários operadores); a etapa fica `InProgress` e a OS sai de `Open` (com `ProductionStepStarted`). A etapa fica `Paused` quando ninguém trabalha nela (todos em pausa, ou todos saíram) e a OS fica `Paused` quando a etapa atual está pausada; "Retomar" volta as duas a `InProgress`. "Finalizar etapa" (ou "Concluir etapa para todos") exige o checklist completo, fecha os apontamentos (`LaborEntryClosed` por apontamento) e conclui a etapa; a próxima fica `Ready` e, se for de qualidade, a OS fica `Quality` ("Aguardando qualidade", decidida na PRD-05). Etapa que exige aprovação fica "Aguardando aprovação" (`InProgress` sem apontamento aberto) até "Aprovar etapa" com `production.approve`. A "Concluir etapa" do supervisor também fecha os apontamentos abertos ("Encerrado pelo supervisor"); quem apontou na etapa não corrige nem aprova, salvo sem outro aprovador elegível (`SoleOwnerRule`, "Autoaprovado" e aviso ao Proprietário). O servidor não restringe por centro de trabalho (D11).
- **Qualidade (PRD-05, D6, D12, pontos 76 a 79):** concluída a etapa anterior à de qualidade, a etapa de qualidade fica `Ready` e a OS `Quality` (também com a inspeção iniciada ou pausada); a fila (`/fab/qualidade` e Produção → Qualidade) é de quem tem `production.approve_quality` e abre em "Aguardando você". **Aprovar** exige todos os itens "Conforme" ou "Não se aplica": a etapa vai a `Done`, o apontamento da inspeção fecha, a próxima fica `Ready` e a OS volta a `InProgress`; se era a última, todas as etapas `Done` = "Pronta para concluir" (estado gravado `ReadyToComplete` desde a PRD-06a1), **nunca** `Completed`. Publica `QualityApproved`. Quem apontou qualquer etapa produtiva da tentativa (desde o início da OS, ou desde a última reprovação) não aprova (403 `quality_segregation`), salvo sem outro aprovador elegível: aprova com a marca "Autoinspeção" na etapa e na OS e o Proprietário é avisado. **Reprovar** (sem segregação), na mesma transação: nasce a NC `Open` (responsável padrão = quem reprovou), a etapa de qualidade vai a `Rejected`, a de retorno (uma anterior não de qualidade da mesma OS; padrão: a produtiva imediatamente anterior) fica `Ready`, as seguintes até a qualidade voltam a `Waiting` (com o checklist das produtivas desmarcado para o retrabalho), a tentativa da OS soma 1, as etapas de qualidade a partir da de retorno ganham o checklist zerado, a OS volta a `InProgress`, `QualityRejected` vai para o outbox e o responsável e quem tem `production.approve` recebem "NC {número} na {OS}: {descrição}. Voltou para {etapa}.". Refeito o retrabalho, a **mesma** etapa de qualidade (`Rejected → Ready`) volta na tentativa nova. Decisões concorrentes: só uma grava (`xmin` da OS). A `production.approve_quality` sozinha não conclui etapa, não corrige apontamento, não cancela nem conclui a OS e não libera parcela.
- **NC (PRD-05):** `Open ─"Iniciar ação" (causa e ação, production.edit)─► InAction ─"Encerrar NC" (resultado; causa e ação preenchidas)─► Closed`; `Open → Closed` direto também exige causa, ação e resultado (422 com os campos). Encerrada não muda nem recebe anexo. O custo estimado é informativo (fora do realizado do projeto: o retrabalho entra pelas horas e pelo material).
- **"OS atrasada" (PRD-02, R-COM-2):** job diário às 07:00 (São Paulo, `Jobs:Cron:production-late-orders`), por tenant com o módulo `production` liberado; avisa o responsável do projeto e quem tem `production.approve`, uma vez por OS e data prometida (`os-late:{id}:{prometido}`).

### Lançamento manual do estoque (EST-01a, D7, ADR 0023 §5)
`Posted` direto quando abaixo dos limites; senão `Pending → Posted | Rejected`, ou `Pending → Cancelled` pelo autor.
- **Pendente (CA 9):** entrada ou saída manual acima de `inventory.parameters.manual_approval_amount` (R$ 1.000,00) ou entrada com custo que desvia mais de `manual_cost_deviation_pct` (20%) do custo médio (só com médio > 0). Nasce com o número `MOV`, sem mexer no saldo nem no custo médio, e avisa quem tem `inventory.approve` menos o autor (`inventory-manual:{id}`).
- **Decisão (CA 10, 11):** outra pessoa com `inventory.approve` decide sobre o que foi enviado (campo a mais no corpo: 422); aprovar grava a decisão (xmin) e os movimentos pelo motor na mesma transação, com a saída pelo custo médio do momento da aprovação; reprovar exige comentário; o autor só aprova o próprio quando não há outro aprovador elegível (`SoleOwnerRule`, "Autoaprovado", aviso ao Proprietário).
- **Saldo inicial (CA 1 a 5):** só antes de `inventory.parameters.opening_completed_at` e sem nenhum movimento no item e local; grava em D 23:59:59 de São Paulo. "Concluir implantação do estoque" (`inventory.approve`) não se desfaz.
- **Estorno (CA 20 a 23):** só movimento manual, pelo movimento inverso (`reverses_movement_id`, único); o autor com `inventory.edit` até R$ 1.000,00 e em 24 h; acima disso ou depois, outra pessoa com `inventory.approve`. A transferência estorna as duas pernas. Movimento de outro documento é estornado pelo documento.
- **Resumo diário (CA 12):** às 06:30 de São Paulo (`Jobs:Cron:inventory-manual-digest`), por tenant com `inventory` liberado: entradas, saídas e estornos manuais do dia anterior, por autor, ao Proprietário (`inventory-manual-digest:{data}`).

### Pedido de compra
`Draft → PendingApproval → Approved → Sent → PartiallyReceived → Received`; `Cancelled` antes de receber.
- **Alçada (ADR 0024 §4):** `PendingApproval` resolve os níveis pelo `IApprovalRouting` do Finance (tipo `PurchaseOrder`, aprovador com `purchasing.approve`); reprovar volta a `Draft` com o histórico imutável; editar pedido aprovado ou enviado volta a `PendingApproval` e exige reenvio ao fornecedor.
- **Caixa (ADR 0024 §2 e §3):** aprovado → títulos a pagar `Planned` por parcela (origem `Purchase`), na mesma transação; cada recebimento → títulos próprios em aberto (origem `GoodsReceipt`) e os previstos replanejados para o saldo; Σ recebido ≤ aprovado + tolerância (padrão 0%), aumento volta à alçada; estorno do recebimento cancela os títulos dele.

### Título a pagar e Ordem de Pagamento
```
Payable: Planned (previsão: pedido de compra/folha/instalador; pode ter data estimada) → Open (confirmado)
         → AwaitingApproval (incluído em OP) → Approved → PartiallyPaid | Paid
PaymentOrder: Pending → (todas as alçadas aprovam) Approved → Paid (baixa com transação bancária)
                      → Rejected (volta títulos para Open, comentário obrigatório)
              Approved → Pending (reenvio: pagamento acima do aprovado + tolerância, 409 exceeds_approved)
              Pending/Approved → Cancelled · Paid → Reversed (estorno: transações inversas, títulos voltam a Open)
```
- O título entra em OP pela transição condicional dele (`Open → AwaitingApproval` com `xmin`); em OP não finalizada, fornecedor, valor, retenções, forma de pagamento, conta, centro e projeto ficam travados (409 `payable_in_payment_order`). A OP grava no envio o retrato de fornecedor, valor líquido e forma de pagamento por item; o pagamento não passa do aprovado (FIN-02, FIN-03a).
- `paid_amount` do título a pagar é medido contra o valor líquido (valor − retenções).
- Títulos de origem `Opening` (FIN-00) nascem `Open` pelo saldo na data de corte e seguem o mesmo fluxo.

Regra dura: **nenhum pagamento é baixado sem OP aprovada**. Exceção: tarifas, IOF, juros e rendimentos lançados no extrato como **lançamento bancário sem OP** (FIN-04, D4 de 20/10/2026), com `finance.pay`, conta do plano e motivo obrigatórios, aviso ao Proprietário e a lista "Movimentos sem OP"; a conciliação (BNK-03) passa a sugerir esses lançamentos a partir do extrato do banco.

### Título a receber
`Planned (marco por evento ainda não ocorrido) → Open (data definida/evento ocorrido) → PartiallyPaid → Paid`; `Overdue` calculado (vencido e não pago); `Cancelled`.
- "Quitar o saldo com desconto" (FIN-01) grava uma baixa de total 0, sem transação bancária. Desconto acima de 5% ou de R$ 500,00 e cancelamento de título com saldo acima de R$ 1.000,00 exigem `finance.approve`; baixa com data mais de 7 dias no passado exige motivo (parâmetros do Finance, D8).
- Título de origem `Contract`: valor só pelo contrato ou pelo aditivo; vencimento editável no título em aberto, com motivo (D14); com o contrato `Cancelled`, o cancelamento do título é liberado com `finance.edit` (ADR 0019 §4).

### Boleto (Inter)
`Requested → Issued → Paid (webhook com reconsulta por mTLS, ou conferência diária) | PaidPendingReview (pagamento com diferença: conferência humana) | Cancelled | Expired` (BNK-01b, BNK-02, ADR 0032). O webhook nunca baixa pelo corpo; estorno de baixa feita pelo banco e estorno de pagamento da OP exigem `finance.approve` (D6, P-20). Multa, juros e desconto vêm de Configurações → Cobrança; acima do limite da D8, `finance.approve`.

### Conciliação bancária (BNK-03a/03b)
Linha do extrato (API do Inter ou OFX; OFX bloqueado em conta com API, salvo contingência com `finance.approve`) `Imported → Matched | Ignored` (imutável; "Ignorada" com motivo). Quem importou não confirma a conciliação; o período `Open → Closed` com `finance.approve`.

### NF-e de saída
`Draft → Processing → Authorized | Rejected (corrigir e reenviar, nova tentativa) | Denied`; `Processing` é reconciliado com a SEFAZ pelo EmissorFiscal (ADR 0027); `Authorized → Cancelled` em uma etapa, com `fiscal.approve` e 2FA de novo, dentro de 24 h (os demais usam "Pedir cancelamento"); números sem nota vão para "A inutilizar" (FIS-03b). A NF-e só referencia o romaneio, e cancelar não mexe no estoque (D12). Venda mista (D4): o equipamento sai em NF-e na expedição e a instalação em NFS-e no aceite técnico (FIS-06); a parcela do contrato é só cobrança.

### Folha lançada (RH-02a/02c, ADR 0036)
`Draft → Closed → Paid`; `Paid → Closed` pelo estorno da OP; reabrir `Closed → Draft` só com motivo e sem pagamento em andamento. O fechamento (com `hr.approve` e 2FA de novo) chama o `IPayables` na mesma transação: um título por funcionário (`payee_type = Employee`) e um por guia; a previsão `PayrollForecast` dos próximos 3 meses é substituída pela folha real. Quem fecha não paga.

### Afastamento (CAD-04c1/04c1b)
Estado calculado na leitura por `BusinessTime.Today`, nunca gravado: `Scheduled` (antes do início) → `Current` (do início ao fim previsto; a situação do funcionário fica `OnLeave`) → `Ended` (depois do fim); `Cancelled` pela marca `cancelled_at`, que prevalece. **Editar** vale no `Scheduled` e no `Current` (no `Current`, início ≤ hoje ≤ fim previsto); **encerrar antes** só no `Current`, com a volta entre o início e o fim previsto e até hoje (o fim previsto vira a véspera da volta, `Current → Ended` na hora); **cancelar** só no `Scheduled`, com motivo (`Scheduled → Cancelled`). `Ended` e `Cancelled` não mudam mais. Cada mudança grava o rastro `LeaveChange`. Doença e Acidente de trabalho exigem `hr.view_health` para registrar e editar.

### Ponto (RH-01a/01b/01c, ADR 0035)
Marcação imutável (controle interno, não registro oficial); ajuste `Pending → Approved | Rejected` (quem pede não aprova); período `Open → Closed` com `hr.edit`, reabertura com motivo.

### OS de manutenção (PAT-03a, ADR 0038)
`Open → InProgress → Done | Cancelled`; o bem fica "Em manutenção" enquanto parado e não é baixado com OS aberta; a parada reduz a capacidade do centro pelo `IWorkCenterCapacity`.

### Pedido do titular (LGPD-01a/01b, ADR 0037)
`Received → IdentityChecked → Exported | Anonymized | Rejected`, com prazo de 15 dias corridos; anonimizar exige confirmação digitada e 2FA de novo, uma transação por módulo, retomável.

## 2. Eventos de domínio (outbox) e reações

| Evento | Publicado por | Reações |
|---|---|---|
| QuoteApproved | Commercial | Nenhuma reação obrigatória: o contrato, os marcos, o projeto, os itens e o orçamento do projeto já nascem na mesma transação da aprovação (ADR 0005 D5, ADR 0006) |
| ContractSigned | Commercial | Finance cria receivables (Planned/Open por marco, com a conta de receita do evento; marcos "recebidos antes da implantação" não geram título) e notifica o financeiro |
| ContractCancelled | Commercial | Finance cancela os receivables sem baixa e lista os com baixa; Production cancela as OS de instalação não iniciadas |
| ContractAmended | Commercial | Finance cria os receivables do acréscimo ou reduz os títulos futuros sem baixa |
| ProjectItemCreated | Commercial | Production cria a OS de fabricação (status Open) a partir do roteiro sugerido, **só para item `Equipment` fora de contrato de implantação**; `Product` tem "Gerar OS" manual e `Service` nunca gera (D14). O evento leva `Kind`, `IsOnboarding` e `RoutingTemplateId`, compatível com as mensagens antigas (PRD-01, ADR 0023 §7) |
| BomReleased | Engineering | Inventory **reconcilia** as reservas da OS a partir da fonte (`IBoms.ReleasedForOrderAsync`), sem aplicar o delta do evento; a falta vira `StockShortageChanged` (EST-02, ADR 0023 §6) |
| BomRevised | Engineering | Inventory reconcilia as reservas até a necessidade da revisão vigente; evento de revisão antiga é ignorado (EST-02, ADR 0023 §6) |
| ProductionStepStarted | Production | Primeira etapa da OS de fabricação iniciada (a OS sai de `Open`): pela "Concluir etapa" do Kanban (PRD-02) e pelo apontamento (PRD-03a). `{ productionOrderId, projectId, stepId, startedAt }`. O Commercial passa o projeto de `Planned` a `InProgress` |
| ProductionOrderCancelled | Production | Inventory reconcilia as reservas da OS com alvo 0 (EST-02); materiais já baixados exigem devolução ou ficam no projeto como perda (EST-03) |
| StockShortageChanged | Inventory | **Só aviso:** Purchasing recompõe a solicitação a partir das faltas abertas (`origin BOMShortage`), conferindo o módulo `purchasing` liberado (CMP-01) |
| StockIssued / StockReturned | Inventory | Publicados pelo motor (EST-01a) em toda saída ou devolução com projeto, inclusive a saída avulsa: `{ movementId, sourceType, sourceId, seq, projectId, productionOrderId?, itemId, quantity, previousAmount, newAmount, at }` (lançamento `seq` 1 de 0 → total; estorno `seq` 2 de total → 0). Commercial grava a diferença na razão `commercial.project_costs`, que entra no realizado de Material do Projeto 360 (EST-03, ADR 0025 §2; até lá o evento fica registrado sem consumidor e a EST-03 recompõe o que veio antes, D3); a baixa em si é síncrona |
| LaborEntryClosed / LaborEntryCorrected | Production | Commercial grava a diferença na `commercial.project_costs` (realizado de Mão de obra, PRD-04, ADR 0025 §2); SignalR atualiza painéis |
| StepStatusChanged | Production | SignalR painel TV/Kanban; Planning recalcula datas da fila |
| QualityApproved | Production | `{ productionOrderId, stepId, attempt, selfInspection }` no outbox (PRD-05); consumidores futuros: DSH-03 (indicadores de qualidade) e PNL-01 (painel) |
| QualityRejected | Production | `{ nonConformityId, productionOrderId, projectId, returnToStepId }`. A NC nasce **síncrona** na reprovação e o responsável e os supervisores são avisados na mesma transação (PRD-05); o evento fica para a DSH-03 e o PNL-01 |
| ProductionOrderCompleted | Production | `{ productionOrderId, projectId, quantity, completedOn }` (PRD-06a1). A entrada do produto acabado com série, os lotes consumidos e a liberação das reservas são **síncronos na conclusão** (PRD-06a2, ADR 0023 §1); o evento serve à projeção: o Commercial passa o projeto a Fabricated quando todas as OS de fabricação não canceladas estão concluídas e publica ProjectFabricated (PRD-06a1); Inventory só reconcilia (alvo 0) |
| ProductionOrderCompletionReversed | Production | Estorno da conclusão (PRD-06a1), `{ productionOrderId, projectId, reversedOn }`: o Commercial volta o projeto de Fabricated para InProgress (só o fabricado pelas OS) e avisa quem tem `finance.edit`; o título já liberado não é estornado |
| ProjectFabricated / ProjectDelivered / TechnicalAcceptance | Commercial (manual na Fase 2, pela OS de instalação ou pela PRD-06) | Finance grava o evento em `finance.project_trigger_events` e libera receivables cujo gatilho é esse evento (Planned → Open, vencimento = data do evento + prazo do marco); título criado depois de um evento já registrado nasce Open (ADR 0019 §1). `ProjectDelivered` também avalia a conclusão do contrato |
| InstallationStarted / InstallationAccepted | Production | Commercial passa o projeto a Installing / Delivered (quando todas as OS de instalação concluem) e publica TechnicalAcceptance e ProjectDelivered |
| ReceivableSettled | Finance | Commercial conclui o contrato (ou o reabre, com `isReversal`) |
| PaymentOrderSubmitted | Finance | Notifica os aprovadores da alçada |
| PurchaseOrderApproved | Purchasing | **Só aviso e projeção** (notificação, custo comprometido do projeto). Os títulos a pagar `Planned` por parcela nascem na transação da aprovação, pelo `IPayables` (CMP-03a, ADR 0024 §1) |
| GoodsReceived | Purchasing | **Só aviso e projeção.** A entrada no estoque (custo médio, ADR 0023) e os títulos do recebimento (origem `GoodsReceipt`, com o replanejamento dos previstos do pedido) são síncronos no recebimento (CMP-04, ADR 0024 §3) |
| PaymentOrderApproved | Finance | Notifica financeiro para pagar |
| BillingSlipPaid | Finance (webhook) | Baixa receivable, cria transação, atualiza estatística de pontualidade |
| ShipmentDispatched | Production | Só aviso e projeção: a baixa do produto acabado (`Shipment`) e as séries expedidas são síncronas na confirmação do romaneio, com 409 `insufficient_stock` (PRD-06b, D4 de 21/10/2026); o `Shipment` não entra no custo do projeto |
| InvoiceAuthorized | Fiscal | A NF-e de saída **referencia o romaneio** e nunca baixa o estoque de novo (D4 de 21/10/2026); Commercial grava o imposto como `InvoiceTax` na razão `project_costs` (imposto realizado só pela nota, ADR 0025 adendo); anexa XML/PDF ao projeto; e-mail ao cliente |
| EmployeeLeaveStarted / EmployeeLeaveEnded | HR | Só id do funcionário e datas, nunca tipo nem motivo; cada um sai uma vez (marcas `start_published_at`/`end_published_at`): no registro, na edição ou no encerramento antes do previsto, quando já venceu, ou pelo job diário `hr-leave-events`; afastamento cancelado nunca publica (CAD-04c1, CAD-04c1b). Sem consumidor nesta fase; a situação "Afastado" e o `IEmployees.Status` são calculados na leitura |
| PayrollClosed | HR | **Só aviso** (ADR 0036): os títulos de salários e guias nascem na transação do fechamento, pelo `IPayables` (RH-02c) |
| PaymentOrderPaid / PaymentOrderReversed | Finance | HR passa a folha a Paid ou de volta a Closed (RH-02c; o `PaymentOrderReversed` é novo) |
| ProjectForecastChanged | Production | Commercial grava `commercial.project_forecasts` só se o `computedAt` for o mais novo; aviso de risco ao responsável e a `production.approve` (PLN-01b, ADR 0029) |
| InvoiceCancelled | Fiscal | Commercial grava `InvoiceTaxReversal` na razão `project_costs` (FIS-03b, ADR 0025 adendo); nada no estoque |
| BillingSlipIssued / BillingSlipCancelled | Finance | Régua de cobrança (FIN-09) e portal (POR-01c), lendo a fonte no envio |
| WebhookReceived | Host (FND-16) | Consumidor do provedor (inbox) reconsulta a fonte; gravado na mesma transação do `webhook_events` (ADR 0031) |
| PartyContactChanged | Parties | Portal invalida o acesso quando o e-mail muda; WhatsApp derruba o opt-in quando o número muda (POR-01a, WPP-01) |
| QuoteAcceptedByCustomer / QuoteRejectedByCustomer | Commercial | Só aviso ao Comercial e pré-preenchimento da aprovação interna (POR-02, ADR 0005 D5) |
| SubjectAnonymized | Settings | Cada módulo confirma a anonimização da sua parte (LGPD-01b) |
| MaintenanceScheduled / MaintenanceUnscheduled / MaintenanceCompleted | Assets | Production grava ou retira a parada em `production.downtimes` (horas por dia), que o `IWorkCenterCapacity` desconta; o plano é recalculado (PAT-03b, PLN-01b, ADR 0038) |

- O outbox não garante ordem: o consumidor que depende de outro evento grava o que recebeu e aplica quando o documento existir (registro de gatilhos, ADR 0019 §1).
- **Efeito síncrono, projeção por evento (ADR 0023 §1, revisão pré-Fase 3, 21/10/2026):** o efeito de estoque e de título de um documento é chamada síncrona ao contrato do dono, na transação do documento; o evento fica para custo do projeto, notificação e painéis. Consumidores de reserva e falta reconciliam a partir da fonte, nunca pelo delta do evento. Avisos de tempo real saem depois do commit, também nos consumidores do outbox.
- **Recompor o passado (D3, ADR 0025 §3):** a story que entrega um consumidor recompõe o que foi publicado antes dela, a partir da fonte, com a marca "Calculado na implantação".
- Notificação disparada dentro do próprio módulo é síncrona, pelo `INotificationService`, na transação do comando; só a reação de outro módulo passa pelo outbox (ADR 0019 §18).

## 3. Regras transversais de cálculo

### 3.1 Custo médio ponderado (por item, global entre locais)
`novo_custo = (saldo_qtd × custo_atual + qtd_entrada × custo_entrada) / (saldo_qtd + qtd_entrada)`
- Custo de entrada da compra = valor do item na NF + rateio de frete/IPI quando não recuperável (regra definida com a contadora).
- Saídas usam o custo médio do momento; o movimento grava o custo (imutável).
- Produto acabado entra pelo custo apurado da OS (materiais baixados + mão de obra apontada).
- **Motor (EST-01a, ADR 0023 §3):** o `saldo_qtd` é o físico total do item em todos os locais antes do movimento, lido com a linha do item travada; o custo médio é arredondado em 6 casas `AwayFromZero` só ao gravar e o total da linha em 2 casas. Com o físico total zerado, o médio passa a ser o custo da entrada. Transferência não muda o médio.
- **Estorno:** o de saída volta pelo custo da saída original (pondera como entrada); o de entrada sai pelo custo da entrada, `(saldo × médio − qtd × custo) ÷ (saldo − qtd)`, e se o resultado for ≤ 0 ou o físico restante zerar, fica o último médio válido.

### 3.2 Custo-hora
`custo_hora_cargo = (salário_base × (1 + %encargos_estimados)) / horas_mensais_contratuais` — %encargos configurável por tenant (padrão sugerido pela contadora). Snapshot gravado em cada apontamento **no fechamento** (PRD-04, ponto 73): custo = minutos líquidos (fim − início − Σ de todas as pausas) ÷ 60 × snapshot, arredondado só no total (2 casas, `AwayFromZero`). Mudança do custo-hora depois do fechamento não mexe no apontamento. Correção: mesmo snapshot, horas novas, versão + 1. Cargo sem custo-hora: custo 0 e "Sem custo-hora" (aviso a quem tem `hr.edit`, `labor-no-cost:{cargo}`); "Recalcular apontamentos sem custo" (`production.approve`) usa o custo-hora atual e publica a diferença. Apontamentos fechados antes da PRD-04: recompostos uma vez por job (D3) com o custo-hora do dia, "Calculado na implantação".

### 3.3 Orçado × realizado por projeto
- Orçado: `project_budget` por tipo de custo, copiado do contrato (e ajustado por aditivo).
- Realizado: material (movimentos de saída para OS do projeto + compras diretas ao projeto), mão de obra (apontamentos), instalação (payables do instalador vinculados), frete/outros (payables com projeto), impostos (só pela NF autorizada: `InvoiceTax`/`InvoiceTaxReversal`; até lá, o projetado usa o imposto orçado).
- Comprometido de material (ADR 0025 adendo, D8): max(0, Σ títulos de compra de item estocável do projeto − Σ baixas líquidas para a OS), fonte única nos títulos do Finance.
- **Três colunas por natureza (PRD-04, ADR 0025 §1, muda a COM-05):** **Realizado** = títulos a pagar confirmados (fora de `Planned` e `Cancelled`) que não são compra de item estocável + títulos `Opening` (saldo da data de corte) + Σ `project_costs` (horas; baixa de material na EST-03). **Comprometido** = títulos `Planned` (previsto do instalador, previsto do pedido) + compra de item estocável por saldo (acima). **Projetado** = realizado + max(orçado − realizado, comprometido). **Desvio** = projetado − orçado. Movimentos `Production` e `Shipment` nunca entram.
- Margem realizada = receita contratada − realizado; margem projetada = (receita − Σ projetado) ÷ receita.

### 3.4 Planejamento (agendamento finito simples)
1. Ordena projetos pela fila do Production (`production.planning_queue`, reordenada com `production.edit`; padrão: data prometida, depois a assinatura; ADR 0029); dentro do projeto, OS por item; dentro da OS, etapas pela sequência, com a etapa em andamento primeiro e a seguinte no dia útil seguinte.
2. Para cada etapa não concluída: horas restantes = padrão − apontadas (mínimo 10% do padrão se em andamento).
3. Aloca a partir do primeiro dia livre do centro de trabalho, respeitando calendário, horas/dia e o fim da etapa anterior.
4. Data prevista do projeto = maior fim de etapa entre suas OS + prazo padrão de instalação (`planning.installation_days`, 5 dias úteis). As datas previstas ficam só nas tabelas do plano (`production.schedule_*`, `commercial.project_forecasts`), com um escritor e fora da auditoria; as OS de instalação mantêm as datas digitadas.
5. Recalcula a cada mudança da fila, apontamento finalizado, mudança de calendário/manutenção, e de hora em hora; pedidos agrupados (1 rodando, 1 pendente), data nova em até 10 s. Capacidade por centro: férias não reduzem; manutenção vira parada.
6. Risco: prevista > prometida → alerta no dashboard e no painel e aviso ao responsável e a `production.approve`, sem tolerância.

### 3.5 Fluxo de caixa projetado
- **Base:** saldo atual + receivables em aberto/planejados (vencimento original) − payables em aberto/planejados − recorrentes futuros − folha prevista.
- **Ajustado:** cada receivable deslocado pela média de atraso do cliente (últimos 12 meses; sem histórico → média geral do tenant); clientes com % de inadimplência acima de limite configurável têm o valor ponderado por (1 − %inadimplência). Decidido em 21/10/2026 (FIN-07): deslocamento em dias corridos; % de inadimplência = vencido há mais de 30 dias ÷ faturado em 12 meses; limite padrão de 20%; vencido em aberto entra em max(hoje, vencimento + atraso médio).
- **Folha prevista** (RH-02c): títulos `Planned` de origem `PayrollForecast` por funcionário para os próximos 3 meses, com 13º e férias, substituídos pela folha real no fechamento.
- Mostra os dois cenários lado a lado e o ponto de menor saldo de cada um.

### 3.6 Alçadas
Regra mais específica vence (categoria/conta > faixa de valor > padrão); a regra por conta tem teto e só vale quando todos os títulos são da conta. A faixa compara o **valor de referência**: o maior entre o total da OP, o maior valor original dos títulos e a soma das OPs do mesmo fornecedor em 30 dias (fracionamento, FIN-03b). Multi-nível: todas as aprovações do nível N antes de liberar o N+1; cada nível precisa de um aprovador elegível, distinto do solicitante e dos outros níveis (ADR 0019 §6). Quem solicita não aprova a própria OP, exceto o Proprietário quando o grupo tem um único membro, ou quando o parâmetro `finance.owner_can_approve_own` está ligado (padrão desligado; troca auditada e notificada) — D6 de 20/10/2026.

### 3.7 Permissão efetiva
```
liberado(tenant, módulo)            ← tenant_modules (catálogo)
habilitado(usuário, módulo)         ← existe grupo ativo do usuário com group_modules.enabled
pode(usuário, módulo, ação) = liberado ∧ habilitado ∧ ∃ grupo do usuário com group_permissions(módulo, ação)
```
- Grupo Proprietário: `pode = liberado` para tudo.
- Ordem de verificação na API: liberado (senão 403 `module_not_released`) → habilitado e ação (senão 403 `forbidden`).
- Menu: módulo não liberado → botão com cadeado que abre a tela padrão; liberado mas sem permissão → botão oculto.
- Alçada de aprovação (3.6) é verificada depois: ter `finance.approve` não dispensa a regra de valor.
- Cache por usuário invalidado ao alterar grupo, membros ou liberação do tenant.

## 4. Numeração padrão (editável)
ORC-AAAA-0001 · CTR-AAAA-0001 · PRJ-AAAA-0001 · OS-AAAA-00001 · NC-AAAA-0001 · SC-AAAA-0001 (solicitação) · COT-AAAA-0001 · PC-AAAA-0001 · REC-AAAA-0001 (recebimento) · CR-AAAA-00001 · CP-AAAA-00001 · OP-AAAA-0001 · MAN-AAAA-0001 · PAT-00001 (etiqueta)

Fase 3 (D15, decisão do PO de 21/10/2026, revisão pré-Fase 3): MOV-AAAA-000001 (movimento de estoque, EST-01a; as pernas de uma transferência e as linhas de um lote de saldo inicial dividem o número) · AJ-AAAA-0001 (ajuste, EST-01b) · REQ-AAAA-0001 (requisição de material, EST-03) · ROM-AAAA-0001 (romaneio, PRD-06b) · INV-AAAA-0001 (inventário, EST-05) · série do produto acabado `{código do item}-{AAAA}-{seq}` (PRD-06a2; antes de implementar, conferir a numeração que a Konebras já usa nos elevadores, campos `elev_*` do Dolibarr, só leitura). Os prefixos são únicos no tenant (teste); a entrada e a saída manual pendentes (D7) usam o número do movimento quando aprovadas.

Fases 4 a 8 (decisões do PO de 21/10/2026): nenhum prefixo novo além dos já previstos (MAN-AAAA-0001 na PAT-03a, PAT-00001 na PAT-01a); "seu número" do boleto é uma sequência opaca por conta em base 36 (BNK-01b), fora desta tabela; o número da NF-e e da NFS-e é do EmissorFiscal, na série exclusiva de Configurações → Fiscal (FIS-02a).
