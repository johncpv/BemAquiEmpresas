# ENG-02 — BOM da OS

| Campo | Valor |
|---|---|
| ID | ENG-02 |
| Título | BOM (lista de materiais) criada na etapa Projeto da OS de fabricação, com cópia de outra OS ou do último item igual; linhas com item, quantidade, unidade e perda %; liberação pela engenharia que dispara reservas e cálculo de faltas; revisão depois da liberação com histórico e ajuste das reservas |
| Tamanho | M |
| Status | Draft |
| Requisitos | R-ENG-3 (BOM na etapa Projeto, cópia de item/OS anterior, itens, quantidades, unidade, perda %) · R-ENG-4 (liberação dispara reserva e faltas) · R-ENG-5 (revisão após liberação ajusta reservas e registra histórico) · R-EST-3 · R-PRP-2 ("BOM liberada com falta → Gerar solicitação de compra") · R-PRP-3 · R-SEC-5 |
| Dependências | PRD-01 (OS de fabricação, `IProductionOrders`), CAD-02a/02b (`IItems`, `IUnitConversion`), EST-01a (`IStockBalances` para a prévia de faltas) |
| Bloqueia | EST-02 (consome `BomReleased`/`BomRevised`), EST-06, CMP-01 (falta da BOM), PRD-03a (a OS só inicia com BOM liberada) |
| Ordem de execução | 6 (Marco 3.2 "Da engenharia à OS") |

## User story
Como **engenheiro**, quero **montar a lista de materiais de cada OS, copiando de uma OS parecida, e liberá-la quando estiver pronta**, para **o almoxarifado reservar o material, o comprador saber o que falta e o custo de material do projeto começar a ser previsto**.

## Contexto e referências
- PRD §4 ("Etapa Projeto: BOM ─► Reserva de estoque ─► falta ─► Solicitação de Compra"); §5.6 R-ENG-3 a R-ENG-5; R-PRP-2.
- `04-fluxos-estados` §1 "OS (fabricação)": `Open ─(BOM liberada e primeira etapa iniciada)─► InProgress`; §2: `BomReleased` → Inventory cria reservas e, se faltar, Purchasing cria solicitação (`BOMShortage`); `BomRevised` → Inventory ajusta reservas (libera ou reserva a diferença).
- `03-modelo-dados` → `engineering.boms` (`production_order_id`, `revision`, `status` Draft/Released, `released_at`, `released_by`, `copied_from_bom_id`), `engineering.bom_lines` (`bom_id`, `item_id`, `quantity`, `unit_id`, `scrap_pct`, `notes`); relacionamento `production_order 1—N boms (revisões) 1—N bom_lines 1—1 reservation`.
- `ModuleCatalog`: `engineering.approve` = "liberar a BOM". ADR 0008: `*.approve` é reservada ao Proprietário para conceder; o seed patch é aplicado pelo sistema, fora da delegação (ADR 0016).
- Protótipo: Estoque com "2 itens sem saldo para as OS abertas geraram solicitações de compra automaticamente".

## Critérios de aceite
**Permissões do seed**
1. **Dado** um tenant novo ou existente no próximo deploy, **quando** o seed patch `engineering-release-bom` (proposta, Pontos em aberto 1) roda, **então** o grupo Engenharia do seed ganha `engineering.approve`, uma vez, com o `perm_version` incrementado.

**Montar a BOM**
2. **Dado** `engineering.create` e uma OS de fabricação `Open` ou `InProgress`, **quando** abro a aba BOM da OS e clico "Criar BOM", **então** nasce a revisão 0 em `Draft`, vazia ou copiada (CA 4), e posso adicionar linhas com item (combobox de itens ativos que não são Serviço nem o próprio produto da OS), quantidade **por unidade do produto** (`numeric(18,4)`, > 0; Pontos em aberto 2), unidade (a de estoque por padrão; outra só com conversão cadastrada, validada por `IUnitConversion`, 422 "Cadastre a conversão de {un} para {un} no item."), perda % (`numeric(7,4)`, 0 a 100, padrão 0) e observação (até 200).
3. **Dado** o editor da BOM, **quando** edito, **então** cada linha mostra a **necessidade da OS** = quantidade × quantidade da OS × (1 + perda %), convertida para a unidade de estoque (4 casas, `AwayFromZero`), o disponível atual (`IStockBalances`) e o chip "Falta {x}" quando a necessidade passa do disponível; o mesmo item não se repete na BOM (422 "O item já está na linha {n}.").
4. **Dado** "Copiar BOM", **quando** escolho a origem (outra OS, padrão: a última OS do mesmo produto com BOM liberada; R-PRP-1), **então** as linhas são copiadas para a BOM em `Draft`, com `copied_from_bom_id`, e itens que ficaram inativos vêm marcados para revisão (aviso âmbar, sem bloquear a edição).
5. **Dado** `engineering.edit`, **quando** salvo a BOM em `Draft`, **então** grava sem efeito em estoque; o Draft pode ser editado à vontade.

**Liberar**
6. **Dado** `engineering.approve` e uma BOM `Draft` com ao menos uma linha, **quando** clico "Liberar BOM" e confirmo no diálogo grande com a prévia (por linha: necessidade, disponível, vai reservar, falta), **então** na mesma transação a BOM fica `Released` (com `released_at` e `released_by`) e o evento `BomReleased` (`{ bomId, productionOrderId, projectId, revision, lines: [{ bomLineId, itemId, requiredQty }] }`, quantidades na unidade de estoque) vai para o outbox; a mensagem diz "BOM liberada. Reservando material…" (ADR 0021, processamento assíncrono) e a aba Material (EST-02) mostra o estado "em andamento".
7. **Dado** a prévia com falta, **quando** a liberação termina, **então** o `NextSteps` oferece "Ver solicitações de compra geradas" (a partir da CMP-01) e "Ver faltas da OS" (R-PRP-2).
8. **Dado** uma OS `Cancelled` ou `Completed`, **quando** tento liberar ou revisar, **então** 409 "A OS {número} está {situação}: a BOM não muda."

**Revisar depois de liberada**
9. **Dado** `engineering.edit` e uma BOM `Released`, **quando** clico "Revisar BOM", **então** nasce a revisão n+1 em `Draft` com as linhas da liberada; a liberada continua valendo até a nova ser liberada.
10. **Dado** `engineering.approve` e a revisão n+1, **quando** clico "Liberar revisão" e confirmo no diálogo com a diferença por item (antes → depois, a reservar a mais, a liberar), **então** a revisão n+1 fica `Released`, a anterior fica `Superseded` (proposta: estado novo, Pontos em aberto 3), e o evento `BomRevised` (`{ bomId, previousBomId, productionOrderId, projectId, revision, lines: [{ bomLineId, itemId, requiredQty, previousRequiredQty }] }`) vai para o outbox, para a EST-02 ajustar as reservas.
11. **Dado** uma revisão que diminui um item já baixado além da nova necessidade, **quando** libero, **então** o diálogo avisa "Já foram baixados {x} de {item}; a nova necessidade é {y}. Devolva a diferença ao estoque pela requisição." (a devolução é da EST-03), sem bloquear a liberação; a quantidade baixada vem de `IStockReservations` (EST-02) quando existir, e o aviso fica oculto até lá.
12. **Dado** a aba BOM, **quando** escolho "Revisões", **então** vejo cada revisão com situação, quem liberou e quando, e a comparação lado a lado de duas revisões (itens incluídos, removidos e quantidades alteradas).

**Contrato e permissões**
13. **Dado** o `IBoms` em `Engineering.Contracts` (novo), **quando** a Produção, o Estoque ou a EST-06 chamam `ReleasedForOrderAsync(productionOrderId)`, **então** recebem a revisão liberada com as linhas e a necessidade na unidade de estoque; a Produção usa `IsReleasedAsync` para permitir iniciar a OS (PRD-03a).
14. **Dado** o módulo `engineering` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; criar sem `engineering.create`, editar sem `engineering.edit`, liberar sem `engineering.approve`: 403 e o botão some. Na OS (Produção), a aba BOM só aparece com `engineering.view`. `RequireModule` e `perm:`; IDOR em `/boms/{id}/lines/{lineId}`; OS validada por `IProductionOrders`.
15. **[Segurança]** **Dado** duas liberações simultâneas da mesma BOM, ou liberação e edição, **quando** chegam juntas, **então** só uma grava (`xmin`), e o `BomReleased` sai uma vez só; o consumidor (EST-02) é idempotente pela inbox.
16. **Dado** qualquer alteração da BOM, **quando** abro "Histórico de alterações", **então** vejo antes/depois, usuário e data; BOM liberada nunca é editada nem apagada, só revisada.

## Regras de negócio e técnicas
- **Quantidade por unidade do produto** (proposta, Pontos em aberto 2); necessidade da OS = qtd × qtd da OS × (1 + perda %), convertida para a unidade de estoque pelo `IUnitConversion`, arredondada em 4 casas no fim.
- **Revisão:** número crescente por OS (único `(production_order_id, revision)`); uma única `Released` por OS (índice único parcial); `Draft` só uma por vez.
- **Evento com a necessidade calculada:** o Inventory não recalcula conversão nem perda; o payload leva só ids e quantidades.
- **BOM e custo:** a BOM não lança custo; o custo de material do projeto nasce na requisição (EST-03). O custo previsto da BOM (necessidade × custo médio) aparece na aba só para quem tem `projects.view_cost` (sigilo D7), e não é gravado.
- Guid v7; `xmin`; auditoria `IAuditable` em BOM e linha; soft delete não se aplica (Draft descartado por "Descartar rascunho", que apaga a revisão em `Draft` sem efeito).

## Permissões, eventos e tabelas
- **Permissões:** `engineering.view`, `engineering.create`, `engineering.edit`, `engineering.approve` (liberar). Seed patch **proposto** `engineering-release-bom` (CA 1).
- **Eventos (`Engineering.Contracts`):** `BomReleased`, `BomRevised` (consumidos pela EST-02).
- **Contratos:** novo `IBoms`; usa `IProductionOrders`, `IItems`, `IUnitConversion`, `IStockBalances`.
- **Tabelas:** `engineering.boms` (+ `Superseded`, `xmin`), `engineering.bom_lines`.

## Telas e estados
- **OS → aba BOM** (`/app/producao/os/$id?aba=bom`, com `engineering.view`) e **Fábrica → Engenharia → BOMs** (`/app/engenharia/boms`: OS com BOM em rascunho, sem BOM ou liberada, filtros na URL; a primeira coluna leva à aba BOM da OS).
- **Verbos:** "Criar BOM" / "BOM criada"; "Copiar BOM" / "BOM copiada"; "Salvar BOM" / "BOM salva"; "Liberar BOM" / "BOM liberada"; "Revisar BOM" / "Revisão criada"; "Liberar revisão" / "Revisão liberada"; "Descartar rascunho".
- **Estados:** carregando; vazio ("Esta OS ainda não tem BOM." + "Criar BOM" e "Copiar de outra OS"); erro; modo leitura da liberada ("BOM liberada em {data} por {usuário}. Para mudar, revise a BOM.").
- **Propositivo:** origem da cópia sugerida (CA 4); unidade de estoque preenchida; necessidade e falta ao vivo, total de itens em falta no rodapé fixo (`aria-live`); próximo passo depois de liberar (CA 7). A liberação pede confirmação porque reserva estoque (R-PRP-7).
- **400 px:** linhas em cartões (item, quantidade, necessidade, falta); diálogo de liberação em tela cheia.
- **Ajuda:** `Ajuda/producao-os-bom.md` (aba `bom` da OS), `Ajuda/engenharia-boms.md`; `<Dica>` em "Perda %", "Necessidade da OS" e "Liberar".

## Plano de testes
- **Unidade:** necessidade com perda e conversão (CA 3); revisão e diferença por item (CA 10); duplicidade de item.
- **Integração (Testcontainers):** seed patch (CA 1); criar, copiar e salvar sem efeito em estoque (CA 2–5); liberar com evento no outbox na mesma transação e rollback (CA 6); OS cancelada (CA 8); revisão e `BomRevised` (CA 9, 10); aviso de baixado acima (CA 11, a partir da EST-02); `IBoms` (CA 13); três camadas e IDOR (CA 14); liberação concorrente com um evento só (CA 15); auditoria (CA 16).
- **E2E (Playwright):** copiar BOM de outra OS, liberar e ver o próximo passo; tela a 400 px.

## Fora do escopo
- Reserva e falta: EST-02. Solicitação de compra: CMP-01. Requisição e devolução: EST-03.
- BOM padrão por produto (estrutura multinível, subconjuntos): fora do MVP; a cópia de OS anterior cobre o caso (R-ENG-3).
- Custo previsto gravado da BOM: fora (o orçado do projeto é o do contrato).

## Pontos em aberto
1. **Liberar BOM no seed** (PO): proposta: seed patch que dá `engineering.approve` à Engenharia (o PRD põe a BOM com a Engenharia). Como `*.approve` é reservada ao Proprietário para conceder (ADR 0008), o patch precisa da concordância do PO, como foi com o `finance.pay` (adendo D5).
2. **Quantidade da linha por unidade ou por OS** (PO): proposta: por unidade do produto (a cópia entre OS de quantidades diferentes fica certa); alternativa: total da OS.
3. **Estado da revisão substituída** (arquiteto): o 03 tem só Draft/Released. Proposta: `Superseded`.
4. **Liberar a BOM com o produto sem item de estoque** (PO): a OS pode não ter `product_id` (item do orçamento sem item de estoque). Proposta: permitido; a série e a entrada do produto acabado (PRD-06a) exigem o produto.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers) e E2E passando.
- [ ] Migration criada (não destrutiva), testada sobre cópia do banco do tenant `konebras`; seed patch.
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Telas com estados, inclusive a 400 px.
- [ ] Ajuda da aba e da lista; teste de cobertura passando.
- [ ] Confirmação com impacto e próximos passos aplicados.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` e `docs/04-fluxos-estados.md` (situação `Superseded`) atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `Bom` e `BomLine` (Domain) com a máquina de estados e o cálculo da necessidade.
- [ ] `BomService` (criar, copiar, salvar, liberar, revisar, descartar) publicando `BomReleased`/`BomRevised`.
- [ ] `IBoms` em `Engineering.Contracts`.
- [ ] Endpoints `/api/v1/engineering/boms`; aba BOM na OS, lista de BOMs, ajudas e seed patch.

## Quality gates
- Agentes previstos: @dev, @architect (evento com necessidade calculada e fronteira Engineering × Inventory), @qa.
- Gate: CA 3, 6, 10 e 15 obrigatórios.

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

- **Correções feitas na validação:** a dependência da PRD-01 ficou explícita (a BOM pertence à OS, então a ordem é ENG-01 → PRD-01 → ENG-02 → EST-02); o evento leva a necessidade já convertida (CA 6); custo previsto só com `projects.view_cost` (D7); concessão de `engineering.approve` por seed patch como ponto em aberto (ADR 0008).
- **Pré-condições para `Ready`:** pontos em aberto 1 e 2.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
