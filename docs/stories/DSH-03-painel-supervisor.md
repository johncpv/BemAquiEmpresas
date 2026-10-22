# DSH-03 — Painel do supervisor

| Campo | Valor |
|---|---|
| ID | DSH-03 |
| Título | Uma tela do supervisor de produção com o Kanban compacto (atrasadas e em risco), a capacidade das próximas semanas por centro, a qualidade a decidir e as não conformidades abertas, as faltas de material que bloqueiam os próximos 5 dias úteis com a situação da compra, e os indicadores de horas paradas e de NC por etapa, cada bloco com a ação que resolve; composição pelo módulo `Dashboard` (ADR 0028) |
| Tamanho | M |
| Status | Ready |
| Requisitos | PRD §5.14 (Painel do supervisor: Kanban + capacidade + não conformidades abertas + faltas de material) · R-PRD-2 · R-PRD-6 · R-PLN-6 · R-EST-4 · R-CMP-1 · R-PRP-4 · R-PRP-5 · PRD §6 |
| Dependências | PRD-02 (quadro e `ProductionHub`), PLN-04 (`IFactoryLoad`), PLN-01b (risco), PRD-05 (qualidade com `production.approve_quality` e NC), EST-02 (faltas, `IStockReservations`), CMP-01 (solicitação a partir da falta), PRD-03a/PRD-04 (pausas), DSH-01a (módulo `Dashboard`, `DashboardCard` e `IBusinessCalendar`) |
| Bloqueia | — |
| Ordem de execução | 13 (Marco 4.4 "A fábrica na parede") |

## User story
Como **supervisor de produção**, quero **numa tela só ver o quadro da fábrica, a capacidade das próximas semanas, os problemas de qualidade e o material que falta**, para **começar o dia sabendo onde agir primeiro**.

## Contexto e referências
- PRD §5.14 ("Painel do supervisor: Kanban + capacidade + não conformidades abertas + faltas de material"); §2 (Supervisor: sequência da fila, distribuição, qualidade).
- Fase 3: PRD-02 (quadro com filtros e contadores "{n} atrasadas", "{n} em risco"; "o painel completo é a DSH-03"); **PRD-05** (decisão de qualidade com `production.approve_quality`, ADR 0003 adendo; segregação do item 76: quem fez qualquer etapa da tentativa não aprova; sem outro aprovador elegível, aprova com a marca "Autoinspeção" e o Proprietário é notificado; NC com número, OS, etapa, responsável, situação; "indicadores de qualidade (taxa de NC por etapa): DSH-03"); PRD-04 ("o tempo parado por falta de material aparece como indicador na Fase 4"); EST-02 (lista Faltas, `StockShortageChanged`); CMP-01 (solicitação por falta da BOM, "Cotar estes itens").
- **ADR 0028** (D9): os painéis compõem números de vários módulos pelo módulo `Dashboard` (só `Application` e `Endpoints`, referenciando `*.Contracts`), com permissão por bloco e DTO por forma de permissão; os contratos de leitura novos ficam no módulo dono (aqui, o Production).
- `GroupSeeder`: Supervisão de produção tem `production.view/create/edit/approve`, `production.approve_quality` (seed patch da PRD-05), `inventory.view/create`, `projects.view`, `kiosk.view/create` e `shopfloor`; não tem `purchasing`.
- **Revisão pré-Fases 4 a 8** (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`): D9; tabela 3.1, pontos 51 a 53; §4.1 (qualidade com `production.approve_quality` e a segregação do item 76); §4.3 (DSH-01a/01b: permissão do dono em cada bloco; SignalR pelas claims); §4.4 padrão 2.

## Critérios de aceite
1. **Dado** o módulo `production` liberado e `production.view`, **quando** abro "Painel do supervisor" (`/app/producao/supervisor`), **então** vejo os blocos Quadro, Capacidade, Qualidade, Faltas de material e Indicadores, cada um num `DashboardCard` com o seu carregamento, o seu erro ("Não foi possível carregar {bloco}." com "Tentar de novo"), "Atualizado às {hh:mm}" e o link "Abrir" para a tela completa; o filtro de linha no topo (na URL, padrão "Todas") vale para todos os blocos; a tela é da Fábrica e não exige `dashboard.view` nem o módulo `dashboard` liberado (os endpoints moram no módulo de composição `Dashboard` só como código, ADR 0028).
2. **Dado** o bloco Quadro, **quando** aparece, **então** mostra as colunas por etapa com a contagem e as horas restantes (PRD-02), só os cartões **atrasados e em risco** expandidos e os demais como contagem por coluna, com os chips "{n} atrasadas" e "{n} em risco" que abrem o Kanban filtrado; atualiza pelo `ProductionHub` (grupo `{tenantId}:production` derivado das claims) como o Kanban.
3. **Dado** o bloco Capacidade, **quando** aparece, **então** mostra a ocupação da semana atual e das próximas 4 por centro (`IFactoryLoad.WeeklyAsync(5)`), com "Lotado", "Folga" e "Acima da capacidade" pelas faixas da PLN-04, "ocupado até {data}" e o link para a Ocupação.
4. **Dado** o bloco Qualidade e `production.approve_quality`, **quando** aparece, **então** lista as etapas de qualidade prontas para decidir que o usuário **pode** decidir (sem a OS em que ele apontou qualquer etapa da tentativa atual, PRD-05 CA 5; com a marca "Autoinspeção" quando não há outro aprovador elegível), com "Aprovar" e "Reprovar" abrindo o diálogo da PRD-05; e as NC abertas (número, OS, etapa, responsável, dias aberta, situação) do mais antigo para o mais novo, com link para a NC; sem `production.approve_quality`, a lista de etapas aparece só como contagem ("{n} etapas aguardam a qualidade") e as NC continuam visíveis.
5. **Dado** o bloco Faltas de material e `inventory.view`, **quando** aparece, **então** lista as faltas das OS abertas (item, OS, projeto, quantidade em falta, necessária em, situação da compra: "Sem solicitação", "Solicitada {SC}", "Em cotação", "Pedido {PC} previsto para {data}"), primeiro as que **bloqueiam** (o início previsto da etapa que consome o item cai em até **5 dias úteis** do calendário padrão, `IBusinessCalendar`), com o chip "Bloqueia em {n} dias"; a situação da compra vem do contrato do Purchasing, **só leitura**, sem exigir `purchasing.view`; com `purchasing.create`, "Gerar solicitação" (CMP-01) na linha "Sem solicitação"; sem `inventory.view`, o bloco não é montado.
6. **Dado** o bloco Indicadores, **quando** aparece, **então** mostra, nos últimos 30 dias, só **horas paradas por motivo de pausa** (Material, Máquina, Outro; `step_pauses`, PRD-03a) e a **taxa de NC por etapa de qualidade** (reprovações ÷ decisões da etapa), cada número com a `<Dica>` "Como calculamos" e o drill-down na lista de origem.
7. **Dado** os blocos, **quando** os dados mudam, **então** Quadro atualiza em tempo real; Qualidade e Faltas recarregam a cada 60 s e ao voltar o foco; Capacidade e Indicadores, ao abrir e com "Atualizar" (cache e rate limit da ADR 0028 §3).
8. **Dado** 300 OS, 200 faltas e 100 NC, **quando** abro o painel, **então** todos os blocos chegam em menos de 1,5 s (cada bloco com uma consulta, nomes por página).
9. **[Segurança]** **Dado** o módulo `production` não liberado, **quando** abro, **então** a tela `ModuleNotReleased` e a API 403 `module_not_released`; sem grupo ou sem `production.view`, 403 `forbidden` e o item some do menu; cada bloco confere o módulo e a permissão do dono (`inventory` liberado e `inventory.view` para faltas, `purchasing.create` para gerar solicitação, `production.approve_quality` para decidir a qualidade) e não é montado sem eles; `RequireModule` e `perm:` em cada endpoint de bloco; sem custo em nenhum bloco (DTO sem propriedade de custo, teste de arquitetura).

## Regras de negócio e técnicas
- **Composição de contratos** (ADR 0028): `IProductionBoard` (quadro, PRD-02), `IFactoryLoad` (PLN-04), qualidade e NC do Production, `IStockReservations` (faltas, EST-02), `IPurchaseRequests`/`IPurchaseOrders` (situação da compra, CMP-01/CMP-03a, só leitura), `IProductionSchedule` (início previsto), `IBusinessCalendar` (dias úteis).
- **Indicadores** (ponto 51): horas paradas e taxa de NC bastam no MVP. **Taxa de NC** = reprovações da etapa ÷ (aprovações + reprovações) no período, 1 casa; **horas paradas** = Σ minutos de pausa ÷ 60 por motivo.
- **Faltas que bloqueiam** (ponto 52): início previsto da etapa em até 5 dias úteis.
- **Compra** (ponto 53): situação lida pelo contrato do Purchasing, só leitura; "Gerar solicitação" com `purchasing.create`.
- Sem gravação; as ações abrem os diálogos das stories donas.

## Permissões, eventos e tabelas
- **Permissões:** `production.view` (tela); `inventory.view` (faltas); `purchasing.create` (gerar solicitação); `production.approve_quality` (decidir a qualidade).
- **Eventos:** nenhum novo; `OrderBoardChanged` (PRD-02) para o Quadro.
- **Contratos:** os listados acima; novo `IProductionIndicators` (`Production.Contracts`) para pausas e taxa de NC.
- **Tabelas:** nenhuma nova.

## Telas e estados
- **Padrões comuns das Fases 4 a 8:** 2 (`DashboardCard` por bloco: número clicável até a origem, "Como calculamos", "Atualizado às", estados por bloco).
- **Fábrica → Produção → Painel do supervisor** (`/app/producao/supervisor`; entrada no `modules.ts`, primeira do grupo Produção para quem tem `production.approve` ou `production.approve_quality`).
- **Estados:** carregando por bloco; vazio por bloco ("Nenhuma NC aberta.", "Nenhuma falta de material.", "Nenhuma OS atrasada ou em risco.", "Nenhuma etapa aguardando a qualidade."); erro por bloco.
- **Propositivo:** cada linha leva à ação (decidir a qualidade, gerar solicitação, abrir a NC, abrir o Kanban filtrado); a falta que bloqueia vem primeiro.
- **400 px:** blocos empilhados na ordem Qualidade → Faltas → Quadro (só os chips e a lista de atrasadas) → Capacidade → Indicadores; botões com 44 px.
- **Ajuda:** `Ajuda/producao-supervisor.md` (inclui por que uma etapa de qualidade não aparece para quem trabalhou nela e a "Autoinspeção"); `<Dica>` em "Taxa de NC", "Horas paradas", "Bloqueia em {n} dias" e "Autoinspeção".

## Plano de testes
- **Unidade:** taxa de NC e horas paradas (CA 6); ordem das faltas e corte de 5 dias úteis (CA 5).
- **Integração (Testcontainers):** cada bloco com dataset fixo e o usuário da Supervisão do seed (CA 2–6); qualidade com a segregação e a "Autoinspeção" (CA 4); bloco de faltas sem `inventory.view`, situação da compra sem `purchasing.view` e ação sem `purchasing.create` (CA 5, 9); tela sem o módulo `dashboard` liberado (CA 1); desempenho (CA 8); três camadas por bloco (CA 9).
- **Arquitetura:** blocos sem propriedade de custo; endpoints no módulo de composição só com `*.Contracts`.
- **E2E (Playwright):** abrir o painel, decidir uma etapa de qualidade pelo bloco, ver o Quadro mudar; tela a 400 px.

## Fora do escopo
- Distribuir trabalho entre operadores (atribuir etapa): fora do MVP.
- Paradas de máquina registradas à parte (R-PRD-9, D) e manutenção (Fase 6).
- Outros indicadores (OEE, retrabalho em horas): quando o PO pedir.

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fases 4 a 8, tabela 3.1, pontos 51–53):
1. Horas paradas e taxa de NC bastam no MVP (CA 6).
2. Faltas que bloqueiam nos próximos 5 dias úteis (CA 5).
3. Situação da compra pelo contrato do Purchasing, só leitura; "Gerar solicitação" com `purchasing.create` (CA 5).

Decisões da revisão aplicadas: §4.1 (qualidade com `production.approve_quality` e a segregação do item 76, com "Autoinspeção", CA 4); D9 e ADR 0028 (composição pelo módulo `Dashboard`, permissão do dono por bloco, CA 1, 9); §4.3 (grupo SignalR pelas claims, CA 2); §4.4 padrão 2.

**Pré-condição externa:** nenhuma.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers), arquitetura, desempenho e E2E passando.
- [ ] Nenhuma migration.
- [ ] Permissões só do `ModuleCatalog`; blocos pela permissão e pelo módulo do dono.
- [ ] Tela com estados por bloco e 400 px.
- [ ] Ajuda escrita; teste de cobertura passando.
- [ ] Ações acionáveis em cada bloco.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] Story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `IProductionIndicators` (Production); endpoints `/api/v1/dashboard/supervisor/*` por bloco no módulo `Dashboard`, com `RequireModule("production")`.
- [ ] Página com os blocos em `DashboardCard` e o Quadro compacto; ajuda.

## Quality gates
- Agentes previstos: @dev, design (composição de blocos), @qa.
- Gate: CA 2, 4, 5 e 9 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fases 4 a 8).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: depende da DSH-01a pelo módulo `Dashboard`) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** a dependência da DSH-04 saiu (nenhum CA a usava); cada bloco com a permissão do dono, inclusive o seed da Supervisão sem `purchasing`.
- **Correções da revalidação (21/10/2026):** pontos 51 a 53 decididos; qualidade pela `production.approve_quality` com a segregação do item 76 e a "Autoinspeção" (§4.1, CA 4); composição pelo módulo `Dashboard` sem exigir `dashboard.view` (ADR 0028, CA 1); `DashboardCard` por bloco (padrão 2); 5 dias úteis pelo `IBusinessCalendar` (CA 5); módulo e permissão do dono em cada bloco (§4.3, CA 9); dependência da DSH-01a.
- **Pré-condições para `Ready`:** nenhuma de decisão de story.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (D9, tabela 3.1 pontos 51–53, §4.1, §4.3, §4.4 padrão 2; ADR 0028); ordem mantida (Marco 4.4, ordem 13) | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
