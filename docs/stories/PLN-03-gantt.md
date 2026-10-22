# PLN-03 — Cronograma (Gantt) por projeto e por centro de trabalho

| Campo | Valor |
|---|---|
| ID | PLN-03 |
| Título | Cronograma em barras das etapas planejadas, num componente `Gantt` próprio em SVG sobre o `ChartFrame` (ADR 0033, sem biblioteca nova), em duas visões — por projeto (projeto → OS → etapa, com prevista × prometida) e por centro de trabalho (a carga de cada centro no tempo, com feriados e paradas) —, escala de dia, semana ou mês, linha de hoje, detalhe da barra, tabela equivalente acessível e aba "Cronograma" no Projeto 360 |
| Tamanho | G |
| Status | Ready |
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
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade (Vitest), integração (Testcontainers), desempenho e E2E passando.
- [ ] Nenhuma migration.
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Tela com estados, acessível (tabela equivalente, teclado) e a 400 px; `Gantt` em SVG com tokens e sem cor literal.
- [ ] Ajuda das duas abas; teste de cobertura passando.
- [ ] Destaques acionáveis ("Define a entrega", sem previsão).
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] Story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Endpoints `/api/v1/production/planning/gantt` (projeto e centro) e `/projects/{id}/gantt`.
- [ ] Componente `Gantt` em SVG (escala, barras, virtualização, tabela equivalente) dentro do `ChartFrame`.
- [ ] Aba Cronograma no Planejamento e no Projeto 360; recarga pelo hub; ajudas.

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
