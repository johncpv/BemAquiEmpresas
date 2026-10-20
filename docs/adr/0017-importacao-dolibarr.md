# ADR 0017: Importação do Dolibarr

- Status: Aceito (18/10/2026). O PO (John) aprovou as decisões D3 e D4 da revisão pré-Fase 1 e definiu o escopo: **a migração traz todos os cadastros e nenhum lançamento.**
- Contexto: a MIG-01a deixava em aberto a forma de leitura do MariaDB do Dolibarr, a rede até ele, onde fica o código e se há tela. A revisão `docs/revisoes/2026-10-18-revisao-pre-fase1.md` (D2 a D5, §2.1 e §2.5) recomendou leitura por snapshot, módulo próprio e tela só de leitura. O PO decidiu que saldos e lançamentos começam no sistema novo.
- Complementa as ADRs 0006 (fronteira de módulos), 0007 (segredos), 0015 (segredos em arquivo e rede compartilhada) e 0018 (LGPD).

## Decisão

### Escopo: só cadastros
- **Migra todo cadastro que tem destino no BemAqui:**
  - terceiros (`llx_societe`), com papéis, documento e endereço de cobrança;
  - contatos (`llx_socpeople`);
  - condições de pagamento (`llx_c_payment_term`), por de-para com as do seed, sem duplicar;
  - contas bancárias e caixas (`llx_bank_account`), **só o cadastro**: nome, banco, agência, conta e tipo. A conta entra sem saldo inicial e sem `opening_date`, até o usuário informar na CAD-05b;
  - unidades (`llx_c_units`), categorias de produto (`llx_categorie` tipo produto e `llx_categorie_product`), itens (`llx_product` e o extrafield `elev_garantia`), códigos do fornecedor (`llx_product_fournisseur_price`, sem preço) e locais (`llx_entrepot`).
- **Não migra nada que seja lançamento ou financeiro transacional:** títulos a receber e a pagar, pagamentos, recebimentos, movimentos bancários, saldos bancários e saldos de estoque. Tabelas fora: `llx_facture*`, `llx_paiement*`, `llx_paiementfourn*`, `llx_bank`, `llx_product_stock`, `llx_product_batch`, `llx_product_lot`, `llx_stock_mouvement`.
- **Não migram também** (sem destino na Fase 1, ou por minimização):
  - orçamentos, pedidos, OS, BOM/OF e projetos (`llx_propal`, `llx_commande`, `llx_mrp_mo`, `llx_bom_bom`, `llx_projet`);
  - usuários (`llx_user`): são convidados no BemAqui (FND-06c);
  - funcionários do módulo RH do Dolibarr, se houver: são cadastrados na CAD-04b, por minimização de dado pessoal;
  - plano contábil do Dolibarr (`llx_accounting_account`): o plano gerencial nasce do seed da CAD-05a;
  - modos de pagamento (`llx_c_paiement`): só fazem sentido no título (Fase 2);
  - contas bancárias de terceiros (`llx_societe_rib`), IBAN, BIC e titular;
  - preços de venda e de compra (`llx_product_price`, colunas de preço do `llx_product_fournisseur_price`), aniversário (`birthday`) e anexos.
- **Categorias de terceiro** (`llx_categorie` tipo cliente ou fornecedor): não viram cadastro, porque o terceiro não tem categoria no BemAqui. Podem ser usadas como fonte do de-para de papéis (Instalador, Transportadora), se o PO indicar no arquivo de de-para.
- Tudo o que não migra continua consultável no Dolibarr em `adm.konebras.com.br` até o desligamento (ADR 0004).
- A GOL-01 repete só a carga dos cadastros.

### Leitura por snapshot (D3)
- Nenhum container do BemAqui conecta no banco do Dolibarr em produção nem entra na rede dele.
- **Usuário somente leitura** `bqe_migracao_ro`, restrito por host (`'bqe_migracao_ro'@'localhost'` ou o IP do container do script), com `GRANT SELECT` **por coluna**, só nas colunas mapeadas da lista fixa de tabelas.
  - `llx_const`: só as colunas `name`, `value` e `entity`, e o script lê só a linha `MAIN_INFO_SIREN`.
- **Extração:** o script `scripts/dolibarr-snapshot.sh` roda na VPS com esse usuário, executa um `SELECT` com colunas explícitas por tabela dentro de `START TRANSACTION WITH CONSISTENT SNAPSHOT, READ ONLY` e grava os arquivos num pacote cifrado com `age` (mesma chave do backup, ADR 0015). Nada fica em claro no disco.
- **Restauração:** o pacote é aberto num MariaDB temporário (`bqe-<ambiente>-dolibarr-snapshot`) na rede interna do projeto, com o DDL fixo do repositório (só as colunas mapeadas). O importador lê dele com as mesmas colunas explícitas.
- Depois da carga, o container e o volume do snapshot são removidos e o pacote é apagado.
- A credencial do usuário somente leitura fica num arquivo de secret (ADR 0015), nunca em variável de ambiente nem em argumento.
- Na GOL-01, depois da carga final: `DROP USER bqe_migracao_ro` e remoção do secret.

### Módulo `Migration`
- Projeto novo em `src/Modules/Migration/`, com as 5 camadas. Fica **fora do `ModuleCatalog`** e não tem rota HTTP de escrita. O teste de arquitetura passa a contar 14 módulos.
- A escrita nos destinos é feita por interfaces `I*Import` (ex.: `IPartyImport`, `IPaymentTermImport`, `IBankAccountImport`, `IItemImport`, `ILocationImport`) nos `*.Contracts` dos módulos de destino.
  - Só o `Migration` pode referenciar essas interfaces (teste de arquitetura).
  - A implementação chama os mesmos handlers da Application, com as mesmas validações da tela.
- O registro importado guarda `import_source` (ex.: `dolibarr:llx_societe:42`) na própria tabela de destino, gravado só pelo `I*Import`. O detalhe mostra o chip "Importado do Dolibarr" com a referência de origem.
- Staging no schema `migration`: `import_runs`, `import_items` e `external_refs`.

### Execução
- Só pela CLI, na mesma imagem da API: `tenant import-dolibarr --tenant <key> --confirm-tenant <key> --snapshot <arquivo> --entities <lista> --operator <email> [--dry-run] [--mapping <arquivo>]`.
- **Proteção contra tenant errado:** o `MAIN_INFO_SIREN` do snapshot precisa ser igual ao CNPJ do tenant no catálogo, e `--confirm-tenant` precisa repetir a chave. Se não bater, o comando recusa antes de gravar.
- **Idempotência:** por `migration.external_refs` (`source`, tabela, id de origem), com o hash do conteúdo e o `xmin` da linha de destino gravados.
  - Mesmo hash: "sem alteração".
  - Hash diferente e `xmin` igual: "atualizado".
  - `xmin` diferente (o registro foi editado no BemAqui): não sobrescreve e sai como "conflito", com os dois valores.
- **`--dry-run`:** executa tudo, inclusive as gravações, numa transação desfeita no fim. O relatório é igual ao real.
- **Auditoria:** usuário de sistema "Importação Dolibarr", com o operador (`--operator`) e o id da execução registrados.
- **Primeira carga real:** na homologação. Em homologação, as cargas são ensaio; a que vale é a da GOL-01.
- **Contagens:** o relatório confere com o total lido na extração, e não com números fixos.

### Relatório e tela (D4)
- O relatório sai no console, em JSON e em CSV, e fica em `migration.import_runs`.
  - O CSV é protegido contra injeção de fórmula: célula que começa com `=`, `+`, `-`, `@`, tabulação ou retorno de carro ganha o prefixo `'`.
  - Os arquivos são gravados com permissão 0600 e apagados depois do aceite do PO.
  - CPF, e-mail e telefone saem mascarados; credencial nunca sai.
- **Tela só de leitura** "Configurações → Importação do Dolibarr" (`settings.view`): execuções, totais por entidade e pendências (erros, avisos, conflitos) com link para o registro. A carga continua só pela CLI.

## Consequências
- A MIG-01a entrega o módulo, o snapshot, o executor, o relatório e a tela. A MIG-01b e a MIG-01c entregam as cargas.
- A MIG-01d (saldos de estoque) e a MIG-01e (títulos) foram canceladas pelo PO em 18/10/2026. A D2 e a D5 da revisão ficam sem efeito. O saldo inicial de estoque é digitado na EST-01 (`OpeningBalance`, ADR 0016) e o saldo das contas na CAD-05b.
- O histórico de pontualidade dos clientes (R-TER-4) começa com os títulos do BemAqui.
- A staging tem prazo de expurgo (ADR 0018).

## Adendo (MIG-01a, 18/10/2026): detalhes de implementação
- **Nome do módulo:** a pasta e os projetos se chamam `Migrations` (`BemAqui.Migrations.*`). Com `BemAqui.Migration`, o namespace encobria a classe `Migration` do EF Core em todo arquivo de migration do Host. O schema continua `migration` e a contagem continua em 14 módulos.
- **`--snapshot`:** recebe o caminho do arquivo de secret com a conexão do MariaDB temporário (`bqe-<ambiente>-dolibarr-snapshot`), e não o pacote cifrado. O pacote é aberto antes, por `scripts/dolibarr/restore-snapshot.sh`. Assim a credencial nunca vai em argumento (CA 5).
- **Formato do snapshot:** `scripts/dolibarr/snapshot.sql` gera um `INSERT` com colunas explícitas por linha (`QUOTE` e `CONVERT ... USING utf8mb4`), lido pelo cliente `mariadb --batch --raw` e entregue direto ao `age`. A primeira linha grava a data e hora da extração em `bqe_snapshot`.
- **Lista fixa única:** tabelas e colunas ficam em `SnapshotTables` (C#). O GRANT, o snapshot e o DDL de `scripts/dolibarr/` são gerados dela, e um teste reprova se os arquivos divergirem.
- **Coluna `entity`:** entra no GRANT das tabelas que a têm, para o filtro `entity = 1`.
- **`idprof1` a `idprof4`:** no Dolibarr padrão, as colunas físicas de `llx_societe` são `siren`, `siret`, `ape` e `idprof4`. Elas são confirmadas no inventário da base real (CA 1).
- **Staging:** `import_items` guarda também `entity`, `warning` (aviso à parte da soma) e `differences` (os dois valores do conflito). As colunas pessoais saem mascaradas no `payload` e nas diferenças.
- **Relatório:** os arquivos ficam em `Migration:ReportDirectory`, por padrão `/data/files/migration-reports`, no volume da API. O `--purge-staging --accepted` registra o aceite do PO e expurga.
- **Alerta:** o recorrente global `migration-staging-check` roda todo dia às 08:00 de São Paulo e enfileira, por tenant, o job que emite `migration_staging_expired`.
- **Risco aceito:** o GRANT em `llx_const` (`name`, `value`, `entity`) permite ao usuário somente leitura ler outras constantes, mas o script lê só `MAIN_INFO_SIREN`. O usuário existe só até a GOL-01, que faz o `DROP USER`.

## Adendo 2 (18/10/2026, decisão do PO): leitura pela API REST do Dolibarr
- **Decisão:** a importação lê o Dolibarr pela **API REST** (`api/index.php`), com a mesma chave (`DOLAPIKEY`) que o Konebras UI já usa. O PO autorizou em 18/10/2026. O snapshot no MariaDB temporário continua no código como alternativa (`--snapshot`), mas deixa de ser o caminho padrão.
- **Por quê:** não cria usuário nem GRANT no MariaDB do Dolibarr em produção. O BemAqui também não entra na rede do Dolibarr: a leitura é por HTTPS, só GET, como a de qualquer cliente da API.
- **Como:**
  - CLI: `tenant import-dolibarr ... --dolibarr-api <arquivo-de-secret>`. O arquivo tem `url=`, `key=` e `cnpj=`, com permissão 0600, e roda no serviço `importer` do compose (perfil `import`, rede interna mais saída).
  - A lista fixa (`SnapshotTables`) continua sendo o contrato: cada tabela é montada do JSON da API só com as colunas mapeadas (`DolibarrApiMap`) e com os tipos do DDL fixo. O resto do JSON (IBAN, BIC, saldo, preços, custo, estoque) é descartado na leitura e não chega à staging, ao log nem ao relatório.
  - Tabelas sem endpoint próprio saem de outros objetos da API: os tipos de terceiro vêm dos próprios terceiros, os vínculos de categoria de `/categories/{id}/objects` e a garantia do extrafield `options_elev_garantia` do produto.
- **Proteção contra tenant errado (CA 6):** a chave não lê `/setup/company` nem `MAIN_INFO_SIREN` (403). O CNPJ da empresa daquele Dolibarr vai no arquivo de secret (`cnpj=`), escrito uma vez por quem configura, e é comparado com o CNPJ do tenant no catálogo. Continua valendo o `--confirm-tenant`.
- **Consistência (CA 3):** a API não tem transação consistente. Vale a hora do início da leitura. O risco é baixo: o volume é pequeno (inventário de 18/10/2026: 118 terceiros, 0 contatos, 34 produtos, 4 depósitos, 3 contas, 13 condições, 7 categorias de fornecedor), o Dolibarr é pouco usado durante a carga e a reexecução é idempotente.
- **Risco aceito:** a chave é de um usuário do Dolibarr sem perfil de admin, mas que pode ter permissão de escrita. O BemAqui só faz GET, e há teste que reprova qualquer outro método. Na GOL-01, a chave deixa de ser usada pelo BemAqui e o secret é apagado.
- **Inventário (CA 1):** feito pela API em 18/10/2026, no Dolibarr 21.0.4. Os campos usados estão em `DolibarrApiMap` e no runbook. O PO registrou o aceite do mapeamento.

- Ver ADR 0020 (na GOL-01 a carga final traz só os cadastros alterados depois da última carga; o financeiro já está no banco promovido da homologação).
