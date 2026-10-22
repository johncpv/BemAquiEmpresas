# ADR 0006 — Persistência, fronteiras de módulo e mensageria

- Status: Aceito (16/10/2026)
- Origem: revisão pré-Fase 0, §2.2 (achados de arquitetura e segurança)

## Decisão

### Estrutura e fronteiras
- Cada módulo tem 5 projetos: `Domain`, `Application`, `Infrastructure`, `Endpoints` e `Contracts`.
- **Um módulo só pode referenciar `*.Contracts` de outro módulo.** Domain, Application, Infrastructure e Endpoints de outro módulo são proibidos, e o NetArchTest garante isso.
- `Domain` não referencia EF Core, ASP.NET Core nem Npgsql.
- Entre módulos, a referência é só por Id. Não há propriedade de navegação EF entre módulos. FK física é permitida apenas onde a integridade for crítica e estiver documentada no `03-modelo-dados.md`.

### Persistência
- **Um único `TenantDbContext`** por banco de tenant, com schema PostgreSQL por módulo.
- Cada módulo expõe a sua interface de dados (ex.: `IFinanceDb`, com os seus `DbSet`s), implementada pelo `TenantDbContext`. Handlers só recebem a interface do próprio módulo.
- As configurações de entidade são `internal` e ficam no `Infrastructure` do módulo.
- Uma chamada síncrona via `Contracts` compartilha a transação do request. Uma reação entre módulos vai pelo outbox. Uma reação dentro do mesmo módulo é síncrona.

### Outbox e inbox
- `outbox_messages`: `id` (Guid v7), `type`, `payload`, `occurred_at`, `attempts`, `next_attempt_at`, `processed_at`, `error`, `correlation_id`.
- `inbox_messages`: `message_id` + `handler`, com chave única. Cada handler roda na sua própria transação e grava a inbox junto com o efeito. Assim, o efeito acontece no máximo uma vez por handler.
- O dispatcher é um `BackgroundService` que percorre os tenants ativos. Ele é acordado logo após o `SaveChanges` e, como reserva, faz polling a cada 5 s, com `FOR UPDATE SKIP LOCKED`. O backoff é exponencial. Depois de 10 tentativas, a mensagem vai para dead-letter, que fica visível no painel da plataforma.
- O push do SignalR para painéis acontece logo após o commit e não passa pelo outbox.

### Jobs (Hangfire)
- Storage no banco de catálogo, schema `hangfire`.
- Todo job herda de `TenantJob<TArgs>`, que exige o `tenantId`, abre um escopo de DI novo, valida que o tenant está ativo e falha se faltar o tenant. O teste de arquitetura proíbe job que não use essa base.
- Para cada tipo de rotina há **um job recorrente global**, que enfileira o trabalho para cada tenant ativo. Não existe job recorrente por tenant.
- Fuso `America/Sao_Paulo`. Filas `default`, `email` e `fiscal`, com poucos workers, porque a VPS é compartilhada.
- O dashboard do Hangfire existe só no host da plataforma (ADR 0005 D2).

### Migrations
- `TenantMigrator` como modo próprio da imagem (`--migrate`), executado pelo CI antes de `docker compose up -d`. A api não migra sozinha em homologação nem em produção. Em desenvolvimento, uma flag `Database:AutoMigrate=true` permite.
- Advisory lock por tenant. Versão de schema por tenant registrada no catálogo. Um tenant com migração falha ou atrasada responde 503 "em manutenção" e não bloqueia os outros.
- Migrations no padrão expand/contract, compatíveis com a versão anterior do código (N-1). Uma migration destrutiva continua exigindo ADR.

### Consultas de dashboard
- Dapper só pela conexão do `TenantDbContext` (`Database.GetDbConnection()`), sempre com parâmetros.
- Um módulo `Reporting` lê os schemas dos outros módulos. É a **única exceção à fronteira** e serve só para leitura.
- Começar com views comuns e índices. Materialized view só quando a medição justificar, com refresh agrupado de no máximo 1 por minuto por tenant.

## Consequências
- O teste de arquitetura da FND-01 implementa as regras de estrutura.
- A FND-07 implementa o outbox, a inbox e o `TenantJob<T>`, e a FND-03a implementa o migrator.

- Ver ADR 0019 (registro de eventos de gatilho, concorrência com `xmin`, idempotência, deduplicação de notificações e jobs, Contracts × Contracts).

## Nota 21/10/2026 (revisão pré-Fases 4 a 8, decisão D9 do PO): `Reporting` revogado
- O módulo `Reporting` da seção "Consultas de dashboard" **foi revogado pela ADR 0028** e nunca foi criado. Não existe exceção à fronteira: o painel é composto pelo módulo `Dashboard`, só de `Application` e `Endpoints`, a partir dos `*.Contracts` de cada módulo dono, com cache de 60 s.
- Continua valendo o resto da seção: Dapper só pela conexão do `TenantDbContext`, sempre com parâmetros, e materialized view só quando a medição justificar, agora dentro do módulo dono.
