# FLH-02b — Folha calculada: horas extras, adicional noturno e DSR a partir do ponto

| Campo | Valor |
|---|---|
| ID | FLH-02b |
| Título | Horas extras do espelho fechado separadas por tipo (dia útil, domingo ou folga, feriado) com percentuais parametrizados pela convenção, adicional noturno quando houver, DSR sobre as variáveis e perda do DSR por falta injustificada, saldo do banco de horas pago ou descontado por decisão do RH (sem vencimento automático); tudo com memória de cálculo e retrato das entradas |
| Tamanho | M (quebra da FLH-02) |
| Status | Done (pré-condição externa: conferência da contadora e conferência paralela da FLH-06) |
| Requisitos | R-RH-6 ("horas extras e DSR a partir do ponto") · R-RH-4 · R-SEC-5 |
| Dependências | FLH-02a (motor), RH-01c (espelho fechado e banco de horas; existe antes da Fase 7, porque a Fase 6 executa a folha lançada antes do ponto e termina com ele), FLH-01 (tabelas), CAD-03 (feriados pelo `IBusinessCalendar`) |
| Bloqueia | FLH-03, FLH-05 (médias de variáveis), FLH-06 |
| Ordem de execução | 3 (Marco 7.1 "Tabelas e cálculo mensal") |

## User story
Como **RH**, quero **que as horas extras e o DSR saiam do ponto fechado, com os percentuais certos**, para **pagar o que foi trabalhado sem recontar as horas à mão**.

## Contexto e referências
- PRD R-RH-6; RH-01c (extras, faltas e banco de horas por dia, com tolerância de 5/10 min; banco de horas simples, só com acordo, **sem vencimento automático**, ponto 11 da Fase 6); CLT e convenção coletiva definem percentuais, adicional noturno, DSR e perda do DSR: **parâmetros**, nunca fixos.
- **Revisão pré-Fases 4 a 8** (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`): D1; D15 (o ponto do app é controle interno, não registro oficial: a folha usa o espelho fechado como fonte de horas, com a mesma ressalva); tabela 3.4, pontos 13 a 15 e linha "Determinismo"; §4.3 FLH-01/02.
- **ADR 0039** (determinismo e dataset sintético), **ADR 0042** (parâmetros "A confirmar com a contadora"), ADR 0035 (autoatendimento e ponto não oficial).
- Jornada da Konebras é diurna (CAD-04a, seed "08:00–17:48"); o adicional noturno fica pré-configurado, mas desligado.

## Critérios de aceite
1. **Dado** o espelho fechado (RH-01c), **quando** o motor calcula, **então** classifica cada minuto extra do dia em dia útil, domingo ou folga e feriado (feriado pelo `IBusinessCalendar`), e gera as linhas "Horas extras {percentual}%" com a referência em horas e o valor = horas × valor da hora × (1 + percentual), com os percentuais de `hr.overtime_rates`, pré-configurados com **50% no dia útil e 100% em domingo, folga e feriado**, com o chip "A confirmar com a contadora" (convenção do sindicato).
2. **Dado** o banco de horas ligado para o funcionário (RH-01c, só com acordo), **quando** calcula, **então** as extras do mês vão para o banco e não para pagamento; o saldo **não vence sozinho**: só sai por decisão do RH na folha ("Pagar {n} h do banco" ou "Descontar {n} h do banco", com motivo), que gera as linhas e baixa o saldo no fechamento; o saldo restante na rescisão é tratado pela FLH-05.
3. **Dado** horas no horário noturno do parâmetro `hr.night_shift` (pré-configurado: 22 h às 5 h, adicional de 20% e hora reduzida de 52 min 30 s; **desligado**), **quando** ligado e existirem horas, **então** gera "Adicional noturno" com o percentual e a redução da hora; desligado, nenhuma linha.
4. **Dado** as variáveis do mês (extras e adicional noturno), **quando** calcula, **então** gera o "DSR sobre variáveis" = variáveis ÷ dias úteis do mês × (domingos + feriados do mês), com os dias contados pelo `IBusinessCalendar` (fórmula pré-configurada em `hr.dsr_formula`, "A confirmar com a contadora").
5. **Dado** uma falta injustificada numa semana (sem abono aprovado), **quando** calcula, **então** desconta o DSR daquela semana pelo parâmetro `hr.dsr_loss_on_absence` (pré-configurado: **ligado**, "A confirmar com a contadora"), com a referência "DSR da semana de {data}"; desligado, não desconta.
6. **Dado** o dataset sintético do CI (ADR 0039: extras em dia útil, domingo e feriado, falta na semana, banco ligado e desligado, pagamento e desconto do banco, noturno ligado), **quando** calcula, **então** linhas e valores batem centavo a centavo; nenhum holerite real no Git.
7. **Dado** a memória de cálculo (FLH-02a CA 12), **quando** abro a linha de extras ou DSR, **então** vejo os dias de origem (link para o espelho), as horas, os percentuais e a versão do parâmetro usados; as horas do espelho entram no retrato das entradas do holerite.
8. **Dado** a folha `Draft`, **quando** um parâmetro de horas (CA 1, 3, 4, 5) muda com `settings.edit` depois do cálculo, **então** a folha mostra "Parâmetro mudou depois do cálculo" com "Recalcular", e a mudança aparece na lista de mudanças do diálogo de fechamento (FLH-01 CA 12).
9. **Dado** o módulo `hr` não liberado ou sem `hr.view_sensitive`, **quando** chamo, **então** 403 com o código da camada (`module_not_released` ou `forbidden`); "Pagar" e "Descontar" do banco exigem `hr.edit` + `hr.view_sensitive`; `RequireModule` e `perm:`.

## Regras de negócio e técnicas
- Novas regras no `PayrollCalculator`: `Overtime`, `NightShift`, `Dsr`, `DsrLoss`, `HourBankPayout`, cada uma testada por tabela; `calculator_version` sobe junto.
- Parâmetros em `hr.parameters` (ADR 0016 M6), editados em Configurações → Parâmetros → RH com `settings.edit`, pré-configurados e com "Marcar como conferido" (ADR 0042); o espelho fechado é a única fonte de horas (nada recalcula marcações).
- Baixa do saldo do banco no fechamento da folha, registrada no timesheet do mês (`bank_payout_minutes`, com o sinal).

## Permissões, eventos e tabelas
- **Permissões:** as da FLH-02a; `settings.edit` nos parâmetros.
- **Eventos:** nenhum novo.
- **Tabelas:** `hr.parameters` (+ `hr.overtime_rates`, `hr.night_shift`, `hr.dsr_formula`, `hr.dsr_loss_on_absence`); `hr.timesheets` (+ `bank_payout_minutes`).

## Telas e estados
- **Padrões comuns das Fases 4 a 8:** 8 (valores no bloco "Dados protegidos").
- Na folha: seção "Banco de horas" por funcionário com saldo, "Pagar" e "Descontar"; memória das extras e do DSR em painel lateral.
- **Estados:** parâmetros de extras apagados bloqueiam o cálculo com "Cadastre os percentuais de horas extras em Configurações → Parâmetros → RH." e o link; vazio da seção ("Sem banco de horas neste mês."); erro.
- **Propositivo:** com saldo positivo no banco acima de 40 h, a seção sugere "Pagar {n} h do banco" (sem decidir); os percentuais vêm preenchidos.
- **400 px:** seção do banco em cartões.
- **Ajuda:** atualizar `Ajuda/rh-folha-detalhe.md` ("Horas extras e DSR", "Banco de horas na folha") e `Ajuda/configuracoes-parametros.md` (seção RH); `<Dica>` em "DSR sobre variáveis" e "Perda do DSR".

## Plano de testes
- **Unidade (tabela de casos):** classificação do dia (CA 1), banco (CA 2), noturno (CA 3), DSR e perda (CA 4, 5), dataset sintético (CA 6).
- **Integração (Testcontainers):** memória com links e retrato (CA 7); baixa do banco no fechamento (CA 2); parâmetro mudou e aviso no fechamento (CA 8); três camadas (CA 9).

## Fora do escopo
- Escalas 12×36 e turnos de revezamento: fora do MVP.
- Médias de variáveis para férias, 13º e rescisão: FLH-03 a FLH-05.

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fases 4 a 8, tabela 3.4, pontos 13–15 e linha "Determinismo"):
1. Percentuais de horas extras e adicional noturno pela convenção, pré-configurados (50%, 100%, noturno 20% desligado) "A confirmar com a contadora" (CA 1, 3).
2. Fórmula do DSR e perda por falta pré-configuradas "A confirmar com a contadora" (CA 4, 5).
3. Banco de horas sem vencimento automático (RH-01c ponto 11): pago ou descontado só por decisão do RH; na rescisão, pela FLH-05 (CA 2).
4. Determinismo: horas do espelho no retrato do holerite (CA 7).

Decisões da revisão aplicadas: D1 (Fase 7 antes do go-live); D15 (ponto do app como controle interno; a folha lê o espelho fechado); §4.3 FLH-01/02 (parâmetro mudado aparece no fechamento, CA 8); dataset sintético no CI (ADR 0039, CA 6).

**Pré-condição externa:** conferência dos percentuais e da fórmula pela contadora, com a convenção do sindicato (valores pré-configurados "A confirmar com a contadora"; não bloqueia o `Ready`).

## Definition of Done
- [x] Critérios de aceite atendidos (ver o Change Log 1.0 para as recomendações aceitas e as pendências externas); testes de unidade (dataset sintético) e integração passando; E2E não escrito (regra do PO de 31/10).
- [x] Migration criada (não destrutiva, `HourBankAndOvertimeFlh02b`); parâmetros com seed e chip; ajuda atualizada; auditoria ligada.
- [x] OpenAPI e cliente Orval regenerados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [x] Regras do motor e parâmetros; seção do banco de horas na folha; casos no dataset sintético.

## Quality gates
- Agentes previstos: @dev, @qa, revisão da contadora (externa, nos parâmetros).
- Gate: CA 1, 2, 4 e 6 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fases 4 a 8).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: RH-01c existe antes da Fase 7 pela nova ordem da Fase 6) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** percentuais e fórmulas como parâmetros (CA 1, 4, 5); o espelho fechado como única fonte de horas.
- **Correções da revalidação (21/10/2026):** pontos 13 a 15 decididos, com valores pré-configurados "A confirmar com a contadora" (CA 1, 3, 4, 5); banco sem vencimento automático (CA 2); dataset sintético (CA 6); retrato das horas (CA 7); aviso de parâmetro mudado (CA 8); permissões do banco de horas (CA 9); FLH-06 em "Bloqueia"; ADRs 0035, 0039 e 0042 citadas; CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma de decisão de story. Externa: conferência dos parâmetros pela contadora (não bloqueia).
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready (pré-condição externa)`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da FLH-02) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (D1, D15, tabela 3.4 pontos 13–15 e "Determinismo", §4.3 FLH-01/02, §4.4 padrão 8); nova ordem (Marco 7.1, ordem 3, antes do go-live); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
| 01/11/2026 | 1.0 | Implementação (@dev): motor `PayrollCalculator` 1.1.0 com as regras `OvertimeWorkday`, `OvertimeRestDay`, `OvertimeHoliday`, `NightShift`, `HourBankPayout`, `HourBankDiscount`, `Dsr` e `DsrLoss` (puras, por tabela de casos), `PayrollHours` (classe do dia, minutos noturnos pelos pares congelados, dias úteis e de descanso); retrato com as horas do espelho fechado, o calendário da competência (`IBusinessCalendar`) e o banco de horas; linhas "Horas extras {percentual}%", "Adicional noturno", "DSR sobre variáveis", "DSR da semana de {data}", "Banco de horas pago" e "Banco de horas descontado" com a memória (dias, horas, percentual, parâmetro) e o link "Ver os dias no espelho de ponto"; seção "Banco de horas" na folha (saldo, sugestão acima de 40 h, "Pagar" e "Descontar" com motivo, `hr.edit` + `hr.view_sensitive`), baixa em `timesheets.bank_payout_minutes` no fechamento e desfeita na reabertura; 11 parâmetros novos "A confirmar com a contadora"; rubricas do seed com as regras (HE50, HE100, DSR, ADNOT) e três novas (DSRFALTA, BHPAGO, BHDESC); aviso "Parâmetro mudou depois do cálculo"; 422 sem os percentuais das extras; ajuda (`rh-folha-detalhe`, `configuracoes-parametros`) e o adendo da ADR 0039. Testes: dataset das horas (7 casos) centavo a centavo, regras por tabela de casos, determinismo do retrato e integração (Testcontainers); os 14 casos da FLH-02a seguem iguais | @dev |
| 01/11/2026 | 1.0 | Recomendações aceitas (PO 24/10): (1) **chaves**: `hr.overtime_rates` e `hr.night_shift` viraram chaves por item (`hr.overtime_rates.weekday|rest_day|holiday`, `hr.night_shift.enabled|pct|start|end|reduced_hour`), porque cada parâmetro guarda um valor, cada um com o próprio "Marcar como conferido"; os percentuais das extras não aceitam menos de 50% (CF art. 7º XVI) nem o noturno menos de 20% (CLT art. 73); (2) **dia sem jornada é folga**: o sábado sem jornada (semana de 44 h em 5 dias) usa o percentual de domingo ou folga (100%), o mais conservador, ajustável pelo parâmetro; (3) **valor da hora com os adicionais**: a insalubridade e a periculosidade do mês entram no valor da hora das extras, do noturno e do banco pago (Súmulas 132 e 139 do TST), pelo parâmetro novo `hr.overtime_base_includes_allowances` (ligado); as faltas e o desconto do banco seguem só pelo salário (como na FLH-02a); (4) **feriado** usa a rubrica HE100 até existir uma rubrica com a regra "Horas extras em feriado"; percentual diferente sai numa linha própria; (5) **DSR**: as variáveis são as extras, o adicional noturno e o banco pago (o banco pago é hora extra); "dias úteis" pré-configurado como dias do mês − domingos − feriados (o sábado é útil), com a opção dos dias úteis do calendário padrão; feriado é todo dia do calendário padrão sem horas (feriado ou parada), como no espelho; (6) **perda do DSR**: só a falta do dia inteiro sem abono (situação "Falta" do espelho), um dia de salário (÷ 30 no mês comercial) por semana de segunda a domingo; atraso e falta parcial não tiram o DSR (o mais favorável ao funcionário até a contadora conferir); (7) **adicional noturno**: os minutos vêm dos pares de marcação congelados no espelho (a virada da meia-noite de um par não é tratada: o espelho agrupa as marcações por dia); a hora reduzida aumenta só a base do adicional, sem gerar hora extra a mais; (8) **banco de horas**: a decisão fica no holerite (`hour_bank_decision_minutes` e o motivo, `[PersonalData]`), "Pagar" até o saldo positivo como extra de dia útil e "Descontar" até o negativo pelo valor da hora; a baixa vale no fechamento oficial (depois da virada da FLH-06) e corrige o saldo de espelhos seguintes já fechados; a sugestão acima de 40 h propõe pagar o saldo inteiro; (9) **parâmetro mudado**: quando só os parâmetros do retrato mudaram, o motivo é "Parâmetro mudou depois do cálculo" (a mudança do horário noturno muda as horas do retrato e aparece como "Cadastro ou parâmetro mudou"); (10) **percentual apagado**: sem a linha gravada (ou sem valor) de um dos três percentuais das extras, o cálculo é recusado com o link para os parâmetros; (11) E2E não escrito (regra do PO de 31/10) | @dev |
| 01/11/2026 | 1.0 | **Pendências externas:** conferência pela contadora, com a convenção coletiva do sindicato, dos 11 parâmetros das horas (percentuais das extras e do noturno, adicionais no valor da hora, fórmula do DSR, perda do DSR e o tratamento do sábado sem jornada), da perda do DSR por atraso e falta parcial, do pagamento do banco como extra de dia útil e se a hora noturna reduzida gera hora extra além do adicional (hoje só a base do adicional; o noturno vem desligado, a jornada da Konebras é diurna); o saldo do banco na rescisão é da FLH-05; a conferência paralela e a virada são da FLH-06 | @dev |
