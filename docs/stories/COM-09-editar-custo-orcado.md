# COM-09 — Editar o custo orçado do projeto

| Campo | Valor |
|---|---|
| ID | COM-09 |
| Título | Edição direta do custo orçado do projeto por natureza, com permissão própria, motivo obrigatório, conferência de versão e histórico (valor anterior, novo, quem e quando) |
| Tamanho | P |
| Status | Done (15/11/2026) |
| Requisitos | Decisão do PO de 15/11/2026 · DSH-02 ("Sem orçamento de custo", PR #209) · R-SEC-5 (auditoria) · D7 (sigilo comercial) · ADR 0003 (ação específica) · ADR 0019 (versão do documento) |
| Dependências | COM-04a (orçado do projeto), COM-05 (aba Custos), COM-07 (aditivo), DSH-02 (margem) |
| Bloqueia | — |
| Ordem de execução | 1 (Pós-go-live: custo orçado do projeto) |

## User story
Como **proprietário**, quero **informar ou corrigir o custo orçado de um projeto depois que o contrato foi aprovado**, para **o projeto sair do "Sem orçamento de custo" e a margem orçada, o desvio e o painel mostrarem o número certo**.

## Contexto e referências
- O orçado mora em `commercial.project_budget`: uma linha por projeto e natureza (Material, Mão de obra, Instalação, Frete, Impostos e Outros). Nasce na aprovação do orçamento (COM-04a CA 8, da composição de custo) e só mudava por aditivo aprovado (COM-07).
- Quem lê, sempre na hora e sem cache: a aba Custos e o resumo do projeto, o relatório "Orçado × realizado", o card Margem e o resultado por projeto do painel (`ProjectMarginService`, um cálculo só), a margem da lista de projetos, o resumo em PDF e a regra "tem instalação" (orçado de Instalação maior que zero).
- Projeto que nasce de contrato sem composição de custo fica "Sem orçamento de custo" (margem orçada e desvio nulos, fora do consolidado). Antes desta story não havia tela para informar o orçado depois da aprovação.
- **Decisão do PO (15/11/2026):** edição direta no projeto, protegida por permissão e com auditoria, sem fluxo de aprovação.

## Critérios de aceite
1. **Dado** `projects.view_cost` e `projects.edit_budget`, **quando** abro a aba Custos de um projeto que não está encerrado nem cancelado, **então** vejo "Editar custo orçado" (no aviso "sem orçamento de custo", quando for o caso, e acima da tabela nos demais); sem a ação, o botão não aparece.
2. **Dado** o diálogo, **quando** abro, **então** as seis naturezas vêm com o orçado de hoje, o campo "Motivo" é obrigatório (até 500 caracteres) e o impacto mostra o custo orçado total e a margem orçada antes e depois; só confirmo com ao menos uma natureza alterada.
3. **Dado** a edição confirmada, **quando** gravo, **então** só as naturezas alteradas mudam, a aba Custos, o "Orçado × realizado" e o painel passam a usar o valor novo na hora, e o projeto com algum custo orçado sai do "Sem orçamento de custo".
4. **Dado** projeto encerrado, **quando** edito, **então** 409 `project_closed` "Projeto encerrado: o custo orçado não pode mais ser editado."; cancelado, 409 `project_cancelled`. Nada é gravado.
5. **Dado** a versão do projeto que abri, **quando** outra gravação aconteceu no meio (outra edição do orçado ou do projeto), **então** 409 `concurrency_conflict` "O projeto mudou desde que você abriu. Recarregue." e o diálogo oferece "Recarregar".
6. **Dado** as edições feitas, **quando** abro a aba Custos, **então** "Edições do custo orçado" lista cada uma, da mais recente à mais antiga, com quem, quando, o motivo e, por natureza, o valor anterior e o novo. No drill-down do Orçado, cada edição aparece como "Custo orçado editado: {motivo}" pela diferença, e a soma continua igual à célula.
7. **[Segurança]** **Dado** quem não tem `projects.edit_budget`, **quando** chama `PUT /projects/{id}/budget`, **então** 403 `forbidden`, mesmo com `projects.edit`, `projects.approve` e `projects.view_cost`; a ação sem `projects.view_cost` também recebe 403. Valor negativo, com mais de 2 casas, natureza repetida, sem motivo ou sem mudança: 422.

## Regras de negócio e técnicas
- **Trilha:** `commercial.project_budget_revisions`, uma linha por natureza alterada (valor anterior, valor novo, motivo, quem, quando), só inclusão. A tabela é a trilha com os valores porque a auditoria geral registra o orçado só como "alterado" (`[ProtectedSecret]`, D7). A auditoria geral (`audit.audit_log`) continua gravando quem, quando e o IP na linha do orçado, na edição (com o motivo) e no projeto.
- **Versão:** a edição confere a versão do projeto (`xmin`, ADR 0019) e grava `projects.budget_revised_at`, o que muda a versão; duas edições sobre a mesma versão dão 409.
- **Sem aprovação e sem evento:** nada é publicado no outbox; quem lê o orçado lê a tabela na hora.
- **Aditivo continua igual:** o aditivo aprovado soma a variação no orçado; a edição direta define o valor da natureza. As duas origens aparecem separadas no drill-down do Orçado.
- **Natureza zerada:** informar 0,00 em todas as naturezas volta o projeto a "Sem orçamento de custo" (com motivo e histórico).

## Permissões, eventos e tabelas
- **Permissão nova:** `projects.edit_budget` ("Editar custo orçado"), ação específica no `ModuleCatalog` (ADR 0003, adendo de 15/11/2026). **Nenhum seed patch a concede:** nasce só com o Proprietário (que tem tudo), único grupo do seed que aprova contrato. O grupo do seed que edita projeto (Engenharia) não vê custo, então não a recebe. O tenant concede a quem também tem "Ver custo e margem" (recomendado: Diretoria / Gerência).
- **Endpoints:** `PUT /api/v1/projects/{id}/budget` (`projects.edit_budget` na rota, `projects.view_cost` no serviço) e `GET /api/v1/projects/{id}/budget/revisions` (`projects.view_cost`).
- **Eventos:** nenhum.
- **Tabelas:** `commercial.project_budget_revisions` (nova) e a coluna `commercial.projects.budget_revised_at`; migration `ProjectBudgetRevisionsCom09` (só acrescenta).

## Telas e estados
- **Projeto → Custos:** botão "Editar custo orçado"; diálogo com os seis valores, o motivo e o impacto; seção "Edições do custo orçado" (carregando, erro com "Tentar de novo" e vazio: "Nenhuma edição: o custo orçado é o do orçamento aprovado e dos aditivos.").
- **Ajuda:** `projetos-projeto-custos.md` atualizada.

## Plano de testes
- **Integração (Testcontainers), `ProjectBudgetEditTests`:** a edição tira o projeto do "sem orçamento de custo", muda a versão, grava a trilha, fecha o drill-down e audita sem os valores (CA 3, 5, 6); sem a ação, sem ver custo, encerrado, versão antiga e sem motivo são recusados e nada é gravado (CA 4, 5, 7).
- **Vitest:** `budget.test.ts` (só as naturezas alteradas vão para a API; margem orçada do impacto).
- Acesso por rota e cobertura da ajuda ficam com as varreduras.

## Fora do escopo
- Fluxo de aprovação da edição e 2FA por ação (decisão do PO: edição direta).
- Editar a receita contratada (continua por aditivo).
- Recalcular o orçado a partir do orçamento de origem.

## Pontos em aberto
1. **Quem recebe a ação por padrão (aguarda PO):** hoje só o Proprietário. Se o Financeiro ou o Comercial do seed deverem editar, basta um seed patch de uma linha.
2. **Ação reservada (aguarda PO):** `projects.edit_budget` não entrou na lista de ações que só o Proprietário concede (ADR 0008). Vale a regra geral (ninguém concede o que não tem), e a edição exige `projects.view_cost`, que é reservada.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 15/11/2026 | 1.0 | Story escrita e implementada a partir da decisão do PO de 15/11/2026 (edição direta, com permissão e auditoria, sem aprovação). | Claude (dev) |
