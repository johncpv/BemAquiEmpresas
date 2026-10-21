# FIN-07 — Projeção de caixa ajustada pelo histórico do cliente

| Campo | Valor |
|---|---|
| ID | FIN-07 |
| Título | Cenário ajustado do fluxo de caixa: cada recebível deslocado pelo atraso médio do cliente (ou da empresa, sem histórico) e ponderado pela inadimplência acima de um limite; base × ajustado lado a lado, com o dia de menor saldo de cada cenário e a diferença causada pelos atrasos; resumo para o painel do dono |
| Tamanho | M |
| Status | Draft |
| Requisitos | R-FIN-11 (cenário ajustado: atraso médio e % de inadimplência, base × ajustado, dia de menor saldo) · R-FIN-10 · R-TER-4 (pontualidade alimenta a projeção ajustada) · R-PRP-5 · PRD §5.14 (Caixa: fluxo projetado 30/60/90 dias, base × ajustado, dia de menor saldo) · PRD §6 (Desempenho) |
| Dependências | FIN-05 (fluxo de caixa base, `CashFlowService`, `BankBalanceAsync`), FIN-06b (`ICustomerPaymentStats`, média do tenant), FIN-00 (títulos `Opening`), FND-09e (`Chart`) |
| Bloqueia | DSH-01a (card Caixa do painel do dono) |
| Ordem de execução | 1 (Marco 4.1 "O dinheiro do jeito que ele vai acontecer") |

## User story
Como **proprietário**, quero **ver o caixa das próximas semanas considerando que cada cliente costuma pagar com o atraso de sempre**, para **saber o dia de aperto real, e não o da planilha que supõe que todo mundo paga em dia**.

## Contexto e referências
- PRD R-FIN-11 e §5.14 (Caixa); §1.1 problema 2 ("não existe previsão confiável de caixa, nem visão de quem atrasa pagamento").
- `04-fluxos-estados` §3.5 **Ajustado**: "cada receivable deslocado pela média de atraso do cliente (últimos 12 meses; sem histórico → média geral do tenant); clientes com % de inadimplência acima de limite configurável têm o valor ponderado por (1 − %inadimplência). Mostra os dois cenários lado a lado e o ponto de menor saldo de cada um."
- FIN-05 (Done): a base, a consulta de saldo atual, o detalhe do dia e as visões diária, semanal e mensal já existem em `/app/financeiro/fluxo-de-caixa`. Esta story só acrescenta o cenário ajustado; a base não muda.
- FIN-06b (Done): `ICustomerPaymentStats.ForPartyAsync` devolve `AvgDelayDays` (ponderado pelo valor, antecipação = 0), `PctPaidLate`, `PctOverdueNow` (vencido ÷ em aberto × 100), `Last12mInvoiced`, `HasHistory` e `TenantAvgDelayDays`, sem os títulos `Opening`. **Código atual:** só existe a consulta por terceiro; a projeção precisa de uma consulta em lote.
- Protótipo (Painel do proprietário): "Caixa projetado, próximas 12 semanas" com o seletor "Os dois / Base / Ajustado", a legenda "Base: vencimentos combinados" e "Ajustado: atraso histórico de cada cliente", e o quadro "Menor saldo previsto (ajustado)", "Menor saldo previsto (base)" e "Diferença causada por atrasos". Tela Financeiro: tabela "Fluxo de caixa semanal" com as colunas "Saldo base" e "Saldo ajustado".
- Revisão pré-Fase 0 §3 (métricas de sucesso com linha de base, ex.: "erro do caixa em D+30 ≤ 10%"): a medição da acurácia fica fora desta story (Fora do escopo).
- ADR 0016 adendo M6 (parâmetro no `finance.parameters`, editado em Configurações → Parâmetros com `settings.edit`).

## Critérios de aceite
**Regra do ajustado**
1. **Dado** o dataset fixo de teste (o da FIN-05 mais 3 clientes: **A** com histórico e atraso médio 10,4 dias; **B** sem histórico, com a média do tenant 6,0 dias; **C** com histórico, atraso médio 3,0 dias e `PctOverdueNow` 40,0%, com o limite em 20,00%), **quando** abro o fluxo diário de hoje até 90 dias com o cenário "Os dois", **então** cada dia mostra entradas, saídas e saldo da base (iguais aos da FIN-05) e do ajustado, centavo a centavo, iguais aos valores esperados do dataset.
2. **Dado** um recebível em aberto (`Open` ou `PartiallyPaid`) ou `Planned` com data, **quando** o ajustado é calculado, **então** ele entra pelo saldo na **data ajustada** = vencimento + atraso médio do cliente arredondado para dias inteiros (`AwayFromZero`), em dias corridos (proposta, Pontos em aberto 3); o cliente sem histórico (`HasHistory = false`) usa `TenantAvgDelayDays`; com o tenant sem nenhum histórico, o deslocamento é 0 e a tela mostra a nota "Ainda não há histórico de pagamento: o ajustado é igual à base."
3. **Dado** um cliente cuja inadimplência (`PctOverdueNow`, proposta, Pontos em aberto 1) passa do parâmetro `finance.adjusted_default_threshold_pct` (seed 20,00, proposta, Pontos em aberto 2), **quando** o ajustado é calculado, **então** cada recebível dele entra com o valor × (1 − inadimplência ÷ 100), arredondado a 2 casas `AwayFromZero` por título, e a diferença aparece no quadro "Risco de não receber no período" (total e clientes); cliente no limite ou abaixo dele entra com o valor cheio.
4. **Dado** um recebível vencido e ainda em aberto, **quando** o ajustado é calculado, **então** ele entra na data max(hoje, vencimento + atraso médio) (proposta, Pontos em aberto 4), com a mesma ponderação do CA 3; na base ele continua na linha "Vencidos em aberto" de hoje (FIN-05 CA 3).
5. **Dado** um recebível deslocado para depois do horizonte, **quando** a tela calcula, **então** ele sai do período do ajustado e o total aparece em "Deslocado para depois de {data final}" com o link para os títulos.
6. **Dado** títulos a pagar, recorrentes, transações realizadas e recebíveis `Planned` sem data, **quando** o ajustado é calculado, **então** eles entram exatamente como na base (só o recebimento é ajustado); os recebíveis `Opening` (FIN-00) são ajustados como os demais, embora não contem para o histórico (FIN-06b CA 2).

**Tela**
7. **Dado** o fluxo de caixa (`/app/financeiro/fluxo-de-caixa`), **quando** escolho o cenário "Os dois", "Base" ou "Ajustado" (filtro na URL, padrão "Os dois"), **então** o gráfico mostra a linha de saldo do cenário escolhido (base e ajustado com cores distintas dos tokens `--chart-*`; o realizado continua em traço contínuo e o projetado tracejado), a tabela ganha a coluna "Saldo ajustado" e as visões semanal e mensal somam os dias como na FIN-05 (os totais das três visões batem, teste).
8. **Dado** o período projetado, **quando** a tela calcula, **então** o quadro de destaques mostra "Menor saldo previsto (ajustado)" e "Menor saldo previsto (base)" (data e valor) e "Diferença causada por atrasos" (menor saldo base − menor saldo ajustado); se o ajustado ficar negativo, o alerta crítico "Considerando os atrasos de sempre, o caixa fica negativo em {data}: {valor}." leva ao detalhe desse dia (R-PRP-5).
9. **Dado** um dia do ajustado, **quando** clico, **então** o painel lateral lista o que entra nesse dia no ajustado, cada recebível com o vencimento original, a data ajustada e o motivo ("Atraso médio de 10 dias de {cliente}" ou "Média da empresa: 6 dias", e "Valor ponderado pela inadimplência de 40,0%"), e o que saiu desse dia por deslocamento, com link para o título.
10. **Dado** o filtro por conta (FIN-05 CA 9), **quando** escolho uma conta, **então** o ajustado segue a regra da base: só aparece no consolidado, com a mesma nota.

**Contrato para o painel do dono**
11. **Dado** o `ICashForecast` em `Finance.Contracts` (novo), **quando** o painel do dono (DSH-01a) chama `SummaryAsync(horizonDays: 30 | 60 | 90)`, **então** recebe o saldo atual consolidado, o saldo final do horizonte e o menor saldo (data e valor) de cada cenário, e a diferença; os números são os mesmos da tela para o mesmo dia e horizonte (teste que compara as duas saídas).

**Parâmetro, desempenho, permissões e segurança**
12. **Dado** `settings.edit`, **quando** altero "Limite de inadimplência para ponderar o ajustado" em Configurações → Parâmetros → Financeiro (0,00 a 100,00, 2 casas), **então** grava em `finance.parameters` com auditoria, pelo endpoint do Finance (ADR 0016 M6), e vale no próximo cálculo; sem `settings.edit`, 403.
13. **Dado** 50 mil títulos, 2 mil clientes e 20 mil transações, **quando** abro o fluxo de 90 dias com "Os dois", **então** a resposta chega em menos de 2 s: a pontualidade de todos os clientes com título em aberto vem numa consulta só (`ICustomerPaymentStats.ForPartiesAsync`, nova, em lote), sem N+1 (medição no teste de integração).
14. **[Segurança]** **Dado** o detalhe do dia com recebíveis de cliente pessoa física, **quando** a API responde, **então** exige `finance.view`, leva `Cache-Control: no-store` e mostra o atraso médio só do cliente do título (FIN-06b CA 7); o SQL usa só parâmetros e a ordenação aceita só a allowlist da FIN-05 (422 fora dela).
15. **Dado** o módulo `finance` não liberado, **quando** chamo, **então** 403 `module_not_released` e a tela `ModuleNotReleased`; sem grupo ou sem `finance.view`, 403 `forbidden`. `RequireModule` e `perm:` no endpoint do fluxo, no do resumo e no do parâmetro.

## Regras de negócio e técnicas
- **Cálculo puro e testável:** `AdjustedCashFlowCalculator` no Domain do Finance recebe a série da base, os recebíveis projetados e a pontualidade por cliente e devolve a série ajustada; a base continua sendo calculada pelo `CashFlowService` da FIN-05, sem mudança de regra.
- **Atraso médio:** `AvgDelayDays` (FIN-06b, últimos 12 meses, ponderado pelo valor, antecipação = 0), arredondado a dias inteiros só no deslocamento.
- **Inadimplência:** `PctOverdueNow` (FIN-06b: vencido ÷ em aberto do cliente × 100) na data de hoje (proposta, Pontos em aberto 1).
- **Dinheiro** `numeric(18,2)`, somas em `decimal`; **datas** `date`, "hoje" pelo `BusinessTime.Today`; nada é gravado (sem tabela nova; o cálculo é na leitura).
- **Horizonte** igual ao da FIN-05 (padrão 90, máximo 365 dias).

## Permissões, eventos e tabelas
- **Permissões:** `finance.view` (fluxo, detalhe, resumo); `settings.edit` (parâmetro).
- **Eventos:** nenhum.
- **Contratos:** novo `ICashForecast` (`Finance.Contracts`); `ICustomerPaymentStats.ForPartiesAsync(ids)` (novo método, em lote).
- **Tabelas:** nenhuma nova; chave nova em `finance.parameters`: `finance.adjusted_default_threshold_pct` (seed 20,00, **proposta**).

## Telas e estados
- **Financeiro → Fluxo de caixa** (existente): seletor de cenário "Os dois / Base / Ajustado" (`ToggleGroup`), coluna "Saldo ajustado", quadro de destaques do protótipo, quadro "Risco de não receber no período" e "Deslocado para depois de {data}".
- **Estados:** carregando (skeleton, como na FIN-05); sem histórico (nota do CA 2, com o ajustado igual à base); vazio e erro como na FIN-05.
- **Propositivo:** o alerta de caixa negativo no ajustado abre o dia; o cliente que puxa o ajustado para baixo tem link para a Inadimplência do cliente (FIN-06a) e para "Ver títulos".
- **Acessibilidade:** a tabela equivalente do gráfico tem as duas colunas de saldo; o resumo textual cita os dois menores saldos.
- **400 px:** a tabela mostra período, saldo base e saldo ajustado (entradas e saídas na linha expansível); o seletor de cenário fica acima do gráfico.
- **Ajuda:** atualizar `Ajuda/financeiro-fluxo-de-caixa.md` (cenários, regra do ajustado, limite de inadimplência) e `Ajuda/configuracoes-parametros.md` (novo parâmetro); `<Dica>` em "Ajustado", "Atraso médio", "Média da empresa" e "Risco de não receber".

## Plano de testes
- **Unidade:** deslocamento com e sem histórico, arredondamento do atraso, ponderação acima e abaixo do limite, vencido em aberto (max), deslocado para fora do horizonte, payables intocados (CA 2–6), com tabela de casos.
- **Integração (Testcontainers, obrigatória):** dataset fixo com valores esperados nos dois cenários (CA 1); visões semanal e mensal batendo (CA 7); destaques e alerta (CA 8); detalhe do dia com motivos (CA 9); `ICashForecast` igual à tela (CA 11); parâmetro com auditoria e `settings.edit` (CA 12); desempenho com 50 mil títulos e 2 mil clientes, sem N+1 (CA 13); `no-store` e allowlist (CA 14); três camadas (CA 15).
- **E2E (Playwright):** abrir o fluxo, trocar o cenário, abrir um dia do ajustado; tela a 400 px.

## Fora do escopo
- Medição da acurácia da projeção (erro em D+30) e calibração automática do limite: depois do go-live, quando houver histórico.
- Ajuste dos pagamentos (atraso da própria empresa) e da folha: não previstos no `04-fluxos` §3.5.
- Cenário por conta bancária: segue a FIN-05 (só consolidado até a BNK-01).

## Pontos em aberto
1. **Qual "% de inadimplência"** (PO): o `04-fluxos` §3.5 não define. Proposta: `PctOverdueNow` da FIN-06b (vencido hoje ÷ em aberto do cliente). Alternativas: `PctPaidLate` (fração paga com atraso nos 12 meses) ou vencido há mais de 30 dias ÷ faturado em 12 meses.
2. **Limite padrão** (PO): proposta 20,00%, editável.
3. **Dias corridos ou úteis no deslocamento** (PO): proposta: dias corridos, sem ajustar o fim de semana, coerente com a CAD-05b (vencimento em dia não útil não é ajustado).
4. **Vencido em aberto no ajustado** (PO): proposta: max(hoje, vencimento + atraso médio). Alternativa: hoje + atraso médio inteiro (mais conservadora).

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers, dataset fixo nos dois cenários), desempenho e E2E passando.
- [ ] Sem migration de tabela; seed da chave do parâmetro idempotente, também nos tenants existentes.
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Tela com estados (inclusive "sem histórico") e 400 px.
- [ ] Ajuda atualizada (fluxo e parâmetros); teste de cobertura passando.
- [ ] Alerta acionável e detalhe do dia com os motivos.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` (chave do parâmetro) e `docs/04-fluxos-estados.md` §3.5 (regras decididas) atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `AdjustedCashFlowCalculator` (Domain) e testes de tabela.
- [ ] `ICustomerPaymentStats.ForPartiesAsync` em lote; `ICashForecast` e o resumo.
- [ ] Endpoint do fluxo com `scenario`; parâmetro no `FinanceParametersService`; tela e ajuda.

## Quality gates
- Agentes previstos: @dev, @qa, design (gráfico com dois cenários e acessibilidade), @data-engineer (consulta em lote).
- Gate: CA 1, 3, 11 e 13 obrigatórios.

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

- **Correções feitas na validação:** a regra do `04-fluxos` §3.5 não diz qual % de inadimplência usar nem como tratar o vencido em aberto: viraram pontos em aberto com proposta (1 e 4), sem decidir; o resumo para o painel passou a ser contrato com teste de igualdade com a tela (CA 11); consulta da pontualidade em lote para a meta de 2 s (CA 13).
- **Pré-condições para `Ready`:** pontos em aberto 1 e 4.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
