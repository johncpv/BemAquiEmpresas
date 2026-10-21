# FLH-03 — Folha calculada: férias (períodos aquisitivos, programação, cálculo e recibo)

| Campo | Valor |
|---|---|
| ID | FLH-03 |
| Título | Períodos aquisitivos por funcionário com saldo de dias (descontadas as faltas pela regra da contadora), programação das férias com aviso, fracionamento e abono pecuniário, cálculo da folha de férias (remuneração, 1/3, médias de variáveis, INSS e IRRF próprios), recibo e aviso em PDF, título a pagar com o vencimento antes do início, dias de férias no espelho e no planejamento |
| Tamanho | G |
| Status | Draft |
| Requisitos | R-RH-6 ("férias") · R-RH-4 (dias de férias no ponto) · R-RH-5 (recibo e CP) · R-FIN-10 · R-PRP-5 · R-SEC-5 |
| Dependências | FLH-02a e FLH-02b (motor e médias), FLH-01 (tabelas), RH-01c (espelho), RH-02b/02c (PDF, fechamento e títulos), CAD-04b/04c (admissão, afastamentos) |
| Bloqueia | FLH-05 (férias vencidas e proporcionais na rescisão) |
| Ordem de execução | 4 (Marco 7.2 "Férias, 13º e rescisão") |

## User story
Como **RH**, quero **saber quem tem férias a vencer, programar as férias de cada um e gerar o cálculo e o recibo**, para **não pagar férias em dobro por atraso e ter o valor no caixa antes do pagamento**.

## Contexto e referências
- PRD R-RH-6; `03-modelo-dados` → `payroll_runs.type = Vacation`.
- CLT (férias: período aquisitivo, concessivo, proporção pelas faltas, 1/3, abono, fracionamento, prazo de pagamento): as regras e os prazos entram como **parâmetros e regras confirmadas pela contadora** (Pontos em aberto 1), cobertas por casos de teste.
- CAD-04c: afastamentos (alguns afastamentos interferem no período aquisitivo, regra da contadora).

## Critérios de aceite
**Períodos aquisitivos**
1. **Dado** um funcionário, **quando** abro a aba "Férias", **então** vejo os períodos aquisitivos desde a admissão (início, fim, dias de direito pela regra de faltas do parâmetro, dias gozados, dias vendidos, saldo, fim do período concessivo) com os chips "Em aquisição", "A gozar", "Vence em {data}" (alerta a 60 dias, proposta) e "Vencido" (crítico).
2. **Dado** o job diário, **quando** um período concessivo entra no prazo de alerta, **então** quem tem `hr.edit` recebe "Férias de {funcionário} vencem em {data}." com link (`dedup_key` `vacation-due:{periodId}`), e a Minha pauta (DSH-04) lista quando existir.

**Programação**
3. **Dado** `hr.edit`, **quando** clico "Programar férias", escolho o período aquisitivo, o início, os dias (com o fracionamento e os limites da regra do parâmetro), o abono pecuniário (dias vendidos, dentro do limite) e o adiantamento do 13º se pedido, **então** a programação fica `Scheduled` e a validação recusa com 422 o que fere a regra ("O primeiro período precisa ter pelo menos {n} dias." com o valor do parâmetro); o espelho dos dias programados mostra "Férias" (RH-01c) e o funcionário vê em "Meu ponto".
4. **Dado** a programação, **quando** salvo, **então** o `NextSteps` oferece "Gerar aviso de férias" (PDF pelo `IPdfRenderer`, com a antecedência do parâmetro) e o planejamento (PLN-01) recebe a ausência para não contar o funcionário na capacidade (proposta: evento `EmployeeVacationScheduled` com id e datas).

**Cálculo**
5. **Dado** uma programação, **quando** clico "Calcular férias", **então** nasce a folha de férias (tipo `Vacation`, modo `Calculated`) com: remuneração dos dias (salário + adicionais fixos + média das variáveis dos últimos 12 meses pela regra do parâmetro), 1/3 constitucional, abono pecuniário e o seu 1/3, INSS e IRRF calculados sobre as férias conforme a regra da contadora (Pontos em aberto 2), e a memória de cálculo.
6. **Dado** o dataset de referência da contadora (pré-condição: férias integrais, fracionadas, com abono e com médias de extras), **quando** calculado, **então** cada valor bate centavo a centavo.
7. **Dado** a folha de férias fechada (RH-02c), **quando** fecha, **então** o título a pagar vence até o prazo do parâmetro antes do início das férias (proposta: `hr.vacation_payment_days_before`), entra no fluxo de caixa e o recibo de férias em PDF fica disponível; fechada depois do prazo, a tela avisa "Pagamento depois do prazo legal: confirme com a contadora." sem bloquear.
8. **Dado** as férias gozadas, **quando** a folha mensal da competência é calculada (FLH-02a), **então** os dias de férias não entram como salário de novo e a rubrica de compensação segue a regra da contadora.

**Cancelamento e permissões**
9. **Dado** uma programação `Scheduled` sem folha fechada, **quando** cancelo com motivo, **então** volta o saldo e somem os dias do espelho; com folha fechada, 409 "Reabra a folha de férias antes."
10. **[Segurança]** **Dado** o módulo `hr` não liberado, **quando** chamo, **então** 403 `module_not_released`; programar com `hr.edit`, valores com `hr.view_sensitive`; o funcionário vê só a própria programação e saldo, sem valores (autoatendimento, IDOR). `RequireModule` e `perm:`.
11. **Dado** programação, cálculo e cancelamento, **quando** abro o histórico, **então** vejo quem, quando, os valores mascarados e as tabelas usadas.

## Regras de negócio e técnicas
- **Tabelas (propostas):** `hr.vacation_periods` (`employee_id`, `starts_on`, `ends_on`, `entitled_days`, `taken_days`, `sold_days`, `concession_ends_on`, `status`), `hr.vacation_schedules` (`period_id`, `starts_on`, `days`, `sold_days`, `advance_13th`, `status` Scheduled/Paid/Taken/Cancelled, `payroll_run_id`).
- **Regras** no `PayrollCalculator` (`VacationPay`, `VacationThird`, `VacationSale`, `VacationAverages`) e num `VacationPolicy` com os parâmetros.
- Datas de negócio em `date`.

## Permissões, eventos e tabelas
- **Permissões:** `hr.view`, `hr.edit`, `hr.view_sensitive`.
- **Eventos:** `EmployeeVacationScheduled`/`EmployeeVacationCancelled` (`{ employeeId, from, to }`, sem valor).
- **Tabelas:** acima; parâmetros de férias em `hr.parameters`.

## Telas e estados
- Aba **Férias** no funcionário; **RH → Férias** (`/app/rh/ferias`) com os vencimentos dos próximos 90 dias e um calendário por mês; diálogo "Programar férias".
- **Estados:** vazio ("Nenhum período aquisitivo completo ainda."); vencido em destaque; erro.
- **Propositivo:** sugere o início no mês de menor carga da equipe (proposta, sem decidir) e o aviso no prazo.
- **400 px:** períodos em cartões.
- **Ajuda:** `Ajuda/rh-ferias.md`, `Ajuda/rh-funcionario-ferias.md`; `<Dica>` em "Período concessivo" e "Abono pecuniário".

## Plano de testes
- **Unidade (tabela de casos):** períodos e saldo com faltas (CA 1); validações da programação (CA 3); cálculo (CA 5, 6).
- **Integração (Testcontainers):** job de aviso (CA 2); espelho e evento (CA 3, 4); folha, título e prazo (CA 7); mensal sem duplicar (CA 8); cancelamento (CA 9); três camadas e IDOR (CA 10); histórico (CA 11).
- **E2E (Playwright):** programar, calcular e baixar o recibo.

## Fora do escopo
- Férias coletivas: fora do MVP (Pontos em aberto 3).
- Rescisão com férias: FLH-05.

## Pontos em aberto
1. **Regras de férias** (pré-condição, contadora): dias de direito por faltas, afastamentos que zeram ou suspendem o período, fracionamento, limite do abono, antecedência do aviso, prazo de pagamento.
2. **INSS e IRRF das férias** (pré-condição, contadora): como o motor trata a tributação das férias no mês do pagamento e no mês do gozo.
3. Férias coletivas.
4. Alerta de vencimento (60 dias, proposta).

## Definition of Done
- [ ] Critérios de aceite atendidos; testes de unidade (casos da contadora), integração e E2E passando.
- [ ] Migration criada (não destrutiva); job registrado; ajuda escrita; teste de cobertura passando.
- [ ] Auditoria ligada; OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` e `docs/04-fluxos-estados.md` atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `VacationPeriod`, `VacationSchedule`, `VacationPolicy`; regras do motor; PDFs de aviso e recibo; job; telas.

## Quality gates
- Agentes previstos: @dev, @qa, revisão da contadora.
- Gate: CA 3, 6 e 7 obrigatórios.

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

- **Correções feitas na validação:** prazos e limites legais como parâmetros com casos da contadora (pontos 1 e 2); o pagamento fora do prazo avisa sem bloquear (CA 7); o funcionário vê o saldo sem valores (CA 10).
- **Pré-condições para `Ready`:** pontos em aberto 1 e 2; FLH-02b concluída.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
