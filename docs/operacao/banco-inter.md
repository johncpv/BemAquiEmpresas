# Banco Inter: integração por conta (BNK-01a)

Runbook da integração com a API do Banco Inter (ADR 0032). A tela fica em **Configurações → Integrações → Banco Inter**
(`/app/configuracoes/integracoes/inter`); os dados são do Finance (`finance.bank_integrations`, uma por conta).

## O que a BNK-01a entregou
- Credenciais por conta: client id e client secret (`inter-credentials`) e certificado do mTLS, chave privada e senha do `.pfx`
  (`inter-certificate`), cifrados por tenant **e por integração** (propósito `bemaqui → tenant:{id} → inter-… → {integrationId} → v1`,
  ADR 0007 adendo). Só de escrita: nunca voltam na API, no log nem na auditoria.
- Validação do certificado antes de gravar: abre (`.pfx` com `EphemeralKeySet`), a chave é a do certificado, não venceu, RSA ≥ 2048
  bits, CNPJ do assunto (quando houver) igual ao do tenant; `.crt`/`.pem`/`.key`/`.pfx`, até 20 KB cada.
- `InterApiClient` (Finance.Infrastructure): OAuth2 `client_credentials` com mTLS, **um `SocketsHttpHandler` por conta** em cache pela
  chave `{tenant}:{integração}:{thumbprint SHA-256}`, certificado só em memória, token em cache até 60 s antes de vencer (renovação sob
  lock, um 401 renova uma vez e repete), **URL base fixa no código** por ambiente e allowlist dos hosts do Inter.
- "Testar conexão": token de prova + leitura do saldo, sem efeito. Credencial com escopo de pagamento ou envio de Pix é recusada
  (422 `bank_credential_can_pay`).
- Produção bloqueada para efeitos até `catalog.tenants.bank_production_allowed` (GOL-01b, ADR 0034): a credencial de produção pode ser
  salva e testada (D−1), mas boleto, webhook e extrato respondem 409 `bank_production_not_allowed`.
- 2FA de novo (`RequireRecentTwoFactor`, prova de até 15 min) para salvar, trocar e desligar, e também para pagar e estornar a OP.
- Recorrente global `inter-certificate-expiry` (08:00 de São Paulo): aviso ao Proprietário em 30, 15, 7 e 1 dia; vencido, alerta
  `bank_integration` e a emissão responde 409 `bank_integration_expired`.

## URLs e caminhos usados (fixos no código, `InterEnvironments`)
| Ambiente | URL base | Token | Leitura sem efeito |
|---|---|---|---|
| Sandbox | `https://cdpj-sandbox.partners.uatinter.co/` | `POST oauth/v2/token` | `GET banking/v2/saldo` |
| Produção | `https://cdpj.partners.bancointer.com.br/` | `POST oauth/v2/token` | `GET banking/v2/saldo` |

Cabeçalho da conta: `x-conta-corrente` (só os números). Mudar uma URL é mudança de código (PR), nunca configuração.

## Escopos (configuração `Inter`, valores padrão no código)
- **Necessários (`Inter:RequiredScopes`):** `boleto-cobranca.read`, `boleto-cobranca.write`, `extrato.read`, `webhook.read`, `webhook.write`.
- **Famílias permitidas (`Inter:AllowedScopePrefixes`):** `boleto-cobranca.`, `extrato.`, `webhook.`. Qualquer outro escopo concedido recusa a credencial.
- **Prova de pagamento (`Inter:ProbeScopes`):** `pagamento-pix.write`, `pagamento-boleto.write`, `pagamento-lote.write`, `pagamento-darf.write`,
  `pix.write`, `cob.write`, `cobv.write`. O token de prova pede os necessários **mais** estes; se o Inter conceder algum, a credencial é recusada.
  Se o Inter responder 400 ao pedido com escopo não autorizado, o cliente pede de novo só os necessários.

## Pré-condição externa: conferir no sandbox quando a credencial chegar (P-08)
A validação real é feita **no tenant `demo`**, nunca no `konebras` antes da GOL-01b. Registre o resultado de cada item na story (Change Log).

1. **Credencial de sandbox:** a Konebras gera no Internet Banking (Soluções para sua empresa → Nova integração) uma integração **só com
   cobrança, extrato e webhook**, e baixa client id, client secret, `.crt` e `.key` (ou `.pfx`).
2. **Formato do certificado:** confira se o Inter entrega `.crt` + `.key` em PEM (sem senha) ou `.pfx`; se a chave vier cifrada, use o `.pfx`.
   Confira se o assunto do certificado traz o CNPJ (a validação compara com o do tenant) e o tamanho da chave (≥ 2048).
3. **Token:** em `demo`, "Salvar integração bancária" em Sandbox e "Testar conexão". Confira:
   - os nomes dos escopos devolvidos em `scope` (se diferirem dos acima, ajuste `Inter:RequiredScopes` e as famílias por configuração);
   - o comportamento com escopo **não autorizado** no pedido (o Inter devolve 400, ou devolve só os concedidos?). Os dois casos estão
     tratados; registre qual acontece;
   - `expires_in` (o cache renova 60 s antes).
4. **Leitura sem efeito:** confirme que `GET banking/v2/saldo` responde 200 com o escopo `extrato.read` e o `x-conta-corrente`.
5. **Recusa de credencial que paga:** gere (no sandbox) uma credencial com `pagamento-pix.write` e confirme que salvar ou testar responde
   "Esta credencial pode fazer pagamentos…" e nada é gravado. Apague essa credencial no Inter depois.
6. **Correlação:** veja no log da API (`Inter token`/`Inter saldo`) qual cabeçalho de correlação o Inter devolve; se for outro, inclua em
   `InterApiClient.CorrelationHeaders`.
7. **Log sem segredo:** procure o client secret e o início do token no log da API do `demo`: não podem aparecer.
8. **Produção (D−1 da GOL-01b):** com a credencial de produção, salve em Produção no `konebras` e só **teste** a conexão. Emitir boleto
   segue bloqueado até o super admin ligar `bank_production_allowed`.

## Saída de rede (container da API)
- O cliente só alcança os dois hosts acima (handler de allowlist no código). Na VPS, a regra de saída do container da API para HTTPS
  deve liberar só `cdpj-sandbox.partners.uatinter.co` e `cdpj.partners.bancointer.com.br` (além dos destinos que já existem: SMTP/SES,
  EmissorFiscal). **Não há nada a mudar no nginx** (a BNK-01a não recebe chamada do Inter; o webhook é a BNK-02).
- A revogação do certificado do servidor não é consultada (`X509RevocationMode.NoCheck`): com a saída restrita, CRL/OCSP não estariam
  acessíveis. A cadeia e o nome do servidor continuam validados pelo padrão do .NET (nenhum callback permissivo; teste de arquitetura).

## Relógio
mTLS e token dependem da hora. Confira o NTP da VPS (`timedatectl`); deriva acima de 60 s faz o token parecer vencido ou ainda não
válido. (O health `ready` ainda não confere a deriva: pendência registrada na story.)

## Troca do certificado (renovação anual) e do client secret
1. O Proprietário recebe o aviso 30, 15, 7 e 1 dia antes, com o link `…/inter/{conta}?acao=trocar`.
2. Gere o certificado novo (ou o client secret novo) no Internet Banking do Inter.
3. Na tela, **Trocar credenciais** (pede o 2FA de novo): o servidor testa a combinação nova antes de trocar; se falhar, as atuais continuam.
4. Depois da troca, o handler e o token antigos saem do cache (o handler é descartado depois de 1 min, para as chamadas em curso).
5. Revogue o certificado antigo no Inter só depois de ver "Conexão com o Inter funcionando" com o novo.

## Desligar e suspeita de vazamento
- **Desligar integração** apaga (sobrescreve com nulo) client id, client secret, certificado, chave e senha; a conta volta a `provider = None`.
- Suspeita de vazamento da credencial: desligue na tela, revogue a integração no Internet Banking do Inter e gere uma nova.
