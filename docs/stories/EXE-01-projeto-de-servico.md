# EXE-01 — Projeto de serviço: tipo do projeto, tela própria, início, entrega e aceite do cliente separados

| Campo | Valor |
|---|---|
| ID | EXE-01 |
| Título | O projeto ganha o tipo (Fabricação ou Serviço), decidido na aprovação e preenchido nos projetos que já existem; o projeto de serviço tem tela própria, sem nada de fábrica, com "Iniciar execução", "Registrar entrega" e "Registrar aceite do cliente" separados; o orçamento de serviço deixa de oferecer o gatilho "Fabricação concluída" e chama o aceite técnico de "Aceite do cliente". **Correção mínima que destrava a BlockWay (CTR-2026-0001).** |
| Tamanho | M |
| Status | Draft |
| Requisitos | [Proposta de execução depois do contrato](../produto/execucao-pos-contrato.md) §1, §4.2, §8, §10.1 e §13 · PRD PRJ-1, PRJ-6 · COM-04a (aprovação → contrato + projeto) · COM-04b (gatilhos, entrega manual D15, conclusão do contrato CA 14) · ADR 0019 §1 e §16 · ADR 0046 (adendo de 17/11/2026, recurso por perfil) · R-SEC-5 |
| Dependências | — (tudo já existe: COM-04a/b, COM-06, PRD-06a1) |
| Bloqueia | EXE-02, EXE-03, EXE-04, EXE-05 |
| Ordem de execução | 1 (Execução depois do contrato, antes da F2 do Conecta) |

## User story
Como **responsável por um projeto de serviço** (ex.: implantação do Conecta para a BlockWay), quero **ver o projeto sem nada de fábrica, iniciar a execução, registrar a entrega e, depois, o aceite do cliente**, para **dar andamento ao que vendi e liberar cada parcela no momento certo, sem fingir uma "fabricação".**

## Contexto e referências
- Hoje o projeto só sai de "Planejado" pela primeira etapa de uma OS de fabricação (PRD-03a), e item `Service` nunca gera OS (PRD-01, D14). O cabeçalho mostra "Registrar fabricação concluída" para todo projeto sem OS (`ProjectPage.tsx`, `canFabricate`), e `RegisterDeliveryAsync` publica `ProjectDelivered` **e** `TechnicalAcceptance` juntos; com marco de fabricação no contrato, a entrega dá 409 `fabrication_pending`.
- O tenant `bemaqui` (ambiente SaaS) foi provisionado com o perfil **Prestação de serviços** (SAAS-01b). A Konebras tem só `factory`.
- Decisões do PO de 27/11/2026 (proposta §13): projeto para Fábrica e Serviços; Konebras não usa projeto de serviço agora; aceite por link fica para depois (v1 = registro interno).

## Critérios de aceite

**Tipo do projeto**
1. **Dado** a migration, **quando** roda num tenant, **então** `commercial.projects` ganha `execution_kind` (`Fabrication`/`Service`, `NOT NULL DEFAULT 'Fabrication'`), `started_on` (`date`), `accepted_on` (`date`), `accepted_by_name` (até 120, `[PersonalData]`) e `accepted_recorded_by`; nenhuma coluna é apagada nem renomeada.
2. **Dado** o backfill (job de dados do `TenantMigrator`, idempotente), **quando** o tenant **não** tem o perfil `factory`, **então** vira `Service` todo projeto cujos itens são todos `Service` (pelo `quote_items.kind`) e que não tem OS de fabricação nem de instalação (nem canceladas); os demais ficam `Fabrication`. **Quando** o tenant tem `factory` (Konebras), **então** nenhum projeto muda. Cada mudança grava no histórico do projeto "Tipo definido como Serviço (ajuste automático)".
3. **Dado** a BlockWay (tenant `bemaqui`, CTR-2026-0001, só itens de serviço), **quando** o backfill roda, **então** o projeto dela fica `Service` sem nenhuma ação manual e mantém situação, datas e parcelas.
4. **Dado** a aprovação de um orçamento (COM-04a), **quando** o tenant tem o recurso `projects.service_execution` e todos os itens não recorrentes são `Service`, **então** o projeto nasce `Service`; **senão**, `Fabrication`. O tipo vai no snapshot do contrato (`v1` ganha `executionKind`, valor padrão `Fabrication` na leitura de snapshot antigo).
5. **[Segurança]** **Dado** um tenant sem o recurso `projects.service_execution` (Konebras), **quando** chama qualquer rota nova desta story (iniciar, aceite), **então** 403 `module_not_released` (`RequireProfileFeature`), e o `ProjectDetail` não expõe nada além do que já expõe hoje (o campo `executionKind` sai sempre `Fabrication`).

**Tela do projeto de serviço**
6. **Dado** um projeto `Service`, **quando** abro, **então** o cabeçalho mostra o chip "Serviço" e o indicador de etapa **P Planejado · X Em execução · E Entregue · A Aceito · C Encerrado** (Entregue sem aceite mostra o chip âmbar "Aguardando aceite"); **não** aparecem "Registrar fabricação concluída", a nota "A fabricação deste projeto é concluída pelas OS", a nota de instalação, a aba "Cronograma" nem as listas de OS de fabricação, romaneios e OS de instalação; a aba "Itens e OS" passa a se chamar "Itens".
7. **Dado** um projeto `Service`, **quando** chamo `POST /projects/{id}/fabrication`, **então** 409 `not_a_fabrication_project` "Projeto de serviço não tem fabricação."; `POST .../installation-orders` responde igual com `not_an_installation_project`.
8. **Dado** um projeto `Fabrication`, **quando** abro, **então** a tela é idêntica à de hoje (teste de regressão de snapshot da tela e da API).

**Início, entrega e aceite**
9. **Dado** `projects.edit`, um projeto `Service` `Planned` e o contrato `Active`, **quando** clico "Iniciar execução", informo a data (≤ hoje, padrão hoje) e confirmo, **então** o projeto vai a `InProgress` com `started_on`, publica `ProjectStarted` (`{ projectId, contractId, occurredOn }`, informativo) e o próximo passo do cabeçalho vira "Registrar entrega".
10. **Dado** `projects.approve` e um projeto `Service` `Planned` ou `InProgress`, **quando** clico "Registrar entrega", informo a data (≤ hoje, ≥ `started_on`) e confirmo no diálogo que lista as parcelas liberadas, **então** o projeto vai a `Delivered` com `delivered_on` e `delivered_manually`, publica `ProjectDelivered` **e** `ProjectFabricated` com a mesma data (compatibilidade: contrato antigo com marco "Fabricação concluída" libera a parcela; ver Regras) e **não** publica `TechnicalAcceptance`. Projeto `Planned` passa por `InProgress` com `started_on` = data da entrega.
11. **Dado** o diálogo de entrega, **quando** o contrato **não** tem marco com gatilho "Aceite do cliente", **então** a opção "O cliente já deu o aceite nesta data" vem marcada (com o campo "Quem aceitou"); marcada, a entrega também grava o aceite (CA 12) na mesma transação. Com marco de aceite, vem desmarcada.
12. **Dado** `projects.approve` e um projeto `Service` `Delivered` sem aceite, **quando** clico "Registrar aceite do cliente", informo a data (≥ `delivered_on`, ≤ hoje), o nome de quem aceitou (obrigatório, até 120) e, se quiser, anexo o termo assinado (anexo `ProjectAcceptance` em `shared.attachments`, mesma validação de tipo e antivírus dos anexos), **então** grava `accepted_on`, `accepted_by_name` e `accepted_recorded_by`, publica `TechnicalAcceptance` e o diálogo mostra "Libera a parcela {marco} de {valor}, vencendo em {data}." quando houver.
13. **Dado** um aceite registrado por engano, **quando** alguém com `projects.approve` clica "Desfazer aceite" com motivo (até 200) **e** nenhuma parcela de gatilho "Aceite do cliente" do contrato está com baixa, **então** limpa o aceite, grava o motivo no histórico e publica `TechnicalAcceptanceReverted`; o Finance volta a `Planned` o título de aceite ainda `Open` sem baixa (vencimento limpo). Com baixa: 409 "A parcela do aceite já foi recebida. Use um aditivo."
14. **[Segurança]** **Dado** as três ações, **quando** chamadas, **então** conferem a versão do projeto (`xmin`, 409 `concurrency_conflict`), o contrato `Active` (409 `contract_not_active`), a data não futura (422 no campo) e a ação da camada 3 no servidor; o nome de quem aceitou não vai em evento nem em log.

**Conclusão do contrato**
15. **Dado** um projeto `Service` com marco de aceite no contrato, **quando** chega o `ReceivableSettled` da última parcela ou o `ProjectDelivered`, **então** o contrato só vai a `Completed` com o projeto entregue **e** aceito (o `ContractCompletionConsumer` passa a consumir também o `TechnicalAcceptance`); sem marco de aceite, como hoje (COM-04b CA 14).
16. **Dado** um projeto `Service` entregue sem aceite há mais de 7 dias, **quando** o responsável abre a "Minha pauta" (DSH-04), **então** vê "Aguardando aceite do cliente: {projeto}" com a ação "Registrar aceite do cliente".

**Orçamento e contrato**
17. **Dado** um orçamento num tenant com o recurso e só itens de serviço, **quando** edito os marcos, **então** o gatilho "Fabricação concluída" não é oferecido e "Aceite técnico" aparece como "Aceite do cliente" (orçamento, contrato, aditivo, PDFs do orçamento e do contrato, chip "Aguarda: …" do título a receber). Marco já gravado com "Fabricação concluída" continua válido e aparece como "Fabricação concluída (conclusão da execução)".
18. **[Segurança]** **Dado** o servidor, **quando** recebe um marco "Fabricação concluída" num orçamento que vai gerar projeto `Service`, **então** 422 no campo `milestones[n].triggerEvent` "Projeto de serviço não tem fabricação. Use entrega, aceite do cliente ou data." (o front esconde, o servidor confere).

## Regras de negócio e técnicas
- **Tipo é do projeto, não do perfil:** um tenant com Fábrica + Serviços pode ter os dois tipos; o rótulo muda pelo `execution_kind`. Nenhuma chave nova no `BusinessVocabulary`.
- **Recurso por perfil** `projects.service_execution` = `[services]` no `BusinessProfileCatalog.Features` (adendo de 17/11/2026 da ADR 0046). `factory` não entra (decisão do PO de 27/11/2026, EXE-08 decide depois).
- **Compatibilidade do `ProjectFabricated` no Serviço:** a entrega publica também o `ProjectFabricated` para que nenhum contrato antigo fique preso; o Finance não muda. O Commercial **não** grava `fabricated_on` no projeto `Service`.
- **Estados usados pelo `Service`:** `Planned`, `InProgress`, `Delivered`, `Closed`, `Cancelled`. `Fabricated` e `Installing` nunca. Aceite é campo, não estado.
- **Indicador:** `projectStage` passa a receber o tipo; o `Service` usa `SERVICE_PROJECT_STAGES` = Planejado, Em execução, Entregue, Aceito, Encerrado (letras P X E A C).

## Permissões, eventos e tabelas
- **Permissões:** `projects.edit` (iniciar), `projects.approve` (entrega, aceite, desfazer aceite). Nenhuma ação nova; nenhum seed patch.
- **Recurso:** `projects.service_execution` (novo em `BusinessProfileCatalog.Features`).
- **Eventos (`Commercial.Contracts`):** novos `ProjectStarted` e `TechnicalAcceptanceReverted` (`{ projectId, contractId, occurredOn }`); `ProjectDelivered`, `ProjectFabricated` e `TechnicalAcceptance` existentes. Consumidores: Finance (`TechnicalAcceptanceReverted`: título de aceite `Open` sem baixa → `Planned`; remove a linha de `project_trigger_events`), Commercial (`ContractCompletionConsumer` + `TechnicalAcceptance`).
- **Tabelas:** `commercial.projects` (+ colunas do CA 1). Migration aditiva `ProjectExecutionKindExe01`.

## Telas e estados
- **Projeto (`ProjectPage.tsx`):** chip do tipo; ações pelo tipo; um próximo passo por vez no cabeçalho ("Iniciar execução" → "Registrar entrega" → "Registrar aceite do cliente").
- **Lista de projetos:** coluna/filtro "Tipo" só para tenant com o recurso.
- **Diálogos:** "Iniciar a execução?" / "Execução iniciada"; "Registrar a entrega ao cliente?" / "Entrega registrada"; "Registrar o aceite do cliente?" / "Aceite registrado"; "Desfazer o aceite?" / "Aceite desfeito".
- **Estados:** carregando (skeleton), erro com "Tentar de novo", projeto sem itens mostra "Este projeto não tem itens. Os itens vêm do orçamento aprovado." (sem ação, contrato é imutável; link "Aditivo").
- **400 px:** ações no menu "Mais ações", o próximo passo sempre visível.
- **Ajuda:** `Ajuda/projetos-projeto.md` ganha a seção "Projeto de serviço" (tipo, início, entrega, aceite, desfazer); `Ajuda/comercial-orcamento-condicoes.md` explica "Aceite do cliente"; `<Dica>` em "Registrar aceite do cliente" ("O cliente confirma que recebeu o serviço. Pode liberar a última parcela.") e no chip "Aguardando aceite".

## Plano de testes
- **Unidade:** regra do tipo na aprovação (só serviço, misto, só recorrente sem REC-01 = como hoje); `projectStage` por tipo; transições válidas e inválidas.
- **Integração (Testcontainers):** backfill nos dois cenários (tenant `services` com projeto só de serviço vira `Service`; tenant `factory` não muda nada); entrega no `Service` com contrato antigo de marco "Fabricação concluída" libera as duas parcelas; aceite separado libera só a de aceite; desfazer aceite volta o título; conclusão do contrato exige aceite quando há marco; 403 sem o recurso.
- **Regressão Konebras:** sobre cópia do banco `konebras` (padrão PRD-04): `GET /projects/{id}` e o Resultado por projeto idênticos antes e depois da migration e do backfill.
- **Front (Vitest):** cabeçalho do `Service` sem ações de fábrica; próximo passo por estado; marcos sem "Fabricação concluída".

## Tarefas
- [ ] Domain: `ExecutionKind`, colunas novas, regras de transição do `Service`.
- [ ] Application: `ProjectService.Lifecycle` (`StartAsync`, `RegisterDeliveryAsync` por tipo, `RegisterAcceptanceAsync`, `RevertAcceptanceAsync`), `QuoteApprovalService.Create` (tipo na aprovação e no snapshot), validação do marco (CA 18), `ContractCompletionConsumer`.
- [ ] Infrastructure: migration aditiva + backfill idempotente; `ProjectViews` com `executionKind`, `startedOn`, `acceptedOn`, `acceptedByName` (este só com `projects.view`).
- [ ] Finance: consumidor `TechnicalAcceptanceReverted`.
- [ ] Endpoints: `POST /projects/{id}/start`, `/acceptance`, `/acceptance/revert` com `RequireProfileFeature("projects", "projects.service_execution")`; OpenAPI e Orval.
- [ ] Front: `ProjectPage.tsx`, `ProjectEventDialog.tsx` (novos tipos), `ProjectItemsTab.tsx`, `contracts.ts` (estágios por tipo), `quotes/milestones.ts`, `receivables.ts` (rótulo), lista de projetos.
- [ ] PDFs: `ContractPdfModel.cs` e PDF do orçamento com o rótulo por tipo.
- [ ] Docs: `04-fluxos-estados` §1 "Projeto" (máquina do `Service`), `03-modelo-dados` (colunas), ajuda.

## Dev Notes
- **Ciclo de vida:** `src/Modules/Commercial/BemAqui.Commercial.Application/ProjectService.Lifecycle.cs` (`RegisterDeliveryAsync` publica hoje os dois eventos), `ProjectDelivery.cs`, `ContractCompletionConsumer.cs`, `InstallationConsumers.cs` (como o aceite da OS publica `TechnicalAcceptance`).
- **Aprovação:** `QuoteApprovalService.Create.cs`, `ContractSnapshots.cs`.
- **Finance:** `src/Modules/Finance/BemAqui.Finance.Application/MilestoneReachedConsumer.cs`, `Domain/ProjectTriggerEvent.cs`.
- **Recurso por perfil:** `BusinessProfileCatalog.Features`, `Web/ModuleEndpoints.cs` (`RequireProfileFeature`), front `ProfileGate`.
- **Front:** `src/Web/src/pages/projects/ProjectPage.tsx` (`canFabricate`, `canDeliver`), `ProjectEventDialog.tsx`, `ProjectItemsTab.tsx`, `src/Web/src/pages/contracts/contracts.ts` (`PROJECT_STAGES`), `src/Web/src/pages/quotes/milestones.ts`.
- **Vocabulário:** `SharedKernel/Milestones.cs` não muda nesta story (o `StageCompleted` é da EXE-05).
- Testes: `--filter-class "*Project*"` e `"*Milestone*"`.

## Fora do escopo
- Etapas, modelos, horas e marcos por etapa (EXE-02 a EXE-05). Termo de aceite em PDF e recusa (EXE-06). Aceite por link (Portal, depois). Projeto de serviço na fábrica (EXE-08).
- Trocar o tipo de um projeto à mão (só pela regra e pelo backfill; se aparecer caso real, story própria com ADR).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 27/11/2026 | 0.1 | Story escrita a partir da proposta aprovada pelo PO (27/11/2026) | Claude |
