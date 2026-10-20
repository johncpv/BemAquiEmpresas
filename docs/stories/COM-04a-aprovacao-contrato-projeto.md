# COM-04a — Aprovação do orçamento: contrato, projeto, itens e orçamento do projeto

| Campo | Valor |
|---|---|
| ID | COM-04a |
| Título | "Aprovar e gerar contrato" num único comando: orçamento `Approved`, contrato `Active` com snapshot e marcos, projeto `Planned` com itens e orçamento por natureza, eventos `QuoteApproved` e `ContractSigned` no outbox; anexo opcional do contrato assinado; tela do contrato |
| Tamanho | G (quebra da COM-04: os recebíveis, a liberação por marco e o cancelamento do contrato foram para a COM-04b) |
| Status | Draft |
| Requisitos | R-COML-7 (aprovação interna gera contrato com cópia congelada + projeto + parcelas) · R-PRJ-1 (projeto vinculado a contrato e cliente, obra, data prometida, responsável) · R-PRJ-2 (itens do projeto) · R-COM-3 (anexo do contrato) · R-COM-4 (CTR, PRJ) · R-PRP-2 · R-PRP-3 · R-PRP-7 · R-SEC-5 |
| Dependências | COM-02 (só a revisão mais recente), COM-01b (marcos 100%), COM-01a; CAD-01a (`IParties`); Fase 0 (FND-07 outbox e anexos, FND-06c 2FA) |
| Bloqueia | COM-04b, COM-05, COM-06, COM-07, PRD-01 (OS de fabricação a partir do item do projeto) |
| Ordem de execução | 10 (Marco 2.3 — do contrato ao caixa) |

## User story
Como **proprietário**, quero **aprovar o orçamento e ter, no mesmo clique, o contrato congelado e o projeto criado com os itens e o orçamento por natureza de custo**, para **não redigitar nada e começar a acompanhar o projeto pelo orçado**.

## Contexto e referências
- PRD §5.4 (contrato = proposta firmada; nasce do orçamento aprovado e é **imutável depois de assinado**; alterações só por aditivo), R-COML-7, R-PRJ-1, R-PRJ-2.
- **ADR 0005 D5:** aprovação do orçamento = assinatura do contrato, num único evento; contrato `Active` com `signed_at` = data da aprovação; projeto, orçamento do projeto e recebíveis; anexo do contrato assinado opcional e posterior; `QuoteApproved` e `ContractSigned` publicados no mesmo comando.
- `04-fluxos-estados` §1 (só a revisão mais recente; validade não vencida; ao menos um marco e soma 100%), §2 (`ContractSigned` → Commercial cria Project + itens + orçamento do projeto; Finance cria receivables; notificação ao financeiro; `ProjectItemCreated` → Production cria OS na Fase 3).
- ADR 0006: reação **dentro do mesmo módulo é síncrona** (contrato e projeto na mesma transação); reação entre módulos pelo outbox (Finance, COM-04b).
- `03-modelo-dados` → `contracts`, `contract_milestones`, `projects`, `project_items`, `project_budget`.
- `ModuleCatalog`: `quotes.approve` = "gerar o contrato"; `projects` = "Contratos e projetos". Ação `*.approve` reservada ao Proprietário (ADR 0008); 2FA obrigatório para quem aprova (FND-06c CA 12).
- Protótipo: botão "Aprovar e gerar contrato" na lista e o toast "contrato, projeto e parcelas criados".

## Critérios de aceite
**Pré-condições da aprovação**
1. **Dado** `quotes.approve` e a revisão corrente `Sent`, com validade ≥ hoje, ao menos um item com preço > 0, ao menos um marco e soma dos marcos = 100,0000%, cliente ativo com papel Cliente, **quando** clico "Aprovar e gerar contrato", **então** o diálogo mostra o resumo do que vai ser criado (contrato com valor total, projeto com N itens, data prometida, marcos com valor e gatilho, e "Os recebíveis serão gerados no financeiro.") e o botão "Aprovar e gerar contrato".
2. **Dado** qualquer pré-condição falhando, **quando** confirmo, **então** a API responde 422 com a lista de motivos em PT-BR ("A validade venceu em {data}. Reabra com nova revisão.", "Os marcos somam {soma}%. Precisam somar 100%.", "Só a revisão mais recente pode ser aprovada.", "Orçamento sem item com preço.") e nada é gravado.

**O comando**
3. **Dado** a confirmação, **quando** a aprovação grava, **então** numa única transação: o orçamento passa a `Approved` (`approved_at`, `approved_by`); nasce o contrato `CTR-2026-0001` `Active` com `quote_id`, cliente, `total_value` = preço total, `signed_at` = data da aprovação (`date`, São Paulo) e `snapshot`; os marcos do orçamento viram `contract_milestones` (sequência, descrição, %, valor, gatilho, evento, data, prazo após o evento); nasce o projeto `PRJ-2026-0001` `Planned`; e os eventos `QuoteApproved` e `ContractSigned` vão para o outbox.
4. **Dado** o projeto criado, **quando** abro, **então** tem contrato, cliente, nome (padrão: "{nome fantasia do cliente} — {descrição do primeiro item}", editável), endereço da obra do orçamento, data prometida = `signed_at` + prazo de entrega prometido (dias corridos), responsável = autor do orçamento (editável, COM-05) e prioridade nula (a fila é a PLN-02).
5. **Dado** os itens do orçamento, **quando** o projeto nasce, **então** cada `quote_item` vira um `project_item` (descrição, quantidade, item do estoque, `quote_item_id`) e, para cada um, o evento `ProjectItemCreated` vai para o outbox (sem consumidor até a PRD-01).
6. **Dado** as composições de custo, **quando** o projeto nasce, **então** o `project_budget` tem uma linha por natureza (Material, Mão de obra, Instalação, Frete, Impostos, Outros) com `budgeted_amount` = Σ (total da linha × quantidade do item), em `numeric(18,2)`, e a soma das naturezas = custo total do orçamento (teste com dataset fixo).
7. **[Segurança]** **Dado** duas aprovações do mesmo orçamento ao mesmo tempo (clique duplo, duas abas), **quando** chegam, **então** só um contrato nasce (índice único em `contracts.quote_id` e transição condicional `WHERE status = 'Sent' AND is_current`) e a segunda recebe 409 "O orçamento já foi aprovado." com o link do contrato.
8. **[Segurança]** **Dado** `quotes.edit` sem `quotes.approve`, **quando** chamo a aprovação, **então** 403 `forbidden`, e o botão não aparece; com `quotes.approve` e sem 2FA, a ação é recusada até configurar o 2FA (FND-06c CA 12).

**Snapshot e imutabilidade**
9. **Dado** o contrato, **quando** consulto o `snapshot`, **então** ele contém os itens, as composições, os preços, os marcos, os totais, o número e a revisão do orçamento, o prazo e as observações, e o cliente **só por id e nome** (sem CPF/CNPJ, e-mail ou telefone, que vêm do `IParties` na hora de exibir); o snapshot não muda depois (nenhum endpoint altera itens, valores ou marcos do contrato).
10. **[Segurança]** **Dado** a auditoria do contrato, **quando** abro o histórico, **então** a criação registra os campos do contrato e "snapshot criado" (sem o JSON inteiro no diff), e nenhum dado pessoal do cliente aparece em `audit.audit_log` (teste de canário com o CPF de um cliente PF).
11. **Dado** o orçamento aprovado, **quando** tento editar, criar revisão, enviar ou registrar perda, **então** 409 "Orçamento aprovado não é alterado. Ajustes no contrato são feitos por aditivo."

**Contrato assinado e telas**
12. **Dado** `projects.edit`, **quando** anexo o PDF do contrato assinado na tela do contrato, **então** o anexo (dono `Contract`, varrido pelo ClamAV) aparece como "Contrato assinado" com data e usuário; o anexo é opcional e pode ser trocado, mantendo o anterior no histórico.
13. **Dado** a aprovação concluída, **quando** a tela volta, **então** o toast diz "Aprovado: contrato, projeto e parcelas criados." (protótipo) e a faixa "Próximos passos" oferece "Ver projeto criado" (R-PRP-2), "Ver contrato" e "Anexar contrato assinado".
14. **Dado** `projects.view`, **quando** abro o contrato (`/app/contratos/$id`), **então** vejo número, cliente, valor, data de assinatura, situação, orçamento de origem (link para a revisão aprovada), projeto (link), itens e marcos do snapshot e anexos; sem `projects.view`, 403.

**Permissões e uso**
15. **Dado** o módulo `quotes` não liberado, **quando** aprovo, **então** 403 `module_not_released`; o módulo `projects` não liberado não impede a aprovação (o contrato nasce), mas a tela do contrato e do projeto mostra `ModuleNotReleased` (ver Pontos em aberto 2). `RequireModule` e `perm:` em todos os endpoints; IDOR em `/contracts/{id}/attachments/{attId}`.
16. **Dado** um cliente com contrato, **quando** tentam excluí-lo no cadastro, **então** `IPartyUsageProvider` responde "contratos" e a exclusão é recusada.
17. **Dado** outros módulos (Finance na COM-04b, Production na COM-06 e PRD-01), **quando** precisam do projeto, **então** usam `IProjects` de `Commercial.Contracts` (id, número, nome, cliente, contrato, situação, endereço da obra), nunca a tabela.

## Regras de negócio e técnicas
- **Transação única** para orçamento, contrato, marcos, projeto, itens e orçamento do projeto (mesmo módulo, ADR 0006); os eventos vão no outbox da mesma transação.
- **Contrato:** `Active` → `Completed`/`Cancelled` na COM-04b. `total_value` é o valor original; o valor vigente com aditivos é calculado (COM-07).
- **Datas** `signed_at` e `promised_date` como `date`; `approved_at` `timestamptz`.
- **Dinheiro** `numeric(18,2)`; os valores dos marcos são copiados do orçamento (não recalculados).
- **Snapshot** em `jsonb`, versão de esquema `v1` dentro do JSON; propriedade marcada para a auditoria registrar só "criado".
- **Numeração** `CTR` e `PRJ` na mesma transação; Guid v7; auditoria `IAuditable` em contrato, marco, projeto, item e orçamento do projeto.

## Permissões, eventos e tabelas
- **Permissões (já no `ModuleCatalog`):** `quotes.approve` (aprovar e gerar contrato), `projects.view` (contrato e projeto), `projects.edit` (anexo do contrato assinado).
- **Eventos (outbox, `Commercial.Contracts`):** `QuoteApproved` (`{ quoteId, contractId }`); `ContractSigned` (`{ contractId, projectId, partyId, signedAt, totalValue, milestones: [{ milestoneId, sequence, description, amount, trigger, triggerEvent, dueDate, daysAfterEvent }] }`, sem dado pessoal); `ProjectItemCreated` (`{ projectId, projectItemId, quoteItemId, itemId, quantity }`).
- **Tabelas:** `commercial.contracts` (como no 03; `file` vira anexo `Contract` em `shared.attachments`); `commercial.contract_milestones` (+ `days_after_event`; `receivable_id` deixa de ser gravado — o Commercial lê o recebível por `IReceivables`, COM-04b) — **proposta**; `commercial.projects`; `commercial.project_items`; `commercial.project_budget`; `commercial.quotes` (+ `approved_at`, `approved_by`).

## Telas e estados
- **Lista de orçamentos e detalhe:** botão "Aprovar e gerar contrato" na revisão corrente `Sent`, dentro de `<Can perm="quotes.approve">`; chip Aprovado (ok).
- **Contrato** (`/app/contratos/$id`), abas: Resumo, Itens e marcos, Documentos, Histórico de alterações.
- **Projeto (primeira versão)** (`/app/projetos/$id`): cabeçalho com número, cliente, contrato, data prometida e indicador de etapa (`StageIndicator`) e a aba Itens; as demais abas vêm na COM-05.
- **Diálogo** "Aprovar e gerar contrato" / "Aprovado: contrato, projeto e parcelas criados." (efeito financeiro: confirmação com o resumo, R-PRP-7).
- **Estados:** carregando; erro com os motivos da recusa; modo leitura.
- **Ajuda:** `Ajuda/comercial-contrato.md` (abas `resumo`, `itens`, `documentos`, `historico`), `Ajuda/projetos-projeto.md` (aba `itens`, `padrao: true` até a COM-05); a ajuda `comercial-orcamento.md` ganha "Aprovar e gerar contrato"; `<Dica>` em "Snapshot do contrato" ("Cópia congelada do orçamento aprovado; muda só por aditivo.").

## Plano de testes
- **Unidade:** pré-condições (CA 1, 2); orçamento do projeto por natureza (CA 6); nome e data prometida padrão (CA 4).
- **Integração (Testcontainers, obrigatória: efeito financeiro em cadeia):** comando completo numa transação, com rollback total se qualquer parte falhar (CA 3–6); eventos no outbox com o payload esperado (CA 3, 5); concorrência (CA 7); permissão e 2FA (CA 8); snapshot sem dado pessoal e canário na auditoria (CA 9, 10); orçamento aprovado imutável (CA 11); anexo (CA 12); três camadas, `projects` bloqueado e IDOR (CA 15); provedor de uso (CA 16).
- **Arquitetura:** Finance e Production só usam `Commercial.Contracts`; nenhum endpoint altera itens ou marcos de contrato.
- **E2E (Playwright):** proprietário aprova um orçamento enviado, abre o projeto pelo próximo passo e anexa o contrato.

## Fora do escopo
- Recebíveis, liberação por marco, cancelamento e conclusão do contrato: COM-04b. Projeto 360: COM-05. OS de instalação: COM-06. Aditivo: COM-07.
- OS de fabricação a partir do item: PRD-01 (Fase 3), consumindo `ProjectItemCreated`.
- Geração do contrato em PDF para assinatura: fora do MVP (o anexo é o contrato assinado que a empresa já usa).

## Pontos em aberto
1. **Nome e responsável padrão do projeto.** **Recomendação:** os do CA 4, editáveis na COM-05.
2. **Módulo `projects` não liberado com `quotes` liberado.** **Recomendação:** permitir a aprovação (o contrato é efeito do orçamento) e tratar o caso como configuração inválida do plano, com aviso ao super admin; no plano da Konebras os dois estão liberados (ADR 0010 item 4).
3. **Serviço no orçamento vira item do projeto?** **Recomendação:** sim, todo item do orçamento vira item do projeto; a PRD-01 decide quais tipos geram OS de fabricação.
4. **P-18:** ver FIN-01, ponto 5.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers), arquitetura e E2E passando.
- [ ] Migration criada (não destrutiva).
- [ ] Permissões só do `ModuleCatalog`; dono de anexo `Contract` registrado.
- [ ] Telas com estados.
- [ ] Ajuda do contrato e do projeto; teste de cobertura passando.
- [ ] Próximo passo "Ver projeto criado" e confirmação com resumo aplicados.
- [ ] Auditoria ligada (sem dado pessoal no snapshot).
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `ApproveQuoteHandler` (comando único) e as entidades `Contract`, `ContractMilestone`, `Project`, `ProjectItem`, `ProjectBudgetLine`.
- [ ] Eventos em `Commercial.Contracts`; `IProjects` em `Commercial.Contracts`.
- [ ] Endpoints `/api/v1/quotes/{id}/approve`, `/api/v1/contracts/{id}`, `/api/v1/projects/{id}`; telas e ajuda.

## Quality gates
- Agentes previstos: @dev, @architect (transação e outbox), cybersecurity (concorrência e snapshot), @qa.
- Gate: CA 3, 6, 7, 9 e 10 obrigatórios.

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

- **Correções feitas na validação (19/10/2026):** quebra da COM-04 (G+) em 04a (contrato e projeto) e 04b (recebíveis e marcos); contrato e projeto na mesma transação e Finance pelo outbox (ADR 0006); `contract_milestones.receivable_id` não é gravado pelo Finance (fronteira de módulo); snapshot sem dado pessoal (CA 9, 10); aprovação dupla (CA 7).
- **Pré-condições para `Ready`:** Pontos em aberto 1 a 3 (com padrão recomendado) e P-18.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 19/10/2026 | 0.1 | Story detalhada (quebra da COM-04) | River (SM) |
| 19/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
