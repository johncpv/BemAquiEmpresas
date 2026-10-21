# CAD-04c1 — Funcionários: afastamentos e situação

| Campo | Valor |
|---|---|
| ID | CAD-04c1 |
| Título | Afastamentos do funcionário (LGPD art. 11), situação `Afastado` calculada na leitura, `Status` no `IEmployees` para o apontamento recusar afastado, e job diário para afastamento com data futura |
| Tamanho | P (quebra da CAD-04c, D9) |
| Status | Ready |
| Requisitos | R-RH-3 (afastamentos) · PRD §6 LGPD · R-SEC-5 · R-SEC-10 |
| Dependências | CAD-04b |
| Bloqueia | PRD-03a (afastado ou desligado não aponta), CAD-04c2 |
| Ordem de execução | 4 (Marco 3.1 "Fábrica visível") |

## User story
Como **RH**, quero **registrar os afastamentos do funcionário e ver a situação dele mudar sozinha nas datas certas**, e como **proprietário**, quero **que o tipo e o motivo do afastamento fiquem protegidos**, para **o apontamento da fábrica recusar quem está afastado sem expor dado de saúde, que a LGPD trata como sensível**.

## Contexto e referências
- PRD R-RH-3, PRD §6 LGPD. LGPD art. 11 (dado de saúde é dado pessoal sensível).
- `docs/03-modelo-dados.md` → `hr.employees` (`status`), `hr.employee_history` (`Leave`).
- **Revisão pré-Fase 3** (`docs/revisoes/2026-10-21-revisao-pre-fase3.md`): D9 (quebra da CAD-04c: só o afastamento e a situação têm consumidor na Fase 3), tabela 1.1 ponto 2 (tipos de afastamento), §2.1 "Outros" (job diário para afastamento com data futura), §2.2 CAD-04c (afastamentos na exportação e na anonimização).
- Decisão D6 (18/10/2026) revista pela D9: o resto do complemento (dependentes, banco/Pix, adicionais, vale-transporte, documentos) foi para a CAD-04c2, na Fase 6, antes da RH-02.
- **ADR 0016** (rotas `/sensitive`, `SensitiveRead`, comandos separados), **ADR 0018** (retenção e direitos do titular), ADR 0019 §2 (`xmin`), ADR 0021 (mensagens no topo).
- Revisão pré-Fase 1 §2.5: afastamentos (LGPD art. 11).
- Padrões comuns a todos os cadastros: `docs/stories/README.md`.

## Critérios de aceite
**Registrar afastamento**
1. **Dado** `hr.edit` + `hr.view_sensitive`, **quando** clico "Registrar afastamento", informo o tipo (Doença, Acidente de trabalho, Maternidade, Paternidade, Serviço militar ou Outros), o início, o fim previsto e o motivo e confirmo "Registrar afastamento", **então** grava uma entrada `Leave` na linha do tempo do funcionário; afastamentos sobrepostos do mesmo funcionário respondem 409; fim previsto antes do início responde 422; a mensagem de sucesso no topo diz "Afastamento registrado" (ADR 0021). Férias não são afastamento (ficam para a folha).
2. **Dado** um afastamento registrado, **quando** a data de hoje (`BusinessTime.Today`, São Paulo) está entre o início e o fim previsto, **então** a situação do funcionário mostra "Afastado" (`OnLeave`), calculada na leitura e nunca gravada (como o "Vencido" do Financeiro); antes do início e depois do fim, a situação volta a "Ativo"; a coluna `status` continua gravando só `Active`/`Terminated`, e "Desligado" prevalece sobre "Afastado".
3. **Dado** o `IEmployees` (`HR.Contracts`), **quando** outro módulo pede o funcionário, **então** o `EmployeeSummary` traz `Status` (`Active`/`OnLeave`/`Terminated`), calculado pela regra do CA 2, sem tipo, motivo nem datas do afastamento; é por ele que a PRD-03a recusa apontamento de afastado ou desligado.

**Proteção do dado sensível**
4. **[Segurança]** **Dado** um usuário com `hr.view` sem `hr.view_sensitive`, **quando** vê o funcionário afastado (lista, detalhe, `IEmployees`), **então** recebe só a situação "Afastado", sem tipo, motivo nem datas; tipo, datas e motivo vêm só de `/sensitive`, com `SensitiveRead` e `Cache-Control: no-store`.
5. **[Segurança]** **Dado** o campo motivo, **quando** preencho, **então** ele aceita até 200 caracteres, mostra a orientação "Não informe CID nem diagnóstico." e o texto nunca vai para log nem para evento.
6. **[Segurança]** **Dado** os eventos de afastamento (`EmployeeLeaveStarted`, `EmployeeLeaveEnded`), **quando** publicados, **então** levam só o id do funcionário e as datas, nunca o tipo nem o motivo.

**Afastamento com data futura**
7. **Dado** um afastamento com início hoje ou no passado, **quando** é registrado, **então** o `EmployeeLeaveStarted` vai para o outbox na mesma transação; **dado** um afastamento com início futuro, **quando** o job diário (Hangfire, um por tenant, recebe o `tenantId`, roda às 00:15 de São Paulo, horário inicial e parâmetro) chega à data do início, **então** publica o `EmployeeLeaveStarted`; no dia seguinte ao fim previsto, publica o `EmployeeLeaveEnded`. Cada evento sai uma vez só (marca de publicação na entrada, idempotente se o job rodar de novo), e o job pula o tenant com o módulo `hr` não liberado.

**LGPD e auditoria**
8. **[Segurança]** **Dado** a exportação e a anonimização do funcionário (ADR 0018, story LGPD-01 da Fase 6), **quando** forem feitas, **então** incluem os afastamentos: a exportação traz tipo, datas e motivo; a anonimização troca o motivo por um marcador e mantém tipo e datas pelo prazo trabalhista e previdenciário (ADR 0018, retenção do funcionário desligado). Nesta story, os campos ficam `[PersonalData]` e a regra fica registrada no `docs/03-modelo-dados.md` para a LGPD-01.
9. **[Segurança]** **Dado** os testes de allowlist e de canário (ADR 0016), **quando** rodam, **então** todas as propriedades novas são `[PersonalData]` ou estão na allowlist, e o tipo e o motivo do afastamento não aparecem em `audit.audit_log` (só mascarados no diff) nem no log.
10. **Dado** o módulo `hr` não liberado, sem grupo que o habilite ou sem `hr.view_sensitive`, **quando** chamo os endpoints desta story, **então** recebo 403 com o código da camada que barrou; o teste de arquitetura confere `RequireModule` e `perm:` em todos.

## Regras de negócio e técnicas
- **Campos sensíveis:** tipo, datas e motivo do afastamento; para quem não tem `hr.view_sensitive`, só a situação.
- **Afastamento:** entrada `Leave` em `hr.employee_history` (`effective_date` = início, `leave_type`, `leave_end_date`, `reason` até 200, `start_published_at`, `end_published_at`). Tipos: `Illness`, `WorkAccident`, `Maternity`, `Paternity`, `Military`, `Other`.
- **Situação:** `OnLeave` derivada do afastamento vigente na leitura (`BusinessTime.Today`); `Terminated` prevalece; a coluna `status` não muda.
- **Job diário:** só publica eventos; a situação não depende dele (é calculada na leitura).
- **Concorrência:** `xmin` no funcionário (ADR 0019 §2), com 409 `concurrency_conflict` e "Recarregar".
- **Ids:** Guid v7. **Auditoria:** `IAuditable` na entrada de afastamento.

## Permissões, eventos e tabelas
- **Permissões:** `hr.view`, `hr.edit`, `hr.view_sensitive` (já no `ModuleCatalog`).
- **Eventos:** `EmployeeLeaveStarted` e `EmployeeLeaveEnded` (só id e datas).
- **Contratos:** `IEmployees.EmployeeSummary.Status` (`Active`/`OnLeave`/`Terminated`).
- **Tabelas:** `hr.employee_history` (`Leave` e as colunas novas).

## Telas e estados
- No detalhe do funcionário (CAD-04b): o afastamento entra na aba "Linha do tempo" e no diálogo "Registrar afastamento"; a situação "Afastado" aparece como chip de alerta no cabeçalho e na lista.
- **Estados:** carregando; vazio da linha do tempo sem afastamento (a linha do tempo já existe); erro; "Dados protegidos" para quem não tem `hr.view_sensitive`.
- **400 px:** diálogo de afastamento em tela cheia.
- **Ajuda:** `Ajuda/rh-funcionario-linha-do-tempo.md` ganha "Afastamentos" (tipos, situação calculada, por que não informar CID); `<Dica>` em "Motivo" e no chip "Afastado".
- **Mensagens (ADR 0021):** sucesso na região do topo; erro dentro do diálogo, com foco.
- **Propositivo:** o diálogo vem com o início em hoje; ao fechar um afastamento, a linha do tempo mostra a volta prevista.

## Plano de testes
- **Unidade:** situação derivada do afastamento, com `Terminated` prevalecendo (CA 2); sobreposição e datas (CA 1).
- **Integração (Testcontainers):** registrar, sobreposto 409 e fim antes do início 422 (CA 1); `Status` no `IEmployees` (CA 3); situação para `hr.view` sem os detalhes (CA 4); motivo limitado e fora do log (CA 5); eventos sem tipo (CA 6); job com início futuro, fim, segunda execução sem duplicar e tenant sem `hr` (CA 7); canário e allowlist (CA 8, 9); três camadas (CA 10).
- **E2E (Playwright):** registrar afastamento e ver só "Afastado" com um usuário de `hr.view`.

## Fora do escopo
- Dependentes, banco/Pix, adicionais, vale-transporte e documentos: CAD-04c2 (Fase 6).
- Férias e cálculo de folha: RH-02 e Fase 7.
- Telas e serviço de exportação e anonimização: LGPD-01 (Fase 6); esta story só deixa o dado marcado e a regra registrada (CA 8).

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fase 3, D9 e tabela 1.1, item 2):
1. **Escopo:** só afastamentos e situação na Fase 3; o resto vai para a CAD-04c2 (D9).
2. **Tipos de afastamento:** doença, acidente de trabalho, maternidade, paternidade, serviço militar e outros. Férias ficam para a folha.

**Pré-condição externa:** nenhuma.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração, arquitetura e E2E passando.
- [ ] Migration criada (não destrutiva).
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Linha do tempo com estados; ajuda atualizada; teste de cobertura passando.
- [ ] Padrões comuns dos cadastros aplicados.
- [ ] Auditoria ligada, com `[PersonalData]` e `SensitiveRead`.
- [ ] Job diário por tenant registrado no Hangfire, com `tenantId`.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado (colunas do `Leave`, `OnLeave` calculado, regra da LGPD-01); story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `Leave` em `EmployeeHistory`, com tipo, fim previsto e marcas de publicação.
- [ ] Comando "Registrar afastamento" e rota `/sensitive` dos detalhes.
- [ ] `Status` calculado no `EmployeesContract`.
- [ ] Job diário `EmployeeLeaveEventsJob` por tenant.
- [ ] Diálogo, chip e ajuda.

## Quality gates
- Agentes previstos: @dev, cybersecurity (LGPD art. 11), @qa.
- Gate: CA 3, 4, 5, 6 e 9 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fase 3).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo (cabeçalho, user story, contexto, CAs, regras, permissões/tabelas, telas, testes, fora do escopo, pontos em aberto, DoD, tarefas) | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK (ajustado: só R-RH-3, sem o R-RH-1, que foi para a CAD-04c2) |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas do CLAUDE.md) que se aplicam | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: ordem 4 do Marco 3.1, antes da PRD-03a) |
| 7 | Plano de testes cobre os CAs (unidade, integração com Testcontainers, arquitetura, E2E quando cabe) | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas como proposta) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD do CLAUDE.md ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (18/10/2026, primeira validação da CAD-04c):** —
- **Correções da revalidação (21/10/2026):** story nova pela quebra da CAD-04c (D9), com os CAs de afastamento da mãe (8 a 11, agora 1, 2, 4, 5 e 6); tipos de afastamento do ponto 2 no CA 1; `Status` do `IEmployees` num CA próprio (3); job diário para afastamento com data futura (§2.1 "Outros", CA 7); afastamentos na exportação e na anonimização da ADR 0018 (§2.2, CA 8); re-estimada para P.
- **Pré-condições para `Ready`:** nenhuma de decisão de story.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 18/10/2026 | 0.1 | CAD-04c: story criada pela quebra D6 da CAD-04b, com os CAs de segurança da revisão pré-Fase 1 §2.5 | River (SM) |
| 18/10/2026 | 0.2 | CAD-04c: validação do PO (checklist): GO condicional, `Draft` | Pax (PO) |
| 21/10/2026 | 0.3 | CAD-04c: revisão na preparação da Fase 3 contra o código atual (ADR 0021, `OnLeave` calculado e `Status` no `IEmployees`, regras do dono de anexo, ajuda por aba, notificação síncrona, `xmin`, 400 px) | River (SM) |
| 21/10/2026 | 0.4 | CAD-04c: revalidação do PO: 10/10, GO condicional, `Draft` até as decisões 1 e 2 | Pax (PO) |
| 21/10/2026 | 0.5 | Quebra da CAD-04c (D9): afastamentos e situação; tipos do ponto 2; job diário para data futura (§2.1); afastamentos na exportação e anonimização (§2.2, ADR 0018); ordem 4 do Marco 3.1 "Fábrica visível" | River (SM) |
| 21/10/2026 | 0.6 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
