# FIS-03b — NF-e de saída: cancelamento com segregação, carta de correção e inutilização de numeração

| Campo | Valor |
|---|---|
| ID | FIS-03b |
| Título | Cancelamento da NF-e autorizada dentro do prazo legal, com `fiscal.approve`, motivo e efeito reverso no custo do projeto e no estoque; carta de correção (até 20 por nota) só para o que a lei permite corrigir; inutilização dos números consumidos sem nota autorizada; aba de eventos; documento fiscal nunca apagado |
| Tamanho | M (quebra da FIS-03) |
| Status | Draft |
| Requisitos | R-FIS-3 (cancelamento, carta de correção) · R-SEC-10 (ação aprovar no grupo) · R-PRJ-5 · R-COM-2 · R-PRP-7 · R-SEC-5 |
| Dependências | FIS-03a (nota autorizada, `InvoiceAuthorized`), FIS-01 (`IFiscalEmitter`: cancelar, corrigir, inutilizar), FIS-02a (`fiscal.approve`), PRD-06b (estorno da expedição) |
| Bloqueia | GOL-01a |
| Ordem de execução | 11 (Marco 5.4 "Nota de saída") |

## User story
Como **proprietário**, quero **que só quem pode cancele uma nota, dentro do prazo e com o motivo registrado**, e como **financeiro**, quero **corrigir um dado simples da nota sem cancelá-la e regularizar os números pulados**, para **não ter nota cancelada por engano nem pendência fiscal com a SEFAZ**.

## Contexto e referências
- PRD R-FIS-3; `04-fluxos-estados` §1 ("`Authorized → Cancelled` (dentro do prazo legal)"); revisão pré-Fase 0 §2.1 (inutilização de número).
- EmissorFiscal (FIS-01): cancelamento recusado **antes** de chamar a SEFAZ fora do prazo de 24 h da autorização e com justificativa fora de 15–255 caracteres; carta de correção de 15–1000 caracteres, até 20 por nota, cada uma substitui a anterior; inutilização de faixa recusa número autorizado ou cancelado; webhook `nfe.cancelada`.
- CLAUDE.md: "documentos com movimento (estoque, financeiro, fiscal) nunca são apagados, só cancelados/estornados"; ADR 0008 (`*.approve` reservada ao Proprietário).
- PRD-06b (Fase 3, rascunho): "com NF-e autorizada, o estorno [da expedição] passa a exigir o cancelamento da nota".

## Critérios de aceite
**Cancelar**
1. **Dado** `fiscal.approve` e uma nota `Authorized` dentro do prazo de cancelamento (o prazo do EmissorFiscal, 24 h, mostrado em contagem regressiva; Pontos em aberto 1), **quando** clico "Cancelar NF-e", escrevo o motivo (15 a 255 caracteres) e confirmo no diálogo `destructive` com o impacto ("Cancelar a NF-e {número}, de R$ {valor}, de {cliente}. O custo de impostos sai do projeto {número}. Esta ação não pode ser desfeita."), **então** o pedido vai pelo `IFiscalEmitter.CancelNfeAsync`, a nota fica `Processing` (cancelamento) e, confirmado pela SEFAZ (resposta ou webhook `nfe.cancelada` com reconsulta), passa a `Cancelled` com o protocolo do evento, e o evento `InvoiceCancelled` (`{ invoiceId, projectId, shipmentId, cancelledOn }`) vai para o outbox.
2. **[Segurança]** **Dado** um usuário com `fiscal.create` ou `fiscal.edit` sem `fiscal.approve` (inclusive quem emitiu), **quando** tenta cancelar, **então** 403 `forbidden`; com `fiscal.view`, o botão aparece desabilitado com o motivo "Só quem aprova notas fiscais pode cancelar." e "Pedir cancelamento" (proposta, Pontos em aberto 2), que notifica os aprovadores com o motivo e o link, sem efeito na nota.
3. **Dado** uma nota fora do prazo, **quando** abro o diálogo, **então** "Cancelar NF-e" não aparece; aparece "Fora do prazo de cancelamento. Emita uma nota de devolução." (devolução fora do MVP, Pontos em aberto 3) e o caminho a seguir com a contadora.
4. **Dado** o `InvoiceCancelled`, **quando** o Commercial processa, **então** lança o custo de impostos inverso (valor negativo, mesma origem, idempotente) e o Projeto 360 refaz a margem; **quando** o Inventory processa, **então** desfaz o vínculo da nota com o romaneio e, se a decisão da PRD-06b for "a nota baixa o estoque", lança a entrada inversa das séries; o romaneio volta a poder ser estornado ou receber outra nota.
5. **Dado** cada cancelamento, **quando** gravado, **então** os membros do Proprietário recebem "NF-e {número} cancelada por {usuário}: {motivo}" com o link (mesmo quando foi um Proprietário, para os demais saberem).

**Carta de correção**
6. **Dado** `fiscal.edit` e uma nota `Authorized`, **quando** clico "Carta de correção", escrevo a correção (15 a 1000 caracteres) e confirmo no diálogo que lembra "A carta de correção não pode mudar valores, impostos, quantidades, destinatário nem a data de emissão.", **então** o pedido vai pelo `IFiscalEmitter.CorrectNfeAsync`, o evento é gravado com sequência e protocolo na aba Eventos, e o XML do evento é guardado como anexo de sistema; a 21ª carta é recusada com "Esta nota já tem 20 cartas de correção."; o e-mail ao cliente com a carta sai pela fila (opção marcada por padrão).
7. **Dado** uma correção com indício de mudança proibida (valor, CNPJ/CPF ou data detectados no texto, proposta de heurística), **quando** confirmo, **então** o diálogo mostra aviso âmbar com o motivo, sem bloquear.

**Inutilização**
8. **Dado** números consumidos sem nota autorizada (rejeições, FIS-03a CA 7; números pulados informados pelo EmissorFiscal), **quando** abro "Fiscal → Notas de saída → Números a inutilizar", **então** vejo série, número, motivo e data; com `fiscal.approve`, seleciono uma faixa contínua, escrevo a justificativa (15 a 255) e confirmo "Inutilizar números" no diálogo `destructive`; o pedido vai pelo `IFiscalEmitter.InvalidateRangeAsync` e, homologado, os números ficam "Inutilizados" com o protocolo.
9. **Dado** um número com nota autorizada ou cancelada na faixa, **quando** peço a inutilização, **então** o sistema recusa antes de chamar ("O número {n} tem nota {situação} e não pode ser inutilizado.").
10. **Dado** números a inutilizar com mais de 30 dias (proposta), **quando** o recorrente global `fiscal-invalidation-reminder` roda (semanal), **então** quem tem `fiscal.approve` recebe a notificação com link para a lista (`dedup_key` por semana).

**Nunca apagar, permissões e auditoria**
11. **[Segurança]** **Dado** qualquer nota com número, **quando** cancelada ou com carta, **então** a nota, o XML autorizado, o XML de cada evento e a DANFE continuam guardados e consultáveis por 5 anos; não existe rota DELETE nem "descartar" para nota com número (teste de arquitetura).
12. **Dado** o módulo `fiscal` não liberado, **quando** chamo, **então** 403 `module_not_released`; cancelar e inutilizar sem `fiscal.approve`, carta de correção sem `fiscal.edit`: 403; `RequireModule` e `perm:`; IDOR em `/outgoing-invoices/{id}/events/{eventId}`.
13. **[Segurança]** **Dado** dois pedidos de cancelamento simultâneos ou carta e cancelamento ao mesmo tempo, **quando** chegam, **então** só um vai ao EmissorFiscal (`xmin` na nota e transição de estado); o segundo recebe 409 com "Recarregar".
14. **Dado** qualquer evento, **quando** abro o histórico e a aba Eventos, **então** vejo tipo, sequência, protocolo, texto, quem pediu, quem aprovou e quando.

## Regras de negócio e técnicas
- **`fiscal.fiscal_events`** (03 + proposta): `invoice_id`, `type` (`Cancel`/`Correction`/`Invalidation`), `sequence`, `text`, `protocol`, `status` (`Requested`/`Registered`/`Rejected`), `xml_attachment_id`, `requested_by`, `requested_at`, `registered_at`, `rejection_reason`; **`fiscal.number_gaps`** (proposta): `series`, `number`, `reason`, `status` (`Pending`/`Invalidated`), `invalidation_event_id`.
- **Prazo** de cancelamento conferido no BemAqui (para o botão e o diálogo) e no EmissorFiscal (que recusa antes da SEFAZ).
- **Reversos** sempre por lançamento inverso (custo, estoque), nunca apagando o original.

## Permissões, eventos e tabelas
- **Permissões:** `fiscal.approve` (cancelar, inutilizar; reservada ao Proprietário, ADR 0008), `fiscal.edit` (carta de correção), `fiscal.view` (pedir cancelamento, ver eventos).
- **Eventos (`Fiscal.Contracts`):** `InvoiceCancelled`; consumidores no Commercial e no Inventory.
- **Tabelas:** `fiscal.fiscal_events` (03 com as colunas acima), `fiscal.number_gaps` (proposta). Templates `invoice_correction` e `invoice_cancelled`.
- **Jobs:** recorrente global `fiscal-invalidation-reminder`.

## Telas e estados
- **Nota → aba Eventos** e os botões "Cancelar NF-e", "Carta de correção" e "Pedir cancelamento"; **Números a inutilizar** (`/app/fiscal/saida/numeros-a-inutilizar`).
- **Verbos:** "Cancelar NF-e" / "NF-e cancelada"; "Registrar carta de correção" / "Carta de correção registrada"; "Inutilizar números" / "Números inutilizados"; "Pedir cancelamento" / "Pedido de cancelamento enviado".
- **Estados:** carregando; sem eventos ("Nenhum evento nesta nota."); processando o cancelamento com o estado de demora; recusado pela SEFAZ com o motivo; nada a inutilizar ("Nenhum número pendente.").
- **Propositivo:** o diálogo mostra o prazo restante; o `NextSteps` da nota cancelada oferece "Emitir nova NF-e do romaneio"; a lista de números já vem agrupada em faixas contínuas.
- **400 px:** diálogos em tela cheia; eventos em cartões.
- **Ajuda:** a ajuda da nota ganha a aba `eventos` e as seções "Cancelar" e "Carta de correção"; `Ajuda/fiscal-numeros-a-inutilizar.md`; `<Dica>` em "Prazo de cancelamento", "Carta de correção" e "Inutilizar".

## Plano de testes
- **Unidade:** prazo e contagem (CA 1, 3); limites de texto e de 20 cartas (CA 6); faixa contínua e números proibidos (CA 8, 9); heurística de aviso (CA 7).
- **Integração (Testcontainers + fake do EmissorFiscal):** cancelar com `fiscal.approve`, webhook e reconsulta (CA 1); 403 para quem emitiu e "Pedir cancelamento" (CA 2); fora do prazo (CA 3); reversos no Commercial e no Inventory, idempotentes (CA 4); notificação (CA 5); carta de correção e e-mail (CA 6); inutilização e recusa (CA 8, 9); lembrete com execução dupla e dois tenants (CA 10); nunca apagar e arquitetura (CA 11); três camadas e IDOR (CA 12); concorrência (CA 13); auditoria (CA 14).
- **Homologação SEFAZ (manual):** um cancelamento, uma carta de correção e uma inutilização no tenant de teste, registrados no runbook.
- **E2E (Playwright):** cancelar uma nota no fake e ver a margem do projeto refeita; carta de correção; tela a 400 px.

## Fora do escopo
- Nota de devolução (finalidade 4) e de entrada própria: fora do MVP (Pontos em aberto 3). Cancelamento extemporâneo.

## Pontos em aberto
1. **Prazo de cancelamento** (contadora): o EmissorFiscal usa 24 h; confirmar o prazo aplicável à Konebras na SEFAZ-SP.
2. **Cancelamento em duas etapas** (PO/segurança): proposta: um passo só, por quem tem `fiscal.approve`, com "Pedir cancelamento" para os demais; alternativa: pedido + aprovação por outra pessoa (risco de perder o prazo de 24 h).
3. **Devolução fora do prazo** (PO/contadora): o MVP precisa da nota de devolução de venda, ou fica com a contadora?
4. **Carta de correção com `fiscal.edit` ou `fiscal.approve`** (PO): proposta `edit`, porque não muda valor nem imposto.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers + fake), arquitetura e E2E passando; eventos em homologação SEFAZ registrados.
- [ ] Migration criada (não destrutiva), testada sobre cópia do banco do tenant `konebras`.
- [ ] Permissões só do `ModuleCatalog`; templates de e-mail registrados.
- [ ] Telas com estados, inclusive a 400 px.
- [ ] Ajudas escritas; teste de cobertura passando.
- [ ] Efeito fiscal com confirmação; próximos passos aplicados.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` e `docs/04-fluxos-estados.md` (cancelamento e inutilização) atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `FiscalEvent` e `NumberGap` (Domain); transições de cancelamento na `OutgoingInvoice`.
- [ ] Endpoints `/api/v1/fiscal/outgoing-invoices/{id}/cancel`, `/corrections`, `/cancel-requests`; `/api/v1/fiscal/number-gaps/invalidate`.
- [ ] Consumidores do `InvoiceCancelled`; recorrente `fiscal-invalidation-reminder`; telas e ajudas.

## Quality gates
- Agentes previstos: @dev, cybersecurity (segregação, nunca apagar, concorrência), @qa.
- Gate: CA 1, 2, 4, 11 e 13 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026.

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto registrados sem decidir | OK |

- **Correções feitas na validação:** quem emite não cancela (CA 2); reversos por lançamento inverso (CA 4); prazo legal registrado como ponto da contadora em vez de fixado (ponto 1); inutilização com recusa local (CA 9).
- **Pré-condições para `Ready`:** pontos em aberto 1 e 2; `fiscal.approve` decidida na FIS-02a.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da FIS-03) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
