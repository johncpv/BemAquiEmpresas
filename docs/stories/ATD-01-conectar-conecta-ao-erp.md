# ATD-01 — Conectar com um clique (começa no ERP), credencial cifrada e recebimento de avisos no Conecta

| Campo | Valor |
|---|---|
| ID | ATD-01 |
| Título | O Conecta vira o cliente OAuth da INT-01: página de início que confirma a conta, PKCE S256 com `state` de uso único, conferência do `iss` e do host do ERP numa lista permitida, troca do código servidor a servidor, credencial da conexão cifrada com algoritmo autenticado, renovação serializada do `bqr_` rotativo, recebimento dos avisos da INT-02 com HMAC `v1=` sobre `{timestamp}.{corpo}`, janela de 5 min e sobreposição de segredo, deduplicação pelo `id`, desconexão dos dois lados e tela "BemAqui Empresas" nas Configurações do Conecta |
| Tamanho | M |
| Status | Draft (27/11/2026) |
| Requisitos | [ADR 0052](../adr/0052-integracoes-conecta-e-marketplaces.md) §1 e §2 · [ADR 0053](../adr/0053-aplicativos-conectados.md) · [Plano de integrações](../integracoes/00-plano-integracoes.md) §3.1, §3.2 e §9 · [INT-01](INT-01-aplicativos-conectados.md) · [INT-02](INT-02-webhooks-de-saida.md) · [INT-03](INT-03-api-integracao-v1.md) (contrato `docs/integracoes/openapi-integration-v1.json`) |
| Dependências | INT-01 e INT-02 (Done); INT-00 (termos e acordo de tratamento de dados antes da primeira conexão em produção) |
| Bloqueia | ATD-02, ATD-03a, ATD-03b, ATD-04, ATD-05, ATD-06 |
| Repositórios | **Conecta** (`D:\GIT\BemAquiAtende`: `bemaqui-atende-api`, `bemaqui-atende-web`) e **ERP** (só configuração de ambiente) |
| Ordem | 1 (F2 — Conecta) |

## User story
Como **administrador da conta do Conecta**, quero **confirmar a conta quando o Proprietário do ERP clica "Conectar"**, para **o Conecta passar a ler e criar pedidos e cobranças no ERP e a receber os avisos sem ninguém copiar chave nenhuma**.

## Contexto
- **ERP pronto (F1):** a conexão começa em Configurações → Integrações → Conecta (INT-01 CA 1). O ERP abre `Integrations:Conecta:StartUrl?erp=<host do tenant>&intent=<opaco>`, e o Conecta só confirma a conta e volta ao `https://<host>/oauth/authorize` com o mesmo `intent`. A troca do código devolve `access_token` (`bqa_`, 15 min), `refresh_token` (`bqr_`, rotativo), `scope`, `connection_id` e `webhook_secret` (uma vez só). Os avisos chegam por `POST` assinado na URL `Integrations:Conecta:WebhookUrl` (INT-02).
- **O que o Conecta tem hoje** (lido em 27/11/2026, branch `main`):
  - Backend **.NET 8** (`bemaqui-atende-api`: API com controllers, Application com MediatR, Infrastructure com EF Core + Npgsql e **Hangfire** no PostgreSQL), front **Vue 3 + Pinia + Tailwind** (`bemaqui-atende-web`), app **Expo/React Native** (`bemaqui-conecta`), um banco único com `TenantId` e filtro global (`ITenantEntity`), sessão por **JWT** com papéis `admin`, `supervisor`, `agent` (conferidos no controller) e tenant tirado do claim (`TenantMiddleware`).
  - Webhooks de entrada de sistemas externos em `POST /api/v1/webhooks/{contact_sync|purchase_confirmed|repurchase_trigger}` com **`X-Api-Key`** (`TenantApiKey`, só o hash) e log em `ExternalEvent`; **não** há verificação de assinatura nem janela de tempo para eles. O único HMAC de entrada é o da Meta (`MetaWebhookSignatureMiddleware`), que **deixa passar sem validar quando falta o segredo**: o recebedor novo não pode repetir isso.
  - Segredos por tenant: `IEncryptionService` (`EncryptionService`) é **AES-CBC sem autenticação**, com uma chave só, usado para a chave de IA. Não existe entidade de credencial de integração.
  - Sem limite de uso na API e sem cliente HTTP dedicado a integrações (há `HttpClient` tipados por serviço).
- **Nomes na interface:** no ERP, "Conecta"; no Conecta, o ERP aparece como **"BemAqui Empresas"** (ponto em aberto 3). "Avisos" no lugar de "webhooks".

## Critérios de aceite

### Início e confirmação da conta (Conecta)
1. **Dado** o ERP abrindo a página de início do Conecta (`/integracoes/bemaqui-empresas/conectar?erp=<host>&intent=<opaco>` no `bemaqui-atende-web`), **quando** não há sessão, **então** o Conecta pede o login e volta à mesma página com os parâmetros; com sessão de quem **não é `admin`**, mostra "Só o administrador da conta pode conectar o BemAqui Empresas." e nada é criado.
2. **[Segurança]** **Dado** o parâmetro `erp`, **quando** a página abre, **então** a API (`POST /api/v1/erp-integration/connect/start`) aceita só um **host** (sem esquema, porta, caminho, usuário ou query), em minúsculas, que esteja na lista `ErpIntegration:AllowedHosts` (sufixos exatos como `.bemaquiapp.com.br` e hosts próprios cadastrados pelo `superadmin`, ponto em aberto 1); fora da lista, a tela mostra "Este endereço não é de um BemAqui Empresas conhecido." e nada é enviado ao host. O segredo do cliente **nunca** vai a um host que não esteja na lista.
3. **Dado** um host válido, **quando** a tela de confirmação abre, **então** mostra "Conectar a conta **{nome da conta do Conecta}** ao BemAqui Empresas em **{host}**?" com o que o Conecta vai poder fazer (texto fixo, o mesmo da ajuda) e os botões "Conectar" e "Cancelar"; "Cancelar" volta ao `https://<host>/app/configuracoes/integracoes/conecta` sem criar nada.
4. **[Segurança]** **Dado** "Conectar", **quando** a API prepara a autorização, **então** grava uma **tentativa de conexão** (`erp_connect_attempts`: tenant, usuário, host, hash do `state`, `code_verifier` cifrado, `intent`, validade de 10 min, uso único), gera `state` (32 bytes) e `code_verifier` (64 caracteres não reservados, RFC 7636), e devolve a URL `https://<host>/oauth/authorize` com `response_type=code`, `client_id=conecta`, `redirect_uri` (a do ambiente, exata), `scope` (os 11 da ADR 0053), `state`, `code_challenge` (S256), `code_challenge_method=S256`, o **mesmo `intent`** e `account_label` = nome da conta do Conecta (até 120 caracteres, só texto).

### Retorno e troca do código
5. **[Segurança]** **Dado** o retorno em `redirect_uri` (rota do `bemaqui-atende-web`, que repassa `code`, `state`, `iss` ou `error` à API com o JWT da sessão), **quando** a API processa, **então** confere, nesta ordem: tentativa achada pelo hash do `state`, **do mesmo usuário e tenant**, não vencida e não usada (marcada usada na mesma transação); `iss` **igual** a `https://<host da tentativa>` (RFC 9207); sem isso, recusa com "Este retorno não corresponde a uma conexão iniciada por você. Comece de novo no ERP." e não chama o ERP. `error=access_denied` mostra "A conexão foi cancelada no ERP." com o link de volta.
6. **[Segurança]** **Dado** o retorno válido, **quando** a API troca o código (`POST https://<host>/api/v1/oauth/token`, `grant_type=authorization_code`, `client_secret_basic` com o segredo **do ambiente**, `code_verifier`, mesmo `redirect_uri`), **então** grava, **na mesma transação**, a conexão `ErpConnection` com host, `connection_id`, escopos concedidos, quem conectou, data, `access_token` e validade, `refresh_token` e `webhook_secret` **cifrados** (CA 8); a resposta do ERP nunca é registrada em log; `invalid_grant` ou `invalid_client` mostram "Não foi possível concluir a conexão. Comece de novo no ERP." com o código técnico em "Detalhes".
7. **Dado** uma conexão nova para um tenant que já tinha uma ativa (o ERP substitui a anterior, INT-01 CA 12), **quando** a troca termina, **então** a anterior vai a `Replaced` e os tokens dela são apagados; **no máximo uma conexão ativa por conta do Conecta** (índice único parcial), e a mesma `connection_id` do ERP não fica ligada a duas contas do Conecta (índice único em `(erp_host, erp_connection_id)`).

### Credencial cifrada
8. **[Segurança]** **Dado** `access_token`, `refresh_token`, `webhook_secret` e o segredo anterior, **quando** são gravados, **então** usam um protetor **autenticado** (AES-256-GCM com identificador da chave, chave própria de integrações lida de arquivo de secret, **não** a chave da IA nem o `EncryptionService` CBC; ponto em aberto 2); nenhum desses valores sai em resposta da API do Conecta, em log, em SignalR, no Hangfire (jobs recebem só ids) nem no `ExternalEvent`. Teste canário no log.
9. **[Segurança]** **Dado** o segredo do cliente OAuth (`client_secret`, um por ambiente, ADR 0053 §2), **quando** a API sobe, **então** ele é lido de **arquivo de secret** montado no contêiner (`ErpIntegration:ClientSecretFile`), nunca do banco nem do `.env` versionado; sem ele, a página de início mostra "Integração com o BemAqui Empresas indisponível neste ambiente." (falha fechada).

### Uso e renovação do token
10. **[Segurança]** **Dado** uma chamada ao ERP, **quando** o `access_token` vence em menos de 60 s, **então** o Conecta renova **antes** (`grant_type=refresh_token`) sob **trava por conexão** (`pg_advisory_xact_lock` pelo id da conexão), grava o par novo **antes** de usar o token e só então chama; duas renovações simultâneas nunca usam o mesmo `bqr_` (teste de concorrência). Se a resposta da renovação se perder, a nova tentativa com o mesmo `bqr_` acontece **em até 10 s** (tolerância do ERP, INT-01 Change Log item 10); depois disso, não reapresenta e marca a conexão "Precisa conectar de novo".
11. **Dado** a resposta da renovação com `webhook_secret` (troca de segredo pedida no ERP, INT-02 CA 6), **quando** chega, **então** o novo vira o atual e o atual vira o anterior, válido até `agora + 24 h`, **na mesma transação** dos tokens novos.
12. **Dado** um erro do ERP, **quando** a resposta chega, **então** o Conecta reage pelo `code` (ADR 0053 §7): 401 `invalid_token` → uma renovação e uma nova tentativa, e se falhar de novo a conexão vai a "Desconectada no ERP"; `invalid_grant` na renovação → "Desconectada no ERP" (tokens apagados, a família foi revogada); 401 `app_grantor_inactive` → "Suspensa: quem autorizou no ERP perdeu o acesso"; 403 `insufficient_scope` → a função some da interface com a `<Dica>` "O ERP não liberou isso para o Conecta"; 403 `module_not_released` → "A integração não está liberada no ERP"; 429 `rate_limited` → espera o `Retry-After` (fila, nunca laço); 5xx e tempo esgotado → nova tentativa com espera crescente só em leitura (escritas seguem o CA da story que as faz).
13. **[Segurança]** **Dado** o cliente HTTP do ERP (`HttpClient` nomeado `bemaqui-erp`), **quando** chama, **então** usa só o host gravado na conexão (nunca um host vindo de aviso, de mensagem ou de parâmetro), `https`, validação TLS ligada, sem seguir redirecionamento, tempo de conexão 5 s e total 15 s, cabeçalhos `Authorization` fora do log, e `User-Agent: BemAqui-Conecta/1`.

### Recebimento dos avisos
14. **[Segurança]** **Dado** o recebedor `POST /api/v1/erp-integration/events` (anônimo de pessoa, autenticado pela assinatura), **quando** chega um aviso, **então** o Conecta: (a) recusa corpo acima de 256 KB (413); (b) lê o corpo **cru** uma vez; (c) extrai só `connection_id` e `tenant` do JSON e acha a conexão ativa por `connection_id` **e** `tenant` = host gravado; (d) confere `BemAqui-Timestamp` dentro de **±5 min** do relógio; (e) calcula o HMAC-SHA256 de `"{timestamp}.{corpo cru}"` com cada segredo válido (o atual e, se dentro das 24 h, o anterior; chave = o segredo em bytes UTF-8, INT-02 Change Log item 2) e compara em **tempo constante** com **cada** `v1=` do cabeçalho `BemAqui-Signature` (que pode trazer `v1=<nova>,v1=<antiga>`); qualquer falha responde **401 sem detalhe** e conta para o alerta "assinatura inválida"; conexão desconhecida responde 401 igual (não diz se existe). **Nunca** passa sem validar por falta de segredo.
15. **Dado** um aviso válido, **quando** é aceito, **então** grava `erp_inbound_events` com o `id` (`BemAqui-Event-Id`) único por conexão, tipo, `occurred_at`, `resource` e o corpo (que, pelo contrato, não tem dado pessoal), enfileira o processamento no Hangfire (`ProcessErpEventJob` com `tenantId` e o id do registro) e responde **202 em menos de 2 s**; o mesmo `id` de novo responde 200 sem processar outra vez (deduplicação).
16. **[Segurança]** **Dado** o processamento, **quando** o aviso traz `resource`, **então** o Conecta só o segue se for caminho relativo começando por `/api/v1/integration/` e o chama no host da conexão; a ordem não é garantida (INT-02 CA 9), então o processamento **relê o recurso** e decide pelo estado atual, nunca pelo `data` do aviso sozinho.
17. **Dado** `app.disconnected`, **quando** chega assinado, **então** a conexão vai a "Desconectada no ERP", os tokens e os segredos são apagados e os administradores do Conecta recebem a notificação; `ping` só atualiza "último aviso recebido". Os tipos de negócio (`sales_order.*`, `billing_slip.*`, `invoice.*`) nesta story **só são registrados e marcados `Processed`**; quem reage a eles são a ATD-02 (atualiza o painel por SignalR) e a ATD-05 (avisos e funil).

### Ver e desconectar
18. **Dado** Configurações → Integrações → **BemAqui Empresas** no Conecta (só `admin`), **quando** abro, **então** cada situação aparece com chip, motivo e ação: "Conectado" ("Conectado em dd/mm por Fulano ao {host}", último aviso recebido, o que o Conecta pode fazer, "Desconectar"); "Suspensa" (motivo e "Peça ao Proprietário do ERP para autorizar de novo em Configurações → Integrações → Conecta"); "Desconectada no ERP" e "Precisa conectar de novo" (com o mesmo caminho); sem conexão, "Para conectar, o Proprietário do ERP clica em Conectar em Configurações → Integrações → Conecta" (não há botão de início no Conecta, ADR 0052 §2). O aviso de "autorizar de novo" aparece 30 dias antes de `reconsent_due_at` (lido de `GET /api/v1/integration/connection`).
19. **Dado** "Desconectar" no Conecta, **quando** o `admin` confirma ("O atendimento deixa de ver pedidos e cobranças do ERP agora. Pedidos já criados continuam no ERP."), **então** o Conecta chama `POST https://<host>/api/v1/oauth/revoke` com o `refresh_token` (RFC 7009, autenticação do cliente), apaga tokens e segredos, grava quem e quando, e a conexão vai a "Desconectada"; se o ERP não responder, apaga do mesmo jeito e registra "revogação pendente", com nova tentativa em fila.
20. **Dado** a lista de avisos recebidos (aba existente de eventos externos), **quando** abro, **então** os avisos do ERP aparecem com tipo, situação (Recebido, Processado, Falhou, Ignorado), data e "Reprocessar" para os que falharam; a assinatura inválida aparece só como contagem, sem corpo.

## Regras de negócio e técnicas
- **Uma conta do Conecta ↔ um tenant do ERP** na v1 (ponto em aberto 4).
- **Escopos pedidos:** os 11 da ADR 0053 (`catalog.view`, `inventory.view`, `parties.view`, `parties.create`, `parties.edit`, `sales.view`, `sales.create`, `sales.edit`, `finance.view`, `fiscal.view`, `projects.view`). A concessão real é a interseção feita pelo ERP; o Conecta lê `scope` da troca e `GET /api/v1/integration/connection` e liga cada função só com o escopo dela.
- **Idempotência (para as stories que escrevem):** toda escrita no ERP leva `Idempotency-Key` gerada no Conecta e gravada antes da chamada (o ERP escopa por `tid + conexão + rota + hash do corpo`, ADR 0053 §13); repetir a chamada com a mesma chave e o mesmo corpo devolve o original; mudar o corpo exige chave nova. O helper `ErpApiClient.SendIdempotentAsync` nasce aqui.
- **Limite de uso do ERP:** `app-api` por conexão (padrão 600 leituras e 120 escritas por minuto) e buscas por telefone ou documento com 30/min por conexão e 2.000/dia por tenant (INT-03 CA 9). O cliente do Conecta respeita `Retry-After` e as stories seguintes cacheiam o vínculo do contato para não buscar de novo.
- **Mesma VPS:** o ERP chama o recebedor pelo host público do Conecta (lista `WebhookAllowedHosts` do ERP; a INT-02 recusa IP privado), e o Conecta chama o ERP pelo host público; conferir o *hairpin* dos dois contêineres para o IP público. Nunca rede interna.
- **Restauração de backup do Conecta:** um banco restaurado traz um `bqr_` antigo, e reapresentá-lo revoga a conexão (ADR 0053 §3). O runbook do Conecta ganha "depois de restaurar, conectar de novo pelo ERP".

## O que muda em cada repositório
**Conecta (`bemaqui-atende-api`):**
- Domínio: `ErpConnection` (`ITenantEntity`, `erp_host`, `erp_connection_id`, `status`, `status_reason`, `scopes text[]`, `account_label`, `connected_by_user_id`, `connected_at`, `reconsent_due_at`, `access_token_enc`, `access_token_expires_at`, `refresh_token_enc`, `webhook_secret_enc`, `previous_webhook_secret_enc`, `previous_valid_until`, `last_event_at`, `revoke_pending`, `xmin`), `ErpConnectAttempt`, `ErpInboundEvent`; migration aditiva.
- Infrastructure: `IntegrationSecretProtector` (AES-GCM), `ErpApiClient` (renovação com trava, erros, idempotência), `ErpOAuthClient` (token e revoke), `ErpEventSignatureVerifier`, `ProcessErpEventJob`, varredor diário de tentativas vencidas e expurgo de corpos com mais de 90 dias.
- API: `ErpIntegrationController` (`connect/start`, `connect/callback`, `GET status`, `POST disconnect`, só `admin`), `ErpEventsController` (recebedor anônimo com assinatura).
- Testes em `tests/BemAquiAtende.Tests`: PKCE (vetores da RFC 7636), host na lista, `iss`, assinatura com vetor fixo (uma e duas `v1`, janela, segredo anterior vencido), deduplicação, renovação concorrente, canário de log; isolamento por tenant no recebedor.

**Conecta (`bemaqui-atende-web`):** rota pública `/integracoes/bemaqui-empresas/conectar` e `/integracoes/bemaqui-empresas/retorno`, seção "BemAqui Empresas" em `SettingsView.vue`, item na lista de eventos recebidos.

**ERP (só configuração, sem código):** em cada ambiente (SaaS e Konebras), `Integrations__Conecta__StartUrl`, `RedirectUris` (a rota de retorno exata), `WebhookUrl` (o recebedor), `WebhookAllowedHosts` (o host da API do Conecta) e `DefaultEvents` (todo o catálogo v1 da INT-02); geração do `client_secret` do ambiente, com o SHA-256 no ERP (`ClientSecretHash`, arquivo de secret) e o valor no arquivo de secret do Conecta, entregue por canal seguro, nunca por chat ou commit. Ajuda `Ajuda/configuracoes-integracoes-conecta.md`: "Conectar" leva ao Conecta para confirmar a conta.

## Telas e estados (Conecta)
- **Confirmar conexão:** carregando; host fora da lista; sem permissão; confirmação; "Conectando…" com o botão desabilitado; erro com "Comece de novo no ERP"; sucesso "Conectado ao BemAqui Empresas" com o próximo passo ("Abra uma conversa: o painel do cliente mostra pedidos e cobranças do ERP", ATD-02).
- **Configurações → Integrações → BemAqui Empresas:** estados do CA 18; "Desconectar" com o impacto; carregando e erro com "Tentar de novo".
- **Verbos:** "Conectar" → "Conectado"; "Desconectar" → "Desconectado"; "Reprocessar" → "Reprocessado".
- **Ajuda:** o Conecta não tem ajuda contextual por arquivo como o ERP; o texto de cada estado e o "O que o Conecta vai poder fazer" ficam num arquivo de textos único, reaproveitado no manual (`docs/MANUAL_ADMINISTRADOR.md` do Conecta).

## Plano de testes
- **Unidade (Conecta):** PKCE S256; normalização e lista de hosts (com `evil.com`, `bemaquiapp.com.br.evil.com`, `user@host`, porta, IDN); `state` e `iss`; assinatura (vetores fixos, duas `v1`, anterior dentro e fora das 24 h, timestamp fora da janela, corpo alterado em um byte); classificação dos erros do CA 12.
- **Integração (Conecta, PostgreSQL real e ERP falso por `HttpMessageHandler`):** fluxo completo (CA 4 a 6); `state` de outro usuário e reusado (CA 5); substituição (CA 7); renovação concorrente com uma só chamada de `refresh` (CA 10); `webhook_secret` na renovação (CA 11); recebedor com aviso válido, duplicado, assinatura errada e conexão de outro tenant (CA 14, 15); `app.disconnected` (CA 17); desconectar com ERP fora do ar (CA 19); canário de log (CA 8).
- **Ponta a ponta (manual, no piloto 21/12):** ERP SaaS `erp.bemaquiapp.com.br` com o Conecta de produção: conectar, "Enviar teste" do ERP chega como `ping`, "Trocar segredo" no ERP sem perder aviso, desconectar dos dois lados.

## Tarefas
- [ ] Conecta: entidades, migration e protetor AES-GCM com chave em arquivo de secret.
- [ ] Conecta: início, confirmação, retorno e troca do código; lista de hosts e `iss`.
- [ ] Conecta: `ErpApiClient` com renovação serializada, erros e idempotência.
- [ ] Conecta: recebedor assinado, deduplicação, job, `app.disconnected`, expurgo.
- [ ] Conecta: tela nas Configurações e avisos na lista de eventos; textos no manual.
- [ ] ERP: variáveis de ambiente no SaaS e na Konebras; `client_secret` gerado e entregue; ajuda da página do Conecta.
- [ ] Fluxo do repositório do Conecta: branch, revisão do mantenedor, merge na `main` e só então deploy (ADR 0052, Consequências).

## Dev Notes
- **Conecta, arquivos de referência:** `bemaqui-atende-api/src/BemAquiAtende.API/Program.cs` (autenticação JWT, sem rate limiter), `Middlewares/MetaWebhookSignatureMiddleware.cs` (leitura do corpo cru com `EnableBuffering`; **não** copiar a falha aberta), `Controllers/ExternalWebhookController.cs` e `Domain/Entities/ExternalEvent.cs` (log de eventos e "Reprocessar"), `Infrastructure/Services/EncryptionService.cs` (CBC, só para comparar), `Infrastructure/DependencyInjection.cs` (Hangfire PostgreSQL, `AddHttpClient`), `Infrastructure/Jobs/HangfireJobScheduler.cs`, `Domain/Entities/Tenant.cs`, `tests/BemAquiAtende.Tests/MultiTenantQueryFilterTests.cs`.
- **Papéis do Conecta:** `admin`, `supervisor`, `agent` (e `superadmin` da plataforma), conferidos no controller (`currentUser.Role`).
- **ERP, contrato:** `src/Modules/Identity/BemAqui.Identity.Application/ConnectedApps/ConnectedAppOptions.cs` (`OAuthRules`: mesmas regras de PKCE e de URI que o Conecta deve respeitar), `docs/integracoes/openapi-integration-v1.json` (`GET /api/v1/integration/connection`, `GET /api/v1/integration/webhooks/subscription`).
- **Assinatura:** INT-02 CA 5 e Change Log (chave = o segredo base64url em bytes UTF-8; `User-Agent: BemAqui-Webhooks/1`; mesmo corpo em todas as tentativas).
- **Sem cabeçalho de conexão no aviso:** o recebedor lê `connection_id` do corpo antes de validar; por isso o limite de tamanho e a leitura mínima (CA 14). Ponto em aberto 5 propõe um cabeçalho aditivo no ERP.

## Fora do escopo
- Painel do cliente, pedidos, IA, avisos ao cliente e WhatsApp do ERP (ATD-02 a ATD-06).
- Botão de início no Conecta (ADR 0052 §2), várias contas de ERP numa conta do Conecta, aplicativo de terceiros.
- Trocar o `EncryptionService` CBC do resto do Conecta (chave de IA, credenciais de canal): fica registrado como risco para o mantenedor.

## Pontos em aberto (para o PO)
1. **Hosts permitidos:** tenants do ERP podem usar domínio próprio (`catalog.tenant_domains`), como `app.konebras.com.br`. **Recomendação:** lista no Conecta com sufixos do BemAqui e hosts próprios cadastrados pelo `superadmin` da plataforma do Conecta; nunca aceitar qualquer host, porque o segredo do cliente seria enviado a ele.
2. **Cifra:** o plano (§3.2) fala em `IEncryptionService`, mas ele é AES-CBC sem autenticação e com a mesma chave da IA. **Recomendação:** protetor AES-GCM novo, com chave só de integrações, mantendo a interface parecida para o mantenedor trocar o resto depois.
3. **Nome do ERP no Conecta:** "BemAqui Empresas" (nome do produto) ou "BemAqui ERP". **Recomendação:** "BemAqui Empresas".
4. **Uma conta do Conecta com mais de um ERP** (grupo com duas empresas): **recomendação:** fora da v1.
5. **Cabeçalho `BemAqui-Connection` no aviso** (aditivo na INT-02) para o recebedor achar a conexão sem ler o corpo. **Recomendação:** pedir na próxima story do ERP que tocar nos avisos; até lá, o CA 14 resolve com o limite de tamanho.
6. **Termos:** a política de privacidade e os termos do Conecta precisam citar o tratamento de dados vindos do ERP (o Conecta como operador do tenant). **Recomendação:** revisão junto com a INT-00, antes da primeira conexão com dado real.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 27/11/2026 | 0.1 | Story escrita a partir da ADR 0052/0053, das INT-01 a INT-03 implementadas e da leitura do repositório do Conecta (`main`, 27/11/2026). | Claude (planejamento) |
