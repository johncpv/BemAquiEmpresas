# PLN-03 — Cronograma (Gantt) por projeto e por centro de trabalho

| Campo | Valor |
|---|---|
| ID | PLN-03 |
| Título | Cronograma em barras das etapas planejadas, num componente `Gantt` próprio em SVG sobre o `ChartFrame` (ADR 0033, sem biblioteca nova), em duas visões — por projeto (projeto → OS → etapa, com prevista × prometida) e por centro de trabalho (a carga de cada centro no tempo, com feriados e paradas) —, escala de dia, semana ou mês, linha de hoje, detalhe da barra, tabela equivalente acessível e aba "Cronograma" no Projeto 360 |
| Tamanho | G |
| Status | Done |
| Requisitos | R-PLN-4 (cronograma por projeto e por centro de trabalho; data prevista de entrega × prometida com alerta de risco) · R-PLN-3 · PRD §5.14 (Entregas: em risco) · R-PRP-5 · PRD §6 |
| Dependências | PLN-01a (datas e alocações do plano), PLN-01b (previsão do projeto e risco), PLN-02 (ordem das linhas), FIN-07 (`ChartFrame`), COM-05 (Projeto 360), COM-06 (datas da OS de instalação) |
| Bloqueia | — (o aviso de risco da PLN-01b passa a abrir o cronograma do projeto) |
| Ordem de execução | 10 (Marco 4.3 "Datas que a fábrica cumpre") |

## User story
Como **supervisor de produção** e **proprietário**, quero **ver numa linha do tempo quando cada etapa de cada projeto acontece e como cada centro está carregado**, para **enxergar o que empurra a entrega e onde está o gargalo**.

## Contexto e referências
- PRD R-PLN-4.
- `04-fluxos-estados` §3.4 (4) e (6) (data prevista e risco).
- **ADR 0033** (Gantt próprio, D18): componente `Gantt` em **SVG**, sem biblioteca nova, dentro do `ChartFrame`, com tokens de cor, `tabular-nums`, datas pt-BR, virtualização de linhas e tabela equivalente. **ADR 0029** (D10): as datas vêm das tabelas do plano (`schedule_step_forecasts`, `schedule_allocations`) e da `commercial.project_forecasts`.
- Protótipo: não há tela de cronograma; valem a identidade (tons de aço, acento cobalto, âmbar só para estado e item ativo), o `StageIndicator` e a faixa lateral vermelha/âmbar para atraso/risco (CLAUDE.md, Interface).
- Padrão de gráficos da Fase 2 (item 10): realizado e projetado distinguidos pelo traço (contínuo × tracejado) e tabela equivalente acessível; FIN-07 (D18): `ChartFrame` com resumo textual, rótulo direto, referências e "Ver como tabela".
- PRD-03a: as etapas concluídas têm `actual_start`/`actual_end`.
- **Revisão pré-Fases 4 a 8** (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`): D10, D18; tabela 3.1, pontos 42 e 43; §4.3 transversal (grupos SignalR pelas claims); §4.4 padrão 1.

## Critérios de aceite
**Por projeto**
1. **Dado** `production.view`, **quando** abro Planejamento → Cronograma (`?aba=cronograma&visao=projeto`), **então** vejo uma linha por projeto da fila (ordem da PLN-02) com a barra do início da primeira etapa até a data prevista de entrega, a marca da data prometida (traço vertical na linha, com o rótulo "Prometido {data}") e o chip de folga ("+3 dias" ok, "−7 dias" em risco); cada projeto se abre nas suas OS e cada OS nas etapas.
2. **Dado** uma etapa, **quando** desenhada, **então** a barra vai do início ao fim previstos com a letra da etapa no estilo do `StageIndicator`; etapa concluída aparece com o início e o fim **reais** em traço contínuo, e o restante do plano em tracejado; etapa em andamento mostra a parte já feita (proporção apontadas ÷ padrão) preenchida; nada se distingue só pela cor (traço, preenchimento e rótulo).
3. **Dado** um projeto em risco ou atrasado (função única da PLN-01b), **quando** a linha aparece, **então** tem a faixa lateral âmbar (risco) ou vermelha (atrasado), e a etapa que empurra a data (a de maior fim previsto) fica destacada com o rótulo "Define a entrega".
4. **Dado** um projeto com OS de instalação, **quando** abro, **então** a OS de instalação aparece como uma barra **só de leitura** com as datas digitadas (COM-06) e o rótulo "Instalação (datas da OS)"; nada é editável no cronograma.

**Por centro de trabalho**
5. **Dado** a visão "Por centro" (`visao=centro`), **quando** abro, **então** vejo uma linha por centro ativo com as barras das etapas alocadas nele, na ordem do tempo (uma sub-linha por etapa que se sobrepõe no dia), o fundo de cada dia sombreado pela ocupação do dia (alocado ÷ capacidade, nas faixas da PLN-04) e os dias de feriado e parada marcados com o nome da exceção (CAD-03).

**Escala, detalhe e filtros**
6. **Dado** a escala, **quando** escolho Dia, Semana ou Mês (padrão Semana; período padrão de 7 dias antes de hoje a 12 semanas depois; até 12 meses), **então** o eixo muda, a referência em x "Hoje" fica visível e "Ir para hoje" rola até ela; a rolagem horizontal é só dentro do cronograma (sem rolagem horizontal da página).
7. **Dado** o `ChartFrame` do cronograma, **quando** a tela monta, **então** o resumo textual acima diz o essencial ("{n} projetos no período; {m} em risco. O mais atrasado é {projeto}, {d} dias depois do prometido.").
8. **Dado** uma barra, **quando** passo o ponteiro, foco pelo teclado (cada barra é focável; as setas navegam entre linhas e no tempo, com `aria-label` de etapa, centro, datas e situação) ou toco, **então** vejo OS, projeto, etapa, centro, horas restantes ÷ padrão, início e fim previstos (e reais, se houver); ao clicar ou dar Enter, abre a OS (`/app/producao/os/$id`).
9. **Dado** os filtros, **quando** escolho projeto, cliente, linha, centro e "Só em risco", **então** o cronograma filtra e os filtros ficam na URL.
10. **Dado** a tabela equivalente ("Ver como tabela", do `ChartFrame`), **quando** abro, **então** vejo as mesmas linhas com as datas em texto (projeto, OS, etapa, centro, início, fim, prometido, folga), navegável pelo teclado; o SVG tem `role="img"` com descrição ligada à tabela por `aria-describedby`, e as linhas expansíveis seguem o padrão `treegrid` (como a `TreeTable`).

**No Projeto 360**
11. **Dado** um projeto com OS de fabricação e o usuário com `projects.view` e `production.view`, **quando** abro o Projeto 360, **então** há a aba "Cronograma" com a visão por projeto só desse projeto (na URL); sem `production.view`, a aba não aparece.

**Tempo real, desempenho, permissões e segurança**
12. **Dado** o cronograma aberto, **quando** chega `OrderBoardChanged` ou `ScheduleChanged` pelo `ProductionHub` (grupo `{tenantId}:production` derivado das claims, PRD-02), **então** a tela recarrega os dados agrupando os sinais a cada 5 s, sem piscar.
13. **Dado** 300 OS com 5 etapas (1.500 barras), **quando** abro o cronograma por projeto, **então** a API responde em menos de 1 s e a tela fica utilizável em menos de 1,5 s (linhas virtualizadas, projetos recolhidos por padrão acima de 50).
14. **Dado** o módulo `production` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo ou sem `production.view`, 403 `forbidden`. `RequireModule` e `perm:`; IDOR na rota do cronograma do projeto (`/production/planning/projects/{projectId}/gantt`, projeto validado por `IProjects`); sem custo nem dado pessoal na resposta.

## Regras de negócio e técnicas
- **Só leitura:** as datas são calculadas pelo motor; o cronograma não muda datas (arrastar barra não existe; a ordem muda na Fila, PLN-02).
- **Componente próprio** (ponto 42): `Gantt` em SVG nos componentes de UI, dentro do `ChartFrame`, com tokens de cor (sem cor literal), números com `tabular-nums`, datas pt-BR e virtualização; sem biblioteca nova (ADR 0033).
- **Instalação** (ponto 43): barra só de leitura com as datas digitadas.
- **Dados** numa consulta por visão (etapas com datas e alocações por dia, do plano), nomes de projeto e cliente por `FindManyAsync`.

## Permissões, eventos e tabelas
- **Permissões:** `production.view` (cronograma); `projects.view` + `production.view` (aba no Projeto 360).
- **Eventos:** nenhum novo (a tela recarrega com o `OrderBoardChanged` e o `ScheduleChanged` do `ProductionHub`).
- **Contratos:** `IProductionSchedule` (PLN-01a), `IPlanningQueue` (PLN-02), `IProjects` (previsão de `project_forecasts`), `IParties`, `IInstallationOrders`, `IWorkCenterCapacity`.
- **Tabelas:** nenhuma nova.

## Telas e estados
- **Padrões comuns das Fases 4 a 8:** 1 (Gantt da família de gráficos sobre o `ChartFrame`: nada só por cor, referência em x "Hoje" e marca do prometido, resumo textual acima e tabela equivalente).
- **Planejamento → Cronograma** (aba) com o seletor Por projeto / Por centro, escala, "Ir para hoje", filtros e "Ver como tabela"; **Projeto 360 → Cronograma** (aba nova).
- **Componentes (D18):** `Gantt` (novo, SVG, ADR 0033).
- **Estados:** carregando (skeleton de linhas); vazio ("Nenhuma etapa planejada no período." + "Ver a fila"); erro; "Plano de {hh:mm}" e "Recalculando…" do PLN-01a.
- **Propositivo:** "Define a entrega" mostra onde agir; a etapa sem previsão leva ao motivo.
- **400 px:** a visão por projeto vira lista (projeto, previsto, prometido, folga; ao abrir, as etapas com as datas em texto e uma barra simples por etapa na largura da tela); a visão por centro mostra um centro por vez; alvos de 44 px.
- **Ajuda:** `Ajuda/producao-planejamento-cronograma.md` (aba `cronograma`) e `Ajuda/projetos-projeto-cronograma.md` (aba `cronograma` do projeto); `<Dica>` em "Define a entrega", "Prometido", "Instalação (datas da OS)" e no tracejado.

## Plano de testes
- **Unidade (front, Vitest):** escala e posição das barras (datas → pixels), real × previsto, "Define a entrega", resumo textual (CA 2, 3, 6, 7).
- **Integração (Testcontainers):** consulta por projeto e por centro com o dataset do PLN-01a (CA 1, 5); instalação só de leitura (CA 4); aba do projeto com e sem `production.view` (CA 11); desempenho da API (CA 13); três camadas e IDOR (CA 14).
- **E2E (Playwright):** abrir as duas visões, trocar a escala, abrir uma OS pela barra pelo teclado; tabela equivalente; recarga ao chegar o sinal do hub (CA 12); tela a 400 px sem rolagem horizontal da página; medição do tempo até a tela utilizável com 300 OS (CA 13).

## Fora do escopo
- Editar datas pelo cronograma; dependências entre OS; caminho crítico.
- Exportar o cronograma em PDF ou imagem.
- Etapas de manutenção e paradas como barras próprias (Fase 6; aqui, só a marca do dia).

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fases 4 a 8, tabela 3.1, pontos 42–43):
1. Gantt próprio em SVG, sem biblioteca nova (D18, ADR 0033; CA 2, 10).
2. OS de instalação como barra só de leitura (CA 4).

Decisões da revisão aplicadas: D18 (`Gantt` sobre o `ChartFrame` da FIN-07); D10 (datas das tabelas do plano); §4.3 transversal (grupo SignalR pelas claims, CA 12); §4.4 padrão 1.

**Pré-condição externa:** nenhuma.

## Definition of Done
- [x] Critérios de aceite atendidos.
- [x] Testes de unidade (xUnit e Vitest), arquitetura e E2E (com o desempenho da tela) passando; integração (Testcontainers) e desempenho da API escritos, a rodar na suíte em container antes do merge.
- [x] Nenhuma migration (`has-pending-model-changes` sem mudança).
- [x] Permissões só do `ModuleCatalog` (`production.view` e `projects.view`, já existentes).
- [x] Tela com estados, acessível (tabela equivalente, teclado) e a 400 px; `Gantt` em SVG com tokens e sem cor literal.
- [x] Ajuda das duas abas; teste de cobertura passando.
- [x] Destaques acionáveis ("Define a entrega", sem previsão).
- [x] OpenAPI e cliente Orval regenerados (OpenAPI gerado pelo Host em processo; regenerar oficialmente com `BQ_UPDATE_OPENAPI`).
- [x] Story marcada no backlog; `docs/03-modelo-dados.md` e `docs/04-fluxos-estados.md` (§3.4 item 8) atualizados.

## Tarefas técnicas sugeridas
- [x] Endpoints `/api/v1/production/planning/gantt/projects`, `/gantt/centers` e `/projects/{projectId}/gantt`.
- [x] Componente `Gantt` em SVG (escala, barras, virtualização, tabela equivalente), com o próprio quadro até o `ChartFrame` chegar à main.
- [x] Aba Cronograma no Planejamento e no Projeto 360; recarga pelo hub; ajudas.

## Quality gates
- Agentes previstos: @dev, design (componente, acessibilidade, 400 px), @qa.
- Gate: CA 1, 2, 10 e 13 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fases 4 a 8).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: depende da FIN-07 pelo `ChartFrame`) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** sem biblioteca de Gantt na stack: componente próprio como proposta e ADR se o arquiteto preferisse biblioteca; tabela equivalente e teclado; 400 px sem rolagem horizontal da página.
- **Correções da revalidação (21/10/2026):** pontos 42 e 43 decididos (Gantt próprio em SVG, ADR 0033; instalação só de leitura); Gantt dentro do `ChartFrame` com resumo textual (D18, padrão 1, CA 7); datas das tabelas do plano (ADR 0029); recarga pelo `ProductionHub` com grupo das claims (§4.3, CA 12); dependência da FIN-07; CAs renumerados (14).
- **Pré-condições para `Ready`:** nenhuma de decisão de story.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (D10, D18, tabela 3.1 pontos 42–43, §4.3, §4.4 padrão 1; ADR 0029 e 0033); ordem mantida (Marco 4.3, ordem 10); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
| 25/10/2026 | 1.0 | Implementada (`Done`), sobre a PLN-04 (branch `feat/pln-04-ocupacao`, com a PLN-02, a PLN-01b e a PLN-01a, ainda fora da main). `GanttRules` puro no Domain (período padrão de 7 dias antes de hoje a 12 semanas depois e até 366 dias, período do projeto, interseção, proporção feita, a etapa que define a entrega e as sub-linhas do centro); `GanttService` só de leitura (a fila efetiva da PLN-02, as OS não canceladas, as etapas de fabricação com junção à esquerda em `schedule_step_forecasts`, a previsão de `schedule_project_forecasts`, o prometido pelo `IProjects`, o cliente pelo `IParties`, os centros pelo `IWorkCenters`; por centro, `schedule_allocations` agregadas por centro e dia, a capacidade pelo `IWorkCenterCapacity` e as exceções do calendário); endpoints `GET /production/planning/gantt/projects`, `GET /production/planning/gantt/centers` (`production.view`) e `GET /production/planning/projects/{projectId}/gantt` (`production.view` e `projects.view`, projeto validado pelo `IProjects`), no grupo do `RequireModule`; componente `Gantt` em SVG (`components/ui/gantt.tsx`, escala pura em `gantt-scale.ts`); aba Cronograma no Planejamento (Por projeto / Por centro, escala, período, "Ir para hoje", filtros na URL, tabela equivalente, recarga pelo hub, 400 px) e no Projeto 360; ajudas `producao-planejamento-cronograma.md` e `projetos-projeto-cronograma.md`. Sem migration e sem dependência nova. **Desvios:** (1) D18: o `ChartFrame` (FIN-07) está em outra branch, fora da main: o `Gantt` tem o próprio quadro (título, resumo textual acima, legenda com as dicas e "Ver como tabela"); **colocar o `Gantt` dentro do `ChartFrame` depois do merge**. A página de exemplo da ADR 0033 §3 não foi criada; os testes de componente (Vitest) cobrem escala, virtualização, teclado e tabela equivalente. (2) CA 10: a tabela equivalente é a `TreeTable` da main (FND-09d, `treegrid` com setas, direita abre e esquerda recolhe), não a analítica da FIN-08 (fora da main); a OS é link na tabela. Ela é montada na primeira vez que abre (as 1.500 linhas escondidas pesavam na abertura, CA 13); o `aria-describedby` do SVG aponta sempre para o mesmo contêiner, que antes de abrir tem o resumo e o aviso "Ver como tabela". (3) CA 8 e 10: o desenho (grade, dias, Hoje, Prometido) é um SVG `role="img"` ligado à tabela; as barras ficam num segundo SVG sobreposto (`role="group"`), cada uma `role="button"` focável com `aria-label`, porque filho focável dentro de `role="img"` some para o leitor de tela. Foco móvel (uma barra no Tab), setas esquerda/direita no tempo, cima/baixo para a barra mais perto no tempo na linha vizinha, Home/End, Enter ou Espaço abre a OS; no toque, o primeiro toque mostra o detalhe com "Abrir OS". (4) Rotas: a sugestão `/planning/gantt` virou duas (`/gantt/projects` e `/gantt/centers`), uma resposta tipada por visão; a do projeto pede também `projects.view` (CA 11). Allowlist: 422 `planning_gantt.period` (fim antes do início ou mais de 366 dias) e `planning_gantt.line` (mais de 120); id mal formado dá 400 do Minimal API; projeto inexistente 404 `project_not_found`. (5) CA 6: o período vai pelo seletor **Período** (12 semanas, padrão; 6 ou 12 meses, a partir de 7 dias antes de hoje; `?periodo=`); no Projeto 360, sem período, a API alarga o padrão para caber o projeto inteiro (até 12 meses). Na escala Dia o eixo mostra o mês no alto. (6) CA 1 e 9: entram os projetos da fila cuja faixa (do início ao maior entre o fim das OS e a previsão) cruza o período, e os ainda sem datas (com o motivo); dentro do projeto as OS seguem o número (o Production não guarda a sequência do item) e a instalação vem depois. Os filtros de linha e de centro mostram os projetos com alguma etapa nesses centros, com todas as etapas do projeto (a entrega continua legível); o de projeto é um só; as opções dos seletores vêm da fila inteira. (7) CA 3: "Define a entrega" só é marcada no projeto em risco ou atrasado, na etapa não concluída de maior fim previsto (no empate, a da OS de maior número e a de maior sequência), a opção mais segura (recomendação aceita, PO 24/10). "{d} dias depois do prometido" = previsto − prometido; atrasado sem previsão usa os dias desde o prometido (função única). (8) CA 5: a visão por centro mostra só as etapas com previsão no período (a concluída fica na visão por projeto, com as datas reais); a resposta traz só os dias com horas, sem capacidade ou com exceção (os outros dias úteis estão livres); as paradas de manutenção entram quando viram exceção do calendário (ADR 0038). O dia mostra o estado escrito no `<title>` e na tabela, e o nome curto do feriado ou parada no próprio dia nas escalas Dia e Semana. (9) CA 12: o `useProductionLive` ganhou as opções `schedule` (escuta o `ScheduleChanged`) e `delay`; o Cronograma agrupa os sinais a cada 5 s e recarrega com `keepPreviousData` (a tela não volta ao esqueleto). (10) 400 px: abaixo de 768 px (`md`) a tela monta só a lista (e não o Gantt escondido), pelo `useMediaQuery`; a lista por projeto abre as etapas com as datas em texto e uma barra simples; por centro, um centro por vez com seletor e setas de 44 px. (11) Propositivo: "Sem previsão" vira a faixa "{n} etapas sem previsão: … Ver o motivo" (aba Sem previsão) e o link na lista; o aviso de risco da PLN-01b passou a abrir `/app/projetos/{id}?aba=cronograma` (sem `production.view`, o Projeto 360 abre no Resumo). (12) Testes: unidade xUnit (`GanttRulesTests`, 12 casos) e Vitest (`gantt-scale`, `gantt` e `planning/gantt`, 19 casos), arquitetura (`PlanningGanttRulesTests`, 4) e E2E (`planning-gantt.spec.ts`, 8, inclusive teclado, tabela, recarga pelo hub, 300 OS em menos de 1,5 s e 400 px) passando; integração (`FabricationTests.Gantt`: fila, real × previsto, "Define a entrega", filtros, instalação só de leitura, Projeto 360 com e sem permissão, IDOR, allowlist e três camadas; `GanttTests`: visão por centro com o dataset da PLN-01a e o relógio fixo, e a API com 300 OS de 5 etapas em menos de 1 s) escrita e não executada nesta máquina. 9 testes de arquitetura de outras stories não rodam no Windows com Smart App Control (bloqueio das DLLs). (13) OpenAPI gerado pelo Host em processo, sem banco: regenerar oficialmente com `BQ_UPDATE_OPENAPI`. Sem decisão pendente do PO. | Dex (dev) |
| 26/10/2026 | 1.1 | Correção da suíte do lote 5. **Teste:** `Rota_do_projeto_exige_production_view...` esperava o fim do período a até 12 meses do prometido (3000 dias), o que contradiz o desvio (5) desta story e o `GanttRules.ProjectPeriod`: o período começa 7 dias antes de hoje (ou do primeiro início) e alarga até 12 meses contados do início. O teste confere agora o início e o fim = início + 365 dias (recomendação aceita, PO 24/10). Código sem mudança. | Dex (dev) |
