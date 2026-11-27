# ATD-03b — Pedido de orçamento para fábrica pelo Conecta

| Campo | Valor |
|---|---|
| ID | ATD-03b |
| Título | Na fábrica, o atendente abre pelo Conecta um **pedido de orçamento**: orçamento em rascunho no Comercial do BemAqui Empresas com o cliente e o texto do que ele pediu, número `ORC-…`, origem "Conecta" e aviso ao Comercial; rota nova `POST /api/v1/integration/quote-requests` com o escopo `quotes.create` (criado na INT-04) |
| Tamanho | M |
| Status | Draft (27/11/2026) |
| Requisitos | [ADR 0052](../adr/0052-integracoes-conecta-e-marketplaces.md) §3 · [ADR 0053](../adr/0053-aplicativos-conectados.md) §5 e §13 · [Plano](../integracoes/00-plano-integracoes.md) §3.3 e §7 item 2 · [INT-03](INT-03-api-integracao-v1.md) · [INT-04](INT-04-ajustes-avisos-e-conexao.md) (CA 7, 8, 14) · COM-01a |
| Dependências | INT-04 (escopo e reautorização); ATD-01, ATD-02, **ATD-03a** (cadastro do cliente e padrões de cartão na conversa); módulo `quotes` liberado (perfil `factory`) |
| Bloqueia | — |
| Repositórios | **ERP** (rota, colunas de origem, limite, aviso ao Comercial) e **Conecta** (formulário na conversa) |
| Ordem | ERP em paralelo com a ATD-06a; Conecta depois da ATD-03a |

## User story
Como **atendente de uma fábrica que usa o Conecta**, quero **registrar no BemAqui Empresas o pedido de orçamento que o cliente fez no WhatsApp**, para **o Comercial receber o pedido com o cliente certo e o texto do que ele quer, sem redigitar nada**.

## Contexto
- ADR 0052 §3: o projeto segue sendo o pedido do sob encomenda; pelo Conecta a fábrica abre **pedido de orçamento**, rascunho no Comercial; peça de prateleira usa o pedido de venda (ATD-03a).
- O escopo `quotes.create` e a reautorização única ficam na INT-04 (CA 7 e 8); esta story faz a rota e o uso.
- O orçamento do ERP nasce `Draft` por `QuoteCreateInput(PartyId, ContactId, SiteAddressId)` e tem `Notes`; preço, custo e margem são do Comercial.

## Critérios de aceite

### ERP
1. **[Segurança]** **Dado** `quotes.create` efetivo, **quando** o aplicativo chama `POST /api/v1/integration/quote-requests` com `Idempotency-Key` e `{customerId, externalId, request}`, **então** o ERP cria pelo serviço do Comercial um orçamento **`Draft`** para o cliente (papel Cliente; outro tenant ou inexistente, 404) com `Notes` = "Pedido de orçamento pelo Conecta:" + o texto (até 2.000 caracteres, **texto puro**: controle removido, sem HTML, exibido escapado em toda tela e PDF), sem itens, preço ou custo; origem, aplicativo, autor e atendente vêm do token e do `BemAqui-On-Behalf-Of`; responde 201 `{id, number, status: "Draft", createdAt}`; mesma chave ou `externalId` com outro corpo → 409 (`idempotency_key_reused`, `external_id_conflict`), por aplicativo + empresa (INT-04 CA 6).
2. **[Segurança]** **Dado** pedidos de orçamento pelo aplicativo, **quando** passam de **3 por cliente em 24 h** ou de `Integrations:QuoteRequestDailyCap` (padrão 50) por empresa no dia, **então** 429 `rate_limited` com `Retry-After`, além da política `app-api`.
3. **Dado** o orçamento criado, **quando** grava, **então** quem tem `quotes.edit` recebe "Novo pedido de orçamento pelo Conecta: {cliente}" com o link; a lista mostra o chip "Pelo Conecta" com a `<Dica>` "Aberto pelo atendimento no Conecta; complete itens e preço"; o texto continua **editável** pelo Comercial.
4. **Dado** sem módulo `quotes`, sem `quotes.create` efetivo ou sem `conecta`, **quando** chamado, **então** 403 `module_not_released` ou `insufficient_scope`; o `integration-v1` cresce de forma aditiva (teste de contrato).

### Conecta
5. **Dado** o contato identificado (ATD-02) **com identidade confirmada** (telefone ou código; cadastro feito na conversa também vale, porque o orçamento é dele), **quando** o atendente clica "Pedido de orçamento", **então** o formulário traz o cliente e um texto sugerido marcado **"Resumo das últimas mensagens: confira antes de enviar"** (editável, contador até 2.000); nada sai sem "Enviar ao Comercial"; chave e `externalId` (`atd-orc-<id>`) gravados antes da chamada.
6. **Dado** a concessão sem `quotes.create`, **quando** a conversa abre, **então** o botão aparece **desabilitado** com a `<Dica>` "O BemAqui Empresas ainda não liberou pedidos de orçamento para o Conecta. O Proprietário libera em Configurações → Integrações → Conecta → Autorizar de novo.".
7. **Dado** 201, **quando** chega, **então** a conversa mostra "Pedido de orçamento ORC-… enviado ao Comercial em dd/mm" (nota interna e cartão), com o próximo passo "Avisar o cliente" (prévia: "Recebemos seu pedido de orçamento ORC-…; o Comercial vai te enviar a proposta.").
8. **Dado** cliente sem cadastro, **quando** abre o pedido de orçamento, **então** vem antes o cadastro mínimo da ATD-03a CA 2 (sem endereço).

## Regras de negócio e técnicas
- **Rota nova (ERP):** `POST /api/v1/integration/quote-requests` (`quotes.create`), `MapIntegrationGroup`, `RequireModule("quotes")`, `RequirePermission("quotes.create")`, `Idempotency-Key` obrigatória, `no-store`; erros `validation`, `not_found`, `idempotency_key_required`, `idempotency_key_reused`, `idempotency_in_progress`, `external_id_conflict`, `rate_limited`, `insufficient_scope`, `module_not_released`.
- **Unicidade:** `(origin, source_app_id, external_id)` em `commercial.quotes` (colunas novas, migration aditiva), dono = aplicativo + empresa.
- **Sem aviso de saída** `quote.*` na v1.

## O que muda em cada repositório
**ERP:** rota no `Commercial.Endpoints` sobre o serviço de orçamento; colunas de origem; limite; notificação; chip; sanitização do texto; OpenAPI `integration-v1`; ajudas `Ajuda/comercial-orcamentos.md` e `Ajuda/comercial-orcamento.md` (chip e origem).
**Conecta:** botão e formulário na conversa (web e app, folha com prévia no app), `ErpQuoteRequestLink` (conversa, contato, atendente, `erp_quote_id`, número, chave, `externalId`).

## Telas e estados
- **Conecta:** formulário com carregando, erro e "Enviando…"; botão desabilitado com Dica (CA 6); sucesso com o próximo passo.
- **ERP:** chip "Pelo Conecta"; notificação.
- **Verbos:** "Enviar ao Comercial" → "Enviado ao Comercial"; "Avisar o cliente" → "Cliente avisado".

## Plano de testes
- **ERP, integração (Testcontainers):** criação com trilha do aplicativo e do atendente; idempotência; limites; 403 sem escopo e sem módulo; IDOR; texto com HTML e controle sai escapado; teste de contrato.
- **Conecta:** formulário, botão desabilitado sem escopo, repetição sem duplicar.

## Tarefas
- [ ] ERP: rota, colunas, limite, notificação, chip, sanitização, OpenAPI, ajudas.
- [ ] Conecta: botão, formulário, cartão e mensagem ao cliente.
- [ ] ERP primeiro (PR, CI, deploy), depois o Conecta.

## Dev Notes
- **ERP:** `src/Modules/Commercial/BemAqui.Commercial.Application/QuoteService.cs`, `QuoteDtos.cs` (`QuoteCreateInput`), `QuoteService.Header.cs` (`Notes`), `src/Modules/Commercial/BemAqui.Commercial.Domain/Quote.cs` (`QuoteStatus.Draft`), `src/Modules/Sales/BemAqui.Sales.Endpoints/IntegrationOrderEndpoints.cs` (molde).
- **Conecta:** os mesmos da ATD-03a.

## Fora do escopo
- `quotes.view`, avisos `quote.*`, anexos (fotos, plantas), orçamento pela IA, preço ou itens pelo Conecta.

## Pontos em aberto
Decididos (PO, 27/11/2026): anexos fora da v1; acompanhar o orçamento pelo Conecta depois do piloto; IA abrindo orçamento só depois do piloto da ATD-04, com envio por atendente. Nada pendente.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 27/11/2026 | 0.1 | Story separada da ATD-03 por mudar o ERP. | Claude (planejamento) |
| 27/11/2026 | 0.2 | Revisões de 27/11/2026: arquitetura, segurança, UX; decisões do PO. Escopo e reautorização movidos para a INT-04; tamanho M; depende da ATD-03a; texto escapado e editável; botão desabilitado com Dica; texto sugerido marcado para conferir; dono = aplicativo + empresa. | Claude (planejamento) |
