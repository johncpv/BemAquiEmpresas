# CAD-03 — Locais de estoque, centros de trabalho e calendário

| Campo | Valor |
|---|---|
| ID | CAD-03 |
| Título | Locais de estoque com local padrão, centros de trabalho com horas por dia derivadas dos turnos, calendário padrão do tenant com feriados e paradas, contrato de capacidade diária e ação `delete` em `inventory` e `production` |
| Tamanho | M (re-estimada: o backlog dizia P; são dois módulos, o calendário com feriados sugeridos e o contrato de capacidade usado pelo planejamento) |
| Status | Ready |
| Requisitos | R-EST-2 · R-PLN-1 · R-SEC-5 · R-PRP-1 |
| Dependências | FND-09d (moldes, `RowsEditor`, `TimeInput`); Fase 0 (FND-06, FND-06c, FND-07, FND-09c, FND-14) |
| Bloqueia | CAD-02a (schema `inventory` e `inventory.delete`), CAD-04a (centro de trabalho padrão do cargo), MIG-01c (locais), ENG-01, EST-01, PLN-01 |
| Ordem de execução | 7 (Marco B — catálogo real) |

## User story
Como **supervisor de produção e almoxarife**, quero **cadastrar os locais de estoque, os centros de trabalho com as horas disponíveis e o calendário de feriados e paradas**, para **que o estoque saiba onde está cada coisa e o planejamento calcule prazos com a capacidade real da fábrica**.

## Contexto e referências
- PRD R-EST-2 (vários locais de estoque), R-PLN-1 (centros com capacidade em horas/dia, turnos e calendário com feriados e paradas), R-PRD-9 (paradas na capacidade).
- `docs/03-modelo-dados.md` → `inventory.locations`; `production.work_centers`; `production.calendars` e `calendar_exceptions` (campos aprovados nesta story).
- `docs/04-fluxos-estados.md` §3.4 (agendamento respeitando calendário e horas/dia).
- `docs/06-pendencias-riscos.md` P-04 (etapas Projeto → Corte → Montagem → Qualidade → Expedição).
- **ADR 0016** (`delete` em `inventory` e `production`; `IWorkCenterCapacity` por id; mudança de calendário sem evento, lida pela PLN-01 por chamada síncrona), revisão pré-Fase 1: D10, tabela 1.1 (CAD-03), §2.4 (padrões).
- Padrões comuns a todos os cadastros: `docs/stories/README.md`. Locais e centros usam o molde **painel lateral**; o calendário é uma página própria.

## Critérios de aceite
**Catálogo de módulos**
1. **Dado** o `ModuleCatalog`, **quando** o seed roda (tenant novo ou existente no próximo deploy), **então** `inventory` e `production` passam a ter a ação `delete`; nenhum grupo do seed a recebe (fica com o Proprietário), e o `perm_version` não muda para quem não ganhou nada.

**Locais de estoque**
2. **Dado** `inventory.create`, **quando** cadastro um local com código (único) e nome e clico "Salvar local", **então** ele é gravado, auditado e o toast diz "Local salvo".
3. **Dado** um tenant novo ou existente no próximo deploy, **quando** o seed roda, **então** existe o local padrão "ALM — Almoxarifado principal" (código `ALM`), com `seed_key`, sem recriar o que o tenant excluiu (`IgnoreQueryFilters()`).
4. **Dado** `inventory.edit`, **quando** marco outro local como padrão, **então** ele passa a ser o único padrão (o anterior é desmarcado na mesma transação) e vem pré-selecionado nos movimentos futuros (R-PRP-1).
5. **Dado** `inventory.edit`, **quando** clico "Inativar local" e confirmo, **então** ele some dos seletores e continua visível onde é usado, com o chip neutro "Inativo"; o local padrão não pode ser inativado (409 "Escolha outro local padrão antes."); local com saldo não pode ser inativado (409 "O local tem saldo. Transfira o saldo antes."), regra ligada pelo EST-01 e testada aqui com um provedor de saldo de teste.
6. **Dado** `inventory.delete` e um local sem uso, **quando** clico "Excluir local" e confirmo "Excluir", **então** ele é excluído logicamente; com uso (movimento, saldo, centro ou item que o referencia), 409 "O local está em uso. Inative-o." e o diálogo oferece "Inativar".
7. **Dado** um código já usado, **quando** salvo, **então** a API responde 409 "Já existe o local {código}."

**Calendário**
8. **Dado** `production.view`, **quando** abro Produção → Calendário, **então** vejo o calendário padrão do tenant (um só no MVP), com os dias úteis da semana (padrão seg–sex) e as exceções do ano.
9. **Dado** `production.edit`, **quando** cadastro uma exceção com data, tipo (Feriado ou Parada), horas disponíveis (0 = dia parado) e descrição e clico "Salvar exceção", **então** ela é gravada; data repetida no calendário responde 409; horas acima das horas por dia do centro são limitadas no cálculo (CA 14).
10. **Dado** um ano, **quando** clico "Sugerir feriados nacionais de {ano}", **então** o sistema mostra a lista de feriados nacionais (fixos e móveis, a partir da Páscoa), com a Sexta-feira Santa marcada e os pontos facultativos (Carnaval e Corpus Christi) desmarcados, para eu confirmar "Adicionar feriados"; nada é gravado sem a confirmação (R-PRP-7). Estaduais e municipais são cadastrados à mão.
11. **[Segurança]** **Dado** a rota `/api/v1/production/calendars/{calendarId}/exceptions/{exceptionId}`, **quando** a exceção é de outro calendário, **então** a API responde 404 (IDOR pai/filho).

**Centros de trabalho**
12. **Dado** `production.create`, **quando** cadastro um centro com nome, linha e turnos (início e fim de cada um, no `RowsEditor`) e clico "Salvar centro de trabalho", **então** as horas por dia são calculadas dos turnos (ex.: 07:00–11:00 + 12:00–16:48 = 8,80 h), gravadas em `hours_per_day`, o centro usa o calendário padrão e o toast diz "Centro de trabalho salvo".
13. **Dado** turnos sobrepostos, turno com fim antes do início ("Turno que vira o dia não é aceito.") ou total acima de 24 h, **quando** salvo, **então** a API responde 400 com a mensagem PT-BR do campo.
14. **Dado** `production.edit`, **quando** inativo um centro, **então** ele some dos seletores e a capacidade dele passa a 0; com `production.delete` e sem uso, posso excluí-lo; com uso (cargo, roteiro, OS), 409 "O centro está em uso. Inative-o." e o diálogo oferece "Inativar".

**Capacidade (contrato para o planejamento)**
15. **Dado** o centro "Corte" (id X) com 8,00 h por dia, calendário seg–sex, o feriado de 20/11/2026 (sexta-feira, 0 h) e uma parada parcial em 18/11/2026 com 4 h, **quando** chamo `IWorkCenterCapacity.GetAvailableHours(X, 16/11/2026, 22/11/2026)`, **então** recebo 8, 8, 4, 8, 0, 0, 0 por dia e 28 h no total.
16. **Dado** um centro inativo ou um intervalo antes da criação do centro, **quando** consulto a capacidade, **então** recebo 0 h nos dias afetados.
17. **Dado** a fixture "planejamento-base" (2 centros, calendário com feriado), **quando** os testes do PLN-01 rodarem, **então** reaproveitam a mesma fixture desta story.

**Contratos**
18. **Dado** HR, Engineering ou Production, **quando** precisam escolher um centro ou um local, **então** usam `IWorkCenters` (`Production.Contracts`) e `ILocations` (`Inventory.Contracts`), só com ativos nos seletores e resolvendo inativos por id; o teste de arquitetura impede outra referência.

**Permissões (3 camadas)**
19. **Dado** o módulo `inventory` (ou `production`) não liberado, **quando** chamo os endpoints de locais (ou de centros e calendário), **então** recebo 403 `module_not_released`; sem grupo que habilite o módulo, 403 `forbidden`; com `view` sem `create`/`edit`/`delete`, as ações ficam ocultas por `<Can>` e a escrita responde 403 `forbidden`. Todo endpoint tem `RequireModule` e `perm:`.

**Auditoria**
20. **Dado** qualquer alteração de local, centro, calendário ou exceção, **quando** abro "Histórico de alterações", **então** vejo antes/depois, usuário e data.

## Regras de negócio e técnicas
- **Horas:** `hours_per_day` em `numeric(5,2)`, sempre derivado dos turnos; turnos em JSON `[{ "start": "07:00", "end": "11:00" }]`, horários locais (America/Sao_Paulo), sem data, sem turno que vira o dia.
- **Capacidade de um dia** = 0 se o centro está inativo, ainda não existia ou o dia não é útil; senão `min(hours_available da exceção, hours_per_day)`, se houver exceção; senão `hours_per_day`.
- **Datas** de exceção são `date`; o cálculo usa datas locais.
- **Feriados:** fixos 1/1, 21/4, 1/5, 7/9, 12/10, 2/11, 15/11, 20/11 e 25/12; móveis a partir da Páscoa (Sexta-feira Santa marcada; Carnaval e Corpus Christi como facultativos desmarcados). A lista fica num só lugar, com teste.
- **Calendário:** um padrão por tenant (`is_default`); `work_centers.calendar_id` aponta para ele e fica pronto para calendários por centro no futuro.
- **Mudança de calendário:** sem evento; o PLN-01 lê o calendário quando recalcula (ADR 0016).
- **Local padrão:** único entre os ativos (índice único parcial).
- **Excluir × inativar** (D10): `is_active` em `locations` e `work_centers`; exclusão lógica só sem uso, com `delete`.
- **Ids:** Guid v7. **Auditoria:** `IAuditable` em todas.

## Permissões, eventos e tabelas
- **Permissões:** `inventory.view`, `create`, `edit`, `delete` (nova, CA 1); `production.view`, `create`, `edit`, `delete` (nova, CA 1).
- **Eventos:** nenhum.
- **Tabelas:** `inventory.locations` (+ `is_active`, `seed_key`); `production.work_centers` (03); `production.calendars` (`name`, `is_default`, `working_days` JSON) e `production.calendar_exceptions` (`calendar_id`, `date`, `type`, `hours_available`, `description`; único por calendário e data).

## Telas e estados
- **Estoque → Locais** (`/app/estoque/locais`): h1 com a linha "Onde o material fica guardado."; DataTable com código (link), nome, padrão (chip) e situação; painel lateral.
- **Produção → Centros de trabalho** (`/app/producao/centros-de-trabalho`): lista com nome (link), linha, horas/dia (`tabular-nums`), turnos resumidos e situação; painel lateral com o `RowsEditor` de turnos e o total ao vivo (`aria-live`).
- **Produção → Calendário** (`/app/producao/calendario`): dias úteis, exceções por ano, "Sugerir feriados nacionais de {ano}".
- **Verbos:** "Salvar local", "Salvar centro de trabalho", "Salvar exceção", "Adicionar feriados", "Inativar", "Excluir"; diálogos com "Voltar".
- **Estados:** carregando; vazio em centros ("Cadastre os centros de trabalho da fábrica, como Corte e Montagem, com os turnos de cada um." + "Novo centro de trabalho"); vazio em exceções ("Nenhum feriado cadastrado em {ano}." + "Sugerir feriados nacionais"); vazio filtrado; erro com "Tentar de novo"; modo leitura.
- **Propositivo:** turnos do último centro vêm como sugestão no próximo; sugestão de feriados; depois do primeiro centro, a faixa oferece "Cadastrar o calendário".
- **400 px e teclado:** um CA de E2E para o editor de turnos.
- **Ajuda:** `Ajuda/estoque-locais.md` (aba `locais`), `Ajuda/producao-centros-de-trabalho.md` (aba `centros-de-trabalho`), `Ajuda/producao-calendario.md` (aba `calendario`); `<Dica>` em "Horas disponíveis" e em "Linha".

## Plano de testes
- **Unidade:** horas a partir dos turnos, sobreposição e turno que vira o dia (CA 12, 13); capacidade por dia (CA 15, 16); Páscoa e feriados móveis de 2026 e 2027 (CA 10).
- **Integração (Testcontainers):** `delete` no catálogo (CA 1); locais, padrão único e seed em tenant novo e existente (CA 2–4, 7); inativar e excluir com provedores de uso de teste (CA 5, 6, 14); exceções com data repetida e IDOR (CA 9, 11); `IWorkCenterCapacity` com a fixture (CA 15–17); contratos só com ativos (CA 18); três camadas (CA 19); auditoria (CA 20).
- **Arquitetura:** só `Production.Contracts` e `Inventory.Contracts` são referenciados por outros módulos; todo endpoint com `RequireModule` e `perm:`.
- **E2E (Playwright):** cadastrar centro com dois turnos (também a 400 px e só pelo teclado) e sugerir feriados.

## Fora do escopo
- Calendários por centro (fica preparado pelo `calendar_id`).
- Capacidade reduzida por manutenção (PAT-03) e paradas registradas na produção (R-PRD-9).
- Motor de agendamento, fila e Gantt (PLN-01 a PLN-04).
- Sublocais (prateleira, posição).

## Pontos em aberto
- Decidido em 18/10/2026 (D10, tabela 1.1 e ADR 0016): `delete` em `inventory` e `production` com o padrão excluir × inativar; um calendário padrão do tenant; horas por dia derivadas dos turnos; pontos facultativos sugeridos e desmarcados; turno que vira o dia fora do MVP; local padrão "ALM — Almoxarifado principal" (com depósito único no Dolibarr, a MIG-01c liga esse depósito ao ALM).
- Nenhum ponto em aberto.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers), arquitetura e E2E passando.
- [ ] Migrations criadas (schemas `inventory` e `production`, não destrutivas); seeds idempotentes (local e calendário padrão) e `delete` no catálogo.
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Telas com estados de carregando, vazio, vazio filtrado e erro.
- [ ] Ajuda das três telas escrita; teste de cobertura passando.
- [ ] Padrões comuns dos cadastros e próximos passos aplicados.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` conferido; fixture "planejamento-base" documentada; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `delete` em `inventory` e `production` no `ModuleCatalog`.
- [ ] `Location` (Inventory) e `WorkCenter`, `Calendar`, `CalendarException` (Production), com as regras de turno, padrão e uso.
- [ ] `IWorkCenterCapacity` (por id) e `IWorkCenters` em `Production.Contracts`; `ILocations` em `Inventory.Contracts`.
- [ ] Gerador de feriados nacionais com testes; seeds do local e do calendário padrão.
- [ ] Endpoints, telas e ajuda; fixture "planejamento-base".

## Quality gates
- Agentes previstos: @dev, @qa.
- Gate: CA 15 (capacidade com feriado e parada) obrigatório.

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

- **Correções feitas na validação (18/10/2026):** `delete` em `inventory` e `production` (D10) com excluir × inativar; valores da tabela 1.1; `IWorkCenterCapacity` por id e sem o evento `CalendarChanged`; IDOR nas exceções; seeds também nos tenants existentes; verbos específicos; ordem 7 do Marco B.
- **Pré-condições para `Ready`:** nenhuma pendente.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 18/10/2026 | 0.1 | Story detalhada a partir do backlog e do PRD R-PLN-1 | River (SM) |
| 18/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 18/10/2026 | 0.3 | Decisões do PO (D1, D10, tabela 1.1) e ADR 0016; ordem 7 do Marco B | River (SM) |
| 18/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
