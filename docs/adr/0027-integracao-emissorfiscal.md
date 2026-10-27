# ADR 0027: Integração com o EmissorFiscal

- Status: Aceito (21/10/2026). O PO (John) aprovou as decisões D2, D3, D5 e D12 da revisão pré-Fases 4 a 8 (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`), os pontos 6 a 12 e 35 a 39 da tabela 3.2 e a decisão D1 nova (tudo antes do go-live; SEFAZ em produção só na GOL-01b).
- Contexto: o CLAUDE.md manda analisar o EmissorFiscal (`D:\GIT\EmissorFiscal`) antes do módulo Fiscal e integrar pelo adaptador `IFiscalEmitter`, sem copiar código sem ADR. A análise está na FIS-01 (commit `e294444` do EmissorFiscal): é um serviço HTTP próprio, com banco, RLS, cofre de certificados, outbox e webhooks, que transporta e assina mas não calcula imposto, e que **ainda não autorizou nenhuma nota real**. A revisão marcou a FIS-01 como NO-GO: a story supunha que o Emissor reconcilia sozinho a nota pendente, o que não acontece (nota em `Processing`, `ContingencyPending` ou duplicidade 204 fica parada se o webhook não chegar), e a API, o banco e o painel do Emissor estão na rede `stack_stack` em HTTP, junto com o Dolibarr.
- Complementa as ADRs 0006 (fronteiras, jobs), 0007 (segredos, adendo de 21/10/2026), 0015 (rede da VPS), 0020 (testes nunca escrevem no `konebras`), 0022 (`IPdfRenderer`), 0031 (recepção de webhooks), 0034 (promoção a produção) e 0042 (configurações fiscais).

## Decisão

### 1. Forma de integração
- **Serviço HTTP separado**, opção (b) do `02-arquitetura` §6.1. Nenhum código do EmissorFiscal nem da Unimake é copiado ou referenciado; um teste de arquitetura reprova referência a assembly de lá.
- O módulo Fiscal fala com o Emissor **só** pelo `IFiscalEmitter` (`Fiscal.Application`), com tipos do BemAqui (inglês, `decimal`, `DateOnly`/`DateTimeOffset`); o adaptador `EmissorFiscalHttpEmitter` (`Fiscal.Infrastructure`) traduz os DTOs e as situações (FIS-01 CA 2 e 3).
- **Cálculo de tributos é do BemAqui** (FIS-02b), por desenho do Emissor ("quem decide valores e códigos fiscais é o sistema cliente").
- **Numeração da NF-e é do Emissor** (emitente + série + ambiente, transacional). O BemAqui grava o número e a chave devolvidos; `fiscal_settings.next_number` deixa de existir.

### 2. Tenants, chaves e escopos
- **Um tenant do Emissor por tenant do BemAqui**, com o emitente igual ao CNPJ do tenant e uma `ClientApplication` (sistema conectado) por tenant.
- **Chave sem o escopo `admin`**, só com `nfe:emitir`, `consultar`, `nfe:eventos` (lacuna, §5) e, na FIS-06, `nfse:emitir`. O teste de conexão recusa chave com `admin` ("Use uma chave sem o escopo admin.") e confere o CNPJ do emitente com o do tenant.
- A chave e o segredo do webhook ficam cifrados no BemAqui (`SecretKind.FiscalApiKey`, propósito `fiscal-api-key`; `SecretKind.WebhookSecret`, propósito `webhook-secret`; adendo de 21/10/2026 à ADR 0007), só de escrita.
- A URL base só aceita o nome do serviço na rede dedicada (lista fixa da configuração do ambiente), nunca IP, `localhost` ou endereço de metadados (contra SSRF). **Substituído pelo adendo de 27/10/2026 (API pública):** a URL é fixa pela configuração, em HTTPS.

### 3. Certificado A1 (D12)
- **O A1 fica só no cofre do EmissorFiscal** (AES-GCM, chave-mestra em Docker secret). O BemAqui guarda **só** a validade, o titular, o CNPJ e o thumbprint, para mostrar o vencimento e alertar.
- O envio do A1 é feito pela tela **Configurações → Fiscal → Certificado digital** do BemAqui (ADR 0042, FIS-02a), que **repassa o arquivo direto ao Emissor** no mesmo request:
  - o `.pfx` e a senha chegam em `[FromForm]` (nunca na query nem em JSON logado), ficam só em memória durante a chamada e são descartados; nada do arquivo vai para disco, `IFileStorage`, log, auditoria, outbox ou Hangfire (teste de canário);
  - o Emissor confere que o CNPJ do certificado é o do emitente e **não aceita falha nessa conferência** (lacuna, §5); o BemAqui confere de novo com o CNPJ do tenant antes de repassar;
  - exige `settings.edit` (reservada ao Proprietário, ADR 0008) e **2FA de novo** (ADR 0008, adendo de 21/10/2026, D6).
- O propósito `fiscal-certificate` previsto na ADR 0007 **não é usado**: o BemAqui nunca cifra nem guarda o A1.
- A R-FIS-2 ("certificado A1 criptografado no cadastro fiscal do tenant") passa a ser atendida pelo cofre do Emissor; o `01-PRD` registra a leitura.
- Alerta de vencimento em 30, 15 e 7 dias ao Proprietário, pelo status lido do Emissor (endpoint sem `admin`, lacuna, §5).

### 4. Rede, retorno e guarda
- **Rede Docker dedicada `bqe-fiscal`** (`internal: true`), só com a API do BemAqui e a API do Emissor, conferida pelo `check-compose.mjs`. O banco e o painel do Emissor saem da `stack_stack`. Alternativa registrada: TLS entre os dois serviços, se a rede dedicada não for possível. **Substituído pelo adendo de 27/10/2026:** sem rede privada; API pública em HTTPS e aviso pelo `stack-nginx`.
- **Retorno assíncrono:** o webhook do Emissor (provedor `emissor-fiscal` da ADR 0031, HMAC-SHA256 com timestamp, lacuna §5) é só gatilho; a transição da nota sempre vem da **reconsulta** `GET /api/v1/nfe/{chave}`. Reserva: o recorrente global `fiscal-pending-check` (a cada 10 min) reconsulta toda nota local em `Processing` ou `ContingencyPending` há mais de 5 min.
- **Idempotência:** `Idempotency-Key` = `nfe-{invoiceId}-{tentativa}`; a tentativa só muda depois de uma rejeição (o número rejeitado não volta). Falha de rede ou 5xx depois do POST nunca gera chave nova: a nota fica `Processing` e a próxima tentativa reenvia a mesma chave. O hash do payload enviado é gravado ao entrar em `Processing`, e um reenvio com payload diferente na mesma chave é recusado.
- **Guarda legal dos dois lados:** o XML autorizado e os eventos são baixados e guardados no BemAqui como arquivo de sistema (dono `OutgoingInvoice`, SHA-256 conferido, 5 anos, sem expurgo, ADR 0018).
- **Nenhum dado do destinatário** vai para o outbox, a auditoria ou o log do BemAqui; a nota local guarda ids e totais.

### 5. Lacunas do EmissorFiscal (D3), com dono e prazo
Trabalho no repositório do Emissor, planejado já, com o PO como dono. "Antes da" indica a story do BemAqui que depende do item.

| # | Lacuna | Antes da |
|---|---|---|
| 1 | **Job de reconciliação com a SEFAZ e consulta forçada** (nota em `Processing`, `ContingencyPending` ou duplicidade 204 sem webhook) | FIS-03a |
| 2 | **DANFE em PDF** no Emissor (reserva: o BemAqui gera o DANFE pelo `IPdfRenderer` a partir do XML; o DANFE em HTML nunca é servido, por XSS) | FIS-03a |
| 3 | Grupo `transp` e volumes; duplicatas (`cobr`/`dup`) conforme a contadora | FIS-03a |
| 4 | **IBS/CBS** na NF-e e na NFS-e, assumido obrigatório (P-03), CST e `cClassTrib` vindos do BemAqui | FIS-03a |
| 5 | `GET /me` (escopos e emitente da chave) e escopo **`nfe:eventos`** separado (hoje cancelar e inutilizar exigem só `nfe:emitir`) | FIS-03b |
| 6 | Envio e status do certificado **sem o escopo `admin`** (escopo próprio, só do emitente da chave), conferência do CNPJ sem aceitar falha, senha do PFX em `[FromForm]`, `EphemeralKeySet` | FIS-02a |
| 7 | Timestamp no HMAC do webhook (janela de 5 min contra reapresentação) | FIS-01 |
| 8 | Rede dedicada, banco sem superusuário, Swagger desligado fora de desenvolvimento | FIS-01 |
| 9 | Cópia offline da chave-mestra do cofre (como o key ring, ADR 0007) e teste de restauração | GOL-01a |
| 10 | Provider da NFS-e de Caçapava (CONAM/e-Transparência), cancelamento, PDF e webhook da NFS-e (D5) | FIS-06 |
| 11 | Coerência do `tipoOperacao` (guia × modelo), conferida pela suíte de contrato | FIS-01 |

- Sem distribuição DF-e nem manifestação do destinatário no MVP (FIS-05 usa XML e PDF com texto).

### 6. Série e ambientes
- **Série exclusiva do BemAqui**, diferente da série do emissor atual da Konebras, obrigatória para emitir em produção (FIS-02a; valor pré-configurado "A confirmar com a contadora", ADR 0042).
- **Homologação SEFAZ no tenant `demo`**, com emitente de teste no ambiente de homologação da SEFAZ (notas sem valor fiscal), para os 3 cenários da contadora (venda a contribuinte em SP, a não contribuinte em SP, peça ou outro estado). A suíte de contrato `emissor-homolog` roda ali.
- **Nada fiscal no tenant `konebras` antes da GOL-01b.** O ambiente de produção do tenant só é aceito com a marca `catalog.tenants.fiscal_production_allowed`, ligada na GOL-01b (ADR 0034), e com os campos fiscais críticos conferidos (ADR 0042).
- O PO implanta o Emissor já, em homologação, em paralelo ao fim da Fase 3 (D2).

## Consequências
- **FIS-01** entrega o `IFiscalEmitter`, o adaptador, o provedor de webhook, o `fiscal-pending-check`, a rede `bqe-fiscal`, a suíte de contrato com fake no CI e a suíte `emissor-homolog` no `demo`.
- **FIS-02a** entrega a tela do certificado (§3), a série exclusiva e o ambiente por tenant; **FIS-03a/03b** dependem das lacunas 1 a 5; **FIS-06** da lacuna 10.
- **GOL-01a** confere as lacunas 1 a 9 fechadas no Emissor de produção e a cópia offline da chave-mestra.
- `docs/02-arquitetura.md` §6.1 e `docs/03-modelo-dados.md` (`fiscal.integration_settings`, `fiscal_settings` sem `next_number`, metadados do certificado) atualizados.

## Alternativas consideradas
- **Biblioteca .NET no processo do BemAqui** (opção a): rejeitada; exigiria copiar ou referenciar código de outro produto e duplicaria cofre, numeração e outbox.
- **A1 cifrado no BemAqui e repassado com chave `admin`** (`fiscal-certificate`): rejeitada; dois cofres e uma chave de privilégio total guardada no BemAqui.
- **Homologação SEFAZ no tenant `konebras`**, excluindo as notas na promoção: rejeitada; mistura nota sem valor fiscal com dado real (ADR 0020).

## Adendo de 27/10/2026 (implementação na FIS-01)
- **Marca do ambiente:** a §6 fala em `catalog.tenants.fiscal_production_allowed`; ficou `catalog.tenants.fiscal_environment_allowed` (`None`/`Homologation`/`Production`, padrão `None`), como a FIS-01 CA 9 define: o `demo` sai da migration com `Homologation` e a GOL-01b liga `Production` no `konebras`.
- **Assinatura do aviso:** o Emissor fechou a lacuna 7 no formato do Stripe: `X-BemAqui-Signature: t={timestamp},v1={hex}[,v1={hex}]` sobre `{timestamp}.{corpo cru}`, com `X-BemAqui-Timestamp`, janela de 5 min e dois `v1` nas 24 h da rotação. O BemAqui confere com o `TimestampedSignatureValidator` (BuildingBlocks), além do `HmacSha256Validator` da FND-16.
- **Segredo do aviso:** fica na credencial do provedor `emissor-fiscal` da FND-16 (`shared.webhook_tokens`, `webhook-secret`, anterior por 24 h), e não em colunas próprias da `integration_settings`: o receptor da FND-16 já confere a origem por ela. Quem gera o segredo é o Emissor (aparece uma vez no cadastro do webhook); o BemAqui só recebe o valor colado.
- **Aviso pela rede interna:** o Emissor chama `http://bqe-<ambiente>-api:8080/webhooks/emissor-fiscal/{tenantKey}` pela `bqe-fiscal`. Esse caminho, e só no host interno declarado em `Webhooks:InternalHosts`, dispensa o proxy confiável (`TrustedProxyMiddleware`); a origem continua provada pela assinatura e só o provedor com `AllowInternalHost` é aceito ali. **Substituído pelo adendo seguinte (API pública):** o host interno e a exceção do proxy confiável saíram.
- **Tabela da nota:** `fiscal.outgoing_invoices` nasce enxuta na FIS-01 (ids, totais, situação, tentativa, `payload_hash`, chave, protocolo, rejeição e o XML guardado), para o envio, o webhook e a conferência de pendentes serem testados de ponta a ponta; a FIS-03a acrescenta o resto sem mudar estas colunas.

## Adendo de 27/10/2026 (decisão do PO): API pública e aviso pelo nginx, sem rede privada
- **Contexto:** o EmissorFiscal é um **SaaS multi-cliente** (ADR 007 de lá): um endereço para todos os clientes, que se separam pela chave de API. O BemAqui é só mais um cliente. Uma rede Docker privada com um cliente específico não cabe nesse produto, e o Emissor só entrega webhook em HTTPS pública (`http` só para rede interna, e isso deixa de valer para o BemAqui).
- **Decisão:**
  - **Endereço:** `https://api.fiscal.bemaquiapp.com.br`, o mesmo para homologação e produção. O ambiente vem do cadastro do emitente no Emissor, não da URL nem da chave. O endereço é **fixo pela configuração** (`Fiscal:Emissor:BaseUrl`, conferido contra `Fiscal:Emissor:AllowedHosts`, só HTTPS na porta padrão, nunca IP, `localhost` ou metadados); a tela mostra só para leitura e a API recusa outro valor. Escolhido por ser o mais seguro: nem quem tem `settings.edit` consegue mandar a chave para outro host. Integração salva com outro endereço (o antigo `http://emissor-fiscal:8080`) fica parada até um "Testar conexão" aprovado no endereço da configuração.
  - **Chave:** a do BemAqui, sem `admin`, com `nfe:emitir`, `nfe:eventos` e `consultar` (teste: `GET /api/v1/me`). Emissão sempre com `Idempotency-Key` estável (`nfe-{invoiceId}-{tentativa}`, §4).
  - **Limite:** o Emissor aceita 10 requisições por segundo por IP (mais o limite da chave) e responde 429 acima disso. O BemAqui sai com no máximo `Fiscal:Emissor:MaxRequestsPerSecond` (padrão 4) por processo, somando tenants, tela, envio, webhook e conferência de pendentes, espaçadas e com fila (`EmissorFiscalRateGate`); homologação e produção saem do mesmo IP da VPS, por isso 4 + 4 ficam abaixo de 10. Um 429 pausa todas as chamadas do processo pelo `Retry-After` (teto de 1 min); leitura tenta de novo uma vez se o `Retry-After` for curto, escrita nunca (a dúvida fica com quem chamou, com a mesma chave).
  - **Aviso (webhook):** chega pela URL pública do **host da plataforma** (`https://<host da plataforma>/webhooks/emissor-fiscal/{tenantKey}`), pelo `stack-nginx`, como os outros provedores (ADR 0031). Sem host interno da API (`Webhooks:InternalHosts` e `AllowInternalHost` saíram) e sem exceção no `TrustedProxyMiddleware`. Conferência: `X-BemAqui-Signature: t=<unix>,v1=<hex>`, HMAC-SHA256 em hex minúsculo de `{t}.{corpo cru}` com o segredo do cadastro, `t` a no máximo 5 min, comparação em tempo constante contra cada `v1` (dois nas 24 h da troca de segredo), `X-BemAqui-Delivery-Id` descarta a repetição na inbox da FND-16 (responde 200 sem gravar de novo), `X-BemAqui-Event` com `nfe.autorizada`, `nfe.rejeitada`, `nfe.cancelada` e `nfe.denegada`; 2xx só depois de gravar o evento e a mensagem do outbox (o Emissor tenta até 8 vezes).
  - **Infra:** sai a rede `bqe-fiscal` do compose, do `deploy.sh` e da regra do `check-compose.mjs`, que passa a reprovar a volta dela, `Fiscal__Emissor__BaseUrl` sem HTTPS e `Webhooks__InternalHosts`. A API sai para a internet pela `stack_stack`, como já faz para o SMTP. O compose passa `Webhooks__PublicBaseUrl` com o host da plataforma.
- **Consequências:** a lacuna L-i (rede dedicada) deixa de ser pré-condição do BemAqui; continua valendo para o Emissor não publicar Swagger nem `/api/v1/admin/*` por fora. A suíte `emissor-homolog` roda de qualquer lugar com a URL pública. O nginx da VPS precisa da location `/webhooks/` no host da plataforma, fora da allowlist de IP (runbook `docs/operacao/emissor-fiscal.md` §2).
- **Alternativas consideradas:** manter a `bqe-fiscal` só para o BemAqui (rejeitada: o Emissor é multi-cliente e não entrega em HTTP); URL editável por quem tem `settings.edit` (rejeitada: abre caminho para mandar a chave a outro host, sem ganho, já que o endereço é um só).
