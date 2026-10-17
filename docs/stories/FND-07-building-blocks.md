# FND-07 — Building blocks: Result, auditoria, outbox, jobs, numeração, anexos e notificações

| Campo | Valor |
|---|---|
| ID | FND-07 |
| Título | Building blocks (Result/ProblemDetails, paginação, numeração, auditoria, soft delete, outbox/inbox, `TenantJob`, anexos, notificações) |
| Tamanho | G (re-estimada: o backlog dizia M; os CAs de segurança e a ADR 0006 ampliaram o escopo) |
| Status | In Progress (07a e 07b prontas; 07c a seguir) |
| Requisitos | R-SEC-5 (auditoria) · R-COM-2 (notificações) · R-COM-3 (anexos) · R-COM-4 (numeração) · R-PRP-5 (alerta leva à ação) |
| Dependências | FND-01, FND-11. **Pré-condição:** ADR 0006 aceita |
| Bloqueia | FND-02, FND-03a, FND-08, FND-12, FND-09c (componente de histórico), FND-09b (sino) e todas as stories que gravam dados |
| Ordem de execução | 3 |

## User story
Como **desenvolvedor dos módulos**, quero **os blocos comuns prontos e testados (erros, paginação, numeração, auditoria, soft delete, eventos entre módulos, jobs por tenant, anexos e notificações)**, para **que cada módulo use o mesmo padrão seguro e não reimplemente infraestrutura**.

## Contexto e referências
- `CLAUDE.md`: Convenções (Result + ProblemDetails RFC 9457, Guid v7, numeração, soft delete, auditoria `IAuditable`, jobs sempre com `tenantId`).
- `docs/02-arquitetura.md` §3 (lista de `BuildingBlocks`), §5 (auditoria em `audit.audit_log`), §8 (padrões de dados).
- `docs/03-modelo-dados.md` → `shared`: `number_sequences`, `attachments`, `audit_log`, `outbox_messages`, `notifications`.
- `docs/04-fluxos-estados.md` §4 (formatos de numeração).
- Revisão §2.2, ADR 0006: outbox com retry, backoff e dead-letter; `inbox` por handler; dispatcher como BackgroundService com `SKIP LOCKED`; Hangfire com storage no catálogo; `TenantJob<T>` obrigatório e um recorrente global por tipo que percorre os tenants ativos; fuso America/Sao_Paulo; filas separadas.
- Revisão §2.3, CAs de segurança da FND-07.

## Critérios de aceite
**Erros e paginação**
1. **Dado** um handler que devolve `Result.Failure(Error.Validation(...))`, **quando** o endpoint responde, **então** o corpo é `application/problem+json` (RFC 9457) com `status`, `title` em PT-BR, `code` e `errors` por campo.
2. **Dado** uma consulta paginada, **quando** chamo com `page`, `pageSize`, `sort` e filtros, **então** a resposta tem `items`, `page`, `pageSize` e `total`, e `pageSize` acima do máximo configurado é limitado ao máximo.

**Numeração**
3. **Dado** o tipo `ORC` sem uso no ano, **quando** gero o primeiro número, **então** recebo `ORC-2026-0001` (formato de `04-fluxos` §4, editável por tipo).
4. **Dado** 50 chamadas paralelas de numeração para o mesmo tipo e tenant, **quando** todas terminam, **então** os 50 números são distintos e consecutivos, sem lacuna.
5. **Dado** a virada do ano em America/Sao_Paulo, **quando** gero um número, **então** a sequência reinicia em 0001 com o novo ano.

**Auditoria**
6. **Dado** uma entidade `IAuditable` alterada, **quando** salvo, **então** `audit.audit_log` recebe entidade, id, ação, usuário, data UTC, IP e `changes` (JSONB com antes e depois só dos campos alterados), na mesma transação.
7. **[Segurança]** **Dado** o papel de banco usado pela aplicação, **quando** tenta `UPDATE` ou `DELETE` em `audit.audit_log`, **então** o banco recusa (só `INSERT` e `SELECT` concedidos).
8. **[Segurança]** **Dado** uma propriedade marcada como dado pessoal (`[PersonalData]`), **quando** ela muda, **então** o diff mostra o valor mascarado; **dado** uma propriedade `Secret` (FND-11), **então** o diff omite o valor e registra só que mudou.
9. **Dado** um registro auditado, **quando** chamo `GET /api/v1/audit/{entity}/{id}`, **então** recebo o histórico paginado, e só se tiver a permissão `view` do módulo dono da entidade.

**Soft delete**
10. **Dado** um cadastro `ISoftDeletable`, **quando** o excluo, **então** `is_deleted` vira verdadeiro, a auditoria registra `Deleted` e as consultas padrão deixam de retorná-lo.

**Outbox, inbox e jobs**
11. **Dado** um comando que publica um evento, **quando** a transação é confirmada, **então** o evento está em `outbox_messages` na mesma transação; **quando** a transação falha, **então** nenhum evento fica gravado.
12. **Dado** um evento pendente, **quando** o dispatcher roda, **então** o handler é chamado e `processed_at` é preenchido; dois dispatchers em paralelo não processam a mesma mensagem (`FOR UPDATE SKIP LOCKED`).
13. **Dado** um handler que falha, **quando** o dispatcher tenta de novo, **então** aplica backoff crescente e, depois do máximo de tentativas, move a mensagem para dead-letter com o erro, sem bloquear as seguintes.
14. **Dado** a mesma mensagem entregue duas vezes ao mesmo handler, **quando** o handler roda, **então** o efeito acontece uma vez só (registro em `inbox_messages` com chave única por handler e mensagem).
15. **Dado** um job agendado com `TenantJob<T>`, **quando** ele executa, **então** abre o escopo do tenant recebido por parâmetro; um job sem `tenantId` não compila ou falha no agendamento (teste de arquitetura).
16. **[Segurança]** **Dado** um tenant suspenso, **quando** um job ou o dispatcher de outbox desse tenant ia rodar, **então** não roda, e o log registra o motivo.
17. **Dado** um job recorrente global por tipo, **quando** dispara, **então** enfileira uma execução por tenant **ativo**, com horário calculado em America/Sao_Paulo, em fila separada da fila de e-mail.

**Anexos**
18. **[Segurança]** **Dado** um upload, **quando** o tipo declarado ou os magic bytes não estão na allowlist, **então** o upload é recusado com 422 e mensagem em PT-BR.
19. **[Segurança]** **Dado** um anexo enviado, **quando** a varredura do ClamAV ainda não terminou ou falhou, **então** o download fica bloqueado; **quando** o ClamAV detecta ameaça, **então** o arquivo é descartado e o envio é registrado como recusado.
20. **[Segurança]** **Dado** um anexo de um documento, **quando** um usuário sem a permissão `view` do módulo dono do documento pede o download, **então** recebe 403 `forbidden`.
21. **[Segurança]** **Dado** um anexo SVG ou HTML, **quando** é baixado, **então** a resposta usa `Content-Disposition: attachment`, `X-Content-Type-Options: nosniff` e nunca é servido inline.

**Notificações**
22. **Dado** `INotificationService.Notify(usuário, tipo, título, link)`, **quando** chamo sem `link`, **então** a chamada é rejeitada (todo alerta leva à ação, R-PRP-5).
23. **Dado** notificações de um usuário, **quando** chamo `GET /api/v1/notifications` e `POST /api/v1/notifications/{id}/read`, **então** recebo só as minhas, com contagem de não lidas, e consigo marcá-las como lidas.

## Regras de negócio e técnicas
- Ids `Guid` v7 gerados na aplicação. Datas em UTC (`timestamptz`); fuso de agenda America/Sao_Paulo.
- Erro padrão: `Error(code, message, type)`; códigos reservados: `validation`, `not_found`, `conflict`, `forbidden`, `module_not_released`, `tenant_suspended`, `tenant_schema_outdated`.
- Numeração por `INumberSequence` com linha bloqueada (`SELECT ... FOR UPDATE`) ou `UPDATE ... RETURNING` na mesma transação do documento; formatos padrão de `04-fluxos` §4.
- Auditoria por interceptor do EF (`SaveChanges`), diff só dos campos alterados; usuário vem de `ICurrentUser`; IP real vem do middleware de forwarded headers (FND-02).
- Outbox e inbox no banco do tenant; dispatcher único (BackgroundService) que percorre os tenants ativos.
- Hangfire com storage PostgreSQL no banco de catálogo (schema `hangfire`), filas separadas (ex.: `default`, `email`, `outbox`, `maintenance`).
- `IFileStorage` com disco local (volume), abstraído para S3 depois. Caminho físico nunca vem do nome enviado pelo usuário.
- Interfaces de catálogo usadas aqui (`ITenantDirectory` para saber os tenants ativos) são implementadas na FND-02. Nesta story os testes usam implementação falsa.

## Permissões, eventos e tabelas
- **Tabelas:** `shared.number_sequences`, `shared.attachments` (+ `sha256`, `scan_status`, `scanned_at`), `audit.audit_log`, `shared.outbox_messages` (+ `attempts`, `next_attempt_at`, `dead_lettered_at`), `shared.inbox_messages` (nova: `handler`, `message_id`, `processed_at`, único por `handler`+`message_id`), `shared.notifications`.
- **Permissões:** nenhuma nova. Histórico de auditoria e download de anexo usam a permissão `view` do módulo dono.
- **Eventos:** nenhum de negócio; a infraestrutura de publicação é entregue aqui.

## Telas e estados
Sem tela nesta story. O componente de histórico por registro fica na FND-09c e o sino na FND-09b, ambos consumindo os endpoints daqui.

## Plano de testes
- **Unidade:** mapeamento Result → ProblemDetails; formatação da numeração; cálculo de backoff; máscara de dado pessoal e omissão de segredo no diff.
- **Integração (Testcontainers):** 50 numerações paralelas (CA 4); auditoria na mesma transação e papel sem UPDATE/DELETE (CA 6, 7); outbox transacional, `SKIP LOCKED` com dois dispatchers, dead-letter e inbox idempotente (CA 11 a 14); job de tenant suspenso não roda (CA 16); upload com magic bytes inválidos, ClamAV em container com arquivo EICAR (CA 18, 19); download sem permissão (CA 20).
- **Arquitetura:** todo job Hangfire deriva de `TenantJob<T>` ou é recorrente global registrado (CA 15).

## Fora do escopo
- Telas de histórico e de notificações (FND-09b/09c).
- Eventos de negócio e as notificações específicas de cada módulo (fases 2 em diante).
- Retenção e particionamento mensal da auditoria e registro de leitura de dado sensível (revisão §3, antes das Fases 2 e 3).
- Notificação em tempo real por SignalR (pode entrar na FND-09b se o PO quiser; ver Pontos em aberto).

## Pontos em aberto
- **Allowlist de anexos:** tipos e tamanho máximo não estão definidos. Sugestão a validar: PDF, PNG, JPEG, XML, DOCX, XLSX, DWG/DXF (desenhos técnicos, R-ENG-6).
- **Política quando o ClamAV estiver fora do ar:** esta story assume "bloqueia o download até a varredura terminar" (falha fechada). Confirmar com o PO.
- **Lista de campos `[PersonalData]`** na Fase 0 (ex.: e-mail e nome de usuário?) e se o mascaramento é total ou parcial.
- **Ordem da revisão:** a FND-07 vem antes da FND-02, mas o storage do Hangfire fica no catálogo e o filtro "tenant suspenso" depende do catálogo. Esta story usa interfaces com implementação falsa nos testes; a integração real é verificada na FND-02.
- **Sino em tempo real (SignalR) ou por consulta periódica:** não definido no PRD.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers) e arquitetura passando.
- [ ] Migrations criadas (schemas `shared` e `audit`, `inbox_messages`, campos novos de `attachments` e `outbox_messages`).
- [ ] Permissão registrada no seed: não se aplica.
- [ ] Tela com estados: não se aplica.
- [ ] Ajuda da tela: não se aplica.
- [ ] Padrões e próximo passo propositivos: notificações sempre com link para a ação (CA 22).
- [ ] Auditoria ligada (é o próprio objeto da story).
- [ ] `docs/03-modelo-dados.md` atualizado com `inbox_messages` e os campos novos; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `Result<T>`, `Error`, mapeador para ProblemDetails e catálogo de códigos.
- [ ] `PagedList<T>` e parâmetros de paginação/ordenação padronizados.
- [ ] `INumberSequence` com teste de concorrência.
- [ ] Interceptor de auditoria, atributos `[PersonalData]` e uso do `Secret` da FND-11; GRANT só INSERT/SELECT em `audit_log`.
- [ ] `ISoftDeletable` com filtro global.
- [ ] Outbox, dispatcher (BackgroundService, `SKIP LOCKED`, backoff, dead-letter) e inbox por handler.
- [ ] Hangfire no catálogo, `TenantJob<T>`, recorrente global por tipo, filas separadas e fuso.
- [ ] `IFileStorage`, validação de tipo e magic bytes, ClamAV (container `clamav` no compose local), cabeçalhos de download.
- [ ] `INotificationService` e endpoints de notificações.
- [ ] Endpoint de histórico de auditoria por registro.

## Quality gates
- Agentes previstos: @architect (outbox e jobs), @data-engineer (concorrência e GRANTs), cybersecurity (anexos), @qa.
- Gate: teste de concorrência e de idempotência estáveis em 10 execuções seguidas no CI.

## Validação do PO
Checklist do @po (Pax), 16/10/2026.

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo (cabeçalho, user story, contexto, CAs, regras, permissões/tabelas, telas, testes, fora do escopo, pontos em aberto, DoD, tarefas) | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança da revisão §2.3 que se aplicam | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK |
| 7 | Plano de testes cobre os CAs (unidade, integração com Testcontainers, arquitetura, E2E quando cabe) | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas como proposta) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | N/A |
| 10 | DoD do CLAUDE.md ajustado; pontos em aberto registrados sem decidir | OK |

- **Correções feitas na validação:** —
- **Pré-condições para `Ready`:** ADR 0006 aceita; allowlist de tipos e tamanho de anexos; confirmar falha fechada com o ClamAV fora do ar.
- **Prontidão:** 8/10. **Veredito:** GO condicional (permanece `Draft` até as pré-condições serem decididas).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 16/10/2026 | 0.1 | Story detalhada a partir do backlog, da ADR 0006 proposta e dos CAs de segurança da revisão | River (SM) |
| 16/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |

## Dev Agent Record
- **Parte A (FND-07a), 17/10/2026, branch `feat/fnd-07a-building-blocks`.** A story foi entregue em três PRs: 07a (esta), 07b (outbox, inbox, jobs) e 07c (anexos e notificações).
  - **Erros:**
    - `Error` passou a ter erros por campo e os códigos reservados.
    - `ResultHttpExtensions` converte o resultado em ProblemDetails RFC 9457. Mapeamento de status: Validation 422, NotFound 404, Conflict 409, Forbidden 403, Unavailable 503.
    - Os títulos são em PT-BR e a resposta traz as extensões `code` e `errors`.
    - `ValidationResult.ToError()` faz a integração com o FluentValidation, com os nomes de campo em camelCase.
  - **Paginação:** `PageRequest` (normaliza e limita o tamanho a 100, aceita `sort` com `-` para ordem decrescente), `PagedList<T>` e `ToPagedListAsync`.
  - **Numeração:**
    - `INumberSequence`/`NumberSequenceService` incrementa de forma atômica (`INSERT ... ON CONFLICT DO UPDATE ... RETURNING`) na transação do documento.
    - Os formatos padrão seguem `04-fluxos` §4. O ano é o de America/Sao_Paulo (`BusinessTime`). O PAT não reinicia por ano.
  - **Auditoria:**
    - `AuditSaveChangesInterceptor` grava o diff só dos campos alterados, no mesmo SaveChanges, com usuário, IP e data em UTC.
    - `[PersonalData]` aparece mascarado; `[ProtectedSecret]` registra apenas `changed`.
    - O `audit.audit_log` é imutável: um trigger recusa UPDATE, DELETE e TRUNCATE, e há `REVOKE` de PUBLIC. Isso fica na migration.
  - **Histórico:**
    - O serviço `AuditHistory` fica atrás de `IPermissionChecker` (padrão `DenyAll`, falha fechada até a FND-06c) e de `AuditableEntityRegistry` (entidade → módulo dono).
    - O endpoint `GET /api/v1/audit/{entity}/{id}` está pronto, mas só será mapeado no Host na FND-02, quando existir tenant no request.
  - **Soft delete:** `ISoftDeletable` com filtro global. O Delete é convertido em exclusão lógica e auditado como `Deleted`.
  - **Base:** `BemAquiDbContext` (base) e `TenantDbContext` (Host), com a migration `InitialSharedAndAudit`.
  - **CAs atendidos:** 1 a 10, com testes de unidade e de integração em PostgreSQL real. O CA 4 (50 numerações em paralelo) foi estável em 10 execuções seguidas.

- **Parte B (FND-07b), 17/10/2026, branch `feat/fnd-07b-outbox-jobs`:**
  - **Outbox:**
    - `IEventPublisher` (`OutboxEventPublisher`) grava `shared.outbox_messages` no SaveChanges do comando, na mesma transação.
    - `OutboxProcessor` pega o lote com `FOR UPDATE SKIP LOCKED` e roda cada consumer em escopo e transação próprios, junto com `shared.inbox_messages` (PK mensagem + handler, idempotente).
    - Backoff de 5 s × 2^(n−1), com teto de 1 h. Depois de `MaxAttempts` (10), a mensagem vai para dead-letter, sem bloquear as seguintes.
  - **Dispatcher:** `OutboxDispatcher`, um BackgroundService único que percorre só os tenants ativos (tenant suspenso não roda) e acorda pelo `OutboxSignal` ou a cada 5 s.
  - **Contratos:** `IIntegrationEvent` (SharedKernel) e `IIntegrationEventConsumer<T>`, com o registro em `IntegrationEventRegistry`. O nome "consumer" vem da regra de análise CA1711.
  - **Jobs:**
    - `TenantJob<TArgs>`: o único ponto de entrada recebe o tenantId; o job não roda para tenant suspenso ou inexistente e abre um escopo novo do tenant.
    - `GlobalRecurringJob<TJob,TArgs>`: enfileira uma execução por tenant ativo, numa fila separada (`JobQueues`).
    - Teste de arquitetura: todo `*Job` deriva de uma dessas duas bases.
  - **Hangfire:** storage no catálogo (schema `hangfire`), filas `default`/`outbox`/`email`/`fiscal`/`maintenance` e 2 workers. Já está ligado no Host. O dashboard fica só no host da plataforma (FND-13).
  - **Segurança:** o `Newtonsoft.Json` 13.0.4 foi fixado por pinagem transitiva; o Hangfire puxava a 11.0.1, que tem vulnerabilidade alta.
  - **Pendente para a FND-02:**
    - implementar de verdade `ITenantDirectory`, `ITenantScopeFactory` e `ITenantContext`;
    - registrar `AddBemAquiMessaging` (com o dispatcher) no Host;
    - mapear o endpoint de histórico de auditoria.
  - **CAs atendidos:** 11 a 17. Os testes de SKIP LOCKED e de idempotência passaram estáveis em 10 execuções seguidas.
## QA Results
_A preencher pelo @qa._