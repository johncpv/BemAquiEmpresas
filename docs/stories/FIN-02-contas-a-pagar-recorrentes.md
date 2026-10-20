# FIN-02 — Contas a pagar e despesas recorrentes

| Campo | Valor |
|---|---|
| ID | FIN-02 |
| Título | Títulos a pagar manuais e parcelados, com origem, projeto e campos de retenção; contrato para outros módulos criarem títulos (instalador, compra, folha); despesas recorrentes geradas por job sem duplicar |
| Tamanho | M |
| Status | Draft |
| Requisitos | R-FIN-4 (CP: compras, folha, instaladores, recorrentes, manual) · R-FIN-6 (despesas recorrentes por regra) · R-FIN-1 · R-COM-4 (numeração CP) · R-PRP-1 · R-SEC-5 |
| Dependências | FIN-01 (molde de título, provedores de uso, parcelamento), CAD-05a, CAD-05b, CAD-01a, CAD-06; Fase 0 (FND-07 `TenantJob`/`GlobalRecurringJob`, FND-09c, FND-14) |
| Bloqueia | FIN-03a (OP agrupa títulos a pagar), FIN-05, COM-06 (CP do instalador), CMP-03/CMP-04 (previsão e confirmação do CP), RH-02 (CP de salários), PAT-03 |
| Ordem de execução | 2 (Marco 2.1 — caixa e pagamentos) |

## User story
Como **financeiro**, quero **lançar as contas a pagar, inclusive as fixas que se repetem todo mês, já com fornecedor, conta e projeto**, para **ver tudo o que vai sair do caixa e preparar as ordens de pagamento sem esquecer nenhuma despesa**.

## Contexto e referências
- PRD R-FIN-4, R-FIN-6; `04-fluxos-estados` §1 "Título a pagar e Ordem de Pagamento" (`Planned → Open → AwaitingApproval → Approved → Paid`) e a regra dura "nenhum pagamento é baixado sem OP aprovada".
- `03-modelo-dados` → `finance.payables`, `finance.recurring_expenses`.
- Revisão pré-Fase 0 §2.1: "o status do Payable passa a ser derivado da OP" (aplicado na FIN-03a).
- P-15 (decidida): retenções ISS/INSS/IR sobre serviço tomado são **opcionais**; o título tem campos de retenção, preenchidos quando aplicável, **sem cálculo automático** no MVP.
- ADR 0006 (job recorrente global que enfileira por tenant, `TenantJob<T>`, fuso America/Sao_Paulo).
- Padrões comuns da Fase 2 (`docs/stories/README.md`).

## Critérios de aceite
**Título manual**
1. **Dado** `finance.create`, **quando** abro "Novo título a pagar", informo fornecedor, descrição, conta de despesa, valor e vencimento e clico "Salvar título a pagar", **então** o título é gravado `Open`, origem `Manual`, número `CP-2026-00001`, o toast diz "Título a pagar salvo" e a faixa "Próximos passos" oferece "Incluir em ordem de pagamento" (FIN-03a) e "Novo título para este fornecedor".
2. **Dado** o formulário, **quando** escolho o terceiro, **então** só aparecem terceiros ativos com papel Fornecedor ou Instalador (422 "Escolha um fornecedor ou instalador."); a conta só pode ser analítica ativa do tipo Despesa, Ativo (investimentos, "4.1") ou Passivo (`IChartOfAccounts`); centro de custo e projeto são opcionais; o projeto é validado por `IProjects` de `Commercial.Contracts` quando existir (COM-04a) — antes dela, o campo fica oculto.
3. **Dado** "Parcelar pela condição", **quando** escolho a condição (padrão: a do fornecedor), **então** a prévia e a gravação seguem `IPaymentTerms.SplitAsync`, com o mesmo agrupamento e descrição "(n/N)" da FIN-01 (CA 3).
4. **Dado** os campos de retenção (ISS, INSS, IR, PIS/COFINS/CSLL), **quando** informo valores, **então** são gravados em `numeric(18,2)`, a tela mostra "Valor líquido a pagar = valor − retenções" e a OP (FIN-03a) paga o líquido; não há cálculo automático (P-15) e a soma das retenções maior ou igual ao valor responde 422.
5. **Dado** valor ≤ 0 ou terceiro sem papel, **quando** salvo, **então** 422 com mensagem PT-BR.

**Situação e regra dura**
6. **Dado** um título `Open` com vencimento anterior a hoje, **quando** consulto, **então** aparece "Vencido" (calculado, como na FIN-01 CA 5), sem gravar.
7. **[Segurança]** **Dado** qualquer título a pagar, **quando** tento dar baixa por esta tela ou pela API de títulos, **então** não existe rota de baixa: o pagamento só acontece pela OP aprovada (FIN-03a); um teste de arquitetura garante que só o caso de uso de pagamento da OP cria transação `Payment`.
8. **Dado** `finance.edit` e um título `Open` fora de OP, **quando** clico "Cancelar título", informo o motivo e confirmo "Cancelar título", **então** ele fica `Cancelled`; título em OP (`AwaitingApproval` ou `Approved`) responde 409 "O título está na {OP}. Retire-o da ordem antes."; não há exclusão.
9. **Dado** um título de origem diferente de `Manual` e `Recurring` (ex.: `Installer`, COM-06), **quando** tento mudar valor, fornecedor ou cancelar por esta tela, **então** 409 "Título gerado por {origem} {número}: ajuste na origem."; descrição, conta, centro de custo e vencimento continuam editáveis.

**Contrato para os outros módulos**
10. **Dado** um módulo que gera conta a pagar (Production na COM-06; Purchasing e HR depois), **quando** chama `IPayables.CreateFromOriginAsync(origin, originId, partyId, projectId, amount, dueDate, description, planned)` de `Finance.Contracts`, **então** o título nasce na mesma transação do chamador, com `Planned` (sem vencimento definido, para previsão) ou `Open`, e a mesma chamada repetida com o mesmo `(origin, originId)` devolve o título existente (idempotente, índice único).
11. **Dado** um título `Planned` de origem, **quando** a origem chama `IPayables.ConfirmAsync(origin, originId, amount, dueDate)`, **então** ele passa a `Open` com o valor e o vencimento confirmados; título já em OP responde erro `payable_in_payment_order`.

**Despesas recorrentes**
12. **Dado** `finance.create`, **quando** cadastro uma recorrente com descrição, fornecedor, conta, centro de custo, valor, dia do vencimento (1 a 31), início e fim opcional e clico "Salvar despesa recorrente", **então** ela é gravada e a tela mostra as próximas 3 ocorrências previstas.
13. **Dado** a recorrente "Aluguel" com dia 10 e antecedência de geração de 10 dias (padrão, ver Pontos em aberto 1), **quando** o job diário roda em 30/11/2026, **então** é criado um título `Open`, origem `Recurring`, competência 12/2026, vencimento 10/12/2026, descrição "Aluguel — 12/2026"; rodando de novo no mesmo dia ou em paralelo, **não** cria outro (índice único em `(recurring_expense_id, competence)`).
14. **Dado** o dia 31 numa competência de 30 dias ou fevereiro, **quando** o job gera, **então** o vencimento é o último dia do mês.
15. **Dado** o job parado por 3 dias, **quando** volta, **então** gera as competências que ficaram para trás, uma vez cada, e registra o atraso no log; um tenant suspenso não roda (`TenantJob`).
16. **Dado** uma recorrente com títulos gerados, **quando** tento excluí-la, **então** 409 "A despesa já gerou títulos. Encerre-a informando o fim." e o diálogo oferece "Encerrar"; mudar o valor vale só para as competências ainda não geradas.

**Permissões (3 camadas)**
17. **Dado** o módulo `finance` não liberado, **quando** chamo os endpoints, **então** 403 `module_not_released`; sem grupo que habilite, 403 `forbidden`; com `finance.view` sem `create`/`edit`/`delete`, ações ocultas e escrita com 403. Todo endpoint tem `RequireModule` e `perm:`; IDOR pai/filho conferido; ids de outro módulo validados pelo Contracts do dono.
18. **Dado** qualquer alteração de título ou recorrente, **quando** abro "Histórico de alterações", **então** vejo antes/depois, usuário e data.

## Regras de negócio e técnicas
- **Dinheiro** `numeric(18,2)`; **datas** `date` (vencimento, competência como o primeiro dia do mês); job em America/Sao_Paulo.
- **Situação gravada:** `Planned`, `Open`, `AwaitingApproval`, `Approved`, `PartiallyPaid` (proposta, ver FIN-03a), `Paid`, `Cancelled`. Nesta story só existem `Planned`, `Open` e `Cancelled`; as demais são gravadas pela OP (FIN-03a), nunca pela tela do título.
- **Valor líquido** = `amount − Σ retenções`; a OP paga o líquido.
- **Origem:** `Manual`, `Recurring`, `Installer`, `Purchase`, `Payroll`, `Maintenance` (enum em `Finance.Contracts`); `(origin, origin_id)` único quando `origin_id` não é nulo.
- **Recorrente:** `recurring_expenses` + `lead_days` (proposta) e `is_active`; o job é um `GlobalRecurringJob` diário às 06:00 de São Paulo que enfileira `GenerateRecurringPayablesJob` por tenant ativo.
- **Ids** Guid v7; **auditoria** `IAuditable`; **numeração** `CP`; soft delete só em recorrente sem título.

## Permissões, eventos e tabelas
- **Permissões (já no `ModuleCatalog`):** `finance.view`, `finance.create`, `finance.edit` (editar, cancelar, encerrar recorrente), `finance.delete` (excluir recorrente sem título).
- **Eventos:** nenhum novo nesta story. O contrato `IPayables` é chamada síncrona via `Finance.Contracts` (ADR 0006: compartilha a transação do chamador).
- **Tabelas:** `finance.payables` (como no 03, mais `retention_iss`, `retention_inss`, `retention_ir`, `retention_pcc`, `issue_date`, `recurring_expense_id`, `competence`, `installment_group_id`, `cancel_reason`, `cancelled_at`, `cancelled_by`; `due_date` anulável para `Planned`; `account_id` = conta do plano de contas) — colunas novas são **proposta**; `finance.recurring_expenses` (como no 03, mais `lead_days`, `is_active`, `end` anulável).

## Telas e estados
- **Financeiro → A pagar** (`/app/financeiro/pagar`): h1 "O que a empresa precisa pagar, por vencimento."; DataTable com número (link), fornecedor, descrição, origem (`Tag`), projeto, vencimento, valor líquido, situação (Previsto neutro, Em aberto info, Vence hoje alerta, Vencido crítico, Em OP info, Aprovado ok, Pago ok, Cancelado neutro); seleção múltipla com "Criar ordem de pagamento" (FIN-03a, visível quando ela existir); filtros na URL.
- **Novo título a pagar** (`/app/financeiro/pagar/novo`) e **Título a pagar** (`/app/financeiro/pagar/$id`, abas Dados, Retenções, Histórico de alterações).
- **Financeiro → Recorrentes** (`/app/financeiro/recorrentes`): lista com próxima geração e painel lateral de edição.
- **Estados:** carregando; vazio ("Lance a primeira conta a pagar ou cadastre as despesas fixas do mês." + "Novo título a pagar" e "Nova despesa recorrente"); vazio filtrado; erro; modo leitura.
- **Propositivo:** condição padrão do fornecedor, conta e centro de custo do último título do mesmo fornecedor (R-PRP-1); "Duplicar" título; depois de cadastrar a recorrente, mostra as próximas ocorrências; título vencido oferece "Incluir em ordem de pagamento".
- **Ajuda:** `Ajuda/financeiro-pagar.md` (`padrao: true`), `Ajuda/financeiro-pagar-novo.md`, `Ajuda/financeiro-pagar-titulo.md` (abas `dados`, `retencoes`, `historico`), `Ajuda/financeiro-recorrentes.md`; `<Dica>` em "Retenções" ("Preencha quando a nota do prestador destacar retenção. O sistema não calcula.") e "Antecedência de geração".

## Plano de testes
- **Unidade:** valor líquido e retenções (CA 4); dia 31 e fevereiro (CA 14); competências atrasadas (CA 15).
- **Integração (Testcontainers):** criar e parcelar (CA 1–3); cancelar dentro e fora de OP, com OP simulada (CA 8); título de origem protegido (CA 9); `IPayables` idempotente e na transação do chamador, com rollback do chamador desfazendo o título (CA 10, 11); job sem duplicar, inclusive com duas execuções paralelas (CA 13); tenant suspenso (CA 15); recorrente com títulos não excluída (CA 16); três camadas (CA 17); auditoria (CA 18).
- **Arquitetura:** só a OP cria transação `Payment` (CA 7); job herda de `TenantJob`; Finance não referencia módulos fora dos Contracts.
- **E2E (Playwright):** cadastrar recorrente e título manual com retenção.

## Fora do escopo
- Ordem de pagamento, aprovação e pagamento: FIN-03a. Alçadas: FIN-03b.
- CP do instalador a partir da OS de instalação: COM-06 (usa o `IPayables` desta story). CP de compras: CMP-03/CMP-04. CP de folha: RH-02.
- Cálculo automático de retenções (P-15).

## Pontos em aberto
1. **Quando o job gera o título recorrente.** O backlog diz "gera título no dia configurado". **Recomendação:** gerar com antecedência (`lead_days`, padrão 10 dias antes do vencimento), para o título passar pela OP antes de vencer; as ocorrências ainda não geradas entram no fluxo de caixa como previsão (FIN-05).
2. **Retenção PIS/COFINS/CSLL.** A P-15 cita ISS, INSS e IR. **Recomendação:** incluir também o campo PCC (soma das três), opcional, porque a nota de serviço PJ costuma destacá-lo; confirmar com a contadora.
3. **P-18:** ver FIN-01, ponto 5.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers), arquitetura e E2E passando.
- [ ] Migration criada (não destrutiva).
- [ ] Permissões só do `ModuleCatalog`; job registrado como recorrente global.
- [ ] Telas com estados de carregando, vazio, vazio filtrado e erro; modo leitura.
- [ ] Ajuda das telas e abas; teste de cobertura passando.
- [ ] Padrões comuns da Fase 2 e próximos passos aplicados.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Entidades `Payable` e `RecurringExpense`; enum `PayableOrigin` em `Finance.Contracts`.
- [ ] `IPayables` (`CreateFromOriginAsync`, `ConfirmAsync`, `ByProjectAsync`) em `Finance.Contracts`.
- [ ] `GenerateRecurringPayablesJob` (`TenantJob`) e o recorrente global.
- [ ] Endpoints `/api/v1/finance/payables` e `/api/v1/finance/recurring-expenses`; telas e ajuda.

## Quality gates
- Agentes previstos: @dev, @qa.
- Gate: CA 7 (sem baixa fora da OP), CA 10 (idempotência) e CA 13 (sem duplicar) obrigatórios.

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

- **Correções feitas na validação (19/10/2026):** campo de projeto oculto até a COM-04a (sem dependência circular); `IPayables` idempotente por origem (CA 10) para a COM-06 e a Fase 3; regra dura "sem baixa fora da OP" virou CA com teste de arquitetura (CA 7); P-15 aplicada (retenção sem cálculo); conta de Ativo aceita, porque compra de imobilizado também é paga por OP (o seed da CAD-05a tem "4.1 Investimentos e imobilizado").
- **Pré-condições para `Ready`:** Pontos em aberto 1 e 2 (com padrão recomendado) e P-18.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 19/10/2026 | 0.1 | Story detalhada | River (SM) |
| 19/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
