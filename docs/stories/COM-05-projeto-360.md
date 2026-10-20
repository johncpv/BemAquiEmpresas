# COM-05 — Projeto 360

| Campo | Valor |
|---|---|
| ID | COM-05 |
| Título | Lista de projetos e tela do projeto com abas: Resumo, Itens e OS, Financeiro (a receber e a pagar do projeto), Custos (orçado, realizado, projetado e desvio por natureza), Documentos, Linha do tempo e Histórico; edição de nome, responsável e endereço da obra; custo e margem só com `projects.view_cost` |
| Tamanho | M |
| Status | Done |
| Requisitos | R-PRJ-1 (projeto com contrato, cliente, obra, data prometida, responsável) · R-PRJ-4 (visão 360: OS, recebíveis, custos lançados, documentos, linha do tempo) · R-PRJ-2 (itens) · R-PRJ-5 (parte: orçado por natureza e margem prevista; o realizado completo é a DSH-02) · R-COM-3 · R-PRP-2 · R-PRP-5 |
| Dependências | COM-04a (projeto, itens, orçamento, `cost_type` no título a pagar, `IProjects`), COM-04b (`IReceivables`), FIN-02 (`IPayables.ByProjectAsync`), FND-09e |
| Bloqueia | COM-06 (aba Itens e OS), COM-07 (ação Aditivo), DSH-02, PLN-02 |
| Ordem de execução | 15 (Marco 2.3 "Contrato ao caixa") |

## User story
Como **proprietário**, quero **abrir um projeto e ver numa tela só os itens, as OS, o que já recebi e falta receber, o que já foi gasto contra o orçado, os documentos e o que aconteceu**, para **saber como o projeto está sem perguntar a ninguém**.

## Contexto e referências
- PRD R-PRJ-1, R-PRJ-4, R-PRJ-5; §1.1 problema 1 (margem real por projeto).
- `04-fluxos-estados` §1 "Projeto" (`Planned → InProgress → Fabricated → Installing → Delivered → Closed`, `Cancelled`) e §3.3 (orçado × realizado; margem projetada = receita − (realizado + restante), restante nunca negativo).
- Protótipo: tela "Projetos" (lista com indicador de etapa e atraso; detalhe com contratado, entrega prometida, entrega prevista, margem orçada → projetada, tabela por natureza com **Orçado, Realizado, Projetado e Desvio**, parcelas do contrato e OS). Nesta fase, o realizado só tem as contas a pagar do projeto (material e horas chegam na Fase 3) e a previsão de entrega vem na PLN-01.
- **D7 (PO, 20/10/2026):** orçado, custo e margem do projeto só com `projects.view_cost` (Proprietário, Comercial e Financeiro no seed); Engenharia e Supervisão de produção não veem.
- Revisão pré-Fase 2 §2.3 (nomes alinhados ao protótipo e colunas da lista; skeleton por aba; aba sem permissão some; 400 px); §2.1-M9 (consultas em lote).
- CLAUDE.md: o indicador de etapa (estilo display de andar) é o componente padrão de etapa do projeto em todas as telas (lista de etapas definida na COM-04a CA 17).

## Critérios de aceite
**Lista**
1. **Dado** `projects.view`, **quando** abro "Projetos", **então** vejo número (link), nome, cliente, Etapa (`StageIndicator`, COM-04a CA 17), Entrega (data prometida, com o atraso em dias quando passou e o projeto não está `Delivered`), Contratado, responsável e situação; com `projects.view_cost`, a coluna Margem (orçada); com `finance.view`, a coluna Recebido (% do contratado); filtros na URL por situação, cliente e responsável; cancelado aparece com chip neutro e atrasado com faixa lateral vermelha.
2. **Dado** 50 mil projetos, **quando** abro a lista, **então** carrega em menos de 1 s (paginação server-side, nomes de cliente e responsável resolvidos por página com `IParties.FindManyAsync` e `IUserDirectory`).

**Resumo**
3. **Dado** um projeto, **quando** abro a aba Resumo, **então** vejo contratado (valor do contrato, mais aditivos aprovados quando a COM-07 existir), data prometida, "Entrega prevista: calculada a partir da Fase 4" (PLN-01), e o contrato e o orçamento de origem com link; com `finance.view`, recebido e a receber; com `projects.view_cost`, margem orçada (do contrato) e margem projetada pela fórmula do `04-fluxos` §3.3 com o realizado disponível.
4. **Dado** `projects.edit`, **quando** edito nome, responsável (usuário ativo do tenant) e endereço da obra (endereço do tipo Obra do cliente) e clico "Salvar projeto", **então** grava com auditoria; data prometida e valor não são editáveis aqui (só por aditivo, COM-07); conflito de versão responde 409 com "Recarregar".

**Itens e OS**
5. **Dado** a aba "Itens e OS", **quando** abro, **então** cada item do projeto mostra descrição, quantidade e as OS ligadas (instalação na COM-06; fabricação a partir da Fase 3) com número, tipo, etapa e situação; sem OS, "Ainda sem OS".

**Financeiro**
6. **Dado** `finance.view`, **quando** abro a aba Financeiro, **então** vejo os títulos a receber do projeto (marco, número, vencimento, valor, saldo, situação; `IReceivables.ByProjectAsync`) e os títulos a pagar com o projeto (`IPayables.ByProjectAsync`: fornecedor, origem, natureza, vencimento, valor, situação), com os totais recebido, a receber, pago e a pagar; cada linha leva ao título.
7. **[Segurança]** **Dado** um usuário com `projects.view` sem `finance.view`, **quando** abre o projeto, **então** a aba Financeiro não aparece e a API dos títulos por projeto responde 403 `forbidden`; o Resumo mostra só o valor contratado.

**Custos**
8. **Dado** `projects.view_cost` e a aba Custos, **quando** abro, **então** a tabela tem uma linha por natureza (Material, Mão de obra, Instalação, Frete, Impostos, Outros) com **Orçado** (`project_budget`), **Realizado** (títulos a pagar do projeto não cancelados, pela natureza do título), **Projetado** (realizado + restante, com restante = orçado − realizado, nunca negativo) e **Desvio** (projetado − orçado, em valor e %), e o total; a legenda diz "Material e horas entram com a produção (Fase 3)."
9. **Dado** um título a pagar com projeto e natureza Instalação de R$ 8.000,00 e orçado de instalação R$ 10.000,00, **quando** abro Custos, **então** a linha mostra Realizado R$ 8.000,00, Projetado R$ 10.000,00 e Desvio R$ 0,00 (0,0%); com um segundo título de R$ 3.000,00 na mesma natureza, Realizado R$ 11.000,00, Projetado R$ 11.000,00 e Desvio +R$ 1.000,00 (+10,0%); a margem projetada do Resumo usa esses valores.
10. **[Segurança]** **Dado** um usuário com `projects.view` sem `projects.view_cost` (ex.: Engenharia do seed), **quando** abre o projeto, **então** a aba Custos não aparece, o Resumo e a lista não mostram margem nem orçado, e a API de custos responde 403; os DTOs dele não têm propriedades de custo (teste de arquitetura, ADR 0019 §20).

**Documentos e linha do tempo**
11. **Dado** a aba Documentos, **quando** abro, **então** vejo os anexos do projeto (dono `Project`, upload com `projects.edit` por "Escolher arquivo" ou arrastando), o contrato assinado (COM-04a) e os PDFs do orçamento aprovado (COM-03), cada um com quem e quando; o download segue a permissão do dono de cada anexo.
12. **Dado** a aba Linha do tempo, **quando** abro, **então** vejo em ordem cronológica: orçamento aprovado, contrato assinado, parcelas geradas e liberadas, fabricação e entrega registradas, OS criadas e aceitas, aditivos e cancelamento, cada um com data, quem (ou "Sistema") e link; eventos financeiros só aparecem para quem tem `finance.view`.

**Permissões e segurança**
13. **Dado** o módulo `projects` não liberado, **quando** chamo, **então** 403 `module_not_released` e a tela `ModuleNotReleased`; sem grupo, 403 `forbidden`; sem `projects.edit`, a edição e o upload ficam ocultos e respondem 403. `RequireModule` e `perm:`; IDOR em `/projects/{id}/attachments/{attId}` e `/projects/{id}/items/{itemId}`.
14. **Dado** qualquer alteração no projeto, **quando** abro "Histórico de alterações", **então** vejo antes/depois, usuário e data.

## Regras de negócio e técnicas
- **Natureza no título a pagar:** `payables.cost_type` criado na COM-04a (seção 1.1, item 26); esta story só lê.
- **Projetado** = realizado + restante; **restante** = max(orçado − realizado, 0) por natureza; **margem projetada** = (receita contratada − Σ projetado) ÷ receita contratada (`04-fluxos` §3.3).
- **Linha do tempo** montada no servidor a partir das tabelas do Commercial e dos contratos `IReceivables`/`IPayables`/`IInstallationOrders` (sem ler tabela de outro módulo), paginada.
- **DTOs por permissão:** resumo e lista sem custo; `ProjectCostDto` só com `projects.view_cost`.
- Dinheiro `numeric(18,2)`; datas `date`; auditoria `IAuditable` no projeto; concorrência pelo `xmin`.

## Permissões, eventos e tabelas
- **Permissões (no `ModuleCatalog`):** `projects.view`, `projects.edit`, `projects.view_cost` (custos e margem); `finance.view` para a aba Financeiro, o recebido e os eventos financeiros da linha do tempo.
- **Eventos:** nenhum novo.
- **Contratos usados:** `IReceivables.ByProjectAsync` (COM-04b), `IPayables.ByProjectAsync` (FIN-02), `IParties.FindManyAsync`, `IUserDirectory.DisplayNameAsync`.
- **Tabelas:** nenhuma nova; anexo `Project` no `AttachmentOwnerRegistry` (leitura `projects.view`, escrita `projects.edit`, tipos e tamanho da ADR 0019 §7, `OwnerExists`).

## Telas e estados
- **Comercial → Projetos** (`/app/projetos`; entrada no `modules.ts` na área Comercial): h1 "Cada contrato vira um projeto. Todo custo lançado cai aqui." (protótipo).
- **Projeto** (`/app/projetos/$id`): cabeçalho com número, chip, cliente e `StageIndicator`; resumo em "facts" (4 → 2 colunas a 400 px); abas na URL: Resumo (padrão), Itens e OS, Financeiro, Custos, Documentos, Linha do tempo, Histórico de alterações (última); a aba sem permissão some.
- **Ações no cabeçalho:** "Registrar fabricação concluída" e "Registrar entrega" (COM-04b), "Nova OS de instalação" (COM-06), "Aditivo" (COM-07), "Documentos".
- **Estados:** carregando (skeleton por aba); vazio da lista ("Os projetos nascem quando um orçamento é aprovado." + "Ver orçamentos enviados"); vazio de custos realizados ("Nenhum custo lançado ainda."); erro por aba com "Tentar de novo"; modo leitura.
- **Propositivo:** a margem projetada abaixo da orçada mostra o aviso "Margem projetada {x}% abaixo da orçada." com link para Custos; parcela vencida no Financeiro leva ao título; projeto atrasado mostra "Entrega prometida passou há {n} dias.".
- **400 px:** a lista mostra número, cliente, Etapa, Entrega e situação (Contratado, Margem, Recebido e responsável na linha expansível); abas com rolagem horizontal; tabela de custos em cartões por natureza.
- **Ajuda:** `Ajuda/projetos.md` (lista) e `Ajuda/projetos-projeto.md` (abas `resumo` com `padrao: true`, `itens`, `financeiro`, `custos`, `documentos`, `linha-do-tempo`, `historico`); `<Dica>` em "Margem projetada", "Realizado", "Projetado" e "Desvio".

## Plano de testes
- **Unidade:** projetado, restante nunca negativo, desvio e margem projetada (CA 3, 8, 9).
- **Integração (Testcontainers):** lista com colunas por permissão e desempenho (CA 1, 2); edição e concorrência (CA 4); títulos por projeto com e sem `finance.view` (CA 6, 7); custos por natureza com dataset fixo (CA 8, 9); sem `projects.view_cost` (CA 10); documentos com permissão do dono (CA 11); linha do tempo filtrada por permissão (CA 12); três camadas e IDOR (CA 13); auditoria (CA 14).
- **Arquitetura:** DTOs sem custo para quem não tem `projects.view_cost`.
- **E2E (Playwright):** abrir projeto aprovado, navegar pelas abas pela URL, lançar um CP com projeto e ver em Custos; usuário da Engenharia sem a aba Custos; lista a 400 px.

## Fora do escopo
- Orçado × realizado completo com material, horas e impostos, e ranking de margem: DSH-02 (Fase 4).
- Data prevista de entrega e Gantt: PLN-01 e PLN-03 (Fase 4). Fila de prioridade: PLN-02.
- Garantia por item entregue (R-PRJ-7, D): depois da PRD-06 (série do produto).
- Histórico comercial e financeiro do terceiro (R-TER-3): FIN-06b.

## Pontos em aberto
Decididos pelo PO em 20/10/2026 (seção 1.1, itens 19 e 26, e D7):
1. Natureza do custo no título a pagar: criada na COM-04a, obrigatória com projeto.
2. Quem vê a margem e o custo do projeto: `projects.view_cost` (D7).
3. P-18 decidida (ADR 0021).

## Definition of Done
- [x] Critérios de aceite atendidos.
- [x] Testes de unidade, integração (Testcontainers), arquitetura e E2E passando.
- [x] Sem migration nova (só leitura e o dono de anexo), salvo índice de apoio à lista.
- [x] Permissões só do `ModuleCatalog`; dono de anexo `Project` registrado.
- [x] Telas com estados por aba.
- [x] Ajuda da lista e de cada aba; teste de cobertura passando.
- [x] Alertas acionáveis aplicados.
- [x] Auditoria ligada.
- [x] OpenAPI e cliente Orval regenerados.
- [x] `docs/03-modelo-dados.md` conferido; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Consultas da lista, do resumo, dos custos e da linha do tempo, com DTOs por permissão.
- [ ] Endpoints `/api/v1/projects` (+ `/{id}/summary`, `/costs`, `/timeline`, `/attachments`); telas e ajuda.

## Quality gates
- Agentes previstos: @dev, design (tela 360), @qa.
- Gate: CA 3, 7, 9 e 10 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 20/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fase 2).

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

- **Correções feitas na validação (19/10/2026):** todos os CAs novos (o backlog não tinha); aba Financeiro só com `finance.view`; realizado limitado às contas a pagar nesta fase; R-TER-3 movido para a FIN-06; data prevista deixada para a PLN-01.
- **Correções da revalidação (20/10/2026):** custo e margem só com `projects.view_cost` (D7, CA 1, 3, 8, 10); nomes do protótipo (Orçado, Realizado, Projetado, Desvio) e colunas da lista (Etapa, Entrega, Contratado, Margem, Recebido, atraso) (§2.3, CA 1, 8, 9); o `cost_type` e o campo no título a pagar foram para a COM-04a (item 26); nomes em lote (M9); skeleton por aba e aba sem permissão some; 400 px. CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma pendente.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 19/10/2026 | 0.1 | Story detalhada | River (SM) |
| 19/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 20/10/2026 | 0.3 | Decisões do PO (D7, seção 1.1) e correções da revisão pré-Fase 2 (§2.1-M9, §2.3); CAs renumerados | River (SM) |
| 20/10/2026 | 1.0 | Implementada. Desvios: margem e recebido da lista em rotas próprias (`/projects/margins` e `/projects/received`), como os custos da lista de orçamentos; o recebido é calculado por projeto da página; a aba Itens mostra "Ainda sem OS" até a COM-06; os PDFs do orçamento entram em Documentos quando a COM-03 for ao ar; a linha do tempo usa as datas gravadas (aprovação, assinatura, fabricação, entrega, cancelamento, conclusão, parcelas e títulos a pagar), sem a liberação de cada parcela; o menu "Contratos e projetos" volta a abrir `/app/projetos`, com "Ver contratos" no cabeçalho; `IPayables.ByProjectAsync` novo no `Finance.Contracts` | @dev |
| 20/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
