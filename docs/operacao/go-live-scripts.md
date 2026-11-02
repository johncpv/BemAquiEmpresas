# Runbook: comandos e scripts da virada (GOL-01a)

Ferramentas do ensaio (GOL-01a CA 4, 7, 8, 10 e 11) e da virada (GOL-01b CA 3 a 5), na ordem do runbook `go-live.md`. Implementam a ADR 0034 §2 a §4.

| Passo | Ferramenta | Onde roda | Grava? |
|---|---|---|---|
| 0 | `migrate` com `DataProtection__AllowKeyRingCreation=true` uma vez (catálogo novo vazio e key ring novo, `segredos-e-chaves.md`) | CLI da imagem, no ambiente novo | sim |
| 1 | `tenant promote` | CLI da imagem, no ambiente **novo** | sim, com `--confirm-tenant` |
| 2 | `reencrypt-secrets` | CLI da imagem, no ambiente **novo** | sim, com `--confirm-tenant` |
| 3 | `migrate` (agora migra o banco do tenant, que já decifra) | CLI da imagem, no ambiente novo | sim (o de sempre, `provisionamento.md`) |
| 4 | `tenant copy-files` | CLI da imagem, com os dois volumes montados | sim, com `--confirm-tenant` |
| 5 | `scripts/golive/acessos-virada.sql` e `acessos-virada-catalogo.sql` | `psql` no banco do tenant e no catálogo novos, **API parada** | sim, com `bqe.confirmar` |
| 6 | `scripts/golive/conferencia.sql` | `psql` no banco do tenant | nunca (`BEGIN READ ONLY`) |
| 7 | `scripts/golive/dolibarr-fechamento.sh` | shell da VPS, no MariaDB do Dolibarr | sim, com `--aplicar --confirmar` |

Regras comuns:
- **Toda ferramenta que grava simula por padrão** (ou tem `--dry-run`) e só grava com a confirmação que repete o alvo (chave do tenant ou nome do banco). Rode sempre a simulação antes e guarde a saída no registro do ensaio.
- **Nenhuma ferramenta imprime segredo.** Conexões e senhas entram por arquivo de secret (0400, fora do repositório), nunca em argumento. A saída da recifração tem só contagens.
- Os comandos da CLI seguem o padrão dos existentes (`migrate`, `tenant provision|invite-owner|import-dolibarr`, `platform-admin create`, `alert raise`): na VPS, `docker compose run --rm migrator <comando>`; no desenvolvimento, `dotnet run --project src/Host/BemAqui.Host -- <comando>`. Código de saída: `0` ok, `1` recusado antes de gravar, `2` falha (o banco foi desfeito) ou, no `copy-files`, anexo com problema.

## 1. `tenant promote`: o tenant no catálogo novo, com o mesmo id

O id do tenant entra no propósito de todos os segredos dele (ADR 0007), então o catálogo novo precisa da **mesma** linha. O comando lê o catálogo antigo e grava no configurado (`ConnectionStrings__Catalog` do ambiente novo):

- leva a linha do tenant (mesmo id, chave, razão social, documento, banco, versão do schema), o plano (pelo nome; se o catálogo novo não tem, cria com os módulos do plano) e os módulos liberados (`tenant_modules`);
- grava **só os hosts informados**: `--primary-host` vira o principal e `--extra-hosts` os demais. Os hosts da homologação **não** vêm (a saída lista quais ficaram para trás);
- `bank_production_allowed = false` e `fiscal_environment_allowed = 'None'` na chegada (a GOL-01b liga); a chave dos webhooks (`webhook_key`) não vem: a produção cadastra os provedores de novo (ADR 0034 §3);
- não traz nenhum outro tenant (`demo` e os de teste ficam na homologação);
- recusa se o catálogo novo já tem a chave, o banco ou um dos hosts com **outro** id; idempotente (o que já existe fica como está).

```sh
umask 077; printf '%s' 'Host=...;Database=catalog;Username=...;Password=...' > /opt/projetos/bemaqui-empresas/secrets/catalogo-homolog   # conexão SÓ LEITURA ao catálogo restaurado da homologação
docker compose run --rm -v /opt/projetos/bemaqui-empresas/secrets/catalogo-homolog:/run/secrets/catalogo-homolog:ro migrator \
  tenant promote --tenant konebras --from-catalog-file /run/secrets/catalogo-homolog --primary-host app.konebras.com.br --dry-run
# conferida a saída:
docker compose run --rm ... migrator tenant promote --tenant konebras --from-catalog-file /run/secrets/catalogo-homolog \
  --primary-host app.konebras.com.br --confirm-tenant konebras
```
No ensaio, use o host de ensaio em `--primary-host` (GOL-01a CA 7). A conexão chega cifrada com o key ring antigo: o passo 2 resolve.

## 2. `reencrypt-secrets`: do key ring da homologação para o da produção

Decifra cada segredo com o key ring **antigo**, aberto só dentro do comando (passo isolado, ADR 0034 §2), e cifra com o key ring **configurado** (o do ambiente novo: `security.data_protection_keys` do catálogo novo e o `dp-cert.pfx` novo). Uma transação no banco do tenant e outra no catálogo; antes do commit, confere que **tudo decifra com o novo e nada decifra com o antigo**, senão desfaz.

Segredos tratados (lista fixa em `src/Host/BemAqui.Host/Cli/Reencryption/SecretColumns.cs`; o comando recusa rodar se o schema tiver outra coluna `*_cipher`):

| Coluna | Tipo (propósito) |
|---|---|
| `catalog.tenants.connection_secret` (só a linha do tenant) | `connection` |
| `catalog.platform_admins.totp_secret_cipher` | `totp` (escopo plataforma) |
| `identity.users.totp_secret_cipher` | `totp` |
| `identity.ai_settings.api_key_cipher` | `ai-key` |
| `fiscal.integration_settings.api_key_cipher` (chave do EmissorFiscal) | `fiscal-api-key` |
| `shared.webhook_tokens.secret_cipher` e `previous_secret_cipher` | `webhook-secret` |
| `finance.bank_integrations.client_id_cipher`, `client_secret_cipher` | `inter-credentials` (por integração) |
| `finance.bank_integrations.certificate_cipher`, `private_key_cipher`, `certificate_password_cipher` | `inter-certificate` (por integração) |

Fora da lista, de propósito: a senha do SMTP (`smtp`) não fica no banco (vem de arquivo de secret, nova na produção); o A1 não fica no BemAqui (cofre do EmissorFiscal); tokens de webhook do Inter, PINs, aparelhos e quiosques são hash, não cifra. Os segredos de sandbox (Inter, Emissor de homologação, webhooks) são recifrados para a ferramenta conferir tudo, mas **são trocados** na GOL-01b de qualquer forma.

Export do key ring antigo (um elemento XML por linha; continua cifrado pelo certificado antigo), num diretório em memória:
```sh
d=$(mktemp -d -p /dev/shm)
psql "$CATALOGO_HOMOLOG" -At -c "SELECT xml FROM security.data_protection_keys ORDER BY id" > "$d/keyring-homolog.xml"
# copie para o mesmo diretório o dp-cert.pfx e o dp-cert-password ANTIGOS (cofre do PO)
```
Recifração (no `migrator` do ambiente novo, com o diretório montado só leitura):
```sh
docker compose run --rm -v "$d":/run/old-keys:ro migrator reencrypt-secrets --tenant konebras \
  --old-keys /run/old-keys/keyring-homolog.xml --old-cert /run/old-keys/dp-cert.pfx --old-cert-password-file /run/old-keys/dp-cert-password --dry-run
docker compose run --rm -v "$d":/run/old-keys:ro -v /opt/projetos/bemaqui-empresas/secrets/tenant-konebras-conn:/run/secrets/tenant-conn:ro migrator \
  reencrypt-secrets --tenant konebras --old-keys ... --old-cert ... --old-cert-password-file ... \
  --tenant-connection-file /run/secrets/tenant-conn --confirm-tenant konebras
rm -rf "$d"
```
- `--tenant-connection-file` (opcional): a conexão **nova** do tenant (usuário e senha novos depois do `ALTER ROLE`, ADR 0034 §2). Com ele, a `connection_secret` é trocada por ela, cifrada com o key ring novo; sem ele, a conexão antiga é recifrada.
- `--old-keys` aceita também um diretório com um `.xml` por chave.
- Recusas: certificado antigo igual ao novo, key rings com chave em comum (a produção precisa de key ring e certificado novos), coluna cifrada desconhecida, falta de `--confirm-tenant`.
- Saída: por coluna e por tipo, "recifrados", "já no key ring novo" e "ilegíveis", e a linha "Conferência: N segredo(s) decifram com o key ring novo; 0 decifram com o antigo". Qualquer ilegível faz o comando desfazer o banco e sair com 2.
- Idempotente: rodar de novo não recifra nada (tudo já decifra com o novo).
- **Teste do ensaio (CA 4):** a última linha é a evidência; anote-a no registro. Depois, apague o export e as cópias do certificado antigo.

## 3. `tenant copy-files`: só os arquivos do konebras

O volume `bqe-homolog-files` mistura o `demo` e o `konebras`, e parte dos arquivos não tem prefixo de tenant. A única referência a arquivo no banco é `shared.attachments.storage_path` (todo arquivo passa pelo `AttachmentService`); o comando lista os anexos do tenant, copia cada arquivo para o mesmo caminho no volume novo e confere o SHA-256 gravado no anexo na origem e na cópia (cópia num temporário, renomeado só depois do hash).

```sh
# lista dos arquivos dos OUTROS tenants da homologação, para separar "de outro tenant" de "sem dono":
psql "$DEMO_DB" -At -c "SELECT storage_path FROM shared.attachments" > /dev/shm/outros-tenants.txt
docker compose run --rm -v bemaqui-empresas-homolog_files:/source:ro -v /dev/shm/outros-tenants.txt:/run/outros.txt:ro migrator \
  tenant copy-files --tenant konebras --from /source --to /data/files --other-tenants-paths /run/outros.txt --dry-run
# conferida a saída, troque --dry-run por --confirm-tenant konebras
```
- Relata: copiados (ou "a copiar"), já no destino com o mesmo hash, **órfãos** (anexo sem arquivo na origem), hash diferente do anexo, destino com outro arquivo no mesmo caminho (nunca sobrescreve) e **arquivos sem dono** (na origem, sem anexo deste tenant nem da lista dos outros; ex.: `migration-reports/`). Até 20 ids ou caminhos por lista.
- Anexo com conteúdo apagado pela LGPD (`content_removed_at`) não tem arquivo e não conta como órfão.
- Nunca apaga nada; idempotente. Saída 2 quando há anexo com problema: investigue antes do "Go".

## 4. `acessos-virada.sql` e `acessos-virada-catalogo.sql`

No banco do tenant, numa transação (GOL-01a CA 10, ADR 0034 §3): apaga os usuários de teste e o que é só deles no Identity; revoga **todas** as famílias de refresh; gera security stamp novo e sobe o `perm_version` de **todos** os usuários (o JWT antigo cai no `sstamp`; o cache de permissões muda de chave); expira convites, redefinições de senha e desafios de 2FA; apaga os PINs; revoga os aparelhos do chão de fábrica (motivo `go_live`), as telas de TV e as sessões delas.

Usuário de teste é quem tem o e-mail num domínio de `bqe.dominios_teste` ou está em `bqe.emails_teste` (lista do PO). O script recusa (erro `BQOWN`, nada gravado) se o grupo Proprietário ficar sem membro ativo.

```sh
# API parada (o cache do security stamp é do processo e some com ele)
docker compose stop api
psql "$TENANT_DB" -v ON_ERROR_STOP=1 -c "SET bqe.dominios_teste = 'teste.com,example.com'" -c "SET bqe.emails_teste = 'qa@konebras.com.br'" \
  -f scripts/golive/acessos-virada.sql                                   # simulação: mostra as contagens e desfaz
psql "$TENANT_DB" -v ON_ERROR_STOP=1 -c "SET bqe.dominios_teste = 'teste.com,example.com'" -c "SET bqe.emails_teste = 'qa@konebras.com.br'" \
  -c "SET bqe.confirmar = 'tenant_konebras'" -f scripts/golive/acessos-virada.sql   # grava
psql "$CATALOG_DB" -v ON_ERROR_STOP=1 -c "SET bqe.confirmar = 'catalog'" -f scripts/golive/acessos-virada-catalogo.sql
docker compose up -d api
```
- `bqe.confirmar` é o nome do banco (`current_database()`): protege contra rodar no banco errado.
- A saída é uma tabela "item / quantidade / situação". O do catálogo também revoga as sessões do painel da plataforma e os links pendentes, e **lista os super admins** (e-mail, situação, 2FA, último acesso) e os tenants e hosts, para o PO conferir. Não cole essa saída em chat nem em ticket.
- Idempotente. Conferência do CA 10: um refresh e um JWT emitidos antes dão 401 depois (teste automatizado `AcessosViradaTests`; no ensaio, com um login feito antes do script).

## 5. `conferencia.sql` (só leitura, sem dado pessoal)

```sh
psql "$TENANT_DB" -v ON_ERROR_STOP=1 -c "SET bqe.mes = '2026-12'" -f scripts/golive/conferencia.sql
```
Seis tabelas, nesta ordem: saldo de cada conta (saldo inicial + transações, com as não conciliadas), títulos a receber e a pagar em aberto (com vencidos), títulos da folha do mês (`bqe.mes`, padrão o mês corrente; salários e guias), estoque por local (quantidade e valor ao custo médio), contagens (terceiros, itens, contratos, projetos) e a **asserção** "nenhuma nota de homologação e nenhum boleto de sandbox" (coluna `resultado` = `OK`). Compare o saldo com o extrato do dia e o estoque com a contagem física (CA 8). Valores em `numeric(18,2)`.

## 6. `dolibarr-fechamento.sh` (VPS, MariaDB do Dolibarr)

```sh
sh scripts/golive/dolibarr-fechamento.sh conferir --container dolibarr-db --database <banco>
sh scripts/golive/dolibarr-fechamento.sh fechar   --container dolibarr-db --database <banco>                 # simulação: lista quem tem api_key
sh scripts/golive/dolibarr-fechamento.sh fechar   --container dolibarr-db --database <banco> --api-logins <login-do-konebras-ui> --aplicar --confirmar <banco>
sh scripts/golive/dolibarr-fechamento.sh restaurar --container dolibarr-db --database <banco> --aplicar --confirmar <banco>   # rollback
```
- `fechar`: numa transação, guarda e remove os direitos que não são de leitura (`llx_rights_def.type <> 'r'`) de `llx_user_rights` e `llx_usergroup_rights`, guarda e tira o `admin` de todos os usuários (o admin do Dolibarr ignora os direitos), troca a `api_key` dos usuários de `--api-logins` por um valor aleatório que ninguém conhece e os desativa (`statut = 0`). Depois, `DROP USER IF EXISTS` do `bqe_migracao_ro` em cada host em que ele existir (provavelmente nunca existiu: o caminho padrão foi a API REST), e a conferência.
- Cópia do que saiu, para o rollback: `bqe_golive_user_rights_bkp`, `bqe_golive_usergroup_rights_bkp` e `bqe_golive_user_bkp` (só `rowid`, `admin` e `statut`; a chave antiga **não** é guardada).
- `restaurar` devolve os direitos e o admin; os usuários da API continuam desativados e a `DOLAPIKEY` antiga não volta (GOL-01a CA 9).
- A senha de root é a do próprio container (`--root-password-env`, padrão `MARIADB_ROOT_PASSWORD`); nada vai em argumento.
- O script imprime os lembretes manuais: allowlist ou `auth_basic` no vhost do `adm.konebras.com.br` (`nginx -t`), rede e firewall sem acesso ao MariaDB, `curl` com a chave antiga respondendo 401/403, apagar o `secrets/dolibarr-api`, Konebras UI fora do ar.
- **Confira na cópia do Dolibarr, antes do ensaio:** `SELECT type, count(*) FROM llx_rights_def GROUP BY type` (o script assume `r` = leitura) e que os logins da API são os que o Konebras UI e a importação usavam.
