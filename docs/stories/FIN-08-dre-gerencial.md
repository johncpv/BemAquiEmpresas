# FIN-08 — DRE gerencial mensal

| Campo | Valor |
|---|---|
| ID | FIN-08 |
| Título | Demonstração de resultado gerencial mês a mês pelos grupos da DRE do plano de contas: receita pela data da nota (antes das notas reais, pela emissão do título, com o rótulo "provisório"), sinal como adiantamento até a nota, despesa pela data do documento, subtotais, % sobre a receita líquida, coluna "Δ mês anterior", drill-down até o documento e o resumo de receita para o painel do dono; nasce aqui a `TreeTable` com coluna fixa e navegação por célula (D18) |
| Tamanho | G (re-estimada de M na primeira validação: mapeamento de juros, multas e descontos das baixas, receita provisória × pela nota com o sinal como adiantamento, drill-down por célula com dado protegido, a `TreeTable` analítica e o resumo para o painel; a visão por projeto saiu para a DSH-01b, mas o tamanho se mantém) |
| Status | Ready (pré-condição externa) |
| Requisitos | R-FIN-13 (DRE gerencial por mês) · R-FIN-1 (plano de contas) · PRD §5.14 (Faturamento do mês e acumulado do ano) · R-PRP-5 · PRD §6 (Desempenho) |
| Dependências | CAD-05a (plano com grupo da DRE), FIN-01 (recebíveis e baixas), FIN-02 (títulos a pagar e recorrentes, competência), FIN-04 (lançamentos bancários sem OP), FIN-00 (títulos `Opening` fora do DRE), COM-01b (marco de pagamento com gatilho Assinatura, que identifica o sinal), PRD-06b (romaneio e `IShipments`, para o sinal no modo provisório), CMP-03a (conta do título de compra em `purchasing.parameters`) |
| Bloqueia | DSH-01a (card Faturamento × meta), DSH-01b (resultado gerencial por projeto e "a faturar"), FIS-03a (troca a fonte da receita para a nota autorizada) |
| Ordem de execução | 3 (Marco 4.1 "O dinheiro do jeito que ele vai acontecer") |

## User story
Como **proprietário**, quero **ver mês a mês quanto a empresa vendeu, quanto custou produzir, quanto gastou para funcionar e quanto sobrou**, para **fechar o mês em menos de um dia e saber se o negócio dá lucro, sem esperar a contadora**.

## Contexto e referências
- PRD R-FIN-13; §1.2 ("fechamento financeiro mensal em menos de 1 dia de trabalho"); §3.3 (contabilidade completa fora do escopo: a contadora recebe exportações).
- **P-17 (decidida, 16/10/2026):** "competência, pela data da venda (receita na data da venda/emissão, despesa na data do documento)".
- **D4 da revisão pré-Fases 4 a 8 (proposta levada à contadora):** o equipamento sai em NF-e na expedição; a instalação, em NFS-e no aceite técnico; a parcela do contrato é só cobrança (boleto), nunca faturamento; a receita do DRE é a data da nota; o sinal fica como adiantamento até a nota. **D1 (nova):** a Fase 4 inteira vem antes do go-live; nota real (SEFAZ em produção) só a partir da GOL-01b. Por isso esta story nasce com a regra por título, marcada "provisório", e a FIS-03a troca a fonte para a nota.
- CAD-05a (Done): grupos da DRE (lista fixa): Receita bruta, Deduções, Custos, Despesas operacionais, Resultado financeiro, Outros resultados, Fora da DRE; seed v1 com 1.1 a 1.9, 2.1 a 2.6, 3.1 a 3.9, 4.1 e 5.x; desvios implementados: 1.3 e 3.5 em Resultado financeiro, 1.4 em Outros resultados, 2.5 em Deduções. `DreGroup` está em `Finance.Contracts`.
- FIN-00 e revisão pré-Fase 2 D2: títulos `Opening` ficam **fora do DRE por competência**.
- FIN-01: baixa com juros, multa e desconto (`receivable_settlements.interest`, `fine`, `discount`), "Quitar o saldo com desconto" (baixa de total 0); FIN-02: `competence` na recorrente; FIN-04: `bank_entries` com conta do plano obrigatória (tarifa, IOF, juros, rendimento).
- CMP-03a CA 19: o título do pedido usa a conta `2.1 Materiais` e a natureza `Material` (parâmetros em `purchasing.parameters`); com a P-17, a compra para estoque vira custo no mês da data do documento, mesmo antes do consumo.
- COM-01b: os marcos de pagamento têm evento-gatilho; o marco com gatilho **Assinatura** é o sinal (exemplo do PRD: 30% "Sinal" na assinatura).
- **ADR 0028** §1: o "Resultado gerencial por projeto" sai do DRE e vai para o módulo `Dashboard` (DSH-01b), com a nota "Não fecha com o DRE mensal: o projeto soma pelo consumo, o DRE pela data do documento."; §4: faturamento "provisório" até existirem notas reais. **ADR 0042** (parâmetros pré-configurados "A confirmar com a contadora", com "Marcar como conferido" auditado). **ADR 0036** (folha no financeiro: título com `payee_type`/`payee_id`, favorecido funcionário).
- **Revisão pré-Fases 4 a 8** (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`): D1, D4, D9, D18 (`TreeTable` com coluna fixa e navegação por célula antes do Marco 4.2, nesta story); tabela 3.1, pontos 10 a 14; §4.3 (drill-down de salários com nome só com `hr.view_sensitive`); §4.4 padrão 3 (sinal no rótulo, crítico só em resultado negativo, coluna "Δ mês anterior").

## Critérios de aceite
**Estrutura**
1. **Dado** `finance.view`, **quando** abro "DRE" (`/app/financeiro/dre`), **então** vejo as linhas na ordem: **Receita bruta**; **(−) Deduções**; **= Receita líquida**; **(−) Custos**; **= Lucro bruto** (com a margem bruta %); **(−) Despesas operacionais**; **= Resultado operacional**; **(±) Resultado financeiro**; **(±) Outros resultados**; **= Resultado do período**; cada grupo se abre nas contas do plano (árvore, sintéticas com subtotal); contas do grupo "Fora da DRE" não aparecem; abaixo do resultado, fora dele, a linha de memória **"Adiantamentos de clientes (sinal ainda sem nota)"** com o saldo no fim de cada mês (CA 5).
2. **Dado** o período (padrão: os 12 meses até o mês corrente; até 24 meses), **quando** a tela monta, **então** há uma coluna por mês, a coluna Total, a coluna "% da receita líquida" e a coluna **"Δ mês anterior"** (último mês do período − mês anterior, em valor e em %); o **sinal fica no rótulo** ("(−) Custos" mostra o valor positivo), os valores usam `tabular-nums` e o tom **crítico** aparece só em **resultado negativo** (Lucro bruto, Resultado operacional e Resultado do período), nunca numa linha de despesa.

**Receita (P-17 e D4)**
3. **Dado** o tenant **antes de existirem notas reais** (sem a data de corte fiscal `fiscal.project_tax_cutover_on`, que a GOL-01b preenche na primeira nota em produção), **quando** a receita é montada, **então** entram os recebíveis não `Cancelled`, não `Planned` e de origem diferente de `Opening`, pelo **valor** do título na sua conta, no mês da **data de emissão** (`issue_date`), exceto o sinal (CA 5); a linha Receita bruta, o cabeçalho e o resumo do CA 13 levam o chip **"Provisório"** com a `<Dica>` "Até a emissão de notas no sistema, a receita é a data de emissão dos títulos. Com as notas, passa a ser a data da nota."
4. **Dado** o tenant **com notas reais** (a partir da data de corte), **quando** a receita é montada, **então** a regra passa a ser a da FIS-03a e da FIS-06: a receita é o valor da nota autorizada (NF-e ou NFS-e), na data de emissão da nota, e os títulos de contrato passam a ser só cobrança; os meses antes da data de corte continuam pela regra do CA 3, com o chip "Provisório" só neles; esta story entrega a regra por título, o ponto de troca (`IRevenueRecognition` no Finance, com a implementação por título) e um teste de contrato que a FIS-03a reaproveita para a implementação pela nota.
5. **Dado** um recebível de **sinal** (título do marco de pagamento com gatilho Assinatura, COM-01b), **quando** a receita é montada, **então** ele não entra na receita na emissão: fica em "Adiantamentos de clientes" até a nota do projeto (com notas reais, a nota absorve o sinal); no modo provisório (CA 3), o sinal entra na receita no mês do primeiro romaneio `Shipped` do projeto (PRD-06b, `IShipments`), que é quando a nota do equipamento sai (D4), ou, em projeto sem romaneio, no mês de emissão do título do marco seguinte; o drill-down mostra "Sinal reconhecido na expedição de {data}" ou "Sinal reconhecido com o marco {nome}".

**Despesas e demais lançamentos (P-17)**
6. **Dado** títulos a pagar, **quando** entram na despesa, **então** entram os não `Cancelled`, não `Planned` e de origem diferente de `Opening`, pelo **valor bruto** (antes das retenções) na sua conta, no mês da **competência** quando houver (recorrente) ou da **data de emissão** (data do documento) nos demais; o título de compra para estoque entra na conta `2.1 Materiais` (CMP-03a) no mês da data do documento, mesmo antes do consumo, com a `<Dica>` em "Custos": "A compra entra aqui no mês da nota do fornecedor. No resultado por projeto, o material entra quando é baixado para a OS; por isso os dois não fecham."
7. **Dado** lançamentos bancários sem OP (FIN-04), **quando** entram, **então** entram pela data e pela conta do lançamento, com o sinal do tipo; lançamentos estornados e transferências entre contas não entram.
8. **Dado** baixas com juros, multa ou desconto, **quando** entram, **então** juros e multa recebidos vão para a conta do parâmetro `finance.dre_interest_income_account_id` (seed: 1.3 Receitas financeiras), descontos concedidos (inclusive "Quitar o saldo com desconto") para `finance.dre_discount_granted_account_id` (seed: 3.5 Financeiras), juros e multa pagos para `finance.dre_interest_expense_account_id` (seed: 3.5 Financeiras) e descontos obtidos para `finance.dre_discount_received_account_id` (seed: 1.3 Receitas financeiras), no mês da data da baixa; baixa estornada não entra; os quatro parâmetros nascem com o chip **"A confirmar com a contadora"** (ADR 0042, CA 16).

**Mudanças retroativas e contas a classificar**
9. **Dado** que não há fechamento de mês (o DRE é recalculado a cada leitura), **quando** abro o DRE, **então**, se desde a minha última consulta (`finance.dre_view_marks`, por usuário) algum documento com data num mês já mostrado foi lançado, cancelado ou estornado, a faixa informativa "Mudou desde a sua última consulta em {data}: {n} documentos em {meses}." aparece com "Ver o que mudou" (lista dos documentos com o mês e o efeito), e o mês afetado mostra o ícone "Mudou"; a marca da consulta é atualizada ao abrir.
10. **Dado** valores em contas "A classificar" (1.9 e 3.9), **quando** o DRE mostra o mês, **então** aparece o aviso "R$ {valor} em contas a classificar neste período." com o link para a lista dos títulos, para reclassificar (R-PRP-5).

**Dataset, tabela analítica e drill-down**
11. **Dado** o dataset fixo de teste (recebíveis de contrato com sinal e manuais, um `Opening`, um cancelado depois do mês, títulos a pagar de recorrente com competência, de instalador, de compra para estoque e manuais com retenção, lançamentos bancários, baixas com juros e desconto, um estorno e um romaneio que reconhece o sinal), **quando** abro o DRE de três meses, **então** cada linha, cada subtotal, a coluna "Δ mês anterior" e a linha de adiantamentos batem centavo a centavo com a planilha do teste.
12. **Dado** a tabela do DRE, **quando** navego, **então** ela é a `TreeTable` com a **coluna de nomes fixa** na rolagem horizontal e **navegação por célula** pelo teclado (setas movem a célula ativa, Home/End vão ao início e ao fim da linha, seta para a direita e para a esquerda na coluna de nomes abrem e fecham o grupo, Enter abre o drill-down; padrão `treegrid` com `aria-rowindex`, `aria-colindex` e foco visível); o componente é o mesmo que a PLN-04 (ocupação) e o espelho de ponto da Fase 6 reaproveitam.
13. **Dado** uma célula (conta × mês, ou grupo × mês), **quando** clico ou dou Enter, **então** o painel lateral lista os documentos que somam o valor (título, lançamento bancário, baixa ou sinal reconhecido: número, contraparte, data de competência, valor, link) e o total do painel é igual ao da célula (teste).
14. **[Segurança]** **Dado** uma célula com títulos de salário (favorecido funcionário, `payee_type = Employee`, ADR 0036, a partir da RH-02c), **quando** abro o drill-down, **então** com `hr.view_sensitive` vejo cada título com o funcionário; sem `hr.view_sensitive`, vejo uma linha agregada "Folha de {mês}: {n} funcionários, {valor}" sem nomes nem valores individuais, com o bloco "Dados protegidos"; o DTO sem a ação não tem nome nem valor por funcionário (teste de contrato, com títulos de teste até a RH-02c existir).

**Contrato para o painel do dono**
15. **Dado** o `IRevenueSummary` em `Finance.Contracts` (novo), **quando** o painel do dono chama `GrossRevenueAsync(from, to)` ou `GrossRevenueByProjectsAsync(projectIds, from, to)`, **então** recebe a receita bruta por mês (e por projeto) pela mesma regra dos CA 3 a 5, com a marca `IsProvisional` por mês, e o total é igual à linha Receita bruta do DRE no mesmo período (teste que compara as duas saídas); a DSH-01a usa o mensal e a DSH-01b, o por projeto.

**Parâmetros, desempenho, permissões e segurança**
16. **Dado** os parâmetros de conta do CA 8, **quando** alguém com `settings.edit` troca a conta em Configurações → Parâmetros → Financeiro, **então** só aceita conta analítica ativa do grupo certo (`IChartOfAccounts`), grava com auditoria (antes e depois, ADR 0016 M6) e o DRE passa a usar a nova conta na próxima leitura; cada parâmetro mostra o chip "A confirmar com a contadora" até alguém com `settings.edit` clicar "Marcar como conferido" (quem e quando, auditado; trocar a conta depois volta o chip, ADR 0042).
17. **Dado** 50 mil títulos a receber, 50 mil a pagar, 100 mil baixas e 24 meses, **quando** abro o DRE mensal, **então** a resposta chega em menos de 2 s (agregação no banco por conta e mês, índices por data de emissão e competência).
18. **Dado** o módulo `finance` não liberado, **quando** chamo, **então** 403 `module_not_released` e a tela `ModuleNotReleased`; sem grupo ou sem `finance.view`, 403 `forbidden`. `RequireModule` e `perm:`; SQL só com parâmetros e ordenação por allowlist (422 fora dela); IDOR nas células (`/finance/dre/cells` aceita só conta do plano do tenant e mês do período).

## Regras de negócio e técnicas
- **Cálculo na leitura**, sem tabela de saldos: consulta agregada por conta e mês no Finance (Dapper ou EF com agregação no banco, sempre com parâmetros, ADR 0006), montagem da árvore e dos subtotais no `DreBuilder` (Domain, puro).
- **Receita** (ponto 10): por título com o rótulo "Provisório" até a data de corte; pela nota depois dela (FIS-03a/FIS-06); o sinal fica em adiantamentos até a nota (D4) e, no provisório, entra na expedição.
- **Sinal no rótulo e crítico** (padrão 3): valores mostrados positivos com o sinal no nome da linha; crítico só em resultado negativo.
- **Receita líquida** = Receita bruta − Deduções; **Lucro bruto** = Receita líquida − Custos; **Resultado operacional** = Lucro bruto − Despesas operacionais; **Resultado do período** = Resultado operacional + Resultado financeiro + Outros resultados.
- **Títulos `Opening` e transferências** fora (FIN-00, D2). **Planned** fora (ainda não é documento).
- **Sem fechamento de período** (ponto 13): aviso "Mudou desde a sua última consulta".
- **Compra para estoque** (ponto 14): conta `2.1` pela data do documento, com a `<Dica>` da diferença para o resultado por projeto.
- **Dinheiro** `numeric(18,2)`; mês pela data local (America/Sao_Paulo); "hoje" pelo `BusinessTime.Today`.

## Permissões, eventos e tabelas
- **Permissões:** `finance.view` (DRE); `hr.view_sensitive` (nomes no drill-down de salários); `settings.edit` (contas do parâmetro e "Marcar como conferido").
- **Eventos:** nenhum.
- **Contratos:** novo `IRevenueSummary` (`Finance.Contracts`: `GrossRevenueAsync`, `GrossRevenueByProjectsAsync`); `IRevenueRecognition` (Finance, ponto de troca da regra da receita); usa `IChartOfAccounts`, `IParties`, `IShipments` (romaneio do projeto, PRD-06b).
- **Tabelas:** `finance.dre_view_marks` (nova: `user_id` único, `viewed_at` `timestamptz`; fora da auditoria); chaves novas em `finance.parameters`: `finance.dre_interest_income_account_id`, `finance.dre_interest_expense_account_id`, `finance.dre_discount_granted_account_id`, `finance.dre_discount_received_account_id`, com a marca de conferência da ADR 0042; índices de apoio em `receivables (issue_date)` e `payables (competence, issue_date)` se a medição pedir.

## Telas e estados
- **Padrões comuns das Fases 4 a 8:** 3 (tabela analítica: coluna fixa, navegação por célula, drill-down em painel lateral com total igual ao da célula; sinal no rótulo, crítico só em resultado negativo, coluna "Δ mês anterior") e 8 (bloco "Dados protegidos" no drill-down de salários).
- **Financeiro → DRE** (`/app/financeiro/dre`; grupo "Caixa e pagamentos" no `modules.ts`): h1 "Quanto a empresa vendeu, gastou e lucrou em cada mês."; seletor de período; chip "Provisório"; faixa "Mudou desde a sua última consulta"; `TreeTable` com a coluna de nomes fixa; painel lateral do drill-down; linha de memória dos adiantamentos.
- **Componentes (D18):** `TreeTable` ganha a coluna fixa e a navegação por célula (CA 12).
- **Estados:** carregando (skeleton da tabela); vazio ("Sem lançamentos no período. Os títulos e lançamentos entram aqui pela data de competência." + "Ver contas a receber"); erro com "Tentar de novo".
- **Propositivo:** aviso de contas a classificar com link; "Mudou desde" leva aos documentos alterados; da célula, o documento de origem; parâmetro não conferido leva a Configurações → Parâmetros.
- **400 px:** um mês por vez (seletor de mês), com as colunas Mês, Δ mês anterior e %; a árvore recolhida nos grupos; o drill-down em tela cheia; botões de `ErrorState`/`EmptyState` com 44 px.
- **Ajuda:** `Ajuda/financeiro-dre.md` (rota `/app/financeiro/dre`: regra da receita, "Provisório", sinal como adiantamento, compra para estoque, mudanças retroativas) e atualizar `Ajuda/configuracoes-parametros.md` (contas do DRE e "A confirmar com a contadora"); `<Dica>` em "Competência", "Provisório", "Adiantamentos de clientes", "Receita líquida", "Lucro bruto", "Resultado financeiro", "A classificar", "Custos" e "Δ mês anterior".

## Plano de testes
- **Unidade:** `DreBuilder` (subtotais, sinal no rótulo, crítico só em resultado, Δ mês anterior, árvore, exclusão de Fora da DRE) e regras de entrada por documento (CA 3–8), inclusive o sinal (CA 5), com tabela de casos.
- **Integração (Testcontainers, obrigatória):** dataset fixo (CA 11); provisório antes da data de corte e troca da regra no contrato `IRevenueRecognition` com uma implementação de teste (CA 3, 4); sinal com e sem romaneio (CA 5); "Mudou desde a sua última consulta" (CA 9); drill-down igual à célula (CA 13); salários com e sem `hr.view_sensitive` (CA 14); `IRevenueSummary` igual à linha, mensal e por projeto (CA 15); parâmetros com validação, auditoria e "Marcar como conferido" (CA 16); desempenho (CA 17); três camadas, allowlist e IDOR (CA 18).
- **Front (Vitest):** `TreeTable` com coluna fixa, navegação por célula, abrir e fechar pelo teclado, Enter no drill-down (CA 12).
- **E2E (Playwright):** abrir o DRE, expandir um grupo, navegar pelo teclado até uma célula, abrir o drill-down e ir ao título; ver o chip "Provisório"; tela a 400 px.

## Fora do escopo
- Exportação para a contadora (R-FIN-14, P-06).
- Fechamento de mês com trava de lançamentos retroativos.
- **Resultado gerencial por projeto:** DSH-01b (módulo `Dashboard`, ADR 0028).
- Receita pela nota autorizada (implementação do `IRevenueRecognition` pela nota): FIS-03a (NF-e) e FIS-06 (NFS-e).
- Rateio de despesas indiretas por projeto e DRE por centro de custo: quando o PO pedir.

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fases 4 a 8, tabela 3.1, pontos 10–14):
1. Receita pela data da nota; antes das notas reais, a data de emissão do título com o rótulo "provisório"; sinal como adiantamento até a nota (D4; CA 3, 4, 5, 15).
2. Contas de juros, multa e desconto do CA 8, pré-configuradas como parâmetro editável com o chip "A confirmar com a contadora" (CA 8, 16).
3. "Resultado gerencial por projeto" sai do DRE e vai para o `Dashboard`, na DSH-01b, com a nota de que não fecha com o DRE mensal (D9, ADR 0028).
4. Sem fechamento de período; aviso "Mudou desde a sua última consulta" (CA 9).
5. Compra para estoque na conta 2.1 pela data do documento, com a `<Dica>` da diferença para o resultado por projeto (CA 6).

Decisões da revisão aplicadas: D1 (Fase 4 antes do go-live: a receita nasce provisória e a FIS-03a troca a fonte); D18 (`TreeTable` com coluna fixa e navegação por célula, CA 12); §4.3 (drill-down de salários com nome só com `hr.view_sensitive`, CA 14); §4.4 padrão 3 (sinal no rótulo, crítico só em resultado negativo, "Δ mês anterior", CA 2); ADR 0042 (parâmetros "A confirmar com a contadora", CA 16).

**Pré-condição externa:** a contadora confere as contas de juros, multa e desconto (CA 8), a conta `2.1` da compra para estoque (CMP-03a) e a regra da venda mista (D4). Os valores vêm pré-configurados e editáveis, com o chip "A confirmar com a contadora"; não bloqueia o `Ready` nem a entrega.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers, dataset fixo), front (Vitest), desempenho e E2E passando.
- [ ] Migration não destrutiva de `finance.dre_view_marks`; seeds das chaves de parâmetro idempotentes (também nos tenants existentes), com a marca "A confirmar com a contadora".
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Tela com estados e 400 px; `TreeTable` com coluna fixa e navegação por célula, sem cor literal.
- [ ] Ajuda da tela e dos parâmetros; teste de cobertura passando.
- [ ] Alertas acionáveis (a classificar, mudou desde, parâmetro não conferido) e drill-down.
- [ ] Auditoria ligada nos parâmetros e no "Marcar como conferido".
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` (chaves e `dre_view_marks`) e `docs/04-fluxos-estados.md` (regra da DRE, nova §3.8, com a receita provisória e o sinal) atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `DreQuery` (agregação por conta e mês) e `DreBuilder` (Domain).
- [ ] `IRevenueRecognition` com a implementação por título e o sinal; `IRevenueSummary` (mensal e por projeto).
- [ ] Endpoints `/api/v1/finance/dre`, `/dre/cells`, `/dre/changes`; parâmetros com a marca de conferência.
- [ ] `TreeTable` com coluna fixa e navegação por célula; tela e ajuda.

## Quality gates
- Agentes previstos: @dev, @qa, @data-engineer (agregação), design (`TreeTable` analítica e 400 px), cybersecurity (drill-down de salários).
- Gate: CA 3, 5, 6, 11, 12, 13, 14 e 15 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fases 4 a 8).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: sai a DSH-02; entram COM-01b e PRD-06b; bloqueia DSH-01b e FIS-03a) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** re-estimada de M para G; a data da receita antes da NF-e, o mapeamento de juros e descontos, a definição do DRE por projeto e a compra para estoque ficaram como pontos em aberto com a contadora, sem contrariar a P-17; títulos `Opening` fora (D2); resumo de receita como contrato para o painel.
- **Correções da revalidação (21/10/2026):** pontos 10 a 14 decididos; receita provisória por título até as notas reais, com o ponto de troca para a FIS-03a e o sinal como adiantamento (D4, D1; CA 3 a 5); a aba "Por projeto" saiu (D9, ADR 0028; o resultado por projeto é da DSH-01b) e o `IRevenueSummary` ganhou o resumo por projeto (CA 15); sem fechamento, com "Mudou desde a sua última consulta" (CA 9); compra para estoque na 2.1 com a `<Dica>` (CA 6); sinal no rótulo, crítico só em resultado negativo e "Δ mês anterior" (padrão 3, CA 2); `TreeTable` com coluna fixa e navegação por célula (D18, CA 12); drill-down de salários com `hr.view_sensitive` (§4.3, CA 14); parâmetros "A confirmar com a contadora" (ADR 0042, CA 16); título da story sem "e por projeto"; CAs renumerados (18).
- **Pré-condições para `Ready`:** nenhuma de decisão de story. Externa: conferência das contas pela contadora (valores pré-configurados e editáveis).
- **Conferência do fechamento (21/10/2026, amostragem do PO):** resultado por projeto fora da story (DSH-01b) e receita provisória até as notas; checklist refeita, sem mudança de resultado.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready (pré-condição externa)`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada; re-estimada de M para G | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (D1, D4, D9, D18, tabela 3.1 pontos 10–14, §4.3, §4.4 padrões 3 e 8; ADR 0028 e 0042); visão por projeto movida para a DSH-01b; ordem mantida (Marco 4.1, ordem 3); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
