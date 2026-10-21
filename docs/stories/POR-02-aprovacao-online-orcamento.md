# POR-02 — Portal do cliente: aceite online do orçamento

| Campo | Valor |
|---|---|
| ID | POR-02 |
| Título | O orçamento enviado aparece no portal do cliente com o PDF; o contato com acesso aceita (ou recusa com motivo) a revisão corrente dentro da validade, com registro de quem, quando, IP e o hash do PDF aceito; o aceite avisa o comercial e pré-preenche a aprovação interna, que continua gerando contrato, projeto e parcelas pela COM-04a |
| Tamanho | M |
| Status | Draft |
| Requisitos | R-COML-10 ("Aprovação online pelo cliente via link/portal", P) · R-COML-4 (validade) · R-COML-3 (revisão corrente) · R-COM-2 · R-PRP-2 · R-PRP-7 · R-SEC-5 |
| Dependências | POR-01a (acesso), POR-01b (documentos), COM-02 (revisões, validade, expiração), COM-03 (PDF e envio), COM-04a (aprovação interna) |
| Bloqueia | — |
| Ordem de execução | 4 (Marco 8.3 "Aceite online") |

## User story
Como **cliente**, quero **aceitar o orçamento pelo portal**, e como **comercial**, quero **saber na hora que o cliente aceitou e aprovar com os dados já preenchidos**, para **fechar a venda sem papel nem troca de e-mails**.

## Contexto e referências
- PRD R-COML-10 (P, Fase 8); backlog POR-02.
- **ADR 0005 D5:** "Aprovação do orçamento = assinatura do contrato, num único evento. Aprovar gera o contrato `Active`…" (COM-04a, com `quotes.approve`). O aceite do cliente **não** substitui essa aprovação sem decisão do PO (Pontos em aberto 1): por isso a proposta é aceite do cliente + aprovação interna pré-preenchida.
- `04-fluxos` §1 "Orçamento" (`Sent`; só a revisão mais recente pode ser aprovada; validade não vencida; marcos somando 100%).
- COM-03 (PDF sem custo nem margem, enviado por e-mail).

## Critérios de aceite
1. **Dado** um orçamento `Sent` do cliente da sessão, **quando** o contato abre `/portal/orcamentos`, **então** vê a revisão corrente de cada orçamento enviado (número, revisão, data do envio, validade, total), com "Ver PDF" (o mesmo PDF enviado pela COM-03); revisões antigas, rascunhos e orçamentos de outro cliente nunca aparecem (IDOR).
2. **Dado** a revisão corrente dentro da validade, **quando** clico "Aceitar orçamento", leio o resumo (itens, total, condições de pagamento por marco, prazo) e confirmo marcando "Li e aceito as condições deste orçamento" e digitando meu nome, **então** grava o aceite (quem, nome digitado, data e hora do servidor, IP, revisão, hash SHA-256 do PDF aceito), o orçamento ganha a marca "Aceito pelo cliente em {data}" e a mensagem diz "Orçamento aceito. A {empresa} vai confirmar e enviar o contrato."
3. **Dado** o aceite, **quando** gravado, **então** quem tem `quotes.approve` e o autor do orçamento recebem a notificação "{cliente} aceitou o orçamento {número} rev. {n}." com link, e o diálogo de aprovação interna (COM-04a) abre com a data de assinatura sugerida = data do aceite e o aviso "Aceito pelo cliente {nome} em {data}" (R-PRP-1); a aprovação continua exigindo `quotes.approve` e confirmação.
4. **Dado** a recusa, **quando** clico "Recusar orçamento" e escolho o motivo (lista de motivos de perda da COM-01b) com um comentário opcional, **então** o comercial é notificado com o motivo e o orçamento ganha a marca "Recusado pelo cliente"; o comercial decide se registra a perda (COM-01b) ou faz nova revisão.
5. **Dado** a validade vencida, uma nova revisão criada depois do envio ou o orçamento já aprovado ou perdido, **quando** tento aceitar, **então** 409 com a mensagem certa ("A validade deste orçamento venceu. Peça uma nova proposta.", "Há uma revisão mais nova: {n}.") e o botão some.
6. **[Segurança]** **Dado** o aceite, **quando** gravado, **então** o PDF guardado no envio (anexo `Quote` gerado pelo sistema) é o que tem o hash conferido; se o PDF mudou depois do envio, 409 "O orçamento mudou depois do envio. Peça o reenvio."; aceite duplo grava uma vez (`Idempotency-Key` e transição pela entidade rastreada).
7. **[Segurança]** **Dado** os DTOs do portal de orçamento, **quando** o teste de arquitetura roda, **então** não têm custo, composição, markup, margem nem observação interna (ADR 0019 §20).
8. **Dado** o módulo `portal` ou `quotes` não liberado, **quando** chamo, **então** 403 `module_not_released`; só usuário do portal do cliente do orçamento aceita ou recusa; IDOR em `/portal/quotes/{id}`.
9. **Dado** aceite e recusa, **quando** abro o histórico do orçamento no backoffice, **então** vejo quem pelo cliente, quando, o IP e o hash; o nome digitado é `[PersonalData]`.

## Regras de negócio e técnicas
- **Tabela (proposta):** `commercial.quote_customer_decisions` (`quote_id`, `revision`, `portal_user_id`, `decision` Accepted/Rejected, `typed_name`, `decided_at`, `ip`, `pdf_sha256`, `lost_reason`, `comment`).
- **Leitura e escrita** pelo `IQuotes.ForPortalAsync(partyId)` e `IQuotes.RecordCustomerDecisionAsync` (Commercial.Contracts, propostas); o orçamento não muda de estado pelo aceite (proposta).
- Datas: `decided_at` em `timestamptz`.

## Permissões, eventos e tabelas
- **Permissões:** usuário do portal; `quotes.approve` (aprovação interna, inalterada).
- **Eventos:** `QuoteAcceptedByCustomer` e `QuoteRejectedByCustomer` (`{ quoteId, revision }`), dentro do Commercial.
- **Tabelas:** `commercial.quote_customer_decisions` (proposta).

## Telas e estados
- **Portal:** `/portal/orcamentos` e `/portal/orcamentos/$id` (resumo, PDF, "Aceitar orçamento", "Recusar orçamento").
- **Backoffice:** chip "Aceito pelo cliente" na lista e no detalhe do orçamento; `NextSteps` "Aprovar e gerar contrato".
- **Estados:** vencido; revisão nova; já decidido; erro.
- **400 px:** resumo e confirmação em tela cheia.
- **Ajuda:** `Ajuda/portal-orcamentos.md`, `Ajuda/portal-orcamento.md`; atualizar `Ajuda/comercial-orcamento.md` ("Aceite do cliente").

## Plano de testes
- **Integração (Testcontainers):** lista só da revisão corrente do cliente (CA 1); aceite com hash e notificação (CA 2, 3); recusa (CA 4); bloqueios (CA 5); PDF alterado e aceite duplo (CA 6); três camadas e IDOR (CA 8); histórico (CA 9).
- **Arquitetura:** DTOs sem custo (CA 7).
- **E2E (Playwright):** o cliente aceita no celular e o comercial aprova com a data sugerida.

## Fora do escopo
- Assinatura eletrônica com certificado ou provedor externo: depois do MVP (Pontos em aberto 2).
- Aceite por link sem login (rota anônima): fora, sem ADR (Pontos em aberto 3).

## Pontos em aberto
1. **Valor do aceite** (pré-condição, PO + jurídico): o aceite do cliente só avisa e pré-preenche a aprovação (proposta, mantém a ADR 0005 D5) ou ele mesmo gera o contrato (muda a D5, pede ADR).
2. Assinatura eletrônica com validade jurídica (provedor externo).
3. Link de aceite sem login, pelo e-mail do orçamento (rota anônima com token, pede ADR).

## Definition of Done
- [ ] Critérios de aceite atendidos; testes de integração, arquitetura e E2E passando.
- [ ] Migration criada (não destrutiva).
- [ ] Telas com estados; ajuda; teste de cobertura passando; auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados; `docs/04-fluxos-estados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Contratos `IQuotes` para o portal; registro da decisão; notificações; telas.

## Quality gates
- Agentes previstos: @dev, cybersecurity (prova do aceite, IDOR), @qa.
- Gate: CA 2, 5, 6 e 7 obrigatórios.

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

- **Correções feitas na validação:** o aceite não gera contrato sozinho, para não contrariar a ADR 0005 D5 (ponto 1); prova do aceite com hash do PDF enviado (CA 2, 6).
- **Pré-condições para `Ready`:** ponto em aberto 1; POR-01a e POR-01b concluídas.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
