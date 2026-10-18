# FND-14 — Ajuda contextual e Dica

| Campo | Valor |
|---|---|
| ID | FND-14 |
| Título | Catálogo de ajudas em Markdown, endpoints `/ajuda`, painel lateral, abertura na primeira visita, `<Dica>` e teste de cobertura |
| Tamanho | M |
| Status | Done |
| Requisitos | R-AJD-1 · R-AJD-2 · R-AJD-3 · R-AJD-4 · R-AJD-6 · R-AJD-7 |
| Dependências | FND-06c (permissões efetivas), FND-06d (menu por permissão), FND-09b (slot do botão Ajuda) |
| Bloqueia | FND-15 |
| Ordem de execução | 17 |

## User story
Como **usuário**, quero **abrir a ajuda da tela em que estou, ver uma explicação curta ao lado do que confunde e receber a ajuda sozinha na primeira visita**, para **aprender o sistema sem treinamento e sem sair da tela**.

## Contexto e referências
- PRD §5.15a (padrão NSHome, `D:\GIT\NSHome`), R-AJD-1, 2, 3, 4, 6, 7.
- `CLAUDE.md` → Interface: ajuda em `Ajuda/<chave>.md` (cabeçalho `titulo`, `rota`, `aba`, `padrao`, `modulo`); o botão "Ajuda" acha a ajuda pela rota e pela aba e abre sozinho na primeira visita; `<Dica texto="..." />`; mudou a tela, mude a ajuda no mesmo commit; o teste de cobertura falha se uma rota ficar sem ajuda.
- Backlog original (CAs): toda rota do front tem ajuda (o teste falha se faltar); a ajuda de um módulo sem permissão não aparece no índice; a primeira visita abre a ajuda uma vez por usuário e por tela, em qualquer dispositivo.

## Critérios de aceite
1. **Dado** um arquivo `Ajuda/<chave>.md` com cabeçalho `titulo`, `rota`, `aba`, `padrao` e `modulo` e as seções "Para que serve", "Como usar" e "Perguntas frequentes", **quando** o build roda, **então** ele é embutido e validado; cabeçalho faltando ou seção obrigatória faltando falha o build.
2. **Dado** `GET /api/v1/ajuda`, **quando** um usuário chama, **então** recebe o índice só com ajudas gerais (sem `modulo`) e dos módulos liberados no tenant e habilitados para ele (R-AJD-6).
3. **Dado** `GET /api/v1/ajuda/{chave}` de um módulo sem permissão, **quando** chamo, **então** recebo 403 com o mesmo código da camada que barrou (`module_not_released` ou `forbidden`).
4. **Dado** uma tela, **quando** clico em "Ajuda" na topbar, **então** abre o painel lateral com a ajuda resolvida pela rota e pela aba atual (`aba` igual à aba ativa; sem aba, a de `padrao: true` da rota), o índice de todas as ajudas permitidas e a busca.
5. **Dado** a busca do painel, **quando** digito um termo, **então** vejo as seções que contêm o termo, priorizando a tela atual, com link para a seção.
6. **Dado** a primeira visita de um usuário a uma tela (rota + aba), **quando** a tela abre, **então** a ajuda abre sozinha uma vez; nas visitas seguintes, em qualquer dispositivo, não abre de novo (registro de "já vista" no perfil do usuário, no servidor).
7. **Dado** o componente `<Dica texto="..." />`, **quando** passo o mouse, foco com Tab ou toco no "?", **então** o texto aparece, é lido por leitor de tela (`aria-describedby`) e fecha com Esc ou ao tocar fora.
8. **Dado** o teste de cobertura, **quando** existe uma rota do TanStack Router (ou uma aba de rota) sem ajuda correspondente, **então** o teste falha com a lista das rotas sem ajuda.
9. **Dado** um Markdown de ajuda com HTML ou script embutido, **quando** é exibido, **então** o HTML é removido ou escapado (renderização segura).
10. **Dado** as telas já entregues na Fase 0 (login e recuperação, se aplicável; 2FA; Configurações → Segurança; Usuários e acessos → Grupos e Usuários; módulo não liberado; acesso negado; início), **quando** o teste de cobertura roda, **então** todas têm ajuda escrita.

## Regras de negócio e técnicas
- Arquivos em `Ajuda/` (local exato: ver Pontos em aberto) embutidos como recurso no assembly do backend; carregados em memória na subida.
- Chave = nome do arquivo sem extensão, em kebab-case.
- O índice reaproveita o cálculo de permissão efetiva da FND-06c (sem regra de acesso própria).
- Markdown renderizado no front com sanitização (sem HTML cru).
- Registro de ajudas vistas por usuário no servidor; leitura junto do `/me` para não abrir a ajuda à toa.
- Textos em PT-BR, curtos, com verbo no imperativo em "Como usar".

## Permissões, eventos e tabelas
- **Tabela:** ajudas vistas por usuário (`identity.user_help_views`: `user_id`, `help_key`, `seen_at`) ou chave em `identity.user_preferences` (FND-09a). Não existe no 03; ver Pontos em aberto.
- **Permissões:** nenhuma nova; a ajuda segue as permissões do módulo (`modulo` do cabeçalho).
- **Eventos:** nenhum.

## Telas e estados
- **Painel de ajuda** (lateral à direita no desktop, sheet em tela cheia no celular): título da ajuda atual, seções, índice e busca.
- **Estados:** carregando (skeleton das seções); vazio (tela sem ajuda não pode existir por causa do teste; para busca sem resultado: "Nada encontrado para '{termo}'. Tente outra palavra."); erro ("Não foi possível carregar a ajuda" + "Tentar de novo").
- **Propositivo:** abre sozinha na primeira visita; a busca prioriza a tela atual; cada "Perguntas frequentes" termina com o link para a ação na própria tela, quando houver.
- **Ajuda do próprio painel:** `Ajuda/ajuda.md` (como usar a ajuda e a Dica).

## Plano de testes
- **Unidade:** parser do cabeçalho e validação das seções (CA 1); resolução por rota e aba (CA 4); sanitização (CA 9).
- **Integração (Testcontainers):** índice filtrado por permissão com usuários de grupos diferentes e módulo não liberado (CA 2, 3); registro de "já vista" (CA 6).
- **Cobertura (Vitest/Node):** enumera a árvore de rotas do TanStack Router e cruza com o índice (CA 8, 10).
- **E2E (Playwright):** primeira visita abre a ajuda, segunda não, e em outro contexto de navegador (outro "dispositivo") também não (CA 6); `<Dica>` por mouse, teclado e toque (CA 7).

## Fora do escopo
- Pergunte ao BemAqui (FND-15).
- Ajudas dos módulos das fases seguintes (cada story escreve a sua).
- Edição de ajuda pelo tenant.

## Pontos em aberto
- **Local da pasta `Ajuda/`:** na raiz do repositório, no `src/Host` ou por módulo (`src/Modules/*/Ajuda`)? O CLAUDE.md diz só `Ajuda/<chave>.md`. Ver também o padrão do NSHome.
- **Significado exato de `padrao`:** esta story interpreta como "ajuda padrão da rota quando não há aba". Confirmar com o padrão do NSHome.
- **Rotas públicas e da superfície Plataforma** (login, acesso suspenso, super admin): entram no teste de cobertura? (mesmo ponto das FND-02 e FND-13).
- **Tabela de ajudas vistas:** não existe no modelo de dados; decidir entre tabela própria e `user_preferences`.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers), cobertura e E2E passando.
- [ ] Migration criada (ajudas vistas).
- [ ] Permissão registrada no seed: não se aplica.
- [ ] Painel com estados de carregando, vazio e erro.
- [ ] Ajuda das telas da Fase 0 escrita (CA 10).
- [ ] Padrões e próximo passo propositivos avaliados.
- [ ] Auditoria: não se aplica.
- [ ] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Formato e validador dos arquivos de ajuda (cabeçalho e seções).
- [ ] Catálogo embutido e endpoints `/api/v1/ajuda` e `/api/v1/ajuda/{chave}`.
- [ ] Registro de ajudas vistas por usuário.
- [ ] Painel lateral com resolução por rota e aba, índice e busca.
- [ ] Componente `<Dica>` acessível.
- [ ] Teste de cobertura de rotas.
- [ ] Escrever as ajudas das telas da Fase 0.

## Quality gates
- Agentes previstos: @dev, @ux-design-expert, @qa.
- Gate: teste de cobertura ligado no CI e obrigatório.

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
- **Pré-condições para `Ready`:** Local da pasta `Ajuda/`, significado de `padrao`, cobertura de rotas públicas e da plataforma.
- **Prontidão:** 8/10. **Veredito:** GO condicional (permanece `Draft` até as pré-condições serem decididas).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 16/10/2026 | 0.1 | Story detalhada a partir do backlog e do PRD §5.15a | River (SM) |
| 16/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |

## Dev Agent Record
- **Entrega (FND-14), 18/10/2026, branch `feat/fnd-14-ajuda`.** Os pontos em aberto foram decididos na ADR 0013.
  - **Catálogo (CA 1):** `HelpCatalog` lê `Ajuda/*.md`, embutidos no Host, valida o cabeçalho e as três seções obrigatórias e derruba a subida se algo estiver errado. O teste de arquitetura valida os arquivos no CI.
  - **API:**
    - `GET /api/v1/ajuda` devolve só as ajudas gerais e as dos módulos liberados e habilitados para o usuário (CA 2);
    - `GET /api/v1/ajuda/{chave}` responde 403 com `module_not_released` ou `forbidden` (CA 3);
    - `GET /ajuda/vistas` e `POST /ajuda/{chave}/vista` guardam a ajuda vista em `user_preferences`.
  - **Painel (CA 4, 5, 6):** o botão "Ajuda" abre o painel lateral, que vira tela cheia no celular. A ajuda é escolhida pela rota e pela aba (a tela informa a aba com `useHelpTab`). O painel tem o índice permitido e a busca, que prioriza a tela atual. Na primeira visita a uma tela, abre sozinho uma vez.
  - **`<Dica>` (CA 7):** abre no mouse, no foco e no toque e fecha com Esc. O texto fica ligado por `aria-describedby`. Já está em "Habilitado" na matriz de grupos e em Configurações → Segurança.
  - **Markdown seguro (CA 9):** renderizador próprio, sem HTML cru.
  - **Cobertura (CA 8, 10):** todas as rotas do roteador têm ajuda. Entraram as que faltavam: `modulo`, `modulo-aba`, `plataforma-definir-senha`, `fab` e `tv`, além de `ajuda.md`, que explica o próprio painel.
  - **Testes:** integração (índice por permissão, módulo não liberado, já vista), Vitest (cobertura, resolução, busca, sanitização, Dica) e Playwright (primeira visita, reload sem reabrir, busca, Dica por teclado e toque).

## QA Results
_A preencher pelo @qa._