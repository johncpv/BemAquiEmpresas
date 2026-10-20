# FIN-03a — Ordem de pagamento: criar, aprovar ou reprovar e pagar

| Campo | Valor |
|---|---|
| ID | FIN-03a |
| Título | Ordem de pagamento com um ou mais títulos a pagar e o retrato de para onde vai o dinheiro, aprovação pela alçada padrão (quem tem `finance.approve`, na Fase 2 só o Proprietário), reprovação com comentário, regra do solicitante, pagamento com `finance.pay` limitado ao valor aprovado, estorno, comprovante e notificações |
| Tamanho | G (quebra da FIN-03: a alçada configurável, o multinível e o link no e-mail foram para a FIN-03b) |
| Status | Ready |
| Requisitos | R-FIN-5 (todo pagamento exige OP aprovada; padrão: proprietário aprova tudo; aprovação/reprovação com comentário) · R-COM-2 (notificação "OP aguardando aprovação") · R-COM-3 (comprovante) · R-COM-4 (numeração OP) · R-PRP-2 · R-PRP-5 · R-PRP-7 · R-SEC-2 (2FA no login de quem aprova ou paga) · R-SEC-5 |
| Dependências | FIN-02 (títulos a pagar, forma de pagamento, trava em OP), FIN-01 (`financial_transactions`, parâmetros do Finance), FIN-05 (saldo atual da conta), FIN-00 (títulos de implantação), CAD-05b (contas), FND-06c (2FA obrigatório no login para quem aprova ou paga), FND-07 (notificações, outbox, anexos), FND-09e; Fase 0 (FND-09c, FND-09d, FND-14) |
| Bloqueia | FIN-04 (pagamentos no extrato), FIN-03b, DSH-01 ("Pendências do dono"), DSH-04 |
| Ordem de execução | 6 (Marco 2.1b "Pagar com OP"; entrega o "primeiro ganho financeiro" do PRD §7) |

## User story
Como **proprietário**, quero **que todo pagamento passe por uma ordem que eu aprovo ou reprovo com um comentário, vendo para quem e para onde vai o dinheiro**, e como **financeiro**, quero **pagar só o que foi aprovado e ver a baixa refletida na conta**, para **nenhum dinheiro sair da empresa sem autorização formal**.

## Contexto e referências
- PRD R-FIN-5, §1.1 problema 5 ("pagamentos saem sem aprovação formal"), §1.2 métrica "100% dos pagamentos passando por OP aprovada".
- `04-fluxos-estados` §1 (`PaymentOrder: Pending → Approved → Paid`, `Rejected` volta os títulos para `Open` com comentário obrigatório; regra dura "nenhum pagamento é baixado sem OP aprovada"); §3.6 (quem solicita não aprova a própria OP, exceto o proprietário, configurável); §3.7 (`finance.approve` não dispensa a alçada).
- `03-modelo-dados` → `payment_orders`, `payment_order_approvals`; revisão pré-Fase 0 §2.1 (`payable_ids` vira tabela de junção; status do título derivado da OP).
- Revisão pré-Fase 2: D5 (`finance.pay` para o Financeiro, `finance.approve` só no Proprietário), D6 (Proprietário aprovando a própria OP), D8 (data retroativa), D9 (forma de pagamento congelada), §2.1 (C2, A2, M1 anexos, M3 transição condicional, baixos: estado `Reversed`), §2.2-C1 (pago = aprovado), §2.2-A4 (avisos na OP), §2.3.
- **Adiados pelo PO (não entram):** 2FA a cada ação (P-19) e estorno de pagamento com `finance.approve` (P-20). O 2FA continua exigido no login (FND-06c CA 12) e o estorno fica com `finance.pay`.
- ADR 0008 (adendo D5), ADR 0010 item 3, ADR 0019, ADR 0021.
- `ModuleCatalog`: `finance.approve` = "ordem de pagamento, dentro da alçada"; `finance.pay` = "registra a saída de caixa de uma ordem aprovada".
- Protótipo: tela "Caixa e pagamentos" com a lista de OPs; o texto "até R$ 5.000 o financeiro aprova" é ilustrativo e não vale.

## Critérios de aceite
**Permissão de pagar (D5)**
1. **Dado** um tenant novo ou existente no próximo deploy, **quando** o seed patch `finance-pay-financeiro` roda, **então** o grupo Financeiro do seed ganha `finance.pay` (uma vez, sem mexer no que o tenant mudou, com o `perm_version` dos membros incrementado e auditoria com o usuário de sistema); `finance.approve` continua só com o Proprietário.

**Criar a OP**
2. **Dado** `finance.create` e títulos `Open` selecionados na lista "A pagar", **quando** clico "Criar ordem de pagamento", **então** a OP abre com os títulos, o valor a pagar de cada um vem com o saldo líquido em aberto (editável para menos, pagamento parcial), os acréscimos previstos (juros e multa, opcionais, padrão 0), a data prevista vem com o menor vencimento e "Enviar para aprovação" grava a OP `Pending` com número `OP-2026-0001`; os títulos passam a `AwaitingApproval` na mesma transação, cada um pela transição condicional do próprio título (`Open → AwaitingApproval` com `xmin`, ADR 0019 §8); o mesmo envio repetido com o mesmo `Idempotency-Key` não cria outra OP.
3. **Dado** um título que já está em outra OP não finalizada, `Planned`, `Cancelled` ou `Paid`, ou sem forma de pagamento, **quando** tento incluí-lo, **então** 422 no item ("O título {número} não está disponível para pagamento.", "Informe a forma de pagamento do título {número}."); valor a pagar maior que o saldo líquido ou ≤ 0 responde 422.
4. **[Segurança]** **Dado** a OP enviada, **quando** grava, **então** cada item guarda o **retrato do envio** (`payee_snapshot`): fornecedor (id e nome), valor líquido, valor a pagar, acréscimos previstos, **total aprovado do item** (valor a pagar + acréscimos) e a forma de pagamento com os dados (chave Pix e titular, linha digitável, ou banco, agência, conta e titular); mudanças posteriores no título não alteram o retrato (§2.2-C1, D9).
5. **Dado** a OP enviada, **quando** grava, **então** cada aprovador elegível (quem tem `finance.approve`; na FIN-03b, os da alçada) recebe no sino "OP {número} de {total} aguarda sua aprovação", com link direto para a OP (R-PRP-5), e o solicitante não recebe.

**Aprovar e reprovar**
6. **Dado** `finance.approve` e uma OP `Pending`, **quando** abro "Aprovar", **então** o diálogo (`size="lg"`) mostra, por item, o fornecedor, o valor e **para onde vai o dinheiro** (forma de pagamento do retrato) e os avisos âmbar que se aplicam: "O titular não é o fornecedor." (FIN-02 CA 7), "Fornecedor cadastrado há {n} dias." (menos de 30, por `IParties`) e "Dados de pagamento alterados há {n} dias." (menos de 30, `payment_method_changed_at`).
7. **Dado** a confirmação de "Aprovar" com comentário opcional, **quando** grava, **então** a OP passa a `Approved`, a decisão (aprovador, nível 1, decisão, comentário, data) é gravada em `payment_order_approvals`, os títulos passam a `Approved` e o evento `PaymentOrderApproved` notifica quem tem `finance.pay` ("OP {número} aprovada. Pode pagar."); a mensagem de sucesso diz "Aprovada".
8. **Dado** `finance.approve` e uma OP `Pending`, **quando** clico "Reprovar", **então** o comentário é obrigatório (422 "Escreva o motivo da reprovação."), a OP passa a `Rejected`, os títulos voltam a `Open` e o solicitante é notificado com o comentário; a mensagem de sucesso diz "Reprovada".
9. **[Segurança]** **Dado** um usuário que criou a OP, **quando** tenta aprová-la, **então** a API responde 403 `own_payment_order` "Quem solicitou a ordem não pode aprová-la.", com as exceções da D6: (a) o grupo Proprietário tem **um único membro** e é ele o solicitante — a aprovação é permitida e marcada "Aprovada pelo próprio solicitante"; (b) o Proprietário tem mais de um membro e o parâmetro `finance.owner_can_approve_own` está ligado (padrão **desligado**) — idem. Membro de outro grupo nunca aprova a própria OP.
10. **[Segurança]** **Dado** o parâmetro `finance.owner_can_approve_own`, **quando** alguém com `settings.edit` o liga ou desliga, **então** a troca é auditada e todos os membros do Proprietário são notificados ("{usuário} ligou a aprovação da própria ordem pelo Proprietário."); a lista de OPs tem o filtro "Aprovadas pelo próprio solicitante".
11. **[Segurança]** **Dado** duas aprovações (ou aprovação e reprovação) enviadas ao mesmo tempo, **quando** chegam, **então** só a primeira muda a OP (transição pela entidade rastreada com `xmin`) e a segunda recebe 409 "A ordem já foi decidida." com "Recarregar".
12. **Dado** um usuário com `finance.approve` ou `finance.pay` que ainda não configurou o 2FA, **quando** entra no sistema, **então** vale a regra existente da FND-06c CA 12 (configuração do 2FA antes de outra tela); se a sessão abrir o diálogo de aprovar ou pagar nesse estado, o diálogo mostra "Para aprovar, ative a verificação em duas etapas." com "Ativar agora" (FND-09e CA 7). O pedido do código a cada ação está adiado (P-19).

**Pagar**
13. **Dado** `finance.pay` e uma OP `Approved`, **quando** clico "Pagar", escolho a conta bancária (padrão: a conta padrão), a data do pagamento (padrão: hoje), informo por título juros, multa e desconto (padrão: os acréscimos previstos) e confirmo "Pagar", **então** na mesma transação: para cada título, uma baixa (`payable_settlements`) e uma `financial_transaction` `Payment` com valor negativo (principal + juros + multa − desconto), o título fica `Paid` (saldo zero) ou `PartiallyPaid` (pagamento parcial) e a OP fica `Paid`; a mensagem de sucesso diz "Pagamento registrado" e a faixa "Próximos passos" oferece "Ver extrato da conta" (FIN-04, quando existir) e "Anexar comprovante".
14. **[Segurança]** **Dado** um item cujo total a pagar (principal + juros + multa − desconto) passa do total aprovado do item mais a tolerância (parâmetro `finance.payment_tolerance`, padrão R$ 0,00), **quando** confirmo "Pagar", **então** a API responde 409 `exceeds_approved` "O pagamento de {título} passa do aprovado em {diferença}. Envie para nova aprovação." e nada é gravado; o diálogo oferece "Reenviar para aprovação", que volta a OP a `Pending` com os novos acréscimos, novo retrato e a decisão "Reenviada: valor acima do aprovado" no histórico, notificando os aprovadores.
15. **Dado** o diálogo "Pagar", **quando** abre, **então** mostra para onde vai o dinheiro (forma de pagamento do retrato, com os avisos do CA 6), o total que vai sair da conta, por título e somado, ao vivo em `aria-live`, a conta e o saldo atual antes e depois (consulta da FIN-05), e o aviso âmbar "O saldo da conta fica negativo." quando for o caso (não bloqueia).
16. **[Segurança]** **Dado** uma OP `Pending`, `Rejected`, `Cancelled`, `Reversed` ou `Paid`, **quando** chamo o pagamento pela API, **então** a resposta é 409 `payment_order_not_approved` "Só é possível pagar uma ordem aprovada." e nenhuma transação é criada; um teste de integração prova que não há outro caminho que crie transação `Payment`.
17. **[Segurança]** **Dado** dois pagamentos da mesma OP enviados ao mesmo tempo, **quando** chegam, **então** só um grava (transição pela entidade rastreada com `xmin`) e o outro recebe 409.
18. **[Segurança]** **Dado** a data do pagamento mais de `finance.backdate_days` (padrão 7) antes de hoje, **quando** confirmo, **então** o motivo é obrigatório e os membros do Proprietário são notificados ("Pagamento da {OP} com data de {data}, lançado por {usuário}.").
19. **Dado** um título pago com retenções, **quando** o pagamento termina, **então** "Próximos passos" sugere "Gerar título do recolhimento", que abre "Novo título a pagar" já preenchido com o valor das retenções, a descrição "Recolhimento de retenções — {título}" e a data sugerida, para o usuário escolher o favorecido e confirmar; nada é criado sem confirmação (seção 1.1, item 6).

**Cancelar, estornar e comprovante**
20. **Dado** `finance.edit` e uma OP `Pending`, **quando** o solicitante (ou quem tem `finance.approve`) clica "Cancelar ordem" e confirma com motivo, **então** a OP fica `Cancelled` e os títulos voltam a `Open`; OP `Approved` só é cancelada com `finance.approve`; OP `Paid` não é cancelada (409 "Estorne o pagamento.").
21. **Dado** `finance.pay` e uma OP `Paid`, **quando** clico "Estornar pagamento", informo o motivo e confirmo "Estornar pagamento" (estilo `destructive`), **então** as baixas ficam estornadas, transações inversas são criadas, os títulos voltam a `Open` (fora de OP), a OP fica `Reversed` com o motivo, e os membros do Proprietário são notificados; nada é apagado.
22. **[Segurança]** **Dado** `finance.pay` e uma OP `Paid`, **quando** anexo o comprovante, **então** o anexo (dono `PaymentOrder`) aceita só pdf, png e jpg até 10 MB, passa pelo ClamAV, só é aceito em OP `Paid` (`CanAttach`) e é lido com `finance.view`; em OP de outra situação, 409 "Anexe o comprovante depois do pagamento."

**Consulta, permissões e auditoria**
23. **Dado** `finance.view`, **quando** abro a lista de OPs, **então** vejo número, solicitante, total, data prevista, títulos e situação (Aguardando aprovação alerta, Aprovada ok, Reprovada crítico, Paga ok, Cancelada neutro, Estornada neutro), com "Aguardando você" filtrado para quem pode aprovar e o filtro "Aprovadas pelo próprio solicitante"; a OP mostra a linha do tempo das decisões.
24. **Dado** o módulo `finance` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; sem `finance.approve`, aprovar e reprovar respondem 403 e os botões não aparecem; sem `finance.pay`, pagar, estornar e anexar comprovante respondem 403. Todo endpoint tem `RequireModule` e `perm:`; `/{opId}/items/{itemId}` confere o vínculo (IDOR).
25. **Dado** qualquer ação na OP, **quando** abro "Histórico de alterações", **então** vejo criação, retrato, decisões, reenvio, pagamento, cancelamento e estorno com usuário, data e comentário (dados de pagamento mascarados).

## Regras de negócio e técnicas
- **Junção** `payment_order_items`: `payment_order_id`, `payable_id`, `amount` (principal a pagar, `numeric(18,2)`, ≤ saldo líquido), `expected_charges` (acréscimos previstos), `approved_total` (= `amount` + `expected_charges`), `payee_snapshot` (jsonb, `[PersonalData]`). `payment_orders.total` = Σ `approved_total`.
- **Situação do título derivada da OP:** `AwaitingApproval` (OP `Pending`), `Approved` (OP `Approved`), `PartiallyPaid`/`Paid` (baixas), `Open` (reprovada, cancelada ou estornada). Só os casos de uso da OP gravam esses estados, pela entidade rastreada (ADR 0019 §2, §8); um título está em no máximo uma OP não finalizada pela transição condicional dele, sem índice único parcial.
- **Situação da OP:** `Pending`, `Approved`, `Rejected`, `Paid`, `Cancelled`, `Reversed` (estorno).
- **Pagamento parcial:** o título fica `PartiallyPaid` e pode entrar numa nova OP pelo saldo.
- **Baixa** `payable_settlements` (simétrica à FIN-01): `payable_id`, `payment_order_id`, `bank_account_id`, `date`, `principal`, `interest`, `fine`, `discount`, `total`, `backdate_reason`, `transaction_id`, colunas de estorno. Invariante `paid_amount = Σ principal das baixas não estornadas`, medido contra o líquido.
- **Alçada nesta story:** regra padrão única "quem tem `finance.approve` aprova qualquer valor, em um nível" (`approval_rule_id` nulo). A FIN-03b substitui pelo motor de regras sem mudar os casos de uso.
- **Parâmetros do Finance** (ADR 0016 adendo M6): `finance.owner_can_approve_own` (padrão `false`) e `finance.payment_tolerance` (padrão R$ 0,00), em Configurações → Parâmetros (seção Financeiro), auditados.
- **Anexos por dono** (ADR 0019 §7, entregue aqui): `AttachmentOwnerRules` ganha `ReadAction`, `WriteAction`, `OwnerExists` obrigatório (teste de arquitetura) e `CanAttach`; o dono `PaymentOrder` é o primeiro a usar.
- **Datas** `date`; dinheiro `numeric(18,2)`; Guid v7; numeração `OP`; auditoria `IAuditable` em OP, itens, decisões e baixas; notificações do próprio Finance síncronas.

## Permissões, eventos e tabelas
- **Permissões (já no `ModuleCatalog`):** `finance.create` (criar, enviar e reenviar OP), `finance.edit` (cancelar a própria OP pendente), `finance.approve` (aprovar, reprovar, cancelar aprovada), `finance.pay` (pagar, estornar pagamento, anexar comprovante). **Seed patch** `finance-pay-financeiro` (ADR 0008 adendo D5).
- **Eventos (outbox, `Finance.Contracts`):** `PaymentOrderSubmitted` (`{ paymentOrderId, total, requestedBy }`) → notificação aos aprovadores; `PaymentOrderApproved` (`{ paymentOrderId }`) → notificação a quem paga; `PaymentOrderPaid` (`{ paymentOrderId, paidOn }`), sem consumidor nesta fase.
- **Tabelas:** `finance.payment_orders` (como no 03, sem `payable_ids`; mais `cancel_reason`, `submitted_at`, `self_approved`, `reversed_at`, `reversal_reason`, `client_request_id`; status com `Reversed`); `finance.payment_order_items` (nova); `finance.payment_order_approvals` (como no 03, mais `kind` Decision/Resubmission); `finance.payable_settlements` (nova); `finance.financial_transactions` (FIN-01). Anexo de comprovante: dono `PaymentOrder` (leitura `finance.view`, escrita `finance.pay`).
- **Destinatários das notificações:** `IUserDirectory` ganha `UsersWithPermissionAsync(permission)` e `GroupMemberCountAsync(systemGroup)`, calculados pela permissão efetiva, sem acesso às tabelas do Identity.

## Telas e estados
- **Financeiro → Ordens de pagamento** (`/app/financeiro/ordens-de-pagamento`; grupo "Caixa e pagamentos"): h1 "Nenhum pagamento sai sem ordem aprovada."; DataTable com filtros (situação, "Aguardando você", "Aprovadas pelo próprio solicitante", período).
- **Ordem de pagamento** (`/app/financeiro/ordens-de-pagamento/$id`): cabeçalho com número, chip e total; resumo em "facts"; abas na URL: Títulos (com a forma de pagamento e os avisos por item), Decisões, Comprovantes, Histórico de alterações. Botões conforme situação e permissão: "Aprovar", "Reprovar", "Pagar", "Cancelar ordem", "Estornar pagamento"; faixa de modo leitura em OP decidida.
- **Diálogos** (`size="lg"`, impacto e pré-condições): "Enviar para aprovação" / "Enviada para aprovação"; "Aprovar" / "Aprovada"; "Reprovar" / "Reprovada"; "Pagar" / "Pagamento registrado"; "Reenviar para aprovação" / "Reenviada para aprovação"; "Cancelar ordem" / "Ordem cancelada"; "Estornar pagamento" / "Pagamento estornado".
- **Estados:** carregando; vazio ("Selecione títulos em A pagar e crie a primeira ordem de pagamento." + "Ir para A pagar"); vazio em "Aguardando você" ("Nada para aprovar agora."); erro; modo leitura.
- **Propositivo:** títulos vencidos e que vencem nos próximos 7 dias aparecem pré-selecionáveis com o atalho "Selecionar os que vencem até {data}"; conta padrão e hoje no pagamento; notificação leva direto ao botão "Aprovar"; depois de aprovar, "Próxima OP aguardando você"; depois de pagar título com retenção, "Gerar título do recolhimento".
- **400 px:** colunas sempre visíveis: número, total, data prevista e situação; solicitante e títulos na linha expansível. E2E de 400 px e teclado do diálogo "Pagar" e da aprovação.
- **Ajuda:** `Ajuda/financeiro-ordens-de-pagamento.md` (`padrao: true`) e `Ajuda/financeiro-ordem-de-pagamento.md` (abas `titulos`, `decisoes`, `comprovantes`, `historico`); `Ajuda/configuracoes-parametros.md` ganha "Proprietário aprova a própria ordem" e "Tolerância no pagamento"; `<Dica>` em "Alçada", "Pagamento parcial", "Acréscimos previstos", "Total aprovado" e "Aprovada pelo próprio solicitante".

## Plano de testes
- **Unidade:** máquina de estados da OP e do título derivado (CA 2, 7, 8, 13, 20, 21); regra do solicitante com um e com dois membros no Proprietário (CA 9); limite do aprovado com tolerância (CA 14); total ao vivo (CA 15).
- **Integração (Testcontainers, obrigatória):** seed patch idempotente em tenant novo e existente (CA 1); criar OP com títulos disponíveis e indisponíveis e idempotência (CA 2, 3); retrato imutável depois de mudar o título (CA 4); notificações a aprovadores e pagadores (CA 5, 7, 8); solicitante não aprova, com Proprietário de um e de dois membros e com o parâmetro (CA 9, 10); concorrência de decisão e de pagamento (CA 11, 17); 2FA no login para quem aprova e paga (CA 12, regra existente); pagamento total e parcial com transações e baixas na mesma transação (CA 13); `exceeds_approved` e reenvio (CA 14); pagamento sem aprovação bloqueado em todos os caminhos (CA 16); data retroativa (CA 18); cancelar e estornar com `Reversed` (CA 20, 21); comprovante só em OP paga, tipos e tamanho (CA 22); três camadas e IDOR (CA 24); auditoria completa (CA 25).
- **Arquitetura:** só `PayPaymentOrderHandler` cria transação `Payment`; títulos não mudam para `AwaitingApproval`/`Approved`/`Paid` fora dos casos de uso da OP; todo dono de anexo registrado com `OwnerExists`.
- **E2E (Playwright):** financeiro cria OP, proprietário aprova pelo link da notificação vendo a forma de pagamento, financeiro paga; solicitante não vê "Aprovar" na própria OP; diálogo a 400 px.

## Fora do escopo
- Regras de alçada por valor e conta, multinível, fracionamento e aprovação pelo link do e-mail: FIN-03b.
- 2FA a cada ação e estorno com `finance.approve`: adiados (P-19, P-20).
- Ações em lote (aprovar várias OPs, R-PRP-6, D).
- Pagamento pelo banco (Pix/TED pela API do Inter): fora do MVP; o pagamento aqui é o registro da saída feita no banco. A BNK-03 concilia.
- Tarifas, IOF, juros e rendimentos "sem OP": FIN-04.

## Pontos em aberto
Decididos pelo PO em 20/10/2026 (seção 1.1 e D5, D6, D9):
1. Proprietário aprovando a própria OP: D6 (CA 9, 10).
2. Pagador diferente do aprovador não é exigido no MVP; os dois ficam registrados na OP.
3. Comprovante opcional, com o próximo passo "Anexar comprovante".
4. `finance.pay` para o Financeiro por seed patch; `finance.approve` só no Proprietário (D5, CA 1).
5. P-18 decidida (ADR 0021).

Adiados (P-19, P-20): 2FA a cada ação; estorno de pagamento com `finance.approve`.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers), arquitetura e E2E passando.
- [ ] Migration criada (não destrutiva); parâmetros e seed patch idempotentes.
- [ ] Permissões só do `ModuleCatalog`; dono de anexo `PaymentOrder` registrado com as regras novas.
- [ ] Telas com estados de carregando, vazio e erro; modo leitura.
- [ ] Ajuda da lista, de cada aba e dos parâmetros; teste de cobertura passando.
- [ ] Padrões comuns da Fase 2, próximos passos e confirmação de efeito financeiro aplicados.
- [ ] Auditoria completa ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` e `docs/04-fluxos-estados.md` (estados `PartiallyPaid` e `Reversed`) atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Entidades `PaymentOrder`, `PaymentOrderItem`, `PaymentOrderApproval`, `PayableSettlement`; máquina de estados com `Reversed`.
- [ ] `IApprovalPolicy` com a implementação padrão (substituída na FIN-03b) e a regra do solicitante (D6).
- [ ] `IUserDirectory.UsersWithPermissionAsync` e `GroupMemberCountAsync` (Identity); seed patch `finance-pay-financeiro`.
- [ ] `AttachmentOwnerRules` com `ReadAction`, `WriteAction`, `OwnerExists` obrigatório e `CanAttach` (BuildingBlocks).
- [ ] Endpoints `/api/v1/finance/payment-orders` (+ `/submit`, `/approve`, `/reject`, `/pay`, `/resubmit`, `/cancel`, `/reverse`, `/attachments`); telas e ajuda.

## Quality gates
- Agentes previstos: @dev, cybersecurity (segregação, retrato, limite do aprovado, concorrência, caminho único de pagamento), @qa.
- Gate: CA 4, 9, 11, 14, 16 e 17 obrigatórios e nunca ignorados.

## Validação do PO
Checklist do @po (Pax), 20/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fase 2).

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

- **Correções feitas na validação (19/10/2026):** quebra da FIN-03; texto de alçada do protótipo ilustrativo; junção com valor por título; `PartiallyPaid` do título a pagar; 2FA explícito; destinatários por permissão efetiva; ponto do `finance.pay` no Financeiro.
- **Correções da revalidação (20/10/2026):** seed patch de `finance.pay` (D5, CA 1); retrato do envio e pago = aprovado com tolerância e reenvio (§2.2-C1, CA 4, 14); para onde vai o dinheiro e avisos de fraude no aprovar e no pagar (D9, §2.2-A4, CA 6, 15); regra do solicitante pela D6 (CA 9, 10); data retroativa (D8, CA 18); "Gerar título do recolhimento" como sugestão (item 6, CA 19); estado `Reversed`; comprovante com as regras de anexo por dono (M1, CA 22); transição condicional do título (M3); idempotência; o 2FA ficou só na regra existente do login, sem pedido por ação (P-19), e o estorno continua com `finance.pay` (P-20); dependência da FIN-05 pela nova ordem. CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma pendente.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 19/10/2026 | 0.1 | Story detalhada (quebra da FIN-03) | River (SM) |
| 19/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 20/10/2026 | 0.3 | Decisões do PO (D5, D6, D8, D9, seção 1.1) e correções da revisão pré-Fase 2 (§2.1, §2.2-C1, A4, §2.3); adiamentos P-19 e P-20; CAs renumerados | River (SM) |
| 20/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
