# FLH-02b — Folha calculada: horas extras, adicional noturno e DSR a partir do ponto

| Campo | Valor |
|---|---|
| ID | FLH-02b |
| Título | Horas extras do espelho fechado separadas por tipo (dia útil, domingo, feriado) com percentuais parametrizados, adicional noturno quando houver, DSR sobre as variáveis e perda do DSR por falta injustificada, saldo do banco de horas pago ou descontado por decisão do RH; tudo com memória de cálculo |
| Tamanho | M (quebra da FLH-02) |
| Status | Draft |
| Requisitos | R-RH-6 ("horas extras e DSR a partir do ponto") · R-RH-4 · R-SEC-5 |
| Dependências | FLH-02a (motor), RH-01c (espelho fechado e banco de horas), FLH-01 (parâmetros), CAD-03 (feriados pelo `IBusinessCalendar`) |
| Bloqueia | FLH-03, FLH-05 (médias de variáveis) |
| Ordem de execução | 3 (Marco 7.1) |

## User story
Como **RH**, quero **que as horas extras e o DSR saiam do ponto fechado, com os percentuais certos**, para **pagar o que foi trabalhado sem recontar as horas à mão**.

## Contexto e referências
- PRD R-RH-6; RH-01c (extras, faltas e banco de horas por dia, com tolerância); CLT e convenção coletiva definem percentuais, adicional noturno, DSR e perda do DSR: **parâmetros**, nunca fixos (Pontos em aberto 1).
- Jornada da Konebras é diurna (CAD-04a, seed "08:00–17:48"); o adicional noturno entra como regra opcional.

## Critérios de aceite
1. **Dado** o espelho fechado (RH-01c), **quando** o motor calcula, **então** classifica cada minuto extra do dia em dia útil, domingo/folga e feriado (feriado pelo calendário padrão), e gera as linhas "Horas extras {percentual}%" com a referência em horas e o valor = horas × valor da hora × (1 + percentual), com os percentuais do parâmetro `hr.overtime_rates` (proposta: um por tipo de dia; seed vazio, a preencher com a contadora e a convenção).
2. **Dado** o banco de horas ligado (RH-01c), **quando** calcula, **então** as extras do mês vão para o banco e não para pagamento, salvo a decisão do RH na folha ("Pagar {n} h do banco" ou "Descontar {n} h do banco", com motivo), que gera as linhas e baixa o saldo no fechamento.
3. **Dado** horas entre o início e o fim do horário noturno do parâmetro (`hr.night_shift`, desligado no seed), **quando** existirem, **então** gera "Adicional noturno" com o percentual e a redução da hora noturna do parâmetro; desligado, nenhuma linha.
4. **Dado** as variáveis do mês (extras e adicional noturno), **quando** calcula, **então** gera o "DSR sobre variáveis" = variáveis ÷ dias úteis do mês × domingos e feriados do mês (fórmula proposta, Pontos em aberto 2), com os dias contados pelo calendário padrão.
5. **Dado** uma falta injustificada numa semana (sem abono aprovado), **quando** calcula, **então** desconta o DSR daquela semana pela regra do parâmetro `hr.dsr_loss_on_absence` (ligado ou desligado; Pontos em aberto 2), com a referência "DSR da semana de {data}".
6. **Dado** o dataset de referência da contadora (pré-condição: casos com extras em dia útil, domingo e feriado, falta na semana e banco ligado e desligado), **quando** calcula, **então** linhas e valores batem centavo a centavo.
7. **Dado** a memória de cálculo (FLH-02a CA 11), **quando** abro a linha de extras ou DSR, **então** vejo os dias de origem (link para o espelho), as horas e os percentuais usados.
8. **Dado** o módulo `hr` não liberado ou sem `hr.view_sensitive`, **quando** chamo, **então** 403 com o código da camada; `RequireModule` e `perm:`.

## Regras de negócio e técnicas
- Novas regras no `PayrollCalculator`: `Overtime`, `NightShift`, `Dsr`, `DsrLoss`, `HourBankPayout`, cada uma testada por tabela.
- Parâmetros em `hr.parameters`; o espelho fechado é a única fonte de horas (nada recalcula marcações).
- Baixa do saldo do banco no fechamento da folha, registrada no timesheet do mês (`bank_payout_minutes`).

## Permissões, eventos e tabelas
- **Permissões:** as da FLH-02a.
- **Eventos:** nenhum novo.
- **Tabelas:** `hr.parameters` (+ `hr.overtime_rates`, `hr.night_shift`, `hr.dsr_loss_on_absence`); `hr.timesheets` (+ `bank_payout_minutes`).

## Telas e estados
- Na folha: seção "Banco de horas" por funcionário com "Pagar" e "Descontar"; memória das extras e do DSR.
- **Estados:** parâmetros de extras vazios bloqueiam o cálculo com "Cadastre os percentuais de horas extras em Configurações → Parâmetros."
- **Ajuda:** atualizar `Ajuda/rh-folha-detalhe.md` ("Horas extras e DSR") e `Ajuda/configuracoes-parametros.md` (seção do RH).

## Plano de testes
- **Unidade (tabela de casos):** classificação do dia (CA 1), banco (CA 2), noturno (CA 3), DSR e perda (CA 4, 5), dataset (CA 6).
- **Integração (Testcontainers):** memória com links (CA 7); baixa do banco no fechamento (CA 2); três camadas (CA 8).

## Fora do escopo
- Escalas 12×36 e turnos de revezamento: fora do MVP.
- Médias de variáveis para férias, 13º e rescisão: FLH-03 a FLH-05.

## Pontos em aberto
1. **Percentuais de horas extras e adicional noturno** (pré-condição, contadora + convenção do sindicato).
2. **Fórmula do DSR sobre variáveis e regra da perda do DSR por falta** (pré-condição, contadora).
3. Pagamento do banco de horas vencido (ligado ao ponto em aberto 2 da RH-01c).

## Definition of Done
- [ ] Critérios de aceite atendidos; testes de unidade (casos da contadora) e integração passando.
- [ ] Migration criada (não destrutiva); ajuda atualizada; auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Regras do motor e parâmetros; seção do banco de horas na folha.

## Quality gates
- Agentes previstos: @dev, @qa, revisão da contadora.
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

- **Correções feitas na validação:** percentuais e fórmulas como parâmetros com seed vazio (CA 1, 4, 5); o espelho fechado como única fonte de horas.
- **Pré-condições para `Ready`:** pontos em aberto 1 e 2; FLH-02a concluída.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da FLH-02) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
