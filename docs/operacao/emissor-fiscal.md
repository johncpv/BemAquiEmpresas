# Runbook: integração com o EmissorFiscal (FIS-01, ADR 0027)

O BemAqui fala com o EmissorFiscal (`D:\GIT\EmissorFiscal`, implantado pelo PO) **só** pelo `IFiscalEmitter`, por HTTP, na rede Docker dedicada `bqe-fiscal`. Nenhum código do Emissor é copiado ou referenciado (teste de arquitetura). O certificado A1 fica no cofre do Emissor; o BemAqui guarda a chave de API (`SecretKind.FiscalApiKey`, propósito `fiscal-api-key`) e o segredo do aviso (`SecretKind.WebhookSecret`, credencial do provedor `emissor-fiscal` da FND-16), cifrados e só de escrita.

## 1. Mapa de tenants

Um tenant do Emissor por tenant do BemAqui, com uma `ClientApplication` (sistema conectado) por tenant e o emitente igual ao CNPJ do tenant. Preencha ao criar cada um:

| Tenant BemAqui | `fiscal_environment_allowed` | Tenant do Emissor (`tenantId` do `GET /me`) | Emitente (CNPJ) | Cliente do Emissor (`clienteId`) | Escopos da chave |
|---|---|---|---|---|---|
| `demo` | `Homologation` | _(preencher)_ | 47.225.005/0001-86 (emitente de teste, SEFAZ homologação) | _(preencher)_ | `nfe:emitir`, `nfe:eventos`, `consultar` |
| `konebras` | `None` até a GOL-01b; `Production` no checklist dela (ADR 0034) | _(na GOL-01b)_ | 47.225.005/0001-86 | _(na GOL-01b)_ | `nfe:emitir`, `nfe:eventos`, `consultar` (+ `nfse:emitir` na FIS-06) |

- **Nunca** dê o escopo `admin` à chave do BemAqui: o "Testar conexão" recusa ("Use uma chave sem o escopo admin."). O painel do próprio Emissor é que usa uma chave `admin`.
- O `certificado:gerenciar` entra na chave quando a FIS-02a (envio do A1 pela tela do BemAqui) for implantada.
- Nada fiscal no `konebras` antes da GOL-01b (ADR 0020): a marca `catalog.tenants.fiscal_environment_allowed` fica `None`, e a API recusa salvar, testar e emitir.

### Ligar a homologação no `demo`
A migration `TenantFiscalEnvironment` já marca o tenant `demo` com `Homologation`. Tenant `demo` criado depois da migration:

```sql
UPDATE catalog.tenants SET fiscal_environment_allowed = 'Homologation' WHERE key = 'demo';
```

A produção (`Production`) só é ligada no checklist da GOL-01b.

## 2. Rede `bqe-fiscal`

- Criada uma vez na VPS, **sem saída para a internet**: `docker network create --internal bqe-fiscal`. O `deploy.sh` cria se faltar e **para o deploy** se a rede existir sem `--internal`.
- Só entram nela a `bqe-<ambiente>-api` (compose do BemAqui) e o `emissor-fiscal` (compose do Emissor). O banco, o painel e o portal do Emissor ficam fora; o banco do BemAqui também. O `scripts/check-compose.mjs` (CI) reprova outro serviço nela, porta publicada no serviço do Emissor e Swagger ligado.
- A API do BemAqui chama `http://emissor-fiscal:8080` (lista `Fiscal:Emissor:AllowedHosts`; a tela recusa IP, `localhost` e outros nomes).
- O Emissor avisa a API em `http://bqe-<ambiente>-api:8080/webhooks/emissor-fiscal/{tenantKey}` (host interno em `Webhooks:InternalHosts`); esse caminho, e só ele, dispensa o `stack-nginx`. A origem é provada pela assinatura.

## 3. Configurar um tenant

1. No painel do Emissor: criar o tenant, o emitente (CNPJ do tenant) e a `ClientApplication` com os escopos da tabela. Copiar a chave (aparece uma vez).
2. No BemAqui, como Proprietário: **Configurações → Integrações → Emissor fiscal → Configurar**: endereço `http://emissor-fiscal:8080`, a chave e, por enquanto, um segredo provisório; **Salvar integração fiscal** (pede o código do autenticador).
3. A tela mostra o **Endereço do aviso**. No painel do Emissor: **Webhooks → Novo webhook** com esse endereço e todos os eventos. Copiar o segredo que o Emissor mostra e colar em **Trocar o segredo** no BemAqui.
4. **Testar conexão**: confere o `GET /api/v1/me` (sem `admin`, com os escopos, CNPJ do emitente igual ao do tenant, ambiente dentro do permitido). Aprovado, a integração fica **Ativa**.

## 4. Rotação

- **Chave:** gerar a nova no Emissor e colar em **Trocar a chave**: o BemAqui testa a nova antes de trocar; recusada, a antiga continua. Aceita, a antiga deixa de ser usada na hora e os membros do Proprietário recebem aviso. Depois, desativar a antiga no Emissor.
- **Segredo do aviso:** rotacionar no Emissor (ele assina com os dois por 24 h) e colar o novo no BemAqui (que aceita o anterior por 24 h, FND-16). Vazou: rotacionar com `?imediata=true` no Emissor e colar o novo já.

## 5. Retorno das notas

- O webhook (`nfe.autorizada`, `nfe.rejeitada`, `nfe.cancelada`, `nfe.denegada`) só dispara a **reconsulta** `GET /api/v1/nfe/{chave}`; a situação vem dela. Aviso de outro tenant do Emissor ou de nota desconhecida fica `Ignored` em **Configurações → Integrações → Eventos recebidos**.
- O recorrente `fiscal-pending-check` (a cada 10 min, fila `fiscal`, um job por tenant com o módulo `fiscal` liberado) passa toda nota em `Processing` ou `ContingencyPending` há mais de 5 min pela **consulta forçada** `POST /api/v1/nfe/{chave}/consultar`. Nunca reemite.
- Autorizada, o XML (`nfeProc`) é guardado no BemAqui como arquivo de sistema (dono `OutgoingInvoice`, prefixo do tenant no storage, SHA-256 conferido na releitura, 5 anos sem expurgo).
- Alerta `fiscal_integration` (super admins e quem tem `fiscal.edit`, uma vez por janela de 30 min): chave recusada (401/403), Emissor fora de alcance, SEFAZ fora do ar, contingência há mais de 2 h e consulta forçada indisponível.

## 6. Suíte `emissor-homolog` (manual, no `demo`)

Fora do CI padrão (pula sem as variáveis). Rodar uma vez com o Emissor de homologação no ar e registrar o resultado aqui:

```sh
EMISSOR_HOMOLOG_URL=http://emissor-fiscal:8080 EMISSOR_HOMOLOG_KEY=<chave do demo> \
  sh scripts/test-docker.sh --filter-class "*EmissorHomologContractTests"
```

(rodar de um container na rede `bqe-fiscal`, ou com um túnel até ela; **nunca** com a chave do `konebras`). Confere: `GET /me` sem `admin` e com os escopos; emitente só em homologação; prévia aceita `tipoOperacao` 1 (saída) e 0 (entrada) (lacuna L-l).

| Data | Versão do Emissor | Resultado | Quem |
|---|---|---|---|
| _(pendente: pré-condição externa)_ | | | |

## 7. O que conferir no EmissorFiscal (lacunas da ADR 0027 §5)

O lado do BemAqui foi feito contra o contrato do guia de integração do Emissor (lido em 26/10/2026, commit `19e0452`) e provado contra o fake. Conferir no Emissor implantado:

| Lacuna | O que o BemAqui espera | Situação no guia de 26/10 |
|---|---|---|
| L-a consulta forçada | `POST /api/v1/nfe/{chave}/consultar` devolvendo o mesmo formato do `GET` | Documentada; conferir na homologação. Sem ela, a nota fica "demorando" com alerta, nunca reemitida |
| L-e `GET /me` | `clienteId`, `tenantId`, `escopos` efetivos, `emitentes[]` com `cnpj`, `ambiente` (`homologacao`/`producao`) e `certificado` | Documentado |
| L-f `nfe:eventos` | Cancelar, corrigir e inutilizar exigem `nfe:eventos` | Documentado |
| L-g timestamp no HMAC | `X-BemAqui-Signature: t={ts},v1={hex}[,v1=…]` sobre `{ts}.{corpo}`, `X-BemAqui-Timestamp`, janela de 5 min, `X-BemAqui-Delivery-Id`, `X-BemAqui-Event` | Documentado (formato do Stripe). O validador do BemAqui é o `TimestampedSignatureValidator` |
| L-c DANFE em PDF | `GET /nfe/{chave}/danfe` com `application/pdf`; HTML é recusado pelo adaptador | Documentado (503 sem o conversor) |
| L-i rede e Swagger | Emissor na `bqe-fiscal`, sem porta publicada, Swagger desligado em produção | Documentado no guia de implantação de lá |
| L-j tenant por tenant | Uma `ClientApplication` por tenant do BemAqui, sem `admin` | Procedimento acima |
| L-l `tipoOperacao` | `0` entrada, `1` saída | Documentado; conferir pela suíte `emissor-homolog` |
| L-b, L-d, L-h, L-k, L-m | IBS/CBS, `transp`, A1 sem `admin`, cópia offline da chave-mestra, NFS-e de Caçapava | Antes da FIS-03a, FIS-02a, GOL-01a e FIS-06 |
| Webhook de outro tenant | O corpo traz `tenantId` do Emissor: o BemAqui ignora aviso de outro tenant | Documentado |

## 8. Problemas comuns

| Sintoma | Onde olhar |
|---|---|
| "O Emissor Fiscal recusou a chave." | Chave errada, desativada ou de outro cliente: gere outra no Emissor e troque |
| "O Emissor Fiscal não respondeu." | `docker network inspect bqe-fiscal` (a API e o `emissor-fiscal` estão nela?); `docker compose ps` do Emissor |
| Aviso recusado (401) em "Eventos recebidos" não aparece | Segredo diferente nos dois lados ou relógio da VPS fora (janela de 5 min): `timedatectl` |
| Nota parada em "esperando a SEFAZ" | A conferência roda a cada 10 min; depois de 2 h em contingência sai alerta. Não emita de novo |

## 9. Configuração fiscal e certificado digital (FIS-02a)
A FIS-02a foi provada contra o fake do Emissor (`FakeFiscalEmitter`, testes de integração). A validação real fica para o tenant `demo`, em homologação, com o Emissor já homologado:

1. **Configurações → Fiscal** (`/app/configuracoes/fiscal`): confira que a tela abre com os valores do seed `fiscal-defaults` (Lucro Presumido, CRT 3, série 2, CFOP 5101/5101/6101/6107/5102/6102, ICMS 18/12/7/4%, PIS 0,65%, COFINS 3%, item 7.02, ISS 5%, IBS/CBS 000/000001 com 0,9%/0,1%, 24 h, frete 0, "VOLUME") e todos com "A confirmar com a contadora". Em homologação, nada bloqueia.
2. **Baixar para a contadora**: o PDF sai com os 40 campos, o valor, a situação e a coluna da resposta. É o roteiro da conversa com a contadora (P-01, P-02, P-03, série, códigos da NFS-e de Caçapava).
3. **Certificado digital** (`/app/configuracoes/fiscal/certificado`), com a integração do `demo` testada:
   - envie o A1 de teste do `demo` com o código do autenticador; o BemAqui confere chave privada, política A1 da ICP-Brasil (`2.16.76.1.2.1.*`), validade e o CNPJ do titular (`otherName` 2.16.76.1.3.3 ou o fim do CN) antes de repassar;
   - o Emissor recebe em `POST /api/v1/emitentes/{cnpj}/certificado` (multipart `arquivo` e `senha`, escopo próprio sem `admin`, lacuna L-h) e confere o CNPJ de novo; o thumbprint e a validade devolvidos aparecem na tela;
   - troque o certificado direto no painel do Emissor e use **Conferir no Emissor Fiscal**: a tela mostra "O certificado no Emissor Fiscal mudou em {data}" e os dados novos (o `GET /me` fica em cache por 1 h).
4. **Vencimento**: o recorrente `fiscal-certificate-expiry` roda às 08:00 de São Paulo (fila `fiscal`). Para conferir sem esperar, dispare-o pelo painel do Hangfire e veja o aviso em 30, 15 e 7 dias aos membros do Proprietário e o item "Certificado digital vencendo" na pauta; vencido, sai o alerta `fiscal_integration`.
5. **Produção** só na GOL-01b: com `catalog.tenants.fiscal_environment_allowed = Production`, todos os campos críticos da NF-e conferidos e o código do autenticador no diálogo "Ligar a emissão com valor fiscal". Sem a marca, a tela responde "A emissão desta empresa só liga na virada para a produção.".
6. O `.pfx` e a senha nunca aparecem em log, auditoria, outbox ou resposta; confira no Seq que o envio só registra "Certificado digital do tenant {id} trocado, válido até {data}".
