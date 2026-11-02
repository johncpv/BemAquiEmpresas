# Runbook: segredos e chaves (Data Protection)

Este runbook é para quem opera a VPS. Ele diz o que precisa ser guardado, como restaurar e o que **não** fazer. Implementa a ADR 0007 (FND-11).

## O que existe

| Peça | Onde fica | Para que serve |
|---|---|---|
| **Key ring** | Banco de catálogo, tabela `security.data_protection_keys` | Chaves que cifram todos os segredos: conexão de cada tenant, SMTP, TOTP, chave de IA e, mais tarde, Inter e certificado fiscal |
| **Certificado de proteção** (`.pfx`, RSA de 3072 bits ou mais, com chave privada) | Secret do Docker em `/run/secrets/dp-cert.pfx` | Cifra cada chave do key ring. Sem ele, o key ring não decifra nada |
| **Senha do `.pfx`** | Outro secret, em arquivo: `/run/secrets/dp-cert-password` (`DataProtection__CertificatePasswordFile`) | Abre o `.pfx`. Não use variável de ambiente, porque ela aparece no `docker inspect` |
| **Cópia offline** | Cofre de senhas do PO, fora da VPS | Guarda o `.pfx` e a senha. **Não** guarda o key ring |

Disso saem três consequências:
- só o backup do banco não decifra nada, porque falta o certificado;
- só o certificado também não decifra nada, porque falta o key ring;
- perder os dois ao mesmo tempo é perder todos os segredos de todos os tenants.

## Configuração do Host

```
ConnectionStrings__Catalog=...
DataProtection__CertificatePath=/run/secrets/dp-cert.pfx
DataProtection__CertificatePasswordFile=/run/secrets/dp-cert-password
DataProtection__AllowKeyRingCreation=false   # true só no primeiro provisionamento
DataProtection__AutoMigrate=false            # em homolog e prod, quem migra é o migrator (ADR 0006)
```

O Host **recusa subir** nos casos abaixo. O motivo vai para o log, pelo logger de bootstrap.
- O certificado não está configurado ou o arquivo não existe.
- A senha do certificado está errada.
- O certificado não tem chave privada, ou o RSA tem menos de 3072 bits.
- O key ring está inacessível (banco fora do ar ou tabela ausente).
- O key ring não tem nenhuma chave ativa (não revogada) e `AllowKeyRingCreation=false`. É o que acontece, por exemplo, ao restaurar um backup sem as chaves.
- Alguma chave não decifra com o certificado configurado, ou seja, o certificado está errado.

O Host registra um aviso (Warning) em duas situações:
- toda subida com `AllowKeyRingCreation=true`;
- o certificado vence em menos de 180 dias.

**Desenvolvimento local:** com `DataProtection__GenerateDevelopmentCertificate=true`, que já vem ligado no `appsettings.Development.json`, o Host gera um certificado autoassinado em `src/Host/BemAqui.Host/.local/`, fora do git. Esse opt-in é **ignorado** fora de `Development` e dentro de container (`DOTNET_RUNNING_IN_CONTAINER=true`).

## Primeiro provisionamento (uma vez por ambiente)

1. **Gere o certificado** na estação do PO, num diretório em memória (tmpfs), para a chave nunca tocar o disco:
   ```bash
   d=$(mktemp -d -p /dev/shm) && cd "$d"
   openssl req -x509 -newkey rsa:3072 -sha256 -days 3650 -nodes \
     -subj "/CN=BemAqui Empresas DataProtection (prod)" \
     -addext "basicConstraints=critical,CA:FALSE" \
     -addext "keyUsage=critical,keyEncipherment,dataEncipherment" \
     -keyout dp.key -out dp.crt
   openssl pkcs12 -export -inkey dp.key -in dp.crt -out dp-cert.pfx   # define a senha
   # copie dp-cert.pfx para o cofre e para a VPS; depois:
   cd / && rm -rf "$d"
   ```
   Alternativa: em C#, `ProtectionCertificate.CreateSelfSigned(subject, senha)` gera o mesmo formato.
2. **Guarde a cópia offline.** Coloque o `dp-cert.pfx` e a senha no cofre de senhas do PO, no item "BemAqui Empresas: certificado Data Protection (ambiente)". Nunca coloque essa cópia no repositório, no banco ou no bucket de backup dos bancos.
3. **Instale na VPS.** Copie `dp-cert.pfx` e um arquivo `dp-cert-password` para `/opt/projetos/bemaqui-empresas[-homolog]/secrets/`, com `chmod 0400` e dono = uid do container. Monte os dois como secrets no compose.
4. **Crie o key ring.** Suba **uma vez** com `DataProtection__AllowKeyRingCreation=true` e confira no log a mensagem "Key ring do Data Protection pronto".
5. **Desligue a flag.** Volte `AllowKeyRingCreation` para `false` e reinicie. Enquanto a flag estiver ligada, toda subida avisa no log.

## Backup do key ring

- O dump diário do catálogo já inclui `security.data_protection_keys`, que está cifrado.
- Há também uma **trilha separada**: um export semanal só do key ring, com outro destino e outra credencial:
  ```bash
  f=keyring-$(date +%F).dump
  pg_dump -h <host> -U <user> -d catalog -t security.data_protection_keys --data-only -Fc -f "$f"
  age -r <chave-publica-do-PO> -o "$f.age" "$f" && rm -f "$f"   # envie "$f.age" ao destino da trilha separada
  ```
- O destino da trilha separada (bucket, provedor e credencial) será definido na FND-10b, e **o CA 8 da FND-11 fica parcial até lá**. Até essa definição, o key ring só sobrevive dentro do dump do catálogo.

## Restauração (outra máquina ou VPS nova)

1. Restaure o catálogo e os bancos de tenant a partir dos dumps.
2. Confira se `security.data_protection_keys` tem linhas. Se estiver vazia, restaure a trilha separada do key ring.
3. Monte o `.pfx` e a senha a partir da cópia offline.
4. Suba com `AllowKeyRingCreation=false`.
   - Se o Host subir, os segredos decifram.
   - Se ele recusar, a mensagem no log diz qual peça falta.
5. **Nunca** suba com `AllowKeyRingCreation=true` para "destravar" uma restauração. Isso cria um key ring novo, e os segredos antigos continuam ilegíveis.

**Teste mensal:** restaure numa máquina limpa e confira que um segredo decifra. O teste automatizado equivalente é `DataProtectionKeyRingTests.Restauracao_com_key_ring_decifra_e_sem_key_ring_nao`.

## Rotação, revogação e troca do certificado

- **Chaves:** o Data Protection cria uma chave nova a cada 90 dias. As antigas continuam decifrando, e o que for regravado passa a usar a nova. Não há nada a fazer.
- **Revogação:** **NÃO revogue chaves.** O que foi cifrado com uma chave revogada deixa de decifrar, e os segredos que ainda usam essa chave (conexões de tenant, TOTP, SMTP, IA) ficam ilegíveis. Só revogue depois que existir o job de recifração (FND-10b). Esse job faz Unprotect e Protect de todos os segredos de todos os tenants e confirma que nenhum segredo ainda usa a chave antiga.
- **Troca do certificado:** **ainda não é suportada.** Hoje o Host aceita um único certificado. A lista de certificados antigos, usados só para decifrar, entra na FND-10b junto com o job de recifração. Até lá:
  - não troque nem retire o certificado em uso;
  - o certificado gerado por este runbook vale 10 anos, e a subida avisa 180 dias antes do vencimento.
- **Suspeita de vazamento do certificado:** trocar o certificado **não** protege os backups antigos, porque quem tem o certificado e um dump antigo decifra os segredos daquela época. O que fazer:
  1. Troque os segredos na origem: senhas de banco dos tenants, senha SMTP, chaves de IA e, na Fase 5, as credenciais do Inter.
  2. Quando a FND-10b existir, recifre tudo com o certificado novo.

## Troca de ambiente: recifração para um key ring novo (GOL-01a, ADR 0034)

A produção nasce com key ring e certificado novos. Os segredos do banco do `konebras` (e a conexão dele no catálogo) passam do key ring
da homologação para o novo pelo comando `reencrypt-secrets`, que abre o key ring antigo só durante a execução, a partir de um export de
`security.data_protection_keys` e do certificado antigo, e confere no fim que nada decifra mais com o antigo. Passo a passo, lista dos
segredos e recusas em [go-live-scripts.md](go-live-scripts.md) §2.

## Credenciais e certificado do Banco Inter (BNK-01a)

- Cifrados por tenant **e por integração de conta**: `inter-credentials` (client id e client secret) e `inter-certificate` (certificado,
  chave privada e senha do `.pfx`), com o id da integração no propósito (`bemaqui → tenant:{id} → inter-… → {integrationId} → v1`,
  teste golden em `SecretProtectorTests`). O ciphertext de uma conta não decifra em outra.
- Só de escrita: a tela mostra "configurado em … por …", o fim do client id, a validade, o titular e a impressão digital SHA-256.
- **Rotação anual do certificado:** o Proprietário é avisado 30, 15, 7 e 1 dia antes (`inter-certificate-expiry`). Gere o certificado novo
  no Internet Banking do Inter e use **Trocar credenciais** (2FA de novo); a combinação nova é testada antes de substituir a atual, e o
  handler mTLS e o token em cache são descartados. Revogue o antigo no Inter só depois do teste com o novo. Detalhes em
  [banco-inter.md](banco-inter.md).
- **Vazamento:** desligue a integração na tela (os segredos são sobrescritos), revogue a integração no Inter e gere outra.

# Produção (GOL-01a CA 5, ADR 0034 §2 e §5)

Regra: **todo segredo de produção é novo**. Nenhum valor, chave, certificado, key ring ou token sai da homologação. Os valores ficam só nos arquivos de `secrets/` (0400) e no cofre de senhas do PO; nunca neste repositório, em chat, ticket, e-mail ou variável de ambiente. Gere na estação do PO, num diretório em memória, e apague no fim:
```bash
d=$(mktemp -d -p /dev/shm) && cd "$d" && umask 077
# ... gere os arquivos abaixo, copie para o cofre e para a VPS (scp para /opt/projetos/bemaqui-empresas/secrets/) ...
cd / && rm -rf "$d"
```
Na VPS, depois de copiar: `cd /opt/projetos/bemaqui-empresas/secrets && chown <dono> <arquivo> && chmod 0400 <arquivo>`, e a conferência `find . -type f ! -perm 0400` (não pode listar nada). O diretório é `root:root 0700`.

## Inventário (sem valores)

| Segredo | Arquivo / local | Dono (uid) | Quem gera | Como gerar | Rotação |
|---|---|---|---|---|---|
| Senha do superuser `postgres` | `secrets/pg-superuser-password` e `secrets/backup-pg-password` (mesmo valor) | 70 / 10001 | PO | `openssl rand -hex 32 \| tr -d '\n'` | Anual ou suspeita: `ALTER ROLE postgres PASSWORD …` e troca dos dois arquivos |
| Senha do role `bemaqui_catalog` | `secrets/pg-catalog-password` (lida só na criação do volume) e dentro de `catalog-connection` | 70 / 1654 | PO | `openssl rand -hex 32 \| tr -d '\n'` | Anual: `ALTER ROLE bemaqui_catalog PASSWORD …`, depois `catalog-connection` e reinício da API |
| Senha do role `bemaqui_provisioner` | `secrets/pg-provisioner-password` e dentro de `provisioner-connection` | 70 / 1654 | PO | `openssl rand -hex 32 \| tr -d '\n'` | Anual, como a do catálogo |
| Conexão do catálogo | `secrets/catalog-connection` | 1654 | PO | `printf 'Host=bqe-prod-postgres;Database=catalog;Username=bemaqui_catalog;Password=%s' "$(cat pg-catalog-password)" > catalog-connection` | Junto com a senha do role |
| Conexão do provisionador | `secrets/provisioner-connection` | 1654 | PO | `printf 'Host=bqe-prod-postgres;Database=postgres;Username=bemaqui_provisioner;Password=%s' "$(cat pg-provisioner-password)" > provisioner-connection` | Junto com a senha do role |
| Usuário e senha do banco do tenant `konebras` | Banco de catálogo, cifrado com o key ring (`connection`) | — | Runbook da virada | **Novos na promoção** (`ALTER ROLE tenant_konebras PASSWORD …` com `openssl rand -hex 32`) e a conexão cifrada de novo com o key ring de produção (ADR 0034 §2) | Anual |
| Assinatura do JWT | `secrets/auth-signing-key` | 1654 | PO | `openssl rand -base64 48 \| tr -d '\n'` | Anual ou suspeita (troca derruba todas as sessões: fazer fora do horário) |
| Senha do SMTP | `secrets/smtp-password` | 1654 | PO, no provedor de e-mail (credencial nova só da produção) | Painel do provedor (SES: usuário SMTP IAM novo) | Anual |
| Certificado do key ring (`.pfx`, RSA 3072) | `secrets/dp-cert.pfx` + cofre | 1654 | PO | Comandos de "Primeiro provisionamento" acima, com `-subj "/CN=BemAqui Empresas DataProtection (prod)"` | Não trocar até existir a lista de certificados antigos (validade 10 anos, aviso 180 dias antes) |
| Senha do `.pfx` | `secrets/dp-cert-password` + cofre | 1654 | PO | `openssl rand -base64 32 \| tr -d '\n'` (a mesma digitada no `openssl pkcs12 -export`) | Junto com o certificado |
| Key ring do Data Protection | Catálogo de produção (`security.data_protection_keys`), criado com `ALLOW_KEY_RING_CREATION=true` uma vez | — | Primeira subida | Automático; **nunca** copiado da homologação | Automática a cada 90 dias; não revogar |
| Chave da API do EmissorFiscal (produção) | Banco do tenant, cifrada (`fiscal-api-key`); entra pela tela Configurações → Integrações → Emissor fiscal | — | PO, no painel do Emissor | Chave nova do tenant de produção do Emissor, **sem** `admin`, só `nfe:emitir`, `nfe:eventos` e `consultar` (ADR 0027) | Anual ou suspeita |
| Segredo do webhook do Emissor | Banco do tenant (`webhook-secret`), colado em "Trocar o segredo" | — | Emissor (mostra ao cadastrar o webhook de produção) | Cadastro novo em `https://plataforma.bemaquiapp.com.br/webhooks/emissor-fiscal/{tenantKey}` | Anual (sobreposição de 24 h) |
| Credenciais do Inter (client id/secret) e certificado mTLS | Banco do tenant, por conta (`inter-credentials`, `inter-certificate`); entram pela tela | — | PO, no Internet Banking do Inter | Integração nova de produção só com cobrança, extrato e webhook (ADR 0032) | Certificado anual (aviso 30/15/7/1 dias) |
| Token do webhook do Inter | Só o hash no banco; a URL aparece uma vez | — | BemAqui ("Ativar aviso de pagamento") | Automático (256 bits) | Na troca de endereço |
| Chave pública do backup (`age1…`) | `secrets/backup-age-recipients` | 10001 | PO | `age-keygen -o bqe-prod-backup.key` e `age-keygen -y bqe-prod-backup.key > backup-age-recipients`; a privada **só no cofre**, nunca na VPS | Anual: nova chave; guarde a antiga enquanto houver backup cifrado com ela (30 dias) |
| B2: bucket dos dumps | `secrets/dumps-s3-{endpoint,bucket,access-key,secret-key}` | 10001 | PO, no B2 | Bucket `bqe-prod-dumps` + chave de aplicação só dele, sem `deleteFiles` (`b2 key create --bucket bqe-prod-dumps bqe-prod-dumps listBuckets,listFiles,readFiles,writeFiles`) | Anual |
| B2: bucket do key ring | `secrets/keyring-s3-{endpoint,bucket,access-key,secret-key}` | 10001 | PO, no B2 | Bucket `bqe-prod-keyring` + **outra** chave, sem `deleteFiles` | Anual |
| Chave SSH de deploy | Privada: secret `PROD_SSH_KEY` do environment `production`; pública em `/root/.ssh/authorized_keys` com `command=…/deploy.sh,restrict` | — | PO | `ssh-keygen -t ed25519 -f bqe-prod-deploy -N '' -C bqe-prod-deploy` | Anual ou saída de alguém com acesso |
| `known_hosts` da VPS | Secret `PROD_KNOWN_HOSTS` do environment `production` | — | PO | `ssh-keyscan -t ed25519 2.25.82.127` (conferir a impressão digital no painel da Hostinger) | Se a VPS mudar |
| Token do GHCR (pull na VPS) | `/opt/projetos/bemaqui-empresas/.docker/config.json` (0600) | root | PO | PAT clássico só com `read:packages`, nome "bqe-prod-vps", login com `DOCKER_CONFIG=…/.docker docker login ghcr.io` | Anual (data de expiração no próprio PAT) |
| Push das imagens | `GITHUB_TOKEN` do workflow (efêmero) | — | GitHub | Automático, `packages: write` só no job de imagens | — |
| Aprovação do deploy | Environment `production`: Required reviewers = PO, Prevent self-review, só tags `v*` | — | PO | Settings → Environments | Revisar a cada go-live/saída de alguém |
| Allowlist do painel da plataforma | `/opt/infra/nginx/conf.d/bemaqui-empresas.allow` | root | PO | IPs fixos conferidos (não é segredo, mas não vai ao repositório) | Quando o IP mudar |
| Senha básica do Dolibarr em `adm.` | `/opt/infra/nginx/conf.d/adm-dolibarr.htpasswd` (0640) + cofre | root | PO | `htpasswd -B -c adm-dolibarr.htpasswd <usuário>` (bcrypt) | Até o desligamento do Dolibarr |
| `DOLAPIKEY` do Dolibarr | Não existe na produção (regenerada e os usuários desativados na virada; secret da importação apagado, ADR 0017 adendo 2) | — | — | — | — |

Também ficam fora dos arquivos: WhatsApp (credenciais do BSP, pela tela, `whatsapp-credentials`) e chave de IA (pela tela, `ai-key`), ambos cifrados no banco do tenant e recadastrados na produção.

## Conferência antes do "Go" (CA 5)
- `find /opt/projetos/bemaqui-empresas/secrets -type f ! -perm 0400` vazio e `stat -c '%u %n' secrets/*` com o dono da tabela.
- Nenhum arquivo igual ao da homologação: `cd /opt/projetos && for f in bemaqui-empresas/secrets/*; do cmp -s "$f" "bemaqui-empresas-homolog/secrets/$(basename "$f")" && echo "REAPROVEITADO: $f"; done` não pode imprimir nada.
- Impressão digital do `dp-cert.pfx` diferente da homologação (`openssl pkcs12 -in dp-cert.pfx -nokeys | openssl x509 -noout -fingerprint -sha256`, com a senha).
- Data de geração de cada item registrada no cofre do PO; o inventário acima fica sem valor.
