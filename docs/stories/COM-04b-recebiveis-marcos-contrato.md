# COM-04b — Recebíveis do contrato, liberação por marco, cancelamento e conclusão

| Campo | Valor |
|---|---|
| ID | COM-04b |
| Título | Finance cria os recebíveis dos marcos ao receber `ContractSigned` (data → `Open`, evento → `Planned`); eventos de marco liberam a parcela com vencimento = data do evento + prazo; registro manual de "fabricação concluída" até a Fase 3; cancelamento do contrato com cancelamento dos recebíveis em aberto; conclusão do contrato e encerramento do projeto |
| Tamanho | M (quebra da COM-04) |
| Status | Draft |
| Requisitos | R-COML-7 (parcelas de Contas a Receber) · R-PRJ-6 (marcos do projeto disparam liberação de parcelas) · R-FIN-3 (títulos gerados pelo contrato) · R-COM-2 (notificação ao financeiro) · R-PRP-5 · R-PRP-7 |
| Dependências | COM-04a (contrato, marcos, eventos), FIN-01 (títulos a receber, origem `Contract`) |
| Bloqueia | COM-05 (aba Financeiro), COM-06 (liberação no aceite técnico), COM-07, FIN-06, PRD-06 (`ProjectFabricated` automático) |
| Ordem de execução | 11 (Marco 2.3 — do contrato ao caixa) |

## User story
Como **financeiro**, quero **que as parcelas do contrato apareçam sozinhas no contas a receber e sejam liberadas quando o marco acontece**, para **cobrar na hora certa sem depender de alguém me avisar**.

## Contexto e referências
- `04-fluxos-estados` §2: `ContractSigned` → Finance cria receivables (`Planned`/`Open` por marco) e notifica o financeiro; `ProjectFabricated / Delivered / TechnicalAcceptance` (Commercial) → Finance libera os receivables cujo gatilho é esse evento (`Planned → Open`, vencimento = data do evento + prazo do marco).
- `04-fluxos-estados` §1 "Contrato": `Active → Completed` (itens entregues e recebíveis quitados); `Active → Cancelled` (exige motivo; cancela recebíveis em aberto e OS não iniciadas; OS iniciadas viram decisão manual). "Projeto": `→ Closed` (financeiro quitado), `Cancelled` antes de `Delivered`.
- ADR 0006 (consumidor idempotente pela inbox, na própria transação); FIN-01 (título de origem `Contract` protegido, CA 14).
- `ModuleCatalog`: `projects.approve` = "aditivos de contrato".

## Critérios de aceite
**Recebíveis a partir do contrato**
1. **Dado** o `ContractSigned` com os 3 marcos do exemplo (30% Assinatura prazo 0; 40% Fabricação concluída prazo 10; 30% Aceite técnico prazo 10) assinado em 05/11/2026, **quando** o consumidor do Finance processa, **então** nascem 3 títulos a receber com origem `Contract`, `origin_id` = id do marco, cliente e projeto do contrato, descrição "{contrato} — {marco} ({%})": o primeiro `Open` com vencimento 05/11/2026, os outros dois `Planned` sem vencimento, com o evento-gatilho e o prazo gravados no título.
2. **Dado** um marco com gatilho "Data", **quando** processado, **então** o título nasce `Open` com o vencimento do marco.
3. **Dado** a mesma mensagem entregue duas vezes, **quando** o consumidor roda de novo, **então** nenhum título é duplicado (inbox por handler + índice único em `(origin, origin_id)`).
4. **Dado** os títulos criados, **quando** a transação do consumidor grava, **então** os membros com `finance.edit` recebem uma notificação "Contrato {número} gerou {n} parcelas a receber ({total})." com link para a lista filtrada pelo contrato (R-PRP-5).
5. **Dado** a conta de receita e o centro de custo dos títulos do contrato, **quando** nascem, **então** a conta é a do seed "1.1 Venda de equipamentos" se estiver ativa e analítica, senão "1.9 Receitas a classificar" (ver Pontos em aberto 2), e o centro de custo fica vazio; o financeiro pode trocar os dois (FIN-01 CA 14).

**Liberação por marco**
6. **Dado** o projeto do exemplo, **quando** o Commercial publica `ProjectFabricated` com a data 20/01/2027, **então** o consumidor do Finance muda o título `Planned` de gatilho "Fabricação concluída" desse contrato para `Open` com vencimento 30/01/2027 (data + prazo 10), uma vez só (idempotente), e notifica `finance.edit` "Parcela {número} liberada: {marco}."
7. **Dado** `TechnicalAcceptance` e `ProjectDelivered` publicados pela COM-06, **quando** processados, **então** liberam os títulos com gatilho "Aceite técnico" e "Entrega", do mesmo jeito; título já `Open`, `Paid` ou `Cancelled` não muda.
8. **Dado** `projects.edit` e um projeto `Planned` ou `InProgress`, **quando** clico "Registrar fabricação concluída" (proposta, ver Pontos em aberto 1), informo a data (≤ hoje) e confirmo "Registrar fabricação concluída" no diálogo que mostra "Libera a parcela {marco} de {valor}, vencendo em {data}.", **então** o projeto passa a `Fabricated` e o evento `ProjectFabricated` vai para o outbox; o toast diz "Fabricação registrada".
9. **Dado** a Fase 3 com a PRD-06, **quando** todas as OS de fabricação do projeto concluem, **então** o mesmo `ProjectFabricated` é publicado automaticamente e o registro manual deixa de aparecer (sem mudança no Finance).

**Cancelamento e conclusão**
10. **Dado** `projects.approve` (ver Pontos em aberto 3) e um contrato `Active`, **quando** clico "Cancelar contrato", informo o motivo (obrigatório, até 500 caracteres) e confirmo no diálogo que lista os títulos que serão cancelados e os que já têm baixa, **então** o contrato passa a `Cancelled`, o projeto a `Cancelled` (se ainda não `Delivered`), e o `ContractCancelled` vai para o outbox; o consumidor do Finance cancela os títulos `Planned` e `Open` sem baixa do contrato e lista os com baixa para o financeiro decidir ("Parcela {número} tem baixa: estorne ou mantenha."), notificando `finance.edit`.
11. **Dado** um projeto já `Delivered`, **quando** tento cancelar o contrato, **então** 409 "Projeto entregue: o contrato não é cancelado. Use um aditivo." (`04-fluxos` §1).
12. **Dado** um contrato cujo projeto está `Delivered` e todos os títulos do contrato estão `Paid` ou `Cancelled` (lidos por `IReceivables.ForContractAsync`), **quando** chega um `ReceivableSettled` da última parcela, **então** o consumidor do Commercial passa o contrato a `Completed` e o projeto a `Closed`, uma vez só.

**Consulta, permissões e segurança**
13. **Dado** o contrato (COM-04a CA 14), **quando** abro "Itens e marcos", **então** cada marco mostra o título a receber (número, situação, vencimento, saldo, link), lido por `IReceivables.ForContractAsync` de `Finance.Contracts`, e só aparece para quem tem `finance.view`; sem a permissão, o marco mostra só "Parcela gerada".
14. **[Segurança]** **Dado** os eventos de marco, **quando** chegam ao Finance, **então** o consumidor confere que o projeto e o contrato do título batem com o evento (um evento de outro projeto não libera nada) e ignora evento de contrato `Cancelled`.
15. **Dado** o módulo `projects` não liberado, **quando** chamo cancelar ou registrar fabricação, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; sem `projects.edit` ou `projects.approve`, 403 e o botão some. `RequireModule` e `perm:`.
16. **Dado** qualquer liberação, cancelamento ou conclusão, **quando** abro o histórico do título, do contrato ou do projeto, **então** vejo a mudança com a origem (evento e id da mensagem) ou o usuário.

## Regras de negócio e técnicas
- **Título do contrato** (FIN-01): `origin = Contract`, `origin_id` = id do marco; colunas novas `trigger_event` e `days_after_event` no título (proposta) para a liberação; `(origin, origin_id)` único.
- **Vencimento liberado** = data do evento + `days_after_event` (dias corridos, sem ajuste de dia útil; FIN-01 ponto 3).
- **Consumidores:** Finance (`ContractSigned`, `ProjectFabricated`, `ProjectDelivered`, `TechnicalAcceptance`, `ContractCancelled`) e Commercial (`ReceivableSettled`), cada um na própria transação com a inbox (ADR 0006).
- **Projeto:** nesta story, `Planned → Fabricated` (manual), `→ Cancelled`, `Delivered → Closed`; `InProgress` (PRD-03), `Installing` e `Delivered` (COM-06).
- Datas `date`; dinheiro `numeric(18,2)`; auditoria nos três módulos.

## Permissões, eventos e tabelas
- **Permissões (já no `ModuleCatalog`):** `projects.edit` (registrar fabricação concluída), `projects.approve` (cancelar contrato, proposta), `finance.view` (ver os títulos no contrato).
- **Eventos novos (`Commercial.Contracts`):** `ProjectFabricated` (`{ projectId, contractId, occurredOn }`), `ContractCancelled` (`{ contractId, projectId, reason }`); `ProjectDelivered` e `TechnicalAcceptance` são publicados pela COM-06 com o mesmo formato. **Consumidos:** `ReceivableSettled` (FIN-01).
- **Contrato novo (`Finance.Contracts`):** `IReceivables.ForContractAsync(contractId)` e `ByProjectAsync(projectId)` (número, marco, situação, vencimento, valor, saldo).
- **Tabelas:** `finance.receivables` (+ `trigger_event`, `days_after_event`, **proposta**); `commercial.contracts` (+ `cancel_reason`, `cancelled_at`, `cancelled_by`, `completed_at`); `commercial.projects` (+ `fabricated_on`, `closed_at`).

## Telas e estados
- **Contrato → Itens e marcos:** marcos com o título vinculado; ação "Cancelar contrato" (diálogo com impacto).
- **Projeto (cabeçalho):** "Registrar fabricação concluída" (até a PRD-06), com o indicador de etapa atualizado.
- **A receber (FIN-01):** filtro "Do contrato {número}"; chip Previsto com o gatilho ("Aguarda: fabricação concluída").
- **Verbos:** "Registrar fabricação concluída" / "Fabricação registrada"; "Cancelar contrato" / "Contrato cancelado".
- **Estados:** carregando; erro; contrato sem títulos ainda ("Gerando parcelas no financeiro…" enquanto o outbox não processou, com atualização automática).
- **Propositivo:** a notificação leva à lista filtrada; o diálogo de cancelamento lista o que acontece com cada título.
- **Ajuda:** a ajuda `comercial-contrato.md` ganha "Parcelas e marcos" e "Cancelar contrato"; `projetos-projeto.md` ganha "Registrar fabricação concluída"; `<Dica>` em "Previsto" ("A parcela espera o marco acontecer para ganhar vencimento.").

## Plano de testes
- **Unidade:** vencimento liberado (CA 6); regras de conclusão (CA 12).
- **Integração (Testcontainers, obrigatória):** consumidor cria títulos com situação certa e é idempotente (CA 1–3); notificação (CA 4); conta padrão (CA 5); liberação pelos três eventos, idempotente e sem afetar título já aberto (CA 6, 7); registro manual com evento (CA 8); cancelamento com e sem baixa (CA 10, 11); conclusão (CA 12); títulos no contrato por permissão (CA 13); evento de outro projeto não libera (CA 14); três camadas (CA 15); auditoria com origem (CA 16). Cadeia ponta a ponta: aprovar (COM-04a) → títulos → registrar fabricação → título aberto.
- **Arquitetura:** consumidores registrados; Finance não referencia `Commercial.Application`.
- **E2E (Playwright):** aprovar, ver as parcelas no financeiro, registrar a fabricação e ver a parcela liberada.

## Fora do escopo
- Aceite técnico e entrega pela OS de instalação: COM-06 (publica `TechnicalAcceptance` e `ProjectDelivered`).
- Ajuste de recebíveis por aditivo: COM-07. Boleto da parcela: BNK-01. OS de fabricação canceladas junto do contrato: PRD-01 (Fase 3).

## Pontos em aberto
1. **"Fabricação concluída" antes da Produção.** Na Fase 2 não há OS de fabricação. **Recomendação:** registro manual no projeto (CA 8), com confirmação do impacto, substituído pelo automático na PRD-06.
2. **Conta e centro de custo dos títulos do contrato.** **Recomendação:** conta "1.1 Venda de equipamentos" do seed, senão "1.9 Receitas a classificar"; centro vazio. Alternativa: escolher a conta na aprovação.
3. **Permissão para cancelar contrato.** **Recomendação:** `projects.approve` (reservada ao Proprietário, ADR 0008), porque cancela recebíveis.
4. **P-18:** ver FIN-01, ponto 5.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers, cadeia ponta a ponta), arquitetura e E2E passando.
- [ ] Migration criada (não destrutiva).
- [ ] Permissões só do `ModuleCatalog`; consumidores registrados.
- [ ] Telas com estados.
- [ ] Ajuda atualizada; teste de cobertura passando.
- [ ] Notificações acionáveis e confirmação com impacto.
- [ ] Auditoria ligada nos três módulos.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` e `docs/04-fluxos-estados.md` conferidos; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Consumidores no Finance (`ContractSignedConsumer`, `MilestoneReachedConsumer`, `ContractCancelledConsumer`) e no Commercial (`ReceivableSettledConsumer`).
- [ ] `IReceivables` em `Finance.Contracts`.
- [ ] Casos de uso "Registrar fabricação concluída" e "Cancelar contrato"; telas e ajuda.

## Quality gates
- Agentes previstos: @dev, @architect (eventos e idempotência), @qa.
- Gate: CA 1, 3, 6, 10 e 14 obrigatórios.

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

- **Correções feitas na validação (19/10/2026):** a decisão "FIN-01 antes da COM-04" fica explícita na dependência (o Finance é o dono dos títulos e reage ao `ContractSigned`); prazo após o evento gravado no título; registro manual da fabricação marcado como proposta; contrato concluído só com projeto entregue e títulos quitados (CA 12); evento de outro projeto ignorado (CA 14).
- **Pré-condições para `Ready`:** Pontos em aberto 1 a 3 (com padrão recomendado) e P-18.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 19/10/2026 | 0.1 | Story detalhada (quebra da COM-04) | River (SM) |
| 19/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
