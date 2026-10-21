# FIS-01 — Análise do EmissorFiscal, ADR de integração e adaptador `IFiscalEmitter`

| Campo | Valor |
|---|---|
| ID | FIS-01 |
| Título | Análise do projeto `D:\GIT\EmissorFiscal` (feita nesta preparação e registrada abaixo), ADR de integração aprovada pelo PO, e o adaptador `IFiscalEmitter` do módulo Fiscal: cliente HTTP do serviço, credencial por tenant cifrada, tradução de situações e rejeições, retorno assíncrono pelo webhook do EmissorFiscal com reconsulta de reserva, e testes de contrato contra um fake e contra a instância de homologação |
| Tamanho | G (re-estimada de M: além da análise e da ADR, entrega o adaptador, o consumidor do webhook e os testes de contrato, base de toda a emissão) |
| Status | Draft |
| Requisitos | R-FIS-1 (integração com o EmissorFiscal via `IFiscalEmitter`; análise é a primeira tarefa) · R-FIS-8 (Reforma Tributária: verificar se o EmissorFiscal atende, P-03) · R-FIS-6 (verificar Distribuição DF-e) · R-FIS-7 (verificar NFS-e) · PRD §6 Segurança · R-SEC-5 |
| Dependências | FND-16 (recepção de webhooks), FND-11 (segredos), FND-07 (outbox, `TenantJob`), FND-12 (alertas); EmissorFiscal implantado na VPS (fora deste repositório, Pontos em aberto 1) |
| Bloqueia | FIS-02a, FIS-02b, FIS-03a, FIS-03b, FIS-06 (todas falam com o EmissorFiscal só por `IFiscalEmitter`) |
| Ordem de execução | 2 (Marco 5.1 "Base das integrações"; a ADR pode ser escrita em paralelo com o Marco 5.2, que não depende do Fiscal) |

## User story
Como **PO**, quero **saber exatamente o que o EmissorFiscal já faz e como o BemAqui fala com ele**, e como **desenvolvedor**, quero **um adaptador único e testado**, para **emitir nota pelo sistema sem copiar código fiscal, sem prender o módulo Fiscal a um fornecedor e sem descobrir uma lacuna no dia do go-live**.

## Contexto e referências
- CLAUDE.md, Integrações externas: "EmissorFiscal (projeto existente em `D:\GIT\EmissorFiscal`): analisar ANTES de implementar o módulo Fiscal (story FIS-01). Integrar via adaptador `IFiscalEmitter`; não copiar código sem ADR."
- `02-arquitetura` §6.1: forma de integração (a) biblioteca .NET ou (b) serviço separado no Compose chamado por HTTP; em ambos os casos o módulo Fiscal só conhece `IFiscalEmitter` (Emitir, Consultar, Cancelar, CartaCorrecao, BaixarPorChave).
- `05-backlog` FIS-01: "CA: ADR aprovado pelo PO antes de FIS-02". `06-pendencias-riscos`: P-01 (regime), P-02 (CNAE e NFS-e), P-03 (Reforma Tributária: "A FIS-01 verifica se o EmissorFiscal já atende").
- `04-fluxos-estados` §1 "NF-e de saída" (`Draft → Processing → Authorized | Rejected`; `Authorized → Cancelled`) e a revisão pré-Fase 0 §2.1 ("a NF-e ganha o estado `Denied` e a inutilização de número").
- ADR 0007 (propósitos `fiscal-certificate`, e os novos propostos abaixo), ADR 0015 (rede `stack_stack` compartilhada com ~10 projetos), ADR 0020 (testes nunca escrevem no tenant `konebras`).
- Fontes lidas no EmissorFiscal (somente leitura, 21/10/2026, commit `e294444`): `README.md`, `CLAUDE.md`, `PLANEJAMENTO.md`, `docs/integracao/README.md`, `docs/decisoes/001` a `006`, `src/EmissorFiscal.Api/Endpoints/*`, `src/EmissorFiscal.Application/Models/NfeRequest.cs` e `NfseRequest.cs`, `Nfe/PublicacaoDeEventos.cs`, `Nfe/ContingencyPolicy.cs`, `Domain/Entities/*`.

## Análise do EmissorFiscal (resultado desta preparação)

### O que é
- **Serviço HTTP independente** em .NET 10 (Minimal API + Swagger), com PostgreSQL próprio (schema `emissor`), outbox no banco e hosted services. Biblioteca fiscal **Unimake.DFe** (MIT, ADR 001 de lá); DANFE pela **Zeus.Danfe.Html**. Multi-tenant com **Row-Level Security** no Postgres (cada `ClientApplication`, `Issuer`, `Certificate` e `FiscalDocument` tem `TenantId`).
- O mesmo repositório tem um segundo produto, o **BemAqui Fiscal** (painel de gestão e portal, com clientes, licenças, onboarding, cobrança e webhooks), que roda sobre o Emissor Fiscal. O BemAqui Empresas seria **mais um sistema cliente**, como o módulo do Dolibarr em `clients/dolibarr/`.
- **Fronteira de produto:** "o serviço transporta e assina; quem decide valores e códigos fiscais (CFOP, CST, NCM, base de cálculo) é o sistema cliente". **Não calcula imposto.**
- **Exposição:** sem porta pública; vive na rede Docker `stack_stack` em `http://emissor-fiscal:8080` (HTTP sem TLS).
- **Estado:** fases F1 a F6 codificadas (448 testes), mas **nenhuma nota foi autorizada de verdade**: falta o certificado A1 (lacuna L1 de lá) e a F7 (homologação com o contador). Não há registro de implantação na VPS ("Deploy (quando existir)").

### O que cobre
| Capacidade | Como | Referência no EmissorFiscal |
|---|---|---|
| NF-e modelo 55, saída e entrada (`tipoOperacao`), finalidades normal, complementar, ajuste e devolução, notas referenciadas (`refNFe`) | `POST /api/v1/nfe`, payload JSON em pt-BR com tributos já calculados por item (ICMS com ST e FCP, PIS, COFINS, IPI opcional) | `NfeRequest.cs`, `NfeEndpoints.cs` |
| Pré-visualização sem transmitir nem consumir número | `POST /api/v1/nfe/previa` (devolve a DANFE) | integração §3 |
| Idempotência obrigatória | `Idempotency-Key` com índice único por cliente; repetição devolve o documento original | integração §4 |
| Numeração | transacional por emitente + série + ambiente (`SELECT … FOR UPDATE`); número rejeitado não é reaproveitado | PLANEJAMENTO §F3 |
| Retorno assíncrono | `201` autorizada, `202` processando ou `ContingencyPending`, `422` rejeitada ou denegada; situações `Received`, `Processing`, `Authorized`, `Rejected`, `Cancelled`, `Denied`, `ContingencyPending` | integração §3 e §5 |
| Webhook de saída | `nfe.autorizada`, `nfe.rejeitada`, `nfe.cancelada`, `nfe.denegada`; HMAC-SHA256 do corpo cru em `X-BemAqui-Signature`, `X-BemAqui-Delivery-Id`, retentativa pelo outbox, 15 s de timeout, desativa após 10 falhas; `http` só em rede interna | `PublicacaoDeEventos.cs`, integração §6 |
| Tradução de rejeições | catálogo com cerca de 35 códigos e `acaoSugerida` | `SefazRejectionCatalog.cs` |
| Contingência | SVC-AN/SVC-RS automática conforme a UF (SP → SVC-AN) nos códigos 108/109; consulta sozinho e resolve depois; rejeição 204 (duplicidade) volta para `Processing` | `ContingencyPolicy.cs` |
| Eventos | cancelamento (prazo de 24 h, justificativa 15–255), carta de correção (15–1000 caracteres, até 20 por nota), inutilização de faixa (recusa número autorizado) | `NfeEventService.cs` |
| Consulta e artefatos | `GET /nfe/{chave}`, `/xml` (autorizado), `/danfe` (HTML A4), `/eventos`; `GET /sefaz/status` | `NfeEndpoints.cs`, `OperationalEndpoints.cs` |
| Autenticação | `X-Api-Key` por sistema cliente (hash SHA-256 no banco), escopos `nfe:emitir`, `nfse:emitir`, `consultar`, `admin`; rate limit por cliente (429 + `Retry-After`); rotação de chave | integração §2, `ClientApplication.cs` |
| Certificado A1 | cofre AES-GCM com chave-mestra em Docker secret; upload por `POST /admin/emitentes/{id}/certificado` (escopo `admin`); validação (abre? CNPJ confere?), ativação atômica; alertas de vencimento em 60/30/15/7/1 dias | PLANEJAMENTO §5, `Certificate.cs` |
| Operação | vigilância de Notas Técnicas, sonda trimestral, métricas (`/admin/metricas`) | PLANEJAMENTO §F4 |
| Guarda legal | XML autorizado guardado no documento, "jamais apagar" (5 anos) | README |
| NFS-e | contrato estável `POST /api/v1/nfse` (ISS e retenções prontos, `nfeVinculada` para venda mista); provider do padrão Nacional; para Caçapava, decisão pelo padrão **CONAM/e-Transparência** da Unimake (ADR 005 de lá), com autenticação por usuário e código de contribuinte, não por certificado | `NfseRequest.cs`, ADR 005 |

### O que falta (lacunas para o BemAqui)
| # | Lacuna | Impacto no BemAqui | Onde trata |
|---|---|---|---|
| L-a | **Nenhuma autorização real**: o A1 não existe ainda (L1 de lá) e a F7 (homologação com o contador) não começou | FIS-03a não fecha sem os 3 cenários autorizados em homologação SEFAZ | Pré-condição externa da FIS-03a |
| L-b | **Reforma Tributária (IBS/CBS):** nenhum campo IBS/CBS no `NfeRequest` nem no `NfseRequest` | NF-e e NFS-e sem os grupos exigidos pela NT vigente (P-03: "seguir a Nota Técnica vigente"); em Caçapava o `Reg60_RTC` (IBS/CBS) já é **obrigatório** no layout 4.00 | Pontos em aberto 4; FIS-02b, FIS-06 |
| L-c | **DANFE só em HTML** ("HTML→PDF na F3" não feito) | R-FIS-3 pede DANFE em PDF por e-mail | Pontos em aberto 5 |
| L-d | **Sem transportadora, volumes e duplicatas** no payload (só `modalidadeFrete`; sem grupos `transp` completo e `cobr`/`dup`) | O romaneio (PRD-06b) tem transportadora, volumes e peso; as parcelas a receber não saem na nota | Pontos em aberto 6 |
| L-e | **Sem DIFAL** (`ICMSUFDest`) | Venda interestadual a não contribuinte (cenário listado no PLANEJAMENTO §10) não é emitida corretamente | FIS-02b, Pontos em aberto 4 |
| L-f | **Sem Distribuição DF-e nem manifestação do destinatário**, e sem consulta de NF-e de terceiros pela chave | FIS-05 não busca o XML pela chave (cai na leitura assistida); a FIS-04a não confere na SEFAZ a autenticidade do XML de entrada | FIS-04a, FIS-05 |
| L-g | **NFS-e incompleta:** Caçapava não configurada nem credenciada; sem cancelamento, sem PDF e sem webhook de NFS-e | FIS-06 (D) bloqueada | FIS-06 |
| L-h | **Status do certificado só pelo escopo `admin`** (`/admin/certificados/vencimentos`) | O BemAqui precisaria de uma chave `admin` só para mostrar o vencimento | Pontos em aberto 2 |
| L-i | **Tráfego HTTP sem TLS na `stack_stack`**, rede compartilhada com outros projetos | Payload com dados do destinatário e a API key passam em claro numa rede compartilhada | CA 14 |
| L-j | **Inconsistência de contrato:** o guia diz `tipoOperacao` `0` entrada e `1` saída; o modelo (`NfeRequest.cs`) comenta "1 = entrada, 2 = saída" | Erro silencioso de mapeamento | CA 12 (teste de contrato) |
| L-k | **Não implantado** na VPS; sem backup do XML descrito como rotina ativa | Guarda legal depende do BemAqui também guardar o XML | CA 9; Pontos em aberto 1 |
| — | NFC-e, CT-e, MDF-e | Não fazem falta ao MVP | — |

## Critérios de aceite
**ADR (gate da story)**
1. **Dado** a análise acima, **quando** a story começa, **então** o @architect redige a **ADR 0023 "Integração com o EmissorFiscal"** com: forma de integração (proposta: **(b) serviço HTTP**, porque o EmissorFiscal é um produto com banco, RLS, cofre, outbox e webhooks próprios; a opção (a) exigiria copiar ou referenciar código de outro produto, o que o CLAUDE.md proíbe sem ADR), o mapa tenant BemAqui → tenant, emitente e sistema cliente do EmissorFiscal, os escopos da chave, onde fica o certificado A1 (Pontos em aberto 2), a rede entre os dois serviços (CA 14), a numeração (do EmissorFiscal), a guarda do XML, a tabela de lacunas com dono e prazo e o plano de homologação SEFAZ sem escrever no tenant `konebras` (Pontos em aberto 3); **e o PO a aprova antes de qualquer código desta story e da FIS-02a** (CA do backlog).

**Contrato `IFiscalEmitter`**
2. **Dado** o módulo Fiscal, **quando** precisa do EmissorFiscal, **então** usa só `IFiscalEmitter` (`Fiscal.Application`), com as operações: `PreviewAsync`, `IssueNfeAsync(request, idempotencyKey)`, `GetNfeAsync(accessKey)`, `GetXmlAsync`, `GetDanfeAsync`, `CancelNfeAsync(accessKey, justification)`, `CorrectNfeAsync(accessKey, text)`, `InvalidateRangeAsync(series, from, to, justification)`, `GetEventsAsync`, `SefazStatusAsync`, `GetCertificateStatusAsync` (se a ADR permitir) e, reservadas para a FIS-06, `IssueNfseAsync`/`GetNfseAsync`; os tipos do contrato são do BemAqui (inglês, `decimal`, `DateOnly`/`DateTimeOffset`), nunca os DTOs do EmissorFiscal; um teste de arquitetura impede qualquer referência a assembly do EmissorFiscal ou da Unimake.
3. **Dado** o adaptador `EmissorFiscalHttpEmitter` (`Fiscal.Infrastructure`), **quando** emite, **então** mapeia o pedido para o JSON do EmissorFiscal (enums numéricos com os códigos da SEFAZ, valores `decimal` com 2 casas e quantidades com 4), envia `X-Api-Key` e o `Idempotency-Key` **estável** (proposta: `nfe-{outgoingInvoiceId}-{tentativa}`, Pontos em aberto 7), e traduz a resposta: `201` → `Authorized`, `202` com situação 0/1 → `Processing`, `202` com situação 6 → `ContingencyPending`, `422` com 3 → `Rejected` (código, motivo e ação sugerida), `422` com 5 → `Denied`; 400 vira erro de validação com o `detail` campo a campo, 401/403 vira `fiscal_integration_misconfigured` (alerta, CA 11), 409 vira conflito de estado, 429 respeita o `Retry-After`.
4. **[Segurança]** **Dado** falha de rede, timeout (proposta: 30 s) ou 5xx depois do POST de emissão, **quando** o adaptador não sabe se a nota saiu, **então** nunca gera chave nova: o documento local fica `Processing`, e a nova tentativa usa o **mesmo** `Idempotency-Key` (o EmissorFiscal devolve a nota existente ou a cria agora); a situação `ContingencyPending` nunca dispara reemissão (teste com o fake simulando queda depois de criar).

**Credencial por tenant**
5. **[Segurança]** **Dado** um membro do Proprietário (`settings.edit`, reservada pela ADR 0008), **quando** informa em "Configurações → Integrações → Emissor fiscal" a URL interna, a API key e o segredo do webhook do EmissorFiscal e clica "Salvar integração fiscal", **então** a chave e o segredo são gravados cifrados (`SecretKind.FiscalApiKey` e `SecretKind.WebhookSecret`, propostos), nunca voltam pela API (a tela mostra "configurada em dd/mm/aaaa"), a URL só aceita host da lista permitida da configuração do ambiente (proposta: `emissor-fiscal` e o nome do serviço na rede dedicada; nunca IP de metadados ou `localhost` de outro serviço), e a auditoria grava "alterado" sem o valor.
6. **[Segurança]** **Dado** a integração salva, **quando** clico "Testar conexão", **então** o sistema chama `GET /api/v1/escopos` e um `GET` de leitura com a chave, e mostra: escopos recebidos, emitente encontrado pelo CNPJ do tenant (o CNPJ do catálogo tem de ser igual ao do emitente, senão "O emitente do Emissor Fiscal não é o CNPJ desta empresa.") e ambiente (homologação ou produção); a chave com escopo `admin` é **recusada** por padrão, com a mensagem "Use uma chave sem o escopo admin." (menor privilégio; exceção só se a ADR decidir o contrário no Pontos em aberto 2).
7. **Dado** a rotação da chave no EmissorFiscal, **quando** o Proprietário cola a chave nova e testa, **então** a antiga deixa de ser usada imediatamente e a troca é notificada aos membros do Proprietário; a rotação do segredo do webhook segue a janela de sobreposição da FND-16 (CA 6).

**Retorno assíncrono**
8. **Dado** o provedor `emissor-fiscal` registrado na FND-16 com `HmacSha256Validator` (cabeçalho `X-BemAqui-Signature`, prefixo `sha256=`, id de entrega `X-BemAqui-Delivery-Id`), **quando** chega `nfe.autorizada`, `nfe.rejeitada`, `nfe.cancelada` ou `nfe.denegada`, **então** o handler do Fiscal localiza a nota local pelo `documentoId` do EmissorFiscal e pela chave de acesso, **confere** que o tenant do evento é o do emitente configurado, **reconsulta** `GET /api/v1/nfe/{chave}` antes de mudar a situação (o corpo só dispara a reconsulta) e publica o evento de domínio da transição (FIS-03a); evento de nota desconhecida fica `Ignored` com o motivo.
9. **Dado** uma nota autorizada, **quando** o handler a confirma, **então** baixa o XML autorizado (`GET /xml`) e o guarda como arquivo do sistema (`StoreGeneratedAsync`, dono `OutgoingInvoice`), com SHA-256 conferido e retenção de **5 anos** sem expurgo (ADR 0018), para o BemAqui ter a sua própria guarda legal (lacuna L-k).
10. **Dado** um webhook que não chega (destino fora do ar ou assinatura desativada depois de 10 falhas), **quando** o recorrente global `fiscal-pending-check` roda (proposta: a cada 10 min), **então** consulta no EmissorFiscal toda nota local em `Processing` ou `ContingencyPending` há mais de 5 min e aplica a mesma transição do CA 8, idempotente (teste com webhook perdido e com webhook e consulta chegando juntos).

**Observabilidade e falhas**
11. **Dado** 401, 403, conexão recusada, `GET /sefaz/status` fora do ar ou nota em `ContingencyPending` há mais de 2 h, **quando** acontece, **então** sai o alerta `fiscal_integration` (FND-12, uma vez por janela por tenant) e a notificação a quem tem `fiscal.edit`, com link para a tela de integração ou para a nota; o log traz só tenant, operação, situação HTTP, código de rejeição e duração, **nunca** a API key, o payload com dados do destinatário ou o XML (teste de canário).

**Contrato e segurança da rede**
12. **Dado** a suíte de contrato, **quando** roda no CI, **então** um **fake do EmissorFiscal** (WireMock ou `TestServer` com as respostas do `docs/integracao/README.md`) cobre 201, 202 (1 e 6), 422 (3 e 5), 400, 401, 403, 409, 429 e o webhook assinado; e uma suíte marcada `[Trait("contract","emissor-homolog")]`, fora do CI padrão, roda contra a instância de homologação do EmissorFiscal num tenant de teste (nunca `konebras`), conferindo o mesmo comportamento e o valor de `tipoOperacao` de entrada e saída (lacuna L-j).
13. **[Segurança]** **Dado** o payload de emissão (com nome, documento, endereço e e-mail do destinatário), **quando** é enviado, **então** nada dele vai para o outbox do BemAqui, para a auditoria ou para o log; a nota local guarda só ids e os totais, e o `RequestPayload` fica no EmissorFiscal.
14. **[Segurança]** **Dado** o compose de homologação e de produção, **quando** o `check-compose.mjs` roda, **então** a API do BemAqui alcança o EmissorFiscal por uma **rede Docker dedicada** (proposta: `bqe-fiscal`, `internal: true`, só os dois serviços), e não pela `stack_stack` (ADR 0015: a rede é compartilhada com outros projetos); alternativa registrada na ADR: TLS entre os dois.

**Permissões**
15. **Dado** o módulo `fiscal` não liberado, **quando** chamo a tela ou a API de integração, **então** 403 `module_not_released`; sem `settings.edit`, a tela fica em modo leitura (com `settings.view`) e o POST responde 403 `forbidden`; `RequireModule` e `perm:` em todo endpoint.

## Regras de negócio e técnicas
- **Forma de integração (proposta da ADR 0023):** serviço HTTP. O BemAqui é um "sistema conectado" do EmissorFiscal, com uma `ClientApplication` por tenant e escopos `nfe:emitir`, `consultar` (e `nfse:emitir` na FIS-06); o emitente é o CNPJ do tenant; nenhum código do EmissorFiscal é copiado.
- **Numeração da NF-e:** dona é o EmissorFiscal (emitente + série + ambiente). O BemAqui grava o número e a chave devolvidos; `fiscal_settings.next_number` do `03-modelo-dados` deixa de existir (corrigir o 03 nesta story).
- **Cálculo de tributos:** fora do EmissorFiscal por desenho; é do BemAqui (FIS-02b).
- **Retorno:** webhook (FND-16) como caminho principal, `fiscal-pending-check` como reserva; transição sempre pela reconsulta.
- **Guarda:** XML autorizado e eventos guardados nos dois lados; no BemAqui como anexo de sistema, nunca apagado.
- **Segredos:** `SecretKind.FiscalApiKey` (`fiscal-api-key`) e `SecretKind.WebhookSecret` (`webhook-secret`, FND-16); `fiscal-certificate` (ADR 0007) só se a ADR decidir que o A1 passa pelo BemAqui (Pontos em aberto 2). Adendo à ADR 0007 e ao teste golden.
- **Ambiente:** a integração guarda o ambiente informado pelo EmissorFiscal; o ambiente de produção só é aceito no tenant `konebras` depois da GOL-01 (Pontos em aberto 3).

## Permissões, eventos e tabelas
- **Permissões:** `settings.edit` (configurar e testar; reservada ao Proprietário, ADR 0008), `settings.view` (ver), `fiscal.view`/`fiscal.edit` (eventos recebidos e alertas).
- **Eventos:** nenhum de domínio nesta story (as transições da nota publicam na FIS-03a); provedor de webhook `emissor-fiscal`.
- **Tabelas:** `fiscal.integration_settings` (proposta: `base_url`, `api_key_cipher`, `webhook_secret_cipher`, `previous_webhook_secret_cipher`, `rotated_at`, `issuer_document`, `environment`, `scopes`, `last_test_at`, `last_test_result`; uma linha por tenant; auditada sem os segredos).
- **Jobs:** recorrente global `fiscal-pending-check`.
- **ADR:** 0023 (nova). Adendo à ADR 0007.

## Telas e estados
- **Configurações → Integrações → Emissor fiscal** (`/app/configuracoes/integracoes/emissor-fiscal`): seções Conexão (URL, chave write-only, segredo do webhook write-only com "Gerar novo" mostrado uma vez), Emitente e ambiente (lidos do teste), Situação (último teste, último aviso recebido, notas pendentes).
- **Verbos:** "Salvar integração fiscal" / "Integração fiscal salva"; "Testar conexão" / "Conexão testada".
- **Estados:** carregando; não configurada ("Conecte o Emissor Fiscal para emitir notas. Peça ao administrador do Emissor Fiscal uma chave sem o escopo admin." + "Configurar"); erro do teste dentro do formulário com o motivo e a ação; ambiente de homologação com faixa âmbar "Ambiente de homologação: as notas não têm valor fiscal."
- **400 px:** seções empilhadas; botões visíveis sem rolar.
- **Ajuda:** `Ajuda/configuracoes-integracoes-emissor-fiscal.md`; `<Dica>` em "Segredo do webhook" e "Escopo admin".

## Plano de testes
- **Unidade:** mapeamento do pedido (enums, casas decimais, `tipoOperacao`) e da resposta (CA 3); política de reenvio com a mesma chave (CA 4); conferência do CNPJ do emitente (CA 6).
- **Integração (Testcontainers + fake do EmissorFiscal):** salvar e testar com chave `admin` recusada (CA 5, 6); rotação (CA 7); webhook assinado → reconsulta → transição, nota desconhecida ignorada, tenant diferente recusado (CA 8); XML guardado com hash (CA 9); webhook perdido resolvido pelo job e corrida webhook × job (CA 10); alertas e canário de log (CA 11, 13); três camadas (CA 15).
- **Contrato:** suíte do fake no CI; suíte `emissor-homolog` manual contra a homologação do EmissorFiscal num tenant de teste (CA 12).
- **Infra:** `check-compose.mjs` com a rede dedicada (CA 14).
- **E2E (Playwright):** configurar a integração com o fake, testar a conexão e ver a faixa de homologação.

## Fora do escopo
- Configuração fiscal do tenant e regras de tributação: FIS-02a e FIS-02b. Emissão, eventos e DANFE: FIS-03a e FIS-03b. NFS-e: FIS-06.
- Implantar o EmissorFiscal na VPS, criar o tenant e o emitente lá, obter o A1: fora deste repositório (pré-condição externa).
- Corrigir as lacunas L-b a L-h no EmissorFiscal: trabalho no outro repositório, registrado na ADR 0023 com dono.

## Pontos em aberto
1. **Implantação do EmissorFiscal** (PO): quem implanta, quando, e em que ambiente (homologação e produção, com dois emitentes ou um com dois ambientes). Pré-condição dos testes de contrato `emissor-homolog`.
2. **Onde fica o certificado A1** (PO/arquiteto, decide na ADR 0023): (a) só no cofre do EmissorFiscal, carregado pelo painel do BemAqui Fiscal; o BemAqui guarda só a chave sem `admin` e precisaria de um endpoint de status do certificado sem `admin` (lacuna L-h); (b) o Proprietário carrega o A1 na tela do BemAqui (`SecretKind.FiscalCertificate`), que o repassa ao EmissorFiscal com uma chave `admin` separada. Recomendação: (a), menor privilégio e um cofre só; conflita com a leitura literal de R-FIS-2 ("cadastro fiscal do tenant: certificado A1 (criptografado)"), por isso precisa de decisão.
3. **Homologação SEFAZ sem o tenant `konebras`** (PO, ADR 0020): proposta: o tenant `demo` usa o emitente da WSP no ambiente de homologação da SEFAZ (notas sem valor fiscal) para os cenários da contadora; o tenant `konebras` só recebe a integração fiscal na GOL-01, já em produção. Alternativa: ligar a integração no `konebras` antes, em homologação SEFAZ, e excluir essas notas na promoção.
4. **Reforma Tributária** (contadora + PO, P-03): a NT vigente exige os grupos IBS/CBS na NF-e e na NFS-e? Em Caçapava o `Reg60_RTC` já é obrigatório (ADR 005 do EmissorFiscal). Se sim, a lacuna L-b bloqueia a FIS-03a; precisa de prazo no EmissorFiscal.
5. **DANFE em PDF** (PO/arquiteto): (a) o EmissorFiscal passa a gerar PDF; (b) o BemAqui renderiza o DANFE pelo `IPdfRenderer` (ADR 0022 já prevê "o DANFE próprio, se houver") a partir do XML; (c) enviar o HTML. Recomendação: (a) ou (b); (c) não atende R-FIS-3.
6. **Transportadora, volumes e duplicatas na NF-e** (contadora/PO): se a nota da Konebras precisa do grupo `transp` completo e das duplicatas (`cobr`), o EmissorFiscal precisa ser estendido (lacuna L-d).
7. **Formato do `Idempotency-Key`** (arquiteto): proposta `nfe-{id da nota local}-{tentativa}`, em que a tentativa só muda quando a nota anterior foi **rejeitada** (a rejeição consome o número e a correção é uma nova emissão).

## Definition of Done
- [ ] ADR 0023 aprovada pelo PO antes do código (CA 1); adendo à ADR 0007.
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers), contrato (fake no CI) e E2E passando; suíte `emissor-homolog` executada uma vez e registrada.
- [ ] Migration criada (não destrutiva), testada sobre cópia do banco do tenant `konebras`.
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Tela com estados.
- [ ] Ajuda escrita; teste de cobertura passando.
- [ ] Alertas acionáveis (link para a integração ou para a nota).
- [ ] Auditoria ligada (sem segredo).
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/02-arquitetura.md` §6.1 e `docs/03-modelo-dados.md` (`fiscal_settings` sem `next_number`, `integration_settings`) atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] ADR 0023 com a análise desta story e a tabela de lacunas.
- [ ] `IFiscalEmitter` e tipos do contrato (Application); `EmissorFiscalHttpEmitter` (Infrastructure, `HttpClient` tipado com timeout e Polly só para leitura).
- [ ] `IntegrationSettings`, `SecretKind.FiscalApiKey`, tela e ajuda.
- [ ] Handler do provedor `emissor-fiscal` (FND-16) e recorrente `fiscal-pending-check`.
- [ ] Fake do EmissorFiscal e suíte de contrato; rede `bqe-fiscal` no compose e no `check-compose.mjs`.

## Quality gates
- Agentes previstos: @architect (ADR 0023), @dev, cybersecurity (chave, rede, webhook, log), @qa.
- Gate: CA 1, 4, 5, 8, 12, 13 e 14 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026.

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto registrados sem decidir | OK |

- **Correções feitas na validação:** a numeração da NF-e passou a ser do EmissorFiscal (o `next_number` do 03 sai); a chave com escopo `admin` é recusada por padrão (CA 6); rede dedicada no lugar da `stack_stack` (CA 14); o webhook só dispara reconsulta (CA 8); re-estimada para G.
- **Pré-condições para `Ready`:** ADR 0023 aprovada (CA 1) com os pontos em aberto 2, 3 e 7 decididos; ponto 1 (instância de homologação do EmissorFiscal) para a suíte de contrato real.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Análise do EmissorFiscal e story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
