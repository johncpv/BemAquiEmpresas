# Runbook: integração com o EmissorFiscal (FIS-01, ADR 0027)

O BemAqui fala com o EmissorFiscal (`D:\GIT\EmissorFiscal`, implantado pelo PO) **só** pelo `IFiscalEmitter`, pela **API pública em HTTPS** `https://api.fiscal.bemaquiapp.com.br` (ADR 0027, adendo de 27/10/2026). O Emissor é um SaaS multi-cliente e o BemAqui é só mais um cliente: não há rede Docker privada entre os dois. Nenhum código do Emissor é copiado ou referenciado (teste de arquitetura). O certificado A1 fica no cofre do Emissor; o BemAqui guarda a chave de API (`SecretKind.FiscalApiKey`, propósito `fiscal-api-key`) e o segredo do aviso (`SecretKind.WebhookSecret`, credencial do provedor `emissor-fiscal` da FND-16), cifrados e só de escrita.

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

## 2. Endereços, limite e nginx

- **API do Emissor:** `https://api.fiscal.bemaquiapp.com.br`, a mesma para homologação e produção (o ambiente vem do cadastro do emitente, não da URL nem da chave). Fica na configuração (`Fiscal:Emissor:BaseUrl`, padrão do `appsettings.json`), conferida contra `Fiscal:Emissor:AllowedHosts` (só HTTPS, porta padrão, nunca IP ou `localhost`). A tela mostra o endereço só para leitura e a API recusa outro. A API do BemAqui sai para a internet pela `stack_stack`, como já faz para o SMTP.
- **Trocar o endereço** (só se o Emissor mudar de host): `Fiscal__Emissor__BaseUrl` e `Fiscal__Emissor__AllowedHosts__0` no compose (o `check-compose.mjs` exige HTTPS). Toda integração salva com o endereço antigo fica **Aguardando o teste** até o Proprietário clicar em **Testar conexão**; nenhuma nota sai antes disso. Foi o que aconteceu com o `http://emissor-fiscal:8080` da versão 1.x.
- **Limite:** o Emissor aceita 10 requisições por segundo por IP (e o limite da chave); acima, 429. O BemAqui manda no máximo `Fiscal:Emissor:MaxRequestsPerSecond` (padrão **4**) por processo, com fila, e pausa todas as chamadas pelo `Retry-After` de um 429. Homologação e produção saem do mesmo IP da VPS: 4 + 4 = 8. Se outro sistema da VPS (o Dolibarr) também chamar o Emissor pelo mesmo IP, baixe para 3 nos dois ambientes.
- **Aviso (webhook):** o Emissor só entrega em HTTPS pública. O endereço é o do **host da plataforma**, um por tenant:

  | Ambiente | Endereço do aviso (cadastrar no painel do Emissor) |
  |---|---|
  | Homologação (`demo`) | `https://plataforma-homolog.bemaquiapp.com.br/webhooks/emissor-fiscal/{tenantKey}` |
  | Produção (`konebras`, na GOL-01b) | `https://plataforma.bemaquiapp.com.br/webhooks/emissor-fiscal/{tenantKey}` |

  O `{tenantKey}` é opaco e próprio de cada tenant: copie o endereço inteiro da tela **Configurações → Integrações → Emissor fiscal** (aparece depois de salvar). O host vem de `Webhooks__PublicBaseUrl` (`https://${PLATFORM_HOST}` no compose). No host do tenant (`homolog.konebras.com.br`) e no host interno da API (`bqe-homolog-api`) a rota `/webhooks/` **não existe** (404), de propósito: o `tenantKey` só vale no host da plataforma (ADR 0031).

### Location do nginx (no `stack-nginx`, feito pelo PO na VPS)

No server `plataforma-homolog.bemaquiapp.com.br` (e no da produção, com `bqe-prod-api`), a allowlist de IP do painel **não pode** valer para `/webhooks/`. No `deploy/nginx/bemaqui-empresas-homolog.conf` o `if ($bqe_homolog_platform_allowed = 0) { return 404; }` está no nível do server e barra tudo; ele vai para dentro das locations do painel, e a `/webhooks/` entra sem ele:

```nginx
server {
    server_name plataforma-homolog.bemaquiapp.com.br;
    # listen, ssl, resolver e set $bqe_api/$bqe_web como hoje; SEM o "if" da allowlist no nível do server.

    # Webhooks (FND-16, ADR 0031; EmissorFiscal, ADR 0027 adendo de 27/10/2026): fora da allowlist, só GET/POST, corpo pequeno.
    location /webhooks/ {
        limit_except GET POST { deny all; }
        client_max_body_size 64k;
        proxy_pass $bqe_api;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $remote_addr;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_read_timeout 30s;
    }

    location ~ ^/(api|health|hangfire)(/|$) {
        if ($bqe_homolog_platform_allowed = 0) { return 404; }
        # proxy_pass $bqe_api e cabeçalhos como hoje
    }

    location / {
        if ($bqe_homolog_platform_allowed = 0) { return 404; }
        # proxy_pass $bqe_web e cabeçalhos como hoje
    }
}
```

- `proxy_set_header Host $host` é obrigatório: a API só aceita `/webhooks/` no host da plataforma. O `X-Forwarded-For $remote_addr` mantém o rate limit por IP real (política `webhooks`, 60 por minuto por IP e provedor). O timeout de entrega do Emissor é 15 s; a API responde em milissegundos (só grava).
- O log `bqe_masked` já grava só o caminho; a API mascara o `tenantKey` no log dela.
- Conferir depois do `nginx -t` e do reload, de um IP **fora** da allowlist: `curl -s -o /dev/null -w '%{http_code}\n' -X POST -H 'Content-Type: application/json' -d '{}' https://plataforma-homolog.bemaquiapp.com.br/webhooks/emissor-fiscal/AAAAAAAAAAAAAAAAAAAAAA` tem de dar **401** (chegou na API e a assinatura foi recusada). 404 quer dizer que a location não está fora da allowlist.
- A rede `bqe-fiscal` antiga pode ser apagada na VPS depois do deploy desta versão: `docker network rm bqe-fiscal` (se nenhum container estiver nela).

## 3. Configurar um tenant

1. No painel do Emissor: criar o tenant, o emitente (CNPJ do tenant) e a `ClientApplication` com os escopos da tabela. Copiar a chave (aparece uma vez).
2. No BemAqui, como Proprietário: **Configurações → Integrações → Emissor fiscal → Configurar**: a chave e, por enquanto, um segredo provisório (o endereço do Emissor já vem do sistema); **Salvar integração fiscal** (pede o código do autenticador).
3. A tela mostra o **Endereço do aviso** (`https://plataforma-homolog.bemaquiapp.com.br/webhooks/emissor-fiscal/{tenantKey}` na homologação). No painel do Emissor: **Webhooks → Novo webhook** com esse endereço e todos os eventos (ou ao menos `nfe.autorizada`, `nfe.rejeitada`, `nfe.cancelada` e `nfe.denegada`). Copiar o segredo que o Emissor mostra e colar em **Trocar o segredo** no BemAqui.
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
EMISSOR_HOMOLOG_URL=https://api.fiscal.bemaquiapp.com.br EMISSOR_HOMOLOG_KEY=<chave do demo> \
  sh scripts/test-docker.sh --filter-class "*EmissorHomologContractTests"
```

(roda de qualquer lugar, pela API pública; **nunca** com a chave do `konebras`). Confere: `GET /me` sem `admin` e com os escopos; emitente só em homologação; prévia aceita `tipoOperacao` 1 (saída) e 0 (entrada) (lacuna L-l).

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
| L-i exposição | Por fora, só `/api/` e `/health` (sem `/api/v1/admin/*` e sem Swagger); 10 req/s por IP com 429 e `Retry-After` | Documentado (ADR 007 de lá). A rede dedicada saiu (ADR 0027, adendo de 27/10/2026) |
| L-j tenant por tenant | Uma `ClientApplication` por tenant do BemAqui, sem `admin` | Procedimento acima |
| L-l `tipoOperacao` | `0` entrada, `1` saída | Documentado; conferir pela suíte `emissor-homolog` |
| L-b, L-d, L-h, L-k, L-m | IBS/CBS, `transp`, A1 sem `admin`, cópia offline da chave-mestra, NFS-e de Caçapava | Antes da FIS-03a, FIS-02a, GOL-01a e FIS-06 |
| Webhook de outro tenant | O corpo traz `tenantId` do Emissor: o BemAqui ignora aviso de outro tenant | Documentado |

## 8. Problemas comuns

| Sintoma | Onde olhar |
|---|---|
| "O Emissor Fiscal recusou a chave." | Chave errada, desativada ou de outro cliente: gere outra no Emissor e troque |
| "O Emissor Fiscal não respondeu." | `docker exec bqe-homolog-api wget -qO- https://api.fiscal.bemaquiapp.com.br/health` (DNS e saída pela `stack_stack`); situação do Emissor com o time dele |
| "Muitas chamadas ao Emissor Fiscal." | 429 do Emissor ou fila cheia no BemAqui (log "sem vez no limite de chamadas"): confira `Fiscal__Emissor__MaxRequestsPerSecond` nos dois ambientes e se outro sistema usa o mesmo IP |
| Aviso não chega (nada em "Eventos recebidos") | No painel do Emissor, o webhook está "Desativado por falhas" (10 falhas seguidas)? O endereço tem o host da plataforma e o `tenantKey` certo? O `curl` do §2 dá 401? Se der 404, falta a location `/webhooks/` fora da allowlist |
| Aviso recusado (401) em "Eventos recebidos" não aparece | Segredo diferente nos dois lados ou relógio da VPS fora (janela de 5 min): `timedatectl` |
| Nota parada em "esperando a SEFAZ" | A conferência roda a cada 10 min; depois de 2 h em contingência sai alerta. Não emita de novo |

## 9. Configuração fiscal e certificado digital (FIS-02a)
A FIS-02a foi provada contra o fake do Emissor (`FakeFiscalEmitter`, testes de integração). A validação real fica para o tenant `demo`, em homologação, com o Emissor já homologado:

1. **Configurações → Fiscal** (`/app/configuracoes/fiscal`): confira que a tela abre com os valores do seed `fiscal-defaults` (Lucro Presumido, CRT 3, série 2, CFOP 5101/5101/6101/6107/5102/6102, ICMS 18/12/7/4%, PIS 0,65%, COFINS 3%, item 7.02, ISS 5%, IBS/CBS com CST 000 e cClassTrib 000001, CBS 0,9%, IBS estadual 0,1% e IBS municipal 0% (dados do emitente passados pelo time do Emissor em 27/10), 24 h, frete 0, "VOLUME") e todos com "A confirmar com a contadora". Em homologação, nada bloqueia.
2. **Baixar para a contadora**: o PDF sai com os 41 campos, o valor, a situação e a coluna da resposta. É o roteiro da conversa com a contadora (P-01, P-02, P-03, série, códigos da NFS-e de Caçapava).
3. **Certificado digital** (`/app/configuracoes/fiscal/certificado`), com a integração do `demo` testada:
   - envie o A1 de teste do `demo` com o código do autenticador; o BemAqui confere chave privada, política A1 da ICP-Brasil (`2.16.76.1.2.1.*`), validade e o CNPJ do titular (`otherName` 2.16.76.1.3.3 ou o fim do CN) antes de repassar;
   - o Emissor recebe em `POST /api/v1/emitentes/{cnpj}/certificado` (multipart `arquivo` e `senha`, escopo próprio sem `admin`, lacuna L-h) e confere o CNPJ de novo; o thumbprint e a validade devolvidos aparecem na tela;
   - troque o certificado direto no painel do Emissor e use **Conferir no Emissor Fiscal**: a tela mostra "O certificado no Emissor Fiscal mudou em {data}" e os dados novos (o `GET /me` fica em cache por 1 h).
4. **Vencimento**: o recorrente `fiscal-certificate-expiry` roda às 08:00 de São Paulo (fila `fiscal`). Para conferir sem esperar, dispare-o pelo painel do Hangfire e veja o aviso em 30, 15 e 7 dias aos membros do Proprietário e o item "Certificado digital vencendo" na pauta; vencido, sai o alerta `fiscal_integration`.
5. **Produção** só na GOL-01b: com `catalog.tenants.fiscal_environment_allowed = Production`, todos os campos críticos da NF-e conferidos e o código do autenticador no diálogo "Ligar a emissão com valor fiscal". Sem a marca, a tela responde "A emissão desta empresa só liga na virada para a produção.".
6. O `.pfx` e a senha nunca aparecem em log, auditoria, outbox ou resposta; confira no Seq que o envio só registra "Certificado digital do tenant {id} trocado, válido até {data}".
7. A virada do emitente para produção é feita no cadastro do emitente no Emissor (sem trocar URL nem chave); no BemAqui, além disso, vale o passo 5.
