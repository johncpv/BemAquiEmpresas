# FLH-02a — Folha calculada: cálculo mensal (salário, adicionais, faltas, INSS, IRRF, FGTS, salário-família, VT)

| Campo | Valor |
|---|---|
| ID | FLH-02a |
| Título | Motor de cálculo da folha mensal no modo "calculada", puro, determinístico e testável por tabela de casos: salário proporcional, insalubridade e periculosidade, faltas e atrasos do ponto fechado, afastamento, salário-família, desconto de vale-transporte, INSS progressivo, IRRF com a melhor dedução, FGTS do mês e encargos da empresa; memória de cálculo e retrato das entradas por holerite; recálculo enquanto rascunho, lançamentos avulsos e retificação; só simulação até a virada (FLH-06) |
| Tamanho | G (quebra da FLH-02 em 02a cálculo mensal e 02b horas extras e DSR do ponto) |
| Status | Done (pré-condição externa: conferência da contadora e conferência paralela da FLH-06) |
| Requisitos | R-RH-6 (INSS, IRRF, FGTS, adicionais e faltas) · R-RH-5 (holerite e CP reaproveitados) · R-FIN-10 · PRD §6 LGPD · R-SEC-5 · R-PRP-7 |
| Dependências | FLH-01 (tabelas e `IPayrollTaxTables`), RH-02a (rubricas e folha), RH-02b (holerite), RH-02c (fechamento, títulos e previsão, ADR 0036), RH-01c (espelho fechado), CAD-04c2 (dependentes de IR e salário-família, adicionais, vale-transporte), CAD-04c1 (afastamentos) |
| Bloqueia | FLH-02b, FLH-03, FLH-04, FLH-05, FLH-06 |
| Ordem de execução | 2 (Marco 7.1 "Tabelas e cálculo mensal") |

## User story
Como **RH**, quero **que o sistema calcule a folha do mês a partir do cadastro, do ponto e das tabelas vigentes, mostrando como chegou em cada valor**, para **conferir linha a linha com a folha da contadora e, depois da virada, parar de depender do cálculo externo, com o custo real da folha no caixa**.

## Contexto e referências
- PRD R-RH-6; `03-modelo-dados` → `hr.payroll_runs.mode = Calculated`, `payslips` (`inss_base`, `irrf_base`, `fgts_base`), `payslip_lines`.
- `06` Riscos: responsabilidade legal → conferência paralela de 3 meses (FLH-06) antes de substituir a folha da contadora; até lá, a calculada é **simulação** e a lançada (RH-02a) continua sendo a oficial.
- **Revisão pré-Fases 4 a 8** (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`): D1 (Fase 7 antes do go-live), D6 (2FA de novo ao fechar a folha), D14 (folha no financeiro); tabela 3.4, pontos 7 a 12, 28 e linha "Determinismo"; §4.3 FLH-01/02; §4.4 padrões 7 e 8.
- **ADR 0039** (`docs/adr/0039-motor-folha-calculada.md`): motor puro, `calculator_version`, ids das tabelas e retrato das entradas por holerite, folha fechada nunca recalculada, retificação em `Draft` com nova conferência, dados sintéticos no CI.
- **ADR 0036** (`docs/adr/0036-folha-no-financeiro.md`): fechamento síncrono pelo `IPayables`, um título por funcionário (`payee_type = Employee`) e um por guia (órgão como `Party`), previsão `PayrollForecast`, quem fecha (`hr.approve`) não paga (`finance.pay`).
- **ADR 0042** (`docs/adr/0042-configuracoes-fiscais-e-integracoes.md`): parâmetros pré-configurados "A confirmar com a contadora".
- FLH-01 (tabelas por vigência; versões `Unconfirmed` valem na simulação); CAD-04c2 (dependentes: `ir_dependent`, `family_allowance`; `insalubrity_pct`; `hazard_pay`; vale-transporte "usa / não usa"); CAD-04c1 (afastamentos com o tipo); RH-01c (faltas, atrasos e abonos do período fechado).
- **Regras legais como parâmetro:** cada regra fica em `hr.parameters` (ADR 0016 M6), pré-configurada com o valor usual da legislação e o chip "A confirmar com a contadora" em Configurações → Parâmetros → RH, com "Marcar como conferido" (ADR 0042).

## Critérios de aceite
**Cálculo**
1. **Dado** `hr.create` + `hr.view_sensitive` e o espelho da competência fechado (RH-01c), **quando** crio a folha Mensal no modo "Calculada" e clico "Calcular", **então** o motor gera, para cada funcionário da competência, as linhas pelas rubricas com regra (salário, adicionais, faltas, atrasos, afastamento, salário-família, VT, INSS, IRRF), as bases e o FGTS do mês, com as tabelas da competência (FLH-01 CA 8); sem espelho fechado, 422 "Feche o ponto de {mês} antes de calcular." com o link; sem tabela, o erro da FLH-01 CA 8; a folha mostra o chip "Simulação" até a virada (CA 15) e o chip "Calculada com tabelas a confirmar" quando alguma versão usada está `Unconfirmed`.
2. **Dado** o salário, **quando** o funcionário foi admitido ou desligado no mês, **então** o salário é proporcional aos dias pelo parâmetro `hr.salary_proration` (pré-configurado: mês comercial de 30 dias; opção: dias do mês).
3. **Dado** um afastamento por doença ou acidente no mês (CAD-04c1), **quando** calculado, **então** a empresa paga os dias até o limite de `hr.sick_leave_company_days` (pré-configurado: 15 dias) e, do dia seguinte em diante, os dias saem do salário (pagos pela previdência), com a linha "Afastamento: {n} dias pela previdência"; no acidente de trabalho, o FGTS continua sobre a remuneração que seria devida, pelo parâmetro `hr.accident_keeps_fgts` (pré-configurado: ligado).
4. **Dado** o adicional de insalubridade (10, 20 ou 40%, CAD-04c2) e o de periculosidade, **quando** calculados, **então** a insalubridade usa a base `hr.insalubrity_base` (pré-configurada: salário mínimo da FLH-01; opção: salário base), a periculosidade é 30% do salário base (`hr.hazard_pay_pct`), e os dois **não se acumulam**: vale o de maior valor no mês (`hr.allowances_accumulate`, pré-configurado: desligado), com a memória dizendo qual foi escolhido.
5. **Dado** as faltas e atrasos não abonados do espelho fechado (RH-01c), **quando** calculados, **então** viram desconto em horas × valor da hora (salário ÷ horas mensais da jornada do cargo, `hr.hour_value_divisor`, pré-configurado: horas mensais da jornada, ex. 220), com a referência "{n} h"; a perda do DSR por falta é da FLH-02b.
6. **Dado** o salário-família, **quando** a remuneração do mês fica dentro do limite da tabela, **então** paga uma cota por dependente marcado "Salário-família" na CAD-04c2, com idade até `hr.family_allowance_max_age` (pré-configurado: 14 anos; sem limite para dependente inválido) e a marca "Documentos do salário-família entregues" no dependente (campo `family_allowance_docs_ok`, **acrescentado por esta story** ao dependente da CAD-04c2, com a caixa no formulário de dependentes e auditoria); sem a marca, a cota não sai e a folha avisa "{funcionário}: documentos do salário-família pendentes."
7. **Dado** o vale-transporte "usa" (CAD-04c2), **quando** calculado, **então** o desconto é o percentual da tabela (FLH-01) sobre o salário base proporcional, limitado ao valor do benefício do mês quando lançado na rubrica avulsa "Vale-transporte fornecido".
8. **Dado** a base do INSS (soma das linhas com "Base do INSS", RH-02a), **quando** calculada, **então** o INSS é progressivo pelas faixas da tabela, limitado ao teto, arredondado pela regra da FLH-01.
9. **Dado** a base do IRRF (soma das linhas com "Base do IRRF" − INSS), **quando** calculada, **então** o motor compara a dedução legal (dependentes de IR × dedução por dependente, mais os avulsos de rubrica marcada "Deduz da base do IRRF", como a pensão judicial) com o desconto simplificado e usa a que der o menor imposto, aplica as faixas, a parcela a deduzir e a redução adicional da tabela vigente, nunca negativo; a memória mostra a opção escolhida e o imposto da outra.
10. **Dado** a base do FGTS, **quando** calculada, **então** o FGTS do mês = base × alíquota da tabela (informativo no holerite e encargo da empresa), e os encargos patronais saem da tabela Encargos da empresa da FLH-01 (Lucro Presumido, P-01), nunca digitados.
11. **Dado** o dataset sintético do CI (ADR 0039: pelo menos 12 funcionários fictícios, com o resultado calculado à mão pela legislação vigente e revisado, cobrindo admissão no mês, desligamento, falta e atraso, afastamento por doença acima de 15 dias, insalubridade e periculosidade, dependentes e salário-família, VT, desconto simplificado × dedução legal, redução do IRRF e o teto do INSS), **quando** o motor calcula, **então** cada linha, base e líquido bate centavo a centavo; nenhum holerite real entra no Git.

**Determinismo, memória, recálculo e avulsos**
12. **Dado** um holerite calculado, **quando** gravado, **então** guarda `calculator_version`, os ids das versões de tabela e dos parâmetros usados e o **retrato das entradas** (salário, jornada, dependentes, adicionais, afastamentos, horas do espelho, avulsos); recalcular o mesmo retrato com a mesma versão do motor dá o mesmo resultado (teste de propriedade); ao abrir uma linha, vejo a memória ("Base R$ 3.200,00 × faixa 2…", tabela e vigência, entradas), sem dado pessoal além do do próprio funcionário.
13. **Dado** a folha `Draft`, **quando** incluo um lançamento avulso (rubrica sem regra: prêmio, adiantamento, pensão judicial, consignado) e clico "Recalcular", **então** as linhas com regra são refeitas, os avulsos ficam e entram nas bases conforme a rubrica; uma linha calculada não é editada à mão: o ajuste é um avulso com motivo obrigatório.
14. **Dado** uma mudança depois do cálculo (reajuste, dependente, tabela alterada ou retificada, FLH-01 CA 7 e 13), **quando** a folha está `Draft`, **então** mostra "Cadastro ou tabela mudou depois do cálculo de {n} funcionários." com "Recalcular"; com a folha **fechada, nada é recalculado**: aparece "Ver diferença" (o que mudaria, por funcionário e rubrica) e "Gerar retificação", que cria uma folha complementar `Draft` ligada à fechada, só com as diferenças, que segue o fechamento normal e, antes da virada, a conferência da FLH-06.

**Fechamento e caixa**
15. **Dado** a folha calculada, **quando** a FLH-06 ainda não autorizou a virada, **então** o botão "Fechar folha" some e a API responde 409 "Folha calculada em conferência paralela: feche a folha lançada." com o próximo passo "Comparar com a lançada" (FLH-06); o holerite da calculada sai só como prévia com a marca "Simulação: não é o holerite oficial" e nunca vai para "Meus holerites" nem para o e-mail.
16. **Dado** a virada feita (FLH-06) e a folha calculada da competência, **quando** quem tem `hr.approve` fecha, **então** valem o fechamento, os títulos e a previsão da RH-02c (2FA de novo, D6; um título por funcionário com o líquido e um por guia; quem fecha não paga, ADR 0036) e o holerite da RH-02b, com os encargos vindos do cálculo; o diálogo mostra a lista de mudanças de tabela e de parâmetro desde o último fechamento (FLH-01 CA 12) e recusa com 422 "Confirme com a contadora as tabelas {lista} antes de fechar." se alguma versão usada estiver `Unconfirmed`.

**Segurança e permissões**
17. **[Segurança]** **Dado** o motor, **quando** roda, **então** é um serviço puro do Domain, sem banco, sem log de valores (canário: salário e líquido fora do log e dos argumentos do job), executado em `TenantJob` com ids para mais de 20 funcionários; o resultado e a memória só saem por rota sensível (RH-02a), e o retrato das entradas é `[PersonalData]` na auditoria.
18. **Dado** o módulo `hr` não liberado, **quando** chamo, **então** 403 `module_not_released`; calcular e recalcular com `hr.create`/`hr.edit` + `hr.view_sensitive`; gerar retificação com `hr.edit` + `hr.view_sensitive`; fechar com `hr.approve`; sem a ação, 403 `forbidden` e o botão some. `RequireModule` e `perm:`.
19. **Dado** cálculo, recálculo e retificação, **quando** abro o histórico da folha, **então** vejo quem, quando, a versão do motor, as versões das tabelas e dos parâmetros usadas e os totais antes/depois (valores só com `hr.view_sensitive`).

## Regras de negócio e técnicas
- **Motor** `PayrollCalculator` (Domain, puro): entrada (retrato: funcionário, contrato, dependentes, adicionais, afastamentos, espelho fechado, tabelas, parâmetros, avulsos) → linhas com memória. Cada regra legal é uma função pequena testada por tabela de casos; `calculator_version` muda a cada mudança de regra (ADR 0039).
- **Rubricas com regra:** `hr.payroll_items.calculation` (`None`, `BaseSalary`, `SickLeave`, `Insalubrity`, `HazardPay`, `Absence`, `Lateness`, `FamilyAllowance`, `TransportVoucher`, `Inss`, `Irrf`, `Fgts`), com a ordem de cálculo fixa no motor; `hr.payroll_items.deducts_irrf_base` para a pensão judicial.
- **Parâmetros** (`hr.parameters`, Configurações → Parâmetros → RH, `settings.edit`, chip "A confirmar com a contadora"): `hr.salary_proration`, `hr.sick_leave_company_days`, `hr.accident_keeps_fgts`, `hr.insalubrity_base`, `hr.hazard_pay_pct`, `hr.allowances_accumulate`, `hr.hour_value_divisor`, `hr.family_allowance_max_age`.
- **Tabelas:** `hr.payslips` + `calculator_version`, `tax_table_ids`, `parameter_ids`, `inputs_snapshot` (jsonb, `[PersonalData]`); `hr.payslip_lines` + `calculated`, `memory` (jsonb, `[PersonalData]` na auditoria); `hr.payroll_runs` + `calculated_at`, `is_simulation`, `rectifies_run_id`; `hr.employee_dependents` + `family_allowance_docs_ok`.
- **Arredondamento** pela regra da FLH-01; dinheiro `numeric(18,2)`; nenhum `double`.

## Permissões, eventos e tabelas
- **Permissões:** `hr.create`, `hr.edit`, `hr.view_sensitive`, `hr.approve` (fechar, pela RH-02c). Nenhuma ação nova.
- **Eventos:** os da RH-02c no fechamento oficial; nenhum na simulação.
- **Tabelas:** extensões acima; parâmetros novos em `hr.parameters`.

## Telas e estados
- **Padrões comuns das Fases 4 a 8:** 7 (fechamento com impacto em lista: títulos por funcionário e por guia, mudanças de tabela e parâmetro); 8 (valores e memória no bloco "Dados protegidos").
- Na folha (RH-02a): modo "Calculada" na criação; "Calcular" e "Recalcular"; memória por linha em painel lateral; faixa "Simulação: em conferência paralela com a folha da contadora" até a virada, com "Comparar com a lançada"; "Ver diferença" e "Gerar retificação" na folha fechada.
- **Estados:** calculando (assíncrono, com o aviso de demora depois de 30 s); pré-condições (ponto e tabelas) com links; erro por funcionário ("Sem jornada", "Sem cargo") sem travar os outros; vazio ("Nenhum funcionário ativo na competência." + "Ver funcionários").
- **Propositivo:** a competência sugerida é a do último espelho fechado; depois de calcular, o `NextSteps` oferece "Comparar com a lançada" (antes da virada) ou "Fechar folha" (depois).
- **400 px:** memória em tela cheia; linhas em cartões.
- **Ajuda:** atualizar `Ajuda/rh-folha.md` e `Ajuda/rh-folha-detalhe.md` ("Folha calculada", "Simulação", "Memória de cálculo", "Retificação") e `Ajuda/configuracoes-parametros.md` (seção RH, folha calculada); `<Dica>` em "Desconto simplificado", "Base do INSS", "Simulação" e "Retificação".

## Plano de testes
- **Unidade (tabela de casos, obrigatória):** cada regra (CA 2–10) com o dataset sintético; dataset centavo a centavo (CA 11); determinismo pelo retrato (CA 12).
- **Integração (Testcontainers):** pré-condições e chips (CA 1); retrato e memória gravados (CA 12); recálculo com avulsos (CA 13); cadastro ou tabela mudou, diferença e retificação sem recalcular a fechada (CA 14); fechamento bloqueado antes da virada e prévia sem envio (CA 15); fechamento oficial com 2FA, títulos por funcionário e por guia e exigência de tabela conferida (CA 16); canário e job (CA 17); três camadas (CA 18); histórico (CA 19).
- **E2E (Playwright):** calcular a folha do tenant `demo`, abrir a memória de uma linha e ver o bloqueio do fechamento.

## Fora do escopo
- Horas extras, adicional noturno e DSR: FLH-02b. Férias, 13º e rescisão: FLH-03 a FLH-05.
- eSocial, DCTFWeb, FGTS Digital e transmissão das guias: fora do produto (PRD §3.3); a contadora transmite.
- Pensão alimentícia calculada pela decisão judicial e consignado com regra: lançamento avulso no MVP.

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fases 4 a 8, tabela 3.4, pontos 7–12 e 28 e linha "Determinismo"):
1. A calculada é só simulação até a virada da FLH-06 (CA 1, 15).
2. Regras parametrizadas e pré-configuradas "A confirmar com a contadora": proporcionalidade de 30 dias, base da insalubridade no salário mínimo, sem acúmulo (vale o maior), valor da hora pelas horas mensais da jornada (CA 2, 4, 5).
3. Afastamento por doença e acidente: empresa paga até 15 dias, a previdência depois; FGTS mantido no acidente (CA 3).
4. Salário-família: até 14 anos (sem limite para inválido), com a marca de documentos entregues (CA 6).
5. Pensão e consignado como lançamento avulso; pensão deduz da base do IRRF pela marca da rubrica (CA 9, 13).
6. Encargos patronais pela tabela de Encargos da FLH-01 (Lucro Presumido, P-01) (CA 10).
7. eSocial, DCTFWeb e FGTS Digital fora do produto (ponto 28).
8. Determinismo: `calculator_version`, ids das tabelas e parâmetros e retrato das entradas por holerite; folha fechada nunca recalculada; retificação nasce `Draft` e passa por nova conferência (CA 12, 14).

Decisões da revisão aplicadas: D1 (Fase 7 antes do go-live); D6 (2FA de novo ao fechar, CA 16); D14/ADR 0036 (um título por funcionário e um por guia, quem fecha não paga, CA 16); §4.3 FLH-01/02 (mudanças de parâmetro e tabela no fechamento, CA 16); CI com dados sintéticos, nunca holerites reais (ADR 0039, CA 11).

**Pré-condição externa:** conferência pela contadora dos parâmetros pré-configurados (chip "A confirmar com a contadora"); o dataset de referência da contadora é pré-condição da virada (FLH-06), não desta story.

## Definition of Done
- [x] Critérios de aceite atendidos (ver o Change Log 1.0 para as recomendações aceitas e as pendências externas).
- [x] Testes de unidade (dataset sintético) e integração (Testcontainers) passando; E2E não escrito (regra do PO de 31/10: a story prova os CAs com unidade, integração e Vitest).
- [x] Migration criada (não destrutiva, `CalculatedPayrollFlh02a`); parâmetros com seed idempotente e o chip "A confirmar com a contadora".
- [x] Permissões só do `ModuleCatalog` (nenhuma ação nova).
- [x] Telas com estados; ajuda atualizada; teste de cobertura passando.
- [x] Auditoria ligada, com `[PersonalData]` no retrato e na memória.
- [x] OpenAPI e cliente Orval regenerados.
- [x] `docs/03-modelo-dados.md` e `docs/04-fluxos-estados.md` (simulação, retificação) atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [x] `PayrollCalculator` e as regras; retrato das entradas e `calculator_version`.
- [x] Dataset sintético em `tests/` (funcionários fictícios, resultado esperado documentado com a regra usada).
- [x] Job de cálculo; memória na tela; "Ver diferença" e "Gerar retificação".
- [x] Bloqueio do fechamento antes da virada e exigência de tabela conferida depois.

## Quality gates
- Agentes previstos: @dev, @architect (determinismo e fronteira com a RH-02c), @qa, revisão da contadora (externa, nos parâmetros).
- Gate: CA 8, 9, 11, 12, 14, 15 e 17 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fases 4 a 8).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: dependências pelos nomes novos CAD-04c1 e CAD-04c2) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** quebra da FLH-02 (maior que G) em cálculo mensal (02a) e horas do ponto (02b); toda regra legal com parâmetro e caso de teste; a calculada não substitui a lançada antes da FLH-06 (CA 14).
- **Correções da revalidação (21/10/2026):** tabela 3.4 pontos 7 a 12 decididos, com os valores pré-configurados "A confirmar com a contadora" (CA 2–7, 9, 10); determinismo com retrato das entradas, folha fechada nunca recalculada e retificação em `Draft` (CA 12, 14); dataset sintético da legislação vigente no CI no lugar dos holerites da contadora (CA 11); prévia da simulação sem envio (CA 15); fechamento oficial com 2FA, títulos por funcionário e por guia (D6, D14) e tabelas conferidas (CA 16); dependências pelos nomes novos (CAD-04c1, CAD-04c2); ADRs 0036, 0039 e 0042 citadas; CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma de decisão de story. Externa: conferência dos parâmetros pela contadora (não bloqueia).
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready (pré-condição externa)`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da FLH-02) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (D1, D6, D14, tabela 3.4 pontos 7–12 e 28 e "Determinismo", §4.3 FLH-01/02, §4.4 padrões 7 e 8); nova ordem (Marco 7.1, ordem 2, antes do go-live); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
| 01/11/2026 | 1.0 | **Pacote de 2026 com os valores oficiais** (primeiro commit): INSS, IRRF (com a redução da Lei 15.270/2025), salário-família, salário mínimo, FGTS e vale-transporte, cada um com a fonte (ato + URL), todos "A confirmar com a contadora" (detalhe e os três pontos em aberto no Change Log 1.1 da FLH-01). Exemplos da Receita testados: 6.000,00 → 394,54 e 4.500,00 → 0 | @dev |
| 01/11/2026 | 1.0 | Implementação (@dev): motor `PayrollCalculator` 1.0.0 no Domain (puro, sem banco, sem relógio, sem log), com o retrato das entradas, a memória por linha, as bases, o FGTS do mês e os encargos da empresa pela tabela; `PayrollCalculationService` (pré-condições: espelho fechado 422 com o link, tabelas da FLH-01, Mensal; até 20 funcionários na hora e acima pelo `PayrollCalculationJob` só com ids; avulsos com motivo e recálculo do holerite; "Cadastro ou tabela mudou depois do cálculo de {n} funcionários." pelo retrato, pela edição e pela troca das tabelas usadas; "Ver diferença" e "Gerar retificação" da folha fechada; histórico com os totais só por `/sensitive`; bloqueio do fechamento antes da virada com "Comparar com a lançada" e, depois, exigência das tabelas conferidas); `hr.payroll_items.calculation` e `deducts_irrf_base` (seed aplicado uma vez, AFAST e VTFORN novas); `family_allowance_docs_ok` no dependente (caixa no formulário, auditada, na exportação LGPD); nove parâmetros novos em `hr.parameters` "A confirmar com a contadora" (com o tipo "Escolha" na tela); guias calculadas (FGTS, INSS com os encargos, IRRF); marca "Simulação: não é o holerite oficial" no PDF; telas (modo na Nova folha, barra do cálculo, memória no painel do holerite, diferença, retificação, histórico), ajuda (`rh-folha`, `rh-folha-detalhe`, `configuracoes-parametros`) e o adendo da ADR 0039. Testes: dataset sintético de 14 funcionários centavo a centavo, regras por tabela de casos, propriedade do determinismo e integração (Testcontainers) | @dev |
| 01/11/2026 | 1.0 | Recomendações aceitas (PO 24/10): (1) **virada**: a FLH-06 ainda não existe; a simulação é enquanto o parâmetro interno `hr.payroll_calculated_official` (sem tela, auditado, gravado pela virada da FLH-06) não for 1; (2) a calculada **convive com a lançada** da mesma competência (o único passa a período, tipo e modo, fora as retificações), para a conferência paralela; (3) **só a Mensal** é calculada nesta story (os outros tipos: 422, "chega nas próximas etapas"); (4) **linha calculada nunca é editada**, e todo avulso da calculada tem motivo (5 a 200) — o mais conservador; lançar ou tirar o avulso recalcula o holerite na hora (o retrato fica sempre igual às linhas); (5) **o afastamento pela previdência** sai como linha informativa ("Afastamento: {n} dias pela previdência") e o salário fica proporcional aos dias pagos pela empresa; só doença e acidente mudam o salário (maternidade e os demais tipos ficam com a contadora); (6) **o mês comercial** conta o último dia do mês como dia 30 (fevereiro cheio paga 30 dias); (7) **o salário-família** usa a base do INSS como remuneração, é proporcional no mês de admissão ou desligamento e vale até o mês em que o filho completa a idade do parâmetro; o cadastro do dependente não tem a marca "inválido" (a CAD-04c2 não criou): sem ela, o limite de idade vale para todos (pendência para a contadora); (8) **as guias calculadas** são FGTS, INSS (o retido + os encargos da empresa) e IRRF, refeitas a cada cálculo, com o vencimento e o órgão padrão; só o vencimento e o órgão mudam à mão; as guias sindical e "Outros" continuam lançadas; (9) **a retificação** tem uma linha por rubrica com a diferença (o provento que diminuiu vira desconto da mesma rubrica) e só as guias da diferença a maior; a diferença a menor é compensada pela contadora; (10) **fechar a calculada** depois da virada exige também o cálculo atualizado (sem "Cadastro ou tabela mudou"); (11) a **competência sugerida** na Nova folha calculada é a do último espelho fechado (`GET /payroll-runs/suggested-period`); (12) E2E não escrito (regra do PO de 31/10) | @dev |
| 01/11/2026 | 1.0 | **Pendências externas:** conferência pela contadora dos nove parâmetros da folha calculada e das tabelas de 2026 (todas "A confirmar"), em especial o rendimento da redução do IRRF (`hr.irrf_reduction_income`, IN RFB 2.299/2025), o salário-família (só fontes secundárias) e o arredondamento do INSS no teto; a conferência paralela de 3 meses e a virada são da FLH-06 | @dev |
