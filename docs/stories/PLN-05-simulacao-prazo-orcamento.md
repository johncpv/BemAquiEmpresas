# PLN-05 — Simulação de prazo do orçamento

| Campo | Valor |
|---|---|
| ID | PLN-05 |
| Título | "Se eu aceitar este orçamento agora, quando entrego?": a partir das horas por centro de trabalho estimadas no orçamento, o motor de agendamento simula um projeto virtual na fila (no fim ou numa posição escolhida) e mostra a data de fim da fabricação, a entrega prevista com a instalação e o efeito nos outros projetos, sem gravar nada e devolvendo só datas a quem tem `quotes.view`; o prazo calculado pode ser usado no orçamento em rascunho |
| Tamanho | M |
| Status | Done |
| Requisitos | R-PLN-5 (simulação a partir das horas estimadas do orçamento, **D**) · R-COML-1 (horas por centro de trabalho no orçamento) · R-PRP-1 · R-PRP-2 |
| Dependências | PLN-01a (`IProductionSchedule.SimulateAsync`), PLN-01b (prazo de instalação), PLN-02 (posição na fila), COM-01a (composição de custo com horas por centro), PRD-01 (`routing_template_id` no item do orçamento e regra de quais itens geram OS), ENG-01 (ordem das etapas do roteiro) |
| Bloqueia | — (última da Fase 4; a Fase 5 não depende dela) |
| Ordem de execução | 15 (Marco 4.5 "A visão completa do dono"; desejável, último da fase) |

## User story
Como **comercial** (ou dono), quero **saber, antes de mandar o orçamento, em que data a fábrica consegue entregar se o cliente aprovar hoje**, para **prometer um prazo que dá para cumprir**.

## Contexto e referências
- PRD R-PLN-5 (D): "Simulação: 'se eu aceitar este orçamento agora, quando entrego?' a partir das horas estimadas do orçamento".
- COM-01a (Done): a composição de custo do item tem a natureza Mão de obra com **horas × custo-hora do cargo** e o **centro de trabalho** "informado para a capacidade (PLN-05)"; o custo unitário do item é para uma unidade, e o total multiplica pela quantidade; a composição só sai com `quotes.view_cost`. O cabeçalho do orçamento tem o "prazo de entrega prometido (dias corridos)" (`delivery_days_promised`).
- **Fase 3, item 6 (ENG-01, decidido):** `routing_template_id` no item do orçamento como coluna anulável, criada junto com a PRD-01, sem tela. **D14 da Fase 3:** `Equipment` gera OS automática; `Product` (componente fabricado) por "Gerar OS"; `Service` nunca.
- EST-06 (Fase 3): "Capacidade de produzir" (material) — a PLN-05 aponta para ela.
- PLN-01a: o motor aceita um projeto virtual em memória (`SimulateAsync(queueOrder, extraProject)`), sem gravar (ADR 0029).
- **Revisão pré-Fases 4 a 8** (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`): tabela 3.1, pontos 54 a 56; §4.1 (item 6 da Fase 3 fechado); §4.3 transversal (rate limit por política nomeada na simulação).

## Critérios de aceite
1. **Dado** `quotes.view` e um orçamento `Draft` ou `Sent` (revisão corrente), **quando** clico "Simular prazo", **então** o servidor monta um projeto virtual com uma OS por item que gera OS pela regra da PRD-01 (D14 da Fase 3: Equipamento; Produto com horas por centro, porque vira OS pelo "Gerar OS"; Serviço nunca) e, em cada OS, uma etapa por centro de trabalho das linhas de Mão de obra do item, com horas = Σ horas das linhas daquele centro × quantidade do item.
2. **Dado** a ordem das etapas da OS virtual, **quando** o servidor monta, **então** usa a sequência do roteiro do item do orçamento, se houver (`routing_template_id`), ou do roteiro sugerido para o produto (PRD-01); **sem roteiro, a ordem das linhas de Mão de obra na composição do orçamento**; centros que não estão no roteiro entram no fim, na ordem das linhas.
3. **Dado** a posição, **quando** simulo, **então** o padrão é "no fim da fila"; posso escolher "Na posição {n}" (1 até o tamanho da fila + 1) e simular de novo; a data de início é hoje (aprovação hoje).
4. **Dado** a simulação, **quando** termina, **então** vejo "Se aprovado hoje, a fabricação termina em {data} e a entrega fica em {data}" (com o prazo de instalação da PLN-01b quando o orçamento tem custo de instalação), a comparação com o prazo do orçamento ("Prazo prometido no orçamento: {n} dias → {data}") com o chip "Cabe no prazo" ou "{n} dias além do prazo", e, quando a posição não é o fim, "Projetos que passam a atrasar: {n}" (com os números e nomes dos projetos só para quem tem `projects.view`; sem, só a contagem).
5. **Dado** a simulação, **quando** roda, **então** nada é gravado (nem plano, nem fila, nem orçamento) e a resposta chega em menos de 2 s com 50 projetos e 300 OS na fila (teste).
6. **Dado** um orçamento `Draft` e `quotes.edit`, **quando** clico "Usar este prazo", **então** o campo "Prazo de entrega prometido" recebe os dias corridos de hoje até a entrega prevista, no formulário, e só grava quando eu salvo o orçamento (R-PRP-1, R-PRP-7); em orçamento `Sent`, o botão diz "Criar nova revisão com este prazo" e segue o fluxo da COM-02.
7. **Dado** um orçamento sem nenhuma linha de Mão de obra com centro, **quando** clico "Simular prazo", **então** vejo "Este orçamento não tem horas por centro de trabalho. Informe as horas na composição de custo para simular." (com link para a composição, para quem tem `quotes.view_cost`), sem chamar o motor.
8. **Dado** a simulação, **quando** mostra o resultado, **então** mostra também "Não considera o prazo de compra do material." com o link "Ver capacidade de produzir" (EST-06), quando existir.
9. **[Segurança]** **Dado** `quotes.view` sem `quotes.view_cost`, **quando** simulo, **então** o servidor lê as horas internamente e devolve **só datas e dias**: nenhum custo, custo-hora, cargo nem horas por centro sai na resposta (teste de contrato do DTO); a fila e a ocupação de outros projetos não aparecem além do CA 4; simular não exige `production.view`.
10. **[Segurança]** **Dado** o endpoint de simulação, **quando** é chamado em sequência, **então** tem rate limit pela política nomeada `planning-simulate` (a mesma da PLN-02: 20 por minuto por usuário; acima, 429 "Aguarde alguns segundos para simular de novo.").
11. **Dado** o módulo `quotes` ou `production` não liberado, **quando** chamo, **então** 403 `module_not_released` (sem o `production`, o botão não aparece); sem grupo ou sem `quotes.view`, 403 `forbidden`. `RequireModule` e `perm:`; IDOR: o orçamento é validado no Commercial e a revisão precisa ser a corrente.

## Regras de negócio e técnicas
- **Quem monta o projeto virtual:** o Commercial (dono do orçamento) monta itens, centros e horas e chama `IProductionSchedule.SimulateAsync` (`Production.Contracts`) com o projeto virtual; o Production não lê tabela do Commercial.
- **Sem roteiro** (ponto 54): a ordem das linhas do orçamento.
- **Quem simula** (ponto 55): `quotes.view`, só datas.
- **Roteiro no item** (ponto 56): `routing_template_id` anulável da PRD-01 (item 6 da Fase 3).
- **Horas** `numeric(18,4)`; datas `date`; dias corridos para o prazo (como o `delivery_days_promised`).
- **Sem efeito**: simulação pura; a única gravação possível é a do usuário ao salvar o orçamento (CA 6).

## Permissões, eventos e tabelas
- **Permissões:** `quotes.view` (simular), `quotes.edit` (usar o prazo), `projects.view` (nomes dos projetos afetados), `quotes.view_cost` (link para a composição).
- **Eventos:** nenhum.
- **Contratos:** `IProductionSchedule.SimulateAsync` (PLN-01a), `IPlanningQueue` (tamanho da fila), `IRoutingTemplates` (ordem das etapas).
- **Tabelas:** nenhuma nova.

## Telas e estados
- **Padrões comuns das Fases 4 a 8:** 7 (impacto em lista: os projetos que passam a atrasar, antes de usar o prazo).
- **Orçamento** (`/app/orcamentos/$id`): ação "Simular prazo" no cabeçalho e no `NextSteps` do rascunho; diálogo com a posição, o resultado e "Usar este prazo".
- **Estados:** "Calculando o prazo…"; sem horas (CA 7); erro com "Tentar de novo"; limite de simulações (CA 10); fila vazia ("A fábrica está livre: o prazo depende só das horas do orçamento.").
- **Propositivo:** "Usar este prazo" preenche o campo; o aviso de material leva à capacidade de produzir.
- **400 px:** diálogo em tela cheia, resultado em "facts" de 1 coluna; botões com 44 px.
- **Ajuda:** atualizar `Ajuda/comercial-orcamento.md` (simulação de prazo, ordem das etapas sem roteiro); `<Dica>` em "Simular prazo" e "Na posição".

## Plano de testes
- **Unidade:** montagem do projeto virtual (itens que geram OS, centros, horas × quantidade, ordem pelo roteiro e sem roteiro pela ordem das linhas) (CA 1, 2).
- **Integração (Testcontainers):** simulação no fim e numa posição com o dataset do PLN-01a e projetos afetados (CA 3, 4); nada gravado (CA 5); sem horas (CA 7); DTO sem custo para quem não tem `quotes.view_cost` e sem exigir `production.view` (CA 9); rate limit (CA 10); três camadas e IDOR (CA 11); desempenho (CA 5).
- **E2E (Playwright):** simular num rascunho, usar o prazo e salvar; tela a 400 px.

## Fora do escopo
- Prazo de compra do material na simulação (lead time do fornecedor).
- Reservar capacidade para o orçamento antes da aprovação.
- Tela para escolher o roteiro no item do orçamento (a coluna existe desde a PRD-01, sem tela).

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fases 4 a 8, tabela 3.1, pontos 54–56):
1. Etapas sem roteiro na ordem das linhas do orçamento (CA 2).
2. Simular com `quotes.view`, só datas (CA 9).
3. Roteiro no item do orçamento: item 6 da Fase 3 (`routing_template_id` anulável da PRD-01, sem tela) (CA 2).

Decisões da revisão aplicadas: §4.1 (item 6 da Fase 3 fechado; D14 da Fase 3 nos itens que geram OS, CA 1); §4.3 transversal (rate limit por política nomeada, CA 10).

**Pré-condição externa:** nenhuma.

## Definition of Done
- [x] Critérios de aceite atendidos.
- [x] Testes de unidade, integração (Testcontainers), desempenho e E2E passando.
- [x] Nenhuma migration.
- [x] Permissões só do `ModuleCatalog`.
- [x] Diálogo com estados e 400 px.
- [x] Ajuda do orçamento atualizada; teste de cobertura passando.
- [x] "Usar este prazo" sem gravar sozinho.
- [x] OpenAPI e cliente Orval regenerados.
- [x] Story marcada no backlog.

## Tarefas técnicas sugeridas
- [x] `QuoteScheduleSimulation` no Commercial (projeto virtual) e endpoint `/api/v1/quotes/{id}/schedule-simulation` com a política `planning-simulate`.
- [x] Diálogo no orçamento; ajuda.

## Quality gates
- Agentes previstos: @dev, @qa.
- Gate: CA 1, 2, 5 e 9 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fases 4 a 8).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: PRD-01 pelo `routing_template_id`; a Fase 5 não depende desta story) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** a composição de custo é sigilosa (`quotes.view_cost`): a simulação devolve só datas (CA 9); "Usar este prazo" só preenche o formulário (R-PRP-7); ordem das etapas sem roteiro como ponto em aberto.
- **Correções da revalidação (21/10/2026):** pontos 54 a 56 decididos (ordem das linhas sem roteiro; `quotes.view` só datas, sem exigir `production.view`; `routing_template_id` da PRD-01); itens que geram OS pela D14 da Fase 3 (CA 1); rate limit pela política nomeada `planning-simulate` (§4.3, CA 10); CAs renumerados (11).
- **Pré-condições para `Ready`:** nenhuma de decisão de story.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (tabela 3.1 pontos 54–56, §4.1, §4.3, §4.4 padrão 7); ordem mantida (Marco 4.5, ordem 15); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
