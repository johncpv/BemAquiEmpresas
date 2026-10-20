# COM-02 — Revisões do orçamento, expiração, prorrogação e comparação

| Campo | Valor |
|---|---|
| ID | COM-02 |
| Título | Nova revisão que congela a anterior (`Superseded`), só a revisão mais recente editável e aprovável, prorrogação da validade sem nova revisão, job diário que expira orçamentos vencidos, alerta de vencimento, reabertura por nova revisão e comparação lado a lado |
| Tamanho | M |
| Status | Done |
| Requisitos | R-COML-3 (revisões versionadas com histórico e comparação) · R-COML-4 (alerta de vencimento e status automático "expirado") · R-COM-2 (notificação) · R-PRP-5 |
| Dependências | COM-01a (índices de número com revisão já criados, ADR 0016 adendo M7), COM-01b; FND-09e (`dedup_key`, concorrência); Fase 0 (FND-07 `TenantJob`, notificações) |
| Bloqueia | COM-03 (PDF por revisão), COM-04a (aprova só a mais recente), COM-08 |
| Ordem de execução | 10 (Marco 2.2 "Orçamento") |

## User story
Como **comercial**, quero **criar uma nova revisão quando o cliente pede mudança, sem perder a anterior, prorrogar a validade quando só o prazo muda e ser avisado antes de um orçamento vencer**, para **negociar com histórico e não aprovar preço velho**.

## Contexto e referências
- PRD R-COML-3, R-COML-4; `04-fluxos-estados` §1 "Orçamento" (`Sent → nova revisão → Superseded`, a nova nasce `Draft`; `Sent → validade vencida (job diário) → Expired → reabrir → nova revisão`; só a revisão mais recente pode ser aprovada; aprovar exige validade não vencida).
- `03-modelo-dados` → `quotes.revision`, `quotes.parent_quote_id`, `is_current`.
- **D16 (PO, 20/10/2026):** "Prorrogar validade" em `Sent` muda só a data, auditada, sem nova revisão.
- ADR 0016 adendo M7 (número com revisão: os índices nascem na COM-01a); ADR 0019 §2 (concorrência), §12 (jobs e `dedup_key`); ADR 0006 (recorrente global que enfileira por tenant; `TenantJob<T>`; fuso America/Sao_Paulo).
- Revisão pré-Fase 2 §2.3 (comparação empilhada a 400 px).

## Critérios de aceite
**Revisão**
1. **Dado** `quotes.edit` e o orçamento `ORC-2026-0007` rev. 0 `Sent`, **quando** clico "Nova revisão" e confirmo, **então** na mesma transação a rev. 0 passa a `Superseded` (só leitura, `is_current = false`) e nasce a rev. 1 `Draft` (`is_current = true`) com o mesmo número, `parent_quote_id` = rev. 0, cópia de itens, composições, preços, marcos, validade recalculada (COM-01b CA 8) e observações; a mensagem de sucesso diz "Revisão criada" e a tela abre a rev. 1.
2. **Dado** um orçamento `Expired`, **quando** clico "Reabrir com nova revisão", **então** acontece o mesmo do CA 1, com a anterior continuando `Expired` (não vira `Superseded`) e a nova validade a partir de hoje.
3. **Dado** um orçamento `Draft`, **quando** procuro "Nova revisão", **então** a ação não aparece (o rascunho é editável) e a API responde 409 "Edite o rascunho; a revisão é para orçamento enviado."; `Rejected` ou `Approved` também respondem 409 (orçamento perdido usa "Copiar para novo orçamento", COM-01b).
4. **[Segurança]** **Dado** uma revisão que não é a mais recente do número, **quando** tento editá-la, enviá-la, prorrogá-la, criar revisão a partir dela ou aprová-la (COM-04a), **então** a API responde 409 "Só a revisão mais recente pode ser alterada ou aprovada." (índice único parcial `(number) WHERE is_current`, conferido na transação).
5. **[Segurança]** **Dado** duas pessoas clicando "Nova revisão" ao mesmo tempo no mesmo orçamento, **quando** chegam, **então** só uma revisão nasce (transição pela entidade rastreada com `xmin`, ADR 0019 §2, e o índice parcial) e a outra recebe 409 com "Recarregar".

**Prorrogar validade (D16)**
6. **Dado** `quotes.edit` e a revisão corrente `Sent`, **quando** clico "Prorrogar validade", informo a nova data (posterior à atual e ≥ hoje) e confirmo "Prorrogar validade", **então** só `valid_until` muda, sem nova revisão, a alteração é auditada ("Validade prorrogada de {antes} para {depois}") e a mensagem de sucesso diz "Validade prorrogada"; os avisos de vencimento passam a valer para a nova data; "Próximos passos" oferece "Enviar PDF atualizado" (COM-03, quando existir).
7. **Dado** uma data anterior ou igual à atual, ou anterior a hoje, **quando** confirmo, **então** 422 no campo ("A nova validade precisa ser depois de {data}."); orçamento `Expired` não é prorrogado (409 "Orçamento expirado: reabra com nova revisão.").

**Histórico e comparação**
8. **Dado** o orçamento, **quando** abro a aba "Revisões", **então** vejo todas as revisões do número com situação, data, usuário, preço total e, com `quotes.view_cost`, margem, com a corrente destacada, e cada uma abre em modo leitura.
9. **Dado** duas revisões escolhidas, **quando** clico "Comparar", **então** a tela mostra lado a lado os itens (incluídos, removidos, alterados em quantidade e preço e, com `quotes.view_cost`, em custo), os marcos, a validade, o prazo e os totais, com a diferença em valor e em %, e marca o que mudou com texto e ícone (não só cor); a 400 px a comparação fica empilhada (antes em cima, depois embaixo, por item).

**Expiração e alerta**
10. **Dado** um orçamento `Sent` com validade 18/11/2026, **quando** o job diário roda em 19/11/2026 (São Paulo), **então** ele passa a `Expired`, o autor recebe no sino "{número} rev. {n} expirou. Reabra com nova revisão se o cliente ainda quiser." com link para a ação, e rodar de novo (ou em paralelo) não repete a notificação (`dedup_key = quote-expired:{quoteId}`).
11. **Dado** um orçamento `Sent` que vence em 3 dias, **quando** o job roda, **então** o autor recebe uma vez "{número} vence em {data}." com link para o orçamento e as ações "Prorrogar validade" e "Nova revisão" (`dedup_key = quote-expiring:{quoteId}:{validade}`, de modo que uma prorrogação gera um novo aviso para a nova data); a lista mostra o chip alerta "Vence em {n} dias" (protótipo).
12. **Dado** um orçamento `Draft` com validade vencida, **quando** o job roda, **então** ele não muda de situação (o rascunho não foi enviado) e continua bloqueado para envio até ajustar a data (COM-01b CA 9).
13. **Dado** o job parado, um tenant suspenso e dois tenants, **quando** volta, **então** expira tudo que venceu enquanto parado; o tenant suspenso não roda (`TenantJob`); um tenant não expira nem avisa nada do outro (teste de dois tenants).

**Permissões**
14. **Dado** o módulo `quotes` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; sem `quotes.edit`, nova revisão, prorrogar e reabrir respondem 403; `/quotes/{id}/revisions/{revId}` confere que a revisão é do mesmo número (IDOR).

## Regras de negócio e técnicas
- Cada revisão é uma linha de `quotes` com o mesmo `number`, `revision` crescente, `parent_quote_id` e `is_current`; os índices único `(number, revision)` e único parcial `(number) WHERE is_current` já nascem na COM-01a (ADR 0016 adendo M7).
- A anterior fica imutável (`Superseded`, `Expired`, `Rejected` ou `Approved`).
- **Prorrogar** muda só `valid_until` da revisão corrente `Sent`; não muda o número da revisão nem os itens.
- **Job:** recorrente global diário às 07:00 de São Paulo, registrado no `RecurringJobsRegistrar`, que enfileira `ExpireQuotesJob` por tenant, com argumentos só de ids; notificações deduplicadas pelo `dedup_key` (FND-09e), sem coluna própria no orçamento.
- **Datas** `date` (validade), comparação com `BusinessTime.Today`.

## Permissões, eventos e tabelas
- **Permissões:** `quotes.view` (revisões, comparação), `quotes.edit` (nova revisão, prorrogar, reabrir), `quotes.view_cost` (custo e margem na comparação).
- **Eventos:** `QuoteExpired` (`{ quoteId }`), sem consumidor nesta fase (DSH-04 "orçamentos expirando" na Fase 4).
- **Tabelas:** `commercial.quotes` (+ `expired_at`).

## Telas e estados
- **Orçamento → aba Revisões** e **Comparar revisões** (`/app/orcamentos/$id/comparar?com=$outra`).
- **Cabeçalho do orçamento:** "ORC-2026-0007 · Rev. 1", chip e cliente; faixa de modo leitura "Há uma revisão mais recente" com "Ir para a revisão atual" quando abro uma antiga, e "Orçamento enviado não é alterado" com "Nova revisão" e "Prorrogar validade" quando abro a corrente `Sent`.
- **Lista:** só a revisão corrente de cada número, com a contagem de revisões; chips Expirado (crítico, como no protótipo), Substituído (neutro), "Vence em {n} dias" (alerta).
- **Verbos:** "Nova revisão" / "Revisão criada"; "Reabrir com nova revisão" / "Orçamento reaberto"; "Prorrogar validade" / "Validade prorrogada"; "Comparar".
- **Estados:** carregando; uma revisão só ("Este orçamento ainda não tem outras revisões."); erro.
- **Propositivo:** a notificação de vencimento leva ao orçamento com "Prorrogar validade" ou "Nova revisão"; ao abrir revisão antiga, "Ir para a revisão atual".
- **Ajuda:** `Ajuda/comercial-orcamento-revisoes.md` (aba `revisoes`) e `Ajuda/comercial-orcamento-comparar.md`; a ajuda `comercial-orcamento.md` ganha "Prorrogar validade"; `<Dica>` em "Revisão", "Substituído" e "Prorrogar validade" ("Muda só a data; para mudar preço ou itens, crie uma nova revisão.").

## Plano de testes
- **Unidade:** regras de transição (CA 1–4, 6, 7); diferença entre revisões (CA 9).
- **Integração (Testcontainers):** nova revisão com cópia completa (CA 1); reabrir expirado (CA 2); recusa em rascunho, perdido e aprovado (CA 3); só a mais recente (CA 4); concorrência (CA 5); prorrogação auditada e validações (CA 6, 7); comparação sem custo para quem não tem `quotes.view_cost` (CA 8, 9); job de expiração e alerta sem repetir, com relógio fixo, prorrogação gerando novo aviso, tenant suspenso e dois tenants (CA 10–13); três camadas e IDOR (CA 14).
- **Arquitetura:** job herda de `TenantJob` e está no `RecurringJobsRegistrar`.
- **E2E (Playwright):** enviar, prorrogar, criar revisão, comparar; comparação a 400 px.

## Fora do escopo
- PDF por revisão: COM-03. "Minha pauta" com orçamentos expirando: DSH-04 (Fase 4).

## Pontos em aberto
Decididos pelo PO em 20/10/2026 (seção 1.1, item 22, e D16):
1. Sem nova revisão de orçamento perdido (usar a cópia).
2. Alerta de vencimento 3 dias antes, fixo no MVP.
3. Número com revisão: adendo à ADR 0016 (M7), com os índices criados na COM-01a.
4. Prorrogar validade sem nova revisão (D16).
5. P-18 decidida (ADR 0021).

## Definition of Done
- [x] Critérios de aceite atendidos.
- [x] Testes de unidade, integração (Testcontainers), arquitetura e E2E passando.
- [x] Migration criada (não destrutiva).
- [x] Permissões só do `ModuleCatalog`; job registrado.
- [x] Telas com estados.
- [x] Ajuda da aba, da comparação e da prorrogação; teste de cobertura passando.
- [x] Alertas acionáveis e próximos passos.
- [x] Auditoria ligada.
- [x] OpenAPI e cliente Orval regenerados.
- [x] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [x] Casos de uso `CreateQuoteRevision` (cópia profunda), `ReopenExpiredQuote` e `ExtendQuoteValidity`.
- [x] `ExpireQuotesJob` e o recorrente global.
- [x] `QuoteDiff` (Domain) e a tela de comparação.

## Quality gates
- Agentes previstos: @dev, @qa.
- Gate: CA 4, 6 e 10 obrigatórios.

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

- **Correções feitas na validação (19/10/2026):** índice único de número composto com a revisão; rascunho vencido não expira; notificações sem repetir.
- **Correções da revalidação (20/10/2026):** "Prorrogar validade" sem nova revisão (D16, CA 6, 7); índices de número com revisão criados na COM-01a (M7); deduplicação pelo `dedup_key` no lugar de `expiry_warned_at` e teste de dois tenants (M8); concorrência pela entidade rastreada com `xmin`; comparação empilhada a 400 px e custo só com `quotes.view_cost` (D7). CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma pendente.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 19/10/2026 | 0.1 | Story detalhada | River (SM) |
| 19/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 20/10/2026 | 0.3 | Decisões do PO (D7, D16, seção 1.1) e correções da revisão pré-Fase 2 (§2.1-C2, M7, M8, §2.3); CAs renumerados | River (SM) |
| 20/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
| 20/10/2026 | 1.0 | Implementada. Desvios: a comparação é montada na tela com as duas revisões (`GET /quotes/{id}/revisions/{revId}`, que confere o mesmo número) e a margem vem por `/quotes/costs`, sem rota de comparação no servidor; o aviso de vencimento sai para orçamentos que vencem em até 3 dias (de hoje ao terceiro dia), uma vez por data de validade; a nova revisão grava primeiro a saída da anterior de corrente e depois a nova, na mesma transação, por causa do índice parcial; `QuoteExpired` publicado no outbox | @dev |
