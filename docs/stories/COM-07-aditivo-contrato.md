# COM-07 — Aditivo de contrato

| Campo | Valor |
|---|---|
| ID | COM-07 |
| Título | Aditivo de valor, escopo e prazo com aprovação (`projects.approve`), editado em página própria com prévia "antes → depois": ajusta o orçamento do projeto por natureza, a data prometida e os recebíveis futuros, sem tocar no contrato original nem nos títulos com baixa |
| Tamanho | M |
| Status | Ready |
| Requisitos | R-COML-8 (aditivo recalcula orçado e recebíveis futuros, **D**) · R-PRJ-5 (orçado ajustado por aditivo) · R-PRP-7 · R-SEC-5 |
| Dependências | COM-04b (recebíveis do contrato, registro de gatilhos), COM-05 (projeto), COM-01b (editor de marcos reaproveitado), FIN-01, FND-09e |
| Bloqueia | DSH-01 ("aditivos aguardando aprovação"), DSH-02 (orçado com aditivos) |
| Ordem de execução | 18 (Marco 2.4 "Controles e D"; desejável) |

## User story
Como **comercial**, quero **registrar um aditivo quando o cliente muda o escopo, o valor ou o prazo, e o proprietário aprovar**, para **o orçado do projeto e as parcelas a receber continuarem certos sem refazer o contrato**.

## Contexto e referências
- PRD §5.4 (contrato imutável depois de assinado; alterações só por aditivo), R-COML-8 (D).
- `03-modelo-dados` → `contract_amendments` (`contract_id`, `number`, `description`, `value_delta`, `days_delta`, `approved_at`); `project_budget` ("copiado do contrato e ajustado por aditivo", `04-fluxos` §3.3).
- PRD §5.14: o dashboard mostra "aditivos aguardando aprovação" (DSH-01).
- Revisão pré-Fase 2: D6 (regra do autor, igual à da OP), D7 (orçado só com `projects.view_cost`), §2.1 (A2 idempotência, C1 registro de gatilhos, baixos: aditivo único por contrato), §2.2-M8 (valores revalidados no servidor), §2.3 (editor em página própria com prévia).
- `ModuleCatalog`: `projects.approve` = "aditivos de contrato" (reservada ao Proprietário, ADR 0008).

## Critérios de aceite
1. **Dado** `projects.edit` e um contrato `Active`, **quando** clico "Novo aditivo", **então** abre a página do aditivo (`/app/contratos/$id/aditivos/novo`) com descrição (obrigatória), variação de valor (positiva ou negativa, `numeric(18,2)`), variação de prazo em dias (inteiro, pode ser 0), os marcos do aditivo (CA 3) e, com `projects.view_cost`, a distribuição da variação no orçado por natureza (opcional; sem ela, ou sem a permissão, a variação é distribuída proporcionalmente ao orçado atual pelo maior resto); "Salvar aditivo" grava `Draft` com número sequencial no contrato ("Aditivo 1", único em `(contract_id, number)`, sem tipo novo de numeração) e com `Idempotency-Key`.
2. **Dado** a página do aditivo, **quando** edito, **então** a prévia "antes → depois" mostra ao vivo: contratado, data prometida, cada título futuro afetado (valor antes e depois), os títulos novos e, com `projects.view_cost`, o orçado por natureza.
3. **Dado** variação de valor positiva, **quando** defino os marcos do aditivo (descrição, %, gatilho, como na COM-01b), **então** a soma precisa ser 100% do acréscimo; dado variação negativa, **quando** salvo, **então** o sistema propõe reduzir os títulos futuros do contrato (`Planned` e `Open` sem baixa) do último para o primeiro, mostrando cada título com o valor novo (editável), e a redução não pode passar do saldo desses títulos (422 "A redução é maior que o saldo a receber sem baixa.").
4. **[Segurança]** **Dado** as reduções e os marcos enviados pelo cliente, **quando** salvo e quando o consumidor do Finance aplica, **então** o servidor revalida: cada título da redução pertence ao contrato, está sem baixa e 0 ≤ valor novo ≤ saldo; cada marco tem % > 0 e ≤ 100; a soma das naturezas é igual à variação; qualquer falha responde 422 no campo da linha (`reductions[n].newAmount`) ou, no consumidor, notifica `finance.edit` sem aplicar (§2.2-M8).
5. **Dado** um aditivo `Draft`, **quando** clico "Enviar para aprovação", **então** passa a `PendingApproval` e quem tem `projects.approve` recebe no sino "Aditivo {n} do {contrato} aguarda aprovação." com link.
6. **Dado** `projects.approve` e um aditivo `PendingApproval`, **quando** clico "Aprovar aditivo" e confirmo no diálogo (`size="lg"`) que mostra o impacto (contratado de {antes} para {depois}, data prometida de {antes} para {depois}, títulos criados e reduzidos e, com `projects.view_cost`, orçado por natureza), **então** na mesma transação o aditivo passa a `Approved` (`approved_at`, `approved_by`), o `project_budget` soma as variações por natureza, a data prometida do projeto soma os dias, e o evento `ContractAmended` vai para o outbox; a mensagem de sucesso diz "Aditivo aprovado".
7. **Dado** o `ContractAmended`, **quando** o consumidor do Finance processa, **então** cria os títulos dos marcos do acréscimo (mesmas regras da COM-04b CA 1 e 2, origem `Contract`, `origin_id` = id do marco do aditivo, conta de receita do contrato), já `Open` quando o evento-gatilho do marco já está no registro de gatilhos (ADR 0019 §1), ou reduz os títulos indicados, uma vez só (idempotente); título que ganhou baixa depois do envio não é reduzido e o Finance notifica `finance.edit` para decidir.
8. **Dado** um aditivo `PendingApproval`, **quando** clico "Reprovar aditivo" com comentário obrigatório, **então** passa a `Rejected` e o autor é notificado.
9. **[Segurança]** **Dado** o autor do aditivo, **quando** tenta aprová-lo, **então** 403 "Quem registrou o aditivo não pode aprová-lo.", com as mesmas exceções da OP (D6, FIN-03a CA 9): Proprietário de um único membro, ou o parâmetro `finance.owner_can_approve_own` ligado, com a marca "Aprovado pelo próprio autor"; sem `projects.approve`, 403 e o botão some; aprovação dupla simultânea grava uma vez (`xmin`).
10. **Dado** o contrato com aditivos aprovados, **quando** abro o contrato e o projeto, **então** o contrato mostra o valor original (snapshot, imutável), a lista de aditivos e o valor vigente (original + Σ aprovados), e o Resumo do projeto (COM-05) usa o valor vigente e o orçado ajustado.
11. **Dado** o módulo `projects` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`. `RequireModule` e `perm:`; IDOR em `/contracts/{id}/amendments/{amendmentId}`.
12. **Dado** qualquer alteração do aditivo, **quando** abro o histórico, **então** vejo antes/depois, usuário e data; a linha do tempo do projeto mostra o aditivo.

## Regras de negócio e técnicas
- **Aditivo** `contract_amendments` (+ `status` Draft/PendingApproval/Approved/Rejected, `budget_deltas` por natureza, `approved_by`, `self_approved`, `rejection_comment`, `client_request_id`; único em `(contract_id, number)`) e `contract_amendment_milestones` (mesmo formato dos marcos do contrato) e `contract_amendment_reductions` (`receivable_id`, `new_amount`).
- **Imutável depois de aprovado**; o contrato original (snapshot e `total_value`) nunca muda.
- **Valor vigente** = `total_value` + Σ `value_delta` aprovados (calculado).
- Dinheiro `numeric(18,2)`; distribuição proporcional pelo maior resto (ADR 0019 §9); datas `date`; Guid v7; auditoria `IAuditable`.

## Permissões, eventos e tabelas
- **Permissões (no `ModuleCatalog`):** `projects.edit` (criar, enviar), `projects.approve` (aprovar, reprovar), `projects.view_cost` (distribuição no orçado).
- **Eventos (`Commercial.Contracts`):** `ContractAmended` (`{ contractId, projectId, amendmentId, valueDelta, newMilestones[], reductions[{ receivableId, newAmount }] }`).
- **Tabelas:** `commercial.contract_amendments` (com as colunas acima), `commercial.contract_amendment_milestones` (nova), `commercial.contract_amendment_reductions` (nova).

## Telas e estados
- **Contrato → aba Aditivos** (lista) e **Aditivo** em página própria (`/app/contratos/$id/aditivos/$aditivoId`), com a prévia "antes → depois"; **Projeto → ação "Aditivo"**.
- **Verbos:** "Salvar aditivo" / "Aditivo salvo"; "Enviar para aprovação" / "Aditivo enviado"; "Aprovar aditivo" / "Aditivo aprovado"; "Reprovar aditivo" / "Aditivo reprovado".
- **Estados:** carregando; vazio ("Nenhum aditivo. Use quando o cliente mudar escopo, valor ou prazo." + "Novo aditivo"); erro; modo leitura (aditivo aprovado ou reprovado, com a faixa explicando).
- **Propositivo:** a redução proposta já vem calculada; a distribuição no orçado vem proporcional ao orçado atual (editável com `projects.view_cost`).
- **400 px:** a prévia fica empilhada (antes em cima, depois embaixo); marcos e reduções viram cartões.
- **Ajuda:** `Ajuda/comercial-contrato-aditivos.md` (aba `aditivos`) e `Ajuda/comercial-contrato-aditivo.md` (página do aditivo); `<Dica>` em "Valor vigente" e "Redução dos títulos futuros".

## Plano de testes
- **Unidade:** proposta de redução do último para o primeiro (CA 3); valor vigente (CA 10); distribuição proporcional (CA 1).
- **Integração (Testcontainers, obrigatória):** ciclo Draft → PendingApproval → Approved/Rejected com idempotência (CA 1, 5, 6, 8); prévia (CA 2); revalidação no servidor e no consumidor (CA 4); orçado e data prometida na mesma transação (CA 6); consumidor do Finance cria e reduz títulos, idempotente, com gatilho já registrado, e não reduz título com baixa (CA 7); autor não aprova, com as exceções da D6, e concorrência (CA 9); três camadas e IDOR (CA 11); auditoria (CA 12).
- **E2E (Playwright):** aditivo de acréscimo com um marco, aprovado, e o título novo no financeiro; página do aditivo a 400 px.

## Fora do escopo
- Aditivo que muda itens do projeto (inclui equipamento novo): depois da PRD-01; aqui o escopo é descritivo e o valor vai para o orçado.
- Assinatura do aditivo pelo cliente: anexo opcional na aba Documentos do contrato.

## Pontos em aberto
Decididos pelo PO em 20/10/2026 (seção 1.1, item 28, e D6):
1. Aditivo negativo reduz os títulos futuros sem baixa, do último para o primeiro, com a proposta editável antes de enviar.
2. Autor aprovando o próprio aditivo: a mesma regra da OP (D6).
3. Prioridade D: no Marco 2.4.
4. P-18 decidida (ADR 0021).

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers) e E2E passando.
- [ ] Migration criada (não destrutiva).
- [ ] Permissões só do `ModuleCatalog`; consumidor registrado.
- [ ] Telas com estados.
- [ ] Ajuda da aba e da página; teste de cobertura passando.
- [ ] Confirmação com impacto aplicada.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Entidades `ContractAmendment`, `ContractAmendmentMilestone` e `ContractAmendmentReduction`; `AmendmentReductionPlanner` (Domain).
- [ ] Consumidor `ContractAmendedConsumer` no Finance, com o registro de gatilhos.
- [ ] Endpoints `/api/v1/contracts/{id}/amendments` (+ `/submit`, `/approve`, `/reject`); página e ajuda.

## Quality gates
- Agentes previstos: @dev, @qa, cybersecurity (revalidação no servidor).
- Gate: CA 4, 6, 7 e 9 obrigatórios.

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

- **Correções feitas na validação (19/10/2026):** todos os CAs novos; situação de aprovação do aditivo; numeração por contrato; título com baixa nunca reduzido.
- **Correções da revalidação (20/10/2026):** editor em página própria com prévia "antes → depois" (§2.3, CA 1, 2); revalidação no servidor e no consumidor (§2.2-M8, CA 4); gatilho já registrado cria título `Open` (C1, CA 7); regra do autor pela D6 (CA 9); orçado só com `projects.view_cost` (D7); idempotência e aditivo único por contrato. CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma pendente.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 19/10/2026 | 0.1 | Story detalhada | River (SM) |
| 19/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 20/10/2026 | 0.3 | Decisões do PO (D6, D7, seção 1.1) e correções da revisão pré-Fase 2 (§2.1-A2, C1, baixos, §2.2-M8, §2.3); CAs renumerados | River (SM) |
| 20/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
