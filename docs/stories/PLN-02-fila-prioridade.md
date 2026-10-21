# PLN-02 — Fila de prioridade dos projetos

| Campo | Valor |
|---|---|
| ID | PLN-02 |
| Título | Fila dos projetos com OS de fabricação abertas, reordenada arrastando (ou pelo teclado), com a simulação "antes → depois" das datas previstas e da folga de cada projeto antes de salvar, aviso de quem passa a ficar em risco, histórico das mudanças e recálculo do plano |
| Tamanho | M |
| Status | Draft |
| Requisitos | R-PLN-2 (fila de projetos com prioridade manual, arrastar para ordenar) · R-PLN-3 · R-PLN-4 · R-PRP-2 · R-SEC-5 |
| Dependências | PLN-01a (motor, `SimulateAsync`), PLN-01b (data prevista, folga e risco), COM-05 (projeto), FND-09e (diálogo, concorrência) |
| Bloqueia | PLN-03 (ordem das linhas), PNL-01b ("próximos na fila"), PLN-05 (posição na simulação) |
| Ordem de execução | 8 (Marco 4.3 "Datas que a fábrica cumpre") |

## User story
Como **supervisor de produção**, quero **arrastar os projetos para mudar a ordem da fila e ver, antes de confirmar, o que acontece com a data de entrega de cada um**, para **decidir a prioridade sabendo quem eu atraso**.

## Contexto e referências
- PRD R-PLN-2; §2 (Supervisor de produção: "Sequência da fila").
- `04-fluxos-estados` §3.4 (1) "Ordena projetos pela prioridade manual" e (5) recalcula "a cada mudança de prioridade".
- `03-modelo-dados`: `commercial.projects.priority` ("int para fila"). **Código atual:** `Project.Priority` (`int?`) existe no Commercial e não é usado.
- CLAUDE.md (stack): dnd-kit para arrastar.
- Fase 3 (rascunho): PRD-02 CA 9 ("o arraste dentro da coluna também não reordena nesta fase; prioridade é a PLN-02"); EST-02 ponto 2 ("na Fase 4, pela prioridade da fila", para a ordem em que as faltas são completadas).
- `GroupSeeder`: Supervisão de produção tem `production.view/create/edit/approve` e só `projects.view`.

## Critérios de aceite
**Fila**
1. **Dado** `production.view`, **quando** abro Planejamento → Fila (`/app/producao/planejamento?aba=fila`), **então** vejo os projetos que têm OS de fabricação não concluída e não cancelada, na ordem da fila, com: posição, número e nome do projeto (link), cliente, OS abertas, horas restantes, prometido, previsto (PLN-01b), folga em dias e o chip (No prazo, Em risco, Atrasado, Sem previsão).
2. **Dado** um projeto que ganha a primeira OS de fabricação (PRD-01), **quando** a fila é lida, **então** ele entra no fim, na ordem da data de assinatura do contrato (proposta, Pontos em aberto 3); o projeto cujas OS de fabricação terminaram ou foram canceladas sai da fila; as posições continuam contíguas (1, 2, 3…), sem buraco.

**Reordenar com prévia**
3. **Dado** `production.edit`, **quando** arrasto um projeto para outra posição (ou uso "Mover para cima", "Mover para baixo" e "Mover para a posição…" pelo teclado), **então** a fila entra em modo de edição com a faixa "Ordem alterada, ainda não salva." e, em até 2 s, cada linha mostra a prévia "previsto antes → depois" e a folga nova, calculadas pelo `IProductionSchedule.SimulateAsync` com a ordem nova, sem gravar nada.
4. **Dado** a prévia, **quando** algum projeto passa a ficar em risco ou piora, **então** as linhas dele ficam destacadas e, ao clicar "Salvar ordem", o diálogo lista "Estes projetos passam a atrasar: {projeto} ({n} dias depois do prometido)…" com "Salvar mesmo assim" e "Voltar"; sem piora, salva direto.
5. **Dado** "Salvar ordem", **quando** confirmo, **então** a nova ordem grava numa transação (com a versão da fila; outra pessoa que salvou antes → 409 `concurrency_conflict` "A fila mudou desde que você abriu. Recarregue."), a mudança fica no histórico (quem, quando, posições antes → depois), um recálculo é pedido (PLN-01b) e a mensagem de sucesso diz "Ordem salva. Recalculando as datas…"; "Descartar" volta à ordem gravada.
6. **[Segurança]** **Dado** o comando de salvar, **quando** a lista enviada não é uma permutação exata dos projetos da fila atual (projeto a mais, a menos, repetido ou de outro tenant), **então** 422 "A fila mudou. Recarregue e reordene de novo." sem gravar.

**Histórico e uso por outros módulos**
7. **Dado** a aba Fila, **quando** abro "Mudanças na fila", **então** vejo as mudanças com quem, quando e as posições antes → depois (auditoria).
8. **Dado** o `IPlanningQueue` em `Production.Contracts` (novo), **quando** o motor (PLN-01a), o painel da TV (PNL-01b) ou o Estoque (EST-02, ordem das faltas, se o PO decidir) pedem `OrderAsync()`, **então** recebem os ids dos projetos na ordem da fila.

**Desempenho, permissões e segurança**
9. **Dado** 50 projetos e 300 OS na fila, **quando** reordeno, **então** a prévia chega em menos de 2 s e a fila abre em menos de 1 s.
10. **Dado** o módulo `production` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo ou sem `production.view`, 403 `forbidden`; salvar sem `production.edit` responde 403 e o arraste fica desligado (a fila aparece só para leitura, com a nota "Para mudar a ordem, peça a quem cuida da produção."). `RequireModule` e `perm:`.

## Regras de negócio e técnicas
- **Dono da fila** (proposta, Pontos em aberto 1): o Production, na tabela `production.planning_queue` (`project_id` único, `position`), com uma linha de versão (`production.planning_queue_state`, `xmin`); o `commercial.projects.priority` do 03 fica sem uso.
- **A prévia é simulação**: o motor roda em memória com a ordem nova (`SimulateAsync`), nada é gravado até "Salvar ordem".
- **Sem efeito financeiro, fiscal nem de estoque**; a confirmação existe só quando a ordem atrasa alguém (R-PRP-7 não se aplica, mas o aviso é propositivo).
- Guid v7; auditoria `IAuditable` na fila (`position` antes/depois).

## Permissões, eventos e tabelas
- **Permissões:** `production.view` (ver), `production.edit` (reordenar; proposta, Pontos em aberto 2).
- **Eventos:** nenhum no outbox (o recálculo é pedido dentro do módulo, PLN-01b).
- **Contratos:** novo `IPlanningQueue` (`Production.Contracts`); usa `IProductionSchedule`, `IProjects`, `IParties`.
- **Tabelas:** `production.planning_queue` e `production.planning_queue_state` (**propostas**).

## Telas e estados
- **Planejamento → Fila** (aba padrão do Planejamento a partir desta story): lista com alça de arraste (dnd-kit), botões de mover para o teclado, faixa de edição, prévia em duas linhas por projeto ("05/12 → 09/12", folga "−7 dias").
- **Verbos:** "Salvar ordem" / "Ordem salva"; "Descartar".
- **Estados:** carregando; vazio ("Nenhum projeto com OS de fabricação aberta." + "Ver OS de fabricação"); erro; simulação em andamento ("Calculando o impacto…"); leitura sem permissão (CA 10).
- **Propositivo:** a prévia mostra o efeito antes de salvar; o projeto "Sem previsão" leva ao motivo.
- **Acessibilidade:** a reordenação funciona só pelo teclado (dnd-kit com sensores de teclado e anúncio "Projeto {n} movido para a posição {p}" por `aria-live`).
- **400 px:** sem arraste; cada linha com "Subir" e "Descer" (alvos de 44 px), número, previsto e chip; a prévia aparece abaixo de cada linha.
- **Ajuda:** `Ajuda/producao-planejamento-fila.md` (aba `fila`, `padrao: true`; a ajuda padrão da PLN-01a passa a ser desta aba); `<Dica>` em "Folga" e "Prévia".

## Plano de testes
- **Unidade:** entrada e saída da fila e posições contíguas (CA 2); validação da permutação (CA 6).
- **Integração (Testcontainers):** prévia sem gravar e igual ao cálculo depois de salvar (CA 3, 5); aviso de quem atrasa (CA 4); concorrência com duas abas (CA 5); permutação inválida e projeto de outro tenant (CA 6); histórico (CA 7); `IPlanningQueue` (CA 8); desempenho (CA 9); três camadas (CA 10).
- **E2E (Playwright):** arrastar um projeto, ver a prévia, salvar e ver as datas novas; reordenar só pelo teclado; tela a 400 px com "Subir"/"Descer".

## Fora do escopo
- Prioridade por OS dentro do projeto (a ordem segue o item do projeto, `04-fluxos` §3.4).
- Mudar a etapa pelo arraste no Kanban (ponto 1 da PRD-02).
- Fila por linha de produção (uma fila só por tenant no MVP).

## Pontos em aberto
1. **Dono da prioridade** (PO/arquiteto): o 03 põe `priority` em `commercial.projects`, mas quem ordena a fila é a Supervisão de produção (permissões do `production`). Proposta: a fila passa a ser do Production (`production.planning_queue`) e o campo do Commercial fica sem uso (remover só com ADR). Alternativa: manter no Commercial, gravado por um contrato `IProjectPriority` chamado pelo endpoint do Production.
2. **Permissão para reordenar** (PO): proposta: `production.edit`. Alternativa: `production.approve` (a mudança de ordem promete datas ao cliente).
3. **Onde entra o projeto novo** (PO): proposta: no fim, pela data de assinatura. Alternativa: pela data prometida (mais urgente primeiro).
4. **Ordem das faltas pela fila** (PO, herdado do ponto 2 da EST-02): o `IPlanningQueue` fica disponível; usar a fila para completar as faltas muda a EST-02 e fica fora desta story até a decisão.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers), desempenho e E2E (arraste e teclado) passando.
- [ ] Migration das tabelas da fila (não destrutiva), com a carga inicial pela data de assinatura.
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Tela com estados, acessível pelo teclado e a 400 px.
- [ ] Ajuda da aba; teste de cobertura passando.
- [ ] Prévia e aviso de atraso aplicados.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `planning_queue` e `planning_queue_state`; manutenção da fila na criação e no fim das OS.
- [ ] Endpoints `/api/v1/production/planning/queue` (GET, `/preview`, PUT); `IPlanningQueue`.
- [ ] Aba Fila com dnd-kit, prévia e ajuda.

## Quality gates
- Agentes previstos: @dev, @qa, design (arraste, teclado e 400 px).
- Gate: CA 3, 5 e 6 obrigatórios.

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

- **Correções feitas na validação:** o dono da prioridade conflita com `commercial.projects.priority` do 03 e ficou como ponto em aberto (1, pré-condição), sem mudar o modelo por conta própria; prévia por simulação sem gravar (CA 3); permutação validada no servidor (CA 6).
- **Pré-condições para `Ready`:** pontos em aberto 1 e 2.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
