# FIN-07 — Projeção de caixa ajustada pelo histórico do cliente

| Campo | Valor |
|---|---|
| ID | FIN-07 |
| Título | Cenário ajustado do fluxo de caixa: cada recebível deslocado pelo atraso médio do cliente (ou da empresa, sem histórico) e ponderado pela inadimplência acima de um limite; base × ajustado lado a lado, com o dia de menor saldo de cada cenário e a diferença causada pelos atrasos; resumo para o painel do dono; nasce aqui o `ChartFrame` da família de gráficos e a troca do `--chart-3` (D18) |
| Tamanho | M |
| Status | Ready |
| Requisitos | R-FIN-11 (cenário ajustado: atraso médio e % de inadimplência, base × ajustado, dia de menor saldo) · R-FIN-10 · R-TER-4 (pontualidade alimenta a projeção ajustada) · R-PRP-5 · PRD §5.14 (Caixa: fluxo projetado 30/60/90 dias, base × ajustado, dia de menor saldo) · PRD §6 (Desempenho) |
| Dependências | FIN-05 (fluxo de caixa base, `CashFlowService`, `BankBalanceAsync`), FIN-06b (`ICustomerPaymentStats`, média do tenant), FIN-00 (títulos `Opening`), FND-09e (`Chart`, tokens `--chart-*`) |
| Bloqueia | DSH-02 (usa o `ChartFrame` e a paleta nova), DSH-01a (card Caixa do painel do dono) |
| Ordem de execução | 1 (Marco 4.1 "O dinheiro do jeito que ele vai acontecer") |

## User story
Como **proprietário**, quero **ver o caixa das próximas semanas considerando que cada cliente costuma pagar com o atraso de sempre**, para **saber o dia de aperto real, e não o da planilha que supõe que todo mundo paga em dia**.

## Contexto e referências
- PRD R-FIN-11 e §5.14 (Caixa); §1.1 problema 2 ("não existe previsão confiável de caixa, nem visão de quem atrasa pagamento").
- `04-fluxos-estados` §3.5 **Ajustado**: "cada receivable deslocado pela média de atraso do cliente (últimos 12 meses; sem histórico → média geral do tenant); clientes com % de inadimplência acima de limite configurável têm o valor ponderado por (1 − %inadimplência). Mostra os dois cenários lado a lado e o ponto de menor saldo de cada um." Esta story fixa o que o §3.5 não dizia (qual inadimplência, dias corridos, vencido em aberto).
- FIN-05 (Done): a base, a consulta de saldo atual, o detalhe do dia e as visões diária, semanal e mensal já existem em `/app/financeiro/fluxo-de-caixa`. Esta story só acrescenta o cenário ajustado; a base não muda.
- FIN-06b (Done): `ICustomerPaymentStats.ForPartyAsync` devolve `AvgDelayDays` (ponderado pelo valor, antecipação = 0), `PctPaidLate`, `PctOverdueNow`, `Last12mInvoiced`, `HasHistory` e `TenantAvgDelayDays`, sem os títulos `Opening`. **Código atual:** só existe a consulta por terceiro; a projeção precisa de uma consulta em lote e de uma medida nova de inadimplência (CA 3).
- **Código atual do front:** `Chart` (`components/ui/chart.tsx`, Recharts, só linhas, realizado contínuo e projetado tracejado, tabela equivalente) e a paleta `--chart-1` a `--chart-6` em `styles/tokens.css`, com o `--chart-3` em âmbar (`#a35a00` no claro, `#f0b25a` no escuro), que o CLAUDE.md reserva para estado e item ativo.
- **Revisão pré-Fases 4 a 8** (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`): tabela 3.1, pontos 1 a 4; D18 (componentes antes dos marcos: `ChartFrame` e troca do `--chart-3` nesta story); §4.4 padrão 1 (família de gráficos sobre o `ChartFrame`).
- Protótipo (Painel do proprietário): "Caixa projetado, próximas 12 semanas" com o seletor "Os dois / Base / Ajustado", a legenda "Base: vencimentos combinados" e "Ajustado: atraso histórico de cada cliente", e o quadro "Menor saldo previsto (ajustado)", "Menor saldo previsto (base)" e "Diferença causada por atrasos". Tela Financeiro: tabela "Fluxo de caixa semanal" com as colunas "Saldo base" e "Saldo ajustado".
- Revisão pré-Fase 0 §3 (métricas de sucesso com linha de base, ex.: "erro do caixa em D+30 ≤ 10%"): a medição da acurácia fica fora desta story (Fora do escopo).
- ADR 0016 adendo M6 (parâmetro no `finance.parameters`, editado em Configurações → Parâmetros com `settings.edit`).

## Critérios de aceite
**Regra do ajustado**
1. **Dado** o dataset fixo de teste (o da FIN-05 mais 3 clientes: **A** com histórico e atraso médio 10,4 dias; **B** sem histórico, com a média do tenant 6,0 dias; **C** com histórico, atraso médio 3,0 dias e inadimplência 40,00% (R$ 8.000,00 vencidos há mais de 30 dias ÷ R$ 20.000,00 faturados nos últimos 12 meses), com o limite em 20,00%), **quando** abro o fluxo diário de hoje até 90 dias com o cenário "Os dois", **então** cada dia mostra entradas, saídas e saldo da base (iguais aos da FIN-05) e do ajustado, centavo a centavo, iguais aos valores esperados do dataset.
2. **Dado** um recebível em aberto (`Open` ou `PartiallyPaid`) ou `Planned` com data, **quando** o ajustado é calculado, **então** ele entra pelo saldo na **data ajustada** = vencimento + atraso médio do cliente arredondado para dias inteiros (`AwayFromZero`), em **dias corridos**, sem ajustar fim de semana nem feriado (coerente com a CAD-05b: vencimento em dia não útil não é ajustado); o cliente sem histórico (`HasHistory = false`) usa `TenantAvgDelayDays`; com o tenant sem nenhum histórico, o deslocamento é 0 e a tela mostra a nota "Ainda não há histórico de pagamento: o ajustado é igual à base."
3. **Dado** a **inadimplência do cliente** = min(100, saldo em aberto dos títulos vencidos **há mais de 30 dias** ÷ valor faturado nos **últimos 12 meses** × 100), com 2 casas (os títulos `Opening` entram no vencido, porque são dívida real, e não entram no faturado, como na FIN-06b CA 2; faturado 0 dá 0% sem vencido e 100% com vencido), **quando** ela passa do parâmetro `finance.adjusted_default_threshold_pct` (seed 20,00), **então** cada recebível desse cliente entra com o valor × (1 − inadimplência ÷ 100), arredondado a 2 casas `AwayFromZero` por título, e a diferença aparece no quadro "Risco de não receber no período" (total e clientes); cliente no limite ou abaixo dele entra com o valor cheio.
4. **Dado** um recebível vencido e ainda em aberto, **quando** o ajustado é calculado, **então** ele entra na data **max(hoje, vencimento + atraso médio)**, com a mesma ponderação do CA 3; na base ele continua na linha "Vencidos em aberto" de hoje (FIN-05 CA 3).
5. **Dado** um recebível deslocado para depois do horizonte, **quando** a tela calcula, **então** ele sai do período do ajustado e o total aparece em "Deslocado para depois de {data final}" com o link para os títulos.
6. **Dado** títulos a pagar, recorrentes, transações realizadas e recebíveis `Planned` sem data, **quando** o ajustado é calculado, **então** eles entram exatamente como na base (só o recebimento é ajustado); os recebíveis `Opening` (FIN-00) são ajustados como os demais, embora não contem para o atraso médio (FIN-06b CA 2).

**Tela**
7. **Dado** o fluxo de caixa (`/app/financeiro/fluxo-de-caixa`), **quando** escolho o cenário "Os dois", "Base" ou "Ajustado" (filtro na URL, padrão "Os dois"), **então** o gráfico mostra a linha de saldo do cenário escolhido, a tabela ganha a coluna "Saldo ajustado" e as visões semanal e mensal somam os dias como na FIN-05 (os totais das três visões batem, teste).
8. **Dado** o período projetado, **quando** a tela calcula, **então** o quadro de destaques mostra "Menor saldo previsto (ajustado)" e "Menor saldo previsto (base)" (data e valor) e "Diferença causada por atrasos" (menor saldo base − menor saldo ajustado); se o ajustado ficar negativo, o alerta crítico "Considerando os atrasos de sempre, o caixa fica negativo em {data}: {valor}." leva ao detalhe desse dia (R-PRP-5).
9. **Dado** um dia do ajustado, **quando** clico, **então** o painel lateral lista o que entra nesse dia no ajustado, cada recebível com o vencimento original, a data ajustada e o motivo ("Atraso médio de 10 dias de {cliente}" ou "Média da empresa: 6 dias", e "Valor ponderado pela inadimplência de 40,00%"), e o que saiu desse dia por deslocamento, com link para o título; o total do painel é igual ao da célula (teste).
10. **Dado** o filtro por conta (FIN-05 CA 9), **quando** escolho uma conta, **então** o ajustado segue a regra da base: só aparece no consolidado, com a mesma nota.

**Família de gráficos (D18)**
11. **Dado** o componente `ChartFrame` (novo, nos componentes de UI), **quando** um gráfico o usa, **então** ele entrega a moldura comum: título, **resumo textual acima do gráfico** (montado pela tela; aqui: "Menor saldo ajustado em {data}: {valor}; na base, em {data}: {valor}."), legenda com **rótulo direto** no fim de cada série, as **referências** em y (aqui, a linha "Saldo zero") e em x (aqui, "Hoje"), o botão "Ver como tabela" com a tabela equivalente navegável pelo teclado e os estados carregando, vazio e erro; o `Chart` de linhas passa a ser desenhado dentro do `ChartFrame` (o `ColumnChart` da DSH-02 e o Gantt da PLN-03 reaproveitam a moldura), e as telas que já usam o `Chart` (FIN-05, COM-08) continuam iguais na regressão visual.
12. **Dado** as séries do fluxo, **quando** desenhadas, **então** nenhuma série se distingue só pela cor: a base em traço tracejado fino e o ajustado em traço tracejado grosso com marcador, além do rótulo direto (o realizado continua em traço contínuo); as cores vêm dos tokens `--chart-*`.
13. **Dado** o token `--chart-3`, **quando** esta story entra, **então** ele deixa o âmbar e passa a um tom frio fora da faixa do `--signal` e dos tons de estado (valor escolhido pelo design nos temas claro e escuro), com contraste ≥ 3:1 contra o card nos dois temas e distinto das outras cinco séries para daltonismo (protanopia e deuteranopia); o teste de contraste dos tokens (FND-09e CA 11) passa a reprovar série de gráfico na faixa de matiz do âmbar.

**Contrato para o painel do dono**
14. **Dado** o `ICashForecast` em `Finance.Contracts` (novo), **quando** o painel do dono (DSH-01a) chama `SummaryAsync(horizonDays: 30 | 60 | 90)`, **então** recebe o saldo atual consolidado, o saldo final do horizonte e o menor saldo (data e valor) de cada cenário, e a diferença; os números são os mesmos da tela para o mesmo dia e horizonte (teste que compara as duas saídas).

**Parâmetro, desempenho, permissões e segurança**
15. **Dado** `settings.edit`, **quando** altero "Limite de inadimplência para ponderar o ajustado" em Configurações → Parâmetros → Financeiro (0,00 a 100,00, 2 casas), **então** grava em `finance.parameters` com auditoria (antes e depois), pelo endpoint do Finance (ADR 0016 M6), e vale no próximo cálculo; sem `settings.edit`, 403 e o campo fica só leitura.
16. **Dado** 50 mil títulos, 2 mil clientes e 20 mil transações, **quando** abro o fluxo de 90 dias com "Os dois", **então** a resposta chega em menos de 2 s: a pontualidade e a inadimplência de todos os clientes com título em aberto vêm numa consulta só (`ICustomerPaymentStats.ForPartiesAsync`, nova, em lote), sem N+1 (medição no teste de integração).
17. **[Segurança]** **Dado** o detalhe do dia com recebíveis de cliente pessoa física, **quando** a API responde, **então** exige `finance.view`, leva `Cache-Control: no-store` e mostra o atraso médio e a inadimplência só do cliente do título (FIN-06b CA 7); o SQL usa só parâmetros e a ordenação aceita só a allowlist da FIN-05 (422 fora dela).
18. **Dado** o módulo `finance` não liberado, **quando** chamo, **então** 403 `module_not_released` e a tela `ModuleNotReleased`; sem grupo ou sem `finance.view`, 403 `forbidden`. `RequireModule` e `perm:` no endpoint do fluxo, no do resumo e no do parâmetro.

## Regras de negócio e técnicas
- **Cálculo puro e testável:** `AdjustedCashFlowCalculator` no Domain do Finance recebe a série da base, os recebíveis projetados e a pontualidade por cliente e devolve a série ajustada; a base continua sendo calculada pelo `CashFlowService` da FIN-05, sem mudança de regra.
- **Atraso médio:** `AvgDelayDays` (FIN-06b, últimos 12 meses, ponderado pelo valor, antecipação = 0), arredondado a dias inteiros só no deslocamento; **dias corridos** (ponto 3).
- **Inadimplência** (ponto 1): vencido há mais de 30 dias ÷ faturado em 12 meses, na data de hoje, limitada a 100%; o atraso já desloca a data, e a inadimplência só pesa a parte que provavelmente não entra (evita oscilar com poucos títulos grandes, como o `PctOverdueNow`).
- **Vencido em aberto** (ponto 4): max(hoje, vencimento + atraso médio).
- **Dinheiro** `numeric(18,2)`, somas em `decimal`; **datas** `date`, "hoje" pelo `BusinessTime.Today`; nada é gravado (sem tabela nova; o cálculo é na leitura).
- **Horizonte** igual ao da FIN-05 (padrão 90, máximo 365 dias).
- **`ChartFrame`:** componente de apresentação, sem regra de negócio; o resumo textual e as referências são passados pela tela; sem cor literal (regra de lint de cor literal).

## Permissões, eventos e tabelas
- **Permissões:** `finance.view` (fluxo, detalhe, resumo); `settings.edit` (parâmetro).
- **Eventos:** nenhum.
- **Contratos:** novo `ICashForecast` (`Finance.Contracts`); `ICustomerPaymentStats.ForPartiesAsync(ids)` (novo método, em lote, com `AvgDelayDays`, `HasHistory`, `TenantAvgDelayDays`, `Overdue30Amount`, `Last12mInvoiced` e `DefaultPct`).
- **Tabelas:** nenhuma nova; chave nova em `finance.parameters`: `finance.adjusted_default_threshold_pct` (seed 20,00).

## Telas e estados
- **Padrões comuns das Fases 4 a 8:** 1 (família de gráficos sobre o `ChartFrame`: série nunca só por cor, referências em x e em y, resumo textual acima e tabela equivalente) e 2 (número com "Como calculamos" e origem, nos destaques).
- **Financeiro → Fluxo de caixa** (existente): seletor de cenário "Os dois / Base / Ajustado" (`ToggleGroup`), coluna "Saldo ajustado", quadro de destaques do protótipo, quadro "Risco de não receber no período" e "Deslocado para depois de {data}"; o gráfico dentro do `ChartFrame`.
- **Componentes (D18):** `ChartFrame` (novo) e o `--chart-3` trocado (CA 11 a 13).
- **Estados:** carregando (skeleton, como na FIN-05); sem histórico (nota do CA 2, com o ajustado igual à base); vazio e erro como na FIN-05.
- **Propositivo:** o alerta de caixa negativo no ajustado abre o dia; o cliente que puxa o ajustado para baixo tem link para a Inadimplência do cliente (FIN-06a) e para "Ver títulos".
- **Acessibilidade:** a tabela equivalente do gráfico tem as duas colunas de saldo; o resumo textual cita os dois menores saldos.
- **400 px:** a tabela mostra período, saldo base e saldo ajustado (entradas e saídas na linha expansível); o seletor de cenário fica acima do gráfico; botões de `ErrorState`/`EmptyState` com alvo de 44 px.
- **Ajuda:** atualizar `Ajuda/financeiro-fluxo-de-caixa.md` (cenários, regra do ajustado, inadimplência de 30 dias sobre 12 meses, limite) e `Ajuda/configuracoes-parametros.md` (novo parâmetro); `<Dica>` em "Ajustado", "Atraso médio", "Média da empresa", "Inadimplência" e "Risco de não receber".

## Plano de testes
- **Unidade:** deslocamento com e sem histórico, arredondamento do atraso, dias corridos, inadimplência (30 dias ÷ 12 meses, `Opening`, faturado 0, teto de 100%), ponderação acima e abaixo do limite, vencido em aberto (max), deslocado para fora do horizonte, payables intocados (CA 2–6), com tabela de casos.
- **Integração (Testcontainers, obrigatória):** dataset fixo com valores esperados nos dois cenários (CA 1); visões semanal e mensal batendo (CA 7); destaques e alerta (CA 8); detalhe do dia com motivos e total igual (CA 9); `ICashForecast` igual à tela (CA 14); parâmetro com auditoria e `settings.edit` (CA 15); desempenho com 50 mil títulos e 2 mil clientes, sem N+1 (CA 16); `no-store` e allowlist (CA 17); três camadas (CA 18).
- **Front (Vitest):** `ChartFrame` com resumo, rótulo direto, referências e tabela equivalente; séries com traço distinto (CA 11, 12); teste de contraste e de matiz dos tokens `--chart-*` nos dois temas (CA 13).
- **E2E (Playwright):** abrir o fluxo, trocar o cenário, abrir um dia do ajustado, abrir "Ver como tabela" pelo teclado; regressão visual da FIN-05 e da COM-08; tela a 400 px.

## Fora do escopo
- Medição da acurácia da projeção (erro em D+30) e calibração automática do limite: quando houver histórico real de pagamentos no tenant (a linha de base das métricas entra na GOL-01a).
- Ajuste dos pagamentos (atraso da própria empresa) e da folha: não previstos no `04-fluxos` §3.5.
- Cenário por conta bancária: segue a FIN-05 (só consolidado até a BNK-01a).
- `ColumnChart`, `Kpi` e `DashboardCard`: DSH-02. `TreeTable` com coluna fixa e navegação por célula: FIN-08. Gantt: PLN-03.

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fases 4 a 8, tabela 3.1, pontos 1–4):
1. Inadimplência = vencido há mais de 30 dias ÷ faturado em 12 meses, com teto de 100% (CA 1, 3).
2. Limite padrão de 20,00%, editável em Configurações → Parâmetros (CA 3, 15).
3. Deslocamento em dias corridos (CA 2).
4. Vencido em aberto no ajustado em max(hoje, vencimento + atraso médio) (CA 4).

Decisões da revisão aplicadas: D18 (`ChartFrame` e troca do `--chart-3` nesta story, CA 11 a 13); §4.4 padrão 1 (série nunca só por cor, referências, resumo textual e tabela equivalente, CA 11, 12).

**Pré-condição externa:** nenhuma.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers, dataset fixo nos dois cenários), front (Vitest), desempenho e E2E passando.
- [ ] Sem migration de tabela; seed da chave do parâmetro idempotente, também nos tenants existentes.
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Tela com estados (inclusive "sem histórico") e 400 px.
- [ ] `ChartFrame` com tokens e sem cor literal; `--chart-3` fora do âmbar, com o teste de contraste e de matiz; regressão visual das telas que já usam o `Chart`.
- [ ] Ajuda atualizada (fluxo e parâmetros); teste de cobertura passando.
- [ ] Alerta acionável e detalhe do dia com os motivos.
- [ ] Auditoria ligada no parâmetro.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` (chave do parâmetro) e `docs/04-fluxos-estados.md` §3.5 (regras decididas) atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `AdjustedCashFlowCalculator` (Domain) e testes de tabela.
- [ ] `ICustomerPaymentStats.ForPartiesAsync` em lote (com a inadimplência de 30 dias sobre 12 meses); `ICashForecast` e o resumo.
- [ ] Endpoint do fluxo com `scenario`; parâmetro no `FinanceParametersService`.
- [ ] `ChartFrame` e o `Chart` dentro dele; troca do `--chart-3` e teste de tokens; tela e ajuda.

## Quality gates
- Agentes previstos: @dev, @qa, design (`ChartFrame`, paleta e acessibilidade), @data-engineer (consulta em lote).
- Gate: CA 1, 3, 11, 13, 14 e 16 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fases 4 a 8).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: passa a bloquear a DSH-02, que usa o `ChartFrame`) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** a regra do `04-fluxos` §3.5 não diz qual % de inadimplência usar nem como tratar o vencido em aberto: viraram pontos em aberto com proposta (1 e 4), sem decidir; o resumo para o painel passou a ser contrato com teste de igualdade com a tela (CA 14); consulta da pontualidade em lote para a meta de 2 s (CA 16).
- **Correções da revalidação (21/10/2026):** pontos 1 a 4 decididos (inadimplência de 30 dias sobre 12 meses no lugar do `PctOverdueNow`, com `Opening` no vencido e teto de 100%, CA 1, 3; limite de 20%; dias corridos; vencido em aberto em max(hoje, vencimento + atraso)); D18: `ChartFrame` (moldura comum da família de gráficos) e troca do `--chart-3` (CA 11 a 13); padrão 1 das Fases 4 a 8 (série nunca só por cor, referências em x e y, resumo textual); total do detalhe do dia igual à célula (CA 9); a FIN-07 passa a bloquear a DSH-02; CAs renumerados (18).
- **Pré-condições para `Ready`:** nenhuma de decisão de story.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (D18, tabela 3.1 pontos 1–4, §4.4 padrão 1); `ChartFrame` e `--chart-3`; ordem mantida (Marco 4.1, ordem 1); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
