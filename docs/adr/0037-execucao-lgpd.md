# ADR 0037: Execução da LGPD

- Status: Aceito (21/10/2026). O PO (John) aprovou as seções 4.2 e 4.3 (LGPD-01) da revisão pré-Fases 4 a 8 (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`), os pontos 31 a 39 da tabela 3.3 e a anonimização de titular com 2FA de novo (D6).
- Contexto: a ADR 0018 decidiu a retenção, o conflito entre auditoria imutável e eliminação (nenhum dado pessoal em claro na auditoria) e que o pedido do titular é atendido pelo Proprietário. As stories LGPD-01a (pedido e exportação) e LGPD-01b (anonimização) dependem de cada módulo saber exportar e anonimizar o que é dele. A revisão encontrou dado pessoal fora das entidades de cadastro, que nenhum provedor cobria: `idempotency_records` (corpo do comando), `webhook_events` (corpo do aviso), `email_logs` (destinatário e modelo), notificações, usuários do portal, payload do outbox, argumentos do Hangfire e backups. O teste `PersonalDataTests` de hoje cobre só Parties e HR.
- Complementa as ADRs 0006 (fronteiras, outbox), 0009 (`[PersonalData]`), 0016 (expurgo controlado, `SensitiveRead`), 0018 (retenção e titular) e 0031 (retenção do webhook).

## Decisão

### 1. Provedor por módulo
- Contrato **`IPersonalDataProvider`** (BuildingBlocks): `ExportAsync(subject)`, `PlanAnonymizationAsync(subject, today)` e `AnonymizeAsync(subject, requestNumber)`. Cada módulo exporta e anonimiza **só o que é dele**, sem ler tabela de outro (ADR 0006).
- Titulares: terceiro PF, contato, funcionário, **usuário do sistema** e usuário do portal. Dependentes do funcionário entram só como informados pelo titular, com o CPF de terceiros mascarado.

### 2. Inventário de onde há dado pessoal
| Onde | Tratamento |
|---|---|
| Entidades de cadastro e documentos (Parties, HR, Identity, Portal, Assets com responsável) | `IPersonalDataProvider` do módulo dono |
| `idempotency_records` | Expurgo de 30 dias (D19), com o usuário no escopo da chave; do pedido fica só o hash, e a resposta guardada sai junto no expurgo |
| `shared.webhook_events` | Corpo apagado em 90 dias (ADR 0031) |
| `email_logs` | Destinatário `[PersonalData]`; modelo dos e-mails de convite e redefinição apagado depois do envio (D19); expurgo do conteúdo em 90 dias, mantendo data, tipo e situação |
| Notificações | Texto sem dado pessoal além do primeiro nome; expurgo das lidas em 180 dias |
| Payload do outbox e da inbox | Eventos só com ids, sem texto livre (teste de allowlist dos eventos); mensagens processadas expurgadas em 30 dias |
| Argumentos do Hangfire | Só `tenantId` e ids (teste de arquitetura sobre os `TenantJob<TArgs>`); histórico de jobs expurgado em 30 dias |
| Auditoria | Campo `[PersonalData]` só mascarado (ADR 0018), sem mudança |
| Backups | Não são editados: a anonimização vale para o banco vivo; um backup restaurado reaplica as anonimizações registradas em `privacy_requests` antes de voltar ao uso (passo do runbook de restauração); backups expiram pela política de retenção do bucket |
- Cada item novo com dado pessoal entra ou no provedor do módulo, ou ganha expurgo com prazo nesta tabela.

### 3. Testes
- O **`PersonalDataTests`** (arquitetura) passa a cobrir **todos os módulos**: toda entidade com propriedade `[PersonalData]` pertence a um provedor registrado; entidade nova com dado pessoal e sem provedor reprova o CI.
- **Texto livre do RH** (observações, justificativas de ponto, motivos de afastamento) é marcado `[PersonalData]`.
- Teste de canário: o dado de um titular anonimizado não aparece em nenhuma tabela do inventário depois da anonimização (exceto os retidos por obrigação legal, listados no resumo).

### 4. Atendimento
- **Configurações → Privacidade**, só membros do Proprietário (conferido no servidor). Prazo de resposta de **15 dias corridos**.
- **Identidade do titular conferida e registrada** (como, por quem, quando) antes de exportar.
- Exportação em JSON e PDF, guardada **30 dias** e baixada por download autenticado com expiração.
- **Eliminação atendida por anonimização**, mantendo os documentos com movimento (marcador no lugar do dado); o que tem obrigação legal fica retido pela tabela de retenção por campo (ADR 0018, conferida com a contadora e o jurídico).
- **Anonimizar exige 2FA de novo** (ADR 0008, adendo, D6) e **confirmação digitada** do nome do titular (padrão 7 das Fases 4 a 8).
- **Uma transação por módulo**, com etapas idempotentes e retomáveis (cada etapa registrada no pedido); `SubjectAnonymized` (só ids) avisa os módulos que guardam cópia (ex.: retrato do favorecido da OP).
- Anonimização automática ao fim da retenção fica depois do MVP.

## Consequências
- **LGPD-01a** implementa o contrato com `ExportAsync`, o inventário e o `PersonalDataTests` ampliado; **LGPD-01b** o planejamento, a anonimização e o passo do runbook de restauração.
- As stories que criam dado pessoal novo (POR-01a, WPP-01, FND-16, RH-01a a RH-02c, PAT-01a) registram o provedor ou o expurgo na própria story.
- Os expurgos apagam dado de propósito e estão cobertos por esta ADR e pela ADR 0031 (não são migrations destrutivas).
