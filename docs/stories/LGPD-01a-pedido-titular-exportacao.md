# LGPD-01a — Direitos do titular: registro do pedido, inventário do dado pessoal e exportação dos dados (JSON e PDF)

| Campo | Valor |
|---|---|
| ID | LGPD-01a |
| Título | Registro e acompanhamento do pedido do titular (confirmação, acesso, correção, anonimização, eliminação, portabilidade) pelo Proprietário em Configurações → Privacidade, com prazo de 15 dias corridos e identidade conferida antes de exportar; inventário de onde há dado pessoal, com provedor ou expurgo para cada lugar; exportação de todos os dados pessoais de um terceiro PF, contato, funcionário ou usuário do sistema em JSON e PDF, reunidos de cada módulo pelo `IPersonalDataProvider`; pedido e atendimento auditados |
| Tamanho | M (quebra da LGPD-01 em 01a pedido e exportação, 01b anonimização) |
| Status | Ready (pré-condição externa) |
| Requisitos | PRD §6 LGPD ("exportação e anonimização sob pedido") · ADR 0018 (atendimento ao titular na Fase 6) · R-SEC-5 · R-COM-3 |
| Dependências | CAD-01a (terceiro PF e contatos), CAD-04b, CAD-04c1 e CAD-04c2 (funcionário), RH-01c, RH-02b, RH-02c e RH-03 (ponto, folha e holerite, o último RH do marco), FND-16 (`webhook_events`), COM-03 (`IPdfRenderer`), FND-06c (Proprietário), FND-07 (auditoria e anexos) |
| Bloqueia | LGPD-01b |
| Ordem de execução | 9 (Marco 6.3 "LGPD") |

## User story
Como **proprietário (controlador dos dados)**, quero **registrar o pedido de um titular e entregar a ele todos os dados que o sistema tem sobre ele, num arquivo legível e num arquivo de máquina**, para **atender a LGPD dentro do prazo e com prova do que foi feito**.

## Contexto e referências
- PRD §6 LGPD; **ADR 0018** ("O pedido do titular… é atendido pelo **Proprietário** do tenant, que é o controlador. O BemAqui é o operador"; "exportação dos dados de um terceiro PF, contato ou funcionário em JSON e PDF"; "registro do pedido e do atendimento na auditoria"; retenção por tipo de dado; auditoria sem dado pessoal em claro).
- **ADR 0037** (`docs/adr/0037-execucao-lgpd.md`: provedores, inventário de onde há dado pessoal, expurgos, backups).
- ADR 0016 (`[PersonalData]`, allowlist, canário, `SensitiveRead`, expurgo controlado), ADR 0009 (`[PersonalData]` na Fase 0: nome, e-mail e telefone do usuário, IP na auditoria), ADR 0031 (retenção do corpo dos webhooks).
- **Revisão pré-Fases 4 a 8** (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`): tabela 3.3 pontos 31 a 35, §4.2 (LGPD-01: inventário, `IPersonalDataProvider`, `PersonalDataTests` cobrindo as entidades novas, texto livre do RH marcado `[PersonalData]`), §4.3 (transversal: jobs com `tenantId`).
- LGPD art. 18 (direitos do titular) e art. 19 (prazo da declaração completa): prazo de 15 dias corridos, parâmetro pré-configurado.
- Dados pessoais espalhados pelos módulos (`03-modelo-dados`): `parties` (PF, documento, contatos, endereços), `hr.*` (funcionário, dependentes, conta de salário, ponto, ciência de localização, ajustes, espelhos, folha, documentos), `identity.users` (nome, e-mail), `shared.email_logs.to`, `commercial.quote_dispatches.to`, `production.production_orders.accepted_by_name`, `finance.payables.payment_details`, `finance.payment_order_items.payee_snapshot`, notificações, `idempotency_records`, `webhook_events`, payload do outbox, Hangfire e backups.

## Critérios de aceite
**Pedido do titular**
1. **Dado** um membro do Proprietário, **quando** abro Configurações → Privacidade (LGPD) → "Registrar pedido", escolho o titular (terceiro PF, contato de terceiro, funcionário ou usuário do sistema, pelo seletor; ou "Titular não encontrado" com nome e meio de contato), o tipo (Confirmação, Acesso, Correção, Anonimização, Eliminação, Portabilidade, Revogação de consentimento, Informação sobre compartilhamento), a data do pedido, o canal e uma observação (até 500 caracteres) e clico "Registrar pedido", **então** o pedido nasce `Open` com o número `LGPD-2026-0001` (`INumberSequence`, tipo `LGPD`) e o prazo = data do pedido + `privacy.response_days` (15 dias corridos, parâmetro pré-configurado e editável na mesma tela).
2. **[Segurança]** **Dado** um usuário que não é membro do Proprietário, mesmo com `settings.edit` ou `hr.view_sensitive`, **quando** chama qualquer endpoint de Privacidade, **então** 403 `forbidden` (ADR 0018: atendimento pelo Proprietário); a tela não aparece no menu.
3. **Dado** um pedido, **quando** o prazo está a 3 dias ou vencido, **então** os membros do Proprietário recebem a notificação "Pedido {número} do titular vence em {data}." com link (job diário idempotente com `tenantId`, só para tenant com o módulo liberado, `dedup_key` `privacy-due:{id}:{data}`, teste de dois tenants), e a lista mostra o chip alerta ou crítico.
4. **Dado** o pedido, **quando** registro o atendimento ("Registrar resposta": data, o que foi feito, arquivo enviado ou não, observação) e clico "Concluir pedido", **então** ele fica `Done`; "Recusar" exige o motivo (ex.: retenção legal, titular não identificado) e fica `Refused`; nenhum pedido é apagado.

**Identidade do titular**
5. **[Segurança]** **Dado** um pedido de Acesso, Portabilidade, Correção, Anonimização ou Eliminação, **quando** o Proprietário clica "Conferir identidade", **então** registra como conferiu (documento apresentado, confirmação pelo e-mail ou telefone do cadastro, presencialmente), a data e uma observação (sem copiar o documento); sem a identidade conferida, "Exportar dados do titular" e "Prever anonimização" ficam desabilitados e a API responde 409 `subject_identity_not_verified`.

**Inventário do dado pessoal**
6. **[Segurança]** **Dado** o inventário da ADR 0037, **quando** a story termina, **então** cada lugar com dado pessoal tem um provedor ou um expurgo, conferido por teste: cadastros e documentos (provedor do módulo dono); `shared.email_logs` e `commercial.quote_dispatches` (provedor e destinatário mascarado); notificações (provedor do BuildingBlocks); `idempotency_records` (expurgo de 30 dias, D19); `webhook_events` (expurgo de 90 dias do corpo, ADR 0031); payload do outbox e argumentos de job do Hangfire (sem dado pessoal, só ids; histórico de jobs expurgado); `finance.payment_order_items.payee_snapshot` (provedor do Finance); backups (expiram em até 30 dias, FND-10b); usuários do portal (provedor obrigatório na POR-01a).
7. **[Segurança]** **Dado** o teste de arquitetura `PersonalDataTests` (hoje só Parties e HR), **quando** roda, **então** cobre todos os módulos: toda entidade com propriedade `[PersonalData]` pertence a um `IPersonalDataProvider` registrado ou a um expurgo registrado; o texto livre do RH (justificativas, observações, motivos) é `[PersonalData]`; tipo de evento do outbox e argumento de job com propriedade `[PersonalData]` reprova; uma entidade nova com dado pessoal e sem provedor reprova o CI.

**Exportação**
8. **Dado** um pedido de Acesso ou Portabilidade com a identidade conferida, **quando** clico "Exportar dados do titular", **então** o sistema chama cada módulo pelo `IPersonalDataProvider.ExportAsync(subject)` (BuildingBlocks, implementado por Parties, HR, Identity, Commercial, Production, Finance, Fiscal e BuildingBlocks para e-mail, notificações e anexos) num job com `tenantId`, e monta um ZIP com `dados.json` (um bloco por módulo, com a origem, os campos e as datas) e `dados.pdf` (o mesmo conteúdo legível, com o logo do tenant, o número do pedido e a data), pelo `IPdfRenderer`.
9. **Dado** o titular funcionário, **quando** exporto, **então** o arquivo traz cadastro, dados pessoais, contrato e linha do tempo, dependentes como o titular informou (nome, parentesco e nascimento; o CPF de dependente e de qualquer outra pessoa sai mascarado), conta de salário, afastamentos (motivo e datas), marcações de ponto (com a localização) e a ciência da geolocalização, ajustes e abonos, espelhos e "Li e confirmo", holerites (linhas e totais) e a lista de documentos anexados (nome e data; os arquivos vão numa pasta `anexos/` do ZIP, só os `Clean`); o titular terceiro PF traz cadastro, endereços, contatos, e-mails enviados a ele (destinatário, assunto, data) e os documentos em que aparece (número e tipo, sem o conteúdo comercial de terceiros); o usuário do sistema traz nome, e-mail, grupos, acessos registrados e notificações.
10. **[Segurança]** **Dado** a exportação, **quando** gerada, **então** é guardada como arquivo do sistema do pedido (dono `PrivacyRequest`, leitura e escrita só do Proprietário, `OwnerExists`, storage com prefixo do tenant), registrada como `SensitiveRead` com o número do pedido, baixada só com login, por link de download de uso único que expira em 15 minutos, com `no-store`, e apagada 30 dias depois da conclusão (job idempotente com `tenantId`); o JSON nunca leva hash de senha, segredo TOTP, token, chave de API nem dado de outro titular (teste de canário com valores conhecidos).
11. **Dado** um pedido de Correção, **quando** o Proprietário clica "Abrir cadastro", **então** vai para a tela de edição do terceiro, do funcionário ou do usuário (a correção é feita lá, com a auditoria normal) e o pedido guarda o link do que foi corrigido.

**Auditoria**
12. **Dado** qualquer pedido, conferência de identidade, resposta, exportação ou download, **quando** abro o histórico do pedido, **então** vejo quem, quando, o quê e o IP; o pedido mascara o nome e o contato do titular fora da tela do Proprietário (a auditoria segue a ADR 0018).

## Regras de negócio e técnicas
- **Tabelas (dono Identity/Configurações, schema `settings`):** `settings.privacy_requests` (`number`, `subject_type` Party/Contact/Employee/User/Unknown, `subject_id`, `subject_name` e `subject_contact` `[PersonalData]` quando Unknown, `kind`, `requested_on` `date`, `channel`, `notes`, `due_on`, `identity_verified_at`, `identity_verified_by`, `identity_method`, `identity_notes` `[PersonalData]`, `status` Open/Done/Refused, `response`, `responded_on`, `refusal_reason`, `export_attachment_id`, `xmin`).
- **Contrato `IPersonalDataProvider`** (BuildingBlocks, ADR 0037): `ExportAsync(PersonalDataSubject)` → seções; `PlanAnonymizationAsync` e `AnonymizeAsync` entram na LGPD-01b. Cada módulo exporta só o que é dele, sem ler tabela alheia (ADR 0006). Registro de expurgos (`IPersonalDataPurge`) para o que não tem provedor.
- **Parâmetro:** `privacy.response_days` (15, dias corridos) em `settings` (Identity, ADR 0016 M6).
- Guid v7; `IAuditable` no pedido; arquivo pelo `StoreGeneratedAsync`.

## Permissões, eventos e tabelas
- **Permissões:** membro do Proprietário (conferido no servidor, como as alçadas da FIN-03b); nenhuma ação nova; o módulo da tela é o de Configurações.
- **Eventos:** nenhum.
- **Tabelas:** `settings.privacy_requests`; dono de anexo `PrivacyRequest`; tipo de numeração `LGPD`.

## Telas e estados
- **Padrões comuns das Fases 4 a 8:** 8 (bloco "Dados protegidos" e titular mascarado fora da tela do Proprietário).
- **Configurações → Privacidade (LGPD)** (`/app/configuracoes/privacidade`): lista de pedidos (número, titular mascarado, tipo, prazo, situação), filtros; detalhe com "Conferir identidade", "Exportar dados do titular", "Registrar resposta", "Concluir", "Recusar"; o prazo de resposta editável na mesma tela.
- **Estados:** carregando; vazio ("Nenhum pedido de titular registrado. Registre aqui cada pedido recebido por e-mail, telefone ou pessoalmente."); identidade não conferida (ações desabilitadas com o motivo); exportando (assíncrono, com o estado de demora); erro com o módulo que falhou.
- **Propositivo:** o tipo escolhido sugere a ação ("Acesso" → "Conferir identidade" e depois "Exportar dados"; "Anonimização" → LGPD-01b); prazo calculado.
- **400 px:** lista em cartões; botões de `ErrorState`/`EmptyState` com 44 px.
- **Ajuda:** `Ajuda/configuracoes-privacidade.md` e `Ajuda/configuracoes-privacidade-pedido.md`; `<Dica>` em "Controlador", "Prazo" e "Conferir identidade".

## Plano de testes
- **Unidade:** prazo em dias corridos (CA 1); montagem das seções e máscara de CPF de terceiros (CA 8, 9).
- **Integração (Testcontainers):** só o Proprietário (CA 2); aviso de prazo, execução dupla e dois tenants (CA 3); conclusão e recusa (CA 4); exportação bloqueada sem identidade conferida (CA 5); expurgos do inventário (CA 6); exportação completa do funcionário, do terceiro PF e do usuário com dataset fixo (CA 8, 9); arquivo do sistema, link de uso único que expira, `SensitiveRead`, expurgo de 30 dias e canário (CA 10); correção (CA 11); histórico (CA 12).
- **Arquitetura:** `PersonalDataTests` em todos os módulos, com outbox e argumentos de job (CA 7).
- **E2E (Playwright):** registrar um pedido de acesso, conferir a identidade e baixar a exportação.

## Fora do escopo
- Anonimização e eliminação: LGPD-01b.
- Portal para o titular fazer o pedido sozinho: depois do MVP.
- Provedor dos usuários do portal do cliente: POR-01a (o teste do CA 7 obriga).

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fases 4 a 8, tabela 3.3, pontos 31–35):
1. **Tela:** Configurações → Privacidade, só o Proprietário (CA 1, 2).
2. **Prazo de resposta:** 15 dias corridos, como parâmetro (CA 1, 3).
3. **Dado de terceiros:** dependentes como o titular informou; CPF de terceiros mascarado (CA 9).
4. **Usuários do sistema** incluídos como titulares (CA 1, 9).
5. **Arquivo guardado 30 dias;** download autenticado com expiração; identidade conferida e registrada antes de exportar (CA 5, 10).

Decisões da revisão aplicadas: §4.2 (inventário do dado pessoal, `IPersonalDataProvider`, `PersonalDataTests` cobrindo as entidades novas e o texto livre do RH, ADR 0037, CA 6, 7); §4.3 (jobs com `tenantId` e módulo liberado, CA 3, 8, 10); §4.4 padrão 8.

**Pré-condição externa:** o jurídico confere o prazo de 15 dias e o tratamento do dado de terceiros (valor pré-configurado e editável em Configurações → Privacidade); não bloqueia o `Ready`.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers), arquitetura e E2E passando.
- [ ] Migration criada (não destrutiva).
- [ ] `IPersonalDataProvider` implementado em todos os módulos com dado pessoal; expurgos do inventário registrados (ADR 0037).
- [ ] Telas com estados; ajuda escrita; teste de cobertura passando.
- [ ] Auditoria ligada, com `SensitiveRead`.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `PrivacyRequest` com a conferência de identidade; `IPersonalDataProvider` e os provedores por módulo; `IPersonalDataPurge`; `PersonalDataPdfDocument`.
- [ ] `PersonalDataTests` em todos os módulos (entidades, outbox, jobs).
- [ ] Jobs de aviso de prazo e de expurgo; endpoints `/api/v1/settings/privacy-requests`; tela e ajuda.

## Quality gates
- Agentes previstos: @dev, @architect (contrato entre módulos, inventário), cybersecurity (exportação, identidade, canário), @qa.
- Gate: CA 2, 5, 6, 7, 9 e 10 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fases 4 a 8).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK (ajustado: identidade, inventário, CA 5 a 7) |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: depois de todo o RH) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** quebra da LGPD-01 (M virou G) em pedido e exportação (01a) e anonimização (01b); só o Proprietário atende; teste de arquitetura que impede entidade com dado pessoal sem provedor; a pergunta do CA de exportação do funcionário virou ponto em aberto.
- **Correções da revalidação (21/10/2026):** pontos 31 a 35 decididos: Configurações → Privacidade só do Proprietário (CA 1, 2), 15 dias corridos (CA 1), dependentes como o titular informou e CPF de terceiros mascarado (CA 9), usuários do sistema como titulares (CA 1, 9), identidade conferida antes de exportar (CA 5, novo) e download de uso único com expiração (CA 10); §4.2 aplicada: inventário com provedor ou expurgo para cada lugar (CA 6, novo) e `PersonalDataTests` em todos os módulos, com outbox e jobs (CA 7); jobs com `tenantId`; dependência do último RH do marco (RH-01c, RH-02b, RH-03); ordem 9 do Marco 6.3; CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma de decisão de story. Externa: conferência do jurídico (prazo e dado de terceiros).
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready (pré-condição externa)`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da LGPD-01) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (tabela 3.3 pontos 31–35, ADR 0037, §4.2 inventário, §4.3, §4.4 padrão 8); nova ordem (Marco 6.3, ordem 9); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
