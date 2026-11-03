# Padrão de layout — BemAqui Empresas

> **Aprovado pelo PO em 02/11/2026** (as três decisões do §G, como recomendadas). Decisão registrada na **ADR 0044**. A base (PR 0 do §E) está implementada: `staticData.width` nas rotas, `AppShell` fluido, `FormLayout`, `FormGrid`, `Field size`, `FormActions` e o `PageHeading` com `description`, `actions` e `meta`. Telas piloto: Configurações → Empresa, Produção → Modelo da OS de instalação e RH → funcionário, aba Dados pessoais. As diferenças entre a proposta e o que foi feito estão marcadas com **(implementado)**.

Revisão de design feita só por leitura em `D:\GIT\bq-deploy` (origin/main). Método: auditoria de design (lente "craft": consistência visual, design system, acessibilidade), com o protótipo aprovado como referência.
Base medida: `src/Web/src/shell/AppShell.tsx`, `components/form/form.tsx`, `index.css`, `docs/prototipo/prototipo-erp-konebras.html` e cerca de 15 telas.

**Pontos fortes que ficam:** tokens sem cor literal, `Field` com rótulo em cima + `(obrigatório)` + `Dica` + `aria-describedby`, `FormSection` como cartão, máscaras com `tabular-nums`, `DataTable` com `overflow-x-auto`, `scroll-padding` para a topbar e o dock (WCAG 2.4.11).
**Lacuna central:** a largura é decidida em cada tela. Não há um componente de página nem uma grade de formulário.

---

## A. Diagnóstico

Medidas a 1920 px de tela. O `main` tem `max-w-6xl` = 1152 px, menos 24 px de padding de cada lado: sobra **1104 px úteis** e **~384 px vazios de cada lado**. A topbar ocupa a largura toda, então a borda do conteúdo não se alinha com a marca nem com a trilha.

| Tipo de tela | Exemplo (arquivo) | Como está hoje | Problema |
|---|---|---|---|
| Quadro | `pages/production/kanban/KanbanPage.tsx` | `flex gap-3 overflow-x-auto`, colunas `sm:w-72` (288 px) | Cabem só ~3,8 colunas em 1104 px. O resto rola, com 768 px livres na tela |
| Gantt | `pages/production/planning/GanttTab.tsx` + `components/ui/gantt.tsx` | Herda os 1104 px; coluna de nomes fixa | A janela de datas fica estreita e obriga a rolar na horizontal |
| Lista larga | `finance/receivables/ReceivablesPage.tsx` (7 col.), `production/orders/FabricationOrdersPage.tsx` (9 col.) | `DataTable` com `w-full` dentro de 1104 px | As colunas truncam ou rolam na horizontal sem necessidade |
| Lista simples | `finance/BankAccountsPage.tsx` (6 col.), `inventory/stock/BalancesPage.tsx` (5 col.) | `PageHeading` + `DataTable` | Funciona, mas desperdiça as laterais |
| Formulário longo | `settings/company/CompanyPage.tsx` | `form max-w-3xl` (768 px) dentro de 1104; `FormSection` com `sm:grid-cols-2` | Todo campo ocupa meia coluna: UF ou CEP com 370 px, razão social com o mesmo tamanho do CEP |
| Aba de cadastro | `hr/EmployeePersonal.tsx`, `hr/EmployeeGeneral.tsx` | `max-w-3xl` + **grade de 2 colunas dentro da grade de 2 colunas do `FormSection`** | **Defeito:** a grade interna ocupa só 1 célula, e cada campo fica com ~180 px (¼). No mesmo formulário, "Identificação" usa ½ e "Contrato" usa ¼. Ocorre em 9 lugares: NewPayable, NewReceivable, EmployeeGeneral, EmployeePersonal (3×), NewEmployee, BillingSettings, CollectionRuleSection |
| Formulário simples | `production/installations/InstallationTemplatePage.tsx` | `max-w-3xl`, textareas | OK isolado. Ao lado de outras telas, a largura é arbitrária |
| Formulários variados | `fiscal/incoming/InvoiceReceiptPage.tsx` (`max-w-5xl`), `finance/payment-orders/NewPaymentOrderPage.tsx` (`4xl`), `settings/SecurityPage.tsx` (`xl`) | 98× `max-w-3xl`, 11× `4xl`, 10× `5xl`, 17× `xl` | Cada formulário com uma largura: é a queixa 2 do PO |
| Detalhe com abas | `quotes/QuotePage.tsx`, `production/orders/FabricationOrderPage.tsx`, `hr/EmployeePage.tsx` | Cabeçalho em 1104 px. A aba decide a própria largura (`QuoteHeaderTab` 3xl, `ConditionsTab` 4xl) | O conteúdo "pula" de largura ao trocar de aba |
| Painel | `dashboard/DashboardPage.tsx` | `grid lg:grid-cols-12`, cartões em 1104 px | Os gráficos ficam espremidos. O protótipo usa 1360 px (`.view`) |

Comparação com o protótipo: `.view{max-width:1360px}`, layout principal + lateral `minmax(0,1.75fr) minmax(0,1fr)` e mestre-detalhe `300–320px + 1fr`. O app atual é mais estreito que o próprio protótipo.

---

## B. Largura por tipo de tela (o centro da proposta)

A largura deixa de estar no `main` e passa a ser declarada pela rota. O `main` fica fluido: `w-full px-4 py-6 sm:px-6 2xl:px-8`.

| Tipo | Uso | Classe do contêiner | Largura útil | Justificativa |
|---|---|---|---|---|
| **Largura total** (`full`) | Kanban, Gantt/planejamento, capacidade, painel, fluxo de caixa, DRE, aging, rastreabilidade, revisão da folha, listas com 8 ou mais colunas | `mx-auto w-full max-w-[1920px]` | Até 1856 px | A densidade é o valor da tela: mais colunas, mais dias, mais cartões sem rolar. O teto de 1920 px evita linhas de 2500 px em ultrawide, onde o olho perde a linha |
| **Padrão** (`default`) | Lista + filtros (até 7 colunas), detalhe com abas, telas mistas | `mx-auto w-full max-w-screen-2xl` (1536 px) | ~1488 px | Um passo acima do protótipo (1360). Tabelas de 5 a 7 colunas ficam sem truncar, e o percurso do olho do nome ao valor fica abaixo de ~1200 px. Em 1920 sobram 192 px de cada lado, não 384 |
| **Formulário** (`form`) | Cadastro e edição, configurações, abas de cadastro | Contêiner `default` + coluna de formulário **à esquerda**: `max-w-4xl` (896 px). A partir de `xl` (≥1280): `grid xl:grid-cols-[minmax(0,56rem)_20rem] gap-6`, com painel lateral. **(implementado)** O shell limita o conteúdo da rota `form` a `max-w-[77.5rem]` (56 + 1,5 + 20 rem), à esquerda, para a tela ainda não migrada não se espalhar; o `FormLayout` monta a coluna e o painel | 896 px de campos + 320 px de painel | Uma coluna de 12 trilhas de ~60 px dá ao campo o tamanho do conteúdo (seção C). O texto de ajuda fica em ≤ 75 caracteres. Alinhado à esquerda, com o título, o olho não "procura" o formulário no meio de dois vazios. O painel aproveita a largura com algo útil |

Regras complementares:
- Texto corrido (descrição de seção, aviso, `EmptyState`) usa `max-w-prose` (~65 caracteres), em qualquer tipo de tela.
- Detalhe com abas: o cabeçalho e a faixa de abas ficam no `default`. Uma aba que é formulário usa o layout `form` dentro dele, e uma aba que é tabela ou gráfico usa a largura do contêiner. A largura não muda ao trocar de aba sem que o conteúdo mude de natureza.
- Diálogos e `SidePanel` (480 px) não mudam. A grade de formulário reage ao tamanho do contêiner (ver C), então funciona igual dentro deles.
- Telas dedicadas (PWA do operador, Painel da TV, quiosque) ficam fora deste padrão.
- Front paralelo (abas internas e Kanban em tela cheia): o modo tela cheia do Kanban é um estado do tipo `full`, sem tipo próprio. As abas internas devem respeitar a regra de largura descrita acima.

---

## C. Padrão de formulário

**Grade:** 12 trilhas, com consulta pelo **contêiner** (Tailwind 4 tem `@container` nativo). O mesmo formulário se adapta à página, ao `SidePanel` e ao diálogo.
```
<FormSection>            → fieldset.@container rounded-md border bg-card p-4 sm:p-5
  <FormGrid>             → grid grid-cols-1 gap-x-4 gap-y-4 @md:grid-cols-6 @3xl:grid-cols-12
    <Field size="sm">    → col-span-full @md:col-span-3 @3xl:col-span-3
```
`@md` = 28rem (448 px) e `@3xl` = 48rem (768 px). Abaixo de 448 px fica uma coluna só, o que inclui o celular e o zoom de 200%.

**Tamanho do campo pelo conteúdo** (a largura sugere o que se espera digitar, como no GOV.UK e no Material):

| `size` | 6 trilhas (`@md`) | 12 trilhas (`@3xl`) | ~px a 896 | Campos |
|---|---|---|---|---|
| `xs` | 2 | 2 | 130 | UF, número do endereço, %, parcelas, dígito, CNH: categoria |
| `sm` | 3 | 3 | 205 | CPF (14 car.), CEP, data, telefone, dinheiro, quantidade, NCM, CEST, CBO, código/SKU, matrícula, PIS, RG |
| `md` | 3 | 4 | 280 | CNPJ (18 car.), CPF/CNPJ, select curto (status, categoria, jornada), complemento, bairro, cidade, agência/conta |
| `lg` | 6 | 6 | 425 | E-mail, nome fantasia, select longo (cargo, cliente, item com busca), senha |
| `xl` | 6 | 8 | 570 | Nome completo, razão social, logradouro, descrição curta |
| `full` | 6 | 12 | 860 | Observação, textarea, upload, `RowsEditor`, checklists, grupos de opções (`choice`) |

Linhas de referência (12 trilhas): Endereço = CEP `sm`3 · Logradouro `xl`8 · Nº `xs`… Como 3+8+2 = 13, o CEP usa 2 nessa linha (`span={2}`). Fica CEP 2 · Logradouro 8 · Nº 2 / Complemento 4 · Bairro 3 · Cidade 3 · UF 2. Identificação PJ = CNPJ 4 · Razão social 8. Pessoa = Nome 8 · Nascimento 4. `span` explícito é exceção, documentada no próprio componente.

**Espaçamento:** entre campos `gap-x-4 gap-y-4` (16 px, sobe dos 12 px de hoje). Entre seções `space-y-5` (20 px, o `gap` do `.view` do protótipo). Dentro do cartão `p-4 sm:p-5`. O título da seção fica em 15 px semibold e a descrição em `text-xs max-w-prose`.

**Rótulo e ajuda:** rótulo sempre acima do campo (já é assim no `Field`). O obrigatório continua com o texto "(obrigatório)", não com asterisco, porque é acessível e já é o padrão FND-09d. Se a maioria dos campos for obrigatória, troque por "(opcional)" nos outros e registre isso no topo do formulário. A `<Dica>` fica ao lado do rótulo, fora do `<label>`. O `hint` fica abaixo do campo, em 12 px e no máximo uma linha.

**Seções:** todas as seções de um formulário têm a mesma largura, a da coluna do formulário. É proibido envolver uma grade em outra grade (é o defeito da seção A). Os campos ficam em ordem de leitura, da esquerda para a direita, e a ordem de tabulação acompanha.

**Ações:**
| Caso | Posição | Ordem |
|---|---|---|
| Formulário curto (1 seção, cabe na tela) | No fim da coluna, `flex justify-end gap-2` | `[Cancelar]` secundário · `[Salvar X]` primário à direita (igual ao `SidePanel` e ao `ConfirmDialog`) |
| Formulário longo (2+ seções ou maior que a tela) | `<FormActions sticky>`: barra presa no rodapé **acima do dock** (`sticky bottom-[var(--dock-offset)]`, `bg-card/95 backdrop-blur border-t`), com a largura da coluna do formulário | À esquerda o estado ("Alterações não salvas" / "Salvo às 14:32"). À direita `[Descartar]` · `[Salvar X]` |
| Ação destrutiva (Excluir, Desligar) | Fora da barra, na zona de risco no fim da página ou no menu do cabeçalho | — |
Um só primário por tela, com verbo claro ("Salvar dados pessoais"). A confirmação usa o mesmo verbo ("Dados pessoais salvos").

**Painel lateral do formulário (`xl` ou mais):** de cima para baixo, Resumo (o que vai ser criado: total, parcelas, impacto no caixa ou no estoque), Próximo passo (`NextSteps`), Pendências (campos obrigatórios que faltam, cada um com link para o campo) e Ajuda da tela (atalho para o `AppHelp`). O painel fica `sticky top-24`. Abaixo de `xl`, ele vai para o fim do formulário, antes das ações, ou some se só repetir a ajuda.

**Celular (< 448 px de contêiner):** uma coluna, todo campo `col-span-full`, botões com `min-h-11` e largura total na barra de ações (primário embaixo, ao alcance do polegar).

---

## D. Componentes compartilhados

| Componente | Onde fica | Props | Reaproveita |
|---|---|---|---|
| `<Page>` | — | — | **(implementado)** Não foi criado: o `AppShell` aplica a largura pela rota (`components/layout/page-width.ts`). Se uma tela precisar de outra largura que a da rota, ela é declarada na rota |
| `<PageHeader>` | **(implementado)** O próprio `shell/PageHeading.tsx`, evoluído (sem segundo componente) | `title`, `documentTitle?`, `count?`, `description?`, `actions?`, `meta?` (chips e status), `children?` | Mantém o foco no h1 e o `document.title`. Ganha uma faixa de ações à direita e a descrição em `max-w-prose` |
| `<FormSection>` | `components/form/form.tsx` (existe) | `title`, `description?`, `grid?: 'legacy'\|'form'\|'none'`, `children` | **(implementado)** `grid="form"` põe a `FormGrid` (com `@container`); `none` não põe grade; sem `grid` fica `legacy` (a grade antiga de 2 colunas) até a tela migrar |
| `<FormGrid>` | `components/form/form.tsx` (novo) | `children`, `className?` | Grade da seção C. **(implementado)** O próprio `FormGrid` é o contêiner (`@container`), então funciona fora do `FormSection`. `FormGridItem` dá o mesmo tamanho a um item que não é `Field` |
| `<Field size span>` | `components/form/form.tsx` (existe) | Novas props `size?: 'xs'\|'sm'\|'md'\|'lg'\|'xl'\|'full'` (padrão `lg`) e `span?: {md?: n; lg?: n}` | **(implementado)** O tamanho só vale dentro da `FormGrid` (fora dela, o `Field` fica como era). A tabela de trilhas está em `components/form/field-size.ts`. O `defaultSize` nas máscaras fica para os PRs de módulo: por enquanto o `size` é passado na tela |
| `<FormActions>` | `components/form/form.tsx` (novo) | `sticky?`, `dirty?`, `status?: ReactNode`, `children` | **(implementado)** `dirty` mostra "Alterações não salvas"; o aviso de sair sem salvar continua no `UnsavedChangesGuard` da tela |
| `<FormLayout>` | **(implementado)** `components/layout/form-layout.tsx` (novo) | `aside?`, `asideLabel?`, `children` | Monta `xl:grid-cols-[minmax(0,56rem)_20rem]`; sem `aside`, só a coluna `max-w-4xl` |

Como o shell resolve a largura: o `AppShell` lê `useMatches()` e pega o `staticData.width` da rota mais profunda (padrão `'default'`). A classe do contêiner vai num wrapper em volta do `<Outlet/>`. Com isso, **o PR 0 já tira a faixa vazia de todas as telas**, e a migração por módulo só ajusta as exceções.

```tsx
// router.tsx
{ path: 'producao/quadro', component: KanbanPage, staticData: { width: 'full' } }

// NewReceivablePage.tsx
<Page width="form" aside={<ReceivableSummary draft={draft} />}>
  <PageHeader title="Nova conta a receber" description="..." />
  <form className="space-y-5">
    <FormSection title="Cliente e documento">
      <FormGrid>
        <Field label="Cliente" required size="xl">{(p) => <PartyCombobox {...p} />}</Field>
        <Field label="Emissão" required>{(p) => <DateInput {...p} />}</Field>{/* size sm vem do DateInput */}
        <Field label="Valor" required>{(p) => <DecimalInput scale={2} prefix="R$" {...p} />}</Field>
        <Field label="Observação" size="full">{(p) => <textarea {...p} />}</Field>
      </FormGrid>
    </FormSection>
    <FormActions sticky status={dirty ? 'Alterações não salvas' : null}>
      <Button variant="secondary">Cancelar</Button><Button type="submit">Lançar conta a receber</Button>
    </FormActions>
  </form>
</Page>
```
**(implementado)** No código, o `<Page width="form" aside={…}>` do exemplo é `staticData: FORM` na rota + `<FormLayout aside={…}>` na tela, e a seção usa `<FormSection grid="form">` (a `FormGrid` vem junto). O `DateInput` e o `DecimalInput` ainda não trazem o `size`: passe `size="sm"`.

Regra de lint (opcional, junto da regra de cor literal): proibir `max-w-(xl|2xl|3xl|4xl|5xl|6xl)` em `pages/**`, exceto `max-w-prose`.

---

## E. Plano de migração

| PR | Conteúdo | Telas | Esforço |
|---|---|---|---|
| **0 — Base** | `AppShell` fluido + `staticData.width`; `Page`, `PageHeader`, `FormGrid`, `Field size`, `FormActions`, `FormLayout`; `defaultSize` nas máscaras; `FormSection` sem grade fixa (temporariamente com `legacyGrid` para não quebrar as 75 seções) | 0 telas, mas todas ganham 1536 px | M |
| **0b — Correção rápida** | Remove a grade dentro da grade (9 lugares) | 7 arquivos | P (pode entrar antes do PR 0) |
| 1 — Financeiro | `full`: fluxo de caixa, DRE, aging, conciliação, extrato. `default`: contas e caixas, CR, CP, ordens, aprovações, plano de contas, centros de custo. `form`: nova CR/CP, nova ordem, condição de pagamento, abertura | ~23 | G |
| 2 — RH | `full`: revisão da folha, espelho de ponto (hoje `5xl`), paralelo. `default`: funcionários, cargos, jornadas, sindicatos, férias. `form`: novo funcionário, abas Geral/Pessoais/Documentos/Pagamento/Contrato, desligamento, exportação contadora | ~20 | G |
| 3 — Produção | `full`: Kanban, planejamento/Gantt, capacidade, qualidade/supervisor. `default`: OS (lista e detalhe), centros, calendário, instalações. `form`: modelo de instalação, centro de trabalho | ~17 | M |
| 4 — Comercial | `default`: orçamentos, funil, contratos, projetos, terceiros. `form`: aba Dados e Condições do orçamento, aditivo, cadastro de terceiro | ~13 | M |
| 5 — Configurações | Todas `form` (empresa, fiscal, cobrança, numeração, parâmetros, segurança, IA, privacidade), exceto importação e integrações (`default`) | ~17 | M |
| 6 — Estoque, Compras, Fiscal, Patrimônio, Engenharia | `full`: saldos (se ≥ 8 colunas), rastreabilidade, inventário. O resto `default`/`form` | ~59 | G (2–3 PRs) |
| 7 — Remoção | Tira o `legacyGrid`, ativa a regra de lint | — | P |

Total: **~150 telas**, um PR por módulo, na ordem acima (maior tráfego primeiro). Cada PR atualiza a ajuda das telas tocadas no mesmo commit (regra do CLAUDE.md).

**Riscos e mitigação:**
- E2E que mede layout: `fnd09e.spec.ts` (diálogo e mensagem contra o dock), `supervisor.spec.ts:221` (ordem vertical dos blocos: com mais largura, dois blocos podem dividir a linha e o teste deve aceitar `≤`), `cash-flow-adjusted.spec.ts:108`, `dashboard.spec.ts:162`, `assets`/`contracts`/`goods-receipts` (viewport de 400 px). Rodar esses E2E no PR 0 e em cada PR de módulo.
- Ajuda com posição escrita: `engenharia-roteiro-novo`, `financeiro-conciliacao`, `producao-planejamento-fila` ("à direita", "no fim"). Revisar quando o painel lateral ou a barra fixa mudarem a posição.
- A barra fixa e o dock disputam o rodapé: definir `--dock-offset` uma vez no shell e somar a altura da barra ao `scroll-padding-bottom`.
- Tabelas largas a 1920 px: linha longa demais. Destacar a linha no hover e no foco e manter o teto de 1920 px.

---

## F. Tema escuro e acessibilidade

| Item | Verificação | Como fica |
|---|---|---|
| Cores | A proposta não cria cor nova. Barra fixa `bg-card/95` + `border-border`, painel lateral `bg-card` | Sem literal, passa na regra de lint; claro/escuro/automático herdados dos tokens |
| Contraste | Status da barra em `text-muted-foreground` sobre `card` | Já validado nos tokens (≥ 4,5:1). Não usar opacidade abaixo de 95% na barra, para o texto não perder contraste sobre a rolagem |
| Foco | `:focus-visible` de 2 px `ring` (existe) | Barra e dock não podem cobrir o campo com foco (2.4.11): `scroll-padding-bottom` = dock + barra |
| Zoom 200% / reflow (1.4.10) | A 1280 px com zoom de 200% sobram 640 px CSS → contêiner < 768 → 6 trilhas. A 320 px → 1 coluna | Consulta por contêiner garante o reflow sem rolagem horizontal. Só `full` com tabela/Gantt rola dentro do próprio componente, o que é permitido para conteúdo bidimensional |
| Ordem e leitura | O painel lateral vem depois do formulário no DOM. A barra fica no `<form>` | Leitor de tela lê o formulário primeiro. O resumo usa `aria-live="polite"` só para o total |
| Alvo de toque | Botões da barra `min-h-11` (44 px) | 2.5.8 atendido. Na PWA do operador continua ≥ 56 px |
| Largura de leitura | `max-w-prose` em descrições | ≤ 80 caracteres por linha (1.4.8, boa prática) |

---

## G. Decisões do PO (02/11/2026: as três como recomendado)

1. **Largura máxima das telas "Padrão".** Opções: 1360 px (protótipo) · **1536 px (`max-w-screen-2xl`)** · sem limite.
   **Recomendo 1536 px centralizado.** Tira metade da faixa vazia em 1920 px, cabe uma tabela de 7 colunas sem truncar e não espalha os dados em monitor largo. "Sem limite" fica só para o tipo `full`, com teto de 1920 px.
2. **Formulário centralizado ou à esquerda com painel lateral.**
   **Recomendo à esquerda (896 px), alinhado ao título, com painel lateral a partir de 1280 px** (resumo, próximo passo, pendências). Centralizar recria as duas faixas vazias. O painel transforma a sobra em informação, o que segue o padrão propositivo do CLAUDE.md.
3. **Barra de ações fixa no rodapé.** Opções: sempre · **só em formulário longo (2+ seções ou maior que a tela)** · nunca.
   **Recomendo só nos longos**, acima do dock, com o estado "Alterações não salvas". Em formulário curto, a barra fixa é ruído. Em formulário longo (funcionário, empresa, nova CR/CP), evita rolar até o fim para salvar e torna visível o que ainda não foi salvo.

**Decidido:** 1) `default` em 1536 px centralizado; `full` fluido até 1920 px. 2) Formulário à esquerda (896 px), alinhado ao título, com painel de 320 px a partir de 1280 px quando a tela tem resumo, pendências ou próximo passo. 3) Barra de ações fixa só nos formulários longos, acima do dock, com "Alterações não salvas"; nos curtos, os botões ficam no fim do formulário.
