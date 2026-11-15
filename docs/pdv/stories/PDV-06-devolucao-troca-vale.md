# PDV-06 — Devolução total ou parcial, troca ligada à venda nova e vale-troca, com a venda original localizada por número ou CPF, prazo por loja e aprovação

| Campo | Valor |
|---|---|
| ID | PDV-06 |
| Título | Devolução total ou parcial, troca ligada à venda nova e vale-troca, com a venda original localizada por número ou CPF, prazo por loja e aprovação |
| Tamanho | M |
| Status | Draft (plano proposto em 15/11/2026; aguarda a aprovação do PO) |
| Fase | 11 — PDV balcão com cupom não fiscal e offline (marco MVP) |
| Requisitos | [Fluxos](../04-fluxos-estados.md#6-cancelamento-devolução-estorno-e-troca) · [PRD do PDV](../01-PRD.md) seção 6.5 (devolução fictícia) |
| Dependências | PDV-05, CX-03, PDV-04 |
| Bloqueia | — |
| Ordem de execução | 14 (Fase 11) |

## User story
Como **operador de caixa**, quero **devolver ou trocar um produto a partir do cupom do cliente**, para **resolver na hora sem inventar lançamento**.

## Contexto e referências
- Devolução depende de dado do servidor (a venda original pode ser de outro caixa): **só funciona com rede**.
- No MVP não há documento fiscal; a devolução gera recibo. A NF-e de devolução é da NFCE-03 (QC-15).

## Critérios de aceite
1. **Dado** `pos.return` e rede, **quando** abro "Devolução", **então** localizo a venda pelo número do cupom ou pelo CPF do cliente; sem a venda original não há devolução.
2. **Dado** a venda localizada, **quando** escolho os itens e as quantidades, **então** não posso devolver mais do que foi vendido menos o que já foi devolvido.
3. **Dado** uma venda fora do prazo de devolução da loja, **quando** tento, **então** vejo "Prazo de devolução encerrado em dd/mm." e só sigo com a aprovação de um supervisor.
4. **Dado** os itens escolhidos, **quando** informo o destino de cada um, **então** escolho "Volta ao estoque" ou "Avariado (perda)", com motivo.
5. **Dado** o reembolso, **quando** escolho a forma, **então** "Vale-troca" vem como padrão; "Dinheiro" exige aprovação de supervisor e não pode passar do dinheiro em caixa; "Estorno no cartão" instrui a estornar na maquininha e pede o NSU.
6. **Dado** a devolução aprovada e concluída, **quando** processa, **então**: a venda original passa a `PartiallyReturned` ou `Returned`; o estoque recebe a entrada de devolução ou o lançamento de perda; o dinheiro sai da sessão de caixa, ou nasce o vale-troca, ou o título da adquirente é reduzido; sai o recibo de devolução.
7. **Dado** um vale-troca, **quando** é emitido, **então** tem código, valor, validade e cliente opcional, e sai impresso.
8. **Dado** um vale-troca válido e rede, **quando** uso como forma de pagamento numa venda, **então** o saldo é abatido; saldo restante continua no mesmo vale; vale vencido ou sem saldo é recusado com a mensagem.
9. **Dado** uma troca, **quando** concluo, **então** a devolução e a venda nova ficam ligadas; a diferença a favor da loja é paga na venda nova; a favor do cliente, vira vale ou dinheiro.
10. **Dado** o Financeiro, **quando** fecho o caixa, **então** os vales emitidos aparecem como passivo e os vales usados como forma de pagamento, sem virar receita em dobro.
11. **Dado** o aparelho sem rede, **quando** abro o menu, **então** "Devolução" e a forma "Vale-troca" estão desabilitadas com "Disponível com internet.".
12. **Dado** `sales.view`, **quando** abro o relatório de devoluções, **então** vejo por operador, motivo e forma de reembolso, com exportação.

## Regras de negócio e técnicas
- Documento com movimento não é apagado: a venda original fica, e a devolução é um documento novo ligado a ela.
- O valor devolvido de cada item é o praticado na venda (com o desconto rateado), não o preço atual.
- Vale-troca é passivo: `decimal(18,2)`, com saldo.
- Devolução em dinheiro e fora do prazo passam pelo supervisor (PDV-04).

## Permissões, eventos e tabelas
- **Permissões:** `pos.return`.
- **Endpoints:** `GET /api/v1/sales/lookup` (por número ou CPF; leitura de CPF auditada), `POST /api/v1/sales/returns`, `POST .../{id}/approve`, `GET /api/v1/sales/store-credits/{code}`, `GET /api/v1/sales/returns/export`. Todos na lista do PIN.
- **Evento de domínio:** `SaleReturned`.
- **Tabelas:** `sales.sale_returns`, `sale_return_lines`, `store_credits`.
- **Auditoria:** devolução, aprovação, emissão e uso de vale.

## Telas e estados
- **Devolução** (`/pdv/devolucao`): localizar → escolher itens → destino → reembolso → concluir. Vazio da busca: "Nenhuma venda encontrada com este número ou CPF.". Carregando e erro.
- **Loja → Devoluções** (`/app/loja/devolucoes`): relatório.
- **Ajuda:** `Ajuda/pdv-devolucao.md` e `Ajuda/loja-devolucoes.md` (novas). `<Dica>` em "Vale-troca".

## Plano de testes
- **Unidade:** quantidade devolvível; valor do item com desconto rateado; saldo do vale.
- **Integração (Testcontainers), obrigatória por movimentar estoque e financeiro:** devolução parcial com volta ao estoque e com perda (CA 4 e 6); vale emitido e usado (CA 7, 8 e 10); troca com diferença (CA 9); prazo e supervisor (CA 3).
- **Vitest:** fluxo da tela; desabilitado sem rede (CA 11).

## Fora do escopo
- NF-e de devolução e cancelamento de NFC-e (NFCE-03).
- Devolução de pedido online (ECM-05).

## Pontos em aberto
- PP-22: prazo padrão de devolução (sugestão: 30 dias) e validade padrão do vale.
- LGPD: a busca por CPF é a segunda finalidade do CPF, além do documento fiscal (PP-26); entra na tabela de retenção.

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
