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

## Extrato do banco (BNK-03a)
A aba **Contas e extrato → conta → Extrato → Extrato do banco** (`/app/financeiro/contas/{id}/extrato?aba=extrato-banco`) mostra as linhas do
banco (`finance.bank_statement_lines`), o saldo do banco por dia (`bank_statement_balances`) e as importações (`bank_statement_batches`).

- **API do Inter:** recorrente global `inter-statement-import` às **06:30** de São Paulo (um job por tenant com o `finance` liberado) e o botão
  "Buscar agora" (`finance.edit`, no máximo 1 vez a cada 5 min por conta). Busca desde a última linha da API menos 3 dias (nunca antes da
  data de corte da FIN-00; sem linha anterior, 30 dias), em janelas de 30 dias e páginas de 1000. Chamadas:
  `GET banking/v2/extrato/completo?dataInicio=&dataFim=&pagina=&tamanhoPagina=` e `GET banking/v2/saldo?dataSaldo=` (últimos 5 dias e hoje).
- **Sem duplicar:** índice único `(conta, origem, external_id)` entre as linhas não desfeitas e advisory lock por conta (job e "Buscar agora" ao
  mesmo tempo esperam um pelo outro).
- **Produção:** sem `bank_production_allowed`, "Buscar agora" responde 409 `bank_production_not_allowed` e o job pula a conta (fica no log).
- **OFX:** contas sem API, até 5 MB e 20 mil lançamentos, antivírus antes da leitura (indisponível recusa), parser próprio sem DTD, 10 s de limite.
  Em conta com API, só com a contingência liberada (`finance.approve`, até 30 dias); as linhas entram "não verificadas pelo banco".
- **Parâmetros** (seção `BankStatement`, valores padrão no código): `FetchCooldown` (00:05:00), `OverlapDays` (3), `InitialDays` (30),
  `WindowDays` (30), `BalanceDays` (5), `DifferenceAlertDays` (2), `InterPageSize` (1000), `OfxMaxBytes`, `OfxMaxTransactions`,
  `OfxParseTimeout`, `ContingencyMaxDays` (30).
- **Log:** só conta, origem, período, contagens e duração (`Extrato do banco importado…`); nunca descrição, contraparte ou valor de linha.

### Pré-condição externa: conferir no sandbox (tenant `demo`) quando a credencial chegar
Tudo foi provado contra o fake do Inter e contra arquivos OFX gerados nos testes. No sandbox real, confira e registre na story:

1. **Caminho e parâmetros do extrato:** `banking/v2/extrato/completo` responde 200 com `extrato.read`; nomes `dataInicio`, `dataFim`, `pagina`,
   `tamanhoPagina`; o maior `tamanhoPagina` aceito (se 1000 for recusado, ajuste `BankStatement:InterPageSize`) e o maior período por chamada
   (se 30 dias for recusado, ajuste `BankStatement:WindowDays`).
2. **Campos de cada transação:** `idTransacao` (estável entre chamadas? é a chave anti-duplicação), `dataTransacao` (ou só `dataInclusao`),
   `tipoOperacao` C/D, `valor` (texto ou número), `tipoTransacao`, `titulo`, `descricao` e, em `detalhes`, os nomes do pagador/recebedor e
   do CPF/CNPJ (`nomePagador`, `cpfCnpjPagador`, `nomeRecebedor`, `cpfCnpjRecebedor`). Campo com outro nome: ajuste o `InterStatementProvider`.
3. **Paginação:** `totalPaginas` e `ultimaPagina` presentes.
4. **Saldo por dia:** `banking/v2/saldo?dataSaldo=AAAA-MM-DD` devolve o saldo do fim daquele dia (e não o atual); campos `disponivel`,
   `bloqueadoCheque`, `bloqueadoJudicialmente`, `bloqueadoAdministrativo` (o sistema soma o disponível com os bloqueados; o `limite` fica fora).
5. **Sobreposição:** rode "Buscar agora" duas vezes (com 5 min entre elas): a segunda deve mostrar "0 novas, N já importadas".
6. **OFX do Inter na contingência:** exporte o OFX do mesmo período pelo Internet Banking e importe com a contingência liberada; confira se o
   `FITID` do OFX é igual ao `idTransacao` da API (se for, as linhas coincidentes são ignoradas; se não for, elas entram como não verificadas e a
   conciliação da BNK-03b as separa — registre o resultado).
7. **OFX de outros bancos:** importe um OFX real de cada banco usado pela Konebras e confira `BANKID`/`ACCTID` (com ou sem dígito e agência),
   o encoding (acentos) e o `FITID`.
8. **Log:** procure no log do `demo` uma descrição ou um nome de contraparte importado: não pode aparecer.

# Boleto com Pix (BNK-01b)

## O que a BNK-01b entregou
- **Configurações → Cobrança** (`/app/configuracoes/cobranca`, `finance.parameters`): multa 2,00%, juros de mora 1,00% a.m., desconto
  padrão 0,00%, "Enviar o boleto por e-mail ao emitir" ligado, limite diário de 200 e-mails (somando orçamentos, pedidos e boletos) e a
  regra do vencido (multa e juros do vencimento original). Multa, juros e a regra do vencido nascem **"A confirmar com a contadora"**
  (ADR 0042); "Marcar como conferido" grava quem e quando. Seed nos tenants existentes sem sobrescrever.
- **Emissão** (`POST /api/v1/finance/receivables/{id}/billing-slips`, `Idempotency-Key`): grava `finance.billing_slips` em `Requested`
  com o "seu número" da sequência da conta (`finance.billing_reference_sequences`, base 36, 6 caracteres no mínimo) e enfileira o
  `IssueBillingSlipJob` (fila `default`, só `tenantId` e o id do boleto). O job cria a cobrança, consulta até ter linha digitável e Pix
  (3 consultas por rodada, nova rodada a cada 30 s, falha depois de 20 rodadas), baixa o PDF (até 2 MB, dono de anexo `BillingSlip`) e
  passa a `Issued`. O título nunca muda (valor, situação, vencimento).
- **Falha** (`Failed`): motivo traduzido sem dado do pagador e notificação a quem pediu. "Tentar de novo" reusa o "seu número" e, se a
  tentativa anterior chegou ao banco, **procura pelo "seu número" antes de criar** outra cobrança.
- **Cancelamento** manual (motivo da lista) e automático (título cancelado, valor ou vencimento alterado, baixa manual): o
  `CancelBillingSlipJob` consulta e cancela no Inter; só depois o boleto fica `Cancelled` e o título fica sem boleto.
- **E-mail** `billing-slip-issued` pela fila com o PDF (só depois do `Clean`); acima do limite diário, o envio fica para o dia seguinte às
  08:00 (`SendBillingSlipEmailJob`).
- `provider_payload` guarda só ids, situação, datas e valores do retorno do Inter (nunca o pagador) e nunca sai pela API.

## Caminhos usados (API de Cobrança v3, relativos à URL base fixa)
| Operação | Método e caminho |
|---|---|
| Emitir | `POST cobranca/v3/cobrancas` → `codigoSolicitacao` |
| Consultar | `GET cobranca/v3/cobrancas/{codigoSolicitacao}` (`cobranca`, `boleto`, `pix`) |
| Procurar pelo "seu número" | `GET cobranca/v3/cobrancas?dataInicial=…&dataFinal=…&filtrarDataPor=EMISSAO&seuNumero=…` |
| PDF | `GET cobranca/v3/cobrancas/{codigoSolicitacao}/pdf` (`pdf` em base64) |
| Cancelar | `POST cobranca/v3/cobrancas/{codigoSolicitacao}/cancelar` (`motivoCancelamento`) |

Situações traduzidas: `A_RECEBER`/`ATRASADO`/`PROTESTO` → emitido; `RECEBIDO`/`MARCADO_RECEBIDO` → pago (baixa na BNK-02); `CANCELADO`;
`EXPIRADO`; `FALHA_EMISSAO` → falha; qualquer outra (ex.: `EM_PROCESSAMENTO`) → aguardando.

## Pré-condição externa: conferir no sandbox (tenant `demo`, P-08)
Tudo foi provado contra o fake (`tests/.../Finance/FakeInterBilling.cs`). Com a credencial de sandbox, no `demo`, confira e registre na
story (Change Log):
1. **Fluxo completo:** emitir um boleto de um título de teste, ver a linha digitável, o Pix copia-e-cola e o QR Code, baixar o PDF,
   enviar ao cliente (e-mail de teste) e cancelar. Confira no Internet Banking que a cobrança aparece e some ao cancelar.
2. **Limite do "seu número":** confirme o tamanho máximo aceito (o código usa até 15, `BillingReference.MaxLength`) e se letras
   maiúsculas são aceitas; se não forem, troque a base (mudança de código).
3. **Campos da emissão:** nomes de `multa` (`PERCENTUAL`, `taxa`), `mora` (`TAXAMENSAL`, `taxa`), `desconto` (`VALORFIXODATAINFORMADA`,
   `valor`, `quantidadeDias`), `mensagem` (`linha1`…`linha3`, até 78 caracteres) e os limites de tamanho do pagador.
4. **Multa e juros do vencido:** confirme se o Inter conta multa e juros a partir do vencimento do boleto (o título mantém o vencimento
   original e a BNK-02 divide o valor pago a partir de `billing_slips.fine_from`). Registre o comportamento.
5. **Dia não útil:** emita com vencimento num sábado e veja a regra do banco (pagamento no dia útil seguinte sem multa?).
6. **Prazo de baixa (`numDiasAgenda`):** confirme o padrão do Inter e o máximo; o código manda `Inter:BillingExpiryDays` (60). Depois do
   prazo, a reconsulta vê `EXPIRADO` (CA 18).
7. **Emissão assíncrona:** quanto tempo até a consulta trazer a linha digitável (ajuste `Inter:BillingRescheduleDelay` e
   `Inter:BillingMaxPolls` se precisar).
8. **Recusas:** envie um CEP inválido e um "seu número" repetido e confira o formato do erro (`violacoes[].propriedade`): o mapa das
   mensagens está em `InterBillingProvider.Refusal`.
9. **Motivos do cancelamento:** confirme os valores aceitos em `motivoCancelamento` (`APEDIDODOCLIENTE`, `PAGODIRETOAOCLIENTE`,
   `ACERTOS`, `SUBSTITUICAO`) ou se o campo é texto livre.
10. **Procura pelo "seu número":** confirme o filtro `seuNumero` na listagem e o intervalo de datas aceito.
11. **Log sem pagador:** procure o nome e o CPF/CNPJ do cliente de teste no log da API: não podem aparecer (o log traz só operação,
    situação HTTP e correlação).

# Aviso de pagamento e conferência diária (BNK-02)

## O que a BNK-02 entregou
- Provedor `inter` da FND-16 (`InterWebhook`): token de 256 bits na URL, só o SHA-256 guardado em `shared.webhook_tokens` com `scope_id` = id da integração (uma credencial por conta). URL: `https://{Webhooks:PublicBaseUrl}/webhooks/inter/{tenantKey}?t={token}` (modo `Query`, padrão) ou `/webhooks/inter/{tenantKey}/{token}` (modo `PathToken`).
- Configurações → Integrações → Banco Inter → conta → seção **Aviso de pagamento**: "Ativar aviso de pagamento" e "Trocar endereço do aviso" (`settings.edit` + 2FA de novo). O sistema cadastra a URL no Inter (`PUT cobranca/v3/cobrancas/webhook`, corpo `{"webhookUrl": "…"}`) e só grava se o Inter aceitar. A URL completa aparece uma única vez. Na troca, o endereço antigo vale por 24 h.
- O handler (`InterWebhookHandler`) lê do corpo só `codigoSolicitacao`, `seuNumero` e `nossoNumero` e **reconsulta a cobrança** (`GET cobranca/v3/cobrancas/{codigo}`) antes de qualquer efeito. Situação, valor pago (`valorTotalRecebido`) e data (`dataSituacao`) vêm da reconsulta.
- Baixa automática pelo `ReceivableService.SettleFromBankAsync` (FIN-01) com o actor de sistema "Banco Inter (aviso de pagamento) · evento {id}" na auditoria; pagamento com diferença vai para `PaidPendingReview` com a entrada na conta (`financial_transactions.pending_review = true`) e aparece em Financeiro → A receber → **Pagamentos a conferir**.
- Recorrente global **`inter-billing-reconcile`** às 07:00 e 13:00 de São Paulo: um job por tenant com `finance` liberado; cada integração ativa consulta `GET cobranca/v3/cobrancas?filtrarDataPor=SITUACAO` desde a última execução menos 2 dias (na primeira, 30 dias) e os boletos `Requested` há mais de 1 h. Relatório em `bank_integrations.last_reconcile_report`; 3 falhas seguidas → alerta `bank_integration` e aviso aos Proprietários e a quem tem `finance.edit`.
- Estorno da baixa feita pelo banco (`POST …/settlements/{id}/reverse-bank`) e do pagamento da OP: `finance.approve` + 2FA de novo (P-20, D6).

## Configuração
| Chave | Padrão | Para que |
|---|---|---|
| `Inter__WebhookMode` | `Query` | `PathToken` se o sandbox mostrar que o Inter **não** aceita query string na URL do webhook. Trocar o modo exige "Trocar endereço do aviso" em cada conta (o endereço cadastrado muda de formato). |
| `Inter__WebhookDeliveryIdHeader` | vazio | Nome do cabeçalho com o id da entrega, se o sandbox mostrar que o Inter manda um. Vazio: o `external_id` é a chave composta `cobranca:{codigoSolicitacao}:{situacao}:{dataHoraSituacao}` (lote: `lote:` + SHA-256), sempre junto com o SHA-256 do corpo. |

## nginx
- A location `/webhooks/` do host da plataforma fica **fora** da allowlist de IP do painel (mesma do EmissorFiscal, `docs/operacao/emissor-fiscal.md` §2). O `curl -X POST …/webhooks/inter/AAAAAAAAAAAAAAAAAAAAAA` de um IP fora da allowlist tem de dar **401**.
- O `log_format bqe_masked` já grava `$uri` (sem a query): no modo `Query` o token nunca vai para o `access_log`. Para o modo `PathToken`, o vhost usa `$bqe_uri`, que troca o que vem depois de `/webhooks/{provedor}/` por `***` (`deploy/nginx/bemaqui-empresas-homolog.conf`). Na location `/webhooks/` (feita pelo PO na VPS, como no `emissor-fiscal.md` §2), pôr `error_log /var/log/nginx/error.log crit;`, para a URL não aparecer em erro de upstream no modo `PathToken`.

## A conferir no sandbox (tenant `demo`, pré-condição externa da BNK-02)
1. O Inter aceita query string (`?t=`) na `webhookUrl`? Se recusar, `Inter__WebhookMode=PathToken`, reiniciar a API e "Trocar endereço do aviso".
2. O aviso traz um id de entrega próprio (cabeçalho)? Se trouxer, `Inter__WebhookDeliveryIdHeader` com o nome dele.
3. Formato do corpo: lista de objetos com `codigoSolicitacao`, `seuNumero`, `situacao`, `dataHoraSituacao`, `valorTotalRecebido`, `origemRecebimento` (o código aceita também um objeto só). Se vier outro nome para o código da solicitação, ajustar `InterWebhook.TryRead`.
4. Política de reenvio do Inter (quantas vezes e em quanto tempo): registrar aqui. A deduplicação pela chave composta e pelo hash cobre o reenvio idêntico.
5. Caminho do cadastro do webhook (`PUT cobranca/v3/cobrancas/webhook`) e o escopo que ele pede (`boleto-cobranca.write` ou `webhook.write`).
6. Na listagem por período, os nomes `filtrarDataPor=SITUACAO`, `paginacao.paginaAtual` e `paginacao.itensPorPagina`; e se `dataSituacao` é a data do crédito (base da baixa e da pontualidade).
7. Pagamento simulado no sandbox chegando no webhook da homologação: título baixado, transação na conta, notificação no sino.
