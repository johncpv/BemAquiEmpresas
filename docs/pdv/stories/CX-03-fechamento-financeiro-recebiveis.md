# CX-03 — Fechamento de caixa no Financeiro: resumos por forma, título contra a adquirente com bruto, taxa prevista e vencimento D+N, quebra de caixa e a receita por dia para o DRE

| Campo | Valor |
|---|---|
| ID | CX-03 |
| Título | Fechamento de caixa no Financeiro: resumos por forma, título contra a adquirente com bruto, taxa prevista e vencimento D+N, quebra de caixa e a receita por dia para o DRE |
| Tamanho | G |
| Status | Ready for Review (24/11/2026; branch `feat/cx-03`) |
| Fase | 11 — PDV balcão com cupom não fiscal e offline (marco MVP) |
| Requisitos | Objetivo central do produto (visibilidade financeira) · [Rascunho C](../adr/rascunho-C-pagamentos.md) item 8 · [Modelo de dados](../03-modelo-dados.md#6-finance-extensões) · BNK-03b (conciliação existente) |
| Dependências | CX-02, PAG-01, VND-01, OFF-02 |
| Bloqueia | PDV-06, PDV-07, PDV-10 |
| Paridade com o mercado | Requisitos mínimos 28 do [benchmark](../00-benchmark-mercado.md#7-matriz-de-paridade-cada-requisito-mínimo-e-a-story-que-o-cobre) |
| Ordem de execução | 9 (Fase 11) |

## User story
Como **financeiro**, quero **que o caixa fechado vire lançamento sozinho, com a taxa e a data em que o cartão cai**, para **o fluxo de caixa e o DRE refletirem a loja sem eu digitar nada**.

## Contexto e referências
- `IReceivables` hoje só lê, e a origem do título é `Manual`, `Opening` ou `Contract`. O contrato de escrita e a origem de venda nascem na VND-01, para o canal sem caixa; esta story acrescenta o agrupamento por sessão de caixa.
- Um supermercado gera milhares de vendas por dia: o Financeiro recebe **resumos**, não uma linha por venda. O detalhe fica no `Sales`.

## Critérios de aceite
### Lançamentos
1. **Dado** o evento `CashSessionClosed`, **quando** o consumidor do Financeiro roda, **então** o **recolhimento** vira transferência Caixa do PDV → Cofre, e na conta Caixa daquele caixa fica só o fundo de troco do próximo turno; a soma de abertura, vendas em dinheiro, sangrias, suprimentos, recolhimento e quebra fecha com o saldo da conta.
2. **Dado** vendas em cartão na sessão, **quando** processo, **então** nasce um título a receber **contra a adquirente** para cada combinação de adquirente, bandeira, forma e data de vencimento, com valor bruto, taxa prevista (a vigente na data da venda) e líquido; o vencimento é a data da venda + N dias da tabela.
3. **Dado** vendas em crédito parcelado, **quando** processo, **então** há um título por parcela, com a taxa e o prazo daquela parcela.
4. **Dado** vendas em Pix conferido pelo operador, **quando** processo, **então** entra um lançamento a conciliar na conta de destino da forma, com o detalhe por pagamento disponível pelo contrato; o que foi marcado "Não localizado" na CX-02 não entra como recebido: vira quebra da forma Pix.
5. **Dado** quebra de caixa na sessão, **quando** processo, **então** entra um lançamento na conta "Quebra de caixa", como sobra ou falta, ligado à sessão e ao operador.
6. **Dado** o mesmo `CashSessionClosed` entregue duas vezes, **quando** processo, **então** nada é duplicado (idempotente por sessão).
7. **Dado** a sessão reaberta (CX-02 CA 10), **quando** processo a reabertura, **então** os lançamentos do fechamento são estornados; título de adquirente já baixado impede a reabertura, com a mensagem "Este caixa já tem recebimento conciliado. Estorne a baixa antes.".

### Conciliação e relatórios
8. **Dado** títulos contra a adquirente, **quando** o depósito aparece no extrato, **então** a conciliação casa **um depósito com vários títulos** (a adquirente deposita um valor só para vários dias, bandeiras e formas), pelo líquido previsto somado; a diferença entre bruto e líquido é baixada na conta de taxa de cartão; diferença entre o previsto e o recebido fica apontada.
9. **Dado** o fluxo de caixa, **quando** abro, **então** os recebíveis de cartão aparecem na data prevista, pelo líquido.
10. **Dado** `ISalesRevenue`, **quando** o DRE por competência é montado, **então** a receita de vendas vem por dia e por loja, do `Sales`, sem depender da data do recebimento.
11. **Dado** um título de adquirente, **quando** abro, **então** vejo o link para a sessão de caixa e, dali, para as vendas que o compõem.

## Regras de negócio e técnicas
- Todos os valores `decimal(18,2)`; a taxa `decimal(9,4)`; o líquido é arredondado por título, e a diferença de centavos fica no último título do grupo.
- O Financeiro não lê tabela do `Sales` nem do `Pos`: recebe os resumos no evento e consulta o detalhe pelo contrato.
- Os títulos de adquirente não entram na régua de cobrança nem no aging de clientes.
- Fiado (título contra o cliente, na hora da venda) é da PDV-08.

## Permissões, eventos e tabelas
- **Permissões:** as do `finance` que já existem.
- **Contratos:** escrita de recebível por origem de venda em `Finance.Contracts`; `ISalesRevenue` em `Sales.Contracts`.
- **Eventos consumidos:** `CashSessionClosed`, `CashSessionReopened`.
- **Tabelas:** `finance.receivables` com as origens `Sale` e `CardSettlement` e os campos `cash_session_id`, `gross_amount`, `expected_fee`, `net_amount`; conta "Quebra de caixa" e conta "Taxas de cartão" no plano de contas (seed).

## Telas e estados
- **Contas a receber:** filtro "Origem: Cartões" e a coluna "Líquido previsto"; os títulos de adquirente ficam fora da visão padrão de clientes.
- **Sessão de caixa** (detalhe, da CX-02): seção "No financeiro", com os lançamentos gerados e os links.
- Vazio, carregando e erro conforme as telas existentes.
- **Ajuda:** `Ajuda/financeiro-contas-a-receber.md` atualizada; `Ajuda/financeiro-recebiveis-de-cartao.md` (nova).

## Plano de testes
- **Unidade:** agrupamento por adquirente, bandeira, forma e vencimento; parcelas; arredondamento do líquido.
- **Integração (Testcontainers), obrigatória por movimentar financeiro:** sessão com dinheiro, débito, crédito à vista, crédito em 3 vezes e Pix (CA 1 a 5); idempotência (CA 6); reabertura e bloqueio (CA 7); conciliação com taxa (CA 8); fluxo de caixa e DRE (CA 9 e 10).

## Fora do escopo
- Conferir taxa prevista × cobrada pelo extrato da adquirente (PAG-05).
- Antecipação de recebíveis.
- Margem por venda (PDV-10).

## Pontos em aberto
- A conta contábil da taxa de cartão e da quebra de caixa no plano de contas padrão: confirmar com a contadora ("A confirmar", ADR 0042).
- Dias corridos ou úteis no D+N: é dado da adquirente; a proposta é dias corridos com ajuste para o próximo dia útil.
- **Conciliação de N títulos para 1 depósito:** conferir se a conciliação existente (BNK-03b) já suporta. Se só casa 1 para 1, esta story inclui a extensão e é reestimada.

## Implementação (24/11/2026)
**Revisão contra o código antes de codar:**
- O recolhimento (CA 1) já entrou na CX-02 (movimento `Deposit` pelo consumidor da CX-01). Aqui ficam os resumos por forma, a quebra e o estorno.
- O contrato de escrita (`ISaleReceivables`, VND-01) e `ISalesRevenue` (DRE por dia, loja e canal, inclusive o `Pos`) já existiam: o CA 10 já vale para o caixa.
- O fluxo de caixa já projeta o título em aberto pelo saldo na data de vencimento: o recebível de cartão aparece pelo líquido na data prevista (CA 9).
- O pacote do Comércio (PERF-04) criou "Quebra de caixa" como **conta Caixa** (não conta do plano): a quebra é movimento entre contas, como a sangria.
- A conciliação (BNK-03b) só baixava um título por linha: a extensão N títulos × 1 depósito entrou aqui.

**Desenho:**
- `CashSessionFinanceConsumer` (Finance) consome `CashSessionClosed` e `CashSessionReopened`, com trava por sessão. Lê os pagamentos da sessão pelo `ISaleLookup.SessionPaymentsAsync` (novo, com o dia de cada venda) e agrupa em `CashSessionReceivables` (regras puras): cartão por adquirente, bandeira, forma e vencimento (D+N da taxa vigente no dia da venda), contra a adquirente, origem `CardSettlement`; parcelado com um título por parcela (D+N + 30 × (i − 1); bruto e taxa em centavos, a sobra na última); Pix e demais por forma; fiado por venda. A taxa é arredondada por pagamento.
- Grava pelo `ISaleReceivables` ("grava o valor": CA 6). A chave ganhou `Brand` e `Break`, que só entram no texto quando preenchidos (a chave da VND-01 não muda).
- Dinheiro: o título da forma Dinheiro é baixado na conta do caixa (`receivable_settlements.source = Pos`). Pix "Não localizado" (CX-02 manda o valor em `CashSessionBreak.PixNotFound`): sai do título do Pix e vira um título de quebra baixado na conta "Quebra de caixa". Quebra do dinheiro: transferência caixa → Quebra (falta) ou Quebra → caixa (sobra), id determinístico por sessão e reabertura. Com fundo, sangria, suprimento e recolhimento, o saldo da conta do caixa fecha no fundo que fica (CA 1, testado).
- Reabertura: estorna as baixas `Pos` e a transferência da quebra (pernas inversas com a data de hoje) e zera os títulos; o próximo fechamento os reabre. O `Pos` pergunta antes ao `ICashSessionFinance` (Finance.Contracts): título da sessão com baixa de outra origem (conciliada) recusa a reabertura com 422 `cash_session_reconciled` e a mensagem do CA 7. Baixa `Pos` não se estorna à mão (409 `settlement_from_cash_closing`).
- Conciliação (CA 8): `POST /finance/bank-accounts/{id}/reconciliation/lines/{lineId}/settle-cards` baixa de 1 a 50 títulos `CardSettlement` da mesma adquirente pelo saldo (líquido previsto), com a data e a conta da linha, e concilia a linha com todos os recebimentos numa transação. Depósito menor: a diferença fica em aberto no último título (pelo vencimento) e volta no resultado; maior: 422. Lista dos candidatos: `GET .../reconciliation/card-receivables`. Os dois só nos perfis que vendem (`finance.sale_receivables`).
- `GET /finance/cash-sessions/{id}` (`finance.view`, perfis que vendem): títulos e transferências da sessão (`bank_transfers.pos_cash_session_id`, nova, preenchida também pelo consumidor da CX-01).
- Contas a receber: filtro "Origem: Cartões", coluna "Líquido previsto"; o título contra a adquirente sai da visão padrão (sem filtro de cliente ou contrato) e da inadimplência. Detalhe com bruto, taxa e o link para a sessão (que leva às vendas).
- Sessão de caixa: seção "No financeiro" para quem tem `finance.view`. Conciliação: "Baixar recebíveis de cartão" na linha de entrada.
- Migration `FinanceCashSessionCx03`: coluna anulável e dois índices; nada muda nos dados existentes (Konebras sem PDV não tem título de cartão nem sessão).

**Fora desta story (registrado):**
- **Taxa de cartão como despesa no DRE** e a **quebra no DRE**: a conta contábil está "A confirmar com a contadora" (ponto em aberto desta story, ADR 0042). O título já vale o líquido e guarda a taxa prevista; a quebra fica na conta Quebra de caixa.
- Diferença de contagem de comprovante de cartão (contado ≠ vendas): não move dinheiro (a adquirente paga pelo que a venda registrou); fica no relatório da sessão (CX-02) e a conciliação aponta a diferença do depósito.
- D+N em dias corridos (como na VND-01), sem ajuste para dia útil: ponto em aberto da story ("dado da adquirente"). Prazo das parcelas seguintes: +30 dias, a confirmar com a adquirente.
- Ordem das entregas: se o `CashSessionReopened` for processado antes do `CashSessionClosed` da mesma sessão (outbox em paralelo), o fechamento antigo seria lançado depois do estorno; o próximo fechamento recompõe pela fonte.

## Definition of Done
- [x] Critérios de aceite atendidos (DRE da taxa e da quebra: decisão da contadora, acima).
- [x] Testes de unidade e de integração (Testcontainers) passando; Vitest no front.
- [x] Migration criada; permissões só do `ModuleCatalog` (sem permissão nova).
- [x] Telas com estados de carregando, vazio e erro; ajuda escrita ou atualizada em `Ajuda/` no mesmo commit.
- [x] Padrões e próximo passo propositivos avaliados (depósito já vem com os recebíveis da maquininha marcados); auditoria ligada (títulos, baixas e transferências são `IAuditable`).
- [x] OpenAPI e cliente Orval regenerados; story marcada no backlog. Sem ADR nova (fronteira da 02 §4.1 e contrato da VND-01).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 15/11/2026 | 0.1 | Story escrita como parte do plano do PDV (proposta). Aguarda a aprovação do plano e as decisões citadas em "Pontos em aberto" para sair de `Draft`. | Claude (planejamento) |
| 15/11/2026 | 0.2 | Revista depois da revisão independente e das decisões do PO do mesmo dia ([o que mudou](../README.md#7-revisão-independente-de-15112026-achados-e-o-que-foi-aplicado)). | Claude (planejamento) |
| 24/11/2026 | 1.0 | Implementada: revisão contra o código, desenho e o que ficou fora na seção "Implementação". | Claude (dev) |
