# ADR 0048: Fronteiras Sales × Pos, aparelho de caixa registrado e esquema `Device`

- Status: Aceito (18/11/2026). Decisões do PO: PP-04 (16/11/2026, fronteiras), PP-11, PP-12, PP-09 e PP-35 (18/11/2026). Escrita na story [PDV-01](../pdv/stories/PDV-01-lojas-caixas-aparelho.md).
- Contexto: o caixa do PDV precisa de loja, caixa e aparelho conhecidos (o aparelho roubado tem de poder ser bloqueado), de um número de venda que não colide quando o aparelho é trocado e de um jeito de o aparelho falar com o servidor sem operador logado (a sincronização da OFF-01 e da OFF-02). O aparelho da fábrica (ADR 0026) já existe, com PIN, segredo rotativo e teto de 10 por tenant. O [rascunho A](../pdv/adr/rascunho-A-offline.md) item 9 e a [arquitetura do PDV, seção 4.1](../pdv/02-arquitetura.md#41-fronteira-sales--pos-quem-é-dono-de-quê) propunham o desenho.
- Complementa as ADRs 0006 (módulos), 0008 (adendo de 18/11/2026), 0026 (adendo de 18/11/2026), 0030 (o esquema `Kiosk`, modelo deste) e 0046 (o `pos` só no perfil Comércio).

## Decisão

### 0. Fronteiras (PP-04)
- **Sales** é dono da venda e de tudo dela (`sales.*`, VND-01). **Pos** é dono da loja, do caixa e, nas próximas stories, da sessão de caixa, dos eventos do aparelho, das divergências, do modelo de leitura do catálogo, das aprovações de supervisor e das impressoras (`pos.*`, rotas `/api/v1/pos/…`). **Catalog** é dono de produto e preço (ADR 0047). **Identity** é dono do aparelho.
- O código de módulo é do catálogo de permissões, não do projeto: as telas de retaguarda de loja e caixa (servidas pelo projeto `Pos`) são autorizadas com `sales.*`; o `pos` é a tela dedicada do caixa e o registro de aparelhos (`pos.manage_devices`).
- Nenhum módulo grava tabela de outro. Quando um precisa mudar o dado do outro na mesma transação, chama um contrato que só rastreia a mudança e deixa quem chama gravar, como o `IStockMovements`: o `Pos` entrega a venda pelo `ISaleIngestion`; o Identity troca o aparelho do caixa pelo `IPosRegisters`.

### 1. Aparelho com tipo
- **A tabela continua `identity.shopfloor_devices`** (renomear seria migration com cuidado e não traz nada agora). Colunas novas, todas aditivas: `kind` (`Factory`, `Register`; KDS, garçom e estação de impressão entram nas Fases 12 e 15), `store_id`, `register_id`, `register_epoch`, `public_key`, `last_sync_at`. `kind` nasce com padrão `Factory`: os aparelhos que já existem ficam como Fábrica e continuam funcionando sem mudança.
- O `/fab` só enxerga aparelho `Factory` (cookie `__Host-bq_dev`); o caixa usa outro cookie, `__Host-bq_pos`. Um nunca vira o outro.
- **Teto por tipo:** os 10 da fábrica (`security.max_shopfloor_devices`) contam só os aparelhos `Factory`. O caixa tem um aparelho ativo por vez (índice único parcial em `pos.registers.active_device_id`). O teto por plano é a PP-17, ainda pendente.
- Validade, sem uso por 30 dias e o job diário valem igual para os dois tipos.

### 2. Registro do caixa e esquema `Device`
- **Registrar:** login completo (nunca a sessão por PIN), `pos.manage_devices` e **TOTP confirmado há no máximo 15 minutos** (a mesma prova do `RequireRecentTwoFactor` da ADR 0008, adendo de 21/10/2026). O registro é feito no próprio aparelho, em `/pdv`: o navegador gera um par **ECDSA P-256 com a chave privada não exportável** (WebCrypto) e envia só a pública (SubjectPublicKeyInfo). O servidor guarda a pública, devolve o segredo opaco só no cookie `__Host-bq_pos` (HttpOnly, Secure, SameSite=Strict, 180 dias), audita e avisa o Proprietário: "Novo caixa registrado na loja X por Fulano.". O login usado para registrar não deixa sessão persistente no aparelho (o refresh é revogado, como na fábrica).
- **Esquema `Device`:** cada requisição do aparelho leva o cookie e a assinatura do texto `{MÉTODO}\n{caminho com a query}\n{instante em ms}\n{SHA-256 do corpo em base64}` (cabeçalhos `X-Device-Timestamp` e `X-Device-Signature`, formato r||s do WebCrypto). O servidor confere o segredo, o instante (±5 min) e a assinatura com a chave registrada. O principal tem só o tenant, o aparelho, a loja, o caixa e a época, tirados do banco; nunca `sub` nem permissão. **A loja e o caixa vêm sempre do principal, nunca do corpo** (CA 10).
- **Lista fechada:** só os endpoints com `RequireDevice()` (policy `device`), todos em `/api/v1/pos/sync/*` e no módulo `pos`, aceitam o esquema. O `EndpointAuthorizationTests` reprova o esquema em qualquer outra rota e rota de sincronização sem ele.
- **Segredo:** gira a cada sincronização bem-sucedida (não a cada PIN), por UPDATE condicional ao segredo lido: com duas sincronizações simultâneas, uma gira e a outra recebe 409 `device_sync_conflict`. **O anterior vale só até ser aposentado** (revisão de segurança do PR #227, M1): aposenta quando o aparelho usa o segredo atual (provou que recebeu o novo) e, numa rotação pedida com o anterior (a resposta perdida já foi coberta uma vez), o anterior novo nasce aposentado. Apresentar um anterior aposentado, com a assinatura certa, é reuso de um clone: o aparelho é revogado (`secret_reused`) e o Proprietário avisado, como na fábrica. Dois clones revezando o segredo são pegos na segunda troca. Coluna `previous_secret_retired_at`.
- **Ordem da conferência:** o aparelho é achado pelo segredo antes de ler o corpo; o corpo só é lido para aparelho que existe, com limite de 4 MB (acima, 413, inclusive no envio sem `Content-Length`). Instante fora da tolerância responde 401 `device_clock_skew`, com a hora do servidor, e o `/pdv` pede para acertar o relógio. A chave pública precisa ser da curva P-256 (OID conferido).
- **Revogar o caixa** só com `pos.manage_devices`: a rota de Configurações (`settings.edit` ou `production.approve`) revoga só aparelho da fábrica.
- Nada do segredo, da assinatura nem do corpo vai para o log.

### 3. Época do caixa e número da venda
- `pos.registers.device_epoch` sobe a cada aparelho registrado no caixa; o aparelho guarda a época em que entrou (`register_epoch`). A sequência das vendas recomeça em 1 em cada época.
- **Número da venda:** `{loja}-{caixa}.{época}-{seq}`, por exemplo `LOJA-02.3-000127` (`PosSaleNumber`). A chave dos eventos (OFF-01) é (`register_id`, `device_epoch`, `seq`). As vendas que o aparelho antigo ainda não enviou não colidem com as do novo, nem na chave nem no número.
- **Trocar o aparelho** de um caixa pede confirmação ("O caixa 02 já tem um aparelho. Trocar?"), revoga o anterior com o motivo `replaced` e abre a época nova, tudo numa transação.

### 4. Aparelho revogado ou substituído
- Perde a descida (aperto de mão, catálogo) e a entrada por PIN: 401 `device_revoked`, e a tela pede novo registro.
- **Continua subindo** o que já tinha gravado: o esquema reconhece o aparelho revogado (com o segredo e a assinatura dele) e marca o principal como revogado; só um endpoint marcado `RequireDevice(allowRevoked: true)` aceita. Nesta story nenhum endpoint usa a marca; a subida de eventos (OFF-02) entra com revisão e põe esses eventos em quarentena. **OFF-02:** só `POST /api/v1/pos/sync/events` usa a marca (ADR 0050). Nenhuma venda se perde pela revogação.
- Revogar solta o caixa (`active_device_id` volta a nulo); a época não volta.

### 5. Exceção nomeada ao TOTP no balcão (PP-11)
- No balcão, **cancelar venda e NFC-e e dar desconto acima do limite** aceitam o **PIN do supervisor** no lugar do TOTP, com **motivo obrigatório e aviso ao Proprietário, tudo auditado** (`pos.supervisor_approvals`). Vale só para essas ações; o TOTP continua valendo na retaguarda e em todas as outras ações sensíveis da ADR 0008. O supervisor com `*.approve` pode ter PIN de balcão por esta exceção, como a `production.approve_quality` (ADR 0026 §2). A implementação é da PDV-04 e da OFF-03.

### 6. Plataformas (PP-09)
- O caixa roda em **Windows com Chrome ou Edge e em Android com Chrome**. iOS fica fora do caixa.

### 7. Validação sem lojista piloto (PP-35)
- A Fase 11 abre sem lojista piloto; a validação é na **Loja Demo** (tenant de teste com o perfil Comércio). Com um piloto, ajusta-se com ele antes de ir à loja real.

## Consequências
- PDV-01 entrega o módulo `pos` (área Loja, só no perfil Comércio), as lojas e os caixas, o aparelho de caixa, o esquema `Device` e o aperto de mão (`POST /api/v1/pos/sync/handshake`). O usuário de operação (CA 12, [ADR 0049](0049-usuario-de-operacao.md)) e o PIN no caixa (CA 11) ficam para a segunda parte.
- OFF-01 usa a chave, o cookie e o aperto de mão; OFF-02 abre a subida (`allowRevoked`) com a quarentena; OFF-03 traz o PIN e o supervisor sem rede.
- **Auditoria:** a rotação do segredo e a aposentadoria do anterior são UPDATE condicionais, fora do diff da auditoria (a sincronização é a trilha); a revogação por reuso é auditada.
- **Para as próximas stories** (revisão do PR #227): a subida da OFF-02 precisa de idempotência por (`register_id`, `device_epoch`, `seq`) (B3); a canonicalização do caminho assinado deve usar o alvo cru da requisição (`RawTarget`) (B4); o registro sobrescreve a chave antiga antes do POST, e um registro recusado deixa o aparelho sem chave (B7); o `/pdv` deve ganhar CSP e origem próprias (B9).
- Um caixa inativo ou de loja inativa não recebe aparelho; desativar a loja ou o caixa exige bloquear o aparelho antes.

## Adendo 18/11/2026 (PDV-01, parte 2): PIN no caixa
- **Entrada pelo aparelho, sem rota anônima nova:** a lista de nomes (`GET /api/v1/pos/sync/operators`) e a entrada por PIN (`POST /api/v1/pos/sync/pin`) usam o esquema `Device`: cookie `__Host-bq_pos` e assinatura da chave do aparelho, com o corpo (o PIN) dentro da assinatura. Limite nomeado `pos-pin-login` (10 por minuto por tenant, IP e aparelho; `RateLimit:Auth:PosPinPerMinute`), além do bloqueio por PIN e por aparelho da ADR 0026 §2. O segredo do caixa não gira no PIN, só na sincronização (§2).
- **Sessão por PIN do caixa:** `amr = ["pin", "hwk"]` (RFC 8176: prova de posse da chave do aparelho), família de refresh `PosPin` presa ao aparelho, 12 h absolutas, um operador por caixa (entrar encerra a sessão anterior do aparelho). O `PinSessionScopeMiddleware` confere, a cada request, o aparelho de caixa do cookie `__Host-bq_pos` (atual, ou o anterior dentro da tolerância da rotação e ainda não aposentado, como no `/fab`), a sessão viva, o usuário ativo, `pos.create`, nenhuma ação reservada, a loja (usuário de operação) e o funcionário ligado ativo.
- **Lista fechada própria:** `[PosPinSessionAllowed]` (`.AllowPosPinSession()`), separada da `[PinSessionAllowed]` da fábrica; as duas sessões nunca alcançam a lista da outra. Hoje: `GET /api/v1/pos/session` (quem está no caixa) e `POST /api/v1/pos/session/end` (sair). A venda (PDV-02) entra na lista com revisão. Testes: `PinSessionAllowListTests` (lista revisada) e `EndpointAuthorizationTests` (só `/api/v1/pos/*`, módulo `pos`, com permissão, fora do esquema `Device` e nunca com a marca da fábrica).
- **PIN de quem tem e-mail:** "PIN do caixa" em Meu perfil → Segurança, com login completo, `pos.create` e nenhuma ação reservada. O PIN de supervisor de balcão (§5, PP-11) e o código de aprovação sem rede (PP-39) continuam na PDV-04 e na OFF-03: até lá, quem tem ação reservada não usa PIN no caixa.
- **Revisão de segurança do PR #228:** a entrada por PIN no caixa roda numa transação com `pg_advisory_xact_lock` do aparelho e do usuário antes de ler os contadores (tentativas paralelas contam uma a uma); o cookie do caixa aceita o segredo anterior só dentro da tolerância; "Novo PIN" e a troca de lojas do usuário de operação passam pela delegação da ADR 0008 sobre os grupos dele. **Antes das próximas stories:** assinatura do aparelho também na renovação da sessão `PosPin`, antes das rotas de venda (PDV-02); a trava "grupo com usuário de operação não ganha ação reservada" também no seeder ou num teste de arquitetura, antes da PDV-04; nonce (`jti`) na assinatura do aparelho; a mesma corrida dos contadores no PIN do `/fab` (`UserPin`), que já existia; bloqueio de tela por inatividade no `/pdv`, pedindo o PIN de novo.
