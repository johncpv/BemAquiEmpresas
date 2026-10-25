# CMP-03b — PDF e envio do pedido de compra ao fornecedor

| Campo | Valor |
|---|---|
| ID | CMP-03b |
| Título | PDF do pedido de compra com o layout do tenant (`IPdfRenderer`, QuestPDF, ADR 0022) e envio por e-mail ao fornecedor pela fila, com o PDF anexado depois da varredura, limite diário por tenant e por usuário e confirmação de destinatário fora dos contatos; o pedido aprovado passa a `Sent` |
| Tamanho | P (quebra da CMP-03) |
| Status | Done |
| Requisitos | R-CMP-3 (envio por e-mail em PDF) · R-COM-1 (e-mail por fila, com templates e histórico) · R-COM-3 (PDF guardado como anexo) · R-PRP-2 · R-SEC-5 |
| Dependências | CMP-03a (pedido aprovado, dono de anexo `PurchaseOrder`), COM-03 (`IPdfRenderer`, `StoreGeneratedAsync`, e-mail com anexo e cópia, espera do `Clean`) |
| Bloqueia | — |
| Ordem de execução | 18 (Marco 3.3 "Comprar o que falta") |

## User story
Como **comprador**, quero **mandar o pedido aprovado ao fornecedor em PDF direto do sistema**, para **o fornecedor receber um documento formal com tudo o que foi combinado e o envio ficar registrado**.

## Contexto e referências
- PRD R-CMP-3 ("envio por e-mail em PDF"), R-COM-1, R-COM-3.
- ADR 0022 (QuestPDF atrás de `IPdfRenderer`, Archivo embutida, teste na imagem Alpine, limites de 300 itens e 2.000 caracteres, só o logo `Clean`); ADR 0019 §7 (`StoreGeneratedAsync`, e-mail com anexo e cópia até 3, espera do `Clean`) e o adendo de 21/10/2026 (dono `PurchaseOrder`: leitura `purchasing.view`, escrita `purchasing.edit`, pdf, png e jpg até 10 MB).
- COM-03 (o mesmo fluxo para o orçamento: `quote_dispatches`, limite de 50 por dia, endereço puro, aviso de destinatário fora dos contatos, mensagem do usuário como valor escapado). Pré-condição externa da COM-03 já cumprida: SPF, DKIM e DMARC.
- CAD-06 (perfil, endereço e logo da empresa) e CAD-01a (contatos do fornecedor), entregues.
- `04-fluxos-estados` §1 "Pedido de compra": `Approved → Sent`.
- Revisão `docs/revisoes/2026-10-21-revisao-pre-fase3.md`: tabela 1.1 itens 45–46, §2.2 CMP-03b, §4 (ordem 18 do Marco 3.3).

## Critérios de aceite
1. **Dado** `purchasing.view` e um pedido `Approved`, `Sent`, `PartiallyReceived` ou `Received`, **quando** clico "Baixar PDF", **então** o PDF é gerado pelo `IPdfRenderer` com o layout `PurchaseOrderPdfDocument` (logo e dados da empresa da CAD-06, número, data, fornecedor e CNPJ, condição de pagamento, previsão de entrega, local de entrega = o endereço da empresa (CAD-06), itens com código do fornecedor quando houver (`IItemSupplierCodes`), quantidade e unidade do fornecedor convertidas, preço, total da linha, frete, IPI e total geral, observações), em pt-BR, com `tabular-nums`; pedido em `Draft` ou `PendingApproval` não gera PDF (409 "Aprove o pedido antes de enviar.").
2. **Dado** `purchasing.edit` e um pedido `Approved`, **quando** clico "Enviar ao fornecedor", **então** o diálogo vem com o destinatário padrão (contato do fornecedor que recebe cobrança ou o e-mail do cadastro, R-PRP-1), até 3 cópias, assunto "Pedido de compra {número} — {empresa}" e uma mensagem opcional (até 1.000 caracteres, texto puro); ao confirmar, o PDF é guardado como arquivo do sistema (`StoreGeneratedAsync`, dono `PurchaseOrder`, CA 9), o e-mail vai para a fila com o anexo, o envio é registrado em `purchase_order_dispatches` e o pedido passa a `Sent`, na mesma transação; a mensagem diz "Pedido enviado para a fila de e-mail"; com `Idempotency-Key`.
3. **Dado** o envio, **quando** o `SendEmailJob` roda, **então** só manda depois do anexo `Clean` (reagenda até 10 vezes) e marca `Failed` com o motivo se o anexo for `Infected`/`Failed` (ADR 0019 §7); a aba Documentos mostra cada envio com destinatário, data, situação do e-mail e o PDF.
4. **Dado** um pedido `Sent`, ou reaprovado com a faixa "Alterado depois do envio: reenvie ao fornecedor." (CMP-03a CA 15), **quando** clico "Reenviar", **então** gera um PDF novo e outro registro de envio (o pedido reaprovado volta a `Sent` e a faixa some; o `Sent` não muda de estado).
5. **[Segurança]** **Dado** o limite de **50 envios por dia por tenant, somando orçamentos (COM-03) e pedidos de compra**, e o limite de **20 envios por dia por usuário** (valor inicial, parâmetro `email.dispatch_max_per_user_day`), também somados, **quando** o envio que passa de um deles é pedido, **então** a API responde 429 "Limite diário de envios atingido. Tente amanhã ou baixe o PDF." (ou "Você atingiu o seu limite diário de envios. Tente amanhã ou baixe o PDF.") e nada é enfileirado; a contagem é feita sobre `shared.email_logs` do dia (São Paulo) dos dois tipos de envio, de forma atômica (trava por tenant na transação do envio: dois envios simultâneos no 50º, só um passa).
6. **[Segurança]** **Dado** o destinatário e as cópias, **quando** informo, **então** cada um é um **endereço puro** (sem nome de exibição, vírgula, ponto e vírgula nem CR/LF; formato válido; até 254 caracteres), validado no servidor (422 no campo); um endereço que não está nos contatos do fornecedor mostra o aviso âmbar "Este e-mail não está nos contatos de {fornecedor}." e exige marcar "Confirmo o destinatário" (o servidor recusa sem a confirmação); o envio é auditado.
7. **[Segurança]** **Dado** o e-mail montado, **quando** a fila envia, **então**: o assunto e o nome fantasia não carregam CR/LF (removidos no servidor); remetente "{nome fantasia} via BemAqui" e `Reply-To` do usuário, como na COM-03; a mensagem digitada entra **como valor do modelo** do template Fluid, escapada, nunca como template (teste com `{{ 1 | plus: 1 }}`, `{% if %}` e HTML: o texto sai literal), e nenhuma URL da mensagem vira link clicável (sem `<a>` gerado); o e-mail não leva link para o sistema.
8. **Dado** os limites da ADR 0022, **quando** o pedido passa de 300 linhas ou a observação de 2.000 caracteres, **então** 422 antes de gerar.
9. **[Segurança]** **Dado** o PDF gerado, **quando** é guardado, **então** fica com o dono `PurchaseOrder` (adendo de 21/10/2026 à ADR 0019 §7: leitura `purchasing.view`, escrita `purchasing.edit`, pdf, png e jpg até 10 MB, `OwnerExists`, `origin = System`) e não contém custo médio, margem, projeto nem dado de outro fornecedor (teste de extração de texto).
10. **Dado** o módulo `purchasing` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; baixar sem `purchasing.view`, enviar ou reenviar sem `purchasing.edit`: 403. `RequireModule` e `perm:`; IDOR em `/purchase-orders/{id}/dispatches/{dispatchId}`; no máximo 3 cópias.
11. **Dado** o envio, **quando** abro "Histórico de alterações", **então** vejo a passagem para `Sent` com usuário e data e a confirmação de destinatário fora dos contatos quando houver; os endereços aparecem mascarados para quem não tem `parties.view` (`[PersonalData]`).

## Regras de negócio e técnicas
- **Layout** `PurchaseOrderPdfDocument` no Purchasing; nenhum outro arquivo do módulo referencia QuestPDF (ADR 0022).
- **Dono de anexo `PurchaseOrder`:** registrado na CMP-03a (primeira story que o usa), com as regras do adendo à ADR 0019 §7; o PDF gerado tem `origin = System`.
- **`purchase_order_dispatches`:** `purchase_order_id`, `email_log_id`, `attachment_id`, `to` e `cc` (`[PersonalData]`), `recipient_confirmed`, `sent_by`, `created_at`.
- **Limite diário:** contado em `shared.email_logs` pelos tipos `quote_dispatch` e `purchase_order_dispatch`, do dia em `America/Sao_Paulo`; a contagem da COM-03 passa a usar a mesma consulta (somada), em vez de `quote_dispatches`.
- **Template de e-mail** `purchase_order_dispatch` (Fluid), com o texto do usuário inserido como valor escapado e sem autolink.

## Permissões, eventos e tabelas
- **Permissões:** `purchasing.view` (PDF), `purchasing.edit` (enviar e reenviar).
- **Eventos:** nenhum entre módulos.
- **Tabelas:** `purchasing.purchase_order_dispatches`; anexos em `shared.attachments`; `shared.email_logs`; parâmetro `email.dispatch_max_per_user_day`.

## Telas e estados
- Padrões de tela da Fase 3: 2 (faixa "Alterado depois do envio"), 4 (diálogo de envio com o mesmo verbo).
- **Pedido → ações** "Baixar PDF", "Enviar ao fornecedor" e "Reenviar"; **aba Documentos** com envios e anexos.
- **Verbos:** "Enviar ao fornecedor" / "Pedido enviado para a fila de e-mail"; "Reenviar" / "Pedido reenviado para a fila de e-mail".
- **Estados:** carregando; vazio da aba Documentos ("Nenhum envio. Envie o pedido ao fornecedor depois de aprovado." + "Enviar ao fornecedor"); erro do envio dentro do diálogo (inclusive o limite diário, com "Baixar PDF"); estado do e-mail ("Na fila", "Aguardando verificação do anexo", "Enviado", "Falhou: {motivo}" com "Reenviar").
- **Propositivo:** depois do aprovado, o `NextSteps` "Enviar ao fornecedor" (CMP-03a); destinatário preenchido.
- **400 px:** diálogo em tela cheia; envios em cartões.
- **Ajuda:** `Ajuda/compras-pedido-documentos.md` (aba `documentos`); `<Dica>` em "Cópia" e "Confirmo o destinatário".

## Plano de testes
- **Unidade:** montagem do modelo do PDF (conversão para a unidade do fornecedor, totais, endereço da empresa); validação de endereço puro e CR/LF (CA 6); limpeza de CR/LF no assunto (CA 7).
- **Integração (Testcontainers):** PDF na imagem Alpine com texto extraído esperado e sem custo (CA 1, 9); envio com anexo, espera do `Clean` e falha com anexo infectado (CA 2, 3); reenvio, inclusive do pedido alterado depois do envio (CA 4); limite de 50 somado ao do orçamento e limite por usuário, com dois envios simultâneos no limite (CA 5); destinatário fora dos contatos sem e com a confirmação (CA 6); mensagem com `{{ }}`, `{% %}`, HTML e URL saindo literal (CA 7); limites da ADR 0022 (CA 8); três camadas e IDOR (CA 10); auditoria (CA 11).
- **E2E (Playwright):** aprovar, enviar ao fornecedor e ver o envio na aba Documentos.

## Fora do escopo
- Portal do fornecedor e confirmação do pedido pelo fornecedor: fora do MVP.
- Cotação por e-mail: fora do MVP (CMP-02).
- Entrega na obra do projeto: fica para quando houver o vínculo com o endereço da obra.

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fase 3, tabela 1.1, itens 45–46):
1. **Limite diário (45):** 50 por dia no tenant, somado ao do orçamento, mais um limite por usuário, contados por `shared.email_logs` de forma atômica (CA 5). O valor do limite por usuário (20 por dia) é valor inicial, parâmetro.
2. **Endereço de entrega (46):** o endereço da empresa (CA 1).

**Pré-condição externa:** nenhuma (SPF, DKIM e DMARC já cumpridos na COM-03).

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers, PDF na imagem Alpine) e E2E passando.
- [ ] Migration criada (não destrutiva).
- [ ] Permissões só do `ModuleCatalog`; dono de anexo `PurchaseOrder` usado com as regras do adendo à ADR 0019 §7; template de e-mail registrado.
- [ ] Contagem do limite diário da COM-03 trocada pela consulta somada em `shared.email_logs`, sem mudar o comportamento do orçamento (teste da COM-03 passando).
- [ ] Telas com estados, inclusive a 400 px.
- [ ] Ajuda da aba; teste de cobertura passando.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `PurchaseOrderPdfDocument` e `PurchaseOrderDispatchService`.
- [ ] Template `purchase_order_dispatch`; contador comum do limite diário (tenant e usuário) sobre `shared.email_logs`.
- [ ] Endpoints `/api/v1/purchasing/purchase-orders/{id}/pdf` e `/dispatches`; aba Documentos e ajuda.

## Quality gates
- Agentes previstos: @dev, cybersecurity (e-mail: destinatário, CR/LF, mensagem escapada, limite), @qa.
- Gate: CA 1, 3, 5, 6, 7 e 9 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fase 3).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK (renumerados, 11) |
| 5 | CAs de segurança e de permissão (3 camadas) | OK (ajustado: CA 5, 6, 7, 9) |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK |

- **Correções feitas na validação (21/10/2026, primeira validação):** reaproveitamento explícito da infraestrutura da COM-03 (PDF, arquivo do sistema, espera do `Clean`), sem código novo de e-mail; PDF sem custo nem projeto (CA 6).
- **Correções da revalidação (21/10/2026):** limite de 50 por dia somado ao do orçamento e limite por usuário, contados de forma atômica em `shared.email_logs` (CA 5); endereço de entrega = endereço da empresa (CA 1); confirmação de destinatário fora dos contatos do fornecedor e CR/LF recusado nos endereços (CA 6); mensagem escapada e sem links clicáveis (CA 7); dono de anexo `PurchaseOrder` pelo adendo à ADR 0019 §7, sem xlsx e docx (CA 9); reenvio do pedido alterado depois do envio (CA 4); padrões de tela 2 e 4. CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma de decisão de story. Pré-condição externa: nenhuma.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da CMP-03) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO (tabela 1.1 itens 45–46) e correções da revisão pré-Fase 3 (§2.2 CMP-03b, adendo à ADR 0019 §7); nova ordem (Marco 3.3, ordem 18); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
| 24/10/2026 | 1.0 | Implementada (`Done`). PDF `PurchaseOrderPdfDocument` (QuestPDF, só ele no Purchasing) montado pelo `PurchaseOrderPdfBuilder` puro; `PurchaseOrderDispatchService` (baixar, documentos, envio lido pelo pedido da URL, diálogo, enviar/reenviar); `purchasing.purchase_order_dispatches`, `purchase_orders.sent_at` e `shared.email_logs.requested_by` + índice (template, created_at) na migration `PurchaseOrderDispatches` (só criação); template `purchase-order-sent`; `EmailDispatchLimiter` comum (COM-03 e CMP-03b) sobre `shared.email_logs` com `pg_advisory_xact_lock`; parâmetro `email.dispatch_max_per_user_day` (20, de 1 a 50) em Configurações → Parâmetros → Envios por e-mail; telas, ajudas e E2E. Desvios e decisões: (1) **Nome do template:** `purchase-order-sent` (padrão kebab-case dos templates, como o `quote-sent` da COM-03); a contagem soma `quote-sent` e `purchase-order-sent`. (2) **COM-03 na consulta somada:** o envio do orçamento passou a abrir transação e contar pelo `EmailDispatchLimiter` (trava do tenant); o 429 continua, mas a mensagem passou a ser a comum "Limite diário de envios atingido. Tente amanhã ou baixe o PDF." e o código `email_dispatch_limit` (antes `quote_send_limit`); o teste da COM-03 semeia `email_logs` no lugar de `quote_dispatches`. (3) **Limite por usuário:** contado por `email_logs.requested_by` (coluna nova; o `EmailRequest` ganhou `RequestedBy`); o parâmetro fica em `identity.tenant_settings` (`ITenantParameters.EmailDispatchMaxPerUserDayAsync`) e é editável com `settings.edit` — recomendação aceita (PO 24/10). (4) **Baixar PDF** gera na hora (`GET /purchase-orders/{id}/pdf`) e não guarda arquivo; o PDF guardado (dono `PurchaseOrder`, `origin = System`) é o de cada envio e reenvio — recomendação aceita (PO 24/10). (5) **Quem envia:** `Approved` (inclusive o reaprovado com a faixa) e `Sent`; `PartiallyReceived`/`Received` e `Cancelled` recusam com 409 `purchase_order_not_dispatchable` (baixar o PDF continua) — recomendação aceita (PO 24/10). (6) **Mensagem opcional** vem vazia (o template já diz que o PDF vai anexo); o destinatário padrão é o contato que recebe cobrança ou o primeiro com e-mail (o `PartySummary` não tem e-mail de cadastro). (7) **Máscara (CA 11):** o histórico mascara os endereços para todos (o interceptor de auditoria mascara `[PersonalData]` sempre); a aba Documentos mostra `m***@dominio` para quem não tem `parties.view` — recomendação mais segura aceita (PO 24/10). (8) **Código do fornecedor:** `SupplierCodeMatch` (Inventory.Contracts) ganhou `SupplierCode` opcional; a unidade do fornecedor divide a quantidade pelo fator (4 casas) e multiplica o preço (6 casas, exibido com 2 a 4), o total da linha não muda. (9) **Limites do PDF (CA 8):** 300 linhas e 2.000 caracteres conferidos antes de gerar, embora o pedido aceite hoje 100 linhas e 500 caracteres (defesa para pedidos importados). (10) **Purchasing referencia `Identity.Contracts`** (empresa do PDF e o parâmetro); teste de arquitetura atualizado. (11) **Envio idempotente** (`Idempotency-Key`) e versão do pedido (409 `concurrency_conflict`). (12) **Endpoints:** `GET /purchase-orders/{id}/pdf`, `/documents`, `/dispatches/{dispatchId}`, `/dispatch-draft` e `POST /dispatches`; o envio devolve o detalhe do pedido. (13) **OpenAPI** gerado pelo Host em processo (Development, sem banco e sem hosted services), não editado à mão; regenerar com `BQ_UPDATE_OPENAPI=1 sh scripts/test-docker.sh --filter-class "*OpenApiDocumentTests"` e `pnpm gen:api`. (14) **Pendente:** testes de integração escritos e não executados (fila do Docker), inclusive o PDF na imagem Alpine; migration a testar sobre cópia do banco `konebras`; os testes de arquitetura que carregam todas as DLLs não rodam nesta máquina (Smart App Control, 0x800711C7). | Dev (Claude) |
