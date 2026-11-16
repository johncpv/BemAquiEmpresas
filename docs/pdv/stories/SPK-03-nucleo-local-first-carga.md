# SPK-03 — Spike: núcleo local-first sob carga (IndexedDB com 50 mil itens em aparelho fraco, registro de eventos e fila, lote com 20 caixas simulados, baixa agregada de estoque, queda de energia no meio da venda)

| Campo | Valor |
|---|---|
| ID | SPK-03 |
| Título | Spike: núcleo local-first sob carga (IndexedDB com 50 mil itens em aparelho fraco, registro de eventos e fila, lote com 20 caixas simulados, baixa agregada de estoque, queda de energia no meio da venda) |
| Tamanho | M |
| Status | Draft (plano proposto em 15/11/2026; aguarda a aprovação do PO) |
| Fase | 10 — Fundação: perfil de negócio, catálogo, pagamentos, núcleo de vendas e spikes |
| Requisitos | Pedido do PO de 15/11/2026 (itens 4 e 5) · [Rascunho A](../adr/rascunho-A-offline.md) · [Rascunho F](../adr/rascunho-F-escala-hospedagem.md) |
| Dependências | Um tablet Android de entrada e um PC antigo para teste |
| Bloqueia | OFF-01 (armazenamento), OFF-02 (protocolo), PDV-05 (forma da baixa); a estimativa de esforço do MVP |
| Paridade com o mercado | Requisitos mínimos prepara 1 e 22 do [benchmark](../00-benchmark-mercado.md#7-matriz-de-paridade-cada-requisito-mínimo-e-a-story-que-o-cobre) |
| Ordem de execução | Fase 10, em paralelo (prazo fechado: 1 a 2 semanas) |

## Pergunta do spike
Como **arquiteto**, quero **medir o núcleo local-first em aparelho fraco e sob carga de vários caixas**, para **escolher o armazenamento, fixar o protocolo de sincronização e saber quanto o offline custa de verdade**.

## Contexto e referências
- Metas do [PRD](../01-PRD.md): item adicionado em menos de 100 ms; venda de 3 itens em dinheiro em até 10 segundos.
- As estimativas de esforço do offline não fecham entre as análises ([backlog, Esforço](../05-backlog.md#esforço)). Este spike é o que tira a dúvida.
- O rascunho A propõe IndexedDB com Dexie atrás de uma porta `LocalStore`, com SQLite-WASM em OPFS como alternativa.

## Critérios de aceite (o que o spike tem de responder, com evidência)
1. **Dado** 50 mil itens com código de barras e preço no `LocalStore`, **quando** busco por código exato e por trecho do nome no tablet de entrada e no PC antigo, **então** o relatório traz p50 e p95 de cada busca, com IndexedDB e com SQLite-WASM.
2. **Dado** a carga inicial de 50 mil itens, **quando** baixo a foto e depois um delta de 500 itens, **então** ficam registrados o tempo e o tamanho de cada um.
3. **Dado** `navigator.storage.persist()`, **quando** peço armazenamento persistente em cada plataforma, **então** fica registrado se foi concedido e o que acontece com os dados quando não é.
4. **Dado** uma venda em andamento, **quando** fecho a aba, mato o navegador e corto a energia do aparelho, **então** ao reabrir a venda está como estava, e nenhum evento concluído se perdeu.
5. **Dado** duas abas do `/pdv` no mesmo aparelho, **quando** as duas tentam escrever, **então** só uma escreve (Web Locks) e a outra avisa.
6. **Dado** um endpoint de lote protótipo e 20 caixas simulados enviando 200 vendas cada, com 30% de reenvios, **quando** rodo a carga, **então** não há venda duplicada nem perdida, e o relatório traz o tempo por lote e a ocupação do banco.
7. **Dado** os mesmos eventos, **quando** a baixa de estoque é feita por venda e depois agregada por item, caixa e janela, **então** o relatório compara o número de movimentos, a disputa pela numeração `MOV` e pela linha do item, e o tempo total.
8. **Dado** tudo acima, **então** o relatório fecha com: (a) IndexedDB ou SQLite-WASM; (b) o protocolo de sincronização confirmado ou corrigido; (c) baixa agregada ou por venda no MVP; (d) estimativa revista das stories OFF-01 a OFF-03.

## Regras
- Prazo fechado.
- Os números são medidos, com o modelo do aparelho e a versão do navegador anotados.

## Entregáveis
- Relatório em `docs/revisoes/`.
- Rascunhos A e F corrigidos.
- Tamanho das stories OFF-01 a OFF-03 reestimado no backlog.

## Fora do escopo
- Autenticação do aparelho e PIN sem rede (OFF-03).
- Particionamento de tabelas (só se a carga pedir).

## Pontos em aberto
- Nenhum que bloqueie o spike.

## Definition of Done
- [ ] Todas as perguntas respondidas com evidência, ou marcadas "não respondida", com o motivo.
- [ ] Relatório publicado em `docs/revisoes/`.
- [ ] Rascunhos de ADR e backlog corrigidos com o que foi medido.
- [ ] Código do spike fora da `main` de produto.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 15/11/2026 | 0.1 | Story escrita como parte do plano do PDV (proposta). Aguarda a aprovação do plano e as decisões citadas em "Pontos em aberto" para sair de `Draft`. | Claude (planejamento) |
| 15/11/2026 | 0.2 | Revista depois da revisão independente e das decisões do PO do mesmo dia ([o que mudou](../README.md#7-revisão-independente-de-15112026-achados-e-o-que-foi-aplicado)). | Claude (planejamento) |
