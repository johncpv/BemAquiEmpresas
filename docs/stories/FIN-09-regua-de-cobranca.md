# FIN-09 — Régua de cobrança por e-mail (D)

| Campo | Valor |
|---|---|
| ID | FIN-09 |
| Título | Seção **"Régua de cobrança"** em Configurações → Cobrança: lembretes por e-mail antes do vencimento, no dia e depois do atraso (passos −3, 0, +3 e +10 dias), com o boleto (linha digitável e PDF) quando houver; textos editáveis literais e escapados, com placeholders de lista fixa; horário de envio; situação do título conferida antes de enfileirar e de novo no envio; nunca para título pago, cancelado ou com boleto pago aguardando conferência; títulos de implantação fora; pausa por cliente e por título; histórico do que foi enviado |
| Tamanho | M |
| Status | Done |
| Requisitos | R-FIN-9 (régua de cobrança: lembrete antes do vencimento e após atraso, por e-mail; D) · R-COM-1 · R-FIN-12 · R-PRP-5 · R-SEC-5 |
| Dependências | BNK-01b (tela Configurações → Cobrança, boleto e PDF, limite diário de e-mails), BNK-02 (baixa automática e `PaidPendingReview`), FIN-01 (títulos), FND-08/COM-03 (e-mail por fila com anexo), FIN-06a (aging) |
| Bloqueia | WPP-01 (Fase 8, régua por WhatsApp) |
| Ordem de execução | 16 (Marco 5.6 "Desejáveis", última da Fase 5) |

## User story
Como **financeiro**, quero **que o cliente receba lembretes automáticos antes e depois do vencimento, com o boleto**, para **receber em dia sem eu mandar e-mail um a um e sem cobrar quem já pagou**.

## Contexto e referências
- PRD R-FIN-9 (D); `05-backlog` FIN-09 ("D").
- **ADR 0042** (Configurações pré-configuradas e editáveis), ADR 0041 (a régua por WhatsApp da WPP-01 parte desta).
- **Revisão pré-Fases 4 a 8** (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`): D1 (a FIN-09 entra antes do go-live, na ordem da Fase 5), D17 (FIN-09 antes da WPP-01); tabela 3.2, pontos 67 a 69; §4.3 (transversal: job com `tenantId` e módulo conferido).
- FIN-06a (aviso diário interno de vencidos, `dedup_key`); BNK-01b (e-mail `billing_slip_issued`, limite diário de 200); BNK-02 (`PaidPendingReview`).
- ADR 0019 §12 (jobs idempotentes, `dedup_key`), ADR 0018 (dados pessoais; contato que recebe cobrança), FND-08 (templates Fluid com variáveis fixas).

## Critérios de aceite
**Configurar a régua**
1. **Dado** os módulos `settings` e `finance` liberados e `settings.edit`, **quando** abro Configurações → Cobrança, seção **"Régua de cobrança"**, **então** a régua vem pré-configurada e **desligada**, com os passos **−3, 0, +3 e +10 dias** em relação ao vencimento (até 6 passos, dias de −30 a +90, sem repetir), um texto padrão por passo e o **horário de envio** 09:00 (São Paulo, de 07:00 a 20:00); ligo pela chave "Régua ligada" e clico "Salvar régua"; sem `settings.edit`, a seção fica em modo leitura para quem tem `settings.view` ou `finance.view`, e a API responde 403.
2. **[Segurança]** **Dado** o texto de um passo, **quando** edito, **então** ele é **literal** (texto puro, até 1.000 caracteres, sem HTML nem sintaxe de template) e só aceita os placeholders da lista fixa `{empresa}`, `{cliente}`, `{numero}`, `{valor}`, `{vencimento}`, `{linha_digitavel}`, `{dias_de_atraso}`; placeholder fora da lista é recusado com 422 "Use só os campos da lista: {lista}."; na montagem do e-mail, o texto é escapado e cada placeholder trocado pelo valor escapado (nenhum texto do usuário vira template Fluid, como na COM-03); a prévia mostra o e-mail com um título de exemplo.
3. **Dado** a régua ligada, **quando** é aplicada, **então** vale para todo título `Open` ou `PartiallyPaid` de origem `Contract` ou `Manual`; títulos de origem `Opening` (implantação, FIN-00) **ficam fora**.

**Enviar**
4. **Dado** a régua ligada, **quando** o recorrente global `collection-reminders` roda (no horário configurado, um job por `tenantId` com o módulo `finance` liberado), **então** para cada título cujo passo cai hoje enfileira o e-mail `collection_reminder` ao contato do cliente marcado "recebe cobrança", com a linha digitável e o PDF do boleto `Issued` anexado quando houver, uma vez por título e passo (`collection_notices` único `(receivable_id, step)`); teste de execução dupla e de dois tenants.
5. **[Segurança]** **Dado** um título `Paid` ou `Cancelled`, com boleto `Paid` ou `PaidPendingReview`, ou com baixa registrada depois das 00:00 do dia, **quando** o job monta a lista **e de novo quando o `SendEmailJob` vai enviar**, **então** nenhum lembrete sai: o template `collection_reminder` registra uma guarda de envio (`IEmailSendGuard`, por template) que reconsulta o título pelo `IReceivables` no momento do envio e marca o e-mail `Skipped` "Título já pago" se a situação mudou (teste: pagamento entre o enfileiramento e o envio).
6. **Dado** um cliente ou um título com "Pausar cobrança" (`finance.edit`, motivo obrigatório, ex.: negociação), **quando** o job roda, **então** não envia; a pausa tem data de fim opcional; pausar e retomar ficam no histórico.
7. **Dado** o envio, **quando** o e-mail é montado, **então** traz só o título em questão (nunca a lista de outros títulos do cliente), sem dado de outro cliente, com Reply-To do financeiro configurado; o limite diário de e-mails de Configurações → Cobrança vale (BNK-01b) e o excedente fica para o dia seguinte, sem pular o passo.

**Consultar, permissões e auditoria**
8. **Dado** `finance.view`, **quando** abro o título ou o cliente, **então** vejo os lembretes enviados (passo, data, situação do e-mail, inclusive `Skipped` com o motivo) e o próximo previsto; a aba do terceiro (FIN-06b) mostra "Lembretes enviados no mês".
9. **Dado** o módulo `finance` ou `settings` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; configurar a régua sem `settings.edit` e pausar sem `finance.edit`: 403; `RequireModule` e `perm:`; IDOR em `/receivables/{id}/collection-pause`.
10. **Dado** qualquer mudança na régua ou pausa, **quando** abro o "Histórico de alterações", **então** vejo antes e depois, quem e quando.

## Regras de negócio e técnicas
- **`finance.collection_rules`:** `steps` (jsonb: dias e texto literal), `send_time`, `is_active`, `updated_by`, `xmin`; seed com os 4 passos e textos padrão (ADR 0042), desligada.
- **`finance.collection_notices`:** `receivable_id`, `step`, `email_log_id`, `status` (`Queued`/`Sent`/`Skipped`), `skip_reason`, `sent_at`; único `(receivable_id, step)`.
- **`finance.collection_pauses`:** `party_id` ou `receivable_id`, motivo, de, até, quem.
- **Guarda de envio:** `IEmailSendGuard` registrado por template no FND-08; o `SendEmailJob` chama a guarda antes de enviar; os demais templates não mudam.
- **Montagem:** o corpo é um template fixo do sistema que recebe o texto do passo já escapado e os valores dos placeholders; nenhuma entrada do usuário é compilada como template.

## Permissões, eventos e tabelas
- **Permissões:** `settings.edit` (configurar a régua), `settings.view`/`finance.view` (ver), `finance.edit` (pausar e retomar). Nenhuma ação nova.
- **Eventos:** consome `BillingSlipIssued` só para anexar o boleto mais recente (leitura pela fonte no envio).
- **Tabelas:** `finance.collection_rules`, `finance.collection_notices`, `finance.collection_pauses` (novas); template `collection_reminder` com a guarda de envio.
- **Jobs:** recorrente global `collection-reminders`.

## Telas e estados
- **Padrões comuns das Fases 4 a 8:** 6 (situação do e-mail: enviado, aguardando, ignorado com motivo).
- **Configurações → Cobrança → seção "Régua de cobrança"** (`/app/configuracoes/cobranca#regua`, na tela criada pela BNK-01b): linha do tempo dos passos em relação ao vencimento, editor do texto com a lista de campos clicáveis, prévia do e-mail com um título de exemplo, horário de envio, chave "Régua ligada".
- **Título e cliente:** bloco "Lembretes" com o histórico e "Pausar cobrança" / "Retomar cobrança".
- **Verbos:** "Salvar régua" / "Régua salva"; "Pausar cobrança" / "Cobrança pausada"; "Retomar cobrança" / "Cobrança retomada".
- **Estados:** carregando; desligada ("A régua está desligada. Ligue para enviar lembretes automáticos." + "Ligar régua"); primeira configuração com os passos e textos sugeridos; erro; modo leitura.
- **Propositivo:** passos e textos sugeridos já preenchidos; ao ligar, a mensagem diz quantos títulos recebem lembrete amanhã; cliente com boleto vencido sem lembrete pausado aparece no próximo passo previsto.
- **400 px:** passos em lista vertical; prévia abaixo do editor; botões de 44 px.
- **Ajuda:** `Ajuda/configuracoes-cobranca.md` ganha a seção "Régua de cobrança" (a ajuda é da tela da BNK-01b); `<Dica>` em "Pausar cobrança" e em "Campos do texto".

## Plano de testes
- **Unidade:** cálculo do passo do dia; elegibilidade com `Opening` fora (CA 3, 5); validação dos placeholders e escape (CA 2).
- **Integração (Testcontainers):** seed desligado e salvar com `settings.edit` (CA 1); texto com placeholder fora da lista e com HTML escapado (CA 2); envio por passo sem repetir, no horário, execução dupla e dois tenants (CA 4); pago entre enfileirar e enviar vira `Skipped` pela guarda (CA 5); pausa (CA 6); conteúdo só do título e limite diário (CA 7); histórico, três camadas e auditoria (CA 8–10).
- **E2E (Playwright):** configurar a régua, ver a prévia e ligar; tela a 400 px.

## Fora do escopo
- WhatsApp (WPP-01, Fase 8). Protesto e negativação. Juros calculados no e-mail.

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fases 4 a 8, tabela 3.2, pontos 67–69):
1. Passos −3, 0, +3 e +10 dias, no seed e editáveis (CA 1).
2. Títulos de implantação fora (CA 3).
3. Conferência antes de enfileirar e de novo no envio (guarda por template); texto editável literal e escapado, com placeholders de lista fixa (CA 2, 5).

Decisões da revisão aplicadas: D1 (a FIN-09 entra antes do go-live, na ordem da Fase 5); D17 (FIN-09 antes da WPP-01); novo requisito de Configurações e ADR 0042 (seção "Régua de cobrança" na tela de Cobrança da BNK-01b, com `settings.edit`, CA 1); §4.3 (job por `tenantId` com o módulo conferido, CA 4).

**Pré-condição externa:** nenhuma (os textos padrão podem ser ajustados pela Konebras na tela).

## Definition of Done
- [x] Critérios de aceite atendidos (CA 1 a 10; ver "Notas de implementação").
- [x] Testes de unidade e integração (Testcontainers) passando. E2E (Playwright) fora desta story pela regra do PO de 31/10: sai no fechamento da feature.
- [x] Migration criada (`CollectionFin09`, só cria tabelas e índices; não destrutiva); seed da régua desligada pelo `FinanceParametersSeeder` (também nos tenants existentes). Teste sobre cópia do banco `konebras`: no deploy.
- [x] Permissões só do `ModuleCatalog` (nenhuma ação nova); template `collection-reminder` e a guarda de envio (`IEmailSendGuard`) registrados.
- [x] Seção com estados (carregando, desligada com "Ligar régua", primeira configuração com os sugeridos, erro, modo leitura), passos em lista vertical a 400 px e botões de 44 px.
- [x] Ajuda da tela de Cobrança atualizada (seção "Régua de cobrança"), ajuda nova da aba "Lembretes" do título e a da aba do terceiro; teste de cobertura passando.
- [x] Auditoria ligada (`CollectionRule` em settings, `CollectionPause` em finance).
- [x] OpenAPI e cliente Orval regenerados.
- [x] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [x] `CollectionRule`, `CollectionNotice`, `CollectionPause`; recorrente `collection-reminders` no horário configurado.
- [x] `IEmailSendGuard` no FND-08 e a guarda do `collection_reminder`; montagem com escape e placeholders fixos.
- [x] Seção "Régua de cobrança" com prévia; bloco "Lembretes" no título e no cliente; ajuda.

## Notas de implementação
- **Template:** `collection-reminder` (kebab-case, como os outros templates do FND-08). O corpo é fixo; o texto do passo é montado em C# (os campos da lista trocados numa passada só) e entra como valor, escapado no HTML. Chave solta (`{{`, `{%`) é recusada como campo fora da lista.
- **Agenda:** recorrente global a cada 15 min das 07:00 às 20:45 (São Paulo); cada tenant envia a partir do horário da régua dele, só em dia útil do calendário padrão (CAD-03). Passo em dia não útil sai no próximo dia útil; dois passos no mesmo dia útil: vale o de mais dias. Rodada sob trava do tenant (`pg_advisory_xact_lock`) e índice único `(receivable_id, step)`.
- **Limite diário:** o lembrete soma no limite de Configurações → Cobrança junto com orçamentos, pedidos e boletos; o excedente fica `Deferred` e sai na próxima rodada com vaga (próximo dia útil), sem pular o passo.
- **Guarda:** o `SendEmailJob` chama as guardas do template antes de enviar; a do lembrete relê o título e a régua (pago, cancelado, boleto pago ou aguardando conferência, baixa de hoje, pausa, régua desligada) e marca o e-mail e o lembrete `Skipped` com o motivo.
- **Destinatário:** só o contato do cliente marcado "recebe cobrança"; sem ele, o lembrete fica ignorado com o motivo. Reply-To: o e-mail informado na régua ou, vazio, o da empresa.
- **Boleto:** com boleto `Issued`, o e-mail é do dono `BillingSlip` (o PDF só anexa ao mesmo documento) e aparece também na lista de e-mails do boleto.

## Quality gates
- Agentes previstos: @dev, cybersecurity (texto do usuário no e-mail, escape), @qa.
- Gate: CA 2, 4, 5 e 7 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fases 4 a 8).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK (ajustado: configurar com `settings.edit`) |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: ordem 16, antes do go-live e da WPP-01) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs | OK (ADR 0042) |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK (ajustado: seção em Configurações → Cobrança) |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** conferência do título no momento do envio; modelos sem texto livre como template; desligada por padrão.
- **Correções da revalidação (21/10/2026):** pontos 67 a 69 decididos (CA 1, 3, 2, 5); régua movida para a seção "Régua de cobrança" de Configurações → Cobrança, com `settings.edit` e horário de envio (CA 1); texto literal e escapado com placeholders de lista fixa (CA 2); guarda de envio por template (`IEmailSendGuard`, CA 5); job por tenant com o módulo conferido (CA 4); entra antes do go-live, como última da Fase 5 (D1); CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma de decisão de story.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (D1, D17, tabela 3.2 pontos 67–69, §4.3); seção em Configurações → Cobrança; nova ordem (Marco 5.6, ordem 16); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
| 31/10/2026 | 1.0 | Implementação: régua em Configurações → Cobrança (seção "Régua de cobrança", `#regua`), agenda por dia útil, rodada por tenant sob trava, `collection_notices` único por título e passo, guarda de envio `IEmailSendGuard` no `SendEmailJob`, pausa do título e do cliente, aba "Lembretes" do título e bloco "Lembretes de cobrança" do terceiro (com "Lembretes enviados no mês"), ajuda. Status `Done` | Dex (dev) |
| 31/10/2026 | 1.0 | Recomendação aceita (PO 24/10): conflitos resolvidos pelo mais conservador. (1) Lembrete só ao contato marcado "recebe cobrança" (sem ele, ignorado com o motivo; o boleto da BNK-01b usa o primeiro contato como reserva). (2) Pausa do cliente e do título: o passo que cai na pausa é registrado como ignorado e não sai depois. (3) Baixa de hoje (inclusive parcial) barra o lembrete do dia. (4) Guarda também barra quando a régua foi desligada entre enfileirar e enviar, e e-mail `Skipped` nunca é reenviado. (5) A guarda relê o título pelo próprio Finance (`CollectionEligibility`, a mesma regra do enfileiramento) em vez de um método novo no `IReceivables`. (6) Lembrete adiado pelo limite sai na próxima rodada com vaga, antes dos novos. (7) Lembrete com boleto fica no dono `BillingSlip` para o PDF anexar (regra da COM-03). (8) Sem E2E nesta story (regra do PO de 31/10). | Dex (dev) |
| 31/10/2026 | 1.0 | Pendências externas: textos padrão dos quatro passos a revisar pelo comercial e pelo jurídico (editáveis na tela); envio real depende do SMTP/SES de produção (FND-08); teste da migration sobre cópia do banco `konebras` no deploy. | Dex (dev) |
