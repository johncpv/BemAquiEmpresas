# FLH-04 — Folha calculada: 13º salário (1ª e 2ª parcelas)

| Campo | Valor |
|---|---|
| ID | FLH-04 |
| Título | 13º salário calculado em duas parcelas (adiantamento sem descontos e parcela final com INSS e IRRF próprios), avos por mês trabalhado pela regra da contadora, médias de variáveis, adiantamento pago nas férias abatido, provisão mensal do 13º no fluxo de caixa |
| Tamanho | M |
| Status | Draft |
| Requisitos | R-RH-6 ("13º") · R-RH-5 · R-FIN-10 · R-SEC-5 |
| Dependências | FLH-02a/02b (motor e variáveis), FLH-01 (tabelas), FLH-03 (adiantamento nas férias), RH-02b/02c (holerite, fechamento, títulos, previsão) |
| Bloqueia | FLH-05 (13º proporcional na rescisão) |
| Ordem de execução | 5 (Marco 7.2) |

## User story
Como **RH**, quero **calcular as duas parcelas do 13º de todos os funcionários**, e como **proprietário**, quero **ver o 13º previsto no caixa desde o começo do ano**, para **não ser pego de surpresa em novembro e dezembro**.

## Contexto e referências
- PRD R-RH-6; `payroll_runs.type = 13th` (RH-02a propôs 13º 1ª e 2ª parcela); RH-02c ponto em aberto 5 (previsão inclui 13º?).
- Regras legais (avos, prazos das parcelas, tributação só na parcela final, médias): **parâmetros confirmados pela contadora** (Pontos em aberto 1).

## Critérios de aceite
1. **Dado** `hr.create` + `hr.view_sensitive`, **quando** crio a folha "13º 1ª parcela" do ano, **então** cada funcionário ativo recebe o adiantamento = metade da remuneração pela regra do parâmetro, proporcional aos avos até o mês, sem INSS e IRRF, abatendo o adiantamento já pago nas férias (FLH-03); o FGTS do adiantamento segue a regra da contadora.
2. **Dado** a folha "13º 2ª parcela", **quando** calculo, **então** o valor = remuneração de dezembro × avos do ano ÷ 12 + média das variáveis pela regra do parâmetro − adiantamento, com o INSS e o IRRF sobre o 13º integral pelas tabelas vigentes (tributação exclusiva, conforme a contadora), e a memória de cálculo.
3. **Dado** os avos, **quando** calculados, **então** um mês conta pela regra do parâmetro (proposta: 15 dias ou mais trabalhados no mês, Pontos em aberto 1), descontadas as faltas injustificadas e os afastamentos que a contadora indicar.
4. **Dado** o dataset de referência da contadora (pré-condição: admitido no meio do ano, com variáveis, com adiantamento nas férias, afastado), **quando** calculado, **então** bate centavo a centavo.
5. **Dado** o fechamento de cada parcela (RH-02c), **quando** fecha, **então** os títulos vencem no prazo do parâmetro (proposta: datas da contadora) e o `NextSteps` oferece "Enviar holerites" e "Criar ordem de pagamento".
6. **Dado** a previsão da folha (RH-02c CA 7), **quando** o parâmetro `hr.forecast_13th` está ligado (proposta), **então** o fluxo de caixa mostra as duas parcelas como estimativa nos meses de pagamento, pelo total estimado dos funcionários ativos, substituídas pela folha real quando fechada.
7. **Dado** uma folha de 13º do mesmo tipo e ano, **quando** tento criar de novo, **então** 409; a 2ª parcela sem a 1ª fechada pede confirmação ("A 1ª parcela não foi paga: a 2ª será calculada sem adiantamento.").
8. **[Segurança]** **Dado** o módulo `hr` não liberado, **quando** chamo, **então** 403 `module_not_released`; valores com `hr.view_sensitive`; `RequireModule` e `perm:`; canário de valores no log.

## Regras de negócio e técnicas
- Regras no motor: `ThirteenthAdvance`, `ThirteenthFinal`, `ThirteenthAverages`, `ThirteenthMonths`; parâmetros em `hr.parameters`.
- `payroll_runs` com `type` `ThirteenthFirst`/`ThirteenthSecond` e `year`.

## Permissões, eventos e tabelas
- **Permissões:** as da FLH-02a.
- **Eventos:** os da RH-02c.
- **Tabelas:** extensões acima.

## Telas e estados
- Na lista de folhas: "Nova folha → 13º"; prévia dos avos por funcionário antes de calcular.
- **Estados:** pré-condições (tabelas, parcela anterior); calculando; erro por funcionário.
- **Ajuda:** atualizar `Ajuda/rh-folha.md` ("13º salário"); `<Dica>` em "Avos".

## Plano de testes
- **Unidade (tabela de casos):** avos (CA 3), parcelas (CA 1, 2), dataset (CA 4).
- **Integração (Testcontainers):** títulos e prazos (CA 5); previsão e substituição (CA 6); duplicidade (CA 7); três camadas e canário (CA 8).

## Fora do escopo
- 13º proporcional na rescisão: FLH-05.

## Pontos em aberto
1. **Regras do 13º** (pré-condição, contadora): avos, médias, prazos das parcelas, FGTS e tributação.
2. Previsão do 13º no fluxo de caixa desde janeiro (proposta) ou só a partir da 1ª parcela.

## Definition of Done
- [ ] Critérios de aceite atendidos; testes de unidade (casos da contadora) e integração passando.
- [ ] Migration criada (não destrutiva); ajuda atualizada; auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Regras do motor; prévia dos avos; previsão no HR.

## Quality gates
- Agentes previstos: @dev, @qa, revisão da contadora.
- Gate: CA 2, 4 e 6 obrigatórios.

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

- **Correções feitas na validação:** regra dos avos e prazos como parâmetros (CA 3, 5); previsão do 13º no caixa, ligada à RH-02c (CA 6).
- **Pré-condições para `Ready`:** ponto em aberto 1; FLH-03 concluída.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
