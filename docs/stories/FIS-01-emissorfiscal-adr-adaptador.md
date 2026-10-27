# FIS-01 — Integração com o EmissorFiscal (ADR 0027) e adaptador `IFiscalEmitter`

| Campo | Valor |
|---|---|
| ID | FIS-01 |
| Título | Implementação da ADR 0027 (integração com o `D:\GIT\EmissorFiscal` como serviço HTTP, um tenant do Emissor por tenant do BemAqui): contrato `IFiscalEmitter` com fake para os testes, adaptador HTTP, credencial por tenant só de escrita, `Idempotency-Key` `nfe-{invoiceId}-{tentativa}` com o hash do payload gravado em `Processing`, retorno pelo webhook assinado com timestamp, reconsulta e conferência de pendentes pela consulta forçada do Emissor, rede `bqe-fiscal`, e a lista de lacunas do Emissor como pré-condição externa com dono |
| Tamanho | G (re-estimada de M: entrega o adaptador, o fake, o consumidor do webhook, a conferência de pendentes e os testes de contrato, base de toda a emissão) |
| Status | Done (suíte `emissor-homolog` no `demo` pendente: pré-condição externa) |
| Requisitos | R-FIS-1 (integração com o EmissorFiscal via `IFiscalEmitter`) · R-FIS-8 (Reforma Tributária, P-03) · R-FIS-6 (Distribuição DF-e verificada) · R-FIS-7 (NFS-e verificada) · PRD §6 Segurança · R-SEC-5 |
| Dependências | FND-16 (recepção de webhooks), FND-11 (segredos), FND-07 (outbox, `TenantJob`), FND-12 (alertas), BNK-01a (`EnvironmentBanner`); ADR 0027 aceita |
| Bloqueia | FIS-02a, FIS-02b, FIS-03a, FIS-03b, FIS-04a, FIS-06 (todas falam com o EmissorFiscal só por `IFiscalEmitter`) |
| Ordem de execução | 7 (Marco 5.1 "Base das integrações"; pode correr em paralelo à trilha do banco, Marcos 5.2 e 5.3) |

## User story
Como **PO**, quero **que o BemAqui fale com o EmissorFiscal de um jeito só, decidido na ADR 0027**, e como **desenvolvedor**, quero **um adaptador único, testado contra um fake e contra a homologação**, para **emitir nota pelo sistema sem copiar código fiscal, sem prender o módulo Fiscal a um fornecedor e sem descobrir uma lacuna no dia do go-live**.

## Contexto e referências
- CLAUDE.md, Integrações externas: "EmissorFiscal (projeto existente em `D:\GIT\EmissorFiscal`): analisar ANTES de implementar o módulo Fiscal (story FIS-01). Integrar via adaptador `IFiscalEmitter`; não copiar código sem ADR."
- **ADR 0027** ([0027-integracao-emissorfiscal.md](../adr/0027-integracao-emissorfiscal.md)): serviço HTTP, um tenant do Emissor por tenant do BemAqui com chave sem `admin`, A1 só no cofre do Emissor (D12), rede `bqe-fiscal`, reconciliação com a SEFAZ como pré-condição, lacunas com dono e prazo (D3). **ADR 0007, adendo de 21/10/2026** (`SecretKind.FiscalApiKey`, `SecretKind.WebhookSecret`). **ADR 0031** ([0031-recepcao-de-webhooks.md](../adr/0031-recepcao-de-webhooks.md): atomicidade com o outbox, roteamento, retenção). **ADR 0042** ([0042-configuracoes-fiscais-e-integracoes.md](../adr/0042-configuracoes-fiscais-e-integracoes.md): segredos só de escrita nas telas de Configurações). **ADR 0034** ([0034-promocao-a-producao.md](../adr/0034-promocao-a-producao.md): produção só na GOL-01b). **ADR 0008, adendo de 21/10/2026** (D6: 2FA de novo nas ações críticas).
- **Revisão pré-Fases 4 a 8** ([2026-10-21-revisao-pre-fases4-8.md](../revisoes/2026-10-21-revisao-pre-fases4-8.md)): D2 (trilha externa), D3 (lacunas do Emissor), D12 (A1 no Emissor), D6 (2FA de novo); tabela 3.2, pontos 6 a 12; §4.3 transversal; §4.4 padrão 6.
- `02-arquitetura` §6.1: o módulo Fiscal só conhece `IFiscalEmitter` (Emitir, Consultar, Cancelar, CartaCorrecao, BaixarPorChave).
- `06-pendencias-riscos`: P-01 (regime), P-02 (CNAE e NFS-e), P-03 (Reforma Tributária).
- `04-fluxos-estados` §1 "NF-e de saída" (`Draft → Processing → Authorized | Rejected | Denied`; `Authorized → Cancelled`).
- ADR 0015 (rede `stack_stack` compartilhada), ADR 0020 (testes nunca escrevem no tenant `konebras`).
- Fontes lidas no EmissorFiscal (somente leitura, commit `e294444`): `README.md`, `CLAUDE.md`, `PLANEJAMENTO.md`, `docs/integracao/README.md`, `docs/decisoes/001` a `006`, `src/EmissorFiscal.Api/Endpoints/*` (inclusive `AdminEndpoints.cs`: a senha do PFX chega como `string senha`, fora do formulário), `NfeRequest.cs`, `NfseRequest.cs`, `PublicacaoDeEventos.cs`, `ContingencyPolicy.cs`, `Infrastructure/BackgroundJobs/*` (nenhum job reconcilia nota pendente com a SEFAZ), `Domain/Entities/*`.

## Análise do EmissorFiscal (resultado da preparação, corrigida na revisão)

### O que é
- **Serviço HTTP independente** em .NET 10 (Minimal API), com PostgreSQL próprio (schema `emissor`), outbox no banco e hosted services. Biblioteca fiscal **Unimake.DFe** (MIT); DANFE pela **Zeus.Danfe.Html**. Multi-tenant com **Row-Level Security** (cada `ClientApplication`, `Issuer`, `Certificate` e `FiscalDocument` tem `TenantId`).
- O mesmo repositório tem o **BemAqui Fiscal** (painel e portal). O BemAqui Empresas é **mais um sistema cliente**, como o módulo do Dolibarr em `clients/dolibarr/`.
- **Fronteira:** "o serviço transporta e assina; quem decide valores e códigos fiscais é o sistema cliente". **Não calcula imposto.**
- **Estado:** fases F1 a F6 codificadas, **nenhuma nota autorizada de verdade** (sem A1; F7 não começou) e **não implantado** na VPS.

### O que cobre
| Capacidade | Como | Referência no EmissorFiscal |
|---|---|---|
| NF-e modelo 55, saída e entrada, finalidades normal, complementar, ajuste e devolução, `refNFe` | `POST /api/v1/nfe`, payload JSON em pt-BR com tributos já calculados por item (ICMS com ST e FCP, PIS, COFINS, IPI opcional) | `NfeRequest.cs`, `NfeEndpoints.cs` |
| Pré-visualização sem transmitir nem consumir número | `POST /api/v1/nfe/previa` | integração §3 |
| Idempotência obrigatória | `Idempotency-Key` com índice único por cliente; repetição devolve o documento original | integração §4 |
| Numeração | transacional por emitente + série + ambiente; número rejeitado não é reaproveitado | PLANEJAMENTO §F3 |
| Retorno assíncrono | `201` autorizada, `202` processando ou `ContingencyPending`, `422` rejeitada ou denegada | integração §3 e §5 |
| Webhook de saída | `nfe.autorizada`, `nfe.rejeitada`, `nfe.cancelada`, `nfe.denegada`; HMAC-SHA256 do corpo cru em `X-BemAqui-Signature`, `X-BemAqui-Delivery-Id`, retentativa pelo outbox, desativa após 10 falhas | `PublicacaoDeEventos.cs` |
| Tradução de rejeições | catálogo com cerca de 35 códigos e `acaoSugerida` | `SefazRejectionCatalog.cs` |
| Contingência | SVC-AN automática para SP nos códigos 108/109; rejeição 204 (duplicidade) volta para `Processing` | `ContingencyPolicy.cs` |
| Eventos | cancelamento (24 h, justificativa 15–255), carta de correção (15–1000, até 20), inutilização de faixa | `NfeEventService.cs` |
| Consulta e artefatos | `GET /nfe/{chave}`, `/xml`, `/danfe` (HTML A4), `/eventos`; `GET /sefaz/status` | `NfeEndpoints.cs` |
| Autenticação | `X-Api-Key` por sistema cliente (hash SHA-256), escopos `nfe:emitir`, `nfse:emitir`, `consultar`, `admin`; rate limit (429 + `Retry-After`) | `ClientApplication.cs` |
| Certificado A1 | cofre AES-GCM com chave-mestra em Docker secret; upload só pelo escopo `admin`; alertas em 60/30/15/7/1 dias | `Certificate.cs`, `AdminEndpoints.cs` |
| Guarda legal | XML autorizado guardado, "jamais apagar" (5 anos) | README |
| NFS-e | contrato `POST /api/v1/nfse` (ISS, retenções, `nfeVinculada`); para Caçapava, padrão CONAM/e-Transparência (ADR 005 de lá), sem certificado | `NfseRequest.cs`, ADR 005 |

### Lacunas do Emissor: pré-condição externa (D3, ADR 0027)
Trabalho no repositório do EmissorFiscal, **com dono PO** (John), planejado já, em paralelo. Não bloqueia o `Ready` desta story; cada linha diz qual story não fecha sem ela.

| # | Lacuna (situação no commit `e294444`) | O que o Emissor precisa entregar | Antes de |
|---|---|---|---|
| L-a | **Nota pendente não se resolve sozinha:** nenhum job reconcilia `Processing`, `ContingencyPending` ou a duplicidade 204 com a SEFAZ, e sem webhook a nota fica parada | Job de reconciliação com a SEFAZ e **consulta forçada** por documento (rota final na ADR 0027), que consulta a SEFAZ e dispara o webhook da transição | FIS-03a |
| L-b | Sem IBS/CBS no `NfeRequest` e no `NfseRequest` (P-03; em Caçapava o `Reg60_RTC` já é obrigatório) | Grupos IBS/CBS (CST, `cClassTrib`, base, alíquota, valor) na NF-e e na NFS-e | FIS-03a, FIS-06 |
| L-c | DANFE só em HTML | **DANFE em PDF**, também com a marca de nota cancelada | FIS-03a |
| L-d | Sem grupo `transp` completo e volumes (só `modalidadeFrete`) | `transp` (transportadora, veículo, volumes, peso); duplicatas (`cobr`) não são exigidas (D4) | FIS-03a |
| L-e | `GET /me` inexistente; situação do certificado só pelo escopo `admin` | `GET /api/v1/me`: tenant, emitente (CNPJ, IE, UF), ambiente, escopos e certificado (validade, titular, CNPJ, thumbprint), sem `admin` | suíte `emissor-homolog` desta story |
| L-f | Cancelar e inutilizar exigem só `nfe:emitir` | Escopo **`nfe:eventos`** para cancelar, corrigir e inutilizar | FIS-03b |
| L-g | Webhook assinado só sobre o corpo (sem proteção contra reenvio antigo) | **Timestamp no HMAC** (`{timestamp}.{corpo}`, cabeçalho definido na ADR 0027), janela de 5 min | esta story (validador) |
| L-h | Upload do PFX com a senha fora do formulário (`string senha`), só com `admin`; CNPJ do certificado conferido aceitando falha | Senha em **`[FromForm]`**; **CNPJ do certificado igual ao do emitente, sem aceitar falha**; rota de certificado do próprio emitente da chave, sem `admin` (escopo próprio, nome na ADR 0027) | FIS-02a |
| L-i | API, banco e painel na `stack_stack` em HTTP, junto com o Dolibarr; banco com superusuário; Swagger ligado | **Rede dedicada `bqe-fiscal`** (`internal: true`), banco com usuário sem superusuário, Swagger desligado fora do desenvolvimento | implantação em homologação |
| L-j | Um cliente pode atender vários tenants | **Um tenant do Emissor por tenant do BemAqui**, com chave sem escopo `admin` | implantação |
| L-k | Chave-mestra do cofre só no Docker secret | **Cópia offline da chave-mestra** (procedimento no runbook do Emissor) | implantação em homologação; conferida na GOL-01a |
| L-l | `tipoOperacao`: o guia diz `0` entrada e `1` saída; o modelo comenta "1 = entrada, 2 = saída" | Confirmado pela suíte de contrato (CA 15) | esta story |
| L-m | NFS-e de Caçapava não configurada; sem cancelamento, PDF e webhook de NFS-e | Provider de Caçapava (CONAM), cancelamento, PDF e webhook de NFS-e | FIS-06 |
| — | Sem DIFAL (`ICMSUFDest`) | Não exigido: DIFAL bloqueado na emissão (FIS-02b) | — |
| — | Sem Distribuição DF-e nem consulta de NF-e de terceiros | Fora do MVP (FIS-04a avisa "situação na SEFAZ não conferida"; FIS-05 sem DF-e) | — |

## Critérios de aceite
**ADR 0027 implementada**
1. **Dado** a ADR 0027 aceita, **quando** esta story é entregue, **então** a integração é a forma (b) **serviço HTTP**, nenhum assembly do EmissorFiscal ou da Unimake é referenciado (teste de arquitetura), cada tenant do BemAqui usa **um tenant próprio do Emissor** com uma `ClientApplication` de escopos `nfe:emitir`, `nfe:eventos` e `consultar` (e `nfse:emitir` na FIS-06), e o runbook registra o mapa tenant BemAqui → tenant, emitente e cliente do Emissor.

**Contrato `IFiscalEmitter` e fake**
2. **Dado** o módulo Fiscal, **quando** precisa do EmissorFiscal, **então** usa só `IFiscalEmitter` (`Fiscal.Application`): `PreviewAsync`, `IssueNfeAsync(request, idempotencyKey)`, `GetNfeAsync`, `ForceConsultAsync` (consulta forçada, L-a), `GetXmlAsync`, `GetDanfePdfAsync`, `CancelNfeAsync`, `CorrectNfeAsync`, `InvalidateRangeAsync`, `GetEventsAsync`, `SefazStatusAsync`, `GetAccountAsync` (o `GET /me`: emitente, ambiente, escopos e certificado), `UploadCertificateAsync` (FIS-02a) e, reservadas para a FIS-06, `IssueNfseAsync`/`GetNfseAsync`; os tipos do contrato são do BemAqui (inglês, `decimal`, `DateOnly`/`DateTimeOffset`), nunca os DTOs do Emissor.
3. **Dado** os testes dos módulos, **quando** rodam, **então** usam o **`FakeFiscalEmitter`** (em memória, em `tests/`), que simula autorização, rejeição, denegação, contingência, demora, queda depois de criar e o webhook; nenhum teste de módulo chama a rede.
4. **Dado** o adaptador `EmissorFiscalHttpEmitter` (`Fiscal.Infrastructure`), **quando** emite, **então** mapeia o pedido para o JSON do Emissor (enums com os códigos da SEFAZ, valores com 2 casas e quantidades com 4), envia `X-Api-Key` e o `Idempotency-Key` e traduz a resposta: `201` → `Authorized`, `202` com situação 0/1 → `Processing`, `202` com situação 6 → `ContingencyPending`, `422` com 3 → `Rejected` (código, motivo e ação sugerida), `422` com 5 → `Denied`; 400 vira erro de validação campo a campo, 401/403 vira `fiscal_integration_misconfigured` (alerta, CA 12), 409 vira conflito de estado, 429 respeita o `Retry-After`; tempo limite de 30 s.

**Idempotência e reenvio**
5. **[Segurança]** **Dado** uma nota local, **quando** é enviada, **então** o `Idempotency-Key` é `nfe-{invoiceId}-{tentativa}`, e a tentativa só muda depois de uma **rejeição** (a rejeição consome o número; a correção é nova emissão); na transição para `Processing`, o SHA-256 do payload canônico fica gravado na nota (`payload_hash`), e todo reenvio com a mesma chave confere o hash: payload diferente com a mesma chave é recusado localmente com 409 `fiscal_payload_changed`, sem chamar o Emissor.
6. **[Segurança]** **Dado** falha de rede, tempo limite ou 5xx depois do POST de emissão, **quando** o adaptador não sabe se a nota saiu, **então** nunca gera chave nova: a nota fica `Processing`, e a nova tentativa usa a **mesma** chave e o mesmo payload (o Emissor devolve a nota existente ou a cria agora); `ContingencyPending` nunca dispara reemissão (teste com o fake simulando queda depois de criar).

**Credencial por tenant**
7. **[Segurança]** **Dado** `settings.edit` (reservada ao Proprietário, ADR 0008), **quando** informo em "Configurações → Integrações → Emissor fiscal" a URL interna, a API key e o segredo do webhook e clico "Salvar integração fiscal", **então** o diálogo pede o **código do autenticador (2FA de novo, D6)**; sem o código válido, 403 `two_factor_required` e nada é gravado; com ele, a chave e o segredo são gravados cifrados (`SecretKind.FiscalApiKey`, `SecretKind.WebhookSecret`), **nunca voltam pela API** (a tela mostra "configurada em dd/mm/aaaa"), a URL só aceita o host da lista permitida do ambiente (o serviço do Emissor na rede `bqe-fiscal`; nunca IP de metadados, `localhost` ou outro serviço), e a auditoria grava "alterado" sem o valor (teste de canário).
8. **[Segurança]** **Dado** a integração salva, **quando** clico "Testar conexão", **então** o sistema chama `GET /api/v1/me` (L-e) e mostra escopos, tenant do Emissor, emitente e ambiente; recusa, com a mensagem e sem ativar a integração: chave com escopo `admin` ("Use uma chave sem o escopo admin."), falta de `nfe:emitir`, `nfe:eventos` ou `consultar` ("Faltam os escopos {lista} nesta chave."), e CNPJ do emitente diferente do CNPJ da empresa do tenant ("O emitente do Emissor Fiscal não é o CNPJ desta empresa.").
9. **[Segurança]** **Dado** o ambiente, **quando** salvo ou testo, **então** vale a flag de catálogo `fiscal_environment_allowed` do tenant (`None`/`Homologation`/`Production`): o tenant `demo` tem `Homologation` (homologação SEFAZ com o emitente da Konebras, notas sem valor fiscal e destinatários fictícios); o `konebras` fica `None` até a GOL-01b, que liga `Production` no checklist (ADR 0034); integração com ambiente acima do permitido é recusada com "A emissão desta empresa só liga na virada para a produção." (nada fiscal no `konebras` antes da GOL-01b, ADR 0020).
10. **Dado** a rotação da chave no Emissor, **quando** o Proprietário cola a chave nova, **então** o sistema testa a nova antes de trocar (CA 8), a antiga deixa de ser usada imediatamente e os membros do Proprietário são avisados; a rotação do segredo do webhook segue a sobreposição de 24 h da FND-16.

**Retorno assíncrono**
11. **[Segurança]** **Dado** o provedor `emissor-fiscal` registrado na FND-16 com o validador HMAC-SHA256 com timestamp (L-g: assinatura sobre `{timestamp}.{corpo}`, janela de 5 min, id de entrega `X-BemAqui-Delivery-Id`), **quando** chega `nfe.autorizada`, `nfe.rejeitada`, `nfe.cancelada` ou `nfe.denegada`, **então** o handler do Fiscal (consumidor com inbox, ADR 0031) localiza a nota local pelo id do documento do Emissor e pela chave, **confere** que o tenant do evento é o da integração, **reconsulta** `GET /api/v1/nfe/{chave}` antes de mudar a situação (o corpo só dispara a reconsulta) e aplica a transição (FIS-03a); evento de nota desconhecida fica `Ignored` com o motivo; assinatura inválida ou timestamp fora da janela: 401 e nada gravado.
12. **Dado** 401, 403, conexão recusada, `GET /sefaz/status` fora do ar ou nota em `ContingencyPending` há mais de 2 h, **quando** acontece, **então** sai o alerta `fiscal_integration` (FND-12, uma vez por janela por tenant) e a notificação a quem tem `fiscal.edit`, com link para a integração ou para a nota; o log traz só tenant, operação, situação HTTP, código de rejeição e duração, **nunca** a API key, o payload com dados do destinatário ou o XML (teste de canário).
13. **Dado** uma nota autorizada, **quando** o handler a confirma, **então** baixa o XML autorizado e o guarda como arquivo do sistema (dono `OutgoingInvoice`, com o prefixo do tenant no storage), com SHA-256 conferido e retenção de **5 anos** sem expurgo (ADR 0018), para o BemAqui ter a própria guarda legal.
14. **[Segurança]** **Dado** o recorrente global `fiscal-pending-check` (a cada 10 min, um `TenantJob` por tenant, com `tenantId` e conferência do módulo `fiscal` liberado), **quando** existe nota local em `Processing` ou `ContingencyPending` há mais de 5 min, **então** chama a **consulta forçada** do Emissor (`ForceConsultAsync`, L-a) e aplica a mesma transição do CA 11, idempotente (teste com webhook perdido, com webhook e consulta chegando juntos e com dois tenants); enquanto a consulta forçada não existir no Emissor, a nota fica `Processing` com o estado de demora e o alerta do CA 12, nunca reemitida.

**Contrato, rede e dados**
15. **Dado** a suíte de contrato, **quando** roda no CI, **então** um **fake HTTP do Emissor** (`TestServer` com as respostas do `docs/integracao/README.md` de lá) cobre 201, 202 (1 e 6), 422 (3 e 5), 400, 401, 403, 409, 429, o `GET /me`, a consulta forçada e o webhook assinado com timestamp; e a suíte `[Trait("contract","emissor-homolog")]`, fora do CI padrão, roda contra a instância de homologação do Emissor no tenant `demo` (nunca `konebras`), conferindo o mesmo comportamento e o valor de `tipoOperacao` de entrada e saída (L-l).
16. **[Segurança]** **Dado** o payload de emissão (nome, documento, endereço e e-mail do destinatário), **quando** é enviado, **então** nada dele vai para o outbox do BemAqui, para a auditoria ou para o log; a nota local guarda ids, totais e o `payload_hash`.
17. **[Segurança]** **Dado** o compose de homologação e de produção, **quando** o `check-compose.mjs` roda, **então** a API do BemAqui alcança o Emissor só pela rede **`bqe-fiscal`** (`internal: true`, só a API do BemAqui e a do Emissor), nunca pela `stack_stack`; o banco do Emissor não entra nessa rede; o teste falha se o serviço do Emissor publicar porta ou se o Swagger estiver ligado na configuração de produção.

**Permissões**
18. **Dado** o módulo `fiscal` não liberado, **quando** chamo a tela ou a API de integração, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; sem `settings.edit`, a tela fica em modo leitura (com `settings.view`) e o POST responde 403 `forbidden`; `RequireModule` e `perm:` em todo endpoint; política de rate limit nomeada `fiscal-integration` no "Testar conexão" (429 com `Retry-After`).

## Regras de negócio e técnicas
- **Forma de integração (ADR 0027):** serviço HTTP; o BemAqui é um "sistema conectado" do Emissor; um tenant do Emissor por tenant do BemAqui; o emitente é o CNPJ da empresa do tenant; nenhum código do Emissor é copiado.
- **Numeração da NF-e:** do Emissor (emitente + série + ambiente). O BemAqui grava o número e a chave devolvidos; `fiscal_settings.next_number` sai do `03-modelo-dados`.
- **Cálculo de tributos:** do BemAqui (FIS-02b).
- **Retorno:** webhook (FND-16, ADR 0031) como caminho principal, `fiscal-pending-check` com a consulta forçada como reserva; transição sempre pela reconsulta.
- **Idempotência:** `nfe-{invoiceId}-{tentativa}`; a tentativa sobe só depois de `Rejected`; `payload_hash` gravado em `Processing`.
- **Guarda:** XML autorizado e eventos guardados nos dois lados; no BemAqui como anexo de sistema, nunca apagado.
- **Segredos:** `SecretKind.FiscalApiKey` (`fiscal-api-key`) e `SecretKind.WebhookSecret` (`webhook-secret`), ADR 0007 adendo; o A1 **não** é segredo do BemAqui (D12: fica no cofre do Emissor; a FIS-02a só o repassa em memória).
- **Ambiente:** `catalog.tenants.fiscal_environment_allowed`; produção só na GOL-01b (ADR 0034).

## Permissões, eventos e tabelas
- **Permissões:** `settings.edit` (configurar e testar; 2FA de novo ao salvar), `settings.view` (ver), `fiscal.view`/`fiscal.edit` (eventos recebidos e alertas).
- **Eventos:** nenhum de domínio nesta story (as transições publicam na FIS-03a); provedor de webhook `emissor-fiscal`.
- **Tabelas:** `fiscal.integration_settings` (`base_url`, `api_key_cipher`, `webhook_secret_cipher`, `previous_webhook_secret_cipher`, `rotated_at`, `emitter_tenant_id`, `issuer_document`, `environment`, `scopes`, `last_test_at`, `last_test_result`, `xmin`; uma linha por tenant; auditada sem os segredos); `fiscal.outgoing_invoices.payload_hash` e `attempt` (a tabela nasce na FIS-03a; o adaptador já trabalha com os dois); `catalog.tenants.fiscal_environment_allowed` (nova, padrão `None`).
- **Jobs:** recorrente global `fiscal-pending-check` (um `TenantJob` por tenant).

## Telas e estados
- **Padrões comuns das Fases 4 a 8:** 6 (integração externa assíncrona: "Enviado / Aguardando / Demorando / Confirmado / Recusado"; `EnvironmentBanner` único, entregue na BNK-01a).
- **Configurações → Integrações → Emissor fiscal** (`/app/configuracoes/integracoes/emissor-fiscal`): seções Conexão (URL, chave só de escrita, segredo do webhook só de escrita com "Gerar novo" mostrado uma vez), Emitente e ambiente (lidos do `GET /me`), Situação (último teste, último aviso recebido, notas pendentes com link).
- **Verbos:** "Salvar integração fiscal" / "Integração fiscal salva"; "Testar conexão" / "Conexão testada".
- **Estados:** carregando; não configurada ("Conecte o Emissor Fiscal para emitir notas. Peça ao administrador do Emissor Fiscal uma chave sem o escopo admin." + "Configurar"); não permitida no tenant ("A emissão desta empresa só liga na virada para a produção.", sem formulário); erro do teste dentro do formulário com o motivo e a ação; `EnvironmentBanner` "Ambiente de homologação: as notas não têm valor fiscal."
- **Propositivo:** depois do teste aprovado, o `NextSteps` oferece "Configurar o fiscal da empresa" (FIS-02a).
- **400 px:** seções empilhadas; botões de 44 px visíveis sem rolar.
- **Ajuda:** `Ajuda/configuracoes-integracoes-emissor-fiscal.md` (prevista); `<Dica>` em "Segredo do webhook", "Escopo admin" e "Ambiente".

## Plano de testes
- **Unidade:** mapeamento do pedido (enums, casas decimais, `tipoOperacao`) e da resposta (CA 4); chave e tentativa, `payload_hash` e recusa de payload mudado (CA 5); política de reenvio com a mesma chave (CA 6); conferência de escopos e CNPJ (CA 8); validador com timestamp fora da janela (CA 11).
- **Integração (Testcontainers + fake):** `FakeFiscalEmitter` nos cenários do CA 3; salvar com e sem 2FA, canário dos segredos (CA 7); testar com chave `admin`, escopo faltando e CNPJ diferente (CA 8); `fiscal_environment_allowed` no `demo` e no `konebras` (CA 9); rotação testando a nova (CA 10); webhook assinado → reconsulta → transição, nota desconhecida ignorada, tenant diferente recusado (CA 11); alertas e canário de log (CA 12, 16); XML guardado com hash (CA 13); pendente resolvido pela consulta forçada, corrida webhook × job, dois tenants (CA 14); três camadas e rate limit (CA 18).
- **Contrato:** suíte do fake HTTP no CI; suíte `emissor-homolog` manual contra a homologação do Emissor no tenant `demo`, registrada no runbook (CA 15).
- **Infra:** `check-compose.mjs` com a rede `bqe-fiscal`, sem porta publicada e sem Swagger (CA 17).
- **Arquitetura:** nenhuma referência a assembly do Emissor ou da Unimake (CA 1).
- **E2E (Playwright):** configurar a integração com o fake, testar a conexão e ver o `EnvironmentBanner`; tela a 400 px.

## Fora do escopo
- Configuração fiscal do tenant, certificado e regras: FIS-02a e FIS-02b. Emissão, eventos e DANFE: FIS-03a e FIS-03b. NFS-e: FIS-06.
- Implantar o Emissor na VPS, criar o tenant e o emitente lá, obter o A1 e corrigir as lacunas L-a a L-m: trabalho fora deste repositório (pré-condição externa, dono PO).

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fases 4 a 8, tabela 3.2, pontos 6–12):
1. O PO implanta o EmissorFiscal já, em homologação; produção só na GOL-01b (D2; CA 9, 15).
2. A1 só no cofre do EmissorFiscal; o BemAqui guarda só validade, titular/CNPJ e thumbprint (D12; a R-FIS-2 fica atendida pelo cofre do Emissor; regras; FIS-02a).
3. Homologação SEFAZ no tenant `demo`; nada fiscal no `konebras` antes da GOL-01b (flag `fiscal_environment_allowed`, CA 9).
4. IBS/CBS assumidos como obrigatórios e priorizados no Emissor (P-03; lacuna L-b, antes da FIS-03a).
5. DANFE em PDF pelo EmissorFiscal (lacuna L-c); reserva registrada na ADR 0027: `IPdfRenderer` a partir do XML, nunca o HTML (fecha o XSS da DANFE em HTML).
6. Grupo `transp` e volumes antes do go-live (lacuna L-d); duplicatas fora da nota (D4).
7. `Idempotency-Key` `nfe-{invoiceId}-{tentativa}`, a tentativa muda só depois de uma rejeição; hash do payload gravado em `Processing` (CA 5).

Decisões da revisão aplicadas: D3 (lacunas do Emissor com dono e "antes de"; consulta forçada no `fiscal-pending-check`, CA 14; `GET /me` e `nfe:eventos`, CA 8; timestamp no HMAC, CA 11; rede `bqe-fiscal`, banco sem superusuário e Swagger desligado, CA 17; um tenant do Emissor por tenant, CA 1); D6 (2FA de novo ao salvar a credencial, CA 7); §4.3 transversal (job com `tenantId` e módulo liberado, CA 14; storage com prefixo de tenant, CA 13; rate limit nomeado, CA 18); a premissa de que o Emissor reconciliava nota pendente foi corrigida (lacuna L-a).

**Pré-condição externa:** trabalho no repositório do EmissorFiscal (lacunas L-a a L-m, dono PO, ADR 0027) e o Emissor implantado em homologação na rede `bqe-fiscal`, com o tenant do `demo` e a cópia offline da chave-mestra (para a suíte `emissor-homolog` e o DoD). Não bloqueia o `Ready`: o código e o CI usam o fake.

## Definition of Done
- [x] Critérios de aceite atendidos; ADR 0027 seguida (adendo de 27/10/2026); adendo à ADR 0007 aplicado (`fiscal-api-key`).
- [x] Testes de unidade, integração (Testcontainers), contrato (fake no CI), arquitetura e E2E passando. [ ] Suíte `emissor-homolog` executada uma vez no `demo` e registrada: pendente (pré-condição externa, roteiro em `docs/operacao/emissor-fiscal.md` §6).
- [x] Migrations criadas (não destrutivas): `FiscalIntegration` (tenant) e `TenantFiscalEnvironment` (catálogo). Teste sobre cópia do `konebras`: pelo deploy de homologação (só `CREATE TABLE` e `ADD COLUMN` com padrão).
- [x] Permissões só do `ModuleCatalog` (nenhuma nova: `settings.view`, `settings.edit`, `fiscal.view`, `fiscal.edit`).
- [x] Tela com estados, inclusive a 400 px.
- [x] Ajuda escrita (`Ajuda/configuracoes-integracoes-emissor-fiscal.md`); teste de cobertura passando.
- [x] Alertas acionáveis (link para a integração ou para a nota).
- [x] Auditoria ligada (sem segredo).
- [x] OpenAPI e cliente Orval regenerados.
- [x] `docs/02-arquitetura.md` §6.1 (rede `bqe-fiscal`, consulta forçada) e `docs/03-modelo-dados.md` (`fiscal_settings` sem `next_number`, `integration_settings`, `fiscal_environment_allowed`) atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [x] `IFiscalEmitter` e tipos do contrato (Application); `FakeFiscalEmitter` (tests); `EmissorFiscalHttpEmitter` (Infrastructure, `HttpClient` tipado com tempo limite e retry só para leitura).
- [x] `IntegrationSettings`, `SecretKind.FiscalApiKey`, tela, 2FA de novo e ajuda.
- [x] Validador HMAC com timestamp no provedor `emissor-fiscal` (FND-16), handler com inbox e recorrente `fiscal-pending-check` com a consulta forçada.
- [x] Fake HTTP e suíte de contrato; rede `bqe-fiscal` no compose e no `check-compose.mjs`.
- [x] Runbook: mapa de tenants, chave sem `admin`, suíte `emissor-homolog`.

## Quality gates
- Agentes previstos: @architect (aderência à ADR 0027), @dev, cybersecurity (chave, rede, webhook, log), @qa.
- Gate: CA 1, 5, 6, 7, 11, 14, 15, 16 e 17 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fases 4 a 8).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: ordem 7, em paralelo à trilha do banco, depois da BNK-01a pelo `EnvironmentBanner`) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK (ajustado: premissa da reconciliação corrigida pelo código do Emissor) |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** a numeração da NF-e passou a ser do EmissorFiscal (o `next_number` do 03 sai); a chave com escopo `admin` é recusada por padrão; rede dedicada no lugar da `stack_stack`; o webhook só dispara reconsulta; re-estimada para G.
- **Correções da revalidação (21/10/2026):** a story deixa de escrever a ADR e passa a implementá-la (ADR 0027 já escrita; CA 1); pontos 6 a 12 decididos; a premissa falsa ("o Emissor consulta sozinho e resolve depois") foi corrigida: nenhum job do Emissor reconcilia nota pendente, então a conferência de pendentes usa a consulta forçada, lacuna L-a (CA 14); tabela de lacunas reescrita com o que o Emissor entrega e "antes de", dono PO (D3); `GET /me` e escopo `nfe:eventos` no teste de conexão (CA 8); timestamp no HMAC (CA 11); `FakeFiscalEmitter` (CA 3); `payload_hash` em `Processing` (CA 5); 2FA de novo ao salvar a credencial (D6, CA 7); `fiscal_environment_allowed` (CA 9); rede `bqe-fiscal`, banco sem superusuário e Swagger desligado (CA 17); padrão 6 das Fases 4 a 8; nova ordem (7, Marco 5.1); CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma de decisão de story. Externa: lacunas do Emissor (L-a a L-m) e a implantação em homologação, dono PO.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready (pré-condição externa)`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Análise do EmissorFiscal e story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (D2, D3, D6, D12, tabela 3.2 pontos 6–12, §4.3, §4.4); implementa a ADR 0027; premissa da reconciliação corrigida; nova ordem (7, Marco 5.1, em paralelo à trilha do banco); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
| 27/10/2026 | 1.0 | Implementação: `IFiscalEmitter` e tipos do BemAqui; `EmissorFiscalHttpEmitter` (30 s, retry só em leitura, 201/202/422/400/401/403/404/409/429/5xx); `FakeFiscalEmitter` e fake HTTP do Emissor (suíte de contrato no CI) e suíte `emissor-homolog` manual; `fiscal.integration_settings` com chave cifrada (`fiscal-api-key`), 2FA de novo, URL só da lista, teste de conexão (`GET /me`: admin, escopos, CNPJ, ambiente) e rotação testando a chave nova; `catalog.tenants.fiscal_environment_allowed` (`demo` em homologação); provedor `emissor-fiscal` com o `TimestampedSignatureValidator` (`t=…,v1=…`, 5 min) e handler que só reconsulta; `fiscal.outgoing_invoices` enxuta com `attempt` e `payload_hash`; `fiscal-pending-check` com a consulta forçada e trava por nota; XML guardado com prefixo do tenant e SHA-256 conferido; alertas `fiscal_integration`; rede `bqe-fiscal` no compose, no `check-compose.mjs` e no `deploy.sh`; tela Configurações → Integrações → Emissor fiscal com `EnvironmentBanner`, ajuda e E2E; runbook `docs/operacao/emissor-fiscal.md`. Desvios: segredo do aviso na credencial da FND-16 (sem `webhook_secret_cipher` na tabela, o Emissor é quem gera o segredo, sem "Gerar novo"); `outgoing_invoices` criada enxuta aqui; preço unitário e alíquotas com até 4 casas; webhook pelo host interno dispensa o proxy confiável só em `/webhooks/*` (recomendação aceita, PO 24/10) | Dex (dev) |
