# PLN-02 — Fila de prioridade dos projetos

| Campo | Valor |
|---|---|
| ID | PLN-02 |
| Título | Fila dos projetos com OS de fabricação abertas, do Production (`production.planning_queue`, ADR 0029), reordenada arrastando (ou pelo teclado) com `production.edit`, com a simulação "antes → depois" das datas previstas e da folga de cada projeto antes de salvar, aviso de quem passa a ficar em risco, histórico auditado das mudanças e recálculo do plano; projeto novo entra pela data prometida |
| Tamanho | M |
| Status | Done |
| Requisitos | R-PLN-2 (fila de projetos com prioridade manual, arrastar para ordenar) · R-PLN-3 · R-PLN-4 · R-PRP-2 · R-SEC-5 |
| Dependências | PLN-01a (motor, `SimulateAsync`), PLN-01b (data prevista, folga e risco), COM-05 (projeto), FND-09e (diálogo, concorrência) |
| Bloqueia | PLN-03 (ordem das linhas), PNL-01b ("próximos na fila"), PLN-05 (posição na simulação) |
| Ordem de execução | 8 (Marco 4.3 "Datas que a fábrica cumpre") |

## User story
Como **supervisor de produção**, quero **arrastar os projetos para mudar a ordem da fila e ver, antes de confirmar, o que acontece com a data de entrega de cada um**, para **decidir a prioridade sabendo quem eu atraso**.

## Contexto e referências
- PRD R-PLN-2; §2 (Supervisor de produção: "Sequência da fila").
- `04-fluxos-estados` §3.4 (1) "Ordena projetos pela prioridade manual" e (5) recalcula "a cada mudança de prioridade".
- **ADR 0029** (D10): a fila de prioridade é do Production, em `production.planning_queue`; `commercial.projects.priority` fica **sem uso, sem apagar a coluna** (apagar só com ADR, na contração); reordenar com `production.edit`, auditado; ordem pela data prometida, depois a assinatura.
- `03-modelo-dados`: `commercial.projects.priority` ("int para fila"). **Código atual:** `Project.Priority` (`int?`) existe no Commercial e não é usado.
- CLAUDE.md (stack): dnd-kit para arrastar.
- Fase 3: PRD-02 ("o arraste dentro da coluna também não reordena; prioridade é a PLN-02", item 60); **EST-02 item 28 decidido**: a ordem das faltas é "primeiro a OS que originou a compra, depois a BOM liberada há mais tempo" (não usa a fila).
- `GroupSeeder`: Supervisão de produção tem `production.view/create/edit/approve` e só `projects.view`.
- **Revisão pré-Fases 4 a 8** (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`): D10; tabela 3.1, pontos 36 a 39; §4.1 (item 28 da Fase 3 fechado); §4.2 (risco de migração: `priority` sem uso).

## Critérios de aceite
**Fila**
1. **Dado** `production.view`, **quando** abro Planejamento → Fila (`/app/producao/planejamento?aba=fila`), **então** vejo os projetos que têm OS de fabricação não concluída e não cancelada, na ordem da fila, com: posição, número e nome do projeto (link), cliente, OS abertas, horas restantes, prometido, previsto (PLN-01b), folga em dias e o chip (No prazo, Em risco, Atrasado, Sem previsão).
2. **Dado** um projeto que ganha a primeira OS de fabricação (PRD-01), **quando** a fila é mantida, **então** ele entra **pela data prometida**: logo antes do primeiro projeto da fila com data prometida posterior à dele (no empate de data, depois dos que já estavam; sem data prometida, no fim); o projeto cujas OS de fabricação terminaram ou foram canceladas sai da fila; as posições continuam contíguas (1, 2, 3…), sem buraco; a entrada e a saída automáticas ficam no histórico com o motivo ("Entrou pela data prometida", "Saiu: OS concluídas").
3. **Dado** a migration desta story, **quando** roda num tenant existente, **então** a carga inicial monta a fila pela data prometida, depois pela data de assinatura e pelo número (a mesma ordem da PLN-01a CA 2), sem ler nem gravar `commercial.projects.priority`.

**Reordenar com prévia**
4. **Dado** `production.edit`, **quando** arrasto um projeto para outra posição (ou uso "Mover para cima", "Mover para baixo" e "Mover para a posição…" pelo teclado), **então** a fila entra em modo de edição com a faixa "Ordem alterada, ainda não salva." e, em até 2 s, cada linha mostra a prévia "previsto antes → depois" e a folga nova, calculadas pelo `IProductionSchedule.SimulateAsync` com a ordem nova, sem gravar nada.
5. **Dado** a prévia, **quando** algum projeto passa a ficar em risco ou piora, **então** as linhas dele ficam destacadas e, ao clicar "Salvar ordem", o diálogo lista "Estes projetos passam a atrasar: {projeto} ({n} dias depois do prometido)…" com "Salvar mesmo assim" e "Voltar"; sem piora, salva direto.
6. **Dado** "Salvar ordem", **quando** confirmo, **então** a nova ordem grava numa transação (com a versão da fila; outra pessoa que salvou antes → 409 `concurrency_conflict` "A fila mudou desde que você abriu. Recarregue."), a mudança fica no histórico auditado (quem, quando, posições antes → depois), um recálculo é pedido (PLN-01b) e a mensagem de sucesso diz "Ordem salva. Recalculando as datas…"; "Descartar" volta à ordem gravada.
7. **[Segurança]** **Dado** o comando de salvar, **quando** a lista enviada não é uma permutação exata dos projetos da fila atual (projeto a mais, a menos, repetido ou de outro tenant), **então** 422 "A fila mudou. Recarregue e reordene de novo." sem gravar.

**Histórico e uso por outros módulos**
8. **Dado** a aba Fila, **quando** abro "Mudanças na fila", **então** vejo as mudanças manuais e automáticas com quem (ou "Sistema"), quando, o motivo e as posições antes → depois (auditoria).
9. **Dado** o `IPlanningQueue` em `Production.Contracts` (novo), **quando** o motor (PLN-01a), o painel da TV (PNL-01b) ou a simulação do orçamento (PLN-05) pedem `OrderAsync()`, **então** recebem os ids dos projetos na ordem da fila; o Estoque não usa a fila para as faltas (item 28 da Fase 3).

**Desempenho, permissões e segurança**
10. **Dado** 50 projetos e 300 OS na fila, **quando** reordeno, **então** a prévia chega em menos de 2 s e a fila abre em menos de 1 s; a prévia tem rate limit pela política nomeada `planning-simulate` (20 por minuto por usuário; acima, 429 "Aguarde alguns segundos para simular de novo.").
11. **Dado** o módulo `production` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo ou sem `production.view`, 403 `forbidden`; salvar sem `production.edit` responde 403 e o arraste fica desligado (a fila aparece só para leitura, com a nota "Para mudar a ordem, peça a quem cuida da produção."). `RequireModule` e `perm:`.

## Regras de negócio e técnicas
- **Dono da fila** (ponto 36): o Production, na tabela `production.planning_queue` (`project_id` único, `position`), com uma linha de versão (`production.planning_queue_state`, `xmin`); o `commercial.projects.priority` fica sem uso, sem apagar (ADR 0029).
- **Reordenar** (ponto 37): `production.edit`, auditado (`IAuditable` na fila, `position` antes/depois).
- **Projeto novo** (ponto 38): entra pela data prometida.
- **Faltas** (ponto 39): a fila não ordena as faltas (item 28 da Fase 3).
- **A prévia é simulação**: o motor roda em memória com a ordem nova (`SimulateAsync`), nada é gravado até "Salvar ordem".
- **Sem efeito financeiro, fiscal nem de estoque**; a confirmação existe só quando a ordem atrasa alguém (R-PRP-7 não se aplica, mas o aviso é propositivo).
- Guid v7.

## Permissões, eventos e tabelas
- **Permissões:** `production.view` (ver), `production.edit` (reordenar).
- **Eventos:** nenhum no outbox (o recálculo é pedido dentro do módulo, PLN-01b).
- **Contratos:** novo `IPlanningQueue` (`Production.Contracts`); usa `IProductionSchedule`, `IProjects` (data prometida e assinatura), `IParties`.
- **Tabelas:** `production.planning_queue` e `production.planning_queue_state` (novas, ADR 0029); `commercial.projects.priority` sem uso.

## Telas e estados
- **Padrões comuns das Fases 4 a 8:** 7 (impacto em lista antes de confirmar: o diálogo lista os projetos que passam a atrasar).
- **Planejamento → Fila** (aba padrão do Planejamento a partir desta story): lista com alça de arraste (dnd-kit), botões de mover para o teclado, faixa de edição, prévia em duas linhas por projeto ("05/12 → 09/12", folga "−7 dias").
- **Verbos:** "Salvar ordem" / "Ordem salva"; "Descartar".
- **Estados:** carregando; vazio ("Nenhum projeto com OS de fabricação aberta." + "Ver OS de fabricação"); erro; simulação em andamento ("Calculando o impacto…"); leitura sem permissão (CA 11); limite de simulação (CA 10).
- **Propositivo:** a prévia mostra o efeito antes de salvar; o projeto "Sem previsão" leva ao motivo; o projeto novo já entra na posição da data prometida.
- **Acessibilidade:** a reordenação funciona só pelo teclado (dnd-kit com sensores de teclado e anúncio "Projeto {n} movido para a posição {p}" por `aria-live`).
- **400 px:** sem arraste; cada linha com "Subir" e "Descer" (alvos de 44 px), número, previsto e chip; a prévia aparece abaixo de cada linha.
- **Ajuda:** `Ajuda/producao-planejamento-fila.md` (aba `fila`, `padrao: true`; a ajuda padrão da PLN-01a passa a ser desta aba; como o projeto novo entra); `<Dica>` em "Folga" e "Prévia".

## Plano de testes
- **Unidade:** entrada pela data prometida, saída da fila e posições contíguas (CA 2); validação da permutação (CA 7).
- **Integração (Testcontainers):** carga inicial sem tocar `priority` (CA 3); prévia sem gravar e igual ao cálculo depois de salvar (CA 4, 6); aviso de quem atrasa (CA 5); concorrência com duas abas (CA 6); permutação inválida e projeto de outro tenant (CA 7); histórico manual e automático (CA 8); `IPlanningQueue` (CA 9); desempenho e rate limit (CA 10); três camadas (CA 11).
- **E2E (Playwright):** arrastar um projeto, ver a prévia, salvar e ver as datas novas; reordenar só pelo teclado; tela a 400 px com "Subir"/"Descer".

## Fora do escopo
- Prioridade por OS dentro do projeto (a ordem segue o item do projeto, `04-fluxos` §3.4).
- Mudar a etapa pelo arraste no Kanban (item 60 da Fase 3).
- Fila por linha de produção (uma fila só por tenant no MVP).
- Remover `commercial.projects.priority`: só com ADR, na contração.

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fases 4 a 8, tabela 3.1, pontos 36–39):
1. Fila no Production (`production.planning_queue`); `priority` do projeto sem uso, sem apagar (D10, ADR 0029; CA 3).
2. Reordenar com `production.edit`, auditado (D10; CA 6, 8, 11).
3. Projeto novo entra pela data prometida (CA 2).
4. Ordem das faltas: item 28 da Fase 3 (primeiro a OS que originou a compra, depois a BOM liberada há mais tempo); a fila não entra (CA 9).

Decisões da revisão aplicadas: D10 e ADR 0029; §4.1 (item 28 da Fase 3 fechado); §4.2 (`priority` sem uso); §4.3 transversal (rate limit por política nomeada na simulação, CA 10).

**Pré-condição externa:** nenhuma.

## Definition of Done
- [x] Critérios de aceite atendidos.
- [x] Testes de unidade, arquitetura e E2E (arraste, teclado e 400 px) passando; integração (Testcontainers) e desempenho escritos, a rodar na suíte em container antes do merge.
- [ ] Migration `PlanningQueue` (não destrutiva, só `CREATE TABLE`; a carga inicial pela data prometida é a primeira manutenção da fila), a testar sobre cópia do banco do tenant `konebras` no deploy.
- [x] Permissões só do `ModuleCatalog` (`production.view` e `production.edit`, já existentes).
- [x] Tela com estados, acessível pelo teclado e a 400 px.
- [x] Ajuda da aba; teste de cobertura passando.
- [x] Prévia e aviso de atraso aplicados.
- [x] Auditoria ligada (posição e versão da fila no `audit_log`; histórico legível em `planning_queue_changes`).
- [x] OpenAPI e cliente Orval regenerados (OpenAPI gerado pelo Host em processo; regenerar oficialmente com `BQ_UPDATE_OPENAPI`).
- [x] `docs/03-modelo-dados.md` (`planning_queue`, `priority` sem uso) e `docs/04-fluxos-estados.md` (§3.4 itens 1 e 5) atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [x] `planning_queue` e `planning_queue_state`; manutenção da fila na criação e no fim das OS, pela data prometida.
- [x] Endpoints `/api/v1/production/planning/queue` (GET, `/preview`, PUT); `IPlanningQueue`; política `planning-simulate`.
- [x] Aba Fila com dnd-kit, prévia e ajuda.

## Quality gates
- Agentes previstos: @dev, @qa, design (arraste, teclado e 400 px).
- Gate: CA 2, 4, 6 e 7 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fases 4 a 8).

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
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** o dono da prioridade conflitava com `commercial.projects.priority` do 03 e ficou como ponto em aberto, sem mudar o modelo por conta própria; prévia por simulação sem gravar (CA 4); permutação validada no servidor (CA 7).
- **Correções da revalidação (21/10/2026):** pontos 36 a 39 decididos (fila no Production com `priority` sem uso, CA 3; `production.edit` auditado; projeto novo pela data prometida, com entrada e saída no histórico, CA 2, 8; faltas pelo item 28 da Fase 3, sem a fila, CA 9); carga inicial da migration (CA 3); rate limit da prévia por política nomeada (§4.3, CA 10); CAs renumerados (11).
- **Pré-condições para `Ready`:** nenhuma de decisão de story.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (D10, tabela 3.1 pontos 36–39, §4.1, §4.2, §4.3, §4.4 padrão 7; ADR 0029); ordem mantida (Marco 4.3, ordem 8); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
| 25/10/2026 | 1.0 | Implementada (`Done`), sobre a PLN-01b (branch `feat/pln-01b-recalculo`, ainda fora da main). `PlanningQueueEntry` (`planning_queue`, auditada), `PlanningQueueState` (`planning_queue_state`, `xmin`, auditada) e `PlanningQueueChange` (`planning_queue_changes`); regras puras `PlanningQueueRules` (entrada pela data prometida, saída, posições contíguas, permutação, movimentos); `PlanningQueueStore` (fila efetiva sem gravar, manutenção sob o advisory lock `production.planning_queue` e `IPlanningQueue.OrderAsync`); `PlanningQueueService` (fila, prévia, salvar e mudanças); o `ScheduleService` mantém a fila na transação do recálculo e passa a ordem ao motor; endpoints `GET/PUT /production/planning/queue`, `POST /preview` (política `planning-simulate`) e `GET /changes`; migration `PlanningQueue` (só `CREATE TABLE`); aba Fila (padrão do Planejamento) com dnd-kit, teclado, prévia, aviso de quem atrasa, "Salvar ordem"/"Descartar", histórico e 400 px; ajuda `producao-planejamento-fila.md`. **Desvios:** (1) Tabela a mais, `production.planning_queue_changes` (só inserção, fora da auditoria), não citada na ADR 0029: é o histórico legível do CA 8 (quem ou Sistema, motivo, posições antes → depois numa linha por mudança); a posição de cada projeto e a versão também vão para o `audit_log` (`IAuditable`). (2) CA 3: a carga inicial não é SQL na migration (leria `commercial.projects` de outro módulo, ADR 0006); a migration só cria as tabelas e a primeira manutenção num tenant sem fila monta a fila pela data prometida, depois a assinatura e o número ("Fila montada pela data prometida"), sem ler nem gravar `priority`. A manutenção roda em todo recálculo do plano (o primeiro chega em até 1 h depois do deploy, ou antes, no primeiro evento) e no "Salvar ordem"; até lá a tela já mostra a fila na ordem padrão, calculada em memória. (3) CA 2: a entrada e a saída não são feitas pelo comando da OS, e sim pela manutenção do recálculo que esse comando já pede (PLN-01b, em até 10 s); a tela e o `IPlanningQueue` já mostram o projeto novo na posição da data prometida antes disso (fila efetiva, sem gravar). Saída tem dois motivos: "Saiu: OS concluídas" (alguma concluída) e "Saiu: OS canceladas". (4) CA 4: a prévia usa o mesmo motor do `SimulateAsync` (`ScheduleInputBuilder` + `FiniteScheduler` em memória, uma leitura e duas execuções) e o `ProjectForecastBuilder.BuildManyAsync` (novo), para a data prevista ter o prazo de instalação como no recálculo; o contrato `SimulateAsync` devolve só o fim da fabricação. O "antes" é a simulação com a ordem gravada (não a previsão publicada), para a diferença mostrar só o efeito da ordem. (5) CA 5: "piora" = a previsão nova passa da prometida e fica depois da anterior (ou antes não havia previsão). (6) CA 6 e 7: a versão (`xmin`) só sobe no "Salvar ordem"; entrada e saída automáticas não mexem nela, e a lista antiga deixa de ser permutação (422 `planning_queue.changed`, que a prévia também confere); mudar a data prometida por aditivo não move o projeto (só a reordenação manual move), a opção mais segura (recomendação aceita, PO 24/10). (7) CA 10: o `UseRateLimiter` passou para depois do `UseAuthentication`, para a política nomeada contar por usuário (as janelas de login continuam por IP e tenant); o 429 da prévia tem a mensagem própria. A prévia exige `production.edit` (quem só vê não reordena). (8) Planejamento ganhou abas: Fila (padrão) e "Sem previsão" (a lista da PLN-01a); a ajuda da PLN-01a virou a da aba `sem-previsao` (`padrao: false`). (9) dnd-kit entrou agora (`@dnd-kit/core` 6.3.1, `sortable` 10.0.0, `utilities` 3.2.2): o Kanban não usa arraste (PRD-02, item 60). Abaixo de 1024 px não há alça: "Subir" e "Descer" (44 px); "Posição…" só na tela larga. (10) No salvar, quem entrou ou saiu na mesma transação aparece como Sistema no histórico, mas o `audit_log` dessas linhas leva o usuário do request. (11) `IPlanningQueue` com `SuppressMessage` do CA1711 (o nome vem da story). (12) Testes de integração e de desempenho (CA 10, em `PlanningTests`) escritos e não executados nesta máquina; 9 testes de arquitetura não rodam no Windows com Smart App Control (bloqueio das DLLs, não ligado à story). (13) OpenAPI gerado pelo Host em processo, sem banco: regenerar oficialmente com `BQ_UPDATE_OPENAPI`. (14) Migration ainda não aplicada sobre cópia do banco `konebras`. | Dex (dev) |
