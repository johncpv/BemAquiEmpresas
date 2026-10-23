# ENG-02 — BOM da OS

| Campo | Valor |
|---|---|
| ID | ENG-02 |
| Título | BOM (lista de materiais) criada na etapa Projeto da OS de fabricação, com cópia de outra OS ou do último item igual; linhas com item, quantidade por unidade, unidade e perda %; liberação pela engenharia que dispara reservas e cálculo de faltas; revisão depois da liberação com histórico e ajuste das reservas; bloqueio das etapas depois da primeira sem BOM liberada (D12) |
| Tamanho | M |
| Status | Done |
| Requisitos | R-ENG-3 (BOM na etapa Projeto, cópia de item/OS anterior, itens, quantidades, unidade, perda %) · R-ENG-4 (liberação dispara reserva e faltas) · R-ENG-5 (revisão após liberação ajusta reservas e registra histórico) · R-EST-3 · R-PRP-2 ("BOM liberada com falta → Gerar solicitação de compra") · R-PRP-3 · R-SEC-5 |
| Dependências | PRD-01 (OS de fabricação, `IProductionOrders`), PRD-03a (início de etapa, onde o bloqueio da D12 liga), EST-01a (`IStockBalances` para a prévia de faltas) |
| Bloqueia | EST-02, EST-06 |
| Ordem de execução | 11 (Marco 3.2 "Estoque e BOM") |

## User story
Como **engenheiro**, quero **montar a lista de materiais de cada OS, copiando de uma OS parecida, e liberá-la quando estiver pronta**, para **o almoxarifado reservar o material, o comprador saber o que falta e o custo de material do projeto começar a ser previsto**.

## Contexto e referências
- PRD §4 ("Etapa Projeto: BOM ─► Reserva de estoque ─► falta ─► Solicitação de Compra"); §5.6 R-ENG-3 a R-ENG-5; R-PRP-2.
- `04-fluxos-estados` §1 "OS (fabricação)": `Open ─(BOM liberada e primeira etapa iniciada)─► InProgress`, mudado pela D12 (ADR 0023 §7): a primeira etapa inicia sem BOM; as seguintes exigem BOM liberada, e o bloqueio liga com esta story. §2: `BomReleased` → Inventory cria reservas e, se faltar, Purchasing cria solicitação (`BOMShortage`); `BomRevised` → Inventory ajusta reservas.
- `03-modelo-dados` → `engineering.boms` (`production_order_id`, `revision`, `status` Draft/Released, `released_at`, `released_by`, `copied_from_bom_id`), `engineering.bom_lines` (`bom_id`, `item_id`, `quantity`, `unit_id`, `scrap_pct`, `notes`); relacionamento `production_order 1—N boms (revisões) 1—N bom_lines 1—1 reservation`.
- **Revisão pré-Fase 3** (`docs/revisoes/2026-10-21-revisao-pre-fase3.md`): D1 (Marco 3.2), D12 (etapas sem BOM), D13 (permissões da Engenharia); tabela 1.1, itens 23 a 26; §2.1 "Reservas e faltas" (reconciliação a partir da fonte); §2.3 padrões 4, 6, 7, 8 e 10.
- ADR 0008 (adendo de 21/10/2026: `engineering.approve` para a Engenharia pelo seed patch `engineering-release-bom`, fora da delegação, e 2FA obrigatório para quem a tem); ADR 0003 (adendo de 21/10/2026: `engineering.delete`, entregue na ENG-01); ADR 0023 §6 (consumidores reconciliam pela fonte, `IBoms.ReleasedForOrderAsync`; evento de revisão antiga ignorado) e §7 (D12); ADR 0016 (seed nos tenants existentes); ADR 0019 §2 (`xmin`); ADR 0021 (processamento assíncrono).
- `ModuleCatalog`: `engineering.approve` = "liberar a BOM"; `engineering.delete` (ENG-01).
- **Marco 3.1 sem BOM:** as OS do Marco 3.1 rodaram sem BOM (D1); quando esta story entra, as OS abertas que ainda vão começar uma etapa depois da primeira passam a precisar de BOM liberada.
- Protótipo: Estoque com "2 itens sem saldo para as OS abertas geraram solicitações de compra automaticamente".

## Critérios de aceite
**Permissões do seed**
1. **Dado** um tenant novo ou existente no próximo deploy, **quando** o seed patch `engineering-release-bom` (D13, ADR 0008 adendo de 21/10/2026, ADR 0016) roda, **então** o grupo Engenharia do seed ganha `engineering.approve`, uma vez, com o `perm_version` incrementado, e os membros passam a ter 2FA obrigatório no próximo login (FND-06c CA 12); fora do seed, só um membro do Proprietário concede `engineering.approve`.

**Montar a BOM**
2. **Dado** `engineering.create` e uma OS de fabricação `Open` ou `InProgress`, **quando** abro a aba BOM da OS e clico "Criar BOM", **então** nasce a revisão 0 em `Draft`, vazia ou copiada (CA 5), e posso adicionar linhas com item (combobox de itens ativos que não são Serviço nem o próprio produto da OS), quantidade **por unidade do produto** (`numeric(18,4)`, > 0), unidade (a de estoque por padrão; outra só com conversão cadastrada, validada por `IUnitConversion`, 422 "Cadastre a conversão de {un} para {un} no item."), perda % (`numeric(7,4)`, 0 a 100, padrão 0) e observação (até 200).
3. **Dado** o editor da BOM, **quando** edito, **então** cada linha mostra a quantidade por unidade e a **necessidade da OS** = quantidade × quantidade da OS × (1 + perda %), convertida para a unidade de estoque (4 casas, `AwayFromZero`), o físico, o reservado e o disponível atuais (`IStockBalances`, disponível = físico − reservado da posição, ADR 0023 §2) e o chip "Falta {x}" quando a necessidade passa do disponível; o mesmo item não se repete na BOM (422 "O item já está na linha {n}.").
4. **Dado** uma OS sem produto (`product_id` nulo: item do orçamento sem item de estoque), **quando** abro a aba BOM, **então** a BOM pode ser criada e liberada normalmente, o cabeçalho mostra o chip de alerta "Sem item de estoque" com a `<Dica>` "Esta OS não tem o produto no estoque. Para concluir a OS, cadastre o item e ligue à OS." e a conclusão da OS (PRD-06a2) é que exige o produto.
5. **Dado** "Copiar BOM", **quando** escolho a origem (outra OS, padrão: a última OS do mesmo produto com BOM liberada, com a tag "Sugerido"; R-PRP-1), **então** as linhas são copiadas para a BOM em `Draft`, com `copied_from_bom_id`, e itens que ficaram inativos vêm marcados para revisão (aviso âmbar, sem bloquear a edição).
6. **Dado** `engineering.edit`, **quando** salvo a BOM em `Draft`, **então** grava sem efeito em estoque; o Draft pode ser editado à vontade.
7. **Dado** `engineering.delete` e uma BOM em `Draft`, **quando** clico "Descartar rascunho" e confirmo, **então** a revisão em `Draft` é apagada sem efeito (a liberada anterior, se houver, continua valendo) e o histórico registra quem descartou; sem `engineering.delete`, o botão some e a API responde 403.

**Liberar**
8. **Dado** `engineering.approve` e uma BOM `Draft`, **quando** clico "Liberar BOM" e confirmo no diálogo de impacto com a prévia (por linha: necessidade, disponível, vai reservar, falta), **então** na mesma transação a BOM fica `Released` (com `released_at` e `released_by`) e o evento `BomReleased` (`{ bomId, productionOrderId, projectId, revision, lines: [{ bomLineId, itemId, requiredQty }] }`, quantidades na unidade de estoque) vai para o outbox; a mensagem diz "BOM liberada. Reservando material…" (padrão 7: aviso de demora depois de 30 s e notificação ao terminar, pela EST-02) e a aba Material (EST-02) mostra o estado "em andamento".
9. **Dado** uma BOM `Draft` **sem nenhuma linha** (OS só de mão de obra), **quando** clico "Liberar BOM", **então** o diálogo diz "BOM sem material: esta OS usa só mão de obra." e, ao confirmar, a BOM fica `Released` sem reservas; o `BomReleased` sai com `lines` vazio.
10. **Dado** a prévia com falta, **quando** a liberação termina, **então** o `NextSteps` oferece "Ver solicitações de compra geradas" (a partir da CMP-01) e "Ver faltas da OS" (R-PRP-2).
11. **Dado** uma OS `Cancelled` ou `Completed`, **quando** tento liberar ou revisar, **então** 409 "A OS {número} está {situação}: a BOM não muda."

**Revisar depois de liberada**
12. **Dado** `engineering.edit` e uma BOM `Released`, **quando** clico "Revisar BOM", **então** nasce a revisão n+1 em `Draft` com as linhas da liberada; a liberada continua valendo até a nova ser liberada.
13. **Dado** `engineering.approve` e a revisão n+1, **quando** clico "Liberar revisão" e confirmo no diálogo de impacto com a diferença por item (antes → depois, a reservar a mais, a liberar), **então** na mesma transação a revisão n+1 fica `Released`, a anterior fica `Superseded`, e o evento `BomRevised` (`{ bomId, previousBomId, productionOrderId, projectId, revision, lines: [{ bomLineId, itemId, requiredQty, previousRequiredQty }] }`) vai para o outbox; o índice único parcial `(production_order_id) WHERE status = 'Released'` garante uma única revisão liberada por OS.
14. **Dado** uma revisão que diminui um item já baixado além da nova necessidade, **quando** libero, **então** o diálogo avisa "Já foram baixados {x} de {item}; a nova necessidade é {y}. Devolva a diferença ao estoque pela requisição." (a devolução é da EST-03), sem bloquear a liberação; a quantidade baixada vem de `IStockReservations` (EST-02) quando existir, e o aviso fica oculto até lá.
15. **Dado** a aba BOM, **quando** escolho "Revisões", **então** vejo cada revisão com situação (Rascunho, Liberada, Substituída), quem liberou e quando, e a comparação lado a lado de duas revisões (itens incluídos, removidos e quantidades alteradas).

**Bloqueio das etapas sem BOM (D12)**
16. **Dado** uma OS sem BOM `Released`, **quando** alguém inicia (PRD-03a, `/fab` ou "Apontar por funcionário") ou conclui pelo supervisor (PRD-02 "Concluir etapa") uma etapa **depois da primeira**, **então** o servidor recusa com 409 `bom_not_released` "A BOM desta OS ainda não foi liberada. Fale com a Engenharia." (o mesmo texto da PRD-03a CA 9); no backoffice o próximo passo é "Abrir BOM da OS" (com `engineering.view`); a primeira etapa continua iniciando sem BOM; a etapa já em andamento no deploy não é interrompida.
17. **Dado** o deploy desta story, **quando** existem OS abertas sem BOM liberada (Marco 3.1), **então** a lista Engenharia → BOMs abre no filtro "OS que vão parar sem BOM" (OS com a etapa atual depois da primeira, ou com a primeira já `Done`, e sem BOM liberada), com "Criar BOM" e "Liberar BOM sem material" por linha.

**Contrato e permissões**
18. **Dado** o `IBoms` em `Engineering.Contracts` (novo), **quando** a Produção, o Estoque ou a EST-06 chamam, **então** `ReleasedForOrderAsync(productionOrderId)` devolve a revisão liberada vigente (número da revisão e linhas com a necessidade na unidade de estoque) ou `null`, para a EST-02 reconciliar as reservas a partir da fonte e ignorar evento de revisão antiga (ADR 0023 §6); `IsReleasedAsync(productionOrderId)` atende o bloqueio do CA 16.
19. **Dado** o módulo `engineering` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; criar sem `engineering.create`, editar sem `engineering.edit`, liberar sem `engineering.approve`, descartar sem `engineering.delete`: 403 e o botão some. Na OS (Produção), a aba BOM só aparece com `engineering.view`. `RequireModule` e `perm:`; IDOR em `/boms/{id}/lines/{lineId}`; OS validada por `IProductionOrders`.
20. **[Segurança]** **Dado** duas liberações simultâneas da mesma BOM, ou liberação e edição, **quando** chegam juntas, **então** só uma grava (`xmin` e o índice único parcial), e o `BomReleased` sai uma vez só; o consumidor (EST-02) é idempotente pela inbox.
21. **Dado** qualquer alteração da BOM, **quando** abro "Histórico de alterações", **então** vejo antes/depois, usuário e data; BOM liberada nunca é editada nem apagada, só revisada.

## Regras de negócio e técnicas
- **Quantidade por unidade do produto** (ponto 24); necessidade da OS = qtd × qtd da OS × (1 + perda %), convertida para a unidade de estoque pelo `IUnitConversion`, arredondada em 4 casas no fim. A tela mostra as duas.
- **Revisão:** número crescente por OS (único `(production_order_id, revision)`); estados `Draft`, `Released` e `Superseded` (ponto 25); uma única `Released` por OS (índice único parcial); `Draft` só uma por vez.
- **Evento com a necessidade calculada:** o Inventory não recalcula conversão nem perda; o payload leva só ids e quantidades. Os consumidores não aplicam o delta: reconciliam pela fonte (`ReleasedForOrderAsync`, ADR 0023 §6).
- **BOM e custo:** a BOM não lança custo; o custo de material do projeto nasce na requisição (EST-03). O custo previsto da BOM (necessidade × custo médio) aparece na aba só para quem tem `projects.view_cost`, nunca no PWA, e não é gravado.
- **Produto sem item de estoque** (ponto 26): permitido (CA 4); BOM vazia liberável (CA 9).
- **Bloqueio D12:** fica no serviço de início e de conclusão de etapa da Produção, que chama `IBoms.IsReleasedAsync` para etapa de sequência > 1 (CA 16).
- Guid v7; `xmin`; auditoria `IAuditable` em BOM e linha; soft delete não se aplica (Draft descartado por "Descartar rascunho", CA 7).

## Permissões, eventos e tabelas
- **Permissões:** `engineering.view`, `engineering.create`, `engineering.edit`, `engineering.approve` (liberar), `engineering.delete` (descartar rascunho, D13). Seed patch `engineering-release-bom` (CA 1).
- **Eventos (`Engineering.Contracts`):** `BomReleased`, `BomRevised` (consumidos pela EST-02).
- **Contratos:** novo `IBoms` (`ReleasedForOrderAsync`, `IsReleasedAsync`); usa `IProductionOrders`, `IItems`, `IUnitConversion`, `IStockBalances`.
- **Tabelas:** `engineering.boms` (+ `Superseded`, `xmin`, índice único parcial da liberada), `engineering.bom_lines`.

## Telas e estados
- **Padrões de tela da Fase 3:** 4 (diálogo de impacto ao liberar e revisar), 6 (origem da cópia "Sugerido"), 7 (processamento assíncrono das reservas), 8 (físico, reservado e disponível por linha), 10 (aba BOM na ordem das abas da OS).
- **OS → aba BOM** (`/app/producao/os/$id?aba=bom`, com `engineering.view`) e **Fábrica → Engenharia → BOMs** (`/app/engenharia/boms`: OS com BOM em rascunho, sem BOM ou liberada, e o filtro "OS que vão parar sem BOM", CA 17; filtros na URL; a primeira coluna leva à aba BOM da OS).
- **Verbos:** "Criar BOM" / "BOM criada"; "Copiar BOM" / "BOM copiada"; "Salvar BOM" / "BOM salva"; "Liberar BOM" / "BOM liberada"; "Revisar BOM" / "Revisão criada"; "Liberar revisão" / "Revisão liberada"; "Descartar rascunho" / "Rascunho descartado".
- **Estados:** carregando; vazio ("Esta OS ainda não tem BOM." + "Criar BOM" e "Copiar de outra OS"); erro; modo leitura da liberada ("BOM liberada em {data} por {usuário}. Para mudar, revise a BOM.").
- **Propositivo:** origem da cópia sugerida (CA 5); unidade de estoque preenchida; necessidade e falta ao vivo, total de itens em falta no rodapé fixo (`aria-live`); próximo passo depois de liberar (CA 10); a recusa do CA 16 leva à BOM. A liberação pede confirmação porque reserva estoque (R-PRP-7).
- **400 px:** linhas em cartões (item, quantidade por unidade, necessidade, disponível e falta; físico e reservado na linha expansível, padrão 8); diálogo de liberação em tela cheia.
- **Ajuda:** `Ajuda/producao-os-bom.md` (aba `bom` da OS; explica a D12: a primeira etapa começa sem BOM, as seguintes não), `Ajuda/engenharia-boms.md`; `<Dica>` em "Perda %", "Necessidade da OS", "Liberar", "Sem item de estoque" (CA 4) e "Substituída".

## Plano de testes
- **Unidade:** necessidade com perda e conversão (CA 3); revisão e diferença por item (CA 13); duplicidade de item; regra do bloqueio por sequência (CA 16).
- **Integração (Testcontainers):** seed patch uma vez só (CA 1); criar, copiar e salvar sem efeito em estoque, inclusive OS sem produto (CA 2–6); descartar com `engineering.delete` (CA 7); liberar com evento no outbox na mesma transação e rollback, e BOM vazia (CA 8, 9); OS cancelada (CA 11); revisão, `Superseded` e índice parcial (CA 12, 13); aviso de baixado acima (CA 14, a partir da EST-02); bloqueio no início de etapa da PRD-03a e na "Concluir etapa" da PRD-02, primeira etapa livre (CA 16); filtro do deploy (CA 17); `IBoms` com revisão vigente (CA 18); três camadas e IDOR (CA 19); liberação concorrente com um evento só (CA 20); auditoria (CA 21).
- **E2E (Playwright):** copiar BOM de outra OS, liberar e ver o próximo passo; tentar iniciar a segunda etapa sem BOM e ver a recusa com o próximo passo; tela a 400 px.

## Fora do escopo
- Reserva e falta: EST-02. Solicitação de compra: CMP-01. Requisição e devolução: EST-03.
- BOM padrão por produto (estrutura multinível, subconjuntos): fora do MVP; a cópia de OS anterior cobre o caso (R-ENG-3).
- Custo previsto gravado da BOM: fora (o orçado do projeto é o do contrato).
- Entrada do produto acabado e exigência do produto na conclusão: PRD-06a2.

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fase 3, tabela 1.1, itens 23–26):
1. D13: `engineering.approve` à Engenharia pelo seed patch `engineering-release-bom`, com adendo à ADR 0008 (CA 1).
2. Quantidade por unidade do produto, mostrando também a necessidade da OS (CA 2, 3).
3. Estado `Superseded`, com índice único parcial da revisão liberada (CA 13).
4. BOM sem item de estoque do produto permitida, com o chip "Sem item de estoque"; a conclusão da OS (PRD-06a2) exige o produto. BOM vazia liberável para OS só de mão de obra (CA 4, 9).

Decisões da revisão aplicadas: D13 (`engineering.delete` para descartar rascunho, CA 7); D12 (bloqueio das etapas depois da primeira, CA 16, 17); ADR 0023 §6 (`IBoms.ReleasedForOrderAsync`, CA 18).

**Pré-condição externa:** nenhuma.

## Definition of Done
- [x] Critérios de aceite atendidos (saldo, falta e custo previsto pela EST-01a; "vai reservar" e o baixado do CA 14 esperam a EST-02; ver Change Log 1.1).
- [x] Testes de unidade (`BomRulesTests`, 11) e E2E (`boms.spec.ts`, 3 cenários × desktop e celular) passando; integração (Testcontainers, `FabricationTests.Bom`, 15 cenários) **escrita e não executada** nesta máquina (Docker em fila): rodar `sh scripts/test-docker.sh` antes do merge.
- [x] Migration `EngineeringBoms` criada (só `CreateTable`, índices e `CHECK`s por SQL, sem Drop nem Rename); **falta** testar sobre cópia do banco do tenant `konebras`. Seed patch `engineering-release-bom` (ADR 0008 adendo de 21/10/2026 aplicado).
- [x] Permissões só do `ModuleCatalog`.
- [x] Bloqueio da D12 ligado no início de etapa (PRD-03a) e na "Concluir etapa" (PRD-02).
- [x] Telas com estados, inclusive a 400 px.
- [x] Ajuda da aba e da lista; teste de cobertura passando.
- [x] Confirmação com impacto e próximos passos aplicados.
- [x] Auditoria ligada (`Bom` e `BomLine`; histórico por OS, inclusive o rascunho descartado).
- [x] OpenAPI e cliente Orval: `openapi.json` editado à mão e cliente gerado com `pnpm gen:api`; **regenerar** com `BQ_UPDATE_OPENAPI=1 sh scripts/test-docker.sh --filter-class "*OpenApiDocumentTests"` e `pnpm gen:api`.
- [x] `docs/03-modelo-dados.md` e `docs/04-fluxos-estados.md` (situação `Superseded`, D12) atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [x] `Bom` e `BomLine` (Domain) com a máquina de estados e o cálculo da necessidade.
- [x] `BomService` (criar, copiar, salvar, liberar, revisar, descartar) publicando `BomReleased`/`BomRevised`.
- [x] `IBoms` em `Engineering.Contracts` (`ReleasedForOrderAsync`, `IsReleasedAsync`).
- [x] Bloqueio por BOM no serviço de etapas da Produção (início e "Concluir etapa").
- [x] Endpoints `/api/v1/engineering/boms`; aba BOM na OS, lista de BOMs com o filtro do deploy, ajudas e seed patch.

## Quality gates
- Agentes previstos: @dev, @architect (evento com necessidade calculada, reconciliação pela fonte e fronteira Engineering × Inventory × Production), @qa.
- Gate: CA 3, 8, 9, 13, 16, 18 e 20 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fase 3).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: Marco 3.2, ordem 11, depois da PRD-03a e da EST-01a) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** a dependência da PRD-01 ficou explícita (a BOM pertence à OS, então a ordem é ENG-01 → PRD-01 → ENG-02 → EST-02); o evento leva a necessidade já convertida (CA 6); custo previsto só com `projects.view_cost` (D7); concessão de `engineering.approve` por seed patch como ponto em aberto (ADR 0008).
- **Correções da revalidação (21/10/2026):** pontos 23 a 26 decididos (seed patch com adendo da ADR 0008 e 2FA, CA 1; quantidade por unidade e necessidade da OS, CA 2, 3; `Superseded` com índice único parcial, CA 13; chip "Sem item de estoque" e BOM vazia liberável, CA 4, 9); "Descartar rascunho" com `engineering.delete` (D13, CA 7); D12: bloqueio das etapas depois da primeira no início de etapa da PRD-03a e na "Concluir etapa" da PRD-02, com mensagem e próximo passo, e filtro das OS do Marco 3.1 que vão parar (CA 16, 17); `IBoms.ReleasedForOrderAsync` para a reconciliação da EST-02 (ADR 0023 §6, CA 18); físico, reservado e disponível na linha (padrão 8); dependências e bloqueios pela nova ordem (agora depois da PRD-03a e da EST-01a); CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma de decisão de story.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO (D1, D12, D13, tabela 1.1 itens 23–26) e correções da revisão pré-Fase 3 (§2.1 reservas e faltas pela fonte, §2.3 padrões 4, 6, 7, 8 e 10); nova ordem (Marco 3.2, ordem 11); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
| 23/10/2026 | 1.0 | Implementada (`Done`). Desvios: (1) **EST-01a ainda fora da main:** o saldo vem de um ponto de ligação pequeno, `IBomStock` (Engineering.Application), com `DisconnectedBomStock` registrado; físico, reservado, disponível, falta, "vai reservar" e custo previsto ficam nulos e a tela diz "o saldo do estoque ainda não aparece". A EST-01a liga `PositionsAsync`/`AverageCostsAsync` num adaptador sobre o `IStockBalances` (e o custo médio); a EST-02 liga `IssuedForOrderAsync` (aviso do CA 14, oculto até lá). Nenhum código da EST-01a foi copiado. (2) `bom_lines` ganha `sequence` e `required_quantity` (a necessidade da OS na unidade de estoque, recalculada a cada gravação e na liberação; é o que o evento e o `IBoms` levam). (3) Contrato novo `IItemUnits` (Inventory.Contracts): unidades usáveis por item com o fator para a de estoque (editor e prévia ao vivo); a conta gravada continua no `IUnitConversion`. (4) `IProductionOrders.OpenAsync` e `ProductionOrderInfo` com `CurrentStepSequence`, `FirstStepDone` e `StepCount` (parâmetros opcionais, compatível) para o filtro do CA 17. (5) `AuditHistory.GetByParentAsync` (BuildingBlocks): histórico de todas as revisões e linhas da OS, inclusive o rascunho apagado. (6) O bloqueio da D12 vale para a etapa depois da primeira que ainda está `Ready`; `InProgress`/`Paused` (inclusive outro operador entrando e o supervisor concluindo) não é interrompida. Com o módulo `engineering` não liberado no tenant, o bloqueio não vale. (7) CA 11 vale para criar, salvar, copiar, revisar e liberar; descartar rascunho de OS fechada é permitido. BOM muda em qualquer OS não concluída nem cancelada (inclusive `Paused`, `Quality` e `ReadyToComplete`). (8) A revisão descartada libera o número (revisão = maior existente + 1). (9) `BomRevised` leva o item removido com `RequiredQty` 0 e o id da linha anterior. (10) Copiar: origem é BOM não rascunho de outra OS; item excluído, Serviço ou o próprio produto não é copiado; unidade sem conversão entra com necessidade 0 até o salvar pedir a conversão. Item inativo entra marcado e não bloqueia salvar nem liberar (o diálogo avisa). (11) Próximos passos depois de liberar com falta: "Ver solicitações de compra geradas" leva a `/app/compras` (CMP-01) e "Ver faltas da OS" fica na aba BOM (a aba Material é da EST-02); a mensagem "BOM liberada. Reservando material…" sai, mas o aviso de 30 s e a notificação são da EST-02. A falta por linha só é calculada no rascunho (na liberada vem das reservas, EST-02). (12) "Fora do seed, só o Proprietário concede `engineering.approve`" já é garantido pela regra de ações reservadas `*.approve` (FND-06b); sem código novo. (13) `FabricationTests` (PRD-01 a PRD-06a1) passa a usar o `NoBomGate`; os testes da ENG-02 usam o `EngineeringBomGate`. (14) OpenAPI editado à mão: regenerar. Pendente do PO: confirmar 6, 7 e 10 (item inativo não bloqueia a liberação) | Dex (dev) |
| 23/10/2026 | 1.1 | Rebase sobre a main com a EST-01a (PR #86). Migration refeita com data nova (`20261023154824_EngineeringBoms`, depois da `20261023124524_InventoryStockEngine`), com o mesmo `Up`/`Down` e os `CHECK`s por SQL. **`IBomStock` ligado ao motor de estoque:** `StockBalancesBomStock` soma o `IStockBalances` nos locais (físico, reservado e disponível) e lê o custo médio por um contrato pequeno novo, `IItemCosts` (Inventory.Contracts), para o custo previsto (só com `projects.view_cost`, nunca gravado). Isso substitui o desvio 1 da 1.0 quanto ao saldo e ao custo. Continua desligado o que é da EST-02: "vai reservar" (`ReservationsConnected` false na prévia; a coluna some do diálogo com o aviso "Quanto vai ser reservado e o que já foi baixado ainda não aparecem nesta versão.") e o baixado por OS do CA 14 (`IssuedForOrderAsync` null). A frase final da prévia passou a "{n} itens em falta pelo saldo disponível." (a solicitação de compra é da CMP-01). Ajuda e testes atualizados (unidade do adaptador; integração com entrada pelo motor, saldo, falta e custo previsto). OpenAPI reaplicado sobre o da main e cliente gerado de novo: continua a regenerar com `BQ_UPDATE_OPENAPI` | Dex (dev) |
