# MIG-01a — Mapeamento e infraestrutura de importação do Dolibarr

| Campo | Valor |
|---|---|
| ID | MIG-01a |
| Título | Mapeamento documentado das tabelas do Dolibarr, leitura somente leitura do MariaDB, área de preparação (staging), chaves externas para reexecução sem duplicar, simulação e relatório de importação |
| Tamanho | G (quebra da MIG-01 em infraestrutura e cargas por entidade: MIG-01b terceiros, MIG-01c itens e locais, MIG-01d saldos, MIG-01e títulos) |
| Status | Draft |
| Requisitos | R-TER-5 (importação do Dolibarr por CSV/consulta ao banco) · PRD §7 Fase 1 ("dados migrados") e §1.2 (Dolibarr desligado) · PRD §6 Segurança e LGPD · R-SEC-5 |
| Dependências | Fase 0 (FND-03a migrator/CLI, FND-07 auditoria e outbox, FND-11 segredos, FND-12 alertas, FND-10a compose e secrets). Pode começar em paralelo depois da CAD-06. **Pré-condição:** pontos em aberto 1, 2 e 3 |
| Bloqueia | MIG-01b, MIG-01c, MIG-01d, MIG-01e, GOL-01 (carga final) |
| Ordem de execução | 11 (∥ desde a ordem 2) |

## User story
Como **proprietário**, quero **trazer do Dolibarr os cadastros e os saldos com um processo que eu possa rodar de novo sem duplicar, e ver um relatório do que entrou, do que ficou de fora e por quê**, para **desligar o Dolibarr com segurança, conferindo os números antes do go-live**.

## Contexto e referências
- PRD R-TER-5 (importação do Dolibarr — clientes, fornecedores, produtos — por CSV/consulta ao banco), §7 (Fase 1: "dados migrados"; go-live e desligamento do Dolibarr na Fase 5).
- Backlog MIG-01 (CA): relatório de importação (importados, ignorados, erros); reexecução não duplica; saldos de estoque entram como movimento "Saldo inicial". Nota: primeiro mapear as tabelas do Dolibarr com o PO.
- `docs/06-pendencias-riscos.md` **P-07** (resolvida: acesso via VPS; volume: 118 terceiros, 34 produtos, 16 orçamentos, 4 pedidos, 26 faturas, 164 faturas de fornecedor, 166 lançamentos bancários, 4 OS, 4 BOM/OF, estoque zerado); risco "dados do Dolibarr inconsistentes" (importação com relatório de erros, reexecutável); **P-11** (`elev_garantia` entra na migração).
- ADR 0004 e ADR 0005 D1: o Dolibarr continua no ar (em `app.konebras.com.br` até o go-live e depois em `adm.konebras.com.br`) e só é desligado no fim da Fase 5. O banco é o MariaDB do container `dolibarr-db`, na mesma VPS.
- Revisão pré-Fase 0 §3: a MIG-01 importa os títulos pagos que existirem, para alimentar a pontualidade dos clientes (MIG-01e).
- ADR 0006 (nenhum módulo acessa tabela de outro; reação entre módulos via outbox; CLI na mesma imagem), ADR 0015 (segredos em arquivo, nunca em variável de ambiente; rede compartilhada com nomes únicos; `check-compose.mjs`), CLAUDE.md "Jobs Hangfire SEMPRE recebem tenantId".
- Código existente: modo CLI do Host (`CliCommand`: `migrate`, `tenant provision`, `tenant invite-owner`…), `SecretFiles` (`/run/secrets/*__*`), `IntegrationHealth`/`Alerts` (FND-12), `INumberSequence`, `AuditSaveChangesInterceptor`.

## Mapeamento (a documentar e validar com o PO)
Entregável desta story: `docs/operacao/migracao-dolibarr.md`, com tabela → entidade, campo → campo, regra de conversão e o que não migra. Ponto de partida (nomes do Dolibarr padrão; conferir na base real, que pode ter extrafields e versão diferentes):

| Dolibarr | BemAqui | Carga |
|---|---|---|
| `llx_societe` (`nom`, `name_alias`, `client`, `fournisseur`, `idprof1`…`idprof6`/`siren`/`siret`, `tva_intra`, `address`, `zip`, `town`, `fk_departement`, `fk_pays`, `phone`, `email`, `note_private`, `status`, `cond_reglement`, `fk_typent`) | `parties.parties`, `party_roles`, `party_addresses` (Billing) | MIG-01b |
| `llx_socpeople` (`fk_soc`, `lastname`, `firstname`, `poste`, `email`, `phone`, `phone_mobile`, endereço, `statut`) | `parties.party_contacts` (e endereços de entrega/obra, se houver) | MIG-01b |
| `llx_c_payment_term` (`code`, `libelle`, `nbjour`, `type_cdr`, `decalage`) | condições de pagamento (CAD-05b) | MIG-01b |
| `llx_bank_account` (`ref`, `label`, `bank`, `code_banque`, `number`, `courant`) + `llx_bank` (saldo na data de corte) | `finance.bank_accounts` (saldo inicial) | MIG-01b |
| `llx_c_units` | `inventory.units` (de-para) | MIG-01c |
| `llx_categorie` (tipo produto) + `llx_categorie_product` | `inventory.item_categories` e `items.category_id` | MIG-01c |
| `llx_product` (`ref`, `label`, `fk_product_type`, `fk_unit`, `customcode`, `fk_country`, `seuil_stock_alerte`, `tobatch`, `tosell`, `tobuy`, `pmp`) + `llx_product_extrafields` (`elev_garantia`) | `inventory.items` | MIG-01c |
| `llx_product_fournisseur_price` (`fk_product`, `fk_soc`, `ref_fourn`, `quantity`) | `inventory.item_supplier_codes` | MIG-01c |
| `llx_entrepot` (`ref`/`label`, `lieu`, `statut`, `fk_parent`) | `inventory.locations` | MIG-01c |
| `llx_product_stock` (`fk_product`, `fk_entrepot`, `reel`) + `llx_product_batch` | movimento "Saldo inicial" | MIG-01d |
| `llx_facture` + `llx_paiement_facture` + `llx_paiement` | títulos a receber em aberto e histórico de pagos | MIG-01e |
| `llx_facture_fourn` + `llx_paiementfourn_facturefourn` + `llx_paiementfourn` | títulos a pagar em aberto | MIG-01e |

**Não migram** (proposta, a confirmar no ponto em aberto 4): orçamentos (`llx_propal`), pedidos (`llx_commande`), OS e BOM/OF (`llx_mrp_mo`, `llx_bom_bom`), lançamentos bancários individuais (`llx_bank`, só o saldo), usuários (`llx_user`), documentos anexos. Ficam consultáveis no Dolibarr em `adm.konebras.com.br` até o desligamento.

## Critérios de aceite
**Mapeamento**
1. **Dado** o inventário das tabelas e colunas reais da base do Dolibarr (lido com o usuário somente leitura), **quando** o mapeamento é revisado, **então** `docs/operacao/migracao-dolibarr.md` lista, para cada entidade, a tabela de origem, os campos, as regras de conversão, os filtros (ex.: `entity = 1`) e o que não migra, e o PO registra o aceite no próprio documento antes da primeira carga real (passo manual verificável).

**Acesso somente leitura**
2. **[Segurança]** **Dado** o usuário MariaDB da migração (proposta: `bqe_migracao_ro`, com `GRANT SELECT` só nas tabelas `llx_*` mapeadas), **quando** o importador tenta `INSERT`, `UPDATE`, `DELETE` ou DDL, **então** o MariaDB recusa; o importador também abre a sessão com `START TRANSACTION READ ONLY` (teste com Testcontainers MariaDB).
3. **[Segurança]** **Dado** a credencial do MariaDB, **quando** o importador roda, **então** ela vem de um arquivo de secret (`/run/secrets/Dolibarr__ConnectionString`, ADR 0015), nunca de variável de ambiente nem de argumento da linha de comando, e não aparece em log, relatório, exceção ou tabela (connection string mascarada).
4. **[Segurança]** **Dado** a leitura, **quando** extraio várias tabelas, **então** todas vêm do mesmo instante (`START TRANSACTION WITH CONSISTENT SNAPSHOT, READ ONLY`), e o relatório registra a data e hora da extração.

**Staging e chaves externas**
5. **Dado** uma extração, **quando** termina, **então** cada linha de origem fica em `migration.import_items` com a tabela e o id de origem, o conteúdo (jsonb) e o hash do conteúdo, ligada à execução em `migration.import_runs`.
6. **Dado** um registro importado, **quando** é criado no módulo de destino, **então** `migration.external_refs` guarda (`source = dolibarr`, tabela, id de origem) → (tipo e id no BemAqui, hash, execução); a chave (`source`, tabela, id de origem) é única.

**Reexecução sem duplicar**
7. **Dado** uma carga já feita, **quando** rodo de novo com a mesma origem, **então** nenhum registro é duplicado: os não alterados saem como "sem alteração" e o total importado da segunda execução é 0.
8. **Dado** um registro alterado no Dolibarr depois da carga, **quando** rodo de novo, **então** ele é atualizado no BemAqui (hash diferente) e sai como "atualizado".
9. **Dado** um registro importado e depois editado no BemAqui, **quando** rodo de novo com a origem também alterada, **então** o BemAqui não é sobrescrito e o relatório mostra "conflito" com os dois valores (proposta; ponto em aberto 5).

**Simulação, transação e concorrência**
10. **Dado** a opção `--dry-run`, **quando** rodo, **então** todas as regras e validações são executadas, o relatório é gerado igual ao real, e nada é gravado nos módulos de destino (contagens antes e depois iguais).
11. **Dado** um erro num registro (ex.: CNPJ inválido), **quando** a carga roda, **então** o registro vai para o relatório com o id de origem e o motivo em PT-BR, e os demais seguem; uma falha de infraestrutura (banco fora do ar) desfaz a entidade em curso e encerra com código ≠ 0.
12. **Dado** uma importação em andamento no tenant, **quando** inicio outra, **então** a segunda recusa com "Já existe uma importação em andamento neste tenant." (advisory lock).
13. **Dado** um tenant inexistente, suspenso ou com schema defasado, **quando** rodo, **então** o comando recusa com a mensagem e código de saída ≠ 0, sem ler o Dolibarr.

**Relatório**
14. **Dado** o fim de uma execução (real ou simulada), **quando** abro o relatório, **então** vejo por entidade: lidos, importados, atualizados, sem alteração, mesclados, ignorados (com o motivo), conflitos e erros (com id de origem e motivo); a soma fecha com os lidos. O relatório sai no console, num arquivo (JSON e CSV) e fica em `migration.import_runs`.
15. **Dado** um relatório, **quando** é gerado, **então** não contém CPF, e-mail nem telefone completos (mascarados), nem credencial.

**Conversões comuns**
16. **Dado** valores do Dolibarr (`double`/`decimal` do MariaDB, datas sem fuso, texto em `latin1` ou `utf8`), **quando** são convertidos, **então** dinheiro vira `numeric(18,2)`, quantidade `numeric(18,4)` e custo unitário `numeric(18,6)` com arredondamento `AwayFromZero` (proposta); datas sem hora viram `date` e datas com hora são lidas como America/Sao_Paulo e gravadas em UTC; textos são normalizados para UTF-8 sem perder acento.

**Auditoria e LGPD**
17. **[Segurança]** **Dado** um registro criado ou atualizado pela importação, **quando** abro o histórico dele, **então** a auditoria mostra a ação com o usuário técnico "Importação Dolibarr" (proposta) e o id da execução; campos `[PersonalData]` seguem mascarados.
18. **[Segurança]** **Dado** a área de staging (que contém dados pessoais do Dolibarr), **quando** a migração é dada como concluída (GOL-01), **então** existe o comando `--purge-staging` que apaga `import_items` e mantém `external_refs` e `import_runs` (sem conteúdo); a área não é exposta por nenhum endpoint.

**Fronteira de módulos**
19. **Dado** o código da migração, **quando** o teste de arquitetura roda, **então** ele só grava nos módulos de destino por interfaces de importação em `*.Contracts` (ex.: `IPartyImport`, `IItemImport`), nunca por `DbSet` de outro módulo, e não referencia `Application`, `Infrastructure` nem `Domain` de outro módulo.

## Regras de negócio e técnicas
- **Execução (proposta):** modo CLI da imagem da API, como os demais comandos da FND-03a/FND-10: `tenant import-dolibarr --tenant <key> --entities <lista> [--dry-run] [--cutoff AAAA-MM-DD] [--confirm-stock] [--confirm-finance] [--purge-staging]`. Roda como container avulso na VPS, pelo PO ou pelo operador, com o secret montado. Sem tela nesta fase (ponto em aberto 3).
- **Ordem das entidades:** condições de pagamento → terceiros → contatos → contas bancárias → unidades → categorias → itens → códigos do fornecedor → locais → saldos → títulos. Cada carga é um `IDolibarrLoader` (MIG-01b a MIG-01e) registrado no executor desta story.
- **Transação:** uma por entidade (lotes de até 500 registros, proposta); a execução é reiniciável.
- **Data de corte** (`--cutoff`): referência para saldo bancário, saldos de estoque e títulos; gravada na execução.
- **Driver MariaDB:** pacote `MySqlConnector` (MIT), **proposta**; entra pela cadeia de suprimentos da FND-01 (`dotnet list package --vulnerable`).
- **Rede:** o container da importação precisa alcançar o `dolibarr-db` (ponto em aberto 2); nada muda nas redes do compose da homologação (ADR 0015).
- **Ids:** Guid v7 nas tabelas de staging. **Datas** em UTC (`timestamptz`).

## Permissões, eventos e tabelas
- **Permissões:** nenhuma no `ModuleCatalog` nesta story (execução só pela CLI, com acesso à VPS). Se a tela do ponto em aberto 3 entrar, usa `settings.view` (relatório) e `settings.edit` (executar), sem ação nova.
- **Eventos:** os que cada módulo de destino já publica ao criar o registro; a migração não cria evento próprio.
- **Tabelas (todas propostas, não estão no 03), schema `migration`:**
  - `import_runs`: `started_at`, `finished_at`, `mode` (DryRun/Real), `cutoff`, `entities`, `status` (Running/Succeeded/Failed), `summary` (jsonb), `started_by`;
  - `import_items`: `run_id`, `source_table`, `source_id`, `payload` (jsonb), `hash`, `outcome` (Imported/Updated/Unchanged/Merged/Skipped/Conflict/Error), `target_type`, `target_id`, `message`;
  - `external_refs`: `source`, `source_table`, `source_id` (único com os dois anteriores), `target_type`, `target_id`, `source_hash`, `imported_at`, `last_run_id`, `target_updated_at` (para detectar edição posterior no BemAqui).

## Telas e estados
- Sem tela nesta story (proposta). O relatório em CSV abre em planilha para a conferência com o PO.
- **Ajuda:** não há rota nova; o passo a passo fica em `docs/operacao/migracao-dolibarr.md` (runbook), incluindo como criar o usuário somente leitura, montar o secret, rodar a simulação, ler o relatório e reexecutar.

## Plano de testes
- **Unidade:** conversões de número, data e texto (CA 16); hash estável do conteúdo; classificação do resultado (importado, atualizado, sem alteração, conflito) (CA 7–9); máscara do relatório (CA 15).
- **Integração (Testcontainers com PostgreSQL e MariaDB):** fixture SQL com um subconjunto anonimizado do esquema do Dolibarr; escrita recusada pelo usuário somente leitura (CA 2); snapshot consistente (CA 4); staging e chaves externas (CA 5, 6); reexecução, atualização e conflito com um carregador de teste (CA 7–9); simulação sem gravação (CA 10); erro de registro × erro de infraestrutura (CA 11); lock (CA 12); tenant suspenso (CA 13); relatório que fecha a soma (CA 14); limpeza da staging (CA 18).
- **Arquitetura:** a migração só usa `*.Contracts` (CA 19); credencial não sai em log (CA 3, teste do `SensitivePropertyMask`).
- **Ensaio manual:** simulação contra uma cópia do banco do Dolibarr (dump restaurado num MariaDB local, nunca a base de produção) antes da primeira carga real em homologação.

## Fora do escopo
- As cargas por entidade: MIG-01b a MIG-01e.
- Tela de importação (ver ponto em aberto 3).
- Importação por planilha CSV genérica para outros sistemas de origem.
- Carga final no go-live: GOL-01 (reusa esta infraestrutura).

## Pontos em aberto
1. **Forma de leitura** (pré-condição): (a) conexão direta somente leitura ao `dolibarr-db` (proposta: menos passos e reexecução simples) ou (b) extração para arquivos (`mysqldump --single-transaction` ou CSV) feita pelo PO e importada por arquivo (sem ligar redes). O R-TER-5 aceita os dois.
2. **Rede até o `dolibarr-db`** (pré-condição da opção a): em qual rede Docker o container do Dolibarr está, e se o container avulso da importação entra nela só durante a execução. Precisa de inspeção da VPS pelo PO ou na FND-10b.
3. **Onde fica o código e se há tela** (pré-condição): módulo novo `Migration` (5 camadas, fora do `ModuleCatalog`) ou dentro do Host/CLI; tela de relatório em Configurações (`settings.view`) ou só CLI e arquivo (proposta). Um módulo novo pede ADR (ADR 0006).
4. **O que não migra:** confirmar a lista (orçamentos, pedidos, OS, BOM/OF, lançamentos bancários individuais, usuários, anexos).
5. **Conflito na reexecução:** não sobrescrever o que foi editado no BemAqui (proposta) ou a origem sempre vence até o go-live.
6. **Retenção da staging** e quem apaga (proposta: comando `--purge-staging` no GOL-01).
7. **Ambiente da primeira carga real:** homologação (`homolog.konebras.com.br`) para conferência, e a carga final em produção na GOL-01 (proposta).
8. **Usuário técnico da auditoria:** usuário de sistema "Importação Dolibarr" ou o usuário do PO que roda o comando.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers PostgreSQL + MariaDB) e arquitetura passando.
- [ ] Migration do schema `migration` criada (não destrutiva).
- [ ] Nenhuma permissão nova (ou, com tela, só `settings.*`).
- [ ] `docs/operacao/migracao-dolibarr.md` escrito (mapeamento + runbook) e aceito pelo PO.
- [ ] Auditoria ligada nas gravações da importação.
- [ ] Credencial só em secret; nada em log.
- [ ] `docs/03-modelo-dados.md` atualizado com o schema `migration`; ADR do módulo/CLI registrada; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Inventário somente leitura das tabelas e colunas reais (com o PO) e o documento de mapeamento.
- [ ] Leitor MariaDB somente leitura com snapshot consistente.
- [ ] Schema `migration`, executor com lock, simulação, transação por entidade e relatório (console, JSON, CSV).
- [ ] Interface `IDolibarrLoader` e convenção das interfaces de importação nos Contracts dos módulos.
- [ ] Comando CLI `tenant import-dolibarr` e `--purge-staging`; runbook.

## Quality gates
- Agentes previstos: @dev, @data-engineer (mapeamento e conversões), cybersecurity (acesso somente leitura, credencial, LGPD da staging), @qa.
- Gate: CA 2, 3, 7 e 10 obrigatórios.

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
| 9 | Telas com estados, ajuda e comportamento propositivo | OK (sem tela nesta story; o passo a passo fica no runbook `docs/operacao/migracao-dolibarr.md`) |
| 10 | DoD do CLAUDE.md ajustado; pontos em aberto registrados sem decidir | OK |

- **Correções feitas na validação:** —
- **Pré-condições para `Ready`:** Pontos em aberto 1 (forma de leitura), 2 (rede até o `dolibarr-db`) e 3 (onde fica o código e se há tela, com ADR); aceite do mapeamento pelo PO (CA 1).
- **Prontidão:** 8/10. **Veredito:** GO condicional (permanece `Draft` até as pré-condições serem decididas).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 18/10/2026 | 0.1 | Story detalhada (quebra da MIG-01) | River (SM) |
| 18/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
