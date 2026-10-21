# FLH-02a — Folha calculada: cálculo mensal (salário, adicionais, faltas, INSS, IRRF, FGTS, salário-família, VT)

| Campo | Valor |
|---|---|
| ID | FLH-02a |
| Título | Motor de cálculo da folha mensal no modo "calculada", puro e testável por tabela de casos: salário proporcional, insalubridade e periculosidade, faltas e atrasos do ponto fechado, salário-família, desconto de vale-transporte, INSS progressivo, IRRF com a melhor dedução, FGTS do mês e encargos da empresa; memória de cálculo por linha, recálculo enquanto rascunho e lançamentos avulsos; mesmo fechamento, holerite e títulos da folha lançada |
| Tamanho | G (quebra da FLH-02 em 02a cálculo mensal e 02b horas extras e DSR do ponto) |
| Status | Draft |
| Requisitos | R-RH-6 (INSS, IRRF, FGTS, adicionais e faltas) · R-RH-5 (holerite e CP reaproveitados) · R-FIN-10 · PRD §6 LGPD · R-SEC-5 · R-PRP-7 |
| Dependências | FLH-01 (tabelas), RH-02a/02b/02c (rubricas, folha, holerite, fechamento), RH-01c (ponto fechado), CAD-04c (dependentes de IR e salário-família, adicionais, VT, afastamentos) |
| Bloqueia | FLH-02b, FLH-03, FLH-04, FLH-05, FLH-06 |
| Ordem de execução | 2 (Marco 7.1 "Tabelas e cálculo mensal") |

## User story
Como **RH**, quero **que o sistema calcule a folha do mês a partir do cadastro, do ponto e das tabelas vigentes, mostrando como chegou em cada valor**, para **parar de depender do cálculo externo, conferir linha a linha e ver o custo real da folha no caixa**.

## Contexto e referências
- PRD R-RH-6; `03-modelo-dados` → `hr.payroll_runs.mode = Calculated`, `payslips` (`inss_base`, `irrf_base`, `fgts_base`), `payslip_lines`.
- `06` Riscos: responsabilidade legal → conferência paralela de 3 meses (FLH-06) antes de substituir a folha da contadora; até lá, a folha calculada é **simulação** e a lançada continua sendo a oficial (Pontos em aberto 1).
- FLH-01 (tabelas por vigência, nenhum valor legal no código); CAD-04c (dependentes: `ir_dependent`, `family_allowance`; `insalubrity_pct`; `hazard_pay`; vale-transporte no formato decidido); RH-01c (faltas, atrasos e abonos do período fechado).
- **Regras legais como parâmetro:** base da insalubridade (salário mínimo ou outra, conforme convenção), proporcionalidade por dias, ordem das deduções do IRRF e escolha entre deduções legais e desconto simplificado: tudo confirmado pela contadora e coberto por casos de teste dela (Pontos em aberto 2).

## Critérios de aceite
**Cálculo**
1. **Dado** `hr.create` + `hr.view_sensitive` e o ponto da competência fechado, **quando** crio a folha Mensal no modo "Calculada" e clico "Calcular", **então** o motor gera, para cada funcionário da competência, as linhas pelas rubricas com regra (salário, adicionais, faltas, atrasos, salário-família, VT, INSS, IRRF) e as bases e o FGTS do mês, usando as tabelas vigentes da competência (FLH-01); sem ponto fechado, 422 "Feche o ponto de {mês} antes de calcular." com o link; sem tabela, o erro da FLH-01 CA 6.
2. **Dado** o salário, **quando** o funcionário foi admitido, desligado ou afastado no mês, **então** o salário é proporcional aos dias pela regra parametrizada (proposta: mês comercial de 30 dias; Pontos em aberto 2), e o afastamento por doença ou acidente segue a regra de quem paga cada período (Pontos em aberto 3).
3. **Dado** o adicional de insalubridade (10, 20 ou 40%, CAD-04c) e o de periculosidade, **quando** calculado, **então** usa a base parametrizada por tenant (proposta: parâmetro `hr.insalubrity_base` = salário mínimo da FLH-01 ou o salário base; periculosidade sobre o salário base), e os dois não se acumulam sem decisão (Pontos em aberto 2).
4. **Dado** as faltas e atrasos não abonados do espelho fechado (RH-01c), **quando** calculados, **então** viram desconto em horas × valor da hora (salário ÷ horas mensais do cargo), com a referência "{n} h"; a perda do DSR por falta é da FLH-02b.
5. **Dado** o salário-família, **quando** a remuneração do mês fica dentro do limite da tabela, **então** paga uma cota por dependente marcado "Salário-família" na CAD-04c (a elegibilidade por idade e documentação conferida pela regra da contadora, Pontos em aberto 4).
6. **Dado** o vale-transporte no formato decidido na CAD-04c, **quando** calculado, **então** o desconto é o menor entre o percentual máximo da tabela sobre o salário base e o valor do benefício do mês.
7. **Dado** a base do INSS (soma das linhas com "Base do INSS", RH-02a), **quando** calculada, **então** o INSS é progressivo pelas faixas da tabela, limitado ao teto, arredondado pela regra da FLH-01.
8. **Dado** a base do IRRF (soma das linhas com "Base do IRRF" − INSS), **quando** calculada, **então** o motor compara a dedução legal (dependentes de IR × dedução por dependente, mais pensão quando houver) com o desconto simplificado e usa a que der o menor imposto, aplica as faixas, a parcela a deduzir e a redução adicional da tabela vigente, nunca negativo; a memória mostra a opção escolhida.
9. **Dado** a base do FGTS, **quando** calculada, **então** o FGTS do mês = base × alíquota da tabela (informativo no holerite e encargo da empresa), e os encargos patronais seguem os parâmetros da FLH-01 (dependem do regime, P-01).
10. **Dado** o dataset de referência da contadora (pré-condição: pelo menos 10 holerites reais anonimizados com o resultado esperado, cobrindo admissão no mês, falta, insalubridade, dependentes e o teto do INSS), **quando** o motor calcula, **então** cada linha, base e líquido bate centavo a centavo; o teste roda no CI.

**Memória, recálculo e avulsos**
11. **Dado** um holerite calculado, **quando** abro uma linha, **então** vejo a memória de cálculo ("Base R$ 3.200,00 × faixa 2…", tabela e vigência usadas, dados de entrada), sem dado pessoal além do do próprio funcionário.
12. **Dado** a folha `Draft`, **quando** incluo um lançamento avulso (rubrica sem regra, ex.: prêmio, adiantamento) e clico "Recalcular", **então** as linhas com regra são refeitas, os avulsos ficam e entram nas bases conforme a rubrica; uma linha calculada não é editada à mão (só por avulso de ajuste com motivo, proposta).
13. **Dado** a mudança de cadastro (reajuste, dependente) depois do cálculo, **quando** a folha está `Draft`, **então** a folha mostra "Cadastro mudou depois do cálculo de {n} funcionários." com "Recalcular"; fechada, nada muda sozinho.

**Fechamento e caixa**
14. **Dado** a folha calculada, **quando** fecho, **então** valem o mesmo fechamento, os títulos e a previsão da RH-02c e o holerite da RH-02b, com os encargos da empresa vindos do cálculo (e não digitados); enquanto a FLH-06 não autorizar a virada, o fechamento da calculada fica bloqueado com "Folha calculada em conferência paralela: feche a folha lançada." (Pontos em aberto 1).

**Segurança e permissões**
15. **[Segurança]** **Dado** o motor, **quando** roda, **então** é um serviço puro do Domain, sem banco, sem log de valores (canário: salário e líquido fora do log e dos argumentos do job), executado em `TenantJob` com ids para mais de 20 funcionários; o resultado só sai por rota sensível (RH-02a CA 11).
16. **Dado** o módulo `hr` não liberado, **quando** chamo, **então** 403 `module_not_released`; calcular e recalcular com `hr.create`/`hr.edit` + `hr.view_sensitive`; sem a ação, 403 `forbidden`. `RequireModule` e `perm:`.
17. **Dado** cálculo e recálculo, **quando** abro o histórico da folha, **então** vejo quem, quando, as versões das tabelas usadas e os totais antes/depois.

## Regras de negócio e técnicas
- **Motor** `PayrollCalculator` (Domain, puro): entrada (funcionário, contrato, dependentes, adicionais, ponto fechado, tabelas, parâmetros, avulsos) → linhas com memória. Cada regra legal é uma função pequena e testada por tabela de casos.
- **Rubricas com regra:** `hr.payroll_items.calculation` (proposta: `None`, `BaseSalary`, `Insalubrity`, `HazardPay`, `Absence`, `Lateness`, `FamilyAllowance`, `TransportVoucher`, `Inss`, `Irrf`, `Fgts`), com a ordem de cálculo fixa no motor.
- **Tabelas:** `hr.payslip_lines` + `calculated`, `memory` (jsonb, `[PersonalData]` na auditoria), `tax_table_ids`; `hr.payroll_runs` + `calculated_at`, `table_versions`.
- **Arredondamento** pela regra da FLH-01; dinheiro `numeric(18,2)`; nenhum `double`.

## Permissões, eventos e tabelas
- **Permissões:** `hr.create`, `hr.edit`, `hr.view_sensitive`.
- **Eventos:** os da RH-02c no fechamento.
- **Tabelas:** extensões acima; parâmetros `hr.insalubrity_base`, `hr.salary_proration`.

## Telas e estados
- Na folha (RH-02a): modo "Calculada" na criação, "Calcular" e "Recalcular", memória por linha (painel), faixa "Em conferência paralela" (FLH-06).
- **Estados:** calculando (assíncrono, com o estado de demora); pré-condições (ponto e tabelas) com links; erro por funcionário ("Sem jornada", "Sem cargo") sem travar os outros.
- **400 px:** memória em tela cheia.
- **Ajuda:** atualizar `Ajuda/rh-folha.md` e `Ajuda/rh-folha-detalhe.md` ("Folha calculada", "Memória de cálculo"); `<Dica>` em "Desconto simplificado" e "Base do INSS".

## Plano de testes
- **Unidade (tabela de casos, obrigatória):** cada regra (CA 2–9) com os casos da contadora; dataset de referência centavo a centavo (CA 10).
- **Integração (Testcontainers):** pré-condições (CA 1); memória (CA 11); recálculo com avulsos (CA 12); cadastro mudou (CA 13); bloqueio do fechamento na conferência (CA 14); canário e job (CA 15); três camadas (CA 16); histórico (CA 17).
- **E2E (Playwright):** calcular a folha de teste e abrir a memória de uma linha.

## Fora do escopo
- Horas extras, adicional noturno e DSR: FLH-02b. Férias, 13º e rescisão: FLH-03 a FLH-05.
- eSocial, DCTFWeb, FGTS Digital e guias: fora do produto (PRD §3.3); a contadora transmite.
- Pensão alimentícia calculada por decisão judicial: lançamento avulso no MVP (Pontos em aberto 5).

## Pontos em aberto
1. **Folha oficial durante a conferência** (pré-condição, PO + contadora): a calculada é só simulação até a FLH-06 autorizar a virada (proposta).
2. **Regras parametrizadas** (pré-condição, contadora): proporcionalidade (30 dias ou dias do mês), base da insalubridade (salário mínimo, salário base ou o que a convenção do sindicato definir), acúmulo de insalubridade e periculosidade, valor da hora para faltas.
3. Afastamento por doença e acidente: dias pagos pela empresa e pela previdência (contadora).
4. Elegibilidade do salário-família (idade, documentos) e se o RH controla os documentos (CAD-04c).
5. Pensão alimentícia e consignado: avulso (proposta) ou regra.
6. Encargos patronais pelo regime (P-01).

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade (tabela de casos da contadora), integração (Testcontainers) e E2E passando.
- [ ] Migration criada (não destrutiva).
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Telas com estados; ajuda atualizada; teste de cobertura passando.
- [ ] Auditoria ligada, com `[PersonalData]`.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `PayrollCalculator` e as regras; dataset de referência anonimizado; job de cálculo; memória na tela.

## Quality gates
- Agentes previstos: @dev, @qa, revisão da contadora (dataset e resultado).
- Gate: CA 7, 8, 10 e 15 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026.

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto registrados sem decidir | OK |

- **Correções feitas na validação:** quebra da FLH-02 (maior que G) em cálculo mensal (02a) e horas do ponto (02b); toda regra legal com parâmetro e caso da contadora, sem decidir regra (pontos 2 a 5); a calculada não substitui a lançada antes da FLH-06 (CA 14).
- **Pré-condições para `Ready`:** pontos em aberto 1 e 2; dataset de referência da contadora (CA 10); FLH-01 concluída.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da FLH-02) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
