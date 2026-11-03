# ADR 0044: Padrão de layout (largura por tipo de tela e grade de formulário)

- Status: Aceito (02/11/2026). Decisões do PO de 02/11/2026, as três como recomendadas na proposta (`docs/ux/padrao-layout.md`, §G).
- Contexto: a largura era decidida em cada tela. O `main` do shell tinha `max-w-6xl` (1152 px): a 1920 px sobravam ~384 px vazios de cada lado, o Kanban mostrava ~3,8 colunas e o Gantt rolava sem necessidade. Os formulários tinham larguras soltas (98× `max-w-3xl`, 11× `4xl`, 10× `5xl`, 17× `xl`), todo campo ocupava meia coluna (UF com o tamanho da razão social) e, em 9 lugares, uma grade de 2 colunas dentro da grade de 2 colunas do `FormSection` deixava cada campo com ¼ da largura.
- Complementa a ADR 0005 (shell, dock e navegação) e o padrão "Interface" do CLAUDE.md (tokens, sem cor literal; ajuda muda junto com a tela).

## Decisão

### 1. Largura por tipo de tela (decisão 1 do PO)
A largura sai do `main` (que fica fluido: `w-full px-4 py-6 sm:px-6 2xl:px-8`) e passa a ser declarada pela rota em `staticData.width` (TanStack Router; tipo `PageWidth` em `src/components/layout/page-width.ts`). O `AppShell` lê a rota mais profunda que declara (`useMatches`) e aplica a classe num contêiner em volta do `Outlet`. Sem declarar, vale `default`.

| Tipo | Uso | Contêiner | Largura útil |
|---|---|---|---|
| `full` | Kanban, Gantt/planejamento, painel do proprietário e sub-telas, DRE, fluxo de caixa; depois: capacidade, aging, rastreabilidade, revisão da folha, listas com 8 ou mais colunas | `mx-auto w-full max-w-[1920px]` | até 1856 px |
| `default` | Lista com filtros (até 7 colunas), detalhe com abas, telas mistas | `mx-auto w-full max-w-screen-2xl` (1536 px), centralizado | ~1488 px |
| `form` | Cadastro e edição, configurações | contêiner do `default`, com o conteúdo limitado a `max-w-[77.5rem]` à esquerda (formulário + painel) | 896 + 320 px |

- Texto corrido (descrição, aviso, vazio) usa `max-w-prose` em qualquer tipo.
- Detalhe com abas fica `default`; a aba que é formulário usa o `FormLayout` dentro dele. A largura só muda ao trocar de aba quando o conteúdo muda de natureza.
- Diálogos, `SidePanel` (480 px) e telas dedicadas (PWA do operador, Painel da TV, quiosque) ficam fora do padrão. O Kanban em tela cheia é um estado do `full`.

### 2. Formulário à esquerda com painel lateral (decisão 2 do PO)
- Coluna do formulário de **896 px** (`max-w-4xl`), alinhada à esquerda, com o título. Nada de `mx-auto` que centralize de novo.
- Com painel (`FormLayout aside`), a partir de **1280 px** (`xl`): `grid xl:grid-cols-[minmax(0,56rem)_20rem] gap-6`, painel de **320 px** preso no topo (`sticky top-24`) com resumo, próximo passo, pendências e atalho da ajuda. Abaixo de `xl`, o painel vai para depois do formulário (no DOM ele vem sempre depois). Tela sem nada útil para o painel não tem painel.

### 3. Grade e tamanho do campo
- `FormGrid`: 12 trilhas com consulta pelo **contêiner** (`@container`): 1 coluna abaixo de 448 px (`@md`), 6 trilhas de 448 a 768 px e 12 a partir de 768 px (`@3xl`). A mesma grade serve na página, no `SidePanel` e no diálogo, e o zoom de 200% cai sozinho para 6 ou 1 trilha.
- `Field size` pelo conteúdo (padrão `lg`): `xs` 2·2 (UF, número, %, parcelas) · `sm` 3·3 (CPF, CEP, data, telefone, dinheiro, quantidade, código, PIS, RG) · `md` 3·4 (CNPJ, select curto, complemento, bairro, cidade) · `lg` 6·6 (e-mail, nome fantasia, select longo) · `xl` 6·8 (nome, razão social, logradouro) · `full` 6·12 (observação, textarea, upload, editor de linhas). `span` explícito só quando a linha não fecha em 12, comentado na tela.
- `FormSection grid="form"` usa a `FormGrid`; `grid="none"` deixa o conteúdo sem grade; sem `grid` (padrão `legacy`) continua a grade antiga de 2 colunas até a tela migrar. **Proibido grade dentro de grade.**
- Espaçamento: 16 px entre campos, 20 px entre seções (`space-y-5`), `p-4 sm:p-5` no cartão. Rótulo acima, "(obrigatório)" em texto, `Dica` ao lado do rótulo.

### 4. Ações do formulário (decisão 3 do PO)
- **Formulário curto** (1 seção, cabe na tela): botões no fim da coluna, à direita (`FormActions`).
- **Formulário longo** (2 ou mais seções ou maior que a tela): `FormActions sticky`, barra presa no rodapé **acima do dock** (`bottom: calc(var(--dock-offset) + 0.5rem)`, `bg-card/95`, borda e `backdrop-blur`), na largura da coluna, com **"Alterações não salvas"** à esquerda enquanto houver mudança. O aviso de sair sem salvar continua no `UnsavedChangesGuard`. No celular, botões de 44 px na largura toda, o primário embaixo.
- `--dock-offset` é definido uma vez no `index.css`; o `scroll-padding-bottom` soma o dock e a barra (WCAG 2.4.11).
- Ação destrutiva fica fora da barra. Um só primário por tela, com verbo claro.

### 5. Componentes
- `src/components/layout/page-width.ts`: `PageWidth`, `staticData.width`, classes por tipo e `usePageWidth`.
- `src/components/layout/form-layout.tsx`: `FormLayout` (coluna + painel opcional).
- `src/components/form/form.tsx`: `Field` (`size`, `span`), `FormGrid`, `FormGridItem`, `FormSection` (`grid`), `FormActions`; `field-size.ts` com a tabela de trilhas.
- `src/shell/PageHeading.tsx`: é o "PageHeader" do padrão; ganhou `description` (em `max-w-prose`), `actions` (à direita do título) e `meta` (chips), sem mudar quem já usa.
- Um componente `Page` não foi criado: o shell já aplica a largura pela rota.

### 6. Tema e acessibilidade
Nenhuma cor nova (tudo por token: `bg-card/95`, `border-border`, `text-muted-foreground`). Ordem do DOM: formulário, depois painel. Consulta por contêiner garante o reflow (1.4.10) sem rolagem horizontal; só `full` com tabela ou Gantt rola dentro do próprio componente. Alvo de toque de 44 px na barra (2.5.8).

## Consequências
- O PR base (este) já tira a faixa vazia de todas as telas: `default` passa de 1104 px úteis para até ~1488 px; as 7 rotas `full` vão até 1856 px. As telas que têm `max-w-*` próprio continuam como estão até o PR do módulo.
- Rotas marcadas `form` neste PR: Configurações (Empresa, Parâmetros, Segurança, IA, Cobrança, Retenção da privacidade, Certificado fiscal, Emissor fiscal), Minha conta e Segurança da conta, Modelo da OS de instalação, nova conta a pagar e a receber, novo funcionário, novo terceiro, novo item, novo bem, condição de pagamento (nova e existente), nova ordem de pagamento, nova solicitação de compra e exportação para a contadora.
- Telas piloto já no padrão: Configurações → Empresa, Produção → Modelo da OS de instalação e RH → funcionário, aba Dados pessoais.
- Migração por módulo, um PR cada, na ordem do `docs/ux/padrao-layout.md` §E (Financeiro, RH, Produção, Comercial, Configurações, demais). Cada PR tira o `max-w-*` solto das telas, corrige a grade dentro da grade, marca as rotas e atualiza a ajuda no mesmo commit. No fim, sai o modo `legacy` do `FormSection` e entra a regra de lint contra `max-w-(xl|2xl|3xl|4xl|5xl|6xl)` em `pages/**`.
- E2E que medem posição (`fnd09e`, `supervisor`, `cash-flow-adjusted`, `dashboard`) rodam em cada PR de módulo.
