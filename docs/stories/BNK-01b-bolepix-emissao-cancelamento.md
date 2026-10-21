# BNK-01b — Bolepix a partir do título a receber: emissão, PDF, linha digitável, Pix copia-e-cola, e-mail e cancelamento

| Campo | Valor |
|---|---|
| ID | BNK-01b |
| Título | Emissão do boleto com Pix (bolepix) do Banco Inter a partir de um título a receber em aberto, com juros, multa e desconto; guarda do PDF, da linha digitável, do código de barras e do Pix copia-e-cola; envio ao cliente por e-mail pela fila; cancelamento e segunda via; falha de emissão nunca perde nem altera o título |
| Tamanho | G (quebra da BNK-01) |
| Status | Draft |
| Requisitos | R-FIN-7 (emissão de bolepix, envio por e-mail) · R-FIN-3 (título a receber) · R-COM-1 (e-mail por fila) · R-COM-3 (PDF anexado) · R-PRP-1 · R-PRP-2 · R-PRP-7 · R-SEC-5 |
| Dependências | BNK-01a (`InterApiClient`, `IBankBillingProvider`), FIN-01 (títulos, `finance.parameters` com `late_fine_pct` e `late_interest_month_pct`), COM-03 (e-mail com anexo, `StoreGeneratedAsync`), CAD-01a (endereço de cobrança e contato que recebe cobrança), FND-09e (diálogo financeiro, idempotência) |
| Bloqueia | BNK-02 (baixa pelo webhook), FIN-09 (régua de cobrança com o boleto), POR-01 (Fase 8, boletos no portal) |
| Ordem de execução | 4 (Marco 5.2 "Cobrança pelo Inter") |

## User story
Como **financeiro**, quero **gerar o boleto com Pix do título com um clique e mandá-lo ao cliente por e-mail**, para **cobrar sem entrar no site do banco, sem digitar o valor de novo e com o pagamento já ligado ao título certo**.

## Contexto e referências
- PRD R-FIN-7 ("Cobrança via Banco Inter: emissão de boleto com Pix (bolepix), envio ao cliente por e-mail, baixa automática por webhook"); `05-backlog` BNK-01 ("emissão a partir do recebível, PDF/linha digitável/Pix copia-e-cola, envio por e-mail, cancelamento. CA: fluxo completo em sandbox; falha de emissão não perde o recebível").
- `04-fluxos-estados` §1 "Boleto (Inter)": `Issued → Paid (webhook ou conciliação) | Cancelled | Expired`.
- `03-modelo-dados` → `finance.billing_slips` (`receivable_id`, `provider`, `our_number`, `barcode`, `digitable_line`, `pix_copy_paste`, `qr_code`, `pdf_path`, `status`, `paid_at`, `paid_amount`, `provider_payload`) e `receivables.billing_slip_id` ("BNK-01").
- FIN-01: juros e multa digitados na baixa; `finance.late_fine_pct` e `finance.late_interest_month_pct` sem seed, "a sugestão do valor fica para a BNK-01"; ponto em aberto 3 ("Dia não útil: vencimento não ajustado; rever na BNK-01").
- ADR 0019 §3 (idempotência), §7 (anexos e e-mail com anexo depois do `Clean`), ADR 0021 (mensagens), ADR 0022 (PDF gerado pelo sistema).
- API de Cobrança do Inter: a emissão é **assíncrona** no lado do banco (a cobrança é criada com um código de solicitação e os dados do boleto ficam disponíveis por consulta); caminhos e campos conferidos no sandbox (BNK-01a).

## Critérios de aceite
**Emitir**
1. **Dado** `finance.edit`, um título `Open` ou `PartiallyPaid` com saldo > 0 e vencimento ≥ hoje, de cliente com CPF/CNPJ e endereço de cobrança completos, e uma conta com integração Inter testada, **quando** clico "Emitir boleto" no título, **então** o diálogo grande mostra o impacto: valor (o **saldo** do título), vencimento (o do título), conta de crédito, multa e juros de mora (padrão: os parâmetros `finance.late_fine_pct` e `finance.late_interest_month_pct`, editáveis no diálogo, Pontos em aberto 1), desconto até uma data (opcional), mensagem ao pagador (até 3 linhas, sem dado de outro cliente) e o e-mail que vai receber (o contato do cliente marcado "recebe cobrança"); "Emitir boleto" confirma.
2. **Dado** a confirmação, **quando** o servidor processa, **então** na mesma transação grava `billing_slips` com `status = Requested`, `client_request_id` pelo `Idempotency-Key`, o "seu número" (`our_reference` = número do título + sequência, Pontos em aberto 2) e enfileira o `IssueBillingSlipJob` (`TenantJob`, só ids); a mensagem diz "Boleto solicitado ao Inter. Assim que o banco confirmar, ele aparece aqui." e o título mostra o chip "Boleto em emissão".
3. **Dado** o `IssueBillingSlipJob`, **quando** roda, **então** cria a cobrança no Inter pelo `IBankBillingProvider.IssueAsync` (dados do pagador lidos do `IParties` no momento, nunca guardados no job), consulta até obter linha digitável, código de barras, nosso número e Pix copia-e-cola, baixa o PDF do banco e o guarda como arquivo do sistema (`StoreGeneratedAsync`, dono `BillingSlip`), e passa o boleto a `Issued`, ligando `receivables.billing_slip_id`; o título **não muda de valor, de situação nem de vencimento**.
4. **[Segurança]** **Dado** erro do Inter (4xx de validação, 5xx, timeout, certificado vencido), **quando** a emissão falha, **então** o título continua exatamente como estava; o boleto fica `Failed` com o motivo traduzido ("O CEP do cliente não foi aceito pelo banco.") e a notificação vai a quem pediu, com link para o título e as ações "Corrigir cadastro do cliente" e "Tentar de novo"; uma nova tentativa reusa o mesmo "seu número" (o Inter recusa duplicidade) e, num timeout depois do envio, **primeiro consulta** pelo "seu número" antes de criar outra cobrança (teste com o fake caindo depois de criar).
5. **Dado** um título que já tem boleto `Requested` ou `Issued`, **quando** peço outro, **então** 409 `billing_slip_exists` "Este título já tem um boleto em aberto. Cancele o atual para emitir outro." com a ação "Ver boleto"; o mesmo `Idempotency-Key` devolve o boleto original.
6. **Dado** cliente sem documento, sem endereço de cobrança ou sem e-mail de cobrança, **quando** abro o diálogo, **então** as pré-condições aparecem antes de confirmar, com link para corrigir, e o botão fica desabilitado com o motivo (sem e-mail, o boleto pode ser emitido sem envio, com aviso âmbar).

**Mostrar e enviar**
7. **Dado** um boleto `Issued`, **quando** abro a aba "Boleto" do título, **então** vejo situação, nosso número, vencimento, valor, linha digitável e Pix copia-e-cola com botão "Copiar" cada, o QR Code, multa, juros e desconto aplicados, e "Baixar PDF"; a lista de títulos ganha o filtro "Com boleto" e o ícone do boleto na linha.
8. **Dado** o boleto `Issued`, **quando** clico "Enviar ao cliente" (ou automaticamente na emissão, se a opção "Enviar por e-mail ao emitir" estava marcada, padrão sim), **então** o e-mail `billing_slip_issued` sai pela fila com o PDF anexado (depois do `Clean`), para o contato de cobrança e com cópia opcional (até 3), Reply-To do usuário; o corpo tem só empresa, número do título, valor, vencimento e a linha digitável, **sem** outros títulos do cliente; o envio fica no histórico do boleto (`email_logs`), com limite diário somado aos envios do tenant (proposta: 200 por dia, Pontos em aberto 5).
9. **[Segurança]** **Dado** os dados do boleto, **quando** saem pela API, **então** só para quem tem `finance.view`, com `Cache-Control: no-store`; o PDF é anexo com leitura `finance.view` e escrita só do sistema; `provider_payload` guarda o retorno do banco **sem** os dados do pagador (só ids, situação, datas e valores) e nunca sai pela API.

**Cancelar, alterar e expirar**
10. **Dado** `finance.edit` e um boleto `Issued` não pago, **quando** clico "Cancelar boleto" com motivo (lista do Inter mapeada: "Pagamento por outro meio", "Pedido do cliente", "Erro na emissão"…) e confirmo no diálogo `destructive`, **então** o cancelamento vai ao Inter pelo `IBankBillingProvider.CancelAsync`, o boleto fica `Cancelled` depois da confirmação do banco, o título fica sem boleto (`billing_slip_id` nulo, histórico preservado) e pode receber outro; o título não é cancelado.
11. **Dado** um título com boleto `Issued` que é **cancelado**, tem o **valor** alterado (aditivo, COM-07), tem o **vencimento** alterado (D14) ou é **quitado por outro meio** (baixa manual da FIN-01), **quando** a mudança é gravada, **então** o sistema cancela o boleto no Inter (job, com o motivo correspondente) e avisa quem tem `finance.edit` ("O boleto do título {número} foi cancelado porque {motivo}. Emita outro se precisar."); a baixa manual de um título com boleto aberto pede confirmação no diálogo ("Este título tem boleto no Inter. Cancelar o boleto e registrar a baixa?").
12. **Dado** um boleto `Issued` vencido há mais dias que o prazo de baixa do banco (proposta: o padrão do Inter, conferido no sandbox), **quando** a reconsulta diária da BNK-02 vê `Expirado`, **então** o boleto fica `Expired` e o título continua `Open` (vencido), com "Emitir novo boleto" no `NextSteps`.
13. **Dado** um título com vencimento passado, **quando** peço o boleto, **então** o diálogo exige um novo vencimento ≥ hoje e explica que o título mantém o vencimento original (os juros e a multa contam do vencimento do título, Pontos em aberto 3).

**Lote, permissões e auditoria**
14. **Dado** vários títulos selecionados na lista (até 50, R-PRP-6 D), **quando** clico "Emitir boletos", **então** o diálogo lista os elegíveis e os recusados com o motivo, e cada um vira um boleto com o seu `Idempotency-Key`; a mensagem diz "{n} boletos solicitados".
15. **Dado** o módulo `finance` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; emitir, enviar e cancelar sem `finance.edit`: 403; `RequireModule` e `perm:`; IDOR em `/receivables/{id}/billing-slips/{slipId}`.
16. **[Segurança]** **Dado** duas emissões simultâneas para o mesmo título, **quando** chegam, **então** só uma cria boleto (transição condicional do título com `xmin` e índice único parcial de boleto ativo por título).
17. **Dado** qualquer emissão, envio, falha, cancelamento ou expiração, **quando** abro o histórico do título, **então** vejo o que houve, quem e quando, com o link do boleto.

## Regras de negócio e técnicas
- **`billing_slips`** (03 + proposta): `receivable_id`, `bank_account_id`, `provider`, `our_reference` (seu número, único por conta), `our_number` (nosso número do banco), `provider_request_id` (código da solicitação no Inter), `barcode`, `digitable_line`, `pix_copy_paste`, `qr_code` (texto), `amount`, `due_date`, `fine_pct`, `interest_month_pct`, `discount_amount`, `discount_until`, `message`, `pdf_attachment_id` (no lugar de `pdf_path`), `status` (`Requested`/`Issued`/`Failed`/`Paid`/`Cancelled`/`Expired`), `failure_reason`, `cancel_reason`, `cancelled_at`, `cancelled_by`, `paid_at`, `paid_amount`, `provider_payload` (sem dado do pagador), `client_request_id`, `xmin`; índice único parcial `(receivable_id) WHERE status IN ('Requested','Issued')`.
- **Valor do boleto** = saldo do título no momento da emissão (`numeric(18,2)`); nunca maior que o saldo.
- **Nunca apagar:** boleto só é cancelado ou expira; o histórico fica.
- **Estado do título:** o boleto não muda o título; só a baixa (BNK-02 ou FIN-01) muda.
- **Pagador:** nome, documento, endereço e e-mail lidos do Parties no envio ao banco, nunca guardados no boleto.
- **Notificação** dentro do Finance síncrona; e-mail pela fila (FND-08); dono de anexo `BillingSlip` (leitura `finance.view`, escrita só do sistema, PDF até 2 MB) na tabela da ADR 0019 §7.

## Permissões, eventos e tabelas
- **Permissões:** `finance.view` (ver), `finance.edit` (emitir, enviar, cancelar). Nenhuma ação nova.
- **Eventos:** `BillingSlipIssued` e `BillingSlipCancelled` (internos ao Finance, sem dado pessoal; consumidos pela FIN-09 e, na Fase 8, pelo Portal).
- **Tabelas:** `finance.billing_slips` (03 com as colunas acima); `finance.receivables.billing_slip_id`. Template de e-mail `billing_slip_issued`. Parâmetros: sugestão de valor para `finance.late_fine_pct` e `finance.late_interest_month_pct` (Pontos em aberto 1).
- **Jobs:** `IssueBillingSlipJob`, `CancelBillingSlipJob` (fila `default`).

## Telas e estados
- **Título a receber → aba "Boleto"** (`/app/financeiro/receber/$id?aba=boleto`) e botão "Emitir boleto" no cabeçalho; **lista A receber** com filtro "Com boleto" e ação em lote.
- **Verbos:** "Emitir boleto" / "Boleto solicitado"; "Enviar ao cliente" / "Boleto enviado"; "Cancelar boleto" / "Boleto cancelado"; "Tentar de novo" / "Emissão reenviada".
- **Estados:** carregando; sem boleto ("Este título ainda não tem boleto." + "Emitir boleto"); em emissão (cerca de 30 s, depois o estado de demora "O Inter está demorando. Avisaremos quando o boleto estiver pronto."); falha com o motivo e as ações; sem integração ("Conecte a conta do Inter para emitir boletos." + link para BNK-01a, só para quem tem `settings.edit`).
- **Propositivo:** valor, vencimento, multa, juros e e-mail já preenchidos; depois de aprovar um contrato, o `NextSteps` do título da primeira parcela oferece "Emitir boleto"; boleto expirado oferece "Emitir novo boleto".
- **400 px:** linha digitável e Pix em blocos com "Copiar" de 44 px; QR Code com 200 px; diálogo em tela cheia.
- **Ajuda:** `Ajuda/financeiro-receber-titulo.md` ganha a aba `boleto`; `<Dica>` em "Seu número", "Nosso número", "Multa", "Juros de mora" e "Pix copia-e-cola".

## Plano de testes
- **Unidade:** valor do boleto = saldo; regras de elegibilidade (CA 1, 6, 13); motivos de cancelamento automático (CA 11).
- **Integração (Testcontainers + fake do Inter):** emitir de ponta a ponta com PDF guardado e título intacto (CA 2, 3); falhas do banco sem mexer no título e reenvio com consulta antes de criar (CA 4); boleto duplicado e idempotência (CA 5, 16); e-mail com anexo depois do `Clean` e limite diário (CA 8); dados sem `finance.view` e payload sem pagador (CA 9); cancelar manual e automático (CA 10, 11); expirar (CA 12); lote (CA 14); três camadas e IDOR (CA 15); auditoria (CA 17).
- **Contrato (sandbox do Inter, manual):** emitir, consultar, baixar PDF e cancelar no sandbox (CA do backlog "fluxo completo em sandbox"), registrado no runbook.
- **E2E (Playwright):** emitir o boleto de um título com o fake, copiar a linha digitável, enviar e cancelar; diálogo a 400 px e só pelo teclado.

## Fora do escopo
- Baixa pelo pagamento: BNK-02. Régua de lembretes: FIN-09. Boleto no portal do cliente: POR-01 (Fase 8).
- Alterar vencimento ou valor de um boleto já emitido no banco (o MVP cancela e emite outro).
- Boleto de outros bancos.

## Pontos em aberto
1. **Multa e juros padrão** (PO/contadora): valores sugeridos para `finance.late_fine_pct` e `finance.late_interest_month_pct` (até hoje sem seed). Proposta: deixar sem seed e pedir na primeira emissão, com Dica sobre os limites legais; não decidir o valor.
2. **Formato do "seu número"** (arquiteto, conferir limite de caracteres no Inter): proposta: número do título sem o prefixo + sequência do boleto.
3. **Título vencido e dia não útil** (PO, FIN-01 ponto 3): proposta: o boleto pode ter vencimento novo, mas multa e juros contam do vencimento original do título; vencimento em dia não útil segue a regra do banco para o boleto, e o título continua sem ajuste.
4. **Envio automático na emissão** (PO): proposta: sim, por padrão.
5. **Limite diário de e-mails** (PO): proposta 200 por tenant, somado aos envios do orçamento (COM-03) e do pedido (CMP-03b).
6. **Desconto no boleto × limite de desconto** (PO, D8): desconto até uma data dado no boleto conta para o limite de desconto da FIN-01 (5% ou R$ 500,00 exigem `finance.approve`)? Proposta: sim, o diálogo pede a aprovação quando passar do limite.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers + fake) e E2E passando; fluxo completo no sandbox registrado.
- [ ] Migration criada (não destrutiva), testada sobre cópia do banco do tenant `konebras`.
- [ ] Permissões só do `ModuleCatalog`; dono de anexo `BillingSlip`; template de e-mail registrado.
- [ ] Telas com estados, inclusive a 400 px.
- [ ] Ajuda da aba e das Dicas; teste de cobertura passando.
- [ ] Efeito financeiro com confirmação e próximos passos aplicados.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` e `docs/04-fluxos-estados.md` (estados `Requested` e `Failed` do boleto) atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `BillingSlip` (Domain) com as transições; `IBankBillingProvider` e `InterBillingProvider` (emitir, consultar, PDF, cancelar).
- [ ] `IssueBillingSlipJob` e `CancelBillingSlipJob`; reações às mudanças do título (CA 11).
- [ ] Endpoints `/api/v1/finance/receivables/{id}/billing-slips` (+ `/send`, `/cancel`, `/retry`), lote; template `billing_slip_issued`.
- [ ] Aba "Boleto", diálogos e ajuda.

## Quality gates
- Agentes previstos: @dev, cybersecurity (dados do pagador, idempotência, PDF), @qa.
- Gate: CA 3, 4, 5, 11 e 16 obrigatórios.

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

- **Correções feitas na validação:** estados `Requested` e `Failed` acrescentados ao boleto (a emissão no banco é assíncrona); reenvio consulta antes de criar (CA 4); boleto cancelado automaticamente quando o título muda ou é quitado por outro meio (CA 11); valor e percentuais de multa e juros não decididos (ponto 1).
- **Pré-condições para `Ready`:** pontos em aberto 1, 3 e 6.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da BNK-01) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
