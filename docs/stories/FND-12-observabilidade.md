# FND-12 — Observabilidade: logs com tenant e usuário, health checks e alertas

| Campo | Valor |
|---|---|
| ID | FND-12 |
| Título | Serilog com tenant/usuário/correlação, `/health`, alertas de jobs, integrações e migração |
| Tamanho | M |
| Status | Done |
| Requisitos | PRD §6 Observabilidade (logs estruturados com tenant e usuário; health checks; alerta de falha em jobs e integrações) · PRD §6 Disponibilidade (99,5%) |
| Dependências | FND-02 (tenant no contexto), FND-07 (jobs e dead-letter), FND-08 (e-mail de alerta), FND-11 (máscara de segredos) |
| Bloqueia | FND-10 (monitoramento em homologação) |
| Ordem de execução | 7 |

## User story
Como **responsável pela plataforma**, quero **logs estruturados com tenant e usuário, health checks e alerta quando um job, uma integração ou uma migração falha**, para **descobrir e resolver o problema antes de o cliente perceber**.

## Contexto e referências
- PRD §6 (Observabilidade, Disponibilidade).
- `docs/02-arquitetura.md` §7 (health checks `/health`, Uptime Kuma, Serilog em arquivo + Seq opcional), §2.1 (falha de migração gera alerta).
- ADR 0005 D7 e `06-pendencias-riscos.md` (risco da VPS compartilhada: monitor externo).
- Revisão §2.3 (FND-12 nova; FND-10: logs do nginx mascaram tokens).

## Critérios de aceite
1. **Dado** uma requisição autenticada, **quando** ela gera log, **então** cada evento tem `TenantId`, `UserId`, `CorrelationId` (TraceId), `RequestPath`, `StatusCode` e `ElapsedMs`, em JSON.
2. **Dado** um job Hangfire ou um handler de outbox, **quando** ele gera log, **então** o evento tem `TenantId`, `JobId`/`MessageId` e o tipo do job.
3. **[Segurança]** **Dado** um log com senha, token, cookie, cabeçalho `Authorization` ou `Secret`, **quando** inspeciono a saída, **então** o valor aparece como `***` (teste com sink em memória).
4. **Dado** `GET /health/live`, **quando** o processo está de pé, **então** responde 200 sem detalhes e sem consultar o banco.
5. **Dado** `GET /health/ready`, **quando** catálogo, ClamAV ou armazenamento de arquivos estão indisponíveis, **então** responde 503; o detalhe por componente só aparece no host da plataforma para super admin (FND-13).
6. **Dado** um job que esgota as tentativas ou uma mensagem de outbox que vai para dead-letter, **quando** isso acontece, **então** um alerta é enviado aos super admins (e-mail, FND-08) com tenant, tipo, erro resumido e link para o painel Hangfire, no máximo um alerta por tipo e tenant a cada 30 minutos.
7. **Dado** o migrator terminando com falha em algum tenant (FND-03a), **quando** termina, **então** o alerta é enviado com o relatório.
8. **Dado** uma integração externa (SMTP, e depois Inter, EmissorFiscal, IA) que falha N vezes seguidas, **quando** atinge o limite, **então** um alerta é enviado (limite configurável).
9. **Dado** a rotação de logs, **quando** o arquivo diário passa do tamanho configurado ou da retenção (padrão 30 dias), **então** o Serilog rotaciona e apaga os antigos.
10. **Dado** o monitor externo configurado, **quando** `/health/ready` fica fora por mais de 2 checagens, **então** o PO recebe aviso (configuração documentada no runbook).

## Regras de negócio e técnicas
- Serilog com `Enrich.FromLogContext`, enricher de tenant e usuário lidos de `ITenantContext` e `ICurrentUser`; saída JSON em arquivo (volume) e Seq opcional por configuração.
- Máscara por política de destructuring (FND-11) e por filtro de nomes de propriedade (`password`, `token`, `authorization`, `cookie`, `secret`, `apiKey`).
- Health checks com `AspNetCore.HealthChecks.*`: catálogo PostgreSQL, ClamAV, disco do armazenamento, Hangfire (servidor ativo).
- Alertas por `IAlertSink` (e-mail no MVP), com deduplicação por chave (tipo + tenant) e janela.
- Datas no log em UTC; exibição no e-mail de alerta em America/Sao_Paulo.

## Permissões, eventos e tabelas
- **Tabelas:** `catalog.platform_alerts` (chave, último envio, contagem) para deduplicação, ou cache distribuído equivalente.
- **Permissões:** nenhuma de tenant. Detalhe do health só para super admin no host da plataforma.
- **Eventos:** consome `EmailFailed`, falhas de job, dead-letter e o relatório do migrator.

## Telas e estados
Sem tela nesta story. O painel Hangfire é protegido na FND-13.

## Plano de testes
- **Unidade:** enricher com e sem usuário; máscara de propriedades (CA 3); deduplicação de alertas (CA 6).
- **Integração (Testcontainers):** job que falha 3× gera alerta para o SMTP de teste (CA 6); `/health/ready` 503 com ClamAV parado (CA 5); log de requisição com `TenantId` de dois tenants distintos (CA 1).

## Fora do escopo
- Métricas e tracing distribuído (OpenTelemetry, Prometheus).
- Painel de logs próprio (usa Seq ou arquivo).
- Notificação por WhatsApp ou Telegram.

## Pontos em aberto
- **Canal de alerta:** o PRD pede alerta, mas não define o canal. Esta story usa e-mail para os super admins; confirmar se o PO quer outro canal.
- **Monitor externo:** a revisão pede monitor externo (D7), e o 02-arquitetura cita Uptime Kuma "na VPS", o que não é externo. Definir o serviço (ex.: Uptime Kuma fora da VPS, UptimeRobot, Better Stack).
- **Seq:** usar ou não em homologação (memória da VPS compartilhada).
- **Rota de health anônima:** `/health/live` precisa ser anônima para o monitor; o CLAUDE.md não lista health entre as exceções. Confirmar a exceção.
- **Limite de falhas de integração (CA 8):** valor padrão não definido.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade e de integração (Testcontainers) passando.
- [ ] Migration criada (se houver `platform_alerts`).
- [ ] Permissão registrada no seed: não se aplica.
- [ ] Tela: não se aplica.
- [ ] Ajuda: runbook `docs/operacao/observabilidade.md` (onde ver logs, o que cada alerta significa e o que fazer).
- [ ] Padrões e próximo passo propositivos: todo alerta traz o link e a ação sugerida.
- [ ] Auditoria: não se aplica.
- [ ] Story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Configuração do Serilog (JSON, arquivo, rotação, Seq opcional).
- [ ] Enrichers de tenant, usuário e correlação; máscara de propriedades.
- [ ] Health checks `live` e `ready`.
- [ ] `IAlertSink` com deduplicação; ligação com Hangfire, dead-letter, migrator e integrações.
- [ ] Runbook de observabilidade e configuração do monitor externo.

## Quality gates
- Agentes previstos: @devops, @dev, @qa.
- Gate: teste de máscara de dados sensíveis obrigatório.

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
- **Pré-condições para `Ready`:** Nenhuma bloqueante (e-mail como canal padrão); confirmar a exceção de rota anônima do `/health/live`.
- **Prontidão:** 9/10. **Veredito:** GO condicional (permanece `Draft` até as pré-condições serem decididas).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 16/10/2026 | 0.1 | Story nova, criada a partir da revisão pré-Fase 0 (§2.3) | River (SM) |
| 16/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |

## Dev Agent Record
- **18/10/2026, branch `feat/fnd-12-observabilidade`.** Runbook em `docs/operacao/monitoramento.md`.
  - **Logs (`LoggingSetup`):**
    - JSON (Compact) fora de Development, no console e num arquivo opcional;
    - o arquivo roda por dia e por tamanho (100 MB) e guarda 30 dias;
    - `SensitivePropertyMask` mascara pelo nome da propriedade, inclusive dentro de objetos e listas; `SecretDestructuringPolicy` mascara pelo tipo.
  - **Contexto de cada log:**
    - request: `TenantId`, `UserId` e `CorrelationId`, mais uma linha por request com caminho, status e tempo (o logger é o do próprio host, não o estático);
    - Hangfire (filtro): `JobId`, `JobType` e `TenantId`;
    - outbox: `MessageId` e `Handler`.
  - **Health:**
    - `/health/live`: sem dependências;
    - `/health/ready`: catálogo, ClamAV (PING), escrita no storage e servidor do Hangfire, cada um podendo ser desligado em `Health`;
    - os dois respondem sem detalhe por componente.
  - **Alertas:**
    - `IAlertSink`, implementado por `CatalogAlertSink` no módulo Platform: e-mail aos super admins (`Alerts:Recipients`), com deduplicação de 30 min por tipo e tenant em `catalog.platform_alerts`;
    - disparam em: job que esgota as tentativas (`JobFailureAlertFilter`), dead-letter do outbox, e-mail `Failed`, migrator com falha (vai o relatório) e integração com N falhas seguidas (`IntegrationHealthTracker`, que já monitora o SMTP).
  - **CAs:** 1 a 9 atendidos. O CA 10 (monitor externo) está documentado no runbook; a configuração do UptimeRobot é manual e fica com o PO.
  - **Pendência herdada da FND-11 resolvida:** cabeçalhos e corpo do request não são logados.

## QA Results
_A preencher pelo @qa._