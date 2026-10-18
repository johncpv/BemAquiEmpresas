# CAD-05a — Plano de contas gerencial e centros de custo

| Campo | Valor |
|---|---|
| ID | CAD-05a |
| Título | Plano de contas gerencial em árvore com seed v1 editável (máscara `9.9.99`, grupos da DRE, contas "A classificar") e centros de custo, com excluir sem uso e inativar com uso |
| Tamanho | M (quebra da CAD-05, que somava plano de contas, centros de custo, contas bancárias e condições de pagamento) |
| Status | Ready |
| Requisitos | R-FIN-1 · R-PLT-3 (seed do plano de contas padrão) · R-FIN-13 (grupo da DRE, preparação) · R-SEC-5 · R-PRP-1 |
| Dependências | CAD-05b (schema `finance` e padrões do módulo), FND-09d (`TreeTable`, painel lateral); Fase 0 (FND-06c, FND-07, FND-09c, FND-14) |
| Bloqueia | Nenhuma story da Fase 1. Fora dela: FIN-01, FIN-02, FIN-08 |
| Ordem de execução | 11 (Marco C — base de custo) |

## User story
Como **financeiro**, quero **um plano de contas gerencial pronto, em árvore, que eu possa ajustar, e os centros de custo da empresa**, para **classificar cada receita e despesa desde o primeiro lançamento e ter DRE e custos por área sem planilha**.

## Contexto e referências
- PRD R-FIN-1 (plano de contas com seed editável, centros de custo), R-PLT-3, R-FIN-13.
- `docs/03-modelo-dados.md` → `finance.chart_of_accounts` e `finance.cost_centers` (com `is_active` e `seed_key`).
- `docs/02-arquitetura.md` §8; `docs/06-pendencias-riscos.md` P-17 (DRE por competência; não muda o cadastro).
- Decidido em 18/10/2026 (tabela 1.1): a estrutura sugerida vira o **seed v1**, editável e sem esperar a contadora; o seed nunca sobrescreve; máscara `9.9.99`; grupos da DRE sugeridos; centros Administrativo, Comercial, Produção e Instalação; contas "A classificar" de receita e despesa.
- ADR 0016 (seeds nos tenants existentes com `IgnoreQueryFilters()`; excluir × inativar).
- Padrões comuns a todos os cadastros: `docs/stories/README.md`. Árvore no `TreeTable` (`treegrid`) e edição no **painel lateral**.
- `ModuleCatalog`: `finance` com `view`, `create`, `edit`, `approve`, `delete`, `pay`. O grupo Financeiro tem `view`, `create` e `edit`.

## Critérios de aceite
**Seed v1**
1. **Dado** um tenant novo ou um tenant existente no próximo deploy, **quando** o seed roda, **então** o plano v1 está carregado em árvore, com contas sintéticas e analíticas:
   - `1 Receitas` (Revenue): 1.1 Venda de equipamentos · 1.2 Serviços de instalação e assistência · 1.3 Receitas financeiras · 1.4 Outras receitas · 1.9 Receitas a classificar;
   - `2 Custos` (Expense): 2.1 Materiais · 2.2 Mão de obra direta · 2.3 Instalação terceirizada · 2.4 Fretes · 2.5 Impostos sobre vendas · 2.6 Outros custos;
   - `3 Despesas` (Expense): 3.1 Pessoal administrativo · 3.2 Ocupação · 3.3 Administrativas · 3.4 Comerciais · 3.5 Financeiras · 3.6 Impostos e taxas · 3.9 Despesas a classificar;
   - `4 Investimentos` (Asset): 4.1 Investimentos e imobilizado;
   - `5 Financiamentos e sócios` (Liability): 5.1 Empréstimos e financiamentos · 5.2 Aportes e retiradas dos sócios;
   cada conta com o grupo da DRE do seed (as raízes 4 e 5 ficam "Fora da DRE") e `seed_key`. As movimentações patrimoniais da sugestão original foram divididas em duas raízes porque o tipo da raiz vale para todos os descendentes (CA 7).
2. **Dado** o seed aplicado e contas editadas, inativadas ou excluídas pelo tenant, **quando** o seed roda de novo, **então** nada é duplicado e nada volta ao padrão (busca por `seed_key` com `IgnoreQueryFilters()`).
3. **Dado** um tenant novo ou existente no próximo deploy, **quando** o seed roda, **então** existem os centros de custo Administrativo, Comercial, Produção e Instalação, com as mesmas regras de idempotência.

**Árvore e cadastro**
4. **Dado** `finance.view`, **quando** abro a tela, **então** vejo a `TreeTable` (`role="treegrid"`) com código, nome, tipo, grupo da DRE e situação, expando e recolho pelo teclado, e a busca por código ou nome mostra a conta com o caminho dos pais.
5. **Dado** `finance.create` e uma conta pai, **quando** clico "Nova conta filha", **então** o código vem sugerido como o próximo do nível na máscara `9.9.99` (ex.: depois de `2.1.03` vem `2.1.04`), o tipo e o grupo da DRE vêm herdados do pai (R-PRP-1), e "Salvar conta contábil" grava com o toast "Conta contábil salva".
6. **Dado** um código fora da máscara `9.9.99` (níveis 1, 1.1 e 1.1.01), **quando** salvo, **então** a API responde 400 "Use o formato 9.9.99."
7. **Dado** uma conta filha com tipo diferente do pai, **quando** salvo, **então** a API responde 400 "A conta precisa ter o mesmo tipo da conta pai."
8. **Dado** um código já usado por outra conta não excluída, **quando** salvo, **então** a API responde 409 "Já existe a conta {código}."; o índice único parcial garante também em gravações simultâneas.
9. **Dado** uma conta que recebe a primeira filha, **quando** a filha é criada, **então** a conta pai passa a sintética (`accepts_entries = false`); só contas analíticas aparecem nos seletores de lançamento.
10. **Dado** uma conta analítica com lançamento, **quando** tento criar uma filha nela, **então** a API responde 409 "Esta conta já tem lançamentos e não pode virar agrupadora. Crie a conta em outro grupo."
11. **Dado** uma conta, **quando** troco a conta pai para uma descendente dela, **então** a API responde 400 "A conta não pode ficar dentro dela mesma."

**Excluir e inativar (D10)**
12. **Dado** `finance.delete` e uma conta sem lançamento e sem filhas, **quando** clico "Excluir conta contábil" e confirmo "Excluir", **então** ela é excluída logicamente e some da árvore.
13. **Dado** uma conta com lançamento, **quando** tento excluir, **então** a API responde 409 "Conta com lançamentos não pode ser excluída. Inative-a." e o `ConfirmDialog` oferece "Inativar" (`recovery`).
14. **Dado** `finance.edit`, **quando** inativo uma conta, **então** ela some dos seletores, continua na árvore com o chip neutro "Inativa" (filtro "Inativas") e nos relatórios; inativar uma sintética exige as filhas inativas (409 com a lista).
15. **Dado** a verificação de uso, **quando** um módulo registra lançamentos ligados a contas (FIN-01, FIN-02, recorrentes), **então** ele implementa `IAccountUsageProvider`; aqui, o teste usa um provedor de teste.

**Centros de custo**
16. **Dado** `finance.create`, **quando** cadastro centros de custo em árvore (código, nome, pai) e clico "Salvar centro de custo", **então** valem as mesmas regras de código único, ciclo, excluir sem uso e inativar com uso (CA 8, 11–14).

**Contratos**
17. **Dado** um módulo que lança valores (Fase 2 em diante), **quando** lista contas para seleção, **então** usa `IChartOfAccounts` de `Finance.Contracts`, que devolve só contas analíticas, ativas e não excluídas, filtrando por tipo, e resolve qualquer conta por id.

**Permissões (3 camadas)**
18. **Dado** o módulo `finance` não liberado, **quando** chamo os endpoints, **então** recebo 403 `module_not_released` e o front mostra `ModuleNotReleased`.
19. **Dado** um usuário sem grupo que habilite `finance`, **quando** chamo os endpoints, **então** recebo 403 `forbidden`; com `finance.view` sem `create`/`edit`/`delete`, as ações ficam ocultas por `<Can>` e a escrita responde 403 `forbidden`. Todo endpoint tem `RequireModule` e `perm:`.

**Auditoria**
20. **Dado** qualquer alteração de conta ou centro de custo, **quando** abro "Histórico de alterações", **então** vejo antes/depois, usuário e data.

## Regras de negócio e técnicas
- **Código hierárquico:** máscara `9.9.99` (três níveis); texto, ordenado por segmento numérico.
- **Tipo:** `Revenue`, `Expense`, `Asset`, `Liability`, gravado como texto; o tipo da raiz define o dos descendentes.
- **Sintética × analítica:** `accepts_entries` derivado (tem filhas ativas → `false`).
- **Grupo da DRE** (lista fixa do sistema, usada pela FIN-08): Receita bruta, Deduções, Custos, Despesas operacionais, Resultado financeiro, Outros resultados, Fora da DRE.
- **Contas "A classificar":** 1.9 (receita) e 3.9 (despesa), para o lançamento que ainda não tem conta certa; aparecem nos seletores com a `<Dica>` "Use só enquanto não souber a conta."
- **Soft delete** com índice único de `code` parcial (`WHERE NOT is_deleted`). **Inativação:** `is_active`.
- **Uso da conta:** `IAccountUsage` consulta os `IAccountUsageProvider`.
- **Seed:** `ChartOfAccountsSeeder` e `CostCentersSeeder` (`ITenantSeeder`), idempotentes, com `seed_key`, rodando também nos tenants existentes.
- **Ids:** Guid v7. **Auditoria:** `IAuditable`. Nenhum outro módulo lê `finance.*` direto.

## Permissões, eventos e tabelas
- **Permissões (já no `ModuleCatalog`):** `finance.view`, `create`, `edit`, `delete`.
- **Eventos:** nenhum.
- **Tabelas:** `finance.chart_of_accounts` (+ `is_active`, `seed_key`); `finance.cost_centers` (+ `is_active`, `seed_key`).

## Telas e estados
- **Financeiro → Plano de contas** (`/app/financeiro/plano-de-contas`): h1 com a linha "Contas para classificar receitas e despesas."; `TreeTable` com busca e filtro "Inativas" na URL; painel lateral para criar e editar; ações "Nova conta contábil", "Nova conta filha", "Inativar", "Reativar", "Excluir conta contábil".
- **Financeiro → Centros de custo** (`/app/financeiro/centros-de-custo`): mesma estrutura, com "Salvar centro de custo".
- **Estados:** carregando; vazio só se o tenant excluir tudo ("Cadastre as contas..." + "Nova conta contábil"); vazio filtrado; erro com "Tentar de novo"; modo leitura.
- **Propositivo:** código, tipo e grupo da DRE sugeridos; exclusão recusada oferece "Inativar"; `<Dica>` em "Analítica/Sintética".
- **400 px e teclado:** um CA de E2E para a árvore (`treegrid` pelas setas).
- **Ajuda:** `Ajuda/financeiro-plano-de-contas.md` (aba `plano-de-contas`) e `Ajuda/financeiro-centros-de-custo.md` (aba `centros-de-custo`).

## Plano de testes
- **Unidade:** próximo código na máscara (CA 5, 6); herança e validação de tipo (CA 7); ciclo (CA 11); derivação de sintética (CA 9); ordenação por segmento.
- **Integração (Testcontainers):** seed v1 e centros em tenant novo e existente, sem duplicar nem sobrescrever e sem recriar excluído (CA 1–3); código único concorrente (CA 8); conta usada pelo provedor de teste (CA 10, 13, 14); centros com as mesmas regras (CA 16); `IChartOfAccounts` (CA 17); três camadas (CA 18, 19); auditoria (CA 20).
- **Arquitetura:** nenhum módulo referencia `Finance.Infrastructure`/`Application`; todo endpoint com `RequireModule` e `perm:`.
- **E2E (Playwright):** criar conta filha com código sugerido, tentar excluir conta usada e inativar; árvore a 400 px e só pelo teclado.

## Fora do escopo
- Contas bancárias, caixas e condições de pagamento: CAD-05b.
- Plano contábil do Dolibarr (`llx_accounting_account`): não migra (ADR 0017).
- Lançamentos e DRE: Fases 2 e 4. Rateio entre centros. Exportação para a contadora (P-06).

## Pontos em aberto
- Decidido em 18/10/2026 (tabela 1.1 e D10): seed v1 conforme o CA 1, editável e sem esperar a contadora; máscara `9.9.99`; grupos da DRE sugeridos; centros Administrativo, Comercial, Produção e Instalação; contas "A classificar"; conta sem uso pode ser excluída.
- Nenhum ponto em aberto. A contadora pode revisar o plano depois, pela própria tela.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers), arquitetura e E2E passando.
- [ ] Migration criada (não destrutiva) e seeds idempotentes.
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Telas com estados de carregando, vazio, vazio filtrado e erro.
- [ ] Ajuda das duas telas; teste de cobertura passando.
- [ ] Padrões comuns dos cadastros e próximos passos aplicados.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` conferido; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Entidades `ChartAccount` e `CostCenter` com regras de árvore, tipo, máscara e código.
- [ ] Migration, `ChartOfAccountsSeeder` e `CostCentersSeeder`.
- [ ] `IAccountUsage` + `IAccountUsageProvider`; `IChartOfAccounts` em `Finance.Contracts`.
- [ ] Endpoints `/api/v1/finance/chart-of-accounts` e `/api/v1/finance/cost-centers`; telas e ajuda.

## Quality gates
- Agentes previstos: @dev, @qa.
- Gate: CA 2 (seed idempotente sem sobrescrever) e CA 13 (exclusão recusada com uso) obrigatórios.

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

- **Correções feitas na validação (18/10/2026):** seed v1, máscara, grupos da DRE, centros e contas "A classificar" decididos (tabela 1.1) e escritos nos CAs; seed também nos tenants existentes; `TreeTable`; verbos específicos; a story não bloqueia mais a CAD-05b nem a MIG-01e (cancelada); ordem 11 do Marco C.
- **Pré-condições para `Ready`:** nenhuma pendente.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 18/10/2026 | 0.1 | Story detalhada (quebra da CAD-05) | River (SM) |
| 18/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 18/10/2026 | 0.3 | Decisões do PO (D1, D10, tabela 1.1); ordem 11 do Marco C | River (SM) |
| 18/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
