# FIN-08 — DRE gerencial mensal e por projeto

| Campo | Valor |
|---|---|
| ID | FIN-08 |
| Título | Demonstração de resultado gerencial por competência (P-17), mês a mês e por projeto, pelos grupos da DRE do plano de contas, com subtotais, % sobre a receita líquida, drill-down até o documento e o resumo de receita para o painel do dono |
| Tamanho | G (re-estimada de M: duas visões com regras próprias, o mapeamento de juros, multas e descontos das baixas, o drill-down por célula e o resumo para o painel) |
| Status | Draft |
| Requisitos | R-FIN-13 (DRE gerencial por mês e por projeto) · R-FIN-1 (plano de contas e vínculo a projeto) · PRD §5.14 (Faturamento do mês e acumulado do ano) · R-PRP-5 · PRD §6 (Desempenho) |
| Dependências | CAD-05a (plano com grupo da DRE), FIN-01 (recebíveis e baixas), FIN-02 (títulos a pagar e recorrentes, competência), FIN-04 (lançamentos bancários sem OP), FIN-00 (títulos `Opening` fora do DRE), DSH-02 (`IProjectMargins` para a visão por projeto), CMP-03a (conta do título do pedido, ponto 2 de lá) |
| Bloqueia | DSH-01a (card Faturamento × meta) |
| Ordem de execução | 3 (Marco 4.1 "O dinheiro do jeito que ele vai acontecer") |

## User story
Como **proprietário**, quero **ver mês a mês quanto a empresa vendeu, quanto custou produzir, quanto gastou para funcionar e quanto sobrou, e o mesmo para cada projeto**, para **fechar o mês em menos de um dia e saber se o negócio dá lucro, sem esperar a contadora**.

## Contexto e referências
- PRD R-FIN-13; §1.2 ("fechamento financeiro mensal em menos de 1 dia de trabalho"); §3.3 (contabilidade completa fora do escopo: a contadora recebe exportações).
- **P-17 (decidida, 16/10/2026):** "competência, pela data da venda (receita na data da venda/emissão, despesa na data do documento)".
- CAD-05a (Done): grupos da DRE (lista fixa): Receita bruta, Deduções, Custos, Despesas operacionais, Resultado financeiro, Outros resultados, Fora da DRE; seed v1 com 1.1 a 1.9, 2.1 a 2.6, 3.1 a 3.9, 4.1 e 5.x; desvios implementados: 1.3 e 3.5 em Resultado financeiro, 1.4 em Outros resultados, 2.5 em Deduções. `DreGroup` está em `Finance.Contracts`.
- FIN-00 e revisão pré-Fase 2 D2: títulos `Opening` ficam **fora do DRE por competência**. `03-modelo-dados` `customer_payment_stats`: "Os títulos `Opening` também ficam fora do DRE por competência (FIN-08)".
- FIN-01: baixa com juros, multa e desconto (`receivable_settlements.interest`, `fine`, `discount`), "Quitar o saldo com desconto" (baixa de total 0); FIN-02: `competence` na recorrente; FIN-04: `bank_entries` com conta do plano obrigatória (tarifa, IOF, juros, rendimento).
- CMP-03a ponto 2 (Fase 3, em aberto): conta e natureza do título do pedido (proposta `2.1 Materiais`; com a contadora, compra para estoque pode ir para conta de estoque fora do DRE).

## Critérios de aceite
**Estrutura**
1. **Dado** `finance.view`, **quando** abro "DRE" (`/app/financeiro/dre`, aba Mensal, padrão), **então** vejo as linhas na ordem: **Receita bruta**; (−) **Deduções**; = **Receita líquida**; (−) **Custos**; = **Lucro bruto** (com a margem bruta %); (−) **Despesas operacionais**; = **Resultado operacional**; ± **Resultado financeiro**; ± **Outros resultados**; = **Resultado do período**; cada grupo se abre nas contas do plano (árvore, sintéticas com subtotal); contas do grupo "Fora da DRE" não aparecem.
2. **Dado** o período (padrão: os 12 meses até o mês corrente; até 24 meses), **quando** a tela monta, **então** há uma coluna por mês, a coluna Total e a coluna "% da receita líquida"; valores negativos em crítico, `tabular-nums`.

**Regras de competência (P-17)**
3. **Dado** recebíveis, **quando** entram na receita, **então** entram os títulos não `Cancelled`, não `Planned` e de origem diferente de `Opening`, pelo **valor** do título na sua conta, no mês da **data de emissão** (`issue_date`, proposta para "data da venda/emissão" até a Fase 5, Pontos em aberto 1).
4. **Dado** títulos a pagar, **quando** entram na despesa, **então** entram os não `Cancelled`, não `Planned` e de origem diferente de `Opening`, pelo **valor bruto** (antes das retenções) na sua conta, no mês da **competência** quando houver (recorrente) ou da **data de emissão** (data do documento) nos demais.
5. **Dado** lançamentos bancários sem OP (FIN-04), **quando** entram, **então** entram pela data e pela conta do lançamento, com o sinal do tipo; lançamentos estornados e transferências entre contas não entram.
6. **Dado** baixas com juros, multa ou desconto, **quando** entram, **então** juros e multa recebidos vão para a conta do parâmetro `finance.dre_interest_income_account_id` (seed: 1.3 Receitas financeiras), descontos concedidos (inclusive "Quitar o saldo com desconto") para `finance.dre_discount_granted_account_id` (seed: 3.5 Financeiras), e juros, multa e desconto nas baixas de títulos a pagar para as contas equivalentes de despesa e receita financeira, no mês da data da baixa; baixa estornada não entra (proposta, Pontos em aberto 2).
7. **Dado** um título cancelado depois do fim do mês, **quando** abro o DRE, **então** ele sai do mês de origem (o DRE é recalculado a cada leitura, sem fechamento de mês no MVP) e a linha do mês mostra o ícone "Mudou desde {data}" quando houve cancelamento ou estorno com data posterior ao fim do mês (proposta, Pontos em aberto 4).
8. **Dado** valores em contas "A classificar" (1.9 e 3.9), **quando** o DRE mostra o mês, **então** aparece o aviso "R$ {valor} em contas a classificar neste período." com o link para a lista dos títulos, para reclassificar (R-PRP-5).

**Dataset e drill-down**
9. **Dado** o dataset fixo de teste (recebíveis de contrato e manuais, um `Opening`, um cancelado, títulos a pagar de recorrente com competência, de instalador e manuais com retenção, lançamentos bancários, baixas com juros e desconto, e um estorno), **quando** abro o DRE de três meses, **então** cada linha e cada subtotal batem centavo a centavo com a planilha do teste.
10. **Dado** uma célula (conta × mês, ou grupo × mês), **quando** clico, **então** o painel lateral lista os documentos que somam o valor (título, lançamento bancário ou baixa: número, contraparte, data de competência, valor, link) e o total do painel é igual ao da célula (teste).

**Por projeto**
11. **Dado** `finance.view` e `projects.view_cost`, **quando** abro a aba "Por projeto", **então** vejo uma linha por projeto (filtros por situação, cliente e período de assinatura) com **Receita** (recebíveis do projeto pela regra do CA 3, no período), **Custos diretos** por natureza pelo realizado do DSH-02 (`IProjectMargins`) e **Resultado** = receita − custos diretos, com a margem %; e ao abrir um projeto, a DRE dele mês a mês com as mesmas linhas (proposta, Pontos em aberto 3).
12. **Dado** um usuário com `finance.view` sem `projects.view_cost`, **quando** abre o DRE, **então** a aba "Por projeto" não aparece e a API dela responde 403; a aba Mensal continua visível.

**Contrato para o painel do dono**
13. **Dado** o `IRevenueSummary` em `Finance.Contracts` (novo), **quando** o painel do dono chama `GrossRevenueAsync(from, to)`, **então** recebe a receita bruta por mês pela mesma regra do CA 3 e o total é igual à linha Receita bruta do DRE no mesmo período (teste que compara as duas saídas).

**Desempenho, permissões e segurança**
14. **Dado** 50 mil títulos a receber, 50 mil a pagar, 100 mil baixas e 24 meses, **quando** abro o DRE mensal, **então** a resposta chega em menos de 2 s (agregação no banco por conta e mês, índices por data de emissão e competência), e a aba Por projeto com 5 mil projetos em menos de 2 s.
15. **Dado** os parâmetros de conta do CA 6, **quando** alguém com `settings.edit` troca a conta em Configurações → Parâmetros → Financeiro, **então** só aceita conta analítica ativa do tipo certo (`IChartOfAccounts`), grava com auditoria (ADR 0016 M6) e o DRE passa a usar a nova conta na próxima leitura.
16. **Dado** o módulo `finance` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo ou sem `finance.view`, 403 `forbidden`. `RequireModule` e `perm:`; SQL só com parâmetros e ordenação por allowlist (422 fora dela); IDOR em `/finance/dre/projects/{id}`.

## Regras de negócio e técnicas
- **Cálculo na leitura**, sem tabela de saldos: consulta agregada por conta e mês no Finance (Dapper ou EF com agregação no banco, sempre com parâmetros, ADR 0006), montagem da árvore e dos subtotais no `DreBuilder` (Domain, puro).
- **Sinal:** receitas positivas e despesas negativas no resultado; deduções e custos mostrados com "(−)".
- **Receita líquida** = Receita bruta − Deduções; **Lucro bruto** = Receita líquida − Custos; **Resultado operacional** = Lucro bruto − Despesas operacionais; **Resultado do período** = Resultado operacional + Resultado financeiro + Outros resultados.
- **Títulos `Opening` e transferências** fora (FIN-00, D2). **Planned** fora (ainda não é documento).
- **Dinheiro** `numeric(18,2)`; mês pela data local (America/Sao_Paulo); "hoje" pelo `BusinessTime.Today`.

## Permissões, eventos e tabelas
- **Permissões:** `finance.view` (DRE mensal); `finance.view` + `projects.view_cost` (por projeto); `settings.edit` (contas do parâmetro).
- **Eventos:** nenhum.
- **Contratos:** novo `IRevenueSummary` (`Finance.Contracts`); usa `IProjectMargins` (DSH-02), `IProjects`, `IChartOfAccounts`, `IParties`.
- **Tabelas:** nenhuma nova; chaves novas em `finance.parameters` (**propostas**): `finance.dre_interest_income_account_id`, `finance.dre_interest_expense_account_id`, `finance.dre_discount_granted_account_id`, `finance.dre_discount_received_account_id`; índices de apoio em `receivables (issue_date)` e `payables (competence, issue_date)` se a medição pedir.

## Telas e estados
- **Financeiro → DRE** (`/app/financeiro/dre`; grupo "Caixa e pagamentos" no `modules.ts`): h1 "Quanto a empresa vendeu, gastou e lucrou em cada mês."; abas na URL Mensal (padrão) e Por projeto; seletor de período; `TreeTable` com colunas de mês fixando a coluna de nomes; painel lateral do drill-down.
- **Estados:** carregando (skeleton da tabela); vazio ("Sem lançamentos no período. Os títulos e lançamentos entram aqui pela data de competência." + "Ver contas a receber"); erro com "Tentar de novo".
- **Propositivo:** aviso de contas a classificar com link; "Mudou desde {data}" leva aos documentos alterados; da célula, o documento de origem.
- **400 px:** um mês por vez (seletor de mês), com as colunas Mês, Total e %; a árvore recolhida nos grupos; o drill-down em tela cheia.
- **Ajuda:** `Ajuda/financeiro-dre.md` (abas `mensal` com `padrao: true` e `por-projeto`) e atualizar `Ajuda/configuracoes-parametros.md`; `<Dica>` em "Competência", "Receita líquida", "Lucro bruto", "Resultado financeiro", "A classificar" e "Custos diretos".

## Plano de testes
- **Unidade:** `DreBuilder` (subtotais, sinais, árvore, exclusão de Fora da DRE) e regras de entrada por documento (CA 3–7), com tabela de casos.
- **Integração (Testcontainers, obrigatória):** dataset fixo (CA 9); drill-down igual à célula (CA 10); por projeto com e sem `projects.view_cost` (CA 11, 12); `IRevenueSummary` igual à linha (CA 13); desempenho (CA 14); parâmetros com validação e auditoria (CA 15); três camadas, allowlist e IDOR (CA 16).
- **E2E (Playwright):** abrir o DRE, expandir um grupo, abrir a célula e ir ao título; aba Por projeto; tela a 400 px.

## Fora do escopo
- Exportação para a contadora (R-FIN-14, P-06).
- Fechamento de mês com trava de lançamentos retroativos.
- Rateio de despesas indiretas por projeto e por centro de custo (DRE por centro de custo fica para quando o PO pedir).
- Impostos pela NF autorizada: Fase 5.

## Pontos em aberto
1. **Data da receita antes da NF-e** (PO/contadora): P-17 diz "data da venda/emissão". Proposta: `issue_date` do título a receber até a FIS-03; depois, a data de emissão da NF-e para os títulos faturados. Alternativa: a data de assinatura do contrato para todo o valor contratado (reconhece a venda inteira no mês do contrato).
2. **Juros, multas e descontos das baixas** (contadora): proposta do CA 6 (contas por parâmetro, mês da baixa). Alternativa: desconto concedido como dedução da receita.
3. **DRE por projeto** (PO): proposta: receita por competência × custos diretos pelo realizado do DSH-02 (material baixado e horas apontadas, que não são despesa do Finance). Alternativa: só os títulos do Finance com o projeto (receitas e despesas), sem os custos apropriados.
4. **Cancelamento depois do mês** (PO): proposta: o DRE é recalculado, sem fechamento, com o aviso "Mudou desde". Alternativa: fechamento mensal com trava (fica fora do MVP).
5. **Compra para estoque** (contadora, herdado do ponto 2 da CMP-03a): se o título do pedido for para `2.1 Materiais`, a compra vira custo no mês da NF, mesmo sem consumo; se for para uma conta de estoque fora do DRE, o custo só aparece pelo consumo (que hoje só existe no realizado do projeto). Decisão necessária antes do Marco 4.1.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers, dataset fixo), desempenho e E2E passando.
- [ ] Sem migration de tabela; seeds das chaves de parâmetro idempotentes (também nos tenants existentes).
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Tela com estados e 400 px.
- [ ] Ajuda da tela e das abas; teste de cobertura passando.
- [ ] Alertas acionáveis (a classificar, mudou desde) e drill-down.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` (chaves) e `docs/04-fluxos-estados.md` (regra da DRE, nova §3.8) atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `DreQuery` (agregação por conta e mês) e `DreBuilder` (Domain).
- [ ] `IRevenueSummary`; visão por projeto com `IProjectMargins`.
- [ ] Endpoints `/api/v1/finance/dre`, `/dre/cells`, `/dre/projects`, `/dre/projects/{id}`; parâmetros; tela e ajuda.

## Quality gates
- Agentes previstos: @dev, @qa, @data-engineer (agregação), design (tabela larga a 400 px).
- Gate: CA 3, 4, 9, 10 e 13 obrigatórios.

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

- **Correções feitas na validação:** re-estimada de M para G; a data da receita antes da NF-e, o mapeamento de juros e descontos, a definição do DRE por projeto e a compra para estoque ficaram como pontos em aberto com a contadora, sem contrariar a P-17; títulos `Opening` fora (D2); resumo de receita como contrato para o painel (CA 13).
- **Pré-condições para `Ready`:** pontos em aberto 1, 3 e 5.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada; re-estimada de M para G | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
