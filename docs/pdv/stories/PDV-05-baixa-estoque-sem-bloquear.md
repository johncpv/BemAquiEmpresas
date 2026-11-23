# PDV-05 — Baixa de estoque da venda sem bloquear: a falta de saldo nunca recusa a venda, a baixa acontece fora do caminho do caixa e a falta fica visível na retaguarda com ação de ajuste

| Campo | Valor |
|---|---|
| ID | PDV-05 |
| Título | Baixa de estoque da venda sem bloquear: a falta de saldo nunca recusa a venda, a baixa acontece fora do caminho do caixa e a falta fica visível na retaguarda com ação de ajuste |
| Tamanho | M |
| Status | Pronta para revisão (23/11/2026, branch `feat/pdv-05-baixa-estoque`); PP-02 decidida em 18/11/2026 (opção B, configurável) e SPK-03 (baixa por venda) |
| Fase | 11 — PDV balcão com cupom não fiscal e offline (marco MVP) |
| Requisitos | [PRD do PDV](../01-PRD.md) regra 1 · [Rascunho F](../adr/rascunho-F-escala-hospedagem.md) itens 2 a 4 · ADR 0023 (motor de estoque) · **[PP-02](../06-pendencias-riscos.md#pp-02--venda-com-saldo-insuficiente)** |
| Dependências | OFF-02, VND-01; **PP-02 decidida**; SPK-03 (baixa agregada ou por venda) |
| Bloqueia | PDV-06, PDV-07, PDV-10 |
| Ordem de execução | 10 (Fase 11) |

## User story
Como **dono**, quero **que o estoque baixe a cada venda sem nunca travar o caixa**, para **o saldo e o custo refletirem a loja**, e como **estoquista**, quero **ver onde o sistema e a prateleira não batem**.

## Contexto e referências
- O motor de estoque é síncrono, exige transação e proíbe saldo negativo (`CHECK quantity_on_hand >= 0`, 409 `insufficient_stock`). Cada movimento pega um número `MOV`, e a linha do item guarda o custo médio.
- A VND-01 já entrega a baixa de estoque da venda para o canal sem caixa, onde há reserva antes e a regra de saldo nunca negativo continua valendo. Esta story trata do **PDV**, onde a mercadoria já saiu da loja quando a venda chega ao servidor.
- **As análises discordam sobre o que fazer com a falta de saldo.** Os critérios abaixo valem nas duas opções; os CA 6A e 6B são alternativos, conforme a PP-02.

## Critérios de aceite
1. **Dado** o `SaleCompleted` de uma venda, **quando** o consumidor do estoque roda, **então** cada item sai do local de estoque da loja, por `IStockMovements`, com a origem de venda do PDV e o rastreio até a venda.
2. **Dado** o lote de sincronização, **quando** é gravado, **então** a baixa **não** acontece na mesma transação: o caixa nunca espera o estoque.
3. **Dado** o mesmo evento entregue duas vezes, **quando** o consumidor roda, **então** a baixa acontece uma vez.
4. **Dado** a forma decidida no SPK-03, **quando** a baixa é agregada, **então** há um movimento por item, por caixa e por janela de tempo, com a lista das vendas que o compõem; quando é por venda, há um movimento por linha.
5. **Dado** um item com saldo insuficiente no local da loja, **quando** a baixa roda, **então** a venda **continua válida** e o `stock_status` dela fica `Shortfall`; nenhuma exceção sobe para o caixa.
6. **Conforme a PP-02:**
   - **6A (saldo negativo em local de loja):** **dado** o local marcado como loja, **quando** falta saldo, **então** o saldo fica negativo, só para a origem de venda do PDV; o item entra no relatório "Divergências de estoque" com o saldo negativo; o custo da saída usa o custo médio vigente. Locais sem a marca continuam recusando saldo negativo.
   - **6B (pendência):** **dado** a falta, **quando** a baixa roda, **então** sai o que há em saldo e o restante fica numa pendência aberta (item, local, quantidade, vendas); o saldo nunca fica negativo; regularizado o estoque, "Aplicar baixa" lança o restante.
7. **Dado** `inventory.view`, **quando** abro "Divergências de estoque", **então** vejo por loja os itens com falta, a quantidade, desde quando e as vendas ligadas, com as ações "Ajustar estoque" (inventário do item) e "Transferir do depósito".
8. **Dado** a baixa feita, **quando** confiro a linha da venda, **então** o custo unitário (`decimal(18,6)`) está gravado, para a margem.
9. **Dado** uma venda cancelada (PDV-04) ou devolvida (PDV-06), **quando** processo, **então** o estorno ou a entrada usa o documento de origem; se a baixa original ficou em falta, o estorno reduz a falta antes de dar entrada.
10. **Dado** a Konebras, **quando** rodo a suíte do estoque, **então** nenhum comportamento existente muda: os locais da fábrica continuam recusando saldo negativo.

## Regras de negócio e técnicas
- A venda concluída nunca é recusada por saldo.
- Mudança no Estoque só aditiva. Na opção 6A, afrouxar o `CHECK` é migration com ADR (adendo à 0023) e vale só para local marcado.
- Item sem controle de estoque (serviço) não gera movimento.
- Quantidades `decimal(18,4)`.

## Permissões, eventos e tabelas
- **Permissões:** `inventory.view` (ver divergências); a de ajuste que já existe.
- **Eventos consumidos:** `SaleCompleted`, `SaleCancelled`, `SaleReturned`.
- **Tabelas:** origem de movimento nova no Estoque; `sales.stock_postings`; na 6A, `inventory.locations.allow_negative`; na 6B, `inventory.stock_shortfalls`.
- **Auditoria:** resolução de divergência.

## Telas e estados
- **Estoque → Divergências de estoque** (`/app/estoque/divergencias`): lista por loja. Vazio: "Nenhuma divergência. O estoque das lojas está batendo.". Carregando e erro.
- **Venda** (detalhe, na PDV-07): chip "Estoque: falta de saldo" com link para a divergência.
- **Ajuda:** `Ajuda/estoque-divergencias.md` (nova), explicando por que a venda não é barrada.

## Plano de testes
- **Integração (Testcontainers), obrigatória por movimentar estoque:** baixa idempotente (CA 3); falta de saldo sem recusar (CA 5); 6A ou 6B conforme a decisão; custo gravado (CA 8); estorno com falta (CA 9).
- **Regressão:** suíte do estoque da fábrica (CA 10).
- **Carga (`LoadTest`):** baixa de um dia de supermercado simulado, medindo a disputa pela numeração `MOV`.

## Fora do escopo
- Ficha técnica baixando insumos (CAT-06).
- Transferência do depósito para a loja (CPD-06).
- Reserva para o e-commerce (ECM-02).

## Pontos em aberto
- **PP-02: opção A ou B.** A story não sai de `Draft` sem a decisão.
- SPK-03: agregada ou por venda no MVP.

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
| 23/11/2026 | 1.0 | Implementada com a PP-02 decidida (opção B, configurável: `pos.sell_without_stock`, padrão desligado) e a baixa por venda do SPK-03. CA 1 a 5, 6B, 7, 8, 9 (cancelamento) e 10. O saldo da loja desce ao caixa (`GET /api/v1/pos/sync/stock`) e o caixa recusa item sem saldo com a opção desligada. Fora: devolução (PDV-06, ainda sem `SaleReturned`), chip na venda (PDV-07), teste de carga, item com lote ou série no caixa (fica `Blocked`). Adendo à ADR 0023. | Claude |
