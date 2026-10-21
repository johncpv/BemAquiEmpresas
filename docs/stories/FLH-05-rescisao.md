# FLH-05 — Folha calculada: rescisão

| Campo | Valor |
|---|---|
| ID | FLH-05 |
| Título | Cálculo da rescisão a partir do desligamento (CAD-04b): tipo de desligamento, aviso prévio (trabalhado, indenizado, dispensado) com os dias pela regra vigente, saldo de salário, férias vencidas e proporcionais com 1/3, 13º proporcional, descontos, INSS e IRRF, FGTS do mês e da rescisão e multa do FGTS como encargo; termo de rescisão para conferência, título a pagar no prazo legal parametrizado |
| Tamanho | G |
| Status | Draft |
| Requisitos | R-RH-6 ("rescisão") · R-RH-3 (desligamento) · R-RH-5 · R-FIN-10 · R-PRP-2 · R-PRP-7 · R-SEC-5 |
| Dependências | FLH-02a/02b, FLH-03 (férias), FLH-04 (13º), FLH-01, CAD-04b (desligamento e `EmployeeTerminated`), RH-01c (ponto até o desligamento), RH-02b/02c |
| Bloqueia | FLH-06 (cenário de rescisão na conferência) |
| Ordem de execução | 6 (Marco 7.2) |

## User story
Como **RH**, quero **calcular a rescisão de quem sai da empresa e ver o valor e o prazo do pagamento**, e como **proprietário**, quero **saber o custo total de um desligamento antes de confirmar**, para **pagar certo e no prazo**.

## Contexto e referências
- PRD R-RH-6; `payroll_runs.type = Termination`; CAD-04b ("Registrar desligamento": data, situação Desligado, `EmployeeTerminated`).
- Regras legais (tipos de desligamento e verbas de cada um, aviso prévio proporcional, multa do FGTS, prazos de pagamento, homologação): **regras e parâmetros confirmados pela contadora e pelo jurídico** (Pontos em aberto 1), com casos de teste; o sistema calcula para conferência, não substitui a responsabilidade da contadora.
- Transmissão ao governo e documentos oficiais de rescisão: fora do produto (PRD §3.3, eSocial).

## Critérios de aceite
**Simulação antes do desligamento**
1. **Dado** `hr.edit` + `hr.view_sensitive`, **quando** clico "Simular rescisão" num funcionário ativo, escolho o tipo (lista do parâmetro: sem justa causa, pedido de demissão, por justa causa, acordo entre as partes, término de contrato de experiência e outros que a contadora indicar), a data prevista e o aviso prévio (trabalhado, indenizado, dispensado), **então** vejo as verbas e descontos com a memória, o líquido, o FGTS e a multa a recolher e o **custo total para a empresa**, sem gravar nada (R-PRP-7: o sistema sugere, não decide).

**Cálculo**
2. **Dado** um desligamento registrado (CAD-04b) e o ponto até a data fechado ou conferido, **quando** clico "Calcular rescisão", **então** nasce a folha `Termination` daquele funcionário com: saldo de salário dos dias do mês, aviso prévio indenizado com os dias da regra vigente (parâmetro), férias vencidas e proporcionais com 1/3 (FLH-03, saldo dos períodos), 13º proporcional (FLH-04, abatido o adiantamento), descontos (aviso não cumprido, adiantamentos, avulsos), INSS e IRRF pelas regras da contadora, e a memória por linha.
3. **Dado** o FGTS, **quando** calculado, **então** o FGTS do mês e o da rescisão entram como encargo, e a multa (percentual do parâmetro pelo tipo de desligamento) é calculada sobre o saldo informado do FGTS do funcionário (digitado pelo RH a partir do extrato, Pontos em aberto 2), como título de encargo.
4. **Dado** o dataset de referência da contadora (pré-condição: um caso por tipo de desligamento, com e sem aviso, com férias vencidas), **quando** calculado, **então** bate centavo a centavo.
5. **Dado** a rescisão calculada, **quando** gero o "Termo de rescisão para conferência" (PDF, `IPdfRenderer`), **então** o documento traz as verbas, os descontos e o líquido com a marca "Documento de conferência interna: o termo oficial é emitido pela contadora." (Pontos em aberto 3).

**Fechamento e prazo**
6. **Dado** o fechamento da folha de rescisão (RH-02c), **quando** confirmo, **então** o diálogo mostra o líquido, os encargos e o prazo de pagamento pelo parâmetro (proposta: `hr.termination_payment_days` contados do desligamento), e os títulos nascem com esse vencimento; depois do prazo, o aviso "Pagamento depois do prazo legal: há multa. Confirme com a contadora." sem bloquear.
7. **Dado** a rescisão fechada, **quando** termina, **então** os períodos de férias e o saldo do banco de horas do funcionário ficam encerrados, e a folha mensal da competência não inclui mais o funcionário (FLH-02a).
8. **Dado** o desligamento estornado (o funcionário volta antes do pagamento, proposta), **quando** reabro a folha de rescisão e cancelo o desligamento, **então** tudo volta sem apagar o histórico; com título pago, 409.

**Permissões**
9. **[Segurança]** **Dado** o módulo `hr` não liberado, **quando** chamo, **então** 403 `module_not_released`; simular e calcular com `hr.edit` + `hr.view_sensitive`, fechar pela regra da RH-02c; o funcionário não vê a simulação; canário de valores no log. `RequireModule` e `perm:`.
10. **Dado** simulação (não grava), cálculo e fechamento, **quando** abro o histórico, **então** vejo o cálculo e o fechamento com quem, quando e as tabelas usadas.

## Regras de negócio e técnicas
- Regras no motor: `TerminationSalaryBalance`, `NoticePay`, `VacationOnTermination`, `ThirteenthOnTermination`, `FgtsFine`, cada uma por tabela de casos; `TerminationPolicy` com a matriz "tipo × verbas" vinda do parâmetro (não do código).
- Tabela: `hr.terminations` (proposta: `employee_id`, `kind`, `notice_kind`, `notice_days`, `fgts_balance`, `payroll_run_id`, `status`).

## Permissões, eventos e tabelas
- **Permissões:** `hr.edit`, `hr.view_sensitive`; fechamento pela RH-02c.
- **Eventos:** consome `EmployeeTerminated` (dentro do HR, sugere "Calcular rescisão" no `NextSteps`).
- **Tabelas:** `hr.terminations`; parâmetros de rescisão.

## Telas e estados
- No funcionário: "Simular rescisão" e, depois do desligamento, o `NextSteps` "Calcular rescisão"; folha de rescisão com memória.
- **Estados:** pré-condições (ponto, saldo do FGTS, tabelas); calculando; erro.
- **400 px:** verbas em cartões.
- **Ajuda:** `Ajuda/rh-rescisao.md`; `<Dica>` em "Aviso prévio indenizado" e "Multa do FGTS".

## Plano de testes
- **Unidade (tabela de casos):** cada tipo e cada verba (CA 2, 3); dataset (CA 4).
- **Integração (Testcontainers):** simulação sem gravar (CA 1); PDF de conferência (CA 5); títulos e prazo (CA 6); encerramentos (CA 7); estorno (CA 8); três camadas e canário (CA 9); histórico (CA 10).
- **E2E (Playwright):** simular, registrar o desligamento, calcular e fechar.

## Fora do escopo
- Termo oficial, homologação, guias e eSocial: contadora (PRD §3.3).
- Rescisão coletiva e de aprendiz com regras próprias: depois do MVP.

## Pontos em aberto
1. **Matriz de verbas por tipo de desligamento, aviso prévio proporcional e prazo de pagamento** (pré-condição, contadora + jurídico).
2. **Saldo do FGTS** para a multa (contadora): digitado pelo RH (proposta) ou calculado pelos depósitos registrados no sistema.
3. Termo de rescisão: só conferência (proposta) ou modelo oficial.

## Definition of Done
- [ ] Critérios de aceite atendidos; testes de unidade (casos da contadora), integração e E2E passando.
- [ ] Migration criada (não destrutiva); ajuda escrita; teste de cobertura passando.
- [ ] Confirmação com impacto financeiro; auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados; `docs/03` e `docs/04` atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `TerminationPolicy` e regras; simulação sem persistência; PDF de conferência; telas.

## Quality gates
- Agentes previstos: @dev, @qa, revisão da contadora e do jurídico.
- Gate: CA 1, 4 e 6 obrigatórios.

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

- **Correções feitas na validação:** a matriz de verbas vem de parâmetro, não do código (ponto 1); simulação com custo total antes de decidir (CA 1); termo só de conferência (CA 5).
- **Pré-condições para `Ready`:** pontos em aberto 1 e 2; FLH-03 e FLH-04 concluídas.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
