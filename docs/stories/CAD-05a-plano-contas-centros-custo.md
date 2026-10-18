# CAD-05a — Plano de contas gerencial e centros de custo

| Campo | Valor |
|---|---|
| ID | CAD-05a |
| Título | Plano de contas gerencial em árvore com seed padrão editável e centros de custo, com inativação em vez de exclusão quando houver lançamento |
| Tamanho | M (quebra da CAD-05, que somava plano de contas, centros de custo, contas bancárias e condições de pagamento) |
| Status | Draft |
| Requisitos | R-FIN-1 · R-PLT-3 (seed do plano de contas padrão) · R-FIN-13 (grupo da DRE, preparação) · R-SEC-5 · R-PRP-1 |
| Dependências | Fase 0 concluída (FND-06c, FND-07, FND-09c, FND-14). Sem dependência da Fase 1 |
| Bloqueia | CAD-05b (conta contábil padrão das contas bancárias, opcional), MIG-01e (conta "a classificar"), FIN-01, FIN-02, FIN-08 |
| Ordem de execução | 2 |

## User story
Como **financeiro**, quero **um plano de contas gerencial pronto, em árvore, que eu possa ajustar, e os centros de custo da empresa**, para **classificar cada receita e despesa desde o primeiro lançamento e ter DRE e custos por área sem planilha**.

## Contexto e referências
- PRD R-FIN-1 (plano de contas gerencial com seed padrão editável, centros de custo, vínculo opcional a projeto em todo lançamento), R-PLT-3 (o provisionamento carrega o plano de contas padrão), R-FIN-13 (DRE gerencial por mês e por projeto).
- `docs/03-modelo-dados.md` → `finance.chart_of_accounts` (`code`, `name`, `type` Revenue/Expense/Asset/Liability, `parent_id`, `accepts_entries`, `dre_group`) e `finance.cost_centers` (`code`, `name`, `parent_id`).
- `docs/02-arquitetura.md` §8 (schema por módulo, `pg_trgm` nos campos de busca, `row_version`).
- `docs/06-pendencias-riscos.md` P-17 (DRE por competência; não muda o cadastro, só o relatório da FIN-08).
- Backlog (CA): plano de contas em árvore; conta com lançamento não pode ser excluída, só inativada.
- Código existente: `ITenantSeeder` (seed idempotente no provisionamento), `ISoftDeletable`, `IAuditable`, `RecordHistory`, `DataTable`.
- `ModuleCatalog`: módulo `finance` ("Caixa e pagamentos", área Financeiro) com `view`, `create`, `edit`, `approve`, `delete` e `pay`. O grupo Financeiro do seed tem `finance.view`, `create` e `edit`.
- Protótipo: não tem tela de plano de contas; seguir o padrão de listagem (cabeçalho com ação principal, painel com tabela).

## Critérios de aceite
**Seed**
1. **Dado** um tenant recém-provisionado, **quando** abro Financeiro → Plano de contas, **então** o plano padrão está carregado em árvore (estrutura no ponto em aberto 1), com contas sintéticas (agrupadoras) e analíticas.
2. **Dado** o seed já aplicado e contas editadas pelo tenant, **quando** o seed roda de novo (reprovisionamento ou migração), **então** nada é duplicado e nenhuma conta editada, inativada ou excluída pelo tenant volta ao padrão (controle por `seed_key`).

**Árvore e cadastro**
3. **Dado** `finance.view`, **quando** abro a tela, **então** vejo a árvore com código, nome, tipo, grupo da DRE e situação, posso expandir e recolher, e a busca por código ou nome mostra a conta com o caminho dos pais.
4. **Dado** `finance.create` e uma conta pai selecionada, **quando** clico "Nova conta filha", **então** o código vem sugerido como o próximo do nível (ex.: depois de `2.1.03` vem `2.1.04`) e o tipo vem herdado do pai (R-PRP-1); salvar com "Salvar conta" grava e confirma "Conta salva".
5. **Dado** uma conta filha com tipo diferente do pai, **quando** salvo, **então** a API responde 400 "A conta precisa ter o mesmo tipo da conta pai."
6. **Dado** um código já usado por outra conta não excluída, **quando** salvo, **então** a API responde 409 "Já existe a conta {código}."; o índice único no banco garante a regra também em gravações simultâneas.
7. **Dado** uma conta que recebe filhas, **quando** a primeira filha é criada, **então** a conta pai passa a sintética (`accepts_entries = false`); só contas analíticas aparecem nos seletores de lançamento.
8. **Dado** uma conta analítica com lançamento, **quando** tento criar uma filha nela, **então** a API responde 409 "Esta conta já tem lançamentos e não pode virar agrupadora. Crie a conta em outro grupo."
9. **Dado** uma conta, **quando** troco a conta pai para uma descendente dela, **então** a API responde 400 "A conta não pode ficar dentro dela mesma."

**Excluir e inativar**
10. **Dado** `finance.delete` e uma conta sem lançamento e sem filhas, **quando** clico "Excluir" e confirmo "Excluir", **então** ela é excluída logicamente (`is_deleted`) e some da árvore.
11. **Dado** uma conta com lançamento, **quando** tento excluir, **então** a API responde 409 "Conta com lançamentos não pode ser excluída. Inative-a." e a tela oferece "Inativar" no mesmo aviso (propositivo).
12. **Dado** `finance.edit`, **quando** inativo uma conta, **então** ela some dos seletores de novos lançamentos, continua visível na árvore com o chip "Inativa" e nos relatórios; inativar uma conta sintética exige que as filhas estejam inativas (409 com a lista).
13. **Dado** a verificação de uso, **quando** um módulo registra lançamentos ligados a contas (FIN-01, FIN-02, recorrentes), **então** ele implementa `IAccountUsageProvider`; nesta story o teste de integração usa um provedor de teste que declara uma conta como usada.

**Centros de custo**
14. **Dado** `finance.create`, **quando** cadastro centros de custo em árvore (código, nome, pai), **então** valem as mesmas regras de código único, ciclo, excluir sem uso e inativar com uso (CA 6, 9, 10, 11, 12).

**Contratos para os próximos módulos**
15. **Dado** um módulo que lança valores (Fase 2 em diante), **quando** precisa listar contas para seleção, **então** usa `IChartOfAccounts` de `Finance.Contracts`, que devolve só contas analíticas, ativas e não excluídas, filtrando por tipo.

**Permissões (3 camadas)**
16. **Dado** o módulo `finance` não liberado, **quando** chamo os endpoints, **então** recebo 403 `module_not_released` e o front mostra `ModuleNotReleased`.
17. **Dado** um usuário sem grupo que habilite `finance`, **quando** chamo os endpoints, **então** recebo 403 `forbidden`; com `finance.view` sem `finance.create`/`edit`/`delete`, as ações ficam ocultas por `<Can>` e as chamadas de escrita respondem 403 `forbidden`.

**Auditoria**
18. **Dado** qualquer alteração de conta ou centro de custo, **quando** abro "Histórico", **então** vejo antes/depois, usuário e data (FND-07, `RecordHistory`).

## Regras de negócio e técnicas
- **Código hierárquico:** segmentos numéricos separados por ponto (ex.: `1`, `1.1`, `1.1.01`); máscara definitiva no ponto em aberto 2. O código é texto; a ordenação da árvore é por segmento numérico, não alfabética.
- **Tipo:** `Revenue`, `Expense`, `Asset`, `Liability`, gravado como texto (02 §8). O tipo da raiz define o dos descendentes.
- **Sintética × analítica:** `accepts_entries` é derivado (tem filhas ativas → `false`), não editado à mão.
- **Soft delete** (cadastro, CLAUDE.md): `is_deleted` + `deleted_at`; o índice único de `code` é parcial (`WHERE NOT is_deleted`).
- **Inativação:** campo `is_active` (**proposta**, não está no 03).
- **Grupo da DRE:** `dre_group` é uma lista fixa do sistema (ponto em aberto 3), usada pela FIN-08.
- **Uso da conta:** `IAccountUsage` (Finance.Application) consulta todos os `IAccountUsageProvider` registrados. Nesta fase ainda não há lançamento real; o mecanismo nasce pronto para FIN-01/FIN-02.
- **Seed:** `ChartOfAccountsSeeder : ITenantSeeder`, idempotente, com `seed_key` (**proposta**) por conta, como o `GroupSeeder`.
- **Ids:** Guid v7. **Auditoria:** `IAuditable` nas duas entidades.
- Nenhum outro módulo lê `finance.*` direto (ADR 0006); só via `Finance.Contracts`.

## Permissões, eventos e tabelas
- **Permissões (já no `ModuleCatalog`):** `finance.view`, `finance.create`, `finance.edit`, `finance.delete`. Nenhuma ação nova.
- **Eventos:** nenhum (sem consumidor nesta fase).
- **Tabelas:** `finance.chart_of_accounts` (03) + `is_active`, `seed_key` (**propostas**); `finance.cost_centers` (03) + `is_active` (**proposta**).

## Telas e estados
- **Financeiro → Plano de contas** (`/app/financeiro/plano-de-contas`): árvore em tabela (código, nome, tipo, grupo da DRE, situação), busca, filtro "mostrar inativas"; painel lateral para criar e editar; ações "Nova conta", "Nova conta filha", "Inativar", "Reativar", "Excluir", "Histórico".
- **Financeiro → Centros de custo** (`/app/financeiro/centros-de-custo`): mesma estrutura.
- As duas rotas entram como abas do módulo `finance` em `navigation/modules.ts`.
- **Estados:** carregando (skeleton da árvore); vazio só nos centros de custo ("Cadastre os centros de custo da empresa, como Administrativo, Comercial e Produção." + "Novo centro de custo"); erro com o motivo e "Tentar de novo".
- **Propositivo:** código e tipo sugeridos (CA 4); exclusão recusada oferece "Inativar" (CA 11); a `<Dica>` em "Analítica/Sintética" explica que só a analítica recebe lançamento.
- **Ajuda:** `Ajuda/financeiro-plano-de-contas.md` (aba `plano-de-contas`, `padrao: true`) e `Ajuda/financeiro-centros-de-custo.md` (aba `centros-de-custo`).

## Plano de testes
- **Unidade:** próximo código do nível (CA 4); herança e validação de tipo (CA 5); detecção de ciclo (CA 9); derivação de sintética (CA 7); ordenação por segmento numérico.
- **Integração (Testcontainers):** seed aplicado e reaplicado sem duplicar nem sobrescrever edição (CA 1, 2); código único com duas gravações simultâneas (CA 6); conta usada pelo provedor de teste não exclui e inativa (CA 8, 11, 12); centros de custo com as mesmas regras (CA 14); `IChartOfAccounts` só com analíticas ativas (CA 15); três camadas (CA 16, 17); auditoria (CA 18).
- **Arquitetura:** nenhum módulo referencia `Finance.Infrastructure` ou `Finance.Application`; `RequirePermission` só com códigos do catálogo.
- **E2E (Playwright):** criar conta filha com código sugerido, tentar excluir conta usada e inativar.

## Fora do escopo
- Contas bancárias, caixas e condições de pagamento: CAD-05b.
- Lançamentos (CR, CP, recorrentes) e a DRE: Fases 2 e 4.
- Rateio de lançamento entre centros de custo.
- Exportação para a contadora (R-FIN-14, P-06).

## Pontos em aberto
1. **Conteúdo do plano de contas padrão** (pré-condição do seed): validar com o PO e a contadora. Sugestão de estrutura gerencial, alinhada às naturezas de custo do PRD R-PRJ-5:
   - `1 Receitas` (Revenue): 1.1 Venda de equipamentos · 1.2 Serviços de instalação e assistência · 1.3 Receitas financeiras · 1.4 Outras receitas;
   - `2 Custos` (Expense): 2.1 Materiais · 2.2 Mão de obra direta · 2.3 Instalação terceirizada · 2.4 Fretes · 2.5 Impostos sobre vendas · 2.6 Outros custos;
   - `3 Despesas` (Expense): 3.1 Pessoal administrativo · 3.2 Ocupação (aluguel, energia, água) · 3.3 Administrativas · 3.4 Comerciais · 3.5 Financeiras (tarifas e juros) · 3.6 Impostos e taxas;
   - `4 Movimentações patrimoniais` (Asset e Liability): investimentos e imobilizado, empréstimos e financiamentos, aportes e retiradas dos sócios.
2. **Máscara do código** (níveis e dígitos por nível; sugestão `9.9.99`).
3. **Lista do grupo da DRE** (sugestão: Receita bruta, Deduções, Custos, Despesas operacionais, Resultado financeiro, Outros resultados, Fora da DRE).
4. **Seed de centros de custo:** criar um padrão (sugestão: Administrativo, Comercial, Produção) ou começar vazio.
5. **Conta sem uso:** pode ser excluída (proposta, igual aos demais cadastros) ou só inativada.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers), arquitetura e E2E passando.
- [ ] Migration criada (schema `finance`, não destrutiva) e seed idempotente.
- [ ] Permissões usadas só do `ModuleCatalog`.
- [ ] Telas com estados de carregando, vazio e erro.
- [ ] Ajuda das duas telas escrita; teste de cobertura passando.
- [ ] Padrões e próximo passo propositivos avaliados.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado (`is_active`, `seed_key`); story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Entidades `ChartAccount` e `CostCenter` (Domain) com regras de árvore, tipo e código.
- [ ] `IFinanceDb`, configurações EF `internal`, migration do schema `finance`.
- [ ] `ChartOfAccountsSeeder` (e de centros de custo, se decidido).
- [ ] `IAccountUsage` + `IAccountUsageProvider`; `IChartOfAccounts` em `Finance.Contracts`.
- [ ] Endpoints `/api/v1/finance/chart-of-accounts` e `/api/v1/finance/cost-centers` com `RequireModule("finance")`.
- [ ] Telas de árvore e ajuda.

## Quality gates
- Agentes previstos: @dev, @qa.
- Gate: CA 2 (seed idempotente sem sobrescrever) e CA 11 (exclusão recusada com uso) obrigatórios.

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

- **Correções feitas na validação:** —
- **Pré-condições para `Ready`:** Ponto em aberto 1 (conteúdo do plano de contas padrão, com o PO e a contadora).
- **Prontidão:** 9/10. **Veredito:** GO condicional (permanece `Draft` até as pré-condições serem decididas).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 18/10/2026 | 0.1 | Story detalhada (quebra da CAD-05) | River (SM) |
| 18/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
