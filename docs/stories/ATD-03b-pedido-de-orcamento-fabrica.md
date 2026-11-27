# ATD-03b — Pedido de orçamento para fábrica pelo Conecta

| Campo | Valor |
|---|---|
| ID | ATD-03b |
| Título | Na fábrica, o atendente (e, depois, a IA) abre pelo Conecta um **pedido de orçamento**: um orçamento em rascunho no Comercial do ERP com o cliente e o que ele pediu, número `ORC-…`, origem "Conecta" e aviso ao Comercial; rota nova `POST /api/v1/integration/quote-requests` com o escopo `quotes.create` (adendo à ADR 0053), que exige **autorizar de novo** as conexões existentes |
| Tamanho | P |
| Status | Draft (27/11/2026) |
| Requisitos | [ADR 0052](../adr/0052-integracoes-conecta-e-marketplaces.md) §3 · [ADR 0053](../adr/0053-aplicativos-conectados.md) §5, §6 e §13 · [Plano](../integracoes/00-plano-integracoes.md) §3.3 e §7 item 2 · [INT-01](INT-01-aplicativos-conectados.md) · [INT-03](INT-03-api-integracao-v1.md) · COM-01a (orçamento) |
| Dependências | ATD-01, ATD-02 (vínculo e telefone conferido); módulo `quotes` liberado no tenant (perfil `factory`) |
| Bloqueia | — (a ferramenta da IA para orçamento entra na ATD-04, se o PO quiser) |
| Repositórios | **ERP** (rota, escopo, adendo de ADR, "autorizar de novo" na página do Conecta) e **Conecta** (formulário na conversa) |
| Ordem | 4 (F2) |

## User story
Como **atendente de uma fábrica que usa o Conecta**, quero **registrar no ERP o pedido de orçamento que o cliente fez no WhatsApp**, para **o Comercial receber o pedido com o cliente certo e o texto do que ele quer, sem eu redigitar nada**.

## Contexto
- ADR 0052 §3: o projeto continua sendo o pedido do sob encomenda; pelo Conecta, a fábrica abre **pedido de orçamento**, um rascunho no Comercial; peça de prateleira usa o pedido de venda (ATD-03a). Nada é criado só para a fábrica.
- ADR 0053 §5 deixou `quotes.create` fora da v1 "para a ATD-03". O orçamento do ERP (`quotes`, ações CRUD + `approve`) nasce `Draft` a partir de `QuoteCreateInput(PartyId, ContactId, SiteAddressId)` e tem `Notes`; preço, custo e margem são trabalho do Comercial.
- **Escopo novo exige consentimento novo:** a concessão de uma conexão é fixada no consentimento (INT-01 CA 9). Pôr `quotes.create` nos `AllowedScopes` **não** dá o escopo às conexões existentes: o Proprietário precisa "Autorizar de novo".

## Critérios de aceite

### ERP
1. **Dado** o adendo à ADR 0053 (§5), **quando** a story entra, **então** `quotes.create` passa a fazer parte de `ConnectedAppScopes.Conecta` com o texto do catálogo "Abrir pedidos de orçamento (rascunho no Comercial)"; a concessão segue a regra de sempre (pedidos ∩ `AllowedScopes` ∩ permissões efetivas de quem autoriza, que já limitam ao módulo liberado).
2. **Dado** uma conexão ativa cuja concessão **não** tem um escopo que hoje seria concedido, **quando** o Proprietário abre Configurações → Integrações → Conecta, **então** vê "O Conecta pode ganhar: Abrir pedidos de orçamento" com "Autorizar de novo" (mesmo fluxo da INT-01: começa no ERP, consentimento com 2FA, conexão nova substitui a anterior e a inscrição de avisos é recriada); o Conecta, ao receber `scope` sem `quotes.create`, esconde a função (ATD-01 CA 12).
3. **[Segurança]** **Dado** `quotes.create` efetivo, **quando** o aplicativo chama `POST /api/v1/integration/quote-requests` com `Idempotency-Key` e `{customerId, externalId, request, contactName?}`, **então** o ERP cria pelo serviço do Comercial um orçamento **`Draft`** para o cliente (terceiro com papel Cliente; outro tenant ou inexistente, 404), com número do serviço de numeração, `Notes` = "Pedido de orçamento pelo Conecta" + o texto do pedido (até 2.000 caracteres, texto puro), sem itens, sem preço e sem custo; origem, aplicativo e autor vêm do token (auditoria "Integração Conecta (autorizada por Fulano)"); responde 201 com `{id, number, status: "Draft", createdAt}`; a mesma chave ou o mesmo `externalId` com outro corpo responde 409 (`idempotency_key_reused`, `external_id_conflict`), como os pedidos (ADR 0053 §13).
4. **[Segurança]** **Dado** pedidos de orçamento pelo aplicativo, **quando** passam de **3 por cliente em 24 h** ou de `Integrations:QuoteRequestDailyCap` (padrão 50) por tenant no dia, **então** 429 `rate_limited` com `Retry-After`; além disso vale a política `app-api` de escrita.
5. **Dado** o orçamento criado, **quando** grava, **então** quem tem `quotes.edit` recebe a notificação "Novo pedido de orçamento pelo Conecta: {cliente}" com o link do orçamento, e a lista de orçamentos mostra o chip "Pelo Conecta"; o Comercial segue o fluxo de sempre (itens, custo, envio, aceite).
6. **Dado** o módulo `quotes` não liberado, sem `quotes.create` efetivo ou sem o módulo `conecta`, **quando** o aplicativo chama, **então** 403 `module_not_released` ou `insufficient_scope` (ADR 0053 §7); o contrato `integration-v1` cresce de forma aditiva e o `openapi-integration-v1.json` é atualizado (teste de contrato).

### Conecta
7. **Dado** a concessão com `quotes.create` e o contato vinculado com telefone conferido (ATD-02), **quando** o atendente clica "Pedido de orçamento" na conversa, **então** o formulário vem com o cliente e um texto sugerido a partir das últimas mensagens do cliente (o atendente edita; nada sai sem ele clicar "Enviar ao Comercial"); o Conecta grava antes a chave e o `externalId` (`atd-orc-<id>`) e chama a rota do CA 3.
8. **Dado** o retorno 201, **quando** chega, **então** a conversa mostra "Pedido de orçamento ORC-… enviado ao Comercial em dd/mm" e uma nota interna; o atendente pode mandar ao cliente "Recebemos seu pedido de orçamento ORC-…; o Comercial vai te enviar a proposta." (botão, com prévia).
9. **Dado** o cliente sem cadastro no ERP, **quando** o atendente abre o pedido de orçamento, **então** o fluxo "Cadastrar no ERP" da ATD-03a CA 2 vem antes (só nome, tipo, documento e telefone são obrigatórios aqui; endereço não, porque não há cobrança).
10. **Dado** um projeto já existente do cliente (ATD-02 CA 11), **quando** o cliente pergunta do andamento, **então** o painel de projetos responde (esta story não cria nada para isso).

## Regras de negócio e técnicas
- **Rota nova (ERP, `integration-v1`):** `POST /api/v1/integration/quote-requests` (`quotes.create`), grupo `MapIntegrationGroup`, `RequireModule("quotes")`, `RequirePermission("quotes.create")`, `Idempotency-Key` obrigatória, `Cache-Control: no-store`; erros `validation`, `not_found`, `idempotency_key_required`, `idempotency_key_reused`, `external_id_conflict`, `rate_limited`, `insufficient_scope`, `module_not_released`.
- **Unicidade:** `(origin, source_app_id, external_id)` no orçamento, como no pedido de venda (VND-02a CA 2); colunas novas e aditivas em `commercial.quotes` (`origin`, `source_app_id`, `external_id`).
- **Texto do pedido** é dado do cliente: fica nas notas do orçamento (já cobertas pela auditoria e pelo inventário de dados pessoais do Comercial); o Conecta não guarda cópia além da mensagem original da conversa.
- **Sem aviso novo de saída** na v1 (`quote.*` fica fora do catálogo da INT-02).

## O que muda em cada repositório
**ERP:** adendo à ADR 0053 (§5: `quotes.create` entra; nova autorização necessária); `ConnectedAppScopes.Conecta`; rota no `Commercial.Endpoints` sobre o serviço de orçamento existente; colunas de origem em `commercial.quotes` (migration aditiva); limite do CA 4; notificação do CA 5; chip na lista; "O Conecta pode ganhar…" na página do Conecta (INT-01); OpenAPI `integration-v1` e o arquivo versionado; ajuda `Ajuda/configuracoes-integracoes-conecta.md` ("Autorizar de novo para ganhar acessos novos") e `Ajuda/comercial-orcamentos.md` (chip "Pelo Conecta").
**Conecta:** "Pedido de orçamento" na conversa (web e app), entidade `ErpQuoteRequestLink` (conversa, contato, atendente, `erp_quote_id`, número, chave, `externalId`).

## Telas e estados
- **ERP:** linha "O Conecta pode ganhar" (some quando nada falta); chip "Pelo Conecta" com `<Dica>` "Aberto pelo atendimento no Conecta; complete itens e preço".
- **Conecta:** formulário com carregando, erro, "Enviando…"; escopo ausente esconde o botão; sucesso com o próximo passo (mensagem ao cliente).
- **Verbos:** "Enviar ao Comercial" → "Enviado ao Comercial"; "Autorizar de novo" → "Autorizado".

## Plano de testes
- **ERP, integração (Testcontainers):** criação com a trilha do aplicativo (CA 3); idempotência e `external_id_conflict` (CA 3); limite por cliente e por tenant (CA 4); 403 sem escopo e sem módulo (CA 6); conexão antiga sem `quotes.create` recebe 403 até autorizar de novo (CA 2); notificação (CA 5); IDOR de cliente de outro tenant (404). Teste de contrato do `integration-v1`.
- **Conecta:** formulário, repetição sem duplicar, escopo ausente.

## Tarefas
- [ ] ERP: adendo à ADR 0053, escopo, rota, colunas, limite, notificação, chip, "O Conecta pode ganhar", OpenAPI, ajudas.
- [ ] Conecta: botão, formulário, vínculo e mensagem ao cliente.
- [ ] Fluxo dos dois repositórios: ERP primeiro (PR, CI, deploy), depois o Conecta.

## Dev Notes
- **ERP:** `src/BuildingBlocks/BemAqui.BuildingBlocks/Modules/ConnectedAppScopes.cs`, `ModuleCatalog.cs` (`quotes`), `src/Modules/Commercial/BemAqui.Commercial.Application/QuoteService.cs` e `QuoteDtos.cs` (`QuoteCreateInput`), `QuoteService.Header.cs` (`Notes`), `src/Modules/Commercial/BemAqui.Commercial.Domain/Quote.cs` (`QuoteStatus.Draft`), `src/Modules/Sales/BemAqui.Sales.Endpoints/SalesEndpoints.cs` (molde do grupo de integração e da idempotência), `src/Modules/Identity/BemAqui.Identity.Application/ConnectedApps/` (concessão).
- **Conecta:** mesmos pontos da ATD-03a.

## Fora do escopo
- Leitura do andamento do orçamento pelo Conecta (`quotes.view`), avisos `quote.*`, anexos (fotos e plantas mandados no WhatsApp) e ferramenta da IA para orçamento.
- Preço, itens ou proposta pelo Conecta: tudo isso é do Comercial no ERP.

## Pontos em aberto (para o PO)
1. **Anexos** (foto do poço do elevador, planta): **recomendação:** fora da v1; o Comercial vê a conversa no Conecta. Numa próxima, anexos pelo armazenamento do ERP com limite de tipo e tamanho.
2. **Acompanhar o orçamento pelo Conecta** (`quotes.view` + aviso `quote.sent`): **recomendação:** depois do piloto, se a fábrica pedir.
3. **IA abrindo pedido de orçamento:** **recomendação:** só depois do piloto da ATD-04; quando entrar, a IA monta o texto e um atendente envia.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 27/11/2026 | 0.1 | Story separada da ATD-03 porque exige mudança no ERP (escopo `quotes.create`, rota nova e nova autorização), conforme a ADR 0053 §5. | Claude (planejamento) |
