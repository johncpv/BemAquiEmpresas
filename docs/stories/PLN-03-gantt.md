# PLN-03 — Cronograma (Gantt) por projeto e por centro de trabalho

| Campo | Valor |
|---|---|
| ID | PLN-03 |
| Título | Cronograma em barras das etapas planejadas, em duas visões — por projeto (projeto → OS → etapa, com prevista × prometida) e por centro de trabalho (a carga de cada centro no tempo, com feriados e paradas) —, escala de dia, semana ou mês, linha de hoje, detalhe da barra, tabela equivalente acessível e aba "Cronograma" no Projeto 360 |
| Tamanho | G |
| Status | Draft |
| Requisitos | R-PLN-4 (cronograma por projeto e por centro de trabalho; data prevista de entrega × prometida com alerta de risco) · R-PLN-3 · PRD §5.14 (Entregas: em risco) · R-PRP-5 · PRD §6 |
| Dependências | PLN-01a (datas e alocações), PLN-01b (previsão do projeto e risco), PLN-02 (ordem das linhas), COM-05 (Projeto 360), COM-06 (datas da OS de instalação) |
| Bloqueia | — (o aviso de risco da PLN-01b passa a abrir o cronograma do projeto) |
| Ordem de execução | 10 (Marco 4.3 "Datas que a fábrica cumpre") |

## User story
Como **supervisor de produção** e **proprietário**, quero **ver numa linha do tempo quando cada etapa de cada projeto acontece e como cada centro está carregado**, para **enxergar o que empurra a entrega e onde está o gargalo**.

## Contexto e referências
- PRD R-PLN-4.
- `04-fluxos-estados` §3.4 (4) e (6) (data prevista e risco).
- Protótipo: não há tela de cronograma; valem a identidade (tons de aço, acento cobalto, âmbar só para estado e item ativo), o `StageIndicator` e a faixa lateral vermelha/âmbar para atraso/risco (CLAUDE.md, Interface).
- Padrão de gráficos da Fase 2 (item 10): realizado e projetado distinguidos pelo traço (contínuo × tracejado) e tabela equivalente acessível. Recharts só desenha o que o `Chart` cobre (linhas); não há biblioteca de Gantt na stack.
- PLN-01a: `production.schedule_allocations` (horas por dia por centro), `planned_start/planned_end` das etapas e das OS; as etapas concluídas têm `actual_start/actual_end` (PRD-03a).

## Critérios de aceite
**Por projeto**
1. **Dado** `production.view`, **quando** abro Planejamento → Cronograma (`?aba=cronograma&visao=projeto`), **então** vejo uma linha por projeto da fila (ordem da PLN-02) com a barra do início da primeira etapa até a data prevista de entrega, a marca da data prometida (traço vertical na linha) e o chip de folga ("+3 dias" ok, "−7 dias" em risco); cada projeto se abre nas suas OS e cada OS nas etapas.
2. **Dado** uma etapa, **quando** desenhada, **então** a barra vai do início ao fim previstos com a letra da etapa no estilo do `StageIndicator`; etapa concluída aparece com o início e o fim **reais** em traço contínuo, e o restante do plano em tracejado; etapa em andamento mostra a parte já feita (proporção apontadas ÷ padrão) preenchida.
3. **Dado** um projeto em risco ou atrasado (função única da PLN-01b), **quando** a linha aparece, **então** tem a faixa lateral âmbar (risco) ou vermelha (atrasado), e a etapa que empurra a data (a de maior fim previsto) fica destacada com o rótulo "Define a entrega".
4. **Dado** um projeto com OS de instalação, **quando** abro, **então** a OS de instalação aparece como uma barra com as datas digitadas (COM-06) e o rótulo "Instalação (datas da OS)", sem ser editável aqui.

**Por centro de trabalho**
5. **Dado** a visão "Por centro" (`visao=centro`), **quando** abro, **então** vejo uma linha por centro ativo com as barras das etapas alocadas nele, na ordem do tempo (uma sub-linha por etapa que se sobrepõe no dia), o fundo de cada dia sombreado pela ocupação do dia (alocado ÷ capacidade) e os dias de feriado e parada marcados com o nome da exceção (CAD-03).

**Escala, detalhe e filtros**
6. **Dado** a escala, **quando** escolho Dia, Semana ou Mês (padrão Semana; período padrão de 7 dias antes de hoje a 12 semanas depois; até 12 meses), **então** o eixo muda, a linha "Hoje" fica visível e "Ir para hoje" rola até ela; a rolagem horizontal é só dentro do cronograma (sem rolagem horizontal da página).
7. **Dado** uma barra, **quando** passo o ponteiro, foco pelo teclado ou toco, **então** vejo OS, projeto, etapa, centro, horas restantes ÷ padrão, início e fim previstos (e reais, se houver); ao clicar ou dar Enter, abre a OS (`/app/producao/os/$id`).
8. **Dado** os filtros, **quando** escolho projeto, cliente, linha, centro e "Só em risco", **então** o cronograma filtra e os filtros ficam na URL.
9. **Dado** a tabela equivalente ("Ver como tabela"), **quando** abro, **então** vejo as mesmas linhas com as datas em texto (projeto, OS, etapa, centro, início, fim, prometido, folga), navegável pelo teclado; o cronograma tem `role="img"` com descrição ligada à tabela por `aria-describedby`, e as linhas expansíveis seguem o padrão `treegrid` (como a `TreeTable`).

**No Projeto 360**
10. **Dado** um projeto com OS de fabricação e o usuário com `projects.view` e `production.view`, **quando** abro o Projeto 360, **então** há a aba "Cronograma" com a visão por projeto só desse projeto (na URL); sem `production.view`, a aba não aparece.

**Desempenho, permissões e segurança**
11. **Dado** 300 OS com 5 etapas (1.500 barras), **quando** abro o cronograma por projeto, **então** a API responde em menos de 1 s e a tela fica utilizável em menos de 1,5 s (linhas virtualizadas, projetos recolhidos por padrão acima de 50).
12. **Dado** o módulo `production` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo ou sem `production.view`, 403 `forbidden`. `RequireModule` e `perm:`; IDOR na rota do cronograma do projeto (`/production/planning/projects/{projectId}/gantt`, projeto validado por `IProjects`); sem custo nem dado pessoal na resposta.

## Regras de negócio e técnicas
- **Só leitura:** as datas são calculadas pelo motor; o cronograma não muda datas (arrastar barra não existe; a ordem muda na Fila, PLN-02).
- **Componente próprio** `Gantt` nos componentes de UI (SVG ou grade CSS), com tokens de cor (sem cor literal), números com `tabular-nums` e datas pt-BR (proposta, Pontos em aberto 1).
- **Dados** numa consulta por visão (etapas com datas e alocações por dia), nomes de projeto e cliente por `FindManyAsync`.

## Permissões, eventos e tabelas
- **Permissões:** `production.view` (cronograma); `projects.view` + `production.view` (aba no Projeto 360).
- **Eventos:** nenhum (a tela recarrega com o `OrderBoardChanged` do hub da PRD-02, agrupado a cada 5 s).
- **Contratos:** `IProductionSchedule` (PLN-01a), `IPlanningQueue` (PLN-02), `IProjects`, `IParties`, `IInstallationOrders`, `IWorkCenterCapacity`.
- **Tabelas:** nenhuma nova.

## Telas e estados
- **Planejamento → Cronograma** (aba) com o seletor Por projeto / Por centro, escala, "Ir para hoje", filtros e "Ver como tabela"; **Projeto 360 → Cronograma** (aba nova).
- **Estados:** carregando (skeleton de linhas); vazio ("Nenhuma etapa planejada no período." + "Ver a fila"); erro; "Plano de {hh:mm}" e "Recalculando…" do PLN-01a.
- **Propositivo:** "Define a entrega" mostra onde agir; a etapa sem previsão leva ao motivo.
- **400 px:** a visão por projeto vira lista (projeto, previsto, prometido, folga; ao abrir, as etapas com as datas em texto e uma barra simples por etapa na largura da tela); a visão por centro mostra um centro por vez.
- **Ajuda:** `Ajuda/producao-planejamento-cronograma.md` (aba `cronograma`) e `Ajuda/projetos-projeto-cronograma.md` (aba `cronograma` do projeto); `<Dica>` em "Define a entrega", "Prometido" e no tracejado.

## Plano de testes
- **Unidade (front, Vitest):** escala e posição das barras (datas → pixels), real × previsto, "Define a entrega" (CA 2, 3, 6).
- **Integração (Testcontainers):** consulta por projeto e por centro com o dataset do PLN-01a (CA 1, 5); instalação (CA 4); aba do projeto com e sem `production.view` (CA 10); desempenho da API (CA 11); três camadas e IDOR (CA 12).
- **E2E (Playwright):** abrir as duas visões, trocar a escala, abrir uma OS pela barra pelo teclado; tabela equivalente; tela a 400 px sem rolagem horizontal da página; medição do tempo até a tela utilizável com 300 OS (CA 11).

## Fora do escopo
- Editar datas pelo cronograma; dependências entre OS; caminho crítico.
- Exportar o cronograma em PDF ou imagem.
- Etapas de manutenção e paradas (Fase 6).

## Pontos em aberto
1. **Componente de Gantt** (arquiteto/design): proposta: componente próprio com os tokens, sem biblioteca nova. Alternativa: uma biblioteca de Gantt de licença livre, com ADR (stack e licença).
2. **Instalação no cronograma** (PO): proposta do CA 4 (barra com as datas digitadas, só leitura).

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade (Vitest), integração (Testcontainers), desempenho e E2E passando.
- [ ] Nenhuma migration.
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Tela com estados, acessível (tabela equivalente, teclado) e a 400 px; `Gantt` com tokens e sem cor literal.
- [ ] Ajuda das duas abas; teste de cobertura passando.
- [ ] Destaques acionáveis ("Define a entrega", sem previsão).
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] Story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Endpoints `/api/v1/production/planning/gantt` (projeto e centro) e `/projects/{id}/gantt`.
- [ ] Componente `Gantt` (escala, barras, virtualização, tabela equivalente).
- [ ] Aba Cronograma no Planejamento e no Projeto 360; ajudas.

## Quality gates
- Agentes previstos: @dev, design (componente, acessibilidade, 400 px), @qa.
- Gate: CA 1, 2, 9 e 11 obrigatórios.

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

- **Correções feitas na validação:** sem biblioteca de Gantt na stack: componente próprio como proposta (ponto 1) e ADR se o arquiteto preferir biblioteca; tabela equivalente e teclado (CA 9); 400 px sem rolagem horizontal da página.
- **Pré-condições para `Ready`:** nenhuma (o ponto 1 pode ser decidido na implementação, com ADR se for biblioteca).
- **Prontidão:** 10/10. **Veredito:** GO. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
