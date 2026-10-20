# COM-04a — Aprovação do orçamento: contrato, projeto, itens e orçamento do projeto

| Campo | Valor |
|---|---|
| ID | COM-04a |
| Título | "Aprovar e gerar contrato" num único comando: orçamento `Approved`, contrato `Active` com data de assinatura editável, snapshot e marcos, projeto `Planned` com itens e orçamento por natureza, conta de receita das parcelas escolhida e validada, eventos `QuoteApproved` e `ContractSigned` no outbox; anexo opcional do contrato assinado; tela do contrato; projeto e natureza do custo no título a pagar |
| Tamanho | G (quebra da COM-04: os recebíveis, a liberação por marco e o cancelamento do contrato foram para a COM-04b; o contrato de implantação foi para a COM-04c) |
| Status | Done |
| Requisitos | R-COML-7 (aprovação interna gera contrato com cópia congelada + projeto + parcelas) · R-PRJ-1 (projeto vinculado a contrato e cliente, obra, data prometida, responsável) · R-PRJ-2 (itens do projeto) · R-FIN-1 (projeto no lançamento) · R-COM-3 (anexo do contrato) · R-COM-4 (CTR, PRJ) · R-PRP-2 · R-PRP-3 · R-PRP-7 · R-SEC-5 |
| Dependências | COM-02 (só a revisão mais recente), COM-01b (marcos 100%), COM-01a (`quotes.view_cost`, `projects.view_cost`, `CostType`); FIN-02 (títulos a pagar, para o campo de projeto); CAD-01a (`IParties`); FND-09e; Fase 0 (FND-07 outbox e anexos, FND-06c 2FA no login) |
| Bloqueia | COM-04b, COM-04c, COM-05, COM-06, COM-07, PRD-01 (OS de fabricação a partir do item do projeto) |
| Ordem de execução | 12 (Marco 2.3 "Contrato ao caixa") |

## User story
Como **proprietário**, quero **aprovar o orçamento e ter, no mesmo clique, o contrato congelado e o projeto criado com os itens e o orçamento por natureza de custo**, para **não redigitar nada e começar a acompanhar o projeto pelo orçado**.

## Contexto e referências
- PRD §5.4 (contrato = proposta firmada; nasce do orçamento aprovado e é **imutável depois de assinado**; alterações só por aditivo), R-COML-7, R-PRJ-1, R-PRJ-2.
- **ADR 0005 D5:** aprovação do orçamento = assinatura do contrato, num único evento; contrato `Active`; projeto, orçamento do projeto e recebíveis; anexo do contrato assinado opcional e posterior; `QuoteApproved` e `ContractSigned` publicados no mesmo comando.
- **D3 (PO, 20/10/2026):** a data de assinatura fica **editável em toda aprovação** (padrão hoje, nunca no futuro). O contrato que já estava em andamento antes do sistema (marcos no passado, sinal já recebido) é a COM-04c.
- `04-fluxos-estados` §1 (só a revisão mais recente; validade não vencida; ao menos um marco e soma 100%), §2 (contrato, projeto, itens e orçamento do projeto nascem na mesma transação da aprovação; Finance cria os recebíveis pelo `ContractSigned`; `ProjectItemCreated` → Production cria OS na Fase 3).
- ADR 0006 (reação dentro do mesmo módulo é síncrona; entre módulos pelo outbox); ADR 0019 §5 (conta de receita validada na aprovação e enviada no evento), §9 (orçamento do projeto pelo maior resto), §13 (`IProjects` em lote), §19 (`[AuditOnCreateOnly]`), §20 (D7).
- Revisão pré-Fase 2: seção 1.1 (itens 24, 25, 26), §2.2 baixos (diálogo destaca preço abaixo do custo e preço digitado à mão), §2.3 (lista de etapas do projeto definida aqui; pré-condições no diálogo; Contratos no menu).
- `03-modelo-dados` → `contracts`, `contract_milestones`, `projects`, `project_items`, `project_budget`.
- `ModuleCatalog`: `quotes.approve` = "gerar o contrato"; `projects` = "Contratos e projetos". Ação `*.approve` reservada ao Proprietário (ADR 0008); 2FA obrigatório no login para quem aprova (FND-06c CA 12).
- Protótipo: botão "Aprovar e gerar contrato" na lista.

## Critérios de aceite
**Pré-condições e diálogo**
1. **Dado** `quotes.approve` e a revisão corrente `Sent`, **quando** clico "Aprovar e gerar contrato", **então** o diálogo (`size="lg"`) lista as pré-condições (validade ≥ hoje, ao menos um item com preço > 0, ao menos um marco, soma dos marcos = 100,0000%, cliente ativo com papel Cliente), cada uma com o link para corrigir e o botão desabilitado com o motivo quando alguma falha; com todas atendidas, mostra o resumo do que vai ser criado (contrato com valor total, projeto com N itens, data prometida, marcos com valor e gatilho) e o botão "Aprovar e gerar contrato".
2. **Dado** o diálogo, **quando** abre, **então** traz a **data de assinatura** (padrão hoje, editável, nunca depois de hoje: 422 "A data de assinatura não pode ser no futuro.") e a **conta de receita das parcelas** (sugerida "1.1 Venda de equipamentos"; sem ela ativa e analítica, "1.9 Receitas a classificar"), validada de forma síncrona por `IChartOfAccounts.PostableAsync(Revenue)` (422 no campo se inválida).
3. **[Segurança]** **Dado** um orçamento com algum item de margem negativa ou com preço digitado à mão, **quando** o diálogo abre para quem tem `quotes.view_cost`, **então** os itens aparecem destacados ("Preço abaixo do custo" no chip crítico; "Digitado à mão" com a `Tag`), com a margem total do orçamento; não bloqueia.
4. **Dado** qualquer pré-condição falhando no servidor, **quando** confirmo, **então** a API responde 422 com a lista de motivos em PT-BR ("A validade venceu em {data}. Reabra com nova revisão.", "Os marcos somam {soma}%. Precisam somar 100%.", "Só a revisão mais recente pode ser aprovada.", "Orçamento sem item com preço.") e nada é gravado.

**O comando**
5. **Dado** a confirmação, **quando** a aprovação grava, **então** numa única transação: o orçamento passa a `Approved` (`approved_at`, `approved_by`); nasce o contrato `CTR-2026-0001` `Active` com `quote_id`, cliente, `total_value` = preço total, `signed_at` = a data de assinatura informada (`date`), `revenue_account_id` e `snapshot`; os marcos do orçamento viram `contract_milestones` (sequência, descrição, %, valor, gatilho, evento, data, prazo após o evento); nasce o projeto `PRJ-2026-0001` `Planned`; e os eventos `QuoteApproved` e `ContractSigned` (com a conta de receita) vão para o outbox.
6. **Dado** o projeto criado, **quando** abro, **então** tem contrato, cliente, nome (padrão: "{nome fantasia do cliente} — {descrição do primeiro item}", editável), endereço da obra do orçamento, data prometida = `signed_at` + prazo de entrega prometido (dias corridos), responsável = autor do orçamento (editável, COM-05) e prioridade nula (a fila é a PLN-02).
7. **Dado** os itens do orçamento, **quando** o projeto nasce, **então** cada `quote_item` vira um `project_item` (descrição, quantidade, item do estoque, `quote_item_id`) e, para cada um, o evento `ProjectItemCreated` vai para o outbox (sem consumidor até a PRD-01).
8. **Dado** as composições de custo, **quando** o projeto nasce, **então** o `project_budget` tem uma linha por natureza (`CostType`: Material, Mão de obra, Instalação, Frete, Impostos, Outros) com `budgeted_amount` = Σ (total da linha × quantidade do item), em `numeric(18,2)`, distribuído pelo maior resto, e a soma das naturezas = custo total do orçamento (teste com dataset fixo de centavos).
9. **[Segurança]** **Dado** duas aprovações do mesmo orçamento ao mesmo tempo (clique duplo, duas abas), **quando** chegam, **então** só um contrato nasce (índice único em `contracts.quote_id` e transição pela entidade rastreada com `xmin`, `Sent` e `is_current`) e a segunda recebe 409 "O orçamento já foi aprovado." com o link do contrato.
10. **[Segurança]** **Dado** `quotes.edit` sem `quotes.approve`, **quando** chamo a aprovação, **então** 403 `forbidden`, e o botão não aparece; quem tem `quotes.approve` passa pela regra existente de 2FA no login (FND-06c CA 12), com o estado do diálogo da FND-09e CA 7 quando a sessão ainda não tem 2FA (sem pedido de código por ação, P-19).
11. **Dado** a aprovação concluída, **quando** a tela volta, **então** a mensagem de sucesso diz "Aprovado: contrato e projeto criados. Gerando as parcelas…" e a faixa "Próximos passos" oferece "Ver projeto criado" (R-PRP-2), "Ver contrato" e "Anexar contrato assinado"; o contrato mostra "Gerando parcelas no financeiro…" até os títulos existirem e, depois de cerca de 30 s sem eles, "As parcelas estão demorando. Avisaremos quando estiverem prontas." (ADR 0019 §5).

**Snapshot e imutabilidade**
12. **Dado** o contrato, **quando** consulto o `snapshot`, **então** ele contém os itens, as composições, os preços, os marcos, os totais, o número e a revisão do orçamento, o prazo e as observações, e o cliente **só por id e nome** (sem CPF/CNPJ, e-mail ou telefone, que vêm do `IParties` na hora de exibir); o snapshot não muda depois (nenhum endpoint altera itens, valores ou marcos do contrato); quem não tem `projects.view_cost` vê o snapshot sem composições nem custo.
13. **[Segurança]** **Dado** a auditoria do contrato, **quando** abro o histórico, **então** a criação registra os campos do contrato e "snapshot criado" (`[AuditOnCreateOnly]`, sem o JSON inteiro no diff), e nenhum dado pessoal do cliente aparece em `audit.audit_log` (teste de canário com o CPF de um cliente PF).
14. **Dado** o orçamento aprovado, **quando** tento editar, criar revisão, prorrogar, enviar ou registrar perda, **então** 409 "Orçamento aprovado não é alterado. Ajustes no contrato são feitos por aditivo."

**Contrato assinado e telas**
15. **Dado** `projects.edit`, **quando** anexo o PDF do contrato assinado na tela do contrato, **então** o anexo (dono `Contract`, só pdf até 25 MB, `OwnerExists`, varrido pelo ClamAV) aparece como "Contrato assinado" com data e usuário; o anexo é opcional e pode ser trocado, mantendo o anterior no histórico.
16. **Dado** `projects.view`, **quando** abro o contrato (`/app/contratos/$id`), **então** vejo número, cliente, valor, data de assinatura, situação, conta de receita, orçamento de origem (link para a revisão aprovada), projeto (link), itens e marcos do snapshot e anexos; sem `projects.view`, 403.
17. **Dado** o projeto criado, **quando** o cabeçalho do projeto aparece, **então** o `StageIndicator` usa a lista de etapas do projeto definida aqui, com letra explícita e rótulo visível: **P** Planejado, **E** Em produção, **F** Fabricado, **I** Instalando, **T** Entregue, **C** Encerrado (o cancelado aparece só com o chip neutro "Cancelado").

**Projeto no título a pagar (seção 1.1, item 26)**
18. **Dado** `finance.create` ou `finance.edit` no título a pagar (FIN-02), **quando** esta story vai ao ar, **então** o formulário passa a mostrar "Projeto" (opcional, validado por `IProjects`) e "Natureza do custo" (`CostType`, obrigatória quando há projeto: 422 "Informe a natureza do custo do projeto."), sugerida pela conta (2.1 Materiais → Material, 2.2 Mão de obra direta → Mão de obra, 2.3 Instalação terceirizada → Instalação, 2.4 Fretes → Frete, 2.5 Impostos sobre vendas → Impostos, 2.6 Outros custos → Outros) e pela origem (`Installer` → Instalação); o título a receber (FIN-01) passa a mostrar o campo "Projeto".

**Permissões e uso**
19. **Dado** o módulo `quotes` não liberado, **quando** aprovo, **então** 403 `module_not_released`; o módulo `projects` não liberado não impede a aprovação (o contrato nasce), mas a tela do contrato e do projeto mostra `ModuleNotReleased`. `RequireModule` e `perm:` em todos os endpoints; IDOR em `/contracts/{id}/attachments/{attId}`.
20. **Dado** um cliente com contrato, **quando** tentam excluí-lo no cadastro, **então** `IPartyUsageProvider` responde "contratos" e a exclusão é recusada.
21. **Dado** outros módulos (Finance na COM-04b, Production na COM-06 e PRD-01), **quando** precisam do projeto ou do contrato, **então** usam `IProjects` (id, número, nome, cliente, contrato, situação, endereço da obra; `FindManyAsync(ids)` em lote) e `IContracts` (`StatusAsync`, número) de `Commercial.Contracts`, nunca a tabela.

## Regras de negócio e técnicas
- **Transação única** para orçamento, contrato, marcos, projeto, itens e orçamento do projeto (mesmo módulo, ADR 0006); os eventos vão no outbox da mesma transação.
- **Contrato:** `Active` → `Completed`/`Cancelled` na COM-04b. `total_value` é o valor original; o valor vigente com aditivos é calculado (COM-07).
- **Datas** `signed_at` e `promised_date` como `date`; `approved_at` `timestamptz`.
- **Dinheiro** `numeric(18,2)`; os valores dos marcos são copiados do orçamento (não recalculados); orçamento do projeto pelo maior resto (ADR 0019 §9).
- **Snapshot** em `jsonb`, versão de esquema `v1` dentro do JSON, com `[AuditOnCreateOnly]`.
- **Numeração** `CTR` e `PRJ` na mesma transação; Guid v7; auditoria `IAuditable` em contrato, marco, projeto, item e orçamento do projeto; concorrência pelo `xmin` (ADR 0019 §2).

## Permissões, eventos e tabelas
- **Permissões (no `ModuleCatalog`):** `quotes.approve` (aprovar e gerar contrato), `projects.view` (contrato e projeto), `projects.edit` (anexo do contrato assinado), `projects.view_cost` (composições e orçamento do projeto; criada na COM-01a).
- **Eventos (outbox, `Commercial.Contracts`):** `QuoteApproved` (`{ quoteId, contractId }`); `ContractSigned` (`{ contractId, projectId, partyId, signedAt, totalValue, revenueAccountId, milestones: [{ milestoneId, sequence, description, amount, trigger, triggerEvent, dueDate, daysAfterEvent }] }`, sem dado pessoal); `ProjectItemCreated` (`{ projectId, projectItemId, quoteItemId, itemId, quantity }`).
- **Contratos novos:** `IProjects` e `IContracts` em `Commercial.Contracts`.
- **Tabelas:** `commercial.contracts` (como no 03, + `revenue_account_id`; `file` vira anexo `Contract` em `shared.attachments`); `commercial.contract_milestones` (+ `days_after_event`; `receivable_id` não é gravado — o Commercial lê o recebível por `IReceivables`, COM-04b); `commercial.projects`; `commercial.project_items`; `commercial.project_budget`; `commercial.quotes` (+ `approved_at`, `approved_by`); `finance.payables` (+ `project_id` exposto na tela, `cost_type`).

## Telas e estados
- **Lista de orçamentos e detalhe:** botão "Aprovar e gerar contrato" na revisão corrente `Sent`, dentro de `<Can perm="quotes.approve">`; chip Aprovado (ok).
- **Comercial → Contratos** (`/app/contratos`; entrada no `modules.ts` na área Comercial): lista com número, cliente, valor, assinatura e situação; **Contrato** (`/app/contratos/$id`): cabeçalho com número, chip e cliente; resumo em "facts"; abas na URL: Resumo, Itens e marcos, Documentos, Histórico de alterações.
- **Projeto (primeira versão)** (`/app/projetos/$id`): cabeçalho com número, cliente, contrato, data prometida e `StageIndicator` (CA 17) e a aba Itens; as demais abas vêm na COM-05.
- **Diálogo** "Aprovar e gerar contrato" / "Aprovado: contrato e projeto criados. Gerando as parcelas…" (efeito financeiro: confirmação com o resumo, pré-condições, data de assinatura e conta de receita, R-PRP-7).
- **Estados:** carregando; erro com os motivos da recusa; modo leitura; "Gerando parcelas…" e o estado de demora no contrato.
- **400 px:** diálogo em tela cheia; lista de contratos com número, cliente, valor e situação.
- **Ajuda:** `Ajuda/comercial-contratos.md` (lista), `Ajuda/comercial-contrato.md` (abas `resumo`, `itens`, `documentos`, `historico`), `Ajuda/projetos-projeto.md` (aba `itens`, `padrao: true` até a COM-05); a ajuda `comercial-orcamento.md` ganha "Aprovar e gerar contrato"; `financeiro-pagar-titulo.md` ganha "Projeto e natureza do custo"; `<Dica>` em "Snapshot do contrato" ("Cópia congelada do orçamento aprovado; muda só por aditivo."), "Data de assinatura" e "Conta de receita das parcelas".

## Plano de testes
- **Unidade:** pré-condições (CA 1, 4); orçamento do projeto por natureza pelo maior resto (CA 8); nome e data prometida padrão (CA 6); sugestão da natureza do custo (CA 18).
- **Integração (Testcontainers, obrigatória: efeito financeiro em cadeia):** comando completo numa transação, com rollback total se qualquer parte falhar (CA 5–8); data de assinatura e conta de receita validadas e no evento (CA 2, 5); destaques do diálogo só com `quotes.view_cost` (CA 3); eventos no outbox com o payload esperado (CA 5, 7); concorrência (CA 9); permissão (CA 10); snapshot sem dado pessoal e canário na auditoria (CA 12, 13); orçamento aprovado imutável (CA 14); anexo só pdf (CA 15); projeto e natureza no título a pagar (CA 18); três camadas, `projects` bloqueado e IDOR (CA 19); provedor de uso (CA 20).
- **Arquitetura:** Finance e Production só usam `Commercial.Contracts`; nenhum endpoint altera itens ou marcos de contrato; DTO do snapshot sem custo para quem não tem `projects.view_cost`.
- **E2E (Playwright):** proprietário aprova um orçamento enviado, abre o projeto pelo próximo passo e anexa o contrato; diálogo a 400 px.

## Fora do escopo
- Recebíveis, liberação por marco, entrega manual, cancelamento e conclusão do contrato: COM-04b. Contrato de implantação: COM-04c. Projeto 360: COM-05. OS de instalação: COM-06. Aditivo: COM-07.
- OS de fabricação a partir do item: PRD-01 (Fase 3), consumindo `ProjectItemCreated`.
- Geração do contrato em PDF para assinatura: fora do MVP (o anexo é o contrato assinado que a empresa já usa).

## Pontos em aberto
Decididos pelo PO em 20/10/2026 (seção 1.1, itens 24 a 26, e D3, D7):
1. Nome e responsável padrão do projeto: os do CA 6, editáveis na COM-05.
2. Módulo `projects` não liberado com `quotes` liberado: a aprovação é permitida; o caso é configuração inválida do plano.
3. Todo item do orçamento vira item do projeto; a PRD-01 decide quais tipos geram OS de fabricação.
4. Data de assinatura editável em toda aprovação (D3); contrato em andamento na COM-04c.
5. Conta de receita escolhida e validada na aprovação, sugerida "1.1", e enviada no evento (item 25).
6. `cost_type` e o campo de projeto no título a pagar criados aqui, com o enum no SharedKernel (item 26).
7. P-18 decidida (ADR 0021).

## Definition of Done
- [x] Critérios de aceite atendidos.
- [x] Testes de unidade, integração (Testcontainers), arquitetura e E2E passando.
- [x] Migration criada (não destrutiva), inclusive `payables.cost_type`.
- [x] Permissões só do `ModuleCatalog`; dono de anexo `Contract` registrado com `OwnerExists`.
- [x] Telas com estados.
- [x] Ajuda do contrato, da lista de contratos, do projeto e do título a pagar; teste de cobertura passando.
- [x] Próximo passo "Ver projeto criado" e confirmação com resumo aplicados.
- [x] Auditoria ligada (sem dado pessoal no snapshot).
- [x] OpenAPI e cliente Orval regenerados.
- [x] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `ApproveQuoteHandler` (comando único) e as entidades `Contract`, `ContractMilestone`, `Project`, `ProjectItem`, `ProjectBudgetLine`.
- [ ] Eventos em `Commercial.Contracts`; `IProjects` (com `FindManyAsync`) e `IContracts` em `Commercial.Contracts`; `[AuditOnCreateOnly]` nos BuildingBlocks.
- [ ] `payables.cost_type` e os campos no formulário do título a pagar e do título a receber (Finance).
- [ ] Endpoints `/api/v1/quotes/{id}/approve`, `/api/v1/contracts`, `/api/v1/contracts/{id}`, `/api/v1/projects/{id}`; telas e ajuda.

## Quality gates
- Agentes previstos: @dev, @architect (transação e outbox), cybersecurity (concorrência e snapshot), @qa.
- Gate: CA 2, 5, 8, 9, 12 e 13 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 20/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fase 2).

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

- **Correções feitas na validação (19/10/2026):** quebra da COM-04 em 04a e 04b; contrato e projeto na mesma transação e Finance pelo outbox; `contract_milestones.receivable_id` não é gravado pelo Finance; snapshot sem dado pessoal; aprovação dupla.
- **Correções da revalidação (20/10/2026):** data de assinatura editável (D3, CA 2); conta de receita no diálogo e no evento, com a mensagem "Gerando as parcelas…" e o estado de demora (§2.1-A4, CA 2, 5, 11); pré-condições no diálogo e destaques de preço (§2.3, §2.2 baixos, CA 1, 3); orçamento do projeto pelo maior resto (M5, CA 8); lista de etapas do projeto (§2.3 item 7, CA 17); projeto e natureza do custo no título a pagar (item 26, CA 18), que saíram da COM-05; `projects.view_cost` no snapshot (D7); `IProjects` em lote e `IContracts` (M9); `[AuditOnCreateOnly]`; Contratos no menu; o 2FA ficou na regra do login (P-19). O contrato de implantação (D3) virou a COM-04c, porque esta story já é G. CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma pendente.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 19/10/2026 | 0.1 | Story detalhada (quebra da COM-04) | River (SM) |
| 19/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 20/10/2026 | 0.3 | Decisões do PO (D3, D7, seção 1.1) e correções da revisão pré-Fase 2 (§2.1-A4, M5, M9, baixos, §2.3); contrato de implantação para a COM-04c; CAs renumerados | River (SM) |
| 20/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
| 20/10/2026 | 1.0 | Implementada. Desvios: a aprovação grava primeiro a transição do orçamento (`xmin`) e depois contrato, projeto e eventos na mesma transação, com o índice único em `contracts.quote_id` como segunda barreira; a parte do custo do item sem composição vai para "Outros" no orçado; o estado "Gerando parcelas…" lê `IReceivables.CountByOriginAsync(Contract, contractId)` (contrato novo no `Finance.Contracts`) e, até a COM-04b consumir o `ContractSigned`, o contrato mostra o aviso de demora; o módulo `projects` abre `/app/contratos` no menu (slug `contratos`, com `projetos` como alias) até a lista de projetos da COM-05; a lista de anexos ganhou o nome de quem anexou (`uploadedByName`); o seletor de projetos do título fica em `/finance/projects` (`finance.view`), sem exigir `projects.view`; título gerado por outra origem com projeto recebe a natureza Instalação (instalador) ou Outros; `IContracts.FindAsync` substitui o `StatusAsync` citado no CA 21; aba Orçado no projeto com `projects.view_cost` | @dev |
