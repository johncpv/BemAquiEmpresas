# MIG-01d — Saldos iniciais de estoque como movimento "Saldo inicial"

| Campo | Valor |
|---|---|
| ID | MIG-01d |
| Título | Importar `llx_product_stock` e `llx_product_batch` como movimentos "Saldo inicial" por item, local e lote, com o custo médio de partida, sob confirmação explícita |
| Tamanho | P (quebra da MIG-01; o Dolibarr da Konebras está com estoque zerado, P-07) |
| Status | Draft |
| Requisitos | R-TER-5 · R-EST-4 · R-EST-5 (estoque negativo bloqueado) · R-EST-6 · R-EST-7 · R-EST-8 · R-PRP-7 |
| Dependências | MIG-01c; **EST-01** (tabelas `stock_movements` e `stock_balances` e a regra do custo médio), que é da Fase 3. **Pré-condição:** ponto em aberto 1 |
| Bloqueia | GOL-01 (carga final dos saldos) |
| Ordem de execução | 14 (depois da EST-01, ou antes se o PO antecipar o núcleo de movimentos) |

## User story
Como **almoxarife**, quero **que o saldo de cada item em cada local entre como um movimento "Saldo inicial", com o custo de partida**, para **que o estoque comece certo e toda alteração depois disso fique rastreada por movimento**.

## Contexto e referências
- Backlog MIG-01 (CA): saldos de estoque entram como movimento "Saldo inicial".
- PRD R-EST-4 a R-EST-8; `docs/04-fluxos-estados.md` §3.1 (custo médio ponderado móvel, global entre locais; o movimento grava o custo e é imutável).
- `docs/03-modelo-dados.md` → `inventory.stock_movements` (`number`, `type` Receipt/Issue/Transfer/Adjustment/Return/Production/Shipment, `item_id`, `location_id`, `lot_id`, `serial_id`, `quantity`, `unit_cost`, `total_cost`, `source_type`, `source_id`, `project_id`, `at`, `user_id` — imutável), `stock_balances` (com `UNIQUE NULLS NOT DISTINCT`, revisão §2.1), `lots`, `serials`. O tipo "Saldo inicial" **não está** na lista de tipos do 03.
- `docs/06-pendencias-riscos.md` P-07 (estoque zerado no Dolibarr).
- MIG-01a (staging, chaves externas, simulação, relatório), MIG-01c (itens, locais e `pmp` guardado na staging).

## Critérios de aceite
1. **Dado** um `llx_product_stock` com `reel` > 0 de um item e um depósito importados, **quando** rodo a carga com `--confirm-stock` e a data de corte, **então** nasce um movimento do tipo "Saldo inicial" com a quantidade na unidade de estoque, `unit_cost` = custo de partida (`pmp` do Dolibarr, `numeric(18,6)`), `total_cost` = quantidade × custo (`numeric(18,2)`), `at` = data de corte, `source_type = DolibarrImport` e o id da execução; o saldo físico do item no local passa a essa quantidade.
2. **Dado** a carga sem `--confirm-stock`, **quando** rodo (mesmo sem `--dry-run`), **então** nada é gravado e o relatório mostra os movimentos que seriam lançados, com a mensagem "Confirme com --confirm-stock para lançar os saldos." (R-PRP-7).
3. **Dado** `reel` = 0, **quando** a carga roda, **então** o registro sai como "ignorado: sem saldo"; com `reel` < 0, sai como erro "saldo negativo no Dolibarr" e não lança nada (R-EST-5).
4. **Dado** um item com "Controla lote", **quando** há `llx_product_batch`, **então** nasce um lote por `batch` (com validade quando houver) e um movimento por lote; a soma dos lotes que não fecha com o `reel` vira erro "lotes não fecham com o saldo" e o item não é lançado.
5. **Dado** um item com "Controla número de série" e saldo, **quando** a carga roda, **então** cada série do Dolibarr vira uma série em estoque (`InStock`) com movimento de 1 unidade; sem as séries, o item vai para erro "item com série sem números de série".
6. **Dado** o custo médio, **quando** os saldos de um item entram em mais de um local, **então** o custo médio do item (global entre locais, 04 §3.1) fica igual ao custo de partida; sem `pmp` (> 0), o relatório avisa "sem custo de partida" e o item entra com custo 0 só se o PO confirmar com `--allow-zero-cost` (proposta).
7. **Dado** saldos já lançados por uma execução anterior, **quando** rodo de novo com a mesma quantidade, **então** sai "sem alteração" e não há segundo movimento; com quantidade diferente, sai "conflito: saldo inicial já lançado; a diferença exige ajuste de inventário" e nada é lançado (o ajuste é feito no BemAqui com `inventory.adjust`, EST-01).
8. **Dado** um item que já tem movimento no BemAqui (entrada, saída), **quando** a carga roda, **então** o saldo inicial desse item não é lançado e sai como conflito.
9. **Dado** o relatório, **quando** confiro, **então** vejo por item e local: quantidade do Dolibarr, quantidade lançada, custo e valor total, e o total geral do estoque importado em reais, para conferência com o PO.
10. **Dado** o Dolibarr da Konebras com estoque zerado (P-07), **quando** a carga roda, **então** o relatório mostra "0 movimentos; todos os saldos estão zerados" sem erro.

## Regras de negócio e técnicas
- **Tipo novo de movimento:** `OpeningBalance`, rótulo "Saldo inicial" (**proposta**; acrescentar ao 03). Imutável como os demais; estorno só por movimento inverso (EST-01).
- **Numeração do movimento:** pelo `INumberSequence`, com o tipo definido no ponto em aberto 9 da CAD-06 (sugestão `MOV-AAAA-00001`).
- Grava só por `IStockOpeningImport` em `Inventory.Contracts` (**proposta**), que usa a mesma regra de movimento e de custo médio do EST-01.
- **Chave de idempotência:** (item, local, lote) por execução e chave externa da linha `llx_product_stock`/`llx_product_batch`.
- Quantidade `numeric(18,4)`, custo unitário `numeric(18,6)`, total `numeric(18,2)`; data de corte como `date`, `at` gravado como o início do dia da data de corte em America/Sao_Paulo, em UTC.

## Permissões, eventos e tabelas
- **Permissões:** nenhuma (CLI, MIG-01a).
- **Eventos:** os que o EST-01 publicar para movimento de estoque (nenhum novo aqui).
- **Tabelas:** `inventory.stock_movements` (+ tipo `OpeningBalance`, **proposta**), `stock_balances`, `lots`, `serials` (EST-01/EST-04); `migration.*`.

## Telas e estados
- Sem tela. Os saldos aparecem em Estoque (EST-01), com o movimento "Saldo inicial" no extrato do item.

## Plano de testes
- **Unidade:** cálculo de `total_cost` e arredondamento (CA 1); lotes que não fecham (CA 4).
- **Integração (Testcontainers PostgreSQL + MariaDB, fixture com saldos):** movimento e saldo (CA 1); sem confirmação (CA 2); zero e negativo (CA 3); lote e série (CA 4, 5); custo médio em dois locais e sem custo (CA 6); reexecução igual e diferente (CA 7); item com movimento (CA 8); relatório e total (CA 9); base zerada (CA 10).

## Fora do escopo
- Movimentos, ajustes de inventário, reservas e consultas de saldo: EST-01, EST-02, EST-05.
- Rastreabilidade de lote e série: EST-04.

## Pontos em aberto
1. **Quando executar** (pré-condição): o movimento precisa das tabelas do EST-01 (Fase 3). Opções: (a) esperar o EST-01 e rodar antes da GOL-01 (proposta, já que o Dolibarr está com estoque zerado); (b) antecipar para a Fase 1 o núcleo mínimo de movimentos e saldos do EST-01.
2. **Tipo "Saldo inicial":** novo tipo `OpeningBalance` (proposta) ou `Adjustment` com motivo "Saldo inicial".
3. **Item sem custo de partida:** entrar com custo 0 sob confirmação (proposta) ou exigir o custo antes.
4. **Data e hora do movimento:** início do dia da data de corte (proposta) ou fim do dia.
5. **Fechamento da Fase 1** (regra do backlog "não comece uma fase sem a anterior concluída"): a Fase 1 fecha com a MIG-01a a MIG-01c, e esta story fica registrada na Fase 1 com execução depois do EST-01 (proposta), ou a Fase 1 só fecha com ela.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade e integração (Testcontainers PostgreSQL + MariaDB) passando.
- [ ] `IStockOpeningImport` no `Inventory.Contracts`; tipo `OpeningBalance` no 03.
- [ ] Simulação contra a cópia da base real conferida com o PO.
- [ ] Story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `StockOpeningLoader` (`IDolibarrLoader`) com a confirmação `--confirm-stock`.
- [ ] `IStockOpeningImport` sobre a regra de movimento do EST-01.
- [ ] Fixture com saldo em dois locais, lote, série, negativo e sem custo.

## Quality gates
- Agentes previstos: @dev, @data-engineer, @qa.
- Gate: CA 2 (confirmação) e CA 7 (reexecução) obrigatórios.

## Validação do PO
Checklist do @po (Pax), 18/10/2026.

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo (cabeçalho, user story, contexto, CAs, regras, permissões/tabelas, telas, testes, fora do escopo, pontos em aberto, DoD, tarefas) | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas do CLAUDE.md) que se aplicam | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK com ressalva: depende do EST-01 (Fase 3); registrado nos pontos em aberto 1 e 5 |
| 7 | Plano de testes cobre os CAs (unidade, integração com Testcontainers, arquitetura, E2E quando cabe) | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas como proposta) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK (sem tela) |
| 10 | DoD do CLAUDE.md ajustado; pontos em aberto registrados sem decidir | OK |

- **Correções feitas na validação:** Acrescentado o ponto em aberto 5 (fechamento da Fase 1 × dependência do EST-01).
- **Pré-condições para `Ready`:** Pontos em aberto 1 (quando executar) e 5 (fechamento da Fase 1).
- **Prontidão:** 7/10. **Veredito:** GO condicional (permanece `Draft` até as pré-condições serem decididas).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 18/10/2026 | 0.1 | Story detalhada (quebra da MIG-01) | River (SM) |
| 18/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
