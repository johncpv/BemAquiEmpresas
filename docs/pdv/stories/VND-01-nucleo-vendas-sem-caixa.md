# VND-01 — Núcleo de vendas sem caixa: módulo `sales`, venda com linhas, partes e pagamentos por canal, contratos `ISaleIngestion` e `IOrderPayments`, baixa de estoque e recebível por canal, sem exigir sessão de caixa

| Campo | Valor |
|---|---|
| ID | VND-01 |
| Título | Núcleo de vendas sem caixa: módulo `sales`, venda com linhas, partes e pagamentos por canal, contratos `ISaleIngestion` e `IOrderPayments`, baixa de estoque e recebível por canal, sem exigir sessão de caixa |
| Tamanho | G |
| Status | Draft (plano proposto em 15/11/2026; aguarda a aprovação do PO) |
| Fase | 10 — Fundação: perfil de negócio, catálogo, pagamentos, núcleo de vendas e spikes |
| Requisitos | Revisão independente de 15/11/2026, achado B1 · Pedido do PO (itens 1, 6 e 11) · [02, seção 4.1](../02-arquitetura.md#41-fronteira-sales--pos-quem-é-dono-de-quê) · [Modelo de dados](../03-modelo-dados.md#5-sales--schema-sales) · [Rascunho C](../adr/rascunho-C-pagamentos.md) · ADR 0006 · ADR 0023 |
| Dependências | CAT-01, PAG-01; PP-04 decidida |
| Bloqueia | PDV-01, PDV-03, OFF-02, CX-03, PDV-05; ECM-02, ECM-03, ECM-06, ECM-07; OFI-01 |
| Paridade com o mercado | Requisitos mínimos base de 10, 28, 35 e 37 do [benchmark](../00-benchmark-mercado.md#7-matriz-de-paridade-cada-requisito-mínimo-e-a-story-que-o-cobre) |
| Ordem de execução | 7 (Fase 10) |

## User story
Como **dono**, quero **que toda venda, do balcão, da loja virtual ou de uma plataforma, seja registrada do mesmo jeito e chegue ao estoque e ao financeiro**, para **ver custo, margem e caixa de todos os canais num lugar só**.

## Contexto e referências
- Na primeira versão do plano, a venda só nascia na Fase 11, dentro do PDV, e o recebível era sempre por sessão de caixa. Um pedido online não tem sessão de caixa, então o e-commerce não tinha onde gravar a venda nem como gerar o financeiro. A revisão independente pediu este núcleo na Fase 10.
- O `Sales` é o dono da venda. O `Pos` (Fase 11) e o `Storefront` (Fase 14) entregam vendas a ele por contrato e não escrevem nas tabelas dele.
- Esta story fecha, com a CAT-01 e a PDV-01, a ADR das fronteiras dos módulos.

## Critérios de aceite
### Módulo e venda
1. **Dado** o projeto `src/Modules/Sales/`, **quando** os testes de arquitetura rodam, **então** ele tem as cinco camadas, schema `sales` e só referencia `*.Contracts`.
2. **Dado** o `ModuleCatalog`, **quando** a aplicação sobe, **então** existe o módulo `sales` na área "Loja", com `view`, `create`, `edit`, `approve`, `delete` e `view_cost`.
3. **Dado** o contrato `ISaleIngestion.IngestAsync` em `Sales.Contracts`, **quando** um módulo entrega uma venda concluída (canal, loja, linhas com produto, quantidade, preço e desconto, partes, pagamentos, id e número de origem), **então** o `Sales` grava venda, linhas, partes e pagamentos **na transação de quem chamou** e põe o `SaleCompleted` no outbox.
4. **Dado** a mesma venda entregue duas vezes (mesmo id), **quando** a segunda chega, **então** nada é duplicado e a resposta é a da primeira.
5. **Dado** uma venda entregue cuja soma dos pagamentos capturados difere do total, **quando** chega por canal sem caixa, **então** é recusada com 422 (quem chama ainda pode corrigir); a regra "venda concluída nunca é recusada" vale para o PDV, que entrega pelo mesmo contrato com a marca de origem no aparelho e gera divergência em vez de erro (OFF-02).

### Canal sem caixa
6. **Dado** uma venda de canal `Online` ou `ServiceOrder`, **quando** é gravada, **então** `cash_session_id` e `register_id` ficam nulos e isso é válido; **dado** uma venda de canal `Pos`, **então** os dois são obrigatórios (validação por canal).
7. **Dado** o contrato `IOrderPayments`, **quando** um módulo pede o pagamento de um pedido (comanda, OS, pedido online), **então** o `Sales` devolve a venda criada com os pagamentos, usando a mesma máquina de estados do pagamento (`Created → Pending → Captured → Voided | Refunded`, mais `Failed` e `Unknown`).
8. **[Segurança]** **Dado** qualquer entrada de pagamento, **quando** é gravada, **então** não existe campo para número de cartão, validade, código de segurança ou trilha; do cartão ficam só bandeira, NSU, código de autorização e a referência do provedor. Um teste varre o modelo e falha se aparecer campo com esses nomes.

### Estoque
9. **Dado** o `SaleCompleted` de uma venda de canal sem caixa, **quando** o consumidor do estoque roda, **então** cada item sai do local de estoque da loja por `IStockMovements`, com origem de venda e rastreio até a venda; a baixa consome a reserva feita antes (ECM-02), e a regra de saldo nunca negativo continua valendo. O consumidor é idempotente.
10. **Dado** a baixa feita, **quando** confiro a linha da venda, **então** o custo unitário (`decimal(18,6)`) está gravado.

### Financeiro
11. **Dado** vendas de canal sem caixa num dia, **quando** o consumidor do Financeiro roda, **então** nasce um recebível por **dia, canal, provedor (adquirente ou gateway), forma e vencimento**, com bruto, taxa prevista e líquido, origem `Sale` ou `CardSettlement`, **sem `cash_session_id`**; reprocessar o dia não duplica.
12. **Dado** `ISalesRevenue`, **quando** o DRE por competência é montado, **então** a receita vem por dia, loja e canal.
13. **Dado** o contrato de escrita de recebível por origem de venda em `Finance.Contracts`, **quando** a CX-03 (Fase 11) o usa, **então** o mesmo contrato aceita o agrupamento por sessão de caixa, sem mudança de forma.

## Regras de negócio e técnicas
- Dinheiro `decimal(18,2)`; quantidade `decimal(18,4)`; custo `decimal(18,6)`.
- Venda é documento com movimento: nunca é apagada, só cancelada ou devolvida.
- A venda não calcula imposto nem emite documento fiscal: publica o evento, e o Fiscal reage (NF-e 55 no e-commerce, NFC-e no PDV).
- Sem tela de venda nesta story: quem cria venda são os canais. A retaguarda de vendas (lista, detalhe, cards) é a PDV-07 e serve a todos os canais.

## Permissões, eventos e tabelas
- **Permissões:** `sales.view`, `create`, `edit`, `approve`, `delete`, `view_cost` (novas, por seed).
- **Contratos:** `ISaleIngestion`, `IOrderPayments`, `ISalesRevenue`, `ISaleLookup` (`Sales.Contracts`); escrita de recebível por origem de venda (`Finance.Contracts`).
- **Eventos:** `SaleCompleted`, `SaleCancelled`.
- **Tabelas:** `sales.sales`, `sale_lines`, `sale_parts`, `sale_payments`, `stock_postings`; em `finance.receivables`, as origens novas e os campos `sale_channel`, `settlement_date`, `gross_amount`, `expected_fee`, `net_amount` e `cash_session_id` (nulo aqui).
- **Auditoria:** cancelamento de venda.

## Telas e estados
- Nenhuma tela nova nesta story. **Contas a receber** ganha o filtro "Origem: Vendas" e a coluna "Canal"; vazio, carregando e erro como já são.
- **Ajuda:** `Ajuda/financeiro-contas-a-receber.md` atualizada (o que são os recebíveis de venda e por que não entram na régua de cobrança).

## Plano de testes
- **Unidade:** validação por canal (CA 6); máquina de estados do pagamento; agrupamento do recebível por dia e canal.
- **Integração (Testcontainers), obrigatória por movimentar estoque e financeiro:** ingestão na transação de quem chama, com rollback conjunto (CA 3); idempotência (CA 4); venda online sem sessão de caixa (CA 6); baixa com reserva e custo (CA 9 e 10); recebível por canal, idempotente (CA 11); DRE (CA 12).
- **Arquitetura:** nenhum módulo além do `Sales` escreve no schema `sales`; nenhum campo de cartão (CA 8).

## Fora do escopo
- Tela de venda, caixa e sincronização (Fase 11).
- Baixa sem bloqueio por saldo (PDV-05), que é do PDV.
- Devolução e vale-troca (PDV-06; ECM-05 no online).
- Carrinho, checkout e gateway (ECM-02 e ECM-03).

## Pontos em aberto
- PP-04: fronteiras dos módulos. Se o PO preferir um projeto só para `Sales` e `Pos`, o contrato vira chamada interna e os critérios não mudam.
- Baixa agregada ou por venda: o SPK-03 decide para o PDV; no canal sem caixa o volume é baixo e a baixa é por venda.

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
| 15/11/2026 | 0.2 | Revista depois da revisão independente e das decisões do PO do mesmo dia ([o que mudou](../README.md#7-revisão-independente-de-15112026-achados-e-o-que-foi-aplicado)). | Claude (planejamento) |
