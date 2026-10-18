# FND-09d — Componentes de formulário e moldes de tela dos cadastros

| Campo | Valor |
|---|---|
| ID | FND-09d |
| Título | Três moldes de tela aprovados pelo PO (cadastro em seções, detalhe com abas na URL, painel lateral) e os componentes de formulário que os cadastros da Fase 1 usam |
| Tamanho | G (nova, da decisão D7 da revisão pré-Fase 1: 3 moldes e cerca de 20 componentes, com acessibilidade e 400 px) |
| Status | Em andamento (aguarda a aprovação dos moldes pelo PO, CA 1 e 2) |
| Requisitos | PRD §6 Usabilidade (responsivo, 400 px) · R-PRP-1 (padrões preenchidos) · R-PRP-2 (próximo passo) · R-PRP-3 (nada digitado duas vezes) · R-AJD-4 (`Dica`) |
| Dependências | FND-09a, FND-09b e FND-09c (concluídas no código). **Pré-condição explícita:** o PO valida a FND-09a e a FND-09b (validação visual pendente) no início desta story (CA 1) |
| Bloqueia | Todas as stories de tela da Fase 1 (CAD-01a, CAD-01b, CAD-05b, CAD-06, CAD-03, CAD-02a, CAD-02b, CAD-05a, CAD-04a, CAD-04b) e a tela da importação (MIG-01a) |
| Ordem de execução | Fase 0, antes do Marco A da Fase 1 |

## User story
Como **desenvolvedor das telas de cadastro**, quero **moldes de tela aprovados e componentes de formulário prontos, acessíveis e no padrão brasileiro**, para **que os 10 cadastros da Fase 1 tenham o mesmo comportamento, sem cada story reinventar máscara, número, combobox ou árvore**.

## Contexto e referências
- Revisão pré-Fase 1, D7 e §2.4 ("Padrões comuns a todos os cadastros", lista que está em `docs/stories/README.md`).
- `CLAUDE.md` → Interface: tokens sem cor literal, `tabular-nums`, estados de carregando/vazio/erro, verbo claro e confirmação com o mesmo verbo, propositivo, `Dica`, 400 px.
- Protótipo `docs/prototipo/prototipo-erp-konebras.html` (padrão de listagem e de detalhe com abas de Usuários e acessos).
- Código existente: `DataTable`, `Field`, `useZodForm`, `applyServerErrors`, `UnsavedChangesGuard`, `ConfirmDialog`, `toastDone`, `StatusChip`, `Money`, `StageIndicator`, `RecordHistory`, `Dica`, `useHelpTab`, `SettingsTabs`, Radix (Dialog, Popover, ToggleGroup), cmdk (FND-09a, 09b, 09c, 14).
- ADR 0009 (bibliotecas do front), ADR 0013 (ajuda por aba).

## Critérios de aceite
**Pré-condição e moldes**
1. **Dado** a FND-09a e a FND-09b prontas no código, **quando** esta story começa, **então** o PO registra a validação visual das duas na seção "Dev Agent Record" delas (passo manual verificável), e a story só segue depois disso.
2. **Dado** a galeria de desenvolvimento, **quando** o PO abre os três moldes com dados de exemplo, **então** vê:
   - **Cadastro em seções:** página única de criação, com seções (`fieldset`/`legend`) só com os campos obrigatórios e os que comandam o resto; ações principais visíveis sem rolar;
   - **Detalhe com abas:** cabeçalho com nome, chips de situação e ações; abas ligadas à URL (`?aba=`) e a `useHelpTab`; "Histórico de alterações" sempre como última aba; faixa "Próximos passos" depois de criar;
   - **Painel lateral:** `Sheet` para cadastro simples (até cerca de 6 campos), com guarda de alteração não salva ao fechar;
   e registra a aprovação dos três (ou os ajustes pedidos) no "Dev Agent Record" desta story.

**Entradas com máscara**
3. **Dado** os campos com máscara, **quando** digito ou colo um valor, **então** a máscara aparece na digitação e o valor entregue ao formulário é normalizado (sem máscara):
   - CNPJ numérico e alfanumérico (IN RFB 2.229/2024, letras maiúsculas nas 12 primeiras posições), CPF e o campo "CPF ou CNPJ" que infere PF/PJ pela quantidade de caracteres;
   - CEP (`99999-999`), telefone fixo e celular (`(99) 9999-9999` e `(99) 99999-9999`), NCM (`9999.99.99`) e CBO (`9999-99`).
4. **Dado** um valor colado com máscara, espaços ou pontuação diferente, **quando** o campo recebe, **então** normaliza sem perder dígitos e sem duplicar a máscara.

**Números no padrão brasileiro**
5. **Dado** os campos `MoneyInput`, `PercentInput` e `QuantityInput`, **quando** digito "1.234,56", **então** o valor é `1234.56` (decimal, nunca `number` com perda); as casas são as do campo (dinheiro 2, % 2 ou 4, quantidade 4); a exibição usa `tabular-nums`; vírgula e ponto digitados fora do lugar são tratados ("1234,5" vira 1.234,50).
6. **Dado** um campo numérico com faixa (ex.: 0 a 200%), **quando** o valor sai da faixa, **então** a mensagem PT-BR aparece no campo, sem travar a digitação.

**Seleção**
7. **Dado** o `Combobox`, **quando** digito, **então** a busca vai ao servidor com debounce, mostra carregando, vazio e erro, e navega por teclado (setas, Enter, Esc); com `onCreate`, a última opção é "Criar '{termo}'", que abre a criação rápida num `Sheet` e, ao salvar, seleciona o novo registro.
8. **Dado** o `ChipGroup` (seleção múltipla, ex.: papéis do terceiro), **quando** uso pelo teclado, **então** ele é um `fieldset` com `legend`, cada chip é um checkbox com rótulo e estado anunciado, e há mensagem de erro de grupo ("Escolha ao menos um papel.").
9. **Dado** o `ToggleGroup` (escolha única, ex.: PF/PJ, Markup/Margem), **quando** uso, **então** funciona como grupo de rádio (setas trocam a opção).

**Estrutura**
10. **Dado** abas ligadas à URL, **quando** troco de aba, **então** a URL muda, o voltar do navegador volta à aba anterior, a ajuda segue a aba (`useHelpTab`), e cada aba mostra um contador de erros quando tem campo inválido; ao enviar, o foco vai ao primeiro erro, mesmo em aba escondida (a aba é aberta).
11. **Dado** o `TreeTable`, **quando** mostro uma árvore (plano de contas, categorias), **então** ela usa `role="treegrid"` com `aria-level`, `aria-expanded` e navegação por setas (direita expande, esquerda recolhe), com busca que mostra o caminho dos pais.
12. **Dado** o `RowsEditor` (linhas repetidas: parcelas, turnos, dias da jornada), **quando** adiciono, removo ou reordeno linhas, **então** cada botão de remover tem o rótulo "Remover {linha n}", o total ao vivo fica num `aria-live="polite"` e, a 400 px, as colunas viram linhas empilhadas.
13. **Dado** os campos de data e hora, **quando** digito ou escolho, **então** a data aceita `dd/mm/aaaa` e entrega `aaaa-mm-dd` (`date`, sem fuso); a hora aceita `hh:mm`; data com hora é exibida em America/Sao_Paulo.

**Mensagens e estados**
14. **Dado** o `Field` com `warning`, **quando** a regra é um aviso (ex.: "Saldo inicial negativo."), **então** a mensagem aparece em âmbar, com ícone e `aria-describedby`, e não bloqueia o envio.
15. **Dado** um campo obrigatório, **quando** renderizado, **então** o rótulo mostra "obrigatório" em texto e o controle tem `aria-required="true"`.
16. **Dado** a faixa `NextSteps`, **quando** um registro é criado, **então** o detalhe mostra uma faixa fixa com até 3 ações de próximo passo; o toast só confirma o verbo ("Terceiro salvo").
17. **Dado** o `ConfirmDialog` com `recovery`, **quando** o servidor recusa a ação (ex.: 409 "Conta com lançamentos não pode ser excluída."), **então** o diálogo mostra o motivo e oferece a ação alternativa ("Inativar", "Restaurar"); o botão de sair diz "Voltar".
18. **Dado** o componente `Tag` (atributo neutro, ex.: "Sem documento", "Importado do Dolibarr") e o tom `neutral` do `StatusChip` (situação "Inativo"), **quando** renderizados, **então** usam tokens neutros, nunca o vermelho reservado ao crítico, e passam no contraste AA nos dois temas.
19. **Dado** o `DataTable`, **quando** uso filtros, **então** os filtros ficam na URL, o filtro "Inativos" existe como padrão, o vazio filtrado mostra "Nenhum resultado para os filtros" com "Limpar filtros", e a primeira coluna é um link para o detalhe (Enter abre).
20. **Dado** a `Dica`, **quando** renderizada ao lado de um campo, **então** o alvo tem no mínimo 24 × 24 px e `aria-label="Dica: {nome do campo}"`.
21. **Dado** um `Sheet` com alteração não salva, **quando** tento fechar (Esc, clique fora ou "Fechar"), **então** a guarda pergunta "Salvar" ou "Descartar", como o `UnsavedChangesGuard` das páginas.

**Teclado e 400 px**
22. **Dado** os três moldes, **quando** navego só pelo teclado, **então** toda ação é alcançável, a ordem do foco segue a leitura, o foco é visível e o `Sheet` prende e devolve o foco.
23. **Dado** os três moldes a 400 px de largura, **quando** renderizados, **então** o `Sheet` vira tela cheia, as abas rolam na horizontal, os editores de linha empilham, as ações principais ficam visíveis sem rolar e não há rolagem horizontal da página.

## Regras de negócio e técnicas
- Componentes em `src/Web/src/components/form/` e `ui/`, com RHF + Zod. Os valores decimais circulam como string normalizada ("1234.56") até o cliente Orval, para não perder precisão em `number`.
- Os validadores de dígito de CPF e CNPJ do front espelham o `BrazilianDocuments` do backend (CAD-01a), com os mesmos casos de teste.
- Nenhuma cor literal (lint da FND-09a). Ícone nunca é o único portador de significado.
- A galeria de desenvolvimento ganha uma página por molde e uma por componente, nos estados vazio, preenchido, erro, aviso e desabilitado.

## Permissões, eventos e tabelas
- Nenhuma permissão, evento ou tabela. Os moldes usam `<Can>` já existente.

## Telas e estados
- Sem tela de negócio. Galeria de desenvolvimento com os três moldes e os componentes nos estados carregando, vazio, erro e modo leitura.
- **Ajuda:** não se aplica (componentes). A galeria não é rota do app.

## Plano de testes
- **Unidade (Vitest + Testing Library):** máscaras e normalização (CA 3, 4); números (CA 5, 6); `Combobox` com teclado e criação rápida (CA 7); `ChipGroup` e `ToggleGroup` (CA 8, 9); abas com contador e foco no primeiro erro (CA 10); `TreeTable` com `treegrid` e setas (CA 11); `RowsEditor` (CA 12); data e hora (CA 13); `warning`, obrigatório, `NextSteps`, `ConfirmDialog` com `recovery`, `Tag` e `Dica` (CA 14–18, 20); guarda do `Sheet` (CA 21).
- **Acessibilidade:** axe sem violação nos três moldes e em cada componente.
- **E2E (Playwright):** filtros na URL e vazio filtrado (CA 19); teclado nos três moldes (CA 22); 400 px (CA 23).
- **Manual:** validação da FND-09a/09b e aprovação dos moldes pelo PO (CA 1, 2).

## Fora do escopo
- Telas de negócio (cada story de cadastro).
- Consulta de CNPJ e de CEP (CAD-01b).
- Upload de arquivo além do já existente.

## Pontos em aberto
- Nenhum bloqueante. A aprovação dos moldes é CA (CA 2): ajustes pedidos pelo PO entram nesta story.

## Definition of Done
- [ ] Critérios de aceite atendidos, com a validação da FND-09a/09b e a aprovação dos moldes registradas.
- [ ] Testes de unidade, acessibilidade e E2E passando.
- [ ] Migration: não se aplica.
- [ ] Permissão: não se aplica.
- [ ] Componentes com estados de carregando, vazio, erro e modo leitura na galeria.
- [ ] Ajuda: não se aplica.
- [ ] Padrões e próximo passo propositivos: `NextSteps`, criação rápida e `recovery` disponíveis.
- [ ] Auditoria: não se aplica.
- [ ] `docs/stories/README.md` (padrões comuns) conferido com os componentes entregues; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Moldes `CreatePage`, `DetailPage` (abas na URL, `NextSteps`) e `SidePanel` (`Sheet` com guarda).
- [ ] Máscaras e `DocumentInput`, `CepInput`, `PhoneInput`, `NcmInput`, `CboInput`.
- [ ] `MoneyInput`, `PercentInput`, `QuantityInput`; `DateInput`, `TimeInput`.
- [ ] `Combobox` com criação rápida, `ChipGroup`, `ToggleGroup`, `TreeTable`, `RowsEditor`.
- [ ] `Field` com `warning` e obrigatório; `ConfirmDialog` com `recovery`; `Tag`; `StatusChip` neutro; `Dica` com alvo de 24 px.
- [ ] `DataTable` com filtros na URL, "Inativos", vazio filtrado e primeira coluna como link.
- [ ] Galeria e testes.

## Quality gates
- Agentes previstos: @dev, @ux-design-expert (design-squad), @qa.
- Gate: CA 2 (moldes aprovados), CA 22 (teclado) e CA 23 (400 px) obrigatórios.

## Validação do PO
Checklist do @po (Pax), 18/10/2026.

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo (cabeçalho, user story, contexto, CAs, regras, permissões/tabelas, telas, testes, fora do escopo, pontos em aberto, DoD, tarefas) | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas do CLAUDE.md) que se aplicam | N/A (componentes, sem endpoint) |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK |
| 7 | Plano de testes cobre os CAs (unidade, integração com Testcontainers, arquitetura, E2E quando cabe) | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas como proposta) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD do CLAUDE.md ajustado; pontos em aberto registrados sem decidir | OK |

- **Correções feitas na validação:** —
- **Pré-condições para `Ready`:** nenhuma bloqueante. A validação da FND-09a/09b e a aprovação dos moldes são passos da própria story (CA 1 e 2).
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Dev Agent Record
**Etapa 1 (18/10/2026): componentes e moldes prontos para a aprovação do PO**
- **Moldes** em `/app/dev/moldes/{cadastro,detalhe,painel}` e componentes em `/app/dev/componentes`, dentro do shell.
  - Só existem em desenvolvimento e no build da homologação (`VITE_GALERIA=true`, build arg do `web.Dockerfile`, ligado pelo `deploy.yml` só no environment `homolog`). Nunca entram em produção.
- **Componentes:**
  - campos: `MaskedInput` e variantes (`DocumentInput` com CNPJ alfanumérico, `CpfInput`, `CnpjInput`, `CepInput`, `PhoneInput`, `NcmInput`, `CboInput`), `DecimalInput` (`MoneyInput`, `PercentInput`, `QuantityInput`, com string normalizada e sem `float`), `DateInput`, `TimeInput`;
  - seleção: `ToggleGroup` (grupo de rádio), `ChipGroup`, `Combobox` (busca no servidor com debounce e "Criar '{termo}'");
  - estrutura: `SidePanel` (com guarda de alteração não salva), `UrlTabs` + `useUrlTab` (abas em `?aba=`, contador de erros, ajuda por aba), `TreeTable` (`treegrid`), `RowsEditor`;
  - mensagens e estados: `Field` com `required`, `warning` e `dica`, `FormSection`, `NextSteps`, `ConfirmDialog` com `recovery`, `Tag`, tom `neutral` no `StatusChip`, `Dica` com alvo de 24 px e nome do campo;
  - `DataTable` com filtros na URL, "Mostrar inativos", vazio filtrado e primeira coluna como link.
- **Bibliotecas** (`src/lib`): `masks.ts`, com CPF e CNPJ numérico e alfanumérico, validado com o exemplo oficial da Receita `12.ABC.345/01DE-35`; o validador antigo `lib/cnpj.ts` foi substituído. Também `decimal.ts` e `text.ts`.
- **Acessibilidade corrigida pelos testes:**
  - a `Dica` saiu de dentro do `<label>`, porque entrava no nome acessível do campo;
  - "(obrigatório)" ganhou espaço real no nome acessível.
- **Testes:**
  - Vitest: máscaras, documentos e números (5) e componentes (8);
  - E2E `e2e/molds.spec.ts`: axe e ausência de rolagem horizontal nos 4 moldes, desktop e 400 px; documento → PF/PJ; criação rápida no combobox; erro em aba escondida com contador e foco; excluir recusado → inativar; filtro de inativos na URL; guarda do painel.
- **Pendente (CA 1 e 2):** o PO validar a FND-09a e a FND-09b e aprovar os três moldes em `https://homolog.konebras.com.br/app/dev/moldes/cadastro`. Os ajustes pedidos entram nesta story.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 18/10/2026 | 0.1 | Story criada pela decisão D7 da revisão pré-Fase 1 | River (SM) |
| 18/10/2026 | 0.2 | Validação do PO (checklist): `Ready` | Pax (PO) |
| 18/10/2026 | 0.3 | Etapa 1: componentes e moldes na galeria da homologação | @dev |
