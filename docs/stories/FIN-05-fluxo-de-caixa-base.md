# FIN-05 — Fluxo de caixa base: realizado + projetado

| Campo | Valor |
|---|---|
| ID | FIN-05 |
| Título | Fluxo de caixa diário, semanal e mensal, consolidado ou por conta: realizado pelas transações e projetado pelos títulos em aberto e previstos e pelas recorrentes futuras, com o dia de menor saldo |
| Tamanho | M |
| Status | Draft |
| Requisitos | R-FIN-10 (realizado + projetado, diário/semanal/mensal; base = títulos em aberto + recorrentes + pedidos de compra + folha prevista) · R-PRP-5 · PRD §6 (Desempenho) |
| Dependências | FIN-01, FIN-02, FIN-03a, FIN-04 |
| Bloqueia | FIN-07 (cenário ajustado), DSH-01 (card de caixa), FIN-08 |
| Ordem de execução | 5 (Marco 2.1 — fecha o "caixa projetado" do PRD §7) |

## User story
Como **proprietário**, quero **ver quanto entra, quanto sai e qual será o saldo dia a dia nas próximas semanas, com o pior dia destacado**, para **antecipar falta de caixa antes que ela aconteça**.

## Contexto e referências
- PRD R-FIN-10; `04-fluxos-estados` §3.5 **Base** ("saldo atual + receivables em aberto/planejados (vencimento original) − payables em aberto/planejados − recorrentes futuros − folha prevista"). O **Ajustado** é a FIN-07 (Fase 4).
- Protótipo: painel "Fluxo de caixa semanal" (semana, entradas previstas, saídas previstas, saldo base, saldo ajustado, destaques). Nesta story a coluna "Saldo ajustado" não aparece (FIN-07).
- ADR 0006 (consultas pesadas: Dapper pela conexão do `TenantDbContext`, views comuns; materialized view só se a medição pedir).

## Critérios de aceite
1. **Dado** o dataset fixo de teste (2 contas, saldo inicial, 6 transações realizadas, 5 títulos a receber em aberto, 4 a pagar em aberto, 1 recorrente de R$ 3.000,00 todo dia 10, 1 recebível `Planned` sem data), **quando** abro o fluxo diário de hoje até 90 dias, **então** cada dia mostra entradas previstas, saídas previstas e saldo projetado iguais aos valores esperados do dataset, centavo a centavo (CA do backlog).
2. **Dado** o fluxo, **quando** abro, **então** o saldo de partida é o saldo atual consolidado das contas ativas (FIN-04 CA 4), os dias passados do período mostram o **realizado** (transações) e os dias de hoje em diante mostram o **projetado**; a linha "hoje" separa os dois.
3. **Dado** títulos em aberto, **quando** projetados, **então** entram pelo **saldo** (recebíveis: `amount − paid_amount`; pagáveis: líquido em aberto, FIN-02 CA 4) na data de vencimento original; títulos vencidos e ainda em aberto entram no dia de hoje, numa linha "Vencidos em aberto" destacada (ver Pontos em aberto 1).
4. **Dado** uma recorrente ativa, **quando** a competência ainda não gerou título dentro do horizonte, **então** a ocorrência entra como saída prevista no vencimento calculado (mesma regra do job, FIN-02 CA 13 e 14), sem duplicar com o título já gerado da mesma competência.
5. **Dado** um recebível `Planned` sem data (marco por evento, COM-04b), **quando** abro o fluxo, **então** ele não entra na série diária e aparece no quadro "A receber sem data (aguardando marco)" com o total e o link para os títulos (ver Pontos em aberto 2).
6. **Dado** o horizonte, **quando** troco a visão para semanal ou mensal, **então** os valores são a soma dos dias de cada semana (segunda a domingo) ou mês e o saldo final de cada período é o saldo do último dia; os totais das três visões batem (teste).
7. **Dado** o período projetado, **quando** a tela calcula, **então** destaca o **dia de menor saldo** (data e valor) e, se o saldo projetado ficar negativo em algum dia, mostra o alerta crítico "O caixa fica negativo em {data}: {valor}." com o link "Ver o que vence nessa semana" (R-PRP-5).
8. **Dado** o filtro por conta, **quando** escolho uma conta, **então** o realizado e o saldo atual são os da conta, e o projetado aparece só no consolidado, com a nota "Títulos ainda não têm conta definida; a projeção é consolidada." (ver Pontos em aberto 3).
9. **Dado** um dia da série, **quando** clico nele, **então** o painel lateral lista os títulos, recorrentes e transações que compõem o valor, cada um com link para o documento.
10. **Dado** folha prevista e pedidos de compra, **quando** a tela monta o fluxo, **então** eles entram pelos títulos `Planned` que as stories RH-02 e CMP-03 criarem, sem código novo aqui; enquanto não existem, a legenda informa "Folha e pedidos de compra entram quando os módulos forem ativados."
11. **Dado** 50 mil títulos e 20 mil transações no banco, **quando** abro o fluxo de 90 dias, **então** a resposta chega em menos de 2 s (índices em `due_date`, `status` e `date`; medição no teste de integração).
12. **Dado** o módulo `finance` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo ou sem `finance.view`, 403 `forbidden`. `RequireModule` e `perm:`.

## Regras de negócio e técnicas
- Cálculo no servidor, consulta de leitura no próprio Finance (Dapper pela conexão do `TenantDbContext`, parâmetros sempre); nenhum dado gravado (sem tabela nova).
- **Dinheiro** `numeric(18,2)`; somas em `decimal`, nunca `double`; **datas** `date`, "hoje" pelo `BusinessTime.Today`.
- Títulos `Cancelled` e `Paid` não entram no projetado; `PartiallyPaid` entra pelo saldo; OP aprovada não muda a data (vale o vencimento).
- Horizonte padrão 90 dias, máximo 365.

## Permissões, eventos e tabelas
- **Permissões:** `finance.view`.
- **Eventos:** nenhum. **Tabelas:** nenhuma nova (leitura de `receivables`, `payables`, `recurring_expenses`, `financial_transactions`, `bank_accounts`).

## Telas e estados
- **Financeiro → Fluxo de caixa** (`/app/financeiro/fluxo-de-caixa`): h1 "Entradas, saídas e saldo, do realizado ao previsto."; filtros na URL (visão diária/semanal/mensal, horizonte 30/60/90 dias, conta); gráfico de linha do saldo (Recharts, cores dos tokens, linha "hoje" e marcador do menor saldo) e tabela com período, entradas, saídas, saldo (`tabular-nums`, negativo em crítico); quadro "A receber sem data".
- **Estados:** carregando (skeleton do gráfico e da tabela); vazio ("Sem contas ou títulos ainda. Cadastre as contas e lance os títulos para ver o fluxo." + "Contas e caixas"); erro.
- **Propositivo:** o alerta do caixa negativo leva à lista de títulos da semana; o dia clicado abre o detalhe.
- **Acessibilidade:** o gráfico tem a tabela equivalente e resumo textual do menor saldo.
- **Ajuda:** `Ajuda/financeiro-fluxo-de-caixa.md`; `<Dica>` em "Realizado", "Projetado", "Vencidos em aberto" e "A receber sem data".

## Plano de testes
- **Unidade:** agregação diária, semanal e mensal e menor saldo (CA 6, 7); ocorrências virtuais da recorrente (CA 4).
- **Integração (Testcontainers, obrigatória):** dataset fixo com valores esperados (CA 1–5, 8, 9); desempenho (CA 11); três camadas (CA 12).
- **E2E (Playwright):** abrir o fluxo, trocar a visão e abrir um dia.

## Fora do escopo
- Cenário ajustado pela pontualidade do cliente e % de inadimplência: FIN-07 (Fase 4).
- DRE: FIN-08. Card do dashboard: DSH-01.

## Pontos em aberto
1. **Títulos vencidos no projetado.** O `04-fluxos` §3.5 diz "vencimento original", que fica no passado. **Recomendação:** entrar no dia de hoje, na linha "Vencidos em aberto" destacada; a FIN-07 desloca pelo atraso médio.
2. **Recebível de marco sem data.** **Recomendação:** fora da série, no quadro "A receber sem data"; a FIN-07 ou a PLN-01 podem estimar a data pela previsão do projeto.
3. **Projeção por conta.** Títulos não têm conta bancária até a baixa. **Recomendação:** projeção só consolidada no MVP; conta prevista no título fica para a BNK-01 (boleto já tem conta).
4. **P-18:** ver FIN-01, ponto 5.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers, dataset fixo) e E2E passando.
- [ ] Sem migration (só leitura), índices conferidos.
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Tela com estados de carregando, vazio e erro.
- [ ] Ajuda escrita; teste de cobertura passando.
- [ ] Alerta acionável e detalhe do dia aplicados.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] Story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `CashFlowQuery` (Dapper) e `CashFlowCalculator` (Domain, puro, testável).
- [ ] Endpoint `/api/v1/finance/cash-flow`; tela com Recharts e tabela; ajuda.

## Quality gates
- Agentes previstos: @dev, @qa, design (gráfico e acessibilidade).
- Gate: CA 1 (dataset fixo) obrigatório.

## Validação do PO
Checklist do @po (Pax), 19/10/2026.

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

- **Correções feitas na validação (19/10/2026):** o "Ajustado" saiu para a FIN-07 (Fase 4); folha e pedidos entram pelos títulos `Planned` das stories que os criam (CA 10), sem acoplar módulos; desempenho com valor (CA 11).
- **Pré-condições para `Ready`:** Pontos em aberto 1 a 3 (com padrão recomendado) e P-18.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 19/10/2026 | 0.1 | Story detalhada | River (SM) |
| 19/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
