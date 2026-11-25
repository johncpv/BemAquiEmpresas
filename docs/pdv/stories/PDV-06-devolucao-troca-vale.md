# PDV-06 — Devolução total ou parcial, troca ligada à venda nova e vale-troca, com a venda original localizada por número ou CPF, prazo por loja e aprovação

| Campo | Valor |
|---|---|
| ID | PDV-06 |
| Título | Devolução total ou parcial, troca ligada à venda nova e vale-troca, com a venda original localizada por número ou CPF, prazo por loja e aprovação |
| Tamanho | M |
| Status | Pronta para revisão (25/11/2026, branch `feat/pdv-06`) |
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
1. **Dado** `pos.return` e rede, **quando** abro "Devolução", **então** localizo a venda pelo número do cupom ou pelo CPF do cliente; sem a venda original não há devolução. **[Segurança]** A busca por CPF vai no **corpo de um POST**, nunca na URL, e o servidor procura pelo índice cego; o CPF não aparece em log de acesso nem em histórico do navegador, e a leitura fica auditada.
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
- **Endpoints:** `POST /api/v1/sales/lookup` (número ou CPF no corpo; leitura de CPF auditada), `POST /api/v1/sales/returns`, `POST .../{id}/approve`, `GET /api/v1/sales/store-credits/{code}`, `GET /api/v1/sales/returns/export`. Todos na lista do PIN.
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
- LGPD: localizar devolução é a segunda finalidade do CPF, além do documento fiscal (PP-26); entra na tabela de retenção. Apagado o CPF pela retenção, a venda só é localizada pelo número.

## Definition of Done
- [x] Critérios de aceite atendidos.
- [x] Testes de unidade e de integração (Testcontainers) passando; Vitest no front.
- [x] Migration criada; permissões só do `ModuleCatalog`.
- [x] Telas com estados de carregando, vazio e erro; ajuda escrita ou atualizada em `Ajuda/` no mesmo commit.
- [x] Padrões e próximo passo propositivos avaliados; auditoria ligada.
- [x] OpenAPI e cliente Orval regenerados; story marcada no backlog; decisões em ADR.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 15/11/2026 | 0.1 | Story escrita como parte do plano do PDV (proposta). Aguarda a aprovação do plano e as decisões citadas em "Pontos em aberto" para sair de `Draft`. | Claude (planejamento) |
| 15/11/2026 | 0.2 | Revista depois da revisão independente e das decisões do PO do mesmo dia ([o que mudou](../README.md#7-revisão-independente-de-15112026-achados-e-o-que-foi-aplicado)). | Claude (planejamento) |
| 25/11/2026 | 1.0 | Implementada (branch `feat/pdv-06`). **Decisões:** (1) PP-22: prazo de troca de 30 dias por loja (decidido em 18/11); a **validade do vale** não tem valor decidido no 06: padrão conservador de **90 dias**, por loja (`pos.stores.store_credit_validity_days`, CHECK 1 a 3650), **a confirmar com o PO**. (2) Rotas do caixa em `/api/v1/pos/sales/lookup`, `/api/v1/pos/returns`, `/api/v1/pos/returns/{id}/approve`, `/api/v1/pos/store-credits/lookup` e `/redeem`, não em `/api/v1/sales/*`: a sessão por PIN do caixa só alcança `/api/v1/pos/*` (ADR 0048, teste de autorização); o relatório fica em `GET /api/v1/sales/returns` e `/export` (`sales.view`). (3) O código do vale é valor ao portador: vai **no corpo de um POST** (não em `GET /store-credits/{code}`), o banco guarda só o hash SHA-256 e os 4 últimos caracteres, e o código aparece uma vez (resposta da conclusão, para imprimir) — recomendação aceita (PO 25/11). (4) Supervisor pela ação `Return` da PDV-04 (`pos.return` do supervisor, PIN no caixa, aprovação presa à devolução, ao valor e ao caixa, 10 min); a exceção da PP-11 para quem tem `*.approve` **não** foi ampliada. (5) Pedir e aprovar: o pedido nasce `Requested`; a aprovação conclui na mesma transação (`Approved` → `Completed`), com trava por venda. (6) Financeiro: contas tipo Caixa criadas sob demanda, como Cofre e Quebra: **Vales-troca** (passivo, saldo negativo) e **Devoluções de vendas**; vale emitido = transferência Vales-troca → Devoluções; vale usado = título da forma Vale-troca baixado na conta Vales-troca no fechamento (CX-03); dinheiro = movimento `Refund` da sessão (Caixa do PDV → Devoluções); estorno de cartão = pagamento líquido no fechamento e, com a sessão já fechada, o título da adquirente regravado. (7) Troca: devolução em vale + venda nova paga com o vale; a ligação (`exchange_sale_id`) nasce quando a venda com o vale chega; diferença a favor do cliente fica no vale (não em dinheiro). (8) Venda com pagamento em vale sem o abatimento no servidor vai para a quarentena; abatimento sem pagamento capturado, venda descartada ou cancelada devolve o valor ao vale. | Claude (dev) |
| 25/11/2026 | 1.1 | Revisão de segurança: gaveta recalculada sob a trava do caixa (mesma da subida) antes do dinheiro; devolução só na loja da venda (**recomendação aceita, PO 25/11**); dinheiro só até o pago em dinheiro menos o já devolvido; abatimento do vale preso ao caixa e à sessão aberta, com job horário que devolve órfãos após 24 h; reemissão do código do vale com PIN do supervisor (ação `CreditReissue`, rota pronta, botão no caixa pendente); receita do DRE sem as devoluções (data da devolução); estorno com título liquidado vira pendência auditada; busca por CPF auditada e com teto por caixa; cliente do vale sempre o da venda; prazo reavaliado na conclusão. Pendências no 06. | Claude (dev) |
