# FLH-05 — Folha calculada: rescisão

| Campo | Valor |
|---|---|
| ID | FLH-05 |
| Título | Cálculo da rescisão a partir do desligamento (CAD-04b): tipo de desligamento pela matriz pré-configurada da CLT, aviso prévio (trabalhado, indenizado, dispensado) com os dias proporcionais, saldo de salário, férias vencidas e proporcionais com 1/3, 13º proporcional, saldo do banco de horas, descontos, INSS e IRRF, FGTS do mês e da rescisão e multa do FGTS sobre o saldo digitado; simulação do custo antes de decidir; termo só para conferência; título a pagar no prazo legal; só simulação até a virada e o primeiro caso de rescisão conferido (FLH-06) |
| Tamanho | G |
| Status | Ready (pré-condição externa) |
| Requisitos | R-RH-6 ("rescisão") · R-RH-3 (desligamento) · R-RH-5 · R-FIN-10 · R-PRP-2 · R-PRP-7 · R-SEC-5 |
| Dependências | FLH-02a/02b, FLH-03 (férias), FLH-04 (13º), FLH-01, CAD-04b (desligamento e `EmployeeTerminated`), RH-01c (ponto até o desligamento e banco de horas), RH-02b/02c (holerite, fechamento e títulos, ADR 0036) |
| Bloqueia | FLH-06 (cenário de rescisão na conferência) |
| Ordem de execução | 6 (Marco 7.2 "Férias, 13º e rescisão") |

## User story
Como **RH**, quero **calcular a rescisão de quem sai da empresa e ver o valor e o prazo do pagamento**, e como **proprietário**, quero **saber o custo total de um desligamento antes de confirmar**, para **pagar certo e no prazo**.

## Contexto e referências
- PRD R-RH-6; `payroll_runs.type = Termination`; CAD-04b ("Registrar desligamento": data, situação Desligado, `EmployeeTerminated`).
- Regras legais (tipos de desligamento e verbas de cada um, aviso prévio proporcional, multa do FGTS, prazo de pagamento): **matriz e parâmetros pré-configurados pela CLT** com o chip "A confirmar com a contadora" (ADR 0042), conferidos também pelo jurídico; o sistema calcula para conferência e não substitui a responsabilidade da contadora.
- Transmissão ao governo e documentos oficiais de rescisão: fora do produto (PRD §3.3, eSocial, FGTS Digital).
- **Revisão pré-Fases 4 a 8** (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`): D1, D6, D14; tabela 3.4, pontos 15, 22 a 24, 27 e 28 e linha "Determinismo"; §4.4 padrões 7 e 8.
- **ADR 0039**, **ADR 0036** (título do funcionário e guias com órgão como `Party`), **ADR 0042**.

## Critérios de aceite
**Simulação antes do desligamento**
1. **Dado** `hr.edit` + `hr.view_sensitive`, **quando** clico "Simular rescisão" num funcionário ativo, escolho o tipo (da matriz `hr.termination_matrix`), a data prevista e o aviso prévio (trabalhado, indenizado, dispensado), **então** vejo as verbas e descontos com a memória, o líquido, o FGTS e a multa a recolher e o **custo total para a empresa**, sem gravar nada (R-PRP-7: o sistema sugere, não decide).

**Cálculo**
2. **Dado** a matriz `hr.termination_matrix` pré-configurada pela CLT ("A confirmar com a contadora" e com o jurídico), **quando** consultada, **então** traz por tipo as verbas e a multa: sem justa causa (saldo, aviso, férias vencidas e proporcionais com 1/3, 13º proporcional, multa de 40%); pedido de demissão (saldo, férias vencidas e proporcionais com 1/3, 13º proporcional, desconto do aviso não cumprido, sem multa); justa causa (saldo e férias vencidas com 1/3, sem proporcionais nem multa); acordo entre as partes (aviso indenizado pela metade, multa de 20%, demais verbas integrais); término do contrato de experiência no prazo (saldo, férias proporcionais com 1/3, 13º proporcional, sem aviso nem multa); término antecipado da experiência pela empresa (mais a indenização de metade dos dias restantes e multa de 40%) e pelo empregado (desconto de até metade dos dias restantes).
3. **Dado** um desligamento registrado (CAD-04b) e o espelho até a data fechado ou conferido, **quando** clico "Calcular rescisão", **então** nasce a folha `Termination` daquele funcionário com: saldo de salário dos dias do mês, aviso prévio indenizado com os dias de `hr.notice_days_rule` (pré-configurado: 30 dias + 3 por ano completo, até 90), projeção do aviso indenizado nos avos de férias e 13º, férias vencidas e proporcionais com 1/3 (FLH-03), 13º proporcional (FLH-04, abatido o adiantamento), saldo do banco de horas (positivo pago como horas extras; negativo descontado só quando o acordo permitir, `hr.hour_bank_termination`), descontos (aviso não cumprido, adiantamentos, avulsos), INSS e IRRF, a memória por linha e o retrato das entradas (FLH-02a CA 12).
4. **Dado** o FGTS, **quando** calculado, **então** o FGTS do mês e o da rescisão entram como encargo, e a multa (percentual da matriz pelo tipo) é calculada sobre o **saldo do FGTS digitado pelo RH** a partir do extrato (obrigatório para os tipos com multa: 422 "Informe o saldo do FGTS do extrato para calcular a multa."), com a data do extrato.
5. **Dado** o dataset sintético do CI (um caso por tipo de desligamento, com e sem aviso, com férias vencidas, com banco de horas positivo e negativo), **quando** calculado, **então** bate centavo a centavo; nenhum holerite real no Git.
6. **Dado** a rescisão calculada, **quando** gero o "Termo de rescisão para conferência" (PDF, `IPdfRenderer`), **então** o documento traz as verbas, os descontos e o líquido com a marca "Documento de conferência interna: o termo oficial é emitido pela contadora."

**Fechamento e prazo**
7. **Dado** a folha de rescisão calculada, **quando** a virada da FLH-06 não aconteceu ou ainda não há **um caso de rescisão conferido** com diferença zero na FLH-06, **então** é simulação: "Fechar" fica bloqueado com "Rescisão calculada em conferência: feche a rescisão lançada." e o termo sai com a marca "Simulação".
8. **Dado** o fechamento da folha de rescisão oficial (RH-02c: `hr.approve`, 2FA de novo, D6; quem fecha não paga), **quando** confirmo, **então** o diálogo mostra o líquido, os encargos, a multa e o prazo de pagamento por `hr.termination_payment_days` (pré-configurado: 10 dias corridos do desligamento), e nascem o título do funcionário (`payee_type = Employee`) e os das guias (FGTS rescisório e multa com o órgão como `Party`, ADR 0036) com esse vencimento; depois do prazo, o aviso "Pagamento depois do prazo legal: há multa. Confirme com a contadora." sem bloquear.
9. **Dado** a rescisão fechada, **quando** termina, **então** os períodos de férias e o saldo do banco de horas do funcionário ficam encerrados, e a folha mensal da competência não inclui mais o funcionário (FLH-02a).
10. **Dado** o desligamento estornado (o funcionário volta antes do pagamento), **quando** reabro a folha de rescisão e cancelo o desligamento (CAD-04b), **então** tudo volta sem apagar o histórico; com título pago, 409 "A rescisão já foi paga: fale com a contadora para o estorno."

**Permissões**
11. **[Segurança]** **Dado** o módulo `hr` não liberado, **quando** chamo, **então** 403 `module_not_released`; simular e calcular com `hr.edit` + `hr.view_sensitive`, fechar com `hr.approve` pela RH-02c; o funcionário não vê a simulação; canário de valores no log e nos argumentos do job. `RequireModule` e `perm:`.
12. **Dado** simulação (não grava), cálculo e fechamento, **quando** abro o histórico, **então** vejo o cálculo e o fechamento com quem, quando, a versão do motor e as tabelas usadas.

## Regras de negócio e técnicas
- Regras no motor: `TerminationSalaryBalance`, `NoticePay`, `VacationOnTermination`, `ThirteenthOnTermination`, `HourBankOnTermination`, `FgtsFine`, cada uma por tabela de casos; `TerminationPolicy` com a matriz "tipo × verbas" vinda de `hr.termination_matrix` (não do código).
- Tabela: `hr.terminations` (`employee_id`, `kind`, `notice_kind`, `notice_days`, `fgts_balance`, `fgts_statement_date`, `payroll_run_id`, `status` Draft/Calculated/Closed/Cancelled).
- Parâmetros pré-configurados "A confirmar com a contadora": `hr.termination_matrix`, `hr.notice_days_rule`, `hr.termination_payment_days`, `hr.hour_bank_termination`.

## Permissões, eventos e tabelas
- **Permissões:** `hr.edit`, `hr.view_sensitive`; `hr.approve` (fechar, RH-02c).
- **Eventos:** consome `EmployeeTerminated` (dentro do HR, sugere "Calcular rescisão" no `NextSteps`).
- **Tabelas:** `hr.terminations`; parâmetros de rescisão.

## Telas e estados
- **Padrões comuns das Fases 4 a 8:** 7 (fechamento com o impacto em lista: títulos e guias); 8 (valores no bloco "Dados protegidos").
- No funcionário: "Simular rescisão" e, depois do desligamento, o `NextSteps` "Calcular rescisão"; folha de rescisão com memória.
- **Verbos:** "Simular rescisão"; "Calcular rescisão" / "Rescisão calculada"; "Gerar termo para conferência".
- **Estados:** pré-condições (ponto, saldo do FGTS, tabelas) com links; calculando; erro; simulação sem dados ("Escolha o tipo e a data para simular.").
- **Propositivo:** a data prevista vem com hoje e o aviso com o mais comum do tipo; o custo total aparece antes de qualquer confirmação; depois do desligamento, o próximo passo é "Calcular rescisão".
- **400 px:** verbas em cartões.
- **Ajuda:** `Ajuda/rh-rescisao.md`; `<Dica>` em "Aviso prévio indenizado", "Multa do FGTS" e "Saldo do FGTS".

## Plano de testes
- **Unidade (tabela de casos):** matriz e cada verba (CA 2–4); dataset sintético (CA 5).
- **Integração (Testcontainers):** simulação sem gravar (CA 1); saldo do FGTS obrigatório (CA 4); PDF de conferência (CA 6); simulação até a conferência (CA 7); títulos, guias, 2FA e prazo (CA 8); encerramentos (CA 9); estorno (CA 10); três camadas e canário (CA 11); histórico (CA 12).
- **E2E (Playwright):** simular, registrar o desligamento, calcular e ver o bloqueio do fechamento no tenant `demo`.

## Fora do escopo
- Termo oficial, homologação, guias e eSocial/FGTS Digital: contadora (PRD §3.3).
- Rescisão coletiva e de aprendiz com regras próprias: depois do MVP.

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fases 4 a 8, tabela 3.4, pontos 15, 22–24, 27 e 28 e linha "Determinismo"):
1. Matriz de rescisão, aviso prévio proporcional e prazo de 10 dias pré-configurados pela CLT, "A confirmar com a contadora" e com o jurídico (CA 2, 3, 8).
2. Saldo do FGTS digitado pelo RH a partir do extrato (CA 4).
3. Termo só para conferência no MVP (CA 6).
4. Saldo do banco de horas tratado na rescisão (banco sem vencimento automático, RH-01c ponto 11) (CA 3).
5. Rescisão calculada só vira oficial depois da virada e de um caso de rescisão conferido na FLH-06 (CA 7).
6. eSocial, DCTFWeb e FGTS Digital fora do produto.
7. Determinismo: retrato e versão do motor (CA 3).

Decisões da revisão aplicadas: D1 (Fase 7 antes do go-live); D6 (2FA no fechamento, CA 8); D14/ADR 0036 (título do funcionário e guias com o órgão como `Party`, CA 8); dataset sintético (ADR 0039, CA 5).

**Pré-condição externa:** conferência da matriz e dos prazos pela contadora e pelo jurídico (valores pré-configurados "A confirmar com a contadora"; não bloqueia o `Ready`).

## Definition of Done
- [ ] Critérios de aceite atendidos; testes de unidade (dataset sintético), integração e E2E passando.
- [ ] Migration criada (não destrutiva); parâmetros com seed e chip; ajuda escrita; teste de cobertura passando.
- [ ] Confirmação com impacto financeiro; auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados; `docs/03` e `docs/04` atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `TerminationPolicy` e regras; simulação sem persistência; PDF de conferência; telas.

## Quality gates
- Agentes previstos: @dev, @qa, revisão da contadora e do jurídico (externa, na matriz).
- Gate: CA 1, 2, 5, 7 e 8 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fases 4 a 8).

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
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** a matriz de verbas vem de parâmetro, não do código (ponto 1); simulação com custo total antes de decidir (CA 1); termo só de conferência (CA 5).
- **Correções da revalidação (21/10/2026):** pontos 22 a 24 decididos (matriz da CLT pré-configurada, CA 2; saldo do FGTS digitado e obrigatório, CA 4; termo de conferência, CA 6); banco de horas na rescisão (ponto 15, CA 3); simulação até a virada e o primeiro caso conferido (ponto 27, CA 7); título do funcionário, guias e 2FA (D14, D6, CA 8); estorno do desligamento definitivo (CA 10); dataset sintético (CA 5); ADRs 0036, 0039 e 0042 citadas; CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma de decisão de story. Externa: conferência da matriz pela contadora e pelo jurídico (não bloqueia).
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready (pré-condição externa)`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (D1, D6, D14, tabela 3.4 pontos 15, 22–24, 27 e 28 e "Determinismo", §4.4 padrões 7 e 8); nova ordem (Marco 7.2, ordem 6, antes do go-live); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
