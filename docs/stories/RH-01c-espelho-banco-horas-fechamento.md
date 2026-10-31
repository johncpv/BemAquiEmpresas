# RH-01c — Ponto: espelho mensal, banco de horas simples e fechamento do período

| Campo | Valor |
|---|---|
| ID | RH-01c |
| Título | Espelho de ponto do período por funcionário (esperado pela jornada e pelo calendário, trabalhado, extras, faltas, atrasos, abonos e inconsistências), tolerância, banco de horas simples (só com acordo) e período de apuração como parâmetros pré-configurados "A confirmar com a contadora"; fechamento do período com os totais congelados, reabertura com motivo, "Li e confirmo" do funcionário, PDF do espelho; horas do espelho na folha lançada e o `ponto.csv` no pacote da contadora |
| Tamanho | G (quebra da RH-01: cálculo diário, banco de horas, fechamento e PDF) |
| Status | Done (pré-condição externa: conferência da contadora e do jurídico) |
| Requisitos | R-RH-4 (parte: espelho mensal, banco de horas simples, fechamento) · R-RH-5 (base de horas para a folha lançada) · R-RH-8 (D, ponto no pacote da contadora) · R-COM-3 · PRD §6 (Desempenho, LGPD) · R-SEC-5 · R-PRP-2 · R-PRP-3 · R-PRP-5 |
| Dependências | RH-01a (marcações), RH-01b (ajustes, abonos e a lista única de ausências), CAD-04a (jornada), CAD-04c1 (afastamentos), CAD-03 (calendário padrão e feriados), COM-03 (`IPdfRenderer`, ADR 0022), RH-02a (`IBusinessCalendar`, parâmetros do RH, folha lançada), RH-03 (pacote da contadora) |
| Bloqueia | LGPD-01a (exportação inclui espelhos), FLH-02b (horas extras e DSR do ponto) |
| Ordem de execução | 8 (Marco 6.1 "Ponto") |

## User story
Como **RH**, quero **ver o espelho do período de cada funcionário com as horas trabalhadas, extras, faltas e inconsistências, e fechar o período**, para **mandar à contadora (e depois à folha calculada) horas conferidas, com o banco de horas de cada um em dia**.

## Contexto e referências
- PRD R-RH-4 (espelho mensal, banco de horas simples), R-RH-5 (folha lançada), R-RH-6 (folha calculada usa HE e DSR do ponto, Fase 7), R-RH-8 (exportação para a contadora).
- `03-modelo-dados` → `hr.timesheets` (`employee_id`, `period` ano-mês, `worked_hours`, `overtime_hours`, `absences`, `hour_bank_balance`, `status` Open/Closed); `hr.work_schedules` (dias, horários e intervalo); calendário padrão da CAD-03 (feriados e paradas), lido pelo `IBusinessCalendar`.
- `CLAUDE.md`: datas de negócio em `date`, exibição em America/Sao_Paulo; quantidades em `numeric(18,4)`.
- CLT art. 58 §1 (variações de até 5 minutos por marcação, no máximo 10 por dia, não contam) e art. 59 (banco de horas depende de acordo): as regras legais entram como **parâmetro** pré-configurado e conferido pela contadora, nunca fixas no código.
- **Revisão pré-Fases 4 a 8** (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`): D15 (ponto como controle interno; a folha vem antes e o ponto passa a alimentá-la quando existir), tabela 3.3 pontos 10 a 14, §4.2 (`IBusinessCalendar` no `Production.Contracts`), §4.4 padrões 3 e 8; novo requisito "tudo configurável em Configurações" (ADR 0042).
- **ADR 0035** (autoatendimento: "Meu espelho" em `/api/v1/me/hr/*`); ADR 0042 (parâmetros "A confirmar com a contadora"); ADR 0022 (PDF pelo `IPdfRenderer`), ADR 0016 (`SensitiveRead`; adendo M6, parâmetros do HR), ADR 0019 §2 (`xmin`).

## Critérios de aceite
**Parâmetros**
1. **Dado** `settings.edit`, **quando** abro Configurações → Parâmetros → RH (RH-02a CA 3), **então** vejo, pré-configurados e editáveis, com o chip "A confirmar com a contadora" e "Marcar como conferido" (quem e quando auditados): tolerância por marcação (`hr.time_tolerance_per_entry_minutes` = 5) e por dia (`hr.time_tolerance_per_day_minutes` = 10); banco de horas (`hr.hour_bank_enabled` = ligado, valendo só para o funcionário com acordo, sem vencimento automático); início do período de apuração (`hr.timesheet_period_start_day` = 1, mês civil; outro dia, como 21, faz o corte de 21 a 20); mudar um parâmetro vale para os períodos em aberto, nunca para os fechados.
2. **Dado** `hr.edit` + `hr.view_sensitive`, **quando** marco no funcionário "Tem acordo de banco de horas" com a data do acordo, **então** o banco de horas passa a valer para ele a partir daquela data; sem acordo, extras e faltas saem como totais do período (CA 10).

**Espelho**
3. **Dado** `hr.view`, **quando** abro RH → Ponto → Espelho, escolho o período e o funcionário, **então** vejo uma linha por dia com: esperado (pela jornada do funcionário no dia da semana, zero em feriado ou parada do calendário padrão, pelo `IBusinessCalendar`), marcações válidas (hora local, origem), trabalhado (soma dos pares entrada/saída menos os intervalos marcados), extras, falta, atraso, abono aprovado (RH-01b) e a situação do dia (Normal, Extra, Falta, Afastado, Feriado, Folga, Inconsistente); a faixa "Controle interno de jornada. Este registro não substitui o registro legal de ponto (REP-P, Portaria 671)." fica no topo.
4. **Dado** o dataset fixo de teste (jornada 44 h, segunda a sexta 08:00–17:48 com 1 h de intervalo, um feriado, um dia com 2 h extras, um dia com 30 min de atraso, um dia sem marcação, um dia com marcação ímpar e um abono de 4 h), **quando** o espelho é calculado, **então** os totais do período batem com a planilha de referência anexada ao teste, minuto a minuto, no mês civil e no corte de 21 a 20.
5. **Dado** a tolerância do CA 1, **quando** a diferença do dia fica dentro dela, **então** o dia conta como Normal, sem extra nem atraso; acima dela, conta a diferença inteira.
6. **Dado** um dia com número ímpar de marcações, tipo fora de sequência, ou intervalo menor que o da jornada, **quando** o espelho é montado, **então** o dia fica "Inconsistente" (chip alerta) com o motivo ("Falta a saída", "Intervalo de 40 min, a jornada prevê 60") e o atalho "Lançar marcação" ou "Ver pedidos de ajuste" (RH-01b); o trabalhado do dia inconsistente conta só os pares fechados.
7. **Dado** um afastamento vigente (CAD-04c1) ou uma data depois do desligamento, **quando** o espelho é montado, **então** o dia fica "Afastado" ou fora do período do funcionário, sem falta; o motivo do afastamento aparece só com `hr.view_sensitive`, e o motivo de saúde só com `hr.view_health` (RH-01b CA 9); para os demais, só "Afastado".
8. **Dado** o funcionário com login completo, **quando** abre "Meu ponto" → "Meu espelho" (`GET /api/v1/me/hr/timesheets`, ADR 0035, fora do `[PinSessionAllowed]`), **então** vê o próprio espelho do período em aberto e dos fechados, nunca o de outro (IDOR).

**Banco de horas**
9. **Dado** o banco ligado e o funcionário com acordo (CA 2), **quando** o período é calculado, **então** o saldo do período = trabalhado + abonado − esperado (em minutos), e o saldo acumulado = saldo de fechamento do período anterior + saldo do período; o espelho mostra "Banco de horas: +3 h 20 min no período, +5 h 10 min acumulado"; nenhum saldo vence sozinho (a decisão de pagar fica na folha).
10. **Dado** um abono do tipo "Folga compensada pelo banco de horas" (RH-01b), **quando** aprovado, **então** ele não conta como hora abonada e desconta do banco o tempo da folga (o dia esperado é compensado pelo saldo), com a marca "Compensado do banco"; sem banco ou sem acordo, extras e faltas aparecem como totais do período para a folha (HE a pagar e faltas a descontar), sem saldo acumulado.

**Fechamento**
11. **Dado** `hr.edit`, **quando** clico "Fechar o período {mês}" no espelho geral, **então** o diálogo lista as pré-condições por funcionário: pedidos de ajuste pendentes (bloqueia) e dias inconsistentes (bloqueia, com "Marcar como revisado" por dia, que exige comentário); com tudo resolvido, confirmar "Fechar período" grava, para cada funcionário ativo no período, o `timesheet` com os totais congelados (esperado, trabalhado, extras, faltas, atrasos, abonado, saldo do período e acumulado) e `status = Closed`, na mesma transação; a mensagem de sucesso diz "Período {mês} fechado" e o `NextSteps` oferece "Baixar espelhos em PDF", "Lançar a folha de {mês}" (RH-02a) e "Gerar pacote para a contadora" (RH-03).
12. **Dado** um período fechado, **quando** alguém tenta lançar, pedir ou decidir ajuste nele (RH-01b CA 16), **então** 409; o espelho mostra a faixa "Período fechado em {data} por {usuário}".
13. **Dado** `hr.approve`, **quando** clico "Reabrir período" e informo o motivo, **então** os timesheets do período voltam a `Open`, os períodos seguintes já fechados também voltam a `Open` (o saldo acumulado depende deles), os "Li e confirmo" desses períodos ficam sem efeito, os membros do Proprietário são notificados e a folha do mês, se já fechada (RH-02c), mostra o aviso "O ponto deste mês foi reaberto depois do fechamento da folha."
14. **[Segurança]** **Dado** dois fechamentos simultâneos do mesmo período, **quando** chegam, **então** só um grava (lock do período e `xmin`), o outro recebe 409 `concurrency_conflict`.
15. **Dado** um período fechado, **quando** o funcionário abre "Meu espelho" e clica "Li e confirmo", **então** a ciência fica registrada (data, hora, usuário e o resumo dos totais) e o RH vê na visão geral "Confirmado em {data}" ou "Aguardando o funcionário"; a ciência não bloqueia nada e não é assinatura legal.

**Ponto na folha e no pacote da contadora**
16. **Dado** a folha lançada de um mês com o período de ponto fechado, **quando** abro um holerite (RH-02a), **então** vejo ao lado os totais do espelho fechado (extras, faltas, atrasos, banco) e "Usar horas do espelho", que preenche só a **referência** das rubricas ligadas a um total do espelho (marca "Origem no ponto" na rubrica: Horas extras 50% ← extras; Faltas ← faltas; Atrasos ← atrasos, no seed), sem mexer no valor, que continua vindo da contadora.
17. **Dado** o pacote da contadora (RH-03), **quando** gero uma competência com o período de ponto fechado, **então** entra o `ponto.csv` (por funcionário: dias trabalhados, horas extras, faltas em dias e horas, atrasos, horas abonadas por motivo, sem motivo de saúde, e o banco de horas do período e acumulado), iguais aos do espelho fechado (teste com o dataset do CA 4); a versão do layout sobe para `v2` (RH-03 CA 7) e o `leia-me.txt` diz "Ponto: incluído (período fechado em {data})"; com o período em aberto, "Ponto: não incluído; feche o período de ponto para exportar."

**PDF do espelho**
18. **Dado** um período fechado, **quando** clico "Baixar espelho em PDF" (um funcionário) ou "Baixar espelhos em PDF" (todos, um arquivo por funcionário num ZIP, `TenantJob` com `tenantId`), **então** o PDF é gerado pelo `IPdfRenderer` com o logo do tenant, nome, matrícula, cargo, período, os dias, os totais, o banco de horas, o texto "Controle interno de jornada; não substitui o registro legal de ponto." e o campo de assinatura do funcionário, sem CPF, sem salário, sem motivo de saúde e sem a localização; é guardado como arquivo do sistema (`StoreGeneratedAsync`, dono `Timesheet`, leitura com `hr.view` ou pelo próprio funcionário) e o download fica auditado.

**Desempenho e permissões**
19. **Dado** 50 funcionários com 4 marcações por dia útil, **quando** abro o espelho geral do período, **então** a tela responde em menos de 1 s (teste de desempenho com o dataset).
20. **Dado** o módulo `hr` não liberado, **quando** chamo, **então** 403 `module_not_released`; espelho dos outros com `hr.view`, fechar e marcar revisado com `hr.edit`, reabrir com `hr.approve`, acordo de banco com `hr.edit` + `hr.view_sensitive`, parâmetros com `settings.edit`; sem a ação, 403 `forbidden` e o botão some. `RequireModule` e `perm:`; IDOR em `/employees/{id}/timesheets/{period}`; "Meu espelho" pelo autoatendimento (CA 8).
21. **Dado** fechamento, reabertura, "Marcar como revisado", "Li e confirmo", mudança de parâmetro e download, **quando** abro o histórico, **então** vejo quem, quando, o motivo e os totais antes/depois.

## Regras de negócio e técnicas
- **Cálculo** num serviço puro do Domain (`TimesheetCalculator`), com entrada: jornada, calendário (feriados e paradas), marcações válidas, abonos aprovados, afastamentos, acordo de banco, parâmetros; saída por dia e totais. Sem acesso a banco, para o teste de tabela.
- **Unidade:** minutos inteiros no cálculo; exibição "h min"; os totais congelados em `hr.timesheets` como minutos (`integer`).
- **Período:** pelo `hr.timesheet_period_start_day` (padrão 1, mês civil); `period` guarda o mês de referência (o do fim do período).
- **Calendário:** pelo `IBusinessCalendar` do `Production.Contracts` (criado na DSH-01a, com os métodos da RH-02a), sem acessar tabela de outro módulo.
- **Fechamento:** idempotente por `(employee_id, period)` único; reabertura não apaga, guarda o histórico.
- **Tabelas:** `hr.timesheets` (03) + `expected_minutes`, `worked_minutes`, `overtime_minutes`, `absence_minutes`, `late_minutes`, `justified_minutes`, `bank_month_minutes`, `bank_balance_minutes`, `closed_at`, `closed_by`, `reopened_at`, `reopened_by`, `reopen_reason`, `employee_ack_at`, `employee_ack_summary`; `hr.timesheet_day_reviews` (dia marcado como revisado, com comentário); `hr.employees` (+ `hour_bank_agreement_on`); `hr.payroll_items` (+ `timesheet_source` Overtime/Absence/Late).

## Permissões, eventos e tabelas
- **Permissões:** `hr.view` (espelho), `hr.edit` (fechar, marcar revisado), `hr.approve` (reabrir), `hr.view_sensitive` (motivo do afastamento, acordo de banco), `hr.view_health` (motivo de saúde), `settings.edit` (parâmetros); autoatendimento para "Meu espelho" e "Li e confirmo" (ADR 0035).
- **Eventos:** `TimesheetPeriodClosed` (`{ period }`) e `TimesheetPeriodReopened` (`{ period }`), em `HR.Contracts`, sem dado pessoal; consumidos dentro do HR pela folha (aviso da RH-02c; FLH-02b).
- **Contratos:** usa `IBusinessCalendar` (Production, DSH-01a e RH-02a); registra a seção `ponto.csv` no `IAccountantExport` (RH-03).
- **Tabelas:** `hr.timesheets` (extensão), `hr.timesheet_day_reviews` (nova), `hr.parameters` (+ tolerâncias, banco, início do período), `hr.employees` (+ acordo de banco), `hr.payroll_items` (+ origem no ponto).

## Telas e estados
- **Padrões comuns das Fases 4 a 8:** 3 (espelho como tabela analítica: coluna do dia fixa, navegação por célula, drill-down do total em painel lateral com o mesmo total), 8 ("Meu espelho" com o layout de autoatendimento; "Dados protegidos" para motivos).
- **RH → Ponto → Espelho** (`/app/rh/ponto/espelho`): visão geral do período (um funcionário por linha com trabalhado, extras, faltas, banco, confirmação do funcionário e chip de situação) e o espelho do funcionário (`/app/rh/ponto/espelho/$employeeId?mes=2026-10`), dias em tabela; faixa lateral âmbar no dia inconsistente.
- **"Meu ponto" → Meu espelho**, com "Li e confirmo".
- **Verbos:** "Fechar período" / "Período fechado"; "Reabrir período" / "Período reaberto"; "Marcar como revisado" / "Dia revisado"; "Li e confirmo" / "Confirmado"; "Usar horas do espelho"; "Baixar espelho em PDF".
- **Estados:** carregando; vazio ("Nenhuma marcação neste período. As marcações chegam pelo Meu ponto ou pelo lançamento manual."); erro; período fechado; pré-condições do fechamento; parâmetros "A confirmar com a contadora".
- **400 px:** dias em cartões, com o trabalhado e a situação sempre visíveis; totais no topo; botões de `ErrorState`/`EmptyState` com 44 px.
- **Propositivo:** o diálogo de fechamento leva direto a cada pendência; depois do fechamento, "Lançar a folha de {mês}" e "Gerar pacote para a contadora"; "Usar horas do espelho" no holerite.
- **Ajuda:** `Ajuda/rh-ponto-espelho.md`, `Ajuda/rh-ponto-espelho-funcionario.md`, atualizar `Ajuda/meu-ponto.md` ("Meu espelho" e "Li e confirmo"), `Ajuda/rh-folha-detalhe.md` ("Usar horas do espelho"), `Ajuda/rh-exportar-contadora.md` (`ponto.csv`, layout `v2`) e a seção RH de `Ajuda/configuracoes-parametros.md`; `<Dica>` em "Tolerância", "Banco de horas", "Acordo de banco de horas", "Período de apuração" e "Inconsistente".

## Plano de testes
- **Unidade (tabela de casos):** cálculo do dia e do período com o dataset fixo, mês civil e corte (CA 4); tolerância (CA 5); inconsistências (CA 6); afastamento e desligamento (CA 7); banco de horas com e sem acordo, folga compensada e banco desligado (CA 9, 10).
- **Integração (Testcontainers):** parâmetros com o chip e "Marcar como conferido", mudança só nos períodos em aberto (CA 1); acordo de banco (CA 2); espelho por permissão, motivo de saúde e IDOR (CA 3, 7, 8); fechamento com pré-condições, totais congelados e idempotência (CA 11); período fechado bloqueia ajuste (CA 12); reabertura em cascata, ciência sem efeito e aviso na folha (CA 13); concorrência (CA 14); "Li e confirmo" (CA 15); "Usar horas do espelho" só na referência (CA 16); `ponto.csv` igual ao espelho, layout `v2` e período em aberto (CA 17); PDF sem CPF, salário, motivo de saúde nem localização, pelo texto extraído (CA 18); desempenho com 50 funcionários (CA 19); três camadas (CA 20); histórico (CA 21).
- **E2E (Playwright):** fechar o período com uma pendência resolvida no caminho, o funcionário confirmar no celular; espelho a 400 px.

## Fora do escopo
- Horas extras com percentuais, adicional noturno e DSR em valor: FLH-02b (Fase 7). Aqui só as horas.
- Vencimento e pagamento automático do saldo do banco de horas: fora do MVP (o banco não vence sozinho).
- Assinatura eletrônica com validade legal do espelho e ponto como registro oficial (REP-P): story pós-MVP da Fase 9 (P-26).

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fases 4 a 8, tabela 3.3, pontos 10–14):
1. **Tolerância** de 5 min por marcação e 10 por dia, salvo a convenção: parâmetro pré-configurado e editável, "A confirmar com a contadora" (CA 1, 5).
2. **Banco de horas simples,** só para quem tem acordo, sem vencimento automático (CA 1, 2, 9, 10).
3. **Apuração pelo mês civil,** como parâmetro editável (corte por outro dia possível) (CA 1, 4).
4. **Fecha o período quem tem `hr.edit`** (CA 11).
5. **"Li e confirmo"** no Meu espelho; PDF com campo de assinatura (CA 15, 18).

Decisões da revisão aplicadas: §4.2 (`IBusinessCalendar` no `Production.Contracts`, criado na DSH-01a (Fase 4), completado na RH-02a e usado aqui); D15 (controle interno, faixa e texto no PDF, CA 3, 18; a folha vem antes e o ponto passa a alimentá-la, CA 16, 17); §4.4 padrões 3 e 8.

**Pré-condição externa:** a contadora confere a tolerância, as regras do banco de horas e o período de apuração (valores pré-configurados em Configurações → Parâmetros → RH com o chip "A confirmar com a contadora"); o jurídico confere o modelo do acordo de banco de horas; não bloqueia o `Ready`.

## Definition of Done
- [x] Critérios de aceite atendidos.
- [x] Testes de unidade (tabela de casos), integração (Testcontainers), desempenho e E2E passando (E2E com a API simulada, pelo CI).
- [x] Migration criada (não destrutiva): `TimesheetsRh01c`.
- [x] Permissões só do `ModuleCatalog` (nenhuma ação nova); `IBusinessCalendar` reusado do Production.
- [x] Telas com estados; ajuda escrita e atualizada; teste de cobertura passando.
- [x] Próximos passos aplicados.
- [x] Auditoria ligada.
- [x] OpenAPI e cliente Orval regenerados.
- [x] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [x] `TimesheetCalculator` (Domain, puro) e o dataset de referência (mês civil e corte).
- [x] Parâmetros no `HrParameters`; acordo de banco no funcionário; leitura dos afastamentos e da jornada no HR.
- [x] Fechamento, reabertura e "Li e confirmo"; `TimesheetPdfDocument`.
- [x] "Usar horas do espelho" no holerite; seção `ponto.csv` no `IAccountantExport`.
- [x] Endpoints `/api/v1/hr/timesheets` e `/api/v1/me/hr/timesheets`; telas e ajuda.

## Quality gates
- Agentes previstos: @dev, @architect (calendário e integração com a folha), @qa (tabela de casos).
- Gate: CA 4, 9, 11, 13, 17 e 18 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fases 4 a 8).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK (ajustado: R-RH-8 pelo `ponto.csv`) |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: depois do Marco 6.2) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** re-estimada para G (cálculo, banco, fechamento e PDF); tolerância e banco como parâmetros, sem regra legal fixa; calendário por contrato do Production, sem ler tabela alheia; reabertura em cascata por causa do saldo acumulado; PDF sem dado sensível.
- **Correções da revalidação (21/10/2026):** pontos 10 a 14 decididos: parâmetros pré-configurados "A confirmar com a contadora" em Configurações → Parâmetros → RH (CA 1), banco só com acordo e sem vencimento (CA 2, 9, 10), mês civil com corte possível (CA 1, 4), `hr.edit` fecha (CA 11), "Li e confirmo" (CA 15, novo); com a folha antes do ponto, entram "Usar horas do espelho" na folha (CA 16, novo) e o `ponto.csv` no pacote da contadora (CA 17, novo, tirado da RH-03); faixa de controle interno no espelho e no PDF (D15); motivo de saúde só com `hr.view_health`; `IBusinessCalendar` reusado da RH-02a; CAD-04c → CAD-04c1; ordem 8 do Marco 6.1; CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma de decisão de story. Externa: conferência da contadora (tolerância, banco, período) e do jurídico (acordo de banco).
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready (pré-condição externa)`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da RH-01) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (D15, tabela 3.3 pontos 10–14, ADR 0035, ADR 0042, §4.2, §4.4 padrões 3 e 8); nova ordem (Marco 6.1, ordem 8; horas do espelho na folha e `ponto.csv` no pacote); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
| 31/10/2026 | 1.0 | Implementação (Done): `hr.timesheets` e `hr.timesheet_day_reviews`, `hr.employees.hour_bank_agreement_on` e `hr.payroll_items.timesheet_source` (migration `TimesheetsRh01c`, não destrutiva); `TimesheetCalculator` puro no Domain; `TimesheetService` (espelho, visão geral, fechamento com lock do período, reabertura em cascata, dia revisado, "Li e confirmo", acordo de banco, horas para o holerite), `TimesheetPdfService` (PDF e ZIP, `TimesheetBatchJob`), a trava real `TimesheetClosings`; parâmetros do ponto em Configurações → Parâmetros → RH; layout v2 do pacote com o `ponto.csv`; rotas `/api/v1/hr/timesheets`, `/api/v1/hr/employees/{id}/timesheets`, `/api/v1/hr/employees/{id}/hour-bank-agreement`, `/api/v1/hr/payroll-runs/{id}/payslips/{payslipId}/timesheet` e `/api/v1/me/hr/timesheets`; telas RH → Ponto → Espelho, espelho do funcionário, "Meu espelho", "Usar horas do espelho", "Origem no ponto" na rubrica e o acordo na aba Ponto; ajudas, testes e E2E | Dev (agente) |

## Notas de implementação (31/10/2026)
- **Cálculo (CA 4 a 7):** minutos inteiros e a marcação no minuto (os segundos são desprezados). O feriado ou a parada é a exceção do calendário padrão com zero hora disponível (`IBusinessCalendar.ExceptionsAsync`); a exceção com horas (dia especial) segue a jornada. O dia de hoje em diante fica "Ainda não chegou", fora dos totais, e o período só fecha depois do último dia. Recomendação aceita (PO 24/10).
- **Tolerância (CA 5):** a diferença do dia (trabalhado + abonado − esperado) não conta quando fica dentro da tolerância do dia **e** a primeira entrada e a última saída ficam dentro da tolerância por marcação contra a jornada; fora disso, conta inteira. A jornada guarda só a duração do intervalo, então a tolerância por marcação vale para a entrada e a saída do dia. Recomendação aceita (PO 24/10).
- **Falta x atraso (CA 3 e 10):** dia sem marcação e sem abono é falta (as horas e o dia); dia com marcação e trabalhado a menos é atraso (inclui a saída antecipada). O dia inconsistente conta só os pares fechados e a diferença entra como atraso até alguém resolver; marcado como revisado, deixa de bloquear o fechamento. Recomendação aceita (PO 24/10).
- **Banco (CA 9 e 10):** com o banco ligado e a partir da data do acordo, a diferença do dia vai inteira para o banco (extras, faltas e atrasos desses dias ficam zerados, para a folha não pagar duas vezes), pela fórmula do CA 9 (trabalhado + abonado − esperado). O acumulado parte do saldo do último período fechado, por isso o período só fecha na ordem (o anterior fechado antes) e a reabertura reabre os seguintes. Recomendação aceita (PO 24/10).
- **Motivo do afastamento (CA 7):** o tipo do afastamento e o motivo do abono só saem com `hr.view_health`, pela regra já decidida para os afastamentos (PO 22/10/2026, CAD-04c1b 1.1: mostrar os outros tipos deixaria inferir o de saúde), e não com `hr.view_sensitive` como o CA 7 sugeria; para os demais, "Afastado" e "Ausência justificada". Recomendação mais conservadora aceita (PO 24/10).
- **Dias congelados:** o fechamento grava, além dos totais, os dias como estavam (`days_snapshot`, `[PersonalData]`), para o espelho fechado, o PDF e o drill-down baterem com os totais mesmo que a jornada ou os parâmetros mudem depois. A reabertura recalcula com os parâmetros de agora (CA 1).
- **Concorrência (CA 14):** `pg_advisory_xact_lock` por período na transação do fechamento e da reabertura; quem chega depois vê o período já fechado e recebe 409 `concurrency_conflict`; o `xmin` do espelho reaberto e o índice único (funcionário, período) seguram o resto.
- **Período fechado (CA 12):** a trava real (`TimesheetClosings`) substitui a da RH-01b; o dia num período fechado responde 409 `timesheet_period_closed` antes da recusa genérica "Data fora do período em aberto".
- **Acordo de banco (CA 2):** a data do acordo é `[PersonalData]` (lida só com `hr.view_sensitive`, mascarada na auditoria) e não pode cair num período já fechado.
- **PDF (CA 18):** até 20 espelhos o ZIP sai na hora; acima, pelo `TimesheetBatchJob` (só o `tenantId`, o período e quem pediu), com o aviso no sino, como os holerites (RH-02b). O ZIP é guardado com o dono `TimesheetBatch` e o id fixo do período. Recomendação aceita (PO 24/10).
- **Pacote para a contadora (CA 17):** o layout padrão sobe para `v2` (o `v1` mais o `ponto.csv`); o `v1` continua disponível. Quem estava no `v1` sem conferir passa para o `v2` no seed; o `v1` já conferido com a contadora fica como está. O motivo de saúde do abono sai como "Ausência justificada".
- **"Usar horas do espelho" (CA 16):** preenche a referência ("5:18 h") das rubricas com "Origem no ponto"; a linha que falta entra sem valor. A marca no seed vale uma vez para as rubricas já existentes (HE50, FALTAS, ATRASOS) e é editável na rubrica.
- **Pendência externa:** a contadora confere a tolerância, as regras do banco de horas e o período de apuração (chip "A confirmar com a contadora" em Configurações → Parâmetros → RH) e as colunas do `ponto.csv` (layout v2); o jurídico confere o modelo do acordo de banco de horas.
