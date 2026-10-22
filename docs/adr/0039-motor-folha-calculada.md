# ADR 0039: Motor da folha calculada

- Status: Aceito (21/10/2026). O PO (John) aprovou a recomendação "Determinismo" e os pontos 1 a 28 da tabela 3.4 da revisão pré-Fases 4 a 8 (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`), com a decisão D1 nova: a Fase 7 entra no planejamento, antes do go-live, e as stories podem ficar `Ready` com pré-condição externa explícita (os datasets de referência da contadora).
- Contexto: a Fase 7 (FLH-01 a FLH-06) troca a folha lançada (RH-02a, valores vindos da contadora) por uma folha calculada no BemAqui: tabelas legais com vigência (INSS, IRRF, salário-família, salário mínimo), cálculo mensal, horas extras e DSR, férias, 13º, rescisão e a conferência paralela antes da virada. Folha errada é passivo trabalhista. Sem regra explícita, um holerite fechado poderia mudar de valor ao recalcular depois de uma tabela nova ou de uma correção do motor, e os testes dependeriam de holerites reais, que não podem ir para o Git.
- Complementa as ADRs 0036 (folha no financeiro), 0016 (`[PersonalData]`, parâmetros do módulo dono), 0018 (retenção) e 0019 (§9 arredondamento).

## Decisão

### 1. Tabelas legais por vigência
- `hr.payroll_tax_tables` com `kind` (INSS, IRRF, salário-família, salário mínimo, teto e os demais), `valid_from`, `valid_to`, faixas e parâmetros; **uma vigência ativa por tipo e data** (`EXCLUDE USING gist` no intervalo).
- O **esquema de cada tipo de tabela é versionado no código** (ex.: o formato da redução do IRRF segue a norma vigente; norma nova com formato novo é uma versão nova do esquema, nunca edição do antigo).
- **Pré-cadastradas por vigência e editáveis**: a plataforma sugere as tabelas vigentes (seed da legislação), o tenant importa, e a contadora confere. **Quem cadastra ou edita não confere** (`hr.approve` de outra pessoa); exceção do Proprietário único só com o dataset da contadora aprovado para aquela vigência.
- Mudança de parâmetro da folha feita com `settings.edit` aparece como **aviso no fechamento** seguinte ("Parâmetros alterados desde a última folha").

### 2. Determinismo
- O motor é **função pura** (`PayrollCalculator` no Domain): mesma entrada, mesmo resultado, sem relógio, sem banco, sem aleatoriedade.
- Cada holerite calculado grava:
  - **`calculator_version`** (versão semântica do motor);
  - os **ids das tabelas** usadas (`tax_table_ids`) e as versões dos esquemas;
  - o **retrato das entradas** (salário, horas, faltas, afastamentos, dependentes, eventos avulsos) e a **memória de cálculo** por rubrica (`memory`, jsonb, `[PersonalData]`, mascarado na auditoria).
- **Arredondamento por regra parametrizada** por rubrica (casa, modo), testado centavo a centavo.
- **Folha fechada nunca é recalculada.** Mudar o motor ou a tabela não altera holerite fechado; o recálculo só existe para folha em `Draft`.

### 3. Retificação
- Corrigir uma folha fechada é uma **retificação**: nasce uma folha nova em **`Draft`**, ligada à original, com a diferença por funcionário e por rubrica, e passa por **nova conferência** e novo fechamento (ADR 0036: títulos da diferença, nunca edição dos títulos pagos).

### 4. Testes sem dado real
- **O CI roda com dados sintéticos** tirados da legislação vigente (exemplos oficiais e casos montados pela equipe), nunca holerites reais. Nenhum holerite ou CPF real entra no Git (teste de canário no repositório).
- Os **datasets de referência da contadora** (casos reais anonimizados ou montados por ela) ficam fora do Git, no cofre do PO, e rodam por uma suíte manual marcada (`[Trait("dataset","contadora")]`); o resultado registrado é pré-condição da virada (FLH-06).
- Cada `calculator_version` nova roda a suíte sintética inteira e mostra a diferença contra a versão anterior.

### 5. Conferência paralela e virada
- Até a virada, a folha calculada é **só simulação**: a oficial continua a lançada (RH-02a).
- **FLH-06:** o motor calcula em paralelo à folha lançada por **3 meses**; `hr.parallel_runs` e `parallel_differences` (`[PersonalData]`) guardam a diferença em R$ e % por rubrica e por funcionário.
- **Critério de aceite da virada:** diferença **zero** no líquido e por rubrica nos meses conferidos; **assina a contadora responsável**, com o registro de quem e quando; a virada (`PayrollModeChanged`) é feita pelo Proprietário.
- Férias, 13º e rescisão só viram oficiais depois de **1 caso conferido** de cada.
- eSocial, DCTFWeb e FGTS Digital ficam **fora do produto**.

## Consequências
- **FLH-01** implementa a seção 1; **FLH-02a/02b, FLH-03, FLH-04 e FLH-05** usam o motor determinístico da seção 2 e os testes da seção 4; **FLH-06** implementa a seção 5; a retificação entra na FLH-02a.
- As stories da Fase 7 ficam `Ready (pré-condição externa)`: datasets de referência da contadora e regras da convenção coletiva, com valores pré-cadastrados e editáveis.
- `docs/03-modelo-dados.md` registra as extensões de `payroll_tax_tables`, `payslip_lines` (`calculated`, `memory`, `tax_table_ids`), `payroll_runs` (`calculator_version`, `table_versions`, `rectifies_run_id`) e `parallel_runs`.
