# MIG-01a — Mapeamento e infraestrutura de importação do Dolibarr

| Campo | Valor |
|---|---|
| ID | MIG-01a |
| Título | Mapeamento dos cadastros do Dolibarr, snapshot cifrado com usuário somente leitura e GRANT por coluna, módulo `Migration` com staging e chaves externas, simulação, relatório e tela só de leitura da importação |
| Tamanho | G (quebra da MIG-01 em infraestrutura e cargas: MIG-01b terceiros e cadastros financeiros, MIG-01c itens e locais) |
| Status | Ready |
| Requisitos | R-TER-5 (importação do Dolibarr) · PRD §7 Fase 1 ("dados migrados") e §1.2 (Dolibarr desligado) · PRD §6 Segurança e LGPD · R-SEC-5 |
| Dependências | Fase 0 (FND-03a CLI, FND-07 auditoria, FND-11 segredos, FND-12 alertas, FND-10a compose e secrets), FND-09d (tela da importação). Não depende dos cadastros: as cargas (MIG-01b/01c) é que esperam os destinos |
| Bloqueia | MIG-01b, MIG-01c, GOL-01 (carga final) |
| Ordem de execução | 2 (Marco A — terceiros reais) |

## User story
Como **proprietário**, quero **trazer do Dolibarr todos os cadastros com um processo que eu possa rodar de novo sem duplicar, e ver na tela o que entrou, o que ficou de fora e por quê**, para **desligar o Dolibarr com segurança, conferindo tudo antes do go-live**.

## Contexto e referências
- PRD R-TER-5, §7 (Fase 1: "dados migrados"; go-live e desligamento do Dolibarr na Fase 5).
- **ADR 0017** (escopo só de cadastros, snapshot, GRANT por coluna, módulo `Migration`, `I*Import`, idempotência por `xmin`, `--dry-run`, `MAIN_INFO_SIREN`, CSV seguro, tela só de leitura, `DROP USER` na GOL-01) e **ADR 0018** (retenção da staging e dos arquivos do relatório).
- Decisão do PO (18/10/2026): **a migração traz todos os cadastros, inclusive condições de pagamento e contas bancárias e caixas sem saldo. Não traz lançamentos nem financeiro transacional** (títulos, pagamentos, recebimentos, movimentos bancários, saldos bancários e de estoque). A MIG-01d e a MIG-01e foram canceladas.
- `docs/06-pendencias-riscos.md` P-07 (volume em 16/10/2026; os CAs conferem com o total lido na extração, não com esses números), P-11 (`elev_garantia`).
- ADR 0004 e ADR 0005 D1 (o Dolibarr continua no ar até o go-live), ADR 0006, ADR 0015 (segredos em arquivo, nomes únicos na rede, `check-compose.mjs`).
- Código existente: modo CLI do Host (`CliCommand`), `SecretFiles`, `IntegrationHealth`/`Alerts`, `AuditSaveChangesInterceptor`, `DataTable`.

## Mapeamento (a documentar e validar com o PO)
Entregável: `docs/operacao/migracao-dolibarr.md`, com tabela → entidade, coluna → campo, regra de conversão, filtros (ex.: `entity = 1`), o que não migra e o runbook. Ponto de partida (nomes do Dolibarr padrão; conferir na base real, que pode ter extrafields e versão diferentes). O GRANT e o snapshot cobrem **só estas colunas**:

| Dolibarr (colunas mapeadas) | BemAqui | Carga |
|---|---|---|
| `llx_societe` (`rowid`, `nom`, `name_alias`, `client`, `fournisseur`, `idprof1`…`idprof4`, `siren`, `siret`, `tva_intra`, `address`, `zip`, `town`, `fk_departement`, `fk_pays`, `phone`, `email`, `note_private`, `note_public`, `status`, `cond_reglement`, `fk_typent`, `tms`) | `parties.parties`, `party_roles`, `party_addresses` (Billing) | MIG-01b |
| `llx_socpeople` (`rowid`, `fk_soc`, `lastname`, `firstname`, `poste`, `email`, `phone`, `phone_mobile`, `address`, `zip`, `town`, `statut`, `tms`) | `parties.party_contacts` | MIG-01b |
| `llx_c_payment_term` (`rowid`, `code`, `libelle`, `nbjour`, `type_cdr`, `decalage`, `active`) | `finance.payment_terms` (de-para com o seed) | MIG-01b |
| `llx_bank_account` (`rowid`, `ref`, `label`, `bank`, `code_banque`, `code_guichet`, `number`, `cle_rib`, `courant`, `clos`) — só cadastro, sem saldo | `finance.bank_accounts` | MIG-01b |
| `llx_c_departements` (`rowid`, `code_departement`), `llx_c_typent` (`id`, `code`) | conversão de UF e de PF/PJ (não viram cadastro) | MIG-01b |
| `llx_categorie` (`rowid`, `label`, `type`, `fk_parent`) e `llx_categorie_fournisseur` (`fk_categorie`, `fk_soc`) | fonte opcional do de-para de papéis (Instalador, Transportadora) | MIG-01b |
| `llx_c_units` (`rowid`, `code`, `label`, `active`) | `inventory.units` (de-para) | MIG-01c |
| `llx_categorie` (tipo produto) e `llx_categorie_product` (`fk_categorie`, `fk_product`) | `inventory.item_categories` e `items.category_id` | MIG-01c |
| `llx_product` (`rowid`, `ref`, `label`, `fk_product_type`, `fk_unit`, `customcode`, `fk_country`, `seuil_stock_alerte`, `tobatch`, `tosell`, `tobuy`, `tms`) e `llx_product_extrafields` (`fk_object`, `elev_garantia`) | `inventory.items` | MIG-01c |
| `llx_product_fournisseur_price` (`rowid`, `fk_product`, `fk_soc`, `ref_fourn`, `quantity`, `fk_unit`) — sem colunas de preço | `inventory.item_supplier_codes` | MIG-01c |
| `llx_entrepot` (`rowid`, `ref`, `label`, `lieu`, `statut`, `fk_parent`) | `inventory.locations` | MIG-01c |
| `llx_const` (`name`, `value`, `entity`), só a linha `MAIN_INFO_SIREN` | proteção contra tenant errado | MIG-01a |

**Não migram** (ADR 0017; decidido em 18/10/2026):
- **lançamentos e financeiro transacional:** faturas e títulos (`llx_facture`, `llx_facture_fourn`), pagamentos e recebimentos (`llx_paiement*`, `llx_paiementfourn*`), movimentos e saldos bancários (`llx_bank`), saldos, lotes e movimentos de estoque (`llx_product_stock`, `llx_product_batch`, `llx_product_lot`, `llx_stock_mouvement`), custo médio (`pmp`);
- **documentos e cadastros sem destino na Fase 1:** orçamentos, pedidos, OS, BOM/OF e projetos (`llx_propal`, `llx_commande`, `llx_mrp_mo`, `llx_bom_bom`, `llx_projet`); usuários (`llx_user`, convidados no BemAqui); funcionários do RH do Dolibarr, se houver (cadastrados na CAD-04b); plano contábil (`llx_accounting_account`, o plano gerencial vem do seed da CAD-05a); modos de pagamento (`llx_c_paiement`, Fase 2); contas bancárias de terceiros (`llx_societe_rib`); preços (`llx_product_price` e preço do fornecedor); `birthday`, IBAN, BIC, titular; anexos.
- Ficam consultáveis no Dolibarr em `adm.konebras.com.br` até o desligamento.

## Critérios de aceite
**Mapeamento**
1. **Dado** o inventário das tabelas e colunas reais da base do Dolibarr, **quando** o mapeamento é revisado, **então** `docs/operacao/migracao-dolibarr.md` lista, para cada entidade, a tabela de origem, as colunas, as regras de conversão, os filtros, o de-para e o que não migra (com o motivo), e o PO registra o aceite no próprio documento antes da primeira carga real (passo manual verificável).

**Snapshot e acesso somente leitura**
2. **[Segurança]** **Dado** o usuário `bqe_migracao_ro`, restrito por host e com `GRANT SELECT` só nas colunas mapeadas, **quando** o script tenta ler uma coluna fora da lista, gravar (`INSERT`, `UPDATE`, `DELETE`) ou rodar DDL, **então** o MariaDB recusa (teste com Testcontainers MariaDB e o mesmo script de GRANT do runbook).
3. **[Segurança]** **Dado** o script `scripts/dolibarr-snapshot.sh`, **quando** roda, **então** executa um `SELECT` com colunas explícitas por tabela da lista fixa, dentro de `START TRANSACTION WITH CONSISTENT SNAPSHOT, READ ONLY`, grava a data e hora da extração e entrega um pacote cifrado com `age`; nada fica em claro no disco.
4. **[Segurança]** **Dado** o pacote cifrado, **quando** a importação roda, **então** ele é aberto num MariaDB temporário (`bqe-<ambiente>-dolibarr-snapshot`) na rede interna do projeto, com o DDL fixo do repositório; nenhum container do BemAqui conecta no banco do Dolibarr nem entra na rede dele; ao terminar, o container, o volume e o pacote são removidos (o runbook tem o comando e a verificação).
5. **[Segurança]** **Dado** a credencial do MariaDB, **quando** usada, **então** vem de um arquivo de secret (ADR 0015), nunca de variável de ambiente nem de argumento, e não aparece em log, relatório, exceção ou tabela.

**Proteção contra tenant errado**
6. **[Segurança]** **Dado** um snapshot cujo `MAIN_INFO_SIREN` é diferente do CNPJ do tenant no catálogo, ou um `--confirm-tenant` que não repete a chave de `--tenant`, **quando** rodo a importação, **então** o comando recusa com a mensagem em PT-BR e código de saída ≠ 0, sem gravar nada.
7. **Dado** um tenant inexistente, suspenso ou com schema defasado, **quando** rodo, **então** o comando recusa com a mensagem e código ≠ 0.

**Módulo, staging e chaves externas**
8. **Dado** o módulo `Migration` (5 camadas, fora do `ModuleCatalog`), **quando** o teste de arquitetura roda, **então** ele conta 14 módulos, o `Migration` grava nos destinos só pelas interfaces `I*Import` dos `*.Contracts`, só ele referencia essas interfaces, e não há rota HTTP de escrita no módulo.
9. **Dado** uma extração, **quando** a carga começa, **então** cada linha de origem fica em `migration.import_items` com a tabela e o id de origem, o conteúdo (jsonb) e o hash, ligada à execução em `migration.import_runs`.
10. **Dado** um registro criado no destino, **quando** a carga grava, **então** `migration.external_refs` guarda (`source = dolibarr`, tabela, id de origem) → (tipo e id no BemAqui, hash da origem, `xmin` da linha de destino, execução), com a chave de origem única; o destino guarda `import_source` (ex.: `dolibarr:llx_societe:42`).

**Reexecução sem duplicar**
11. **Dado** uma carga já feita, **quando** rodo de novo com o mesmo snapshot, **então** nada é duplicado e todos os registros saem como "sem alteração".
12. **Dado** um registro alterado no Dolibarr e não editado no BemAqui (mesmo `xmin`), **quando** rodo de novo, **então** ele é atualizado e sai como "atualizado".
13. **Dado** um registro importado e depois editado no BemAqui (`xmin` diferente), **quando** rodo de novo com a origem alterada, **então** o BemAqui não é sobrescrito e o relatório mostra "conflito" com os dois valores.

**Simulação, transação e concorrência**
14. **Dado** `--dry-run`, **quando** rodo, **então** tudo é executado, inclusive as gravações, numa transação desfeita no fim; o relatório é igual ao de uma execução real e as contagens dos destinos antes e depois são iguais.
15. **Dado** um erro num registro (ex.: CNPJ inválido), **quando** a carga roda, **então** o registro vai para o relatório com o id de origem e o motivo em PT-BR, e os demais seguem; uma falha de infraestrutura desfaz a entidade em curso e encerra com código ≠ 0.
16. **Dado** uma importação em andamento no tenant, **quando** inicio outra, **então** a segunda recusa com "Já existe uma importação em andamento neste tenant." (advisory lock).

**Relatório**
17. **Dado** o fim de uma execução, **quando** abro o relatório, **então** vejo por entidade o total lido na extração e, somando exatamente esse total: importados, atualizados, sem alteração, mesclados, vinculados a cadastro existente, ignorados (com motivo), conflitos e erros; avisos ficam à parte. O relatório sai no console, em JSON e em CSV e fica em `migration.import_runs`.
18. **[Segurança]** **Dado** o CSV do relatório, **quando** uma célula começa com `=`, `+`, `-`, `@`, tabulação ou retorno de carro, **então** ela é gravada com o prefixo `'`; o relatório não contém CPF, e-mail nem telefone completos (mascarados), nem credencial; os arquivos são gravados com permissão 0600.

**Tela só de leitura (D4)**
19. **Dado** `settings.view`, **quando** abro Configurações → Importação do Dolibarr, **então** vejo as execuções (data, modo, operador, situação) e, por execução, os totais por entidade e a lista de pendências (erros, avisos e conflitos) com o motivo e o link para o registro no BemAqui quando existir; não há botão de executar. Sem `settings.view`, 403 `forbidden`; com o módulo `settings` não liberado, 403 `module_not_released`.

**Conversões comuns**
20. **Dado** valores do Dolibarr (`double`/`decimal`, datas sem fuso, texto em `latin1` ou `utf8`), **quando** são convertidos, **então** quantidades viram `numeric(18,4)` com `AwayFromZero`; datas sem hora viram `date` e datas com hora são lidas como America/Sao_Paulo e gravadas em UTC; textos são normalizados para UTF-8 sem perder acento e sem caracteres de controle e bidi.

**Auditoria e LGPD**
21. **[Segurança]** **Dado** um registro criado ou atualizado pela importação, **quando** abro o histórico dele, **então** a auditoria mostra o usuário de sistema "Importação Dolibarr", o operador (`--operator`) e o id da execução; campos `[PersonalData]` seguem mascarados.
22. **[Segurança]** **Dado** a staging, **quando** o PO registra o aceite da carga ou passam 30 dias da carga (o que vier primeiro), **então** `--purge-staging` apaga `import_items` e os arquivos do relatório e mantém `external_refs` e `import_runs` sem conteúdo; aos 30 dias sem expurgo, o alerta `migration_staging_expired` dispara (FND-12). A staging não é exposta por nenhum endpoint além dos totais e pendências da tela.

## Regras de negócio e técnicas
- **Execução:** só pela CLI da imagem da API (ADR 0017): `tenant import-dolibarr --tenant <key> --confirm-tenant <key> --snapshot <arquivo> --entities <lista> --operator <email> [--dry-run] [--mapping <arquivo>]` e `tenant import-dolibarr --purge-staging --tenant <key>`. Roda como container avulso na VPS, com o secret montado.
- **Ordem das entidades:** terceiros → contatos → condições de pagamento → contas bancárias (MIG-01b) → unidades → categorias → locais → itens → códigos do fornecedor (MIG-01c). Cada carga é um `IDolibarrLoader` registrado no executor desta story.
- **Transação:** uma por entidade (lotes de até 500); a execução é reiniciável.
- **Primeira carga real:** na homologação (ensaio). A carga que vale é a da GOL-01, que repete só os cadastros e depois faz `DROP USER bqe_migracao_ro` e remove o secret.
- **Arquivo de de-para** (`--mapping`, JSON validado): unidades, tipos de item, papéis especiais (ids de instaladores e transportadoras) e condições de pagamento, para o PO ajustar sem mudar código.
- **Driver MariaDB:** `MySqlConnector` (MIT), pela cadeia de suprimentos da FND-01.
- **Ids:** Guid v7. **Datas** em UTC (`timestamptz`).

## Permissões, eventos e tabelas
- **Permissões:** nenhuma ação nova. A tela usa `settings.view`. A execução é só pela CLI, com acesso à VPS.
- **Eventos:** os que cada módulo de destino já publica ao criar o registro; a migração não cria evento próprio.
- **Tabelas (schema `migration`, criadas nesta story):**
  - `import_runs`: `started_at`, `finished_at`, `mode` (DryRun/Real), `entities`, `status` (Running/Succeeded/Failed), `summary` (jsonb), `operator`, `snapshot_taken_at`, `accepted_at`, `purged_at`;
  - `import_items`: `run_id`, `source_table`, `source_id`, `payload` (jsonb), `hash`, `outcome` (Imported/Updated/Unchanged/Merged/Linked/Skipped/Conflict/Error), `target_type`, `target_id`, `message`;
  - `external_refs`: `source`, `source_table`, `source_id` (únicos juntos), `target_type`, `target_id`, `source_hash`, `target_xmin`, `imported_at`, `last_run_id`.

## Telas e estados
- **Configurações → Importação do Dolibarr** (`/app/configuracoes/importacao`), aba de `settings` depois de IA: lista de execuções (DataTable) e detalhe da execução com totais por entidade e pendências (filtros por entidade e tipo de pendência na URL).
- **Estados:** carregando; vazio ("Nenhuma importação feita. A carga é feita pela CLI; veja o passo a passo na ajuda."); vazio filtrado; erro com "Tentar de novo". Sem ações de escrita.
- **Ajuda:** `Ajuda/configuracoes-importacao.md` (aba `importacao`), com "como ler o relatório" e "o que não migra". O passo a passo técnico fica no runbook `docs/operacao/migracao-dolibarr.md`.

## Plano de testes
- **Unidade:** conversões (CA 20); hash estável; classificação do resultado por hash e `xmin` (CA 11–13); máscara e proteção contra fórmula do CSV (CA 18); comparação do `MAIN_INFO_SIREN` (CA 6).
- **Integração (Testcontainers PostgreSQL + MariaDB):** fixture SQL anonimizada com o DDL fixo; GRANT por coluna recusando coluna fora da lista e escrita (CA 2); snapshot consistente (CA 3); tenant errado e suspenso (CA 6, 7); staging e chaves externas (CA 9, 10); reexecução, atualização e conflito com um carregador de teste (CA 11–13); simulação desfeita (CA 14); erro de registro × de infraestrutura (CA 15); lock (CA 16); relatório que fecha a soma com o total lido (CA 17); expurgo e alerta (CA 22); tela com e sem permissão (CA 19).
- **Arquitetura:** 14 módulos e `I*Import` só no `Migration` (CA 8); credencial fora do log (CA 5).
- **E2E (Playwright):** tela da importação com uma execução de teste, pendência com link (CA 19).
- **Ensaio manual:** snapshot e simulação com a base real na VPS (pacote cifrado, MariaDB temporário), conferência com o PO e remoção do snapshot (CA 4).

## Fora do escopo
- As cargas por entidade: MIG-01b e MIG-01c.
- Lançamentos e financeiro transacional (decisão do PO de 18/10/2026; MIG-01d e MIG-01e canceladas).
- Importação por planilha genérica para outros sistemas.
- Carga final no go-live: GOL-01 (reusa esta infraestrutura).

## Pontos em aberto
- Decidido em 18/10/2026 (D3, D4, tabela 1.1 e decisão do PO sobre o escopo, ADR 0017): leitura por snapshot com GRANT por coluna; módulo `Migration`; tela só de leitura; lista do que não migra; reexecução não sobrescreve o que foi editado no BemAqui (conflito no relatório); primeira carga na homologação; usuário de sistema "Importação Dolibarr" com o operador registrado; expurgo da staging após o aceite e em até 30 dias (ADR 0018).
- **Pré-condição externa (não bloqueia o `Ready`):** o aceite do mapeamento pelo PO (CA 1), feito durante a story.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (PostgreSQL + MariaDB), arquitetura e E2E passando.
- [ ] Migration do schema `migration` criada (não destrutiva).
- [ ] Nenhuma permissão nova (tela com `settings.view`).
- [ ] `docs/operacao/migracao-dolibarr.md` escrito (mapeamento, GRANT, snapshot, runbook) e aceito pelo PO.
- [ ] Tela com estados de carregando, vazio e erro; ajuda escrita; teste de cobertura passando.
- [ ] Auditoria ligada nas gravações da importação.
- [ ] Credencial só em secret; nada em log.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` conferido (schema `migration`); story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Inventário das colunas reais (com o PO), mapeamento e script de GRANT por coluna.
- [ ] `scripts/dolibarr-snapshot.sh`, DDL fixo do snapshot e restauração no MariaDB temporário.
- [ ] Módulo `Migration` (5 camadas), schema `migration`, executor com lock, transação por entidade, `--dry-run` e relatório (console, JSON, CSV seguro).
- [ ] `IDolibarrLoader` e a convenção `I*Import`; regra de arquitetura.
- [ ] Comando CLI, `--purge-staging` e alerta de prazo; endpoints de leitura da tela; tela e ajuda; runbook.

## Quality gates
- Agentes previstos: @dev, @data-engineer (mapeamento e conversões), cybersecurity (GRANT, snapshot, credencial, LGPD da staging), @qa.
- Gate: CA 2, 4, 6, 11, 13 e 14 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 18/10/2026.

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo (cabeçalho, user story, contexto, CAs, regras, permissões/tabelas, telas, testes, fora do escopo, pontos em aberto, DoD, tarefas) | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas do CLAUDE.md) que se aplicam | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK |
| 7 | Plano de testes cobre os CAs (unidade, integração com Testcontainers, arquitetura, E2E quando cabe) | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas como proposta) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD do CLAUDE.md ajustado; pontos em aberto registrados sem decidir | OK |

- **Correções feitas na validação (18/10/2026):** escopo reduzido a cadastros (sem `llx_facture*`, `llx_paiement*`, `llx_bank` e `llx_product_stock`), com `llx_bank_account` só nas colunas de cadastro e `llx_c_payment_term`; snapshot com GRANT por coluna no lugar da conexão direta; módulo `Migration`; idempotência por `xmin`; `--dry-run` com transação desfeita; proteção por `MAIN_INFO_SIREN`; CSV seguro; tela só de leitura; contagens pelo total lido; expurgo da staging (ADR 0018).
- **Pré-condições para `Ready`:** nenhuma pendente. O aceite do mapeamento é CA (CA 1).
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 18/10/2026 | 0.1 | Story detalhada (quebra da MIG-01) | River (SM) |
| 18/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 18/10/2026 | 0.3 | Decisões do PO: só cadastros (ADR 0017), D3, D4, D8; correções da revisão pré-Fase 1; ordem 2 do Marco A | River (SM) |
| 18/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
