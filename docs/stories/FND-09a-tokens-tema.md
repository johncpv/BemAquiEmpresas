# FND-09a — Tokens de design, tema e componentes visuais base

| Campo | Valor |
|---|---|
| ID | FND-09a |
| Título | Tokens (claro, escuro, TV), mapeamento para shadcn, tema salvo no perfil sem flash, Archivo, chips, indicador de etapa e `Money` |
| Tamanho | M |
| Status | In Progress (pronta; E2E com o Playwright na FND-09b) |
| Requisitos | PRD §6 Usabilidade (responsivo) · base visual de R-SEC-11 e R-AJD-4 |
| Dependências | FND-01 (lint contra cor literal). A persistência do tema no perfil usa a API da FND-04; antes dela, vale o armazenamento local |
| Bloqueia | FND-09b, FND-09c e todas as telas |
| Ordem de execução | Paralela, logo após a FND-01 |

## User story
Como **usuário**, quero **o sistema com a identidade aprovada no protótipo, legível no tema claro e no escuro, e com o tema que escolhi lembrado em qualquer dispositivo**, para **trabalhar com conforto e reconhecer o estado das coisas pela forma e pela cor**.

## Contexto e referências
- `CLAUDE.md` → Interface: tema claro, escuro e automático, salvo no perfil; toda cor vem de tokens; TV sempre escura; tons de aço frios, trilho escuro, acento cobalto, âmbar só para destaque de estado e item ativo; Archivo com larguras variáveis; `tabular-nums`; indicador de etapa no estilo display de andar; chips ok/alerta/crítico/informativo.
- `docs/prototipo/prototipo-erp-konebras.html` (tokens `--steel`, `--surface`, `--sunken`, `--ink`, `--muted`, `--line`, `--rail*`, `--accent*`, `--ok*`, `--warn*`, `--crit*`, `--led`).
- Revisão §2.4 (correções de contraste e tokens novos; âmbar com dois papéis; TV; fonte; tema sem flash; `Money`; lint; mapeamento shadcn).

## Critérios de aceite
**Tokens e contraste**
1. **Dado** o arquivo de tokens, **quando** inspeciono, **então** os tokens do protótipo estão mapeados para o shadcn: `--accent` do protótipo → `--primary`; `--muted` do protótipo → `--muted-foreground`; demais conforme a tabela do Plano.
2. **Dado** os tokens novos da revisão, **quando** inspeciono, **então** existem `--success-foreground`, `--ok-ink`, `--warn-ink`, `--crit-ink`, `--info` (separado do acento), `--info-ink`, `--input` e `--signal` (que substitui `--led`, usado só para marca, item ativo e indicador de etapa).
3. **Dado** o tema escuro, **quando** meço o contraste de botão de sucesso (texto sobre `--ok`), **então** é ≥ 4,5:1 (era 2,12:1).
4. **Dado** o tema claro, **quando** meço os chips ok, alerta e crítico (texto `*-ink` sobre `*-soft`), **então** todos são ≥ 4,5:1.
5. **Dado** os dois temas, **quando** meço a borda de input (`--input`) contra o fundo e o anel de foco sobre o trilho, **então** ambos são ≥ 3:1.
6. **Dado** o tema escuro, **quando** comparo `--signal` e `--warn`, **então** são cores distintas (o âmbar deixa de ter dois papéis).
7. **Dado** o lint, **quando** um componente usa cor hex, `rgb()` ou classe Tailwind de cor fixa (ex.: `bg-blue-600`) fora do arquivo de tokens, **então** o `pnpm lint` falha.

**Tema**
8. **Dado** a escolha "Claro", "Escuro" ou "Automático", **quando** recarrego a página, **então** não há flash do tema errado: o tema é aplicado antes da primeira pintura (script inline mínimo que lê a última escolha guardada localmente).
9. **Dado** um usuário logado, **quando** troca o tema, **então** a escolha é salva no perfil (`PUT /api/v1/me/preferences`) e vale em outro dispositivo no próximo login.
10. **Dado** "Automático", **quando** o sistema operacional muda de claro para escuro, **então** o app acompanha sem recarregar.
11. **Dado** o escopo `.theme-tv`, **quando** aplicado, **então** é sempre escuro, sem cor literal, com corpo de texto ≥ 24px.

**Fonte e componentes base**
12. **Dado** o build, **quando** inspeciono as requisições, **então** a Archivo variável é servida localmente (`@fontsource-variable/archivo`), sem chamada a Google Fonts; títulos e números usam a largura expandida; números usam `tabular-nums`.
13. **Dado** o componente `StatusChip`, **quando** renderizado com `ok`, `warn`, `crit` ou `info`, **então** usa os tokens `*-soft`/`*-ink` e tem rótulo de texto (o estado nunca é só cor).
14. **Dado** o componente `StageIndicator` (display de andar), **quando** recebe a etapa atual e o total, **então** mostra a letra da etapa em `--signal` sobre fundo escuro e os segmentos, com rótulo acessível "Etapa {nome}, {n} de {total}".
15. **Dado** o componente `Money`, **quando** recebe `1234.5`, **então** mostra `R$ 1.234,50`; com `cents={false}`, mostra `R$ 1.235`; sempre com `tabular-nums`; valores negativos com sinal e token de crítico quando pedido.
16. **Dado** a galeria de desenvolvimento (rota só em `dev`), **quando** comparo com o protótipo nos dois temas e a 400px de largura, **então** cores, tipografia e componentes batem (comparação por screenshot no Playwright).

## Regras de negócio e técnicas
- Tailwind com tokens em CSS variables (`:root`, `.dark`, `.theme-tv`); shadcn/ui configurado para ler os tokens.
- Nenhum componente usa cor literal (lint da FND-01 ampliado para classes de cor do Tailwind).
- Preferência de tema: `system | light | dark`; guardada localmente para evitar flash e no perfil para seguir o usuário.
- Formatação: `Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' })`; datas em America/Sao_Paulo.
- Radix para Switch e ToggleGroup (revisão §2.4) usados pelo seletor de tema.

## Permissões, eventos e tabelas
- **Tabela:** preferências do usuário (tema; e, na FND-14, ajudas vistas). Não existe no `03-modelo-dados.md`: propõe-se `identity.user_preferences` (`user_id`, `key`, `value`). Ver Pontos em aberto.
- **Permissões:** nenhuma (o usuário edita as próprias preferências).

## Telas e estados
- **Galeria de componentes** (rota de desenvolvimento, fora do build de produção): tokens, chips, indicador de etapa, `Money`, botões, inputs, foco.
- **Seletor de tema:** ToggleGroup "Auto · Claro · Escuro" (protótipo); a 400px vai para o menu do usuário (FND-09b).
- Sem estados de lista nesta story.

## Plano de testes
- **Unidade (Vitest):** `Money` (CA 15); `StatusChip` com rótulo (CA 13); `StageIndicator` (CA 14).
- **Contraste automatizado:** script que calcula o contraste dos pares de tokens definidos e falha abaixo do limite (CA 3 a 6).
- **Lint:** arquivo de exemplo com cor literal deve falhar (CA 7).
- **E2E (Playwright):** sem flash na carga (screenshot no primeiro frame) (CA 8); tema salvo no perfil (CA 9, após FND-04); `prefers-color-scheme` emulado (CA 10); screenshots da galeria nos dois temas e a 400px (CA 16).

### Tabela de mapeamento (base)
| Protótipo | shadcn/token final |
|---|---|
| `--steel` | `--background` |
| `--surface` | `--card`, `--popover` |
| `--sunken` | `--muted` |
| `--ink` | `--foreground` |
| `--muted` | `--muted-foreground` |
| `--line` | `--border` |
| (novo) | `--input` (≥ 3:1) |
| `--accent` / `--accent-ink` / `--accent-soft` | `--primary` / `--primary-foreground` / `--primary-soft` |
| `--rail`, `--rail-ink`, `--rail-dim`, `--rail-hi` | `--rail*` (mantidos) |
| `--ok`, `--warn`, `--crit` + `-soft` | mantidos + `*-ink` e `--success-foreground` |
| `--led` | `--signal` |
| (novo) | `--info`, `--info-soft`, `--info-ink` |

## Fora do escopo
- Dock, topbar e navegação (FND-09b).
- DataTable, estados e formulários (FND-09c).
- `<Dica>` (FND-14).
- Layouts do PWA do operador e da TV (Fases 3 e 4), além do escopo `.theme-tv`.

## Pontos em aberto
- **Tabela de preferências do usuário:** não existe no modelo de dados. Propor `identity.user_preferences` e atualizar o 03 (decisão do arquiteto/PO).
- **Valores finais dos tokens novos:** a revisão define os limites de contraste, não as cores. O @ux-design-expert propõe e o PO aprova, mantendo a identidade.
- **Logo do BemAqui Empresas** (marca da plataforma) para telas sem tenant (plataforma): não existe no protótipo.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, contraste, lint e E2E passando.
- [ ] Migration criada para as preferências do usuário (se aprovada a tabela).
- [ ] Permissão registrada no seed: não se aplica.
- [ ] Tela com estados: não se aplica (galeria de desenvolvimento).
- [ ] Ajuda da tela: não se aplica (galeria não vai para produção).
- [ ] Padrões e próximo passo propositivos: tema "Automático" como padrão.
- [ ] Auditoria: não se aplica a preferências.
- [ ] Story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Arquivo de tokens (claro, escuro, TV) e configuração do Tailwind e do shadcn.
- [ ] Ajustes de contraste e tokens novos da revisão §2.4.
- [ ] Script de verificação de contraste no CI.
- [ ] Lint ampliado (classes de cor do Tailwind).
- [ ] Archivo self-hosted e utilitários de largura e `tabular-nums`.
- [ ] Script inline de tema e provider de tema com sincronização ao perfil.
- [ ] `StatusChip`, `StageIndicator`, `Money` e galeria de desenvolvimento.

## Quality gates
- Agentes previstos: @ux-design-expert, design-squad (acessibilidade), @dev, @qa.
- Gate: contraste automatizado verde nos dois temas.

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
- **Pré-condições para `Ready`:** Tabela de preferências do usuário; valores finais dos tokens novos.
- **Prontidão:** 8/10. **Veredito:** GO condicional (permanece `Draft` até as pré-condições serem decididas).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 16/10/2026 | 0.1 | Story criada pela quebra da FND-09 (revisão §2.3), com as correções de design da §2.4 | River (SM) |
| 16/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |

## Dev Agent Record
- **Entrega (FND-09a), 18/10/2026, branch `feat/fnd-09a-tokens-tema`.**
  - **Tokens** (`src/styles/tokens.css`):
    - nomes do shadcn conforme a tabela do Plano (CA 1);
    - tokens novos da revisão §2.4: `*-ink`, `--success-foreground`, `--critical-foreground`, `--input`, `--rail-ring`, `--info*` e `--signal` no lugar de `--led` (CA 2);
    - três escopos: claro, escuro (automático e forçado) e `.theme-tv`, que é sempre escuro, com corpo de 24px (CA 11).
  - **Ajustes de cor** (o PO valida na galeria): o `--warn` escuro passou a laranja `#ff8a5c` para se separar do `--signal` `#f2a93b` (CA 6), e os tons de `*-ink` foram escurecidos ou clareados até passar no contraste. A identidade do protótipo continua igual.
  - **Contraste automatizado** (`scripts/check-contrast.mjs`): 20 pares por tema, dentro do `pnpm lint` e do CI (CA 3 a 6).
  - **Lint de cor:** além de hex, barra `rgb()`/`hsl()` e classes de cor fixa do Tailwind (CA 7).
  - **Tema:**
    - script inline no `index.html`, sem flash (CA 8);
    - `ThemeProvider` + `ThemeToggle` (ToggleGroup do Radix). "Automático" deixa o CSS seguir o sistema, sem recarregar (CA 10);
    - o perfil vence o navegador;
    - `GET`/`PUT /api/v1/me/preferences` grava o tema em `identity.user_preferences` (CA 9). A ligação no front entra com o cliente Orval na FND-09c.
  - **Archivo self-hosted** (`@fontsource-variable/archivo`, eixos `wdth` e `wght`), com utilitários `font-wide`, `font-wider` e `font-widest` e `tabular-nums` nos números (CA 12).
  - **Componentes:** `StatusChip` (CA 13), `StageIndicator` (CA 14), `Money` (CA 15) e a galeria em `/dev/galeria`, que fica fora do build de produção (CA 16).
  - **Testes:** unidade (Vitest) dos componentes, do tema, do lint e do contraste; integração das preferências.
  - **Pendente:** E2E com Playwright (sem flash no primeiro frame e screenshots da galeria nos dois temas e a 400px). Entra junto com a configuração do Playwright na FND-09b.

## QA Results
_A preencher pelo @qa._