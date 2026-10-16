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

### Contrato
`Active → Completed` (todos os itens entregues e recebíveis quitados) · `Active → Cancelled` (exige motivo; cancela recebíveis em aberto e OS não iniciadas; OS iniciadas viram decisão manual).

### Projeto
`Planned → InProgress` (primeira etapa de OS iniciada) `→ Fabricated` (todas OS de fabricação concluídas) `→ Installing` (OS de instalação iniciada) `→ Delivered` (entrega técnica aceita) `→ Closed` (financeiro quitado). `Cancelled` a partir de qualquer estado antes de Delivered.

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
Payable: Planned (previsão: pedido de compra/folha) → Open (confirmado) → AwaitingApproval (incluído em OP)
         → Approved → Paid
PaymentOrder: Pending → (todas as alçadas aprovam) Approved → Paid (baixa com transação bancária)
                      → Rejected (volta títulos para Open, comentário obrigatório)
```
Regra dura: **nenhum pagamento é baixado sem OP aprovada**. Exceção configurável: débitos automáticos/tarifas lançados pela conciliação, marcados como "sem OP" e listados para o dono.

### Título a receber
`Planned (marco por evento ainda não ocorrido) → Open (data definida/evento ocorrido) → PartiallyPaid → Paid`; `Overdue` calculado (vencido e não pago); `Cancelled`.

### Boleto (Inter)
`Issued → Paid (webhook ou conciliação) | Cancelled | Expired`.

### NF-e de saída
`Draft → Processing → Authorized | Rejected (corrigir e reenviar)`; `Authorized → Cancelled` (dentro do prazo legal).

## 2. Eventos de domínio (outbox) e reações

| Evento | Publicado por | Reações |
|---|---|---|
| QuoteApproved | Commercial | Commercial cria Contract (snapshot) |
| ContractSigned | Commercial | Commercial cria Project + itens + orçamento do projeto; Finance cria receivables (Planned/Open por marco); Notification ao financeiro |
| ProjectItemCreated | Commercial | Production cria OS de fabricação (status Open) a partir do template |
| BomReleased | Engineering | Inventory cria reservas; se faltar disponível, Purchasing cria solicitação (origin BOMShortage) |
| BomRevised | Engineering | Inventory ajusta reservas (libera ou reserva diferença) |
| StockIssued | Inventory | Commercial/Projects registra custo de material do projeto |
| LaborEntryClosed | Production | Registra custo de mão de obra no projeto; SignalR atualiza painéis |
| StepStatusChanged | Production | SignalR painel TV/Kanban; Planning recalcula datas da fila |
| QualityRejected | Production | Cria NC; notifica supervisor |
| ProductionOrderCompleted | Production | Inventory entra produto acabado com série; se todas OS do projeto → evento ProjectFabricated |
| ProjectFabricated / Delivered / TechnicalAcceptance | Commercial | Finance libera receivables cujo gatilho é esse evento (Planned → Open, vencimento = data do evento + prazo do marco) |
| PurchaseOrderApproved | Purchasing | Finance cria payables Planned (previsão) |
| GoodsReceived | Purchasing | Inventory entrada (custo médio); Finance confirma payable (Open) com valor da NF |
| PaymentOrderApproved | Finance | Notifica financeiro para pagar |
| BillingSlipPaid | Finance (webhook) | Baixa receivable, cria transação, atualiza estatística de pontualidade |
| InvoiceAuthorized | Fiscal | Inventory baixa produto acabado (Shipment); anexa XML/PDF ao projeto; e-mail ao cliente |
| PayrollClosed | HR | Finance cria payables de salários e encargos |
| MaintenanceScheduled | Assets | Planning reduz capacidade do centro de trabalho no período |

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
Regra mais específica vence (categoria/conta > faixa de valor > padrão). Multi-nível: todas as aprovações do nível N antes de liberar o N+1. Quem solicita não aprova a própria OP (exceto o proprietário, configurável).

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
