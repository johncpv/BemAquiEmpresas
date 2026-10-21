# PRD-05 — Qualidade e não conformidade

| Campo | Valor |
|---|---|
| ID | PRD-05 |
| Título | Etapa de qualidade com checklist, decidida por quem tem `production.approve_quality` (usável por PIN; a conclusão da OS segue com `production.approve`): aprovar libera a próxima etapa ou deixa a OS "Pronta para concluir" (nunca `Completed`); reprovar abre a Não Conformidade e devolve a OS a uma etapa anterior escolhida; segregação com a marca "Autoinspeção"; acompanhamento e encerramento da NC |
| Tamanho | M |
| Status | Ready |
| Requisitos | R-PRD-6 (checklist por etapa; reprovação abre NC com descrição, causa, ação, responsável e devolve a OS à etapa escolhida) · R-COM-2 · R-COM-4 (numeração NC) · R-COM-3 (fotos da NC) · R-PRP-5 · R-SEC-5 · R-SEC-10 (aprovação na qualidade) |
| Dependências | PRD-03a |
| Bloqueia | PRD-06a1, PRD-03b |
| Ordem de execução | 7 (Marco 3.1 "Fábrica visível") |

## User story
Como **inspetor/supervisor de qualidade**, quero **aprovar ou reprovar a OS na etapa de qualidade com o checklist, inclusive no tablet, e quando reprovar, registrar a não conformidade e mandar a OS de volta para a etapa certa**, para **nada sair da fábrica com defeito e o retrabalho ficar registrado**.

## Contexto e referências
- PRD §5.7 R-PRD-6; protótipo do operador: "na Qualidade, reprovar devolve a OS para uma etapa anterior".
- `04-fluxos-estados` §1 "OS (fabricação)": `InProgress ─(etapa de qualidade)─► Quality ─aprovar─► InProgress (próxima)`; `Quality ─reprovar─► abre NC + volta etapa escolhida (InProgress)`; etapa `Rejected`. Pela D12, aprovar a última etapa deixa a OS "Pronta para concluir" e a conclusão é explícita (PRD-06a1); o `04-fluxos` §1 é atualizado nesta story. §2: `QualityRejected` → cria NC, notifica supervisor. §4: `NC-AAAA-0001` (em `NumberFormats`).
- `03-modelo-dados` → `production.non_conformities` (`number`, `order_id`, `order_step_id`, `description`, `cause`, `action`, `responsible_user_id`, `return_to_step_id`, `status` Open/InAction/Closed, `cost`).
- `ModuleCatalog`: até esta story, `production.approve` era "liberar na qualidade"; a decisão de qualidade passa à `production.approve_quality` (D6) e a Supervisão de produção ganha a ação nova por seed patch (CA 1).
- **Revisão pré-Fase 3:** `docs/revisoes/2026-10-21-revisao-pre-fase3.md` (D6, D12, tabela 1.1 itens 76–79 e 94, §2.2 ponto 76, §2.3 padrões 1, 4 e 5).
- **ADR 0003, adendo de 21/10/2026** (`production.approve_quality`); **ADR 0026** §2 e §4 (a ação é a única de aprovação usável por PIN; fila e decisão de qualidade na lista permitida); **ADR 0019 §7, adendo de 21/10/2026** (dono de anexo `NonConformity`); ADR 0023 §7 (D12: "Pronta para concluir").

## Critérios de aceite
**Ação nova e fila**
1. **Dado** o `ModuleCatalog`, **quando** a migration e o seed desta story rodam, **então** o módulo `production` declara a ação específica **`production.approve_quality`** ("Aprovar ou reprovar a qualidade: decidir a etapa de qualidade da OS, sem concluir a OS nem liberar parcela.", ADR 0003 adendo), que aparece como coluna extra na matriz de permissões; o Proprietário recebe a ação; a concessão segue a delegação da ADR 0008 (ninguém concede o que não tem); o seed patch `production-quality` dá a ação ao grupo Supervisão de produção do seed, uma vez, com o `perm_version` incrementado (a Supervisão decidia a qualidade por `production.approve` e continua decidindo; valor inicial do seed, o tenant ajusta). Daqui em diante, **aprovar e reprovar a qualidade exigem `production.approve_quality`**; a `production.approve` sozinha não decide a qualidade e continua sendo a da conclusão da OS (PRD-06a1).
2. **Dado** a etapa anterior concluída e a próxima é de qualidade (`is_quality_step`), **quando** a etapa anterior vai a `Done`, **então** a etapa de qualidade fica `Ready`, a OS vai a `Quality` e quem tem `production.approve_quality` vê a OS na fila de qualidade (`/fab` e "Fábrica → Produção → Qualidade", `/app/producao/qualidade`), com push no Kanban; a fila abre no filtro "Aguardando você" (OS que eu posso decidir, sem a segregação do CA 5).

**Aprovar**
3. **Dado** `production.approve_quality` e a etapa de qualidade `Ready` ou `InProgress`, **quando** marco todos os itens do checklist (cada um "Conforme" ou "Não se aplica", com usuário e data), clico "Aprovar qualidade" e confirmo no diálogo de impacto ("A OS vai para {próxima etapa}" ou "A OS fica pronta para concluir"), **então** a etapa vai a `Done`, o apontamento do inspetor (se iniciado) fecha como na PRD-03a, o evento `QualityApproved` vai para o outbox, e a OS volta a `InProgress` com a próxima etapa `Ready`, ou, se a qualidade era a última etapa, fica com todas as etapas `Done` e "Pronta para concluir" (D12, ponto 79): **nunca** `Completed`; a conclusão é da PRD-06a1. A mensagem diz "Qualidade aprovada".
4. **Dado** a qualidade aprovada na última etapa, **quando** o `NextSteps` aparece, **então** oferece "Concluir OS" (PRD-06a1) só para quem tem `production.approve` numa sessão completa; para quem só tem `production.approve_quality`, mostra "A OS está pronta para concluir. O supervisor conclui." sem ação.
5. **[Segurança]** **Dado** o inspetor, **quando** ele apontou **qualquer** etapa da tentativa atual da OS (apontamento em qualquer etapa produtiva desde o início da OS, na tentativa 1, ou desde a reprovação anterior, nas seguintes), **então** não aprova: 403 "Quem fez uma etapa desta tentativa não aprova a qualidade dela." (ponto 76); **exceto** quando não há outro aprovador elegível (nenhum outro usuário ativo com `production.approve_quality` que não tenha apontado a tentativa): aí aprova com a marca **"Autoinspeção"** na etapa e na OS, e o Proprietário recebe a notificação "Autoinspeção na {OS}: {nome} aprovou a qualidade do próprio trabalho." com link (síncrona, ADR 0019 §18). Reprovar não tem segregação.

**Reprovar e abrir NC**
6. **Dado** `production.approve_quality`, **quando** marco ao menos um item "Não conforme" (ou clico "Reprovar qualidade") e preencho no diálogo grande a descrição (obrigatória, até 1.000), a causa (opcional na abertura, até 500), a ação corretiva (opcional na abertura, até 500), o responsável (usuário ativo com `production.view`; padrão: **quem reprovou**, ponto 77) e a **etapa de retorno** (uma das etapas anteriores da OS, padrão: a etapa produtiva imediatamente anterior, com a tag "Sugerido"), e confirmo "Reprovar qualidade" vendo o impacto ("Volta para {etapa}; {n} etapas voltam a esperar"), **então** na mesma transação: nasce a NC `Open` com número `NC-2026-0001`, a etapa de qualidade vai a `Rejected`, a etapa de retorno e as seguintes até a qualidade voltam a `Waiting` (a de retorno a `Ready`), a OS volta a `InProgress`, o evento `QualityRejected` (`{ nonConformityId, productionOrderId, projectId, returnToStepId }`) vai para o outbox e o responsável e os supervisores (com `production.approve`) recebem a notificação "NC {número} na {OS}: {descrição curta}. Voltou para {etapa}." com link (síncrona, ADR 0019 §18).
7. **Dado** a OS devolvida, **quando** a etapa de retorno é refeita e chega de novo à qualidade, **então** uma **nova** etapa de qualidade não é criada: a mesma etapa volta a `Ready`, com o checklist zerado e o histórico da tentativa anterior guardado (tentativa 2, 3…), e a aba Qualidade da OS mostra cada tentativa com o resultado, quem decidiu e a marca "Autoinspeção" quando houver.
8. **Dado** a reprovação ou uma NC aberta, **quando** anexo fotos ou documentos à NC (dono `NonConformity`: png, jpg, pdf até 10 MB, ClamAV; leitura `production.view`; escrita `production.edit` ou `production.approve_quality`; limite de 30 envios por hora por usuário com 429; ADR 0019 §7, adendo), **então** ficam na NC; no tablet, "Tirar foto" usa a câmera (alvo ≥ 56 px).

**Acompanhar e encerrar a NC**
9. **Dado** `production.edit` e uma NC `Open`, **quando** preencho causa e ação e clico "Iniciar ação", **então** a NC vai a `InAction`; **quando** clico "Encerrar NC" com o resultado (obrigatório, até 500), **então** vai a `Closed` com quem e quando; encerrar exige causa e ação preenchidas (422 com os campos).
10. **Dado** uma NC, **quando** informo o custo estimado da não conformidade (`numeric(18,2)`, opcional, com `projects.view_cost`), **então** grava só como informação (ponto 78): não entra no realizado do projeto, porque o retrabalho já entra pelas horas (PRD-04) e pelo material (EST-03).
11. **Dado** a lista "Não conformidades" (`/app/producao/nao-conformidades`), **quando** abro, **então** vejo número, OS, projeto, etapa, responsável, situação (Aberta crítico, Em ação alerta, Encerrada ok), idade em dias; filtro padrão "Aguardando você" (NC em que sou o responsável e que não estão encerradas); filtros na URL; o Kanban mostra o chip "NC aberta" no cartão da OS.

**Permissões e segurança**
12. **Dado** o módulo `production` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; aprovar ou reprovar sem `production.approve_quality` (mesmo com `production.approve`), editar ou encerrar NC sem `production.edit`: 403 e o botão some. `RequireModule` e `perm:`; IDOR em `/production-orders/{id}/non-conformities/{ncId}` e `/non-conformities/{id}/attachments/{attId}`; etapa de retorno de outra OS ou posterior à qualidade: 422.
13. **[Segurança]** **Dado** um usuário só com `production.approve_quality` (sem `production.approve`), **quando** tenta concluir etapa, corrigir apontamento, cancelar ou concluir OS, aprovar etapa "Exige aprovação para concluir" ou liberar parcela, **então** 403 em todas (D6: a ação só aprova ou reprova a qualidade); os endpoints da fila e da decisão de qualidade ficam separados dos de supervisão, para a PRD-03b colocá-los na lista permitida da sessão por PIN (ADR 0026 §4) sem levar mais nada.
14. **[Segurança]** **Dado** aprovação e reprovação simultâneas da mesma etapa, **quando** chegam juntas, **então** só uma grava (`xmin`); a NC não duplica.
15. **Dado** qualquer decisão de qualidade ou mudança de NC, **quando** abro o histórico da OS ou da NC, **então** vejo antes/depois, usuário, data, a tentativa e a marca "Autoinspeção" quando houver.

## Regras de negócio e técnicas
- **NC** (03 + colunas desta story): `non_conformities` + `attempt`, `opened_by`, `opened_at`, `closed_by`, `closed_at`, `result`, `xmin`; o resultado do checklist da qualidade por item em `production_order_checklist` (com `step_id`) + `attempt`; a decisão da etapa guarda `decided_by`, `decided_at`, `attempt` e `self_inspection`.
- **Tentativa:** começa no início da OS (1) e a cada reprovação (n + 1); a segregação olha os apontamentos (`labor_entries`) das etapas produtivas feitos na tentativa atual.
- **Aprovador elegível** (CA 5): usuário ativo com a permissão efetiva `production.approve_quality` (cache de permissões), fora de quem apontou a tentativa.
- **Retorno:** as etapas entre a de retorno e a qualidade voltam a `Waiting` sem apagar apontamentos (as horas do retrabalho entram como novos apontamentos e no custo, PRD-04).
- **"Pronta para concluir":** nesta story é a situação calculada "todas as etapas `Done`" (chip, como na PRD-03a); o estado gravado e a conclusão são da PRD-06a1 (ADR 0023 §7).
- **Notificação** síncrona; `QualityApproved` e `QualityRejected` no outbox para os consumidores futuros (DSH-03, PNL-01).
- Guid v7; numeração `NC`; auditoria `IAuditable`.

## Permissões, eventos e tabelas
- **Permissões:** `production.approve_quality` (nova: aprovar, reprovar, anexar à NC), `production.approve` (não decide a qualidade; recebe o aviso da NC e conclui a OS na PRD-06a1), `production.edit` (acompanhar e encerrar NC, anexos), `production.view`, `projects.view_cost` (custo estimado da NC).
- **Eventos (`Production.Contracts`):** `QualityApproved` (`{ productionOrderId, stepId, attempt, selfInspection }`), `QualityRejected`; SignalR `OrderBoardChanged`.
- **Tabelas:** `production.non_conformities` (com as colunas acima); dono de anexo `NonConformity`.

## Telas e estados
- Padrões de tela da Fase 3: 1 (no `/fab`), 4, 5.
- **`/fab`:** a etapa de qualidade com "Aprovar qualidade" e "Reprovar qualidade", checklist "Conforme / Não conforme / Não se aplica", confirmação em folha de tela cheia; **backoffice:** Qualidade (`/app/producao/qualidade`), Não conformidades (`/app/producao/nao-conformidades`, detalhe `/app/producao/nao-conformidades/$id` com abas Detalhes, Documentos, Histórico de alterações), **OS → aba Qualidade** (na ordem do padrão 10).
- **Verbos:** "Aprovar qualidade" / "Qualidade aprovada"; "Reprovar qualidade" / "NC {número} aberta"; "Iniciar ação" / "Ação iniciada"; "Encerrar NC" / "NC encerrada".
- **Estados:** carregando; vazio da fila ("Nenhuma OS aguardando qualidade."); vazio das NC ("Nenhuma não conformidade aberta."); erro dentro do diálogo; modo leitura da NC encerrada; chip alerta "Autoinspeção".
- **Propositivo:** etapa de retorno e responsável sugeridos (CA 6); itens "Não conforme" viram o rascunho da descrição; depois de aprovar a última etapa, `NextSteps` "Concluir OS" (CA 4).
- **400 px:** checklist e diálogo de reprovação em tela cheia; lista de NC com número, OS, responsável e situação.
- **Ajuda:** `Ajuda/producao-qualidade.md`, `Ajuda/producao-nao-conformidades.md`, `Ajuda/producao-nao-conformidade.md` (abas `detalhes` padrão, `documentos`, `historico`), `Ajuda/producao-os-qualidade.md` (aba `qualidade`), atualização de `Ajuda/fab-etapa.md`; `<Dica>` em "Etapa de retorno", "Não conformidade" e "Autoinspeção" ("Aprovada por quem fez parte do trabalho, porque não havia outro aprovador. O Proprietário foi avisado.").

## Plano de testes
- **Unidade:** transições da OS e das etapas na aprovação e na reprovação com retorno, nunca `Completed` (CA 3, 6, 7); segregação por tentativa e aprovador elegível (CA 5).
- **Integração (Testcontainers):** ação no catálogo e matriz, seed patch `production-quality` uma vez para a Supervisão (CA 1); entrada na qualidade e fila só com `production.approve_quality` e "Aguardando você" (CA 2); aprovar com checklist, OS pronta para concluir quando é a última (CA 3); próximo passo por permissão (CA 4); inspetor que apontou a tentativa, sem outro elegível com "Autoinspeção" e notificação (CA 5); reprovar cria NC com responsável = quem reprovou, devolve à etapa e notifica na mesma transação (CA 6); nova tentativa sem etapa nova (CA 7); anexos com `production.approve_quality` e limite de envios (CA 8); ciclo da NC (CA 9); custo só informativo (CA 10); lista e chip no Kanban (CA 11); três camadas e IDOR (CA 12); `production.approve_quality` sozinha recusada fora da qualidade (CA 13); decisão concorrente (CA 14); auditoria (CA 15).
- **E2E (Playwright):** reprovar no tablet com foto, devolver para Montagem, refazer, aprovar na segunda tentativa com outro usuário; tela a 400 px.

## Fora do escopo
- Conclusão da OS: PRD-06a1. Produto acabado: PRD-06a2. Sessão por PIN e lista permitida: PRD-03b. NC de fornecedor no recebimento: fora do MVP.
- Indicadores de qualidade (taxa de NC por etapa): DSH-03 (Fase 4).

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fase 3, tabela 1.1, itens 76–79 e 94):
1. Segregação: quem fez qualquer etapa da tentativa não aprova; sem outro aprovador elegível, permite com a marca "Autoinspeção" e notifica o Proprietário (CA 5).
2. Responsável padrão da NC: quem reprovou (CA 6; corrige o CA 4 antigo).
3. Custo da NC: informativo, fora do realizado (CA 10).
4. Aprovar a última etapa deixa a OS "Pronta para concluir", nunca `Completed`; a conclusão é da PRD-06a1 (D12, CA 3).
5. Qualidade no tablet: ação nova `production.approve_quality` (ADR 0003 adendo), usável por PIN, que só aprova ou reprova a qualidade; a fila e a decisão exigem `production.approve_quality`; a Supervisão ganha a ação pelo seed patch `production-quality` (D6, CA 1, 2, 13).

**Pré-condição externa:** nenhuma.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers) e E2E passando.
- [ ] Migration criada (não destrutiva), testada sobre cópia do banco do tenant `konebras`.
- [ ] Permissões só do `ModuleCatalog` (`production.approve_quality`, ADR 0003 adendo); dono de anexo `NonConformity` registrado (ADR 0019 §7 adendo).
- [ ] Telas com estados, inclusive a 400 px e no `/fab` (kit chão de fábrica).
- [ ] Ajudas novas e atualizadas; teste de cobertura passando.
- [ ] Notificações acionáveis, diálogos de impacto e próximos passos aplicados.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` e `docs/04-fluxos-estados.md` §1 (qualidade → "Pronta para concluir", D12) atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `production.approve_quality` no `ModuleCatalog`; seed patch `production-quality` (Supervisão de produção).
- [ ] `NonConformity` (Domain) e as transições de qualidade na OS, com tentativa e "Autoinspeção".
- [ ] `QualityService` (aprovar, reprovar, elegíveis) e `NonConformityService` (ação, encerrar, anexos).
- [ ] Endpoints `/api/v1/production/quality` (separados dos de supervisão) e `/non-conformities`; telas no `/fab` e no backoffice; ajudas.

## Quality gates
- Agentes previstos: @dev, @ux-design-expert (reprovação no tablet), @qa.
- Gate: CA 3, 5, 6, 7, 13 e 14 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fase 3).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK (renumerados) |
| 5 | CAs de segurança e de permissão (3 camadas) | OK (ajustado: CA 5 e 13) |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: Marco 3.1) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK |

- **Correções feitas na validação (21/10/2026, primeira validação):** a mesma etapa de qualidade é reaproveitada por tentativa, sem criar etapa nova; custo da NC informativo para não contar o retrabalho duas vezes; divergência com o `04-fluxos` (`Quality → Completed`) registrada como ponto em aberto (4).
- **Correções da revalidação (21/10/2026):** ação nova `production.approve_quality` no catálogo, usável por PIN e recusada fora da qualidade (D6, CA 1, 13); fila e decisão só com `production.approve_quality` (a `production.approve` fica com a conclusão da OS), seed patch `production-quality` para a Supervisão, e filtro "Aguardando você"; segregação por tentativa com a marca "Autoinspeção" e aviso ao Proprietário, no lugar da regra do Proprietário único (ponto 76); responsável padrão = quem reprovou (ponto 77); última etapa → "Pronta para concluir", nunca `Completed`, e próximo passo "Concluir OS" só para `production.approve` (D12); anexo `NonConformity` pelo adendo da ADR 0019 §7 com limite de envios; diálogos de impacto; dependências só da PRD-03a. CAs renumerados (15).
- **Pré-condições para `Ready`:** nenhuma de decisão de story.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO (D6, D12, tabela 1.1 itens 76–79 e 94) e correções da revisão pré-Fase 3 (§2.1 fluxos D12, ADR 0019 §7 adendo; §2.2 ponto 76; §2.3 padrões 1, 4, 5); nova ordem (Marco 3.1) | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
| 21/10/2026 | 0.5 | Fechamento da revisão: aprovar e reprovar a qualidade só com `production.approve_quality` (D6, CA 1, 2, 3, 5, 6, 12), seed patch `production-quality` para a Supervisão; a conclusão da OS segue com `production.approve` | River (SM) |
