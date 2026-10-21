# CMP-03b — PDF e envio do pedido de compra ao fornecedor

| Campo | Valor |
|---|---|
| ID | CMP-03b |
| Título | PDF do pedido de compra com o layout do tenant (`IPdfRenderer`, QuestPDF, ADR 0022) e envio por e-mail ao fornecedor pela fila, com o PDF anexado depois da varredura; o pedido aprovado passa a `Sent` |
| Tamanho | P (quebra da CMP-03) |
| Status | Draft |
| Requisitos | R-CMP-3 (envio por e-mail em PDF) · R-COM-1 (e-mail por fila, com templates e histórico) · R-COM-3 (PDF guardado como anexo) · R-PRP-2 · R-SEC-5 |
| Dependências | CMP-03a (pedido aprovado), COM-03 (`IPdfRenderer`, `StoreGeneratedAsync`, e-mail com anexo e cópia, espera do `Clean`), CAD-06 (perfil e logo da empresa), CAD-01a (contato do fornecedor) |
| Bloqueia | — |
| Ordem de execução | 11 (Marco 3.3 "Comprar o que falta") |

## User story
Como **comprador**, quero **mandar o pedido aprovado ao fornecedor em PDF direto do sistema**, para **o fornecedor receber um documento formal com tudo o que foi combinado e o envio ficar registrado**.

## Contexto e referências
- PRD R-CMP-3 ("envio por e-mail em PDF"), R-COM-1, R-COM-3.
- ADR 0022 (QuestPDF atrás de `IPdfRenderer`, Archivo embutida, teste na imagem Alpine, limites de 300 itens e 2.000 caracteres, só o logo `Clean`); ADR 0019 §7 (`StoreGeneratedAsync`, e-mail com anexo e cópia até 3, espera do `Clean`).
- COM-03 (o mesmo fluxo para o orçamento: `quote_dispatches`, limite diário, mensagem do usuário nunca vira template). Pré-condição externa da COM-03 já cumprida: SPF, DKIM e DMARC.
- `04-fluxos-estados` §1 "Pedido de compra": `Approved → Sent`.

## Critérios de aceite
1. **Dado** `purchasing.view` e um pedido `Approved`, `Sent`, `PartiallyReceived` ou `Received`, **quando** clico "Baixar PDF", **então** o PDF é gerado pelo `IPdfRenderer` com o layout `PurchaseOrderPdfDocument` (logo e dados da empresa da CAD-06, número, data, fornecedor e CNPJ, condição de pagamento, previsão de entrega, local de entrega — o endereço da empresa —, itens com código do fornecedor quando houver (`IItemSupplierCodes`), quantidade e unidade do fornecedor convertidas, preço, total da linha, frete, IPI e total geral, observações), em pt-BR, com `tabular-nums`; pedido em `Draft` ou `PendingApproval` não gera PDF (409 "Aprove o pedido antes de enviar.").
2. **Dado** `purchasing.edit` e um pedido `Approved`, **quando** clico "Enviar ao fornecedor", **então** o diálogo vem com o destinatário padrão (contato do fornecedor que recebe cobrança ou o e-mail do cadastro, R-PRP-1), até 3 cópias, assunto "Pedido de compra {número} — {empresa}" e uma mensagem opcional (até 1.000 caracteres, texto puro, nunca template); ao confirmar, o PDF é guardado como arquivo do sistema (`StoreGeneratedAsync`, dono `PurchaseOrder`), o e-mail vai para a fila com o anexo, o envio é registrado (`purchase_order_dispatches`, proposta) e o pedido passa a `Sent`; a mensagem diz "Pedido enviado para a fila de e-mail"; com `Idempotency-Key`.
3. **Dado** o envio, **quando** o `SendEmailJob` roda, **então** só manda depois do anexo `Clean` (reagenda até 10 vezes) e marca `Failed` com o motivo se o anexo for `Infected`/`Failed` (ADR 0019 §7); a aba Documentos mostra cada envio com destinatário, data, situação do e-mail e o PDF.
4. **Dado** um pedido `Sent`, **quando** clico "Reenviar", **então** gera um PDF novo e outro registro de envio (sem mudar o estado); o limite é de 50 envios por dia no tenant somando orçamentos e pedidos (proposta, como a COM-03).
5. **Dado** os limites da ADR 0022, **quando** o pedido passa de 300 linhas ou a observação de 2.000 caracteres, **então** 422 antes de gerar.
6. **[Segurança]** **Dado** o PDF, **quando** gerado, **então** não contém custo médio, margem, projeto nem dado de outro fornecedor (teste de extração de texto); o e-mail não leva link para o sistema.
7. **Dado** o módulo `purchasing` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; baixar sem `purchasing.view`, enviar sem `purchasing.edit`: 403. `RequireModule` e `perm:`; IDOR em `/purchase-orders/{id}/dispatches/{dispatchId}`; destinatário validado como e-mail e limitado a 3 cópias.
8. **Dado** o envio, **quando** abro "Histórico de alterações", **então** vejo a passagem para `Sent` com usuário e data; os endereços aparecem mascarados para quem não tem `parties.view` (`[PersonalData]`).

## Regras de negócio e técnicas
- **Layout** `PurchaseOrderPdfDocument` no Purchasing; nenhum outro arquivo do módulo referencia QuestPDF (ADR 0022).
- **Dono de anexo `PurchaseOrder`** (proposta): leitura `purchasing.view`, escrita `purchasing.edit`, pdf, png, jpg, xlsx, docx até 10 MB, `OwnerExists`; o PDF gerado tem `origin = System`.
- **`purchase_order_dispatches`** (proposta): `purchase_order_id`, `email_log_id`, `attachment_id`, `to` e `cc` (`[PersonalData]`), `sent_by`, `created_at`.
- **Template de e-mail** `purchase_order_dispatch` (Fluid), com o texto do usuário inserido como valor escapado.

## Permissões, eventos e tabelas
- **Permissões:** `purchasing.view` (PDF), `purchasing.edit` (enviar).
- **Eventos:** nenhum entre módulos.
- **Tabelas:** `purchasing.purchase_order_dispatches` (proposta); anexos em `shared.attachments`; `shared.email_logs`.

## Telas e estados
- **Pedido → ações** "Baixar PDF", "Enviar ao fornecedor" e "Reenviar"; **aba Documentos** com envios e anexos.
- **Verbos:** "Enviar ao fornecedor" / "Pedido enviado para a fila de e-mail".
- **Estados:** carregando; vazio da aba Documentos ("Nenhum envio. Envie o pedido ao fornecedor depois de aprovado." + "Enviar ao fornecedor"); erro do envio dentro do diálogo; estado do e-mail ("Na fila", "Aguardando verificação do anexo", "Enviado", "Falhou: {motivo}" com "Reenviar").
- **Propositivo:** depois do aprovado, o `NextSteps` "Enviar ao fornecedor" (CMP-03a); destinatário preenchido.
- **400 px:** diálogo em tela cheia; envios em cartões.
- **Ajuda:** `Ajuda/compras-pedido-documentos.md` (aba `documentos`); `<Dica>` em "Cópia".

## Plano de testes
- **Unidade:** montagem do modelo do PDF (conversão para a unidade do fornecedor, totais).
- **Integração (Testcontainers):** PDF na imagem Alpine com texto extraído esperado e sem custo (CA 1, 6); envio com anexo, espera do `Clean` e falha com anexo infectado (CA 2, 3); reenvio e limite diário (CA 4); limites da ADR 0022 (CA 5); três camadas e IDOR (CA 7); auditoria (CA 8).
- **E2E (Playwright):** aprovar, enviar ao fornecedor e ver o envio na aba Documentos.

## Fora do escopo
- Portal do fornecedor e confirmação do pedido pelo fornecedor: fora do MVP.
- Cotação por e-mail: CMP-02 (ponto em aberto 1).

## Pontos em aberto
1. **Limite diário de envios** (PO): proposta: o mesmo limite de 50 por dia da COM-03, somado; alternativa: limite separado.
2. **Endereço de entrega** (PO): proposta: o endereço da empresa (CAD-06); compra entregue na obra fica para quando houver o vínculo com o endereço da obra do projeto.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers, PDF na imagem Alpine) e E2E passando.
- [ ] Migration criada (não destrutiva).
- [ ] Permissões só do `ModuleCatalog`; dono de anexo `PurchaseOrder` registrado; template de e-mail registrado.
- [ ] Telas com estados, inclusive a 400 px.
- [ ] Ajuda da aba; teste de cobertura passando.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `PurchaseOrderPdfDocument` e `PurchaseOrderDispatchService`.
- [ ] Template `purchase_order_dispatch`; dono de anexo `PurchaseOrder`.
- [ ] Endpoints `/api/v1/purchasing/purchase-orders/{id}/pdf` e `/dispatches`; aba Documentos e ajuda.

## Quality gates
- Agentes previstos: @dev, @qa.
- Gate: CA 1, 3 e 6 obrigatórios.

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

- **Correções feitas na validação:** reaproveitamento explícito da infraestrutura da COM-03 (PDF, arquivo do sistema, espera do `Clean`), sem código novo de e-mail; PDF sem custo nem projeto (CA 6).
- **Pré-condições para `Ready`:** nenhuma bloqueante.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Draft` até a aprovação do PO.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da CMP-03) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
