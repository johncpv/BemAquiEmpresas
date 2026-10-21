# PNL-01b — Visões do painel da TV

| Campo | Valor |
|---|---|
| ID | PNL-01b |
| Título | As visões do painel de produção na TV — quadro por etapa (OS, operador atual, tempo na etapa, horas reais × padrão, previsto, atrasos), fila com as próximas entradas e a previsão de entrega × prometido, e atrasos e riscos —, com rotação automática, filtro pela linha do token, fonte grande, sempre escuro e atualização em menos de 2 s depois do apontamento |
| Tamanho | M (quebra da PNL-01) |
| Status | Draft |
| Requisitos | PRD §5.14 (Painel de produção: OS por etapa, operador atual, tempo na etapa, previsto × real, atrasos destacados, próximos projetos da fila e previsão de entrega; rotação automática; modo escuro e fonte grande) · R-PRD-2 · R-PLN-4 · PRD §6 (painel atualiza em < 2 s após apontamento) |
| Dependências | PNL-01a (sessão, `KioskHub`, superfície `/tv`), PRD-02 (consulta do quadro, etapa atual), PRD-03a (apontamento, operador, início da etapa), PLN-01a (datas previstas), PLN-01b (previsão do projeto e risco), PLN-02 (`IPlanningQueue`) |
| Bloqueia | — |
| Ordem de execução | 12 (Marco 4.4 "A fábrica na parede") |

## User story
Como **operador e supervisor**, quero **olhar a TV da fábrica e ver em que etapa está cada OS, quem está nela, há quanto tempo, o que está atrasado e o que vem a seguir**, para **saber sem perguntar o que precisa de atenção e ver na hora o efeito do meu apontamento**.

## Contexto e referências
- PRD §5.14 (Painel de produção); §6 (< 2 s); `06-pendencias-riscos` (risco "Adoção no chão de fábrica": "painel TV mostrando o efeito do apontamento").
- Protótipo (TV): cabeçalho "Linha 1, elevadores — 9 OS em andamento, 2 atrasadas" com relógio grande; colunas por etapa com a contagem; cartão com o número da OS, o título, o projeto, "{apontadas} de {padrão} h" e a data prevista ou "ATRASADA" (borda e faixa vermelhas); rodapé "Próximos na fila" com o projeto e "início {data}". Cores do protótipo: fundo `#131C24`, borda `#22303B`, atraso `#FF7B70` — no sistema, viram tokens do tema escuro (nenhuma cor literal, CLAUDE.md).
- PRD-02 (Fase 3, rascunho): etapa atual = a de menor sequência não `Done`; consulta do quadro com nomes por página; push só com o id.
- PRD-03a (Fase 3, rascunho): vários operadores podem apontar na mesma etapa (ponto 3 de lá); apontamento com `started_at`.

## Critérios de aceite
**Quadro por etapa**
1. **Dado** uma sessão de quiosque com a linha "Linha 1" (PNL-01a), **quando** a visão "Quadro por etapa" aparece, **então** mostra uma coluna por etapa (as do quadro da PRD-02, só das OS cujos centros da etapa atual são da linha; "Todas" mostra todas), cada uma com a letra da etapa no estilo display de andar e a contagem, e o cabeçalho "{linha} — {n} OS em andamento, {m} atrasadas".
2. **Dado** um cartão, **quando** aparece, **então** mostra: número da OS, produto, número do projeto e o nome curto do projeto; o operador atual (primeiro nome e inicial do sobrenome; com mais de um, "{nome} +{n}"; proposta, Pontos em aberto 1); o **tempo na etapa** (desde o início real da etapa, em h:mm, contando no próprio navegador a cada minuto); "{apontadas} de {padrão} h" com o medidor (acima do padrão em crítico); e "Previsto {data}" (fim previsto da etapa, PLN-01a) ou "ATRASADA" (função única de atraso da PLN-01b) com a faixa vermelha, e "EM RISCO" com a faixa âmbar.
3. **Dado** uma etapa pausada, **quando** o cartão aparece, **então** mostra "Pausada: {motivo}" (motivo da pausa, PRD-03a) no lugar do operador.

**Fila e previsões**
4. **Dado** a visão "Fila e previsões", **quando** aparece, **então** mostra os próximos projetos da fila (PLN-02) que têm OS na linha, até 8, com posição, número e nome curto do projeto, início previsto da primeira etapa na linha, **previsão de entrega** e **prometido**, e o chip de folga (no prazo, em risco, atrasado).

**Atrasos e riscos**
5. **Dado** a visão "Atrasos e riscos", **quando** aparece, **então** lista as OS atrasadas e em risco da linha (até 10, do maior atraso para o menor), com a etapa atual, o operador, quantos dias e a etapa que define a entrega; sem nenhuma, a visão é pulada na rotação.

**Rotação e atualização**
6. **Dado** as visões e o tempo de rotação configurados no token (PNL-01a CA 2), **quando** o painel roda, **então** alterna entre as visões no intervalo, com transição curta e um indicador discreto da visão atual ("1/3"); com uma visão só, não há rotação; a troca de configuração chega pelo `KioskConfigChanged` sem recarregar a página.
7. **Dado** um apontamento fechado ou uma etapa iniciada no PWA, **quando** a transação faz commit, **então** o cartão da OS na TV muda (coluna, operador, horas) em **menos de 2 s** (medição no teste de ponta a ponta: commit → `OrderBoardChanged` → GET do cartão → tela); uma nova previsão (PLN-01b) chega em até 10 s pelo `ScheduleChanged` (mensagem nova no `KioskHub`, só sinal) e recarrega a visão de fila.
8. **Dado** 50 mudanças por minuto, **quando** chegam, **então** a TV agrupa as recargas por cartão (espera de 300 ms, como no Kanban) e não pisca a tela inteira.

**Leitura, desempenho e segurança**
9. **Dado** a TV a 1920 × 1080, **quando** o painel aparece, **então** o texto principal do cartão tem no mínimo 24 px, os números com `tabular-nums`, contraste AA no tema escuro, âmbar só para risco e para o item ativo, e cabem 5 colunas com 4 cartões visíveis por coluna (o excedente mostra "+{n} OS" na coluna); a TV 1366 × 768 também funciona (teste visual nos dois tamanhos).
10. **Dado** 300 OS abertas e 20 TVs ligadas no mesmo tenant, **quando** o painel carrega, **então** a API de cada visão responde em menos de 500 ms e o push não degrada o Kanban dos usuários (teste de carga simples).
11. **[Segurança]** **Dado** as respostas do painel, **quando** a TV recebe, **então** o DTO tem só número da OS, produto, projeto (número e nome curto), etapa, horas, datas, primeiro nome e inicial do operador e o motivo da pausa — **sem** valores, custos, cliente pessoa física, e-mail, telefone nem documento (teste de arquitetura e de contrato do DTO); a linha vem sempre do escopo do token (PNL-01a CA 8).
12. **Dado** a sessão de quiosque revogada ou o módulo não liberado, **quando** a TV pede uma visão, **então** vale o PNL-01a (CA 11 e 12): a tela desliga em menos de 1 min.

## Regras de negócio e técnicas
- **Endpoints** `GET /api/v1/kiosk/board`, `/kiosk/board/orders/{id}`, `/kiosk/queue`, `/kiosk/delays` no Production, só com a policy `kiosk`; reaproveitam a consulta do quadro da PRD-02 com o filtro de linha do token.
- **Tempo na etapa** = agora − `actual_start` da etapa atual (se não iniciada, não aparece); o relógio da TV só anima, o servidor manda o início.
- **Nome curto do projeto**: o nome do projeto (COM-04a) cortado em 40 caracteres; o cliente só aparece se for pessoa jurídica (proposta, Pontos em aberto 2).
- Sem gravação: o painel é só leitura.

## Permissões, eventos e tabelas
- **Permissões:** nenhuma de usuário; policy `kiosk` (sessão de quiosque com o escopo).
- **Eventos:** nenhum no outbox; SignalR `OrderBoardChanged` (PRD-02) e `ScheduleChanged` (novo, enviado pelo `ScheduleService` depois de cada execução que mudou datas).
- **Contratos:** `IProductionSchedule`, `IPlanningQueue`, `IProjects`, `IParties` (tipo PF/PJ), `IEmployees` (nome do operador).
- **Tabelas:** nenhuma nova.

## Telas e estados
- **Superfície `/tv`**: as três visões, o cabeçalho com linha, contagens e relógio, o indicador de visão e o estado da conexão.
- **Estados:** carregando ("Conectando ao painel…"); vazio ("Nenhuma OS em produção nesta linha."); sem previsão ("Previsão em cálculo"); offline (PNL-01a CA 14).
- **Propositivo:** o painel mostra o efeito do apontamento na hora (o operador vê o cartão andar).
- **Ajuda:** atualizar `Ajuda/tv.md` (o que cada visão mostra, cores, rotação) e `Ajuda/painel-tv.md` (escolha das visões); a TV não tem botão de ajuda (layout dedicado), mas a rota continua coberta pelo teste.

## Plano de testes
- **Unidade (front, Vitest):** cartão (operador, tempo na etapa, atrasada/risco), rotação e pulo da visão vazia (CA 2, 3, 5, 6).
- **Integração (Testcontainers):** visões com o filtro de linha do token e o dataset do PLN-01a (CA 1, 4, 5); DTO sem dado proibido (CA 11); desempenho com 300 OS (CA 10).
- **E2E (Playwright):** TV aberta, apontamento no PWA em outra página e o cartão muda em menos de 2 s (CA 7); troca de configuração sem recarregar (CA 6); comparação visual a 1920 × 1080 e 1366 × 768 (CA 9).

## Fora do escopo
- Som, alertas sonoros e mensagens livres na TV.
- Indicadores de qualidade e de paradas na TV: DSH-03 (painel do supervisor, no backoffice).
- Visões por operador.

## Pontos em aberto
1. **Nome do operador na TV** (PO/LGPD): proposta: primeiro nome e inicial do sobrenome. Alternativa: só as iniciais (como o avatar do protótipo).
2. **Cliente na TV** (PO/LGPD): proposta: o nome do projeto e, se o cliente for pessoa jurídica, o nome fantasia; cliente pessoa física nunca aparece.
3. **Visões do MVP** (PO): proposta: as três desta story. Alternativa: só o quadro e a fila (como o protótipo).

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers), desempenho e E2E (< 2 s e visual) passando.
- [ ] Nenhuma migration.
- [ ] Sem cor literal: tokens do tema escuro da TV.
- [ ] Ajudas atualizadas; teste de cobertura passando.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] Story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Endpoints `/api/v1/kiosk/*` no Production com a policy `kiosk`; DTOs do painel.
- [ ] `ScheduleChanged` no `KioskHub`; recarga agrupada no cliente.
- [ ] Componentes das três visões, rotação e relógio; ajudas.

## Quality gates
- Agentes previstos: @dev, design (TV, legibilidade e contraste), cybersecurity (DTO e escopo), @qa.
- Gate: CA 2, 7, 9 e 11 obrigatórios.

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

- **Correções feitas na validação:** cores do protótipo viram tokens do tema escuro (sem cor literal); DTO da TV sem valores nem dado pessoal além do primeiro nome do operador (CA 11), com o nome na TV como ponto em aberto de LGPD.
- **Pré-condições para `Ready`:** pontos em aberto 1 e 2.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada; quebra da PNL-01 (01a/01b) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
