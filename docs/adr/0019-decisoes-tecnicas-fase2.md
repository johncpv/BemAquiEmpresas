# ADR 0019: Decisões técnicas da Fase 2

- Status: Aceito (20/10/2026). O PO (John) aprovou a revisão pré-Fase 2 (`docs/revisoes/2026-10-20-revisao-pre-fase2.md`): as correções de arquitetura da §2.1 entram como estão, junto com a decisão D7 (custo e margem). As exceções adiadas (2FA por ação e estorno com `finance.approve`) estão em `06-pendencias-riscos.md` (P-19 e P-20).
- Contexto: as 17 stories da Fase 2 dependiam de convenções que ainda não existiam no código das Fases 0 e 1: ordem dos eventos do outbox, concorrência otimista, idempotência de comandos sem documento-pai, anexos e e-mail com as regras de cada dono, dono dos parâmetros, numeração com revisão, deduplicação de notificações e consultas em lote. A revisão marcou dois NO-GOs de infraestrutura (registro de gatilhos e PDF) e vários "GO com ajustes" que dependem destas regras.
- Complementa as ADRs 0003, 0006, 0008, 0009 e 0016. A geração de PDF está na ADR 0022 e as mensagens da interface na ADR 0021.

## Decisão

### 1. Registro dos eventos de gatilho (C1)
- O outbox não garante ordem. Se o `ContractSigned` cair no backoff, o `ProjectFabricated`, o `ProjectDelivered` ou o `TechnicalAcceptance` podem chegar antes, sem título para liberar, e a liberação se perderia.
- O Finance grava cada evento de gatilho em `finance.project_trigger_events` (`project_id`, `event`, `occurred_on`, `message_id`, `received_at`), com chave única em `(project_id, event)`.
- **Título criado depois de um evento já registrado** nasce `Open`, com vencimento = `occurred_on` + `days_after_event`.
- **Evento sem título** fica gravado e é aplicado quando o título nascer (pelo `ContractSigned` ou por um aditivo).
- Teste obrigatório na COM-04b: `ContractSigned` atrasado (evento antes do contrato) e aditivo criado depois do evento (COM-07).

### 2. Concorrência, transições e auditoria (C2)
- **`xmin` como token de concorrência** em toda entidade de documento (título, baixa, OP, orçamento, contrato, projeto, OS, aditivo). Nenhuma entidade tem isso hoje.
- **A versão vai e volta pela API:** o DTO de detalhe tem o campo `version` (o `xmin`); o comando de alteração o devolve (no corpo ou em `If-Match`).
- **Conflito** vira 409 ProblemDetails com `code: "concurrency_conflict"` e a mensagem "{Documento} mudou desde que você abriu. Recarregue." O front mostra o botão "Recarregar" (ADR 0021).
- **Transição de estado pela entidade rastreada** (o handler carrega a entidade, confere o status e salva com o `xmin`), nunca por `UPDATE … WHERE status = …` em SQL cru, porque o SQL cru fura o interceptor de auditoria.
- Se o SQL cru for inevitável (consulta em lote, job), a auditoria é gravada explicitamente pelo `IAuditWriter` na mesma transação.
- A FND-09e entrega a convenção (mapeamento do `xmin`, conversão de `DbUpdateConcurrencyException` em 409 e o tratamento no front).

### 3. Idempotência de comando sem documento-pai (A2)
- Vale para os comandos que criam um documento do zero e que um clique duplo ou um reenvio duplicariam: título manual e parcelamento (FIN-01, FIN-02), lançamento de implantação (FIN-00), criação de OP (FIN-03a), transferência e lançamento bancário sem OP (FIN-04), envio do orçamento por e-mail (COM-03) e aditivo (COM-07).
- O front gera um `Idempotency-Key` (UUID) por abertura de formulário ou diálogo e o envia no cabeçalho.
- O servidor grava a chave em `client_request_id` na tabela do documento, com índice único parcial (`WHERE client_request_id IS NOT NULL`).
- No reenvio com a mesma chave, o servidor devolve o resultado original (mesmo status e mesmo corpo, com o id do documento já criado), sem gravar de novo.
- Mesma chave com corpo diferente responde 422 `idempotency_key_reused`.
- Os comandos com documento-pai (baixa, aprovação, pagamento) já são protegidos pelo `xmin` e pela transição de estado.
- **Adendo de 20/10/2026 (FND-09e):** em vez de uma coluna `client_request_id` em cada tabela de documento, o filtro `.RequireIdempotencyKey()` grava a chave numa tabela única, `shared.idempotency_records` (chave, método e rota, hash do corpo, status e corpo da resposta como texto). Assim a regra vale igual para todo comando, sem migration por documento. A resposta guardada é devolvida idêntica no reenvio; uma recusa (4xx) não fica guardada, para o usuário corrigir e reenviar; enquanto o primeiro envio está em andamento, o segundo recebe 409 `idempotency_in_progress`. O expurgo dos registros antigos entra com o primeiro job que precisar (sugestão: 30 dias).

### 4. Título órfão de origem cancelada (A3)
- O título de origem `Contract` ou `Installer` só muda de valor e é cancelado pela origem (FIN-01 CA 14, FIN-02 CA 9).
- Quando a origem já está `Cancelled` (consultada pelo Contracts do dono: `IContracts.StatusAsync` ou `IInstallationOrders.StatusAsync`), o Finance libera o cancelamento do título com `finance.edit`, motivo obrigatório e a marca "Origem cancelada".

### 5. Parcelas do contrato (A4)
- A conta de receita dos títulos do contrato é escolhida no diálogo de aprovação (sugerida "1.1 Venda de equipamentos"; sem ela, "1.9 Receitas a classificar"), validada de forma síncrona por `IChartOfAccounts.PostableAsync(Revenue)` e enviada no `ContractSigned`. O consumidor do Finance não escolhe conta.
- A mensagem de sucesso da aprovação diz "Aprovado: contrato e projeto criados. Gerando as parcelas…". A tela do contrato mostra o estado de demora depois de cerca de 30 s ("As parcelas estão demorando. Avisaremos quando estiverem prontas.").
- Mensagem de domínio do tenant que vai para dead-letter gera uma notificação a quem tem `finance.edit` (para os eventos do Finance) com a ação "Reprocessar", que devolve a mensagem para a fila (endpoint com `finance.edit`, auditado). O painel da plataforma continua vendo o dead-letter.

### 6. Alçada viável (A1 da arquitetura, FIN-03b)
- Ao salvar uma regra, o servidor recusa o mesmo grupo ou usuário em níveis diferentes com faixas que se sobrepõem.
- No envio da OP, cada nível resolvido precisa ter ao menos um aprovador elegível: com `finance.approve` efetivo, diferente do solicitante e de quem está nos outros níveis. Se faltar, 422 com o motivo ("O nível 2 não tem aprovador diferente do nível 1.").
- Teste obrigatório: tenant com um Proprietário só e duas regras de nível.

### 7. Anexos e e-mail (M1)
- **Regras por dono:** o `AttachmentOwnerRules` ganha `ReadAction` e `WriteAction` (hoje a leitura é sempre `view` e a escrita `edit` do módulo), `OwnerExists` obrigatório (teste de arquitetura reprova o registro sem ele) e `CanAttach(estado)` opcional (ex.: comprovante só em OP paga).
- **Allowlist e tamanho por dono**, dentro da allowlist global da ADR 0009:

| Dono | Leitura | Escrita | Tipos | Tamanho |
|---|---|---|---|---|
| `PaymentOrder` (comprovante) | `finance.view` | `finance.pay` | pdf, png, jpg | 10 MB |
| `Quote` (PDF gerado e anexos) | `quotes.view` | `quotes.edit` | pdf, png, jpg, docx, xlsx, dwg, dxf | 25 MB |
| `Contract` (contrato assinado) | `projects.view` | `projects.edit` | pdf | 25 MB |
| `Project` | `projects.view` | `projects.edit` | pdf, png, jpg, docx, xlsx, dwg, dxf | 25 MB |
| `ProductionOrder` (OS de instalação) | `production.view` | `production.edit` | png, jpg, pdf | 10 MB |

- **Arquivo gerado pelo sistema:** `IAttachmentService.StoreGeneratedAsync(ownerType, ownerId, fileName, stream)`, interno, sem rota, na transação do chamador, com a marca "Gerado pelo sistema". Passa pela varredura como qualquer anexo.
- **E-mail com anexo e cópia:** `IEmailSender.Enqueue` ganha `attachmentIds` e `cc` (até 3 endereços); `email_logs` ganha `attachment_ids` e `cc`.
- **Espera do anexo `Clean`:** o `SendEmailJob` confere a varredura antes de montar o e-mail. Com anexo `Pending`, reagenda a si mesmo (até 10 vezes, a cada 1 min) sem gastar as 3 tentativas de envio da FND-08. Anexo `Infected` ou `Failed` marca o e-mail `Failed` com o motivo.
- A FIN-03a entrega as regras por dono; a COM-03 entrega o arquivo gerado e o e-mail com anexo.

### 8. Título a pagar (M2, M3, M4)
- `payables.paid_amount` é medido contra o **valor líquido** (`amount − Σ retenções`). Saldo do título a pagar = líquido − `paid_amount`.
- Um título entra em no máximo uma OP não finalizada pela **transição condicional do próprio título** (`Open → AwaitingApproval` pela entidade rastreada, com `xmin`), e não por índice único parcial, que dependeria do estado de outra tabela.
- **"Quitar o saldo com desconto"** (FIN-01) é uma ação própria: uma baixa com principal = saldo, desconto = saldo e total 0, **sem transação bancária**. Está sujeita ao limite de desconto (D8).

### 9. Arredondamento (M5)
- Arredonda só no total da linha: custo unitário com 6 casas × quantidade → 2 casas, `AwayFromZero`. Os totais somam linhas já arredondadas.
- O orçamento do projeto por natureza é distribuído pelo **maior resto**, para a soma das naturezas fechar com o custo total do orçamento.
- `margin_pct` é `numeric(9,4)` e fica nulo quando o preço é 0.
- Os valores dos marcos usam o `PaymentTermSplitter` (o mesmo da CAD-05b): a última parcela absorve a diferença.

### 10. Dono dos parâmetros (M6)
- Ver o adendo de 20/10/2026 à ADR 0016.

### 11. Numeração com revisão (M7)
- Ver o adendo de 20/10/2026 à ADR 0016.

### 12. Jobs e notificações (M8)
- `shared.notifications` ganha `dedup_key` (texto, anulável) com índice único parcial `(user_id, dedup_key)`. O `INotificationService.NotifyAsync` aceita a chave e ignora a duplicata (ex.: `overdue:2026-11-19`, `quote-expired:{quoteId}`).
- Todo job é idempotente. Cada job tem dois testes: execução dupla (não duplica nada) e dois tenants (um não vê o outro).
- Os recorrentes globais são registrados no `RecurringJobsRegistrar`, com o fuso de São Paulo (`BusinessTime.Zone`).
- Os argumentos de job carregam só ids (tenant, documento), nunca dado pessoal nem valor.

### 13. Consultas em lote (M9)
- `IParties.FindManyAsync(ids)` e `IProjects.FindManyAsync(ids)` resolvem os nomes de uma página de lista numa chamada.
- O filtro por terceiro ou projeto usa o id escolhido no seletor (combobox), nunca busca por texto no módulo dono.
- Os nomes são resolvidos por página, depois da paginação.

### 14. Vocabulário compartilhado e Contracts × Contracts (M10)
- Os enums usados por mais de um módulo ficam no `BemAqui.SharedKernel`: `CostType` (Material, Labor, Installation, Freight, Tax, Other; COM-01a), `MilestoneTrigger` e `TriggerEvent` (COM-01b) e `DocumentOrigin` (Manual, Opening, Recurring, Contract, Installer, Purchase, Payroll, Maintenance; FIN-01 e FIN-02).
- Regra de arquitetura nova: **um `*.Contracts` não referencia outro `*.Contracts`**. O que dois contratos precisam compartilhar vai para o SharedKernel.

### 15. Teste de posse de tabela (M11)
- O teste da ADR 0016 (cada assembly mapeia só as tabelas do próprio schema; o SQL cru não cita schema alheio) é implementado na FND-09e, antes dos primeiros consumidores cruzados (COM-04b).

### 16. Conclusão do contrato e estorno (M12)
- A conclusão do contrato (`Completed`) é avaliada no `ReceivableSettled` e também no `ProjectDelivered`.
- O `ReceivableSettled` carrega `settlementId` e `isReversal`.
- Estorno de baixa depois de `Completed` reabre o contrato (`Active`) e o projeto (`Delivered`).

### 17. CP estimado do instalador (M13)
- O título `Planned` do instalador nasce com data estimada (`planned_end` da OS + a condição padrão do instalador) e a marca `is_estimate`. Assim entra no fluxo de caixa (FIN-05) como estimativa, com a `Tag` "Estimado".
- No aceite, o `IPayables.ConfirmAsync` grava o vencimento real e tira a marca.

### 18. Eventos dentro do módulo e notificações (M14)
- Contrato, marcos, projeto, itens e orçamento do projeto nascem na mesma transação da aprovação (ADR 0005 D5, ADR 0006). O `04-fluxos` §2 foi corrigido.
- Notificação disparada dentro do próprio módulo é síncrona, pelo `INotificationService`, na transação do comando. Só a notificação que é reação de outro módulo passa pelo outbox.
- `origin` e `origin_id` em `receivables` e `payables` estão no `03-modelo-dados.md`.

### 19. Ajustes menores
- OP estornada fica no estado `Reversed` (não `Cancelled`).
- Faixas de alçada sem sobreposição por `EXCLUDE USING gist` (`document_type`, `level`, conta, faixa como `numrange`).
- Atributo de auditoria `[AuditOnCreateOnly]` para o `snapshot` do contrato: a criação registra "snapshot criado", sem o JSON.
- Aditivo único em `(contract_id, number)`.
- `ContractCancelled` sem texto livre: o motivo fica no contrato, não no evento.
- Índices parciais: `receivables` e `payables` por `(status, due_date) WHERE status NOT IN ('Paid','Cancelled')`; `financial_transactions` por `(bank_account_id, date)`.

### 20. Custo e margem (D7)
- Ações específicas novas no `ModuleCatalog` (ADR 0003, sigilo comercial): **`quotes.view_cost`** ("Ver custo e margem do orçamento") e **`projects.view_cost`** ("Ver orçado, custo lançado e margem do projeto").
- No seed: o Proprietário tem tudo; Comercial e Financeiro recebem as duas por *seed patch* (`view-cost-default-permissions`). Engenharia e Supervisão de produção não recebem.
- Os DTOs de quem não tem a ação não carregam custo, composição, markup, margem nem orçado por natureza (DTO separado, não campo nulo). Teste de arquitetura impede propriedade de custo nos DTOs de resumo e de lista sem a ação.
- O PDF do orçamento nunca mostra custo, para ninguém (COM-03).
- As ações não são reservadas ao Proprietário (ADR 0008): quem as tem pode concedê-las.

## Consequências
- **FND-09e** (nova, antes da FIN-01): convenção de concorrência e idempotência, `dedup_key` das notificações, regra Contracts × Contracts e teste de posse de tabela, além dos componentes de tela (ADR 0021).
- **FIN-01:** `DocumentOrigin`, "Quitar o saldo com desconto", `IParties.FindManyAsync`, índices parciais.
- **FIN-02:** `paid_amount` sobre o líquido; título travado em OP.
- **FIN-03a:** regras de anexo por dono; transição condicional do título; estado `Reversed`.
- **FIN-03b:** alçada viável e `EXCLUDE USING gist`.
- **COM-01a:** `CostType` no SharedKernel, `quotes.view_cost` e `projects.view_cost` com o seed patch, arredondamento e índices de número com revisão.
- **COM-03:** `StoreGeneratedAsync`, e-mail com anexo e cópia, espera do `Clean`.
- **COM-04a:** conta de receita no diálogo e no evento; `IProjects` em lote; `[AuditOnCreateOnly]`.
- **COM-04b:** registro de gatilhos, conclusão também no `ProjectDelivered`, reprocessar dead-letter.
- **COM-06:** CP estimado do instalador.
- **COM-07:** aditivo único por contrato; aplicação de gatilho já registrado.
