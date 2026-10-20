# FIN-05 — Fluxo de caixa base: realizado + projetado

| Campo | Valor |
|---|---|
| ID | FIN-05 |
| Título | Fluxo de caixa diário, semanal e mensal, consolidado ou por conta: realizado pelas transações e projetado pelos títulos em aberto, previstos e estimados e pelas recorrentes futuras, com o dia de menor saldo; consulta de saldo atual por conta |
| Tamanho | M |
| Status | Done |
| Requisitos | R-FIN-10 (realizado + projetado, diário/semanal/mensal; base = títulos em aberto + recorrentes + pedidos de compra + folha prevista) · R-FIN-2 (saldo por conta) · R-PRP-5 · PRD §6 (Desempenho) |
| Dependências | FIN-01 (transações, recebíveis), FIN-02 (títulos a pagar, recorrentes), FIN-00 (saldos e títulos de implantação, para o dado real), FND-09e (`Chart`, tokens de gráfico) |
| Bloqueia | FIN-04 (reaproveita a consulta de saldo), FIN-07 (cenário ajustado), DSH-01 (card de caixa), FIN-08 |
| Ordem de execução | 4 (Marco 2.1a "Ver o caixa"; fecha o "caixa projetado" do PRD §7) |

## User story
Como **proprietário**, quero **ver quanto entra, quanto sai e qual será o saldo dia a dia nas próximas semanas, com o pior dia destacado**, para **antecipar falta de caixa antes que ela aconteça**.

## Contexto e referências
- PRD R-FIN-10; `04-fluxos-estados` §3.5 **Base** ("saldo atual + receivables em aberto/planejados (vencimento original) − payables em aberto/planejados − recorrentes futuros − folha prevista"). O **Ajustado** é a FIN-07 (Fase 4).
- Protótipo: painel "Fluxo de caixa semanal" (semana, entradas previstas, saídas previstas, saldo base, saldo ajustado, destaques). Nesta story a coluna "Saldo ajustado" não aparece (FIN-07).
- ADR 0006 (consultas pesadas: Dapper pela conexão do `TenantDbContext`, sempre com parâmetros); ADR 0019 §17 (CP estimado do instalador entra como estimativa).
- Revisão pré-Fase 2 §4 (ordem): esta story vem antes da FIN-03a e da FIN-04 e passa a ser dona da consulta de saldo atual, que a FIN-04 reaproveita. Os pagamentos (FIN-03a), transferências e lançamentos (FIN-04) entram no realizado sem código novo, porque são transações.

## Critérios de aceite
1. **Dado** o dataset fixo de teste (2 contas, saldo inicial, 6 transações realizadas, 5 títulos a receber em aberto, 4 a pagar em aberto, 1 título a pagar `Planned` estimado, 1 recorrente de R$ 3.000,00 todo dia 10, 1 recebível `Planned` sem data), **quando** abro o fluxo diário de hoje até 90 dias, **então** cada dia mostra entradas previstas, saídas previstas e saldo projetado iguais aos valores esperados do dataset, centavo a centavo (CA do backlog).
2. **Dado** o fluxo, **quando** abro, **então** o saldo de partida é o saldo atual consolidado das contas ativas (`opening_balance` + Σ transações com data ≤ hoje, sem as futuras), calculado pela consulta `BankBalanceQuery` desta story; os dias passados do período mostram o **realizado** (transações) e os dias de hoje em diante o **projetado**; a linha "hoje" separa os dois.
3. **Dado** títulos em aberto, **quando** projetados, **então** entram pelo **saldo** (recebíveis: `amount − paid_amount`; pagáveis: líquido − `paid_amount`) na data de vencimento original; títulos vencidos e ainda em aberto entram no dia de hoje, numa linha "Vencidos em aberto" destacada; títulos de implantação (`Opening`) entram como os demais.
4. **Dado** uma recorrente ativa, **quando** a competência ainda não gerou título dentro do horizonte, **então** a ocorrência entra como saída prevista no vencimento calculado (mesma regra do job, FIN-02 CA 17 e 18), sem duplicar com o título já gerado da mesma competência.
5. **Dado** um título a pagar `Planned` com data estimada (`is_estimate`, CP do instalador, COM-06), **quando** abro o fluxo, **então** ele entra na data estimada com a `Tag` "Estimado" no detalhe do dia; `Planned` sem data não entra na série.
6. **Dado** um recebível `Planned` sem data (marco por evento, COM-04b), **quando** abro o fluxo, **então** ele não entra na série diária e aparece no quadro "A receber sem data (aguardando marco)" com o total e o link para os títulos.
7. **Dado** o horizonte, **quando** troco a visão para semanal ou mensal, **então** os valores são a soma dos dias de cada semana (segunda a domingo) ou mês e o saldo final de cada período é o saldo do último dia; os totais das três visões batem (teste).
8. **Dado** o período projetado, **quando** a tela calcula, **então** destaca o **dia de menor saldo** (data e valor) e, se o saldo projetado ficar negativo em algum dia, mostra o alerta crítico "O caixa fica negativo em {data}: {valor}." com o link "Ver o que vence nessa semana" (R-PRP-5).
9. **Dado** o filtro por conta, **quando** escolho uma conta, **então** o realizado e o saldo atual são os da conta, e o projetado aparece só no consolidado, com a nota "Títulos ainda não têm conta definida; a projeção é consolidada.".
10. **Dado** um dia da série, **quando** clico nele, **então** o painel lateral lista os títulos, recorrentes e transações que compõem o valor, cada um com link para o documento.
11. **Dado** folha prevista e pedidos de compra, **quando** a tela monta o fluxo, **então** eles entram pelos títulos `Planned` que as stories RH-02 e CMP-03 criarem, sem código novo aqui; enquanto não existem, a legenda informa "Folha e pedidos de compra entram quando os módulos forem ativados."
12. **Dado** 50 mil títulos e 20 mil transações no banco, **quando** abro o fluxo de 90 dias, **então** a resposta chega em menos de 2 s (índices parciais `(status, due_date)` e `(bank_account_id, date)`, ADR 0019 §19; medição no teste de integração).
13. **[Segurança]** **Dado** os filtros e a ordenação do fluxo, **quando** a consulta roda, **então** o SQL só usa parâmetros e a ordenação aceita só as colunas da allowlist (valor fora dela responde 422).
14. **Dado** o módulo `finance` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo ou sem `finance.view`, 403 `forbidden`. `RequireModule` e `perm:`.

## Regras de negócio e técnicas
- Cálculo no servidor, consulta de leitura no próprio Finance (Dapper pela conexão do `TenantDbContext`, parâmetros sempre); nenhum dado gravado (sem tabela nova).
- **Saldo atual** (`BankBalanceQuery`): `opening_balance` (ou 0) + Σ `amount` com `date` ≤ hoje, por conta e consolidado; reaproveitado pela FIN-04 e pelo DSH-01.
- **Dinheiro** `numeric(18,2)`; somas em `decimal`, nunca `double`; **datas** `date`, "hoje" pelo `BusinessTime.Today`.
- Títulos `Cancelled` e `Paid` não entram no projetado; `PartiallyPaid` entra pelo saldo; OP aprovada não muda a data (vale o vencimento).
- Horizonte padrão 90 dias, máximo 365.

## Permissões, eventos e tabelas
- **Permissões:** `finance.view`.
- **Eventos:** nenhum. **Tabelas:** nenhuma nova (leitura de `receivables`, `payables`, `recurring_expenses`, `financial_transactions`, `bank_accounts`); índices parciais conferidos.

## Telas e estados
- **Financeiro → Fluxo de caixa** (`/app/financeiro/fluxo-de-caixa`; grupo "Caixa e pagamentos"): h1 "Entradas, saídas e saldo, do realizado ao previsto."; filtros na URL (visão diária/semanal/mensal, horizonte 30/60/90 dias, conta); gráfico de linha do saldo (`Chart` da FND-09e, tokens `--chart-*`, realizado em traço contínuo e projetado tracejado, linha "hoje" e marcador do menor saldo) e tabela com período, entradas, saídas, saldo (`tabular-nums`, negativo em crítico); quadro "A receber sem data".
- **Estados:** carregando (skeleton do gráfico e da tabela); vazio ("Sem contas ou títulos ainda. Faça a implantação financeira para ver o fluxo." + "Implantação financeira"); erro com "Tentar de novo".
- **Propositivo:** o alerta do caixa negativo leva à lista de títulos da semana; o dia clicado abre o detalhe.
- **Acessibilidade:** o gráfico tem a tabela equivalente acessível por teclado e um resumo textual do menor saldo.
- **400 px:** a tabela mostra período, entradas, saídas e saldo; o gráfico ocupa a largura e o detalhe do dia abre em tela cheia.
- **Ajuda:** `Ajuda/financeiro-fluxo-de-caixa.md`; `<Dica>` em "Realizado", "Projetado", "Vencidos em aberto", "Estimado" e "A receber sem data".

## Plano de testes
- **Unidade:** agregação diária, semanal e mensal e menor saldo (CA 7, 8); ocorrências virtuais da recorrente (CA 4).
- **Integração (Testcontainers, obrigatória):** dataset fixo com valores esperados (CA 1–6, 9, 10); saldo atual sem futuras (CA 2); desempenho (CA 12); ordenação fora da allowlist (CA 13); três camadas (CA 14).
- **E2E (Playwright):** abrir o fluxo, trocar a visão e abrir um dia; tela a 400 px.

## Fora do escopo
- Cenário ajustado pela pontualidade do cliente e % de inadimplência: FIN-07 (Fase 4).
- Extrato por conta e transferências: FIN-04. DRE: FIN-08. Card do dashboard: DSH-01.

## Pontos em aberto
Decididos pelo PO em 20/10/2026 (seção 1.1, item 12): vencidos em aberto no dia de hoje; recebível de marco sem data fora da série; projeção só consolidada (conta prevista no título fica para a BNK-01). P-18 decidida (ADR 0021).

## Definition of Done
- [x] Critérios de aceite atendidos.
- [x] Testes de unidade, integração (Testcontainers, dataset fixo) e E2E passando.
- [x] Migration só com os índices parciais, se a FIN-01 e a FIN-02 não os criaram.
- [x] Permissões só do `ModuleCatalog`.
- [x] Tela com estados de carregando, vazio e erro.
- [x] Ajuda escrita; teste de cobertura passando.
- [x] Alerta acionável e detalhe do dia aplicados.
- [x] OpenAPI e cliente Orval regenerados.
- [x] Story marcada no backlog.

## Tarefas técnicas sugeridas
- [x] `CashFlowQuery` e `BankBalanceQuery` (Dapper) e `CashFlowCalculator` (Domain, puro, testável).
- [x] Endpoint `/api/v1/finance/cash-flow` e `/api/v1/finance/bank-accounts/balances`; tela com `Chart` e tabela; ajuda.

## Quality gates
- Agentes previstos: @dev, @qa, design (gráfico e acessibilidade).
- Gate: CA 1 (dataset fixo) e CA 2 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 20/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fase 2).

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

- **Correções feitas na validação (19/10/2026):** o "Ajustado" saiu para a FIN-07; folha e pedidos entram pelos títulos `Planned` das stories que os criam; desempenho com valor.
- **Correções da revalidação (20/10/2026):** a nova ordem (seção 4) põe esta story antes da FIN-03a e da FIN-04: a dependência delas saiu e a consulta de saldo atual passou para cá (CA 2); CP estimado do instalador (M13, CA 5); títulos de implantação (FIN-00); SQL com allowlist de ordenação (CA 13); gráfico com os tokens e o traço realizado × projetado da FND-09e; navegação (D12) e 400 px. CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma pendente.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 19/10/2026 | 0.1 | Story detalhada | River (SM) |
| 19/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 20/10/2026 | 0.3 | Nova ordem da fase (antes da FIN-03a e da FIN-04), consulta de saldo, CP estimado, ajustes da revisão pré-Fase 2; CAs renumerados | River (SM) |
| 20/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
| 20/10/2026 | 1.0 | Implementada. Desvios: consultas pelo EF (LINQ com agregação no banco e parâmetros), sem Dapper, porque o EF atendeu o CA 12 (50 mil títulos e 20 mil transações em menos de 2 s no teste); a `BankBalanceQuery` é o `CashFlowService.BankBalanceAsync`; o período começa no 1º dia do mês corrente (`from`, até um ano antes); a allowlist de ordenação vale no detalhe do dia (`kind`, `amount`, `-amount`, `description`); o alerta de caixa negativo abre o dia em que o saldo fica negativo ("Ver o que vence nesse dia"), porque a lista de títulos não filtra por semana; o h1 segue o padrão das telas do Financeiro ("Financeiro: Fluxo de caixa") com a frase da story logo abaixo; o `Chart` ganhou marcas verticais ("Hoje" e "Menor saldo") | @dev |
