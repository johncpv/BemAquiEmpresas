# COM-05 — Projeto 360

| Campo | Valor |
|---|---|
| ID | COM-05 |
| Título | Lista de projetos e tela do projeto com abas: Resumo, Itens e OS, Financeiro (a receber e a pagar do projeto), Custos (orçado por natureza e o que já foi lançado), Documentos, Linha do tempo e Histórico; edição de nome, responsável e endereço da obra |
| Tamanho | M |
| Status | Draft |
| Requisitos | R-PRJ-1 (projeto com contrato, cliente, obra, data prometida, responsável) · R-PRJ-4 (visão 360: OS, recebíveis, custos lançados, documentos, linha do tempo) · R-PRJ-2 (itens) · R-PRJ-5 (parte: orçado por natureza e margem prevista; o realizado completo é a DSH-02) · R-COM-3 · R-PRP-2 · R-PRP-5 |
| Dependências | COM-04a (projeto, itens, orçamento), COM-04b (`IReceivables`), FIN-02 (`IPayables.ByProjectAsync`, títulos com projeto) |
| Bloqueia | COM-06 (aba Itens e OS), COM-07 (ação Aditivo), DSH-02, PLN-02 |
| Ordem de execução | 12 (Marco 2.3 — do contrato ao caixa) |

## User story
Como **proprietário**, quero **abrir um projeto e ver numa tela só os itens, as OS, o que já recebi e falta receber, o que já foi gasto contra o orçado, os documentos e o que aconteceu**, para **saber como o projeto está sem perguntar a ninguém**.

## Contexto e referências
- PRD R-PRJ-1, R-PRJ-4, R-PRJ-5; §1.1 problema 1 (margem real por projeto).
- `04-fluxos-estados` §1 "Projeto" (`Planned → InProgress → Fabricated → Installing → Delivered → Closed`, `Cancelled`) e §3.3 (orçado × realizado: realizado de material, mão de obra, instalação, frete e outros, impostos).
- Protótipo: tela "Projetos" (lista com indicador de etapa e atraso; detalhe com contratado, entrega prometida, entrega prevista, margem orçada → projetada, tabela por natureza com orçado, realizado, projetado e desvio, parcelas do contrato e OS). Nesta fase, o realizado só tem as contas a pagar do projeto (material e horas chegam na Fase 3) e a previsão de entrega vem na PLN-01.
- CLAUDE.md: o indicador de etapa (estilo display de andar) é o componente padrão de etapa do projeto em todas as telas.
- Padrões comuns: molde **detalhe com abas na URL**.

## Critérios de aceite
**Lista**
1. **Dado** `projects.view`, **quando** abro "Projetos", **então** vejo número (link), nome, cliente, indicador de etapa (`StageIndicator` com as etapas do projeto: Planejado, Em produção, Fabricado, Instalando, Entregue, Encerrado), data prometida, responsável e situação, com filtros na URL por situação, cliente e responsável; cancelado aparece com chip neutro.
2. **Dado** 50 mil projetos, **quando** abro a lista, **então** carrega em menos de 1 s (paginação server-side).

**Resumo**
3. **Dado** um projeto, **quando** abro a aba Resumo, **então** vejo contratado (valor do contrato, mais aditivos aprovados quando a COM-07 existir), recebido e a receber, data prometida, "Entrega prevista: calculada a partir da Fase 4" (PLN-01), margem orçada (do contrato) e margem projetada pela fórmula do `04-fluxos` §3.3 com o realizado disponível, e o contrato e o orçamento de origem com link.
4. **Dado** `projects.edit`, **quando** edito nome, responsável (usuário ativo do tenant) e endereço da obra (endereço do tipo Obra do cliente) e clico "Salvar projeto", **então** grava com auditoria; data prometida e valor não são editáveis aqui (só por aditivo, COM-07).

**Itens e OS**
5. **Dado** a aba "Itens e OS", **quando** abro, **então** cada item do projeto mostra descrição, quantidade e as OS ligadas (instalação na COM-06; fabricação a partir da Fase 3) com número, tipo, etapa e situação; sem OS, "Ainda sem OS".

**Financeiro**
6. **Dado** `finance.view`, **quando** abro a aba Financeiro, **então** vejo os títulos a receber do projeto (marco, número, vencimento, valor, saldo, situação; `IReceivables.ByProjectAsync`) e os títulos a pagar com o projeto (`IPayables.ByProjectAsync`: fornecedor, origem, vencimento, valor, situação), com os totais recebido, a receber, pago e a pagar; cada linha leva ao título.
7. **[Segurança]** **Dado** um usuário com `projects.view` sem `finance.view`, **quando** abre o projeto, **então** a aba Financeiro não aparece e a API dos títulos por projeto responde 403 `forbidden`; o Resumo mostra só o valor contratado.

**Custos**
8. **Dado** a aba Custos, **quando** abro, **então** a tabela tem uma linha por natureza (Material, Mão de obra, Instalação, Frete, Impostos, Outros) com orçado (`project_budget`), lançado (títulos a pagar do projeto não cancelados, pela natureza do título — ver Pontos em aberto 1), restante e desvio %, e o total; a legenda diz "Material e horas entram com a produção (Fase 3)."
9. **Dado** um título a pagar com projeto e natureza Instalação de R$ 8.000,00 e orçado de instalação R$ 10.000,00, **quando** abro Custos, **então** a linha mostra lançado R$ 8.000,00, restante R$ 2.000,00 e desvio −20,0%, e a margem projetada do Resumo usa esses valores.

**Documentos e linha do tempo**
10. **Dado** a aba Documentos, **quando** abro, **então** vejo os anexos do projeto (dono `Project`, upload com `projects.edit`), o contrato assinado (COM-04a) e os PDFs do orçamento aprovado (COM-03), cada um com quem e quando; o download segue a permissão do dono de cada anexo.
11. **Dado** a aba Linha do tempo, **quando** abro, **então** vejo em ordem cronológica: orçamento aprovado, contrato assinado, parcelas geradas e liberadas, fabricação registrada, OS criadas e aceitas, aditivos e cancelamento, cada um com data, quem (ou "Sistema") e link; eventos financeiros só aparecem para quem tem `finance.view`.

**Permissões e segurança**
12. **Dado** o módulo `projects` não liberado, **quando** chamo, **então** 403 `module_not_released` e a tela `ModuleNotReleased`; sem grupo, 403 `forbidden`; sem `projects.edit`, a edição e o upload ficam ocultos e respondem 403. `RequireModule` e `perm:`; IDOR em `/projects/{id}/attachments/{attId}` e `/projects/{id}/items/{itemId}`.
13. **Dado** qualquer alteração no projeto, **quando** abro "Histórico de alterações", **então** vejo antes/depois, usuário e data.

## Regras de negócio e técnicas
- **Natureza no título a pagar** (proposta): `payables.cost_type` (mesmas naturezas do orçamento), obrigatório quando o título tem projeto, sugerido pela conta (2.1 Materiais → Material, 2.3 Instalação terceirizada → Instalação, 2.4 Fretes → Frete, 2.5 Impostos sobre vendas → Impostos, 2.6 Outros custos → Outros) e pela origem (`Installer` → Instalação).
- **Margem projetada** = (receita contratada − (lançado + restante)) ÷ receita contratada, restante por natureza nunca negativo (`04-fluxos` §3.3).
- **Linha do tempo** montada no servidor a partir das tabelas do Commercial e dos contratos `IReceivables`/`IPayables` (sem ler tabela de outro módulo), paginada.
- Dinheiro `numeric(18,2)`; datas `date`; auditoria `IAuditable` no projeto.

## Permissões, eventos e tabelas
- **Permissões (já no `ModuleCatalog`):** `projects.view`, `projects.edit`; `finance.view` para a aba Financeiro e os eventos financeiros da linha do tempo.
- **Eventos:** nenhum novo.
- **Contratos usados:** `IReceivables.ByProjectAsync` (COM-04b), `IPayables.ByProjectAsync` (FIN-02), `IParties`, `IUserDirectory.DisplayNameAsync`.
- **Tabelas:** `finance.payables` (+ `cost_type`, **proposta**); anexo `Project` no `AttachmentOwnerRegistry` (leitura `projects.view`, escrita `projects.edit`).

## Telas e estados
- **Comercial → Projetos** (`/app/projetos`): h1 "Cada contrato vira um projeto. Todo custo lançado cai aqui." (protótipo).
- **Projeto** (`/app/projetos/$id`), abas na URL: Resumo (padrão), Itens e OS, Financeiro, Custos, Documentos, Linha do tempo, Histórico de alterações (última).
- **Ações no cabeçalho:** "Registrar fabricação concluída" (COM-04b), "Nova OS de instalação" (COM-06), "Aditivo" (COM-07), "Documentos".
- **Estados:** carregando (skeleton por aba); vazio da lista ("Os projetos nascem quando um orçamento é aprovado." + "Ver orçamentos enviados"); vazio de custos lançados ("Nenhum custo lançado ainda."); erro por aba com "Tentar de novo"; modo leitura.
- **Propositivo:** natureza do título sugerida pela conta; a margem projetada abaixo da orçada mostra o aviso "Margem projetada {x}% abaixo da orçada." com link para Custos; parcela vencida no Financeiro leva ao título.
- **400 px:** abas com rolagem horizontal; tabela de custos empilhada.
- **Ajuda:** `Ajuda/projetos.md` (lista) e `Ajuda/projetos-projeto.md` (abas `resumo` com `padrao: true`, `itens`, `financeiro`, `custos`, `documentos`, `linha-do-tempo`, `historico`); `<Dica>` em "Margem projetada", "Lançado" e "Restante".

## Plano de testes
- **Unidade:** margem projetada e restante nunca negativo (CA 3, 9); sugestão de natureza (regra).
- **Integração (Testcontainers):** lista e desempenho (CA 1, 2); edição (CA 4); títulos por projeto com e sem `finance.view` (CA 6, 7); custos por natureza com dataset fixo (CA 8, 9); documentos com permissão do dono (CA 10); linha do tempo filtrada por permissão (CA 11); três camadas e IDOR (CA 12); auditoria (CA 13).
- **E2E (Playwright):** abrir projeto aprovado, navegar pelas abas pela URL, lançar um CP com projeto e ver em Custos.

## Fora do escopo
- Orçado × realizado completo com material, horas e impostos, e ranking de margem: DSH-02 (Fase 4).
- Data prevista de entrega e Gantt: PLN-01 e PLN-03 (Fase 4). Fila de prioridade: PLN-02.
- Garantia por item entregue (R-PRJ-7, D): fica para depois da PRD-06 (série do produto).
- Histórico comercial e financeiro do terceiro (R-TER-3): FIN-06.

## Pontos em aberto
1. **Natureza do custo no título a pagar.** O 03 não tem `cost_type` em `payables`, e o `04-fluxos` §3.3 pede o realizado por natureza. **Recomendação:** coluna `cost_type` obrigatória quando há projeto, sugerida pela conta do plano e pela origem.
2. **Quem vê a margem do projeto.** **Recomendação:** `projects.view` (como no orçamento, COM-01a ponto 5); restringir só se o PO pedir.
3. **P-18:** ver FIN-01, ponto 5.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers) e E2E passando.
- [ ] Migration criada (não destrutiva).
- [ ] Permissões só do `ModuleCatalog`; dono de anexo `Project` registrado.
- [ ] Telas com estados por aba.
- [ ] Ajuda da lista e de cada aba; teste de cobertura passando.
- [ ] Alertas acionáveis aplicados.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Consultas da lista, do resumo, dos custos e da linha do tempo.
- [ ] `IPayables.ByProjectAsync` e `payables.cost_type` (Finance), com o campo "Natureza do custo" no formulário do título a pagar (FIN-02) quando há projeto, e a ajuda `financeiro-pagar-titulo.md` atualizada.
- [ ] Endpoints `/api/v1/projects` (+ `/{id}/summary`, `/costs`, `/timeline`, `/attachments`); telas e ajuda.

## Quality gates
- Agentes previstos: @dev, design (tela 360), @qa.
- Gate: CA 3, 7 e 9 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 19/10/2026.

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

- **Correções feitas na validação (19/10/2026):** o backlog não tinha CA: todos os CAs desta story são novos; aba Financeiro só com `finance.view` (CA 7); realizado limitado às contas a pagar nesta fase, com a legenda; R-TER-3 movido para a FIN-06; data prevista deixada para a PLN-01.
- **Pré-condições para `Ready`:** Pontos em aberto 1 e 2 (com padrão recomendado) e P-18.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 19/10/2026 | 0.1 | Story detalhada | River (SM) |
| 19/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
