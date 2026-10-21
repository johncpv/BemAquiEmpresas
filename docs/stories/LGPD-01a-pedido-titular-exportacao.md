# LGPD-01a — Direitos do titular: registro do pedido e exportação dos dados (JSON e PDF)

| Campo | Valor |
|---|---|
| ID | LGPD-01a |
| Título | Registro e acompanhamento do pedido do titular (confirmação, acesso, correção, anonimização, eliminação, portabilidade) pelo Proprietário, com prazo e situação; exportação de todos os dados pessoais de um terceiro PF, de um contato ou de um funcionário em JSON e PDF, reunidos de cada módulo por um contrato de dados pessoais; pedido e atendimento auditados |
| Tamanho | M (quebra da LGPD-01 em 01a pedido e exportação, 01b anonimização) |
| Status | Draft |
| Requisitos | PRD §6 LGPD ("exportação e anonimização sob pedido") · ADR 0018 (atendimento ao titular na Fase 6) · R-SEC-5 · R-COM-3 |
| Dependências | CAD-01a (terceiro PF e contatos), CAD-04b/04c (funcionário), RH-01a a RH-02c (ponto e folha, que também têm dado pessoal), COM-03 (`IPdfRenderer`), FND-06c (Proprietário), FND-07 (auditoria e anexos) |
| Bloqueia | LGPD-01b |
| Ordem de execução | 8 (Marco 6.3 "LGPD") |

## User story
Como **proprietário (controlador dos dados)**, quero **registrar o pedido de um titular e entregar a ele todos os dados que o sistema tem sobre ele, num arquivo legível e num arquivo de máquina**, para **atender a LGPD dentro do prazo e com prova do que foi feito**.

## Contexto e referências
- PRD §6 LGPD; **ADR 0018** ("O pedido do titular… é atendido pelo **Proprietário** do tenant, que é o controlador. O BemAqui é o operador"; "exportação dos dados de um terceiro PF, contato ou funcionário em JSON e PDF"; "registro do pedido e do atendimento na auditoria"; retenção por tipo de dado; auditoria sem dado pessoal em claro).
- ADR 0016 (`[PersonalData]`, allowlist, canário, `SensitiveRead`, expurgo controlado), ADR 0009 (`[PersonalData]` na Fase 0: nome, e-mail e telefone do usuário, IP na auditoria).
- LGPD art. 18 (direitos do titular) e art. 19 (prazo da declaração completa): o prazo entra como parâmetro, confirmado pelo jurídico (Pontos em aberto 2).
- Dados pessoais espalhados pelos módulos (`03-modelo-dados`): `parties` (PF, documento, contatos, endereços), `hr.*` (funcionário, dependentes, ponto, folha, documentos), `identity.users` (nome, e-mail), `shared.email_logs.to`, `commercial.quote_dispatches.to`, `production.production_orders.accepted_by_name`, `finance.payables.payment_details`, `finance.payment_order_items.payee_snapshot`.

## Critérios de aceite
**Pedido do titular**
1. **Dado** um membro do Proprietário, **quando** abro Configurações → Privacidade (LGPD) → "Registrar pedido", escolho o titular (terceiro PF, contato de terceiro ou funcionário, pelo seletor; ou "Titular não encontrado" com nome e meio de contato), o tipo (Confirmação, Acesso, Correção, Anonimização, Eliminação, Portabilidade, Revogação de consentimento, Informação sobre compartilhamento), a data do pedido, o canal e uma observação (até 500 caracteres) e clico "Registrar pedido", **então** o pedido nasce `Open` com o número `LGPD-2026-0001` (proposta, `INumberSequence`) e o prazo = data do pedido + `privacy.response_days` (proposta: 15 dias; Pontos em aberto 2).
2. **[Segurança]** **Dado** um usuário que não é membro do Proprietário, mesmo com `settings.edit` ou `hr.view_sensitive`, **quando** chama qualquer endpoint de Privacidade, **então** 403 `forbidden` (ADR 0018: atendimento pelo Proprietário); a tela não aparece no menu.
3. **Dado** um pedido, **quando** o prazo está a 3 dias ou vencido, **então** os membros do Proprietário recebem a notificação "Pedido {número} do titular vence em {data}." com link (job diário idempotente, `dedup_key` `privacy-due:{id}:{data}`, teste de dois tenants), e a lista mostra o chip alerta ou crítico.
4. **Dado** o pedido, **quando** registro o atendimento ("Registrar resposta": data, o que foi feito, arquivo enviado ou não, observação) e clico "Concluir pedido", **então** ele fica `Done`; "Recusar" exige o motivo (ex.: retenção legal, titular não identificado) e fica `Refused`; nenhum pedido é apagado.

**Exportação**
5. **Dado** um pedido de Acesso ou Portabilidade com titular identificado, **quando** clico "Exportar dados do titular", **então** o sistema chama cada módulo pelo contrato de dados pessoais (proposta: `IPersonalDataProvider.ExportAsync(subject)` em `BuildingBlocks`, implementado por Parties, HR, Identity, Commercial, Production, Finance e BuildingBlocks para e-mail e anexos) e monta um ZIP com `dados.json` (um bloco por módulo, com a origem, os campos e as datas) e `dados.pdf` (o mesmo conteúdo legível, com o logo do tenant, o número do pedido e a data), pelo `IPdfRenderer`.
6. **Dado** o titular funcionário, **quando** exporto, **então** o arquivo traz cadastro, dados pessoais, contrato e linha do tempo, dependentes (o CPF dos dependentes conforme a decisão do ponto em aberto 3), dados bancários, afastamentos (tipo e datas), marcações de ponto (com a localização), espelhos, holerites (linhas e totais) e a lista de documentos anexados (nome e data; os arquivos vão numa pasta `anexos/` do ZIP, só os `Clean`); o titular terceiro PF traz cadastro, endereços, contatos, e-mails enviados a ele (destinatário, assunto, data) e os documentos em que aparece (número e tipo, sem o conteúdo comercial de terceiros).
7. **[Segurança]** **Dado** a exportação, **quando** gerada, **então** é guardada como arquivo do sistema do pedido (dono `PrivacyRequest`, leitura e escrita só do Proprietário, `OwnerExists`), registrada como `SensitiveRead` com o número do pedido, baixada com `no-store`, e apagada depois de 30 dias da conclusão (proposta, job idempotente); o JSON nunca leva hash de senha, segredo TOTP, token, chave de API nem dado de outro titular (teste de canário com valores conhecidos).
8. **[Segurança]** **Dado** o teste de arquitetura, **quando** roda, **então** toda entidade com propriedade `[PersonalData]` pertence a algum `IPersonalDataProvider` registrado; uma entidade nova com dado pessoal e sem provedor reprova o CI.
9. **Dado** um pedido de Correção, **quando** o Proprietário clica "Abrir cadastro", **então** vai para a tela de edição do terceiro ou do funcionário (a correção é feita lá, com a auditoria normal) e o pedido guarda o link do que foi corrigido.

**Auditoria**
10. **Dado** qualquer pedido, resposta, exportação ou download, **quando** abro o histórico do pedido, **então** vejo quem, quando, o quê e o IP; o pedido mascara o nome e o contato do titular para quem não é Proprietário (a auditoria segue a ADR 0018).

## Regras de negócio e técnicas
- **Tabelas (propostas, dono Identity/Configurações, schema `settings`):** `settings.privacy_requests` (`number`, `subject_type` Party/Contact/Employee/Unknown, `subject_id`, `subject_name` e `subject_contact` `[PersonalData]` quando Unknown, `kind`, `requested_on` `date`, `channel`, `notes`, `due_on`, `status` Open/Done/Refused, `response`, `responded_on`, `refusal_reason`, `export_attachment_id`, `xmin`).
- **Contrato `IPersonalDataProvider`** (BuildingBlocks, proposta): `ExportAsync(PersonalDataSubject)` → seções; `AnonymizeAsync` entra na LGPD-01b. Cada módulo exporta só o que é dele, sem ler tabela alheia (ADR 0006).
- **Parâmetro:** `privacy.response_days` em `settings` (Identity, ADR 0016 M6).
- Guid v7; `IAuditable` no pedido; arquivo pelo `StoreGeneratedAsync`.

## Permissões, eventos e tabelas
- **Permissões:** membro do Proprietário (conferido no servidor, como as alçadas da FIN-03b); nenhuma ação nova.
- **Eventos:** nenhum.
- **Tabelas:** `settings.privacy_requests`; dono de anexo `PrivacyRequest`; tipo de numeração `LGPD` (proposta).

## Telas e estados
- **Configurações → Privacidade (LGPD)** (`/app/configuracoes/privacidade`): lista de pedidos (número, titular mascarado, tipo, prazo, situação), filtros; detalhe com "Exportar dados do titular", "Registrar resposta", "Concluir", "Recusar".
- **Estados:** carregando; vazio ("Nenhum pedido de titular registrado. Registre aqui cada pedido recebido por e-mail, telefone ou pessoalmente."); exportando (assíncrono, com o estado de demora); erro com o módulo que falhou.
- **Propositivo:** o tipo escolhido sugere a ação ("Acesso" → "Exportar dados"; "Anonimização" → LGPD-01b); prazo calculado.
- **400 px:** lista em cartões.
- **Ajuda:** `Ajuda/configuracoes-privacidade.md` e `Ajuda/configuracoes-privacidade-pedido.md`; `<Dica>` em "Controlador" e "Prazo".

## Plano de testes
- **Unidade:** prazo (CA 1); montagem das seções (CA 5).
- **Integração (Testcontainers):** só o Proprietário (CA 2); aviso de prazo, execução dupla e dois tenants (CA 3); conclusão e recusa (CA 4); exportação completa do funcionário e do terceiro PF com dataset fixo (CA 5, 6); arquivo do sistema, `SensitiveRead`, expurgo e canário (CA 7); histórico (CA 10).
- **Arquitetura:** toda entidade com `[PersonalData]` coberta por um provedor (CA 8).
- **E2E (Playwright):** registrar um pedido de acesso e baixar a exportação.

## Fora do escopo
- Anonimização e eliminação: LGPD-01b.
- Portal para o titular fazer o pedido sozinho: depois do MVP.
- Pedido sobre usuário do Identity sem funcionário (ex.: ex-usuário): coberto pelo provedor do Identity, mas a tela escolhe só terceiro, contato e funcionário (Pontos em aberto 4).

## Pontos em aberto
1. **Onde fica a tela** (PO): Configurações → Privacidade (proposta) e só o Proprietário, como diz a ADR 0018.
2. **Prazo de resposta** (jurídico): 15 dias (proposta pelo art. 19 da LGPD) e se a contagem é em dias corridos.
3. **Dado de terceiros na exportação** (jurídico): o CPF dos dependentes e o nome de outras pessoas que aparecem nos documentos do titular entram ou saem.
4. Usuários do sistema (Identity) como titulares na tela.
5. Prazo de guarda do arquivo exportado (30 dias, proposta).

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers), arquitetura e E2E passando.
- [ ] Migration criada (não destrutiva).
- [ ] `IPersonalDataProvider` implementado em todos os módulos com dado pessoal.
- [ ] Telas com estados; ajuda escrita; teste de cobertura passando.
- [ ] Auditoria ligada, com `SensitiveRead`.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `PrivacyRequest`; `IPersonalDataProvider` e os provedores por módulo; `PersonalDataPdfDocument`.
- [ ] Job de aviso de prazo e de expurgo; endpoints `/api/v1/settings/privacy-requests`; tela e ajuda.

## Quality gates
- Agentes previstos: @dev, @architect (contrato entre módulos), cybersecurity (exportação, canário), @qa.
- Gate: CA 2, 6, 7 e 8 obrigatórios.

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

- **Correções feitas na validação:** quebra da LGPD-01 (M virou G) em pedido e exportação (01a) e anonimização (01b); só o Proprietário atende (ADR 0018, CA 2); teste de arquitetura que impede entidade com dado pessoal sem provedor (CA 8); o CA 6 tinha uma pergunta no texto, que foi movida para o ponto em aberto 3 e o CA passa a dizer "conforme a decisão do ponto em aberto 3".
- **Pré-condições para `Ready`:** pontos em aberto 2 e 3; RH-02c concluída.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da LGPD-01) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
