# PNL-01b — Visões do painel da TV

| Campo | Valor |
|---|---|
| ID | PNL-01b |
| Título | As visões do painel de produção na TV para leitura a distância — quadro por etapa (cartão de 4 linhas: OS e projeto, operador pelo primeiro nome e inicial, tempo na etapa e horas, previsto ou atraso; até 3 cartões por coluna), fila com as próximas entradas e a previsão × prometido, e atrasos e riscos —, com rotação automática, filtro pela linha do token, cliente nunca exibido, sempre escuro, atualização em menos de 2 s depois do apontamento e prévia no backoffice (ADR 0030) |
| Tamanho | M (quebra da PNL-01) |
| Status | Done |
| Requisitos | PRD §5.14 (Painel de produção: OS por etapa, operador atual, tempo na etapa, previsto × real, atrasos destacados, próximos projetos da fila e previsão de entrega; rotação automática; modo escuro e fonte grande) · R-PRD-2 · R-PLN-4 · PRD §6 (painel atualiza em < 2 s após apontamento) |
| Dependências | PNL-01a (sessão, `KioskHub`, superfície `/tv`, kit TV base), PRD-02 (consulta do quadro, etapa atual), PRD-03a (apontamento, operador, início da etapa), PLN-01a (datas previstas), PLN-01b (previsão do projeto, risco e `ScheduleChanged`), PLN-02 (`IPlanningQueue`) |
| Bloqueia | — |
| Ordem de execução | 12 (Marco 4.4 "A fábrica na parede") |

## User story
Como **operador e supervisor**, quero **olhar a TV da fábrica e ver em que etapa está cada OS, quem está nela, há quanto tempo, o que está atrasado e o que vem a seguir**, para **saber sem perguntar o que precisa de atenção e ver na hora o efeito do meu apontamento**.

## Contexto e referências
- PRD §5.14 (Painel de produção); §6 (< 2 s); `06-pendencias-riscos` (risco "Adoção no chão de fábrica": "painel TV mostrando o efeito do apontamento").
- **ADR 0030** (D11): **leitura a distância** — mínimo de 32 px nos títulos, 28 px nas linhas e 48 px em contagens e relógio; cartão de 4 linhas sem o produto; até 3 cartões por coluna com "+{n} OS"; operador pelo **primeiro nome e inicial**; **cliente nunca aparece**, só o nome curto do projeto; prévia no backoffice (`/app/painel-tv/$id/previa`).
- Protótipo (TV): cabeçalho "Linha 1, elevadores — 9 OS em andamento, 2 atrasadas" com relógio grande; colunas por etapa com a contagem; cartão com o número da OS, o título, o projeto, "{apontadas} de {padrão} h" e a data prevista ou "ATRASADA" (borda e faixa vermelhas); rodapé "Próximos na fila" com o projeto e "início {data}". Cores do protótipo viram tokens do tema escuro (nenhuma cor literal, CLAUDE.md).
- PRD-02 (Fase 3): etapa atual = a de menor sequência não `Done`; consulta do quadro com nomes por página; push só com o id.
- PRD-03a (Fase 3, item 65 decidido): vários operadores podem apontar na mesma etapa ("Sair da etapa (só eu)" e "Concluir etapa para todos"); apontamento com `started_at`.
- **Revisão pré-Fases 4 a 8** (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`): D11, D18 (kit TV antes do Marco 4.4); tabela 3.1, pontos 48 a 50; §4.1 (item 65 da Fase 3 fechado); §4.3 (PNL-01a: 404 por linha em `/kiosk/board/orders/{id}`); §4.4 padrão 4.

## Critérios de aceite
**Quadro por etapa**
1. **Dado** uma sessão de quiosque com a linha "Linha 1" (PNL-01a), **quando** a visão "Quadro por etapa" aparece, **então** mostra uma coluna por etapa (as do quadro da PRD-02, só das OS cujos centros da etapa atual são da linha; "Todas" mostra todas), cada uma com a letra da etapa no `StageIndicator size="tv"` e a contagem (48 px), e o cabeçalho "{linha} — {n} OS em andamento, {m} atrasadas".
2. **Dado** um cartão, **quando** aparece, **então** tem **exatamente 4 linhas**, sem o produto: (1) número da OS e nome curto do projeto (o nome do projeto cortado em 28 caracteres); (2) o operador atual pelo **primeiro nome e inicial do sobrenome** ("João S."; com mais de um, "João S. +{n}"); (3) o **tempo na etapa** (desde o início real da etapa, em h:mm, contando no próprio navegador a cada minuto) e "{apontadas} de {padrão} h" com o medidor (acima do padrão em crítico); (4) "Previsto {data}" (fim previsto da etapa, PLN-01a) ou "ATRASADA" (função única de atraso da PLN-01b) com a faixa vermelha, ou "EM RISCO" com a faixa âmbar; títulos de coluna com no mínimo 32 px e linhas do cartão com no mínimo 28 px.
3. **Dado** uma coluna com mais de 3 OS, **quando** aparece, **então** mostra **até 3 cartões** (primeiro as atrasadas, depois em risco, depois pelo fim previsto) e a linha "+{n} OS" com o total que não coube.
4. **Dado** uma etapa pausada, **quando** o cartão aparece, **então** a linha 2 mostra "Pausada: {motivo}" (motivo da pausa, PRD-03a) no lugar do operador.

**Fila e previsões**
5. **Dado** a visão "Fila e previsões", **quando** aparece, **então** mostra os próximos projetos da fila (PLN-02) que têm OS na linha, até 6, com posição, número e nome curto do projeto, início previsto da primeira etapa na linha, **previsão de entrega** e **prometido**, e o chip de folga (no prazo, em risco, atrasado), com as mesmas medidas mínimas do CA 2.

**Atrasos e riscos**
6. **Dado** a visão "Atrasos e riscos", **quando** aparece, **então** lista as OS atrasadas e em risco da linha (até 6, do maior atraso para o menor), com a etapa atual, o operador (primeiro nome e inicial), quantos dias e a etapa que define a entrega; sem nenhuma, a visão é **pulada** na rotação.

**Rotação e atualização**
7. **Dado** as visões e o tempo de rotação configurados no token (PNL-01a CA 3), **quando** o painel roda, **então** alterna entre as visões no intervalo, com transição curta e um indicador discreto da visão atual ("1/3"); com uma visão só, não há rotação; a troca de configuração chega pelo `KioskConfigChanged` sem recarregar a página.
8. **Dado** um apontamento fechado ou uma etapa iniciada no PWA, **quando** a transação faz commit, **então** o cartão da OS na TV muda (coluna, operador, horas) em **menos de 2 s** (medição no teste de ponta a ponta: commit → `OrderBoardChanged` → GET do cartão → tela); uma nova previsão (PLN-01b) chega em até 10 s pelo `ScheduleChanged` e recarrega a visão de fila.
9. **Dado** 50 mudanças por minuto, **quando** chegam, **então** a TV agrupa as recargas por cartão (espera de 300 ms, como no Kanban) e não pisca a tela inteira.

**Prévia no backoffice**
10. **Dado** `kiosk.view` e `production.view`, **quando** abro "Ver prévia" de uma tela (`/app/painel-tv/$id/previa`), **então** vejo as visões exatamente como a TV mostraria com o escopo daquela tela (linha, visões, rotação), em 1920 × 1080 escalado à janela, sem sessão de quiosque, pela API do usuário (mesmo DTO e mesmo filtro de linha, com `RequireModule("kiosk")`), com a faixa "Prévia: esta é a tela {nome}" e os botões "Visão anterior", "Próxima visão" e "Pausar rotação".

**Leitura, desempenho e segurança**
11. **Dado** a TV a 1920 × 1080, **quando** o painel aparece, **então** cabem 5 colunas com 3 cartões cada, os números usam `tabular-nums`, o contraste é AA no tema escuro e o âmbar só aparece para risco e para o item ativo; a TV 1366 × 768 também funciona com as mesmas medidas mínimas (menos colunas por tela, com rolagem automática das colunas a cada rotação); teste visual nos dois tamanhos.
12. **Dado** 300 OS abertas e 20 TVs ligadas no mesmo tenant, **quando** o painel carrega, **então** a API de cada visão responde em menos de 500 ms e o push não degrada o Kanban dos usuários (teste de carga simples).
13. **[Segurança]** **Dado** as respostas do painel, **quando** a TV recebe, **então** o DTO tem só número da OS, número e nome curto do projeto, etapa, horas, datas, primeiro nome e inicial do operador e o motivo da pausa — **nunca** o cliente (nem pessoa jurídica), o produto, valores, custos, e-mail, telefone nem documento (teste de arquitetura e de contrato do DTO); a linha vem sempre do escopo do token (PNL-01a CA 10).
14. **[Segurança]** **Dado** `GET /api/v1/kiosk/board/orders/{id}`, **quando** a OS não existe, é de outro tenant ou **não é da linha do token**, **então** responde **404** (sem distinguir os casos), sem dado nenhum da OS.
15. **Dado** a sessão de quiosque revogada ou o módulo não liberado, **quando** a TV pede uma visão, **então** vale a PNL-01a (CA 14 e 15): a tela desliga em menos de 1 min.

## Regras de negócio e técnicas
- **Endpoints** `GET /api/v1/kiosk/board`, `/kiosk/board/orders/{id}`, `/kiosk/queue`, `/kiosk/delays` no Production, só com a policy `kiosk`; reaproveitam a consulta do quadro da PRD-02 com o filtro de linha do token; a prévia usa os mesmos serviços pelos endpoints de usuário `/api/v1/kiosk-screens/{id}/preview/*`.
- **Tempo na etapa** = agora − `actual_start` da etapa atual (se não iniciada, não aparece); o relógio da TV só anima, o servidor manda o início.
- **Operador** (ponto 48): primeiro nome e inicial do sobrenome, montados no servidor pelo `IEmployees`; o nome completo nunca sai.
- **Nome curto do projeto** (ponto 49): o nome do projeto (COM-04a) cortado em 28 caracteres; o cliente nunca aparece.
- **Visões** (ponto 50): as três, pulando "Atrasos e riscos" quando vazia.
- Sem gravação: o painel é só leitura.

## Permissões, eventos e tabelas
- **Permissões:** nenhuma de usuário na TV; policy `kiosk` (sessão de quiosque com o escopo); prévia com `kiosk.view` e `production.view`.
- **Eventos:** nenhum no outbox; SignalR `OrderBoardChanged` (PRD-02) e `ScheduleChanged` (PLN-01b).
- **Contratos:** `IProductionSchedule`, `IPlanningQueue`, `IProjects` (número e nome do projeto), `IEmployees` (nome do operador).
- **Tabelas:** nenhuma nova.

## Telas e estados
- **Padrões comuns das Fases 4 a 8:** 4 (kit TV: tema escuro, 32/28/48 px, até 4 linhas por cartão e 3 cartões por coluna, "Ao vivo / Reconectando… / Atualizado às", prévia no backoffice, `StageIndicator` com `size="tv"`).
- **Superfície `/tv`**: as três visões, o cabeçalho com linha, contagens e relógio (do kit base da PNL-01a), o indicador de visão e o estado da conexão.
- **Prévia** (`/app/painel-tv/$id/previa`): a mesma superfície dentro do backoffice, com a faixa de prévia e os controles do CA 10.
- **Componentes (D18, kit TV):** `TvCard` (4 linhas), `TvColumn` (até 3 cartões e "+{n} OS"), `TvQueueRow`, `TvViewIndicator`, sobre os tokens da PNL-01a.
- **Estados:** carregando ("Conectando ao painel…"); vazio ("Nenhuma OS em produção nesta linha."); sem previsão ("Previsão em cálculo"); offline (PNL-01a CA 18).
- **Propositivo:** o painel mostra o efeito do apontamento na hora (o operador vê o cartão andar); a prévia deixa o supervisor conferir a tela antes de ir até a TV.
- **Ajuda:** atualizar `Ajuda/tv.md` (o que cada visão mostra, cores, rotação, por que o cliente não aparece) e `Ajuda/painel-tv.md` (escolha das visões e prévia); `Ajuda/painel-tv-previa.md` (rota `/app/painel-tv/$id/previa`); a TV não tem botão de ajuda (layout dedicado), mas a rota continua coberta pelo teste.

## Plano de testes
- **Unidade (front, Vitest):** cartão de 4 linhas (operador, "+n", tempo na etapa, atrasada/risco, nome curto), coluna com 3 cartões e "+{n} OS", rotação e pulo da visão vazia (CA 2, 3, 4, 6, 7).
- **Integração (Testcontainers):** visões com o filtro de linha do token e o dataset do PLN-01a (CA 1, 5, 6); prévia com o escopo da tela e sem sessão de quiosque (CA 10); DTO sem cliente, produto nem dado proibido (CA 13); 404 para OS de outra linha e de outro tenant (CA 14); desempenho com 300 OS (CA 12).
- **E2E (Playwright):** TV aberta, apontamento no PWA em outra página e o cartão muda em menos de 2 s (CA 8); troca de configuração sem recarregar (CA 7); prévia no backoffice (CA 10); comparação visual a 1920 × 1080 e 1366 × 768 com as medidas mínimas (CA 2, 11).

## Fora do escopo
- Som, alertas sonoros e mensagens livres na TV.
- Indicadores de qualidade e de paradas na TV: DSH-03 (painel do supervisor, no backoffice).
- Visões por operador.

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fases 4 a 8, tabela 3.1, pontos 48–50):
1. Operador pelo primeiro nome e inicial do sobrenome (CA 2, 13).
2. Cliente nunca aparece; só o nome curto do projeto (D11; CA 2, 13).
3. As 3 visões, pulando "Atrasos e riscos" quando vazia (CA 6, 7).

Decisões da revisão aplicadas: D11 e ADR 0030 (leitura a distância 32/28/48 px, cartão de 4 linhas sem o produto, 3 cartões por coluna, prévia no backoffice; CA 2, 3, 10, 11); D18 (kit TV: `TvCard`, `TvColumn`, `TvQueueRow`, `TvViewIndicator`); §4.1 (item 65 da Fase 3: "+{n}" operadores); §4.3 (404 por linha, CA 14); §4.4 padrão 4.

**Pré-condição externa:** nenhuma (a TV é da Konebras, P-12 decidida).

## Definition of Done
- [x] Critérios de aceite atendidos.
- [x] Testes de unidade, integração (Testcontainers), desempenho e E2E (< 2 s, prévia e visual) passando.
- [x] Nenhuma migration.
- [x] Sem cor literal: tokens do tema escuro da TV; medidas mínimas conferidas no teste visual.
- [x] Ajudas atualizadas (inclusive a da prévia); teste de cobertura passando.
- [x] OpenAPI e cliente Orval regenerados.
- [x] Story marcada no backlog.

## Tarefas técnicas sugeridas
- [x] Endpoints `/api/v1/kiosk/*` no Production com a policy `kiosk` e o 404 por linha; DTOs do painel sem cliente nem produto.
- [x] Endpoints da prévia e a rota `/app/painel-tv/$id/previa`.
- [x] Componentes `TvCard`, `TvColumn`, `TvQueueRow`, `TvViewIndicator`; rotação e relógio; recarga agrupada; ajudas.

## Quality gates
- Agentes previstos: @dev, design (TV, legibilidade a distância e contraste), cybersecurity (DTO e escopo), @qa.
- Gate: CA 2, 3, 8, 11, 13 e 14 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fases 4 a 8).

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
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** cores do protótipo viram tokens do tema escuro (sem cor literal); DTO da TV sem valores nem dado pessoal além do primeiro nome do operador, com o nome na TV como ponto em aberto de LGPD.
- **Correções da revalidação (21/10/2026):** pontos 48 a 50 decididos (primeiro nome e inicial; cliente nunca, nem pessoa jurídica; três visões com o pulo de "Atrasos"); leitura a distância pela ADR 0030 (32/28/48 px, cartão de 4 linhas sem o produto, até 3 cartões por coluna com "+{n} OS"; CA 2, 3, 11), no lugar dos 24 px e 4 cartões; prévia no backoffice (CA 10); 404 por linha (§4.3, CA 14); kit TV (D18); `ScheduleChanged` vem da PLN-01b; CAs renumerados (15).
- **Pré-condições para `Ready`:** nenhuma de decisão de story.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada; quebra da PNL-01 (01a/01b) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (D11, D18, tabela 3.1 pontos 48–50, §4.1, §4.3, §4.4 padrão 4; ADR 0030); ordem mantida (Marco 4.4, ordem 12); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
| 26/10/2026 | 1.0 | Implementada (`Done`). **Backend:** contrato `IKioskPanel` no `Production.Contracts` (DTO de allowlist `KioskBoard`, `KioskColumn`, `KioskCard`, `KioskQueueView`, `KioskQueueRow`, `KioskDelays`, `KioskDelayRow`), `KioskPanelService` com as regras do quadro da PRD-02 (etapa atual, coluna pelo código, linha pelo centro da etapa atual, `ProductionOrderRisk`) e sem ler cliente nem produto (sem `IParties`/`IItems`); endpoints `GET /api/v1/kiosk/board`, `/board/orders/{id}`, `/queue`, `/delays` no Production com `RequireModule("kiosk")` no grupo e `RequireKiosk()`; prévia `GET /api/v1/kiosk-screens/{id}/preview` e `/preview/board`, `/preview/board/orders/{orderId}`, `/preview/queue`, `/preview/delays` no grupo do Identity com `kiosk.view` + `production.view` (mesmo `IKioskPanel`); `ScheduleChanged` ligado ao `KioskHub` (a porta desligada da PNL-01a saiu). Nenhuma migration, nenhuma permissão nova no catálogo. **Front:** `TvCard`, `TvColumn`, `TvQueueRow`, `TvDelayRow`, `TvViewIndicator` (`components/tv/panel.tsx`), `TvSurface`, rotação (`useRotation`), recarga agrupada por cartão em 300 ms (`useTvData`, `patchBoard`), `KioskPreviewPage` em `/app/painel-tv/$id/previa` (1920 × 1080 escalado, tokens de 1080 linhas, "Visão anterior", "Próxima visão", "Pausar rotação"), "Ver prévia" na lista e no próximo passo; ajudas `tv.md`, `painel-tv.md` e `painel-tv-previa.md`. **Decisões onde a story não detalhava (recomendação aceita, PO 24/10):** (1) visão fora das visões da tela responde o mesmo 404 da OS de outra linha (menor privilégio), na TV e na prévia; (2) "etapa que define a entrega" (CA 6) = a etapa da OS que termina por último no plano (PLN-01a), com o fim previsto; (3) "quantos dias" (CA 6): atrasada, dias desde o prometido; em risco, quanto a previsão passa do prometido; atrasadas antes das em risco; (4) nome curto: 27 caracteres + "…" quando passa de 28; (5) operadores distintos pelo primeiro nome e inicial, o primeiro a entrar na etapa e "+{n}"; (6) colunas só com OS da linha ("Sem roteiro" e "Pronta para concluir" só quando têm OS); (7) a TV também desliga quando uma visão responde 401/403 do quiosque, sem esperar a revalidação de 30 s; (8) a TV mostra o nome da tela ao lado do indicador da visão; (9) cartão com `leading-[1.15]` para caber 3 cartões a 768 linhas mantendo 28 px. **Desvios:** (a) E2E do CA 8: o hub do SignalR é simulado no Playwright (negociação e WebSocket) e o "apontamento no PWA" é a mudança no mock seguida do `OrderBoardChanged`; o caminho commit → `OrderBoardChanged` no `KioskHub` está coberto pela integração da PNL-01a (`KioskTests.Hub`); medição do push à tela < 2 s; (b) "teste visual" a 1920 × 1080 e 1366 × 768 por medidas (fontes ≥ 32/28/48 px, cartões dentro da tela, sem rolagem horizontal) e captura anexada ao relatório, sem baseline de pixels (depende da fonte e do SO); (c) carga do CA 12: 300 OS e 20 sessões de TV na integração (cada visão < 500 ms; 20 recargas simultâneas com vazão < 500 ms por TV); o efeito no Kanban dos usuários não é medido à parte; (d) o teste do PNL-01a "criar a tela... revogar" (`e2e/kiosk.spec.ts`) ganhou `test.slow()`: cinco cargas de página no servidor de desenvolvimento passam dos 30 s. | Dex (Dev) |
