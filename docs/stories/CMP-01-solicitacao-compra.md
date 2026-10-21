# CMP-01 — Solicitação de compra

| Campo | Valor |
|---|---|
| ID | CMP-01 |
| Título | Solicitação de compra manual, automática pela falta de material da BOM liberada (aviso `StockShortageChanged`, recomposta a partir das faltas abertas) e automática pelo estoque mínimo (job diário), sem duplicar; lista de solicitações com o que precisa ser comprado e para quando; ação `purchasing.receive` no catálogo e seed de Compras |
| Tamanho | M |
| Status | Ready |
| Requisitos | R-CMP-1 (manual ou automática pela falta da BOM ou pelo estoque mínimo) · R-CMP-6 (vínculo com projeto ou estoque geral) · R-COM-2 (notificação de estoque mínimo) · R-COM-4 (numeração SC) · R-PRP-2 · R-PRP-4/5 (falta de material na pauta, alerta acionável) · R-SEC-5 |
| Dependências | EST-02 (`StockShortageChanged`, `IStockReservations.OpenShortagesAsync`) |
| Bloqueia | CMP-03a, CMP-02, EST-06 |
| Ordem de execução | 14 (Marco 3.3 "Comprar o que falta") |

## User story
Como **comprador**, quero **que o sistema abra sozinho a solicitação do que falta para as OS e do que ficou abaixo do mínimo, e que eu possa pedir o resto à mão**, para **comprar a tempo, sem esquecer nada e sem comprar duas vezes**.

## Contexto e referências
- PRD §1.1 problema 4 ("compras são reativas"); §5.10 R-CMP-1 e R-CMP-6; §5.15a R-PRP-2/4/5 (faltas de material na pauta).
- `04-fluxos-estados` §2: `BomReleased` → se faltar disponível, Purchasing cria solicitação (`origin BOMShortage`). §4: numeração `SC-AAAA-0001` (já em `NumberFormats`).
- `03-modelo-dados` → `purchasing.purchase_requests` (`number`, `origin` Manual/BOMShortage/MinStock, `project_id`, `production_order_id`, `status` Open/Quoting/Ordered/Cancelled), `purchase_request_lines` (`request_id`, `item_id`, `quantity`, `needed_by`).
- Contratos usados, todos já entregues antes desta story: `IStockBalances` (EST-01a), estoque mínimo do item (CAD-02a, `IItems`), `IProductionOrders` (PRD-01), `IProjects` com `PromisedDate` (COM-04a, PRD-01).
- `ModuleCatalog`: `purchasing` (`view`, `create`, `edit`, `approve` = "pedido de compra dentro da alçada", `delete`). **No seed, nenhum grupo além do Proprietário tem `purchasing`** (`GroupSeeder`).
- Revisão pré-Fase 3: `docs/revisoes/2026-10-21-revisao-pre-fase3.md` (D3, D8, tabela 1.1 itens 30 a 34, §2.1 "Reservas e faltas", §2.2 CMP-01, §2.3).
- ADR 0023 §6 (`StockShortageChanged` é só aviso; reconciliação a partir da fonte); ADR 0024 §5 (segregação em compras, seed do Almoxarifado); ADR 0003, adendo de 21/10/2026 (`purchasing.receive`); ADR 0008 (delegação); ADR 0019 §12 (job idempotente, dois testes, `dedup_key`); ADR 0006 (job recorrente global que enfileira por tenant); ADR 0016 adendo M6 (parâmetros por módulo).

## Critérios de aceite
**Catálogo e permissões do seed**
1. **Dado** o deploy desta story, **quando** o `ModuleCatalog` carrega, **então** o módulo `purchasing` declara a ação específica `purchasing.receive` com o texto "Registrar recebimento: dar entrada no estoque do que chegou de um pedido de compra aprovado." (ADR 0003, adendo de 21/10/2026), concedível pela regra de delegação da ADR 0008; nenhum endpoint desta story usa a ação (o recebimento é da CMP-04).
2. **Dado** um tenant novo ou existente no próximo deploy, **quando** o seed patch `purchasing-permissions` roda, **então** o Almoxarifado do seed ganha `purchasing.view` + `purchasing.receive`, sem `purchasing.create`, `purchasing.edit` e `purchasing.approve` (D8, ADR 0024 §5), e o Financeiro ganha `purchasing.view`, uma vez só, com o `perm_version` incrementado; `purchasing.create`, `purchasing.edit` e `purchasing.approve` continuam só no Proprietário (o tenant concede a quem compra).

**Manual**
3. **Dado** `purchasing.create`, **quando** clico "Nova solicitação", informo as linhas (item ativo, quantidade na unidade de estoque ou convertida, data de necessidade `date` ≥ hoje, preenchida com hoje + prazo padrão e a tag "Sugerido"), o destino (projeto em andamento, validado por `IProjects`, ou "Estoque geral"; R-CMP-6) e uma observação, e clico "Enviar solicitação", **então** nasce a SC `Open` com número `SC-2026-0001`, origem `Manual`, sem aprovação (ponto 32), com `Idempotency-Key`, e a mensagem "Solicitação enviada".
4. **Dado** um item do tipo Serviço, **quando** tento incluí-lo na solicitação, **então** 422 "Serviço não entra na solicitação de compra. Lance um título a pagar manual." (ponto 33).

**Pela falta da BOM**
5. **Dado** um `StockShortageChanged` de uma OS, **quando** o consumidor processa, **então** trata o evento só como aviso (ADR 0023 §6): lê as faltas abertas da OS na fonte (`IStockReservations.OpenShortagesAsync`) e recompõe as linhas `BOMShortage` em aberto da OS até a falta atual, numa SC `Open` por OS, com item, quantidade, OS, projeto, `bom_line_id` e necessidade = data prometida do projeto − `purchasing.default_lead_days` (15 dias; sem data prometida, hoje + 15 dias); reentregas, eventos fora de ordem e faltas repetidas nunca duplicam (inbox e chave `(production_order_id, bom_line_id)`).
6. **Dado** a falta diminuiu (chegou material ou a BOM foi revista) ou zerou na fonte, **quando** o consumidor recompõe, **então** a linha em aberto é reduzida ou cancelada (motivo "Falta resolvida pelo estoque"); linha já em cotação ou pedido (`Quoting`/`Ordered`) não muda e o comprador recebe o aviso "A falta de {item} na {OS} caiu para {x}; a linha já está em {cotação/pedido}."; linha "Editada à mão" (CA 13) não é sobrescrita.
7. **Dado** faltas abertas antes desta story existir (EST-02 já em produção, D3), **quando** a CMP-01 sobe, **então** um passo de recomposição na implantação lê `IStockReservations.OpenShortagesAsync()` de todas as OS e cria ou acerta as linhas `BOMShortage`, sem duplicar, com a marca "Calculado na implantação" (padrão 11); o job diário repete a mesma recomposição como rede de segurança.
8. **[Segurança]** **Dado** o módulo `purchasing` não liberado no tenant, **quando** o consumidor de `StockShortageChanged`, a recomposição ou o job do mínimo rodam para esse tenant, **então** conferem o módulo liberado (`tenant_modules`) antes de qualquer escrita e não criam nada (a mensagem é dada como processada, com log informativo); quando o módulo for liberado, a recomposição do job diário cria as linhas a partir das faltas abertas, sem perder nenhuma (§2.2 CMP-01).

**Pelo estoque mínimo (job diário)**
9. **Dado** o job diário `MinStockPurchaseRequestJob` (recorrente global, 06:00 São Paulo, `TenantJob` por tenant ativo), **quando** roda, **então** para cada item ativo com `min_stock` > 0 calcula a posição = disponível (todos os locais, `IStockBalances`) + o que já está em compra para o estoque geral (linhas de SC "Estoque geral" ainda não pedidas, fora a própria linha `MinStock` aberta, mais o saldo a receber dos pedidos em aberto pelo `IPurchaseOrders.OpenQuantityByItemAsync` com destino "Estoque geral", ligado pela CMP-03a CA 24; até lá essa parcela é zero); se a posição está abaixo do mínimo (ponto 30), cria ou atualiza **uma** linha `MinStock` em aberto com a quantidade = mínimo − posição (arredondada para cima na unidade de estoque; compra até o mínimo) e a necessidade = hoje + `purchasing.default_lead_days`; item que voltou ao mínimo tem a linha em aberto cancelada.
10. **Dado** o job, **quando** roda duas vezes no mesmo dia ou em dois tenants, **então** não duplica nada e um tenant não vê o outro (os dois testes da ADR 0019 §12); os argumentos do job só têm o `tenantId`.
11. **Dado** linhas `MinStock` novas, **quando** o job termina, **então** quem tem `purchasing.edit` recebe uma notificação consolidada "{n} itens abaixo do mínimo." com link para a lista filtrada (`dedup_key` `min-stock:{data}`; R-COM-2, R-PRP-5).

**Lista, detalhe e cancelamento**
12. **Dado** `purchasing.view`, **quando** abro "Solicitações" (`/app/compras/solicitacoes`), **então** vejo número, origem (`Tag` "Manual", "Falta da BOM", "Estoque mínimo"), destino (projeto ou "Estoque geral"), OS, itens, necessidade mais próxima, situação (Aberta info, Em cotação, Pedida ok, Cancelada neutro) e o chip alerta "Precisa em {n} dias" ou crítico "Atrasada {n} d" quando a necessidade é ≤ 7 dias ou já passou; para quem tem `purchasing.edit`, o filtro padrão é "Aguardando você" (SC com linha aberta, ainda sem cotação nem pedido; padrão 5); filtros na URL; a visão "Por item" soma as linhas abertas de todas as SC por item (o comprador compra por item, não por SC).
13. **Dado** `purchasing.edit` e uma linha `Open`, **quando** edito quantidade ou necessidade, **então** grava com auditoria; linha `BOMShortage` editada a menor que a falta mostra o aviso "A OS {número} continua com falta de {x}."; linha automática editada recebe a marca "Editada à mão" e não é mais sobrescrita pela recomposição nem pelo job.
14. **Dado** `purchasing.edit`, **quando** cancelo uma SC ou linha em aberto com motivo, **então** fica `Cancelled`; SC com linha em cotação ou pedido não é cancelada (409 com o documento).
15. **Dado** o contrato `IPurchaseRequests` em `Purchasing.Contracts` (novo), **quando** a OS (aba Material) ou a lista de Faltas pedem `ForOrderAsync(productionOrderId)`, **então** recebem as linhas de SC da OS com a situação e o número, para o link "SC gerada" do protótipo.

**Permissões e segurança**
16. **Dado** o módulo `purchasing` não liberado, **quando** chamo, **então** 403 `module_not_released` e a tela `ModuleNotReleased` (o protótipo mostra Compras com cadeado); sem grupo, 403 `forbidden` e o botão some; criar sem `purchasing.create`, editar e cancelar sem `purchasing.edit`: 403 (o Almoxarifado do seed, só com `view` + `receive`, vê a lista e recebe 403 ao criar). `RequireModule` e `perm:`; IDOR em `/purchase-requests/{id}/lines/{lineId}`; item, projeto e OS validados pelos Contracts dos donos.
17. **Dado** qualquer alteração de SC, **quando** abro "Histórico de alterações", **então** vejo antes/depois, usuário (ou "Sistema · Falta da BOM {OS}" / "Sistema · Estoque mínimo" / "Sistema · Calculado na implantação") e data.

## Regras de negócio e técnicas
- **SC por origem:** `BOMShortage` agrupa por OS; `MinStock` agrupa as linhas do dia numa SC por execução do job; `Manual` como o usuário montou.
- **Linhas:** `request_id`, `item_id`, `quantity` (`numeric(18,4)`, unidade de estoque), `needed_by` (`date`), `production_order_id`, `bom_line_id`, `project_id`, `status` (Open/Quoting/Ordered/Cancelled), `manually_edited`, `calculated_on_onboarding`, `ordered_quantity` (CMP-03a).
- **Falta é aviso, a fonte manda:** o consumidor nunca aplica o delta do evento; recompõe a partir de `OpenShortagesAsync` (ADR 0023 §6). O mesmo código serve ao consumidor, à recomposição na implantação e ao job diário.
- **Situação da SC** derivada das linhas (todas canceladas → `Cancelled`; alguma em cotação → `Quoting`; todas pedidas → `Ordered`).
- **Sem aprovação e sem efeito financeiro nem de estoque:** a SC não reserva nem gera CP; o controle e a previsão de caixa nascem no pedido aprovado (CMP-03a).
- Guid v7; numeração `SC` na mesma transação; `xmin`; auditoria `IAuditable`; datas `date`; job pelo `RecurringJobsRegistrar` com `BusinessTime.Zone`; conferência do módulo liberado antes de qualquer escrita automática (CA 8).

## Permissões, eventos e tabelas
- **Permissões:** `purchasing.view`, `purchasing.create`, `purchasing.edit`; ação nova `purchasing.receive` no catálogo (CA 1, usada na CMP-04). Seed patch `purchasing-permissions` (CA 2).
- **Eventos:** **consome** `StockShortageChanged` (EST-02), só como aviso. Nenhum publicado.
- **Contratos:** novo `IPurchaseRequests`; usa `IStockReservations`, `IStockBalances`, `IItems`, `IProjects`, `IProductionOrders`; o saldo a receber do job do mínimo vem do `IPurchaseOrders.OpenQuantityByItemAsync` (CMP-03a CA 24).
- **Tabelas:** `purchasing.purchase_requests` e `purchasing.purchase_request_lines` (primeira migration do schema `purchasing`); `purchasing.parameters` com `default_lead_days` = 15 (dono Purchasing, ADR 0016 adendo M6), editável em Configurações → Parâmetros com `settings.edit`.

## Telas e estados
- Padrões de tela da Fase 3: 2, 5, 6, 11.
- **Fábrica → Compras → Solicitações** (`/app/compras/solicitacoes`, detalhe `/app/compras/solicitacoes/$id`; `purchasing` ganha as abas Solicitações, Cotações, Pedidos e Recebimentos no `modules.ts` ao longo do marco).
- **Verbos:** "Enviar solicitação" / "Solicitação enviada"; "Cancelar solicitação" / "Solicitação cancelada".
- **Estados:** carregando; vazio ("Nenhuma solicitação aberta. As faltas da BOM e o estoque mínimo abrem solicitações sozinhos." + "Nova solicitação"); erro; modo leitura.
- **Propositivo:** destino e necessidade preenchidos a partir da OS; "Por item" para comprar junto; da linha, `NextSteps` "Cotar estes itens" (CMP-02) e "Pedir direto ao último fornecedor" (CMP-03a); a falta da BOM entra na "Minha pauta" quando ela existir (DSH-04).
- **400 px:** lista com número, itens, necessidade e situação; linhas em cartões.
- **Ajuda:** `Ajuda/compras-solicitacoes.md`, `Ajuda/compras-solicitacao.md` (abas `itens` padrão, `historico`); `<Dica>` em "Estoque mínimo", "Falta da BOM", "Necessidade" e "Calculado na implantação".

## Plano de testes
- **Unidade:** posição contra o mínimo, com e sem pedidos em aberto e sem contar a própria linha (CA 9); data de necessidade com e sem data prometida (CA 5); recomposição pela falta atual (CA 5, 6).
- **Arquitetura:** `purchasing.receive` declarada no `ModuleCatalog` (CA 1).
- **Integração (Testcontainers):** seed patch idempotente, Almoxarifado sem `create`/`edit` (CA 2); manual com idempotência e Serviço recusado (CA 3, 4); consumidor sem duplicar, com eventos fora de ordem, reduzir e cancelar, linha em cotação e editada intactas (CA 5, 6); recomposição na implantação com a marca (CA 7); módulo não liberado não cria nada e a liberação recupera (CA 8); job do mínimo com execução dupla e dois tenants (CA 9, 10); notificação consolidada (CA 11); lista, "Aguardando você" e "Por item" (CA 12); edição e cancelamento (CA 13, 14); `IPurchaseRequests` (CA 15); três camadas e IDOR (CA 16); auditoria (CA 17).
- **E2E (Playwright):** liberar BOM com falta e ver a SC; criar SC manual; tela a 400 px.

## Fora do escopo
- Cotação: CMP-02. Pedido: CMP-03a. Recebimento: CMP-04 (usa `purchasing.receive`).
- Aprovação da solicitação: não existe (ponto 32; a aprovação é do pedido).
- Serviço na solicitação: fora do MVP (ponto 33).
- Ponto de pedido e estoque máximo: fora (o PRD só tem o mínimo).

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fase 3, tabela 1.1, itens 30–34):
1. Gatilho do mínimo: disponível + pedidos em aberto < mínimo; comprar até o mínimo (CA 9).
2. Prazo padrão de compra: 15 dias, em `purchasing.parameters` (CA 5, 9).
3. Aprovação da SC: sem aprovação (CA 3).
4. Serviço na SC: fora do MVP (CA 4).
5. Seed de Compras: aprovado, com o Almoxarifado só em `purchasing.view` + `purchasing.receive` (D8; CA 1, 2).

**Pré-condição externa:** nenhuma.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, arquitetura, integração (Testcontainers, com os dois testes de job) e E2E passando.
- [ ] Migration criada (não destrutiva), testada sobre cópia do banco do tenant `konebras`; seed patch `purchasing-permissions` idempotente.
- [ ] Permissões só do `ModuleCatalog` (`purchasing.receive` incluída); job registrado no `RecurringJobsRegistrar`.
- [ ] ADR 0023 §6 aplicada (falta como aviso, recomposição pela fonte); D3 aplicada (recomposição na implantação).
- [ ] Telas com estados, inclusive a 400 px.
- [ ] Ajuda da lista e do detalhe; teste de cobertura passando.
- [ ] Padrões, alertas acionáveis e próximos passos avaliados.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `IPurchasingDb` com `PurchaseRequest` e `PurchaseRequestLine`; schema `purchasing`; `purchasing.parameters`.
- [ ] `PurchaseRequestService`, `ShortageRecomposer` (usado pelo consumidor `StockShortageChangedHandler`, pela recomposição na implantação e pelo job), `MinStockPurchaseRequestJob`.
- [ ] `purchasing.receive` no `ModuleCatalog`; seed patch `purchasing-permissions`.
- [ ] `IPurchaseRequests` em `Purchasing.Contracts`.
- [ ] Endpoints `/api/v1/purchasing/purchase-requests`; telas, `modules.ts`, ajudas.

## Quality gates
- Agentes previstos: @dev, @architect (consumidor e recomposição), @qa.
- Gate: CA 2, 5, 6, 7, 8, 9 e 10 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fase 3).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK (ajustado: comprador) |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK (ajustado) |
| 5 | CAs de segurança e de permissão (3 camadas) | OK (ajustado: CA 8) |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: ordem 14) |
| 7 | Plano de testes cobre os CAs | OK (ajustado) |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK |

- **Correções feitas na validação (21/10/2026, primeira validação):** recomposição das faltas publicadas antes do consumidor existir; linha automática editada à mão não é sobrescrita; o seed não tinha Compras para nenhum grupo: seed patch proposto, sem `approve`.
- **Correções da revalidação (21/10/2026):** `purchasing.receive` entra no `ModuleCatalog` (CA 1) e o Almoxarifado fica só com `view` + `receive` (CA 2, D8); o evento de falta é só aviso e a SC é recomposta pelas faltas abertas (CA 5, 6, ADR 0023 §6); recomposição na implantação com "Calculado na implantação" (CA 7, D3); job e consumidores conferem o módulo liberado (CA 8, §2.2); gatilho do mínimo e prazo de 15 dias definitivos (CA 9); Serviço recusado (CA 4); filtro "Aguardando você" (CA 12); depende só da EST-02 e vira a primeira do Marco 3.3. CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma de decisão de story.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO (D3, D8, tabela 1.1 itens 30–34) e correções da revisão pré-Fase 3 (§2.1 reservas e faltas, ADR 0023 §6; §2.2 CMP-01; §2.3 padrões 2, 5, 6, 11); nova ordem (Marco 3.3, ordem 14) | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
