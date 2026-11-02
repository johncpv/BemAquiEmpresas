# FLH-04 — Folha calculada: 13º salário (1ª e 2ª parcelas)

| Campo | Valor |
|---|---|
| ID | FLH-04 |
| Título | 13º salário calculado em duas parcelas (adiantamento sem INSS e IRRF e parcela final com INSS e IRRF próprios), avos por mês trabalhado pela regra pré-configurada, médias de variáveis, adiantamento pago nas férias abatido, previsão das duas parcelas no fluxo de caixa desde janeiro; só simulação até a virada e o primeiro caso de 13º conferido (FLH-06) |
| Tamanho | M |
| Status | Done (pré-condição externa: contadora e conferência paralela) |
| Requisitos | R-RH-6 ("13º") · R-RH-5 · R-FIN-10 · R-SEC-5 |
| Dependências | FLH-02a/02b (motor e variáveis), FLH-01 (tabelas), FLH-03 (adiantamento nas férias), RH-02b/02c (holerite, fechamento, títulos e previsão `PayrollForecast`, ADR 0036) |
| Bloqueia | FLH-05 (13º proporcional na rescisão) |
| Ordem de execução | 5 (Marco 7.2 "Férias, 13º e rescisão") |

## User story
Como **RH**, quero **calcular as duas parcelas do 13º de todos os funcionários**, e como **proprietário**, quero **ver o 13º previsto no caixa desde o começo do ano**, para **não ser pego de surpresa em novembro e dezembro**.

## Contexto e referências
- PRD R-RH-6; `payroll_runs.type` `ThirteenthFirst`/`ThirteenthSecond` (RH-02a); RH-02c (folha prevista de 3 meses com 13º e férias, origem `PayrollForecast`, ponto 27 da Fase 6).
- Regras legais (avos, prazos das parcelas, tributação só na parcela final, médias): **parâmetros pré-configurados pela legislação** com o chip "A confirmar com a contadora" (ADR 0042).
- **Revisão pré-Fases 4 a 8** (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`): D1, D6, D14; tabela 3.4, pontos 20, 21 e 27 e linha "Determinismo"; §4.4 padrão 8.
- **ADR 0039** (motor, determinismo, dados sintéticos), **ADR 0036** (títulos por funcionário, previsão `PayrollForecast`), **ADR 0042**.

## Critérios de aceite
1. **Dado** `hr.create` + `hr.view_sensitive`, **quando** crio a folha "13º 1ª parcela" do ano, **então** cada funcionário ativo recebe o adiantamento = metade da remuneração do mês anterior, proporcional aos avos até o mês, sem INSS e IRRF, com FGTS (`hr.thirteenth_rules`, pré-configurado), abatendo o adiantamento já pago nas férias (FLH-03); a data limite sugerida é `hr.thirteenth_first_due` (pré-configurada: 30/11).
2. **Dado** a folha "13º 2ª parcela", **quando** calculo, **então** o valor = remuneração de dezembro × avos do ano ÷ 12 + média das variáveis do ano (`hr.thirteenth_averages`) − adiantamento, com o INSS e o IRRF sobre o 13º integral pelas tabelas vigentes (IRRF exclusivo na fonte, separado do salário), a memória de cálculo e o retrato das entradas (FLH-02a CA 12); a data limite sugerida é `hr.thirteenth_second_due` (pré-configurada: 20/12).
3. **Dado** os avos, **quando** calculados, **então** um mês conta quando há **15 dias ou mais** trabalhados no mês (`hr.thirteenth_month_min_days`, pré-configurado: 15), descontadas as faltas injustificadas e os afastamentos pela previdência acima de 15 dias (`hr.thirteenth_leave_rules`), com a memória mês a mês.
4. **Dado** o dataset sintético do CI (admitido no meio do ano, com variáveis, com adiantamento nas férias, afastado pela previdência, com faltas que derrubam um avo), **quando** calculado, **então** bate centavo a centavo; nenhum holerite real no Git.
5. **Dado** uma folha de 13º calculada, **quando** a virada da FLH-06 não aconteceu ou ainda não há **um caso de 13º conferido** com diferença zero na FLH-06, **então** é simulação: "Fechar" fica bloqueado com "13º calculado em conferência: feche a folha de 13º lançada." e a prévia do holerite sai com a marca "Simulação".
6. **Dado** o fechamento de cada parcela oficial (RH-02c: `hr.approve`, 2FA de novo, D6; quem fecha não paga), **quando** fecha, **então** nasce um título por funcionário (`payee_type = Employee`) e um por guia (ADR 0036), vencendo na data limite do parâmetro (ou antes, se o RH escolher), e o `NextSteps` oferece "Publicar holerites" e "Criar ordem de pagamento".
7. **Dado** a previsão da folha (RH-02c), **quando** o parâmetro `hr.forecast_13th` está ligado (pré-configurado: ligado), **então** desde janeiro o fluxo de caixa mostra as duas parcelas como estimativa (origem `PayrollForecast`) nos meses das datas limite, pelo total estimado dos funcionários ativos (salário atual × avos projetados do ano), substituídas pela folha real quando fechada; o rótulo "Estimativa do 13º" aparece no fluxo.
8. **Dado** uma folha de 13º do mesmo tipo e ano, **quando** tento criar de novo, **então** 409; a 2ª parcela sem a 1ª fechada pede confirmação ("A 1ª parcela não foi paga: a 2ª será calculada sem adiantamento.").
9. **[Segurança]** **Dado** o módulo `hr` não liberado, **quando** chamo, **então** 403 `module_not_released`; valores com `hr.view_sensitive`, criar com `hr.create`, fechar com `hr.approve`; sem a ação, 403 `forbidden`; `RequireModule` e `perm:`; canário de valores no log e nos argumentos do job.

## Regras de negócio e técnicas
- Regras no motor: `ThirteenthAdvance`, `ThirteenthFinal`, `ThirteenthAverages`, `ThirteenthMonths`; parâmetros em `hr.parameters` (`hr.thirteenth_rules`, `hr.thirteenth_first_due`, `hr.thirteenth_second_due`, `hr.thirteenth_averages`, `hr.thirteenth_month_min_days`, `hr.thirteenth_leave_rules`, `hr.forecast_13th`), pré-configurados e "A confirmar com a contadora".
- `payroll_runs` com `type` `ThirteenthFirst`/`ThirteenthSecond` e `year` (único por tipo e ano).
- A previsão usa o mesmo caminho da RH-02c (`PayrollForecast`), só com horizonte até dezembro para o 13º.

## Permissões, eventos e tabelas
- **Permissões:** as da FLH-02a.
- **Eventos:** os da RH-02c (incluindo o de mudança da previsão).
- **Tabelas:** extensões acima; parâmetros novos.

## Telas e estados
- **Padrões comuns das Fases 4 a 8:** 8 (valores no bloco "Dados protegidos"); 7 (fechamento com impacto em lista: um título por funcionário e por guia).
- Na lista de folhas: "Nova folha → 13º"; prévia dos avos por funcionário antes de calcular.
- **Estados:** pré-condições (tabelas, parcela anterior); calculando; erro por funcionário; vazio ("Nenhum funcionário com avos no ano.").
- **Propositivo:** em novembro, a Minha pauta (DSH-04) lembra "13º 1ª parcela vence em {data}"; a parcela e o ano vêm preenchidos.
- **400 px:** prévia dos avos em cartões.
- **Ajuda:** atualizar `Ajuda/rh-folha.md` ("13º salário") e `Ajuda/financeiro-fluxo-de-caixa.md` ("Estimativa do 13º"); `<Dica>` em "Avos".

## Plano de testes
- **Unidade (tabela de casos):** avos (CA 3), parcelas (CA 1, 2), dataset sintético (CA 4).
- **Integração (Testcontainers):** simulação até a conferência (CA 5); títulos por funcionário e por guia, 2FA e prazos (CA 6); previsão desde janeiro e substituição (CA 7); duplicidade (CA 8); três camadas e canário (CA 9).

## Fora do escopo
- 13º proporcional na rescisão: FLH-05.

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fases 4 a 8, tabela 3.4, pontos 20, 21 e 27 e linha "Determinismo"):
1. Regras do 13º pré-configuradas pela legislação, "A confirmar com a contadora": avos com 15 dias, médias do ano, 1ª parcela até 30/11 com FGTS e sem INSS e IRRF, 2ª até 20/12 com tributação exclusiva (CA 1–3).
2. Previsão do 13º no caixa desde janeiro (CA 7).
3. 13º calculado só vira oficial depois da virada e de um caso de 13º conferido na FLH-06 (CA 5).
4. Determinismo: retrato e versão do motor (CA 2).

Decisões da revisão aplicadas: D1 (Fase 7 antes do go-live); D6 (2FA no fechamento, CA 6); D14/ADR 0036 (títulos por funcionário e por guia, `PayrollForecast`, CA 6, 7); dataset sintético (ADR 0039, CA 4).

**Pré-condição externa:** conferência dos parâmetros do 13º pela contadora (valores pré-configurados "A confirmar com a contadora"; não bloqueia o `Ready`).

## Definition of Done
- [x] Critérios de aceite atendidos; testes de unidade (dataset sintético) e integração passando.
- [x] Sem migration (nenhuma coluna nova: enum em texto e o retrato no `inputs_snapshot`); parâmetros com seed e chip; ajuda atualizada; auditoria ligada (entidades já auditadas).
- [x] OpenAPI e cliente Orval regenerados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [x] Regras do motor; prévia dos avos; previsão do 13º no caminho da RH-02c.

## Quality gates
- Agentes previstos: @dev, @qa, revisão da contadora (externa, nos parâmetros).
- Gate: CA 2, 4, 5 e 7 obrigatórios.

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

- **Correções feitas na validação (21/10/2026, primeira validação):** regra dos avos e prazos como parâmetros (CA 3, 5); previsão do 13º no caixa, ligada à RH-02c (CA 6).
- **Correções da revalidação (21/10/2026):** pontos 20 e 21 decididos, com as regras pré-configuradas "A confirmar com a contadora" (CA 1–3) e a previsão desde janeiro (CA 7); simulação até a virada e o primeiro caso conferido (ponto 27, CA 5); títulos por funcionário e por guia e 2FA no fechamento (D14, D6, CA 6); dataset sintético (CA 4); ADRs 0036, 0039 e 0042 citadas; CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma de decisão de story. Externa: conferência dos parâmetros pela contadora (não bloqueia).
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready (pré-condição externa)`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (D1, D6, D14, tabela 3.4 pontos 20, 21 e 27 e "Determinismo", §4.4 padrões 7 e 8); nova ordem (Marco 7.2, ordem 5, antes do go-live); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
| 01/11/2026 | 1.0 | Implementada: `ThirteenthCalculator` (avos, 1ª e 2ª parcelas, médias das horas da FLH-02b, adiantamento das férias pela regra `ThirteenthAdvance`), folha de 13º calculada no caminho da Mensal, prévia dos avos, simulação até a virada e o primeiro 13º conferido, estimativa no caixa desde janeiro, lembrete na Minha pauta (`hr.thirteenth_due`). **Pendências:** conferência da contadora dos parâmetros do 13º; o adiantamento nas férias depende da FLH-03 gravar a linha com a regra `ThirteenthAdvance`; a retificação do 13º fechado fica para a FLH-06 | @dev |
