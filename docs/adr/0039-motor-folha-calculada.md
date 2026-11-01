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

## Adendo FLH-01 (31/10/2026): esquema 1 das tabelas e vigência
- **Esquema 1** (`PayrollTaxSchema.Current = 1`, no Domain do HR): INSS com faixas (limite e alíquota; o limite da última é o teto); IRRF com faixas (limite, alíquota e parcela a deduzir; a última "acima de"), dedução por dependente, desconto simplificado (usado quando é maior que INSS + dependentes) e **redução adicional** em faixas de rendimento tributável, cada uma com `valor fixo − percentual × rendimento`, limitada a [0, imposto das faixas]; com percentual zero, é a redução de valor fixo. Salário-família (remuneração máxima e cota por filho), salário mínimo, FGTS (mensal, multa da rescisão e aprendiz), vale-transporte (percentual máximo) e encargos (regime, patronal, RAT × FAP e terceiros). Alíquotas em %, com até 4 casas (`numeric(7,4)`); dinheiro com 2. Norma que pedir outra forma vira o esquema 2, com nota aqui.
- **Vigência sem `valid_to`** (recomendação aceita, PO 24/10): cada versão vale até a próxima do mesmo tipo; a garantia "uma vigência por tipo e data" vem do índice único parcial `(kind, valid_from)` entre as vigentes (`Active`, ou `Unconfirmed` que não é retificação), no lugar do `EXCLUDE USING gist`. Sem intervalo gravado, não há sobreposição possível e a escolha da competência é a de maior `valid_from` até o 1º dia do mês.
- **Pacote do ano** (`PayrollTaxPackages/hr-payroll-tables-{ano}.json`, embutido no HR.Application): aplicado pelo seed do HR em todo `migrate`, uma vez por (ano do pacote, tipo) pela coluna `seed_package` com índice único; o HR não grava em `identity.seed_patches` (tabela de outro módulo, ADR 0006). Entrada sem fonte oficial no repositório fica `pending` e não cria versão.
- **Mudanças no fechamento** (§1, último item): a lista vem da auditoria (`IAuditReader.ChangesSinceAsync`) de `PayrollTaxTable` e `HrParameter` desde o último fechamento; fechar com a lista não vazia exige a ciência (`acknowledgedChanges`).

## Adendo FLH-02a (01/11/2026): motor 1.0.0, retrato, simulação e retificação
- **Motor** `PayrollCalculator` (Domain, `Version = "1.0.0"`): salário proporcional (`hr.salary_proration`), afastamento por doença e acidente pela previdência depois de `hr.sick_leave_company_days` (o FGTS do acidente mantido por `hr.accident_keeps_fgts`), insalubridade (`hr.insalubrity_base`) e periculosidade (`hr.hazard_pay_pct`) sem acúmulo (`hr.allowances_accumulate`, vale o maior), faltas e atrasos do espelho fechado pelo valor da hora (`hr.hour_value_divisor`), avulsos, vale-transporte limitado ao fornecido, salário-família (`hr.family_allowance_max_age` e a marca de documentos), INSS progressivo, IRRF pela menor entre a dedução legal (INSS, dependentes e rubricas "Deduz da base do IRRF") e o simplificado, com a redução sobre o rendimento de `hr.irrf_reduction_income` (bruto tributável, como os exemplos da Receita; ou a base), FGTS e encargos da empresa. Cada valor em 2 casas (`AwayFromZero`); os impostos pelo arredondamento da versão da tabela. O mês comercial conta o último dia do mês como dia 30.
- **Retrato** (`payslips.inputs_snapshot`): salário vigente no fim da competência, admissão e desligamento, cargo e horas mensais, adicionais, vale-transporte, dependentes (sem nome nem CPF), afastamentos, espelho fechado, avulsos e os parâmetros — em ordem estável. "Cadastro ou parâmetro mudou depois do cálculo" compara o retrato de hoje com o gravado (relido e reescrito, porque o `jsonb` reordena as chaves).
- **Rubricas com regra**: `payroll_items.calculation`, uma ativa por regra; as do seed recebem a regra uma vez (sem desfazer a escolha do tenant). Linha com regra nunca é editada à mão: o ajuste é avulso com motivo.
- **Simulação e virada**: a folha calculada convive com a lançada (único por período, tipo e modo); `is_simulation` enquanto o parâmetro interno `hr.payroll_calculated_official` (sem tela, gravado pela virada da FLH-06) não for 1. Antes da virada, fechar é 409 com "Comparar com a lançada".
- **Retificação** (§3): folha complementar `Draft` (`rectifies_run_id`) com um holerite por funcionário com diferença e uma linha por rubrica com a diferença (o provento que diminuiu vira desconto da mesma rubrica, "(diferença a menor)"); guias só da diferença a maior (a menor é compensada pela contadora). Uma retificação em rascunho por vez; recalcular a retificação refaz as diferenças contra a fechada.
