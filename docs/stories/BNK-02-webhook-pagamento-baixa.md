# BNK-02 — Webhook de pagamento do Inter: reconsulta, baixa automática do título, transação e conferência diária

| Campo | Valor |
|---|---|
| ID | BNK-02 |
| Título | Recepção do aviso de pagamento do boleto pelo webhook do Inter (provedor `inter` na FND-16, token na URL), reconsulta obrigatória da cobrança pelo mTLS, baixa automática do título a receber com juros, multa e desconto e a transação na conta, na mesma transação e uma vez só; cadastro do webhook no banco; job diário de conferência dos boletos em aberto para o caso de o aviso falhar |
| Tamanho | G (re-estimada de M: cria o primeiro consumidor de webhook com efeito financeiro, o cadastro do webhook no banco e a conferência diária) |
| Status | Draft |
| Requisitos | R-FIN-7 (baixa automática por webhook) · R-FIN-3 (baixa com juros, multa e desconto) · R-FIN-2 (transação na conta) · R-TER-4 (pontualidade) · R-COM-2 · R-PRP-5 · R-SEC-5 |
| Dependências | FND-16 (rota, token, `webhook_events`, `TenantJob`), BNK-01a (`InterApiClient`), BNK-01b (`billing_slips`), FIN-01 (`ReceivableService` de baixa, `ReceivableSettled`), FIN-06b (pontualidade pelo `ReceivableSettled`) |
| Bloqueia | BNK-03b (conciliação reconhece a transação do boleto), FIN-09 (régua para de lembrar o que foi pago), GOL-01a |
| Ordem de execução | 5 (Marco 5.2 "Cobrança pelo Inter") |

## User story
Como **financeiro**, quero **que o título baixe sozinho quando o cliente paga o boleto ou o Pix**, para **o caixa e a inadimplência estarem certos sem eu conferir o extrato boleto por boleto**.

## Contexto e referências
- PRD R-FIN-7; `05-backlog` BNK-02 ("Recebe, valida, baixa título, cria transação, idempotente. CA: webhook repetido não baixa duas vezes; job de conferência diária consulta boletos em aberto (caso o webhook falhe)").
- `04-fluxos-estados` §2: `BillingSlipPaid` (Finance, webhook) → "Baixa receivable, cria transação, atualiza estatística de pontualidade"; §1 "Boleto (Inter)".
- Revisão pré-Fase 0 §3, "Webhook do Inter (S)": "o webhook só dispara uma reconsulta da cobrança via mTLS, nunca a baixa direta; URL com segredo; chave única para idempotência".
- FIN-01: baixa com principal, juros, multa e desconto, transação na mesma transação, invariante `paid_amount = Σ baixas`, limite de desconto (D8) e data retroativa.
- ADR 0019 §2 (transição pela entidade com `xmin`), §12 (jobs idempotentes, dois testes), §18 (notificação síncrona no módulo).

## Critérios de aceite
**Cadastrar o webhook no banco**
1. **Dado** a integração Inter testada (BNK-01a), **quando** o Proprietário clica "Ativar aviso de pagamento", **então** o sistema gera um token aleatório de 256 bits, guarda só o hash (FND-16 CA 5), registra no Inter a URL `https://{host da plataforma}/webhooks/inter/{tenantKey}?t={token}` (Pontos em aberto 1) pela API de webhook da cobrança, e mostra "Aviso de pagamento ativo desde dd/mm/aaaa"; o token em claro não é mostrado nem guardado depois do registro.
2. **[Segurança]** **Dado** o webhook ativo, **quando** o Proprietário clica "Trocar endereço do aviso", **então** um token novo é gerado e registrado no Inter; o antigo vale pela janela de sobreposição da FND-16 e depois é recusado; a troca é auditada sem valor e notificada.

**Receber e reconsultar**
3. **[Segurança]** **Dado** um POST na rota do Inter com token válido, **quando** chega, **então** a FND-16 grava o evento e responde 200; o handler do Finance lê do corpo **apenas** os identificadores (código da solicitação, seu número ou nosso número) e **reconsulta a cobrança no Inter pelo mTLS** (`IBankBillingProvider.GetAsync`) antes de qualquer efeito; situação, valor pago e data do pagamento vêm **da reconsulta**, nunca do corpo (teste: corpo com valor e situação falsos e reconsulta dizendo "a receber" → nenhum efeito, evento `Ignored` "Situação no banco: a receber").
4. **Dado** a reconsulta com a cobrança paga, **quando** o boleto local está `Issued`, **então** numa transação: o boleto vai a `Paid` com `paid_at` e `paid_amount`; a baixa do título é registrada pelo mesmo serviço da FIN-01 (`ReceivableService.SettleAsync`) com a conta da integração, a data do crédito informada pelo banco, principal = menor entre o valor pago e o saldo do título, juros e multa = a parte paga acima do saldo e desconto = a diferença quando pagou menos dentro da data de desconto; a transação `Receipt` nasce na conta; o `ReceivableSettled` vai para o outbox; o actor da auditoria é o usuário de sistema "Banco Inter (aviso de pagamento)" com o id do evento.
5. **Dado** o mesmo aviso repetido, a reconsulta diária (CA 8) e o aviso chegando juntos, ou dois avisos de entregas diferentes para a mesma cobrança, **quando** processam, **então** o título é baixado **uma vez só**: deduplicação na FND-16 por id de entrega, inbox por handler e a transição `Issued → Paid` do boleto pela entidade com `xmin`; o segundo processamento termina `Ignored` "Boleto já baixado" (teste de concorrência com 10 execuções paralelas).
6. **Dado** um pagamento a menor fora da data de desconto, um pagamento a maior além de juros e multa plausíveis, ou um boleto local `Cancelled`/`Expired` que aparece pago no banco, **quando** processa, **então** a baixa **não** é automática: o boleto fica `PaidPendingReview` (proposta), o valor entra como transação `Receipt` na conta com a marca "Recebido do Inter, aguardando conferência" e vínculo ao título (Pontos em aberto 2), e quem tem `finance.edit` recebe a notificação com link para "Conferir pagamento", onde escolhe "Baixar com desconto" (sujeito ao limite da D8, com `finance.approve` acima dele), "Baixar com juros" ou "Deixar saldo em aberto".
7. **Dado** um aviso de pagamento de cobrança desconhecida (não emitida pelo BemAqui, ex.: boleto feito direto no Internet Banking), **quando** a reconsulta confirma o pagamento, **então** nada é baixado; o evento fica `Ignored` "Cobrança fora do sistema" e a entrada aparece para a conciliação (BNK-03b).

**Conferência diária**
8. **Dado** boletos `Issued` (e `Requested` há mais de 1 h), **quando** o recorrente global `inter-billing-reconcile` roda (proposta: 07:00 e 13:00 de São Paulo), **então** cada tenant com integração consulta no Inter as cobranças pagas, canceladas e expiradas desde a última execução (com 2 dias de sobreposição) e aplica as mesmas transições dos CA 4, 6 e 7 pelo mesmo serviço; job idempotente, com teste de execução dupla e de dois tenants; o relatório da execução (consultados, baixados, divergentes) fica no painel de integração.
9. **Dado** o Inter fora do ar, token recusado ou certificado vencido, **quando** o job ou o handler falham, **então** o evento ou o job seguem o backoff; depois de 3 execuções seguidas sem sucesso do job, sai o alerta `bank_integration` (FND-12) e a notificação aos Proprietários e a quem tem `finance.edit`, com o link para a integração; nenhum título é alterado.

**Efeitos e visibilidade financeira**
10. **Dado** a baixa automática, **quando** gravada, **então** o título fica `Paid` (ou `PartiallyPaid`), o fluxo de caixa realizado (FIN-05) e o extrato (FIN-04) mostram a entrada no mesmo dia, a inadimplência (FIN-06a) tira o título, a pontualidade (FIN-06b) usa a data do crédito, e a COM-04b conclui o contrato quando for o último título; quem tem `finance.view` recebe no sino (com `dedup_key = slip-paid:{boletoId}`) "Recebido R$ {valor} de {cliente} · título {número}" com link para o título, num resumo por hora quando passar de 5 avisos (proposta).
11. **Dado** uma baixa automática, **quando** alguém quer estornar (FIN-01, `finance.edit`), **então** o diálogo avisa "Este pagamento foi confirmado pelo Inter. Estorne só se o banco devolveu o valor." e exige motivo (Pontos em aberto 3: exigir `finance.approve`).

**Permissões e auditoria**
12. **Dado** o módulo `finance` não liberado, **quando** chamo as telas de integração e conferência, **então** 403 `module_not_released`; ativar e trocar o aviso exigem `settings.edit` (Proprietário); conferir pagamento exige `finance.edit`; ver, `finance.view`; `RequireModule` e `perm:`; IDOR em `/billing-slips/{id}/review`.
13. **Dado** qualquer baixa automática, conferência ou divergência, **quando** abro o histórico do título e do boleto, **então** vejo o evento de origem (id), o actor de sistema, os valores da reconsulta e a decisão de quem conferiu.

## Regras de negócio e técnicas
- **Provedor `inter`** na FND-16: `UrlTokenValidator`, `Content-Type` JSON, id de entrega = o identificador da cobrança + situação + data do evento (o Inter não manda id de entrega próprio; Pontos em aberto 4), fila `default`.
- **Nunca confiar no corpo:** o corpo só identifica a cobrança; tudo o mais vem da reconsulta por mTLS.
- **Baixa** pelo serviço da FIN-01 (mesmas validações, invariante e `ReceivableSettled`), sem caminho paralelo; a data da baixa é a do crédito (pode ser retroativa sem pedir motivo, por ser do banco).
- **Divergência** (CA 6): `billing_slips.status = PaidPendingReview` e transação `Receipt` com `receivable_id` e `pending_review = true` (proposta), para o saldo da conta bater com o banco sem decidir pelo usuário.
- **Job** `inter-billing-reconcile`: recorrente global; enfileira por tenant com integração ativa; consulta por período.

## Permissões, eventos e tabelas
- **Permissões:** `settings.edit` (ativar e trocar o aviso), `finance.edit` (conferir divergência), `finance.view` (ver). Nenhuma ação nova.
- **Eventos:** `BillingSlipPaid` (interno ao Finance, síncrono na transação, ADR 0019 §18); `ReceivableSettled` (já existe) para o Commercial.
- **Tabelas:** `finance.billing_slips` (status `PaidPendingReview`, `paid_at`, `paid_amount`, `credit_date`); `finance.bank_integrations` (+ `webhook_token_hash`, `previous_webhook_token_hash`, `webhook_registered_at`, `last_reconcile_at`); `finance.financial_transactions.pending_review` (proposta).
- **Jobs:** handler do provedor `inter`; recorrente global `inter-billing-reconcile`.

## Telas e estados
- **Integração bancária → conta → "Aviso de pagamento"**: situação, última entrega, última conferência e o relatório; botões "Ativar aviso de pagamento" e "Trocar endereço do aviso".
- **Financeiro → A receber → "Pagamentos a conferir"** (`/app/financeiro/receber/pagamentos-a-conferir`): lista dos `PaidPendingReview` com título, cliente, valor do boleto, valor pago, data e diferença; diálogo "Conferir pagamento" com o impacto (saldo antes e depois).
- **Verbos:** "Ativar aviso de pagamento" / "Aviso de pagamento ativo"; "Conferir pagamento" → "Baixar com desconto" / "Pagamento conferido".
- **Estados:** carregando; vazio ("Nenhum pagamento a conferir. Os boletos pagos com diferença aparecem aqui."); erro; aviso não ativo com faixa âmbar "Sem o aviso de pagamento, os boletos pagos só baixam na conferência das 7h e das 13h."
- **400 px:** título, valor pago e diferença sempre visíveis.
- **Ajuda:** `Ajuda/financeiro-pagamentos-a-conferir.md`; a ajuda da integração ganha "Aviso de pagamento"; `<Dica>` em "Aguardando conferência".

## Plano de testes
- **Unidade:** divisão do valor pago em principal, juros, multa e desconto (CA 4, 6) com tabela de casos.
- **Integração (Testcontainers + fake do Inter):** registro do webhook e troca do token (CA 1, 2); corpo falso com reconsulta verdadeira (CA 3); baixa com transação, `ReceivableSettled` e actor de sistema (CA 4); repetição e 10 execuções paralelas (CA 5); divergências (CA 6); cobrança desconhecida (CA 7); conferência diária com execução dupla e dois tenants e webhook perdido (CA 8); Inter fora do ar sem alterar título (CA 9); efeitos em FIN-05, FIN-06a e COM-04b com dataset fixo (CA 10); estorno com aviso (CA 11); três camadas (CA 12); auditoria (CA 13).
- **Contrato (sandbox, manual):** pagamento simulado no sandbox do Inter chegando no webhook da homologação (tenant `demo`).
- **E2E (Playwright):** boleto pago pelo fake → título baixado na tela; conferir um pagamento a menor.

## Fora do escopo
- Conciliação geral do extrato: BNK-03a/03b. Pix avulso sem boleto: fora do MVP.
- Devolução de pagamento ao cliente.

## Pontos em aberto
1. **Formato da URL aceito pelo Inter** (arquiteto, sandbox): se o cadastro do webhook não aceitar query string, o token vai no caminho e o log `bqe_masked` precisa mascarar o último segmento da location `/webhooks/inter/` (muda o vhost, ADR 0015).
2. **Pagamento com diferença** (PO/financeiro): proposta do CA 6 (entra no caixa, aguarda conferência); alternativa: baixa automática com desconto ou juros sempre.
3. **Estorno de baixa confirmada pelo banco** (PO, ligado à P-20): exigir `finance.approve`? Proposta: sim, por ser dinheiro que o banco confirmou.
4. **Id de entrega do Inter** (arquiteto, sandbox): confirmar se o aviso traz um identificador próprio; senão, a chave composta do CA de regras.
5. **Resumo das notificações** (PO): por hora acima de 5 avisos, ou um a um.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers + fake) e E2E passando; pagamento simulado no sandbox registrado.
- [ ] Migration criada (não destrutiva), testada sobre cópia do banco do tenant `konebras`.
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Telas com estados, inclusive a 400 px.
- [ ] Ajuda escrita; teste de cobertura passando.
- [ ] Alertas acionáveis; efeito financeiro automático só com reconsulta e conferência humana na divergência (R-PRP-7).
- [ ] Auditoria ligada (actor de sistema com o id do evento).
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` e `docs/04-fluxos-estados.md` (boleto `PaidPendingReview`) atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Registro do provedor `inter` na FND-16 e `InterWebhookHandler` (Finance).
- [ ] `BillingSlipSettlement` (divisão do valor) e uso do `ReceivableService.SettleAsync`.
- [ ] Cadastro do webhook no Inter; recorrente `inter-billing-reconcile`.
- [ ] Tela "Pagamentos a conferir", diálogo e ajuda.

## Quality gates
- Agentes previstos: @dev, cybersecurity (reconsulta, token, concorrência), @qa.
- Gate: CA 3, 4, 5, 6 e 8 obrigatórios.

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

- **Correções feitas na validação:** baixa só pela reconsulta (CA 3), como pede a revisão pré-Fase 0; pagamento com diferença não é decidido pelo sistema (CA 6, R-PRP-7); re-estimada para G.
- **Pré-condições para `Ready`:** pontos em aberto 1, 2 e 4 (os dois de sandbox se resolvem no início da story).
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
