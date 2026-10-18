# FND-03a — Migrator multi-tenant e serviço/CLI de provisionamento

| Campo | Valor |
|---|---|
| ID | FND-03a |
| Título | `TenantMigrator` (container próprio) e provisionamento de tenant por serviço e CLI |
| Tamanho | G |
| Status | Done |
| Requisitos | R-PLT-1 · R-PLT-3 |
| Dependências | FND-02, FND-07, FND-11 |
| Bloqueia | FND-03b, FND-04 (passo "criar proprietário e convidar"), FND-06 (seed de liberação), FND-10 |
| Ordem de execução | 5 |

## User story
Como **super admin da plataforma**, quero **provisionar um tenant novo com um comando (banco, role, migrations, seeds, proprietário e convite) e aplicar as migrations em todos os tenants no deploy, com relatório**, para **colocar um cliente no ar em menos de 1 minuto e manter todos os bancos na mesma versão sem trabalho manual**.

## Contexto e referências
- `docs/02-arquitetura.md` §2.1 (migrator aplica a mesma migration em todos os bancos, com lock e log por tenant; falha em um não bloqueia os demais, mas gera alerta; provisionamento: cria banco → migra → seeds → proprietário → convite).
- `docs/03-modelo-dados.md` → `catalog.tenants` (status `Provisioning`), `catalog.provisioning_logs` (`tenant_id`, `step`, `status`, `message`).
- Revisão §2.2, ADR 0006: migrator como **container próprio**, com advisory lock e versão de schema por tenant (503 se defasado); migrations expand/contract.
- Revisão §2.3, CAs de segurança da FND-03: role do Postgres por tenant com `REVOKE CONNECT FROM PUBLIC`; role dedicado para provisionamento.
- `CLAUDE.md`: "Migration nunca destrutiva sem ADR".

## Critérios de aceite
**Provisionamento**
1. **Dado** o comando `bemaqui tenant provision --key konebras --legal-name "..." --document <CNPJ> --host homolog.konebras.com.br --owner-email <e-mail> --plan <plano>`, **quando** executo num ambiente sem esse tenant, **então** em menos de 1 minuto o tenant fica `Active`, com banco e role próprios, migrations aplicadas e seeds carregados, e as etapas `CreateOwner` e `SendInvite` executadas (nesta story por implementação falsa; com usuário e e-mail reais a partir da FND-04, CA 19).
2. **Dado** um provisionamento, **quando** cada etapa termina, **então** `catalog.provisioning_logs` registra etapa, status (`Started`/`Succeeded`/`Failed`), mensagem e horário UTC.
3. **Dado** uma falha na etapa N (ex.: seed), **quando** o comando termina, **então** o tenant fica em `Provisioning`, o log mostra a etapa e o erro, e `bemaqui tenant provision --key konebras --resume` retoma da etapa N sem repetir as concluídas e sem duplicar dados.
4. **Dado** uma etapa que já rodou, **quando** é executada de novo (retomada), **então** o efeito é idempotente (banco existente não é recriado, seed não duplica).
5. **Dado** uma chave (`key`) ou um host já usado, **quando** tento provisionar, **então** o comando recusa antes de criar qualquer recurso, com mensagem em PT-BR.
6. **[Segurança]** **Dado** um tenant provisionado, **quando** inspeciono o PostgreSQL, **então** existe um role próprio (`tenant_<key>`) dono só do banco dele, sem `SUPERUSER`, `CREATEDB` ou `CREATEROLE`, e o banco tem `REVOKE CONNECT ... FROM PUBLIC`.
7. **[Segurança]** **Dado** o provisionamento, **quando** ele cria banco e role, **então** usa um role dedicado (`bemaqui_provisioner`, com `CREATEDB` e `CREATEROLE`, sem `SUPERUSER`), diferente do role da aplicação em execução.
8. **[Segurança]** **Dado** a senha do role do tenant, **quando** é gerada, **então** é aleatória (≥ 32 bytes), entra no catálogo só cifrada (`SecretKind.TenantConnection`) e não aparece em log nem na saída do comando.

**Migrações**
9. **Dado** uma versão nova com migrations, **quando** o container `migrator` roda no deploy, **então** aplica as migrations ao catálogo e a cada tenant ativo, com advisory lock por banco, e imprime um relatório por tenant (versão anterior, versão nova, status, duração).
10. **Dado** que a migration falha no tenant A, **quando** o migrator continua, **então** os demais tenants são migrados, o relatório marca A como `Failed`, o processo termina com código de saída diferente de zero e o erro fica registrado para alerta (o alerta em si é ligado na FND-12).
11. **Dado** dois migrators iniciados ao mesmo tempo, **quando** rodam, **então** o advisory lock impede que o mesmo banco seja migrado em paralelo.
12. **Dado** um tenant cujo schema está abaixo da versão esperada pela API, **quando** uma requisição chega, **então** a API responde 503 `code: "tenant_schema_outdated"` até a migração terminar.
13. **Dado** uma migration que remove ou renomeia coluna ou tabela, **quando** o CI roda, **então** um teste/verificação falha se não houver ADR referenciada no comentário da migration (expand/contract).

## Regras de negócio e técnicas
- O provisionamento é um serviço em `Platform.Application` (usado pela CLI e pela tela da FND-03b), com etapas registradas como `IProvisioningStep` em ordem: `CreateRole` → `CreateDatabase` → `Migrate` → `Seed` → `CreateOwner` → `SendInvite` → `Activate`.
- Os módulos contribuem com `ITenantSeeder` (ex.: catálogo de módulos e grupos iniciais nas FND-06/06b; plano de contas e unidades na Fase 1). Seeds são idempotentes.
- `CreateOwner` e `SendInvite` são implementados pelo módulo Identity (FND-04/FND-06c) e pelo e-mail (FND-08). Até lá, os testes desta story usam etapas falsas e o CA 1 é verificado de ponta a ponta na FND-04.
- O migrator é um executável próprio (`src/Tools/Migrator` ou modo `--migrate` do Host empacotado em imagem separada) que roda antes da subida da API.
- Versão de schema por tenant: lida da `__EFMigrationsHistory` e guardada no catálogo após migrar; a API compara com a versão embutida no build.
- Migrations expand/contract: mudança destrutiva só em duas versões e com ADR.
- Dados com Guid v7, datas UTC.

## Permissões, eventos e tabelas
- **Tabelas:** `catalog.provisioning_logs`; `catalog.tenants` (+ `schema_version`, `migrated_at`).
- **Permissões:** nenhuma de tenant; a CLI roda no servidor com o role de provisionamento.
- **Eventos:** `TenantProvisioned` (catálogo) para auditoria e para a tela da FND-03b.

## Telas e estados
Sem tela (CLI). A tela fica na FND-03b. Saída da CLI em PT-BR, com uma linha por etapa e o próximo passo sugerido no fim ("Convite enviado para {e-mail}. Acesse https://{host} para concluir o cadastro.").

## Plano de testes
- **Unidade:** ordem das etapas, retomada a partir da etapa com falha, validação de chave e host duplicados.
- **Integração (Testcontainers):** provisionamento completo com etapas falsas de proprietário e convite, medindo < 1 min (CA 1); falha forçada no seed e retomada (CA 3, 4); roles e `REVOKE CONNECT` (CA 6, 7); senha do role ausente do log (CA 8); migrator com dois tenants, falha forçada em um (CA 9, 10); dois migrators em paralelo (CA 11); 503 com schema defasado (CA 12).
- **CI:** verificação de migration destrutiva sem ADR (CA 13).

## Fora do escopo
- Tela do super admin (FND-03b).
- Exclusão de tenant e exportação dos dados do tenant.
- Pool de conexões (PgBouncer): só acima de ~200 bancos (02-arquitetura §2.2).

## Pontos em aberto
- **Plano inicial:** não há plano cadastrado nem regra de quais planos existem. Para a Konebras, sugere-se um plano "Piloto" com todos os módulos da fase vigente; confirmar com o PO (afeta FND-06).
- **Migrator como container próprio (ADR 0006) × "migrations aplicadas na subida do api (com flag)" (`02-arquitetura` §7):** a ADR 0006 prevalece quando aceita; atualizar o §7 do 02 na mesma PR.
- **Destino do alerta** de falha de migração: definido na FND-12.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade e de integração (Testcontainers) passando.
- [ ] Migrations criadas (`provisioning_logs`, campos de versão em `tenants`).
- [ ] Permissão registrada no seed: não se aplica.
- [ ] Tela: não se aplica (CLI com mensagens claras em PT-BR).
- [ ] Ajuda: `docs/operacao/provisionamento.md` com o uso da CLI e da retomada.
- [ ] Padrões e próximo passo propositivos: a CLI termina indicando o próximo passo.
- [ ] Auditoria ligada para provisionamento e mudanças de status no catálogo.
- [ ] `docs/02-arquitetura.md` §7 alinhado à ADR 0006; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Serviço de provisionamento com `IProvisioningStep` e retomada.
- [ ] Etapas `CreateRole` e `CreateDatabase` com o role de provisionamento.
- [ ] `ITenantSeeder` e execução idempotente.
- [ ] CLI `bemaqui tenant provision` (System.CommandLine) com `--resume`.
- [ ] Executável/imagem do migrator com advisory lock e relatório.
- [ ] Middleware de versão de schema (503).
- [ ] Verificação de migration destrutiva no CI.
- [ ] Runbook de provisionamento.

## Quality gates
- Agentes previstos: @architect, @data-engineer, @devops, cybersecurity (roles e GRANTs), @qa.
- Gate: teste de retomada após falha e teste de roles obrigatórios.

## Validação do PO
Checklist do @po (Pax), 16/10/2026.

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo (cabeçalho, user story, contexto, CAs, regras, permissões/tabelas, telas, testes, fora do escopo, pontos em aberto, DoD, tarefas) | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança da revisão §2.3 que se aplicam | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK |
| 7 | Plano de testes cobre os CAs (unidade, integração com Testcontainers, arquitetura, E2E quando cabe) | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas como proposta) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | N/A |
| 10 | DoD do CLAUDE.md ajustado; pontos em aberto registrados sem decidir | OK |

- **Correções feitas na validação:** CA 1 reescrito para ser testável sem a FND-04.
- **Pré-condições para `Ready`:** Plano inicial da Konebras; alinhar o `02-arquitetura` §7 à ADR 0006 (migrator próprio).
- **Prontidão:** 8/10. **Veredito:** GO condicional (permanece `Draft` até as pré-condições serem decididas).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 16/10/2026 | 0.1 | Story criada pela quebra da FND-03 (revisão §2.3): parte de serviço/CLI e migrator | River (SM) |
| 16/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |

## Dev Agent Record
- **18/10/2026, branch `feat/fnd-03a-provisionamento`.**
  - **CLI no executável da API** (ADR 0006), com dois comandos: `tenant provision` e `migrate`. O uso está em `docs/operacao/provisionamento.md`.
  - **`ProvisioningService`** (Platform.Application):
    - roda as etapas Register, CreateRole, CreateDatabase, Migrate, Seed, CreateOwner, SendInvite e Activate;
    - grava log em `catalog.provisioning_logs` e retoma com `--resume`, pulando as etapas já concluídas (idempotente);
    - valida chave, host, e-mail e CPF/CNPJ, e recusa chave ou host repetidos antes de criar qualquer recurso.
  - **`PostgresTenantDatabaseAdmin`** usa o role `bemaqui_provisioner`, que tem CREATEDB e CREATEROLE e não é superuser.
    - Cria o role `tenant_<key>` sem privilégios extras, com senha de 32 bytes aleatórios. A senha vai como literal pelo `quote_literal` e só é gravada cifrada.
    - Cria o banco com esse role como dono e aplica `REVOKE CONNECT, TEMPORARY FROM PUBLIC`.
  - **`TenantDatabaseMigrator`** (Host) usa advisory lock por banco.
    - `TenantMigrationRunner` migra todos os tenants e grava `schema_version` e `migrated_at` no catálogo. O relatório mostra cada tenant, e a saída é 2 quando algum falha.
  - **Middleware:** responde 503 `tenant_schema_outdated` quando o schema do tenant está abaixo do esperado pelo build. Um schema mais novo é aceito, por causa do expand/contract.
  - **Teste de arquitetura:** migration destrutiva sem ADR referenciada reprova o CI (CA 13).
  - **Fixture de tenancy:** passou a usar o provisionamento real.
  - **Etapas provisórias:** `CreateOwner` e `SendInvite` usavam `PendingOwnerOnboarding`. Desde a FND-04b, usam `IdentityOwnerOnboarding`, e o CA 1 é verificado de ponta a ponta (FND-04 CA 19).
  - **Plano:** "Piloto" é o padrão, criado se ainda não existir. Os módulos de cada plano chegam na FND-06.
  - **CAs:** 1 a 13 atendidos, com testes de integração no PostgreSQL real.

## QA Results
_A preencher pelo @qa._