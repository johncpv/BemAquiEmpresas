# OFF-02 — Sincronização idempotente: lote de eventos assinado pelo aparelho, registro imutável no servidor, projeção em vendas, regras de conflito e a tela "Pendências do PDV"

| Campo | Valor |
|---|---|
| ID | OFF-02 |
| Título | Sincronização idempotente: lote de eventos assinado pelo aparelho, registro imutável no servidor, projeção em vendas, regras de conflito e a tela "Pendências do PDV" |
| Tamanho | G |
| Status | Draft (plano proposto em 15/11/2026; aguarda a aprovação do PO) |
| Fase | 11 — PDV balcão com cupom não fiscal e offline (marco MVP) |
| Requisitos | Pedido do PO de 15/11/2026 (item 5) · [Rascunho A](../adr/rascunho-A-offline.md) itens 6 e 8 · [Rascunho F](../adr/rascunho-F-escala-hospedagem.md) (ingestão em dois tempos) · [Fluxos](../04-fluxos-estados.md#8-sincronização-offline) |
| Dependências | OFF-01, PDV-03, VND-01; SPK-03 concluído; PP-26 (CPF) |
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
2. **Dado** um evento, **quando** confiro o conteúdo, **então** ele leva `event_id`, `device_id`, `register_id`, a **época do aparelho**, `seq` por caixa e época, `schema_version`, o instante do aparelho e o hash do evento anterior. **Não leva dado pessoal.**
3. **Dado** um lote recebido, **quando** o servidor grava, **então** cada evento entra em `pos.device_events` com `ON CONFLICT (event_id) DO NOTHING`; a tabela é imutável por trigger (UPDATE e DELETE falham).
4. **Dado** o mesmo lote enviado 50 vezes em paralelo, **quando** confiro, **então** cada evento existe uma vez e cada venda existe uma vez.
5. **Dado** a resposta do servidor, **quando** o aparelho a recebe, **então** sabe até qual `seq` foi confirmado e, por evento, se foi aceito, aceito com divergência ou posto em quarentena; os confirmados saem da fila.
6. **Dado** a rede caindo no meio do envio, **quando** volta, **então** o aparelho reenvia do último `seq` confirmado.

### Projeção
7. **Dado** os eventos de uma venda concluída, **quando** são gravados, **então** na mesma transação curta o `Pos` chama o contrato `ISaleIngestion` (VND-01) e é o `Sales` que grava a venda, as linhas, as partes e os pagamentos, com o id e o número gerados no aparelho, e põe o `SaleCompleted` no outbox; o `Pos` não escreve tabela do `Sales`. Estoque, financeiro e fiscal reagem depois.
8. **Dado** uma venda concluída, **quando** chega ao servidor, **então** ela **nunca é recusada**: preço diferente, saldo insuficiente ou relógio errado geram divergência, não erro.

### Conflitos
9. **Dado** uma venda com `price_list_version` mais antiga que o limite da loja, **quando** é projetada, **então** vale o preço do aparelho e nasce a divergência "Preço desatualizado", com o preço vendido e o vigente.
10. **Dado** o instante do aparelho diferente do do servidor além da tolerância, **quando** o lote chega, **então** os eventos são aceitos com a divergência "Relógio do aparelho"; as duas datas ficam gravadas. Qual delas vale para o documento fiscal depende da QC-17; para o financeiro do MVP, vale a do servidor.
11. **Dado** uma lacuna de `seq` ou um hash que não fecha com o anterior, **quando** o lote chega, **então** os eventos a partir dali entram em quarentena e nasce a divergência "Sequência quebrada"; nada é descartado.
12. **Dado** um aparelho revogado ou substituído, **quando** envia eventos que já tinha gravado, **então** o endpoint de subida os aceita (é o único que continua aberto para ele, PDV-01 CA 8) e eles entram em quarentena para revisão; não são descartados. Eventos de uma época anterior do caixa não colidem com os da época atual.
13. **Dado** um evento com `schema_version` antiga ainda suportada, **quando** chega, **então** é aceito; fora do prazo de suporte, vai para quarentena com o motivo.

### Pendências do PDV
14. **Dado** `sales.view`, **quando** abro "Pendências do PDV", **então** vejo as divergências abertas por tipo, loja e caixa, com a venda, o operador e o que divergiu.
15. **Dado** `sales.approve`, **quando** resolvo uma divergência, **então** escolho a resolução (ex.: "Aceitar como está", "Liberar da quarentena"), informo o motivo e ela fica `Resolved`, auditada. Liberar da quarentena projeta a venda.
16. **Dado** a fila de um aparelho com evento mais velho que o alerta da loja, **quando** o aparelho faz contato ou o prazo vence no servidor (aparelho sem contato), **então** o gerente recebe a notificação "Caixa 02 sem enviar vendas há N horas.".

### Dado pessoal
17. **[Segurança]** **Dado** uma venda com CPF, **quando** o lote sobe, **então** o CPF vai num bloco separado dos eventos, é gravado cifrado em `sales.sale_customer_documents` com um índice cego, e **não aparece** em `pos.device_events`, em log nem no outbox. Apagar o CPF pela retenção não quebra a cadeia de hash.

## Regras de negócio e técnicas
- Ingestão em dois tempos: gravar e projetar numa transação curta; efeitos nos outros módulos por consumidores idempotentes.
- Os endpoints de sincronização estão na lista fechada do esquema `Device` (PDV-01 CA 9); a loja vem do principal.
- `pos.device_events` é `[NotAudited]`: ele é a trilha.
- Dado pessoal no payload (CPF) é marcado `[PersonalData]` e fica fora de log e do outbox.

## Permissões, eventos e tabelas
- **Permissões:** `sales.view` (ver pendências), `sales.approve` (resolver).
- **Endpoints:** `POST /api/v1/pos/sync/events`; `GET /api/v1/pos/sync-divergences`, `POST .../{id}/resolve`, `GET .../export` (telas de retaguarda, autorizadas com `sales.*`).
- **Eventos de domínio:** `SaleCompleted`, `SaleDiscarded`.
- **Tabelas:** `pos.device_events`, `pos.sync_divergences`. As do `sales` são gravadas pelo `Sales`, via `ISaleIngestion`.

## Telas e estados
- **Loja → Pendências do PDV** (`/app/loja/pendencias`): lista com filtros. Vazio: "Nenhuma pendência. Todas as vendas chegaram sem divergência.". Carregando e erro.
- **`/pdv`:** a faixa de estado da OFF-01 passa a refletir a fila real.
- **Ajuda:** `Ajuda/loja-pendencias-pdv.md` (nova), explicando cada tipo de divergência e o que fazer.

## Plano de testes
- **Integração (Testcontainers):** 50 reenvios em paralelo (CA 4); imutabilidade (CA 3); ingestão e outbox na mesma transação (CA 7); cada conflito (CA 9 a 13); duas épocas do mesmo caixa sem colisão (CA 12); CPF ausente dos eventos (CA 17); resolver e liberar da quarentena (CA 15).
- **Arquitetura:** o `Pos` não referencia nada do `Sales` além de `Sales.Contracts`.
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

## Requisitos de segurança vindos da PDV-02
Da revisão de segurança da PDV-02 (18/11/2026), registrados lá em "Pendências de segurança":

- **Achado 4 (requisitos da OFF-02, que o servidor deve cumprir na subida):** a regra da venda roda no aparelho, que não é confiável; na subida, o servidor:
  - refaz o replay da venda a partir dos eventos, sem confiar no estado que o aparelho calculou;
  - valida `last-item` (remoção) e `remove-last` (desfazer o último lançamento que somou na linha): só valem para o último lançamento de fato, e a redução de quantidade de outra linha só vale com autorização;
  - lê o `freeRemoveLast` da configuração do servidor (a da loja ou do caixa), nunca o valor guardado no aparelho;
  - exige quem autorizou em `authorized` (o supervisor, não só o operador) e confere, pelo histórico de permissões, que essa pessoa tinha `pos.cancel_item` no instante do evento;
  - amarra o `operatorId` de cada evento ao log de sessões por PIN do aparelho (o operador tinha sessão aberta naquele caixa naquele horário);
  - gera o relatório de exceções por operador (remoções, reduções, descartes e eventos recusados na validação).

## Requisitos vindos da PDV-03 (pagamento)
A venda concluída sobe como uma entrada `pos.sale` com `status: completed`, o número, `totalCents`, `changeCents` e os eventos (`PaymentStarted`, `TenderCaptured`, `TenderChanged`, `PaymentCancelled`, `SaleCompleted`). Na subida, o servidor:
- refaz a soma: os `TenderCaptured` somam exatamente o total refeito das linhas; troco só em forma com `AllowsChange` (dinheiro) e o `amountCents` de cada pagamento nunca passa do que faltava;
- confere que a forma (`methodId`) existe, estava ativa e que a bandeira está na tabela da forma (ou é "Outra");
- recusa NSU, observação ou código de autorização com sequência de 13 dígitos ou mais (parece número de cartão) e nunca grava outro dado de cartão. Conta os dígitos depois da mesma normalização do aparelho (`looksLikeCardNumber`): NFKC, sem os caracteres `\p{Cf}`, qualquer não-alfanumérico entre dígitos tratado como separador e `\p{Nd}{13,}`; e recusa acima do tamanho máximo (140 na observação, 12 no NSU);
- amarra o `confirmedBy` de cada pagamento e o `operatorId` do `PaymentStarted` e do `SaleCompleted` (e o `recoveredBy`, quando a venda foi concluída ao reabrir o caixa) ao log de sessões por PIN do aparelho; `operatorId` ou `confirmedBy` nulos são recusados;
- projeta em `sales.sale_payments` com `integration = Manual`, "atestado pelo operador" e o Pix com `needsReconciliation` para a lista da CX-02;
- trata `TenderChanged` com `Unknown` sem captura como pendência humana (não abate da venda).

Da revisão de segurança da PDV-03 (18/11/2026, item 3), o servidor também:
- confere o troco de cada pagamento: `changeCents == receivedCents − amountCents` e `receivedCents ≥ amountCents`, todos inteiros e ≥ 0; ignora os totais do topo do payload (`totalCents`, `changeCents`) e os recalcula dos eventos;
- confere que o `PaymentStarted.totalCents` é igual ao total refeito das linhas;
- refaz a máquina de estados por `tenderId` (`Created → Pending → Captured`, mais `Failed` e `Unknown`); `Captured → Voided/Refunded` só com a autorização da PDV-04 (quem autorizou, com a permissão no instante); `tenderId` único;
- trata a venda como idempotente por `saleId` e pelo número `{loja}-{caixa}-{seq}` (reenvio não duplica; o mesmo número com outra venda é conflito);
- define ele mesmo `integration`, `operatorAttested` e `needsReconciliation` (não confia no que veio); no modo Manual, `txid`, `endToEndId`, `provider`, `acquirerId` e `authorizationCode` têm de vir nulos;
- confere que o `methodType` é o tipo da forma (`methodId`) no cadastro; `installments` só em cartão e ≤ `maxInstallments` da forma; bandeira só em cartão.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 15/11/2026 | 0.1 | Story escrita como parte do plano do PDV (proposta). Aguarda a aprovação do plano e as decisões citadas em "Pontos em aberto" para sair de `Draft`. | Claude (planejamento) |
| 15/11/2026 | 0.2 | Revista depois da revisão independente e das decisões do PO do mesmo dia ([o que mudou](../README.md#7-revisão-independente-de-15112026-achados-e-o-que-foi-aplicado)). | Claude (planejamento) |
| 18/11/2026 | 0.3 | Recebidos os requisitos de segurança da PDV-02 (achado 4 da revisão de 18/11): replay no servidor, validação de `last-item` e `remove-last`, `freeRemoveLast` do servidor, quem autorizou com a permissão no instante, `operatorId` amarrado às sessões PIN e relatório de exceções por operador. | Claude (dev) |
| 18/11/2026 | 0.4 | Recebidos os requisitos da PDV-03: a venda concluída sobe com os pagamentos; o servidor refaz a soma, confere troco, forma e bandeira, recusa dado de cartão e amarra o `confirmedBy` às sessões por PIN. | Claude (dev) |
| 18/11/2026 | 0.5 | Recebido o item 3 da revisão de segurança da PDV-03: troco coerente e totais do topo recalculados, `PaymentStarted.totalCents` conferido, máquina de estados por `tenderId` (saída de `Captured` só com a autorização da PDV-04), venda idempotente por `saleId` e número, campos de integração definidos pelo servidor, `methodType`, parcelas e bandeira conferidos, normalização do número de cartão com tamanho máximo e operador nulo recusado. | Claude (dev) |
