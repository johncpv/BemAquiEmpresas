# FIN-09 — Régua de cobrança por e-mail (D)

| Campo | Valor |
|---|---|
| ID | FIN-09 |
| Título | Régua de cobrança configurável por tenant: lembretes por e-mail antes do vencimento, no dia e depois do atraso, com o boleto (linha digitável e PDF) quando houver; nunca para título pago, cancelado ou com boleto pago aguardando conferência; pausa por cliente e por título; histórico do que foi enviado |
| Tamanho | M |
| Status | Draft |
| Requisitos | R-FIN-9 (régua de cobrança: lembrete antes do vencimento e após atraso, por e-mail; D) · R-COM-1 · R-FIN-12 · R-PRP-5 · R-SEC-5 |
| Dependências | FIN-01 (títulos), BNK-01b (boleto e PDF), BNK-02 (baixa automática), FND-08/COM-03 (e-mail por fila com anexo), FIN-06a (aging) |
| Bloqueia | WPP-01 (Fase 8, régua por WhatsApp) |
| Ordem de execução | 15 (Marco 5.6 "Desejáveis") |

## User story
Como **financeiro**, quero **que o cliente receba lembretes automáticos antes e depois do vencimento, com o boleto**, para **receber em dia sem eu mandar e-mail um a um e sem cobrar quem já pagou**.

## Contexto e referências
- PRD R-FIN-9 (D); `05-backlog` FIN-09 ("D").
- FIN-06a (aviso diário interno de vencidos, `dedup_key`); BNK-01b (e-mail `billing_slip_issued`, limite diário); BNK-02 (`PaidPendingReview`).
- ADR 0019 §12 (jobs idempotentes, `dedup_key`), ADR 0018 (dados pessoais; contato que recebe cobrança).

## Critérios de aceite
1. **Dado** `finance.edit`, **quando** abro "Configuração financeira → Régua de cobrança" e defino os passos (dias em relação ao vencimento, ex.: −3, 0, +3, +10; até 6 passos), o texto de cada um (modelo com variáveis fixas: empresa, cliente, número, valor, vencimento, linha digitável, dias de atraso; sem texto livre do usuário virando template, como na COM-03) e ligo a régua, **então** a régua vale para todo título `Open` ou `PartiallyPaid` de origem `Contract` ou `Manual` (proposta; `Opening` fora, Pontos em aberto 2); desligada por padrão.
2. **Dado** a régua ligada, **quando** o recorrente global `collection-reminders` roda (diário, 09:00 de São Paulo), **então** para cada título cujo passo cai hoje envia o e-mail `collection_reminder` pela fila ao contato do cliente marcado "recebe cobrança", com a linha digitável e o PDF do boleto `Issued` anexado quando houver, uma vez por título e passo (`collection_notices` único `(receivable_id, step)`); teste de execução dupla e de dois tenants.
3. **[Segurança]** **Dado** um título `Paid`, `Cancelled`, com boleto `Paid` ou `PaidPendingReview`, ou com baixa registrada depois das 00:00 do dia, **quando** o job roda, **então** nenhum lembrete sai; a situação é conferida no momento do envio pelo `SendEmailJob` (proposta: o e-mail confere o título antes de enviar e vira `Skipped` se já foi pago).
4. **Dado** um cliente ou um título com "Pausar cobrança" (motivo obrigatório, ex.: negociação), **quando** o job roda, **então** não envia, e a pausa tem data de fim opcional; pausar e retomar ficam no histórico.
5. **Dado** o envio, **quando** o e-mail é montado, **então** traz só o título em questão (nunca a lista de outros títulos do cliente), sem dado de outro cliente, com Reply-To do financeiro configurado; o limite diário de e-mails do tenant vale (BNK-01b ponto 5) e o excedente fica para o dia seguinte.
6. **Dado** `finance.view`, **quando** abro o título ou o cliente, **então** vejo os lembretes enviados (passo, data, situação do e-mail) e o próximo previsto; a aba do terceiro (FIN-06b) mostra "Lembretes enviados no mês".
7. **Dado** o módulo `finance` não liberado, **quando** chamo, **então** 403 `module_not_released`; configurar e pausar sem `finance.edit`: 403; `RequireModule` e `perm:`; IDOR em `/receivables/{id}/collection-pause`.
8. **Dado** qualquer mudança na régua ou pausa, **quando** abro o histórico, **então** vejo antes e depois, quem e quando.

## Regras de negócio e técnicas
- **`finance.collection_rules`** (proposta): passos (jsonb: dias, modelo), `is_active`, `updated_by`; **`finance.collection_notices`**: `receivable_id`, `step`, `email_log_id`, `sent_at`, único `(receivable_id, step)`; **`finance.collection_pauses`**: `party_id` ou `receivable_id`, motivo, de, até.
- Modelos com variáveis fixas (Fluid, FND-08), sem HTML livre.

## Permissões, eventos e tabelas
- **Permissões:** `finance.view`, `finance.edit`. Nenhuma ação nova.
- **Tabelas:** as três propostas acima; template `collection_reminder`.
- **Jobs:** recorrente global `collection-reminders`.

## Telas e estados
- **Configuração financeira → Régua de cobrança** (`/app/financeiro/regua-de-cobranca`): linha do tempo dos passos, prévia do e-mail com um título de exemplo, chave "Régua ligada".
- **Verbos:** "Salvar régua" / "Régua salva"; "Pausar cobrança" / "Cobrança pausada"; "Retomar cobrança" / "Cobrança retomada".
- **Estados:** carregando; desligada ("A régua está desligada. Ligue para enviar lembretes automáticos."); erro.
- **Propositivo:** passos sugeridos (−3, 0, +3, +10) com textos padrão editáveis.
- **400 px:** passos em lista vertical.
- **Ajuda:** `Ajuda/financeiro-regua-de-cobranca.md`; `<Dica>` em "Pausar cobrança".

## Plano de testes
- **Unidade:** cálculo do passo do dia; elegibilidade (CA 1, 3).
- **Integração (Testcontainers):** envio por passo sem repetir, execução dupla e dois tenants (CA 2); pago no mesmo dia não recebe (CA 3); pausa (CA 4); conteúdo só do título e limite diário (CA 5); histórico e três camadas (CA 6–8).
- **E2E (Playwright):** configurar a régua e ver a prévia.

## Fora do escopo
- WhatsApp (Fase 8). Protesto e negativação. Juros calculados no e-mail.

## Pontos em aberto
1. **Passos padrão** (PO): proposta −3, 0, +3, +10.
2. **Títulos de implantação** (PO): entram na régua? Proposta: não.
3. **Conferência no envio** (arquiteto): o `SendEmailJob` genérico passa a aceitar uma condição de envio por template (proposta), ou a régua confere antes de enfileirar só.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers) e E2E passando.
- [ ] Migration criada (não destrutiva), testada sobre cópia do banco do tenant `konebras`.
- [ ] Permissões só do `ModuleCatalog`; template registrado.
- [ ] Tela com estados.
- [ ] Ajuda escrita; teste de cobertura passando.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `CollectionRule`, `CollectionNotice`, `CollectionPause`; recorrente `collection-reminders`; condição de envio no e-mail; tela e ajuda.

## Quality gates
- Agentes previstos: @dev, @qa.
- Gate: CA 2, 3 e 5 obrigatórios.

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

- **Correções feitas na validação:** conferência do título no momento do envio (CA 3); modelos sem texto livre como template (CA 1); desligada por padrão.
- **Pré-condições para `Ready`:** pontos em aberto 1 e 3.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
