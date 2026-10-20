# FIN-01 — Contas a receber: títulos, parcelas, baixa total ou parcial, juros, multa, desconto e estorno

| Campo | Valor |
|---|---|
| ID | FIN-01 |
| Título | Títulos a receber manuais e parcelados pela condição de pagamento, baixa total ou parcial com juros, multa e desconto, transação na conta bancária, "Quitar o saldo com desconto", estorno de baixa, cancelamento com motivo, limites de desconto, cancelamento e data retroativa, relatório de descontos e cancelamentos e situação "Vencido" calculada |
| Tamanho | G (re-estimada de M: esta story cria também `financial_transactions` e as baixas, base da FIN-03a, FIN-04 e FIN-05) |
| Status | Done |
| Requisitos | R-FIN-3 (CR: manual, parcelas, juros/multa, descontos, baixa parcial) · R-FIN-1 (plano de contas, centro de custo e projeto em todo lançamento) · R-FIN-2 (movimento na conta) · R-COM-4 (numeração CR) · R-COM-2 (notificações) · R-PRP-1 · R-PRP-7 · R-SEC-5 |
| Dependências | FND-09e (mensagens, diálogo grande, concorrência, idempotência), CAD-05a (`IChartOfAccounts`, centros de custo), CAD-05b (contas bancárias, `IPaymentTerms.SplitAsync`, `IBankAccountUsageProvider`), CAD-01a (`IParties`), CAD-06 (numeração, Configurações → Parâmetros), FND-09d; Fase 0 (FND-07, FND-09c, FND-14) |
| Bloqueia | FIN-02 (mesmo molde de título), FIN-00 (implantação), FIN-05, FIN-06a, FIN-03a (transação de pagamento), FIN-04, COM-04b (recebíveis do contrato), BNK-01, BNK-02 |
| Ordem de execução | 1 (Marco 2.1a "Ver o caixa") |

## User story
Como **financeiro**, quero **lançar o que a empresa tem a receber, parcelar pela condição de pagamento e dar baixa do que entrou, inteiro ou em parte, com juros, multa e desconto**, para **saber o que falta receber de cada cliente e ter o saldo das contas batendo com o banco**.

## Contexto e referências
- PRD R-FIN-3, R-FIN-1, R-FIN-2; `04-fluxos-estados` §1 "Título a receber" (`Planned → Open → PartiallyPaid → Paid`, `Overdue` calculado, `Cancelled`).
- `03-modelo-dados` → `finance.receivables`, `finance.financial_transactions`; `bank_accounts`, `payment_terms`, `chart_of_accounts`, `cost_centers` (CAD-05a/05b).
- Revisão pré-Fase 2: D8 (limites de desconto, cancelamento e data retroativa), D14 (vencimento do título do contrato), §2.1 (C2 concorrência, A2 idempotência, A3 título órfão, M4 "abater saldo", M9 consultas em lote, M10 vocabulário), §2.2-A5 (relatório e notificação de cancelamento), §2.3 (padrões de tela).
- ADR 0019 (decisões técnicas da Fase 2), ADR 0021 (mensagens), ADR 0016 adendo M6 (parâmetros do Finance).
- Revisão pré-Fase 0 §2.1 (`Overdue` calculado, não gravado); revisão pré-Fase 1 §3 (invariante `paid_amount = Σ baixas`).
- CAD-05b CA 5 (saldo inicial travado depois da primeira transação) e CA 13 (vencimento em dia não útil "revisto na FIN-01").
- CAD-05a CA 15 e 16 (`IAccountUsageProvider`, `ICostCenterUsageProvider`): esta story implementa os provedores para títulos e transações.
- `ModuleCatalog`: `finance` com `view`, `create`, `edit`, `approve`, `delete` e `pay`. O grupo Financeiro do seed tem `view`, `create` e `edit` (ADR 0010 item 3); `finance.approve` fica só com o Proprietário na Fase 2 (ADR 0008, adendo D5).
- Padrões comuns da Fase 2 (`docs/stories/README.md`).

## Critérios de aceite
**Título manual e parcelas**
1. **Dado** `finance.create`, **quando** abro "Novo título a receber" e informo cliente, descrição, conta de receita, valor, emissão e vencimento e clico "Salvar título a receber", **então** o título é gravado `Open`, origem `Manual`, com número `CR-2026-00001` (`INumberSequence`, tipo `CR`), a mensagem de sucesso diz "Título a receber salvo" e o detalhe abre com a faixa "Próximos passos" ("Dar baixa", "Novo título para este cliente").
2. **Dado** o cliente escolhido, **quando** o formulário é preenchido, **então** o cliente só pode ser terceiro ativo com o papel Cliente (`IParties.HasRoleAsync`; senão 422 "Escolha um terceiro com o papel Cliente."), a conta só pode ser analítica ativa do tipo Receita (`IChartOfAccounts.PostableAsync(Revenue)`), o centro de custo é opcional, a emissão vem com hoje (R-PRP-1) e o projeto é opcional e validado por `IProjects` de `Commercial.Contracts` — antes da COM-04a o campo fica oculto (sem dependência circular).
3. **Dado** "Parcelar pela condição", **quando** escolho a condição (padrão: a condição padrão do cliente, `PartySummary.DefaultPaymentTermId`), o valor R$ 1.000,00 e a data base 10/11/2026 com a condição "30/60/90 dias", **então** a prévia mostra 3 parcelas de R$ 333,33, R$ 333,33 e R$ 333,34 com vencimentos 10/12/2026, 09/01/2027 e 08/02/2027, idêntica a `IPaymentTerms.SplitAsync`, e "Salvar títulos a receber" grava 3 títulos com números seguidos, descrição "{descrição} (1/3)", "(2/3)", "(3/3)" e o mesmo `installment_group_id`.
4. **Dado** valor ≤ 0, vencimento anterior à emissão ou conta agrupadora, **quando** salvo, **então** a API responde 422 com a mensagem PT-BR no campo ("Informe um valor maior que zero.", "O vencimento não pode ser antes da emissão.", "Escolha uma conta analítica.").
5. **[Segurança]** **Dado** o formulário de título manual ou de parcelamento, **quando** o mesmo envio chega duas vezes (clique duplo ou reenvio) com o mesmo `Idempotency-Key`, **então** só um título (ou um grupo de parcelas) é criado e o segundo envio devolve o resultado original (ADR 0019 §3).

**Situação calculada**
6. **Dado** um título `Open` ou `PartiallyPaid` com vencimento anterior a hoje (data de São Paulo, `BusinessTime.Today`), **quando** aparece na lista, no detalhe ou na API, **então** a situação exibida é "Vencido" (chip crítico, faixa lateral vermelha na lista) e o campo `isOverdue` é `true`, sem gravar `Overdue` na tabela; o filtro "Vencidos" usa a mesma regra no servidor. Vencimento em dia não útil não é ajustado: "Vencido" vale a partir do dia seguinte ao vencimento.
7. **Dado** um título com vencimento hoje, **quando** consulto, **então** ele aparece como "Vence hoje" (chip alerta), não como vencido.

**Baixa**
8. **Dado** `finance.edit` e um título `Open` de R$ 1.000,00, **quando** clico "Dar baixa", informo a conta bancária (padrão: a conta padrão ativa), a data do recebimento (padrão: hoje), o valor recebido de principal R$ 400,00, juros R$ 5,00, multa R$ 20,00 e desconto R$ 0,00, e confirmo "Dar baixa", **então** na mesma transação: grava uma baixa (`receivable_settlements`), cria uma `financial_transaction` do tipo `Receipt` de +R$ 425,00 na conta e na data informadas, o título fica `PartiallyPaid` com `paid_amount` = R$ 400,00 e saldo R$ 600,00, e a mensagem de sucesso diz "Baixa registrada".
9. **Dado** o diálogo de baixa (`size="lg"`), **quando** abre, **então** mostra o saldo antes e depois, o total que vai entrar na conta (principal + juros + multa − desconto) atualizado ao vivo em `aria-live`, a conta e a data, e o botão repete o verbo "Dar baixa" (R-PRP-7); erro de validação aparece no campo dentro do diálogo.
10. **Dado** uma baixa de principal igual ao saldo, **quando** confirmo, **então** o título fica `Paid`; principal maior que o saldo, desconto maior que o principal da baixa, valor negativo, conta inativa ou data futura respondem 422 com mensagem PT-BR no campo.
11. **Dado** um saldo pequeno que o cliente não vai pagar, **quando** clico "Quitar o saldo com desconto" e confirmo, **então** é gravada uma baixa com principal = saldo, desconto = saldo e total 0, **sem transação bancária** (ADR 0019 §8), e o título fica `Paid`; a ação segue os limites de desconto do CA 13.
12. **Dado** qualquer título, **quando** consulto, **então** vale o invariante `paid_amount = Σ principal das baixas não estornadas` e `interest`, `fine` e `discount` do título são a soma das baixas; um teste de propriedade confere o invariante depois de baixas, quitações com desconto e estornos aleatórios.
13. **[Segurança]** **Dado** os parâmetros do Finance `finance.discount_approval_pct` (padrão 5,00%) e `finance.discount_approval_amount` (padrão R$ 500,00), **quando** uma baixa ou uma quitação tem desconto acima de qualquer um dos dois (o % sobre o saldo antes da baixa), **então** só quem tem `finance.approve` confirma: para os demais, o diálogo mostra a pré-condição "Desconto acima de {limite}: só quem aprova em Caixa e pagamentos pode confirmar." com o botão desabilitado, e a API responde 403 `discount_requires_approval`.
14. **[Segurança]** **Dado** o parâmetro `finance.backdate_days` (padrão 7), **quando** a data da baixa fica mais de 7 dias antes de hoje, **então** o motivo passa a ser obrigatório (422 "Informe o motivo da data retroativa.") e os membros do Proprietário são notificados ("Baixa de {número} com data de {data}, lançada por {usuário}.").
15. **[Segurança]** **Dado** duas baixas enviadas ao mesmo tempo para o mesmo título, **quando** a soma passaria do saldo, **então** só a primeira grava e a segunda recebe 409 `concurrency_conflict` "O título mudou desde que você abriu. Recarregue." (`xmin`, ADR 0019 §2), com o botão "Recarregar" no diálogo.

**Estorno e cancelamento**
16. **Dado** `finance.edit` e uma baixa, **quando** clico "Estornar baixa", informo o motivo (obrigatório, até 200 caracteres) e confirmo "Estornar baixa" (estilo `destructive`), **então** a baixa fica marcada como estornada (nada é apagado), uma `financial_transaction` inversa (−R$ 425,00, mesma conta, data do estorno, ligada à original) é criada (quitação com desconto não tem transação, então não gera inversa), a situação do título é recalculada e os membros do Proprietário são notificados pelo sino ("Baixa de {número} estornada por {usuário}"); a mensagem de sucesso diz "Baixa estornada".
17. **Dado** `finance.edit` e um título sem baixa ativa, **quando** clico "Cancelar título", informo o motivo e confirmo "Cancelar título", **então** ele fica `Cancelled` com motivo, usuário e data, some dos totais em aberto, continua consultável pelo filtro "Cancelados" e os membros do Proprietário são notificados ("Título {número} de {saldo} cancelado por {usuário}: {motivo}"); com baixa ativa, a API responde 409 "Estorne as baixas antes de cancelar." e o diálogo oferece "Estornar baixa".
18. **[Segurança]** **Dado** o parâmetro `finance.cancel_approval_amount` (padrão R$ 1.000,00), **quando** o título a cancelar tem saldo acima dele, **então** só quem tem `finance.approve` confirma (pré-condição no diálogo e 403 `cancel_requires_approval` na API).
19. **Dado** um título de origem `Contract` (COM-04b), **quando** tento cancelar ou mudar o valor por esta tela, **então** a API responde 409 "Título do contrato {número}: ajuste pelo contrato ou pelo aditivo."; descrição, conta, centro de custo e projeto continuam editáveis, e o **vencimento** de um título `Open` também, com motivo obrigatório e auditoria (`finance.edit`, D14).
20. **Dado** um título de origem `Contract` cujo contrato está `Cancelled` (consultado por `IContracts.StatusAsync` de `Commercial.Contracts`), **quando** clico "Cancelar título", **então** o cancelamento é permitido com `finance.edit` e motivo, com a marca "Origem cancelada" (ADR 0019 §4), respeitando o CA 18.
21. **Dado** qualquer título ou transação, **quando** tento excluir pela API, **então** não existe rota de exclusão (R-FIN-3: título nunca é apagado).

**Relatório de descontos e cancelamentos (§2.2-A5)**
22. **Dado** `finance.view`, **quando** abro "Descontos e cancelamentos" (ação na lista A receber) e escolho o período (padrão: mês corrente), **então** vejo cada desconto concedido (título, cliente, valor, % do saldo, quem, quando, se passou do limite) e cada cancelamento (título, saldo cancelado, motivo, quem, quando), com os totais do período; a FIN-02 acrescenta os títulos a pagar cancelados.

**Uso por outros cadastros**
23. **Dado** uma conta bancária com transação, **quando** a CAD-05b pergunta `IBankAccountUsageProvider.HasMovementAsync`, **então** a resposta é `true` (saldo inicial travado e conta só inativa); idem `IAccountUsageProvider`, `ICostCenterUsageProvider` e `IPartyUsageProvider` ("títulos a receber") para conta, centro e terceiro usados em título.
24. **Dado** uma transação com data anterior ao `opening_date` da conta, **quando** salvo a baixa, **então** a API responde 422 "A data é anterior ao saldo inicial da conta ({data})."

**Permissões (3 camadas) e segurança**
25. **Dado** o módulo `finance` não liberado, **quando** chamo os endpoints, **então** 403 `module_not_released` e o front mostra `ModuleNotReleased`; sem grupo que habilite `finance`, 403 `forbidden`; com `finance.view` sem `create`/`edit`, as ações ficam ocultas por `<Can>` e a escrita responde 403. Todo endpoint tem `RequireModule` e `perm:` (teste de arquitetura).
26. **[Segurança]** **Dado** uma rota `/receivables/{id}/settlements/{settlementId}`, **quando** a baixa não pertence ao título, **então** 404 (IDOR pai/filho); projeto, terceiro, conta e centro informados são validados pelo Contracts do dono (id inexistente → 422).
27. **Dado** qualquer alteração de título ou baixa, **quando** abro "Histórico de alterações", **então** vejo antes/depois, usuário e data; baixas, quitações e estornos aparecem também na aba "Baixas" com usuário e data.

## Regras de negócio e técnicas
- **Dinheiro** `numeric(18,2)` (`amount`, `paid_amount`, `interest`, `fine`, `discount`, valores da baixa e da transação), arredondamento `AwayFromZero`. **Datas de negócio** `date` (`issue_date`, `due_date`, data da baixa, data da transação); instantes em `timestamptz`.
- **Saldo do título** = `amount − paid_amount`. **Situação gravada:** `Planned`, `Open`, `PartiallyPaid`, `Paid`, `Cancelled`. `Planned` (só para marco por evento, COM-04b) tem `due_date` nulo. "Vencido" é calculado (CA 6).
- **Baixa** (`receivable_settlements`): `receivable_id`, `bank_account_id` (nulo na quitação com desconto), `date`, `principal`, `interest`, `fine`, `discount`, `total` (= principal + interest + fine − discount; > 0 numa baixa comum, 0 na quitação), `kind` (`Settlement` | `DiscountWriteOff`), `backdate_reason`, `transaction_id` (nulo na quitação), `reversed_at`, `reversed_by`, `reversal_reason`, `reversal_transaction_id`. Imutável, exceto as colunas de estorno.
- **Transação** (`financial_transactions`): `amount` com sinal (+ entrada), `type` (`Receipt` aqui; `Payment` na FIN-03a; `Transfer` e `BankEntry` na FIN-04), `reverses_transaction_id`, imutável. O saldo da conta é `opening_balance + Σ amount` (consulta na FIN-05, reaproveitada pela FIN-04).
- **Juros e multa** são valores informados na baixa, sem cálculo automático. Os parâmetros opcionais `finance.late_fine_pct` e `finance.late_interest_month_pct` entram em `finance.parameters` sem valor no seed, para a sugestão futura (BNK-01).
- **Parâmetros do Finance** (ADR 0016 adendo M6): `finance.discount_approval_pct` (`numeric(7,4)`, 5,0000), `finance.discount_approval_amount` (R$ 500,00), `finance.cancel_approval_amount` (R$ 1.000,00) e `finance.backdate_days` (7) em `finance.parameters`, lidos por `IFinanceParameters`, editados em Configurações → Parâmetros (seção Financeiro, `settings.edit`), com seed idempotente e auditoria.
- **Parcelamento:** `installment_group_id` agrupa as parcelas; cada parcela é um título independente (baixa, cancelamento).
- **Origem:** `origin` (`DocumentOrigin` no SharedKernel, ADR 0019 §14: `Manual` aqui; `Opening` na FIN-00; `Contract` na COM-04b) e `origin_id`.
- **Concorrência e transições** pela entidade rastreada com `xmin` (ADR 0019 §2); `Idempotency-Key` com `client_request_id` (ADR 0019 §3).
- **Lista:** nomes de cliente e projeto resolvidos por página com `IParties.FindManyAsync(ids)` (nova nesta story) e `IProjects.FindManyAsync` (COM-04a); filtro por cliente pelo id do combobox (ADR 0019 §13); índice parcial `(status, due_date) WHERE status NOT IN ('Paid','Cancelled')`.
- **Notificações** do próprio Finance (estorno, cancelamento, data retroativa) síncronas, pelo `INotificationService`, na transação do comando (ADR 0019 §18).
- **Ids** Guid v7; **auditoria** `IAuditable` em título e baixa; **numeração** `CR` na mesma transação; índice único em `number`.

## Permissões, eventos e tabelas
- **Permissões (já no `ModuleCatalog`):** `finance.view` (lista, detalhe, relatório), `finance.create` (novo título), `finance.edit` (editar, baixa, quitação, estorno de baixa, cancelamento), `finance.approve` (desconto e cancelamento acima do limite).
- **Eventos:** `ReceivableSettled` (`{ receivableId, settlementId, partyId, projectId, dueDate, settledOn, principal, isReversal }`, sem dado pessoal) publicado no outbox a cada baixa, quitação e estorno (consumido pela COM-04b para concluir o contrato e, na Fase 4, pelo DSH-02).
- **Contratos:** `IParties.FindManyAsync(ids)` (Parties, nova); `IFinanceParameters` (Finance, nova); `IContracts.StatusAsync` (Commercial, usado quando existir, COM-04a).
- **Tabelas:** `finance.receivables` (como no 03, mais `origin`, `origin_id`, `installment_group_id`, `cancel_reason`, `cancelled_at`, `cancelled_by`, `client_request_id`; `account_id` é a conta do **plano de contas**; `due_date` anulável; sem `status = Overdue`, sem `billing_slip_id` até a BNK-01); `finance.receivable_settlements` (nova); `finance.financial_transactions` (como no 03, mais `reverses_transaction_id`, `settlement_id`, `created_at`, `created_by`; `reconciled` e `statement_line_id` nulos até a BNK-03); `finance.parameters` (nova, ADR 0016 adendo M6).

## Telas e estados
- **Financeiro → A receber** (`/app/financeiro/receber`; entrada no `modules.ts` no grupo "Caixa e pagamentos"): h1 com a linha "O que os clientes devem à empresa, por vencimento."; DataTable com número (link), cliente, descrição, projeto, vencimento, valor, saldo (`Money`, `tabular-nums`) e situação (mapa de chips da Fase 2: Previsto neutro, Em aberto info, Vence hoje alerta, Vencido crítico com faixa lateral vermelha, Parcial info, Pago ok, Cancelado neutro); `Tag` "Implantação" nos títulos de origem `Opening`; filtros na URL por situação, cliente, projeto, período de vencimento; rodapé de totais do filtro (em aberto, vencido) pelo `DataTable` com `totals`.
- **Novo título a receber** (`/app/financeiro/receber/novo`): página em seções (Cliente e conta; Valor e vencimento; Parcelamento opcional com prévia).
- **Título a receber** (`/app/financeiro/receber/$id`): cabeçalho com número, chip e cliente; resumo em "facts" (4 → 2 colunas a 400 px); abas na URL: Dados, Baixas, Histórico de alterações (última); faixa de modo leitura quando `Paid` ou `Cancelled`, explicando por quê.
- **Descontos e cancelamentos** (`/app/financeiro/receber/descontos-e-cancelamentos`).
- **Diálogos** (`size="lg"`, impacto com saldo antes e depois): "Dar baixa" / "Baixa registrada"; "Quitar o saldo com desconto" / "Saldo quitado"; "Estornar baixa" / "Baixa estornada"; "Cancelar título" / "Título cancelado"; "Alterar vencimento" / "Vencimento alterado" (título do contrato). Saem com "Voltar".
- **Estados:** carregando (skeleton por aba); vazio ("Lance o primeiro título a receber ou aprove um orçamento para gerar as parcelas do contrato." + "Novo título a receber"); vazio filtrado com "Limpar filtros"; erro com "Tentar de novo"; modo leitura sem `finance.edit`.
- **Propositivo:** condição padrão do cliente, conta padrão, data de hoje e saldo inteiro já preenchidos na baixa; "Quitar o saldo com desconto" para fechar diferença de centavos; depois da baixa total, "Próximos passos" oferece "Ver extrato da conta" (FIN-04, quando existir) e "Próximo título vencido deste cliente".
- **400 px:** colunas sempre visíveis: número, cliente, vencimento, saldo e situação; descrição, projeto e valor original vão para a linha expansível. E2E de 400 px e só pelo teclado para o diálogo de baixa.
- **Ajuda:** `Ajuda/financeiro-receber.md` (lista, `padrao: true`), `Ajuda/financeiro-receber-novo.md`, `Ajuda/financeiro-receber-titulo.md` com uma por aba (`dados`, `baixas`, `historico`), `Ajuda/financeiro-descontos-cancelamentos.md`; `Ajuda/configuracoes-parametros.md` ganha "Limites de desconto, cancelamento e data retroativa"; `<Dica>` em "Principal", "Juros", "Multa", "Desconto", "Quitar o saldo com desconto" e "Vencido".

## Plano de testes
- **Unidade:** situação e saldo (CA 6, 7, 10, 12, teste de propriedade); total da baixa e quitação (CA 8, 9, 11); limites de desconto, cancelamento e data retroativa (CA 13, 14, 18); validações (CA 4, 10, 24).
- **Integração (Testcontainers, obrigatória: movimenta financeiro):** criar título e parcelar pela condição, mesma divisão do `SplitAsync` (CA 1–3); idempotência com envio repetido (CA 5); baixa parcial e total com transação na mesma transação do banco (CA 8, 10); quitação sem transação (CA 11); limites com e sem `finance.approve` (CA 13, 18); data retroativa com motivo e notificação (CA 14); concorrência de baixas (CA 15); estorno com transação inversa e notificação (CA 16); cancelamento com e sem baixa, com notificação (CA 17); título de contrato protegido e vencimento editável (CA 19, com título de origem `Contract` criado pelo teste); origem cancelada com `IContracts` falso (CA 20); relatório (CA 22); provedores de uso (CA 23); data antes do saldo inicial (CA 24); três camadas e IDOR (CA 25, 26); auditoria (CA 27); numeração sem duplicidade em 50 criações paralelas.
- **Arquitetura:** Finance só referencia `Parties.Contracts` e `Commercial.Contracts` (e não Application/Infrastructure); sem rota DELETE em títulos e transações; endpoints com `RequireModule` e `perm:`; `DocumentOrigin` no SharedKernel.
- **E2E (Playwright):** lançar título parcelado, dar baixa parcial e total, quitar com desconto, estornar; diálogo de baixa a 400 px e só pelo teclado.

## Fora do escopo
- Recebíveis gerados pelo contrato e liberação por marco: COM-04b. Títulos de implantação (origem `Opening`): FIN-00.
- Boleto com Pix, webhook e conciliação: BNK-01 a BNK-03 (o modelo já deixa `billing_slip_id` e `reconciled` para depois, sem fechar portas).
- Extrato, transferências e lançamento sem OP: FIN-04. Aging: FIN-06a. Pontualidade: FIN-06b. Régua de cobrança: FIN-09.
- Baixa em lote (R-PRP-6, D): fica para depois do MVP.

## Pontos em aberto
Decididos pelo PO em 20/10/2026 (revisão pré-Fase 2, seção 1.1 e D8, D14):
1. Baixa e estorno de recebimento com `finance.edit`; `finance.pay` só para pagamentos (FIN-03a).
2. Juros e multa digitados na baixa; parâmetros opcionais sem seed para sugerir depois.
3. Dia não útil: vencimento não ajustado e "Vencido" a partir do dia seguinte; rever na BNK-01.
4. Título do contrato: valor só pelo contrato ou pelo aditivo; vencimento editável no título em aberto com motivo e auditoria (D14); origem cancelada libera o cancelamento (ADR 0019 §4).
5. P-18 decidida (ADR 0021): mensagens no topo do conteúdo.

## Definition of Done
- [x] Critérios de aceite atendidos.
- [x] Testes de unidade, integração (Testcontainers), arquitetura e E2E passando.
- [x] Migration criada (não destrutiva) com `receivables`, `receivable_settlements`, `financial_transactions` e `parameters`; seed dos parâmetros idempotente.
- [x] Permissões só do `ModuleCatalog`.
- [x] Telas com estados de carregando, vazio, vazio filtrado e erro; modo leitura.
- [x] Ajuda da lista, da criação, de cada aba, do relatório e dos parâmetros; teste de cobertura passando.
- [x] Padrões comuns da Fase 2, padrões preenchidos e próximos passos aplicados; efeito financeiro com confirmação.
- [x] Auditoria ligada (título, baixa, quitação, estorno, cancelamento, parâmetros).
- [x] OpenAPI e cliente Orval regenerados.
- [x] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [x] Entidades `Receivable`, `ReceivableSettlement` e `FinancialTransaction` (Domain) com as regras de saldo, situação, baixa, quitação, estorno e cancelamento; `DocumentOrigin` no SharedKernel.
- [x] `FinanceParameters` (`finance.parameters`), `IFinanceParameters` e a seção Financeiro em Configurações → Parâmetros.
- [x] Provedores `IBankAccountUsageProvider`, `IAccountUsageProvider`, `ICostCenterUsageProvider` e `IPartyUsageProvider`; `IParties.FindManyAsync`.
- [x] Endpoints `/api/v1/finance/receivables` (+ `/split-preview`, `/{id}/settlements`, `/{id}/write-off`, `/{id}/settlements/{sid}/reverse`, `/{id}/cancel`, `/{id}/due-date`, `/discounts-and-cancellations`).
- [x] Telas, diálogos e ajuda.

## Quality gates
- Agentes previstos: @dev, @qa, cybersecurity (concorrência, estorno, limites).
- Gate: CA 8, 12, 13, 15, 16 e 18 obrigatórios (dinheiro, invariante e limites).

## Validação do PO
Checklist do @po (Pax), 20/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fase 2).

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

- **Correções feitas na validação (19/10/2026):** re-estimada de M para G; invariante `paid_amount = Σ baixas` com teste de propriedade; "Vencido" calculado; concorrência de baixas; título de contrato protegido; data da transação depois do saldo inicial; campo de projeto oculto até a COM-04a.
- **Correções da revalidação (20/10/2026):** limites de desconto, cancelamento e data retroativa com parâmetros do Finance (D8, CA 13, 14, 18); notificação em todo cancelamento e relatório de descontos e cancelamentos (§2.2-A5, CA 17, 22); vencimento do título do contrato editável (D14, CA 19) e origem cancelada (CA 20); "Quitar o saldo com desconto" sem transação (M4, CA 11); idempotência (CA 5) e 409 com "Recarregar" (CA 15); `ReceivableSettled` com `settlementId` e `isReversal`; `DocumentOrigin` no SharedKernel; `IParties.FindManyAsync`; mensagens pela ADR 0021; colunas a 400 px; dependência da FND-09e. CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma pendente (pontos em aberto decididos, P-18 decidida, FND-09e antes).
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 19/10/2026 | 0.1 | Story detalhada | River (SM) |
| 19/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 20/10/2026 | 0.3 | Decisões do PO (D8, D14, seção 1.1) e correções da revisão pré-Fase 2 (§2.1, §2.2, §2.3); CAs renumerados | River (SM) |
| 20/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
| 20/10/2026 | 1.0 | Implementada. Desvios: idempotência pelo filtro comum (`shared.idempotency_records`, adendo da ADR 0019 §3), sem `client_request_id` em `receivables`; o seletor de cliente usa a rota nova `GET /finance/customers` (com `finance.view`, documento só de PJ); `IContracts` nasce em `Commercial.Contracts` com `IsCancelledAsync` (o Finance trata "sem implementação" como não cancelado até a COM-04a); o vencimento só muda em título `Open` sem baixa parcial; a baixa guarda `balance_before`, base do % de desconto no limite e no relatório; o campo de projeto é recusado (422) até a COM-04a; filtros da lista por situação e período de vencimento (o filtro por cliente existe na API); o próximo passo depois da baixa total é "Ver os títulos em aberto"; os parâmetros do Finance ficam no histórico do módulo `settings`; a lista a 400 px esconde descrição e valor original (colunas secundárias) em vez da linha expansível | @dev |
