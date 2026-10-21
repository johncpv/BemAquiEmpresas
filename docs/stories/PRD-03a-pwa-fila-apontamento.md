# PRD-03a — App do operador: fila, apontamento e checklist

| Campo | Valor |
|---|---|
| ID | PRD-03a |
| Título | Tela dedicada do operador (`/fab`, sem dock, alvos ≥ 56 px): minha fila de etapas, iniciar, pausar com motivo, retomar e finalizar etapa com checklist obrigatório e cronômetro; apontamento por funcionário (início, fim, pausas e motivo); avanço da etapa e da OS, projeto em produção; conclusão e apontamento pelo supervisor; tempo real no Kanban |
| Tamanho | G (quebra da PRD-03: o acesso rápido por PIN e a tolerância a queda de rede foram para a PRD-03b) |
| Status | Draft |
| Requisitos | R-PRD-3 (operador vê a fila, inicia/pausa/finaliza; apontamento com funcionário, início, fim, pausas e motivo) · R-PRD-6 (checklist por etapa) · PRD §6 (Usabilidade: PWA com botões grandes, uso com luva, tablet de entrada) · R-PRP-5 · R-SEC-5 |
| Dependências | PRD-01 (OS e etapas), ENG-02 (`IBoms.IsReleasedAsync`), PRD-02 (`ProductionHub`), CAD-04b/04c (funcionário ligado ao usuário, `IEmployees` com `Status`), EST-03 (pedido de material pelo operador), FND-09b (layout `/fab`) |
| Bloqueia | PRD-03b, PRD-04 (custo das horas), PRD-05 (etapa de qualidade), PNL-01 |
| Ordem de execução | 15 (Marco 3.4 "Fábrica rodando") |

## User story
Como **operador**, quero **ver no tablet a minha fila e apontar início, pausa e fim de cada etapa com poucos toques**, e como **supervisor**, quero **apontar por quem não conseguiu e concluir etapas quando preciso**, para **cada hora trabalhada virar dado real de prazo e de custo**.

## Contexto e referências
- PRD §5.7 R-PRD-3 e R-PRD-6; §6 Usabilidade; riscos (`06`): "Adoção no chão de fábrica (operador não apontar)" → mitigação "PWA simples, poucos toques, painel TV mostrando o efeito do apontamento; supervisor pode apontar por eles".
- `04-fluxos-estados` §1 "OS (fabricação)": `Open ─(BOM liberada e primeira etapa iniciada)─► InProgress ◄─► Paused`; etapa `Waiting → Ready → InProgress ↔ Paused → Done`; só inicia com a anterior `Done`. §1 "Projeto": `Planned → InProgress` (primeira etapa de OS iniciada). §2: `StepStatusChanged` → SignalR; `LaborEntryClosed` → custo (PRD-04).
- `03-modelo-dados` → `production.labor_entries` (`order_step_id`, `employee_id`, `started_at`, `ended_at`, `paused_minutes`, `hourly_cost_snapshot`, `cost`, `notes`), `production.step_pauses` (`labor_entry_id`, `reason` Material/Machine/Break/Other, `started_at`, `ended_at`); `identity.users.employee_id` (CAD-04b).
- `ModuleCatalog`: `shopfloor` ("App do operador", `view`, `create` = "apontar"). Seed: Operadores `shopfloor.view/create`; Supervisão `shopfloor.view/create` e `production.*`.
- `06` **P-12 (decidida):** tablet Android de entrada, providenciado pela Konebras.
- Protótipo (App do operador): "Linha 1, sexta 16/10 · Olá, Diego"; cartão "OS-2026-00041, etapa Montagem"; cronômetro "02:14:36 · Em andamento desde 14:28. Padrão da etapa: 18 h, apontadas 21 h."; checklist com caixas; botões "Pausar" e "Finalizar etapa"; "Próximas na sua fila"; textos "Pausa com motivo. Faltas de material avisam o almoxarifado." e "Checklist obrigatório. A etapa só finaliza com os itens marcados".
- `Ajuda/fab.md` já existe (estrutura da Fase 0).

## Critérios de aceite
**Acesso e fila**
1. **Dado** um usuário com `shopfloor.view` ligado a um funcionário ativo (CAD-04b), **quando** abre `/fab` (depois do login normal; o PIN é da PRD-03b), **então** vê a tela dedicada sem dock nem topbar do backoffice, com o nome ("Olá, {primeiro nome}"), a linha do centro de trabalho padrão do cargo e a data, e a **minha fila**: a etapa em andamento dele (se houver) em destaque e as próximas etapas `Ready` dos centros do cargo dele (`job_positions.default_work_center_id`; Pontos em aberto 1), ordenadas pela prioridade da OS e pela data prometida do projeto.
2. **Dado** um usuário sem funcionário ligado, ou um funcionário afastado ou desligado (`IEmployees.Status` = `OnLeave`/`Terminated`, CAD-04c), **quando** abre `/fab`, **então** vê o estado "Seu usuário não está ligado a um funcionário ativo. Fale com o supervisor." e a API de apontamento responde 403 `employee_not_active`.
3. **Dado** a fila, **quando** toco numa etapa, **então** abro a etapa com OS, produto, projeto e cliente, o `StageIndicator` da OS, horas padrão, horas já apontadas na etapa (todos os operadores), o checklist, os anexos técnicos da OS (só visualização) e os botões grandes.

**Iniciar, pausar, retomar, finalizar**
4. **Dado** `shopfloor.create` e uma etapa `Ready` de uma OS com BOM liberada (`IBoms.IsReleasedAsync`; sem BOM, a etapa Projeto é a exceção, Pontos em aberto 2), **quando** toco "Iniciar etapa", **então** na mesma transação nasce o apontamento (funcionário, `started_at` = agora UTC), a etapa vai a `InProgress`, a OS vai a `InProgress` se estava `Open` (com `actual_start`), e o evento `ProductionStepStarted` (`{ productionOrderId, projectId, stepId, startedAt }`) vai para o outbox; o consumidor do Commercial passa o projeto a `InProgress` se ainda estava `Planned`; o cronômetro começa; o push do Kanban sai depois do commit (PRD-02).
5. **Dado** um funcionário com um apontamento aberto, **quando** tenta iniciar outra etapa, **então** 409 "Você já está na etapa {etapa} da {OS}. Finalize ou pause antes." (um apontamento aberto por funcionário; índice único parcial).
6. **Dado** uma etapa em andamento, **quando** toco "Pausar" e escolho o motivo (Falta de material, Máquina parada, Intervalo, Outro + texto até 120), **então** nasce a pausa (`step_pauses`), o cronômetro para e o cartão mostra "Pausada: {motivo}"; a etapa fica `Paused` quando nenhum outro operador está nela, e a OS fica `Paused` quando nenhuma etapa dela está em andamento (`04-fluxos` §1); "Falta de material" notifica quem tem `inventory.create` ("{operador} parou a {OS}, etapa {etapa}, por falta de material.", link para a aba Material da OS; R-PRP-5) e oferece "Pedir material" (EST-03 CA 5).
7. **Dado** uma etapa pausada, **quando** toco "Retomar", **então** a pausa fecha (`ended_at`), o cronômetro continua e a etapa e a OS voltam a `InProgress`.
8. **Dado** uma etapa em andamento, **quando** marco todos os itens do checklist (cada item com alvo ≥ 56 px, marcação gravada com usuário e data) e toco "Finalizar etapa", **então** o apontamento fecha (`ended_at`, `paused_minutes` = Σ pausas), o evento `LaborEntryClosed` (`{ laborEntryId, productionOrderId, projectId, stepId, employeeId, minutes }`) vai para o outbox (o custo é da PRD-04), e a etapa vai a `Done` com `actual_end`; a próxima etapa passa a `Ready`; com checklist incompleto, o botão fica desabilitado com "Marque os {n} itens que faltam" e a API responde 422 com a lista.
9. **Dado** mais de um operador na mesma etapa (Pontos em aberto 3), **quando** um deles toca "Finalizar etapa", **então** o diálogo pergunta "Encerrar só o seu apontamento" ou "Concluir a etapa" (concluir fecha os apontamentos dos outros com `ended_at` = agora e a marca "Encerrado por {nome}"); só "Concluir a etapa" exige o checklist completo.
10. **Dado** uma etapa marcada "Exige aprovação para concluir", **quando** o operador finaliza, **então** a etapa fica "Aguardando aprovação" (proposta: estado de exibição, a etapa continua `InProgress` sem apontamento aberto) e quem tem `production.approve` recebe a notificação; ao aprovar na OS, a etapa vai a `Done`.
11. **Dado** uma etapa de qualidade, **quando** chega a vez dela, **então** ela aparece só na fila de quem tem `production.approve`, e o "Finalizar" é trocado por "Aprovar" e "Reprovar" da PRD-05; até a PRD-05 entrar, a OS fica parada em `Quality` com o chip "Aguardando qualidade".
12. **Dado** a última etapa `Done` (não sendo de qualidade), **quando** grava, **então** a OS fica com todas as etapas concluídas e o chip "Pronta para concluir"; a conclusão da OS (série e produto acabado) é da PRD-06a.

**Supervisor**
13. **Dado** `production.edit` + `shopfloor.create`, **quando** abro a OS → aba Apontamentos e clico "Apontar por funcionário", escolho o funcionário ativo, a etapa, o início e o fim (ou "agora") e as pausas, **então** grava o apontamento com a marca "Apontado por {supervisor}" e o mesmo efeito dos CA 4 e 8; início no futuro ou fim antes do início: 422.
14. **Dado** `production.approve`, **quando** clico "Concluir etapa" na OS (ex.: operador esqueceu), **então** a etapa vai a `Done` com motivo obrigatório, fecha os apontamentos abertos dela com `ended_at` = agora e a marca "Encerrado pelo supervisor"; e "Corrigir apontamento" (início, fim, pausas) com motivo grava a correção auditada e publica `LaborEntryCorrected` (`{ laborEntryId }`) para a PRD-04 refazer o custo.

**Tempo real e tela**
15. **Dado** qualquer início, pausa, retomada ou fim, **quando** a transação faz commit, **então** o Kanban (PRD-02) e o cartão da etapa no PWA recebem o push (`OrderBoardChanged`, só o id) e atualizam em até 2 s (PRD §6).
16. **Dado** o tablet Android de entrada (P-12) a 800 × 1280 e o celular a 400 px, **quando** uso o PWA, **então** todos os alvos de toque têm no mínimo 56 × 56 px, o texto principal ≥ 18 px, contraste AA, uso com luva (sem gesto fino nem toque duplo), o cronômetro em `tabular-nums` e nenhum dado de custo aparece; o tema segue o do usuário.

**Permissões e segurança**
17. **Dado** o módulo `shopfloor` não liberado, **quando** abro `/fab`, **então** a tela `ModuleNotReleased` e a API 403 `module_not_released`; sem grupo, 403 `forbidden`; apontar sem `shopfloor.create`: 403. `RequireModule("shopfloor")` e `perm:` nos endpoints do PWA; os do supervisor com `production.*`.
18. **[Segurança]** **Dado** um operador, **quando** tenta iniciar, pausar ou finalizar etapa em nome de outro funcionário, ou mexer no apontamento de outro, **então** 403: o funcionário do apontamento é sempre o ligado ao usuário logado (nunca vem do corpo), exceto no "Apontar por funcionário" do supervisor (CA 13); IDOR em `/production-orders/{id}/steps/{stepId}` e `/labor-entries/{id}`.
19. **[Segurança]** **Dado** dois toques em "Iniciar" (ou dois tablets), **quando** chegam juntos, **então** só um apontamento nasce (`Idempotency-Key` por toque e índice único do apontamento aberto) e a etapa não muda duas vezes (`xmin`).
20. **Dado** qualquer apontamento, pausa, correção ou conclusão pelo supervisor, **quando** abro a aba Apontamentos ou o histórico da OS, **então** vejo funcionário, início, fim, pausas com motivo, minutos líquidos, quem registrou e as correções com antes/depois.

## Regras de negócio e técnicas
- **Apontamento** (03 + proposta): `labor_entries` + `recorded_by` (supervisor), `closed_by`, `close_kind` (Operator/Supervisor/StepCompleted), `corrected_at`, `xmin`; índice único parcial `(employee_id) WHERE ended_at IS NULL`. Minutos líquidos = (fim − início) − Σ pausas; horas `numeric(18,4)`.
- **Situação da etapa e da OS** pelas entidades rastreadas (ADR 0019 §2); `Paused` da OS calculado a partir das etapas (CA 6, 7).
- **Funcionário** sempre resolvido do usuário logado (`IUserEmployeeLink.UserOfEmployeeAsync` no sentido inverso, proposta `EmployeeOfUserAsync`) e conferido por `IEmployees` (ativo).
- **Endpoints do PWA** no Production, com `RequireModule("shopfloor")` (o `shopfloor` é módulo do catálogo sem projeto próprio; proposta).
- **Instantes** em `timestamptz` (UTC); exibição em São Paulo; o relógio do servidor vale (o do tablet não é confiado nesta story; offline na PRD-03b).
- **Sem custo** nesta story: `hourly_cost_snapshot` e `cost` nascem na PRD-04.
- Guid v7; auditoria `IAuditable` em apontamento, pausa e checklist.

## Permissões, eventos e tabelas
- **Permissões:** `shopfloor.view`, `shopfloor.create` (apontar); `production.edit` + `shopfloor.create` (apontar por funcionário); `production.approve` (concluir etapa, corrigir apontamento, aprovar etapa).
- **Eventos (`Production.Contracts`):** `ProductionStepStarted` (consumido pelo Commercial: projeto `InProgress`), `LaborEntryClosed` e `LaborEntryCorrected` (consumidos na PRD-04). SignalR `OrderBoardChanged`.
- **Contratos:** `IEmployees` (com `Status`, CAD-04c), `IUserEmployeeLink` (+ funcionário do usuário), `IBoms`, `IMaterialRequisitions`.
- **Tabelas:** `production.labor_entries`, `production.step_pauses`; marcação do checklist em `production.production_order_checklist` (com `step_id`, PRD-01).

## Telas e estados
- **`/fab`** (fila), **`/fab/etapa/$stepId`** (etapa com cronômetro, checklist e botões), layout dedicado (`DedicatedLayouts`), sem dock; **OS → aba Apontamentos** no backoffice.
- **Verbos:** "Iniciar etapa" / "Etapa iniciada"; "Pausar" / "Etapa pausada"; "Retomar" / "Etapa retomada"; "Finalizar etapa" / "Etapa finalizada: {h} h apontadas"; "Apontar por funcionário" / "Apontamento registrado"; "Concluir etapa" / "Etapa concluída".
- **Estados:** carregando (skeleton grande); fila vazia ("Nada na sua fila agora. Fale com o supervisor se estiver esperando uma etapa."); erro com "Tentar de novo" grande; sem funcionário (CA 2).
- **Propositivo:** a etapa em andamento abre direto ao entrar; depois de finalizar, a próxima da fila aparece com "Iniciar próxima: {etapa} da {OS}"; o motivo de pausa mais usado pelo operador vem primeiro; "Pedir material" na pausa por falta.
- **Ajuda:** `Ajuda/fab.md` (reescrita: fila), `Ajuda/fab-etapa.md` (rota `/fab/etapa/$stepId`), `Ajuda/producao-os-apontamentos.md` (aba `apontamentos`); `<Dica>` em "Horas padrão" e "Pausa".

## Plano de testes
- **Unidade:** máquina de estados da etapa e da OS com pausa de vários operadores (CA 4–12); minutos líquidos com pausas; fila (ordem e centro).
- **Integração (Testcontainers):** acesso sem funcionário e afastado (CA 2); iniciar com BOM liberada, OS e projeto em produção pelo evento (CA 4); um apontamento aberto por funcionário (CA 5); pausa, notificação de falta, retomada (CA 6, 7); finalizar com checklist e `LaborEntryClosed` (CA 8); vários operadores (CA 9); etapa com aprovação (CA 10); qualidade bloqueada até a PRD-05 (CA 11); supervisor aponta, conclui e corrige (CA 13, 14); push depois do commit (CA 15); três camadas, IDOR e funcionário do corpo ignorado (CA 17, 18); toques duplicados (CA 19); auditoria (CA 20).
- **E2E (Playwright):** operador entra, inicia, pausa por falta de material, retoma e finaliza com checklist, num viewport de tablet (800 × 1280) e de celular (400 px), conferindo os alvos ≥ 56 px com `boundingBox`; o Kanban em outra aba atualiza.
- **Manual:** teste num tablet Android de entrada (P-12) com luva, registrado na story.

## Fora do escopo
- PIN, dispositivo registrado e funcionamento sem rede: PRD-03b.
- Custo das horas: PRD-04. Aprovar/reprovar qualidade e NC: PRD-05. Conclusão da OS: PRD-06a.
- Ponto eletrônico do RH (RH-4): Fase 6; apontamento de produção não é ponto.
- Leitura de QR code da OS: fora desta story (Pontos em aberto 4).

## Pontos em aberto
1. **Fila do operador** (PO): proposta: etapas `Ready` dos centros do cargo do funcionário (centro padrão do cargo); alternativas: todas as etapas `Ready` da linha, ou etapas atribuídas pelo supervisor.
2. **Etapa Projeto sem BOM** (PO): o `04-fluxos` diz que a OS passa a `InProgress` com a BOM liberada e a primeira etapa iniciada, mas a BOM é criada **na** etapa Projeto (R-ENG-3). Proposta: a etapa Projeto pode ser iniciada sem BOM; as etapas seguintes só iniciam com a BOM liberada.
3. **Vários operadores na mesma etapa** (PO): proposta: permitido, cada um com o seu apontamento (CA 9); alternativa: um operador por etapa.
4. **QR code da OS** (PO): proposta: fora do MVP da PRD-03; a leitura por câmera entra com o Patrimônio (PAT-02) ou quando o PO pedir.
5. **Funcionário do usuário** (arquiteto): proposta: `IUserEmployeeLink.EmployeeOfUserAsync(userId)` no `Identity.Contracts`, sem o Production ler `identity.users`.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers) e E2E (tablet e 400 px) passando; teste manual no tablet registrado.
- [ ] Migration criada (não destrutiva), testada sobre cópia do banco do tenant `konebras`.
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Telas dedicadas com estados, alvos ≥ 56 px e sem dock.
- [ ] Ajudas `/fab`, `/fab/etapa/$stepId` e aba Apontamentos; teste de cobertura passando.
- [ ] Próximo passo e alertas acionáveis (falta de material) aplicados.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` e `docs/04-fluxos-estados.md` atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `LaborEntry` e `StepPause` (Domain); máquina de estados da etapa e da OS.
- [ ] `ShopfloorService` (fila, iniciar, pausar, retomar, finalizar) e `SupervisorLaborService` (apontar por, concluir, corrigir).
- [ ] Consumidor de `ProductionStepStarted` no Commercial (projeto `InProgress`).
- [ ] Endpoints `/api/v1/shopfloor/*` (`RequireModule("shopfloor")`) e `/api/v1/production/production-orders/{id}/labor-entries`.
- [ ] Páginas `/fab` e `/fab/etapa/$stepId`, aba Apontamentos, ajudas.

## Quality gates
- Agentes previstos: @dev, @ux-design-expert (PWA de chão de fábrica, luva, 56 px), cybersecurity (funcionário do token, IDOR), @qa.
- Gate: CA 4, 5, 8, 16, 18 e 19 obrigatórios.

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

- **Correções feitas na validação:** quebra da PRD-03 (PIN e offline na PRD-03b); funcionário sempre do usuário logado (CA 18); afastado não aponta (CA 2, depende da CAD-04c); o conflito "OS inicia com BOM liberada" × "BOM criada na etapa Projeto" virou ponto em aberto (2), sem decidir; supervisor aponta por funcionário (mitigação do risco de adoção).
- **Pré-condições para `Ready`:** pontos em aberto 1, 2 e 3; CAD-04c concluída.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da PRD-03) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
