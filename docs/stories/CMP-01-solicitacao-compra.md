# CMP-01 — Solicitação de compra

| Campo | Valor |
|---|---|
| ID | CMP-01 |
| Título | Solicitação de compra manual, automática pela falta de material da BOM liberada (consumidor de `StockShortageChanged`) e automática pelo estoque mínimo (job diário), sem duplicar; lista de solicitações com o que precisa ser comprado e para quando |
| Tamanho | M |
| Status | Draft |
| Requisitos | R-CMP-1 (manual ou automática pela falta da BOM ou pelo estoque mínimo) · R-CMP-6 (vínculo com projeto ou estoque geral) · R-COM-2 (notificação de estoque mínimo) · R-COM-4 (numeração SC) · R-PRP-2 · R-PRP-4/5 (falta de material na pauta, alerta acionável) · R-SEC-5 |
| Dependências | EST-02 (`StockShortageChanged`, `IStockReservations.OpenShortagesAsync`), EST-01a (`IStockBalances`), CAD-02a (estoque mínimo do item), PRD-01 (`IProductionOrders`), COM-04a (`IProjects`) |
| Bloqueia | CMP-02 (cotação a partir da SC), CMP-03a |
| Ordem de execução | 8 (Marco 3.3 "Comprar o que falta"; primeira story do módulo `purchasing`) |

## User story
Como **almoxarife/comprador**, quero **que o sistema abra sozinho a solicitação do que falta para as OS e do que ficou abaixo do mínimo, e que eu possa pedir o resto à mão**, para **comprar a tempo, sem esquecer nada e sem comprar duas vezes**.

## Contexto e referências
- PRD §1.1 problema 4 ("compras são reativas"); §5.10 R-CMP-1 e R-CMP-6; §5.15a R-PRP-2/4/5 (faltas de material na pauta).
- `04-fluxos-estados` §2: `BomReleased` → se faltar disponível, Purchasing cria solicitação (`origin BOMShortage`). §4: numeração `SC-AAAA-0001` (já em `NumberFormats`).
- `03-modelo-dados` → `purchasing.purchase_requests` (`number`, `origin` Manual/BOMShortage/MinStock, `project_id`, `production_order_id`, `status` Open/Quoting/Ordered/Cancelled), `purchase_request_lines` (`request_id`, `item_id`, `quantity`, `needed_by`).
- `ModuleCatalog`: `purchasing` (`view`, `create`, `edit`, `approve` = "pedido de compra dentro da alçada", `delete`). **No seed, nenhum grupo além do Proprietário tem `purchasing`** (`GroupSeeder`); o PRD põe compras com o Almoxarife ("Requisições, recebimentos, inventário, compras").
- ADR 0019 §12 (job idempotente, dois testes, `dedup_key`), ADR 0006 (job recorrente global que enfileira por tenant).

## Critérios de aceite
**Permissões do seed**
1. **Dado** um tenant novo ou existente no próximo deploy, **quando** o seed patch `purchasing-permissions` (proposta) roda, **então** o Almoxarifado do seed ganha `purchasing` com `view`, `create` e `edit`, e o Financeiro ganha `purchasing.view`, uma vez, com o `perm_version` incrementado; `purchasing.approve` continua só no Proprietário (reservada, ADR 0008).

**Manual**
2. **Dado** `purchasing.create`, **quando** clico "Nova solicitação", informo as linhas (item ativo que não é Serviço — Pontos em aberto 4 —, quantidade na unidade de estoque ou convertida, data de necessidade `date` ≥ hoje), o destino (projeto em andamento, validado por `IProjects`, ou "Estoque geral"; R-CMP-6) e uma observação, e clico "Enviar solicitação", **então** nasce a SC `Open` com número `SC-2026-0001`, origem `Manual`, com `Idempotency-Key`, e a mensagem "Solicitação enviada".

**Pela falta da BOM**
3. **Dado** um `StockShortageChanged` com falta 4 de um item para uma OS, **quando** o consumidor processa, **então** existe uma linha de SC de origem `BOMShortage` com o item, a quantidade 4, a OS, o projeto e a data de necessidade = data prometida do projeto menos o prazo padrão de compra (proposta: 15 dias; Pontos em aberto 2), em uma SC `Open` por OS; reentregas e faltas repetidas **atualizam** a mesma linha em aberto (idempotente pela inbox e por `(production_order_id, bom_line_id)`), nunca duplicam.
4. **Dado** a falta diminuiu (chegou material ou a BOM foi revista) ou zerou, **quando** o evento chega, **então** a linha em aberto é reduzida ou cancelada (com o motivo "Falta resolvida pelo estoque"); linha já em cotação ou pedido (`Quoting`/`Ordered`) não muda e o comprador recebe o aviso "A falta de {item} na {OS} caiu para {x}; a linha já está em {cotação/pedido}."
5. **Dado** eventos publicados antes desta story existir (EST-02 já em produção), **quando** a CMP-01 sobe, **então** um passo de recomposição (no deploy e no job diário) lê `IStockReservations.OpenShortagesAsync()` e cria ou acerta as linhas `BOMShortage`, sem duplicar.

**Pelo estoque mínimo (job diário)**
6. **Dado** o job diário `MinStockPurchaseRequestJob` (recorrente global, 06:00 São Paulo, `TenantJob` por tenant ativo), **quando** roda, **então** para cada item ativo com `min_stock` > 0 cuja posição (disponível + quantidade em SC/pedido em aberto, proposta; Pontos em aberto 1) está abaixo do mínimo, cria ou atualiza **uma** linha `MinStock` em aberto com a quantidade = mínimo − posição (arredondada para cima na unidade de estoque) e a necessidade = hoje + prazo padrão; item que voltou ao mínimo tem a linha em aberto cancelada.
7. **Dado** o job, **quando** roda duas vezes no mesmo dia ou em dois tenants, **então** não duplica nada e um tenant não vê o outro (os dois testes da ADR 0019 §12); os argumentos do job só têm o `tenantId`.
8. **Dado** linhas `MinStock` novas, **quando** o job termina, **então** quem tem `purchasing.edit` recebe uma notificação consolidada "{n} itens abaixo do mínimo." com link para a lista filtrada (`dedup_key` `min-stock:{data}`; R-COM-2, R-PRP-5).

**Lista, detalhe e cancelamento**
9. **Dado** `purchasing.view`, **quando** abro "Solicitações" (`/app/compras/solicitacoes`), **então** vejo número, origem (`Tag` "Manual", "Falta da BOM", "Estoque mínimo"), destino (projeto ou "Estoque geral"), OS, itens, necessidade mais próxima, situação (Aberta info, Em cotação, Pedida ok, Cancelada neutro) e o chip alerta "Precisa em {n} dias" / crítico "Atrasada" quando a necessidade é ≤ 7 dias ou já passou; filtros na URL; a visão "Por item" soma as linhas abertas de todas as SC por item (o comprador compra por item, não por SC).
10. **Dado** `purchasing.edit` e uma linha `Open`, **quando** edito quantidade ou necessidade, **então** grava com auditoria; linha `BOMShortage` editada a menor que a falta mostra o aviso "A OS {número} continua com falta de {x}."; linha automática editada não é mais sobrescrita pelo evento ("Editada à mão", proposta).
11. **Dado** `purchasing.edit`, **quando** cancelo uma SC ou linha em aberto com motivo, **então** fica `Cancelled`; SC com linha em cotação ou pedido não é cancelada (409 com o documento).
12. **Dado** o contrato `IPurchaseRequests` em `Purchasing.Contracts` (novo), **quando** a OS (aba Material) ou a lista de Faltas pedem `ForOrderAsync(productionOrderId)`, **então** recebem as linhas de SC da OS com a situação e o número, para o link "SC gerada" do protótipo.

**Permissões e segurança**
13. **Dado** o módulo `purchasing` não liberado, **quando** chamo, **então** 403 `module_not_released` e a tela `ModuleNotReleased` (o protótipo mostra Compras com cadeado); sem grupo, 403 `forbidden` e o botão some; criar sem `purchasing.create`, editar e cancelar sem `purchasing.edit`: 403. `RequireModule` e `perm:`; IDOR em `/purchase-requests/{id}/lines/{lineId}`; item, projeto e OS validados pelos Contracts dos donos.
14. **Dado** qualquer alteração de SC, **quando** abro "Histórico de alterações", **então** vejo antes/depois, usuário (ou "Sistema · Falta da BOM {OS}" / "Sistema · Estoque mínimo") e data.

## Regras de negócio e técnicas
- **SC por origem:** `BOMShortage` agrupa por OS; `MinStock` agrupa as linhas do dia numa SC por execução do job (proposta); `Manual` como o usuário montou.
- **Linhas** (03 + proposta): `request_id`, `item_id`, `quantity` (`numeric(18,4)`, unidade de estoque), `needed_by` (`date`), `production_order_id`, `bom_line_id`, `project_id`, `status` (Open/Quoting/Ordered/Cancelled), `manually_edited`, `ordered_quantity` (CMP-03a).
- **Situação da SC** derivada das linhas (todas canceladas → `Cancelled`; alguma em cotação → `Quoting`; todas pedidas → `Ordered`).
- **Sem efeito financeiro nem de estoque:** a SC não reserva nem gera CP; a previsão de caixa nasce no pedido aprovado (CMP-03a).
- Guid v7; numeração `SC` na mesma transação; `xmin`; auditoria `IAuditable`; datas `date`; job pelo `RecurringJobsRegistrar` com `BusinessTime.Zone`.

## Permissões, eventos e tabelas
- **Permissões:** `purchasing.view`, `purchasing.create`, `purchasing.edit`. Seed patch **proposto** `purchasing-permissions` (CA 1).
- **Eventos:** **consome** `StockShortageChanged` (EST-02). Nenhum publicado.
- **Contratos:** novo `IPurchaseRequests`; usa `IStockReservations`, `IStockBalances`, `IItems`, `IProjects`, `IProductionOrders`.
- **Tabelas:** `purchasing.purchase_requests` e `purchasing.purchase_request_lines` (primeira migration do schema `purchasing`); parâmetro `purchasing.default_lead_days` em `purchasing.parameters` (proposta, dono Purchasing, ADR 0016 adendo M6).

## Telas e estados
- **Fábrica → Compras → Solicitações** (`/app/compras/solicitacoes`, detalhe `/app/compras/solicitacoes/$id`; `purchasing` ganha as abas Solicitações, Cotações, Pedidos e Recebimentos no `modules.ts` ao longo do marco).
- **Verbos:** "Enviar solicitação" / "Solicitação enviada"; "Cancelar solicitação" / "Solicitação cancelada".
- **Estados:** carregando; vazio ("Nenhuma solicitação aberta. As faltas da BOM e o estoque mínimo abrem solicitações sozinhos." + "Nova solicitação"); erro; modo leitura.
- **Propositivo:** destino e necessidade preenchidos a partir da OS; "Por item" para comprar junto; da linha, `NextSteps` "Cotar estes itens" (CMP-02) e "Pedir direto ao último fornecedor" (CMP-03a); a falta da BOM entra na "Minha pauta" quando ela existir (DSH-04).
- **400 px:** lista com número, itens, necessidade e situação; linhas em cartões.
- **Ajuda:** `Ajuda/compras-solicitacoes.md`, `Ajuda/compras-solicitacao.md` (abas `itens` padrão, `historico`); `<Dica>` em "Estoque mínimo", "Falta da BOM" e "Necessidade".

## Plano de testes
- **Unidade:** posição contra o mínimo (CA 6) e data de necessidade (CA 3).
- **Integração (Testcontainers):** seed patch (CA 1); manual com idempotência (CA 2); consumidor da falta sem duplicar, reduzir e cancelar, linha em cotação intacta (CA 3, 4); recomposição pelos `OpenShortagesAsync` (CA 5); job do mínimo com execução dupla e dois tenants (CA 6, 7); notificação consolidada (CA 8); lista e "Por item" (CA 9); edição e cancelamento (CA 10, 11); `IPurchaseRequests` (CA 12); três camadas e IDOR (CA 13); auditoria (CA 14).
- **E2E (Playwright):** liberar BOM com falta e ver a SC; criar SC manual; tela a 400 px.

## Fora do escopo
- Cotação: CMP-02. Pedido: CMP-03a. Recebimento: CMP-04.
- Aprovação da solicitação: não existe no MVP (a aprovação é do pedido, Pontos em aberto 3).
- Ponto de pedido e estoque máximo: fora (o PRD só tem o mínimo).

## Pontos em aberto
1. **Gatilho do mínimo** (PO): proposta: disponível + quantidade em SC e pedido em aberto < mínimo, comprando até o mínimo; alternativas: físico < mínimo; comprar até um máximo.
2. **Prazo padrão de compra** (PO): proposta: parâmetro `purchasing.default_lead_days` (15 dias) em Configurações → Parâmetros, com `settings.edit`; depois, o prazo do fornecedor da cotação.
3. **Aprovação da solicitação** (PO): proposta: sem aprovação; o controle está no pedido com alçada (CMP-03a).
4. **Serviço na solicitação** (PO): proposta: itens do tipo Serviço não entram (não têm estoque); serviço comprado vira título a pagar manual ou pedido sem recebimento físico, fora do MVP.
5. **Permissões do seed** (PO): seed patch proposto do CA 1.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers, com os dois testes de job) e E2E passando.
- [ ] Migration criada (não destrutiva), testada sobre cópia do banco do tenant `konebras`; seed patch.
- [ ] Permissões só do `ModuleCatalog`; job registrado no `RecurringJobsRegistrar`.
- [ ] Telas com estados, inclusive a 400 px.
- [ ] Ajuda da lista e do detalhe; teste de cobertura passando.
- [ ] Padrões, alertas acionáveis e próximos passos avaliados.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `IPurchasingDb` com `PurchaseRequest` e `PurchaseRequestLine`; schema `purchasing`.
- [ ] `PurchaseRequestService`, consumidor `StockShortageChangedHandler`, `MinStockPurchaseRequestJob` e a recomposição.
- [ ] `IPurchaseRequests` em `Purchasing.Contracts`; `purchasing.parameters` se o ponto 2 for aprovado.
- [ ] Endpoints `/api/v1/purchasing/purchase-requests`; telas, `modules.ts`, ajudas, seed patch.

## Quality gates
- Agentes previstos: @dev, @architect (consumidor e recomposição), @qa.
- Gate: CA 3, 4, 5, 6 e 7 obrigatórios.

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

- **Correções feitas na validação:** recomposição das faltas publicadas antes do consumidor existir (CA 5); linha automática editada à mão não é sobrescrita (CA 10); o seed não tinha Compras para nenhum grupo: seed patch proposto, sem `approve`.
- **Pré-condições para `Ready`:** pontos em aberto 1, 2 e 5.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
