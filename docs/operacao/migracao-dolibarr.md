# Migração do Dolibarr: mapeamento e runbook

MIG-01a, ADR 0017 e ADR 0018. A migração traz **só cadastros**. Lançamentos e financeiro transacional começam no BemAqui.

> **Aceite do PO (CA 1):** aceito em 18/10/2026, por John (PO). Inventário pela API REST do Dolibarr 21.0.4 na mesma data: 118 terceiros (36 clientes, 81 fornecedores, 31 com CNPJ/CPF no `idprof1`, todos na entidade 1), 0 contatos, 34 produtos (com o extrafield `elev_garantia`), 4 depósitos, 3 contas bancárias, 13 condições de pagamento, 36 unidades, 27 UFs, 7 categorias de fornecedor e nenhuma de produto ou cliente.

## 1. Mapeamento

A lista fixa de tabelas e colunas fica em `SnapshotTables` (`src/Modules/Migrations/BemAqui.Migrations.Application/Snapshot/`). O GRANT, o snapshot e o DDL de `scripts/dolibarr/` são gerados dela; o teste `SnapshotScriptsTests` reprova se divergirem. Para mudar a lista: altere `SnapshotTables` e rode `BQ_UPDATE_SNAPSHOT_SQL=1 sh scripts/test-docker.sh --filter-class "*SnapshotScriptsTests"`.

Nomes do Dolibarr padrão. No `llx_societe`, o `idprof1` a `idprof4` da tela são as colunas físicas `siren`, `siret`, `ape` e `idprof4`. O inventário da base real confirma, incluindo extrafields.

| Dolibarr | Colunas lidas | BemAqui | Carga |
|---|---|---|---|
| `llx_societe` | rowid, nom, name_alias, client, fournisseur, siren, siret, ape, idprof4, tva_intra, address, zip, town, fk_departement, fk_pays, phone, email, note_private, note_public, status, cond_reglement, fk_typent, entity, tms | `parties.parties`, `party_roles`, `party_addresses` (cobrança) | MIG-01b |
| `llx_socpeople` | rowid, fk_soc, lastname, firstname, poste, email, phone, phone_mobile, address, zip, town, statut, entity, tms | `parties.party_contacts` | MIG-01b |
| `llx_c_payment_term` | rowid, code, libelle, nbjour, type_cdr, decalage, active, entity | `finance.payment_terms` (de-para com o seed) | MIG-01b |
| `llx_bank_account` | rowid, ref, label, bank, code_banque, code_guichet, number, cle_rib, courant, clos, entity (sem saldo) | `finance.bank_accounts` | MIG-01b |
| `llx_c_departements`, `llx_c_typent` | rowid, code_departement · id, code | conversão de UF e de PF/PJ | MIG-01b |
| `llx_categorie`, `llx_categorie_fournisseur` | rowid, label, type, fk_parent, entity · fk_categorie, fk_soc | de-para opcional de papéis (Instalador, Transportadora) | MIG-01b |
| `llx_c_units` | rowid, code, label, active | `inventory.units` (de-para) | MIG-01c |
| `llx_categorie_product` | fk_categorie, fk_product | `inventory.item_categories` e `items.category_id` | MIG-01c |
| `llx_product`, `llx_product_extrafields` | rowid, ref, label, fk_product_type, fk_unit, customcode, fk_country, seuil_stock_alerte, tobatch, tosell, tobuy, entity, tms · fk_object, elev_garantia | `inventory.items` | MIG-01c |
| `llx_product_fournisseur_price` | rowid, fk_product, fk_soc, ref_fourn, quantity, fk_unit, entity (sem preço) | `inventory.item_supplier_codes` | MIG-01c |
| `llx_entrepot` | rowid, ref, label, lieu, statut, fk_parent, entity | `inventory.locations` | MIG-01c |
| `llx_const` | name, value, entity (o script lê só `MAIN_INFO_SIREN`) | proteção contra tenant errado | MIG-01a |

### Regras comuns
- **Filtro:** `entity = 1`. Linha de outra entidade sai como "ignorado", com o motivo.
- **Chave de origem:** tabela + `rowid` (ou a chave composta, unida por `-`). Vai em `migration.external_refs` e no `import_source` do destino (`dolibarr:llx_societe:42`).
- **Conversões (CA 20):** quantidade para `numeric(18,4)` com arredondamento para longe do zero; data sem hora para `date`; data com hora lida como horário de São Paulo e gravada em UTC; data zerada (`0000-00-00`) vira vazio; texto em UTF-8 (NFC), sem caracteres de controle nem marcas bidi, com o acento recuperado quando foi gravado duas vezes em latin1.
- **De-para (`--mapping`):** JSON `{ "maps": { "<nome>": { "<valor no Dolibarr>": "<valor no BemAqui>" } } }`. Os nomes dos mapas são definidos pelas cargas (MIG-01b e MIG-01c).
- **Reexecução:** mesmo conteúdo sai como "sem alteração". Conteúdo mudou e o registro não foi editado no BemAqui: "atualizado". Editado no BemAqui depois da carga: "conflito", sem sobrescrever, com os dois valores.

### Cargas da MIG-01b (`--entities payment-terms,bank-accounts,parties,contacts`)
Rodam nesta ordem, porque o terceiro aponta para a condição padrão:

| Entidade | Origem | Regras |
|---|---|---|
| `payment-terms` | `llx_c_payment_term` | De-para `payment-terms` (id → `seed_key` ou nome). Sem de-para: `nbjour` 0 liga ao "À vista" e 30 ao "30 dias". O resto é criado com uma parcela de 100% em `nbjour` dias. Regra de fim de mês (`type_cdr`) só gera aviso. |
| `bank-accounts` | `llx_bank_account` | `courant` 2 = Caixa, o resto = Banco. O banco vem de `code_banque` ou do de-para `bank-codes`, e a agência de `code_guichet` ou de `bank-agencies`. Sem banco, a conta vai para erro e é cadastrada à mão. Entra sem saldo; `clos` = 1 entra inativa. |
| `parties` | `llx_societe` | `client` 1/3 = Cliente, 2 = Cliente com "Prospect no Dolibarr"; `fournisseur` 1 = Fornecedor. Papéis especiais pelo de-para `special-roles` (id → `Installer`/`Carrier`) ou `special-role-categories` (categoria de fornecedor → papel). Documento do `idprof1` (`siren`): inválido vai para erro; ausente entra sem documento, PF se o tipo for `TE_PRIVATE`. O mesmo CNPJ na carga vira um terceiro só, com os dados do registro mais recente. CNPJ já cadastrado à mão é vinculado, sem sobrescrever. `status` 0 entra inativo. Notas em texto, com o prefixo da origem. Endereço com CEP, cidade e UF vira o de cobrança, com o bairro "Não informado" (o Dolibarr não tem bairro); sem eles, aviso. |
| `contacts` | `llx_socpeople` | Só de terceiro importado e ativo. E-mail inválido entra vazio, com aviso. |

Exemplo de de-para:
```json
{ "maps": {
  "special-roles": { "57": "Installer" },
  "special-role-categories": { "6": "Carrier" },
  "payment-terms": { "1": "a-vista" },
  "bank-codes": { "1": "077" },
  "bank-agencies": { "1": "0001" }
} }
```

### Cargas da MIG-01c (`--entities units,item-categories,locations,items,supplier-codes`)
Rodam nesta ordem, depois da MIG-01b, porque o item aponta para a unidade e para a categoria, e o código do fornecedor aponta para o item e para o terceiro:

| Entidade | Origem | Regras |
|---|---|---|
| `units` | `llx_c_units` | Só as unidades usadas pelos produtos ou pelos preços de fornecedor; as outras ficam como ignoradas. De-para `units` (código ou id → código do BemAqui, ou `criar`). Sem de-para, vale o mesmo código, e `U` vira UN. Unidade que o BemAqui não tem vai para erro. A chave `none` do mesmo mapa diz a unidade do produto que não tem nenhuma. |
| `item-categories` | `llx_categorie` (tipo 0) | Viram a árvore de categorias, com os pais antes dos filhos. Categorias de fornecedor e de cliente ficam como ignoradas. |
| `locations` | `llx_entrepot` | Um único depósito ativo é ligado ao ALM. Com vários, cada um vira local, com o código saneado do `ref` (até 20 caracteres, letras, números e hífen; sem nada aproveitável, `DEP-{id}`); o sub-depósito leva o nome do pai ("ALMOX / Chapa Inox"). De-para `locations` (id → código de um local do BemAqui) liga a um local existente. A carga nunca troca o local padrão: o relatório sugere um. `statut` 0 entra inativo. |
| `items` | `llx_product` e `llx_product_extrafields` | Código = `ref`, nome = `label`, NCM = `customcode` só com 8 dígitos, estoque mínimo = `seuil_stock_alerte`, ativo quando `tosell` ou `tobuy`. `fk_product_type` 1 = Serviço; os outros pelo de-para `item-types` (ref ou id → `RawMaterial`, `Component`, `Finished`, `Consumable`, ou `materia-prima`, `componente`, `produto-acabado`, `consumo`); sem de-para, Componente com aviso. `tobatch` 1 = lote, 2 = série. `elev_garantia` em meses; vazio ou inválido usa a garantia padrão do tipo. Matéria-prima, Componente e Produto acabado sem NCM entram inativos, com o aviso "sem NCM: ativar depois de preencher". Produto em várias categorias fica na primeira. `ref` repetido vai para erro. Código que já existe no BemAqui é vinculado. Sem custo, saldo nem lote. |
| `supplier-codes` | `llx_product_fournisseur_price` | Código = `ref_fourn`, unidade do fornecedor = código da `fk_unit` (ou o de-para `supplier-units`, id → texto), fator do de-para `supplier-factors` (id → número), padrão 1. Fornecedor ou produto não importado e linha sem `ref_fourn` ficam como ignorados; o mesmo código do mesmo fornecedor em dois produtos vai para erro. Nenhum preço é lido. |

Exemplo de de-para:
```json
{ "maps": {
  "units": { "none": "UN", "CX": "criar" },
  "item-types": { "ELV-QT-001": "produto-acabado", "MP-0001": "materia-prima" },
  "locations": { "1": "ALM" },
  "supplier-units": { "12": "CX" },
  "supplier-factors": { "12": "25" }
} }
```

### O que não migra (decidido em 18/10/2026)
- **Lançamentos e financeiro transacional:** `llx_facture*`, `llx_paiement*`, `llx_paiementfourn*`, `llx_bank`, `llx_product_stock`, `llx_product_batch`, `llx_product_lot`, `llx_stock_mouvement`, custo médio (`pmp`). O saldo inicial de estoque é digitado na EST-01 e o das contas na CAD-05b.
- **Sem destino na Fase 1:** `llx_propal`, `llx_commande`, `llx_mrp_mo`, `llx_bom_bom`, `llx_projet`, usuários (`llx_user`: são convidados), funcionários (CAD-04b, por minimização), plano contábil (o gerencial vem do seed da CAD-05a), modos de pagamento (`llx_c_paiement`, Fase 2).
- **Minimização (LGPD):** `llx_societe_rib`, IBAN, BIC, titular, `birthday`, preços (`llx_product_price` e as colunas de preço do fornecedor) e anexos.
- Tudo continua consultável no Dolibarr em `adm.konebras.com.br` até o desligamento.

## 2. Origem padrão: API REST do Dolibarr (ADR 0017, adendo 2)
A importação lê pela API (`api/index.php`), só com GET, usando a chave do Konebras UI (autorizada pelo PO em 18/10/2026). O campo de cada coluna está em `DolibarrApiMap`; o que não está na lista fixa (IBAN, saldo, preços, custo, estoque) é descartado na leitura.

1. Crie o secret uma vez, sem mostrar a chave na tela (0600, dono 1654, que é o usuário da CLI):
   ```sh
   cd /opt/projetos/bemaqui-empresas-homolog
   umask 077
   { echo "url=$(sed -n 's/^DOLIBARR__BASEURL=//p' /opt/projetos/konebras-ui/deploy/.env)"
     echo "key=$(sed -n 's/^DOLIBARR__APIKEY=//p' /opt/projetos/konebras-ui/deploy/.env)"
     echo "cnpj=47.225.005/0001-86"; } > secrets/dolibarr-api
   chown 1654:1654 secrets/dolibarr-api
   ```
   Depois do go-live, a URL passa a ser a do `adm.konebras.com.br`.
2. Rode a importação pelo serviço `importer` (perfil `import`: rede interna mais saída para a API):
   ```sh
   docker compose --profile import run --rm importer tenant import-dolibarr --tenant konebras --confirm-tenant konebras \
     --dolibarr-api /run/secrets/dolibarr-api --entities parties,contacts --operator <email> --dry-run
   ```
   Siga as seções 4 e 6 para ler o relatório, rodar a carga real e expurgar a staging. O snapshot (seções 3 e 5) só é usado se a API não estiver disponível.

## 3. Alternativa: acesso somente leitura ao MariaDB (só se a API não servir; precisa de nova autorização do PO)
O usuário `bqe_migracao_ro` vale só para `localhost` dentro do container do MariaDB do Dolibarr. Nenhum container do BemAqui entra na rede do Dolibarr.

1. Gere a senha e grave o secret no formato de opções do cliente, com permissão 0600, fora do repositório:
   ```sh
   umask 077
   printf '[client]\nuser=bqe_migracao_ro\npassword=%s\n' "$(openssl rand -base64 24)" > /opt/projetos/bemaqui-empresas-homolog/secrets/dolibarr-ro.cnf
   ```
2. Crie o usuário e aplique o GRANT por coluna (`scripts/dolibarr/grant-readonly.sql`). A senha vai pelo stdin, nunca por argumento:
   ```sh
   PW=$(sed -n 's/^password=//p' /opt/projetos/bemaqui-empresas-homolog/secrets/dolibarr-ro.cnf)
   printf "CREATE USER 'bqe_migracao_ro'@'localhost' IDENTIFIED BY '%s';\n" "$PW" | docker exec -i dolibarr-db sh -c 'mariadb -uroot -p"$MARIADB_ROOT_PASSWORD"'
   docker exec -i dolibarr-db sh -c 'mariadb -uroot -p"$MARIADB_ROOT_PASSWORD" <banco-do-dolibarr>' < scripts/dolibarr/grant-readonly.sql
   unset PW
   ```
3. Confira: `SHOW GRANTS FOR 'bqe_migracao_ro'@'localhost';` lista só `SELECT (colunas)` nas tabelas da lista fixa.

### 3.1 Snapshot cifrado (CA 3)
```sh
sh scripts/dolibarr-snapshot.sh dolibarr-db <banco-do-dolibarr> /opt/projetos/bemaqui-empresas-homolog/secrets/dolibarr-ro.cnf <destinatario-age-do-backup> /opt/projetos/bemaqui-empresas-homolog/import/snapshot-$(date +%Y%m%d%H%M).sql.age
```
O script lê com colunas explícitas dentro de `START TRANSACTION WITH CONSISTENT SNAPSHOT, READ ONLY`, grava a data e hora da extração e entrega o pacote cifrado com `age`. Nada fica em claro no disco.

### 3.2 Restauração no MariaDB temporário (CA 4)
```sh
umask 077; openssl rand -base64 24 > /opt/projetos/bemaqui-empresas-homolog/secrets/snapshot-root
sh scripts/dolibarr/restore-snapshot.sh homolog bemaqui-empresas-homolog_internal <chave-age> <snapshot.sql.age> /opt/projetos/bemaqui-empresas-homolog/secrets/snapshot-root
```
Crie o secret da conexão que a importação usa (`--snapshot`):
```sh
printf 'Server=bqe-homolog-dolibarr-snapshot;Database=snapshot;User ID=root;Password=%s\n' "$(cat /opt/projetos/bemaqui-empresas-homolog/secrets/snapshot-root)" > /opt/projetos/bemaqui-empresas-homolog/secrets/snapshot-connection
chown 1654:1654 /opt/projetos/bemaqui-empresas-homolog/secrets/snapshot-connection   # a CLI roda como 1654; o arquivo continua 0600
```

## 4. Importação: códigos de saída, relatório e carga real
Roda no serviço `migrator` (mesma imagem da API, com os secrets do catálogo e do key ring), na rede interna. Sempre uma simulação antes. O operador é o e-mail de quem executa. Com de-para, monte o arquivo e passe `--mapping`.
```sh
cd /opt/projetos/bemaqui-empresas-homolog
docker compose run --rm --no-deps -v bemaqui-empresas-homolog_files:/data/files \
  -v /opt/projetos/bemaqui-empresas-homolog/secrets/snapshot-connection:/run/secrets/snapshot-connection:ro \
  migrator tenant import-dolibarr --tenant konebras --confirm-tenant konebras \
  --snapshot /run/secrets/snapshot-connection --entities parties,contacts --operator <email> --dry-run
```
- Recusa, sem gravar nada, se o `MAIN_INFO_SIREN` do snapshot não for o CNPJ do tenant, se o `--confirm-tenant` não repetir a chave, se o tenant não existir, estiver suspenso ou com o schema defasado, e se já houver outra importação no tenant.
- Saída 0: concluída (pode ter erros de registro, listados). 1: recusada antes de gravar. 2: falha de infraestrutura (a entidade em curso foi desfeita; rode de novo).
- O relatório sai no console e em `run-<id>.json` e `.csv` em `/data/files/migration-reports` (0600). A tela Configurações → Importação do Dolibarr mostra o mesmo.
- Confira o relatório com o PO. Depois, rode sem `--dry-run`. Pode rodar de novo quantas vezes for preciso.

## 5. Limpeza do snapshot (no mesmo dia)
```sh
sh scripts/dolibarr/restore-snapshot.sh --remove homolog
rm -f /opt/projetos/bemaqui-empresas-homolog/import/snapshot-*.sql.age /opt/projetos/bemaqui-empresas-homolog/secrets/snapshot-root /opt/projetos/bemaqui-empresas-homolog/secrets/snapshot-connection
docker ps -a | grep dolibarr-snapshot   # não deve listar nada
```

## 6. Aceite e expurgo da staging (CA 22, ADR 0018)
- Com o aceite do PO: `tenant import-dolibarr --purge-staging --tenant konebras --accepted`. Apaga `import_items` e os arquivos do relatório e mantém `external_refs` e os totais em `import_runs`.
- Sem aceite, o prazo é de 30 dias. Depois disso, o alerta `migration_staging_expired` chega aos super admins todo dia, até o expurgo (`--purge-staging` sem `--accepted`).

## 7. Go-live (GOL-01)
Repete as seções 2, 4 e 6 em produção. Depois da carga final, apague o `secrets/dolibarr-api`. Se o snapshot tiver sido usado, faça também `DROP USER 'bqe_migracao_ro'@'localhost';` e apague o `dolibarr-ro.cnf`.
