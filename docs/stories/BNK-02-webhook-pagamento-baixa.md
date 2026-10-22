# BNK-02 — Webhook de pagamento do Inter: reconsulta, baixa automática do título, transação e conferência diária

| Campo | Valor |
|---|---|
| ID | BNK-02 |
| Título | Recepção do aviso de pagamento do boleto pelo webhook do Inter (provedor `inter` na FND-16, token na URL guardado só como hash), reconsulta obrigatória da cobrança pelo mTLS, baixa automática do título a receber com juros, multa e desconto e a transação na conta, na mesma transação e uma vez só; pagamento com diferença em conferência humana; cadastro do webhook no banco pela seção "Aviso de pagamento" de Configurações → Integrações → Banco Inter; conferência diária dos boletos em aberto; estorno de baixa bancária e de pagamento da OP com `finance.approve` (P-20) |
| Tamanho | G (re-estimada de M: cria o primeiro consumidor de webhook com efeito financeiro, o cadastro do webhook no banco e a conferência diária) |
| Status | Ready (pré-condição externa) |
| Requisitos | R-FIN-7 (baixa automática por webhook) · R-FIN-3 (baixa com juros, multa e desconto) · R-FIN-2 (transação na conta) · R-FIN-5 (estorno de pagamento) · R-TER-4 (pontualidade) · R-COM-2 · R-PRP-5 · R-PRP-7 · R-SEC-5 |
| Dependências | FND-16 (rota, `UrlTokenValidator`, `webhook_events`, `TenantJob`), BNK-01a (`InterApiClient`, `RequireRecentTwoFactor`, tela Banco Inter), BNK-01b (`billing_slips`, `ConfirmDialog`), FIN-01 (`ReceivableService` de baixa e estorno, `ReceivableSettled`), FIN-03a (estorno do pagamento da OP), FIN-06b (pontualidade pelo `ReceivableSettled`) |
| Bloqueia | BNK-03b (conciliação reconhece a transação do boleto), FIN-09 (régua para de lembrar o que foi pago) |
| Ordem de execução | 4 (Marco 5.2 "Cobrança pelo Inter") |

## User story
Como **financeiro**, quero **que o título baixe sozinho quando o cliente paga o boleto ou o Pix**, para **o caixa e a inadimplência estarem certos sem eu conferir o extrato boleto por boleto**.

## Contexto e referências
- PRD R-FIN-7; `05-backlog` BNK-02 ("Recebe, valida, baixa título, cria transação, idempotente. CA: webhook repetido não baixa duas vezes; job de conferência diária consulta boletos em aberto (caso o webhook falhe)").
- **ADR 0031** (recepção de webhooks), **ADR 0032** (Banco Inter: token na URL, reconsulta por mTLS), **ADR 0008 adendo de 21/10/2026** (D6: 2FA de novo em estornar e salvar credenciais; estorno de pagamento e de baixa bancária com `finance.approve`), ADR 0034 (Inter de produção só na GOL-01b).
- **Revisão pré-Fases 4 a 8** (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`): D6, D13; tabela 3.2, pontos 23 a 27; §4.1 (`02` sem o tenant pelo nome na URL); §4.4 (padrão 6 e achado "Pagamentos a conferir" vira filtro).
- `06-pendencias-riscos` P-20 (estorno com `finance.approve`, decidida pela D6).
- `04-fluxos-estados` §2: `BillingSlipPaid` (Finance, webhook) → "Baixa receivable, cria transação, atualiza estatística de pontualidade"; §1 "Boleto (Inter)".
- Revisão pré-Fase 0 §3: "o webhook só dispara uma reconsulta da cobrança via mTLS, nunca a baixa direta; URL com segredo; chave única para idempotência".
- FIN-01: baixa com principal, juros, multa e desconto, transação na mesma transação, invariante `paid_amount = Σ baixas`, limite de desconto e data retroativa. FIN-03a CA 21: estorno da OP paga com `finance.pay` (muda aqui).
- ADR 0019 §2 (transição pela entidade com `xmin`), §12 (jobs idempotentes, dois testes), §18 (notificação síncrona no módulo).

## Critérios de aceite
**Cadastrar o webhook no banco**
1. **Dado** a integração Inter testada (BNK-01a) e um membro do Proprietário (`settings.edit`), **quando** clica "Ativar aviso de pagamento" na seção "Aviso de pagamento" de Configurações → Integrações → Banco Inter e passa pelo **2FA de novo** (`RequireRecentTwoFactor`), **então** o sistema gera um token aleatório de 256 bits, guarda só o hash (`shared.webhook_tokens`, FND-16 CA 6), registra no Inter pela API de webhook da cobrança a URL `https://{host da plataforma}/webhooks/inter/{tenantKey}?t={token}` (`tenantKey` opaco, FND-16 CA 2) e mostra "Aviso de pagamento ativo desde dd/mm/aaaa"; a URL completa aparece **uma única vez**, com "Copiar", para o caso de o cadastro no Internet Banking ser manual; depois só a máscara (`…/webhooks/inter/••••?t=••••`).
2. **Dado** a confirmação no sandbox (pré-condição externa) de que o Inter **não** aceita query string na URL do webhook, **quando** o aviso é ativado, **então** o provedor `inter` usa o modo `PathToken` da FND-16 (`/webhooks/inter/{tenantKey}/{token}`) e o vhost mascara o último segmento da location `/webhooks/inter/` no `access_log` e no `error_log` (ADR 0015); a escolha fica registrada no runbook e no registro do provedor, coberta por teste nos dois modos.
3. **[Segurança]** **Dado** o webhook ativo, **quando** o Proprietário clica "Trocar endereço do aviso" (com 2FA de novo), **então** um token novo é gerado e registrado no Inter; o antigo vale pelas 24 h de sobreposição da FND-16 e depois é recusado; a troca é auditada sem valor e notificada.
4. **Dado** a conta em produção sem `bank_production_allowed` (BNK-01a CA 12), **quando** tento ativar o aviso, **então** 409 `bank_production_not_allowed`; em sandbox, o aviso aponta para a homologação (tenant `demo`).

**Receber e reconsultar**
5. **[Segurança]** **Dado** um POST na rota do Inter com token válido, **quando** chega, **então** a FND-16 grava o evento e responde 200; o handler do Finance lê do corpo **apenas** os identificadores (código da solicitação, seu número ou nosso número) e **reconsulta a cobrança no Inter pelo mTLS** (`IBankBillingProvider.GetAsync`) antes de qualquer efeito; situação, valor pago e data do pagamento vêm **da reconsulta**, nunca do corpo (teste: corpo com valor e situação falsos e reconsulta dizendo "a receber" → nenhum efeito, evento `Ignored` "Situação no banco: a receber").
6. **[Segurança]** **Dado** o provedor `inter`, **quando** um aviso é registrado, **então** a deduplicação usa a **chave composta** (identificador da cobrança + situação + data do evento) como `external_id` **e** o SHA-256 do corpo (FND-16 CA 10); se o sandbox mostrar que o aviso traz um id de entrega próprio, ele passa a ser o `external_id` e a chave composta fica como segunda verificação no handler.
7. **Dado** a reconsulta com a cobrança paga, **quando** o boleto local está `Issued`, **então** numa transação: o boleto vai a `Paid` com `paid_at`, `paid_amount` e `credit_date`; a baixa do título é registrada pelo mesmo serviço da FIN-01 (`ReceivableService.SettleAsync`) com a conta da integração, a data do crédito informada pelo banco, principal = menor entre o valor pago e o saldo do título, juros e multa = a parte paga acima do saldo dentro do calculado pelos percentuais do boleto, e desconto = a diferença quando pagou menos dentro da data de desconto; a transação `Receipt` nasce na conta; o `ReceivableSettled` vai para o outbox; o actor da auditoria é o usuário de sistema "Banco Inter (aviso de pagamento)" com o id do evento.
8. **Dado** o mesmo aviso repetido, a conferência diária (CA 11) e o aviso chegando juntos, ou dois avisos de entregas diferentes para a mesma cobrança, **quando** processam, **então** o título é baixado **uma vez só**: deduplicação na FND-16, inbox por handler e a transição `Issued → Paid` do boleto pela entidade com `xmin`; o segundo processamento termina `Ignored` "Boleto já baixado" (teste de concorrência com 10 execuções paralelas).
9. **[Segurança]** **Dado** um pagamento a menor fora da data de desconto, um pagamento a maior além dos juros e da multa calculados, ou um boleto local `Cancelled`/`Expired` que aparece pago no banco, **quando** processa, **então** a baixa **não** é automática: o boleto fica `PaidPendingReview`, o valor entra como transação `Receipt` na conta com `pending_review = true`, a marca "Recebido do Inter, aguardando conferência" e o vínculo ao título (o saldo da conta bate com o banco sem decidir pelo usuário), e quem tem `finance.edit` recebe a notificação com link para "Conferir pagamento", onde escolhe "Baixar com desconto" (desconto acima do limite da FIN-01 só com `finance.approve`), "Baixar com juros" ou "Deixar saldo em aberto", sempre no diálogo com o impacto (saldo antes e depois).
10. **Dado** um aviso de pagamento de cobrança desconhecida (não emitida pelo BemAqui, ex.: boleto feito direto no Internet Banking), **quando** a reconsulta confirma o pagamento, **então** nada é baixado; o evento fica `Ignored` "Cobrança fora do sistema" e a entrada aparece para a conciliação (BNK-03b).

**Conferência diária**
11. **Dado** boletos `Issued` (e `Requested` há mais de 1 h), **quando** o recorrente global `inter-billing-reconcile` roda (07:00 e 13:00 de São Paulo, enfileirando um job por `tenantId` com integração ativa e módulo `finance` liberado), **então** cada tenant consulta no Inter as cobranças pagas, canceladas e expiradas desde a última execução (com 2 dias de sobreposição) e aplica as mesmas transições dos CA 7, 9 e 10 pelo mesmo serviço; job idempotente, com teste de execução dupla e de dois tenants; o relatório da execução (consultados, baixados, divergentes) fica na seção "Aviso de pagamento".
12. **Dado** o Inter fora do ar, token recusado ou certificado vencido, **quando** o job ou o handler falham, **então** o evento ou o job seguem o backoff; depois de 3 execuções seguidas sem sucesso do job, sai o alerta `bank_integration` (FND-12) e a notificação aos Proprietários e a quem tem `finance.edit`, com o link para a integração; nenhum título é alterado.

**Efeitos e visibilidade financeira**
13. **Dado** a baixa automática, **quando** gravada, **então** o título fica `Paid` (ou `PartiallyPaid`), o fluxo de caixa realizado (FIN-05) e o extrato (FIN-04) mostram a entrada no mesmo dia, a inadimplência (FIN-06a) tira o título, a pontualidade (FIN-06b) usa a data do crédito, e a COM-04b conclui o contrato quando for o último título; quem tem `finance.view` recebe no sino "Recebido R$ {valor} de {cliente} · título {número}" com link para o título (`dedup_key = slip-paid:{boletoId}`); **acima de 5 avisos na mesma hora**, os seguintes viram um resumo por hora ("{n} boletos pagos nesta hora · R$ {total}") com link para a lista filtrada.

**Estornos com `finance.approve` (P-20, D6)**
14. **[Segurança]** **Dado** uma baixa feita pelo aviso do Inter (ou pela conferência diária), **quando** alguém quer estorná-la (FIN-01), **então** o estorno exige **`finance.approve`** e **2FA de novo**, motivo obrigatório e o `ConfirmDialog` com o aviso "Este pagamento foi confirmado pelo Inter. Estorne só se o banco devolveu o valor."; sem `finance.approve`, o botão some e a API responde 403; o estorno é notificado aos membros do Proprietário.
15. **[Segurança]** **Dado** uma OP paga (FIN-03a), **quando** alguém a estorna, **então** passa a exigir **`finance.approve`** (antes `finance.pay`, FIN-03a CA 21) além do 2FA de novo já aplicado pela BNK-01a; o título estornado volta a ficar disponível e, ao entrar numa nova OP, aparece com a marca "pago e estornado em {data}" para o aprovador decidir de novo; teste de regressão do fluxo de estorno da FIN-03a.

**Permissões e auditoria**
16. **Dado** o módulo `finance` não liberado, **quando** chamo as telas de integração e conferência, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; ativar e trocar o aviso exigem `settings.edit` + 2FA de novo (Proprietário); conferir pagamento exige `finance.edit`; estornar baixa do banco ou pagamento de OP, `finance.approve` + 2FA de novo; ver, `finance.view`; `RequireModule` e `perm:`; IDOR em `/billing-slips/{id}/review`.
17. **Dado** qualquer baixa automática, conferência, divergência ou estorno, **quando** abro o histórico do título e do boleto, **então** vejo o evento de origem (id), o actor de sistema, os valores da reconsulta e a decisão de quem conferiu ou estornou.

## Regras de negócio e técnicas
- **Provedor `inter`** na FND-16: `UrlTokenValidator` (modo `Query`, ou `PathToken` pelo CA 2), `Content-Type` JSON, `external_id` pela chave composta (cobrança + situação + data) ou o id de entrega se o sandbox mostrar que existe, fila `default`.
- **Nunca confiar no corpo:** o corpo só identifica a cobrança; tudo o mais vem da reconsulta por mTLS.
- **Baixa** pelo serviço da FIN-01 (mesmas validações, invariante e `ReceivableSettled`), sem caminho paralelo; a data da baixa é a do crédito (pode ser retroativa sem pedir motivo, por ser do banco).
- **Divergência** (CA 9): `billing_slips.status = PaidPendingReview` e transação `Receipt` com `receivable_id` e `pending_review = true`.
- **Divisão do valor** (`BillingSlipSettlement`, puro): juros e multa calculados pelos percentuais gravados no boleto e pela data do crédito; o que passar disso é divergência.
- **Job** `inter-billing-reconcile`: recorrente global; enfileira por tenant com integração ativa e módulo liberado; consulta por período.
- **Estorno do pagamento da OP:** a mudança de `finance.pay` para `finance.approve` é do serviço da FIN-03a (melhoria do que está no ar, P-20).

## Permissões, eventos e tabelas
- **Permissões:** `settings.edit` + 2FA de novo (ativar e trocar o aviso), `finance.edit` (conferir divergência), `finance.approve` + 2FA de novo (estornar baixa do banco e pagamento de OP), `finance.view` (ver). Nenhuma ação nova.
- **Eventos:** `BillingSlipPaid` (interno ao Finance, síncrono na transação, ADR 0019 §18); `ReceivableSettled` (já existe) para o Commercial.
- **Tabelas:** `finance.billing_slips` (status `PaidPendingReview`, `paid_at`, `paid_amount`, `credit_date`); `finance.bank_integrations` (+ `webhook_registered_at`, `webhook_mode`, `last_reconcile_at`, `last_reconcile_report`); tokens em `shared.webhook_tokens` (FND-16); `finance.financial_transactions.pending_review` (nova coluna); `finance.payables` ou a OP com a marca de "pago e estornado" (`reversed_payment_at`).
- **Jobs:** handler do provedor `inter`; recorrente global `inter-billing-reconcile`.

## Telas e estados
- **Padrões comuns das Fases 4 a 8:** 6 (integração assíncrona: situação do aviso e da conferência), 7 (`ConfirmDialog` no estorno).
- **Configurações → Integrações → Banco Inter → conta → seção "Aviso de pagamento"** (tela da BNK-01a): situação, modo do endereço, última entrega, última conferência e o relatório; botões "Ativar aviso de pagamento" e "Trocar endereço do aviso".
- **Financeiro → A receber, filtro "Pagamentos a conferir"** (`/app/financeiro/receber?filtro=a-conferir`, filtro da lista e não aba própria, pelo achado da §4.4 sobre o Financeiro a 400 px): os `PaidPendingReview` com título, cliente, valor do boleto, valor pago, data e diferença; diálogo "Conferir pagamento" com o impacto (saldo antes e depois); o filtro mostra a contagem quando houver algum.
- **Verbos:** "Ativar aviso de pagamento" / "Aviso de pagamento ativo"; "Trocar endereço do aviso" / "Endereço trocado"; "Conferir pagamento" → "Baixar com desconto" / "Pagamento conferido"; "Estornar baixa" / "Baixa estornada".
- **Estados:** carregando; vazio do filtro ("Nenhum pagamento a conferir. Os boletos pagos com diferença aparecem aqui."); erro; aviso não ativo com faixa âmbar "Sem o aviso de pagamento, os boletos pagos só baixam na conferência das 7h e das 13h." e a ação "Ativar aviso de pagamento" (só para `settings.edit`).
- **Propositivo:** a notificação de divergência abre direto o diálogo de conferência com a opção mais provável pré-selecionada (desconto dentro do limite, se pagou a menor na data); o resumo por hora leva à lista filtrada.
- **400 px:** título, valor pago e diferença sempre visíveis; diálogos em tela cheia; botões de 44 px.
- **Ajuda:** `Ajuda/configuracoes-integracoes-inter.md` ganha a seção "Aviso de pagamento"; `Ajuda/financeiro-receber.md` ganha o filtro "Pagamentos a conferir"; `<Dica>` em "Aguardando conferência" e em "pago e estornado".

## Plano de testes
- **Unidade:** divisão do valor pago em principal, juros, multa e desconto (CA 7, 9) com tabela de casos; chave composta (CA 6).
- **Integração (Testcontainers + fake do Inter):** registro do webhook com 2FA de novo, URL mostrada uma vez (CA 1); modos `Query` e `PathToken` (CA 2); troca do token (CA 3); produção sem a flag (CA 4); corpo falso com reconsulta verdadeira (CA 5); dedup pela chave composta e pelo hash (CA 6); baixa com transação, `ReceivableSettled` e actor de sistema (CA 7); repetição e 10 execuções paralelas (CA 8); divergências (CA 9); cobrança desconhecida (CA 10); conferência diária com execução dupla, dois tenants e webhook perdido (CA 11); Inter fora do ar sem alterar título (CA 12); efeitos em FIN-05, FIN-06a e COM-04b com dataset fixo e resumo por hora (CA 13); estorno de baixa com `finance.approve` e 2FA (CA 14); regressão do estorno da OP com `finance.approve` e a marca na nova OP (CA 15); três camadas (CA 16); auditoria (CA 17).
- **Contrato (sandbox, manual, tenant `demo`):** pagamento simulado chegando no webhook da homologação; confirmação da query string e do id de entrega, registrados no runbook.
- **E2E (Playwright):** boleto pago pelo fake → título baixado na tela; conferir um pagamento a menor pelo filtro; tela a 400 px.

## Fora do escopo
- Conciliação geral do extrato: BNK-03a/03b. Pix avulso sem boleto: fora do MVP.
- Devolução de pagamento ao cliente.

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fases 4 a 8, tabela 3.2, pontos 23–27):
1. Confirmar no sandbox se o Inter aceita query; se não, token no caminho com máscara do último segmento (CA 1, 2).
2. Pagamento com diferença vai para conferência humana (CA 9).
3. Estorno de baixa com `finance.approve` (fecha a P-20, D6), e também o estorno do pagamento da OP (CA 14, 15).
4. Chave composta (cobrança, situação, data) mais o SHA-256 do corpo; confirmar o id de entrega no sandbox (CA 6).
5. Resumo por hora acima de 5 avisos (CA 13).

Decisões da revisão aplicadas: D6 (2FA de novo em ativar e trocar o aviso e nos estornos; P-20 com `finance.approve` e a marca "pago e estornado em {data}", CA 1, 3, 14, 15); D13 (token só em hash, reconsulta por mTLS, `tenantKey` opaco, CA 1, 5, 6); D1 (Inter de produção só na GOL-01b, CA 4); §4.1 (URL sem o tenant pelo nome); §4.4 ("Pagamentos a conferir" como filtro da lista A receber; padrões 6 e 7).

**Pré-condição externa:** confirmação no sandbox do Inter (query string na URL do webhook, id de entrega próprio, política de reenvio); a story implementa os dois modos e a chave composta, então não bloqueia o `Ready`.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers + fake) e E2E passando; regressão do estorno da FIN-03a; pagamento simulado no sandbox registrado.
- [ ] Migration criada (não destrutiva), testada sobre cópia do banco do tenant `konebras`.
- [ ] Permissões só do `ModuleCatalog`; estornos com `finance.approve` e 2FA de novo no teste de arquitetura da D6.
- [ ] Telas com estados, inclusive a 400 px.
- [ ] Ajudas atualizadas; teste de cobertura passando.
- [ ] Alertas acionáveis; efeito financeiro automático só com reconsulta e conferência humana na divergência (R-PRP-7).
- [ ] Auditoria ligada (actor de sistema com o id do evento).
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` e `docs/04-fluxos-estados.md` (boleto `PaidPendingReview`; estorno com `finance.approve`) atualizados; vhost com a máscara se o modo `PathToken` for o escolhido; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Registro do provedor `inter` na FND-16 (modo do token, chave composta) e `InterWebhookHandler` (Finance).
- [ ] `BillingSlipSettlement` (divisão do valor) e uso do `ReceivableService.SettleAsync`.
- [ ] Cadastro do webhook no Inter; seção "Aviso de pagamento"; recorrente `inter-billing-reconcile`.
- [ ] Filtro "Pagamentos a conferir", diálogo de conferência; estornos com `finance.approve` e 2FA de novo (FIN-01 e FIN-03a); ajudas.

## Quality gates
- Agentes previstos: @dev, cybersecurity (reconsulta, token, concorrência, estornos), @qa.
- Gate: CA 2, 5, 6, 7, 8, 9, 11, 14 e 15 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fases 4 a 8).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK (ajustado: R-FIN-5 pelo estorno da OP) |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK (ajustado: estornos com `finance.approve` e 2FA de novo) |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: ordem 4; a GOL-01a passa ao marco final) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs | OK (ADRs 0031, 0032, adendo 0008) |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK (ajustado: filtro "Pagamentos a conferir") |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** baixa só pela reconsulta, como pede a revisão pré-Fase 0; pagamento com diferença não é decidido pelo sistema (R-PRP-7); re-estimada para G.
- **Correções da revalidação (21/10/2026):** pontos 23 a 27 decididos (CA 1, 2, 6, 9, 13, 14); token só em hash com a URL mostrada uma vez e o modo `PathToken` com máscara (CA 1, 2); 2FA de novo para ativar e trocar o aviso (CA 1, 3); produção bloqueada até a GOL-01b (CA 4); dedup pela chave composta e pelo SHA-256 (CA 6); P-20: estorno de baixa do banco e do pagamento da OP com `finance.approve` e 2FA de novo, com a marca "pago e estornado em {data}" (CA 14, 15); "Pagamentos a conferir" virou filtro da lista A receber; seção "Aviso de pagamento" na tela de Configurações da BNK-01a; GOL-01a tirada do "Bloqueia" (passa ao marco final pela WPP-01); CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma de decisão de story. Pré-condição externa explícita: confirmações no sandbox do Inter, sem bloquear.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready (pré-condição externa)`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (D1, D6, D13, P-20, tabela 3.2 pontos 23–27, §4.1, §4.4); nova ordem (Marco 5.2, ordem 4); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
