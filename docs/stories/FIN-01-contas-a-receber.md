# FIN-01 — Contas a receber: títulos, parcelas, baixa total ou parcial, juros, multa, desconto e estorno

| Campo | Valor |
|---|---|
| ID | FIN-01 |
| Título | Títulos a receber manuais e parcelados pela condição de pagamento, baixa total ou parcial com juros, multa e desconto, transação na conta bancária, estorno de baixa, cancelamento com motivo e situação "Vencido" calculada |
| Tamanho | G (re-estimada de M: esta story cria também `financial_transactions` e as baixas, base da FIN-03a, FIN-04 e FIN-05) |
| Status | Draft |
| Requisitos | R-FIN-3 (CR: manual, parcelas, juros/multa, descontos, baixa parcial) · R-FIN-1 (plano de contas, centro de custo e projeto em todo lançamento) · R-FIN-2 (movimento na conta) · R-COM-4 (numeração CR) · R-PRP-1 · R-PRP-7 · R-SEC-5 |
| Dependências | CAD-05a (`IChartOfAccounts`, centros de custo), CAD-05b (contas bancárias, `IPaymentTerms.SplitAsync`, `IBankAccountUsageProvider`), CAD-01a (`IParties`), CAD-06 (numeração), FND-09d; Fase 0 (FND-07, FND-09c, FND-14) |
| Bloqueia | FIN-02 (mesmo molde de título), FIN-03a (transação de pagamento), FIN-04, FIN-05, FIN-06, COM-04b (recebíveis do contrato), BNK-01, BNK-02 |
| Ordem de execução | 1 (Marco 2.1 — caixa e pagamentos) |

## User story
Como **financeiro**, quero **lançar o que a empresa tem a receber, parcelar pela condição de pagamento e dar baixa do que entrou, inteiro ou em parte, com juros, multa e desconto**, para **saber o que falta receber de cada cliente e ter o saldo das contas batendo com o banco**.

## Contexto e referências
- PRD R-FIN-3, R-FIN-1, R-FIN-2; `04-fluxos-estados` §1 "Título a receber" (`Planned → Open → PartiallyPaid → Paid`, `Overdue` calculado, `Cancelled`).
- `03-modelo-dados` → `finance.receivables`, `finance.financial_transactions`; `bank_accounts`, `payment_terms`, `chart_of_accounts`, `cost_centers` (CAD-05a/05b).
- Revisão pré-Fase 0 §2.1 (`Overdue` calculado, não gravado); revisão pré-Fase 1 §3 (invariante `paid_amount = Σ baixas`, definido nesta story).
- CAD-05b CA 5 (saldo inicial travado depois da primeira transação, por `IBankAccountUsageProvider`) e CA 13 (vencimento em dia não útil "revisto na FIN-01").
- CAD-05a CA 15 e 16 (`IAccountUsageProvider`, `ICostCenterUsageProvider`): esta story implementa os provedores para títulos e transações.
- `ModuleCatalog`: `finance` com `view`, `create`, `edit`, `approve`, `delete` e `pay`. O grupo Financeiro do seed tem `view`, `create` e `edit` (ADR 0010 item 3).
- Padrões comuns da Fase 2 (`docs/stories/README.md`, "Padrões comuns a todos os documentos").

## Critérios de aceite
**Título manual e parcelas**
1. **Dado** `finance.create`, **quando** abro "Novo título a receber" e informo cliente, descrição, conta de receita, valor, emissão e vencimento e clico "Salvar título a receber", **então** o título é gravado `Open` com número `CR-2026-00001` (`INumberSequence`, tipo `CR`), o toast diz "Título a receber salvo" e o detalhe abre com a faixa "Próximos passos" ("Dar baixa", "Novo título para este cliente").
2. **Dado** o cliente escolhido, **quando** o formulário é preenchido, **então** o cliente só pode ser terceiro ativo com o papel Cliente (`IParties.HasRoleAsync`; senão 422 "Escolha um terceiro com o papel Cliente."), a conta só pode ser analítica ativa do tipo Receita (`IChartOfAccounts.PostableAsync(Revenue)`), o centro de custo é opcional, a emissão vem com hoje (R-PRP-1) e o projeto é opcional e validado por `IProjects` de `Commercial.Contracts` — antes da COM-04a o campo fica oculto (sem dependência circular, como na FIN-02 CA 2).
3. **Dado** "Parcelar pela condição", **quando** escolho a condição (padrão: a condição padrão do cliente, `PartySummary.DefaultPaymentTermId`), o valor R$ 1.000,00 e a data base 10/11/2026 com a condição "30/60/90 dias", **então** a prévia mostra 3 parcelas de R$ 333,33, R$ 333,33 e R$ 333,34 com vencimentos 10/12/2026, 09/01/2027 e 08/02/2027, idêntica a `IPaymentTerms.SplitAsync`, e "Salvar títulos a receber" grava 3 títulos com números seguidos, descrição "{descrição} (1/3)", "(2/3)", "(3/3)" e o mesmo `installment_group_id`.
4. **Dado** valor ≤ 0, vencimento anterior à emissão ou conta agrupadora, **quando** salvo, **então** a API responde 422 com a mensagem PT-BR do campo ("Informe um valor maior que zero.", "O vencimento não pode ser antes da emissão.", "Escolha uma conta analítica.").

**Situação calculada**
5. **Dado** um título `Open` ou `PartiallyPaid` com vencimento anterior a hoje (data de São Paulo, `BusinessTime.Today`), **quando** aparece na lista, no detalhe ou na API, **então** a situação exibida é "Vencido" (chip crítico) e o campo `isOverdue` é `true`, sem gravar `Overdue` na tabela; o filtro "Vencidos" usa a mesma regra no servidor.
6. **Dado** um título com vencimento hoje, **quando** consulto, **então** ele aparece como "Vence hoje" (chip alerta), não como vencido.

**Baixa**
7. **Dado** `finance.edit` e um título `Open` de R$ 1.000,00, **quando** clico "Dar baixa", informo a conta bancária (padrão: a conta padrão ativa), a data do recebimento (padrão: hoje), o valor recebido de principal R$ 400,00, juros R$ 5,00, multa R$ 20,00 e desconto R$ 0,00, e confirmo "Dar baixa", **então** na mesma transação: grava uma baixa (`receivable_settlements`), cria uma `financial_transaction` do tipo `Receipt` de +R$ 425,00 na conta e na data informadas, o título fica `PartiallyPaid` com `paid_amount` = R$ 400,00 e saldo R$ 600,00, e o toast diz "Baixa registrada".
8. **Dado** o diálogo de baixa, **quando** abre, **então** mostra o saldo em aberto, o total que vai entrar na conta (principal + juros + multa − desconto) atualizado ao vivo em `aria-live`, a conta e a data, e o botão repete o verbo "Dar baixa" (R-PRP-7).
9. **Dado** uma baixa de principal igual ao saldo, ou "Baixar o saldo com desconto" (o desconto cobre o saldo restante), **quando** confirmo, **então** o título fica `Paid`; principal maior que o saldo, desconto maior que o principal da baixa, valor negativo, conta inativa ou data futura respondem 422 com mensagem PT-BR.
10. **Dado** qualquer título, **quando** consulto, **então** vale o invariante `paid_amount = Σ principal das baixas não estornadas` e `interest`, `fine` e `discount` do título são a soma das baixas; um teste de propriedade confere o invariante depois de baixas e estornos aleatórios.
11. **[Segurança]** **Dado** duas baixas enviadas ao mesmo tempo para o mesmo título, **quando** a soma passaria do saldo, **então** só a primeira grava e a segunda recebe 409 "O título mudou desde que você abriu. Recarregue." (concorrência otimista por `xmin`).

**Estorno e cancelamento**
12. **Dado** `finance.edit` e uma baixa, **quando** clico "Estornar baixa", informo o motivo (obrigatório, até 200 caracteres) e confirmo "Estornar baixa", **então** a baixa fica marcada como estornada (nada é apagado), uma `financial_transaction` inversa (−R$ 425,00, mesma conta, data do estorno, ligada à original) é criada, a situação do título é recalculada e os membros do Proprietário são notificados pelo sino ("Baixa de {número} estornada por {usuário}"); o toast diz "Baixa estornada".
13. **Dado** `finance.edit` e um título sem baixa ativa, **quando** clico "Cancelar título", informo o motivo e confirmo "Cancelar título", **então** ele fica `Cancelled` com motivo, usuário e data, some dos totais em aberto e continua consultável pelo filtro "Cancelados"; com baixa ativa, a API responde 409 "Estorne as baixas antes de cancelar." e o diálogo oferece "Estornar baixa".
14. **Dado** um título de origem `Contract` (COM-04b), **quando** tento cancelar ou mudar valor e vencimento por esta tela, **então** a API responde 409 "Título do contrato {número}: ajuste pelo contrato ou pelo aditivo." (proposta, ver Pontos em aberto 4); descrição, conta, centro de custo e projeto continuam editáveis.
15. **Dado** qualquer título ou transação, **quando** tento excluir pela API, **então** não existe rota de exclusão (R-FIN-3: título nunca é apagado).

**Uso por outros cadastros**
16. **Dado** uma conta bancária com transação, **quando** a CAD-05b pergunta `IBankAccountUsageProvider.HasMovementAsync`, **então** a resposta é `true` (saldo inicial travado e conta só inativa); idem `IAccountUsageProvider`, `ICostCenterUsageProvider` e `IPartyUsageProvider` ("títulos a receber") para conta, centro e terceiro usados em título.
17. **Dado** uma transação com data anterior ao `opening_date` da conta, **quando** salvo a baixa, **então** a API responde 422 "A data é anterior ao saldo inicial da conta ({data})."

**Permissões (3 camadas) e segurança**
18. **Dado** o módulo `finance` não liberado, **quando** chamo os endpoints, **então** 403 `module_not_released` e o front mostra `ModuleNotReleased`; sem grupo que habilite `finance`, 403 `forbidden`; com `finance.view` sem `create`/`edit`, as ações ficam ocultas por `<Can>` e a escrita responde 403. Todo endpoint tem `RequireModule` e `perm:` (teste de arquitetura).
19. **[Segurança]** **Dado** uma rota `/receivables/{id}/settlements/{settlementId}`, **quando** a baixa não pertence ao título, **então** 404 (IDOR pai/filho); projeto, terceiro, conta e centro informados são validados pelo Contracts do dono (id inexistente → 422).
20. **Dado** qualquer alteração de título ou baixa, **quando** abro "Histórico de alterações", **então** vejo antes/depois, usuário e data; baixas e estornos aparecem também na aba "Baixas" com usuário e data.

## Regras de negócio e técnicas
- **Dinheiro** `numeric(18,2)` (`amount`, `paid_amount`, `interest`, `fine`, `discount`, valores da baixa e da transação), arredondamento `AwayFromZero`. **Datas de negócio** `date` (`issue_date`, `due_date`, data da baixa, data da transação); instantes em `timestamptz`.
- **Saldo do título** = `amount − paid_amount`. **Situação gravada:** `Planned`, `Open`, `PartiallyPaid`, `Paid`, `Cancelled`. `Planned` (só para marco por evento, COM-04b) tem `due_date` nulo. "Vencido" é calculado (CA 5).
- **Baixa** (`receivable_settlements`, proposta): `receivable_id`, `bank_account_id`, `date`, `principal`, `interest`, `fine`, `discount`, `total` (= principal + interest + fine − discount, > 0), `transaction_id`, `reversed_at`, `reversed_by`, `reversal_reason`, `reversal_transaction_id`. Imutável, exceto as colunas de estorno.
- **Transação** (`financial_transactions`): `amount` com sinal (+ entrada), `type` (`Receipt` aqui; `Payment` na FIN-03a; `Transfer` na FIN-04), `reverses_transaction_id` (proposta), imutável. O saldo da conta (FIN-04) é `opening_balance + Σ amount`.
- **Juros e multa** são valores informados na baixa, sem cálculo automático (ver Pontos em aberto 2).
- **Vencimento em dia não útil:** não é ajustado (CAD-05b), e "Vencido" vale a partir do dia seguinte ao vencimento (ver Pontos em aberto 3).
- **Parcelamento:** `installment_group_id` (proposta) agrupa as parcelas; cada parcela é um título independente (baixa, cancelamento).
- **Origem** (proposta): `origin` (`Manual` aqui; `Contract` na COM-04b) e `origin_id`.
- **Ids** Guid v7; **auditoria** `IAuditable` em título e baixa; **numeração** `CR` na mesma transação; índice único em `number`.

## Permissões, eventos e tabelas
- **Permissões (já no `ModuleCatalog`):** `finance.view` (lista, detalhe), `finance.create` (novo título), `finance.edit` (editar, baixa, estorno de baixa, cancelamento — ver Pontos em aberto 1).
- **Eventos:** `ReceivableSettled` (`{ receivableId, partyId, projectId, dueDate, settledOn, principal }`, sem dado pessoal) publicado no outbox a cada baixa e estorno, sem consumidor nesta fase (alimenta a pontualidade na FIN-06 e o realizado no DSH-02).
- **Tabelas:** `finance.receivables` (como no 03, mais `origin`, `origin_id`, `installment_group_id`, `cancel_reason`, `cancelled_at`, `cancelled_by`; `account_id` é a conta do **plano de contas**; `due_date` anulável; sem `status = Overdue`, sem `billing_slip_id` até a BNK-01); `finance.receivable_settlements` (**proposta**); `finance.financial_transactions` (como no 03, mais `reverses_transaction_id`, `settlement_id`, `created_at`, `created_by`; `reconciled` e `statement_line_id` ficam nulos até a BNK-03).

## Telas e estados
- **Financeiro → A receber** (`/app/financeiro/receber`): h1 com a linha "O que os clientes devem à empresa, por vencimento."; DataTable com número (link), cliente, descrição, projeto, vencimento, valor, saldo (`Money`, `tabular-nums`) e situação (chips: Previsto neutro, Em aberto info, Vence hoje alerta, Vencido crítico, Parcial info, Pago ok, Cancelado neutro); filtros na URL por situação, cliente, projeto, período de vencimento; totais do filtro no rodapé (em aberto, vencido).
- **Novo título a receber** (`/app/financeiro/receber/novo`): página em seções (Cliente e conta; Valor e vencimento; Parcelamento opcional com prévia).
- **Título a receber** (`/app/financeiro/receber/$id`), abas na URL: Dados, Baixas, Histórico de alterações (última).
- **Diálogos:** "Dar baixa" / "Baixa registrada"; "Estornar baixa" / "Baixa estornada"; "Cancelar título" / "Título cancelado". Saem com "Voltar".
- **Estados:** carregando; vazio ("Lance o primeiro título a receber ou aprove um orçamento para gerar as parcelas do contrato." + "Novo título a receber"); vazio filtrado com "Limpar filtros"; erro com "Tentar de novo"; modo leitura sem `finance.edit`.
- **Propositivo:** condição padrão do cliente, conta padrão, data de hoje e saldo inteiro já preenchidos na baixa; "Baixar o saldo com desconto" para fechar diferença de centavos; depois da baixa total, "Próximos passos" oferece "Ver extrato da conta" (FIN-04) e "Próximo título vencido deste cliente".
- **400 px e teclado:** um CA de E2E para o diálogo de baixa.
- **Ajuda:** `Ajuda/financeiro-receber.md` (lista, `padrao: true`), `Ajuda/financeiro-receber-novo.md`, `Ajuda/financeiro-receber-titulo.md` com uma por aba (`dados`, `baixas`, `historico`); `<Dica>` em "Principal", "Juros", "Multa", "Desconto" e "Vencido".

## Plano de testes
- **Unidade:** situação e saldo (CA 5, 6, 9, 10, teste de propriedade); total da baixa (CA 7, 8); validações (CA 4, 9, 17).
- **Integração (Testcontainers, obrigatória: movimenta financeiro):** criar título e parcelar pela condição, mesma divisão do `SplitAsync` (CA 1–3); baixa parcial e total com transação na mesma transação do banco (CA 7, 9); concorrência de baixas (CA 11); estorno com transação inversa e notificação (CA 12); cancelamento com e sem baixa (CA 13); título de contrato protegido (CA 14, com título de origem `Contract` criado pelo teste); provedores de uso (CA 16); data antes do saldo inicial (CA 17); três camadas e IDOR (CA 18, 19); auditoria (CA 20); numeração sem duplicidade em 50 criações paralelas.
- **Arquitetura:** Finance só referencia `Parties.Contracts` (e não Application/Infrastructure); sem rota DELETE em títulos e transações; endpoints com `RequireModule` e `perm:`.
- **E2E (Playwright):** lançar título parcelado, dar baixa parcial e total, estornar; diálogo de baixa a 400 px e só pelo teclado.

## Fora do escopo
- Recebíveis gerados pelo contrato e liberação por marco: COM-04b.
- Boleto com Pix, webhook e conciliação: BNK-01 a BNK-03 (o modelo já deixa `billing_slip_id` e `reconciled` para depois, sem fechar portas).
- Extrato, saldo atual e transferências: FIN-04. Aging e pontualidade: FIN-06. Régua de cobrança: FIN-09.
- Baixa em lote (R-PRP-6, D): fica para depois do MVP.

## Pontos em aberto
1. **Permissão da baixa de recebimento e do estorno.** O `finance.pay` do catálogo é "registrar a saída de caixa de uma ordem aprovada". **Recomendação:** baixa e estorno de recebimento com `finance.edit` (o Financeiro do seed já tem), estorno com motivo e notificação aos Proprietários; `finance.pay` só para pagamentos (FIN-03a).
2. **Juros e multa automáticos.** O PRD pede "juros/multa", sem regra de cálculo. **Recomendação:** valores digitados na baixa no MVP; parâmetros opcionais `finance.late_fine_pct` e `finance.late_interest_month_pct` em Configurações, sem valor no seed, para sugerir os valores numa story futura (ou na BNK-01, quando o boleto levar juros e multa).
3. **Dia não útil.** **Recomendação:** não ajustar o vencimento e considerar "Vencido" só a partir do dia seguinte ao vencimento (CA 5); tratar fim de semana e feriado na BNK-01, quando o banco define a regra do boleto.
4. **Edição de título do contrato.** **Recomendação:** valor e vencimento de título de origem `Contract` só mudam pelo contrato (cancelamento, COM-04b) ou pelo aditivo (COM-07), para o orçado e o recebível não se separarem.
5. **P-18 (mensagens no meio da tela):** pendente; vale para todas as telas da Fase 2. **Recomendação:** decidir a ADR antes da primeira tela desta fase; até lá, os CAs citam o toast atual.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers), arquitetura e E2E passando.
- [ ] Migration criada (não destrutiva) com `receivables`, `receivable_settlements` e `financial_transactions`.
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Telas com estados de carregando, vazio, vazio filtrado e erro; modo leitura.
- [ ] Ajuda da lista, da criação e de cada aba; teste de cobertura passando.
- [ ] Padrões comuns da Fase 2, padrões preenchidos e próximos passos aplicados; efeito financeiro com confirmação.
- [ ] Auditoria ligada (título, baixa, estorno, cancelamento).
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado (colunas e tabelas marcadas como proposta); story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Entidades `Receivable`, `ReceivableSettlement` e `FinancialTransaction` (Domain) com as regras de saldo, situação, baixa, estorno e cancelamento.
- [ ] Provedores `IBankAccountUsageProvider`, `IAccountUsageProvider`, `ICostCenterUsageProvider` e `IPartyUsageProvider`.
- [ ] Endpoints `/api/v1/finance/receivables` (+ `/split-preview`, `/{id}/settlements`, `/{id}/settlements/{sid}/reverse`, `/{id}/cancel`).
- [ ] Telas, diálogos e ajuda.

## Quality gates
- Agentes previstos: @dev, @qa, cybersecurity (concorrência e estorno).
- Gate: CA 7, 10, 11 e 12 obrigatórios (dinheiro e invariante).

## Validação do PO
Checklist do @po (Pax), 19/10/2026.

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo (cabeçalho, user story, contexto, CAs, regras, permissões/tabelas, telas, testes, fora do escopo, pontos em aberto, DoD, tarefas) | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas do CLAUDE.md) que se aplicam | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK |
| 7 | Plano de testes cobre os CAs (unidade, integração com Testcontainers, arquitetura, E2E quando cabe) | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas como proposta) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD do CLAUDE.md ajustado; pontos em aberto registrados sem decidir | OK |

- **Correções feitas na validação (19/10/2026):** re-estimada de M para G (transações e baixas nascem aqui); invariante `paid_amount = Σ baixas` com teste de propriedade (revisão pré-Fase 1 §3); "Vencido" calculado, nunca gravado; concorrência de baixas (CA 11); título de contrato protegido (CA 14); data da transação depois do saldo inicial (CA 17); campo de projeto oculto até a COM-04a, porque esta story vem antes dela na ordem (CA 2).
- **Pré-condições para `Ready`:** decisão dos Pontos em aberto 1 a 4 (todos com padrão recomendado) e da P-18.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 19/10/2026 | 0.1 | Story detalhada | River (SM) |
| 19/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
