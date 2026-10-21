# RH-01c — Ponto: espelho mensal, banco de horas simples e fechamento do período

| Campo | Valor |
|---|---|
| ID | RH-01c |
| Título | Espelho de ponto mensal por funcionário (esperado pela jornada e pelo calendário, trabalhado, extras, faltas, atrasos, abonos e inconsistências), banco de horas simples com saldo acumulado, fechamento do período com os totais congelados, reabertura com motivo e PDF do espelho |
| Tamanho | G (quebra da RH-01: cálculo diário, banco de horas, fechamento e PDF) |
| Status | Draft |
| Requisitos | R-RH-4 (parte: espelho mensal, banco de horas simples, fechamento) · R-RH-5 (base de horas para a folha lançada) · R-COM-3 · PRD §6 (Desempenho, LGPD) · R-SEC-5 · R-PRP-2 · R-PRP-5 |
| Dependências | RH-01a (marcações), RH-01b (ajustes e abonos), CAD-04a (jornada), CAD-04c (afastamentos), CAD-03 (calendário padrão e feriados), COM-03 (`IPdfRenderer`, ADR 0022) |
| Bloqueia | RH-02a (horas do período como referência), RH-03 (exportação do ponto), FLH-02b (horas extras e DSR do ponto) |
| Ordem de execução | 3 (Marco 6.1 "Ponto") |

## User story
Como **RH**, quero **ver o espelho do mês de cada funcionário com as horas trabalhadas, extras, faltas e inconsistências, e fechar o período**, para **mandar à contadora (e depois à folha calculada) horas conferidas, com o banco de horas de cada um em dia**.

## Contexto e referências
- PRD R-RH-4 (espelho mensal, banco de horas simples), R-RH-5 (folha lançada), R-RH-6 (folha calculada usa HE e DSR do ponto, Fase 7).
- `03-modelo-dados` → `hr.timesheets` (`employee_id`, `period` ano-mês, `worked_hours`, `overtime_hours`, `absences`, `hour_bank_balance`, `status` Open/Closed); `hr.work_schedules` (dias, horários e intervalo); `production.calendars` e `calendar_exceptions` (CAD-03: feriados e paradas do calendário padrão).
- `CLAUDE.md`: datas de negócio em `date`, exibição em America/Sao_Paulo; quantidades em `numeric(18,4)`.
- CLT art. 58 §1 (variações de até 5 minutos por marcação, no máximo 10 por dia, não contam) e art. 59 (banco de horas depende de acordo): as regras legais entram como **parâmetro** confirmado pela contadora (Pontos em aberto 1 e 2), nunca fixas no código.
- ADR 0022 (PDF pelo `IPdfRenderer`), ADR 0016 (`SensitiveRead`), ADR 0019 §2 (`xmin`), ADR 0016 adendo M6 (parâmetros do HR).

## Critérios de aceite
**Espelho**
1. **Dado** `hr.view`, **quando** abro RH → Ponto → Espelho, escolho o mês e o funcionário, **então** vejo uma linha por dia com: esperado (pela jornada do funcionário no dia da semana, zero em feriado ou parada do calendário padrão, CAD-03), marcações válidas (hora local, origem), trabalhado (soma dos pares entrada/saída menos os intervalos marcados), extras, falta, atraso, abono aprovado (RH-01b) e a situação do dia (Normal, Extra, Falta, Afastado, Feriado, Folga, Inconsistente).
2. **Dado** o dataset fixo de teste (jornada 44 h, segunda a sexta 08:00–17:48 com 1 h de intervalo, um feriado, um dia com 2 h extras, um dia com 30 min de atraso, um dia sem marcação, um dia com marcação ímpar e um abono de 4 h), **quando** o espelho é calculado, **então** os totais do mês batem com a planilha de referência anexada ao teste, minuto a minuto.
3. **Dado** o parâmetro de tolerância (proposta `hr.time_tolerance_per_entry_minutes` = 5 e `hr.time_tolerance_per_day_minutes` = 10, em `hr.parameters`, editáveis com `settings.edit`, Pontos em aberto 1), **quando** a diferença do dia fica dentro da tolerância, **então** o dia conta como Normal, sem extra nem atraso; acima dela, conta a diferença inteira.
4. **Dado** um dia com número ímpar de marcações, tipo fora de sequência, ou intervalo menor que o da jornada, **quando** o espelho é montado, **então** o dia fica "Inconsistente" (chip alerta) com o motivo ("Falta a saída", "Intervalo de 40 min, a jornada prevê 60") e o atalho "Lançar marcação" ou "Ver pedidos de ajuste" (RH-01b); o trabalhado do dia inconsistente conta só os pares fechados.
5. **Dado** um afastamento vigente (CAD-04c) ou uma data depois do desligamento, **quando** o espelho é montado, **então** o dia fica "Afastado" ou fora do período do funcionário, sem falta; para quem não tem `hr.view_sensitive`, o tipo do afastamento não aparece (só "Afastado").
6. **Dado** o funcionário, **quando** abre "Meu ponto" → "Meu espelho", **então** vê o próprio espelho do mês em aberto e dos fechados, nunca o de outro (IDOR).

**Banco de horas**
7. **Dado** o parâmetro `hr.hour_bank_enabled` (proposta: ligado; Pontos em aberto 2), **quando** o mês é calculado, **então** o saldo do mês = trabalhado + abonado − esperado (em minutos), e o saldo acumulado = saldo de fechamento do mês anterior + saldo do mês; o espelho mostra "Banco de horas: +3 h 20 min no mês, +5 h 10 min acumulado".
8. **Dado** um abono do tipo "Folga compensada" (RH-01b), **quando** aprovado, **então** ele não conta como hora abonada e desconta do banco o tempo da folga (o dia esperado é compensado pelo saldo), com a marca "Compensado do banco".
9. **Dado** o banco desligado, **quando** o mês é calculado, **então** extras e faltas aparecem como totais do mês para a folha (HE a pagar e faltas a descontar), sem saldo acumulado.

**Fechamento**
10. **Dado** `hr.edit` (proposta), **quando** clico "Fechar o período {mês}" no espelho geral, **então** o diálogo lista as pré-condições por funcionário: pedidos de ajuste pendentes (bloqueia) e dias inconsistentes (bloqueia, com "Marcar como revisado" por dia, que exige comentário); com tudo resolvido, confirmar "Fechar período" grava, para cada funcionário ativo no mês, o `timesheet` com os totais congelados (esperado, trabalhado, extras, faltas, atrasos, abonado, saldo do mês e acumulado) e `status = Closed`, na mesma transação; a mensagem de sucesso diz "Período {mês} fechado" e o `NextSteps` oferece "Baixar espelhos em PDF" e "Lançar a folha de {mês}" (RH-02a).
11. **Dado** um período fechado, **quando** alguém tenta lançar, pedir ou decidir ajuste nele (RH-01b CA 14), **então** 409; o espelho mostra a faixa "Período fechado em {data} por {usuário}".
12. **Dado** `hr.approve`, **quando** clico "Reabrir período" e informo o motivo, **então** os timesheets do mês voltam a `Open`, os meses seguintes já fechados também voltam a `Open` (o saldo acumulado depende deles), os membros do Proprietário são notificados e a folha do mês, se já fechada (RH-02c), mostra o aviso "O ponto deste mês foi reaberto depois do fechamento da folha."
13. **[Segurança]** **Dado** dois fechamentos simultâneos do mesmo período, **quando** chegam, **então** só um grava (lock do período e `xmin`), o outro recebe 409 `concurrency_conflict`.

**PDF do espelho**
14. **Dado** um período fechado, **quando** clico "Baixar espelho em PDF" (um funcionário) ou "Baixar espelhos em PDF" (todos, um arquivo por funcionário num ZIP), **então** o PDF é gerado pelo `IPdfRenderer` com o logo do tenant, nome, matrícula, cargo, mês, os dias, os totais, o banco de horas e o campo de assinatura do funcionário (proposta), sem CPF, sem salário e sem a localização; é guardado como arquivo do sistema (`StoreGeneratedAsync`, dono `Timesheet`, leitura com `hr.view`) e o download fica auditado.

**Desempenho e permissões**
15. **Dado** 50 funcionários com 4 marcações por dia útil, **quando** abro o espelho geral do mês, **então** a tela responde em menos de 1 s (teste de desempenho com o dataset).
16. **Dado** o módulo `hr` não liberado, **quando** chamo, **então** 403 `module_not_released`; espelho dos outros com `hr.view`, fechar com `hr.edit`, reabrir com `hr.approve`; sem a ação, 403 `forbidden` e o botão some. `RequireModule` e `perm:`; IDOR em `/employees/{id}/timesheets/{period}`.
17. **Dado** fechamento, reabertura, "Marcar como revisado" e download, **quando** abro o histórico, **então** vejo quem, quando, o motivo e os totais antes/depois.

## Regras de negócio e técnicas
- **Cálculo** num serviço puro do Domain (`TimesheetCalculator`), com entrada: jornada, calendário (feriados e paradas), marcações válidas, abonos aprovados, afastamentos, parâmetros; saída por dia e totais. Sem acesso a banco, para o teste de tabela.
- **Unidade:** minutos inteiros no cálculo (proposta); exibição "h min"; os totais congelados em `hr.timesheets` como minutos (`integer`) ou horas `numeric(18,4)` (decisão técnica no DoD).
- **Período:** mês civil (proposta, Pontos em aberto 3).
- **Calendário:** lido do Production por contrato síncrono novo em `Production.Contracts` (proposta: `IBusinessCalendar.ExceptionsAsync(from, to)` do calendário padrão), sem acessar tabela de outro módulo.
- **Fechamento:** idempotente por `(employee_id, period)` único; reabertura não apaga, guarda o histórico.
- **Tabela:** `hr.timesheets` (03) + `expected_minutes`, `worked_minutes`, `overtime_minutes`, `absence_minutes`, `late_minutes`, `justified_minutes`, `bank_month_minutes`, `bank_balance_minutes`, `closed_at`, `closed_by`, `reopened_at`, `reopened_by`, `reopen_reason`; `hr.timesheet_day_reviews` (dia marcado como revisado, com comentário).

## Permissões, eventos e tabelas
- **Permissões:** `hr.view` (espelho), `hr.edit` (fechar, marcar revisado; proposta), `hr.approve` (reabrir), `settings.edit` (parâmetros); autoatendimento para "Meu espelho".
- **Eventos:** `TimesheetPeriodClosed` (`{ period }`) e `TimesheetPeriodReopened` (`{ period }`), em `HR.Contracts`, sem dado pessoal; consumidos dentro do HR pela folha (RH-02a/FLH-02b).
- **Contratos:** `IBusinessCalendar` (Production, proposta).
- **Tabelas:** `hr.timesheets` (extensão), `hr.timesheet_day_reviews` (nova), `hr.parameters` (+ tolerâncias e banco).

## Telas e estados
- **RH → Ponto → Espelho** (`/app/rh/ponto/espelho`): visão geral do mês (um funcionário por linha com trabalhado, extras, faltas, banco e chip de situação) e o espelho do funcionário (`/app/rh/ponto/espelho/$employeeId?mes=2026-10`), dias em tabela; faixa lateral âmbar no dia inconsistente.
- **"Meu ponto" → Meu espelho.**
- **Verbos:** "Fechar período" / "Período fechado"; "Reabrir período" / "Período reaberto"; "Marcar como revisado" / "Dia revisado"; "Baixar espelho em PDF".
- **Estados:** carregando; vazio ("Nenhuma marcação neste mês. As marcações chegam pelo Meu ponto ou pelo lançamento manual."); erro; período fechado; pré-condições do fechamento.
- **400 px:** dias em cartões, com o trabalhado e a situação sempre visíveis; totais no topo.
- **Propositivo:** o diálogo de fechamento leva direto a cada pendência; depois do fechamento, "Lançar a folha de {mês}".
- **Ajuda:** `Ajuda/rh-ponto-espelho.md`, `Ajuda/rh-ponto-espelho-funcionario.md`, atualizar `Ajuda/meu-ponto.md`; `<Dica>` em "Tolerância", "Banco de horas" e "Inconsistente".

## Plano de testes
- **Unidade (tabela de casos):** cálculo do dia e do mês com o dataset fixo (CA 2); tolerância (CA 3); inconsistências (CA 4); afastamento e desligamento (CA 5); banco de horas, folga compensada e banco desligado (CA 7–9).
- **Integração (Testcontainers):** espelho por permissão e IDOR (CA 1, 5, 6); fechamento com pré-condições, totais congelados e idempotência (CA 10); período fechado bloqueia ajuste (CA 11); reabertura em cascata e aviso na folha (CA 12); concorrência (CA 13); PDF sem CPF, salário nem localização, pelo texto extraído (CA 14); desempenho com 50 funcionários (CA 15); três camadas (CA 16); histórico (CA 17).
- **E2E (Playwright):** fechar o mês com uma pendência resolvida no caminho; espelho a 400 px.

## Fora do escopo
- Horas extras com percentuais, adicional noturno e DSR em valor: FLH-02b (Fase 7). Aqui só as horas.
- Regras de vencimento e de pagamento do saldo do banco de horas: Pontos em aberto 2.
- Assinatura eletrônica do espelho pelo funcionário: fora do MVP.

## Pontos em aberto
1. **Tolerância** (contadora): 5 min por marcação e 10 por dia (proposta pelo CLT art. 58 §1), ou o que a convenção do sindicato definir.
2. **Banco de horas** (PO + contadora): todos os funcionários no banco ou só quem tem acordo; prazo de compensação e o que fazer com o saldo vencido (pagar como HE ou zerar); limite de saldo. Proposta do MVP: banco simples, sem vencimento automático, com o saldo visível e a decisão de pagar feita na folha.
3. **Período de apuração** (contadora): mês civil (proposta) ou um corte (ex.: 21 a 20).
4. **Quem fecha o período** (PO): `hr.edit` (proposta) ou `hr.approve`.
5. **Assinatura do espelho** (jurídico): o PDF com campo de assinatura atende o controle interno? Ligado ao ponto em aberto 5 da RH-01a.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade (tabela de casos), integração (Testcontainers), desempenho e E2E passando.
- [ ] Migration criada (não destrutiva).
- [ ] Permissões só do `ModuleCatalog`; contrato `IBusinessCalendar` no Production.
- [ ] Telas com estados; ajuda escrita; teste de cobertura passando.
- [ ] Próximos passos aplicados.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `TimesheetCalculator` (Domain, puro) e o dataset de referência.
- [ ] `IBusinessCalendar` no Production; leitura dos afastamentos e da jornada no HR.
- [ ] Fechamento e reabertura; `TimesheetPdfDocument`.
- [ ] Endpoints `/api/v1/hr/timesheets` e `/api/v1/hr/me/timesheets`; telas e ajuda.

## Quality gates
- Agentes previstos: @dev, @architect (contrato de calendário), @qa (tabela de casos).
- Gate: CA 2, 10, 12 e 14 obrigatórios.

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

- **Correções feitas na validação:** re-estimada para G (cálculo, banco, fechamento e PDF); tolerância e banco como parâmetros, sem regra legal fixa (CA 3, 7); calendário por contrato do Production, sem ler tabela alheia; reabertura em cascata por causa do saldo acumulado (CA 12); PDF sem dado sensível (CA 14).
- **Pré-condições para `Ready`:** pontos em aberto 1, 2 e 3; RH-01b concluída.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da RH-01) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
