# PRD-05 — Qualidade e não conformidade

| Campo | Valor |
|---|---|
| ID | PRD-05 |
| Título | Etapa de qualidade com checklist: aprovar libera a próxima etapa (ou deixa a OS pronta para concluir); reprovar abre a Não Conformidade (descrição, causa, ação, responsável) e devolve a OS a uma etapa anterior escolhida; acompanhamento e encerramento da NC |
| Tamanho | M |
| Status | Draft |
| Requisitos | R-PRD-6 (checklist por etapa; reprovação abre NC com descrição, causa, ação, responsável e devolve a OS à etapa escolhida) · R-COM-2 · R-COM-4 (numeração NC) · R-COM-3 (fotos da NC) · R-PRP-5 · R-SEC-5 · R-SEC-10 (`production.approve` = "liberar na qualidade") |
| Dependências | PRD-03a (etapas, apontamento, fila), PRD-01 (OS e checklist por etapa), PRD-02 (push do Kanban) |
| Bloqueia | PRD-06a (a OS só conclui com a qualidade aprovada), DSH-03 (NC abertas) |
| Ordem de execução | 18 (Marco 3.4 "Fábrica rodando") |

## User story
Como **inspetor/supervisor de qualidade**, quero **aprovar ou reprovar a OS na etapa de qualidade com o checklist, e quando reprovar, registrar a não conformidade e mandar a OS de volta para a etapa certa**, para **nada sair da fábrica com defeito e o retrabalho ficar registrado**.

## Contexto e referências
- PRD §5.7 R-PRD-6; protótipo do operador: "na Qualidade, reprovar devolve a OS para uma etapa anterior".
- `04-fluxos-estados` §1 "OS (fabricação)": `InProgress ─(etapa de qualidade)─► Quality ─aprovar─► InProgress (próxima) / Completed (se última)`; `Quality ─reprovar─► abre NC + volta etapa escolhida (InProgress)`; etapa `Rejected`. §2: `QualityRejected` → cria NC, notifica supervisor. §4: `NC-AAAA-0001` (em `NumberFormats`).
- `03-modelo-dados` → `production.non_conformities` (`number`, `order_id`, `order_step_id`, `description`, `cause`, `action`, `responsible_user_id`, `return_to_step_id`, `status` Open/InAction/Closed, `cost`).
- `ModuleCatalog`: `production.approve` = "liberar na qualidade"; Supervisão de produção tem a ação no seed.
- Observação: no `04-fluxos`, aprovar a última etapa de qualidade leva a `Completed`; esta story deixa a OS "pronta para concluir" e a conclusão (série, produto acabado) é da PRD-06a (Pontos em aberto 4).

## Critérios de aceite
**Entrar na qualidade**
1. **Dado** a etapa anterior concluída e a próxima é de qualidade (`is_quality_step`), **quando** a etapa anterior vai a `Done`, **então** a etapa de qualidade fica `Ready`, a OS vai a `Quality` e quem tem `production.approve` vê a OS na fila de qualidade (PWA e "Fábrica → Produção → Qualidade", `/app/producao/qualidade`), com push no Kanban.

**Aprovar**
2. **Dado** `production.approve` e a etapa de qualidade `Ready` ou `InProgress`, **quando** marco todos os itens do checklist (cada um "Conforme" ou "Não se aplica", com usuário e data) e clico "Aprovar qualidade", **então** a etapa vai a `Done`, o apontamento do inspetor (se iniciado) fecha como na PRD-03a, a OS volta a `InProgress` com a próxima etapa `Ready`, ou, se a qualidade era a última etapa, fica com todas as etapas `Done` e o chip "Pronta para concluir" (PRD-06a); a mensagem diz "Qualidade aprovada".
3. **[Segurança]** **Dado** o inspetor, **quando** foi ele quem apontou a etapa produtiva imediatamente anterior da mesma OS, **então** não aprova (403 "Quem fez a etapa não aprova a qualidade dela.", proposta de segregação; Pontos em aberto 1), exceto o Proprietário quando o grupo tem um único membro (regra da D6), com a marca "Autoaprovado".

**Reprovar e abrir NC**
4. **Dado** `production.approve`, **quando** marco ao menos um item "Não conforme" (ou clico "Reprovar qualidade") e preencho no diálogo grande a descrição (obrigatória, até 1.000), a causa (opcional na abertura, até 500), a ação corretiva (opcional na abertura, até 500), o responsável (usuário ativo com `production.view`; padrão: o supervisor da linha, Pontos em aberto 2) e a **etapa de retorno** (uma das etapas anteriores da OS, padrão: a etapa produtiva imediatamente anterior), e confirmo, **então** na mesma transação: nasce a NC `Open` com número `NC-2026-0001`, a etapa de qualidade vai a `Rejected`, a etapa de retorno e as seguintes até a qualidade voltam a `Waiting` (a de retorno a `Ready`), a OS volta a `InProgress`, o evento `QualityRejected` (`{ nonConformityId, productionOrderId, projectId, returnToStepId }`) vai para o outbox e o responsável e os supervisores (com `production.approve`) recebem a notificação "NC {número} na {OS}: {descrição curta}. Voltou para {etapa}." com link (síncrona, ADR 0019 §18).
5. **Dado** a OS devolvida, **quando** a etapa de retorno é refeita e chega de novo à qualidade, **então** uma **nova** etapa de qualidade não é criada: a mesma etapa volta a `Ready`, com o checklist zerado e o histórico da tentativa anterior guardado (proposta: tentativa 2, 3…), e a aba Qualidade da OS mostra cada tentativa com o resultado.
6. **Dado** a reprovação, **quando** anexo fotos ou documentos à NC (dono `NonConformity`, proposta: png, jpg, pdf até 10 MB, ClamAV; leitura `production.view`, escrita `production.edit`), **então** ficam na NC; no tablet, "Tirar foto" usa a câmera (alvo ≥ 56 px).

**Acompanhar e encerrar a NC**
7. **Dado** `production.edit` e uma NC `Open`, **quando** preencho causa e ação e clico "Iniciar ação", **então** a NC vai a `InAction`; **quando** clico "Encerrar NC" com o resultado (obrigatório, até 500), **então** vai a `Closed` com quem e quando; encerrar exige causa e ação preenchidas (422 com os campos).
8. **Dado** uma NC, **quando** informo o custo estimado da não conformidade (`numeric(18,2)`, opcional, com `projects.view_cost`), **então** grava só como informação (não entra no realizado do projeto, porque o retrabalho já entra pelas horas e pelo material; Pontos em aberto 3).
9. **Dado** a lista "Não conformidades" (`/app/producao/nao-conformidades`), **quando** abro, **então** vejo número, OS, projeto, etapa, responsável, situação (Aberta crítico, Em ação alerta, Encerrada ok), idade em dias; filtros na URL; o Kanban mostra o chip "NC aberta" no cartão da OS.

**Permissões e segurança**
10. **Dado** o módulo `production` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; aprovar ou reprovar sem `production.approve`, editar ou encerrar NC sem `production.edit`: 403 e o botão some. `RequireModule` e `perm:`; IDOR em `/production-orders/{id}/non-conformities/{ncId}` e `/non-conformities/{id}/attachments/{attId}`; etapa de retorno de outra OS ou posterior à qualidade: 422.
11. **[Segurança]** **Dado** aprovação e reprovação simultâneas da mesma etapa, **quando** chegam juntas, **então** só uma grava (`xmin`); a NC não duplica.
12. **Dado** qualquer decisão de qualidade ou mudança de NC, **quando** abro o histórico da OS ou da NC, **então** vejo antes/depois, usuário, data e a tentativa.

## Regras de negócio e técnicas
- **NC** (03 + proposta): `non_conformities` + `attempt`, `opened_by`, `opened_at`, `closed_by`, `closed_at`, `result`, `xmin`; o resultado do checklist da qualidade por item em `production_order_checklist` (com `step_id`) + `attempt`.
- **Retorno:** as etapas entre a de retorno e a qualidade voltam a `Waiting` sem apagar apontamentos (as horas do retrabalho entram como novos apontamentos e no custo, PRD-04).
- **Segregação** (proposta): inspetor ≠ quem apontou a etapa imediatamente anterior.
- **Notificação** síncrona; `QualityRejected` no outbox para os consumidores futuros (DSH-03, PNL-01).
- Guid v7; numeração `NC`; auditoria `IAuditable`.

## Permissões, eventos e tabelas
- **Permissões:** `production.approve` (aprovar, reprovar), `production.edit` (acompanhar e encerrar NC, anexos), `production.view`, `projects.view_cost` (custo estimado da NC).
- **Eventos (`Production.Contracts`):** `QualityApproved` (`{ productionOrderId, stepId }`), `QualityRejected`; SignalR `OrderBoardChanged`.
- **Tabelas:** `production.non_conformities` (com as colunas propostas); dono de anexo `NonConformity`.

## Telas e estados
- **PWA:** a etapa de qualidade com "Aprovar qualidade" e "Reprovar qualidade" (alvos ≥ 56 px, checklist "Conforme / Não conforme / Não se aplica"); **backoffice:** Qualidade (`/app/producao/qualidade`), Não conformidades (`/app/producao/nao-conformidades`, detalhe `/app/producao/nao-conformidades/$id` com abas Detalhes, Documentos, Histórico de alterações), **OS → aba Qualidade**.
- **Verbos:** "Aprovar qualidade" / "Qualidade aprovada"; "Reprovar qualidade" / "NC {número} aberta"; "Iniciar ação" / "Ação iniciada"; "Encerrar NC" / "NC encerrada".
- **Estados:** carregando; vazio da fila ("Nenhuma OS aguardando qualidade."); vazio das NC ("Nenhuma não conformidade aberta."); erro dentro do diálogo; modo leitura da NC encerrada.
- **Propositivo:** etapa de retorno e responsável sugeridos (CA 4); itens "Não conforme" viram o rascunho da descrição; depois de aprovar, `NextSteps` "Concluir a OS" (PRD-06a) quando era a última etapa.
- **400 px:** checklist e diálogo de reprovação em tela cheia; lista de NC com número, OS, responsável e situação.
- **Ajuda:** `Ajuda/producao-qualidade.md`, `Ajuda/producao-nao-conformidades.md`, `Ajuda/producao-nao-conformidade.md` (abas `detalhes` padrão, `documentos`, `historico`), `Ajuda/producao-os-qualidade.md` (aba `qualidade`), atualização de `Ajuda/fab-etapa.md`; `<Dica>` em "Etapa de retorno" e "Não conformidade".

## Plano de testes
- **Unidade:** transições da OS e das etapas na aprovação e na reprovação com retorno (CA 2, 4, 5); segregação (CA 3).
- **Integração (Testcontainers):** entrada na qualidade (CA 1); aprovar com checklist, OS pronta para concluir quando é a última (CA 2); inspetor que fez a etapa e Proprietário único (CA 3); reprovar cria NC, devolve à etapa e notifica na mesma transação (CA 4); nova tentativa sem etapa nova (CA 5); anexos (CA 6); ciclo da NC e encerramento exige causa e ação (CA 7); custo só informativo, fora do realizado (CA 8); lista e chip no Kanban (CA 9); três camadas e IDOR (CA 10); decisão concorrente (CA 11); auditoria (CA 12).
- **E2E (Playwright):** reprovar no tablet com foto, devolver para Montagem, refazer, aprovar na segunda tentativa; tela a 400 px.

## Fora do escopo
- Conclusão da OS e produto acabado: PRD-06a. NC de fornecedor no recebimento: fora do MVP.
- Indicadores de qualidade (taxa de NC por etapa): DSH-03 (Fase 4).

## Pontos em aberto
1. **Segregação na qualidade** (PO): proposta do CA 3 (quem fez a etapa anterior não aprova); alternativa: sem segregação, só auditoria.
2. **Responsável padrão da NC** (PO): proposta: o usuário que reprovou; alternativa: um responsável de qualidade configurável.
3. **Custo da NC** (PO): proposta: informativo (CA 8), sem entrar no realizado (evita contar o retrabalho duas vezes).
4. **Aprovação da última etapa** (PO/arquiteto): o `04-fluxos` diz `Quality → Completed` quando a qualidade é a última; proposta: "pronta para concluir" e a conclusão explícita na PRD-06a (que gera a série e o produto acabado), atualizando o `04-fluxos`.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers) e E2E passando.
- [ ] Migration criada (não destrutiva), testada sobre cópia do banco do tenant `konebras`.
- [ ] Permissões só do `ModuleCatalog`; dono de anexo `NonConformity` registrado.
- [ ] Telas com estados, inclusive a 400 px e no PWA.
- [ ] Ajudas novas e atualizadas; teste de cobertura passando.
- [ ] Notificações acionáveis e próximos passos aplicados.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` e `docs/04-fluxos-estados.md` atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `NonConformity` (Domain) e as transições de qualidade na OS.
- [ ] `QualityService` (aprovar, reprovar) e `NonConformityService` (ação, encerrar, anexos).
- [ ] Endpoints `/api/v1/production/quality` e `/non-conformities`; telas no PWA e no backoffice; ajudas.

## Quality gates
- Agentes previstos: @dev, @ux-design-expert (reprovação no tablet), @qa.
- Gate: CA 2, 3, 4, 5 e 11 obrigatórios.

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

- **Correções feitas na validação:** a mesma etapa de qualidade é reaproveitada por tentativa, sem criar etapa nova (CA 5); custo da NC informativo para não contar o retrabalho duas vezes (CA 8); divergência com o `04-fluxos` (`Quality → Completed`) registrada como ponto em aberto (4).
- **Pré-condições para `Ready`:** pontos em aberto 1 e 4.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
