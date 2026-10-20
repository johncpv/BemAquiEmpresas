# COM-07 — Aditivo de contrato

| Campo | Valor |
|---|---|
| ID | COM-07 |
| Título | Aditivo de valor, escopo e prazo com aprovação (`projects.approve`): ajusta o orçamento do projeto por natureza, a data prometida e os recebíveis futuros, sem tocar no contrato original nem nos títulos com baixa |
| Tamanho | M |
| Status | Draft |
| Requisitos | R-COML-8 (aditivo recalcula orçado e recebíveis futuros, **D**) · R-PRJ-5 (orçado ajustado por aditivo) · R-PRP-7 · R-SEC-5 |
| Dependências | COM-04b (recebíveis do contrato), COM-05 (projeto), COM-01b (editor de marcos reaproveitado), FIN-01 |
| Bloqueia | DSH-01 ("aditivos aguardando aprovação"), DSH-02 (orçado com aditivos) |
| Ordem de execução | 16 (Marco 2.4 — desejável; fica no fim porque o contrato já funciona sem ele e o PRD o marca D) |

## User story
Como **comercial**, quero **registrar um aditivo quando o cliente muda o escopo, o valor ou o prazo, e o proprietário aprovar**, para **o orçado do projeto e as parcelas a receber continuarem certos sem refazer o contrato**.

## Contexto e referências
- PRD §5.4 (contrato imutável depois de assinado; alterações só por aditivo), R-COML-8 (D).
- `03-modelo-dados` → `contract_amendments` (`contract_id`, `number`, `description`, `value_delta`, `days_delta`, `approved_at`); `project_budget` ("copiado do contrato e ajustado por aditivo", `04-fluxos` §3.3).
- PRD §5.14: o dashboard mostra "aditivos aguardando aprovação" (DSH-01).
- `ModuleCatalog`: `projects.approve` = "aditivos de contrato".

## Critérios de aceite
1. **Dado** `projects.edit` e um contrato `Active`, **quando** clico "Novo aditivo", informo descrição (obrigatória), variação de valor (positiva ou negativa, `numeric(18,2)`), a distribuição da variação no orçado por natureza (soma igual à variação de custo informada, opcional), variação de prazo em dias (inteiro, pode ser 0) e os marcos do aditivo (CA 3), e clico "Salvar aditivo", **então** ele nasce `Draft` com número sequencial no contrato ("Aditivo 1", sem tipo novo de numeração).
2. **Dado** um aditivo `Draft`, **quando** clico "Enviar para aprovação", **então** passa a `PendingApproval` e quem tem `projects.approve` recebe no sino "Aditivo {n} do {contrato} aguarda aprovação." com link.
3. **Dado** variação de valor positiva, **quando** defino os marcos do aditivo (descrição, %, gatilho, como na COM-01b), **então** a soma precisa ser 100% do acréscimo; dado variação negativa, **quando** salvo, **então** o sistema propõe reduzir os títulos futuros do contrato (`Planned` e `Open` sem baixa) do último para o primeiro, mostrando cada título com o valor novo (ver Pontos em aberto 1), e a redução não pode passar do saldo desses títulos (422 "A redução é maior que o saldo a receber sem baixa.").
4. **Dado** `projects.approve` e um aditivo `PendingApproval`, **quando** clico "Aprovar aditivo" e confirmo no diálogo que mostra o impacto (contratado de {antes} para {depois}, data prometida de {antes} para {depois}, títulos criados e reduzidos, orçado por natureza), **então** na mesma transação o aditivo passa a `Approved` (`approved_at`, `approved_by`), o `project_budget` soma as variações por natureza, a data prometida do projeto soma os dias, e o evento `ContractAmended` vai para o outbox; o toast diz "Aditivo aprovado".
5. **Dado** o `ContractAmended`, **quando** o consumidor do Finance processa, **então** cria os títulos dos marcos do acréscimo (mesmas regras da COM-04b CA 1 e 2, origem `Contract`, `origin_id` = id do marco do aditivo) ou reduz os títulos indicados, uma vez só (idempotente); título que ganhou baixa depois do envio não é reduzido e o Finance notifica `finance.edit` para decidir.
6. **Dado** um aditivo `PendingApproval`, **quando** clico "Reprovar aditivo" com comentário obrigatório, **então** passa a `Rejected` e o autor é notificado.
7. **[Segurança]** **Dado** o autor do aditivo, **quando** tenta aprová-lo, **então** 403 "Quem registrou o aditivo não pode aprová-lo." (exceto o membro do Proprietário, ver Pontos em aberto 2); sem `projects.approve`, 403 e o botão some; aprovação dupla simultânea grava uma vez.
8. **Dado** o contrato com aditivos aprovados, **quando** abro o contrato e o projeto, **então** o contrato mostra o valor original (snapshot, imutável), a lista de aditivos e o valor vigente (original + Σ aprovados), e o Resumo do projeto (COM-05) usa o valor vigente e o orçado ajustado.
9. **Dado** o módulo `projects` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`. `RequireModule` e `perm:`; IDOR em `/contracts/{id}/amendments/{amendmentId}`.
10. **Dado** qualquer alteração do aditivo, **quando** abro o histórico, **então** vejo antes/depois, usuário e data; a linha do tempo do projeto mostra o aditivo.

## Regras de negócio e técnicas
- **Aditivo** `contract_amendments` (+ `status` Draft/PendingApproval/Approved/Rejected, `budget_deltas` por natureza, `approved_by`, `rejection_comment`, **proposta**) e `contract_amendment_milestones` (**proposta**, mesmo formato dos marcos do contrato).
- **Imutável depois de aprovado**; o contrato original (snapshot e `total_value`) nunca muda.
- **Valor vigente** = `total_value` + Σ `value_delta` aprovados (calculado).
- Dinheiro `numeric(18,2)`; datas `date`; Guid v7; auditoria `IAuditable`.

## Permissões, eventos e tabelas
- **Permissões (já no `ModuleCatalog`):** `projects.edit` (criar, enviar), `projects.approve` (aprovar, reprovar).
- **Eventos (`Commercial.Contracts`):** `ContractAmended` (`{ contractId, projectId, amendmentId, valueDelta, newMilestones[], reductions[{ receivableId, newAmount }] }`).
- **Tabelas:** `commercial.contract_amendments` (com as colunas propostas), `commercial.contract_amendment_milestones` (**proposta**).

## Telas e estados
- **Contrato → aba Aditivos** e **Projeto → ação "Aditivo"**.
- **Verbos:** "Salvar aditivo" / "Aditivo salvo"; "Enviar para aprovação" / "Aditivo enviado"; "Aprovar aditivo" / "Aditivo aprovado"; "Reprovar aditivo" / "Aditivo reprovado".
- **Estados:** carregando; vazio ("Nenhum aditivo. Use quando o cliente mudar escopo, valor ou prazo."); erro; modo leitura.
- **Propositivo:** a redução proposta já vem calculada; a distribuição no orçado vem proporcional ao orçado atual (editável).
- **Ajuda:** `Ajuda/comercial-contrato-aditivos.md` (aba `aditivos`); `<Dica>` em "Valor vigente" e "Redução dos títulos futuros".

## Plano de testes
- **Unidade:** proposta de redução do último para o primeiro (CA 3); valor vigente (CA 8).
- **Integração (Testcontainers, obrigatória):** ciclo Draft → PendingApproval → Approved/Rejected (CA 1, 2, 4, 6); orçado e data prometida na mesma transação (CA 4); consumidor do Finance cria e reduz títulos, idempotente, e não reduz título com baixa (CA 5); autor não aprova e concorrência (CA 7); três camadas e IDOR (CA 9); auditoria (CA 10).
- **E2E (Playwright):** aditivo de acréscimo com um marco, aprovado, e o título novo no financeiro.

## Fora do escopo
- Aditivo que muda itens do projeto (inclui equipamento novo): fica para depois da PRD-01; aqui o escopo é descritivo e o valor vai para o orçado.
- Assinatura do aditivo pelo cliente: anexo opcional na aba Documentos do contrato.

## Pontos em aberto
1. **Como a variação negativa afeta os recebíveis.** **Recomendação:** reduzir os títulos futuros sem baixa, do último para o primeiro (CA 3), com a proposta editável antes de enviar.
2. **Autor aprovando o próprio aditivo.** **Recomendação:** mesma regra da OP (FIN-03a ponto 1): proibido, exceto o membro do Proprietário.
3. **Prioridade D.** **Recomendação:** manter no fim da Fase 2; se o PO precisar antes do go-live, entra antes da FIN-06.
4. **P-18:** ver FIN-01, ponto 5.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers) e E2E passando.
- [ ] Migration criada (não destrutiva).
- [ ] Permissões só do `ModuleCatalog`; consumidor registrado.
- [ ] Telas com estados.
- [ ] Ajuda da aba; teste de cobertura passando.
- [ ] Confirmação com impacto aplicada.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Entidades `ContractAmendment` e `ContractAmendmentMilestone`; `AmendmentReductionPlanner` (Domain).
- [ ] Consumidor `ContractAmendedConsumer` no Finance.
- [ ] Endpoints `/api/v1/contracts/{id}/amendments` (+ `/submit`, `/approve`, `/reject`); aba e ajuda.

## Quality gates
- Agentes previstos: @dev, @qa.
- Gate: CA 4, 5 e 7 obrigatórios.

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

- **Correções feitas na validação (19/10/2026):** o backlog não tinha CA (todos novos); situação de aprovação do aditivo marcada como proposta (o 03 só tem `approved_at`); numeração por contrato, sem tipo novo de documento; título com baixa nunca reduzido (CA 5).
- **Pré-condições para `Ready`:** Pontos em aberto 1 e 2 (com padrão recomendado) e P-18.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 19/10/2026 | 0.1 | Story detalhada | River (SM) |
| 19/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
