# CLAUDE.md — BemAqui Empresas

Instruções permanentes para o Claude Code neste repositório. Leia antes de qualquer tarefa.

## O que é
SaaS multi-tenant de gestão empresarial e fabricação por processos, para pequenas indústrias sob encomenda.
Tenant piloto: **Konebras Elevadores** (fabrica elevadores e componentes; terceiriza a instalação, mas responde por ela ao cliente final), em **https://app.konebras.com.br** (substitui o Dolibarr, que vai para `adm.konebras.com.br` até o go-live; ver ADR 0004).
**Objetivo central do produto: visibilidade financeira.** Todo módulo existe para alimentar custo, margem e caixa com dado real.

Documentação de produto (fonte da verdade, nesta ordem):
1. `docs/01-PRD.md` — escopo, módulos e regras de negócio
2. `docs/02-arquitetura.md` — decisões técnicas, multi-tenant, segurança, integrações
3. `docs/03-modelo-dados.md` — entidades e relacionamentos
4. `docs/04-fluxos-estados.md` — máquinas de estado e eventos entre módulos
5. `docs/05-backlog.md` — épicos, user stories e critérios de aceite, por fase
6. `docs/06-pendencias-riscos.md` — o que ainda não está decidido (NÃO implemente item pendente sem decisão registrada)

Se uma tarefa conflitar com esses documentos, pare e pergunte. Decisões novas viram ADR em `docs/adr/NNNN-titulo.md`.

## Stack
- **Backend:** .NET 10 (LTS), ASP.NET Core, EF Core 10 + Npgsql, PostgreSQL 17
- **Frontend:** React 19 + Vite + TypeScript, shadcn/ui + Tailwind, TanStack Query, TanStack Router, React Hook Form + Zod, dnd-kit (Kanban), Recharts (gráficos), vite-plugin-pwa
- **Tempo real:** SignalR · **Jobs:** Hangfire (storage PostgreSQL) · **PDF:** QuestPDF · **Logs:** Serilog
- **Validação:** FluentValidation · **Testes:** xUnit v3 + FluentAssertions **7.2.2 (fixo; 8+ é comercial)** + NetArchTest + Testcontainers (PostgreSQL real), Vitest, Playwright (E2E) — ADR 0009
- **Infra:** Docker Compose na VPS Hostinger compartilhada (2.25.82.127, acesso em `D:\GIT\Konebras\CLAUDE.md`), atrás do `stack-nginx` + certbot já existentes. Sem Caddy (ADR 0004)
- **Não usar:** MediatR/AutoMapper (licença comercial). Usar handlers simples injetados e mapeamento manual.

## Arquitetura (resumo — detalhes em docs/02)
Monolito modular. Um projeto por módulo, comunicação entre módulos só por contratos públicos (`*.Contracts`) e eventos de domínio via outbox. Nenhum módulo acessa tabela de outro.

```
src/
  Host/                    # ASP.NET Core: composição, middlewares, auth, SignalR hubs
  BuildingBlocks/          # Result, erros, paginação, outbox, auditoria, tenant context, numeração
  Modules/
    Platform/              # catálogo de tenants, planos, provisionamento (banco de catálogo)
    Identity/              # usuários, perfis, permissões, 2FA, tokens de quiosque
    Parties/               # terceiros (clientes, fornecedores, instaladores)
    HR/                    # funcionários, cargos, ponto, folha
    Commercial/            # orçamentos, contratos, projetos
    Engineering/           # BOM, roteiros (templates de etapas)
    Production/            # OS, Kanban, apontamentos, qualidade, capacidade
    Inventory/             # itens, locais, saldos, reservas, movimentos, lotes/séries
    Purchasing/            # solicitação, cotação, pedido, recebimento
    Finance/               # plano de contas, CR/CP, ordens de pagamento, bancos, fluxo de caixa
    Fiscal/                # adaptador EmissorFiscal, NF-e saída/entrada, NFS-e
    Assets/                # patrimônio, QR code, manutenção
    Portal/                # portal do cliente (fase posterior)
  Web/                     # app React (backoffice + PWA chão de fábrica + painel TV + portal)
tests/
```
Cada módulo: `Domain` (entidades, regras), `Application` (casos de uso, validação), `Infrastructure` (EF, integrações), `Endpoints` (Minimal API agrupada por recurso), `Contracts`.

## Convenções obrigatórias
- **Multi-tenant:** banco PostgreSQL separado por tenant + banco de catálogo. Tenant resolvido pelo host da requisição (`catalog.tenant_domains`, aceita domínio próprio do cliente). Nunca crie query sem passar pelo `TenantDbContext` resolvido no request. Jobs Hangfire SEMPRE recebem `tenantId`.
- **Dinheiro:** `decimal(18,2)`; quantidades `decimal(18,4)`; custo unitário `decimal(18,6)`. Nunca `double`.
- **Datas:** gravar UTC (`timestamptz`), exibir em `America/Sao_Paulo`. Datas de negócio sem hora usam `date`.
- **Ids:** `Guid` v7. Números legíveis (ORC-2026-0001) via serviço de numeração por tenant e tipo.
- **Exclusão:** soft delete em cadastros; documentos com movimento (estoque, financeiro, fiscal) nunca são apagados, só cancelados/estornados.
- **Auditoria:** interceptor EF grava quem/quando/o quê (antes/depois em JSON) para toda entidade marcada `IAuditable`.
- **Controle de acesso (3 camadas, todas obrigatórias):**
  1. **Módulo liberado na versão do tenant** (`tenant_modules` no catálogo). Módulo não liberado: API responde 403 com `code: "module_not_released"`; o front mostra a tela padrão com a mensagem exata **"Funcionalidade não liberada nessa versão do sistema."** O botão do módulo aparece no menu com cadeado.
  2. **Módulo habilitado no grupo** (`group_modules`). Usuário sem nenhum grupo que habilite o módulo não vê o botão no menu e recebe 403 `code: "forbidden"`.
  3. **Ação marcada no grupo** (`group_permissions`, código `modulo.acao`, ex.: `finance.approve`). Ações padrão: `view`, `create`, `edit`, `approve`, `delete`; cada módulo declara quais se aplicam e, quando houver necessidade real (LGPD, segregação de função), ações específicas em snake_case (ex.: `hr.view_sensitive`) — ADR 0003. Nunca três níveis (`modulo.entidade.acao`).
  - Usuário pertence a **um ou mais grupos**; a permissão efetiva é a **união** dos grupos, sempre limitada pelos módulos liberados no tenant. Não existe permissão direta no usuário.
  - Grupo **Proprietário** é do sistema: tem tudo, não pode ser editado nem ficar sem membros.
  - Permissões efetivas ficam em cache por usuário e são invalidadas quando o grupo ou o vínculo muda.
  - Novos módulos e ações entram no catálogo por seed (`ModuleCatalog`); nunca escreva código de permissão "solto" fora do catálogo.
  - Sem rota anônima, exceto login, convite, recuperação de senha, quiosque (token de tela), webhooks (assinatura validada), `GET /api/v1/public/tenant-context` (nome/logo para o login) e `/health/live|ready` — ADR 0009.
  - Delegação: ninguém concede o que não tem; JWT só com identidade (`sub`, `tid`, `aud`, `sstamp`), permissões vêm do cache versionado — ADR 0008.
- **Erros:** padrão Result + ProblemDetails (RFC 9457). Mensagens de validação em PT-BR.
- **Idioma:** código e nomes de tabela em inglês; textos de UI, mensagens e docs em português.
- **API:** REST, versionada (`/api/v1`), OpenAPI gerado; cliente TS gerado com Orval — não escrever chamadas fetch à mão.
- **Migrations:** EF Core, aplicadas pelo `TenantMigrator` em todos os bancos de tenant. Migration nunca destrutiva sem ADR.

## Interface (referência visual: `docs/prototipo/prototipo-erp-konebras.html`)
O protótipo aprovado é a referência de layout, hierarquia e comportamento. Abra no navegador antes de criar qualquer tela.
- **Navegação:** dock fixo na parte inferior (ícone + rótulo curto), agrupado por área: Visão geral, Comercial, Fábrica, Financeiro, Pessoas, Telas dedicadas, Sistema. Item ativo marcado por traço âmbar no topo. No celular, o dock encosta na borda inferior e rola na horizontal. Barra superior com marca do tenant, trilha de navegação, busca, seletor de tema, notificações e usuário.
- **Tema:** claro, escuro e automático (segue o sistema), escolhido pelo usuário e salvo no perfil. Toda cor vem de tokens CSS/Tailwind; nenhum componente usa cor literal. O Painel da TV é sempre escuro.
- **Identidade:** tons de aço frios, trilho/dock escuro, acento cobalto, âmbar só para destaque de estado e do item ativo. Tipografia Archivo (larguras variáveis: expandida em títulos e números, normal no texto). Números sempre com `tabular-nums`.
- **Indicador de etapa** no estilo display de andar (letra da etapa em âmbar sobre fundo escuro + segmentos) é o componente padrão de etapa de OS/projeto em todas as telas.
- **Estado visível na forma:** chips de status (ok, alerta, crítico, informativo), faixa lateral vermelha em cartão atrasado, âmbar em risco. Cor semântica separada da cor de acento.
- **Toda tela** tem estados de carregando, vazio (com ação para começar) e erro (o que houve e como resolver). Ações com verbo claro ("Aprovar", "Salvar grupo") e confirmação com o mesmo verbo ("Aprovada").
- **Telas dedicadas:** PWA do operador (botões ≥ 56px, checklist, cronômetro) e Painel da TV (fonte grande, sem login) têm layout próprio, sem o dock.
- **Ajuda contextual (padrão NSHome, PRD §5.15a):** toda tela, e cada aba, tem ajuda em `Ajuda/<chave>.md` (cabeçalho `titulo`, `rota`, `aba`, `padrao`, `modulo`). O botão "Ajuda" da topbar acha a ajuda pela rota e pela aba, e abre sozinho na primeira visita. O "Pergunte ao BemAqui" responde só com esses textos e cita a fonte. Termo ou campo que confunde ganha `<Dica texto="..." />`. **Mudou a tela, mude a ajuda no mesmo commit.** O teste de cobertura falha se uma rota ficar sem ajuda.
- **Propositivo:** o sistema antecipa o próximo passo e evita ação desnecessária. Formulário vem preenchido com o mais provável, toda conclusão oferece o próximo passo, nada é digitado duas vezes e alerta leva direto à ação. O sistema sugere, mas não decide: efeito financeiro, fiscal ou de estoque sempre exige confirmação.
- **Módulo não liberado:** tela padrão `ModuleNotReleased` com o nome do módulo, a mensagem "Funcionalidade não liberada nessa versão do sistema." e uma linha explicando o que o módulo fará.

## Como trabalhar
1. Pegue a próxima story do `docs/05-backlog.md` na fase atual. Uma story por branch/PR.
2. Antes de codar: releia as regras do módulo no PRD e as entidades no modelo de dados.
3. Escreva testes de regra de negócio (unidade) e de integração com Testcontainers para todo caso de uso que movimenta estoque, financeiro ou fiscal.
   - **Não repita teste de padrão.** As três camadas de acesso (por endpoint), as rotas anônimas, a cobertura de ajuda e a arquitetura já são verificadas por varredura (`EndpointAuthorizationTests`, teste de cobertura da ajuda, `Architecture.Tests`). Story nova não escreve `Tres_camadas_*`; teste de acesso só para regra própria do módulo (ação específica, seed patch, IDOR, dado mascarado).
   - **Teste de carga** (milhares de linhas, tempo de resposta) leva `[Trait(LoadTest.Trait, LoadTest.Value)]`. O `scripts/test-docker.sh` pula esses testes (`BQ_CARGA=1` inclui); o CI roda todos.
   - No dia a dia, rode só as classes do módulo tocado (`--filter-class "*Fabrication*"`). A suíte inteira fica com o CI.
4. Critério de pronto: critérios de aceite atendidos, testes passando, migration criada, permissão registrada no seed, tela com estados de vazio/carregando/erro, ajuda da tela escrita ou atualizada, padrões e próximo passo propositivos avaliados, auditoria ligada.
5. Marque a story como concluída no backlog e registre decisões em ADR.

## Comandos
```bash
docker compose up -d postgres                     # banco local (PostgreSQL 17)
dotnet build BemAquiEmpresas.slnx
dotnet test --solution BemAquiEmpresas.slnx       # runner Microsoft.Testing.Platform (global.json)
sh scripts/test-docker.sh                         # mesma suíte num container Linux (igual ao CI)
dotnet run --project src/Host/BemAqui.Host        # API em http://localhost:5080 (/health)
cd src/Web && pnpm i && pnpm dev                  # front em http://localhost:5173
pnpm lint && pnpm test && pnpm build              # dentro de src/Web
BQ_UPDATE_OPENAPI=1 sh scripts/test-docker.sh --filter-class "*OpenApiDocumentTests"  # atualiza src/Web/openapi.json
pnpm gen:api                                      # regenera o cliente TS (src/api/generated) a partir do openapi.json
```
- **Windows com Smart App Control ativo:** ele bloqueia DLLs recém-compiladas quando os testes de arquitetura as carregam (erro `0x800711C7`). Nesse caso, rode a suíte com `sh scripts/test-docker.sh`. O Host roda normalmente.
- **RTK:** o hook reescreve `pnpm lint` como eslint. Use `rtk proxy pnpm lint` para rodar o script real (oxlint + regra de cor literal).
- **pnpm:** `node-linker=hoisted` (`src/Web/.npmrc`), porque o modo com symlinks quebra no Windows.
- **Projeto novo** em `src/Modules/<M>/` segue as 5 camadas. A regra de referências entre módulos está nos testes de arquitetura (ADR 0006).

## Integrações externas
- **EmissorFiscal** (projeto existente em `D:\GIT\EmissorFiscal`): analisar ANTES de implementar o módulo Fiscal (story FIS-01). Integrar via adaptador `IFiscalEmitter`; não copiar código sem ADR.
- **Banco Inter:** API de Cobrança (boleto com Pix / "bolepix"), webhooks e extrato. Certificado mTLS por tenant, guardado criptografado.
- **E-mail:** SMTP/AWS SES atrás de `IEmailSender`, envio por fila (Hangfire).
