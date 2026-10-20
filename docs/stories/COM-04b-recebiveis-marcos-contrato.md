# COM-04b — Recebíveis do contrato, liberação por marco, entrega manual, cancelamento e conclusão

| Campo | Valor |
|---|---|
| ID | COM-04b |
| Título | Finance cria os recebíveis dos marcos ao receber `ContractSigned` (data → `Open`, evento → `Planned`) e grava cada evento de gatilho num registro próprio, para liberar a parcela mesmo com os eventos fora de ordem; registro manual de "fabricação concluída" e de "entrega" (venda sem instalação); cancelamento do contrato com os recebíveis; conclusão do contrato e encerramento do projeto; reprocessamento de mensagem em dead-letter |
| Tamanho | M (quebra da COM-04) |
| Status | Done |
| Requisitos | R-COML-7 (parcelas de Contas a Receber) · R-PRJ-6 (marcos do projeto disparam liberação de parcelas) · R-FIN-3 (títulos gerados pelo contrato) · R-COM-2 (notificação ao financeiro) · R-PRP-5 · R-PRP-7 |
| Dependências | COM-04a (contrato, marcos, eventos, `IProjects`, `IContracts`), FIN-01 (títulos a receber, origem `Contract`, `ReceivableSettled`), FND-09e (teste de posse de tabela, Contracts × Contracts, `dedup_key`) |
| Bloqueia | COM-04c, COM-05 (aba Financeiro), COM-06 (liberação no aceite técnico), COM-07, FIN-06b, PRD-06 (`ProjectFabricated` automático) |
| Ordem de execução | 13 (Marco 2.3 "Contrato ao caixa") |

## User story
Como **financeiro**, quero **que as parcelas do contrato apareçam sozinhas no contas a receber e sejam liberadas quando o marco acontece**, para **cobrar na hora certa sem depender de alguém me avisar**.

## Contexto e referências
- `04-fluxos-estados` §2: `ContractSigned` → Finance cria receivables (`Planned`/`Open` por marco) e notifica o financeiro; `ProjectFabricated / ProjectDelivered / TechnicalAcceptance` (Commercial) → Finance libera os receivables cujo gatilho é esse evento (`Planned → Open`, vencimento = data do evento + prazo do marco).
- `04-fluxos-estados` §1 "Contrato": `Active → Completed` (itens entregues e recebíveis quitados); `Active → Cancelled` (exige motivo; cancela recebíveis em aberto e OS não iniciadas; OS iniciadas viram decisão manual). "Projeto": `→ Closed` (financeiro quitado), `Cancelled` antes de `Delivered`.
- **ADR 0019 §1 (C1):** o outbox não garante ordem; o Finance grava os eventos de gatilho em `finance.project_trigger_events` e aplica a liberação quando o título nascer. Era o NO-GO da arquitetura nesta story.
- ADR 0019 §4 (título órfão de origem cancelada), §5 (conta de receita no evento; dead-letter com "Reprocessar"), §16 (conclusão também no `ProjectDelivered`; `ReceivableSettled` com `isReversal`), §19 (`ContractCancelled` sem texto livre).
- **D15 (PO, 20/10/2026):** venda sem instalação: "Registrar entrega" manual no projeto, com `projects.approve`, confirmação do impacto, só sem OS de instalação aberta. **D14:** vencimento da parcela editável no título em aberto (FIN-01 CA 19).
- ADR 0006 (consumidor idempotente pela inbox, na própria transação); FIN-01 (título de origem `Contract` protegido).
- `ModuleCatalog`: `projects.approve` = "aditivos de contrato" (reservada ao Proprietário, ADR 0008).

## Critérios de aceite
**Recebíveis a partir do contrato**
1. **Dado** o `ContractSigned` com os 3 marcos do exemplo (30% Assinatura prazo 0; 40% Fabricação concluída prazo 10; 30% Aceite técnico prazo 10) assinado em 05/11/2026, **quando** o consumidor do Finance processa, **então** nascem 3 títulos a receber com origem `Contract`, `origin_id` = id do marco, cliente e projeto do contrato, a conta de receita do evento, centro de custo vazio e descrição "{contrato} — {marco} ({%})": o primeiro `Open` com vencimento 05/11/2026, os outros dois `Planned` sem vencimento, com o evento-gatilho e o prazo gravados no título.
2. **Dado** um marco com gatilho "Data", **quando** processado, **então** o título nasce `Open` com o vencimento do marco.
3. **Dado** a mesma mensagem entregue duas vezes, **quando** o consumidor roda de novo, **então** nenhum título é duplicado (inbox por handler + índice único em `(origin, origin_id)`).
4. **Dado** os títulos criados, **quando** a transação do consumidor grava, **então** os membros com `finance.edit` recebem uma notificação "Contrato {número} gerou {n} parcelas a receber ({total})." com link para a lista filtrada pelo contrato (R-PRP-5).
5. **Dado** uma mensagem de evento do Finance que vai para dead-letter (10 tentativas, ADR 0006), **quando** isso acontece, **então** os membros com `finance.edit` recebem "Não foi possível gerar as parcelas do contrato {número}." com a ação "Reprocessar", que devolve a mensagem para a fila (endpoint com `finance.edit`, auditado) (ADR 0019 §5).

**Liberação por marco e registro de gatilhos (C1)**
6. **Dado** o projeto do exemplo, **quando** o Commercial publica `ProjectFabricated` com a data 20/01/2027, **então** o consumidor do Finance grava o evento em `project_trigger_events` (chave única `(project_id, event)`), muda o título `Planned` de gatilho "Fabricação concluída" desse contrato para `Open` com vencimento 30/01/2027 (data + prazo 10), uma vez só (idempotente), e notifica `finance.edit` "Parcela {número} liberada: {marco}."
7. **[Segurança]** **Dado** o `ContractSigned` atrasado no backoff e o `ProjectFabricated` processado antes, **quando** o evento chega sem título, **então** fica gravado em `project_trigger_events`; **quando** o `ContractSigned` é processado depois, **então** o título do marco "Fabricação concluída" já nasce `Open` com vencimento = data do evento + prazo (teste obrigatório).
8. **Dado** `TechnicalAcceptance` e `ProjectDelivered` publicados pela COM-06 ou pelo registro manual de entrega (CA 10), **quando** processados, **então** liberam os títulos com gatilho "Aceite técnico" e "Entrega", do mesmo jeito; título já `Open`, `Paid` ou `Cancelled` não muda.
9. **Dado** `projects.edit` e um projeto `Planned` ou `InProgress`, **quando** clico "Registrar fabricação concluída", informo a data (≤ hoje) e confirmo "Registrar fabricação concluída" no diálogo que mostra "Libera a parcela {marco} de {valor}, vencendo em {data}." (com `finance.view`; sem ela, "Libera a parcela do marco {marco}."), **então** o projeto passa a `Fabricated` e o evento `ProjectFabricated` vai para o outbox; a mensagem de sucesso diz "Fabricação registrada".
10. **Dado** `projects.approve`, um projeto `Fabricated` (ou `Planned`/`InProgress` sem fabricação a registrar) e **nenhuma OS de instalação aberta** (consultada por `IInstallationOrders` quando a COM-06 existir), **quando** clico "Registrar entrega", informo a data (≤ hoje) e confirmo no diálogo que mostra as parcelas liberadas ("Libera as parcelas {marcos} de {total}."), **então** o projeto passa a `Delivered` e o Commercial publica `ProjectDelivered` e `TechnicalAcceptance` com essa data; com OS de instalação aberta, 409 "Há OS de instalação aberta: a entrega é registrada pelo aceite técnico."; a mensagem de sucesso diz "Entrega registrada" (D15).
11. **Dado** a Fase 3 com a PRD-06, **quando** todas as OS de fabricação do projeto concluem, **então** o mesmo `ProjectFabricated` é publicado automaticamente e o registro manual deixa de aparecer (sem mudança no Finance).

**Cancelamento e conclusão**
12. **Dado** `projects.approve` e um contrato `Active`, **quando** clico "Cancelar contrato", informo o motivo (obrigatório, até 500 caracteres, guardado no contrato) e confirmo no diálogo (estilo `destructive`) que lista os títulos que serão cancelados e os que já têm baixa, **então** o contrato passa a `Cancelled`, o projeto a `Cancelled` (se ainda não `Delivered`), e o `ContractCancelled` (`{ contractId, projectId }`, sem texto livre) vai para o outbox; o consumidor do Finance cancela os títulos `Planned` e `Open` sem baixa do contrato e lista os com baixa para o financeiro decidir ("Parcela {número} tem baixa: estorne ou mantenha."), notificando `finance.edit`; depois de estornada a baixa, o título pode ser cancelado no Finance com a marca "Origem cancelada" (FIN-01 CA 20).
13. **Dado** um projeto já `Delivered`, **quando** tento cancelar o contrato, **então** 409 "Projeto entregue: o contrato não é cancelado. Use um aditivo." (`04-fluxos` §1).
14. **Dado** um contrato cujo projeto está `Delivered` e todos os títulos do contrato estão `Paid` ou `Cancelled` (lidos por `IReceivables.ForContractAsync`), **quando** chega o `ReceivableSettled` da última parcela **ou** o `ProjectDelivered` (o que vier por último), **então** o consumidor do Commercial passa o contrato a `Completed` e o projeto a `Closed`, uma vez só.
15. **Dado** um contrato `Completed`, **quando** chega um `ReceivableSettled` com `isReversal = true` de um título dele, **então** o contrato volta a `Active` e o projeto a `Delivered`, com o motivo "Baixa estornada" no histórico (ADR 0019 §16).

**Consulta, permissões e segurança**
16. **Dado** o contrato (COM-04a CA 16), **quando** abro "Itens e marcos", **então** cada marco mostra o título a receber (número, situação, vencimento, saldo, link), lido por `IReceivables.ForContractAsync` de `Finance.Contracts`, e só aparece para quem tem `finance.view`; sem a permissão, o marco mostra só "Parcela gerada".
17. **[Segurança]** **Dado** os eventos de marco, **quando** chegam ao Finance, **então** o consumidor confere que o projeto e o contrato do título batem com o evento (um evento de outro projeto não libera nada) e ignora evento de contrato `Cancelled`.
18. **Dado** o módulo `projects` não liberado, **quando** chamo cancelar ou registrar fabricação ou entrega, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; sem `projects.edit` ou `projects.approve`, 403 e o botão some. `RequireModule` e `perm:`; o reprocessamento exige `finance.edit`.
19. **Dado** qualquer liberação, cancelamento, conclusão ou reabertura, **quando** abro o histórico do título, do contrato ou do projeto, **então** vejo a mudança com a origem (evento e id da mensagem) ou o usuário.

## Regras de negócio e técnicas
- **Título do contrato** (FIN-01): `origin = Contract`, `origin_id` = id do marco; colunas `trigger_event` e `days_after_event` no título para a liberação; `(origin, origin_id)` único.
- **Registro de gatilhos** `finance.project_trigger_events` (`project_id`, `event`, `occurred_on`, `message_id`, `received_at`; único em `(project_id, event)`): todo consumidor que cria título de contrato (este e o do aditivo, COM-07) consulta o registro e já cria `Open` quando o evento existe.
- **Vencimento liberado** = data do evento + `days_after_event` (dias corridos, sem ajuste de dia útil). Depois de `Open`, o vencimento é editável no título com motivo (D14, FIN-01 CA 19).
- **Consumidores:** Finance (`ContractSigned`, `ProjectFabricated`, `ProjectDelivered`, `TechnicalAcceptance`, `ContractCancelled`) e Commercial (`ReceivableSettled`, `ProjectDelivered` para a conclusão), cada um na própria transação com a inbox (ADR 0006). A notificação dentro do mesmo módulo é síncrona (ADR 0019 §18).
- **Projeto:** nesta story, `Planned → Fabricated` (manual), `→ Delivered` (entrega manual, D15), `→ Cancelled`, `Delivered → Closed` e `Closed → Delivered` (estorno); `InProgress` (PRD-03), `Installing` e `Delivered` pela OS (COM-06).
- Datas `date`; dinheiro `numeric(18,2)`; auditoria nos três módulos; concorrência pelo `xmin` do contrato e do projeto.

## Permissões, eventos e tabelas
- **Permissões (já no `ModuleCatalog`):** `projects.edit` (registrar fabricação concluída), `projects.approve` (registrar entrega, cancelar contrato), `finance.view` (ver os títulos no contrato), `finance.edit` (reprocessar dead-letter).
- **Eventos novos (`Commercial.Contracts`):** `ProjectFabricated` (`{ projectId, contractId, occurredOn }`), `ProjectDelivered` e `TechnicalAcceptance` (mesmo formato; publicados aqui pela entrega manual e na COM-06 pelo aceite), `ContractCancelled` (`{ contractId, projectId }`). **Consumidos:** `ReceivableSettled` (FIN-01, com `settlementId` e `isReversal`).
- **Contrato novo (`Finance.Contracts`):** `IReceivables.ForContractAsync(contractId)` e `ByProjectAsync(projectId)` (número, marco, situação, vencimento, valor, saldo).
- **Tabelas:** `finance.receivables` (+ `trigger_event`, `days_after_event`); `finance.project_trigger_events` (nova); `commercial.contracts` (+ `cancel_reason`, `cancelled_at`, `cancelled_by`, `completed_at`); `commercial.projects` (+ `fabricated_on`, `delivered_on`, `delivered_manually`, `closed_at`).

## Telas e estados
- **Contrato → Itens e marcos:** marcos com o título vinculado; ação "Cancelar contrato" (diálogo com impacto).
- **Projeto (cabeçalho):** "Registrar fabricação concluída" (até a PRD-06) e "Registrar entrega" (venda sem instalação), com o `StageIndicator` atualizado.
- **A receber (FIN-01):** filtro "Do contrato {número}"; chip Previsto com o gatilho ("Aguarda: fabricação concluída").
- **Verbos:** "Registrar fabricação concluída" / "Fabricação registrada"; "Registrar entrega" / "Entrega registrada"; "Cancelar contrato" / "Contrato cancelado"; "Reprocessar" / "Reenviado para processamento".
- **Estados:** carregando; erro; contrato sem títulos ainda ("Gerando parcelas no financeiro…" com atualização automática e o estado de demora depois de cerca de 30 s, COM-04a CA 11).
- **Propositivo:** a notificação leva à lista filtrada; os diálogos de fabricação, entrega e cancelamento mostram o que acontece com cada parcela.
- **Ajuda:** a ajuda `comercial-contrato.md` ganha "Parcelas e marcos" e "Cancelar contrato"; `projetos-projeto.md` ganha "Registrar fabricação concluída" e "Registrar entrega"; `<Dica>` em "Previsto" ("A parcela espera o marco acontecer para ganhar vencimento.") e "Registrar entrega" ("Use quando não há instalação; com OS de instalação, a entrega vem do aceite técnico.").

## Plano de testes
- **Unidade:** vencimento liberado (CA 6); regras de conclusão e reabertura (CA 14, 15).
- **Integração (Testcontainers, obrigatória):** consumidor cria títulos com situação certa e conta do evento, idempotente (CA 1–3); notificação (CA 4); dead-letter com "Reprocessar" (CA 5); liberação pelos eventos, idempotente e sem afetar título já aberto (CA 6, 8); **`ContractSigned` atrasado com o evento antes** (CA 7); registro manual de fabricação e de entrega, com e sem OS aberta (CA 9, 10); cancelamento com e sem baixa e título órfão (CA 12, 13); conclusão pelo último evento e reabertura pelo estorno (CA 14, 15); títulos no contrato por permissão (CA 16); evento de outro projeto não libera (CA 17); três camadas (CA 18); auditoria com origem (CA 19). Cadeia ponta a ponta: aprovar (COM-04a) → títulos → registrar fabricação → título aberto → registrar entrega → contrato concluído depois das baixas.
- **Arquitetura:** consumidores registrados; Finance não referencia `Commercial.Application`; teste de posse de tabela (FND-09e) passando com os consumidores cruzados.
- **E2E (Playwright):** aprovar, ver as parcelas no financeiro, registrar a fabricação e ver a parcela liberada.

## Fora do escopo
- Aceite técnico pela OS de instalação: COM-06 (publica `TechnicalAcceptance` e `ProjectDelivered`).
- Contrato de implantação (marcos já recebidos): COM-04c. Ajuste de recebíveis por aditivo: COM-07. Boleto da parcela: BNK-01. OS de fabricação canceladas junto do contrato: PRD-01 (Fase 3).

## Pontos em aberto
Decididos pelo PO em 20/10/2026 (seção 1.1, itens 20 e 25, e D14, D15):
1. "Fabricação concluída" registrada à mão no projeto até a PRD-06.
2. Conta dos títulos do contrato: escolhida e validada na aprovação (COM-04a) e recebida no evento; centro vazio.
3. Cancelar contrato com `projects.approve`.
4. Venda sem instalação: "Registrar entrega" manual (D15).
5. Vencimento editável no título em aberto (D14, FIN-01).
6. P-18 decidida (ADR 0021).

## Definition of Done
- [x] Critérios de aceite atendidos.
- [x] Testes de unidade, integração (Testcontainers, cadeia ponta a ponta e eventos fora de ordem), arquitetura e E2E passando.
- [x] Migration criada (não destrutiva).
- [x] Permissões só do `ModuleCatalog`; consumidores registrados.
- [x] Telas com estados.
- [x] Ajuda atualizada; teste de cobertura passando.
- [x] Notificações acionáveis e confirmação com impacto.
- [x] Auditoria ligada nos três módulos.
- [x] OpenAPI e cliente Orval regenerados.
- [x] `docs/03-modelo-dados.md` e `docs/04-fluxos-estados.md` conferidos; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Consumidores no Finance (`ContractSignedConsumer`, `MilestoneReachedConsumer` com o registro de gatilhos, `ContractCancelledConsumer`) e no Commercial (`ReceivableSettledConsumer`, `ProjectDeliveredConsumer`).
- [ ] `IReceivables` em `Finance.Contracts`; `project_trigger_events`; alerta de dead-letter com "Reprocessar".
- [ ] Casos de uso "Registrar fabricação concluída", "Registrar entrega" e "Cancelar contrato"; telas e ajuda.

## Quality gates
- Agentes previstos: @dev, @architect (eventos fora de ordem e idempotência), @qa.
- Gate: CA 1, 3, 6, 7, 10, 12 e 17 obrigatórios.

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

- **Correções feitas na validação (19/10/2026):** "FIN-01 antes da COM-04" explícita; prazo após o evento no título; registro manual da fabricação; contrato concluído só com projeto entregue e títulos quitados; evento de outro projeto ignorado.
- **Correções da revalidação (20/10/2026):** o NO-GO da arquitetura foi resolvido pelo registro de gatilhos (ADR 0019 §1, CA 6, 7); "Registrar entrega" para venda sem instalação (D15, CA 10); conclusão também no `ProjectDelivered` e reabertura pelo estorno (M12, CA 14, 15); conta de receita vinda do evento (item 25); dead-letter com "Reprocessar" (A4, CA 5); `ContractCancelled` sem texto livre e título órfão (CA 12); dependência da FND-09e (teste de posse de tabela). CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma pendente.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 19/10/2026 | 0.1 | Story detalhada (quebra da COM-04) | River (SM) |
| 19/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 20/10/2026 | 0.3 | Decisões do PO (D14, D15, seção 1.1) e correções da revisão pré-Fase 2 (§2.1-C1, A3, A4, M12, baixos); CAs renumerados | River (SM) |
| 20/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
| 20/10/2026 | 1.0 | Implementada. Desvios: os eventos de Comercial e Financeiro não estavam registrados no processo (iriam para dead-letter); todos foram registrados no `HostEvents`, sem mensagem perdida em homologação; `project_trigger_events` guarda `contract_id` no lugar de `message_id` (a mensagem fica na origem da auditoria, que agora todo consumidor grava como "Evento … · mensagem …"); `receivables.contract_id` novo para ler as parcelas do contrato; `ContractMilestoneInfo` ganhou o percentual (descrição da parcela); com marco de fabricação no contrato, "Registrar entrega" exige a fabricação registrada (409); a checagem de OS de instalação aberta entra com o `IInstallationOrders` da COM-06; o aviso de dead-letter leva à lista A receber, que mostra os eventos com falha e "Reprocessar"; consumidores dentro do processo: `ContractSignedConsumer`, `MilestoneReachedConsumer`, `ContractCancelledConsumer` (Finance) e `ContractCompletionConsumer` (Commercial) | @dev |
