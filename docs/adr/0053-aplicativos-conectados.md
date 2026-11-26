# ADR 0053 — Aplicativos conectados: servidor de autorização no ERP

- **Status:** Aceita (26/11/2026)
- **Stories:** [INT-01](../stories/INT-01-aplicativos-conectados.md), [INT-02](../stories/INT-02-webhooks-de-saida.md), [INT-03](../stories/INT-03-api-integracao-v1.md), [VND-02a](../stories/VND-02a-pedido-de-venda-nucleo.md)
- **Relaciona:** ADR 0052 (integrações), 0003 (ações), 0007 (segredos), 0008 (delegação), 0009 (rotas anônimas), 0011 (tokens opacos), 0031 (webhooks de entrada), 0046 (perfis e exceção manual), 0048 (esquema `Device`)

## Contexto

A ADR 0052 decidiu que o Conecta, os marketplaces e a futura API pública acessam o ERP como **aplicativo conectado**, com OAuth 2.0. O ERP hoje só conhece pessoa, PIN de aparelho, quiosque e aparelho do PDV. Faltava fixar o formato do token, o tipo de cliente, as rotas, os escopos, os códigos de erro e o limite de uso num lugar só, para as stories não divergirem.

## Decisão

1. **Fluxo:** OAuth 2.0 *authorization code* com **PKCE S256 obrigatório** (`plain` recusado), `iss` na resposta (RFC 9207) e `redirect_uri` comparada caractere a caractere. A conexão começa no ERP (intenção de conexão de 10 min, uso único, da mesma pessoa).
2. **Cliente:** o Conecta é aplicativo **primeira parte, cliente confidencial**, `client_secret_basic`, com **um segredo por ambiente** (igual em todos os tenants), guardado como arquivo de secret; o ERP guarda só o SHA-256. PKCE e URI exata são a segunda barreira.
3. **Tokens opacos, só o hash no banco:**
   - acesso `bqa_` (32 bytes, 15 min);
   - *refresh* `bqr_`, rotativo a cada uso na mesma família; reuso de um substituído fora da tolerância de 10 s **revoga a família e a conexão** e alerta os Proprietários; 90 dias sem uso expira;
   - código de autorização (60 s, uso único; reapresentado, revoga o que emitiu).
   O token nunca é JWT, então nunca vale como sessão de pessoa.
4. **Esquema `App`** aceito **só em `/api/v1/integration/*`**, com a marca `ConnectedAppEndpoint`; as rotas de aplicativo não aceitam sessão de pessoa. O principal não tem `sub`: tem `app`, `conn`, `grantor` e `tid`. A auditoria grava o autorizador com a origem "Integração {aplicativo}". Rotas da própria conexão (`/integration/connection`, leitura da inscrição de avisos) exigem só token válido e o módulo `conecta`.
5. **Escopos** (fonte única; `AllowedScopes` do Conecta): `catalog.view`, `inventory.view`, `parties.view`, `parties.create`, `parties.edit` (restrito: documento nunca muda; contato só preenche campo vazio), `sales.view`, `sales.create`, `sales.edit`, `finance.view`, `fiscal.view`, `projects.view`. `quotes.create` fica fora da v1 (ATD-03). `sales.approve` e as demais ações reservadas da ADR 0008 **nunca** são concedidas.
6. **Delegação:** concessão = pedidos ∩ `AllowedScopes` ∩ permissões efetivas de quem autorizou; a cada chamada, efetiva = concessão ∩ permissões atuais do autorizador (que já limitam aos módulos liberados). Ninguém concede o que não tem; perda de permissão vale na chamada seguinte.
7. **Códigos de erro únicos:** 401 `invalid_token` para toda falha de token (desconhecido, vencido, revogado, outro tenant, rota errada); 401 `app_grantor_inactive` (autorizador desativado, conexão suspensa); 403 `insufficient_scope` (ação fora da efetiva); 403 `module_not_released`; 429 `rate_limited`. Os do OAuth seguem a RFC 6749 §5.2.
8. **Limite de uso único:** política `app-api` por conexão (leituras e escritas por minuto no parâmetro de plataforma `Integrations:AppRateLimit`, ajustável por exceção), usada pela INT-01 e pela INT-03; buscas de cliente por documento ou telefone têm limite próprio, teto diário por tenant e alerta de anomalia (INT-03).
9. **Amarração da intenção:** no primeiro `inspect`, o ERP grava o hash de `state + code_challenge + redirect_uri` na intenção e exige o mesmo no `consent`. Telas `/oauth/*` com `Referrer-Policy: no-referrer` e `frame-ancestors 'none'`. Consentimento com **2FA de novo**.
10. **Vida da conexão:** no máximo uma ativa por aplicativo e tenant; **nova autorização a cada 12 meses** (aviso 30 dias antes; vencida, suspensa); autorizador desativado suspende; 2FA do autorizador zerado **não** suspende, mas notifica os Proprietários. Revogação dos dois lados (RFC 7009).
11. **Cache de revogação:** busca do token por hash com cache de 30 s invalidado na revogação. Com uma instância (hoje), o efeito é imediato; com várias, o atraso máximo é de **30 s**.
12. **Módulo `conecta`** (área Sistema, ações `view` e `edit`, esta não reservada), **fora de todos os perfis**, liberado por empresa por exceção manual no painel de plataforma (ADR 0046).
13. **Idempotência para aplicativo:** chave com escopo `tid + conexão + rota + hash do corpo`; a mesma chave ou o mesmo `externalId` com outro corpo responde 409 e nunca devolve outro recurso. Origem, aplicativo e autor vêm do token, nunca do corpo.

## Adendos a outras ADRs

- **ADR 0009 (rotas anônimas):** entram na lista `/api/v1/oauth/token` e `/api/v1/oauth/revoke`, anônimas de pessoa mas **autenticadas pelo cliente** (`client_secret_basic`), com rate limit próprio e corpo fora do log.
- **ADR 0007 (segredos):** novo `SecretKind.OutboundWebhookSecret` → `outbound-webhook-secret`, segredo de assinatura dos avisos de saída por inscrição, com o anterior guardado durante 24 h na troca.
- **BNK-02:** a baixa do boleto passa a publicar também `BillingSlipPaid` no outbox, na mesma transação; o efeito interno síncrono não muda.

## Consequências

- O `EndpointAuthorizationTests` ganha a classe "aplicativo conectado" e o fato "esquema `App` só em `/api/v1/integration/*`".
- O `PermissionHandler` ganha o ramo do principal de aplicativo (hoje lê só `sub`).
- O `IdempotencyFilter` passa a usar a conexão no escopo para principal de aplicativo (o autorizador sozinho não distingue conexões).
- A API pública (API-01) reaproveita tudo isto com cadastro de aplicativo de terceiros; aí o segredo passa a ser por aplicativo.
- Risco aceito: com várias instâncias, uma revogação pode levar até 30 s para valer em todas.
