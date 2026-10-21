# EST-06 — Capacidade de produzir

| Campo | Valor |
|---|---|
| ID | EST-06 |
| Título | Para uma BOM (de uma OS ou copiada de outra), quantas unidades o disponível atual permite produzir e o que falta, item a item, para uma quantidade desejada; atalho para gerar a solicitação de compra do que falta |
| Tamanho | P |
| Status | Draft |
| Requisitos | R-EST-10 (capacidade de produzir: quantas unidades o disponível permite e o que falta, **D**) · R-PRP-2 · R-CMP-1 |
| Dependências | ENG-02 (`IBoms`), EST-02 (`IStockBalances` com reservado), CMP-01 (solicitação manual) |
| Bloqueia | PLN-05 (Fase 4, simulação de prazo do orçamento, pode reaproveitar) |
| Ordem de execução | 23 (Marco 3.5 "Fechar a OS"; desejável, último da fase) |

## User story
Como **comercial ou supervisor**, quero **saber quantos equipamentos iguais a um já feito dá para produzir com o estoque de hoje e o que falta para chegar a uma quantidade**, para **prometer prazo com base no que existe e comprar só a diferença**.

## Contexto e referências
- PRD §5.9 R-EST-10 (D): "para um item/BOM, quantas unidades o disponível atual permite e o que falta".
- `04-fluxos-estados` §3.1 (disponível = físico − reservado). ENG-02 (necessidade com perda e conversão).

## Critérios de aceite
1. **Dado** `inventory.view` + `engineering.view`, **quando** abro "Capacidade de produzir" (`/app/estoque/capacidade`) e escolho uma BOM de referência (a liberada de uma OS, padrão: a última do produto escolhido), **então** vejo por item da BOM: necessidade por unidade (com perda, na unidade de estoque), disponível atual (todos os locais, já sem o reservado), unidades possíveis por item (`floor(disponível ÷ necessidade)`), e o total **possível** = menor dos itens, com o item que limita destacado ("Limita: {item}").
2. **Dado** uma quantidade desejada (padrão 1), **quando** informo, **então** cada item mostra a falta = max(necessidade × quantidade − disponível, 0) e o total de itens em falta; com `projects.view_cost`, o valor estimado da falta pelo custo médio (não gravado).
3. **Dado** faltas, **quando** clico "Gerar solicitação de compra" (`purchasing.create`), **então** abre a nova solicitação da CMP-01 preenchida com os itens e as quantidades em falta, destino "Estoque geral" (editável), sem gravar até o usuário confirmar (R-PRP-7).
4. **Dado** um item da BOM inativo ou sem conversão de unidade, **quando** calcula, **então** a linha mostra o aviso e o item entra como "não calculado", sem derrubar o resto.
5. **Dado** os módulos `inventory` ou `engineering` não liberados, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; sem `inventory.view` ou `engineering.view`, 403. `RequireModule` e `perm:`; a BOM é validada por `IBoms`; o cálculo só lê (nenhuma escrita).
6. **Dado** uma BOM com 100 itens, **quando** calculo, **então** responde em menos de 1 s (saldos em lote pelo `IStockBalances`).

## Regras de negócio e técnicas
- Cálculo puro no Inventory sobre `IBoms.ReleasedForOrderAsync` e `IStockBalances`; nada gravado.
- Quantidades `numeric(18,4)`; possível por item com `floor`; custo estimado `numeric(18,2)` só para exibição.

## Permissões, eventos e tabelas
- **Permissões:** `inventory.view` + `engineering.view`; `purchasing.create` (atalho); `projects.view_cost` (valor da falta).
- **Eventos:** nenhum. **Tabelas:** nenhuma.

## Telas e estados
- **Fábrica → Estoque → Capacidade de produzir** (`/app/estoque/capacidade`).
- **Estados:** carregando; vazio ("Escolha um produto ou uma OS para ver quantas unidades o estoque permite."); erro.
- **Propositivo:** BOM sugerida pela última do produto; atalho para a solicitação (CA 3).
- **400 px:** itens em cartões com necessidade, disponível e falta; total possível no topo.
- **Ajuda:** `Ajuda/estoque-capacidade.md`; `<Dica>` em "Limita".

## Plano de testes
- **Unidade:** possível e falta com perda e conversão (CA 1, 2) com tabela de casos.
- **Integração (Testcontainers):** dataset fixo com reservado (o disponível desconta a reserva) (CA 1); item sem conversão (CA 4); três camadas (CA 5); desempenho com 100 itens (CA 6).
- **E2E (Playwright):** calcular, aumentar a quantidade e abrir a solicitação preenchida.

## Fora do escopo
- Prazo de entrega com capacidade da fábrica: PLN-05 (Fase 4).
- Considerar pedidos de compra em aberto como disponível futuro: Pontos em aberto 1.

## Pontos em aberto
1. **Material a caminho** (PO): proposta: só o disponível de hoje (R-EST-10); mostrar à parte "Com os pedidos em aberto: {n} unidades" se o PO pedir.

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
- [ ] Tela, atalho para a CMP-01 e ajuda.

## Quality gates
- Agentes previstos: @dev, @qa.
- Gate: CA 1 e 2 obrigatórios.

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

- **Correções feitas na validação:** o disponível já desconta o reservado das OS abertas (CA 1); o atalho abre a solicitação sem gravar (CA 3); valor da falta só com `projects.view_cost`.
- **Pré-condições para `Ready`:** nenhuma.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Draft` até a aprovação do PO.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
