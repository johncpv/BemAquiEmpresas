# RH-02c — Folha lançada: fechamento, títulos a pagar de salários e encargos e folha prevista no fluxo de caixa

| Campo | Valor |
|---|---|
| ID | RH-02c |
| Título | Fechar a folha da competência com 2FA de novo (pré-condições, totais congelados) e, na mesma transação, criar pelo `IPayables` um título a pagar por funcionário (salário líquido, favorecido `Employee`) e um por guia (órgão como terceiro), com conta e centro de custo pelo centro do cargo e vencimento no 5º dia útil; conta de salário lida do HR só no pagamento da OP, conta em quarentena fora do pagamento automático, quem fecha não paga; folha `Paid` quando tudo estiver pago, reabertura controlada e **folha prevista** de 3 meses, com 13º e férias, no fluxo de caixa (origem `PayrollForecast`) |
| Tamanho | G (re-estimada de M: favorecido funcionário no `finance.payables` e na OP da FIN-03a, leitura sensível da conta no pagamento, 2FA de novo e previsão por funcionário, ADR 0036) |
| Status | Done (pré-condição externa: conferência da contadora) |
| Requisitos | R-RH-5 (parte: gerar CP de salários) · R-FIN-4 (CP originado da folha) · R-FIN-10 (folha prevista no fluxo de caixa) · R-FIN-13 (despesa de pessoal no DRE) · R-PRP-2 · R-PRP-7 · R-SEC-5 |
| Dependências | RH-02a (folha, guias, parâmetros do RH, `IBusinessCalendar`), CAD-04c2 (conta de salário, quarentena e revisão), FIN-02 (`IPayables`, origem `Payroll`), FIN-03a (OP), FIN-05 (fluxo de caixa com títulos `Planned` estimados), CAD-05a (contas 2.2 e 3.1, centros de custo), CMP-03a (`IPayables` por parcela, ADR 0024) |
| Bloqueia | RH-02b (holerite só com folha fechada), RH-03, LGPD-01a, FLH-02a |
| Ordem de execução | 3 (Marco 6.2 "Folha lançada") |

## User story
Como **proprietário**, quero **que o fechamento da folha gere sozinho os pagamentos de salários e de encargos, e que o fluxo de caixa já mostre a folha dos próximos meses**, para **saber quanto a folha tira do caixa e pagar cada funcionário por ordem de pagamento aprovada, sem desviar salário para a conta errada**.

## Contexto e referências
- PRD R-RH-5 ("gerar CP de salários"); R-FIN-4 ("CP originados de… folha"); R-FIN-10 ("base = títulos em aberto + recorrentes + pedidos de compra + **folha prevista**"); objetivo central do produto: visibilidade financeira.
- `04-fluxos-estados` §1 "Título a pagar e OP" (`Planned` → `Open` → OP; **nenhum pagamento sem OP aprovada**); §3.5 (fluxo base com "folha prevista"). §2 `PayrollClosed` → "Finance cria payables" muda: o HR chama o `IPayables` na transação do fechamento (ADR 0036, padrão da ADR 0024 §1), e o evento fica só para aviso e projeção.
- `SharedKernel.DocumentOrigin.Payroll` (FIN-02); origem nova `PayrollForecast` (esta story).
- FIN-05 CA 11 ("folha e pedidos de compra entram pelos títulos `Planned` que as stories RH-02a/02c e CMP-03 criarem"); FIN-03a (retrato do envio, aprovar, pagar, estorno, `PaymentOrderPaid`).
- Plano de contas do seed (CAD-05a): 2.2 "Mão de obra direta" (Custos) e 3.1 "Pessoal administrativo" (Despesas).
- **Revisão pré-Fases 4 a 8** (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`): D6 (2FA de novo para fechar a folha e pagar), D14 (opção do arquiteto: `payee_type`/`payee_id`, contrato sensível do HR, guias com o órgão como terceiro, `PayrollForecast`, chamada síncrona, quem fecha não paga, conta em quarentena), tabela 3.3 pontos 19 e 23 a 27, §4.2 (riscos de migração: `payables.party_id` aceitando nulo é expansão, código N-1 tolerante), §4.3 (transversal, DTO por forma de permissão), §4.4 padrões 2 e 8.
- **ADR 0036** (`docs/adr/0036-folha-no-financeiro.md`); ADR 0024 §1 e §2 (chamada síncrona e `IPayables` por parcela); ADR 0008 (adendo de 21/10/2026, D6); ADR 0042 (parâmetros "a confirmar com a contadora"); ADR 0019 §1, §2, §4, §17 (consumidor idempotente, `xmin`, título de origem cancelada, CP estimado).

## Critérios de aceite
**Fechamento**
1. **Dado** `hr.approve` + `hr.view_sensitive` e uma folha `Draft`, **quando** clico "Fechar folha", **então** o diálogo (`size="lg"`, impacto em lista) mostra: as pré-condições que bloqueiam (holerites sem lançamento, líquido negativo, funcionário sem conta para pagamento, CAD-04c2, guia sem vencimento ou sem favorecido), cada uma com link para resolver; a **data de pagamento** da folha, preenchida pelo tipo (Mensal: 5º dia útil do mês seguinte; Adiantamento: dia 20 da competência; 13º 1ª parcela: 30/11; 13º 2ª parcela: 20/12; Férias e Rescisão: obrigatória, informada aqui; dia não útil antecipa, pelo `IBusinessCalendar`) e editável; o aviso **"Novos na folha"** (primeira folha do funcionário) e **"Conta alterada"** (conta em quarentena, que fica fora do pagamento automático, CA 9), com os nomes; e o impacto: "Gera {n} títulos a pagar de salários, total {valor}, e {m} títulos de guias, total {valor}, que seguem para ordem de pagamento."
2. **Dado** o diálogo sem pendência, **quando** confirmo "Fechar folha" com o código do 2FA de novo (TOTP recente, D6; sem ele, 403 `mfa_step_up_required`), **então** **na mesma transação**: a folha passa a `Closed` com os totais congelados (`closed_at`, `closed_by`); o HR chama o `IPayables` e cria, `Open`, **um título por holerite** com líquido > 0 (favorecido `Employee`, `payee_id` = funcionário, `party_id` nulo, forma de pagamento `EmployeeAccount`, descrição "Salário {tipo} {mês} — {matrícula}", vencimento = data de pagamento) e **um título por guia** (favorecido `Party` = o órgão da RH-02a CA 7, descrição "Guia {tipo} {mês}", vencimento da guia), com origem `Payroll` e `origin_id` = holerite ou guia; o `PayrollClosed` (`{ payrollRunId, period }`, sem valor nem dado pessoal) vai para o outbox só para aviso e projeção; se qualquer título falhar, nada é gravado; a mensagem de sucesso diz "Folha de {mês} fechada. {n} títulos a pagar gerados." (ADR 0021).
3. **[Segurança]** **Dado** dois fechamentos simultâneos da mesma folha ou o reenvio do comando, **quando** chegam, **então** só um grava (`xmin` e `Idempotency-Key`), os títulos não duplicam (índice único `(origin, origin_id, installment)`, ADR 0024) e um só `PayrollClosed` vai para o outbox; teste com dois tenants.
4. **Dado** os títulos da folha, **quando** criados, **então** a conta e o centro de custo seguem o centro do cargo (parâmetros do RH, pré-configurados "A confirmar com a contadora"): cargo com centro de trabalho padrão (CAD-04a) → conta 2.2 "Mão de obra direta" e centro de custo "Produção"; demais → 3.1 "Pessoal administrativo" e centro "Administrativo"; a guia, que é um título só, vai para a conta e o centro do grupo com a maior base do FGTS na competência (`<Dica>` "A guia vai inteira para o grupo com a maior base; a divisão exata fica com a contadora."); nenhum título de folha leva projeto (o custo de mão de obra do projeto vem do apontamento, PRD-04).
5. **Dado** a folha fechada, **quando** abro a aba "Pagamentos", **então** vejo cada título com número, favorecido, valor, vencimento e situação (pelo `IPayables`; a situação do pagamento só com `finance.view`, sem ela só "Título gerado"), e o `NextSteps` oferece "Criar ordem de pagamento dos salários" (abre a OP da FIN-03a com os títulos de salário pré-selecionados **menos os de conta em quarentena**, listados à parte em "Fora do pagamento automático: conta em quarentena até {data}" com o atalho "Revisar conta", R-PRP-2) e "Criar ordem de pagamento das guias".

**Favorecido funcionário e pagamento (mudanças no Finance feitas por esta story)**
6. **Dado** a migration desta story, **quando** roda sobre a cópia do banco do `konebras`, **então** `finance.payables` ganha `payee_type` (`Party` | `Employee`) e `payee_id`, preenchidos nos títulos existentes com `Party` e o `party_id`; `party_id` passa a aceitar nulo (expansão; o código N-1 continua funcionando, porque só a versão nova cria título `Employee`); o `CHECK` garante `(payee_type = 'Party' AND party_id IS NOT NULL AND payee_id = party_id) OR (payee_type = 'Employee' AND party_id IS NULL AND payee_id IS NOT NULL)`; os métodos do `IPayables` (FIN-02, CMP-03a) passam a receber o favorecido (`Payee(type, id)`), com as assinaturas de hoje como atalho para `Party`; o favorecido `Employee` é validado pelo `IEmployees`; teste de regressão dos títulos de origem que já existem (COM-06, FIN-02, CMP-03a, CMP-04).
7. **[Segurança]** **Dado** um título de salário (`payee_type = Employee`), **quando** aparece no Financeiro (lista de títulos, fluxo de caixa, OP), **então** quem só tem `finance.view` vê os salários agrupados numa linha "Folha {tipo} {mês}: {n} salários, total {valor}", sem nome nem valor por funcionário (DTO por forma de permissão, §4.3); quem tem `finance.approve` ou `finance.pay` vê cada título com "{matrícula} — {nome}" e o valor; ninguém no Financeiro vê CPF, salário-base, rubricas nem dados bancários fora do CA 10; o evento e os contratos não levam esses dados.
8. **[Segurança]** **Dado** uma OP com título de salário, **quando** é enviada (FIN-03a CA 4), **então** o retrato do envio (`payee_snapshot`) guarda só o favorecido (`Employee`, id, matrícula e nome), a **versão da conta** (`bank_account_version`, CAD-04c2) e o final da conta mascarado ("****-12"), lidos pelo `IEmployeeBankAccounts.SummaryAsync` (novo, `HR.Contracts`), nunca agência, conta, titular ou chave em claro; o diálogo de aprovação mostra o resumo mascarado e os avisos "Novo na folha" e "Conta revisada em {data}" quando cabem.
9. **[Segurança]** **Dado** um título de salário cuja conta está em quarentena (CAD-04c2 CA 7) e não foi revisada, **quando** alguém tenta incluí-lo numa OP, **então** 422 `payee_account_in_quarantine` "A conta de {matrícula} está em quarentena até {data}. Peça a revisão da conta no RH." e o título fica fora da pré-seleção do CA 5; revisada (CAD-04c2 CA 8) ou vencida a quarentena, o título volta a ser incluído normalmente.
10. **[Segurança]** **Dado** `finance.pay` e uma OP `Approved` com título de salário, **quando** abro "Pagar", **então** o diálogo lê os dados bancários do funcionário **só nesse momento**, pelo `IEmployeeBankAccounts.GetForPaymentAsync(employeeId, expectedVersion, paymentOrderId)` (novo, `HR.Contracts`), que registra `SensitiveRead` com o número da OP e responde com `no-store`; os dados aparecem para quem vai pagar e não são gravados no Finance; se a versão da conta for diferente da do retrato, o diálogo e a confirmação respondem 409 `payee_account_changed` "A conta de {matrícula} mudou depois do envio. Envie para nova aprovação." (o "Reenviar para aprovação" da FIN-03a CA 14 refaz o retrato); o pagamento exige o 2FA de novo (D6), como todo pagamento.
11. **[Segurança]** **Dado** o usuário que fechou a folha, **quando** tenta registrar o pagamento ("Pagar") de uma OP com título dessa folha, **então** 403 "Quem fechou a folha não registra o pagamento dela." (o Finance confere pelo `created_by` dos títulos de origem `Payroll`, gravado com o usuário do fechamento); a exceção do Proprietário único vale com a marca "autopagamento", como a D6 do Financeiro.

**Pago**
12. **Dado** todos os títulos da folha `Paid`, **quando** o Finance publica `PaymentOrderPaid` (FIN-03a), **então** o consumidor do HR (inbox, idempotente) reconcilia pela fonte (`IPayables.StatusByOriginAsync(Payroll, originIds)`, novo) e a folha passa a `Paid`; o estorno de pagamento publica `PaymentOrderReversed` (novo, acrescentado à FIN-03a por esta story) e a folha volta a `Closed`.

**Reabertura**
13. **Dado** `hr.approve` + `hr.view_sensitive`, uma folha `Closed` e nenhum título dela em OP não finalizada nem pago, **quando** clico "Reabrir folha" e informo o motivo, **então** na mesma transação a folha volta a `Draft`, os títulos `Open` dela são cancelados pela origem (`IPayables.CancelAllFromOriginAsync`, motivo "Folha reaberta: {motivo}"), a previsão é refeita (CA 15) e os membros do Proprietário e quem tem `finance.edit` são notificados; com título em OP ou pago, 409 "Há pagamentos desta folha em andamento. Estorne ou cancele a ordem de pagamento antes." com o link para a OP.

**Folha prevista no fluxo de caixa**
14. **Dado** a última folha Mensal fechada, **quando** ela fecha (ou quando muda o parâmetro de meses ou alguém com `hr.edit` clica "Recalcular previsão"), **então** na mesma transação o HR mantém a previsão dos próximos 3 meses (`hr.payroll_forecast_months` = 3) como títulos `Planned` com a marca de estimativa (`is_estimate`, ADR 0019 §17), origem **`PayrollForecast`** e `origin_id` = linha de `hr.payroll_forecasts`: um por funcionário ativo e mês (líquido da última folha, favorecido `Employee`) e um por guia e mês (valor da última guia, favorecido o órgão), vencimento pela regra do CA 1; mais o **13º** (1ª parcela em novembro e 2ª em dezembro, 50% do salário-base vigente cada, por funcionário, quando o mês está na janela e a folha de 13º do tipo ainda não fechou) e as **férias** (o líquido de cada holerite de folha de Férias em `Draft` na janela, no mês da competência); o fluxo de caixa (FIN-05) mostra com a `Tag` "Estimado", sem código novo no Finance.
15. **Dado** a folha real de um mês com previsão, **quando** ela fecha, **então** a previsão daquele mês e tipo é cancelada pela origem ("Substituída pela folha real") na mesma transação, e a dos meses seguintes é recalculada (`ReplacePlannedAsync`, ADR 0024 §2); teste: a previsão de novembro some quando a folha de novembro fecha, a do 13º some quando a folha do 13º fecha, sem o fluxo contar duas vezes.

**Segurança, permissões e histórico**
16. **Dado** o módulo `hr` ou `finance` não liberado, **quando** chamo, **então** 403 `module_not_released`; fechar e reabrir exigem `hr.approve` + `hr.view_sensitive`; "Recalcular previsão" exige `hr.edit`; parâmetros do RH, `settings.edit`; incluir, aprovar e pagar seguem a FIN-03a (`finance.create`, `finance.approve`, `finance.pay`); sem a ação, 403 `forbidden` e o botão some. `RequireModule` e `perm:`; o `IEmployeeBankAccounts` só atende chamada do Finance com `finance.pay` no request (teste).
17. **Dado** fechamento, reabertura, previsão e leitura da conta no pagamento, **quando** abro o histórico, **então** vejo quem, quando, o motivo e os totais; o título no Financeiro mostra a origem "Folha {mês}" com link para a folha (visível com `hr.view`); a auditoria não guarda dado bancário (canário).

## Regras de negócio e técnicas
- **Chamada síncrona (ADR 0036, padrão da ADR 0024 §1):** o fechamento e a reabertura chamam o `IPayables` na própria transação; `PayrollClosed` e `PayrollReopened` ficam só para aviso (notificação a quem tem `finance.pay`: "Folha de {mês} fechada: {n} títulos para pagar", sem valor) e projeção.
- **Favorecido:** salário com `payee_type = Employee`; guia com `payee_type = Party` (o órgão: Receita Federal, Caixa/FGTS, sindicato).
- **Origem:** `Payroll` (`origin_id` = holerite ou guia) e `PayrollForecast` (`origin_id` = previsão); índice único parcial `(origin, origin_id, installment)` (ADR 0024).
- **Conta de salário:** nunca copiada para o Finance; o retrato guarda só a versão e o final mascarado; os dados em claro só no "Pagar", pelo `IEmployeeBankAccounts`.
- **Previsão:** só no HR (quem conhece a folha); os títulos `Planned` estimados são cancelados e recriados pela origem, nunca editados à mão no Financeiro (FIN-02 trava título de origem).
- **Folha:** `Draft → Closed → Paid`; `Closed → Draft` pela reabertura; `Paid → Closed` pelo estorno.
- **Parâmetros do RH** (`hr.parameters`, Configurações → Parâmetros → RH, RH-02a CA 3), pré-configurados e editáveis com `settings.edit`, com o chip "A confirmar com a contadora" nos legais: `hr.salary_due_business_day` (5), `hr.advance_due_day` (20), `hr.thirteenth_first_due` (30/11), `hr.thirteenth_second_due` (20/12), `hr.payroll_forecast_months` (3), `hr.payroll_account_direct` (2.2) e `hr.payroll_cost_center_direct` (Produção), `hr.payroll_account_admin` (3.1) e `hr.payroll_cost_center_admin` (Administrativo).

## Mudanças no Finance feitas por esta story
- `finance.payables`: `payee_type`, `payee_id`, `CHECK`, `party_id` anulável (expansão, CA 6); `payment_method` ganha `EmployeeAccount`.
- `DocumentOrigin` (SharedKernel): valor novo `PayrollForecast`.
- `IPayables` (FIN-02/CMP-03a): favorecido `Payee` nos métodos de criação (`CreateOpenInstallmentsAsync`, `CreatePlannedInstallmentsAsync`, `ReplacePlannedAsync`) e `StatusByOriginAsync` (novo).
- Lista de títulos, fluxo de caixa e DRE: DTO por forma de permissão para os títulos `Employee` (CA 7).
- OP (FIN-03a): retrato do favorecido `Employee` (CA 8), recusa de conta em quarentena (CA 9), leitura da conta no "Pagar" com conferência da versão (CA 10), "quem fecha não paga" (CA 11), evento `PaymentOrderReversed` no estorno (CA 12).

## Permissões, eventos e tabelas
- **Permissões:** `hr.approve` + `hr.view_sensitive` (fechar, reabrir; o rótulo de `hr.approve` no `ModuleCatalog` passa a "ajustes de ponto, revisão da conta de salário e fechamento da folha", sem ação nova), `hr.edit` (recalcular previsão), `settings.edit` (parâmetros), `finance.view` (situação dos pagamentos), `finance.create`/`finance.approve`/`finance.pay` (OP, FIN-03a).
- **Eventos:** `PayrollClosed` (`{ payrollRunId, period }`) e `PayrollReopened` (`{ payrollRunId }`), em `HR.Contracts`, para aviso; consumidor no HR de `PaymentOrderPaid` (existente) e `PaymentOrderReversed` (novo, Finance).
- **Contratos:** `IEmployeeBankAccounts` (novo, `HR.Contracts`: `SummaryAsync`, `GetForPaymentAsync`), `IEmployees` (`BankAccountStatus`, CAD-04c2), `IPayables` (Finance, estendido), `IBusinessCalendar` (Production, RH-02a).
- **Tabelas:** `hr.payroll_runs` (+ `payment_date`, `closed_at`, `closed_by`, `paid_at`, `reopen_reason`); `hr.payroll_forecasts` (`period`, `kind` Salary/Charge/Thirteenth1/Thirteenth2/Vacation, `employee_id` ou `charge_kind`, `amount`, `due_date`, `source_run_id`); `finance.payables` (+ `payee_type`, `payee_id`, `CHECK`, `party_id` anulável).

## Telas e estados
- **Padrões comuns das Fases 4 a 8:** 2 (o total da aba Pagamentos clicável até os títulos de origem), 8 (bloco "Dados protegidos" padrão e dados bancários só no "Pagar").
- Na folha (RH-02a): "Fechar folha" e "Reabrir folha", aba "Pagamentos"; faixa de modo leitura "Folha fechada em {data}. Para mudar, reabra a folha."; chips Rascunho (info), Fechada (ok), Paga (ok), com o mapa da Fase 2.
- No Financeiro (FIN-02/03a): linha agrupada "Folha {mês}" para `finance.view`; favorecido "Funcionário {matrícula} — {nome}" para quem aprova ou paga; no "Pagar", o bloco "Para onde vai o dinheiro" com a conta lida na hora.
- **Estados:** carregando; pré-condições do fechamento com links; erro do `IPayables` dentro do diálogo (nada foi fechado); conta em quarentena fora da pré-seleção; conta mudou depois do envio (CA 10).
- **Propositivo:** data de pagamento sugerida pelo tipo; "Criar ordem de pagamento dos salários" e "das guias"; "Revisar conta" para a conta em quarentena; pré-condições com link para cada pendência. Efeito financeiro sempre com confirmação e 2FA.
- **400 px:** pagamentos em cartões com número, valor e situação; diálogo de fechamento em tela cheia; botões de `ErrorState`/`EmptyState` com 44 px.
- **Ajuda:** atualizar `Ajuda/rh-folha-detalhe.md` (aba `pagamentos`, nova, com "Novos na folha", "Conta alterada" e "Quem fecha não paga"), `Ajuda/financeiro-fluxo-de-caixa.md` ("Folha prevista"), `Ajuda/financeiro-ordens-de-pagamento.md` ("Salários: conta lida no pagamento") e a seção RH de `Ajuda/configuracoes-parametros.md`; `<Dica>` em "Folha prevista", "Data de pagamento" e na guia (CA 4).

## Plano de testes
- **Unidade:** data de pagamento por tipo com dia não útil (CA 1); regra de conta e centro, guia pelo grupo da maior base (CA 4); previsão dos 3 meses com 13º e férias e substituição (CA 14, 15).
- **Integração (Testcontainers, obrigatória: move financeiro):** pré-condições e avisos (CA 1); fechamento com 2FA, títulos `Employee` e `Party` na mesma transação, rollback se um título falhar (CA 2); concorrência, idempotência e dois tenants (CA 3); conta e centro (CA 4); pré-seleção sem quarentena (CA 5); migration sobre cópia do `konebras`, `CHECK`, N-1 e regressão dos títulos existentes (CA 6); DTO por permissão (CA 7); retrato sem dado bancário (CA 8); quarentena na OP (CA 9); leitura no "Pagar" com `SensitiveRead`, versão diferente 409 e 2FA (CA 10); quem fecha não paga e Proprietário único (CA 11); folha paga e estorno pela fonte (CA 12); reabertura com e sem OP (CA 13); previsão no fluxo de caixa sem contar duas vezes (CA 14, 15); três camadas e `IEmployeeBankAccounts` sem `finance.pay` (CA 16); histórico e canário (CA 17).
- **E2E (Playwright):** fechar a folha com 2FA, abrir a OP dos salários com os títulos pré-selecionados (um em quarentena fora), aprovar e pagar com outro usuário vendo a conta só no "Pagar".

## Fora do escopo
- Guias oficiais (DCTFWeb, FGTS Digital, DARF): a contadora emite; o título leva o valor e o vencimento informados.
- Pagamento de salários em lote pela API do banco: depois do MVP (pergunta ao Inter, item 6 da revisão).
- Rateio da folha por projeto: o custo do projeto vem do apontamento (PRD-04). Comparar folha real com custo apontado: pós-MVP.
- Rateio da guia entre custo e despesa: a contadora faz no fechamento contábil (CA 4).

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fases 4 a 8, D14 pela opção do arquiteto, D6, tabela 3.3, pontos 19 e 23–27, ADR 0036):
1. **Favorecido:** um título por funcionário com `payee_type = Employee`/`payee_id` e um por guia com o órgão como terceiro; `party_id` anulável com `CHECK` (expansão, N-1 tolerante); dados bancários lidos do HR só no pagamento da OP (`SensitiveRead`); fechamento síncrono pelo `IPayables` (CA 2, 6, 8, 10).
2. **Conta e centro de custo:** pelo centro do cargo, como parâmetro pré-configurado "a confirmar com a contadora" (CA 4).
3. **Quem fecha:** `hr.approve`, que não paga (`finance.pay`), com 2FA de novo para fechar (CA 1, 2, 11).
4. **Vencimento dos salários:** 5º dia útil do mês seguinte, como parâmetro (CA 1).
5. **Folha prevista:** 3 meses, com 13º e férias, origem `PayrollForecast` (CA 14, 15).
6. **Aviso "novos na folha / conta alterada"** no fechamento, com a conta em quarentena fora do pagamento automático (CA 1, 5, 9).

Decisões da revisão aplicadas: §4.2 (riscos de migração, CA 6); §4.3 (DTO por forma de permissão, CA 7); §4.4 padrões 2 e 8.

**Pré-condição externa:** a contadora confere a conta e o centro de custo da folha e das guias e os vencimentos padrão (valores pré-configurados em Configurações → Parâmetros → RH com o chip "A confirmar com a contadora"); não bloqueia o `Ready`.

## Definition of Done
- [x] Critérios de aceite atendidos.
- [x] Testes de unidade, integração (Testcontainers, cadeia até o título e o pagamento) e E2E passando.
- [ ] Migration criada (não destrutiva: expansão do `payables`), testada sobre cópia do banco do tenant `konebras`. (criada e aplicada nos bancos do Testcontainers; a cópia do `konebras` fica para a homologação, fora do alcance do agente)
- [x] Permissões só do `ModuleCatalog`.
- [x] Telas com estados, inclusive pré-condições, quarentena e conta mudada.
- [x] Ajuda atualizada (RH, Financeiro e Configurações); teste de cobertura passando.
- [x] Confirmação com impacto financeiro, 2FA de novo e próximos passos aplicados.
- [x] Auditoria ligada, com `SensitiveRead` na leitura da conta.
- [x] OpenAPI e cliente Orval regenerados.
- [x] `docs/03-modelo-dados.md` (`payables`, `payroll_runs`, `payroll_forecasts`) e `docs/04-fluxos-estados.md` (máquina da folha; `PayrollClosed` só aviso; chamada síncrona) atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [x] Transições da `PayrollRun`; serviço de fechamento chamando o `IPayables` na transação.
- [x] Migration do `payables` (`payee_type`, `payee_id`, `CHECK`, `party_id` anulável) e `Payee` no `IPayables`.
- [x] `IEmployeeBankAccounts` no `HR.Contracts`; retrato, quarentena, leitura no "Pagar" e "quem fecha não paga" na OP.
- [x] Previsão da folha (`PayrollForecast`); consumidores de `PaymentOrderPaid`/`PaymentOrderReversed`.
- [x] Endpoints de fechar, reabrir e recalcular; aba Pagamentos e ajuda.

## Quality gates
- Agentes previstos: @dev, @architect (favorecido no título, chamada síncrona, migração N-1), cybersecurity (dado do funcionário no Financeiro, conta no pagamento, segregação), @qa.
- Gate: CA 2, 3, 6, 7, 9, 10, 11 e 15 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fases 4 a 8).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK (ajustado: CAs 7 a 11) |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: depois da CAD-04c2 e da RH-02a) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** o evento não leva valor nem dado pessoal; a previsão é substituída pela folha real para não contar duas vezes; reabertura recusada com pagamento em andamento; o conflito com o `partyId` obrigatório do `IPayables` virou pré-condição.
- **Correções da revalidação (21/10/2026):** D14 pela opção do arquiteto: fechamento síncrono pelo `IPayables` com 2FA de novo (CA 2), um título por funcionário (`Employee`) e um por guia (órgão), migration de expansão do `payables` com `CHECK` (CA 6), DTO por permissão no Financeiro (CA 7), retrato sem dado bancário (CA 8), conta em quarentena fora da OP (CA 9), conta lida só no "Pagar" com conferência da versão (CA 10), quem fecha não paga (CA 11), folha paga pela fonte (CA 12); pontos 19 e 23 a 27 decididos (conta e centro, vencimento no 5º dia útil, previsão de 3 meses com 13º e férias pela origem `PayrollForecast`, avisos no fechamento); seção "Mudanças no Finance feitas por esta story"; re-estimada para G; ordem 3 do Marco 6.2; CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma de decisão de story. Externa: conferência da contadora (conta, centro e vencimentos).
- **Conferência do fechamento (21/10/2026, amostragem do PO):** IBusinessCalendar vindo da DSH-01a; favorecido pela ADR 0036; checklist refeita, sem mudança de resultado.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready (pré-condição externa)`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da RH-02) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (D6, D14 pela opção do arquiteto, tabela 3.3 pontos 19 e 23–27, ADR 0036, §4.2, §4.3, §4.4 padrões 2 e 8); nova ordem (Marco 6.2, ordem 3); re-estimada para G; CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
| 27/10/2026 | 1.0 | Implementada (@dev). Backend: `PayrollClosingService` (prévia `/close/sensitive` com pendências, data de pagamento pelo tipo, "Novos na folha" e "Conta alterada"; fechar com `hr.approve` + `hr.view_sensitive` e 2FA de novo, `xmin` e `Idempotency-Key`; um título por holerite com `Payee Employee` e um por guia no órgão, na mesma transação pelo `IPayables`; reabrir com motivo, cancelando os títulos pela origem; aba Pagamentos `/payments/sensitive`; folha `Paid` pela fonte nos consumidores de `PaymentOrderPaid`/`PaymentOrderReversed`), `PayrollForecastService` (folha prevista de 3 meses com 13º e férias, origem nova `PayrollForecast`, "Recalcular previsão" com `hr.edit`), `PayrollCostTargets` (conta e centro pelo centro do cargo, parâmetros "A confirmar com a contadora"), `IEmployeeBankAccounts` no `HR.Contracts` (resumo mascarado e conta só no "Pagar" com `finance.pay` e `SensitiveRead` com o número da OP). Finance: `payables` com `payee_type`/`payee_id`, `CHECK` e `party_id` anulável; OP recusa conta em quarentena (422), confere a versão da conta no "Pagar" (409 `payee_account_changed`), quem fecha a folha não paga (403 `payroll_closer_cannot_pay`), estorno publica `PaymentOrderReversed`; salário agrupado "Folha {mês}: N salários" para quem só tem `finance.view`; rota `GET /finance/payment-orders/{id}/payee-accounts` (no-store). Migration `PayrollClosing` (só expansão: colunas, `payroll_forecasts`, índice, `CHECK` e preenchimento de `payee_type = Party`). Front: "Fechar folha" e "Reabrir folha" (`ClosingDialogs`), aba Pagamentos com os próximos passos "Criar ordem de pagamento dos salários/das guias" e a quarentena à parte, bloco "Para onde vai o dinheiro" no "Pagar", favorecido funcionário no Pagar, parâmetros do RH (conta, centro e vencimentos), ajudas `rh-folha-detalhe-pagamentos`, `rh-folha-detalhe`, `financeiro-fluxo-de-caixa`, `financeiro-ordens-de-pagamento` e `configuracoes-parametros`. Desvios e decisões: (1) "Novo na folha" no retrato da OP = sem holerite em **outra** folha fechada ou paga (a folha do próprio salário já conta uma vez); (2) a situação na aba Pagamentos vem nula sem `finance.view`, e o gerador do OpenAPI marca o enum `PayableStatus` como anulável (padrão dos enums anuláveis do projeto): as telas de Compras que indexam pela situação ganharam `?? ''`; (3) o bloco "Para onde vai o dinheiro" lê a conta sem `AbortSignal` e sem refazer a busca, para gravar um `SensitiveRead` por abertura do "Pagar"; (4) a migration foi aplicada nos bancos do Testcontainers; a conferência sobre a cópia do `konebras` fica para a homologação. | @dev |
