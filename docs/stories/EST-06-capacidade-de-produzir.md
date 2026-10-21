# EST-06 — Capacidade de produzir

| Campo | Valor |
|---|---|
| ID | EST-06 |
| Título | Para uma BOM (de uma OS ou copiada de outra), quantas unidades o disponível de hoje permite produzir e o que falta, item a item, para uma quantidade desejada, com a linha informativa "Com os pedidos em aberto"; atalho para gerar a solicitação de compra do que falta |
| Tamanho | P (D, desejável; pode ir à Fase 4) |
| Status | Ready |
| Requisitos | R-EST-10 (capacidade de produzir: quantas unidades o disponível permite e o que falta, **D**) · R-PRP-2 · R-CMP-1 |
| Dependências | ENG-02 (`IBoms`), EST-02 (`IStockBalances` com reservado), CMP-01 (solicitação manual) |
| Bloqueia | — |
| Ordem de execução | 24 (Marco 3.4 "Fechar e rastrear"; desejável, último da fase) |

## User story
Como **comercial ou supervisor**, quero **saber quantos equipamentos iguais a um já feito dá para produzir com o estoque de hoje e o que falta para chegar a uma quantidade**, para **prometer prazo com base no que existe e comprar só a diferença**.

## Contexto e referências
- PRD §5.9 R-EST-10 (D): "para um item/BOM, quantas unidades o disponível atual permite e o que falta".
- `04-fluxos-estados` §3.1 (disponível = físico − reservado). ENG-02 (necessidade por unidade do produto, com perda e conversão).
- Pedidos em aberto: saldo a receber dos pedidos de compra aprovados e não cancelados (CMP-03a e CMP-04, que vêm antes na ordem), lido pelo `IPurchaseOrders.OpenQuantityByItemAsync` (CMP-03a CA 24).
- A PLN-05 (Fase 4, simulação de prazo do orçamento) pode reaproveitar o cálculo; não depende desta story.
- Revisão pré-Fase 3: `docs/revisoes/2026-10-21-revisao-pre-fase3.md` (tabela 1.1 item 92, §2.3 padrões 6, 8 e 9, §4 Marco 3.4).
- ADR 0023 §2 (disponível lido da posição agregada, `IStockBalances`).

## Critérios de aceite
1. **Dado** `inventory.view` + `engineering.view`, **quando** abro "Capacidade de produzir" (`/app/estoque/capacidade`) e escolho uma BOM de referência (a liberada de uma OS; padrão: a última do produto escolhido, com a tag "Sugerido"), **então** vejo por item da BOM: necessidade por unidade (com perda, na unidade de estoque), disponível de hoje (todos os locais, já sem o reservado, lido da posição agregada), unidades possíveis por item (`floor(disponível ÷ necessidade)`), e o total **possível** = menor dos itens, com o item que limita destacado ("Limita: {item}").
2. **Dado** uma quantidade desejada (padrão 1), **quando** informo, **então** cada item mostra a falta = max(necessidade × quantidade − disponível, 0) e o total de itens em falta; com `projects.view_cost`, o valor estimado da falta pelo custo médio (não gravado).
3. **Dado** o resultado, **quando** o usuário tem `purchasing.view` e o módulo `purchasing` está liberado, **então** abaixo do total possível aparece a linha informativa "Com os pedidos em aberto: {n}" (unidades possíveis somando ao disponível o saldo a receber dos pedidos de compra em aberto de cada item); o total possível e as faltas continuam só com o disponível de hoje (ponto 92); sem `purchasing.view` ou sem o módulo, a linha não aparece e o resto funciona.
4. **Dado** faltas, **quando** clico "Gerar solicitação de compra" (`purchasing.create`), **então** abre a nova solicitação da CMP-01 preenchida com os itens e as quantidades em falta, destino "Estoque geral" (editável), sem gravar até o usuário confirmar (R-PRP-7).
5. **Dado** um item da BOM inativo ou sem conversão de unidade, **quando** calcula, **então** a linha mostra o aviso e o item entra como "não calculado", sem derrubar o resto.
6. **Dado** os módulos `inventory` ou `engineering` não liberados, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; sem `inventory.view` ou `engineering.view`, 403. `RequireModule` e `perm:`; a BOM é validada por `IBoms`; o cálculo só lê (nenhuma escrita).
7. **Dado** uma BOM com 100 itens, **quando** calculo, **então** responde em menos de 1 s (saldos e pedidos em aberto em lote, uma chamada por contrato).

## Regras de negócio e técnicas
- Cálculo puro no Inventory sobre `IBoms.ReleasedForOrderAsync` e `IStockBalances`; nada gravado.
- **Pedidos em aberto** só na linha informativa (CA 3), pela consulta de leitura do Purchasing com o saldo a receber por item (`IPurchaseOrders.OpenQuantityByItemAsync`, entregue pela CMP-03a CA 24, com destino "todos").
- Quantidades `numeric(18,4)`; possível por item com `floor`; custo estimado `numeric(18,2)` só para exibição.

## Permissões, eventos e tabelas
- **Permissões:** `inventory.view` + `engineering.view`; `purchasing.view` (linha dos pedidos em aberto); `purchasing.create` (atalho); `projects.view_cost` (valor da falta).
- **Eventos:** nenhum. **Tabelas:** nenhuma.

## Telas e estados
- Padrões de tela da Fase 3: 6, 8, 9.
- **Fábrica → Estoque → Capacidade de produzir** (`/app/estoque/capacidade`, aba do grupo "Operação").
- **Disponível** no padrão 8: a lista mostra físico, reservado e disponível; a 400 px só o disponível, o resto na linha expansível.
- **Estados:** carregando; vazio ("Escolha um produto ou uma OS para ver quantas unidades o estoque permite."); erro.
- **Propositivo:** BOM sugerida pela última do produto; atalho para a solicitação (CA 4).
- **400 px:** itens em cartões com necessidade, disponível e falta; total possível e a linha dos pedidos em aberto no topo.
- **Ajuda:** `Ajuda/estoque-capacidade.md`; `<Dica>` em "Limita" e "Com os pedidos em aberto" ("Quanto daria para produzir se todos os pedidos de compra em aberto chegassem. Não entra no total.").

## Plano de testes
- **Unidade:** possível e falta com perda e conversão (CA 1, 2) com tabela de casos; linha com os pedidos em aberto sem mudar o total (CA 3).
- **Integração (Testcontainers):** dataset fixo com reservado (o disponível desconta a reserva) (CA 1); pedidos em aberto com e sem `purchasing.view` e com o módulo não liberado (CA 3); item sem conversão (CA 5); três camadas (CA 6); desempenho com 100 itens (CA 7).
- **E2E (Playwright):** calcular, aumentar a quantidade e abrir a solicitação preenchida.

## Fora do escopo
- Prazo de entrega com capacidade da fábrica: PLN-05 (Fase 4).
- Material a caminho no total possível: não entra (ponto 92); só a linha informativa.

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fase 3, tabela 1.1, item 92):
1. Material a caminho: só o disponível de hoje, com a linha "Com os pedidos em aberto: {n}" (CA 3).

**Pré-condição externa:** nenhuma.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers) e E2E passando.
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Tela com estados e 400 px.
- [ ] Ajuda escrita; teste de cobertura passando.
- [ ] Próximo passo (solicitação) aplicado, sem gravar sem confirmação.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] Story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `ProductionCapacityCalculator` puro; endpoint `/api/v1/inventory/production-capacity`.
- [ ] Leitura dos pedidos em aberto pelo `Purchasing.Contracts`.
- [ ] Tela, atalho para a CMP-01 e ajuda.

## Quality gates
- Agentes previstos: @dev, @qa.
- Gate: CA 1, 2 e 3 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fase 3).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK (ajustado) |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: ordem 24, sem bloquear a PLN-05) |
| 7 | Plano de testes cobre os CAs | OK (ajustado) |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK |

- **Correções feitas na validação (21/10/2026, primeira validação):** o disponível já desconta o reservado das OS abertas; o atalho abre a solicitação sem gravar; valor da falta só com `projects.view_cost`.
- **Correções da revalidação (21/10/2026):** linha informativa "Com os pedidos em aberto: {n}", sem mudar o total (CA 3, ponto 92); padrões 6, 8 e 9 na tela; Marco 3.4, ordem 24, desejável e com possibilidade de ir à Fase 4; a PLN-05 só reaproveita, não é bloqueada. CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma de decisão de story.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO (tabela 1.1 item 92) e correções da revisão pré-Fase 3 (§2.1 disponível pela posição agregada, ADR 0023 §2; §2.3 padrões 6, 8, 9); nova ordem (Marco 3.4, ordem 24) | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
