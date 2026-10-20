# COM-03 — PDF e envio do orçamento

| Campo | Valor |
|---|---|
| ID | COM-03 |
| Título | PDF do orçamento com o layout do tenant (QuestPDF, logo, dados da empresa, itens, condições, validade e prazo, sem custo nem margem), anexado à revisão, e envio por e-mail ao contato pela fila, com o PDF anexo e registrado no log |
| Tamanho | M |
| Status | Draft |
| Requisitos | R-COML-5 (PDF com layout do tenant e envio por e-mail) · R-COM-1 (e-mail por fila, templates, histórico) · R-COM-3 (anexos) · R-PRP-2 |
| Dependências | COM-02 (PDF por revisão), COM-01b (condições), CAD-06 (perfil da empresa e logo `TenantLogo`), FND-08 (e-mail), FND-07 (anexos) |
| Bloqueia | — (COM-04a não depende: "Marcar como enviado" já existe na COM-01a) |
| Ordem de execução | 9 (Marco 2.2 — fecha o orçamento real) |

## User story
Como **comercial**, quero **gerar o PDF do orçamento com a cara da empresa e mandar por e-mail ao contato do cliente em um clique**, para **não montar proposta no Word e ter registrado o que foi enviado a quem**.

## Contexto e referências
- PRD R-COML-5, R-COM-1, R-COM-3; backlog COM-03 (CA: "PDF anexado ao orçamento e ao log de e-mail").
- ADR 0009 (QuestPDF Community; o pacote entra na Fase 2; reavaliar a licença a cada tenant; Fluid para todos os templates de e-mail); `02-arquitetura` §3 (`IPdfRenderer` nos BuildingBlocks).
- FND-08 (`IEmailSender.Enqueue` grava `email_logs` e o outbox; templates do sistema; links com o host primário; nunca buscar URL fornecida pelo tenant). Hoje o `IEmailSender` não aceita anexo.
- CAD-06 (perfil da empresa em `settings.company_profile`; logo = anexo `TenantLogo` mais recente com `Clean`).
- FND-07 / ADR 0016 (`AttachmentOwnerRegistry`, ClamAV, download bloqueado até a varredura).

## Critérios de aceite
**PDF**
1. **Dado** `quotes.view` e a revisão corrente, **quando** clico "Gerar PDF", **então** o PDF é gerado no servidor (QuestPDF, fonte Archivo embutida) com: logo e razão social, CNPJ, endereço e contato da empresa (perfil da CAD-06); número e revisão; data; cliente (nome, documento, endereço da obra e contato); itens com descrição, quantidade, unidade, preço unitário e total; total geral; condições por marco (descrição, %, valor, gatilho); validade; prazo de entrega; observações; e o rodapé "Orçamento válido até {data}. Sem efeito de contrato." (ver Pontos em aberto 1).
2. **[Segurança]** **Dado** o PDF gerado, **quando** inspeciono o conteúdo, **então** não há custo, composição de custo, markup, margem, cargo ou custo-hora em nenhum lugar (teste extrai o texto do PDF e procura os valores de custo do dataset).
3. **Dado** o PDF gerado, **quando** a geração termina, **então** ele é gravado como anexo do orçamento (dono `Quote`, a revisão), nome `ORC-2026-0007-rev1.pdf`, aparece na aba "Documentos" e uma nova geração da mesma revisão substitui a anterior como "atual", mantendo a anterior no histórico de anexos.
4. **Dado** o tenant sem logo `Clean`, **quando** gero, **então** o PDF sai só com o nome fantasia no lugar do logo; perfil da empresa incompleto mostra antes o aviso "Complete os dados da empresa para o PDF." com link para Configurações → Empresa (não bloqueia).
5. **Dado** um orçamento com 50 itens, **quando** gero, **então** o PDF sai em menos de 3 s, com quebra de página que repete o cabeçalho da tabela.

**Envio**
6. **Dado** `quotes.edit` e a revisão corrente `Draft` ou `Sent` com validade não vencida, **quando** clico "Enviar por e-mail", **então** o diálogo vem com o destinatário = o contato do orçamento (ou o contato de cobrança do cliente), assunto "Orçamento {número} rev. {n} — {nome fantasia}", mensagem padrão editável (até 2.000 caracteres, texto simples), o PDF atual anexo (gerado na hora se não existir) e o botão "Enviar orçamento".
7. **Dado** a confirmação, **quando** envio, **então** na mesma transação o e-mail entra na fila (`IEmailSender.Enqueue` com o template de sistema `quote_sent` e o anexo), `email_logs` grava `related_type = Quote`, `related_id` e o id do anexo, e um orçamento `Draft` passa a `Sent` (como no "Marcar como enviado"); o toast diz "Orçamento enviado" e a aba "Documentos" mostra o envio (para quem, quando, por quem, situação do e-mail).
8. **Dado** o e-mail com falha depois das 3 tentativas (FND-08), **quando** abro a aba "Documentos", **então** o envio aparece "Falhou" (chip crítico) com "Enviar de novo" e "Baixar PDF"; o orçamento continua `Sent`.
9. **[Segurança]** **Dado** o destinatário, **quando** informo um e-mail que não é contato do cliente, **então** o diálogo mostra o aviso âmbar "Este e-mail não está nos contatos de {cliente}." e exige marcar "Confirmo o destinatário"; o endereço é validado (formato, até 254 caracteres, um só destinatário mais até 3 em cópia) e o envio é auditado.
10. **[Segurança]** **Dado** o anexo, **quando** a fila envia, **então** o arquivo é lido pelo `IFileStorage` pelo id do anexo (nunca por URL) e só sai se o anexo pertence ao orçamento e tem situação liberada (ver Pontos em aberto 2); a mensagem digitada é escapada no template Fluid (sem HTML do usuário).
11. **Dado** a rev. 0 enviada e depois a rev. 1, **quando** abro "Documentos" de cada uma, **então** cada revisão tem os próprios PDFs e envios.

**Permissões**
12. **Dado** o módulo `quotes` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; `quotes.view` gera e baixa o PDF; `quotes.edit` envia; o download do anexo segue a regra do dono `Quote` (`quotes.view`).

## Regras de negócio e técnicas
- **`IPdfRenderer`** nos BuildingBlocks (QuestPDF Community, licença registrada na subida), com o layout do orçamento no Commercial; números com `pt-BR` e `tabular-nums`; datas em São Paulo.
- **Anexo no e-mail** (proposta, BuildingBlocks): `IEmailSender.Enqueue` ganha `attachmentIds`; `email_logs` ganha `attachment_ids` (uuid[]); o `SendEmailJob` lê do `IFileStorage`; limite de 10 MB por e-mail.
- **Template** `quote_sent` (sistema, Fluid) com link para nada além do host primário do tenant (sem link do portal, que é Fase 8).
- **Dono do anexo** `Quote` no `AttachmentOwnerRegistry`: leitura `quotes.view`, escrita `quotes.edit`, só PDF gerado pelo sistema e anexos do usuário (pdf, png, jpg, docx, xlsx, dwg, dxf), 25 MB.
- Dados do cliente no PDF vêm do `IParties` na hora da geração; o PDF guardado é a foto daquela geração.

## Permissões, eventos e tabelas
- **Permissões:** `quotes.view` (gerar e baixar PDF), `quotes.edit` (enviar).
- **Eventos:** o envio usa o outbox do e-mail (FND-08); `QuoteSent` (`{ quoteId, revision, sentAt }`), sem consumidor nesta fase.
- **Tabelas:** `shared.attachments` (dono `Quote`); `shared.email_logs` (+ `attachment_ids`, **proposta**); `commercial.quote_dispatches` (**proposta**: `quote_id`, `email_log_id`, `attachment_id`, `to`, `sent_by`, `created_at`; `to` com `[PersonalData]`).

## Telas e estados
- **Orçamento → aba Documentos**: PDFs gerados, envios e anexos do usuário (arrastar e soltar), com situação da varredura.
- **Diálogo** "Enviar por e-mail" / "Orçamento enviado"; ações "Gerar PDF" / "PDF gerado", "Baixar PDF".
- **Estados:** carregando; vazio ("Gere o PDF para enviar ao cliente." + "Gerar PDF"); erro de geração com o motivo e "Tentar de novo".
- **Propositivo:** destinatário e assunto preenchidos; PDF gerado na hora do envio; depois de enviar, "Próximos passos": "Ver orçamentos que vencem esta semana" e, com `quotes.approve`, "Aprovar e gerar contrato" (COM-04a).
- **Ajuda:** `Ajuda/comercial-orcamento-documentos.md` (aba `documentos`); `<Dica>` em "Sem custo no PDF" ("O PDF nunca mostra custo nem margem.").

## Plano de testes
- **Unidade:** montagem do modelo do PDF (CA 1, 4); validação do destinatário (CA 9).
- **Integração (Testcontainers):** PDF gerado e anexado, sem custo no texto extraído (CA 1–3); sem logo e perfil incompleto (CA 4); desempenho com 50 itens (CA 5); envio enfileirado com anexo, log e `Sent` na mesma transação (CA 6, 7); falha visível (CA 8); anexo por id, do mesmo dono, e mensagem escapada (CA 10); por revisão (CA 11); três camadas (CA 12).
- **E2E (Playwright):** gerar, baixar e enviar (SMTP de teste).

## Fora do escopo
- Layout editável pelo tenant (templates próprios): fora do MVP; o layout é do sistema com logo e dados do tenant.
- Aprovação online pelo cliente: POR-02. WhatsApp: WPP-01.

## Pontos em aberto
1. **Conteúdo do PDF.** **Recomendação:** o do CA 1, sem assinatura (orçamento não é contrato, PRD §5.4); termos comerciais fixos (garantia, frete) entram pelas observações até o PO pedir um campo próprio.
2. **Varredura do PDF gerado pelo sistema.** O ClamAV bloqueia download até varrer. **Recomendação:** o PDF gerado pelo sistema passa pela varredura como qualquer anexo e a fila de e-mail espera `Clean` (com nova tentativa); se a espera atrapalhar, marcar como origem "Gerado pelo sistema" em ADR.
3. **P-18:** ver FIN-01, ponto 5.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers) e E2E passando.
- [ ] Migration criada (não destrutiva); template `quote_sent` registrado.
- [ ] Permissões só do `ModuleCatalog`; dono de anexo registrado.
- [ ] Aba com estados.
- [ ] Ajuda da aba; teste de cobertura passando.
- [ ] Próximos passos aplicados.
- [ ] Auditoria ligada (envio).
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado; story marcada no backlog; licença QuestPDF conferida (ADR 0009).

## Tarefas técnicas sugeridas
- [ ] `IPdfRenderer` (BuildingBlocks) e `QuotePdfDocument` (Commercial).
- [ ] Anexos no `IEmailSender` e no `SendEmailJob`.
- [ ] Endpoints `/api/v1/quotes/{id}/pdf`, `/send-email`; aba Documentos; ajuda.

## Quality gates
- Agentes previstos: @dev, cybersecurity (anexo por id, destinatário, template), @qa.
- Gate: CA 2, 7 e 10 obrigatórios.

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

- **Correções feitas na validação (19/10/2026):** o `IEmailSender` não tem anexo hoje: extensão marcada como proposta nos BuildingBlocks; PDF sem custo nem margem com teste de extração de texto (CA 2); destinatário fora dos contatos com confirmação (CA 9); COM-04a sem dependência desta story.
- **Pré-condições para `Ready`:** Pontos em aberto 1 e 2 (com padrão recomendado) e P-18.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 19/10/2026 | 0.1 | Story detalhada | River (SM) |
| 19/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
