# FIN-06 — Inadimplência, aging, pontualidade e histórico do terceiro

| Campo | Valor |
|---|---|
| ID | FIN-06 |
| Título | Títulos vencidos por cliente e por projeto com aging (0–30, 31–60, 61–90, 90+), aviso diário de título vencido, indicador de pontualidade do cliente e aba "Comercial e financeiro" no terceiro |
| Tamanho | M (re-estimada de P: recebeu R-TER-3 e R-TER-4, que a revisão pré-Fase 1 §2.2 mandou para a Fase 2) |
| Status | Draft |
| Requisitos | R-FIN-12 (vencidos por cliente e projeto, aging) · R-TER-4 (pontualidade: média de dias de atraso) · R-TER-3 (histórico do terceiro: orçamentos, contratos, títulos, pontualidade) · R-COM-2 (notificação de título vencido) · R-PRP-5 |
| Dependências | FIN-01 (títulos, baixas, `ReceivableSettled`), COM-04b (títulos de projeto), FIN-02; COM-01a e COM-04a (blocos de orçamentos e contratos no terceiro) |
| Bloqueia | FIN-07 (cenário ajustado usa a pontualidade), FIN-09 (régua), DSH-01 (card de inadimplência) |
| Ordem de execução | 15 (Marco 2.4 — controles) |

## User story
Como **financeiro**, quero **ver quem está devendo, há quanto tempo e em qual projeto, e saber se cada cliente costuma pagar em dia**, para **cobrar primeiro quem pesa mais e negociar com quem atrasa**.

## Contexto e referências
- PRD R-FIN-12, R-TER-3, R-TER-4; §1.1 problema 2 ("nem visão de quem atrasa pagamento").
- `03-modelo-dados` → `customer_payment_stats` (view: `party_id`, `avg_delay_days`, `pct_overdue`, `last_12m_invoiced`).
- `04-fluxos-estados` §3.5 (o Ajustado usa a média de atraso dos últimos 12 meses; sem histórico, a média geral do tenant): esta story calcula; a FIN-07 usa.
- ADR 0017: sem histórico do Dolibarr; a pontualidade começa com os títulos do BemAqui.
- Revisão pré-Fase 1 §2.2: histórico comercial e financeiro do terceiro (R-TER-3) na Fase 2.
- Protótipo: painel "A receber vencido" (cliente, título, dias, valor).

## Critérios de aceite
**Aging**
1. **Dado** o dataset fixo (títulos vencidos há 5, 35, 70 e 120 dias para 3 clientes, um com projeto), **quando** abro "Inadimplência" hoje, **então** vejo o total vencido, a quantidade de títulos e de clientes e as faixas 0–30, 31–60, 61–90 e 90+ (dias de atraso = hoje − vencimento) com o saldo em aberto de cada uma, batendo com o esperado.
2. **Dado** a visão "Por cliente", **quando** abro, **então** cada cliente mostra o total vencido por faixa, o maior atraso e a pontualidade (CA 6), ordenado do maior saldo vencido para o menor; clicar abre os títulos do cliente.
3. **Dado** a visão "Por projeto", **quando** abro, **então** cada projeto (com link para a COM-05) mostra o vencido por faixa; títulos sem projeto ficam em "Sem projeto".
4. **Dado** um título `PartiallyPaid` vencido, **quando** entra no aging, **então** conta pelo saldo em aberto; títulos `Planned` (sem vencimento) e `Cancelled` não entram.

**Aviso diário**
5. **Dado** títulos a receber que venceram ontem e continuam em aberto, **quando** o job diário roda às 08:00 de São Paulo, **então** cada usuário com `finance.edit` recebe uma notificação consolidada "{n} títulos a receber venceram ontem ({total})." com link para a lista filtrada; sem títulos, nada é enviado; o job não repete o aviso do mesmo dia.

**Pontualidade (R-TER-4)**
6. **Dado** um cliente com títulos quitados nos últimos 12 meses, **quando** o indicador é calculado, **então** o atraso médio é a média, ponderada pelo valor, de (data da baixa que quitou − vencimento), com antecipação contando como 0, e o "% pago com atraso" é a fração do valor quitado depois do vencimento; sem título quitado, o indicador mostra "Sem histórico" e expõe a média geral do tenant para a FIN-07.
7. **Dado** o indicador, **quando** outro módulo precisa dele (FIN-07, Fase 4), **então** usa `ICustomerPaymentStats` de `Finance.Contracts` (`avgDelayDays`, `pctPaidLate`, `pctOverdueNow`, `last12mInvoiced`, `hasHistory`); a view `customer_payment_stats` é recalculada na consulta (sem materializar no MVP).

**Terceiro: aba "Comercial e financeiro" (R-TER-3)**
8. **Dado** um terceiro, **quando** abro a aba "Comercial e financeiro" no cadastro (CAD-01a), **então** a aba mostra blocos montados pelo front, cada um com o endpoint e a permissão do seu módulo: Orçamentos (`quotes.view`: número, revisão, situação, preço, data), Contratos e projetos (`projects.view`), Títulos a receber e a pagar (`finance.view`: aberto, vencido, pago nos últimos 12 meses) e Pontualidade (`finance.view`); um bloco sem permissão não aparece (nem a chamada é feita) e a aba só aparece se ao menos um bloco for visível.
9. **[Segurança]** **Dado** um usuário com `parties.view` e sem `finance.view`, **quando** chama o endpoint de títulos por terceiro, **então** 403 `forbidden`; o Parties não lê tabela de outro módulo (os blocos são endpoints dos módulos donos, filtrados por `partyId`).

**Permissões**
10. **Dado** o módulo `finance` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo ou sem `finance.view`, 403 `forbidden`. `RequireModule` e `perm:`; desempenho: aging com 50 mil títulos em menos de 1 s.

## Regras de negócio e técnicas
- Leitura no próprio Finance (Dapper pela conexão do `TenantDbContext`), sem tabela nova; "hoje" pelo `BusinessTime.Today`; dinheiro `numeric(18,2)`.
- **Faixas** por dias de atraso: 1–30 (rótulo "0–30", como no PRD), 31–60, 61–90, 91+.
- **Job** `GlobalRecurringJob` diário que enfileira `OverdueReceivablesNoticeJob` por tenant; deduplicação por dia.
- Endpoints novos por terceiro: `GET /api/v1/quotes?partyId=`, `GET /api/v1/projects?partyId=`, `GET /api/v1/finance/receivables?partyId=` e `/payables?partyId=` (filtros), `GET /api/v1/finance/parties/{id}/payment-stats`.

## Permissões, eventos e tabelas
- **Permissões:** `finance.view` (aging, pontualidade, títulos por terceiro), `finance.edit` (destinatários do aviso), `quotes.view`, `projects.view`, `parties.view` (aba no terceiro).
- **Eventos:** nenhum novo. **Contrato:** `ICustomerPaymentStats` em `Finance.Contracts`.
- **Tabelas:** view `finance.customer_payment_stats` (como no 03, + `pct_paid_late`).

## Telas e estados
- **Financeiro → Inadimplência** (`/app/financeiro/inadimplencia`): cards por faixa (cor semântica com texto), abas na URL "Por cliente" e "Por projeto"; cada linha leva aos títulos.
- **Terceiro → aba "Comercial e financeiro"** (`/app/cadastros/terceiros/$id?aba=comercial-financeiro`).
- **Estados:** carregando; vazio ("Nenhum título vencido. Bom sinal."); erro.
- **Propositivo:** o aviso leva à lista dos vencidos de ontem; cada cliente oferece "Ver títulos" e, a partir da BNK-01, "Enviar boleto".
- **Ajuda:** `Ajuda/financeiro-inadimplencia.md` (abas `clientes` com `padrao: true`, `projetos`) e `Ajuda/cadastros-terceiro-comercial-financeiro.md`; `<Dica>` em "Aging", "Atraso médio" e "% pago com atraso".

## Plano de testes
- **Unidade:** faixas e dias de atraso (CA 1, 4); atraso médio ponderado e sem histórico (CA 6).
- **Integração (Testcontainers):** dataset fixo de aging por cliente e projeto (CA 1–4); job com e sem vencidos, sem repetir (CA 5); `ICustomerPaymentStats` (CA 7); blocos do terceiro por permissão e 403 sem `finance.view` (CA 8, 9); três camadas e desempenho (CA 10).
- **E2E (Playwright):** abrir inadimplência, ir ao cliente, ver a aba do terceiro.

## Fora do escopo
- Cenário ajustado do fluxo de caixa: FIN-07. Régua de cobrança por e-mail: FIN-09. Boleto: BNK-01.
- Compras do terceiro no histórico (R-TER-3): entram com a CMP-03 (Fase 3), como mais um bloco da aba.

## Pontos em aberto
1. **Fórmula do atraso médio.** O PRD diz "média de dias de atraso". **Recomendação:** média ponderada pelo valor, antecipação = 0, últimos 12 meses (CA 6).
2. **Destinatários do aviso diário.** **Recomendação:** quem tem `finance.edit` (o Financeiro do seed), um aviso consolidado por dia.
3. **P-18:** ver FIN-01, ponto 5.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers, dataset fixo) e E2E passando.
- [ ] View criada por migration (não destrutiva).
- [ ] Permissões só do `ModuleCatalog`; job registrado.
- [ ] Telas com estados.
- [ ] Ajuda escrita (inclusive a nova aba do terceiro); teste de cobertura passando.
- [ ] Alerta acionável aplicado.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `AgingQuery` e `PaymentStatsQuery` (Dapper); `ICustomerPaymentStats`.
- [ ] `OverdueReceivablesNoticeJob`.
- [ ] Filtros por `partyId` nos endpoints de orçamentos, projetos e títulos; aba no terceiro; ajuda.

## Quality gates
- Agentes previstos: @dev, @qa.
- Gate: CA 1, 6 e 9 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 19/10/2026.

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

- **Correções feitas na validação (19/10/2026):** re-estimada de P para M; R-TER-3 resolvido por composição no front, sem o Parties ler outros módulos (CA 8, 9); pontualidade exposta por contrato para a FIN-07; aviso de título vencido (R-COM-2) que nenhuma outra story cobria.
- **Pré-condições para `Ready`:** Pontos em aberto 1 e 2 (com padrão recomendado) e P-18.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 19/10/2026 | 0.1 | Story detalhada | River (SM) |
| 19/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
