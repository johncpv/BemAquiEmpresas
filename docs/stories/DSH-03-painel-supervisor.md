# DSH-03 — Painel do supervisor

| Campo | Valor |
|---|---|
| ID | DSH-03 |
| Título | Uma tela do supervisor de produção com o Kanban compacto (atrasadas e em risco), a capacidade das próximas semanas por centro, as não conformidades abertas, as faltas de material com a solicitação de compra, as etapas aguardando a qualidade e os indicadores de paradas e de NC por etapa, cada bloco com a ação que resolve |
| Tamanho | M |
| Status | Draft |
| Requisitos | PRD §5.14 (Painel do supervisor: Kanban + capacidade + não conformidades abertas + faltas de material) · R-PRD-2 · R-PRD-6 · R-PLN-6 · R-EST-4 · R-CMP-1 · R-PRP-4 · R-PRP-5 · PRD §6 |
| Dependências | PRD-02 (quadro e `ProductionHub`), PLN-04 (`IFactoryLoad`), PLN-01b (risco), PRD-05 (NC e qualidade), EST-02 (faltas, `IStockReservations`), CMP-01 (solicitação a partir da falta), PRD-03a/PRD-04 (pausas) |
| Bloqueia | — |
| Ordem de execução | 13 (Marco 4.4 "A fábrica na parede") |

## User story
Como **supervisor de produção**, quero **numa tela só ver o quadro da fábrica, a capacidade das próximas semanas, os problemas de qualidade e o material que falta**, para **começar o dia sabendo onde agir primeiro**.

## Contexto e referências
- PRD §5.14 ("Painel do supervisor: Kanban + capacidade + não conformidades abertas + faltas de material"); §2 (Supervisor: sequência da fila, distribuição, qualidade).
- Fase 3 (rascunho): PRD-02 (quadro com filtros e contadores "{n} atrasadas", "{n} em risco"; "o painel completo é a DSH-03"); PRD-05 (NC com número, OS, etapa, responsável, situação; "indicadores de qualidade (taxa de NC por etapa): DSH-03"); PRD-04 ponto 3 ("o tempo parado por falta de material aparece como indicador na Fase 4"); EST-02 (lista Faltas, `StockShortageChanged`); CMP-01 (solicitação por falta da BOM, "Cotar estes itens").
- `GroupSeeder`: Supervisão de produção tem `production.view/create/edit/approve`, `inventory.view/create`, `projects.view`, `kiosk.view/create` e `shopfloor`; não tem `purchasing`.

## Critérios de aceite
1. **Dado** `production.view`, **quando** abro "Painel do supervisor" (`/app/producao/supervisor`), **então** vejo os blocos: Quadro, Capacidade, Qualidade, Faltas de material e Indicadores, cada um com o seu carregamento, o seu erro ("Não foi possível carregar {bloco}." com "Tentar de novo") e o seu link "Abrir" para a tela completa; o filtro de linha no topo (na URL, padrão "Todas") vale para todos os blocos.
2. **Dado** o bloco Quadro, **quando** aparece, **então** mostra as colunas por etapa com a contagem e as horas restantes (PRD-02), só os cartões **atrasados e em risco** expandidos e os demais como contagem por coluna, com os chips "{n} atrasadas" e "{n} em risco" que abrem o Kanban filtrado; atualiza pelo `ProductionHub` como o Kanban.
3. **Dado** o bloco Capacidade, **quando** aparece, **então** mostra a ocupação da semana atual e das próximas 4 por centro (`IFactoryLoad.WeeklyAsync(5)`), com "Lotado" e "Folga" pelas faixas da PLN-04 e o link para a Ocupação.
4. **Dado** o bloco Qualidade, **quando** aparece, **então** lista as etapas de qualidade prontas para aprovar (com "Aprovar" e "Reprovar" para quem tem `production.approve`, abrindo o diálogo da PRD-05, e a regra de segregação de lá) e as NC abertas (número, OS, etapa, responsável, dias aberta, situação) do mais antigo para o mais novo, com link para a NC.
5. **Dado** o bloco Faltas de material e `inventory.view`, **quando** aparece, **então** lista as faltas das OS abertas (item, OS, projeto, quantidade em falta, necessária em, situação da compra: "Sem solicitação", "Solicitada {SC}", "Em cotação", "Pedido {PC} previsto para {data}"), primeiro as que bloqueiam a próxima etapa planejada (início previsto em até 5 dias úteis, proposta); com `purchasing.create`, "Gerar solicitação" (CMP-01) na linha "Sem solicitação"; sem `inventory.view`, o bloco não aparece.
6. **Dado** o bloco Indicadores, **quando** aparece, **então** mostra, nos últimos 30 dias: horas paradas por motivo de pausa (Material, Máquina, Outro; `step_pauses`, PRD-03a), e a taxa de NC por etapa de qualidade (reprovações ÷ inspeções da etapa), cada número com a `<Dica>` da regra e o drill-down na lista de origem (proposta, Pontos em aberto 1).
7. **Dado** os blocos, **quando** os dados mudam, **então** Quadro atualiza em tempo real; Qualidade e Faltas recarregam a cada 60 s e ao voltar o foco; Capacidade e Indicadores, ao abrir e com "Atualizar".
8. **Dado** 300 OS, 200 faltas e 100 NC, **quando** abro o painel, **então** todos os blocos chegam em menos de 1,5 s (cada bloco com uma consulta, nomes por página).
9. **Dado** o módulo `production` não liberado, **quando** abro, **então** a tela `ModuleNotReleased` e a API 403 `module_not_released`; sem grupo ou sem `production.view`, 403 `forbidden` e o item some do menu; cada bloco confere a permissão do dono (`inventory.view` para faltas, `purchasing.create` para gerar solicitação, `production.approve` para aprovar); `RequireModule` e `perm:`; sem custo em nenhum bloco.

## Regras de negócio e técnicas
- **Composição de contratos:** `IProductionBoard` (quadro, PRD-02), `IFactoryLoad` (PLN-04), NC e qualidade do próprio Production, `IStockReservations` (faltas, EST-02), `IPurchaseRequests`/`IPurchaseOrders` (situação da compra, CMP-01/CMP-03a), `IProductionSchedule` (início previsto).
- **Taxa de NC** = reprovações da etapa ÷ (aprovações + reprovações) no período, 1 casa; horas paradas = Σ minutos de pausa ÷ 60 por motivo.
- Sem gravação; as ações abrem os diálogos das stories donas.

## Permissões, eventos e tabelas
- **Permissões:** `production.view` (tela); `inventory.view` (faltas); `purchasing.create` (gerar solicitação); `production.approve` (qualidade).
- **Eventos:** nenhum novo; `OrderBoardChanged` (PRD-02) para o Quadro.
- **Contratos:** os listados acima; novo `IProductionIndicators` (Production, proposta) para pausas e taxa de NC.
- **Tabelas:** nenhuma nova.

## Telas e estados
- **Fábrica → Produção → Painel do supervisor** (`/app/producao/supervisor`; entrada no `modules.ts`, primeira do grupo Produção para quem tem `production.approve`, proposta).
- **Estados:** carregando por bloco; vazio por bloco ("Nenhuma NC aberta.", "Nenhuma falta de material.", "Nenhuma OS atrasada ou em risco."); erro por bloco.
- **Propositivo:** cada linha leva à ação (aprovar, gerar solicitação, abrir a NC, abrir o Kanban filtrado).
- **400 px:** blocos empilhados na ordem Qualidade → Faltas → Quadro (só os chips e a lista de atrasadas) → Capacidade → Indicadores.
- **Ajuda:** `Ajuda/producao-supervisor.md`; `<Dica>` em "Taxa de NC", "Horas paradas" e "Bloqueia a próxima etapa".

## Plano de testes
- **Unidade:** taxa de NC e horas paradas (CA 6); ordem das faltas (CA 5).
- **Integração (Testcontainers):** cada bloco com dataset fixo e o usuário da Supervisão do seed (CA 2–6); bloco de faltas sem `inventory.view` e ação sem `purchasing.create` (CA 5, 9); desempenho (CA 8); três camadas (CA 9).
- **E2E (Playwright):** abrir o painel, aprovar uma etapa de qualidade pelo bloco, ver o Quadro mudar; tela a 400 px.

## Fora do escopo
- Distribuir trabalho entre operadores (atribuir etapa): fora do MVP (ponto 1 da PRD-03a).
- Paradas de máquina registradas à parte (R-PRD-9, D) e manutenção (Fase 6).

## Pontos em aberto
1. **Indicadores do supervisor** (PO): proposta: horas paradas por motivo e taxa de NC por etapa, 30 dias. Alternativa: só os quatro blocos do PRD, sem indicadores.
2. **Faltas que bloqueiam** (PO): proposta: início previsto da etapa que consome o item em até 5 dias úteis.
3. **Supervisor sem `purchasing`** (PO): no seed, a Supervisão não tem `purchasing`; proposta: o bloco mostra a situação da compra (leitura pelo contrato do Purchasing, sem precisar de `purchasing.view`) e o botão "Gerar solicitação" só para quem tem `purchasing.create`.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers), desempenho e E2E passando.
- [ ] Nenhuma migration.
- [ ] Permissões só do `ModuleCatalog`; blocos pela permissão do dono.
- [ ] Tela com estados por bloco e 400 px.
- [ ] Ajuda escrita; teste de cobertura passando.
- [ ] Ações acionáveis em cada bloco.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] Story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `IProductionIndicators`; endpoints `/api/v1/production/supervisor/*` por bloco.
- [ ] Página com os blocos e o Quadro compacto; ajuda.

## Quality gates
- Agentes previstos: @dev, design (composição de blocos), @qa.
- Gate: CA 2, 5 e 9 obrigatórios.

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

- **Correções feitas na validação:** a dependência da DSH-04 saiu (nenhum CA a usava); cada bloco com a permissão do dono, inclusive o seed da Supervisão sem `purchasing` (ponto 3).
- **Pré-condições para `Ready`:** nenhuma pendente (os pontos podem ser decididos na implementação).
- **Prontidão:** 10/10. **Veredito:** GO. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
