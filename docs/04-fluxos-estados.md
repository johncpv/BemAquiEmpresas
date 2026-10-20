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
`Planned → InProgress` (primeira etapa de OS iniciada) `→ Fabricated` (todas OS de fabricação concluídas; até a PRD-06, registro manual "Registrar fabricação concluída") `→ Installing` (OS de instalação iniciada) `→ Delivered` (entrega técnica aceita em todas as OS de instalação não canceladas; venda sem instalação: "Registrar entrega" manual, com `projects.approve`, só sem OS de instalação aberta, D15) `→ Closed` (financeiro quitado). `Closed → Delivered` quando uma baixa é estornada. `Cancelled` a partir de qualquer estado antes de Delivered.
- Etapas exibidas no `StageIndicator` (COM-04a): P Planejado · E Em produção · F Fabricado · I Instalando · T Entregue · C Encerrado.

### OS de instalação (COM-06)
`Open ─iniciar─► InProgress ─aceite técnico─► Completed`; `Open → Cancelled` (`production.edit`, cancela o CP previsto do instalador); `InProgress → Cancelled` só com `production.approve` (o CP previsto fica para o financeiro decidir). Etapas sequenciais; a última ("Entrega técnica") só conclui pelo aceite, com o checklist completo. Sem `Paused` nem `Quality`.

### OS (fabricação)
```
Open ─(BOM liberada e primeira etapa iniciada)─► InProgress ◄─► Paused
InProgress ─(etapa de qualidade)─► Quality ─aprovar─► InProgress (próxima) / Completed (se última)
Quality ─reprovar─► abre NC + volta etapa escolhida (InProgress)
Completed: gera produto acabado com série
Cancelled: libera reservas; materiais já baixados exigem devolução ou apropriação ao projeto como perda
```
Etapa da OS: `Waiting → Ready (anterior concluída) → InProgress ↔ Paused → Done` ou `Rejected`. Etapa só inicia se a anterior estiver Done (sequencial no MVP).

### Pedido de compra
`Draft → PendingApproval → Approved → Sent → PartiallyReceived → Received`; `Cancelled` antes de receber.

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
`Issued → Paid (webhook ou conciliação) | Cancelled | Expired`.

### NF-e de saída
`Draft → Processing → Authorized | Rejected (corrigir e reenviar)`; `Authorized → Cancelled` (dentro do prazo legal).

## 2. Eventos de domínio (outbox) e reações

| Evento | Publicado por | Reações |
|---|---|---|
| QuoteApproved | Commercial | Nenhuma reação obrigatória: o contrato, os marcos, o projeto, os itens e o orçamento do projeto já nascem na mesma transação da aprovação (ADR 0005 D5, ADR 0006) |
| ContractSigned | Commercial | Finance cria receivables (Planned/Open por marco, com a conta de receita do evento; marcos "recebidos antes da implantação" não geram título) e notifica o financeiro |
| ContractCancelled | Commercial | Finance cancela os receivables sem baixa e lista os com baixa; Production cancela as OS de instalação não iniciadas |
| ContractAmended | Commercial | Finance cria os receivables do acréscimo ou reduz os títulos futuros sem baixa |
| ProjectItemCreated | Commercial | Production cria OS de fabricação (status Open) a partir do template |
| BomReleased | Engineering | Inventory cria reservas; se faltar disponível, Purchasing cria solicitação (origin BOMShortage) |
| BomRevised | Engineering | Inventory ajusta reservas (libera ou reserva diferença) |
| StockIssued | Inventory | Commercial/Projects registra custo de material do projeto |
| LaborEntryClosed | Production | Registra custo de mão de obra no projeto; SignalR atualiza painéis |
| StepStatusChanged | Production | SignalR painel TV/Kanban; Planning recalcula datas da fila |
| QualityRejected | Production | Cria NC; notifica supervisor |
| ProductionOrderCompleted | Production | Inventory entra produto acabado com série; se todas OS do projeto → evento ProjectFabricated |
| ProjectFabricated / ProjectDelivered / TechnicalAcceptance | Commercial (manual na Fase 2, pela OS de instalação ou pela PRD-06) | Finance grava o evento em `finance.project_trigger_events` e libera receivables cujo gatilho é esse evento (Planned → Open, vencimento = data do evento + prazo do marco); título criado depois de um evento já registrado nasce Open (ADR 0019 §1). `ProjectDelivered` também avalia a conclusão do contrato |
| InstallationStarted / InstallationAccepted | Production | Commercial passa o projeto a Installing / Delivered (quando todas as OS de instalação concluem) e publica TechnicalAcceptance e ProjectDelivered |
| ReceivableSettled | Finance | Commercial conclui o contrato (ou o reabre, com `isReversal`) |
| PaymentOrderSubmitted | Finance | Notifica os aprovadores da alçada |
| PurchaseOrderApproved | Purchasing | Finance cria payables Planned (previsão) |
| GoodsReceived | Purchasing | Inventory entrada (custo médio); Finance confirma payable (Open) com valor da NF |
| PaymentOrderApproved | Finance | Notifica financeiro para pagar |
| BillingSlipPaid | Finance (webhook) | Baixa receivable, cria transação, atualiza estatística de pontualidade |
| InvoiceAuthorized | Fiscal | Inventory baixa produto acabado (Shipment); anexa XML/PDF ao projeto; e-mail ao cliente |
| PayrollClosed | HR | Finance cria payables de salários e encargos |
| MaintenanceScheduled | Assets | Planning reduz capacidade do centro de trabalho no período |

- O outbox não garante ordem: o consumidor que depende de outro evento grava o que recebeu e aplica quando o documento existir (registro de gatilhos, ADR 0019 §1).
- Notificação disparada dentro do próprio módulo é síncrona, pelo `INotificationService`, na transação do comando; só a reação de outro módulo passa pelo outbox (ADR 0019 §18).

## 3. Regras transversais de cálculo

### 3.1 Custo médio ponderado (por item, global entre locais)
`novo_custo = (saldo_qtd × custo_atual + qtd_entrada × custo_entrada) / (saldo_qtd + qtd_entrada)`
- Custo de entrada da compra = valor do item na NF + rateio de frete/IPI quando não recuperável (regra definida com a contadora).
- Saídas usam o custo médio do momento; o movimento grava o custo (imutável).
- Produto acabado entra pelo custo apurado da OS (materiais baixados + mão de obra apontada).

### 3.2 Custo-hora
`custo_hora_cargo = (salário_base × (1 + %encargos_estimados)) / horas_mensais_contratuais` — %encargos configurável por tenant (padrão sugerido pela contadora). Snapshot gravado em cada apontamento.

### 3.3 Orçado × realizado por projeto
- Orçado: `project_budget` por tipo de custo, copiado do contrato (e ajustado por aditivo).
- Realizado: material (movimentos de saída para OS do projeto + compras diretas ao projeto), mão de obra (apontamentos), instalação (payables do instalador vinculados), frete/outros (payables com projeto), impostos (NF autorizadas).
- Margem realizada = receita contratada − realizado; margem projetada = receita − (realizado + custo restante estimado), onde restante = orçado − realizado por tipo, nunca negativo.

### 3.4 Planejamento (agendamento finito simples)
1. Ordena projetos pela prioridade manual; dentro do projeto, OS por item; dentro da OS, etapas pela sequência.
2. Para cada etapa não concluída: horas restantes = padrão − apontadas (mínimo 10% do padrão se em andamento).
3. Aloca a partir do primeiro dia livre do centro de trabalho, respeitando calendário, horas/dia e o fim da etapa anterior.
4. Data prevista do projeto = maior fim de etapa entre suas OS (+ prazo padrão de instalação configurável).
5. Recalcula a cada mudança de prioridade, apontamento finalizado, mudança de calendário/manutenção, e de hora em hora.
6. Risco: prevista > prometida → alerta no dashboard e no painel.

### 3.5 Fluxo de caixa projetado
- **Base:** saldo atual + receivables em aberto/planejados (vencimento original) − payables em aberto/planejados − recorrentes futuros − folha prevista.
- **Ajustado:** cada receivable deslocado pela média de atraso do cliente (últimos 12 meses; sem histórico → média geral do tenant); clientes com % de inadimplência acima de limite configurável têm o valor ponderado por (1 − %inadimplência).
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
