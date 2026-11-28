# ADR 0052 — Integrações: Conecta, marketplaces e a base comum

- **Status:** Aceita (PO, 26/11/2026)
- **Plano:** [docs/integracoes/00-plano-integracoes.md](../integracoes/00-plano-integracoes.md)
- **Relaciona:** ADR 0003 (ações), 0007 (segredos), 0008 (delegação), 0031 (webhooks de entrada), 0041 e 0042 (WhatsApp), 0046 (perfis e exceção manual), PP-21 (hub e gateway), P-29 (BSP)

## Contexto

O ERP não aceita acesso de outro sistema: só sessão de pessoa, PIN de aparelho e token de TV. Também não avisa sistemas externos (o outbox é interno) e não tem pedido de venda em aberto (VND-01 registra a venda concluída). O Conecta (nome comercial do BemAqui Atende, do mesmo grupo) já tem o WhatsApp oficial da Meta, CRM e agentes de IA, mas não tem catálogo nem pedido. Os concorrentes (Bling, Tiny, Omie) integram marketplaces; o Nibo tem API pública e MCP.

## Decisão

1. **Base comum, construída uma vez,** que serve ao Conecta, aos marketplaces e à futura API pública:
   - **Pedido de venda (VND-02)** no módulo `Sales`, com estados, reserva de estoque, cobrança Pix/boleto e NF-e.
   - **Aplicativos conectados (INT-01):** OAuth 2.0 com *authorization code* e PKCE. Escopos são os `modulo.acao` do catálogo. O aplicativo recebe a interseção entre os escopos pedidos, as permissões de quem autorizou e os módulos liberados (ADR 0008). O ERP guarda só o hash dos tokens, o *refresh* gira a cada uso, a conexão é revogável e a auditoria registra "Integração X (autorizada por Fulano)". O consentimento pede 2FA.
   - **Webhooks de saída (INT-02):** inscrição por evento, HMAC-SHA256 sobre `{timestamp}.{corpo}`, nova tentativa pelo Hangfire, registro de entregas e segredo com rotação de 24 h.
2. **Conecta:** a conexão começa no ERP (Configurações → Integrações → Conecta), e o Conecta só confirma a conta. É um aplicativo primeira parte, pré-cadastrado. O ERP recomenda sempre o Conecta como WhatsApp oficial: o `IWhatsAppSender` do WPP-01 ganha o adaptador "via Conecta", e o P-29 fica resolvido sem BSP separado.
3. **Fábrica:** o projeto segue sendo o pedido do sob encomenda. Pelo Conecta, a fábrica abre **pedido de orçamento** (rascunho no Comercial). Peça de prateleira usa o pedido de venda comum, desligado no perfil `factory` e ligado por empresa.
4. **Pagamento:** Pix e boleto com Pix do Inter primeiro; cartão depois (PP-21).
5. **Marketplaces:** adaptador por canal (`IMarketplaceConnector`), entrada pelo `/webhooks/{provedor}/{tenantKey}` sempre consultando o canal de novo, token do lojista cifrado num `SecretKind` novo, e repasse e tarifas no financeiro (margem por canal). A ordem é Mercado Livre (direto), Shopee, Amazon, Magalu e TikTok Shop (este sob demanda). Os pedidos de aprovação de Shopee e Amazon começam junto com a F1. O hub fica só para a cauda.
6. **Liberação:** a integração com o Conecta e cada marketplace são **módulos novos do `ModuleCatalog`, fora dos perfis**, ligados por empresa no painel de plataforma por exceção manual (ADR 0046). A política de planos fica para depois.
7. **Prioridade:** antes das pendências do PDV que aguardam loja parceira (IMP-02, SPK-01, INFRA-02).

## Consequências

- **Rotas não anônimas, por exceção:** surgem rotas autenticadas por token de aplicativo, e não de pessoa. O `EndpointAuthorizationTests` precisa reconhecer esse tipo de autenticação. As três camadas de acesso continuam obrigatórias para o aplicativo.
- **Mudanças no repositório do Conecta** (credencial de integração cifrada por empresa, recebimento de eventos, painel, ferramentas da IA) seguem o fluxo combinado com o mantenedor do Conecta: branch, merge na main e depois deploy.
- **API pública e MCP (F4)** reaproveitam o INT-01 a INT-03, com cadastro de aplicativo de terceiros.
- **Risco externo:** prazos de aprovação de Shopee e Amazon, e regras de dado pessoal da Amazon (token restrito).

## Adendo de 28/11/2026: SAIR por categoria

Decisão do PO que **revisa o "SAIR global"** da decisão 4 de 27/11/2026. O SAIR respeita a regra da Meta e **desliga a categoria do que o cliente está recebendo**: (a) avisos do pedido, ou (b) marketing do Conecta (campanhas, reengajamento, retomada e automações que não são de evento do ERP). A categoria é a da última mensagem automática enviada àquele telefone naquele tenant nos últimos 7 dias; sem mensagem recente ou em caso ambíguo, desliga tudo (opção segura pela Meta e pela LGPD); o botão "Parar promoções" da Meta desliga só (b). **VOLTAR volta geral**, mas não cria o aceite dos avisos (a pergunta separada da ATD-05a continua exigida). O tratamento é **único**: no Conecta estende-se o `Contact.IsBlocked` e a lista de bloqueio existentes, com `ContactSuppression` por categoria e uma porta única de envio automático, em vez de criar um mecanismo paralelo. Detalhes na ATD-05a (CA 11 a 15); o mantenedor do Conecta (Thiago) revisa.
