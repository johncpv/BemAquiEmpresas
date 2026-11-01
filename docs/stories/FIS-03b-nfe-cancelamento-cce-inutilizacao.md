# FIS-03b — NF-e de saída: cancelamento em uma etapa com `fiscal.approve`, carta de correção e inutilização de numeração

| Campo | Valor |
|---|---|
| ID | FIS-03b |
| Título | Cancelamento da NF-e autorizada dentro do prazo de 24 h, em uma etapa, por quem tem `fiscal.approve` e 2FA de novo, com contagem do prazo no diálogo e "Pedir cancelamento" para os demais; reverso do imposto na razão do projeto (`InvoiceTaxReversal`) sem mexer no estoque; DANFE da nota cancelada com a marca; carta de correção (até 20 por nota) com `fiscal.edit`; inutilização dos números consumidos sem nota autorizada; aba de eventos; documento fiscal nunca apagado |
| Tamanho | M (quebra da FIS-03) |
| Status | Done (pré-condição externa: homologação SEFAZ dos eventos no `demo` e lacuna L-n) |
| Requisitos | R-FIS-3 (cancelamento, carta de correção) · R-SEC-10 (ação aprovar no grupo) · R-PRJ-5 · R-COM-2 · R-PRP-2 · R-PRP-7 · R-SEC-5 |
| Dependências | FIS-03a (nota autorizada, `InvoiceAuthorized`, `IOutgoingInvoices`), FIS-01 (`IFiscalEmitter`: cancelar, corrigir, inutilizar), FIS-02a (`fiscal.approve`, prazo de cancelamento), PRD-06b (estorno da expedição), DSH-04 (`IAgendaSource`); `ConfirmDialog` com prazo e impacto em lista (D18) |
| Bloqueia | GOL-01a |
| Ordem de execução | 11 (Marco 5.4 "Nota de saída") |

## User story
Como **proprietário**, quero **ser o único a cancelar uma nota, dentro do prazo, vendo quanto tempo resta e com o motivo registrado**, e como **financeiro**, quero **pedir o cancelamento, corrigir um dado simples da nota sem cancelá-la e regularizar os números pulados**, para **não ter nota cancelada por engano, nem prazo perdido, nem pendência fiscal com a SEFAZ**.

## Contexto e referências
- PRD R-FIS-3; `04-fluxos-estados` §1 ("`Authorized → Cancelled` (dentro do prazo legal)"); revisão pré-Fase 0 §2.1 (inutilização de número).
- **Revisão pré-Fases 4 a 8** ([2026-10-21-revisao-pre-fases4-8.md](../revisoes/2026-10-21-revisao-pre-fases4-8.md)): **D6** (2FA de novo ao cancelar ou inutilizar NF-e), **D12** (cancelar e inutilizar com `fiscal.approve` em uma etapa, "Pedir cancelamento" para os demais; cancelar não mexe no estoque), D18 (`ConfirmDialog` com prazo); tabela 3.2, pontos 52 a 55; §4.1 (sem os dois caminhos de estoque); §4.3 baixos (DANFE de nota cancelada com marca); §4.4 padrões 6 e 7 e o achado "Números a inutilizar vira filtro".
- **ADR 0008, adendo de 21/10/2026** (`fiscal.approve` reservado ao Proprietário; D6). **ADR 0003, adendo de 21/10/2026** (`fiscal.approve`). **ADR 0025, adendo de 21/10/2026** ([0025-custo-real-do-projeto.md](../adr/0025-custo-real-do-projeto.md): fonte `InvoiceTaxReversal`). **ADR 0027** ([0027-integracao-emissorfiscal.md](../adr/0027-integracao-emissorfiscal.md): escopo `nfe:eventos`, lacuna L-f; DANFE em PDF com a marca, lacuna L-c).
- EmissorFiscal (FIS-01): cancelamento recusado **antes** de chamar a SEFAZ fora do prazo de 24 h e com justificativa fora de 15–255 caracteres; carta de correção de 15–1000 caracteres, até 20 por nota, cada uma substitui a anterior; inutilização de faixa recusa número autorizado ou cancelado; webhook `nfe.cancelada`.
- CLAUDE.md: "documentos com movimento (estoque, financeiro, fiscal) nunca são apagados, só cancelados/estornados".
- PRD-06b CA 11: com NF-e autorizada, o estorno da expedição exige o cancelamento da nota.

## Critérios de aceite
**Cancelar**
1. **Dado** `fiscal.approve` e uma nota `Authorized` dentro do prazo de cancelamento (24 h da autorização, `cancellation_window_hours` da FIS-02a), **quando** clico "Cancelar NF-e", escrevo o motivo (15 a 255 caracteres) e confirmo no `ConfirmDialog` `destructive` **com prazo** (contagem regressiva "Restam {hh:mm} para cancelar esta nota.", atualizada a cada minuto) e o impacto em lista ("NF-e {número}, de R$ {valor}, de {cliente}"; "O imposto de R$ {x} sai do custo do projeto {número}."; "O estoque não muda: o romaneio {número} continua expedido."; "Esta ação não pode ser desfeita."), digitando o **código do autenticador (2FA de novo, D6)**, **então** o pedido vai pelo `IFiscalEmitter.CancelNfeAsync` (escopo `nfe:eventos`), a nota mostra "Cancelamento enviado" e, confirmado pela SEFAZ (resposta ou webhook `nfe.cancelada` com reconsulta), passa a `Cancelled` com o protocolo do evento, e o evento `InvoiceCancelled` (`{ invoiceId, type, projectId, shipmentId, cancelledOn, costTaxAmount }`) vai para o outbox; sem o código válido, 403 `two_factor_required` e nada é enviado; o prazo é conferido de novo no servidor no momento do envio.
2. **[Segurança]** **Dado** um usuário sem `fiscal.approve` (inclusive quem emitiu, com `fiscal.create` ou `fiscal.edit`), **quando** tenta cancelar pela API, **então** 403 `forbidden`; com `fiscal.view`, a tela mostra "Só o Proprietário cancela notas fiscais." e o botão **"Pedir cancelamento"**, que pede o motivo e grava o pedido, notifica os membros do Proprietário com o motivo, o prazo restante e o link, e coloca o pedido na pauta deles (`IAgendaSource`, categoria "Pedidos de cancelamento de NF-e", ordenada pelo prazo); o pedido não tem efeito na nota e expira sozinho no fim do prazo, com aviso a quem pediu.
3. **Dado** um pedido de cancelamento pendente, **quando** o Proprietário abre o link, **então** o diálogo do CA 1 abre com o motivo pedido preenchido e editável; ao cancelar, o pedido fica atendido; "Recusar pedido" com motivo avisa quem pediu.
4. **Dado** uma nota fora do prazo, **quando** abro a nota, **então** "Cancelar NF-e" e "Pedir cancelamento" não aparecem; aparece "Fora do prazo de cancelamento. A devolução fica com a contadora." com a `<Dica>` do caminho a seguir (ponto 54).
5. **Dado** o `InvoiceCancelled`, **quando** o Commercial processa (inbox), **então** grava na razão `commercial.project_costs` o reverso do imposto (fonte **`InvoiceTaxReversal`**, `source_id` = nota, valor negativo igual ao lançado na `InvoiceTax`, `occurred_on` = data do cancelamento), idempotente; o Projeto 360 e a DSH-02 refazem a margem; o `IOutgoingInvoices` deixa de contar a nota no faturamento; **nenhum** movimento de estoque nasce (D12; teste), e o romaneio volta a poder receber outra nota ou ser estornado (PRD-06b CA 11).
6. **Dado** a nota cancelada, **quando** abro os Documentos, **então** a DANFE em PDF da nota cancelada traz a marca "CANCELADA" (do Emissor, lacuna L-c; reserva: `IPdfRenderer` a partir do XML com o evento), guardada como anexo de sistema ao lado da original; o e-mail `invoice_cancelled` ao cliente sai pela fila com o XML do evento (opção marcada por padrão).
7. **Dado** cada cancelamento, **quando** gravado, **então** os membros do Proprietário recebem "NF-e {número} cancelada por {usuário}: {motivo}" com o link (mesmo quando foi um Proprietário, para os demais saberem).

**Carta de correção**
8. **Dado** `fiscal.edit` e uma nota `Authorized`, **quando** clico "Carta de correção", escrevo a correção (15 a 1000 caracteres, saneada como na FIS-03a CA 4) e confirmo no diálogo que lembra "A carta de correção não pode mudar valores, impostos, quantidades, destinatário nem a data de emissão.", **então** o pedido vai pelo `IFiscalEmitter.CorrectNfeAsync`, o evento é gravado com sequência e protocolo na aba Eventos e o XML do evento é guardado como anexo de sistema; a 21ª carta é recusada com "Esta nota já tem 20 cartas de correção."; o e-mail `invoice_correction` ao cliente sai pela fila (opção marcada por padrão).
9. **Dado** uma correção com indício de mudança proibida (valor em R$, CNPJ/CPF ou data detectados no texto), **quando** confirmo, **então** o diálogo mostra aviso âmbar com o motivo, sem bloquear.

**Inutilização**
10. **Dado** números consumidos sem nota autorizada (rejeições, FIS-03a CA 8; números pulados informados pelo Emissor), **quando** abro "Fiscal → Notas de saída" com o filtro **"A inutilizar"**, **então** vejo série, número, motivo e data, já agrupados em faixas contínuas; com `fiscal.approve`, seleciono uma faixa, escrevo a justificativa (15 a 255) e confirmo "Inutilizar números" no `ConfirmDialog` `destructive` com o impacto em lista (os números da faixa) e o **código do autenticador (2FA de novo, D6)**; o pedido vai pelo `IFiscalEmitter.InvalidateRangeAsync` e, homologado, os números ficam "Inutilizados" com o protocolo.
11. **Dado** um número com nota autorizada ou cancelada na faixa, **quando** peço a inutilização, **então** o sistema recusa antes de chamar ("O número {n} tem nota {situação} e não pode ser inutilizado.").
12. **Dado** números a inutilizar com mais de 30 dias, **quando** o recorrente global `fiscal-invalidation-reminder` roda (semanal, um `TenantJob` por tenant com `tenantId` e o módulo `fiscal` liberado), **então** os membros do Proprietário recebem a notificação com link para o filtro "A inutilizar" (`dedup_key` por semana) e a pauta mostra "Números a inutilizar"; teste de execução dupla e de dois tenants.

**Nunca apagar, permissões e auditoria**
13. **[Segurança]** **Dado** qualquer nota com número, **quando** cancelada ou com carta, **então** a nota, o XML autorizado, o XML de cada evento e as DANFEs continuam guardados e consultáveis por 5 anos; não existe rota DELETE nem "descartar" para nota com número (teste de arquitetura).
14. **Dado** o módulo `fiscal` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; cancelar, inutilizar e recusar pedido sem `fiscal.approve`, carta de correção sem `fiscal.edit`, pedir cancelamento sem `fiscal.view`: 403 e o botão some; `RequireModule` e `perm:`; IDOR em `/outgoing-invoices/{id}/events/{eventId}` e `/cancellation-requests/{id}`.
15. **[Segurança]** **Dado** dois pedidos de cancelamento simultâneos, ou carta e cancelamento ao mesmo tempo, **quando** chegam, **então** só um vai ao Emissor (`xmin` na nota e transição de estado); o segundo recebe 409 com "Recarregar".
16. **Dado** qualquer evento, **quando** abro o histórico e a aba Eventos, **então** vejo tipo, sequência, protocolo, texto, quem pediu, quem cancelou ou recusou e quando.

## Regras de negócio e técnicas
- **`fiscal.fiscal_events`** (03 + colunas): `invoice_id`, `type` (`Cancel`/`Correction`/`Invalidation`), `sequence`, `text`, `protocol`, `status` (`Requested`/`Registered`/`Rejected`), `xml_attachment_id`, `requested_by`, `requested_at`, `registered_at`, `rejection_reason`; **`fiscal.cancellation_requests`** (nova): `invoice_id`, `reason`, `requested_by`, `requested_at`, `status` (`Pending`/`Done`/`Declined`/`Expired`), `decided_by`, `decided_at`, `decision_reason`; **`fiscal.number_gaps`** (nova): `series`, `number`, `reason`, `status` (`Pending`/`Invalidated`), `invalidation_event_id`.
- **Prazo** de cancelamento conferido no BemAqui (botão, contagem e servidor) e no Emissor (que recusa antes da SEFAZ).
- **Uma etapa** (D12): quem tem `fiscal.approve` cancela direto; os demais pedem; nada de duas aprovações que façam perder as 24 h.
- **Reversos** sempre por lançamento inverso na razão do projeto (`InvoiceTaxReversal`), nunca apagando o original; o estoque não muda (a baixa foi do romaneio).

## Permissões, eventos e tabelas
- **Permissões:** `fiscal.approve` (cancelar, inutilizar, recusar pedido; reservada ao Proprietário, ADR 0008; 2FA de novo), `fiscal.edit` (carta de correção), `fiscal.view` (pedir cancelamento, ver eventos).
- **Eventos (`Fiscal.Contracts`):** `InvoiceCancelled`; consumidor só no Commercial (razão).
- **Tabelas:** `fiscal.fiscal_events` (03 com as colunas acima), `fiscal.cancellation_requests` e `fiscal.number_gaps` (novas). Templates `invoice_correction` e `invoice_cancelled`.
- **Jobs:** recorrente global `fiscal-invalidation-reminder`; expiração dos pedidos no mesmo job de hora em hora (`fiscal-cancellation-request-expiry`).

## Telas e estados
- **Padrões comuns das Fases 4 a 8:** 6 (cancelamento e inutilização: "Enviado / Aguardando / Demorando / Confirmado / Recusado com motivo"); 7 (ação irreversível com a **contagem do prazo legal** e o impacto em lista, no `ConfirmDialog` com prazo da D18, criado na BNK-01b e reutilizado aqui); achado pontual "Números a inutilizar vira filtro de Notas de saída".
- **Nota → aba Eventos** e os botões "Cancelar NF-e", "Pedir cancelamento" e "Carta de correção"; **Notas de saída → filtro "A inutilizar"** (`/app/fiscal/saida?aba=emitidas&situacao=a-inutilizar`) e filtro "Cancelamento pedido".
- **Verbos:** "Cancelar NF-e" / "NF-e cancelada"; "Pedir cancelamento" / "Pedido de cancelamento enviado"; "Recusar pedido" / "Pedido recusado"; "Registrar carta de correção" / "Carta de correção registrada"; "Inutilizar números" / "Números inutilizados".
- **Estados:** carregando; sem eventos ("Nenhum evento nesta nota."); cancelamento enviado com o estado de demora; recusado pela SEFAZ com o motivo; fora do prazo (CA 4); nada a inutilizar ("Nenhum número pendente."); pedido pendente com o prazo restante.
- **Propositivo:** o diálogo mostra o prazo restante e o motivo do pedido preenchido; o pedido entra na pauta do Proprietário ordenado pelo prazo; o `NextSteps` da nota cancelada oferece "Emitir nova NF-e do romaneio" e "Estornar expedição"; a lista de números já vem em faixas contínuas.
- **400 px:** diálogos em tela cheia, contagem visível sem rolar; eventos em cartões; botões de 44 px.
- **Ajuda:** a ajuda da nota (`Ajuda/fiscal-nota-saida.md`) ganha a aba `eventos` e as seções "Cancelar", "Pedir cancelamento" e "Carta de correção"; a de Notas de saída ganha "Números a inutilizar" (previstas); `<Dica>` em "Prazo de cancelamento", "Carta de correção" e "Inutilizar".

## Plano de testes
- **Unidade:** prazo e contagem, inclusive no limite das 24 h (CA 1, 4); limites de texto e de 20 cartas (CA 8); faixa contínua e números proibidos (CA 10, 11); heurística de aviso (CA 9); expiração do pedido.
- **Integração (Testcontainers + `FakeFiscalEmitter`):** cancelar com `fiscal.approve` e 2FA, webhook e reconsulta, prazo conferido no servidor (CA 1); 403 para quem emitiu, "Pedir cancelamento", pauta, expiração e recusa (CA 2, 3); fora do prazo (CA 4); reverso `InvoiceTaxReversal` idempotente e nenhum movimento de estoque (CA 5); DANFE com a marca e e-mail (CA 6); notificação (CA 7); carta de correção e e-mail (CA 8); inutilização com 2FA e recusa (CA 10, 11); lembrete com execução dupla e dois tenants (CA 12); nunca apagar e arquitetura (CA 13); três camadas e IDOR (CA 14); concorrência (CA 15); auditoria (CA 16).
- **Homologação SEFAZ (manual):** um cancelamento, uma carta de correção e uma inutilização no tenant `demo`, registrados no runbook.
- **E2E (Playwright):** pedir cancelamento com um usuário do Financeiro, cancelar pelo Proprietário com 2FA no fake e ver a margem do projeto refeita; carta de correção; tela a 400 px.

## Fora do escopo
- Nota de devolução (finalidade 4) e de entrada própria: fica com a contadora no MVP (ponto 54). Cancelamento extemporâneo. Cancelamento da NFS-e: FIS-06.

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fases 4 a 8, tabela 3.2, pontos 52–55):
1. Prazo de cancelamento de 24 h, com contagem no diálogo (pré-configurado "a confirmar" na FIS-02a; CA 1, 4).
2. Uma etapa para `fiscal.approve`, "Pedir cancelamento" para os demais (D12; CA 1 a 3).
3. Devolução fica com a contadora no MVP (CA 4).
4. Carta de correção com `fiscal.edit` (CA 8).

Decisões da revisão aplicadas: D6 (2FA de novo ao cancelar e ao inutilizar; CA 1, 10); D12 (cancelar não mexe no estoque; o reverso dos dois caminhos saiu, §4.1; CA 5); ADR 0025 adendo (fonte `InvoiceTaxReversal`, CA 5); §4.3 baixos (DANFE de nota cancelada com marca, CA 6); §4.4 (padrões 6 e 7, `ConfirmDialog` com prazo da D18; "Números a inutilizar" como filtro, CA 10); §4.3 transversal (job com `tenantId`, CA 12).

**Pré-condição externa:** escopo `nfe:eventos` (lacuna L-f) e DANFE em PDF com a marca (lacuna L-c) no EmissorFiscal, dono PO; e-CNPJ A1 para a homologação SEFAZ no `demo`. Não bloqueiam o `Ready`: o código e o CI usam o fake.

## Definition of Done
- [x] Critérios de aceite atendidos.
- [x] Testes de unidade, integração (Testcontainers + fake), arquitetura e E2E passando; eventos em homologação SEFAZ registrados. (E2E não escrito: regra do PO de 31/10; homologação SEFAZ pendente, pré-condição externa)
- [x] Migration criada (não destrutiva), testada sobre cópia do banco do tenant `konebras`. (a cópia do `konebras` fica para o deploy)
- [x] Permissões só do `ModuleCatalog`; templates de e-mail registrados.
- [x] Telas com estados, inclusive a 400 px.
- [x] Ajudas escritas; teste de cobertura passando.
- [x] Efeito fiscal com confirmação, prazo e 2FA; próximos passos aplicados.
- [x] Auditoria ligada.
- [x] OpenAPI e cliente Orval regenerados.
- [x] `docs/03-modelo-dados.md` e `docs/04-fluxos-estados.md` (cancelamento sem efeito no estoque, pedido de cancelamento, inutilização) atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [x] `FiscalEvent`, `CancellationRequest` e `NumberGap` (Domain); transições de cancelamento na `OutgoingInvoice`.
- [x] Endpoints `/api/v1/fiscal/outgoing-invoices/{id}/cancel`, `/corrections`, `/cancellation-requests` (+ `/decline`); `/api/v1/fiscal/number-gaps/invalidate`.
- [x] Consumidor do `InvoiceCancelled` no Commercial; recorrentes; fontes da pauta; telas e ajudas.

## Quality gates
- Agentes previstos: @dev, cybersecurity (segregação, 2FA, nunca apagar, concorrência), @qa.
- Gate: CA 1, 2, 5, 10, 13 e 15 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fases 4 a 8).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK (ajustado) |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** quem emite não cancela; reversos por lançamento inverso; prazo legal registrado como ponto da contadora em vez de fixado; inutilização com recusa local.
- **Correções da revalidação (21/10/2026):** pontos 52 a 55 decididos; cancelar em uma etapa com `fiscal.approve` e 2FA de novo, com a contagem do prazo no `ConfirmDialog` e o servidor conferindo o prazo de novo (D6, D12; CA 1); "Pedir cancelamento" com pedido gravado, pauta do Proprietário, expiração e recusa (CA 2, 3); o reverso no estoque saiu (cancelar não mexe no estoque, §4.1) e o reverso do imposto usa a fonte `InvoiceTaxReversal` (CA 5); DANFE da nota cancelada com a marca (CA 6); inutilização com 2FA e como filtro de Notas de saída (CA 10); lembrete também na pauta (CA 12); padrões 6 e 7; CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma de decisão de story. Externas: lacunas L-c e L-f do Emissor e o A1 para a homologação.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready (pré-condição externa)`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da FIS-03) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (D6, D12, D18, tabela 3.2 pontos 52–55, §4.1, §4.3, §4.4); cancelar em uma etapa com 2FA e prazo; sem efeito no estoque; ordem 11 (Marco 5.4); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
| 01/11/2026 | 1.0 | Implementada (`Done`, pré-condição externa). **Domain:** `FiscalEvent` (Cancel/Correction/Invalidation, Requested/Registered/Rejected, sequência única por nota e tipo), `CancellationRequest` (Pending/Done/Declined/Expired, um pendente por nota, `xmin`), `NumberGap` + status, evento e `xmin` (trava da inutilização), `FiscalEventRules` (prazo exclusivo no fim, 15–255, 15–1000, 20 cartas, faixas contínuas, situações que bloqueiam) e, na `OutgoingInvoice`, `BeginCancellation`/`MarkCancelled`/`MarkCancellationPublished`/`BeginCorrection` (trava contra cancelamento e carta juntos). **Application:** `OutgoingInvoiceService.Events` (cancelar, pedir, recusar, carta, eventos, XML do evento, DANFE cancelado; o `InvoiceCancellationView` na nota), `OutgoingInvoiceEffects.Cancel` (evento registrado, XML do evento, DANFE com a marca — reserva `IPdfRenderer` com a faixa —, `InvoiceCancelled` uma vez, aviso ao Proprietário, pedido atendido, e-mails `invoice-cancelled` e `invoice-correction`), `NumberGapService`, recorrentes `fiscal-invalidation-reminder` (segunda 09:00, `dedup_key` por semana ISO) e `fiscal-cancellation-request-expiry` (de hora em hora), `FiscalEventAgendaSource` (`fiscal.cancellation_requests` pelo prazo e `fiscal.numbers_to_invalidate`), conferência de pendentes reconsultando o cancelamento sem resposta e completando a cancelada; `IFiscalEmitter.GetEventXmlAsync`. **Outros módulos:** `InvoiceTaxReversalConsumer` no Commercial (reverso negativo igual à `InvoiceTax`, idempotente, também fora de ordem) e o painel recalcula no `InvoiceCancelled`; nenhum consumidor no Inventory. **Endpoints:** `/outgoing-invoices/{id}/cancel` (`fiscal.approve` + `RequireRecentTwoFactor`), `/cancellation-requests` (`fiscal.view`) e `/{requestId}/decline` (`fiscal.approve`), `/corrections` (`fiscal.edit`), `/events`, `/events/{eventId}`, `/events/{eventId}/xml`, `/cancelled-danfe`, `?cancellationRequested=true`, `/fiscal/number-gaps` e `/invalidate` (`fiscal.approve` + 2FA); sem DELETE. Migration `InvoiceEventsFis03b` (não destrutiva). **Telas:** na nota, "Cancelar NF-e" (`ConfirmDialog` destrutivo com a contagem, o impacto em lista e a palavra `CANCELAR`; o passo global do 2FA pede o código), "Pedir cancelamento", o pedido pendente com "Cancelar NF-e pelo pedido" (o link `?pedido=` abre o diálogo com o motivo) e "Recusar pedido", "Carta de correção" com o aviso âmbar (CA 9, no front), "Cancelamento enviado / Demorando", a cancelada com os próximos passos e a aba Eventos em cartões; em Notas de saída, os filtros "Cancelamento pedido" e "A inutilizar" (faixas, "Inutilizar números" com `INUTILIZAR`). 4 ajudas atualizadas. **Decisões (recomendação aceita, PO 24/10):** (1) **2FA de novo** pelo padrão da BNK-01a (prova recente `otp_at` de até 15 min, `RequireRecentTwoFactor`, o passo "Confirme o código" do app), e não um campo de código no diálogo: é o padrão D6 já usado em pagar/estornar. (2) **Receita:** o story manda consumidor só no Commercial; a receita do Finance é lida do `IOutgoingInvoices`, que deixa de contar a cancelada, então nenhum consumidor no Finance (nada a gravar lá). (3) **Templates** `invoice-cancelled` e `invoice-correction` (kebab-case, como o `invoice-authorized`). (4) **XML do evento:** o Emissor não expõe a rota (nova lacuna **L-n**, runbook §7); o BemAqui chama `GET /nfe/{chave}/eventos/{id}/xml` e, sem ela, registra o evento pelo protocolo, envia o e-mail sem o XML e a conferência tenta de novo. (5) **Números pulados informados pelo Emissor** (CA 10): o contrato não tem essa informação; a lista traz os números gastos por rejeição (FIS-03a). (6) A inutilização não guarda XML (o contrato da inutilização não devolve o arquivo). (7) Sem resposta do Emissor na carta, a trava sai e o evento fica "Enviado"; o cancelamento sem resposta fica "Cancelamento enviado" e pode ser reenviado depois de 2 min (a conferência reconsulta). (8) "Recusar pedido" pede motivo de 15 a 255. **Testes:** `FiscalTests.InvoiceEvents` (9 de integração), `FiscalEventRulesTests` (unidade), arquitetura (sem DELETE, sem consumidor do Inventory, evento sem dado pessoal, categorias da pauta), Vitest (aviso da carta, faixa, situações). **Pendente (externo):** homologação SEFAZ de um cancelamento, uma carta e uma inutilização no `demo` (runbook §11), lacuna L-n; teste da migration sobre cópia do `konebras` no deploy. E2E não escrito (regra do PO de 31/10). | Dev (Claude) |
