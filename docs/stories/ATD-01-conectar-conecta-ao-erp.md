# ATD-01 — Conectar com um clique (começa no ERP), credencial cifrada e recebimento de avisos no Conecta

| Campo | Valor |
|---|---|
| ID | ATD-01 |
| Título | O Conecta vira o cliente OAuth da INT-01: código de pareamento gerado pelo administrador do Conecta e colado no ERP, confirmação com o nome da empresa e o host real, PKCE S256 com `state` de uso único, conferência do `iss`, troca do código servidor a servidor, credencial cifrada com AES-GCM e rotação de chave, renovação serializada do `bqr_` rotativo, recebedor de avisos achado pelo `BemAqui-Connection` com HMAC `v1=` sobre `{timestamp}.{corpo}`, janela de 5 min e sobreposição de segredo, deduplicação pelo `id`, desconexão dos dois lados, tela "BemAqui Empresas" e o arquivo único de textos do Conecta |
| Tamanho | M |
| Status | Draft (27/11/2026) |
| Requisitos | [ADR 0052](../adr/0052-integracoes-conecta-e-marketplaces.md) §1 e §2 · [ADR 0053](../adr/0053-aplicativos-conectados.md) · [Plano](../integracoes/00-plano-integracoes.md) §3.1, §3.2 e §9 · [INT-01](INT-01-aplicativos-conectados.md) · [INT-02](INT-02-webhooks-de-saida.md) · [INT-03](INT-03-api-integracao-v1.md) · [INT-04](INT-04-ajustes-avisos-e-conexao.md) · decisões do PO de 27/11/2026 (2) |
| Dependências | INT-04; INT-01 a INT-03 (Done); INT-00 (termos e acordo de tratamento de dados antes da primeira conexão com dado real) |
| Bloqueia | ATD-02, ATD-03a, ATD-03b, ATD-04, ATD-05a, ATD-05b, ATD-06b |
| Repositórios | **Conecta** (`D:\GIT\BemAquiAtende`: `bemaqui-atende-api`, `bemaqui-atende-web`) e **ERP** (configuração de ambiente) |
| Ordem | 1 (F2, depois da INT-04) |

## User story
Como **administrador da conta do Conecta**, quero **gerar um código, passar ao Proprietário do ERP e confirmar a conexão vendo o nome da empresa e o endereço real do ERP**, para **o Conecta ler e criar pedidos e cobranças e receber os avisos sem ninguém copiar chave e sem risco de conectar no lugar errado**.

## Contexto
- **ERP (F1 + INT-04):** a conexão começa em Configurações → Integrações → Conecta; o Proprietário cola o código de pareamento e o ERP abre `Integrations:Conecta:StartUrl?erp=<host>&intent=<opaco>&pairing=<código>`; o Conecta confirma a conta e volta ao `https://<host>/oauth/authorize` com o mesmo `intent`. A troca devolve `access_token` (`bqa_`, 15 min), `refresh_token` (`bqr_`, rotativo; reuso fora de 10 s revoga a família e a conexão), `scope`, `connection_id` e `webhook_secret` (reentregue até a prova da INT-04 CA 5). Avisos com `BemAqui-Connection`, `BemAqui-Event-Id`, `BemAqui-Event-Type`, `BemAqui-Timestamp` e `BemAqui-Signature`; `tenant` no corpo é o **slug** da empresa.
- **Conecta hoje** (lido em 27/11/2026, `main`): .NET 8 (MediatR, EF Core + Npgsql, Hangfire no PostgreSQL), Vue 3 + Pinia, app Expo; banco único com `TenantId` e filtro global (`ITenantEntity`); JWT com papéis `admin`, `supervisor`, `agent` conferidos no controller. Webhooks de outros sistemas entram por `X-Api-Key` sem assinatura; o HMAC da Meta (`MetaWebhookSignatureMiddleware`) **deixa passar sem validar quando falta o segredo**; `EncryptionService` é AES-CBC sem autenticação, com a chave da IA. Sem limite de uso na API.

## Glossário e textos (vale para todas as ATD)
- Arquivo único de textos do Conecta para a integração (`bemaqui-atende-web/src/texts/bemaquiEmpresas.ts`, reaproveitado no app e no manual). Regras: na tela do Conecta **nunca** "ERP", sempre **"BemAqui Empresas"**; "vínculo" → **"cliente identificado"**; "janela de 24 h" → **"o cliente falou com você nas últimas 24 h"**; "modelo aprovado" → **"mensagem pronta aprovada pelo WhatsApp"**; "webhook/evento" → **"aviso"**.
- Todo vazio tem o próximo passo; todo erro diz o que houve, quem resolve e um botão; falha de comunicação: **"Não consegui falar com o BemAqui Empresas. Tentar de novo"**, e depois de 3 falhas seguidas, **"Avisar o administrador"** (notificação ao `admin`); quem pode agir vê o botão, quem não pode vê "Peça a {papel} para…".

## Critérios de aceite

### Pareamento e confirmação
1. **Dado** Configurações → Integrações → **BemAqui Empresas** (só `admin`), **quando** clico "Gerar código de pareamento", **então** o Conecta mostra um código `XXXX-XXXX` (alfabeto sem caracteres ambíguos, 40 bits), válido por **15 min**, uso único, guardado só em hash com a conta e o `admin` que gerou, com o texto "Passe este código a quem é Proprietário no BemAqui Empresas. Ele cola o código ao clicar em Conectar."; gerar outro invalida o anterior.
2. **Dado** o ERP abrindo a página de início (`/integracoes/bemaqui-empresas/conectar?erp=&intent=&pairing=`), **quando** não há sessão, **então** pede o login e volta com os parâmetros; com sessão de quem não é `admin`, mostra "Peça a quem administra o Conecta para abrir este link." com "Copiar link" e nada é criado.
3. **[Segurança]** **Dado** os parâmetros, **quando** a API valida (`POST /api/v1/erp-integration/connect/start`), **então** exige, nesta ordem: código de pareamento válido, não vencido, não usado e **da mesma conta do `admin` logado**; `erp` como **host puro** (sem esquema, porta, caminho, usuário, query, IP ou IDN fora do ASCII) que passe no **filtro de sufixos** `ErpIntegration:AllowedHostSuffixes` ou esteja entre os hosts próprios cadastrados pelo `superadmin`; só então busca `GET https://<host>/api/v1/public/tenant-context` (sem credencial, 5 s) para o nome da empresa. Falhou qualquer passo: "Este pedido de conexão não veio do seu BemAqui Empresas. Gere um código novo e comece de novo no ERP." O segredo do cliente nunca vai a um host que não passou aqui.
4. **Dado** tudo válido, **quando** a confirmação abre, **então** mostra em destaque **o nome da empresa** (vindo do host) e **o host real** ("erp.suaempresa.com.br"), a conta do Conecta e, em 4 a 5 linhas: "Ver clientes, pedidos, cobranças e notas" · "Criar clientes e pedidos e gerar cobranças" · "Não vê custo nem margem" · "Você desconecta quando quiser"; botões "Conectar" e "Cancelar" ("Cancelar" volta ao ERP sem criar nada).
5. **[Segurança]** **Dado** "Conectar", **quando** a API prepara a autorização, **então** consome o código de pareamento e grava a tentativa (`erp_connect_attempts`: tenant, usuário, host, hash do `state`, `code_verifier` cifrado, `intent`, 10 min, uso único), gera `state` (32 bytes) e `code_verifier` (64 caracteres, RFC 7636) e devolve a URL do `/oauth/authorize` com `response_type=code`, `client_id=conecta`, `redirect_uri` exata do ambiente, `scope` (os escopos da ADR 0053 com os da INT-04), `state`, `code_challenge` S256, o mesmo `intent` e `account_label` = nome da conta (até 120, só texto).

### Retorno e troca
6. **[Segurança]** **Dado** o retorno (rota do `bemaqui-atende-web` que repassa `code`, `state`, `iss` ou `error` à API com o JWT), **quando** a API processa, **então** confere: tentativa pelo hash do `state`, do mesmo usuário e conta, não vencida e não usada (marcada usada na mesma transação); `iss` igual a `https://<host da tentativa>` (RFC 9207); falhou, "Este retorno não corresponde a uma conexão iniciada por você. Comece de novo no ERP." e não chama o ERP; `error=access_denied` mostra "A conexão foi cancelada no BemAqui Empresas.".
7. **[Segurança]** **Dado** o retorno válido, **quando** a API troca o código (`client_secret_basic` com o segredo do ambiente, `code_verifier`, mesmo `redirect_uri`), **então** grava na mesma transação a `ErpConnection` (host, `connection_id`, escopos, quem conectou, data, tokens e `webhook_secret` cifrados, CA 9) e leva o navegador a `https://<host>/app/configuracoes/integracoes/conecta?conectado=1` (INT-04 CA 10); no Conecta, "Conectado à {empresa}" com o próximo passo ("Abra uma conversa: o painel mostra pedidos e cobranças"); `invalid_grant`/`invalid_client`: "Não consegui concluir a conexão. Gere um código novo e comece de novo no ERP." com o código técnico em "Detalhes". A resposta do ERP nunca vai para log.
8. **Dado** uma conexão nova numa conta que já tinha uma, **quando** a troca termina, **então** a anterior vai a `Replaced` com tokens apagados; no máximo uma ativa por conta (índice único parcial) e `(erp_host, erp_connection_id)` único. Os pedidos continuam do Conecta daquela empresa (INT-04 CA 6), sem órfãos.

### Credencial cifrada
9. **[Segurança]** **Dado** tokens e segredos da conexão, **quando** gravados, **então** usam o `IntegrationSecretProtector`: AES-256-GCM, nonce aleatório, **AAD = tenant + conexão + nome do campo** (um valor copiado para outra linha ou campo não abre), identificador da chave no texto cifrado; chaves em **arquivo de secret** fora do banco e **fora do backup do banco**; **rotação** com uma primária e as anteriores só para abrir, e job de re-cifragem; nada disso sai em resposta, log, SignalR, Hangfire (jobs só com ids) ou `ExternalEvent` (canário). O runbook do Conecta ganha "chave de integrações comprometida": nova primária, re-cifrar, pedir ao ERP "Trocar segredo" com "Invalidar o anterior agora" e desconectar/conectar.
10. **[Segurança]** **Dado** o `client_secret` do ambiente (ADR 0053 §2), **quando** a API sobe, **então** é lido de arquivo de secret (`ErpIntegration:ClientSecretFile`); sem ele, a integração fica indisponível com "Integração com o BemAqui Empresas indisponível neste ambiente." (falha fechada).

### Token, renovação e erros
11. **[Segurança]** **Dado** uma chamada ao ERP com o `access_token` a menos de 60 s do fim, **quando** o Conecta renova, **então**: `SELECT … FOR UPDATE` na linha da conexão com `lock_timeout` de 5 s; **relê `access_token_expires_at` depois da trava** (outra renovação pode ter acabado) e só renova se ainda precisar; grava `refresh_started_at` (intenção) e faz o `refresh`; grava o par novo e o `webhook_secret` (se vier) e limpa a intenção **na mesma transação**; só então usa o token. Caiu entre a resposta e o commit: na próxima tentativa, com `refresh_started_at` há menos de 10 s, repete o mesmo `bqr_` (tolerância do ERP, que devolve par novo e o segredo pendente, INT-04 CA 5); com mais de 10 s, não reapresenta, marca "Precisa conectar de novo" e notifica os `admin` (teste da queda entre resposta e commit; teste de duas renovações simultâneas com uma só chamada).
12. **Dado** `webhook_secret` na resposta, **quando** chega, **então** o novo vira atual e o atual vira anterior, válido até agora + 24 h, na mesma transação.
13. **Dado** um erro do ERP, **quando** chega, **então** o Conecta reage pelo `code`: `invalid_token` → uma renovação e uma nova tentativa, depois "Desconectada no BemAqui Empresas"; `invalid_grant` na renovação → "Desconectada no BemAqui Empresas"; `app_grantor_inactive` → "Suspensa: quem autorizou no BemAqui Empresas perdeu o acesso"; `insufficient_scope` → a função some com a `<Dica>` "O BemAqui Empresas não liberou isso para o Conecta"; `module_not_released` → "A integração não está liberada no BemAqui Empresas"; `rate_limited` → espera o `Retry-After` em fila; **`idempotency_in_progress`** → espera 2 s e repete com **a mesma chave** (até 3 vezes); **`concurrency_conflict`** → relê o recurso e mostra "O pedido mudou no BemAqui Empresas. Recarregar."; **`bank_slip_method_missing`** → "O BemAqui Empresas ainda não configurou a forma de pagamento do boleto. Avise o administrador de lá."; 5xx e tempo esgotado → nova tentativa com espera crescente só em leitura.
14. **[Segurança]** **Dado** o `HttpClient` `bemaqui-erp`, **quando** chama, **então** usa só o host gravado na conexão, `https`, TLS validado, sem redirecionamento, conexão 5 s e total 15 s, `Authorization` fora do log, `User-Agent: BemAqui-Conecta/1`; teste de IDOR: um `ErpApiClient` montado para a conta A nunca usa a conexão da conta B.

### Recebedor de avisos
15. **[Segurança]** **Dado** `POST /api/v1/erp-integration/events` (anônimo de pessoa, autenticado pela assinatura), **quando** chega um aviso, **então**: limite por IP e por `BemAqui-Connection` (429, sem bloqueio por contagem); corpo lido em *stream* com teto de **64 KB** (413 acima); conexão achada pelo cabeçalho `BemAqui-Connection` numa consulta controlada (`IgnoreQueryFilters` só aqui, por `erp_connection_id`, status ativo); `BemAqui-Timestamp` dentro de ±5 min; HMAC-SHA256 de `"{timestamp}.{corpo cru}"` com o segredo atual e, dentro das 24 h, o anterior (chave = segredo em bytes UTF-8), comparado em tempo constante com **cada** `v1=` do cabeçalho; qualquer falha, inclusive conexão desconhecida, responde **401 sem detalhe e com latência uniforme** (calcula um HMAC de mentira quando não há conexão); **nunca** passa sem validar por falta de segredo.
16. **[Segurança]** **Dado** a assinatura válida, **quando** o corpo é lido, **então** o `tenant` do corpo é comparado ao gravado na conexão (gravado no **primeiro `ping` válido** depois de conectar; depois, diferente = 401 e alerta), `connection_id` do corpo = cabeçalho; o `TenantId` do Conecta é **fixado a partir da conexão** antes de gravar ou enfileirar.
17. **Dado** o aviso aceito, **quando** grava, **então** `erp_inbound_events` com `id` único por conexão, tipo, `occurred_at`, `resource` e corpo; enfileira `ProcessErpEventJob` (`tenantId` e id do registro) e responde **202 em menos de 2 s**; `id` repetido responde 200 sem processar.
18. **[Segurança]** **Dado** o processamento, **quando** há `resource`, **então** ele é lido com `Uri` como caminho relativo e **recusado** se tiver esquema, host, `..`, `//`, `%`, `\` ou não começar por `/api/v1/integration/`; a URL é reconstruída só a partir do host da conexão (ou mapeada pelo tipo, INT-04 CA 1); a ordem não é garantida, então o processamento relê o recurso e decide pelo estado atual.
19. **Dado** `app.disconnected`, **quando** chega assinado, **então** a conexão vai a "Desconectada no BemAqui Empresas", tokens e segredos são apagados e os `admin` são notificados; `ping` atualiza "último aviso recebido". Os tipos de negócio são registrados e entregues às reações das ATD-02, ATD-03a, ATD-05a/b e ATD-06b.
20. **Dado** nenhum aviso válido (inclusive `ping`) em **24 h** com a conexão ativa, **quando** o varredor roda, **então** a tela mostra "Avisos não verificados: o Conecta não recebe avisos do BemAqui Empresas desde dd/mm" com "Pedir um teste" (instrução ao Proprietário: "Enviar teste" no ERP) e o `admin` é notificado.

### Ver e desconectar
21. **Dado** a tela BemAqui Empresas (só `admin`), **quando** abro, **então** cada situação tem chip, motivo e ação: "Conectado" ("Conectado em dd/mm por Fulano à {empresa} ({host})", último aviso, o que pode fazer, "Desconectar"); "Suspensa", "Desconectada no BemAqui Empresas", "Precisa conectar de novo" (com "Gerar código de pareamento" e o caminho do ERP); "Avisos não verificados" (CA 20); sem conexão, "Gere um código e passe ao Proprietário do BemAqui Empresas". O aviso de autorizar de novo aparece 30 dias antes de `reconsent_due_at`; `supervisor` e `agent` veem só "Conectado ao BemAqui Empresas" ou "Peça ao administrador para conectar".
22. **Dado** "Desconectar", **quando** o `admin` confirma ("O atendimento deixa de ver pedidos e cobranças do BemAqui Empresas agora. Pedidos já criados continuam lá."), **então** chama `POST /oauth/revoke` com o `bqr_`, apaga tokens e segredos, grava quem e quando; ERP fora do ar: apaga igual e deixa "revogação pendente" em fila.
23. **Dado** a lista de avisos recebidos, **quando** abro, **então** cada aviso tem **rótulo humano** ("Pagamento confirmado do pedido PV-2026-0012"), situação (Recebido, Processado, Falhou, Ignorado) e "Tentar de novo" nos que falharam; assinaturas inválidas aparecem só como contagem.

## Regras de negócio e técnicas
- **Uma conta do Conecta ↔ uma empresa no ERP** na v1.
- **Idempotência:** toda escrita no ERP leva `Idempotency-Key` gerada e gravada no Conecta antes da chamada; repetir só com o mesmo corpo; o escopo no ERP é aplicativo + empresa (INT-04 CA 6). Helper `ErpApiClient.SendIdempotentAsync`.
- **Limites do ERP:** `app-api` por conexão (padrão 600 leituras e 120 escritas por minuto) e buscas por telefone ou documento (30/min por conexão, 2.000/dia por empresa). O Conecta reparte a cota diária: **reserva 20% para reconferência e 10% para a IA**; o resto para buscas do atendente (ATD-02).
- **Entidades novas** implementam `ITenantEntity`; teste de varredura confere que toda entidade nova com `TenantId` tem filtro global.
- **Mesma VPS:** os dois lados se chamam pelo host público (*hairpin*), nunca rede interna.
- **Backup restaurado** traz `bqr_` antigo e a conexão cai: runbook "conectar de novo".

## O que muda em cada repositório
**Conecta (`bemaqui-atende-api`):** `ErpConnection` (`erp_host`, `erp_connection_id`, `erp_tenant_slug`, `status`, `status_reason`, `scopes`, `account_label`, `connected_by_user_id`, `connected_at`, `reconsent_due_at`, tokens e segredos cifrados, `access_token_expires_at`, `refresh_started_at`, `previous_valid_until`, `last_event_at`, `revoke_pending`, `xmin`), `ErpPairingCode`, `ErpConnectAttempt`, `ErpInboundEvent`; migration aditiva; `IntegrationSecretProtector` e job de re-cifragem; `ErpApiClient`, `ErpOAuthClient`, `ErpEventSignatureVerifier`, `ProcessErpEventJob`, varredores (tentativas vencidas, "Avisos não verificados", expurgo de corpos com 90 dias); `ErpIntegrationController` (pareamento, `connect/start`, `connect/callback`, status, desconectar; só `admin`) e `ErpEventsController`; limite por IP no recebedor.
**Conecta (`bemaqui-atende-web`):** rotas `/integracoes/bemaqui-empresas/conectar` e `/retorno`, seção em `SettingsView.vue`, avisos na lista de eventos, arquivo de textos.
**ERP (configuração):** em cada ambiente, `Integrations__Conecta__StartUrl`, `RedirectUris`, `WebhookUrl`, `WebhookAllowedHosts` (host da API do Conecta), `DefaultEvents` (catálogo v1 com os tipos da INT-04) e `Integrations__AppRateLimit`; `client_secret` do ambiente gerado, SHA-256 no ERP e valor no arquivo de secret do Conecta, por canal seguro.

## Telas e estados (Conecta)
- **Confirmar conexão:** carregando; código inválido ou vencido; host fora do filtro; não-`admin` com "Copiar link"; confirmação (CA 4); "Conectando…" com botão desabilitado; erro com causa, quem resolve e botão; sucesso com o próximo passo.
- **Configurações → BemAqui Empresas:** CA 1, 20, 21 e 22; carregando e erro.
- **Verbos:** "Gerar código de pareamento" → "Código gerado"; "Conectar" → "Conectado"; "Desconectar" → "Desconectado"; "Tentar de novo".

## Plano de testes
- **Unidade:** PKCE; código de pareamento (formato, validade, conta); host (com `evil.com`, `bemaquiapp.com.br.evil.com`, `user@host`, porta, IP, IDN); `iss`; `resource` (esquema, host, `..`, `//`, `%`, `\`); assinatura (vetores, duas `v1`, anterior dentro e fora das 24 h, janela, um byte alterado); AES-GCM com AAD trocado falha; erros do CA 13.
- **Integração (Conecta, PostgreSQL real, ERP falso):** pareamento + fluxo completo; `state` de outro usuário e reusado; substituição; renovação concorrente e queda entre resposta e commit; segredo na renovação; recebedor (válido, duplicado, assinatura errada, conexão desconhecida com latência parecida, `tenant` diferente, 64 KB); `app.disconnected`; "Avisos não verificados"; desconectar com ERP fora; canário; isolamento por tenant e IDOR do `ErpApiClient`; varredura de `ITenantEntity`.
- **Manual (piloto):** ver ATD-99.

## Tarefas
- [ ] Conecta: entidades, migration, protetor AES-GCM com rotação e runbook.
- [ ] Conecta: pareamento, início, confirmação, retorno e troca.
- [ ] Conecta: `ErpApiClient` (renovação, erros, idempotência).
- [ ] Conecta: recebedor, deduplicação, job, varredores.
- [ ] Conecta: tela, lista de avisos, arquivo de textos; manual do administrador.
- [ ] ERP: variáveis de ambiente e `client_secret` no SaaS e na Konebras.
- [ ] Fluxo do repositório do Conecta: branch, revisão do mantenedor, merge na `main` e só então deploy.

## Dev Notes
- **Conecta:** `bemaqui-atende-api/src/BemAquiAtende.API/Program.cs`, `Middlewares/MetaWebhookSignatureMiddleware.cs` (corpo cru; **não** copiar a falha aberta), `Controllers/ExternalWebhookController.cs`, `Domain/Entities/ExternalEvent.cs`, `Infrastructure/Services/EncryptionService.cs` (CBC, só referência), `Infrastructure/DependencyInjection.cs`, `Infrastructure/Jobs/HangfireJobScheduler.cs`, `tests/BemAquiAtende.Tests/MultiTenantQueryFilterTests.cs`.
- **ERP:** `ConnectedAppOptions.cs` (`OAuthRules`), `OutboundWebhookCatalog.cs`, `openapi-integration-v1.json`; a rota pública `GET /api/v1/public/tenant-context` (ADR 0009).

## Fora do escopo
- Painel, pedidos, IA, avisos e WhatsApp (ATD-02 a ATD-06b); botão de início no Conecta; várias empresas numa conta; trocar o `EncryptionService` CBC do resto do Conecta (risco registrado para o mantenedor).

## Pontos em aberto
Decididos (PO, 27/11/2026): pareamento com o nome e o host em destaque, sufixo como filtro adicional (decisão 2); AES-GCM novo; "BemAqui Empresas" no Conecta; uma empresa por conta; `BemAqui-Connection` (INT-04); termos do Conecta revisados com a INT-00. Nada pendente.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 27/11/2026 | 0.1 | Story escrita a partir das ADRs 0052/0053, das INT-01 a INT-03 e da leitura do Conecta. | Claude (planejamento) |
| 27/11/2026 | 0.2 | Revisões de 27/11/2026: arquitetura, segurança, UX; decisões do PO. Pareamento e host em destaque; recebedor pelo `BemAqui-Connection`, `tenant` no 1º `ping`, latência uniforme, limite por IP, 64 KB; `resource` validado; renovação com trava, releitura e intenção; erros `idempotency_in_progress`, `concurrency_conflict`, `bank_slip_method_missing`; AES-GCM com AAD e rotação; "Avisos não verificados"; glossário e textos; depende da INT-04. | Claude (planejamento) |
