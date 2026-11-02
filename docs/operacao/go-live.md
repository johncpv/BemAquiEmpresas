# Runbook: go-live da Konebras (GOL-01a e GOL-01b)

Runbook único da virada: critérios de entrada, checklist antes da primeira operação fiscal e bancária, ambiente de produção novo, segredos, passo a passo do dia, rollback, ensaio, Dolibarr, treinamento, comunicados e métricas. Base: ADR 0034 (com o adendo de 02/11/2026), ADRs 0004, 0005 (D1), 0015, 0017, 0020, 0027 (adendo de 27/10/2026: API pública, sem rede `bqe-fiscal`) e 0032.

| Campo | Valor |
|---|---|
| Data da virada | **02/11/2026 (segunda-feira)**, das 20:16 às 20:20 UTC (17:16 às 17:20 em São Paulo), decidida pelo PO no mesmo dia (dispensou a janela "fora do horário comercial" combinada antes, critério 1l). As datas que estavam em estudo (01/12 e 15–16/11) não valem mais |
| Hora H | Parada da API para a virada (20:16 UTC). Os horários do §12 estão em UTC; os do resto do runbook, em São Paulo |
| Responsáveis | **PO** (John): decisões, Go/No-go, cofre, Konebras. **Ops**: quem opera a VPS pelo SSH (o PO ou alguém indicado por ele). **Financeiro** e **Proprietário** da Konebras: conferências. **Contadora**: trilha fiscal (§2) |
| Situação | **Virada executada em 02/11/2026** pela forma do item 7 abaixo (a homologação virou a produção); registro no §12. GOL-01b em andamento: primeira semana, checklist do §2, cópia do Dolibarr fora da VPS e DNS |

**Decisões do PO de 02/11/2026** (aplicadas aqui):
1. **A contadora e o Inter não seguram o Go.** Regime, CNAE, IBS/CBS, Configurações → Fiscal e regras fiscais, e a conta e a credencial de produção do Inter são configurados **com o sistema já no ar e antes das primeiras operações** (§2). O ponto sem retorno continua sendo **a primeira NF-e real ou o primeiro boleto real**.
2. **Fase 8 fora do Go** (POR-*, WPP-01): o critério 1a passa a ser "Fases 0 a 7 concluídas".
3. **2FA de novo por ação** (`RequireRecentTwoFactor` da D6) e **estorno com aprovação** saem do Go e voltam à revisão depois do go-live. O critério 1f fica informativo.
4. **Backup B2 ligado no ambiente de produção novo já na GOL-01a**, para o critério de 7 dias de backup verificado estar cumprido antes da data.
5. **Data da virada em aberto** (recomendada 01/12; alternativa 15–16/11).
6. **EmissorFiscal publicado em 02/11/2026** (imagem `be1253e`), com o XML do evento (L-n), o XML da inutilização, as lacunas de numeração, o provider de Caçapava e o bloco `transporte` (L-d). Detalhe em [emissor-fiscal.md](emissor-fiscal.md) §7.
7. **A homologação virou a produção** (02/11/2026, à noite; ADR 0034, adendo de 02/11/2026 à noite; volta à forma original da ADR 0020):
   - **sem ambiente novo, sem promoção, sem recifração e sem cópia de arquivos:** o `konebras` continua no banco, no catálogo e no key ring da stack `bemaqui-empresas-homolog` (`$HM`), que agora serve `app.konebras.com.br`;
   - o **ambiente novo** (`deploy/prod/`, PRs #175 e #176; §3, §5.4, §5.5 recifração, §5.7 e §7 deste runbook) passa para a **fase 2, depois do go-live**, junto com o **B2**;
   - **backup:** a política de hoje, dump do `catalog` e do `tenant_konebras` antes de cada deploy (`scripts/deploy-homolog.sh`, [deploy.md](deploy.md) seção do topo);
   - **Dolibarr e Konebras UI desligados e removidos da VPS** no mesmo dia, a pedido do PO, com backup final e teste de restauração (§8 e §12), no lugar do "só leitura em `adm.`";
   - as marcas `fiscal_environment_allowed = 'None'` e `bank_production_allowed = false` ficam até o checklist do §2 (contadora e Inter). Até lá, **NF-e fora do sistema** e **boletos como antes**;
   - `cliente.konebras.com.br` fica reservado para o portal do cliente da Fase 8.

Convenções dos comandos:
```bash
P=/opt/projetos/bemaqui-empresas            # ambiente novo da fase 2 (ADR 0034 §2); NÃO está em uso
HM=/opt/projetos/bemaqui-empresas-homolog   # PRODUÇÃO desde 02/11/2026 (app.konebras.com.br)
T=$(date -u +%Y%m%dT%H%M%SZ)                # carimbo de cada passo
# nginx: SEMPRE "docker exec stack-nginx nginx -t" antes de "docker exec stack-nginx nginx -s reload".
```
Os scripts marcados com † ainda não existem: estão sendo escritos em outra frente. A lista completa fica em [§13](#13-a-confirmar-com-a-implementação).

---

## 1. Critérios de entrada (go/no-go)

Cada linha precisa de evidência (link do PR, registro, print ou relatório), data e responsável. **Situação:** `Pendente`, `OK`, `Não se aplica` ou `Informativo`. O "Go" exige todas as linhas obrigatórias em `OK`.

| # | Critério | Evidência esperada | Data | Responsável | Situação |
|---|---|---|---|---|---|
| 1a | **Fases 0 a 7 concluídas**: todas as stories `Done` no backlog (Fase 4 inteira, sem exceção; Fases 6 e 7 inteiras) e o PR de reforço da D19 no ar. **A Fase 8 (POR-*, WPP-01) não segura o Go** (decisão de 02/11) | Link do `05-backlog.md` na `main` com as fases marcadas; PR da D19 | | PO | Pendente |
| 1b | **EmissorFiscal de produção pronto para o BemAqui**: versão publicada (`be1253e`, 02/11/2026) com as lacunas da [emissor-fiscal.md §7](emissor-fiscal.md#7-o-que-conferir-no-emissorfiscal-lacunas-da-adr-0027-5) conferidas na homologação (`demo`); cópia offline da chave-mestra do cofre e teste de restauração (L-k); um tenant e uma `ClientApplication` do Emissor para o `konebras`, sem `admin`; **certificado A1 válido por mais de 60 dias no cofre do Emissor** (já instalado, decisão de 31/10) | Suíte `emissor-homolog` registrada; tabela da §7 com a situação; tela do certificado (validade, titular, thumbprint) | | PO | Pendente |
| 1c | _Movido para o [§2](#2-antes-da-primeira-operação-fiscal-e-bancária)_ (Configurações → Fiscal e regras fiscais conferidas com a contadora) | — | 02/11/2026 | PO | Não se aplica |
| 1d | **Homologação fiscal no `demo`**: os 3 cenários SEFAZ (FIS-03a) e os eventos (FIS-03b) autorizados em homologação, com o emitente de teste. A conferência dos cenários pela contadora e a NFS-e de Caçapava vão para o §2 | Tabelas das §10 e §11 de [emissor-fiscal.md](emissor-fiscal.md) preenchidas | | PO | Pendente |
| 1e | _Movido para o §2_ (conta e credencial de produção do Inter, teste de conexão, fluxo no sandbox) | — | 02/11/2026 | PO | Não se aplica |
| 1f | **Informativo:** D6 (2FA de novo por ação e estorno com `finance.approve`, P-19 e P-20) **fora do Go** (decisão de 02/11); volta à revisão depois do go-live, como os itens adiados de 20/10. Registrar aqui o que já existe (ex.: 2FA de novo ao salvar credenciais do Inter e do A1, ao pagar e estornar a OP, BNK-01a) | Lista do que já está no ar | | PO | Informativo |
| 1g | **Estoque:** contagem física feita e saldo inicial lançado na janela de implantação (EST-01, D7 da Fase 3), com o relatório de diferenças aceito | Relatório de diferenças assinado | | Almoxarifado + PO | Pendente |
| 1h | **Pessoas:** usuários do chão de fábrica com e-mail da empresa (P-24); todo usuário real com convite aceito e 2FA quando exigido (ADR 0010); **folha do mês anterior lançada pela RH-02c** no BemAqui. A virada da folha calculada (FLH-06) é operação e não segura o go-live | Lista de usuários (sem e-mail no runbook, só a contagem); folha do mês | | RH + PO | Pendente |
| 1i | **Backup e volta:** backup diário do **ambiente novo** (B2 + `age`, §3.6) ligado na GOL-01a e verificado nos **últimos 7 dias**; backup da homologação (dado real) verificado nos últimos 7 dias; teste de restauração do `konebras` e **rollback ensaiados e cronometrados** numa cópia (§7) | Listagem dos buckets com data e tamanho; registro do §7; linha em [restauracao.md](restauracao.md) | 02/11/2026 | PO | **Substituído pelo PO (02/11):** o backup B2 de 7 dias vai para a fase 2; vale a política de hoje (dump antes de cada deploy). O ensaio numa cópia foi trocado pela **virada no próprio lugar, com os dumps de antes e o rollback guardado** (§12) |
| 1j | **Operação:** monitor externo da produção (`https://app.konebras.com.br/health/ready`, [monitoramento.md](monitoramento.md)) criado e pausado até a virada; SPF/DKIM/DMARC do domínio de envio; inventário de segredos completo (§4); scripts de acessos, do Dolibarr e de conferência ensaiados (§7) | Print do monitor; resultado do teste de DNS de e-mail; §4 preenchido | | Ops | Pendente |
| 1k | **Métrica:** linha de base do PRD §1.2 medida, inclusive o desvio da data prevista ≤ 15% nos projetos em andamento (§11) | Tabela do §11 preenchida | 02/11/2026 | PO | **Depois da virada** (decisão de 02/11): medir na primeira semana |
| 1l | **Data:** virada no início de um mês, fora do horário comercial, janela aprovada pela Konebras: **[data da virada, a definir pelo PO]** | Aceite da Konebras (e-mail ou mensagem) | 02/11/2026 | PO | **Dispensado pelo PO:** virada em 02/11/2026, segunda-feira, às 17:16 de São Paulo (horário comercial), sem a janela combinada antes |
| 1m | **Ambiente de produção novo** pronto (§3): compose e vhost aprovados (`check-compose.mjs`, `nginx -t` local), catálogo novo, key ring e certificado novos, segredos novos | Saída dos checks; §4 com datas | 02/11/2026 | PO | **Não se aplica:** a homologação virou a produção; o ambiente novo vai para a fase 2 |
| 1n | **Treinamento** por grupo feito no `demo`, com a lista de presença (§9); **comunicados** aprovados pelo PO 5 dias úteis antes (§10) | §9 e §10 preenchidos | 02/11/2026 | PO | **Depois da virada** (decisão de 02/11): treinamento e comunicados com o sistema no ar (o `demo` não existe na stack de produção) |

**Go/No-go do PO**

| Decisão | Data e hora | Data da virada confirmada | Ressalvas | Assinatura |
|---|---|---|---|---|
| **Go**, pela forma do item 7 do topo (a homologação virou a produção) | 02/11/2026 | 02/11/2026 | Critérios 1i, 1k, 1l, 1m e 1n dispensados ou substituídos pelo PO (acima); os outros ficam como estão, sem evidência registrada aqui | John (PO) |

---

## 2. Antes da primeira operação fiscal e bancária

Feito **com o sistema já em produção** (depois da virada) e **antes** da primeira NF-e, NFS-e ou boleto real. Enquanto este checklist não fecha, o `konebras` fica com `fiscal_environment_allowed = 'None'` e `bank_production_allowed = false`: emitir nota, boleto, registrar webhook do Inter e importar extrato pela API respondem 409, e o resto do sistema funciona normalmente.

**Ponto sem retorno:** a primeira NF-e autorizada em produção **ou** o primeiro boleto real emitido pelo BemAqui (o que vier primeiro). Depois dele, o rollback do §6 só com decisão expressa do PO e plano próprio.

### 2.1 Fiscal

| # | Item | Evidência | Data | Responsável | Situação |
|---|---|---|---|---|---|
| F1 | **P-01 regime tributário** respondido pela contadora | Resposta registrada em `06-pendencias-riscos.md` | | Contadora + PO | Pendente |
| F2 | **P-02 CNAE de fabricação** no cartão CNPJ (sem ele não há NF-e de produção própria) | Cartão CNPJ atualizado | | Konebras + Contadora | Pendente |
| F3 | **P-03 IBS/CBS** (CST, `cClassTrib`, alíquotas, `IndCodOpe` da NFS-e) | Resposta registrada | | Contadora | Pendente |
| F4 | **Configurações → Fiscal** sem nenhum campo crítico com o chip "A confirmar com a contadora" (regime, CRT, série exclusiva, CFOP/CST padrão, alíquotas, código de serviço da NFS-e, ISS e retenções, IBS/CBS; FIS-02a) | PDF "Baixar para a contadora" com a coluna de resposta preenchida | | Contadora + Financeiro | Pendente |
| F5 | **Regras fiscais** (FIS-02b) conferidas e ativas | Print de Fiscal → Regras fiscais | | Contadora | Pendente |
| F6 | **3 cenários de homologação** (critério 1d) conferidos pela contadora | Coluna "Conferido pela contadora" de [emissor-fiscal.md](emissor-fiscal.md) §10 | | Contadora | Pendente |
| F7 | **NFS-e de Caçapava**: usuário e códigos de contribuinte da prefeitura (e-Transparência/CONAM) cadastrados no emitente do `konebras` no Emissor; escopo **`nfse:emitir`** na chave; o `GET /me` mostra Caçapava em `nfseMunicipios`; uma NFS-e de homologação (ou a primeira real acompanhada) conferida | `GET /me` no "Testar conexão"; registro da NFS-e | | PO + Contadora | Pendente |
| F8 | **Emitente em produção** no Emissor (cadastro do emitente, sem trocar URL nem chave) e **integração fiscal do `konebras`** configurada: chave nova sem `admin` (`nfe:emitir`, `nfe:eventos`, `consultar`, `nfse:emitir`), webhook no host de produção (`https://plataforma.bemaquiapp.com.br/webhooks/emissor-fiscal/{tenantKey}`), segredo do aviso colado, **Testar conexão** aprovado | Tela da integração "Ativa"; `curl` do aviso dando 401 ([emissor-fiscal.md](emissor-fiscal.md) §2) | | Proprietário + PO | Pendente |
| F9 | **Marca de produção fiscal** ligada pelo super admin: `fiscal_environment_allowed = 'Production'` (a "fiscal_production_allowed" da story, nome real da coluna pelo adendo de 27/10 da ADR 0027), depois o diálogo "Ligar a emissão com valor fiscal" com o código do autenticador | Comando abaixo + print do diálogo | | PO (super admin) | Pendente |
| F10 | **Primeira NF-e real** (e a primeira NFS-e) com o PO acompanhando: autorizada pela SEFAZ, XML e DANFE em Documentos, e-mail ao cliente. **Ponto sem retorno** | Chave e protocolo (sem dado do destinatário) | | Financeiro + PO | Pendente |

```bash
# F9: no Postgres da produção, catálogo (super admin; registrar a hora aqui)
docker exec -i bqe-prod-postgres psql -U postgres -d catalog -v ON_ERROR_STOP=1 \
  -c "UPDATE catalog.tenants SET fiscal_environment_allowed = 'Production' WHERE key = 'konebras' RETURNING key, fiscal_environment_allowed;"
```

### 2.2 Bancário (Inter)

| # | Item | Evidência | Data | Responsável | Situação |
|---|---|---|---|---|---|
| B1 | **Conta PJ do Inter com API de produção** (P-08) | Confirmação do Inter | | Konebras + PO | Pendente |
| B2 | **Fluxo do sandbox no `demo`** (BNK-01b, BNK-02, BNK-03a, BNK-03b), se ainda não foi feito: boleto, webhook, baixa com reconsulta, extrato, conciliação ([banco-inter.md](banco-inter.md), "Pré-condição externa") | Itens 1 a 7 do runbook do Inter registrados | | PO | Pendente |
| B3 | **Credencial de produção só com cobrança, extrato e webhook**, salva em Produção na conta do `konebras` (Configurações → Integrações → Banco Inter, 2FA) e **"Testar conexão" aprovado** (token + leitura do saldo, sem efeito). Credencial que paga ou envia Pix é recusada | Tela com o último teste OK | | Proprietário + PO | Pendente |
| B4 | **Marca de produção bancária** ligada pelo super admin: `bank_production_allowed = true` | Comando abaixo | | PO (super admin) | Pendente |
| B5 | **Webhook do Inter** registrado no host de produção (BNK-02), com o token novo; aviso de teste chegando em "Eventos recebidos" | Print | | Proprietário + PO | Pendente |
| B6 | **Extrato** importado pela API e **saldo da conta igual ao Internet Banking** | Conferência do dia | | Financeiro | Pendente |
| B7 | **Primeiro boleto real** com o PO acompanhando: aparece no Internet Banking; a baixa, quando paga, chega pelo webhook com reconsulta mTLS. **Ponto sem retorno** | "Seu número" e data | | Financeiro + PO | Pendente |

```bash
# B4: no Postgres da produção, catálogo
docker exec -i bqe-prod-postgres psql -U postgres -d catalog -v ON_ERROR_STOP=1 \
  -c "UPDATE catalog.tenants SET bank_production_allowed = true WHERE key = 'konebras' RETURNING key, bank_production_allowed;"
```

**Até este checklist fechar** (a decidir pelo PO, ver dúvidas): a NF-e sai pelo emissor que a Konebras usa hoje, e a cobrança segue como hoje; o que sair fora do BemAqui é registrado nele quando houver tela para isso (a NFS-e já tem "Registrar NFS-e emitida na prefeitura").

---

## 3. Ambiente de produção novo

> **Fase 2 (02/11/2026):** esta seção, o §4 (inventário do ambiente novo), o §5.4, a recifração do §5.5, o §5.7 e o §7 **não foram usados** na virada: a homologação virou a produção (item 7 do topo). Ficam como plano do ambiente novo, depois do go-live, junto com o B2. O que foi feito de fato está no §12.

ADR 0034 §2: a produção nasce **nova**. Só o dado do banco do `konebras` passa, recifrado. Nada da homologação é reaproveitado: nem key ring, nem certificado, nem senha, nem sessão.

| Item | Produção | Homologação (fica para testes) |
|---|---|---|
| Diretório | `/opt/projetos/bemaqui-empresas/` | `/opt/projetos/bemaqui-empresas-homolog/` |
| Projeto do compose | `bqe-prod` (`deploy/prod/docker-compose.yml`) | `bqe-homolog` |
| Containers | `bqe-prod-api`, `bqe-prod-web`, `bqe-prod-postgres`, `bqe-prod-clamav`, `bqe-prod-migrator`, `bqe-prod-fiscal-pdf-reader`, `bqe-prod-backup` | `bqe-homolog-*` |
| Volumes | `bqe-prod-pgdata`, `bqe-prod-files`, `bqe-prod-clamdb`, `bqe-prod-pdf-socket` | `bqe-homolog-*` |
| Hosts | `app.konebras.com.br` (tenant), `plataforma.bemaquiapp.com.br` (super admin, Hangfire, webhooks) | `homolog.konebras.com.br` (deixa de servir o `konebras`), `plataforma-homolog.bemaquiapp.com.br`, host do `demo` |
| Vhost | `deploy/prod/nginx/bemaqui-empresas.conf` → `/opt/infra/nginx/conf.d/bemaqui-empresas.conf` | `bemaqui-empresas-homolog.conf` |
| Catálogo | **novo, só com o `konebras`** | catálogo atual, com o `demo` e os de teste |
| Key ring / certificado DP | **novos** | novos depois da virada (GOL-01b CA 11) |
| Buckets | `bqe-prod-dumps`, `bqe-prod-keyring` (chaves próprias, sem `deleteFiles`) | `bqe-homolog-dumps`, `bqe-homolog-keyring` |
| EmissorFiscal | mesmo SaaS, `https://api.fiscal.bemaquiapp.com.br`, tenant e chave próprios do `konebras` (ADR 0027, adendo de 27/10) | tenant e chave do `demo` |
| Pipeline | `deploy-prod.yml` (tag `v*`, `vars.PROD_DEPLOY`, environment `production` com aprovação do PO) | `deploy-homolog.yml` |

### 3.1 Preparação da VPS (GOL-01a, uma vez)

| Passo | Comando | Resp. | Tempo | Verificação |
|---|---|---|---|---|
| Diretórios | `install -d -m 0755 $P $P/postgres $P/golive && install -d -m 0700 $P/secrets $P/predeploy-dumps` | Ops | 2 min | `ls -ld $P/secrets` mostra `drwx------` |
| Arquivos | `cp deploy/prod/docker-compose.yml deploy/prod/deploy.sh deploy/prod/backup-cron.sh $P/ && cp deploy/postgres/init-roles.sh $P/postgres/ && cp deploy/prod/.env.example $P/.env && chmod 0600 $P/.env && chmod 0755 $P/*.sh $P/postgres/init-roles.sh` | Ops | 2 min | `docker compose -f $P/docker-compose.yml config -q` sem erro |
| Scripts da virada | `install -m 0700 scripts/golive/* $P/golive/` † | Ops | 1 min | `ls $P/golive` |
| `.env` | `TENANT_HOST=app.konebras.com.br`, `PLATFORM_HOST=plataforma.bemaquiapp.com.br`, alertas, SMTP, `IMAGE_TAG=<tag v* ou sha>` | Ops | 5 min | Sem segredo no `.env` (só hosts e nomes) |
| CI local | `node scripts/check-compose.mjs` e o `nginx -t` do vhost de produção (variante do `scripts/check-nginx.sh` para `deploy/prod/nginx/`) † | Dev | 5 min | Os dois passam |
| DNS | `plataforma.bemaquiapp.com.br` e `adm.konebras.com.br`: A → `2.25.82.127`, DNS-only (nuvem cinza). `app.konebras.com.br` já aponta para a VPS | PO | 10 min | `dig +short` devolve o IP |

### 3.2 Segredos novos
Gere todos de novo (nenhum vem da homologação) em `$P/secrets`, `chmod 0400`, dono = uid do container (api/migrator `1654`, postgres `70`, backup `10001`). Senha sem quebra de linha: `openssl rand -hex 24 | tr -d '\n' > arquivo`. Lista completa no §4.

```bash
cd $P/secrets
for f in pg-superuser-password pg-catalog-password pg-provisioner-password; do openssl rand -hex 24 | tr -d '\n' > $f; done
openssl rand -base64 48 | tr -d '\n' > auth-signing-key
cp pg-superuser-password backup-pg-password
printf 'Host=bqe-prod-postgres;Database=catalog;Username=bemaqui_catalog;Password=%s' "$(cat pg-catalog-password)" > catalog-connection
printf 'Host=bqe-prod-postgres;Database=postgres;Username=bemaqui_provisioner;Password=%s' "$(cat pg-provisioner-password)" > provisioner-connection
chown 70:70 pg-*-password && chown 1654:1654 catalog-connection provisioner-connection auth-signing-key && chown 10001:10001 backup-pg-password
chmod 0400 *
# smtp-password, dp-cert.pfx, dp-cert-password, backup-age-recipients e dumps-/keyring-s3-*: ver §3.3 e §3.6.
```
Conferência: `find $P/secrets -type f ! -perm 0400` não lista nada; `ls -ln $P/secrets` com os donos certos.

### 3.3 Key ring e certificado do Data Protection novos
1. Na estação do PO, gere `dp-cert.pfx` de produção (RSA 3072, 10 anos) pelo procedimento de [segredos-e-chaves.md](segredos-e-chaves.md) "Primeiro provisionamento", com `-subj "/CN=BemAqui Empresas DataProtection (prod)"`. Cópia offline no cofre: item "BemAqui Empresas: certificado Data Protection (prod)".
2. Copie `dp-cert.pfx` e `dp-cert-password` para `$P/secrets` (0400, dono 1654).
3. Suba o banco e crie o catálogo e o key ring **uma vez**:
   ```bash
   cd $P
   docker compose up -d bqe-prod-postgres bqe-prod-clamav
   ALLOW_KEY_RING_CREATION=true docker compose run --rm migrator     # log: "Key ring do Data Protection pronto"
   docker compose run --rm migrator                                    # de novo, sem a flag: sobe sem aviso
   docker compose run --rm migrator platform-admin create --email <e-mail do super admin>   # link uma vez, 24 h
   ```
4. **Catálogo novo, só com o `konebras`:** nenhum `tenant provision` na produção. A linha do `konebras` entra na promoção (§5.4), com o **mesmo id** da homologação (o id entra no propósito dos segredos), e é a única.

Verificação: `docker exec bqe-prod-postgres psql -U postgres -d catalog -Atc "select count(*) from security.data_protection_keys"` ≥ 1; `select count(*) from catalog.tenants` = 0 até a promoção.

### 3.4 Recifração (ADR 0034 §2)
O banco do `konebras` traz segredos cifrados com o key ring da **homologação** (conexão do tenant no catálogo, SMTP, TOTP dos usuários, chave de IA, chave do Emissor e segredos de webhook, credenciais do Inter de sandbox, se houver). Regra:

| Segredo | O que fazer |
|---|---|
| Conexão do tenant (`catalog.tenants.connection_secret`) | **Nova**: role `tenant_konebras` com senha nova (§5.4), cifrada com o key ring novo |
| `tenantKey` dos webhooks (`catalog.tenants.webhook_key`) | **Novo** (ADR 0034 §3), cadastrado de novo no Emissor e no Inter |
| TOTP dos usuários, SMTP do tenant, chave de IA | **Recifrados**: decifrados com o key ring antigo num passo isolado e cifrados com o novo (`reencrypt-secrets` †) |
| Chave do Emissor, segredo do aviso do Emissor, Inter (sandbox ou produção), tokens de webhook | **Não migram**: são apagados/recadastrados (o `konebras` não tinha nada fiscal nem bancário real, ADRs 0027 e 0032) |

Comando (forma prevista, a confirmar):
```bash
# key ring e certificado ANTIGOS montados só neste passo, só leitura, apagados logo depois
docker compose run --rm -v $P/golive/old:/run/old:ro migrator \
  reencrypt-secrets --tenant konebras --old-keyring /run/old/keyring.dump \
  --old-cert /run/old/dp-cert.pfx --old-cert-password-file /run/old/dp-cert-password --dry-run
# o mesmo sem --dry-run; depois:
shred -u $P/golive/old/* && rmdir $P/golive/old
```
(no container, o comando equivale a `dotnet BemAqui.Host.dll reencrypt-secrets`). Verificação: o relatório lista, por tipo, quantos segredos foram recifrados, apagados e falharam (falha = 0); um segredo de cada tipo decifra com o key ring novo; **sem** o key ring antigo montado nada falha (a API sobe e o login com 2FA funciona).

### 3.5 Endurecimento
O compose de produção segue a ADR 0015 (rede `internal` sem saída, postgres sem `ports:`, não-root, `read_only` + `tmpfs`, `cap_drop: ALL`, `no-new-privileges`, limites, nomes únicos `bqe-prod-*` na `stack_stack`) e o adendo de 27/10 da ADR 0027 (sem rede `bqe-fiscal`; `Fiscal__Emissor__BaseUrl` só HTTPS; `Webhooks__PublicBaseUrl=https://plataforma.bemaquiapp.com.br`). Tudo conferido pelo `check-compose.mjs`.

### 3.6 Backup B2 + `age` (ligado já na GOL-01a)
1. PO cria os buckets `bqe-prod-dumps` e `bqe-prod-keyring` no B2, regra de ciclo de vida de 30 dias, uma chave de aplicação por bucket **sem `deleteFiles`**.
2. Chave do backup **nova** para produção: `age-keygen -o bqe-prod-backup.key` na estação do PO; a privada no cofre, só a linha `age1…` vai para `$P/secrets/backup-age-recipients`.
3. Secrets `dumps-s3-{endpoint,bucket,access-key,secret-key}` e `keyring-s3-*` (dono 10001, 0400).
4. Cron do root:
   ```
   30 3 * * *  /opt/projetos/bemaqui-empresas/backup-cron.sh daily   >> /var/log/bqe-prod-backup.log 2>&1
   45 3 * * 0  /opt/projetos/bemaqui-empresas/backup-cron.sh keyring >> /var/log/bqe-prod-backup.log 2>&1
   ```
5. Rode uma vez à mão (`$P/backup-cron.sh daily`), liste o bucket e **tente apagar** um objeto com a chave do backup: o B2 tem de recusar.

Verificação diária até a virada (critério 1i): o objeto `catalog-*.dump.age` do dia em `bqe-prod-dumps/prod/<AAAA-MM-DD>/`, com tamanho coerente. Depois da promoção entra o `tenant_konebras-*.dump.age`.

### 3.7 nginx e certificados (preparação; a troca é no §5.7)
- `plataforma.bemaquiapp.com.br`: emitir o certificado já na GOL-01a pelo certbot existente (volume `stack_certbot-etc`, webroot `/var/www/certbot`), com o bootstrap do vhost.
- `app.konebras.com.br`: o certificado atual (do Dolibarr) continua valendo; é o mesmo host.
- `adm.konebras.com.br`: emitir o certificado antes do D−1, se ainda não existir.
- Allowlist do painel da plataforma: `/opt/infra/nginx/conf.d/bemaqui-empresas.allow` com os IPs do PO. A `location /webhooks/` fica **fora** da allowlist ([emissor-fiscal.md](emissor-fiscal.md) §2).

---

## 4. Inventário de segredos (produção)

**Sem nenhum valor.** "Arquivo 0400" = `$P/secrets/<nome>`, `chmod 0400`. "Cofre" = cofre de senhas do PO. "Cifrado no banco" = gravado pela tela, cifrado com o key ring de produção, só de escrita. Preencher a data de criação no dia em que for gerado.

| Segredo | Local | Dono (uid) | Responsável | Criado em | Rotação |
|---|---|---|---|---|---|
| Senha do superuser `postgres` (`pg-superuser-password`, `backup-pg-password`) | Arquivo 0400 + cofre | 70 / 10001 | Ops | | Anual ou na suspeita (`ALTER ROLE postgres PASSWORD …` + os dois arquivos) |
| Senhas dos roles `bemaqui_catalog` e `bemaqui_provisioner` (+ `catalog-connection`, `provisioner-connection`) | Arquivo 0400 | 70 / 1654 | Ops | | Anual (`ALTER ROLE`) |
| Senha do role `tenant_konebras` | Cifrado no banco (catálogo, `connection_secret`) | — | Ops | | Na suspeita (`ALTER ROLE` + recifrar a conexão) |
| Assinatura do JWT (`auth-signing-key`) | Arquivo 0400 | 1654 | Ops | | Anual (derruba todas as sessões) |
| SMTP/SES (`smtp-password`) | Arquivo 0400 | 1654 | PO | | Conforme o provedor; na suspeita |
| Certificado Data Protection (`dp-cert.pfx`, `dp-cert-password`) | Arquivo 0400 + **cópia offline no cofre** | 1654 | PO | | 10 anos (aviso 180 dias antes); troca ainda não suportada |
| Key ring | Catálogo (`security.data_protection_keys`) + trilha semanal no `bqe-prod-keyring` | — | Ops | | Automática a cada 90 dias; **nunca revogar** |
| Chave privada do backup (`age`) | **Só no cofre** (a pública em `backup-age-recipients`, 0400, 10001) | — | PO | | Na suspeita (backups antigos continuam com a antiga) |
| B2 dumps e keyring (`dumps-s3-*`, `keyring-s3-*`) | Arquivo 0400 | 10001 | PO | | Anual; chave sem `deleteFiles` |
| Chave SSH de deploy (comando restrito) | Privada no GitHub (environment `production`, `PROD_SSH_KEY`); pública no `authorized_keys` com `command="/opt/projetos/bemaqui-empresas/deploy.sh",restrict` | — | PO | | Anual |
| `PROD_KNOWN_HOSTS` | GitHub (environment `production`) | — | PO | | Quando a chave do host mudar |
| Token do GHCR (`read:packages`) | `docker login` do root na VPS | — | PO | | Validade do PAT (anotar) |
| Environment protection do `deploy-prod.yml` | GitHub: environment `production` com aprovação obrigatória do PO | — | PO | | Revisar no go-live e a cada 6 meses |
| Chave da API do EmissorFiscal do `konebras` (sem `admin`) | Cifrado no banco (só pela tela; nenhuma cópia, decisão de 31/10) | — | Proprietário | | Na suspeita ou anual (§4 de [emissor-fiscal.md](emissor-fiscal.md)) |
| Segredo do aviso do Emissor | Cifrado no banco (credencial do provedor `emissor-fiscal`) | — | Proprietário | | Na suspeita (rotação com 24 h de sobreposição) |
| Chave-mestra do cofre do Emissor | Lado do Emissor + cópia offline (L-k) | — | PO (Emissor) | | Conforme o Emissor |
| Certificado A1 | Cofre do Emissor (o BemAqui guarda só validade, titular e thumbprint) | — | Proprietário | | Anual (aviso 30, 15, 7 dias) |
| Inter: client id, client secret, certificado mTLS | Cifrado no banco, por conta | — | Proprietário | | Anual (certificado); aviso 30, 15, 7, 1 dia |
| Token do webhook do Inter e `tenantKey` | Cifrado no banco / catálogo (só o hash do token) | — | Proprietário | | Na suspeita |
| Segredos TOTP e códigos de recuperação dos usuários | Cifrado no banco (recifrados na promoção) | — | Cada usuário | | Ao trocar o aparelho |
| Chave de IA (se usada) | Cifrado no banco | — | Proprietário | | Na suspeita |
| `secrets/dolibarr-api` (URL, `DOLAPIKEY` antiga, CNPJ) | Arquivo 0600 em `$HM/secrets` **só até a carga final**; apagado no §5.3 | 1654 | Ops | | Uso único |
| `DOLAPIKEY` nova do Dolibarr | Não é guardada (ninguém usa depois da virada) | — | PO | | — |
| Autenticação básica do `adm.konebras.com.br` (se escolhida no lugar da allowlist) | `/opt/infra/nginx/conf.d/adm-konebras.htpasswd` (0640, root:nginx) + cofre | — | PO | | Na saída de quem conhece |
| WhatsApp (WPP-01) | Fora do Go (Fase 8) | — | — | — | — |

Conferências do inventário (CA 5): `find $P/secrets -type f ! -perm 0400` vazio; nenhum arquivo de `$P/secrets` tem o mesmo hash de um de `$HM/secrets` (`sha256sum $P/secrets/* $HM/secrets/* | sort | uniq -D -w64` vazio).

---

## 5. Passo a passo (D−5 a D+1)

> **02/11/2026:** este passo a passo era o da virada pelo ambiente novo. A virada real seguiu um roteiro mais curto, na própria stack da homologação (§12): dumps, carga final em simulação, API parada, JWT trocado, acessos, `tenant_domains`, vhosts e API recriada.

**Tempo total estimado da noite da virada (H até o smoke): cerca de 3 h**, a confirmar no ensaio (§7). Cada passo executado é registrado no §12 (hora, quem, resultado).

### 5.1 D−5 úteis e D−1

| Passo | Comando / ação | Resp. | Tempo | Verificação |
|---|---|---|---|---|
| D−5 Comunicados | Enviar o aviso aos usuários e o e-mail aos clientes aprovados (§10), pela fila do BemAqui ou pelo e-mail da Konebras | PO | 30 min | Quantidade enviada e falhas (sem a lista de destinatários) |
| D−5 Congelar deploys | Nenhum deploy na homologação do D−1 até o fim da virada, salvo correção combinada | PO | — | Registro |
| D−1 Ambiente novo | `cd $P && docker compose ps` e o backup diário da noite | Ops | 5 min | `bqe-prod-postgres` saudável; dump do dia no bucket |
| D−1 Backups extras | Dolibarr: dump do MariaDB e cópia dos documentos, cifrados com `age`; BemAqui: `$HM/backup-cron.sh daily` à mão | Ops | 20 min | Objetos com data e tamanho no bucket |
| D−1 Certificados | `docker exec stack-nginx sh -c 'for h in app.konebras.com.br adm.konebras.com.br plataforma.bemaquiapp.com.br; do openssl x509 -enddate -noout -in /etc/letsencrypt/live/$h/fullchain.pem; done'` | Ops | 5 min | Todos com mais de 30 dias |
| D−1 A1 | Tela do certificado no Emissor (validade) | PO | 2 min | Mais de 60 dias |
| D−1 Imagem | Tag `v*` da `main` escolhida e imagens no GHCR; `IMAGE_TAG` no `$P/.env` | PO | 10 min | `docker compose pull` sem erro |
| D−1 Lembrete | Lembrete do e-mail aos clientes | PO | 10 min | Registro |
| D−1 Inter (opcional) | Se a credencial de produção já existir: "Testar conexão" no D+0 ou depois (§2 B3). **Não adia mais a virada** (decisão de 02/11) | PO | — | — |

### 5.2 Hora H: congelamento

| Passo | Comando / ação | Resp. | Tempo | Verificação |
|---|---|---|---|---|
| H.1 Avisar | Mensagem curta aos usuários: "Dolibarr e BemAqui em pausa até a volta em app.konebras.com.br" | PO | 2 min | — |
| H.2 Dolibarr só leitura | `sh $P/golive/dolibarr-fechamento.sh congelar` †: perfis sem escrita (o usuário da `DOLAPIKEY` continua lendo pela API para a carga final) | Ops | 10 min | Um usuário de teste não consegue salvar nada; registrar a **última hora de lançamento** |
| H.3 Konebras UI fora | Parar o Konebras UI (`erp.konebras.com.br`) no diretório dele (`/opt/projetos/konebras-ui/deploy`): `docker compose stop` | Ops | 2 min | `curl -s -o /dev/null -w '%{http_code}' https://erp.konebras.com.br/` ≠ 200 |
| H.4 Homologação do `konebras` em pausa | No `bemaqui-empresas-homolog.conf`, o server de `homolog.konebras.com.br` passa a `return 503;` (cópia `.bak-$T` guardada), `nginx -t`, reload. O `demo` segue no host dele | Ops | 5 min | `curl -s -o /dev/null -w '%{http_code}' https://homolog.konebras.com.br/` = 503 |

### 5.3 Carga final do Dolibarr (MIG-01b/c, na homologação, antes do dump)
A carga roda no banco do `konebras` da homologação; o dump do §5.4 já leva o resultado. Só cadastros alterados depois da última carga ([migracao-dolibarr.md](migracao-dolibarr.md) §2, §4 e §6).

| Passo | Comando | Resp. | Tempo | Verificação |
|---|---|---|---|---|
| 3.1 Simulação MIG-01b | `cd $HM && docker compose --profile import run --rm importer tenant import-dolibarr --tenant konebras --confirm-tenant konebras --dolibarr-api /run/secrets/dolibarr-api --entities payment-terms,bank-accounts,parties,contacts --operator <email> --dry-run` | Ops | 5 min | Saída 0; totais iguais à leitura |
| 3.2 Simulação MIG-01c | o mesmo com `--entities units,item-categories,locations,items,supplier-codes` (e `--mapping` se houver) | Ops | 5 min | Saída 0 |
| 3.3 Relatório | PO confere o relatório (Configurações → Importação do Dolibarr ou `run-<id>.csv`): atualizados, novos, **conflitos** | PO | 15 min | Aceite registrado; conflitos resolvidos à mão |
| 3.4 Carga real | 3.1 e 3.2 sem `--dry-run` | Ops | 10 min | Saída 0; mesmo relatório |
| 3.5 Expurgo | `docker compose run --rm migrator tenant import-dolibarr --purge-staging --tenant konebras --accepted` | Ops | 2 min | `import_items` vazio |
| 3.6 Fechar o Dolibarr | `sh $P/golive/dolibarr-fechamento.sh fechar` †: `DOLAPIKEY` regenerada e usuários dela desativados; `DROP USER IF EXISTS 'bqe_migracao_ro'@'localhost'` com a revogação na rede (o caminho padrão foi a API, então o usuário pode não existir) | Ops | 10 min | Chamada com a chave antiga dá 401; `SELECT user FROM mysql.user WHERE user='bqe_migracao_ro'` vazio |
| 3.7 Apagar o secret | `shred -u $HM/secrets/dolibarr-api` (e `dolibarr-ro.cnf`, se o snapshot foi usado) | Ops | 1 min | `ls $HM/secrets/dolibarr*` sem resultado |

### 5.4 Promoção do banco do `konebras` para o ambiente novo
Tudo dentro da VPS: o dump não sai dela e não vai em claro para lugar nenhum fora de `$P/predeploy-dumps` (0700), apagado depois do primeiro backup diário de produção.

| Passo | Comando | Resp. | Tempo | Verificação |
|---|---|---|---|---|
| 4.1 Dump final | `docker exec bqe-homolog-postgres pg_dump -U postgres -Fc tenant_konebras > $P/predeploy-dumps/tenant_konebras-$T.dump && chmod 0600 $P/predeploy-dumps/*` | Ops | 5 min | `docker exec -i bqe-homolog-postgres pg_restore -l < $P/predeploy-dumps/tenant_konebras-$T.dump > /dev/null` sem erro |
| 4.2 Key ring antigo (só para a recifração) | `install -d -m 0700 $P/golive/old && docker exec bqe-homolog-postgres pg_dump -U postgres -Fc -d catalog -t security.data_protection_keys --data-only > $P/golive/old/keyring.dump && cp $HM/secrets/dp-cert.pfx $HM/secrets/dp-cert-password $P/golive/old/` | Ops | 2 min | Arquivos 0400, dono 1654 |
| 4.3 Suspender o `konebras` na homologação | `docker exec -i bqe-homolog-postgres psql -U postgres -d catalog -c "UPDATE catalog.tenants SET status = 'Suspended' WHERE key = 'konebras';"` (jobs e e-mails param de rodar sobre o banco antigo) | Ops | 1 min | Log da homologação sem jobs do `konebras` |
| 4.4 Role e banco na produção | Bloco "4.4" abaixo: role `tenant_konebras` com **senha nova**, banco com ele de dono, `REVOKE CONNECT FROM PUBLIC` | Ops | 3 min | `\l tenant_konebras` com o dono certo |
| 4.5 Restaurar | `docker exec -i bqe-prod-postgres pg_restore -U postgres --exit-on-error -d tenant_konebras < $P/predeploy-dumps/tenant_konebras-$T.dump` | Ops | 5 min | Saída 0 |
| 4.6 Linha do catálogo | Criar o `konebras` no catálogo novo com o **mesmo id**, chave, razão social, documento, plano e módulos liberados, conexão nova (com `tenant-pw`) cifrada com o key ring novo, `tenantKey` novo, `fiscal_environment_allowed = 'None'`, `bank_production_allowed = false` † (parte da `reencrypt-secrets` ou comando próprio, a confirmar) | Ops | 5 min | `select key, status, fiscal_environment_allowed, bank_production_allowed from catalog.tenants` = 1 linha |
| 4.7 Arquivos do tenant | Copiar do volume `bqe-homolog-files` para `bqe-prod-files` **só** os arquivos referenciados pelo `konebras` (`shared.attachments.storage_path` e os outros donos de arquivo de sistema: XML, holerites, relatórios) † | Ops | 10 min | Conferência abre 3 anexos ao acaso; nenhum arquivo do `demo` copiado |

Bloco 4.4 (a senha vai pelo stdin, nunca por argumento, como em [migracao-dolibarr.md](migracao-dolibarr.md) §3):
```bash
umask 077; openssl rand -hex 24 | tr -d '\n' > $P/golive/tenant-pw
{ printf "CREATE ROLE tenant_konebras LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE PASSWORD '%s';\n" "$(cat $P/golive/tenant-pw)"
  echo "CREATE DATABASE tenant_konebras OWNER tenant_konebras;"
  echo "REVOKE CONNECT ON DATABASE tenant_konebras FROM PUBLIC;"; } \
  | docker exec -i bqe-prod-postgres psql -U postgres -d postgres -v ON_ERROR_STOP=1
```

### 5.5 Recifração, migrator e acessos

| Passo | Comando | Resp. | Tempo | Verificação |
|---|---|---|---|---|
| 5.1 Recifração | §3.4 (`reencrypt-secrets` †, primeiro `--dry-run`); depois `shred -u $P/golive/old/* $P/golive/tenant-pw` | Ops | 10 min | Falhas = 0; arquivos antigos apagados |
| 5.2 Migrator | `cd $P && docker compose run --rm migrator` | Ops | 5 min | Saída 0; relatório com o `konebras` na versão do build |
| 5.3 Subir | `docker compose up -d --no-deps --wait --wait-timeout 180 bqe-prod-fiscal-pdf-reader bqe-prod-api bqe-prod-web` | Ops | 3 min | Containers `healthy` |
| 5.4 Acessos | `sh $P/golive/acessos-virada.sh` † (ou `acessos-virada.sql` pelo `psql`): revoga **todas** as famílias de refresh, sobe o security stamp de todos, expira convites, redefinições, PINs, aparelhos e quiosques/telas da TV, apaga usuários e convites de teste e lista os super admins | Ops | 5 min | Contagens na saída; o PO confere a lista de super admins; um refresh e um JWT salvos antes (do ensaio ou da homologação) dão 401 |
| 5.5 Conferência | `docker exec -i -e PGOPTIONS='-c default_transaction_read_only=on' bqe-prod-postgres psql -U postgres -d tenant_konebras -At -v ON_ERROR_STOP=1 < $P/golive/conferencia.sql > $P/golive/conferencia-$T.txt` † | Ops + Financeiro | 10 min | Igual ao relatório do ensaio e ao Dolibarr congelado (§7.3); nenhuma nota de homologação nem boleto de sandbox |

### 5.6 Troca de domínio

| Passo | Comando | Resp. | Tempo | Verificação |
|---|---|---|---|---|
| 6.1 `tenant_domains` (produção) | `docker exec -i bqe-prod-postgres psql -U postgres -d catalog -v ON_ERROR_STOP=1 -c "DELETE FROM catalog.tenant_domains WHERE host <> 'app.konebras.com.br'; INSERT INTO catalog.tenant_domains (id, tenant_id, host, is_primary, verified_at) SELECT gen_random_uuid(), id, 'app.konebras.com.br', true, now() FROM catalog.tenants WHERE key = 'konebras' ON CONFLICT (host) DO UPDATE SET is_primary = true;"` (se a linha do 4.6 já vier com o host, só confere) | Ops | 2 min | `select host, is_primary from catalog.tenant_domains` = só `app.konebras.com.br`, primário |
| 6.2 `tenant_domains` (homologação) | `docker exec -i bqe-homolog-postgres psql -U postgres -d catalog -c "DELETE FROM catalog.tenant_domains WHERE host = 'homolog.konebras.com.br';"` | Ops | 1 min | `homolog.konebras.com.br` dá 404 depois do 6.4 |
| 6.3 Guardar o vhost atual | `cp -p /opt/infra/nginx/conf.d/<vhost do Dolibarr em app.>.conf /opt/infra/nginx/conf.d/<…>.conf.bak-$T` (fora do `conf.d`, ou com extensão que o nginx não carrega) | Ops | 2 min | Arquivo guardado (é o rollback) |
| 6.4 Vhosts novos | `install -m 0644 deploy/prod/nginx/bemaqui-empresas.conf /opt/infra/nginx/conf.d/` (app. e plataforma.); vhost `adm.konebras.com.br` do Dolibarr com allowlist de IP ou autenticação básica (feito pelo `dolibarr-fechamento.sh` † ou à mão); retirar o server de `app.` do vhost do Dolibarr; `homolog.konebras.com.br` volta do 503 para o normal | Ops | 10 min | — |
| 6.5 Certificados | Se faltar algum: `certbot certonly --webroot -w /var/www/certbot -d <host>` no container do certbot (volume `stack_certbot-etc`) | Ops | 5 min | `openssl x509 -enddate` |
| 6.6 Testar e recarregar | `docker exec stack-nginx nginx -t && docker exec stack-nginx nginx -s reload` (falhou: volta os arquivos e **não** recarrega) | Ops | 2 min | `nginx -t` ok |
| 6.7 Dolibarr em `adm.` | `$dolibarr_main_url_root='https://adm.konebras.com.br'` no `conf.php` do Dolibarr | Ops | 5 min | `adm.` abre o login do Dolibarr só da allowlist (ou com a senha); fora dela, 403/401 |

### 5.7 Pipeline de produção

| Passo | Ação | Resp. | Tempo | Verificação |
|---|---|---|---|---|
| 7.1 Chave de deploy | `ssh-keygen -t ed25519 -f bqe-prod-deploy -N ''`; linha `command="/opt/projetos/bemaqui-empresas/deploy.sh",restrict ssh-ed25519 … bqe-prod-deploy` no `authorized_keys` do root | PO | 5 min | `ssh` com a chave só roda o `deploy.sh` |
| 7.2 GitHub | Environment `production` com aprovação obrigatória do PO, secrets `PROD_SSH_KEY` e `PROD_KNOWN_HOSTS`; variáveis `PROD_SSH_HOST` e `PROD_DEPLOY=true` | PO | 10 min | Um deploy de teste fica esperando aprovação |
| 7.3 Monitor | Liga o monitor externo de `https://app.konebras.com.br/health/ready`; troca o da homologação para o host do `demo` | PO | 5 min | Primeira checagem verde |
| 7.4 Alertas | `Alerts__Recipients__*` e `Alerts__PlatformBaseUrl=https://plataforma.bemaquiapp.com.br` no compose | Ops | — | Um `alert raise` de teste chega |

### 5.8 Smoke

| Passo | Comando / ação | Resp. | Tempo | Verificação |
|---|---|---|---|---|
| 8.1 Fumaça | `sh scripts/smoke.sh app.konebras.com.br` | Ops | 3 min | "Fumaça ok" |
| 8.2 Dolibarr em `adm.` | De um IP da allowlist: `curl -s -L https://adm.konebras.com.br/ \| grep -qi dolibarr`; de fora: 403 (ou 401 sem a senha) | Ops | 3 min | Conforme |
| 8.3 Homologação | `curl -s -o /dev/null -w '%{http_code}' https://homolog.konebras.com.br/api/v1/public/tenant-context` = 404; o `demo` responde | Ops | 2 min | Conforme |
| 8.4 Webhooks | `curl -s -o /dev/null -w '%{http_code}\n' -X POST -H 'Content-Type: application/json' -d '{}' https://plataforma.bemaquiapp.com.br/webhooks/emissor-fiscal/AAAAAAAAAAAAAAAAAAAAAA` = 401 | Ops | 1 min | 401 (404 = location presa na allowlist) |
| 8.5 Login | Proprietário entra em `https://app.konebras.com.br` com 2FA; painel abre | Proprietário | 5 min | OK |
| 8.6 Números | Saldo de cada conta = extrato do dia; totais a receber e a pagar = conferência (5.5) | Financeiro | 15 min | Iguais |
| 8.7 Sem efeito real | Nada fiscal ou bancário real hoje: marcas em `None`/`false` até o §2 | Ops | 1 min | `select fiscal_environment_allowed, bank_production_allowed from catalog.tenants` |
| 8.8 Banner | O `EnvironmentBanner` de homologação some no `konebras` | Proprietário | 1 min | OK |

### 5.9 Comunicação e D+1

| Passo | Ação | Resp. | Tempo | Verificação |
|---|---|---|---|---|
| 9.1 No ar | Aviso aos usuários: "o BemAqui está em app.konebras.com.br; entre de novo com o seu e-mail e o autenticador" (todas as sessões foram derrubadas) | PO | 10 min | — |
| 9.2 Reaparelhar | Refazer PINs, aparelhos do chão de fábrica e telas da TV (expirados no 5.4) | Supervisão | 1 h | Aparelhos entram |
| 9.3 Primeiro dia útil | Acompanhamento de 30 min com o Financeiro e o PO (GOL-01b CA 7); **decisão de manter ou voltar até o fim do primeiro dia útil** (§6) | PO | 30 min | Registro no §12 |
| 9.4 Limpeza | Depois do primeiro backup diário de produção com o `tenant_konebras`: `shred -u $P/predeploy-dumps/*` | Ops | 2 min | Diretório vazio |
| 9.5 Homologação | Key ring novo na homologação (GOL-01b CA 11); o banco do `konebras` dela é apagado depois do primeiro fechamento mensal em produção | Ops | — | Registro em [restauracao.md](restauracao.md) |
| 9.6 Antes da primeira operação | Abrir o §2 (fiscal e bancário) | PO | — | — |

**Ponto sem retorno:** não acontece na noite da virada. É a primeira NF-e autorizada ou o primeiro boleto real (§2, F10 e B7). Até lá, e só até o fim do primeiro dia útil, o rollback do §6 está disponível.

---

## 6. Rollback

> **Depois da virada de 02/11/2026:** a volta é pelos **dumps de antes da virada** (`catalog` e `tenant_konebras`, §12) e pelo vhost antigo do Dolibarr (`default.conf`), guardado fora do `conf.d`. Como o **Dolibarr foi desligado e removido** da VPS no mesmo dia, voltar para ele exige antes restaurar o backup final (`/opt/backups/dolibarr-final-20261102T202934Z`, restauração já testada) e os volumes guardados (`stack_dolidb`, `stack_dolidocs`); os passos R1 a R7 abaixo supõem o Dolibarr ainda no ar e servem só de roteiro.

**Prazo para decidir:** até o **fim do primeiro dia útil** depois da virada (18:00). **Quem decide:** o PO. **Tempo alvo:** menos de 30 min (medido no ensaio, §7). Depois do ponto sem retorno, não há rollback automático.

| Passo | Comando / ação | Resp. | Tempo |
|---|---|---|---|
| R1 Exportar o que foi lançado | `docker exec -i -e PGOPTIONS='-c default_transaction_read_only=on' bqe-prod-postgres psql -U postgres -d tenant_konebras -v desde="'<hora H em UTC>'" < $P/golive/conferencia.sql` † (seção "lançado depois da virada": títulos, baixas, pagamentos, movimentos de estoque, OS, compras), salvo em `$P/golive/rollback-$T.txt` (0600) | Ops | 5 min |
| R2 BemAqui fora de `app.` | `docker compose -f $P/docker-compose.yml stop bqe-prod-api bqe-prod-web` (dados preservados no volume) e retirar `bemaqui-empresas.conf` do `conf.d` | Ops | 3 min |
| R3 Dolibarr de volta em `app.` | Restaurar o `.bak-$T` do 6.3; `$dolibarr_main_url_root='https://app.konebras.com.br'`; `nginx -t` e reload | Ops | 5 min |
| R4 Descongelar | `sh $P/golive/dolibarr-fechamento.sh descongelar` †: perfis com escrita de volta. **A `DOLAPIKEY` antiga não é reativada**; o Konebras UI só volta com a chave nova, se o PO quiser | Ops | 5 min |
| R5 Conferir | `curl -s -L https://app.konebras.com.br/ \| grep -qi dolibarr`; login de um usuário do Dolibarr | Ops | 3 min |
| R6 Avisar | Mensagem aos usuários: "voltamos ao Dolibarr; relancem os itens da lista" | PO | 5 min |
| R7 Relançar | O Financeiro relança no Dolibarr a lista do R1 | Financeiro | — |

**O que fica de fora do rollback:**
- **Nota fiscal autorizada não volta:** fica no EmissorFiscal (e na SEFAZ); é registrada no Dolibarr como referência. Por isso a primeira NF-e é o ponto sem retorno.
- **Boleto emitido no Inter:** fica no Inter; cancelar pelo Internet Banking ou acompanhar a baixa à mão.
- **Homologação:** o `konebras` continua suspenso lá; voltar a usá-la exige decisão do PO (o banco de lá parou na hora do dump).
- Nova tentativa de virada: novo Go do PO, com o runbook corrigido.

---

## 7. Ensaio (GOL-01a CA 7 a 11)

**Sempre numa cópia restaurada**, num ambiente separado (máquina local com Docker ou um projeto de compose `bqe-ensaio` com rede própria); **nunca na homologação em uso** e nunca no ambiente de produção. A cópia é descartada no fim e o exercício entra em [restauracao.md](restauracao.md).

### 7.1 Montagem
1. Restaurar o backup real do dia (catálogo e `tenant_konebras` da homologação) num "ambiente antigo" de ensaio ([restauracao.md](restauracao.md)), com o `dp-cert` da homologação.
2. Montar um "ambiente novo" de ensaio com `deploy/prod/docker-compose.yml`, key ring e certificado **de ensaio** (nunca os de produção), host de ensaio (ex.: `ensaio.localhost`).
3. Cópia do Dolibarr (MariaDB + arquivos do backup do D−1) para o script do Dolibarr; a carga em `--dry-run` lê o Dolibarr real só por GET.

### 7.2 Roteiro e tempos

| Passo do runbook | Tempo medido | Resultado | Observação |
|---|---|---|---|
| §5.3 carga final em `--dry-run` | | | |
| §5.4 promoção (dump, role, restauração, linha do catálogo, arquivos) | | | |
| §5.5 recifração, migrator, acessos | | | |
| §5.6 `tenant_domains` para o host de ensaio | | | |
| §5.8 smoke | | | |
| Decifrar um segredo de cada tipo com o key ring novo | | | |
| **Sem** o key ring antigo: nada deixa de decifrar | | | |
| Refresh e JWT emitidos antes do script de acessos dão 401 | | | |
| Script do Dolibarr na cópia (`congelar`, `fechar`, `descongelar`) | | | |
| **Rollback (§6) cronometrado** | | | Meta: < 30 min |
| **Total** | | | |

### 7.3 Conferência dos dados (`conferencia.sql` †, só leitura, sem dado pessoal)

| Item | Ensaio | Fonte de comparação | Igual? |
|---|---|---|---|
| Saldo de cada conta bancária | | Extrato do banco no dia (conciliação fechada) | |
| Total a receber em aberto | | Relatório do BemAqui na homologação / Dolibarr congelado | |
| Total a pagar em aberto | | Idem | |
| Títulos da folha do mês | | RH-02c | |
| Saldo do estoque por local | | Contagem física (EST-01) | |
| Nº de terceiros, itens, contratos, projetos | | Homologação no mesmo dia | |
| Notas fiscais de homologação no tenant | **0** | — | |
| Boletos de sandbox no tenant | **0** | — | |

Falhou alguma coisa: corrigir o runbook (ou abrir story) e ensaiar de novo antes do Go.

| Data do ensaio | Ambiente | Origem do backup | Tempo total | Rollback | Resultado | Quem |
|---|---|---|---|---|---|---|
| _(pendente)_ | | | | | | |

---

## 8. Dolibarr depois da virada

> **02/11/2026, cerca de 20:30 UTC:** a pedido do PO, o Dolibarr e o Konebras UI foram **desligados e removidos** da VPS logo depois da virada, no lugar do "só leitura em `adm.`" (GOL-01b CA 9). O desligamento da tabela abaixo (GOL-01b CA 10) foi antecipado, com backup final e teste de restauração; **falta a cópia de 5 anos fora da VPS**. Registro no §12. As linhas "Só leitura", "`DOLAPIKEY`", "`bqe_migracao_ro`" e "Consulta do histórico" perderam o objeto: o Dolibarr não existe mais na VPS (o histórico fica no backup final).

| Etapa | O que | Quando | Como | Verificação |
|---|---|---|---|---|
| Só leitura | Perfis sem escrita; `adm.konebras.com.br` com **allowlist de IP ou autenticação básica** no vhost | Hora H / §5.6 | `dolibarr-fechamento.sh congelar` † + vhost | Fora da allowlist: 403; nenhum perfil salva |
| `DOLAPIKEY` | Regenerada; usuários que a usavam desativados (é a mesma do Konebras UI) | §5.3, depois da carga final | `dolibarr-fechamento.sh fechar` † | Chave antiga: 401 |
| `bqe_migracao_ro` | `DROP USER IF EXISTS` + regra de rede (o caminho padrão foi a API; o usuário só existe se o snapshot foi usado) | §5.3 | Idem | Consulta em `mysql.user` vazia |
| Konebras UI | Desligado de vez | Hora H | `docker compose stop` (depois `down`, mantendo o backup) | `erp.konebras.com.br` fora |
| Consulta do histórico | Fica no ar só para consulta | Até o desligamento | — | — |
| **Desligamento** | Backup final (MariaDB + documentos) **cifrado com `age`**, guardado **5 anos** (bucket ou prefixo próprio, sem a regra de 30 dias), **teste de restauração** registrado, vhost `adm.` removido, container parado | No **1º fechamento mensal** no BemAqui ou em **até 3 meses** (o que vier primeiro); data no §12 | `dolibarr-fechamento.sh desligar` † | Restauração do backup final numa máquina de teste abre o Dolibarr |

**O que veio e o que não veio do Dolibarr** (ADR 0017, [migracao-dolibarr.md](migracao-dolibarr.md) §1):

| Veio (só cadastros, MIG-01b/c, carga final na hora H) | Não veio (fica consultável em `adm.` até o desligamento) |
|---|---|
| Terceiros (clientes, fornecedores, papéis especiais), contatos, condições de pagamento, contas bancárias (sem saldo), unidades, categorias de produto, depósitos, itens (sem custo, saldo nem lote), códigos de fornecedor | Faturas, pagamentos e recebimentos, movimentos bancários, saldos e movimentos de estoque, lotes, custo médio, orçamentos, pedidos, OS, BOM/OF, projetos, usuários (convidados no BemAqui), funcionários, plano contábil, IBAN/BIC, preços, anexos |

**Como entram os saldos e o que está em aberto** (já no banco promovido, não na carga final):
- **Contas a receber e a pagar em aberto:** pela implantação financeira (FIN-00) na homologação, com dado real desde então (ADR 0020). Na virada, só a conferência (§7.3): totais comparados com o Dolibarr congelado; diferença vira ajuste com aprovação.
- **Saldos bancários:** saldo inicial na data de corte (CAD-05b) mais os movimentos desde então; na virada, saldo de cada conta = extrato do dia, com a conciliação fechada.
- **Estoque:** saldo inicial da contagem física (EST-01, `OpeningBalance`), critério 1g.
- **Contratos e projetos em andamento:** COM-04c, já na homologação.

---

## 9. Treinamento por grupo

No tenant **`demo`** (nunca no `konebras`), com o material de [docs/apresentacao/](../apresentacao/README.md): o [roteiro](../apresentacao/roteiro-apresentacao.md) para a visão geral, os [grupos e permissões](../apresentacao/grupos-e-permissoes.md) para quem configura acessos e o [manual de cada papel](../apresentacao/manual-usuario/). Cada grupo faz a rotina do dia a dia no `demo` e as dúvidas ficam registradas. O 2FA de novo é treinado só onde já existe (ex.: salvar credenciais, pagar e estornar a OP).

| Grupo | Material | O que praticar | Duração | Data | Instrutor |
|---|---|---|---|---|---|
| Proprietário | [proprietario.md](../apresentacao/manual-usuario/proprietario.md), roteiro completo, grupos e permissões | Painel, aprovar OP e contrato, grupos e membros, Configurações (Fiscal, Integrações), 2FA | 2 h | | PO |
| Financeiro | [financeiro.md](../apresentacao/manual-usuario/financeiro.md) | CR/CP, OP, conciliação, fluxo de caixa, cobrança, fechamento do dia | 2 h | | PO |
| Comercial | [comercial.md](../apresentacao/manual-usuario/comercial.md) | Orçamento → aprovação → contrato e projeto | 1 h | | PO |
| Engenharia | [fabrica-supervisor.md](../apresentacao/manual-usuario/fabrica-supervisor.md) (partes de BOM e roteiro) + atos de Engenharia do roteiro (não há manual próprio) | BOM, roteiros, etapa de projeto das OS | 1 h | | PO |
| Supervisão | [fabrica-supervisor.md](../apresentacao/manual-usuario/fabrica-supervisor.md) | Kanban, OS, capacidade, qualidade, painel da TV, aparelhos e PINs | 1,5 h | | PO |
| Operadores | [operador.md](../apresentacao/manual-usuario/operador.md) | PWA: entrar com PIN, checklist, cronômetro, apontamento | 30 min por turma | | Supervisão |
| Almoxarifado | [compras-almoxarifado.md](../apresentacao/manual-usuario/compras-almoxarifado.md) | Recebimento, reservas, movimentos, inventário | 1 h | | PO |
| RH | [rh.md](../apresentacao/manual-usuario/rh.md) | Funcionários, ponto, folha lançada (RH-02c), holerites, exportação para a contadora | 1,5 h | | PO |
| Contadora (opcional) | [contadora.md](../apresentacao/manual-usuario/contadora.md) | Pacote mensal, Configurações → Fiscal (§2) | 1 h | | PO |

**Lista de presença** (nome e grupo; sem e-mail nem telefone):

| Data | Grupo | Participante | Presente | Dúvidas registradas |
|---|---|---|---|---|
| | | | | |

---

## 10. Comunicados (RASCUNHOS: aguardam aprovação do PO)

Aprovar até **5 dias úteis antes** da virada. Enviar no D−5 e lembrar no D−1 (GOL-01b CA 2). Registrar só a quantidade enviada e as falhas.

### 10.1 Aviso aos usuários (RASCUNHO)
> **Assunto:** Mudança de sistema: o BemAqui entra em app.konebras.com.br em [data da virada, a definir pelo PO]
>
> Olá, equipe Konebras.
>
> No dia **[data da virada, a definir pelo PO]**, o sistema da empresa muda para o **BemAqui Empresas**, no mesmo endereço: **https://app.konebras.com.br**.
>
> **O que muda**
> - A partir de **[data] às [hora H]**, o Dolibarr fica **só para consulta** e não aceita mais lançamento. Termine o que estiver lançando antes desse horário.
> - Durante a virada (até cerca de **[hora prevista de volta]**), nenhum sistema fica disponível.
> - Depois da virada, entre em **https://app.konebras.com.br** com o seu e-mail, a sua senha e o código do aplicativo autenticador. Todas as sessões antigas são encerradas: é preciso entrar de novo, inclusive nos tablets da fábrica (a supervisão refaz os PINs).
> - O histórico antigo continua no Dolibarr, para consulta, em **adm.konebras.com.br** (acesso só para quem precisar).
>
> **Onde pedir ajuda:** [nome e contato do responsável]. Cada tela do BemAqui tem o botão **Ajuda**.
>
> Obrigado,
> [assinatura]

### 10.2 E-mail aos clientes da Konebras (RASCUNHO)
> **Assunto:** Konebras Elevadores: novo sistema de faturamento a partir de [data da virada, a definir pelo PO]
>
> Prezado cliente,
>
> A partir de **[data da virada, a definir pelo PO]**, a Konebras Elevadores passa a emitir as notas fiscais e os boletos por um **novo sistema**. Para você, muda pouco:
>
> - **Boletos:** os próximos boletos serão emitidos pelo **Banco Inter**, com **Pix no próprio boleto** (QR Code). O valor, o vencimento e o beneficiário (Konebras / WSP Equipamentos por Elevação Ltda, CNPJ 47.225.005/0001-86) continuam os combinados. Boletos já recebidos antes dessa data continuam válidos.
> - **Notas fiscais:** a nota fiscal (NF-e) e, quando houver serviço, a NFS-e chegam por e-mail, enviadas pelo novo sistema, com o XML e o PDF.
> - **Na dúvida sobre um boleto**, confira o beneficiário e o CNPJ antes de pagar, ou fale conosco.
>
> **Contato:** [e-mail e telefone do financeiro da Konebras].
>
> Atenciosamente,
> Konebras Elevadores

Observação: o e-mail aos clientes só sai depois de o §2 ter data provável. Se o primeiro boleto pelo Inter ficar para depois da virada, trocar "a partir de [data da virada]" pela data da primeira cobrança pelo Inter.

---

## 11. Linha de base das métricas (PRD §1.2)

Medida antes do Go (critério 1k) e repetida 3 meses depois da virada.

| Métrica (PRD §1.2) | Meta em 3 meses | Linha de base | Como medir | Data | Quem |
|---|---|---|---|---|---|
| Projetos com orçado × realizado visível em tempo real | 100% | | Projetos em andamento com orçamento do projeto ÷ projetos em andamento | | PO |
| Pagamentos passando por OP aprovada | 100% | | Pagamentos do mês com OP aprovada ÷ pagamentos do mês | | Financeiro |
| Desvio da data de entrega prevista | ≤ 15% (projetos iniciados depois do go-live) | | Média de \|entrega real − prevista\| ÷ prazo previsto, nos projetos em andamento | | PO |
| Dolibarr desligado | Sim | Não | Data do desligamento (§8): **02/11/2026** (cópia de 5 anos fora da VPS pendente) | 02/11/2026 | PO |
| Fechamento financeiro mensal | < 1 dia de trabalho | | Horas do último fechamento | | Financeiro |

Data da primeira medição de 3 meses: **[data da virada + 3 meses]**.

---

## 12. Execução (registro da virada, GOL-01b)

Virada de **02/11/2026**, pela forma do item 7 do topo (a homologação virou a produção). Horários em **UTC**; os passos 1 a 8 couberam na janela das **20:16 às 20:20**. "Ops" = quem operou a VPS pelo SSH.

| Data e hora (UTC) | Passo | Responsável | Resultado / verificação | Desvio |
|---|---|---|---|---|
| 02/11, mesmo dia | 0. Decisão | PO | A homologação vira a produção: sem ambiente novo, sem promoção, sem recifração, sem cópia de arquivos; ambiente novo (PRs #175/#176) e B2 para a fase 2; backup pela política de hoje | Substitui o §3, o §5.4, a recifração do §5.5, o §5.7 e o §7 (ensaio numa cópia) |
| 02/11, mesmo dia | 0a. nginx da plataforma | Ops | `plataforma-homolog.bemaquiapp.com.br` com a `location /webhooks/` fora da allowlist: antes os webhooks **não chegavam à API** (o `POST` caía no front, [deploy.md](deploy.md)); corrigido no mesmo dia com o vhost atual (passo 8) | Correção fora do roteiro |
| 02/11, 20:16–20:20 | 1. Dumps antes da virada | Ops | `pg_dump -Fc` do `catalog` e do `tenant_konebras` em `$HM/predeploy-dumps/` | — |
| 02/11, 20:16–20:20 | 2. Carga final do Dolibarr (MIG-01b/c) em **simulação** | Ops + PO | **Nada novo nem alterado** desde a última carga; carga real desnecessária. Erros no dado do Dolibarr, **para o PO cadastrar à mão**: 3 contas bancárias sem código do banco (BB-CC, CAIXA, INTER; o BemAqui tem 1 conta e o PO mantém assim) e 2 terceiros com CNPJ inválido no Dolibarr (ids 10 e 14) | — |
| 02/11, 20:16–20:20 | 3. API parada | Ops | `bqe-homolog-api` parada | — |
| 02/11, 20:16–20:20 | 4. **Chave de assinatura do JWT trocada** | Ops | `secrets/auth-signing-key` novo; a antiga guardada **fora** de `secrets/` (para o rollback) | Único segredo trocado; os demais e o key ring seguem os da homologação (ADR 0034, adendo de 02/11 à noite) |
| 02/11, 20:16–20:20 | 5. `scripts/golive/acessos-virada.sql` (`tenant_konebras`) | Ops | **2** famílias de refresh / **12** refresh tokens revogados; security stamp de **2** usuários; **1** convite pendente expirado; **1** aparelho do chão de fábrica revogado; **0** usuários de teste | — |
| 02/11, 20:16–20:20 | 6. `scripts/golive/acessos-virada-catalogo.sql` (`catalog`) | Ops | Aplicado | — |
| 02/11, 20:16–20:20 | 7. `catalog.tenant_domains` | Ops | Só `app.konebras.com.br` (primário); `homolog.konebras.com.br` removido | — |
| 02/11, 20:16–20:20 | 8. nginx | Ops | `00-bemaqui-empresas-common.conf` + `bemaqui-empresas-homolog.conf` (cópia no repositório: `deploy/nginx/bemaqui-empresas-homolog.conf`): server 443 de `app.konebras.com.br` com a API e a web da homologação e o certificado do `app.`; `homolog.konebras.com.br` com **301** para o `app.`; `plataforma-homolog.bemaquiapp.com.br` sem mudança. `default.conf` (vhost antigo do Dolibarr em `app.`) **retirado do `conf.d` e guardado** para o rollback | Sem `adm.konebras.com.br` (Dolibarr desligado, passo 11) |
| 02/11, 20:20 | 9. API recriada | Ops | API de volta, já com a chave nova do JWT | — |
| 02/11, depois das 20:20 | 10. Login | PO | **Login com sucesso em `https://app.konebras.com.br`** | — |
| 02/11, depois das 20:20 | 10a. Marcas | Ops | `fiscal_environment_allowed = 'None'` e `bank_production_allowed = false` mantidas até o §2 (contadora e Inter). NF-e fora do sistema e boletos como antes até lá | — |
| 02/11, ~20:30 | 11. **Dolibarr e Konebras UI desligados e removidos** (pedido do PO) | Ops | **Backup final** (MariaDB com todos os bancos, documentos e configuração) em `/opt/backups/dolibarr-final-20261102T202934Z`, com **teste de restauração** num MariaDB descartável: **118 terceiros, 34 produtos, 26 faturas, 16 propostas**, iguais ao sistema no ar. Dados do Konebras UI arquivados em `/opt/backups/konebras-ui-final-*`. Vhosts `erp.` e `adm.` removidos; containers e imagens removidos; cron do Konebras UI removido; pastas `/opt/projetos/konebras` e `konebras-ui` apagadas. **Volumes `stack_dolidb`, `stack_dolidocs` e `konebras-ui_konebras_ui_db` mantidos** até o PO confirmar uma cópia fora da VPS (retenção de 5 anos) | Substitui o "Dolibarr só leitura em `adm.`" (GOL-01b CA 9) e antecipa o desligamento (CA 10), **menos a cópia de 5 anos fora da VPS** |

**Pendências da virada (GOL-01b, em andamento):**
- **PO:** cópia do backup final do Dolibarr (e dos volumes mantidos) **fora da VPS**, para os 5 anos; só depois os volumes podem ser apagados.
- **PO:** tirar os registros DNS de `erp.konebras.com.br` e `adm.konebras.com.br`.
- **PO:** cadastrar à mão o que a carga acusou (códigos de banco das contas, se quiser, e os 2 terceiros com CNPJ inválido, ids 10 e 14 no Dolibarr).
- Primeira semana (tabela abaixo), checklist do §2 (contadora e Inter) antes da primeira NF-e ou boleto pelo sistema, treinamento, comunicados e linha de base das métricas (§11).
- `cliente.konebras.com.br` reservado para o portal do cliente (Fase 8).

Acompanhamento da primeira semana (GOL-01b CA 7): saldo sistema × banco por conta, boletos emitidos e baixados, pagamentos a conferir, notas autorizadas e rejeitadas, webhooks com falha ou parados, jobs com falha, dead-letters, chamados.

| Dia | Saldos conferem | Boletos | Notas | Webhooks/jobs | Chamados | Problemas (dono, prazo) |
|---|---|---|---|---|---|---|
| D+1 | | | | | | |

---

## 13. A confirmar com a implementação

Hipóteses deste runbook sobre peças que outra frente está escrevendo. Conferir cada uma quando a peça chegar e corrigir o runbook.

| # | Hipótese | Onde é usada |
|---|---|---|
| 1 | `deploy/prod/docker-compose.yml` cria o projeto `bqe-prod` com os serviços `bqe-prod-postgres`, `bqe-prod-clamav`, `migrator`, `bqe-prod-fiscal-pdf-reader`, `bqe-prod-api`, `bqe-prod-web` e `backup`, os volumes `bqe-prod-*` e os secrets do §3.2; sem serviço `importer` (a carga final roda na homologação) | §3, §5 |
| 2 | `deploy/prod/deploy.sh` igual ao da homologação (SHA, pull, migrator, subida com volta), em `/opt/projetos/bemaqui-empresas/deploy.sh`, e um `deploy/prod/backup-cron.sh` com `BACKUP_ENV=prod` | §3.1, §3.6, §5.7 |
| 3 | `deploy/prod/nginx/bemaqui-empresas.conf` serve `app.konebras.com.br` e `plataforma.bemaquiapp.com.br` (log `bqe_masked`, WebSocket, `/webhooks/` fora da allowlist e mascarado), com um bootstrap para os certificados e um `nginx -t` local equivalente ao `scripts/check-nginx.sh` | §3.7, §5.6 |
| 4 | `deploy/prod/.env.example` com `TENANT_HOST`, `PLATFORM_HOST`, `IMAGE_TAG`, alertas e SMTP | §3.1 |
| 5 | `dotnet BemAqui.Host.dll reencrypt-secrets` existe e aceita o key ring e o certificado antigos só para decifrar (`--old-keyring`, `--old-cert`, `--old-cert-password-file`), por tenant, com `--dry-run` e relatório por tipo, sem mostrar valor | §3.4, §5.5 |
| 6 | Quem cria a linha do `konebras` no catálogo novo (mesmo id, módulos liberados, conexão nova, `tenantKey` novo): a `reencrypt-secrets` ou um comando próprio | §5.4 (4.6) |
| 7 | Cópia dos arquivos do `konebras` entre os volumes `bqe-homolog-files` e `bqe-prod-files` só pelos caminhos referenciados no banco do tenant (hoje o volume mistura `demo` e `konebras`, e parte dos arquivos não tem o prefixo do tenant) | §5.4 (4.7) |
| 8 | `scripts/golive/acessos-virada.sh` (ou `.sql`): refresh, security stamp, convites, redefinições, PINs, aparelhos, quiosques, usuários de teste, lista de super admins; saída só com contagens | §5.5 |
| 9 | `scripts/golive/dolibarr-fechamento.sh` com as fases `congelar`, `fechar`, `descongelar` e `desligar` (perfis sem escrita, `DOLAPIKEY`, usuários, `DROP USER IF EXISTS`, regra de rede, vhost `adm.`, backup final de 5 anos) | §5.2, §5.3, §6, §8 |
| 10 | `scripts/golive/conferencia.sql`: só leitura, sem dado pessoal, com os itens do §7.3 e uma seção "lançado depois da virada" a partir de uma variável `desde` (para o rollback) | §5.5, §6, §7.3 |
| 11 | Suspender o `konebras` na homologação (`status = 'Suspended'`) para os jobs dele, sem afetar o `demo` | §5.4 (4.3) |
| 12 | Nome da marca fiscal: `fiscal_environment_allowed = 'Production'` (a coluna real; a story fala em `fiscal_production_allowed`) | §2 F9 |
