# INT-03 — API de integração v1: catálogo, disponibilidade, clientes, pedidos, cobranças, notas e status do projeto, para aplicativos conectados

| Campo | Valor |
|---|---|
| ID | INT-03 |
| Título | API de integração v1 (`/api/v1/integration/...`) para aplicativos conectados (INT-01): produtos, preços e disponível; clientes com dado mínimo (LGPD) e edição restrita; pedidos com idempotência por conexão; cobranças em aberto e 2ª via; notas e DANFE; status do projeto da fábrica. Escopos e erros da ADR 0053, três camadas de acesso, limite único `app-api`, ProblemDetails, OpenAPI próprio e teste de contrato |
| Tamanho | M |
| Status | Draft (26/11/2026) |
| Requisitos | [ADR 0053](../adr/0053-aplicativos-conectados.md) · [ADR 0052](../adr/0052-integracoes-conecta-e-marketplaces.md) §1, §2 e Consequências · [Plano de integrações](../integracoes/00-plano-integracoes.md) §3.2, §3.3, §5 e §9 · ADR 0003 · ADR 0008 · ADR 0019 · ADR 0037 |
| Dependências | VND-02a (pedido de venda), INT-01 (token de aplicativo) |
| Bloqueia | ATD-02, ATD-03, ATD-04; API-01 e API-02 (F4) |
| Ordem | 4 (F1 — Base no ERP) |

## User story
Como **aplicativo conectado** (o Conecta primeiro, depois a API pública e o MCP), quero **consultar produtos, clientes, pedidos, cobranças e notas, e criar pedidos, com os mesmos limites de quem me autorizou**, para **atender o cliente na conversa sem ninguém copiar dado de um sistema para o outro**.

## Contexto
- A INT-01 cria o token de aplicativo; a ADR 0053 é a fonte única dos escopos, dos códigos de erro e do limite de uso. Esta story expõe as rotas.
- As rotas reusam os serviços e contratos dos módulos; **nenhuma regra é reescrita para a API**. Máscara, idempotência, IDOR e limites de desconto valem igual (VND-02a CA 2 a 5 e 13).

## Critérios de aceite

### Autenticação, escopos e as três camadas
1. **[Segurança]** **Dado** uma rota `/api/v1/integration/*`, **quando** chega sem token de aplicativo válido (ou com token de pessoa, aparelho ou TV), **então** 401 `invalid_token`; nenhuma rota de integração é anônima, e a resposta não diz se o recurso existe.
2. **[Segurança]** **Dado** um token válido, **quando** chamo uma rota, **então** valem as três camadas (INT-01 CA 16): módulo liberado (403 `module_not_released`), ação na concessão e nas permissões atuais do autorizador (403 `insufficient_scope`); autorizador desativado, 401 `app_grantor_inactive`.
3. **Dado** o mapa abaixo, **quando** o aplicativo chama a rota, **então** só passa com o escopo listado (teste de unidade gerado da tabela):

| Rota | Escopo |
|---|---|
| `GET /products`, `GET /products/{id}` | `catalog.view` (disponível só com `inventory.view`, CA 6) |
| `GET /customers?phone=` ou `?document=`, `GET /customers/{id}` | `parties.view` |
| `POST /customers` / `PATCH /customers/{id}` | `parties.create` / `parties.edit` (restrito, CA 11) |
| `POST /orders` / `GET /orders/{id}`, `GET /customers/{id}/orders` | `sales.create` / `sales.view` |
| `POST /orders/{id}/confirm` / `POST /orders/{id}/charge`, `POST /orders/{id}/cancel` | `sales.create` / `sales.edit` |
| `GET /customers/{id}/charges`, `GET /charges/{id}`, `GET /charges/{id}/pdf` | `finance.view` |
| `GET /orders/{id}/invoice`, `GET /invoices/{id}/danfe` | `fiscal.view` |
| `GET /customers/{id}/projects` | `projects.view` |

### Catálogo e disponível
4. **Dado** `catalog.view`, **quando** chamo `GET /products?q=&page=&pageSize=`, **então** recebo os produtos ativos e vendáveis: id, código, GTIN, descrição, unidade, preço da tabela padrão e a versão da tabela; **nunca custo nem margem**.
5. **Dado** a paginação, **quando** peço, **então** segue o `PageRequest` (página a partir de 1, `pageSize` até 100, padrão 20) com `items`, `page`, `pageSize` e `total`.
6. **Dado** `inventory.view` também, **quando** consulto um produto, **então** vem `available` = físico − reservado no local padrão, já descontadas as reservas dos pedidos; sem `inventory.view`, o campo não vem.

### Clientes (LGPD)
7. **[Segurança]** **Dado** `parties.view`, **quando** busco por **telefone** (E.164), **então** recebo id, tipo, nome, documento **mascarado** (`***.456.789-**`), cidade, UF e os indicadores `canBill` e `canInvoice`; nunca endereço completo, outros contatos, crédito ou histórico financeiro.
8. **[Segurança]** **Dado** `parties.view`, **quando** busco por **documento** (CPF ou CNPJ válido), **então** recebo só id, **primeiro nome e iniciais** ("Maria S. O."), `canBill` e `canInvoice` (quem já tem o documento não ganha o nome completo nem o contato).
9. **[Segurança]** **Dado** buscas por documento ou telefone, **quando** passam de 30 por minuto por conexão, **então** 429 `rate_limited` com `Retry-After`; o 409 `party_exists` do `POST /customers` **conta no mesmo limite**; há teto diário por tenant (`Integrations:LookupDailyCap`, padrão 2.000); mais de 50 documentos distintos em uma hora por conexão gera o alerta `integration_lookup_anomaly` aos Proprietários; cada busca grava auditoria com o termo em hash.
10. **Dado** `parties.create`, **quando** crio um cliente, **então** só são aceitos nome, tipo, documento, telefone, e-mail e endereço; documento já cadastrado devolve 409 `party_exists` com o id; o papel `Customer` é dado.
11. **[Segurança]** **Dado** `parties.edit`, **quando** o aplicativo chama `PATCH /customers/{id}`, **então** o **documento nunca muda**; e-mail, telefone e endereço **só preenchem campo vazio**; trocar um contato existente é só no ERP, por pessoa (422 `field_change_requires_backoffice`); quando um contato é trocado no ERP, o contato anterior recebe o aviso da troca.

### Pedidos
12. **[Segurança]** **Dado** `sales.create`, **quando** chamo `POST /orders` com `Idempotency-Key`, cliente, linhas (produto e quantidade, desconto opcional) e `externalId`, **então** o pedido nasce pela VND-02a em `Draft` "Pedido do Conecta: confirme com o cliente"; origem, `source_app_id` e `created_by` vêm do token, nunca do corpo; preço enviado é ignorado; desconto acima de `sales.app_discount_limit_pct` fica para aprovação (VND-02a CA 5); sem a chave, 400 `idempotency_key_required`; a chave tem escopo `tid + conexão + rota + hash do corpo`, e a mesma chave ou o mesmo `externalId` com outro corpo responde **409** (`idempotency_key_reused` ou `external_id_conflict`), nunca outro pedido.
13. **Dado** `POST /orders/{id}/confirm`, **quando** o cliente confirmou na conversa, **então** o pedido é confirmado com a reserva (VND-02a CA 9) dentro dos limites do aplicativo (VND-02a CA 13: quantidades, 5 pedidos em aberto por cliente, cliente cobrável); sem saldo, 409 `insufficient_stock` com as linhas em falta; desconto para aprovar, 403 `approval_required`.
14. **Dado** `POST /orders/{id}/charge`, **quando** o pedido está `Open` e o cliente `canBill`, **então** a cobrança é gerada pela VND-02a (CA 15), com vencimento de no máximo 3 dias, e a resposta é 202 com `status = Requested`; linha digitável, Pix e PDF chegam por `GET /charges/{id}` e pelo aviso `billing_slip.issued` (INT-02).
15. **Dado** `GET /orders/{id}` ou `GET /customers/{id}/orders`, **quando** consulto, **então** vejo número, estado, origem, linhas, total, cobrança (id e estado), nota (id e estado) e as datas de cada estado; sem custo, sem dado de outro cliente.
16. **Dado** `POST /orders/{id}/cancel` com motivo, **quando** o pedido está `Draft`, `Open` ou `AwaitingPayment`, **então** é cancelado pela VND-02a (CA 25); em `Paid` em diante, 409 `order_cancel_requires_backoffice` ("Este pedido já foi pago. O cancelamento é feito no ERP.").

### Cobranças, notas e projeto
17. **[Segurança]** **Dado** `finance.view`, **quando** chamo `GET /customers/{id}/charges`, **então** recebo os títulos em aberto do cliente (número, vencimento, valor, saldo, "Vencido") e, por título, só o boleto mais recente (`Issued`); `GET /charges/{id}` traz linha digitável, Pix copia-e-cola e o link do PDF, com `Cache-Control: no-store`; `provider_payload` nunca sai.
18. **Dado** `fiscal.view`, **quando** consulto a nota do pedido, **então** recebo tipo, situação, série, número, chave, data e o link do DANFE; só notas de produção e não descartadas.
19. **[Segurança]** **Dado** `projects.view` (perfil `factory`), **quando** chamo `GET /customers/{id}/projects`, **então** recebo um **DTO próprio e mínimo** (`IntegrationProjectStatus`: número, nome, etapa atual com letra e nome, situação e data prevista de entrega), montado a partir do `IProjects.ListAsync`; teste de campos proibidos garante que nenhum campo de custo, orçado, margem ou valor aparece.

### IDOR, limites, erros, auditoria e contrato
20. **[Segurança]** **Dado** um id de outro tenant, inexistente ou de outro cliente (`/customers/{a}/orders/{b}` com `b` de outro cliente; `/charges/{id}` sem relação com o cliente), **quando** chamo, **então** 404 igual nos três casos; o tenant vem do host e precisa bater com o `tid` do token (outro host: 401 `invalid_token`).
21. **Dado** um aplicativo, **quando** passa do limite, **então** 429 `rate_limited` com `Retry-After` pela política única **`app-api`** (INT-01 CA 19, parâmetro `Integrations:AppRateLimit`); o contador `integration_rate_limited{app}` alimenta o painel.
22. **Dado** qualquer erro, **quando** a API responde, **então** é ProblemDetails (RFC 9457) com `type`, `title` em PT-BR, `status`, `code` estável (lista da ADR 0053: `invalid_token`, `insufficient_scope`, `app_grantor_inactive`, `module_not_released`, `validation`, `not_found`, `rate_limited`, `idempotency_key_required`, `idempotency_key_reused`, `external_id_conflict`, `insufficient_stock`, `invalid_order_transition`, `approval_required`, `party_exists`…) e `errors` por campo; `traceId` junto e nada de pilha.
23. **Dado** qualquer escrita, **quando** é gravada, **então** a auditoria mostra "Integração {aplicativo} (autorizada por {pessoa})"; leituras de documento, cobrança e nota também ficam registradas (termo em hash).
24. **Dado** o build, **quando** o `OpenApiDocumentTests` roda, **então** o documento `integration-v1` (só `/api/v1/integration`) é comparado ao arquivo versionado `docs/integracoes/openapi-integration-v1.json`; remover ou renomear campo, rota ou código falha o teste.

## Regras de negócio e técnicas
- **Rotas finas:** cada endpoint chama o serviço ou contrato do módulo dono; o grupo não acessa tabela de módulo nenhum.
- **Versão:** `v1` só cresce de forma aditiva; quebra vai para `/api/v2`, com o v1 mantido por 6 meses e `Deprecation`/`Sunset`.
- **Datas** em ISO 8601 UTC; dinheiro decimal com 2 casas; enums como texto. **Sem CORS.** Respostas com dado pessoal: `Cache-Control: no-store`; logs sem documento, telefone ou e-mail.
- **Contrato com a ATD-01 (nota obrigatória):** o Conecta **confere o telefone da conversa contra o telefone do cliente** no ERP antes de ler cobranças ou notas daquele cliente; sem bater, não mostra.

## Permissões, eventos e tabelas
- **Permissões:** nenhuma nova; escopos da ADR 0053 (`catalog.view`, `inventory.view`, `parties.view|create|edit`, `sales.view|create|edit`, `finance.view`, `fiscal.view`, `projects.view`).
- **Eventos:** nenhum novo. **Tabelas:** nenhuma; contadores no rate limiter.

## Telas e estados
- Nenhuma tela nova. Em **Configurações → Integrações → Conecta** (INT-01), a referência gerada do `integration-v1` fica recolhida em **"Para desenvolvedores"**.
- **Ajuda:** "O que o Conecta poderá fazer" na ajuda da INT-01 usa o mesmo texto do catálogo.

## Tarefas
- [ ] Grupo `/api/v1/integration` com o esquema `App`, `RequireModule`, `RequirePermission` e a política `app-api`; limite de buscas e teto diário (CA 9).
- [ ] Endpoints de produtos, clientes, pedidos, cobranças, notas e projetos, só sobre contratos.
- [ ] Contratos novos: `IParties.FindByPhoneAsync`, `IParties.FillEmptyContactAsync`; `IOrderCharges.OpenByCustomerAsync` e leitura do boleto; `IOutgoingInvoices.BySaleAsync` e DANFE; `ISalesOrderLookup.ByCustomerAsync`; `IntegrationProjectStatus`.
- [ ] Aviso ao contato anterior quando o ERP troca e-mail ou telefone de cliente (Parties).
- [ ] OpenAPI `integration-v1` separado, arquivo versionado e teste de contrato.
- [ ] Integração (Testcontainers): uma prova por camada; IDOR (CA 20); máscara, nome abreviado, limite e teto das buscas, `party_exists` no limite (CA 7 a 9); `PATCH` restrito (CA 11); pedido idempotente e chave com outro corpo (CA 12); cancelar fora do estado (CA 16); campos proibidos do projeto (CA 19); auditoria (CA 23).
- [ ] Unidade: mapa escopo × rota a partir da tabela (CA 3).

## Dev Notes
- **Base:** `src/Modules/Sales/BemAqui.Sales.Endpoints/SalesEndpoints.cs`; `src/Host/BemAqui.Host/Security/PermissionAuthorization.cs`, `AuthRateLimiting.cs`, `DeviceAuthentication.cs`, `KioskAuthentication.cs`; `tests/BemAqui.Integration.Tests/Security/EndpointAuthorizationTests.cs`.
- **Comuns:** `src/BuildingBlocks/BemAqui.BuildingBlocks/Web/IdempotencyFilter.cs` (escopo por conexão, VND-02a), `Paging/PageRequest.cs`, `PagedList.cs`, `Logging`, `Persistence/Auditing`.
- **Catálogo:** `src/Modules/Catalog/BemAqui.Catalog.Contracts/CatalogContracts.cs`. **Estoque:** `src/Modules/Inventory/BemAqui.Inventory.Contracts/Stock.cs`.
- **Terceiros:** `src/Modules/Parties/BemAqui.Parties.Contracts/PartiesContracts.cs` (`FindByDocumentAsync` existe; telefone é novo).
- **Financeiro:** `src/Modules/Finance/BemAqui.Finance.Application/Billing/BillingSlipService.cs`.
- **Fiscal:** `src/Modules/Fiscal/BemAqui.Fiscal.Contracts/OutgoingInvoiceContracts.cs`.
- **Projeto:** `src/Modules/Commercial/BemAqui.Commercial.Contracts/ProjectContracts.cs` (`ProjectSummary` tem mais campos do que a API pode mostrar: por isso o DTO próprio).

## Fora de escopo
- Token, consentimento e revogação (INT-01); avisos de saída (INT-02).
- Aplicativo de terceiros e portal do desenvolvedor (API-01); MCP (API-02).
- Pedido de orçamento da fábrica (`quotes.create`, ATD-03); escrita em estoque, preço ou financeiro pela API.

## Pontos em aberto (para o PO)
1. O Conecta precisa do endereço completo do cliente para confirmar a entrega na conversa? A proposta mínima (LGPD) não o devolve.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 26/11/2026 | 0.1 | Story escrita a partir da ADR 0052 e do plano de integrações, conferida contra os contratos dos módulos. | Claude (planejamento) |
| 26/11/2026 | 0.2 | Recomendação aceita (PO 24/10), revisões de 26/11/2026: escopos e códigos de erro da ADR 0053; limite único `app-api`; idempotência por conexão com 409; busca por documento só com nome abreviado, teto diário, alerta de anomalia e `party_exists` no limite; `PATCH` restrito; DTO mínimo do projeto; nota do contrato ATD-01 sobre o telefone da conversa. | Claude (planejamento) |
