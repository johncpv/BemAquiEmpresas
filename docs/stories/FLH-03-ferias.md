# FLH-03 — Folha calculada: férias (períodos aquisitivos, programação, cálculo e recibo)

| Campo | Valor |
|---|---|
| ID | FLH-03 |
| Título | Períodos aquisitivos por funcionário com saldo de dias (descontadas as faltas pela regra pré-configurada da CLT), programação das férias com aviso, fracionamento e abono pecuniário, cálculo da folha de férias (remuneração, 1/3, médias de variáveis, INSS e IRRF próprios), recibo e aviso em PDF, título a pagar com o vencimento antes do início, dias de férias no espelho e no "Meu ponto"; só simulação até a virada e o primeiro caso de férias conferido (FLH-06) |
| Tamanho | G |
| Status | Done (pré-condição externa: contadora e conferência paralela) |
| Requisitos | R-RH-6 ("férias") · R-RH-4 (dias de férias no ponto) · R-RH-5 (recibo e CP) · R-FIN-10 · R-PRP-5 · R-SEC-5 |
| Dependências | FLH-02a e FLH-02b (motor e médias), FLH-01 (tabelas), RH-01c (espelho), RH-02b/02c (PDF, fechamento e títulos, ADR 0036), CAD-04b (admissão), CAD-04c1 (afastamentos) |
| Bloqueia | FLH-04 (adiantamento do 13º nas férias), FLH-05 (férias vencidas e proporcionais na rescisão) |
| Ordem de execução | 4 (Marco 7.2 "Férias, 13º e rescisão") |

## User story
Como **RH**, quero **saber quem tem férias a vencer, programar as férias de cada um e gerar o cálculo e o recibo**, para **não pagar férias em dobro por atraso e ter o valor no caixa antes do pagamento**.

## Contexto e referências
- PRD R-RH-6; `03-modelo-dados` → `payroll_runs.type = Vacation`.
- CLT (férias: período aquisitivo e concessivo, proporção pelas faltas, 1/3, abono, fracionamento, prazo de pagamento): as regras entram como **parâmetros pré-configurados pela CLT** com o chip "A confirmar com a contadora" (ADR 0042) e são cobertas pelo dataset sintético do CI (ADR 0039).
- CAD-04c1: afastamentos (alguns interferem no período aquisitivo, pela regra pré-configurada).
- **Revisão pré-Fases 4 a 8** (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`): D1, D6, D10 (a capacidade é por centro de trabalho; férias **não reduzem** a capacidade do planejamento, ADR 0029), D14 (título por funcionário, ADR 0036), D15 ("Meu ponto" em `/api/v1/me/hr/*`, ADR 0035); tabela 3.4, pontos 16 a 19 e 27 e linha "Determinismo"; §4.4 padrão 8.
- **ADR 0039** (motor, determinismo, dados sintéticos), **ADR 0036**, **ADR 0035**, **ADR 0042**.

## Critérios de aceite
**Períodos aquisitivos**
1. **Dado** um funcionário, **quando** abro a aba "Férias", **então** vejo os períodos aquisitivos desde a admissão (início, fim, dias de direito, dias gozados, dias vendidos, saldo, fim do período concessivo) com os chips "Em aquisição", "A gozar", "Vence em {data}" (alerta a 60 dias) e "Vencido" (crítico); os dias de direito seguem `hr.vacation_absence_brackets` (pré-configurado pela CLT: 30 dias até 5 faltas injustificadas, 24 de 6 a 14, 18 de 15 a 23, 12 de 24 a 32, nenhum acima de 32).
2. **Dado** um afastamento no período aquisitivo (CAD-04c1), **quando** o período é calculado, **então** segue `hr.vacation_leave_rules` (pré-configurado: afastamento pela previdência por mais de 6 meses, contínuos ou não, ou licença remunerada por mais de 30 dias, encerram o período e um novo começa no retorno), com a memória "Período reiniciado em {data}: {motivo}".
3. **Dado** o job diário, **quando** um período concessivo entra nos 60 dias antes do fim, **então** quem tem `hr.edit` recebe "Férias de {funcionário} vencem em {data}." com link (`dedup_key` `vacation-due:{periodId}`), e a Minha pauta (DSH-04) lista o item.

**Programação**
4. **Dado** `hr.edit`, **quando** clico "Programar férias", escolho o período aquisitivo, o início, os dias, o abono pecuniário e o adiantamento do 13º se pedido, **então** a programação fica `Scheduled`, e a validação recusa com 422 o que fere `hr.vacation_split` (pré-configurado: até 3 períodos, um com pelo menos 14 dias e os demais com pelo menos 5), o limite do abono (até 1/3 dos dias de direito) e o início nos 2 dias antes de feriado ou do repouso semanal, com a mensagem e o valor do parâmetro ("O primeiro período precisa ter pelo menos 14 dias.").
5. **Dado** a programação, **quando** salvo, **então** o espelho mostra "Férias" nos dias (RH-01c), o funcionário vê as datas e o saldo no "Meu ponto" (`/api/v1/me/hr/*`, ADR 0035, sem valores), o evento `EmployeeVacationScheduled` (ids e datas) sai pelo outbox e o `NextSteps` oferece "Gerar aviso de férias" (PDF pelo `IPdfRenderer`), avisando quando o início fica a menos de `hr.vacation_notice_days` (pré-configurado: 30 dias); a capacidade do planejamento não muda (D10).

**Cálculo**
6. **Dado** uma programação, **quando** clico "Calcular férias", **então** nasce a folha de férias (tipo `Vacation`, modo `Calculated`) com: remuneração dos dias (salário + adicionais fixos + média das variáveis dos 12 meses do período aquisitivo, `hr.vacation_averages`), 1/3 constitucional, abono pecuniário e o seu 1/3, INSS e IRRF pela regra pré-configurada `hr.vacation_taxation` (INSS sobre férias + 1/3 na competência do gozo, rateado quando atravessa o mês, junto com o salário; IRRF em separado no pagamento; abono e o seu 1/3 sem INSS, FGTS nem IRRF), com a memória e o retrato das entradas (FLH-02a CA 12).
7. **Dado** o dataset sintético do CI (férias integrais, fracionadas, com abono, com médias de extras, com faltas e com afastamento que reinicia o período), **quando** calculado, **então** cada valor bate centavo a centavo; nenhum holerite real no Git.
8. **Dado** a folha de férias calculada, **quando** a virada da FLH-06 não aconteceu ou ainda não há **um caso de férias conferido** com diferença zero na FLH-06, **então** a calculada é simulação: "Fechar" fica bloqueado com "Férias calculadas em conferência: feche a folha de férias lançada." e a prévia do recibo sai com a marca "Simulação".
9. **Dado** a folha de férias oficial (depois do CA 8), **quando** quem tem `hr.approve` fecha (RH-02c: 2FA de novo, D6; quem fecha não paga), **então** o título a pagar do funcionário (`payee_type = Employee`, ADR 0036) vence até `hr.vacation_payment_days_before` (pré-configurado: 2 dias antes do início), entra no fluxo de caixa e o recibo de férias em PDF fica disponível; fechada depois do prazo, o diálogo avisa "Pagamento depois do prazo legal: confirme com a contadora." sem bloquear.
10. **Dado** as férias gozadas, **quando** a folha mensal da competência é calculada (FLH-02a), **então** os dias de férias não entram como salário de novo e a compensação segue `hr.vacation_taxation`.

**Cancelamento e permissões**
11. **Dado** uma programação `Scheduled` sem folha fechada, **quando** cancelo com motivo, **então** volta o saldo, somem os dias do espelho e sai `EmployeeVacationCancelled`; com folha fechada, 409 "Reabra a folha de férias antes."
12. **[Segurança]** **Dado** o módulo `hr` não liberado, **quando** chamo, **então** 403 `module_not_released`; programar com `hr.edit`, valores com `hr.view_sensitive`, fechar com `hr.approve`; o funcionário vê só a própria programação e saldo, sem valores, pelas rotas `/api/v1/me/hr/*` (IDOR: outro funcionário dá 404). `RequireModule` e `perm:`; canário de valores fora do log.
13. **Dado** programação, cálculo e cancelamento, **quando** abro o histórico, **então** vejo quem, quando, os valores mascarados para quem não tem `hr.view_sensitive`, a versão do motor e as tabelas usadas.

## Regras de negócio e técnicas
- **Tabelas:** `hr.vacation_periods` (`employee_id`, `starts_on`, `ends_on`, `entitled_days`, `taken_days`, `sold_days`, `concession_ends_on`, `status` Accruing/Available/Expired/Closed, `restarted_reason`), `hr.vacation_schedules` (`period_id`, `starts_on`, `days`, `sold_days`, `advance_13th`, `status` Scheduled/Paid/Taken/Cancelled, `payroll_run_id`).
- **Regras** no `PayrollCalculator` (`VacationPay`, `VacationThird`, `VacationSale`, `VacationAverages`) e num `VacationPolicy` com os parâmetros pré-configurados (`hr.vacation_absence_brackets`, `hr.vacation_leave_rules`, `hr.vacation_split`, `hr.vacation_notice_days`, `hr.vacation_payment_days_before`, `hr.vacation_averages`, `hr.vacation_taxation`), todos "A confirmar com a contadora" em Configurações → Parâmetros → RH.
- Datas de negócio em `date`.

## Permissões, eventos e tabelas
- **Permissões:** `hr.view`, `hr.edit`, `hr.view_sensitive`, `hr.approve` (fechar). Autoatendimento pelo vínculo (ADR 0035).
- **Eventos:** `EmployeeVacationScheduled`/`EmployeeVacationCancelled` (`{ employeeId, from, to }`, sem valor), dentro do HR (espelho, "Meu ponto").
- **Tabelas:** acima; parâmetros de férias em `hr.parameters`.

## Telas e estados
- **Padrões comuns das Fases 4 a 8:** 8 (bloco "Dados protegidos"; o "Meu ponto" com o mesmo layout no `/fab` e no celular).
- Aba **Férias** no funcionário; **RH → Férias** (`/app/rh/ferias`) com os vencimentos dos próximos 90 dias e um calendário por mês; diálogo "Programar férias".
- **Verbos:** "Programar férias" / "Férias programadas"; "Calcular férias" / "Férias calculadas"; "Cancelar programação" / "Programação cancelada".
- **Estados:** vazio ("Nenhum período aquisitivo completo ainda." + "Ver funcionários"); vencido em destaque; erro; carregando.
- **Propositivo:** o início sugerido é o primeiro dia permitido depois do aviso de 30 dias que não caia nos 2 dias antes de feriado ou repouso; os dias vêm com o saldo do período; o aviso é oferecido no prazo.
- **400 px:** períodos em cartões; diálogo em tela cheia.
- **Ajuda:** `Ajuda/rh-ferias.md`, `Ajuda/rh-funcionario-ferias.md` (aba `ferias`); `<Dica>` em "Período concessivo", "Abono pecuniário" e "Fracionamento".

## Plano de testes
- **Unidade (tabela de casos):** períodos e saldo com faltas e afastamento (CA 1, 2); validações da programação (CA 4); cálculo e tributação (CA 6, 7).
- **Integração (Testcontainers):** job de aviso e pauta (CA 3); espelho, "Meu ponto" e evento, capacidade sem mudança (CA 5); simulação até a conferência (CA 8); folha, título do funcionário e prazo (CA 9); mensal sem duplicar (CA 10); cancelamento (CA 11); três camadas, autoatendimento e IDOR (CA 12); histórico (CA 13).
- **E2E (Playwright):** programar, calcular e baixar a prévia do recibo no tenant `demo`.

## Fora do escopo
- Férias coletivas: fora do MVP; se a Konebras usar, vira story nova.
- Rescisão com férias: FLH-05.

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fases 4 a 8, tabela 3.4, pontos 16–19 e 27 e linha "Determinismo"):
1. Regras de férias pré-configuradas pela CLT, "A confirmar com a contadora": faltas, afastamentos que reiniciam o período, fracionamento, abono, aviso de 30 dias, pagamento 2 dias antes (CA 1, 2, 4, 5, 9).
2. INSS e IRRF das férias pela regra pré-configurada (INSS na competência do gozo, IRRF em separado, abono sem incidência), a confirmar (CA 6).
3. Férias coletivas fora, salvo se a Konebras usar.
4. Alerta de vencimento com 60 dias (CA 3).
5. Férias calculadas só viram oficiais depois da virada e de um caso de férias conferido na FLH-06 (CA 8).
6. Determinismo: retrato das entradas e versão do motor na folha de férias (CA 6).

Decisões da revisão aplicadas: D1 (Fase 7 antes do go-live); D6 (2FA no fechamento, CA 9); D10 (férias não reduzem a capacidade do planejamento, CA 5); D14/ADR 0036 (título do funcionário, CA 9); D15/ADR 0035 ("Meu ponto" em `/api/v1/me/hr/*`, CA 5, 12); dataset sintético (ADR 0039, CA 7).

**Pré-condição externa:** conferência dos parâmetros de férias pela contadora (valores pré-configurados "A confirmar com a contadora"; não bloqueia o `Ready`).

## Definition of Done
- [ ] Critérios de aceite atendidos; testes de unidade (dataset sintético), integração e E2E passando.
- [ ] Migration criada (não destrutiva); parâmetros com seed e chip; job registrado com `tenantId`; ajuda escrita; teste de cobertura passando.
- [ ] Auditoria ligada; OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` e `docs/04-fluxos-estados.md` atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `VacationPeriod`, `VacationSchedule`, `VacationPolicy`; regras do motor; PDFs de aviso e recibo; job; telas e "Meu ponto".

## Quality gates
- Agentes previstos: @dev, @qa, revisão da contadora (externa, nos parâmetros).
- Gate: CA 4, 7, 8 e 9 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fases 4 a 8).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: CAD-04c1; FLH-04 em "Bloqueia") |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** prazos e limites legais como parâmetros com casos de teste (pontos 1 e 2); o pagamento fora do prazo avisa sem bloquear (CA 7); o funcionário vê o saldo sem valores (CA 10).
- **Correções da revalidação (21/10/2026):** pontos 16 a 19 decididos, com as regras da CLT pré-configuradas "A confirmar com a contadora" (CA 1, 2, 4, 5, 6, 9); simulação até a virada e o primeiro caso conferido (ponto 27, CA 8); férias fora da capacidade do planejamento (D10, CA 5); "Meu ponto" pela ADR 0035 (CA 5, 12); título do funcionário e 2FA no fechamento (D14, D6, CA 9); dataset sintético (CA 7); sugestão de início definida (sem "proposta"); ADRs 0029, 0035, 0036, 0039 e 0042 citadas; CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma de decisão de story. Externa: conferência dos parâmetros pela contadora (não bloqueia).
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready (pré-condição externa)`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (D1, D6, D10, D14, D15, tabela 3.4 pontos 16–19 e 27 e "Determinismo", §4.4 padrão 8); nova ordem (Marco 7.2, ordem 4, antes do go-live); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
