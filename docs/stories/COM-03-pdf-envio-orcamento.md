# COM-03 — PDF e envio do orçamento

| Campo | Valor |
|---|---|
| ID | COM-03 |
| Título | PDF do orçamento com o layout do tenant (`IPdfRenderer`, QuestPDF, fonte Archivo embutida, logo, dados da empresa, itens, condições, validade e prazo, sem custo nem margem), anexado à revisão como arquivo gerado pelo sistema, e envio por e-mail ao contato pela fila, com o PDF anexo, cópia, Reply-To do usuário e limite diário |
| Tamanho | M |
| Status | Ready (com pré-condições externas: licença do QuestPDF confirmada pelo PO e SPF, DKIM e DMARC no domínio de envio) |
| Requisitos | R-COML-5 (PDF com layout do tenant e envio por e-mail) · R-COM-1 (e-mail por fila, templates, histórico) · R-COM-3 (anexos) · R-PRP-2 · R-SEC-5 |
| Dependências | COM-02 (PDF por revisão), COM-01b (condições), CAD-06 (perfil da empresa e logo `TenantLogo`), FND-08 (e-mail), FND-07 (anexos), FIN-03a (regras de anexo por dono), FND-09e; **ADR 0022**; **pré-condição:** P-21 (licença do QuestPDF) |
| Bloqueia | — (COM-04a não depende: "Marcar como enviado" já existe na COM-01a) |
| Ordem de execução | 11 (Marco 2.2 "Orçamento"; fecha o marco) |

## User story
Como **comercial**, quero **gerar o PDF do orçamento com a cara da empresa e mandar por e-mail ao contato do cliente em um clique**, para **não montar proposta no Word e ter registrado o que foi enviado a quem**.

## Contexto e referências
- PRD R-COML-5, R-COM-1, R-COM-3; backlog COM-03 (CA: "PDF anexado ao orçamento e ao log de e-mail").
- **ADR 0022** (geração de PDF: `IPdfRenderer`, Archivo embutida, `UseEnvironmentFonts = false`, teste na imagem Alpine, limites; a licença Community depende da elegibilidade, confirmada pelo PO: P-21). ADR 0009 (QuestPDF; Fluid para os templates de e-mail).
- **ADR 0019 §7** (arquivo gerado pelo sistema por `StoreGeneratedAsync`, e-mail com anexo e cópia, espera do anexo `Clean` com reagendamento próprio).
- FND-08 (`IEmailSender.Enqueue` grava `email_logs` e o outbox; templates do sistema; links com o host primário; nunca buscar URL fornecida pelo tenant). Hoje o `IEmailSender` não aceita anexo nem cópia.
- CAD-06 (perfil da empresa em `settings.company_profile`; logo = anexo `TenantLogo` mais recente com `Clean`).
- Revisão pré-Fase 2 §2.2-M2 (e-mail do orçamento), §2.2 baixos (limites de itens e de observações no PDF), §2.1-A2 (idempotência do envio), §2.3 ("Escolher arquivo" além do arrastar).

## Critérios de aceite
**PDF**
1. **Dado** `quotes.view` e a revisão corrente, **quando** clico "Gerar PDF", **então** o PDF é gerado no servidor pelo `IPdfRenderer` (QuestPDF, fonte Archivo embutida, `UseEnvironmentFonts = false`) com: logo e razão social, CNPJ, endereço e contato da empresa (perfil da CAD-06); número e revisão; data; cliente (nome, documento, endereço da obra e contato); itens com descrição, quantidade, unidade, preço unitário e total; total geral; condições por marco (descrição, %, valor, gatilho); validade; prazo de entrega; observações; e o rodapé "Orçamento válido até {data}. Sem efeito de contrato.".
2. **[Segurança]** **Dado** o PDF gerado, **quando** inspeciono o conteúdo, **então** não há custo, composição de custo, markup, margem, cargo ou custo-hora em nenhum lugar, para nenhum usuário (o modelo do PDF é montado do DTO sem custo; teste extrai o texto do PDF e procura os valores de custo do dataset).
3. **Dado** o PDF gerado, **quando** a geração termina, **então** ele é gravado pelo `IAttachmentService.StoreGeneratedAsync` (interno, sem rota, na transação da geração) como anexo do orçamento (dono `Quote`, a revisão), com a marca "Gerado pelo sistema", nome `ORC-2026-0007-rev1.pdf`, aparece na aba "Documentos" e passa pela varredura como qualquer anexo; uma nova geração da mesma revisão substitui a anterior como "atual", mantendo a anterior no histórico de anexos.
4. **Dado** o tenant sem logo `Clean`, **quando** gero, **então** o PDF sai só com o nome fantasia no lugar do logo; perfil da empresa incompleto mostra antes o aviso "Complete os dados da empresa para o PDF." com link para Configurações → Empresa (não bloqueia).
5. **Dado** um orçamento com 50 itens, **quando** gero, **então** o PDF sai em menos de 3 s, com quebra de página que repete o cabeçalho da tabela; acima de 300 itens ou com observações acima de 2.000 caracteres, 422 antes de gerar (ADR 0022).
6. **Dado** a imagem Alpine da API (a mesma do deploy), **quando** o teste de integração gera o PDF de referência dentro dela, **então** o PDF abre, o texto extraído tem os valores esperados e as fontes embutidas são só a Archivo (ADR 0022).

**Envio**
7. **Dado** `quotes.edit` e a revisão corrente `Draft` ou `Sent` com validade não vencida, **quando** clico "Enviar por e-mail", **então** o diálogo (`size="lg"`) vem com o destinatário = o contato do orçamento (ou o contato de cobrança do cliente), até 3 endereços em cópia (opcional), assunto "Orçamento {número} rev. {n} — {nome fantasia}", mensagem padrão editável (até 2.000 caracteres, texto simples), o PDF atual anexo (gerado na hora se não existir) e o botão "Enviar orçamento".
8. **Dado** a confirmação, **quando** envio, **então** na mesma transação o e-mail entra na fila (`IEmailSender.Enqueue` com o template de sistema `quote_sent`, o anexo e a cópia), `email_logs` grava `related_type = Quote`, `related_id`, o id do anexo e a cópia, e um orçamento `Draft` passa a `Sent` (como no "Marcar como enviado"); a mensagem de sucesso diz "Orçamento enviado" e a aba "Documentos" mostra o envio (para quem, quando, por quem, situação do e-mail); o mesmo envio repetido com o mesmo `Idempotency-Key` não enfileira outro e-mail.
9. **[Segurança]** **Dado** o destinatário e a cópia, **quando** informo, **então** cada um é um **endereço puro** (sem nome de exibição, vírgula, ponto e vírgula nem CR/LF; formato válido; até 254 caracteres), validado no servidor (422 no campo); um endereço que não é contato do cliente mostra o aviso âmbar "Este e-mail não está nos contatos de {cliente}." e exige marcar "Confirmo o destinatário"; o envio é auditado.
10. **[Segurança]** **Dado** o e-mail montado, **quando** a fila envia, **então**: o assunto e o nome fantasia não carregam CR/LF (removidos no servidor); o nome de exibição do remetente é "{nome fantasia} via BemAqui" no endereço de envio da plataforma; o `Reply-To` é o e-mail do usuário que enviou; e a mensagem digitada entra **como valor do modelo** do template Fluid, escapada, nunca como template (teste com `{{ 1 | plus: 1 }}`, `{% if %}` e HTML na mensagem: o texto sai literal).
11. **[Segurança]** **Dado** o limite de 50 envios de orçamento por dia por tenant, **quando** o 51º envio é pedido, **então** a API responde 429 "Limite diário de envios de orçamento atingido. Tente amanhã ou baixe o PDF." e nada é enfileirado.
12. **[Segurança]** **Dado** o anexo, **quando** a fila envia, **então** o arquivo é lido pelo `IFileStorage` pelo id do anexo (nunca por URL) e só sai se o anexo pertence ao orçamento e está `Clean`; com o anexo ainda `Pending`, o `SendEmailJob` se reagenda (até 10 vezes, a cada 1 min) sem gastar as 3 tentativas de envio; `Infected` ou `Failed` marca o e-mail `Failed` com o motivo (ADR 0019 §7).
13. **Dado** o e-mail com falha depois das 3 tentativas (FND-08) ou pela varredura, **quando** abro a aba "Documentos", **então** o envio aparece "Falhou" (chip crítico) com o motivo, "Enviar de novo" e "Baixar PDF"; o orçamento continua `Sent`.
14. **Dado** a rev. 0 enviada e depois a rev. 1, **quando** abro "Documentos" de cada uma, **então** cada revisão tem os próprios PDFs e envios.

**Anexos do usuário e permissões**
15. **Dado** `quotes.edit`, **quando** anexo um arquivo na aba Documentos por "Escolher arquivo" ou arrastando, **então** o anexo do usuário (dono `Quote`) aceita pdf, png, jpg, docx, xlsx, dwg e dxf até 25 MB, passa pelo ClamAV e mostra a situação da varredura.
16. **Dado** o módulo `quotes` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; `quotes.view` gera e baixa o PDF; `quotes.edit` envia e anexa; o download do anexo segue a regra do dono `Quote` (`quotes.view`). `RequireModule` e `perm:`.

## Regras de negócio e técnicas
- **`IPdfRenderer`** nos BuildingBlocks (ADR 0022), com o layout `QuotePdfDocument` no Commercial; números com `pt-BR` e `tabular-nums`; datas em São Paulo. A licença do QuestPDF é configurada na subida conforme a decisão do PO (P-21).
- **Arquivo gerado** por `StoreGeneratedAsync` (BuildingBlocks, entregue aqui), com `origin = System` no anexo.
- **E-mail com anexo e cópia** (BuildingBlocks, entregue aqui): `IEmailSender.Enqueue` ganha `attachmentIds` e `cc`; `email_logs` ganha `attachment_ids` (uuid[]) e `cc`; o `SendEmailJob` lê do `IFileStorage` e espera o `Clean` com reagendamento próprio; limite de 10 MB por e-mail; argumentos do job só com ids.
- **Template** `quote_sent` (sistema, Fluid) com a mensagem do usuário como valor escapado e link só para o host primário do tenant (sem link do portal, que é Fase 8).
- **Dono do anexo** `Quote` no `AttachmentOwnerRegistry` (ADR 0019 §7): leitura `quotes.view`, escrita `quotes.edit`, `OwnerExists` e tipos da tabela da ADR.
- **Limite diário** contado por `quote_dispatches` do dia (São Paulo) no tenant.
- Dados do cliente no PDF vêm do `IParties` na hora da geração; o PDF guardado é a foto daquela geração.

## Permissões, eventos e tabelas
- **Permissões:** `quotes.view` (gerar e baixar PDF), `quotes.edit` (enviar, anexar).
- **Eventos:** o envio usa o outbox do e-mail (FND-08); `QuoteSent` (`{ quoteId, revision, sentAt }`), sem consumidor nesta fase.
- **Tabelas:** `shared.attachments` (dono `Quote`, + `origin`); `shared.email_logs` (+ `attachment_ids`, `cc`); `commercial.quote_dispatches` (nova: `quote_id`, `email_log_id`, `attachment_id`, `to`, `cc`, `sent_by`, `client_request_id`, `created_at`; `to` e `cc` com `[PersonalData]`).

## Telas e estados
- **Orçamento → aba Documentos**: PDFs gerados (com a marca "Gerado pelo sistema"), envios e anexos do usuário ("Escolher arquivo" e arrastar e soltar), com situação da varredura.
- **Diálogo** "Enviar por e-mail" / "Orçamento enviado"; ações "Gerar PDF" / "PDF gerado", "Baixar PDF".
- **Estados:** carregando; vazio ("Gere o PDF para enviar ao cliente." + "Gerar PDF"); erro de geração com o motivo e "Tentar de novo"; envio aguardando a varredura ("Aguardando a verificação do arquivo").
- **Propositivo:** destinatário e assunto preenchidos; PDF gerado na hora do envio; depois de enviar, "Próximos passos": "Ver orçamentos que vencem esta semana" e, com `quotes.approve`, "Aprovar e gerar contrato" (COM-04a).
- **400 px:** diálogo em tela cheia; a lista de documentos mostra nome, data e situação.
- **Ajuda:** `Ajuda/comercial-orcamento-documentos.md` (aba `documentos`); `<Dica>` em "Sem custo no PDF" ("O PDF nunca mostra custo nem margem.") e "Responder para" ("As respostas do cliente vão para o seu e-mail.").

## Plano de testes
- **Unidade:** montagem do modelo do PDF (CA 1, 4); validação de destinatário e cópia (CA 9); limpeza de CR/LF e nome de exibição (CA 10).
- **Integração (Testcontainers):** PDF gerado e anexado como arquivo do sistema, sem custo no texto extraído (CA 1–3); sem logo e perfil incompleto (CA 4); desempenho com 50 itens e limites (CA 5); PDF na imagem Alpine (CA 6); envio enfileirado com anexo, cópia, log e `Sent` na mesma transação, com idempotência (CA 7, 8); mensagem com `{{ }}`, `{% %}` e HTML saindo literal (CA 10); limite diário (CA 11); anexo por id, do mesmo dono, espera do `Clean` sem gastar tentativas (CA 12); falha visível (CA 13); por revisão (CA 14); anexo do usuário (CA 15); três camadas (CA 16).
- **E2E (Playwright):** gerar, baixar e enviar (SMTP de teste).

## Fora do escopo
- Layout editável pelo tenant (templates próprios): fora do MVP; o layout é do sistema com logo e dados do tenant.
- Aprovação online pelo cliente: POR-02. WhatsApp: WPP-01. Assinatura digital: fora do MVP.

## Pontos em aberto
Decididos pelo PO em 20/10/2026 (seção 1.1, item 23, e D17):
1. Conteúdo do PDF: o do CA 1, sem assinatura (orçamento não é contrato, PRD §5.4); termos comerciais fixos entram pelas observações.
2. O PDF gerado passa pela varredura e o e-mail espera o `Clean`, com reagendamento próprio (ADR 0019 §7).
3. PDF pelo QuestPDF atrás do `IPdfRenderer` (ADR 0022).
4. P-18 decidida (ADR 0021).

**Pré-condições externas** (não são decisão de story; bloqueiam o deploy em homologação, não o desenvolvimento):
- **P-21:** decidida em 20/10/2026: licença Community (adendo da ADR 0022).
- SPF, DKIM e DMARC conferidos no domínio de envio da plataforma (§2.2-M2), pelo PO na configuração do SMTP (FND-08).

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers, inclusive na imagem Alpine) e E2E passando.
- [ ] Migration criada (não destrutiva); template `quote_sent` registrado.
- [ ] Permissões só do `ModuleCatalog`; dono de anexo `Quote` registrado com `OwnerExists`.
- [ ] Aba com estados.
- [ ] Ajuda da aba; teste de cobertura passando.
- [ ] Próximos passos aplicados.
- [ ] Auditoria ligada (envio).
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado; story marcada no backlog; licença do QuestPDF configurada conforme a P-21.

## Tarefas técnicas sugeridas
- [ ] `IPdfRenderer` (BuildingBlocks), registro da fonte Archivo e `QuotePdfDocument` (Commercial).
- [ ] `StoreGeneratedAsync`; anexos e cópia no `IEmailSender` e no `SendEmailJob`, com a espera do `Clean`.
- [ ] Endpoints `/api/v1/quotes/{id}/pdf`, `/send-email`, `/attachments`; aba Documentos; ajuda.

## Quality gates
- Agentes previstos: @dev, @architect (PDF na imagem Alpine), cybersecurity (anexo por id, destinatário, cabeçalhos e template), @qa.
- Gate: CA 2, 6, 8, 10 e 12 obrigatórios.

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

- **Correções feitas na validação (19/10/2026):** extensão de anexo no `IEmailSender`; PDF sem custo com teste de extração; destinatário fora dos contatos com confirmação; COM-04a sem dependência desta story.
- **Correções da revalidação (20/10/2026):** o NO-GO da arquitetura foi resolvido pela ADR 0022 (`IPdfRenderer`, fonte, imagem Alpine, limites; CA 1, 5, 6) e pela ADR 0019 §7 (`StoreGeneratedAsync`, e-mail com anexo e cópia, espera do `Clean`; CA 3, 12); regras do e-mail da §2.2-M2 (endereço puro, CR/LF, "via BemAqui", Reply-To, mensagem como valor, limite diário; CA 9–11); idempotência do envio; "Escolher arquivo" (CA 15); dependência da FIN-03a (regras de anexo por dono). A licença do QuestPDF e o SPF/DKIM/DMARC viraram pré-condições externas explícitas. CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma de decisão de story. Pré-condições externas explícitas: P-21 (licença) e SPF/DKIM/DMARC, antes do deploy em homologação.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 19/10/2026 | 0.1 | Story detalhada | River (SM) |
| 19/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 20/10/2026 | 0.3 | Decisão D17 (ADR 0022), ADR 0019 §7 e correções da revisão pré-Fase 2 (§2.2-M2, baixos, §2.1-A2, §2.3); pré-condições externas; CAs renumerados | River (SM) |
| 20/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` com pré-condições externas | Pax (PO) |
| 20/10/2026 | 0.5 | P-21 decidida pelo PO: licença Community do QuestPDF (adendo da ADR 0022); resta só SPF/DKIM/DMARC antes do deploy | River (SM) |
