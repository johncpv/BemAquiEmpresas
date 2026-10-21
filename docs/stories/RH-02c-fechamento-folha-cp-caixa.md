# RH-02c — Folha lançada: fechamento, títulos a pagar de salários e encargos e folha prevista no fluxo de caixa

| Campo | Valor |
|---|---|
| ID | RH-02c |
| Título | Fechar a folha da competência (pré-condições, totais congelados), publicar `PayrollClosed`, o Finance criar os títulos a pagar dos salários líquidos e dos encargos da empresa (origem `Payroll`, conta e centro de custo propostos), a folha passar a `Paid` quando tudo estiver pago, reabertura controlada e a **folha prevista** dos próximos meses como estimativa no fluxo de caixa |
| Tamanho | M (quebra da RH-02) |
| Status | Draft |
| Requisitos | R-RH-5 (parte: gerar CP de salários) · R-FIN-4 (CP originado da folha) · R-FIN-10 (folha prevista no fluxo de caixa) · R-FIN-13 (despesa de pessoal no DRE) · R-PRP-2 · R-PRP-7 · R-SEC-5 |
| Dependências | RH-02a (folha e encargos), FIN-02 (`IPayables`, origem `Payroll`), FIN-03a (OP), FIN-05 (fluxo de caixa com títulos `Planned` estimados), CAD-05a (contas 2.2 e 3.1, centros de custo), CAD-04c (forma de pagamento do funcionário) |
| Bloqueia | RH-02b (envio só com folha fechada), RH-03, FLH-02a |
| Ordem de execução | 5 (Marco 6.2 "Folha lançada") |

## User story
Como **proprietário**, quero **que o fechamento da folha gere sozinho os pagamentos de salários e de encargos, e que o fluxo de caixa já mostre a folha dos próximos meses**, para **saber quanto a folha tira do caixa e pagar cada funcionário por ordem de pagamento aprovada**.

## Contexto e referências
- PRD R-RH-5 ("gerar CP de salários"); R-FIN-4 ("CP originados de… folha"); R-FIN-10 ("base = títulos em aberto + recorrentes + pedidos de compra + **folha prevista**"); objetivo central do produto: visibilidade financeira.
- `04-fluxos-estados` §2: `PayrollClosed` (HR) → "Finance cria payables de salários e encargos"; §1 "Título a pagar e OP" (`Planned` → `Open` → OP; **nenhum pagamento sem OP aprovada**); §3.5 (fluxo base com "folha prevista").
- `SharedKernel.DocumentOrigin.Payroll` (FIN-02); `IPayables.CreateFromOriginAsync` exige `partyId` (código atual): o título do salário precisa de um favorecido, ponto em aberto 1.
- FIN-05 CA 11 ("folha e pedidos de compra entram pelos títulos `Planned` que as stories RH-02 e CMP-03 criarem").
- Plano de contas do seed (CAD-05a): 2.2 "Mão de obra direta" (Custos) e 3.1 "Pessoal administrativo" (Despesas).
- ADR 0019 §1, §2, §4, §17 (consumidor idempotente, `xmin`, título de origem cancelada, CP estimado).

## Critérios de aceite
**Fechamento**
1. **Dado** `hr.approve` (proposta; Pontos em aberto 3) e uma folha `Draft`, **quando** clico "Fechar folha", **então** o diálogo (`size="lg"`) lista as pré-condições: holerites sem lançamento, líquido negativo, funcionário sem forma de pagamento (CAD-04c, para os salários) e encargos sem vencimento; e o impacto: "Gera {n} títulos a pagar de salários, total {valor}, e {m} títulos de encargos, total {valor}, que seguem para ordem de pagamento."; confirmar "Fechar folha" passa a folha a `Closed` com os totais congelados e publica `PayrollClosed` no outbox, na mesma transação; a mensagem de sucesso diz "Folha de {mês} fechada. Gerando os títulos a pagar…" (ADR 0021).
2. **Dado** o `PayrollClosed` (`{ payrollRunId, period }`, sem valor nem dado pessoal), **quando** o consumidor do Finance processa, **então** ele lê os valores pelo contrato do HR (proposta: `IPayrollRuns.PayablesOfAsync(runId)`, que devolve, por título, origem, favorecido, valor, vencimento, conta, centro de custo e descrição) e cria, idempotente por `(Payroll, origin_id)`: um título por holerite com o líquido (descrição "Salário {mês} — {matrícula}", vencimento pelo parâmetro do ponto em aberto 4) e um título por encargo da empresa (RH-02a CA 6), todos `Open` e com natureza de custo conforme a regra do ponto em aberto 2.
3. **Dado** o consumidor rodando duas vezes ou o evento reentregue, **quando** processa, **então** nenhum título é duplicado (inbox + `(origin, origin_id)` único); teste com dois tenants.
4. **Dado** a folha fechada, **quando** abro a aba "Pagamentos", **então** vejo cada título com número, valor, vencimento e situação (pelo `IPayables`, só com `finance.view` para ver a situação do pagamento; sem ela, só "Título gerado"), e o `NextSteps` oferece "Criar ordem de pagamento dos salários" (abre a OP da FIN-03a com os títulos da folha pré-selecionados, R-PRP-2).

**Pago**
5. **Dado** todos os títulos da folha `Paid`, **quando** o último é pago, **então** o HR recebe a reação (proposta: o Finance publica `PayrollPayablesSettled` ou o HR consulta por `IPayables` ao abrir) e a folha passa a `Paid`; estorno de pagamento (FIN-03a) a devolve a `Closed`.

**Reabertura**
6. **Dado** `hr.approve`, uma folha `Closed` e nenhum título dela em OP não finalizada nem pago, **quando** clico "Reabrir folha" e informo o motivo, **então** a folha volta a `Draft`, os títulos `Open` dela são cancelados pela origem (`IPayables.CancelFromOriginAsync`, motivo "Folha reaberta: {motivo}"), os membros do Proprietário e quem tem `finance.edit` são notificados; com título em OP ou pago, 409 "Há pagamentos desta folha em andamento. Estorne ou cancele a ordem de pagamento antes." com o link para a OP.

**Folha prevista no fluxo de caixa**
7. **Dado** a última folha Mensal fechada, **quando** ela fecha (ou quando muda o parâmetro de meses), **então** o HR mantém a previsão dos próximos N meses (proposta: 3, parâmetro `hr.payroll_forecast_months`) como títulos `Planned` com a marca de estimativa (`is_estimate`, ADR 0019 §17), um por mês e por grupo (salários e encargos), pelo total da última folha, vencimento estimado, origem `Payroll` e `origin_id` = previsão do mês; o fluxo de caixa (FIN-05) mostra com a `Tag` "Estimado" sem código novo no Finance.
8. **Dado** a folha real de um mês com previsão, **quando** ela fecha, **então** a previsão daquele mês é cancelada pela origem ("Substituída pela folha real") na mesma reação, e a do mês seguinte em diante é recalculada pelo novo total; teste: previsão de novembro some quando a folha de novembro fecha, sem o fluxo contar duas vezes.

**Custo e DRE**
9. **Dado** os títulos da folha, **quando** criados, **então** a conta e o centro de custo seguem a regra decidida no ponto em aberto 2 (proposta: funcionário cujo cargo tem centro de trabalho padrão → conta 2.2 "Mão de obra direta" e centro "Produção"; demais → 3.1 "Pessoal administrativo" e centro "Administrativo"; encargos rateados na mesma proporção), para a DRE (FIN-08) separar custo de despesa; nenhum título de folha leva projeto (o custo de mão de obra do projeto vem do apontamento, PRD-04).

**Segurança e permissões**
10. **[Segurança]** **Dado** o título de salário, **quando** aparece no Financeiro e na OP, **então** mostra só a descrição "Salário {mês} — {matrícula}", valor e vencimento; o favorecido e os dados bancários seguem a decisão do ponto em aberto 1, e quem só tem `finance.view` nunca vê dado do HR além disso; o evento e o contrato não levam salário-base, rubricas nem CPF.
11. **[Segurança]** **Dado** dois fechamentos simultâneos, **quando** chegam, **então** só um grava (`xmin`) e um só `PayrollClosed` vai para o outbox.
12. **Dado** o módulo `hr` ou `finance` não liberado, **quando** chamo, **então** 403 `module_not_released`; fechar e reabrir exigem `hr.approve` (proposta) e `hr.view_sensitive`; sem a ação, 403 `forbidden`. `RequireModule` e `perm:`.
13. **Dado** fechamento, reabertura e previsão, **quando** abro o histórico, **então** vejo quem, quando, o motivo e os totais; o título no Financeiro mostra a origem "Folha {mês}" com link para a folha (visível com `hr.view`).

## Regras de negócio e técnicas
- **Reação entre módulos pelo outbox** (`04-fluxos` §2): o HR publica `PayrollClosed`; o Finance chama o contrato do HR para ler os valores (o evento não leva dado pessoal nem valor).
- **Origem:** `DocumentOrigin.Payroll`; `origin_id` = id do holerite, do encargo ou da previsão do mês; `(origin, origin_id)` único (FIN-02).
- **Previsão:** só no HR (quem conhece a folha); os títulos `Planned` estimados são cancelados e recriados pela origem, nunca editados à mão no Financeiro (FIN-02 trava título de origem).
- **Folha:** `Draft → Closed → Paid`; `Closed → Draft` pela reabertura; `Paid → Closed` pelo estorno.
- Parâmetros do HR em `hr.parameters`: `hr.payroll_forecast_months`, `hr.salary_due_day` (ponto em aberto 4).

## Permissões, eventos e tabelas
- **Permissões:** `hr.approve` + `hr.view_sensitive` (fechar, reabrir; proposta), `finance.view` (situação dos pagamentos).
- **Eventos:** `PayrollClosed` (`{ payrollRunId, period }`, já previsto no `04-fluxos`), `PayrollReopened` (`{ payrollRunId }`), `PayrollForecastChanged` (`{ period }`); consumidores no Finance. Proposta: `PayrollPayablesSettled` do Finance.
- **Contratos:** `IPayrollRuns` (HR.Contracts, proposta), `IPayables` (Finance).
- **Tabelas:** `hr.payroll_runs` (+ `closed_at`, `closed_by`, `paid_at`, `reopen_reason`); `hr.payroll_forecasts` (`period`, `kind`, `amount`, `due_date`, `source_run_id`).

## Telas e estados
- Na folha (RH-02a): "Fechar folha" e "Reabrir folha", aba "Pagamentos"; faixa de modo leitura "Folha fechada em {data}. Para mudar, reabra a folha."; chips Rascunho (info), Fechada (ok), Paga (ok), com o mapa da Fase 2.
- **Estados:** gerando os títulos (assíncrono, com o estado de demora depois de cerca de 30 s); erro do consumidor com "Reprocessar" para `finance.edit` (ADR 0019 §5).
- **Propositivo:** "Criar ordem de pagamento dos salários"; pré-condições com link para cada pendência.
- **400 px:** pagamentos em cartões com número, valor e situação.
- **Ajuda:** atualizar `Ajuda/rh-folha-detalhe.md` (aba `pagamentos`, nova) e `Ajuda/financeiro-fluxo-de-caixa.md` ("Folha prevista"); `<Dica>` em "Folha prevista".

## Plano de testes
- **Unidade:** regra de conta e centro (CA 9); previsão dos N meses e substituição (CA 7, 8).
- **Integração (Testcontainers, obrigatória: move financeiro):** fechamento com pré-condições e evento (CA 1); títulos criados pelo contrato, idempotência e dois tenants (CA 2, 3); aba Pagamentos por permissão (CA 4); folha paga e estorno (CA 5); reabertura com e sem OP (CA 6); previsão no fluxo de caixa sem contar duas vezes (CA 7, 8); dados expostos no título (CA 10); concorrência (CA 11); três camadas (CA 12); histórico (CA 13).
- **E2E (Playwright):** fechar a folha e abrir a OP dos salários com os títulos pré-selecionados.

## Fora do escopo
- Guias oficiais (DCTFWeb, FGTS Digital, DARF): a contadora emite; o título leva o valor e o vencimento informados.
- Rateio da folha por projeto: o custo do projeto vem do apontamento (PRD-04). Comparar folha real com custo apontado: pós-MVP.

## Pontos em aberto
1. **Favorecido do título de salário** (pré-condição, PO + arquiteto, ADR): o `IPayables` exige um terceiro (`partyId`). Opções: (a) o título ganha um favorecido "funcionário" (`employee_id`, validado pelo `IEmployees`) e a forma de pagamento vem do HR; (b) um título único "Folha {mês}" por competência, pago em lote pelo banco, com o detalhe por funcionário só no HR; (c) cadastrar o funcionário como terceiro (não recomendado: duplica dado pessoal). Inclui quem vê os dados bancários do funcionário na OP (hoje só `hr.view_sensitive`).
2. **Conta e centro de custo da folha** (contadora + PO): a regra proposta (direta × administrativa pelo centro do cargo) ou uma conta por rubrica.
3. **Quem fecha a folha** (PO): `hr.approve` (proposta, porque gera efeito financeiro) ou `hr.edit`; muda o significado de "aprovar" do RH no `ModuleCatalog` para "ajustes de ponto e fechamento da folha".
4. **Vencimento dos salários e dos encargos** (contadora): dia padrão (proposta: 5º dia útil do mês seguinte para salários; o vencimento de cada encargo informado na RH-02a).
5. Meses da folha prevista (3, proposta) e se a previsão inclui 13º e férias programadas.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers, cadeia até o título) e E2E passando.
- [ ] Migration criada (não destrutiva); ADR do ponto em aberto 1.
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Telas com estados, inclusive o assíncrono.
- [ ] Ajuda atualizada; teste de cobertura passando.
- [ ] Confirmação com impacto financeiro e próximos passos aplicados.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` e `docs/04-fluxos-estados.md` (máquina da folha) atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Transições da `PayrollRun`; `IPayrollRuns` no HR.Contracts.
- [ ] Consumidor do Finance para `PayrollClosed`, `PayrollReopened` e `PayrollForecastChanged`.
- [ ] Previsão da folha; endpoints de fechar e reabrir; aba Pagamentos e ajuda.

## Quality gates
- Agentes previstos: @dev, @architect (favorecido do título, contrato entre HR e Finance), cybersecurity (dado do funcionário no Financeiro), @qa.
- Gate: CA 2, 3, 6, 8 e 10 obrigatórios.

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

- **Correções feitas na validação:** o evento não leva valor nem dado pessoal; o Finance lê pelo contrato do HR (CA 2); a previsão é substituída pela folha real para não contar duas vezes (CA 8); reabertura recusada com pagamento em andamento (CA 6); o conflito com o `partyId` obrigatório do `IPayables` virou pré-condição (ponto 1), sem decidir.
- **Pré-condições para `Ready`:** pontos em aberto 1, 2 e 3; RH-02a concluída.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da RH-02) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
