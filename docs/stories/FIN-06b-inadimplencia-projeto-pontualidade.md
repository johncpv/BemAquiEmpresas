# FIN-06b — Inadimplência por projeto, pontualidade do cliente e histórico do terceiro

| Campo | Valor |
|---|---|
| ID | FIN-06b |
| Título | Aging por projeto, indicador de pontualidade do cliente (atraso médio e % pago com atraso) exposto por contrato para a FIN-07, e aba "Comercial e financeiro" no terceiro |
| Tamanho | M (quebra da FIN-06, que tinha sido re-estimada de P para M ao receber R-TER-3 e R-TER-4) |
| Status | Ready |
| Requisitos | R-FIN-12 (parte: vencidos por projeto) · R-TER-4 (pontualidade: média de dias de atraso) · R-TER-3 (histórico do terceiro: orçamentos, contratos, títulos, pontualidade) · R-PRP-5 |
| Dependências | FIN-06a (aging e tela), FIN-01 (baixas), COM-04b (títulos de projeto), COM-01a e COM-04a (blocos de orçamentos e contratos no terceiro), FIN-02 |
| Bloqueia | FIN-07 (cenário ajustado usa a pontualidade), DSH-01 |
| Ordem de execução | 17 (Marco 2.4 "Controles e D") |

## User story
Como **financeiro**, quero **ver a inadimplência por projeto e saber se cada cliente costuma pagar em dia, com o histórico dele numa aba do cadastro**, para **negociar com quem atrasa e prever melhor o caixa**.

## Contexto e referências
- PRD R-FIN-12, R-TER-3, R-TER-4; §1.1 problema 2.
- `03-modelo-dados` → `customer_payment_stats` (view: `party_id`, `avg_delay_days`, `pct_overdue`, `last_12m_invoiced`).
- `04-fluxos-estados` §3.5 (o Ajustado usa a média de atraso dos últimos 12 meses; sem histórico, a média geral do tenant): esta story calcula; a FIN-07 usa.
- ADR 0017: sem histórico do Dolibarr; revisão pré-Fase 2, seção 1.1 item 14: **títulos de origem `Opening` ficam fora do cálculo da pontualidade** (o atraso deles começou antes do sistema).
- Revisão pré-Fase 1 §2.2: histórico comercial e financeiro do terceiro (R-TER-3) na Fase 2; revisão pré-Fase 2 §2.2 baixos (pontualidade de cliente PF só com `finance.view` e `no-store`; SQL parametrizado).

## Critérios de aceite
**Aging por projeto**
1. **Dado** o dataset fixo da FIN-06a com títulos de contrato de 2 projetos, **quando** abro a aba "Por projeto" da Inadimplência, **então** cada projeto (com link para a COM-05) mostra o vencido por faixa e o cliente; títulos sem projeto ficam em "Sem projeto"; os nomes de projeto vêm de `IProjects.FindManyAsync` por página.

**Pontualidade (R-TER-4)**
2. **Dado** um cliente com títulos quitados nos últimos 12 meses, **quando** o indicador é calculado, **então** o atraso médio é a média, ponderada pelo valor, de (data da baixa que quitou − vencimento), com antecipação contando como 0, e o "% pago com atraso" é a fração do valor quitado depois do vencimento; títulos `Opening` e quitações com desconto total (`DiscountWriteOff` sem principal recebido) não entram; sem título quitado, o indicador mostra "Sem histórico" e expõe a média geral do tenant para a FIN-07.
3. **Dado** o indicador, **quando** outro módulo precisa dele (FIN-07, Fase 4), **então** usa `ICustomerPaymentStats` de `Finance.Contracts` (`avgDelayDays`, `pctPaidLate`, `pctOverdueNow`, `last12mInvoiced`, `hasHistory`); a view `customer_payment_stats` é recalculada na consulta (sem materializar no MVP).
4. **Dado** a visão "Por cliente" da FIN-06a, **quando** abro depois desta story, **então** cada cliente mostra também a pontualidade (atraso médio e % pago com atraso).

**Terceiro: aba "Comercial e financeiro" (R-TER-3)**
5. **Dado** um terceiro, **quando** abro a aba "Comercial e financeiro" no cadastro (CAD-01a), **então** a aba mostra blocos montados pelo front, cada um com o endpoint e a permissão do seu módulo: Orçamentos (`quotes.view`: número, revisão, situação, preço, data), Contratos e projetos (`projects.view`), Títulos a receber e a pagar (`finance.view`: aberto, vencido, pago nos últimos 12 meses) e Pontualidade (`finance.view`); um bloco sem permissão não aparece (nem a chamada é feita) e a aba só aparece se ao menos um bloco for visível; custo e margem nunca aparecem aqui.
6. **[Segurança]** **Dado** um usuário com `parties.view` e sem `finance.view`, **quando** chama o endpoint de títulos ou de pontualidade por terceiro, **então** 403 `forbidden`; o Parties não lê tabela de outro módulo (os blocos são endpoints dos módulos donos, filtrados por `partyId`).
7. **[Segurança]** **Dado** um cliente pessoa física, **quando** a pontualidade dele sai pela API, **então** exige `finance.view` e a resposta leva `Cache-Control: no-store`.

**Permissões**
8. **Dado** o módulo `finance` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo ou sem `finance.view`, 403 `forbidden`. `RequireModule` e `perm:`; SQL só com parâmetros e ordenação por allowlist; desempenho: aging por projeto e pontualidade com 50 mil títulos em menos de 1 s.

## Regras de negócio e técnicas
- Leitura no próprio Finance (Dapper pela conexão do `TenantDbContext`), sem tabela nova; "hoje" pelo `BusinessTime.Today`; dinheiro `numeric(18,2)`.
- Endpoints por terceiro: `GET /api/v1/quotes?partyId=`, `GET /api/v1/projects?partyId=`, `GET /api/v1/finance/receivables?partyId=` e `/payables?partyId=` (filtros), `GET /api/v1/finance/parties/{id}/payment-stats`.

## Permissões, eventos e tabelas
- **Permissões:** `finance.view` (aging por projeto, pontualidade, títulos por terceiro), `quotes.view`, `projects.view`, `parties.view` (aba no terceiro).
- **Eventos:** nenhum novo. **Contrato:** `ICustomerPaymentStats` em `Finance.Contracts`.
- **Tabelas:** view `finance.customer_payment_stats` (como no 03, + `pct_paid_late`, sem os títulos `Opening`).

## Telas e estados
- **Financeiro → Inadimplência**: aba "Por projeto" (`?aba=projetos`) e a coluna de pontualidade em "Por cliente".
- **Terceiro → aba "Comercial e financeiro"** (`/app/cadastros/terceiros/$id?aba=comercial-financeiro`), com skeleton por bloco; o bloco sem permissão some.
- **Estados:** carregando; vazio por bloco ("Sem orçamentos para este terceiro."); erro por bloco com "Tentar de novo".
- **Propositivo:** cada bloco leva à lista do módulo filtrada pelo terceiro.
- **400 px:** blocos empilhados; tabelas com número, situação e valor sempre visíveis.
- **Ajuda:** `Ajuda/financeiro-inadimplencia.md` ganha a aba `projetos`; `Ajuda/cadastros-terceiro-comercial-financeiro.md`; `<Dica>` em "Atraso médio" e "% pago com atraso".

## Plano de testes
- **Unidade:** atraso médio ponderado, sem histórico e sem `Opening` (CA 2).
- **Integração (Testcontainers):** aging por projeto com dataset fixo (CA 1); `ICustomerPaymentStats` (CA 3); blocos do terceiro por permissão, 403 sem `finance.view` e `no-store` para PF (CA 5–7); três camadas e desempenho (CA 8).
- **E2E (Playwright):** abrir a inadimplência por projeto, ir ao cliente e ver a aba do terceiro.

## Fora do escopo
- Cenário ajustado do fluxo de caixa: FIN-07. Régua de cobrança: FIN-09.
- Compras do terceiro no histórico (R-TER-3): entram com a CMP-03 (Fase 3), como mais um bloco da aba.

## Pontos em aberto
Decididos pelo PO em 20/10/2026 (seção 1.1, item 14): atraso médio ponderado pelo valor, antecipação = 0, últimos 12 meses, sem os títulos `Opening`. P-18 decidida (ADR 0021).

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers, dataset fixo) e E2E passando.
- [ ] View criada por migration (não destrutiva).
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Telas com estados.
- [ ] Ajuda escrita (inclusive a nova aba do terceiro); teste de cobertura passando.
- [ ] Próximos passos e links acionáveis aplicados.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `PaymentStatsQuery` (Dapper) e `ICustomerPaymentStats`; aging por projeto.
- [ ] Filtros por `partyId` nos endpoints de orçamentos, projetos e títulos; aba no terceiro; ajuda.

## Quality gates
- Agentes previstos: @dev, @qa.
- Gate: CA 2, 6 e 7 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 20/10/2026.

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

- **Correções feitas na validação (20/10/2026):** quebra da FIN-06 (CA 3, 6, 7, 8 e 9 da original vieram para cá); títulos `Opening` fora da pontualidade; pontualidade de PF com `no-store`; nomes de projeto em lote.
- **Pré-condições para `Ready`:** nenhuma pendente.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 19/10/2026 | 0.1 | Story detalhada (como FIN-06) | River (SM) |
| 19/10/2026 | 0.2 | Validação do PO (checklist) e correções (como FIN-06) | Pax (PO) |
| 20/10/2026 | 0.3 | Quebra da FIN-06 em 06a e 06b (esta); ajustes da revisão pré-Fase 2 | River (SM) |
| 20/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
