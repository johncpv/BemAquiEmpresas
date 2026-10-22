# POR-02 — Portal do cliente: aceite online do orçamento

| Campo | Valor |
|---|---|
| ID | POR-02 |
| Título | O orçamento enviado aparece no portal do cliente com o PDF congelado no envio; o contato com acesso aceita (com código por e-mail obrigatório) ou recusa com motivo a revisão corrente dentro da validade, com registro só de inclusão de quem, quando, revisão, hash do PDF, IP e user-agent, e recibo por e-mail com o hash; o aceite só avisa o comercial e pré-preenche a aprovação interna, que continua gerando contrato, projeto e parcelas pela COM-04a; revisão diferente da aceita bloqueia a sugestão |
| Tamanho | M |
| Status | Ready |
| Requisitos | R-COML-10 ("Aprovação online pelo cliente via link/portal", P) · R-COML-4 (validade) · R-COML-3 (revisão corrente) · R-COM-2 · R-PRP-2 · R-PRP-7 · R-SEC-5 |
| Dependências | POR-01a (acesso e identidade do portal), POR-01b (documentos e Kit Portal), COM-02 (revisões, validade, expiração), COM-03 (PDF e envio), COM-04a (aprovação interna), FND-08 (e-mail) |
| Bloqueia | — |
| Ordem de execução | 4 (Marco 8.3 "Aceite online") |

## User story
Como **cliente**, quero **aceitar o orçamento pelo portal**, e como **comercial**, quero **saber na hora que o cliente aceitou e aprovar com os dados já preenchidos**, para **fechar a venda sem papel nem troca de e-mails**.

## Contexto e referências
- PRD R-COML-10 (P, Fase 8); backlog POR-02.
- **ADR 0005 D5:** "Aprovação do orçamento = assinatura do contrato, num único evento. Aprovar gera o contrato `Active`…" (COM-04a, com `quotes.approve`). Mantida: o aceite do cliente **só avisa** o Comercial e pré-preenche a aprovação interna.
- `04-fluxos` §1 "Orçamento" (`Sent`; só a revisão mais recente pode ser aprovada; validade não vencida; marcos somando 100%).
- COM-03 (PDF sem custo nem margem, guardado pelo `StoreGeneratedAsync` e enviado por e-mail).
- **Revisão pré-Fases 4 a 8** (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`): D16 (código por e-mail obrigatório no aceite; versão, hash do PDF congelado no envio, IP, user-agent e recibo; revisão nova invalida o aceite; sem link anônimo); tabela 3.5, pontos 14 a 16; §4.3 POR-02; §4.4 padrões 5 e 7.
- **ADR 0040** (identidade do portal, código por e-mail no aceite); ADR 0019 §20 (sem custo nem margem fora de `*.view_cost`).

## Critérios de aceite
1. **Dado** um orçamento `Sent` do cliente da sessão, **quando** o contato abre `/portal/orcamentos`, **então** vê a revisão corrente de cada orçamento enviado (número, revisão, data do envio, validade, total), com "Ver PDF" (o PDF guardado no envio pela COM-03, pelo endpoint autorizado do portal); revisões antigas, rascunhos e orçamentos de outro cliente nunca aparecem (IDOR).
2. **Dado** a revisão corrente dentro da validade, **quando** clico "Aceitar orçamento", leio o resumo (itens, total, condições de pagamento em linguagem simples, prazo), marco "Li e aceito as condições deste orçamento", digito meu nome e clico "Enviar código", **então** recebo no e-mail do acesso um código de 6 dígitos (válido por 10 min, uso único, até 5 tentativas, política de rate limit `portal-accept`), obrigatório **com ou sem 2FA**.
3. **Dado** o código certo, **quando** confirmo, **então** grava a decisão (usuário do portal, nome digitado, data e hora do servidor, revisão, hash SHA-256 do PDF congelado no envio, IP real e user-agent), o orçamento ganha a marca "Aceito pelo cliente em {data}", a tela diz "Orçamento aceito. A {empresa} vai confirmar e enviar o contrato." e o contato recebe o **recibo por e-mail** com o número, a revisão, a data e hora e o hash do PDF aceito.
4. **[Segurança]** **Dado** o hash, **quando** o aceite é confirmado, **então** o servidor confere que o hash do PDF guardado é o gravado no envio (`pdf_sha256` do envio da COM-03); se o PDF mudou depois do envio, 409 "O orçamento mudou depois do envio. Peça o reenvio."; aceite duplo grava uma vez (`Idempotency-Key` e transição pela entidade rastreada).
5. **[Segurança]** **Dado** `commercial.quote_customer_decisions`, **quando** qualquer código tenta alterar ou apagar uma decisão, **então** não consegue: a tabela é **só de inclusão** (sem `UPDATE` nem `DELETE` para o usuário da aplicação, trigger que recusa, teste de integração); uma decisão nova sobre a mesma revisão só entra se a anterior foi invalidada por revisão nova (CA 7).
6. **Dado** o aceite, **quando** gravado, **então** quem tem `quotes.approve` e o autor do orçamento recebem "{cliente} aceitou o orçamento {número} rev. {n}." com link, e o diálogo de aprovação interna (COM-04a) abre com a data de assinatura sugerida = data do aceite e o aviso "Aceito pelo cliente {nome} em {data}" (R-PRP-1); a aprovação continua exigindo `quotes.approve` e confirmação.
7. **Dado** uma revisão nova criada depois do aceite (COM-02), **quando** abro a aprovação interna, **então** a sugestão **não** é preenchida: o diálogo mostra "O cliente aceitou a rev. {n}; a revisão atual é a {m}. Envie a nova revisão para um novo aceite." e o aceite anterior fica marcado "Invalidado pela rev. {m}" (sem apagar o registro).
8. **Dado** a recusa, **quando** clico "Recusar orçamento" e escolho o motivo (lista de motivos de perda da COM-01b) com um comentário opcional, **então** o comercial é notificado com o motivo e o orçamento ganha a marca "Recusado pelo cliente"; o comercial decide se registra a perda (COM-01b) ou faz nova revisão.
9. **Dado** a validade vencida, uma nova revisão criada depois do envio ou o orçamento já aprovado ou perdido, **quando** tento aceitar, **então** 409 com a mensagem certa ("A validade deste orçamento venceu. Peça uma nova proposta.", "Há uma revisão mais nova: {n}.") e o botão some, com o bloco "Fale com a {empresa}".
10. **[Segurança]** **Dado** os DTOs do portal de orçamento, **quando** o teste de arquitetura roda, **então** não têm custo, composição, markup, margem nem observação interna (ADR 0019 §20).
11. **Dado** o módulo `portal` ou `quotes` não liberado, **quando** chamo, **então** 403 `module_not_released`; só usuário do portal do cliente do orçamento aceita ou recusa; IDOR em `/api/v1/portal/quotes/{id}` (404 uniforme, dois clientes e dois tenants); não existe link de aceite sem login.
12. **Dado** aceite, recusa e invalidação, **quando** abro o histórico do orçamento no backoffice, **então** vejo quem pelo cliente, quando, o IP, o user-agent, a revisão e o hash; o nome digitado, o IP e o user-agent são `[PersonalData]`.

## Regras de negócio e técnicas
- **Tabela:** `commercial.quote_customer_decisions` (`quote_id`, `revision`, `portal_user_id`, `decision` Accepted/Rejected, `typed_name`, `decided_at`, `ip`, `user_agent`, `pdf_sha256`, `lost_reason`, `comment`, `invalidated_by_revision`), só de inclusão; `invalidated_by_revision` é a única coluna que muda, de nulo para a revisão nova, por função própria com auditoria (trigger recusa qualquer outra alteração).
- **Hash congelado no envio (ajuste na COM-03, já `Done`, feito dentro desta story):** a COM-03 passa a gravar `pdf_sha256` no registro do envio, com teste de regressão do envio; para envios anteriores ao deploy, o hash é calculado uma vez do PDF guardado.
- **Código do aceite:** `portal.portal_confirmation_codes` (hash do código, finalidade `QuoteAccept`, `expires_at`, `attempts`, `used_at`).
- **Leitura e escrita** pelo `IQuotes.ForPortalAsync(partyId)` e `IQuotes.RecordCustomerDecisionAsync` (Commercial.Contracts); o orçamento não muda de estado pelo aceite (ADR 0005 D5).
- Datas: `decided_at` em `timestamptz`.

## Permissões, eventos e tabelas
- **Permissões:** usuário do portal; `quotes.approve` (aprovação interna, inalterada).
- **Eventos:** `QuoteAcceptedByCustomer` e `QuoteRejectedByCustomer` (`{ quoteId, revision }`), dentro do Commercial.
- **Tabelas:** `commercial.quote_customer_decisions` (nova); `portal.portal_confirmation_codes` (nova); envio da COM-03 + `pdf_sha256`; templates `portal_accept_code`, `portal_accept_receipt`.

## Telas e estados
- **Padrões comuns das Fases 4 a 8:** 5 (Kit Portal: condições em linguagem simples, "Fale com a {empresa}"); 7 (confirmação com o impacto: o que o cliente aceita, em lista).
- **Portal:** `/portal/orcamentos` e `/portal/orcamentos/$id` (resumo, PDF, "Aceitar orçamento" com o passo do código, "Recusar orçamento").
- **Backoffice:** chip "Aceito pelo cliente" ou "Aceite invalidado" na lista e no detalhe do orçamento; `NextSteps` "Aprovar e gerar contrato".
- **Verbos:** "Aceitar orçamento" / "Orçamento aceito"; "Recusar orçamento" / "Orçamento recusado"; "Enviar código" / "Código enviado".
- **Estados:** carregando; vazio ("Nenhum orçamento para aprovar." + "Fale com a {empresa}"); vencido; revisão nova; já decidido; código errado ou vencido ("Código inválido ou vencido. Peça outro."); erro.
- **400 px:** resumo, código e confirmação em tela cheia.
- **Ajuda:** `Ajuda/portal-orcamentos.md`, `Ajuda/portal-orcamento.md` (previstas); atualizar `Ajuda/comercial-orcamento.md` ("Aceite do cliente").

## Plano de testes
- **Integração (Testcontainers):** lista só da revisão corrente do cliente (CA 1); código por e-mail, validade, tentativas e rate limit (CA 2); aceite com hash, IP, user-agent e recibo (CA 3); PDF alterado e aceite duplo (CA 4); tabela só de inclusão (CA 5); notificação e sugestão na COM-04a (CA 6); revisão nova bloqueia a sugestão e invalida (CA 7); recusa (CA 8); bloqueios (CA 9); três camadas e IDOR (CA 11); histórico (CA 12).
- **Arquitetura:** DTOs sem custo (CA 10).
- **E2E (Playwright):** o cliente aceita no celular com o código e o comercial aprova com a data sugerida.

## Fora do escopo
- Assinatura eletrônica com certificado ou provedor externo: fora do MVP.
- Aceite por link sem login (rota anônima): fora.
- O aceite gerar o contrato sozinho: fora (ADR 0005 D5 mantida).

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fases 4 a 8, tabela 3.5, pontos 14–16):
1. O aceite só avisa o Comercial e pré-preenche a aprovação interna; mantém a ADR 0005 D5 (CA 6).
2. Assinatura eletrônica com validade jurídica fora (código por e-mail, hash, IP e user-agent como prova do aceite).
3. Sem link de aceite sem login (CA 11).

Decisões da revisão aplicadas: D16 (código por e-mail obrigatório, versão, hash do PDF congelado no envio, IP, user-agent e recibo, revisão nova invalida o aceite, CA 2, 3, 4, 7); §4.3 POR-02 (decisões só de inclusão, recibo com o hash, revisão diferente bloqueia a sugestão na COM-04a, CA 3, 5, 7); §4.4 padrões 5 e 7.

**Pré-condição externa:** nenhuma.

## Definition of Done
- [ ] Critérios de aceite atendidos; testes de integração, arquitetura e E2E passando.
- [ ] Migration criada (não destrutiva; trigger de só inclusão; `pdf_sha256` no envio com o cálculo dos envios antigos).
- [ ] Telas com estados; ajudas previstas escritas; teste de cobertura passando; auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados; `docs/04-fluxos-estados.md` (aceite e invalidação) atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `pdf_sha256` no envio da COM-03; contratos `IQuotes` para o portal; registro só de inclusão; código por e-mail.
- [ ] Notificações e recibo; sugestão e bloqueio no diálogo da COM-04a; telas e ajudas.

## Quality gates
- Agentes previstos: @dev, cybersecurity (prova do aceite, código, IDOR), @qa.
- Gate: CA 2, 3, 4, 5, 7 e 10 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fases 4 a 8).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: FND-08 para o código e o recibo) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** o aceite não gera contrato sozinho, para não contrariar a ADR 0005 D5 (ponto 1); prova do aceite com hash do PDF enviado (CA 2, 6).
- **Correções da revalidação (21/10/2026):** pontos 14 a 16 decididos (só avisa, CA 6; sem assinatura eletrônica externa; sem link anônimo, CA 11); D16 (código por e-mail obrigatório, user-agent, recibo com o hash, hash congelado no envio, CA 2–4); §4.3 (só inclusão, CA 5; revisão diferente bloqueia a sugestão e invalida o aceite, CA 7); CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma de decisão de story.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (D16, tabela 3.5 pontos 14–16, §4.3 POR-02, §4.4 padrões 5 e 7, ADR 0040); nova ordem (Marco 8.3, ordem 4, antes do go-live); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
