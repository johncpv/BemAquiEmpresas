# DSH-01a — Painel do proprietário: módulo Dashboard e cards de caixa, faturamento, inadimplência, comercial e decisões

| Campo | Valor |
|---|---|
| ID | DSH-01a |
| Título | Módulo `Dashboard` só de composição (ADR 0028) e a tela única do dono com a faixa "Atenção" e os cards de saldo em contas, caixa projetado 30/60/90 dias (base × ajustado), faturamento do mês e do ano × meta (provisório até as notas reais), inadimplência, comercial e "Aguardando sua decisão"; cada número com a regra e o drill-down até o relatório de origem; cache de 60 s por usuário e versão de permissão, com contador de geração por tenant; o Proprietário cai no painel depois do login |
| Tamanho | G (quebra da DSH-01: o módulo de composição, o cache, as metas e os cards que só dependem do Financeiro e do Comercial) |
| Status | Done |
| Requisitos | PRD §5.14 (Dashboard do proprietário: Caixa, Faturamento × meta, Inadimplência, Comercial, Pendências do dono; cada card leva ao detalhe) · R-FIN-10 · R-FIN-11 · R-FIN-12 · R-FIN-13 · R-COML-9 · R-PRP-4 · R-PRP-5 · PRD §6 (Desempenho) |
| Dependências | FIN-05 (saldo atual), FIN-07 (`ICashForecast`, `ChartFrame`), FIN-08 (`IRevenueSummary` com a marca de provisório), FIN-06a (aging), COM-08 (funil), DSH-04 (`IAgendaService`, Início e `homeRouteFor`), DSH-02 (`Kpi` e `DashboardCard`), FIN-03a/FIN-03b (OP), COM-07 (aditivos), CAD-03 (calendário padrão) |
| Bloqueia | DSH-01b (cards de margem, carteira, produção, entregas e resultado gerencial por projeto), DSH-03 (padrão de card e cache do `Dashboard`) |
| Ordem de execução | 5 (Marco 4.2 "O painel do dono, parte do dinheiro") |

## User story
Como **proprietário**, quero **abrir uma tela só e ver quanto tenho em caixa, como ele fica nas próximas semanas, quanto faturei contra a meta, quem me deve e o que espera a minha decisão**, para **entender a empresa em um minuto e ir direto ao que precisa de mim**.

## Contexto e referências
- PRD §5.14 (lista dos KPIs, "uma tela, visão simples e geral", "cada card leva ao detalhe"); §1 ("o dono saber, a qualquer momento, quanto cada projeto custa, quanto rende e como estará o caixa"); §7 Fase 4 ("Visão completa do dono").
- Backlog Fase 4 (CA da DSH-01): "carrega < 2 s; números conferem com relatórios de origem (teste com dataset fixo)".
- `ModuleCatalog`: módulo `dashboard` ("Painel do proprietário", área Visão geral, só `view`); `GroupSeeder`: o Financeiro tem `dashboard.view`; o Proprietário tem tudo. `modules.ts`: slug `painel`. **Código atual:** não há projeto do módulo `dashboard` em `src/Modules` (14 módulos no teste de arquitetura).
- **ADR 0028** (D9): §1 módulo `Dashboard` só de composição (`Application` e `Endpoints`, referenciando só `*.Contracts`, sem `Domain`, `Infrastructure`, schema nem tabela; o `Reporting` da ADR 0006 é revogado); metas em `finance.revenue_targets`, editadas com `settings.edit`; §2 permissão por card e DTO por forma de permissão; §3 cache de 60 s com chave `{tenant}:{usuário}:{versão de permissão}:{card}:{parâmetros}`, contador de geração por tenant e "Atualizar" com rate limit; §4 Início como página inicial de todos e o Proprietário com `dashboard.view` em `/app/painel`; faturamento "provisório" até as notas reais.
- `02-arquitetura` §3 ("Consultas pesadas de dashboard…") e ADR 0006 (Dapper pela conexão do tenant; materialized view só quando a medição justificar, no módulo dono).
- **Revisão pré-Fases 4 a 8** (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`): D1, D4, D9, D18 (`Kpi`/`DashboardCard` nascem na DSH-02 e são usados aqui); tabela 3.1, pontos 18 a 22; §4.3 (DSH-01a/01b: DTO por forma de permissão e `RequireModule` do módulo dono em cada card); §4.4 padrões 1 e 2.
- Protótipo (Painel): cabeçalho "Boa tarde, John" + frase-resumo; faixa de alertas ("Ed. Monte Castelo vai atrasar 7 dias", "3 ordens de pagamento aguardam você", "Hospital São Lucas com parcela vencida"); KPIs "Saldo em contas", "Faturado em outubro / 380 mil" com medidor de meta, "Margem real média", "A receber vencido"; "Caixa projetado, próximas 12 semanas" (Os dois/Base/Ajustado e menores saldos); "Aguardando sua aprovação"; "Comercial" (em aberto, conversão, vencem em 7 dias, funil).

## Critérios de aceite
**Módulo de composição**
1. **Dado** o módulo `Dashboard` (novo em `src/Modules/Dashboard/`, com `BemAqui.Dashboard.Application` e `BemAqui.Dashboard.Endpoints`), **quando** o teste de arquitetura roda, **então** ele é reconhecido como módulo de composição: referencia só `*.Contracts` dos outros módulos e o `BuildingBlocks`, e o teste reprova qualquer `DbSet`, Dapper, `TenantDbContext`, migration ou schema nele (ADR 0028 §1); a contagem de módulos do teste passa a incluir o `Dashboard` com essa exceção às 5 camadas.

**Estrutura e acesso**
2. **Dado** `dashboard.view`, **quando** abro "Painel do proprietário" (`/app/painel`), **então** vejo o cabeçalho com a saudação pelo horário ("Bom dia/Boa tarde/Boa noite, {primeiro nome}"), a data por extenso e uma frase-resumo montada dos cards visíveis (ex.: "Caixa positivo nas próximas 12 semanas; 3 decisões esperam você."), a faixa "Atenção" e os cards.
3. **Dado** o login de um membro do grupo Proprietário com o módulo `dashboard` liberado, **quando** não há `returnUrl`, **então** ele cai em `/app/painel` (regra acrescentada ao `homeRouteFor` da DSH-04); qualquer outro usuário, mesmo com `dashboard.view` (ex.: Financeiro do seed), cai no Início; o painel continua no dock para quem tem `dashboard.view`.
4. **[Segurança]** **Dado** um card, **quando** o usuário não tem a permissão do módulo dono do dado ou o módulo dono não está liberado (tabela do CA 5), **então** o card não é montado no servidor nem aparece (não vira card vazio); chamado direto, o endpoint do card responde 403 com o código da camada (`module_not_released` ou `forbidden`); cada endpoint de card declara `RequireModule("dashboard")`, `perm:dashboard.view`, **e** o `RequireModule` e a permissão do módulo dono; o painel com `dashboard.view` e sem nenhum card visível mostra "Você ainda não tem acesso aos indicadores. Fale com o administrador."
5. **Dado** os cards desta story, **quando** o painel monta, **então** cada card segue a tabela (fontes por contrato, regra visível na `<Dica>` "Como calculamos" e drill-down em "Ver origem"):

| Card | Permissão | Número | Fonte (contrato) | Drill-down |
|---|---|---|---|---|
| Saldo em contas | `finance.view` | saldo consolidado e por conta | `BankBalanceAsync` (FIN-05) | Contas e extrato |
| Caixa projetado | `finance.view` | linhas base × ajustado de 12 semanas, saldo final em 30/60/90 dias, menor saldo de cada cenário e a diferença | `ICashForecast` (FIN-07) | Fluxo de caixa no cenário escolhido |
| Faturamento × meta | `finance.view` | receita bruta do mês e acumulada do ano, meta do mês e do ano, % atingido, com o chip "Provisório" nos meses sem nota real | `IRevenueSummary` (FIN-08) + metas (CA 9) | DRE, linha Receita bruta |
| A receber vencido | `finance.view` | total vencido, aging 0–30/31–60/61–90/90+, 5 maiores clientes, maior atraso | `IReceivablesAging.SummaryAsync` (novo, Finance) | Inadimplência |
| Comercial | `quotes.view` | em aberto (valor), conversão dos últimos 90 dias, vencendo em 7 dias, funil | `IQuoteFunnel.SummaryAsync` (novo, Commercial) | Funil |
| Aguardando sua decisão | (cada item, a sua) | os itens do nível 1 da pauta do usuário (OP, aditivos, qualidade, pedidos, ajustes) | `IAgendaService` (DSH-04) | o documento na ação |

**Números que conferem**
6. **Dado** o dataset fixo de teste (o mesmo das stories de origem), **quando** abro o painel, **então** cada número de cada card é igual ao do relatório de origem no mesmo dia e período (teste que chama o card e o relatório e compara, centavo a centavo); o painel nunca recalcula por conta própria: chama o contrato que o relatório usa.
7. **Dado** "Ver origem" de qualquer número (o `Kpi` é clicável), **quando** clico, **então** abre o relatório de origem com os mesmos filtros (período, cenário, faixa de aging), e o número está lá.
8. **Dado** a `<Dica>` "Como calculamos" de um card, **quando** abro, **então** ela explica a regra em uma ou duas frases (ex.: "Ajustado: cada recebível deslocado pelo atraso médio do cliente nos últimos 12 meses; clientes com mais de 20% vencido há mais de 30 dias entram com o valor reduzido."; no faturamento provisório: "Até a emissão de notas no sistema, o faturamento é a data de emissão dos títulos.").

**Meta de faturamento**
9. **Dado** `settings.edit`, **quando** abro Configurações → Parâmetros → Financeiro → "Metas de faturamento" e informo o valor de cada mês de um ano (`numeric(18,2)`, ≥ 0; "Repetir para o ano" preenche os 12 meses), **então** grava em `finance.revenue_targets` com auditoria (antes e depois), pelo endpoint do Finance (ADR 0016 M6); o card mostra o medidor "{x}% da meta" e a frase "Faltam {n} dias úteis" (dias úteis pelo calendário padrão do tenant, CAD-03, lido pelo `IBusinessCalendar`, novo em `Production.Contracts`); sem meta, o card mostra só o faturado e o link "Definir meta" para quem tem `settings.edit`; sem `settings.edit`, 403 e o link não aparece.

**Faixa "Atenção"**
10. **Dado** os cards visíveis, **quando** o painel monta, **então** a faixa "Atenção" mostra até 3 alertas, na ordem: caixa ajustado negativo nos próximos 90 dias (crítico), decisões aguardando você (alerta, com total e o primeiro vencimento), maior título vencido (alerta, com dias e o atraso médio do cliente); cada alerta é um link para a ação (R-PRP-5); sem alerta, a faixa não aparece. (Os alertas de projeto atrasado e em risco entram na DSH-01b.)

**Cache, atualização e desempenho**
11. **Dado** os cards, **quando** o painel carrega, **então** cada card tem o seu endpoint (`/api/v1/dashboard/cards/{card}`) e o seu skeleton; um card com erro mostra "Não foi possível carregar {card}." com "Tentar de novo" e os outros continuam.
12. **Dado** o cache (ADR 0028 §3), **quando** um card é calculado, **então** o resultado fica em memória por **60 s** com a chave `{tenantId}:{userId}:{permVersion}:{geração}:{card}:{parâmetros}`; o **contador de geração** do tenant (`dashboard:{tenant}:gen`) sobe quando chegam os eventos que mudam os números (`ReceivableSettled`, `PaymentOrderSubmitted`, `PaymentOrderApproved`, `PaymentOrderPaid`, `ContractSigned`, `ContractAmended`, `QuoteApproved`; consumidores idempotentes pela inbox), o que invalida todo o painel do tenant sem varrer o cache; a perda de uma permissão vale na hora (a `perm_version` muda a chave); o card mostra "Atualizado às {hh:mm}".
13. **Dado** o botão "Atualizar", **quando** clico, **então** os cards são lidos de novo ignorando o cache do usuário; o botão tem rate limit pela política nomeada `dashboard-refresh` (1 a cada 10 s por usuário; acima, 429 com "Aguarde alguns segundos para atualizar de novo.").
14. **Dado** 50 mil títulos, 20 mil transações, 50 mil orçamentos e 2 mil clientes, **quando** abro o painel sem cache, **então** todos os cards desta story chegam em menos de 2 s (cada card em menos de 1,5 s), e com cache em menos de 300 ms (medição no teste de integração).
15. **[Segurança]** **Dado** dois tenants e dois usuários do mesmo tenant com permissões diferentes, **quando** abrem o painel com o cache quente, **então** cada um vê só os números do próprio tenant e das próprias permissões (teste); nenhum card desta story leva custo, orçado ou margem (isso é da DSH-01b, com `projects.view_cost`); os DTOs são por forma de permissão (ADR 0028 §2), conferidos por teste de arquitetura.
16. **Dado** o módulo `dashboard` não liberado, **quando** abro, **então** a tela `ModuleNotReleased` ("Funcionalidade não liberada nessa versão do sistema.") e a API responde 403 `module_not_released`; sem grupo com `dashboard.view`, o item some do dock e a API responde 403 `forbidden`.

## Regras de negócio e técnicas
- **Composição, não recálculo** (ponto 18): os endpoints ficam no módulo `Dashboard`, chamam só os `*.Contracts` dos donos e devolvem DTOs de card; nenhum SQL do painel lê tabela de módulo.
- **Mesma fonte do relatório:** cada contrato usado aqui é o mesmo serviço que o relatório usa (FIN-05, FIN-07, FIN-08, FIN-06a, COM-08, DSH-04), garantido por teste.
- **Meta** (ponto 19): mensal por ano, editada com `settings.edit`.
- **Faturamento** (ponto 20): antes das notas reais, os títulos emitidos com o rótulo "Provisório"; com as notas (a partir da GOL-01b), pela nota, pelo mesmo `IRevenueSummary` (a FIS-03a troca a fonte, nada muda aqui).
- **Cache** (ponto 21): por usuário e versão de permissão, 60 s, geração por tenant, "Atualizar"; em memória do processo (uma réplica hoje).
- **Primeira tela** (ponto 22): Início para todos; o Proprietário com `dashboard.view` em `/app/painel`.
- **Dinheiro** `numeric(18,2)`, exibido com `tabular-nums`; "hoje" e dias úteis pelo `BusinessTime` e pelo calendário padrão.

## Permissões, eventos e tabelas
- **Permissões:** `dashboard.view` (tela); `finance.view` e `quotes.view` (cards); `settings.edit` (metas).
- **Eventos:** consome (só para subir o contador de geração) `ReceivableSettled`, `PaymentOrderSubmitted`, `PaymentOrderApproved`, `PaymentOrderPaid`, `ContractSigned`, `ContractAmended`, `QuoteApproved`.
- **Contratos:** `ICashForecast` (FIN-07), `IRevenueSummary` (FIN-08), `IReceivablesAging.SummaryAsync` (novo, `Finance.Contracts`), `IQuoteFunnel.SummaryAsync` (novo, `Commercial.Contracts`), `IAgendaService` (DSH-04), `IBusinessCalendar` (novo, `Production.Contracts`; a RH-01c reaproveita).
- **Tabelas:** `finance.revenue_targets` (`year`, `month`, `amount` `numeric(18,2)`, único em `(year, month)`, auditada; dona do Finance). O `Dashboard` não tem tabela.

## Telas e estados
- **Padrões comuns das Fases 4 a 8:** 1 (caixa no `Chart` dentro do `ChartFrame`, com referência "Saldo zero" e resumo textual) e 2 (`Kpi` e `DashboardCard`: todo número clicável até a origem, "Como calculamos", "Atualizado às", estados por card; faixa "Atenção" com até 3 alertas).
- **Visão geral → Painel do proprietário** (`/app/painel`): cabeçalho com "Atualizar"; faixa Atenção; linha de `Kpi` (Saldo em contas, Faturado no mês com medidor e chip "Provisório", A receber vencido); grade principal com "Caixa projetado" (seletor de cenário) e "Aguardando sua decisão"; grade com "Comercial" (barras do funil, como na COM-08) e o espaço dos cards da DSH-01b.
- **Componentes:** `Kpi` e `DashboardCard` (da DSH-02), `ChartFrame` e `Chart` (da FIN-07).
- **Estados:** carregando (skeleton por card); vazio por card ("Sem títulos ainda. Comece pela implantação financeira." + link); erro por card (CA 11); sem nenhum card visível (CA 4); "Atualizar" bloqueado pelo rate limit (CA 13).
- **Propositivo:** alertas e números levam à ação; "Definir meta" quando falta a meta; o Proprietário chega ao painel sem clicar.
- **400 px:** `Kpi` em 1 coluna, cards empilhados na ordem Atenção → Caixa → Aguardando sua decisão → Faturamento → Vencido → Comercial; o gráfico ocupa a largura com a tabela equivalente; botões com 44 px.
- **Ajuda:** `Ajuda/painel.md` (rota `/app/painel`) com cada card, a regra, o "Provisório" e o "Atualizado às"; `Ajuda/configuracoes-parametros.md` (metas); `<Dica>` em cada "Como calculamos" e em "Provisório".

## Plano de testes
- **Unidade:** frase-resumo e ordem da faixa Atenção (CA 2, 10); dias úteis restantes (CA 9); chave do cache com geração e versão de permissão (CA 12).
- **Integração (Testcontainers, obrigatória):** cada card igual ao relatório de origem com o dataset fixo (CA 6); visibilidade por permissão e por módulo liberado com os usuários do seed (CA 4, 5); página inicial do Proprietário e do Financeiro (CA 3); metas com auditoria e `settings.edit` (CA 9); cache, geração por evento e perda de permissão (CA 12); rate limit do "Atualizar" (CA 13); desempenho com e sem cache (CA 14); dois tenants e dois usuários com permissões diferentes (CA 15); três camadas (CA 16).
- **Arquitetura:** `Dashboard` sem `DbSet`, Dapper, `TenantDbContext` nem schema, referenciando só `*.Contracts` (CA 1); DTOs de card sem custo.
- **E2E (Playwright):** Proprietário entra e cai no painel, troca o cenário do caixa, vai à origem de um número e volta; usuário do Financeiro cai no Início e vê os cards dele no painel; tela a 400 px.

## Fora do escopo
- Margem, carteira, produção, entregas e resultado gerencial por projeto: DSH-01b.
- Painel configurável (mover e esconder cards), comparação com ano anterior, envio do painel por e-mail.
- Cache fora do processo (PostgreSQL ou Redis): só quando a API tiver réplicas (ADR 0028 §3).

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fases 4 a 8, tabela 3.1, pontos 18–22):
1. Módulo `Dashboard` só de composição, ADR 0028; o `Reporting` da ADR 0006 é revogado (D9; CA 1).
2. Meta de faturamento mensal por ano em `finance.revenue_targets`, editada com `settings.edit` (CA 9).
3. Faturamento antes do go-live = títulos emitidos com o rótulo "Provisório"; com as notas reais, pela nota (D4; CA 5, 8).
4. Cache de 60 s por usuário e versão de permissão, contador de geração por tenant e "Atualizar" (D9; CA 12, 13).
5. Primeira tela: Início para todos; o Proprietário com `dashboard.view` em `/app/painel` (D9; CA 3).

Decisões da revisão aplicadas: D9 e ADR 0028 (§1 a §4); D18 (`Kpi` e `DashboardCard` da DSH-02); §4.3 (DTO por forma de permissão e `RequireModule` do dono em cada card, CA 4, 15); o card "Pendências do dono" virou "Aguardando sua decisão" (a pauta filtrada).

**Pré-condição externa:** nenhuma.

## Definition of Done
- [x] Critérios de aceite atendidos.
- [x] Testes de unidade, integração (Testcontainers, dataset fixo comparado com as origens, dois tenants), arquitetura, desempenho e E2E passando (integração e desempenho escritos; rodam na suíte em container, ver Change Log 1.0).
- [x] Projetos `Dashboard.Application` e `Dashboard.Endpoints` registrados no `ModuleRegistry`; teste de arquitetura atualizado.
- [x] Migration de `finance.revenue_targets` (não destrutiva, no Finance).
- [x] Permissões só do `ModuleCatalog`; cada card com a permissão e o módulo do dono.
- [x] Tela com estados por card e 400 px.
- [x] Ajuda do painel e dos parâmetros; teste de cobertura passando.
- [x] Faixa Atenção e drill-down acionáveis.
- [x] Auditoria ligada nas metas.
- [x] OpenAPI e cliente Orval regenerados (gerado pelo Host em processo; falta a regeneração oficial com `BQ_UPDATE_OPENAPI`).
- [x] `docs/02-arquitetura.md` (módulo `Dashboard`) e `docs/03-modelo-dados.md` (`revenue_targets`) atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [x] Projetos do módulo `Dashboard`, endpoints `/api/v1/dashboard/cards/*`, cache com geração e consumidores.
- [x] `IReceivablesAging.SummaryAsync`, `IQuoteFunnel.SummaryAsync` e `IBusinessCalendar`.
- [x] `finance.revenue_targets`, endpoint e tela das metas.
- [x] Tela `/app/painel`, `homeRouteFor` do Proprietário e ajuda.

## Quality gates
- Agentes previstos: @dev, @architect (módulo de composição e teste de arquitetura), @qa, design (painel e 400 px), cybersecurity (cache por usuário e cards por permissão).
- Gate: CA 1, 4, 6, 12, 14 e 15 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fases 4 a 8).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: depende da DSH-02 pelos componentes; bloqueia também a DSH-03) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** quebra da DSH-01 em 01a (estrutura, cache e cards do Financeiro e do Comercial) e 01b; o painel não recalcula: cada card chama o contrato do relatório de origem, com teste de igualdade (CA 6); onde ficam as consultas ficou para o arquiteto; meta e definição de faturamento como pontos em aberto.
- **Correções da revalidação (21/10/2026):** pontos 18 a 22 decididos; módulo `Dashboard` de composição com teste de arquitetura (ADR 0028, CA 1); cache por usuário e versão de permissão com geração por tenant no lugar do cache por tenant, e "Atualizar" com rate limit (CA 12, 13); Proprietário cai no painel (CA 3); faturamento "Provisório" (CA 5, 8); "Aguardando sua decisão" como pauta filtrada; `RequireModule` do dono e DTO por permissão em cada card (§4.3, CA 4, 15); `Kpi` e `DashboardCard` vêm da DSH-02 (D18); `IBusinessCalendar` no `Production.Contracts`; CAs renumerados (16).
- **Pré-condições para `Ready`:** nenhuma de decisão de story.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada; quebra da DSH-01 (01a/01b) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (D4, D9, D18, tabela 3.1 pontos 18–22, §4.3, §4.4 padrões 1 e 2; ADR 0028); ordem mantida (Marco 4.2, ordem 5); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
| 25/10/2026 | 1.0 | Implementada, `Done`. Desvios e pontos de ligação abaixo | Dex (dev) |
| 25/10/2026 | 1.1 | Rebase no lote 4 (FIN-07 → DSH-02 → DSH-04 → FIN-08) e portas ligadas; ver "Ligação (1.1)" | Dex (dev) |

**Ligação (1.1, 25/10/2026)**
- **Portas ligadas** (`Dashboard.Application/Ports.cs`): `CashForecastSource` sobre o `ICashForecast` da FIN-07 (série semanal pelo método novo `WeeksAsync`, a visão semanal da tela do fluxo nos dois cenários, e o saldo final de 30/60/90 dias pelo `SummaryAsync`); `RevenueSource` sobre o `IRevenueSummary.GrossRevenueAsync` da FIN-08 (janeiro até o mês, com o provisório); `DecisionAgenda` sobre o `IAgendaService` da DSH-04 (só as categorias de nível 1, um item por categoria com a contagem). As implementações desligadas e as mensagens "ainda não disponível nesta versão" saíram.
- **"Ver origem"** do faturamento vai ao DRE (`/app/financeiro/dre`); o do caixa vai ao fluxo semanal de 90 dias com o cenário da tela (`cenario=os-dois|base|ajustado`).
- **Front:** o painel usa o `Kpi` e o `DashboardCard` da DSH-02 e o `Chart`/`ChartFrame` da FIN-07 (com "Saldo zero" em y); os locais foram apagados.
- **Página inicial:** uma regra só, `homeRouteFor`/`landingAfterLogin` em `src/Web/src/auth/home-route.ts`, usada no login, no 2FA e no convite: o Proprietário com o painel vai a `/app/painel`, os demais ao Início com a pauta, e a `volta` vale quando houver. O `PageHeading` tem a `count` da DSH-04 e o `documentTitle` desta story.
- **Migration** `FinanceRevenueTargets` refeita depois da `FinanceDre` (mesmo `Up`/`Down`, snapshot da FIN-08).
- **Testes:** unidade dos adaptadores (`DashboardAdaptersTests`) e integração do CA 6 para caixa e faturamento (`DashboardTests`, escrita, não executada). O E2E da pauta passou a dizer que o usuário do "entrar sem rota de volta" não é Proprietário.
- **Pendências:** OpenAPI e cliente gerados localmente (regenerar oficialmente); `CapacityRulesTests.Rotas_da_capacidade_tem_os_dois_modulos_e_as_duas_permissoes` já falha na base (a rota de capacidade do estoque sem `RequireModule("engineering")`), fora desta story.

**Entrega (1.0, 25/10/2026)**
- **Módulo `Dashboard`** (CA 1): `src/Modules/Dashboard/` com `BemAqui.Dashboard.Application` e `BemAqui.Dashboard.Endpoints`, no `ModuleRegistry`; sem seeder (o `dashboard.view` já vinha do `ModuleCatalog` e do `GroupSeeder`), então fora do `CliCommand.Modules`. Teste `CompositionRulesTests` (camadas, referências, `DbSet`/Dapper/`TenantDbContext`/migration/schema, DTOs sem custo) e `SolutionArchitectureTests` com 15 módulos (o `Dashboard` com a exceção das 5 camadas).
- **Contratos novos nos donos:** `IBankBalances` e `IReceivablesAging.SummaryAsync` (Finance, sobre o `BankStatementService` e o `AgingService` das telas), `IRevenueTargets` (Finance), `IQuoteFunnel.SummaryAsync` (Commercial, o mesmo cálculo do funil, refatorado em `StagesAsync`/`ExpiringQuery`), `IBusinessCalendar` (Production, mesma regra da capacidade, só leitura).
- **Metas** (CA 9): `finance.revenue_targets` (migration `FinanceRevenueTargets`, só `CreateTable`/`CreateIndex`), auditada, `GET/PUT /api/v1/finance/revenue-targets/{year}` (`finance.view`; gravar também `settings.edit`), seção "Metas de faturamento" em Configurações → Parâmetros e o evento `RevenueTargetsChanged`, que sobe a geração do painel.
- **Cache e Atualizar** (CA 12 e 13): `DashboardCache` em memória (60 s, chave `dashboard:{tenant}:{usuário}:{perm_version}:{geração}:{card}:{parâmetros}`, teto de 20 mil entradas); "Atualizar" é `POST /api/v1/dashboard/refresh` com a política `dashboard-refresh` e marca o usuário (o card calculado antes da marca é recalculado).
- **Tela** `/app/painel`, ajuda `Ajuda/painel.md` e `configuracoes-parametros.md`; o Proprietário cai no painel depois do login (`homeRouteFor` em `navigation/home-route.ts`).

**Pontos de ligação (portas desligadas, ligar depois do merge)**
- **FIN-07 → `ICashForecastSource`** (`Dashboard.Application/Ports.cs`): adaptador sobre `ICashForecast.SummaryAsync` (cenário ajustado), com as 12 semanas, os horizontes de 30/60/90 dias, os menores saldos e o primeiro dia negativo do ajustado; trocar o registro de `OffCashForecastSource` em `DashboardServices.AddDashboard`. Enquanto desligada, o card mostra "A projeção ajustada pelo histórico dos clientes ainda não está disponível nesta versão." com o link do fluxo de caixa, e o alerta de caixa negativo da faixa Atenção não dispara.
- **FIN-08 → `IRevenueSource`**: adaptador sobre `IRevenueSummary` (linha Receita bruta por mês, com a marca de provisório); trocar `OffRevenueSource`. Enquanto desligada, o card mostra a meta (ou "Definir meta") e "O faturado do mês aparece aqui quando o resumo de faturamento for ativado."; o "Ver origem" do faturamento aponta para `/app/financeiro/dre?linha=receita-bruta`, a conferir com a rota da FIN-08.
- **DSH-04 → `IDecisionAgenda`**: adaptador sobre `IAgendaService` (nível 1 da pauta do usuário); trocar `OffDecisionAgenda`. Unificar o `homeRouteFor` local com o da DSH-04.
- **DSH-02 → `Kpi` e `DashboardCard`**: as versões locais em `src/Web/src/pages/dashboard/DashboardCard.tsx` devem ser unificadas com as da DSH-02.
- **FIN-07 → `ChartFrame`/`Chart`**: o gráfico do caixa é local (`CashChart.tsx`, Recharts com a referência "Saldo zero" e a tabela equivalente); trocar pelo `ChartFrame` depois do merge.
- **Teste CA 6 do caixa e do faturamento:** com as portas ligadas, acrescentar ao `DashboardTests` a comparação do card com o relatório de origem (FIN-07 e DRE da FIN-08), como já é feito para saldo, vencido e comercial.

**Desvios**
- "Aguardando sua decisão" aparece para quem tem uma ação de decisão da pauta (`finance.approve`, `projects.approve`, `production.approve_quality`, `purchasing.approve` ou `hr.approve`), e o endpoint exige uma delas; sem isso o card valeria para todo `dashboard.view` e o estado "sem nenhum card visível" (CA 4) nunca aconteceria. O Financeiro do seed não vê o card. Recomendação mais segura aplicada: recomendação aceita (PO 24/10).
- "Em aberto" do Comercial = rascunhos + enviados dos últimos 90 dias (o que o funil chama de "ainda não decididos"); "Vencendo" conta todos os enviados que vencem em 7 dias (a tela do funil lista os 50 primeiros). Recomendação aceita (PO 24/10).
- A 400 px o Saldo em contas vem antes do Caixa (a lista da story não o citava); depois segue a ordem da story.
- `app.UseRateLimiter()` passou para depois de `app.UseAuthentication()`, para a política por usuário enxergar o `sub`; os limites de `/auth` continuam por IP e tenant.
- Quem chega ao login com `volta=/app` (a sessão caiu no Início) é tratado como sem rota pedida: o Proprietário vai ao painel.
- `PageHeading` ganhou `documentTitle` (a aba mostra "Painel do proprietário" e o h1 a saudação).
- **Testes:** integração (`Dashboard/DashboardTests*.cs`) e desempenho (`DashboardPerformanceTests`) escritos e não executados (Docker em fila); os 4 testes de arquitetura que carregam as DLLs pelo arquivo (`Solution.Assemblies`) não rodam com o Smart App Control do Windows e precisam da suíte em container; o teste de dependências do `Dashboard` carrega pelos tipos e roda.
- **OpenAPI** gerado pelo Host em processo (teste temporário, sem banco, juntando sem reordenar); regenerar oficialmente com `BQ_UPDATE_OPENAPI=1 sh scripts/test-docker.sh --filter-class "*OpenApiDocumentTests"`.
