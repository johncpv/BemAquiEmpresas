# CAD-03 — Locais de estoque, centros de trabalho e calendário

| Campo | Valor |
|---|---|
| ID | CAD-03 |
| Título | Locais de estoque com local padrão, centros de trabalho com horas por dia e turnos, calendário com feriados e paradas, e contrato de capacidade diária |
| Tamanho | M (re-estimada: o backlog dizia P; são dois módulos, o calendário com feriados sugeridos e o contrato de capacidade usado pelo planejamento) |
| Status | Draft |
| Requisitos | R-EST-2 · R-PLN-1 · R-SEC-5 · R-PRP-1 |
| Dependências | Fase 0 (FND-06c, FND-07, FND-09c, FND-14). Pode rodar em paralelo com CAD-01a/01b |
| Bloqueia | CAD-04a (centro de trabalho padrão do cargo), MIG-01c (locais do Dolibarr), ENG-01, EST-01, PLN-01 |
| Ordem de execução | 6 |

## User story
Como **supervisor de produção e almoxarife**, quero **cadastrar os locais de estoque, os centros de trabalho com as horas disponíveis e o calendário de feriados e paradas**, para **que o estoque saiba onde está cada coisa e o planejamento calcule prazos com a capacidade real da fábrica**.

## Contexto e referências
- PRD R-EST-2 (múltiplos locais de estoque), R-PLN-1 (centros de trabalho com capacidade em horas/dia, turnos e calendário com feriados e paradas), R-PRD-9 (paradas refletidas na capacidade, D).
- `docs/03-modelo-dados.md` → `inventory.locations` (`name`, `code`, `is_default`); `production.work_centers` (`name`, `line`, `hours_per_day`, `shifts` JSON, `calendar_id`, `is_active`); `production.calendars / calendar_exceptions` (`date`, `type` Holiday/Shutdown, `hours_available`).
- `docs/04-fluxos-estados.md` §3.4 (agendamento finito: aloca "respeitando calendário, horas/dia").
- Backlog (CA): capacidade diária por centro usada nos testes de planejamento. PLN-01 (CA): "cenários fixos (2 centros, 3 projetos, feriado)".
- `docs/06-pendencias-riscos.md` P-04 (etapas Projeto → Corte → Montagem → Qualidade → Expedição; os centros de trabalho seguem essas etapas).
- `ModuleCatalog`: locais em `inventory` (`view`, `create`, `edit`, `approve`, `adjust` — **sem `delete`**); centros e calendário em `production` (`view`, `create`, `edit`, `approve` — **sem `delete`**). Por isso, nesta story, "excluir" é "inativar" com `edit` (ponto em aberto 1).
- Código existente: `DataTable`, formulário, `RecordHistory`, `IAuditable`, `BusinessTime` (fuso America/Sao_Paulo).

## Critérios de aceite
**Locais de estoque**
1. **Dado** `inventory.create`, **quando** cadastro um local com código (único) e nome e clico "Salvar local", **então** ele é gravado e auditado.
2. **Dado** um tenant novo, **quando** é provisionado, **então** existe um local padrão (sugestão: "ALM — Almoxarifado principal"), criado por seed idempotente.
3. **Dado** `inventory.edit`, **quando** marco outro local como padrão, **então** ele passa a ser o único padrão (o anterior é desmarcado na mesma transação) e vem pré-selecionado nos movimentos futuros (R-PRP-1).
4. **Dado** `inventory.edit`, **quando** inativo um local, **então** ele some dos seletores; o local padrão não pode ser inativado (409 "Escolha outro local padrão antes."); um local com saldo não pode ser inativado (409 "O local tem saldo. Transfira o saldo antes."), regra ligada pelo EST-01 e testada aqui com um provedor de saldo de teste.
5. **Dado** um código já usado, **quando** salvo, **então** a API responde 409 "Já existe o local {código}."

**Calendário**
6. **Dado** `production.edit`, **quando** abro Produção → Calendário, **então** vejo o calendário padrão do tenant com os dias úteis da semana (padrão seg–sex) e a lista de exceções do ano.
7. **Dado** `production.edit`, **quando** cadastro uma exceção com data, tipo (Feriado ou Parada), horas disponíveis (0 = dia parado) e descrição, **então** ela é gravada; datas repetidas no mesmo calendário respondem 409.
8. **Dado** um ano sem exceções, **quando** clico "Sugerir feriados nacionais de {ano}", **então** o sistema mostra a lista de feriados nacionais (fixos e móveis, calculados a partir da Páscoa) para eu marcar e confirmar "Adicionar feriados"; nada é gravado sem a confirmação (R-PRP-7). Feriados estaduais e municipais são cadastrados à mão.

**Centros de trabalho**
9. **Dado** `production.create`, **quando** cadastro um centro com nome, linha, turnos (início e fim de cada turno) e calendário, e clico "Salvar centro", **então** as horas por dia são calculadas a partir dos turnos (ex.: 07:00–11:00 + 12:00–16:48 = 8,80 h) e gravadas em `hours_per_day`.
10. **Dado** horas por dia acima de 24, turnos sobrepostos ou turno com fim antes do início (sem virar o dia), **quando** salvo, **então** a API responde 400 com a mensagem PT-BR do campo.
11. **Dado** `production.edit`, **quando** inativo um centro, **então** ele some dos seletores e a capacidade dele passa a 0.

**Capacidade (contrato para o planejamento)**
12. **Dado** o centro "Corte" com 8,00 h por dia, calendário seg–sex, o feriado de 20/11/2026 (sexta-feira, 0 h) e uma parada parcial em 18/11/2026 com 4 h disponíveis, **quando** chamo `IWorkCenterCapacity.GetAvailableHours("Corte", 16/11/2026, 22/11/2026)`, **então** recebo 8, 8, 4, 8, 0, 0, 0 por dia e 28 h no total.
13. **Dado** um centro inativo ou um intervalo antes da criação do centro, **quando** consulto a capacidade, **então** recebo 0 h nos dias afetados.
14. **Dado** a fixture "planejamento-base" (2 centros, calendário com feriado), **quando** os testes do PLN-01 rodarem, **então** reaproveitam a mesma fixture desta story (backlog CAD-03 e PLN-01).

**Contratos**
15. **Dado** HR, Engineering ou Production, **quando** precisam escolher um centro ou um local, **então** usam `IWorkCenters` (`Production.Contracts`) e `ILocations` (`Inventory.Contracts`), só com registros ativos; o teste de arquitetura impede outra referência.

**Permissões (3 camadas)**
16. **Dado** o módulo `inventory` (ou `production`) não liberado, **quando** chamo os endpoints de locais (ou de centros e calendário), **então** recebo 403 `module_not_released`; sem grupo que habilite o módulo, 403 `forbidden`; com `view` sem `create`/`edit`, as ações ficam ocultas por `<Can>` e a escrita responde 403 `forbidden`.

**Auditoria**
17. **Dado** qualquer alteração de local, centro, calendário ou exceção, **quando** abro "Histórico", **então** vejo antes/depois, usuário e data.

## Regras de negócio e técnicas
- **Horas:** `hours_per_day` em `numeric(5,2)` (**proposta** de precisão); turnos em JSON `[{ "start": "07:00", "end": "11:00" }]`, horários locais (America/Sao_Paulo), sem data.
- **Capacidade de um dia** = 0 se o centro está inativo ou o dia não é útil no calendário; senão `hours_available` da exceção, se houver; senão `hours_per_day`. A exceção nunca passa de `hours_per_day` (400).
- **Datas** de exceção são `date`; o cálculo usa datas locais, não UTC.
- **Feriados móveis**, calculados a partir da Páscoa: Sexta-feira Santa (sugerida marcada); Carnaval (segunda e terça) e Corpus Christi, que são pontos facultativos, sugeridos desmarcados. **Fixos:** 1/1, 21/4, 1/5, 7/9, 12/10, 2/11, 15/11, 20/11 e 25/12. A lista fica num só lugar, com teste, e é revisada pelo PO.
- **Local padrão:** único entre os ativos (índice único parcial).
- **Inativação:** `is_active` em `locations` (**proposta**, não está no 03); `work_centers.is_active` já existe.
- **Calendário:** `production.calendars` (**proposta** de campos: `name`, `is_default`, `working_days` JSON com os dias da semana) e `production.calendar_exceptions` (**proposta** de campos: `calendar_id`, `date`, `type`, `hours_available`, `description`; único por calendário e data).
- **Ids:** Guid v7. **Auditoria:** `IAuditable` em todas.

## Permissões, eventos e tabelas
- **Permissões (já no `ModuleCatalog`):** `inventory.view`, `inventory.create`, `inventory.edit`; `production.view`, `production.create`, `production.edit`. Nenhuma ação nova.
- **Eventos:** `CalendarChanged` (**proposta**, sem consumidor nesta fase; o `04-fluxos` §3.4 manda recalcular o plano a cada mudança de calendário — a PLN-01 consome).
- **Tabelas:** `inventory.locations` (+ `is_active`, **proposta**); `production.work_centers` (03); `production.calendars` e `production.calendar_exceptions` (campos **propostos**).

## Telas e estados
- **Estoque → Locais** (`/app/estoque/locais`): DataTable com código, nome, padrão (chip) e situação; painel de edição.
- **Produção → Centros de trabalho** (`/app/producao/centros-de-trabalho`): lista com nome, linha, horas/dia (`tabular-nums`), turnos resumidos e situação; editor de turnos com o total calculado ao vivo.
- **Produção → Calendário** (`/app/producao/calendario`): dias úteis da semana, lista de exceções por ano, "Sugerir feriados nacionais de {ano}".
- **Estados:** carregando (skeleton); vazio em centros ("Cadastre os centros de trabalho da fábrica, como Corte e Montagem, com os turnos de cada um." + "Novo centro"); vazio em exceções ("Nenhum feriado cadastrado em {ano}." + "Sugerir feriados nacionais"); erro com motivo e "Tentar de novo".
- **Propositivo:** turnos do último centro cadastrado vêm como sugestão no próximo; o calendário padrão vem selecionado; sugestão de feriados (CA 8).
- **Ajuda:** `Ajuda/estoque-locais.md` (aba `locais`), `Ajuda/producao-centros-de-trabalho.md` (aba `centros-de-trabalho`), `Ajuda/producao-calendario.md` (aba `calendario`); `<Dica>` em "Horas disponíveis" da exceção e em "Linha".

## Plano de testes
- **Unidade:** horas por dia a partir dos turnos e validação de sobreposição (CA 9, 10); capacidade por dia (CA 12, 13); cálculo da Páscoa e feriados móveis de 2026 e 2027 (CA 8).
- **Integração (Testcontainers):** locais com padrão único e seed idempotente (CA 1–3, 5); inativação com provedor de saldo de teste (CA 4); exceções com data repetida (CA 7); `IWorkCenterCapacity` com a fixture "planejamento-base" (CA 12–14); contratos só com ativos (CA 15); três camadas nos dois módulos (CA 16); auditoria (CA 17).
- **Arquitetura:** só `Production.Contracts` e `Inventory.Contracts` são referenciados por outros módulos.
- **E2E (Playwright):** cadastrar centro com dois turnos e sugerir feriados.

## Fora do escopo
- Capacidade reduzida por manutenção de máquina (R-PAT-5, PAT-03) e paradas registradas na produção (R-PRD-9).
- Motor de agendamento, fila e Gantt (PLN-01 a PLN-04).
- Sublocais (prateleira, posição) dentro do local.

## Pontos em aberto
1. **Excluir × inativar** (pré-condição): `inventory` e `production` não têm a ação `delete` no catálogo. Proposta: só inativar, com `edit`. Alternativa: incluir `delete` nos dois módulos (mudança no `ModuleCatalog`, ADR 0003).
2. **Calendário único ou por centro:** o 03 põe `calendar_id` no centro. Proposta: um calendário padrão do tenant, com a opção de outro calendário por centro.
3. **Horas por dia × turnos:** derivar dos turnos (proposta) ou permitir valor manual sem turnos.
4. **Pontos facultativos** (Carnaval, Corpus Christi): entram como sugestão desmarcada (proposta) ou não entram.
5. **Nome e código do local padrão do seed.**
6. **Turno que vira o dia** (ex.: 22:00–06:00): fora do escopo do MVP (proposta) ou suportado.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers), arquitetura e E2E passando.
- [ ] Migrations criadas (schemas `inventory` e `production`, não destrutivas) e seed idempotente do local padrão.
- [ ] Permissões usadas só do `ModuleCatalog`.
- [ ] Telas com estados de carregando, vazio e erro.
- [ ] Ajuda das três telas escrita; teste de cobertura passando.
- [ ] Padrões e próximo passo propositivos avaliados.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado; fixture "planejamento-base" documentada; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `Location` (Inventory) e `WorkCenter`, `Calendar`, `CalendarException` (Production), com as regras de turno e padrão.
- [ ] `IWorkCenterCapacity` e `IWorkCenters` em `Production.Contracts`; `ILocations` em `Inventory.Contracts`.
- [ ] Gerador de feriados nacionais (Páscoa) com testes.
- [ ] Migrations, seed do local padrão e do calendário padrão.
- [ ] Endpoints e telas; fixture "planejamento-base".

## Quality gates
- Agentes previstos: @dev, @qa.
- Gate: CA 12 (capacidade com feriado e parada) obrigatório.

## Validação do PO
Checklist do @po (Pax), 18/10/2026.

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo (cabeçalho, user story, contexto, CAs, regras, permissões/tabelas, telas, testes, fora do escopo, pontos em aberto, DoD, tarefas) | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas do CLAUDE.md) que se aplicam | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK |
| 7 | Plano de testes cobre os CAs (unidade, integração com Testcontainers, arquitetura, E2E quando cabe) | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas como proposta) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD do CLAUDE.md ajustado; pontos em aberto registrados sem decidir | OK |

- **Correções feitas na validação:** Lista de feriados: Carnaval e Corpus Christi tratados como pontos facultativos (sugeridos desmarcados).
- **Pré-condições para `Ready`:** Ponto em aberto 1 (excluir × inativar, sem `delete` em `inventory` e `production`).
- **Prontidão:** 9/10. **Veredito:** GO condicional (permanece `Draft` até as pré-condições serem decididas).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 18/10/2026 | 0.1 | Story detalhada a partir do backlog e do PRD R-PLN-1 | River (SM) |
| 18/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
