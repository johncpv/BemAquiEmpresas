# FND-09b — Shell do backoffice e navegação

| Campo | Valor |
|---|---|
| ID | FND-09b |
| Título | Dock com 7 áreas, menu por área (popover/sheet), abas de sub-tela na topbar, paleta Ctrl K, topbar e acessibilidade de navegação |
| Tamanho | M |
| Status | Draft |
| Requisitos | PRD §6 Usabilidade (responsivo) · base de R-SEC-11, R-SEC-12 (menu) e R-AJD-2 (slot do botão Ajuda) · R-COM-2 (sino) |
| Dependências | FND-09a; sino consome a API da FND-07; menu real liga em `/me/permissions` na FND-06d (até lá, provider de menu de exemplo) |
| Bloqueia | FND-06d, FND-14, todas as telas do backoffice |
| Ordem de execução | Paralela, depois da FND-09a |

## User story
Como **usuário do backoffice**, quero **navegar por 7 áreas fixas no dock, achar qualquer módulo pelo menu da área ou pelo Ctrl K e saber sempre onde estou**, para **chegar à tarefa em poucos toques, no computador e no celular**.

## Contexto e referências
- **ADR 0005 D8:** o dock mostra as **7 áreas** (Visão geral, Comercial, Fábrica, Financeiro, Pessoas, Telas dedicadas, Sistema). Cada área abre um menu (popover no desktop, sheet no celular) com os módulos. Sub-telas ficam em abas na topbar. Ctrl K abre a paleta de comandos. A identidade visual do protótipo é mantida.
- `CLAUDE.md` → Interface: item ativo com traço âmbar no topo (`--signal`); no celular o dock encosta na borda inferior e rola na horizontal; topbar com marca do tenant, trilha, busca, seletor de tema, notificações e usuário; telas dedicadas sem dock.
- `docs/02-arquitetura.md` §4 (superfícies `/app`, `/fab`, `/tv`, `/portal` com layouts próprios; TanStack Router).
- Protótipo: `.rail`/`.dock`, `.topbar`, `.crumb`, `.search` com `Ctrl K`, `#themeSeg`.
- Revisão §2.4: topbar a 400px (tema no menu do usuário, busca vira ícone); acessibilidade (`<nav>`, rótulos de "não liberado" e de contadores, foco e título da página na troca de rota, link "pular para o conteúdo").

## Critérios de aceite
**Dock e menu**
1. **Dado** o backoffice no desktop, **quando** a página carrega, **então** o dock fixo na parte inferior mostra as 7 áreas na ordem do CLAUDE.md, com ícone e rótulo curto, dentro de um `<nav aria-label="Navegação principal">`.
2. **Dado** uma área, **quando** clico nela no desktop, **então** abre um popover com os módulos da área (ícone, nome e, se houver, contador); **quando** toco nela no celular, **então** abre um sheet a partir da borda inferior.
3. **Dado** um módulo aberto, **quando** olho o dock, **então** a área dele tem o traço `--signal` no topo e `aria-current="page"`.
4. **Dado** a largura de 400px, **quando** a página carrega, **então** o dock encosta na borda inferior, ocupa a largura toda, rola na horizontal sem barra visível e respeita `safe-area-inset`.
5. **Dado** o menu aberto, **quando** uso o teclado, **então** as setas movem entre os módulos, Enter abre, Esc fecha e o foco volta para a área.

**Topbar e abas**
6. **Dado** a topbar, **quando** a página carrega, **então** mostra marca do tenant (logo e nome), trilha de navegação, busca com o atalho `Ctrl K`, botão "Ajuda" (slot para a FND-14), sino de notificações com contador, seletor de tema e menu do usuário.
7. **Dado** a largura de 400px, **quando** a página carrega, **então** a busca vira um ícone que abre a paleta e o seletor de tema vai para o menu do usuário; nada transborda na horizontal.
8. **Dado** um módulo com sub-telas (ex.: "Usuários e acessos" → Grupos e Usuários), **quando** o abro, **então** as sub-telas aparecem como abas na topbar, cada uma com rota própria (URL compartilhável).

**Paleta de comandos**
9. **Dado** qualquer tela do backoffice, **quando** pressiono `Ctrl K` (ou `⌘ K` no macOS), **então** abre a paleta com busca por nome de módulo e de sub-tela; Enter navega; Esc fecha.

**Notificações**
10. **Dado** notificações não lidas (FND-07), **quando** abro o sino, **então** vejo a lista com título e horário (America/Sao_Paulo); clicar leva ao link da notificação e a marca como lida; o contador tem rótulo "{N} notificações não lidas".

**Acessibilidade e rotas**
11. **Dado** a troca de rota, **quando** a tela nova carrega, **então** o `document.title` vira "{Tela} · {Tenant} · BemAqui Empresas" e o foco vai para o título principal da tela.
12. **Dado** a navegação por teclado, **quando** pressiono Tab na carga, **então** o primeiro item é o link "Pular para o conteúdo".
13. **Dado** as superfícies `/fab` e `/tv`, **quando** abertas, **então** usam layout próprio sem dock e sem topbar do backoffice (só o esqueleto nesta story).
14. **Dado** o backoffice, **quando** comparo com o protótipo nos dois temas e a 400px, **então** dock, topbar e menus batem com a identidade aprovada, com a mudança de navegação da D8.

## Regras de negócio e técnicas
- TanStack Router com rotas tipadas; layouts por superfície (`/app`, `/fab`, `/tv`; `/portal` reservado).
- O menu vem de um provider (`useNavigation`) que, nesta story, usa dados de exemplo tipados com o mesmo formato de `/me/permissions`; a FND-06d troca pela API real.
- Popover e Sheet do shadcn/Radix; paleta com `cmdk` (componente Command do shadcn).
- Sem cor literal; tokens da FND-09a.
- Sino: consulta periódica ou SignalR conforme decisão da FND-07.

## Permissões, eventos e tabelas
- Sem tabela nova. Consome `/api/v1/notifications` (FND-07) e, na FND-06d, `/me/permissions`.

## Telas e estados
- **Shell:** dock, topbar, área de conteúdo com `<main id="conteudo">`.
- **Estados do menu:** carregando (skeleton das 7 áreas); erro (cabe na FND-06d com a API real).
- **Sino:** carregando, vazio ("Nada novo por aqui"), erro ("Não foi possível carregar as notificações" + "Tentar de novo").
- **Paleta:** vazio de busca ("Nenhum resultado para '{termo}'").
- **Menu do usuário:** nome, "Meu perfil", "Verificação em duas etapas" (FND-05), tema (a 400px), "Sair".
- **Ajuda:** `Ajuda/inicio.md` para a tela inicial do backoffice (a ajuda das telas de cada módulo fica nas stories delas).
- **Propositivo:** a paleta mostra primeiro os módulos usados por último.

## Plano de testes
- **Unidade (Vitest):** montagem do dock a partir do provider; atalho Ctrl K; rótulos acessíveis.
- **E2E (Playwright):** abrir área por clique (desktop) e por toque (viewport móvel) (CA 2); 400px sem rolagem horizontal da página (CA 4, 7); teclado no menu (CA 5); Ctrl K (CA 9); título e foco na troca de rota (CA 11); skip link (CA 12); screenshots nos dois temas (CA 14).
- **Acessibilidade:** axe-core no Playwright sem violações sérias no shell.

## Fora do escopo
- Regras de permissão no menu e a tela de módulo não liberado (FND-06d).
- Conteúdo do painel de ajuda (FND-14).
- Busca global de registros (projeto, cliente, OS): a busca da topbar, nesta fase, só navega por módulos e telas.
- Layouts completos do PWA do operador e da TV (Fases 3 e 4).

## Pontos em aberto
- **Tela inicial do backoffice na Fase 0:** sem módulos de negócio, o que aparece em `/app`? Proposta: boas-vindas com os próximos passos do proprietário (convidar usuários, revisar grupos, ativar 2FA).
- **Busca da topbar:** o protótipo diz "Buscar projeto, cliente, OS..."; a busca de registros depende dos módulos. Confirmar que, na Fase 0, a paleta só navega.
- **Ícones:** biblioteca (Lucide, padrão do shadcn) ou os SVG do protótipo.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, E2E e acessibilidade passando.
- [ ] Migration: não se aplica.
- [ ] Permissão registrada no seed: não se aplica.
- [ ] Telas com estados de carregando, vazio e erro.
- [ ] Ajuda da tela inicial escrita.
- [ ] Padrões e próximo passo propositivos avaliados.
- [ ] Auditoria: não se aplica.
- [ ] Story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Rotas e layouts por superfície no TanStack Router.
- [ ] Componente Dock com áreas, Popover (desktop) e Sheet (celular).
- [ ] Topbar com marca, trilha, busca, Ajuda (slot), sino, tema e usuário; versão de 400px.
- [ ] Abas de sub-tela ligadas a rotas.
- [ ] Paleta de comandos (Ctrl K / ⌘ K).
- [ ] Sino com a API de notificações.
- [ ] Título e foco na troca de rota; skip link.
- [ ] Testes E2E e axe-core.

## Quality gates
- Agentes previstos: @ux-design-expert, design-squad (acessibilidade), @dev, @qa.
- Gate: E2E a 400px e axe-core sem violações sérias.

## Validação do PO
Checklist do @po (Pax), 16/10/2026.

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo (cabeçalho, user story, contexto, CAs, regras, permissões/tabelas, telas, testes, fora do escopo, pontos em aberto, DoD, tarefas) | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança da revisão §2.3 que se aplicam | N/A |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK |
| 7 | Plano de testes cobre os CAs (unidade, integração com Testcontainers, arquitetura, E2E quando cabe) | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas como proposta) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD do CLAUDE.md ajustado; pontos em aberto registrados sem decidir | OK |

- **Correções feitas na validação:** —
- **Pré-condições para `Ready`:** Conteúdo da tela inicial do backoffice na Fase 0.
- **Prontidão:** 9/10. **Veredito:** GO condicional (permanece `Draft` até as pré-condições serem decididas).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 16/10/2026 | 0.1 | Story criada pela quebra da FND-09 (revisão §2.3), com a navegação da ADR 0005 D8 | River (SM) |
| 16/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |

## Dev Agent Record
_A preencher pelo @dev._

## QA Results
_A preencher pelo @qa._