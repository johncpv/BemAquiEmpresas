# OFF-02 — Sincronização idempotente: lote de eventos assinado pelo aparelho, registro imutável no servidor, projeção em vendas, regras de conflito e a tela "Pendências do PDV"

| Campo | Valor |
|---|---|
| ID | OFF-02 |
| Título | Sincronização idempotente: lote de eventos assinado pelo aparelho, registro imutável no servidor, projeção em vendas, regras de conflito e a tela "Pendências do PDV" |
| Tamanho | G |
| Status | Draft (plano proposto em 15/11/2026; aguarda a aprovação do PO) |
| Fase | 11 — PDV balcão com cupom não fiscal e offline (marco MVP) |
| Requisitos | Pedido do PO de 15/11/2026 (item 5) · [Rascunho A](../adr/rascunho-A-offline.md) itens 6 e 8 · [Rascunho F](../adr/rascunho-F-escala-hospedagem.md) (ingestão em dois tempos) · [Fluxos](../04-fluxos-estados.md#8-sincronização-offline) |
| Dependências | OFF-01, PDV-03; SPK-03 concluído |
| Bloqueia | CX-03, PDV-05, OFF-03, PDV-07 |
| Paridade com o mercado | Requisitos mínimos 1 do [benchmark](../00-benchmark-mercado.md#7-matriz-de-paridade-cada-requisito-mínimo-e-a-story-que-o-cobre) |
| Ordem de execução | 7 (Fase 11) |

## User story
Como **dono**, quero **que toda venda feita no caixa chegue ao sistema uma vez só, mesmo que a internet caia e volte dez vezes**, para **confiar no número de vendas e no dinheiro**.

## Contexto e referências
- O `IdempotencyFilter` não serve aqui: o escopo inclui o usuário, e um reenvio em outra sessão dá 422.
- O `NumberSequenceService` não existe sem rede; o número da venda é do aparelho.

## Critérios de aceite
### Subida
1. **Dado** eventos na fila de saída, **quando** há rede, **então** o aparelho envia lotes a `POST /api/v1/pos/sync/events`, assinados com a chave do aparelho, sem precisar de operador com sessão.
2. **Dado** um evento, **quando** confiro o conteúdo, **então** ele leva `event_id`, `device_id`, `register_id`, `seq` por caixa, `schema_version`, o instante do aparelho e o hash do evento anterior.
3. **Dado** um lote recebido, **quando** o servidor grava, **então** cada evento entra em `pos.device_events` com `ON CONFLICT (event_id) DO NOTHING`; a tabela é imutável por trigger (UPDATE e DELETE falham).
4. **Dado** o mesmo lote enviado 50 vezes em paralelo, **quando** confiro, **então** cada evento existe uma vez e cada venda existe uma vez.
5. **Dado** a resposta do servidor, **quando** o aparelho a recebe, **então** sabe até qual `seq` foi confirmado e, por evento, se foi aceito, aceito com divergência ou posto em quarentena; os confirmados saem da fila.
6. **Dado** a rede caindo no meio do envio, **quando** volta, **então** o aparelho reenvia do último `seq` confirmado.

### Projeção
7. **Dado** os eventos de uma venda concluída, **quando** são gravados, **então** na mesma transação curta a venda é projetada em `sales.sales`, `sale_lines`, `sale_parts` e `sale_payments`, com o id e o número gerados no aparelho, e o `SaleCompleted` vai ao outbox; estoque, financeiro e fiscal reagem depois.
8. **Dado** uma venda concluída, **quando** chega ao servidor, **então** ela **nunca é recusada**: preço diferente, saldo insuficiente ou relógio errado geram divergência, não erro.

### Conflitos
9. **Dado** uma venda com `price_list_version` mais antiga que o limite da loja, **quando** é projetada, **então** vale o preço do aparelho e nasce a divergência "Preço desatualizado", com o preço vendido e o vigente.
10. **Dado** o instante do aparelho diferente do do servidor além da tolerância, **quando** o lote chega, **então** os eventos são aceitos com a divergência "Relógio do aparelho"; a data que vale para o financeiro e o fiscal é a do servidor.
11. **Dado** uma lacuna de `seq` ou um hash que não fecha com o anterior, **quando** o lote chega, **então** os eventos a partir dali entram em quarentena e nasce a divergência "Sequência quebrada"; nada é descartado.
12. **Dado** um aparelho revogado, **quando** envia eventos gravados antes da revogação, **então** eles entram em quarentena para revisão, não são descartados.
13. **Dado** um evento com `schema_version` antiga ainda suportada, **quando** chega, **então** é aceito; fora do prazo de suporte, vai para quarentena com o motivo.

### Pendências do PDV
14. **Dado** `sales.view`, **quando** abro "Pendências do PDV", **então** vejo as divergências abertas por tipo, loja e caixa, com a venda, o operador e o que divergiu.
15. **Dado** `sales.approve`, **quando** resolvo uma divergência, **então** escolho a resolução (ex.: "Aceitar como está", "Liberar da quarentena"), informo o motivo e ela fica `Resolved`, auditada. Liberar da quarentena projeta a venda.
16. **Dado** a fila de um aparelho com evento mais velho que o alerta da loja, **quando** o aparelho faz contato ou o prazo vence no servidor (aparelho sem contato), **então** o gerente recebe a notificação "Caixa 02 sem enviar vendas há N horas.".

## Regras de negócio e técnicas
- Ingestão em dois tempos: gravar e projetar numa transação curta; efeitos nos outros módulos por consumidores idempotentes.
- Os endpoints de sincronização estão na lista fechada do esquema `Device` (PDV-01 CA 9); a loja vem do principal.
- `pos.device_events` é `[NotAudited]`: ele é a trilha.
- Dado pessoal no payload (CPF) é marcado `[PersonalData]` e fica fora de log e do outbox.

## Permissões, eventos e tabelas
- **Permissões:** `sales.view` (ver pendências), `sales.approve` (resolver).
- **Endpoints:** `POST /api/v1/pos/sync/events`; `GET /api/v1/sales/sync-divergences`, `POST .../{id}/resolve`, `GET .../export`.
- **Eventos de domínio:** `SaleCompleted`, `SaleDiscarded`.
- **Tabelas:** `pos.device_events`, `pos.sync_divergences`; as do `sales`.

## Telas e estados
- **Loja → Pendências do PDV** (`/app/loja/pendencias`): lista com filtros. Vazio: "Nenhuma pendência. Todas as vendas chegaram sem divergência.". Carregando e erro.
- **`/pdv`:** a faixa de estado da OFF-01 passa a refletir a fila real.
- **Ajuda:** `Ajuda/loja-pendencias-pdv.md` (nova), explicando cada tipo de divergência e o que fazer.

## Plano de testes
- **Integração (Testcontainers):** 50 reenvios em paralelo (CA 4); imutabilidade (CA 3); projeção e outbox na mesma transação (CA 7); cada conflito (CA 9 a 13); resolver e liberar da quarentena (CA 15).
- **Vitest:** fila de saída, reenvio a partir do `seq` confirmado, cadeia de hash.
- **Carga (`LoadTest`):** 20 caixas simulados, como no SPK-03.

## Fora do escopo
- Baixa de estoque (PDV-05) e lançamentos financeiros (CX-03).
- Sessão de caixa e PIN sem rede (OFF-03).
- Particionamento de `device_events`.

## Pontos em aberto
- O prazo de suporte a `schema_version` antiga (proposta: duas versões).
- O limite de idade da tabela de preço e a tolerância de relógio, por loja (proposta: herdar os 2 min da ADR 0026 para o relógio).

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
