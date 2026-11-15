# CX-03 — Fechamento de caixa no Financeiro: resumos por forma, título contra a adquirente com bruto, taxa prevista e vencimento D+N, quebra de caixa e a receita por dia para o DRE

| Campo | Valor |
|---|---|
| ID | CX-03 |
| Título | Fechamento de caixa no Financeiro: resumos por forma, título contra a adquirente com bruto, taxa prevista e vencimento D+N, quebra de caixa e a receita por dia para o DRE |
| Tamanho | G |
| Status | Draft (plano proposto em 15/11/2026; aguarda a aprovação do PO) |
| Fase | 11 — PDV balcão com cupom não fiscal e offline (marco MVP) |
| Requisitos | Objetivo central do produto (visibilidade financeira) · [Rascunho C](../adr/rascunho-C-pagamentos.md) item 8 · [Modelo de dados](../03-modelo-dados.md#6-finance-extensões) · BNK-03b (conciliação existente) |
| Dependências | CX-02, PAG-01, OFF-02 |
| Bloqueia | PDV-06, PDV-07, PDV-10 |
| Paridade com o mercado | Requisitos mínimos 28 do [benchmark](../00-benchmark-mercado.md#7-matriz-de-paridade-cada-requisito-mínimo-e-a-story-que-o-cobre) |
| Ordem de execução | 9 (Fase 11) |

## User story
Como **financeiro**, quero **que o caixa fechado vire lançamento sozinho, com a taxa e a data em que o cartão cai**, para **o fluxo de caixa e o DRE refletirem a loja sem eu digitar nada**.

## Contexto e referências
- `IReceivables` hoje só lê, e a origem do título é `Manual`, `Opening` ou `Contract`. Falta o contrato de escrita e a origem de venda.
- Um supermercado gera milhares de vendas por dia: o Financeiro recebe **resumos**, não uma linha por venda. O detalhe fica no `Sales`.

## Critérios de aceite
### Lançamentos
1. **Dado** o evento `CashSessionClosed`, **quando** o consumidor do Financeiro roda, **então** o dinheiro contado fica como saldo da conta Caixa daquele caixa, coerente com a abertura, as sangrias e os suprimentos já transferidos.
2. **Dado** vendas em cartão na sessão, **quando** processo, **então** nasce um título a receber **contra a adquirente** para cada combinação de adquirente, bandeira, forma e data de vencimento, com valor bruto, taxa prevista (a vigente na data da venda) e líquido; o vencimento é a data da venda + N dias da tabela.
3. **Dado** vendas em crédito parcelado, **quando** processo, **então** há um título por parcela, com a taxa e o prazo daquela parcela.
4. **Dado** vendas em Pix conferido pelo operador, **quando** processo, **então** entra um lançamento a conciliar na conta de destino da forma, pelo total do dia.
5. **Dado** quebra de caixa na sessão, **quando** processo, **então** entra um lançamento na conta "Quebra de caixa", como sobra ou falta, ligado à sessão e ao operador.
6. **Dado** o mesmo `CashSessionClosed` entregue duas vezes, **quando** processo, **então** nada é duplicado (idempotente por sessão).
7. **Dado** a sessão reaberta (CX-02 CA 10), **quando** processo a reabertura, **então** os lançamentos do fechamento são estornados; título de adquirente já baixado impede a reabertura, com a mensagem "Este caixa já tem recebimento conciliado. Estorne a baixa antes.".

### Conciliação e relatórios
8. **Dado** um título contra a adquirente, **quando** o depósito aparece no extrato, **então** a conciliação bancária existente o casa pelo líquido previsto; a diferença entre bruto e líquido é baixada na conta de taxa de cartão; diferença entre líquido previsto e recebido fica apontada.
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

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade e de integração (Testcontainers) passando; Vitest no front.
- [ ] Migration criada; permissões só do `ModuleCatalog`.
- [ ] Telas com estados de carregando, vazio e erro; ajuda escrita ou atualizada em `Ajuda/` no mesmo commit.
- [ ] Padrões e próximo passo propositivos avaliados; auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados; story marcada no backlog; decisões em ADR.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 15/11/2026 | 0.1 | Story escrita como parte do plano do PDV (proposta). Aguarda a aprovação do plano e as decisões citadas em "Pontos em aberto" para sair de `Draft`. | Claude (planejamento) |
