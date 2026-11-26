# INT-03 — API de integração v1: catálogo, disponibilidade, clientes, pedidos, cobranças, notas e status do projeto, para aplicativos conectados

| Campo | Valor |
|---|---|
| ID | INT-03 |
| Título | API de integração v1 (`/api/v1/integration/...`) para aplicativos conectados (INT-01): produtos, preços e disponível; clientes com dado mínimo (LGPD); pedidos com `Idempotency-Key`; cobranças em aberto e 2ª via; notas e DANFE; status do projeto da fábrica. Escopo = `modulo.acao`, três camadas de acesso, limite por aplicativo, ProblemDetails, OpenAPI próprio e teste de contrato |
| Tamanho | M |
| Status | Draft (26/11/2026) |
| Requisitos | [ADR 0052](../adr/0052-integracoes-conecta-e-marketplaces.md) §1, §2 e Consequências · [Plano de integrações](../integracoes/00-plano-integracoes.md) §3.2, §3.3, §5 e §9 · ADR 0003 (ações) · ADR 0008 (delegação) · ADR 0009 (rotas anônimas) · ADR 0019 (idempotência e paginação) · ADR 0037 (LGPD) |
| Dependências | VND-02 (pedido de venda), INT-01 (aplicativos conectados e token de aplicativo) |
| Bloqueia | ATD-02, ATD-03, ATD-04; API-01 e API-02 (F4) |
| Ordem | 4 (F1 — Base no ERP) |

## User story
Como **aplicativo conectado** (o Conecta primeiro, depois a API pública e o MCP), quero **consultar produtos, clientes, pedidos, cobranças e notas, e criar pedidos, com os mesmos limites de quem me autorizou**, para **atender o cliente na conversa sem ninguém copiar dado de um sistema para o outro**.

## Contexto
- A INT-01 cria o aplicativo conectado e o token de aplicativo (OAuth com PKCE; escopos `modulo.acao`; o efetivo é a interseção entre o pedido, as permissões de quem autorizou e os módulos liberados). Esta story expõe as rotas que esse token usa.
- As rotas reusam os serviços e contratos dos módulos; **nenhuma regra é reescrita para a API**. O que é da tela (máscara, idempotência, IDOR, limites de desconto) vale igual.
- O contrato é entre dois produtos e dois times (plano §9): versionado em `/api/v1/integration`, com OpenAPI publicado e teste de contrato.

## Critérios de aceite

### Autenticação, escopos e as três camadas
1. **[Segurança]** **Dado** uma rota `/api/v1/integration/*`, **quando** chega sem token de aplicativo válido (ou com token de pessoa, de aparelho ou de TV), **então** 401; nenhuma rota de integração é anônima e o `EndpointAuthorizationTests` reconhece o esquema de aplicativo (ADR 0052, Consequências).
2. **[Segurança]** **Dado** um token válido, **quando** chamo uma rota, **então** valem as três camadas para o aplicativo: módulo liberado no tenant (403 `module_not_released`), escopo efetivo com o módulo (403 `forbidden`) e a ação (`RequirePermission("modulo.acao")`, 403 `forbidden`); quem autorizou perdeu a permissão ou foi desativado, o efetivo cai junto (cache versionado, ADR 0008).
3. **[Segurança]** **Dado** a conexão revogada ou o token expirado, **quando** chamo, **então** 401 `app_token_revoked` ou `app_token_expired`, e a resposta não diz se o recurso existe.
4. **Dado** o mapa de escopos abaixo, **quando** o aplicativo chama a rota, **então** só passa com o escopo listado:

| Rota | Escopo |
|---|---|
| `GET /products`, `GET /products/{id}` | `catalog.view` (disponível só com `inventory.view`, CA 7) |
| `GET /customers?phone=` ou `?document=`, `GET /customers/{id}` | `parties.view` |
| `POST /customers`, `PATCH /customers/{id}` | `parties.create` / `parties.edit` |
| `POST /orders`, `GET /orders/{id}`, `GET /customers/{id}/orders` | `sales.create` / `sales.view` |
| `POST /orders/{id}/confirm`, `POST /orders/{id}/charge`, `POST /orders/{id}/cancel` | `sales.create` (confirmar) / `sales.edit` |
| `GET /customers/{id}/charges`, `GET /charges/{id}`, `GET /charges/{id}/pdf` | `finance.view` |
| `GET /orders/{id}/invoice`, `GET /invoices/{id}/danfe` | `fiscal.view` |
| `GET /customers/{id}/projects` | `projects.view` |

### Catálogo e disponível
5. **Dado** `catalog.view`, **quando** chamo `GET /products?q=&page=&pageSize=`, **então** recebo os produtos ativos e vendáveis (`ICatalogLookup.SearchAsync`/`ISellableProducts`): id, código, GTIN, descrição, unidade, preço da tabela padrão e a versão da tabela; **nunca custo nem margem** (mesmo com `catalog.view_cost` no escopo).
6. **Dado** a paginação, **quando** peço, **então** segue o `PageRequest` (página a partir de 1, `pageSize` até 100, padrão 20) e a resposta traz `items`, `page`, `pageSize` e `total`.
7. **Dado** `inventory.view` também no escopo, **quando** consulto um produto, **então** vem `available` = físico − reservado no local padrão (`IStockBalances`), já descontadas as reservas dos pedidos (VND-02); sem `inventory.view`, o campo não vem.

### Clientes (LGPD)
8. **[Segurança]** **Dado** `parties.view`, **quando** busco por telefone (`phone`, normalizado E.164) ou documento (`document`, CPF ou CNPJ válido), **então** recebo só id, tipo, nome, documento **mascarado** (`***.456.789-**`), cidade e UF, e os indicadores `canBill` (documento e endereço de cobrança completos) e `canInvoice` (documento e IBGE); nunca endereço completo, outros contatos, limite de crédito ou histórico financeiro.
9. **[Segurança]** **Dado** muitas buscas por documento ou telefone, **quando** passam de 30 por minuto por aplicativo, **então** 429 com `Retry-After` (contra enumeração), contado à parte do limite geral; cada busca grava auditoria com o termo em hash, nunca em texto.
10. **Dado** `parties.create`, **quando** crio um cliente, **então** só são aceitos nome, tipo, documento, telefone, e-mail e endereço (os necessários ao pedido, à cobrança e à nota); documento já cadastrado devolve 409 `party_exists` com o id; o papel `Customer` é dado; `parties.edit` altera só esses campos.

### Pedidos
11. **[Segurança]** **Dado** `sales.create`, **quando** chamo `POST /orders` com `Idempotency-Key`, cliente, linhas (produto e quantidade, desconto opcional) e `externalId`, **então** o pedido nasce pela VND-02 com origem `Conecta` ou `Api` e `source_app_id` do aplicativo, em `Draft` "Aguardando confirmação do cliente"; preço enviado é ignorado (VND-02 CA 5); sem a chave, 400 `idempotency_key_required`; o mesmo `externalId` do mesmo aplicativo devolve o pedido existente.
12. **Dado** `POST /orders/{id}/confirm`, **quando** o cliente confirmou na conversa, **então** o pedido é confirmado com a reserva (VND-02 CA 9); sem saldo, 409 `insufficient_stock` com as linhas em falta.
13. **Dado** `POST /orders/{id}/charge` (`sales.edit`), **quando** o pedido está `Open` e o cliente `canBill`, **então** a cobrança é gerada pela VND-02 (CA 13) e a resposta é 202 com `status = Requested`; a linha digitável, o Pix e o PDF chegam por `GET /charges/{id}` e pelo evento da INT-02.
14. **Dado** `GET /orders/{id}` ou `GET /customers/{id}/orders`, **quando** consulto, **então** vejo número, estado, origem, linhas, total, cobrança (id e estado), nota (id e estado) e as datas de cada estado; sem custo, sem dado de outro cliente.
15. **Dado** `POST /orders/{id}/cancel` com motivo, **quando** o pedido está `Draft`, `Open` ou `AwaitingPayment`, **então** é cancelado pela VND-02 (CA 26); em `Paid` em diante, 409 `order_cancel_requires_backoffice` ("Este pedido já foi pago. O cancelamento é feito no ERP.").

### Cobranças, notas e projeto
16. **[Segurança]** **Dado** `finance.view`, **quando** chamo `GET /customers/{id}/charges`, **então** recebo os títulos em aberto do cliente (número, vencimento, valor, saldo, situação calculada "Vencido") e, por título, só o boleto mais recente (`Issued`); `GET /charges/{id}` traz linha digitável, Pix copia-e-cola e o link do PDF, com `Cache-Control: no-store`; `provider_payload` nunca sai.
17. **Dado** `fiscal.view`, **quando** consulto a nota do pedido, **então** recebo tipo, situação, série, número, chave, data e o link do DANFE (`GET /invoices/{id}/danfe`, PDF); só notas de produção e não descartadas.
18. **Dado** `projects.view` (perfil `factory`), **quando** chamo `GET /customers/{id}/projects`, **então** recebo número, nome, etapa atual (letra e nome), situação e a previsão de entrega dos projetos do cliente (`IProjects.ListAsync`), **sem orçado, custo ou margem**.

### IDOR, limites, erros, auditoria e contrato
19. **[Segurança]** **Dado** um id de outro tenant, inexistente ou de um cliente diferente do da rota (`/customers/{a}/orders/{b}` com `b` de outro cliente; `/charges/{id}` de título sem relação com cliente), **quando** chamo, **então** 404 igual nos três casos; o tenant vem do host da requisição e precisa bater com o `tid` do token (outro host: 401).
20. **Dado** um aplicativo, **quando** passa de 120 leituras ou 30 escritas por minuto (por conexão, política `integration-app`), **então** 429 com `Retry-After`; os limites ficam em parâmetro de plataforma e o contador `integration_rate_limited{app}` alimenta o painel.
21. **Dado** qualquer erro, **quando** a API responde, **então** é ProblemDetails (RFC 9457) com `type`, `title` em PT-BR, `status`, `code` estável (`validation`, `not_found`, `forbidden`, `module_not_released`, `insufficient_stock`, `invalid_order_transition`, `idempotency_key_required`, `rate_limited`…) e `errors` por campo na validação; o `traceId` vai junto e nada de pilha.
22. **Dado** qualquer escrita (cliente, pedido, confirmação, cobrança, cancelamento), **quando** é gravada, **então** a auditoria mostra "Integração {aplicativo} (autorizada por {pessoa})"; leituras de documento, cobrança e nota também ficam registradas (com o termo em hash).
23. **Dado** o build, **quando** o `OpenApiDocumentTests` roda, **então** o documento `integration-v1` (só as rotas `/api/v1/integration`) é gerado e comparado ao arquivo versionado `docs/integracoes/openapi-integration-v1.json`; mudança que remove ou renomeia campo, rota ou código falha o teste (só aditivo dentro do v1).

## Regras de negócio e técnicas
- **Rotas finas:** cada endpoint chama o serviço ou o contrato do módulo dono; o grupo `/api/v1/integration` não acessa tabela de módulo nenhum. Os módulos ganham, quando falta, o método no `*.Contracts` (ver Dev Notes).
- **Versão:** `v1` só cresce de forma aditiva. Mudança que quebra vai para `/api/v2`, com o v1 mantido por 6 meses e o aviso `Deprecation`/`Sunset` nos cabeçalhos.
- **Datas** em ISO 8601 UTC; dinheiro em decimal com 2 casas no JSON (nunca `double`); enums como texto.
- **Sem CORS:** a API é de servidor para servidor; o navegador nunca chama com token de aplicativo.
- **Dados pessoais:** respostas com `Cache-Control: no-store`; logs sem documento, telefone ou e-mail (Serilog com a máscara existente).

## Permissões, eventos e tabelas
- **Permissões:** nenhuma nova; escopos são as ações existentes `catalog.view`, `inventory.view`, `parties.view|create|edit`, `sales.view|create|edit`, `finance.view`, `fiscal.view`, `projects.view` (catálogo de escopos da INT-01).
- **Eventos:** nenhum novo; os do pedido (VND-02) e do boleto saem pela INT-02.
- **Tabelas:** nenhuma; contadores de limite no armazenamento do rate limiter.
- **Auditoria:** autor de aplicativo (INT-01) em todas as escritas e nas leituras de dado pessoal.

## Telas e estados
- Nenhuma tela nova. Em **Configurações → Integrações → Conecta** (INT-01), o link "Documentação da API" abre a referência gerada do `integration-v1`.
- **Ajuda:** atualizar a ajuda da tela de integrações da INT-01 com "O que o aplicativo pode fazer" (lista de escopos em português).

## Tarefas
- [ ] Grupo `/api/v1/integration` no Host com o esquema de token de aplicativo da INT-01, `RequireModule`, `RequirePermission` e a política `integration-app`; `EndpointAuthorizationTests` cobrindo o esquema.
- [ ] Endpoints de produtos, clientes, pedidos, cobranças, notas e projetos, só sobre contratos.
- [ ] Contratos que faltam (todos novos): `IParties.FindByPhoneAsync`; `IOrderCharges.OpenByCustomerAsync` e leitura do boleto em `Finance.Contracts`; `IOutgoingInvoices.BySaleAsync` e leitura do DANFE; `ISalesOrderLookup.ByCustomerAsync`.
- [ ] Documento OpenAPI `integration-v1` separado do da tela, arquivo versionado e teste de contrato.
- [ ] Integração (Testcontainers): três camadas para o aplicativo (um teste por camada, não por rota: a varredura cobre o resto); IDOR (CA 19); máscara e limite das buscas (CA 8, 9); criar pedido idempotente e cancelar fora do estado (CA 11, 15); cobrança sem `canBill`; auditoria com o autor de aplicativo (CA 22).
- [ ] Unidade: mapa de escopo × rota (CA 4) a partir da tabela, para não divergir.

## Dev Notes
- **Base de rotas e acesso:** `src/Modules/Sales/BemAqui.Sales.Endpoints/SalesEndpoints.cs` (padrão `MapGroup`, `RequireModule`, `RequirePermission`, `RequireRateLimiting`); `src/Host/BemAqui.Host/Security/PermissionAuthorization.cs`, `AuthRateLimiting.cs`, `DeviceAuthentication.cs` e `KioskAuthentication.cs` (modelos de esquema que não é de pessoa); `tests/BemAqui.Integration.Tests/Security/EndpointAuthorizationTests.cs`.
- **Comuns:** `src/BuildingBlocks/BemAqui.BuildingBlocks/Web/IdempotencyFilter.cs`, `Paging/PageRequest.cs` e `PagedList.cs`, `Logging` (máscara), `Persistence/Auditing`.
- **Catálogo:** `src/Modules/Catalog/BemAqui.Catalog.Contracts/CatalogContracts.cs` (`ICatalogLookup.SearchAsync`, `ISellableProducts.FindManyAsync`).
- **Estoque:** `src/Modules/Inventory/BemAqui.Inventory.Contracts/Stock.cs` (`IStockBalances.GetAsync`, `StockAvailability.Available`).
- **Terceiros:** `src/Modules/Parties/BemAqui.Parties.Contracts/PartiesContracts.cs` (`FindByDocumentAsync` existe; busca por telefone é nova).
- **Pedido:** contratos da VND-02 (`ISalesOrderLookup`, serviço do pedido).
- **Financeiro:** `src/Modules/Finance/BemAqui.Finance.Application/Billing/BillingSlipService.cs` (`GetAsync` do boleto; a leitura para a API vira contrato); a 2ª via pelo portal (POR-01c, Fase 8) segue a mesma regra "só o boleto mais recente".
- **Fiscal:** `src/Modules/Fiscal/BemAqui.Fiscal.Contracts/OutgoingInvoiceContracts.cs` (`OutgoingInvoiceSummary` já tem `Status`, `AccessKey`, `Environment` e `Link`).
- **Projeto:** `src/Modules/Commercial/BemAqui.Commercial.Contracts/ProjectContracts.cs` (`IProjects.ListAsync(statuses, partyId)`, `ProjectSummary`).

## Fora de escopo
- Token, consentimento e revogação (INT-01); webhooks de saída (INT-02).
- Cadastro de aplicativo de terceiros e portal do desenvolvedor (API-01); servidor MCP (API-02).
- Pedido de orçamento da fábrica (ATD-03); escrita em estoque, preço ou financeiro pela API.

## Pontos em aberto (para o PO)
1. Limites por aplicativo (120 leituras e 30 escritas por minuto) valem para o Conecta, que atende muitas conversas ao mesmo tempo? Proposta: subir por exceção no painel de plataforma.
2. O Conecta precisa do endereço completo do cliente para confirmar a entrega na conversa? A proposta mínima (LGPD) não o devolve.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 26/11/2026 | 0.1 | Story escrita a partir da ADR 0052 e do plano de integrações, conferida contra os contratos dos módulos. | Claude (planejamento) |
