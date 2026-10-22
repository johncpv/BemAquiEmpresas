# BNK-01b — Bolepix a partir do título a receber: Configurações → Cobrança, emissão, PDF, linha digitável, Pix copia-e-cola, e-mail e cancelamento

| Campo | Valor |
|---|---|
| ID | BNK-01b |
| Título | Tela **Configurações → Cobrança** (multa, juros, desconto padrão, envio automático do boleto, limite diário de e-mails, regras do vencido e do dia não útil, pré-configurados e marcados "A confirmar com a contadora"); emissão do boleto com Pix (bolepix) do Banco Inter a partir de um título a receber em aberto, com juros, multa e desconto; guarda do PDF, da linha digitável, do código de barras e do Pix copia-e-cola; envio ao cliente por e-mail pela fila; cancelamento e segunda via; falha de emissão nunca perde nem altera o título; componente `ConfirmDialog` (impacto em lista, confirmação digitada e contagem de prazo) |
| Tamanho | G (quebra da BNK-01; cresceu com a tela Configurações → Cobrança, o mecanismo "A confirmar com a contadora" e o `ConfirmDialog`, sem passar do G) |
| Status | Ready (pré-condição externa) |
| Requisitos | R-FIN-7 (emissão de bolepix, envio por e-mail) · R-FIN-3 (título a receber) · R-COM-1 (e-mail por fila) · R-COM-3 (PDF anexado) · R-PRP-1 · R-PRP-2 · R-PRP-6 · R-PRP-7 · R-SEC-5 |
| Dependências | BNK-01a (`InterApiClient`, `IBankBillingProvider`, `EnvironmentBanner`, `bank_production_allowed`), FIN-01 (títulos, `finance.parameters` com `late_fine_pct` e `late_interest_month_pct`, limites de desconto), COM-03 (e-mail com anexo, `StoreGeneratedAsync`), CAD-01a (endereço de cobrança e contato que recebe cobrança), FND-09e (diálogo financeiro, idempotência) |
| Bloqueia | BNK-02 (baixa pelo webhook), FIN-09 (régua na mesma tela de Cobrança e boleto no lembrete), POR-01c (Fase 8, boletos no portal); o `ConfirmDialog` é usado depois por BNK-03b, FIS-03b, LGPD-01b e as ações em lote |
| Ordem de execução | 3 (Marco 5.2 "Cobrança pelo Inter") |

## User story
Como **financeiro**, quero **gerar o boleto com Pix do título com um clique, com multa e juros já configurados, e mandá-lo ao cliente por e-mail**, para **cobrar sem entrar no site do banco, sem digitar o valor de novo e com o pagamento já ligado ao título certo**.

## Contexto e referências
- PRD R-FIN-7; `05-backlog` BNK-01 ("emissão a partir do recebível, PDF/linha digitável/Pix copia-e-cola, envio por e-mail, cancelamento. CA: fluxo completo em sandbox; falha de emissão não perde o recebível").
- **ADR 0042** (Configurações pré-configuradas, chip "A confirmar com a contadora" e "Marcar como conferido"), **ADR 0032** (Banco Inter), ADR 0008 adendo de 21/10/2026 (D6).
- **Revisão pré-Fases 4 a 8** (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`): D1, D13, D18; tabela 3.2, pontos 17 a 22; §4.4 (padrões 6 e 7).
- `04-fluxos-estados` §1 "Boleto (Inter)": `Issued → Paid (webhook ou conciliação) | Cancelled | Expired`, com `Requested` e `Failed` desta story.
- `03-modelo-dados` → `finance.billing_slips` e `receivables.billing_slip_id`.
- FIN-01: juros e multa digitados na baixa; `finance.late_fine_pct` e `finance.late_interest_month_pct` sem seed ("a sugestão do valor fica para a BNK-01"); limites de desconto (5% do saldo ou R$ 500,00 exigem `finance.approve`); dia não útil sem ajuste.
- ADR 0019 §3 (idempotência), §7 (anexos e e-mail com anexo depois do `Clean`), ADR 0021 (mensagens), ADR 0022 (PDF gerado pelo sistema).
- API de Cobrança do Inter: emissão **assíncrona** no banco (código de solicitação e dados disponíveis por consulta); caminhos, campos e limite do "seu número" conferidos no sandbox.

## Critérios de aceite
**Configurações → Cobrança**
1. **Dado** os módulos `settings` e `finance` liberados e um usuário com `settings.view` ou `finance.view`, **quando** abro **Configurações → Cobrança** (`/app/configuracoes/cobranca`), **então** vejo, pré-preenchidos pelo seed (ADR 0042): **multa** 2,00%, **juros de mora** 1,00% ao mês, **desconto padrão** 0,00%, **"Enviar o boleto por e-mail ao emitir"** marcado, **limite diário de e-mails** 200 por tenant (somado aos envios do orçamento, COM-03, e do pedido, CMP-03b), **multa e juros do boleto de título vencido** contados do **vencimento original do título**, e **vencimento em dia não útil** pela regra do banco, com o título sem ajuste (texto fixo, sem edição).
2. **Dado** os valores do seed, **quando** a tela abre, **então** multa, juros e a regra do vencido mostram o chip **"A confirmar com a contadora"** até alguém com `settings.edit` clicar "Marcar como conferido" (registra quem e quando, auditado); o chip some só para o campo conferido; mudar um valor conferido volta o chip para "A confirmar com a contadora" (a conferência vale para o valor, ADR 0042 §1); a tela mostra no topo "{n} valores a confirmar".
3. **Dado** `settings.edit`, **quando** altero e clico "Salvar cobrança", **então** os valores são validados (multa de 0 a 2,00%; juros de 0 a 1,00% ao mês; desconto de 0 a 100%; limite de e-mails de 1 a 2.000), com mensagens em PT-BR ("A multa não pode passar de 2,00%."), gravados em `finance.parameters` e auditados com antes e depois; sem `settings.edit`, a tela fica em modo leitura e a API responde 403.

**Emitir**
4. **Dado** `finance.edit`, um título `Open` ou `PartiallyPaid` com saldo > 0, de cliente com CPF/CNPJ e endereço de cobrança completos, e uma conta com integração Inter testada, **quando** clico "Emitir boleto" no título, **então** o diálogo grande mostra o impacto: valor (o **saldo** do título), vencimento (o do título; se vencido, o novo vencimento ≥ hoje, CA 15), conta de crédito, multa e juros (os de Configurações → Cobrança), desconto até uma data (padrão: o de Configurações), mensagem ao pagador (até 3 linhas, sem dado de outro cliente) e o **e-mail que vai receber, visível** (o contato do cliente marcado "recebe cobrança"), com "Enviar por e-mail ao emitir" marcado pelo padrão; "Emitir boleto" confirma.
5. **[Segurança]** **Dado** o diálogo, **quando** o desconto passa do limite de desconto da FIN-01 (5% do saldo ou R$ 500,00) ou a multa ou os juros ficam **acima** dos valores de Configurações → Cobrança, **então** só quem tem `finance.approve` confirma (D8, D13); para os demais, "Emitir boleto" fica desabilitado com o motivo "Desconto acima do limite: só quem aprova no Financeiro pode emitir." e a ação "Usar os valores padrão"; a API responde 403 `approval_required` se a regra for contornada; a aprovação fica no histórico do boleto.
6. **Dado** a confirmação, **quando** o servidor processa, **então** na mesma transação grava `billing_slips` com `status = Requested`, `client_request_id` pelo `Idempotency-Key`, o "seu número" (`our_reference`: sequência por conta em **base 36**, curta e opaca, sem o número do título nem dado do cliente, no limite de caracteres do Inter conferido no sandbox) e enfileira o `IssueBillingSlipJob` (`TenantJob`, só `tenantId` e ids); a mensagem diz "Boleto solicitado ao Inter. Assim que o banco confirmar, ele aparece aqui." e o título mostra o chip "Boleto em emissão".
7. **Dado** o `IssueBillingSlipJob`, **quando** roda, **então** confere o módulo `finance` liberado e a integração válida, cria a cobrança no Inter pelo `IBankBillingProvider.IssueAsync` (dados do pagador lidos do `IParties` no momento, nunca guardados no job), consulta até obter linha digitável, código de barras, nosso número e Pix copia-e-cola, baixa o PDF do banco e o guarda como arquivo do sistema (`StoreGeneratedAsync`, dono `BillingSlip`), e passa o boleto a `Issued`, ligando `receivables.billing_slip_id`; o título **não muda de valor, de situação nem de vencimento**.
8. **[Segurança]** **Dado** erro do Inter (4xx de validação, 5xx, timeout, certificado vencido), **quando** a emissão falha, **então** o título continua exatamente como estava; o boleto fica `Failed` com o motivo traduzido ("O CEP do cliente não foi aceito pelo banco.") e a notificação vai a quem pediu, com link para o título e as ações "Corrigir cadastro do cliente" e "Tentar de novo"; a nova tentativa reusa o mesmo "seu número" (o Inter recusa duplicidade) e, num timeout depois do envio, **primeiro consulta** pelo "seu número" antes de criar outra cobrança (teste com o fake caindo depois de criar).
9. **Dado** um título que já tem boleto `Requested` ou `Issued`, **quando** peço outro, **então** 409 `billing_slip_exists` "Este título já tem um boleto em aberto. Cancele o atual para emitir outro." com a ação "Ver boleto"; o mesmo `Idempotency-Key` devolve o boleto original.
10. **Dado** cliente sem documento, sem endereço de cobrança ou sem e-mail de cobrança, **quando** abro o diálogo, **então** as pré-condições aparecem antes de confirmar, com link para corrigir, e o botão fica desabilitado com o motivo (sem e-mail, o boleto pode ser emitido sem envio, com aviso âmbar).
11. **Dado** a conta em produção sem `bank_production_allowed` (BNK-01a CA 12), **quando** peço o boleto, **então** 409 `bank_production_not_allowed`; em sandbox, o diálogo e a aba mostram o `EnvironmentBanner`.

**Mostrar e enviar**
12. **Dado** um boleto `Issued`, **quando** abro a aba "Boleto" do título, **então** vejo situação, nosso número, vencimento, valor, linha digitável e Pix copia-e-cola com botão "Copiar" cada, o QR Code, multa, juros e desconto aplicados, e "Baixar PDF"; a lista de títulos ganha o filtro "Com boleto" e o ícone do boleto na linha; a emissão segue o padrão 6 (Enviado / Aguardando / Demorando depois de 30 s / Confirmado / Recusado com motivo e ação), sem reenvio enquanto processa.
13. **Dado** o boleto `Issued`, **quando** clico "Enviar ao cliente" (ou automaticamente na emissão, se "Enviar por e-mail ao emitir" estava marcado), **então** o e-mail `billing_slip_issued` sai pela fila com o PDF anexado (depois do `Clean`), para o contato de cobrança e com cópia opcional (até 3), Reply-To do usuário; o corpo tem só empresa, número do título, valor, vencimento e a linha digitável, **sem** outros títulos do cliente; o envio fica no histórico do boleto (`email_logs`); acima do limite diário de Configurações → Cobrança, o envio fica para o dia seguinte e o usuário é avisado ("Limite diário de e-mails atingido. O boleto será enviado amanhã às 08:00.").
14. **[Segurança]** **Dado** os dados do boleto, **quando** saem pela API, **então** só para quem tem `finance.view`, com `Cache-Control: no-store`; o PDF é anexo com leitura `finance.view` e escrita só do sistema; `provider_payload` guarda o retorno do banco **sem** os dados do pagador (só ids, situação, datas e valores) e nunca sai pela API.

**Vencido, cancelar, alterar e expirar**
15. **Dado** um título com vencimento passado, **quando** peço o boleto, **então** o diálogo exige um novo vencimento ≥ hoje (dia não útil segue a regra do banco) e explica "O título mantém o vencimento de dd/mm/aaaa; a multa e os juros contam dessa data."; o título não é alterado.
16. **Dado** `finance.edit` e um boleto `Issued` não pago, **quando** clico "Cancelar boleto" com motivo (lista do Inter mapeada: "Pagamento por outro meio", "Pedido do cliente", "Erro na emissão"…) e confirmo no `ConfirmDialog` `destructive` com o mesmo verbo, **então** o cancelamento vai ao Inter pelo `IBankBillingProvider.CancelAsync`, o boleto fica `Cancelled` depois da confirmação do banco, o título fica sem boleto (`billing_slip_id` nulo, histórico preservado) e pode receber outro; o título não é cancelado.
17. **Dado** um título com boleto `Issued` que é **cancelado**, tem o **valor** alterado (aditivo, COM-07), tem o **vencimento** alterado (D14) ou é **quitado por outro meio** (baixa manual da FIN-01), **quando** a mudança é gravada, **então** o sistema cancela o boleto no Inter (job, com o motivo correspondente) e avisa quem tem `finance.edit` ("O boleto do título {número} foi cancelado porque {motivo}. Emita outro se precisar."); a baixa manual de um título com boleto aberto pede confirmação ("Este título tem boleto no Inter. Cancelar o boleto e registrar a baixa?").
18. **Dado** um boleto `Issued` vencido há mais dias que o prazo de baixa do banco (o padrão do Inter, conferido no sandbox), **quando** a reconsulta da BNK-02 vê `Expirado`, **então** o boleto fica `Expired` e o título continua `Open` (vencido), com "Emitir novo boleto" no `NextSteps`.

**Lote, `ConfirmDialog`, permissões e auditoria**
19. **Dado** vários títulos selecionados na lista (até 50, R-PRP-6), **quando** clico "Emitir boletos", **então** o **`ConfirmDialog` com impacto em lista** mostra os elegíveis (título, cliente, valor, vencimento, e-mail) e os recusados com o motivo, o total, e "Emitir {n} boletos" confirma; cada um vira um boleto com o seu `Idempotency-Key`; a mensagem diz "{n} boletos solicitados".
20. **Dado** o componente `ConfirmDialog` (novo, D18, padrão 7), **quando** é usado, **então** oferece três formas, todas acessíveis pelo teclado e em tela cheia a 400 px: **impacto em lista** (itens afetados e recusados com motivo, total; usado no CA 19), **confirmação digitada** (o botão só habilita depois de digitar a palavra ou o número pedido; para a LGPD-01b) e **contagem de prazo** ("Restam 3 h 12 min para cancelar", atualizada a cada minuto e bloqueando ao zerar; para a FIS-03b); o verbo do botão é o da ação e a confirmação usa o mesmo verbo; testes de componente das três formas nesta story.
21. **Dado** o módulo `finance` (ou `settings`, na tela de Cobrança) não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; emitir, enviar e cancelar sem `finance.edit`: 403; salvar a Cobrança sem `settings.edit`: 403; `RequireModule` e `perm:`; IDOR em `/receivables/{id}/billing-slips/{slipId}`.
22. **[Segurança]** **Dado** duas emissões simultâneas para o mesmo título, **quando** chegam, **então** só uma cria boleto (transição condicional do título com `xmin` e índice único parcial de boleto ativo por título).
23. **Dado** qualquer emissão, envio, falha, aprovação, cancelamento ou expiração, ou mudança em Configurações → Cobrança, **quando** abro o histórico do título ou o "Histórico de alterações" da tela, **então** vejo o que houve, quem e quando, antes e depois, com o link do boleto.

## Regras de negócio e técnicas
- **`billing_slips`:** `receivable_id`, `bank_account_id`, `provider`, `our_reference` (seu número, base 36, único por conta), `our_number`, `provider_request_id`, `barcode`, `digitable_line`, `pix_copy_paste`, `qr_code` (texto), `amount`, `due_date`, `fine_pct`, `interest_month_pct`, `discount_amount`, `discount_until`, `message`, `approved_by` (quando passou do limite), `pdf_attachment_id` (no lugar de `pdf_path`), `status` (`Requested`/`Issued`/`Failed`/`Paid`/`Cancelled`/`Expired`), `failure_reason`, `cancel_reason`, `cancelled_at`, `cancelled_by`, `paid_at`, `paid_amount`, `provider_payload` (sem dado do pagador), `client_request_id`, `xmin`; índice único parcial `(receivable_id) WHERE status IN ('Requested','Issued')`.
- **Sequência do "seu número":** `finance.billing_reference_sequences` (por conta, transacional), convertida para base 36.
- **Parâmetros de cobrança** (`finance.parameters`, seed pela ADR 0042): `late_fine_pct` = 2,00, `late_interest_month_pct` = 1,00, `default_discount_pct` = 0, `billing_slip_auto_email` = verdadeiro, `daily_email_limit` = 200, `overdue_fine_from` = `OriginalDueDate`; conferência por chave com `review_state` (`ToConfirm`/`Confirmed`), `reviewed_by` e `reviewed_at` em `finance.parameters` (ADR 0042 §1); o componente `ToConfirmChip` e o endpoint de "Marcar como conferido" nascem aqui e são reutilizados pela FIS-02a e pela WPP-01.
- **Valor do boleto** = saldo do título na emissão (`numeric(18,2)`); nunca maior que o saldo.
- **Nunca apagar:** boleto só é cancelado ou expira. **Estado do título:** o boleto não muda o título; só a baixa (BNK-02 ou FIN-01).
- **Pagador:** nome, documento, endereço e e-mail lidos do Parties no envio ao banco, nunca guardados no boleto.
- **Notificação** dentro do Finance síncrona; e-mail pela fila (FND-08); dono de anexo `BillingSlip` (leitura `finance.view`, escrita só do sistema, PDF até 2 MB).

## Permissões, eventos e tabelas
- **Permissões:** `finance.view` (ver), `finance.edit` (emitir, enviar, cancelar), `finance.approve` (emitir acima dos limites), `settings.view`/`settings.edit` (Configurações → Cobrança, marcar como conferido). Nenhuma ação nova.
- **Eventos:** `BillingSlipIssued` e `BillingSlipCancelled` (internos ao Finance, sem dado pessoal; consumidos pela FIN-09 e, na Fase 8, pelo Portal).
- **Tabelas:** `finance.billing_slips` (com as colunas acima); `finance.receivables.billing_slip_id`; `finance.billing_reference_sequences` (nova); `finance.parameters` (novas chaves e seed; colunas `review_state`, `reviewed_by`, `reviewed_at` por chave, ADR 0042). Template de e-mail `billing_slip_issued`.
- **Jobs:** `IssueBillingSlipJob`, `CancelBillingSlipJob` (fila `default`).
- **Componentes novos:** `ConfirmDialog` (três formas, D18); `ToConfirmChip` ("A confirmar com a contadora") com a ação "Marcar como conferido".

## Telas e estados
- **Padrões comuns das Fases 4 a 8:** 6 (emissão assíncrona no Inter, `EnvironmentBanner`), 7 (`ConfirmDialog`: impacto em lista no lote; confirmação digitada e contagem de prazo para as stories seguintes).
- **Configurações → Cobrança** (`/app/configuracoes/cobranca`, aba nova em `SettingsTabs`): seções "Multa e juros", "Desconto", "Envio do boleto" (envio automático, limite diário) e "Vencido e dia não útil"; a FIN-09 acrescenta "Régua de cobrança" e a POR-01c (Fase 8) "Segunda via pelo portal". Estados: carregando; primeira configuração com os valores sugeridos e os chips "A confirmar com a contadora"; erro; modo leitura sem `settings.edit`; módulo não liberado. Verbos: "Salvar cobrança" / "Cobrança salva"; "Marcar como conferido" / "Conferido".
- **Título a receber → aba "Boleto"** (`/app/financeiro/receber/$id?aba=boleto`) e botão "Emitir boleto" no cabeçalho; **lista A receber** com filtro "Com boleto" e ação em lote.
- **Verbos:** "Emitir boleto" / "Boleto solicitado"; "Enviar ao cliente" / "Boleto enviado"; "Cancelar boleto" / "Boleto cancelado"; "Tentar de novo" / "Emissão reenviada"; "Emitir {n} boletos" / "{n} boletos solicitados".
- **Estados do boleto:** carregando; sem boleto ("Este título ainda não tem boleto." + "Emitir boleto"); em emissão (padrão 6, demora depois de 30 s "O Inter está demorando. Avisaremos quando o boleto estiver pronto."); falha com o motivo e as ações; sem integração ("Conecte a conta do Inter para emitir boletos." + link para Configurações → Integrações → Banco Inter, só para quem tem `settings.edit`).
- **Propositivo:** valor, vencimento, multa, juros, desconto e e-mail preenchidos pelos padrões; depois de aprovar um contrato, o `NextSteps` do título da primeira parcela oferece "Emitir boleto"; boleto expirado oferece "Emitir novo boleto"; título vencido sugere o novo vencimento no próximo dia útil.
- **400 px:** linha digitável e Pix em blocos com "Copiar" de 44 px; QR Code com 200 px; diálogos em tela cheia; seções da Cobrança empilhadas.
- **Ajuda:** `Ajuda/configuracoes-cobranca.md` (prevista, `modulo: settings`); `Ajuda/financeiro-receber-titulo-boleto.md` (prevista, aba `boleto` do título); `<Dica>` em "Seu número", "Nosso número", "Multa", "Juros de mora", "Pix copia-e-cola" e "A confirmar com a contadora".

## Plano de testes
- **Unidade:** valor do boleto = saldo; elegibilidade (CA 4, 10, 15); limites que exigem `finance.approve` (CA 5); base 36 do "seu número" (CA 6); motivos de cancelamento automático (CA 17); validação dos parâmetros (CA 3).
- **Componente (Vitest):** `ConfirmDialog` nas três formas, teclado e foco (CA 20); `ToConfirmChip` (CA 2).
- **Integração (Testcontainers + fake do Inter):** seed da Cobrança e "Marcar como conferido" auditado (CA 1–3); emitir de ponta a ponta com PDF guardado e título intacto (CA 6, 7); acima do limite sem `finance.approve` (CA 5); falhas do banco sem mexer no título e reenvio com consulta antes de criar (CA 8); boleto duplicado e idempotência (CA 9, 22); produção sem a flag (CA 11); e-mail com anexo depois do `Clean` e limite diário (CA 13); dados sem `finance.view` e payload sem pagador (CA 14); vencido (CA 15); cancelar manual e automático (CA 16, 17); expirar (CA 18); lote (CA 19); três camadas e IDOR (CA 21); auditoria (CA 23).
- **Contrato (sandbox do Inter, manual, tenant `demo`):** emitir, consultar, baixar PDF e cancelar (CA do backlog "fluxo completo em sandbox"), limite do "seu número", registrado no runbook.
- **E2E (Playwright):** conferir a multa em Configurações → Cobrança; emitir o boleto de um título com o fake, copiar a linha digitável, enviar e cancelar; lote com impacto em lista; diálogo a 400 px e só pelo teclado.

## Fora do escopo
- Baixa pelo pagamento: BNK-02. Régua de lembretes: FIN-09. Boleto no portal do cliente: POR-01c (Fase 8).
- Alterar vencimento ou valor de um boleto já emitido no banco (o MVP cancela e emite outro).
- Boleto de outros bancos.

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fases 4 a 8, tabela 3.2, pontos 17–22):
1. Multa 2% e juros 1% a.m. no seed, editáveis, marcados "A confirmar com a contadora" (CA 1–3).
2. "Seu número" curto e opaco, sequência por conta em base 36, conferido no sandbox (CA 6).
3. Vencido e dia não útil: o boleto pode ter vencimento novo; multa e juros contam do vencimento original; dia não útil pela regra do banco, título sem ajuste (CA 1, 15).
4. Envio automático marcado por padrão, com o e-mail visível no diálogo (CA 1, 4).
5. Limite de 200 e-mails por dia por tenant (CA 1, 13).
6. Desconto, multa e juros contam no limite da D8; acima, `finance.approve` (CA 5).

Decisões da revisão aplicadas: novo requisito de Configurações e ADR 0042 (tela Configurações → Cobrança, chip "A confirmar com a contadora" e "Marcar como conferido", CA 1–3); D13 (limites com `finance.approve`, CA 5); D18 (`ConfirmDialog` com impacto em lista, confirmação digitada e contagem de prazo criado nesta story, antes do Marco 5.4, CA 19, 20); D1 (produção só depois da GOL-01b, CA 11); §4.4 (padrões 6 e 7).

**Pré-condição externa:** conferência no sandbox do Inter (limite do "seu número", prazo de baixa do boleto vencido, regra do dia não útil); multa e juros conferidos pela contadora (o seed já vale; o chip registra a conferência); não bloqueia o `Ready`.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, componente, integração (Testcontainers + fake) e E2E passando; fluxo completo no sandbox registrado.
- [ ] Migration criada (não destrutiva), testada sobre cópia do banco do tenant `konebras`; seed dos parâmetros de cobrança nos tenants existentes (ADR 0016).
- [ ] Permissões só do `ModuleCatalog`; dono de anexo `BillingSlip`; template de e-mail registrado.
- [ ] Telas com estados (inclusive primeira configuração), a 400 px; `ConfirmDialog` documentado no kit de componentes.
- [ ] Ajudas `configuracoes-cobranca` e aba `boleto` escritas; teste de cobertura passando.
- [ ] Efeito financeiro com confirmação e próximos passos aplicados.
- [ ] Auditoria ligada (boleto, aprovação, parâmetros e conferência).
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` e `docs/04-fluxos-estados.md` (estados `Requested` e `Failed` do boleto) atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Parâmetros de cobrança com seed e `review_state` por chave, `ToConfirmChip`; aba Configurações → Cobrança e ajuda.
- [ ] `BillingSlip` (Domain) com as transições; sequência do "seu número"; `IBankBillingProvider` e `InterBillingProvider` (emitir, consultar, PDF, cancelar).
- [ ] `IssueBillingSlipJob` e `CancelBillingSlipJob`; reações às mudanças do título (CA 17).
- [ ] Endpoints `/api/v1/finance/receivables/{id}/billing-slips` (+ `/send`, `/cancel`, `/retry`), lote; `/api/v1/settings/billing`; template `billing_slip_issued`.
- [ ] `ConfirmDialog` (três formas) e testes de componente; aba "Boleto" e diálogos.

## Quality gates
- Agentes previstos: @dev, cybersecurity (dados do pagador, idempotência, PDF, limites), @ux-design-expert (`ConfirmDialog`, tela de Cobrança), @qa.
- Gate: CA 5, 7, 8, 9, 17, 20 e 22 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fases 4 a 8).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK (ajustado: Configurações com `settings.edit`; limites com `finance.approve`) |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: ordem 3; bloqueia a POR-01c) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs | OK (ADRs 0042 e 0032) |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK (ajustado: tela de Cobrança e `ConfirmDialog`) |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** estados `Requested` e `Failed` acrescentados ao boleto (a emissão no banco é assíncrona); reenvio consulta antes de criar; boleto cancelado automaticamente quando o título muda ou é quitado por outro meio; valores de multa e juros não decididos naquela validação.
- **Correções da revalidação (21/10/2026):** pontos 17 a 22 decididos (CA 1–6, 13, 15); tela Configurações → Cobrança com seed, chip "A confirmar com a contadora" e "Marcar como conferido" (CA 1–3); "seu número" em base 36 (CA 6); limites de desconto, multa e juros com `finance.approve` (CA 5); produção bloqueada até a GOL-01b (CA 11); `ConfirmDialog` com as três formas da D18, antes do Marco 5.4 (CA 19, 20); padrão 6 na aba; POR-01 → POR-01c; CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma de decisão de story. Pré-condição externa explícita: conferências no sandbox do Inter e conferência da multa e dos juros pela contadora, sem bloquear.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready (pré-condição externa)`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da BNK-01) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (D1, D13, D18, tabela 3.2 pontos 17–22, §4.4); tela Configurações → Cobrança; `ConfirmDialog`; nova ordem (Marco 5.2, ordem 3); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
