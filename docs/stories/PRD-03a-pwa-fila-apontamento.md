# PRD-03a — App do operador: fila, apontamento e checklist

| Campo | Valor |
|---|---|
| ID | PRD-03a |
| Título | Tela dedicada do operador (`/fab`, sem dock, kit "chão de fábrica"): fila com todas as etapas prontas (as do meu centro primeiro), busca da OS pelo número, iniciar, pausar com motivo, retomar, sair da etapa e finalizar com checklist obrigatório, cronômetro e confirmação; apontamento por funcionário (início, fim, pausas e motivo); avanço da etapa e da OS, projeto em produção; apontamento, correção e conclusão pelo supervisor; apontamentos abertos há muito tempo; tempo real no Kanban |
| Tamanho | G (quebra da PRD-03: o PIN e o aparelho registrado foram para a PRD-03b) |
| Status | Ready |
| Requisitos | R-PRD-3 (operador vê a fila, inicia/pausa/finaliza; apontamento com funcionário, início, fim, pausas e motivo) · R-PRD-6 (checklist por etapa) · PRD §6 (Usabilidade: PWA com botões grandes, uso com luva, tablet de entrada) · R-PRP-5 · R-SEC-5 |
| Dependências | PRD-01, PRD-02, CAD-04c1 |
| Bloqueia | PRD-04, PRD-05, PRD-03b, ENG-02, EST-03 |
| Ordem de execução | 5 (Marco 3.1 "Fábrica visível") |

## User story
Como **operador**, quero **ver no tablet as etapas prontas e apontar início, pausa e fim de cada uma com poucos toques**, e como **supervisor**, quero **apontar por quem não conseguiu, corrigir e concluir etapas quando preciso**, para **cada hora trabalhada virar dado real de prazo e de custo**.

## Contexto e referências
- PRD §5.7 R-PRD-3 e R-PRD-6; §6 Usabilidade; riscos (`06`): "Adoção no chão de fábrica (operador não apontar)" → mitigação "PWA simples, poucos toques, painel TV mostrando o efeito do apontamento; supervisor pode apontar por eles".
- `04-fluxos-estados` §1 "OS (fabricação)": etapa `Waiting → Ready → InProgress ↔ Paused → Done`; só inicia com a anterior `Done`; a D12 muda a regra da BOM (a primeira etapa inicia sem BOM) e acrescenta "Pronta para concluir". §1 "Projeto": `Planned → InProgress` (primeira etapa de OS iniciada). §2: `StepStatusChanged` → SignalR; `LaborEntryClosed` → custo (PRD-04).
- `03-modelo-dados` → `production.labor_entries` (`order_step_id`, `employee_id`, `started_at`, `ended_at`, `paused_minutes`, `hourly_cost_snapshot`, `cost`, `notes`), `production.step_pauses` (`labor_entry_id`, `reason`, `started_at`, `ended_at`); `identity.users.employee_id` (CAD-04b).
- `ModuleCatalog`: `shopfloor` ("App do operador", `view`, `create` = "apontar"). Seed: Operadores `shopfloor.view/create`; Supervisão `shopfloor.view/create` e `production.*`.
- `06` **P-12 (decidida):** tablet Android de entrada, providenciado pela Konebras.
- **Revisão pré-Fase 3** (`docs/revisoes/2026-10-21-revisao-pre-fase3.md`): D1 (Marco 3.1 sem estoque e sem BOM), D11 (fila), D12 (etapas sem BOM e conclusão explícita), tabela 1.1 itens 63 a 67 e 95, §2.1 "Outros" (`EmployeeOfUserAsync`, avisos de tempo real depois do commit), §2.2 PRD-01 e PRD-03a, §2.3 padrões 1, 3 e 4 e os achados pontuais (motivos de pausa, apontamentos abertos, dock "Operador").
- ADR 0019 §2 (`xmin`), §3 (idempotência), §7 e adendo de 21/10/2026 (dono `ProductionOrder`: o operador lê pelo endpoint do chão de fábrica que confere a fila dele); ADR 0021 (mensagens); ADR 0026 §1 (tema por aparelho, aplicado na PRD-03b).
- Protótipo (App do operador): "Linha 1, sexta 16/10 · Olá, Diego"; cartão "OS-2026-00041, etapa Montagem"; cronômetro "02:14:36 · Em andamento desde 14:28. Padrão da etapa: 18 h, apontadas 21 h."; checklist com caixas; botões "Pausar" e "Finalizar etapa"; "Próximas na sua fila"; textos "Pausa com motivo. Faltas de material avisam o almoxarifado." e "Checklist obrigatório. A etapa só finaliza com os itens marcados".
- `Ajuda/fab.md` já existe (estrutura da Fase 0).

## Critérios de aceite
**Acesso e fila**
1. **Dado** um usuário com `shopfloor.view` ligado a um funcionário ativo (`IUserEmployeeLink.EmployeeOfUserAsync` e `IEmployees.Status` = `Active`, CAD-04c1), **quando** abre `/fab` (depois do login normal; o PIN é da PRD-03b), **então** vê a tela dedicada sem dock nem topbar do backoffice, com o nome ("Olá, {primeiro nome}"), o centro de trabalho padrão do cargo e a data, e a **fila**: a etapa em andamento dele (se houver) em destaque e **todas as etapas `Ready`** das OS não canceladas do tenant, primeiro as do centro do cargo dele (`job_positions.default_work_center_id`) e depois as dos outros centros, cada grupo ordenado pela prioridade da OS e pela data prometida do projeto; cada cartão mostra o centro da etapa.
2. **Dado** a fila, **quando** ligo o filtro "Só meu centro", **então** a lista mostra só as etapas do centro do cargo; o filtro fica lembrado no aparelho (armazenamento local, com o padrão desligado se não houver).
3. **Dado** a fila, **quando** toco "Buscar OS pelo número" e digito o número no teclado numérico grande, **então** vejo as etapas `Ready`, em andamento ou pausadas daquela OS; OS inexistente, cancelada ou de outro tenant mostra "OS não encontrada." (sem leitura de QR code).
4. **Dado** um usuário sem funcionário ligado, ou um funcionário afastado ou desligado (`IEmployees.Status` = `OnLeave`/`Terminated`, CAD-04c1), **quando** abre `/fab`, **então** vê o estado "Seu usuário não está ligado a um funcionário ativo. Fale com o supervisor." e a API de apontamento responde 403 `employee_not_active`.
5. **Dado** a fila, **quando** toco numa etapa, **então** abro a etapa com OS, produto, projeto e cliente, o `StageIndicator` da OS (padrão 3), horas padrão, horas já apontadas na etapa (todos os operadores), quem está nela agora, o checklist, os anexos técnicos da OS (só visualização) e os botões grandes.
6. **[Segurança]** **Dado** um anexo técnico da OS (dono `ProductionOrder`, PRD-01), **quando** o operador abre, **então** o download vem pelo endpoint do chão de fábrica (`/api/v1/shopfloor/steps/{stepId}/attachments/{attachmentId}`), que confere que a etapa está na fila do operador (OS não cancelada, do tenant, etapa `Ready`, em andamento ou pausada) e que o anexo é daquela OS; fora disso, 404; pdf, png e jpg abrem na tela, dwg e dxf só com "Baixar". O operador não envia anexo nesta story.

**Iniciar, pausar, retomar, sair e finalizar**
7. **Dado** `shopfloor.create` e uma etapa `Ready`, **quando** toco "Iniciar etapa", **então** na mesma transação nasce o apontamento (funcionário do usuário logado, `started_at` = agora UTC, `recorded_by` = usuário), a etapa vai a `InProgress`, a OS vai a `InProgress` se estava `Open` (com `actual_start`), e o evento `ProductionStepStarted` (`{ productionOrderId, projectId, stepId, startedAt }`) vai para o outbox; o evento e o consumidor do Commercial (projeto a `InProgress` se ainda estava `Planned`) já existem desde a PRD-02 e são reaproveitados; o cronômetro começa; o push do Kanban sai depois do commit (PRD-02).
8. **[Segurança]** **Dado** um pedido de início, **quando** a etapa não está `Ready` (409 `step_not_ready`), é de OS cancelada (409 `order_cancelled`) ou é de outro tenant (404), **então** o servidor recusa; o servidor **não** restringe por centro de trabalho (D11), e o apontamento registra o funcionário e o usuário que apontou.
9. **Dado** a regra da BOM (D12), **quando** o operador inicia a **primeira** etapa do roteiro, **então** ela inicia sem BOM; **a partir da ENG-02**, as etapas seguintes só iniciam com a BOM da OS liberada (`IBoms.IsReleasedAsync`, 409 `bom_not_released` com "A BOM desta OS ainda não foi liberada. Fale com a Engenharia."); até a ENG-02, todas as etapas iniciam sem conferir BOM (a ENG-02 liga o bloqueio no início de etapa desta story e na "Concluir etapa" do supervisor, ENG-02 CA 16).
10. **Dado** um funcionário com um apontamento aberto, **quando** tenta iniciar outra etapa, **então** 409 "Você já está na etapa {etapa} da {OS}. Finalize ou pause antes." (um apontamento aberto por funcionário; índice único parcial).
11. **Dado** uma etapa em andamento, **quando** toco "Pausar" e escolho o motivo na folha de tela cheia (Falta de material, Máquina parada, Intervalo, Fim do turno ou Outro, com texto opcional até 120), **então** nasce a pausa (`step_pauses`), o cronômetro para e o cartão mostra "Pausada: {motivo}"; a etapa fica `Paused` quando nenhum outro operador está nela, e a OS fica `Paused` quando nenhuma etapa dela está em andamento (`04-fluxos` §1); "Falta de material" notifica quem tem `inventory.create` ("{operador} parou a {OS}, etapa {etapa}, por falta de material.", com link para a OS; R-PRP-5). O botão "Pedir material" não é desta story (EST-03 entrega a API e o botão).
12. **Dado** uma etapa pausada, **quando** toco "Retomar", **então** a pausa fecha (`ended_at`), o cronômetro continua e a etapa e a OS voltam a `InProgress`.
13. **Dado** uma etapa em andamento só comigo, **quando** marco todos os itens do checklist (cada item com alvo de 56 × 56 px, marcação gravada com usuário e data) e toco "Finalizar etapa", **então** abre a folha de confirmação de tela cheia "Finalizar etapa · {h} h apontadas", com o antes → depois ("{etapa}: Em andamento → Concluída"; "Próxima: {etapa seguinte} fica pronta") e os botões "Finalizar etapa" e "Voltar"; confirmado, o apontamento fecha (`ended_at`, `paused_minutes` = Σ pausas), o evento `LaborEntryClosed` (`{ laborEntryId, productionOrderId, projectId, stepId, employeeId, minutes }`) vai para o outbox (o custo é da PRD-04), a etapa vai a `Done` com `actual_end` e a próxima etapa passa a `Ready`; a mensagem diz "Etapa finalizada: {h} h apontadas". Com checklist incompleto, o botão fica desabilitado com "Marque os {n} itens que faltam" e a API responde 422 com a lista. "Pausar" fica separado de "Finalizar etapa", com 16 px entre as duas ações.
14. **Dado** mais de um operador na mesma etapa, **quando** um deles abre a etapa, **então** vê "Sair da etapa (só eu)" e "Concluir etapa para todos" no lugar de "Finalizar etapa": "Sair da etapa (só eu)" fecha só o apontamento dele, sem exigir o checklist, e a etapa continua em andamento; "Concluir etapa para todos" exige o checklist completo, abre a folha de confirmação do CA 13 com os nomes de quem será encerrado e fecha os apontamentos dos outros com `ended_at` = agora e a marca "Encerrado por {nome}".
15. **Dado** uma etapa marcada "Exige aprovação para concluir", **quando** o operador finaliza, **então** a etapa fica "Aguardando aprovação" (estado de exibição: a etapa continua `InProgress` sem apontamento aberto) e quem tem `production.approve` recebe a notificação; ao aprovar na OS, a etapa vai a `Done`.
16. **Dado** uma etapa de qualidade, **quando** chega a vez dela, **então**, a partir da PRD-05, ela aparece só na fila de quem tem `production.approve_quality` (D6), com "Aprovar" e "Reprovar" da PRD-05 no lugar de "Finalizar"; o operador sem essa ação recebe 403 ao tentar iniciá-la; até a PRD-05 entrar, a OS fica parada em `Quality` com o chip "Aguardando qualidade".
17. **Dado** a última etapa `Done` (não sendo de qualidade), **quando** grava, **então** a OS fica com todas as etapas concluídas e o chip "Pronta para concluir" (D12); a conclusão da OS é explícita, com `production.approve`, na PRD-06a1.

**Supervisor**
18. **Dado** `production.edit` + `shopfloor.create`, **quando** abro a OS → aba Apontamentos e clico "Apontar por funcionário", escolho o funcionário ativo, a etapa, o início e o fim (ou "agora") e as pausas, **então** grava o apontamento com a marca "Apontado por {supervisor}" e o mesmo efeito dos CA 7 e 13; início no futuro ou fim antes do início: 422.
19. **[Segurança]** **Dado** o "Apontar por funcionário", **quando** o intervalo se sobrepõe a outro apontamento do mesmo funcionário (409 "{funcionário} já tem apontamento entre {início} e {fim}.") ou o início é mais antigo que 7 dias (422 "Apontamento retroativo só até 7 dias."), **então** o servidor recusa.
20. **Dado** `production.approve`, **quando** clico "Concluir etapa" na OS (ex.: operador esqueceu), **então** a etapa vai a `Done` com motivo obrigatório e fecha os apontamentos abertos dela com `ended_at` = agora e a marca "Encerrado pelo supervisor"; e "Corrigir apontamento" (início, fim, pausas) com motivo grava a correção auditada e publica `LaborEntryCorrected` (`{ laborEntryId }`) para a PRD-04 refazer o custo; a correção segue as regras do CA 19.
21. **[Segurança]** **Dado** o supervisor, **quando** tenta corrigir um apontamento dele mesmo (funcionário ligado ao usuário), aprovar uma etapa em que tem apontamento ou concluir pelo CA 20 uma etapa em que apontou, **então** 403 "Quem fez o apontamento não corrige nem aprova a própria etapa." (o dono do apontamento ou da etapa não é quem corrige ou aprova); **exceto** quando não há outro aprovador elegível (nenhum outro usuário ativo com `production.approve` que não tenha apontado a etapa), decidido pelo `SoleOwnerRule` (BuildingBlocks, ADR 0023 §5): aí a ação passa, a correção ou a etapa ganha a marca **"Autoaprovado"** e o Proprietário recebe a notificação "{nome} corrigiu ou aprovou o próprio apontamento na {OS}, etapa {etapa}." com link (síncrona, ADR 0019 §18), a mesma regra aprovada no ponto 76 para a qualidade.
22. **Dado** `production.view`, **quando** abro "Produção → Apontamentos abertos", **então** vejo os "Apontamentos abertos há mais de {x} h" ({x} = 12 h, valor inicial, parâmetro `production.open_labor_alert_hours`), com funcionário, OS, etapa, início e a última pausa; com `production.approve`, cada linha leva direto a "Corrigir apontamento" ou "Concluir etapa"; vazio: "Nenhum apontamento aberto há mais de {x} h.".

**Tempo real e tela**
23. **Dado** qualquer início, pausa, retomada, saída ou fim, **quando** a transação faz commit, **então** o Kanban (PRD-02) e o cartão da etapa no PWA recebem o push (`OrderBoardChanged`, só o id) e atualizam em até 2 s (PRD §6).
24. **Dado** o kit "chão de fábrica" criado nesta story (padrão 1) e o tablet Android de entrada (P-12) a 800 × 1280 e o celular a 400 px, **quando** uso o `/fab`, **então** todo alvo de toque tem no mínimo 56 × 56 px com 8 px entre alvos e 16 px entre ações opostas, o texto principal tem 18 px, o contraste é AA, nenhum gesto fino é exigido (sem arrastar, toque longo nem toque duplo), as mensagens aparecem numa região própria do kit (`aria-live`), toda confirmação é uma folha de tela cheia, o cabeçalho tem o botão "Ajuda" (no `/fab`, a ajuda não abre sozinha) e o cronômetro usa `tabular-nums`.
25. **[Segurança]** **Dado** os DTOs de `/api/v1/shopfloor/*`, **quando** o teste de contrato roda, **então** nenhum traz custo (custo-hora, custo do apontamento, custo médio, valor do item ou da OS) (D7, §2.2); a tela também não mostra.
26. **Dado** o tema do `/fab` (ponto 95), **quando** abro, **então** até a PRD-03b segue a preferência do usuário (automático se ele não escolheu); com a PRD-03b, passa a ser guardado por aparelho registrado, com padrão automático.
27. **Dado** o dock do backoffice (grupo "Telas dedicadas"), **quando** um usuário com `shopfloor.view` toca "Operador", **então** vai para o `/fab`.

**Permissões e segurança**
28. **Dado** o módulo `shopfloor` não liberado, **quando** abro `/fab`, **então** a tela `ModuleNotReleased` e a API 403 `module_not_released`; sem grupo, 403 `forbidden`; apontar sem `shopfloor.create`: 403. `RequireModule("shopfloor")` e `perm:` nos endpoints do PWA; os do supervisor com `production.*`.
29. **[Segurança]** **Dado** um operador, **quando** tenta iniciar, pausar, sair ou finalizar etapa em nome de outro funcionário, ou mexer no apontamento de outro, **então** 403: o funcionário do apontamento é sempre o ligado ao usuário logado (nunca vem do corpo), exceto no "Apontar por funcionário" do supervisor (CA 18); IDOR em `/production-orders/{id}/steps/{stepId}` e `/labor-entries/{id}`.
30. **[Segurança]** **Dado** dois toques em "Iniciar" (ou dois tablets), **quando** chegam juntos, **então** só um apontamento nasce (`Idempotency-Key` por toque e índice único do apontamento aberto) e a etapa não muda duas vezes (`xmin`).
31. **Dado** qualquer apontamento, pausa, saída, correção ou conclusão pelo supervisor, **quando** abro a aba Apontamentos ou o histórico da OS, **então** vejo funcionário, início, fim, pausas com motivo, minutos líquidos, quem registrou (usuário) e as correções com antes/depois.

## Regras de negócio e técnicas
- **Apontamento:** `labor_entries` + `recorded_by` (usuário que apontou), `closed_by`, `close_kind` (Operator/Left/Supervisor/StepCompleted), `corrected_at`, `xmin`; índice único parcial `(employee_id) WHERE ended_at IS NULL`. Minutos líquidos = (fim − início) − Σ pausas; horas `numeric(18,4)`. Nenhuma pausa entra no custo (PRD-04).
- **Motivos de pausa:** `Material`, `Machine`, `Break`, `EndOfShift`, `Other` (texto opcional até 120).
- **Fila:** todas as etapas `Ready` de OS não canceladas; ordem: centro do cargo primeiro, depois prioridade da OS e data prometida (`PromisedDate` do projeto, §2.1). O servidor confere estado e tenant, nunca o centro (D11).
- **Situação da etapa e da OS** pelas entidades rastreadas (ADR 0019 §2); `Paused` da OS calculado a partir das etapas (CA 11, 12).
- **Funcionário** sempre resolvido do usuário logado por `IUserEmployeeLink.EmployeeOfUserAsync(userId)` no `Identity.Contracts` (o Production não lê `identity.users`) e conferido por `IEmployees.Status`.
- **Endpoints do PWA** no Production, em `/api/v1/shopfloor/*`, com `RequireModule("shopfloor")` (o `shopfloor` é módulo do catálogo sem projeto próprio).
- **Kit "chão de fábrica"** no `src/Web` (botão grande, folha de confirmação de tela cheia, região de mensagens, cabeçalho com "Ajuda", teclado numérico): usado depois pela PRD-03b (`/fab/entrar`), pela PRD-05 e pela contagem pelo celular (EST-05).
- **Instantes** em `timestamptz` (UTC); exibição em São Paulo; o relógio do servidor vale.
- **Sem custo** nesta story: `hourly_cost_snapshot` e `cost` nascem na PRD-04.
- Guid v7; auditoria `IAuditable` em apontamento, pausa e checklist.

## Permissões, eventos e tabelas
- **Permissões:** `shopfloor.view`, `shopfloor.create` (apontar); `production.edit` + `shopfloor.create` (apontar por funcionário); `production.view` (apontamentos abertos); `production.approve` (concluir etapa, corrigir apontamento, aprovar etapa); `production.approve_quality` (fila de qualidade, a partir da PRD-05).
- **Eventos (`Production.Contracts`):** `ProductionStepStarted` (consumido pelo Commercial: projeto `InProgress`), `LaborEntryClosed` e `LaborEntryCorrected` (consumidos na PRD-04). SignalR `OrderBoardChanged`.
- **Contratos:** `IEmployees` (com `Status`, CAD-04c1), `IUserEmployeeLink.EmployeeOfUserAsync` (novo, `Identity.Contracts`), `IBoms.IsReleasedAsync` (a partir da ENG-02).
- **Tabelas:** `production.labor_entries`, `production.step_pauses`; marcação do checklist em `production.production_order_checklist` (com `step_id`, PRD-01).

## Telas e estados
- Padrões de tela da Fase 3: 1, 3, 4, 10.
- **`/fab`** (fila, filtro "Só meu centro", "Buscar OS pelo número"), **`/fab/etapa/$stepId`** (etapa com cronômetro, checklist, anexos e botões), layout dedicado (`DedicatedLayouts`), sem dock; no backoffice, **OS → aba Apontamentos** e **Produção → Apontamentos abertos**.
- **Verbos:** "Iniciar etapa" / "Etapa iniciada"; "Pausar" / "Etapa pausada"; "Retomar" / "Etapa retomada"; "Sair da etapa (só eu)" / "Você saiu da etapa"; "Finalizar etapa" / "Etapa finalizada: {h} h apontadas"; "Concluir etapa para todos" / "Etapa concluída"; "Apontar por funcionário" / "Apontamento registrado"; "Corrigir apontamento" / "Apontamento corrigido"; "Concluir etapa" / "Etapa concluída".
- **Estados:** carregando (skeleton grande); fila vazia ("Nada pronto para apontar agora. Fale com o supervisor se estiver esperando uma etapa."); "Só meu centro" vazio ("Nada pronto no seu centro. Desligue o filtro para ver os outros centros."); erro com "Tentar de novo" grande; sem funcionário (CA 4).
- **Propositivo:** a etapa em andamento abre direto ao entrar; depois de finalizar, a próxima da fila aparece com "Iniciar próxima: {etapa} da {OS}"; o motivo de pausa mais usado pelo operador vem primeiro; a falta de material avisa o almoxarifado sem o operador digitar nada.
- **Ajuda:** `Ajuda/fab.md` (reescrita: fila, filtro e busca), `Ajuda/fab-etapa.md` (rota `/fab/etapa/$stepId`), `Ajuda/producao-os-apontamentos.md` (aba `apontamentos`), `Ajuda/producao-apontamentos-abertos.md`; `<Dica>` em "Horas padrão", "Pausa" e "Só meu centro".

## Plano de testes
- **Unidade:** máquina de estados da etapa e da OS com pausa, saída e vários operadores (CA 7–17); minutos líquidos com pausas; ordem da fila e filtro por centro (CA 1, 2); sobreposição e retroativo (CA 19).
- **Integração (Testcontainers):** fila com todas as etapas prontas e busca pelo número (CA 1–3); sem funcionário e afastado (CA 4); anexo pelo endpoint do chão de fábrica, fora da fila 404 (CA 6); iniciar, OS e projeto em produção pelo evento (CA 7); recusa de etapa não pronta, de OS cancelada e de outro tenant, sem restrição por centro (CA 8); primeira etapa sem BOM e as seguintes com BOM (teste com `IBoms` falso, a ligar na ENG-02) (CA 9); um apontamento aberto por funcionário (CA 10); pausa com os cinco motivos, notificação de falta, retomada (CA 11, 12); finalizar com checklist e `LaborEntryClosed` (CA 13); sair e concluir para todos (CA 14); etapa com aprovação (CA 15); qualidade parada até a PRD-05 (CA 16); "Pronta para concluir" (CA 17); supervisor aponta, sobreposição, 7 dias, conclui e corrige (CA 18–20); dono não corrige nem aprova, e a exceção sem outro aprovador elegível com "Autoaprovado" e notificação ao Proprietário (CA 21); apontamentos abertos (CA 22); push depois do commit (CA 23); DTOs sem custo (CA 25); três camadas, IDOR e funcionário do corpo ignorado (CA 28, 29); toques duplicados (CA 30); auditoria (CA 31).
- **E2E (Playwright):** operador entra, liga "Só meu centro", inicia, pausa por falta de material, retoma, confirma na folha "Finalizar etapa · {h} h apontadas"; dois operadores na mesma etapa, um sai e o outro conclui para todos; num viewport de tablet (800 × 1280) e de celular (400 px), conferindo os alvos ≥ 56 px e os espaços de 8 e 16 px com `boundingBox` (CA 24); o Kanban em outra aba atualiza; dock "Operador" leva ao `/fab` (CA 27); tema automático (CA 26).
- **Manual:** teste num tablet Android de entrada (P-12) com luva, registrado na story.

## Fora do escopo
- PIN, aparelho registrado e tema por aparelho: PRD-03b. Funcionamento sem rede: adiado (ADR 0026 §6).
- Botão e API "Pedir material": EST-03. Bloqueio de etapa por BOM: ligado pela ENG-02.
- Custo das horas: PRD-04. Aprovar/reprovar qualidade e NC: PRD-05. Conclusão da OS: PRD-06a1.
- Ponto eletrônico do RH (RH-4): Fase 6; apontamento de produção não é ponto.
- Leitura de QR code da OS (a busca pelo número cobre).
- Envio de anexo pelo operador.

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fase 3, D11, D12 e tabela 1.1, itens 63–67 e 95):
1. **Fila do operador:** todas as etapas prontas, as do centro do operador primeiro, com o filtro "Só meu centro"; o servidor recusa etapa não pronta, de OS cancelada ou de outro tenant, sem restringir por centro, e registra quem apontou (D11).
2. **Etapa sem BOM:** a primeira etapa inicia sem BOM; o bloqueio das seguintes liga a partir da ENG-02; depois da última etapa, a OS fica "Pronta para concluir" e a conclusão é explícita (D12).
3. **Vários operadores na mesma etapa:** permitido, com "Sair da etapa (só eu)" e "Concluir etapa para todos".
4. **QR code da OS:** fora; "Buscar OS pelo número" na fila.
5. **Funcionário do usuário:** `EmployeeOfUserAsync` no `Identity.Contracts`.
6. **Tema do `/fab`** (ponto 95): por aparelho registrado, padrão automático (PRD-03b); até lá, preferência do usuário ou automático.

**Pré-condição externa:** nenhuma.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers) e E2E (tablet e 400 px) passando; teste manual no tablet registrado.
- [ ] Migration criada (não destrutiva), testada sobre cópia do banco do tenant `konebras`.
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Kit "chão de fábrica" criado e usado nas telas dedicadas, com estados, alvos de 56 px e sem dock.
- [ ] Ajudas `/fab`, `/fab/etapa/$stepId`, aba Apontamentos e Apontamentos abertos; teste de cobertura passando.
- [ ] Próximo passo e alertas acionáveis (falta de material, apontamentos abertos) aplicados.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` (motivo `EndOfShift`, colunas novas do apontamento) e `docs/04-fluxos-estados.md` §1 (D12) atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `LaborEntry` e `StepPause` (Domain); máquina de estados da etapa e da OS.
- [ ] `IUserEmployeeLink.EmployeeOfUserAsync` no Identity.
- [ ] `ShopfloorService` (fila, busca, iniciar, pausar, retomar, sair, finalizar, anexos) e `SupervisorLaborService` (apontar por, concluir, corrigir, abertos).
- [ ] Ponto de extensão do início de etapa para o bloqueio por BOM da ENG-02.
- [ ] Publicar o `ProductionStepStarted` no início de etapa (evento e consumidor do Commercial já entregues na PRD-02).
- [ ] Endpoints `/api/v1/shopfloor/*` (`RequireModule("shopfloor")`) e `/api/v1/production/production-orders/{id}/labor-entries`.
- [ ] Kit "chão de fábrica"; páginas `/fab` e `/fab/etapa/$stepId`, aba Apontamentos, Apontamentos abertos, item "Operador" do dock, ajudas.

## Quality gates
- Agentes previstos: @dev, @ux-design-expert (kit chão de fábrica, luva, 56 px), cybersecurity (funcionário do usuário, IDOR, anexos pela fila, DTO sem custo), @qa.
- Gate: CA 6, 8, 10, 13, 19, 21, 24, 25, 29 e 30 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fase 3).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK (ajustado: renumerados) |
| 5 | CAs de segurança e de permissão (3 camadas) | OK (ajustado: §2.2) |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: sem ENG-02 e EST-03, Marco 3.1) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK (ajustado: kit chão de fábrica, padrões 1, 3, 4, 10) |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** quebra da PRD-03 (PIN e offline na PRD-03b); funcionário sempre do usuário logado; afastado não aponta (depende da CAD-04c); o conflito "OS inicia com BOM liberada" × "BOM criada na etapa Projeto" virou ponto em aberto (2), sem decidir; supervisor aponta por funcionário (mitigação do risco de adoção).
- **Correções da revalidação (21/10/2026):** fila com todas as etapas prontas, as do meu centro primeiro, filtro "Só meu centro" e recusa no servidor sem restringir por centro (D11, CA 1, 2, 8); busca da OS pelo número (CA 3); primeira etapa sem BOM e bloqueio das seguintes a partir da ENG-02 (D12, CA 9); "Sair da etapa (só eu)" e "Concluir etapa para todos" (CA 14); "Pronta para concluir" com conclusão na PRD-06a1 (CA 17); qualidade pela `production.approve_quality` (D6, CA 16); "Pedir material" movido para a EST-03, com a pausa ainda avisando o almoxarifado (CA 11); motivos "Fim do turno" e "Outro" opcional (CA 11); confirmação "Finalizar etapa · {h} h apontadas" com "Pausar" separado (padrão 4, CA 13); kit "chão de fábrica" (padrão 1, CA 24); anexos pelo endpoint que confere a fila, apontar por funcionário sem sobreposição e com 7 dias, dono não corrige nem aprova, com a exceção do `SoleOwnerRule` do ponto 76 (§2.2, CA 6, 19, 21); DTO sem custo (CA 25); apontamentos abertos (CA 22); tema (ponto 95, CA 26); dock "Operador" (CA 27); `EmployeeOfUserAsync`; dependências e ordem pela nova sequência (Marco 3.1).
- **Pré-condições para `Ready`:** nenhuma de decisão de story.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da PRD-03) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO (D6, D7, D11, D12, tabela 1.1 itens 63–67 e 95) e correções da revisão pré-Fase 3 (§2.1 "Outros", §2.2 PRD-01/PRD-03a, §2.3 padrões 1, 3, 4, 10 e achados pontuais); sem ENG-02 e EST-03 nas dependências; nova ordem (Marco 3.1 "Fábrica visível"); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
| 21/10/2026 | 0.5 | Fechamento da revisão: exceção do `SoleOwnerRule` no CA 21 (ponto 76); `ProductionStepStarted` reaproveitado da PRD-02 (CA 7); bloqueio de BOM também na "Concluir etapa" (CA 9, ENG-02 CA 16) | River (SM) |
