# COM-02 — Revisões do orçamento, expiração e comparação

| Campo | Valor |
|---|---|
| ID | COM-02 |
| Título | Nova revisão que congela a anterior (`Superseded`), só a revisão mais recente editável e aprovável, job diário que expira orçamentos vencidos, alerta de vencimento, reabertura por nova revisão e comparação lado a lado |
| Tamanho | M |
| Status | Draft |
| Requisitos | R-COML-3 (revisões versionadas com histórico e comparação) · R-COML-4 (alerta de vencimento e status automático "expirado") · R-COM-2 (notificação) · R-PRP-5 |
| Dependências | COM-01a, COM-01b; Fase 0 (FND-07 `TenantJob`, notificações) |
| Bloqueia | COM-03 (PDF por revisão), COM-04a (aprova só a mais recente), COM-08 |
| Ordem de execução | 8 (Marco 2.2 — orçamento real) |

## User story
Como **comercial**, quero **criar uma nova revisão quando o cliente pede mudança, sem perder a anterior, e ser avisado antes de um orçamento vencer**, para **negociar com histórico e não aprovar preço velho**.

## Contexto e referências
- PRD R-COML-3, R-COML-4; `04-fluxos-estados` §1 "Orçamento" (`Sent → nova revisão → Superseded`, a nova nasce `Draft`; `Sent → validade vencida (job diário) → Expired → reabrir → nova revisão`; só a revisão mais recente pode ser aprovada; aprovar exige validade não vencida).
- `03-modelo-dados` → `quotes.revision`, `quotes.parent_quote_id`.
- ADR 0006 (recorrente global que enfileira por tenant; `TenantJob<T>`; fuso America/Sao_Paulo).

## Critérios de aceite
**Revisão**
1. **Dado** `quotes.edit` e o orçamento `ORC-2026-0007` rev. 0 `Sent`, **quando** clico "Nova revisão" e confirmo, **então** na mesma transação a rev. 0 passa a `Superseded` (só leitura) e nasce a rev. 1 `Draft` com o mesmo número, `parent_quote_id` = rev. 0, cópia de itens, composições, preços, marcos, validade recalculada (COM-01b CA 7) e observações; o toast diz "Revisão criada" e a tela abre a rev. 1.
2. **Dado** um orçamento `Expired`, **quando** clico "Reabrir com nova revisão", **então** acontece o mesmo do CA 1, com a anterior continuando `Expired` (não vira `Superseded`) e a nova validade a partir de hoje.
3. **Dado** um orçamento `Draft`, **quando** procuro "Nova revisão", **então** a ação não aparece (o rascunho é editável) e a API responde 409 "Edite o rascunho; a revisão é para orçamento enviado."; `Rejected` ou `Approved` também respondem 409 (ver Pontos em aberto 1).
4. **[Segurança]** **Dado** uma revisão que não é a mais recente do número, **quando** tento editá-la, enviá-la, criar revisão a partir dela ou aprová-la (COM-04a), **então** a API responde 409 "Só a revisão mais recente pode ser alterada ou aprovada." (índice único parcial de "revisão corrente" por número, conferido na transação).
5. **Dado** duas pessoas clicando "Nova revisão" ao mesmo tempo no mesmo orçamento, **quando** chegam, **então** só uma revisão nasce (transição condicional `WHERE status = 'Sent'`) e a outra recebe 409.

**Histórico e comparação**
6. **Dado** o orçamento, **quando** abro a aba "Revisões", **então** vejo todas as revisões do número com situação, data, usuário, preço total e margem, a corrente destacada, e cada uma abre em modo leitura.
7. **Dado** duas revisões escolhidas, **quando** clico "Comparar", **então** a tela mostra lado a lado os itens (incluídos, removidos, alterados em quantidade, custo e preço), os marcos, a validade, o prazo e os totais, com a diferença em valor e em %, e marca o que mudou com texto e ícone (não só cor).

**Expiração e alerta**
8. **Dado** um orçamento `Sent` com validade 18/11/2026, **quando** o job diário roda em 19/11/2026 (São Paulo), **então** ele passa a `Expired`, o autor recebe no sino "{número} rev. {n} expirou. Reabra com nova revisão se o cliente ainda quiser." com link para a ação, e rodar de novo não repete a notificação.
9. **Dado** um orçamento `Sent` que vence em 3 dias (ver Pontos em aberto 2), **quando** o job roda, **então** o autor recebe uma vez "{número} vence em {data}." com link para o orçamento; a lista mostra o chip alerta "Vence em {n} dias" (protótipo).
10. **Dado** um orçamento `Draft` com validade vencida, **quando** o job roda, **então** ele não muda de situação (o rascunho não foi enviado) e continua bloqueado para envio até ajustar a data (COM-01b CA 8).
11. **Dado** o job parado e um tenant suspenso, **quando** volta, **então** expira tudo que venceu enquanto parado; o tenant suspenso não roda (`TenantJob`).

**Permissões**
12. **Dado** o módulo `quotes` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; sem `quotes.edit`, nova revisão e reabrir respondem 403; `/quotes/{id}/revisions/{revId}` confere que a revisão é do mesmo número (IDOR).

## Regras de negócio e técnicas
- Cada revisão é uma linha de `quotes` com o mesmo `number`, `revision` crescente e `parent_quote_id`; índice único em `(number, revision)` (o índice único só em `number` do ADR 0016 passa a ser composto para orçamentos — proposta) e índice único parcial `(number) WHERE is_current` (proposta: coluna `is_current`).
- A anterior fica imutável (`Superseded`, `Expired`, `Rejected` ou `Approved`).
- **Job:** `GlobalRecurringJob` diário às 07:00 de São Paulo, que enfileira `ExpireQuotesJob` por tenant; notificações deduplicadas por `(quote_id, tipo)` (proposta: `quote_alerts` ou chave no próprio orçamento `expiry_warned_at`).
- **Datas** `date` (validade), comparação com `BusinessTime.Today`.

## Permissões, eventos e tabelas
- **Permissões:** `quotes.view` (revisões, comparação), `quotes.edit` (nova revisão, reabrir).
- **Eventos:** `QuoteExpired` (`{ quoteId }`), sem consumidor nesta fase (DSH-04 "orçamentos expirando" na Fase 4).
- **Tabelas:** `commercial.quotes` (+ `is_current`, `expiry_warned_at`, `expired_at`) — **proposta**.

## Telas e estados
- **Orçamento → aba Revisões** e **Comparar revisões** (`/app/orcamentos/$id/comparar?com=$outra`).
- **Cabeçalho do orçamento:** "ORC-2026-0007 · Rev. 1" e o aviso "Há uma revisão mais recente" com link quando abro uma antiga.
- **Lista:** só a revisão corrente de cada número, com a contagem de revisões; chips Expirado (crítico, como no protótipo), Substituído (neutro).
- **Verbos:** "Nova revisão" / "Revisão criada"; "Reabrir com nova revisão" / "Orçamento reaberto"; "Comparar".
- **Estados:** carregando; uma revisão só ("Este orçamento ainda não tem outras revisões."); erro.
- **Propositivo:** a notificação de vencimento leva ao orçamento com "Nova revisão" ou "Ajustar validade"; ao abrir revisão antiga, "Ir para a revisão atual".
- **Ajuda:** `Ajuda/comercial-orcamento-revisoes.md` (aba `revisoes`) e `Ajuda/comercial-orcamento-comparar.md`; `<Dica>` em "Revisão" e "Substituído".

## Plano de testes
- **Unidade:** regras de transição (CA 1–4); diferença entre revisões (CA 7).
- **Integração (Testcontainers):** nova revisão com cópia completa (CA 1); reabrir expirado (CA 2); recusa em rascunho, perdido e aprovado (CA 3); só a mais recente (CA 4); concorrência (CA 5); job de expiração e alerta sem repetir, com relógio fixo (CA 8–11); três camadas e IDOR (CA 12).
- **Arquitetura:** job herda de `TenantJob`.
- **E2E (Playwright):** enviar, criar revisão, comparar.

## Fora do escopo
- PDF por revisão: COM-03. "Minha pauta" com orçamentos expirando: DSH-04 (Fase 4).

## Pontos em aberto
1. **Nova revisão a partir de orçamento perdido.** O `04-fluxos` não prevê. **Recomendação:** não permitir; usar "Copiar para novo orçamento" (COM-01b), para o funil contar a perda.
2. **Antecedência do alerta.** **Recomendação:** 3 dias, fixo no MVP.
3. **Índice único de número com revisão.** A ADR 0016 manda índice único em `number` em toda tabela de documento numerado, o que impede revisões com o mesmo número. **Recomendação:** adendo à ADR 0016 com a exceção do orçamento: único em `(number, revision)` e único parcial em `number WHERE is_current`.
4. **P-18:** ver FIN-01, ponto 5.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers), arquitetura e E2E passando.
- [ ] Migration criada (não destrutiva).
- [ ] Permissões só do `ModuleCatalog`; job registrado.
- [ ] Telas com estados.
- [ ] Ajuda da aba e da comparação; teste de cobertura passando.
- [ ] Alertas acionáveis e próximos passos.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Caso de uso `CreateQuoteRevision` (cópia profunda) e `ReopenExpiredQuote`.
- [ ] `ExpireQuotesJob` e o recorrente global.
- [ ] `QuoteDiff` (Domain) e a tela de comparação.

## Quality gates
- Agentes previstos: @dev, @qa.
- Gate: CA 4 e 8 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 19/10/2026.

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

- **Correções feitas na validação (19/10/2026):** índice único de número composto com a revisão (o ADR 0016 pede índice único em `number`, o que impediria revisões); rascunho vencido não expira (CA 10); notificações sem repetir.
- **Correção da revalidação:** o índice composto conflita com a ADR 0016 e virou o ponto em aberto 3 (adendo à ADR antes de codar).
- **Pré-condições para `Ready`:** Pontos em aberto 1 a 3 (com padrão recomendado; o 3 pede adendo à ADR 0016) e P-18.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 19/10/2026 | 0.1 | Story detalhada | River (SM) |
| 19/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
