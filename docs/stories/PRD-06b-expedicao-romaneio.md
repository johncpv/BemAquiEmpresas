# PRD-06b — Expedição: romaneio e saída do produto acabado

| Campo | Valor |
|---|---|
| ID | PRD-06b |
| Título | Romaneio de expedição do projeto com os produtos acabados e as séries, dados de transporte e PDF; confirmação da expedição com a saída do produto acabado do estoque (série `Shipped`); preparação para o vínculo com a NF-e de saída da Fase 5 |
| Tamanho | M (quebra da PRD-06) |
| Status | Draft |
| Requisitos | R-PRD-8 (expedição: romaneio, vínculo com NF-e de saída, baixa do produto acabado) · R-EST-6 (saída por venda) · R-EST-8 (série) · R-COM-3 (PDF do romaneio) · R-COM-4 · R-PRP-2 · R-PRP-7 · R-SEC-5 |
| Dependências | PRD-06a (produto acabado com série em estoque), EST-01a (`IStockMovements`, tipo `Shipment`), COM-03 (`IPdfRenderer`, `StoreGeneratedAsync`), COM-04a (`IProjects`, endereço da obra), CAD-01a (transportadora) |
| Bloqueia | FIS-03 (Fase 5: NF-e de saída a partir da expedição), EST-04 (série expedida para qual projeto) |
| Ordem de execução | 20 (Marco 3.5 "Fechar a OS") |

## User story
Como **supervisor de expedição/almoxarife**, quero **montar o romaneio do que vai para a obra, com as séries, e confirmar a saída**, para **o estoque baixar o produto acabado e ficar registrado o que foi entregue a cada cliente**.

## Contexto e referências
- PRD §4 ("Expedição: nº de série ─► NF-e"); §5.7 R-PRD-8.
- `04-fluxos-estados` §2: `InvoiceAuthorized` (Fiscal) → Inventory baixa produto acabado (`Shipment`); anexa XML/PDF ao projeto. **Na Fase 3 não há NF-e** (Fiscal é Fase 5): ver Pontos em aberto 1.
- `03-modelo-dados` → `stock_movements.type = Shipment`; `serials.status` (InStock/Shipped), `outgoing_invoices.shipment_id` (Fase 5). O romaneio não está no modelo (proposta abaixo). `04-fluxos` §4 não tem o tipo de numeração (proposta `ROM`).
- ADR 0022 (PDF); ADR 0019 §7 (arquivo gerado pelo sistema).
- `04-fluxos-estados` §1 "Projeto": a expedição não muda o projeto (a entrega é a OS de instalação ou o registro manual, D15).

## Critérios de aceite
**Montar o romaneio**
1. **Dado** `production.create` (Pontos em aberto 3) e um projeto com produto acabado em estoque (`serials` `InStock` com o `project_id` do projeto, ou produto sem série com saldo vindo das OS do projeto), **quando** clico "Novo romaneio" no projeto ou em "Fábrica → Produção → Expedição", **então** o formulário vem com as séries e quantidades disponíveis do projeto (padrão: todas), o endereço de entrega (o da obra do projeto, `IProjects` + `IParties.AddressesAsync`; editável), a transportadora (terceiro com papel Transportadora, opcional), a placa e o motorista (texto, opcional, `[PersonalData]`), volumes e peso (opcionais) e a data prevista; ao salvar, nasce o romaneio `Draft` com número proposto `ROM-2026-0001` (Pontos em aberto 2), com `Idempotency-Key`.
2. **Dado** uma série já em outro romaneio não cancelado, **quando** tento incluí-la, **então** 422 "A série {série} já está no romaneio {número}."; série de outro projeto é recusada (422) — com a opção "Incluir produto de estoque geral" só para quem tem `inventory.edit` (proposta).
3. **Dado** o romaneio `Draft`, **quando** clico "Baixar PDF", **então** o PDF (`ShipmentPdfDocument`, `IPdfRenderer`) traz empresa, número, data, cliente, endereço de entrega, transportadora, itens com descrição, quantidade e séries, volumes e peso, e campos de assinatura de quem recebeu; sem valores (o romaneio não é documento fiscal) e sem custo (teste de extração de texto).

**Confirmar a expedição**
4. **Dado** `production.edit` e o romaneio `Draft`, **quando** clico "Confirmar expedição" e confirmo no diálogo com o impacto (por item: saída do estoque, séries que passam a "Expedido", local), **então** na mesma transação o romaneio vai a `Shipped` com data e usuário, o PDF fica guardado como arquivo do sistema (dono `Shipment`) e o evento `ShipmentDispatched` (`{ shipmentId, projectId, lines: [{ itemId, serialIds, quantity, locationId }] }`) vai para o outbox; a mensagem diz "Expedição confirmada".
5. **Dado** o `ShipmentDispatched`, **quando** o consumidor do Inventory processa, **então** lança as saídas `Shipment` pelo `IStockMovements` (custo médio do momento do produto, `source_type = Shipment`), as séries vão a `Shipped`, e o movimento **não** gera custo de projeto (o custo do equipamento já está no projeto pelas baixas e pelas horas); idempotente pela inbox e por `(Shipment, romaneioId, linha)`; saldo insuficiente (alguém tirou o produto antes) → o romaneio volta a `Draft` com o motivo e o autor é notificado (proposta).
6. **Dado** o romaneio expedido, **quando** o `NextSteps` aparece, **então** oferece "Registrar entrega" (projeto sem OS de instalação, COM-04b, com `projects.approve`) ou "Ver OS de instalação" (COM-06), e "Emitir NF-e" aparece como "Disponível na Fase 5" (sem ação).

**Cancelar e estornar**
7. **Dado** um romaneio `Draft`, **quando** clico "Cancelar romaneio" com motivo, **então** fica `Cancelled` e as séries ficam livres.
8. **Dado** um romaneio `Shipped` que não saiu de fato (erro de lançamento) e sem NF-e (Fase 5), **quando** clico "Estornar expedição" (`production.approve`, motivo) e confirmo no diálogo `destructive`, **então** o evento `ShipmentReversed` faz o Inventory lançar as entradas inversas (séries voltam a `InStock`) e o romaneio fica `Reversed`; com NF-e autorizada (Fase 5), o estorno passa a exigir o cancelamento da nota.

**Consulta, permissões e auditoria**
9. **Dado** `production.view`, **quando** abro "Expedição" (`/app/producao/expedicao`), **então** vejo a lista "Prontos para expedir" (projetos com produto acabado em estoque) e os romaneios com número, projeto, cliente, itens, data e situação (Rascunho neutro, Expedido ok, Cancelado neutro, Estornado neutro); o projeto mostra os romaneios na aba Itens e OS e na linha do tempo.
10. **Dado** o módulo `production` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; criar sem `production.create`, confirmar e cancelar sem `production.edit`, estornar sem `production.approve`: 403. `RequireModule` e `perm:`; IDOR em `/shipments/{id}/lines/{lineId}`; projeto, endereço e transportadora validados pelos Contracts dos donos.
11. **[Segurança]** **Dado** duas confirmações simultâneas, ou a mesma série em dois romaneios confirmados ao mesmo tempo, **quando** chegam, **então** só um grava (`xmin` e índice único parcial da série em romaneio não cancelado), e a saída do estoque não duplica.
12. **Dado** qualquer mudança no romaneio, **quando** abro o histórico, **então** vejo antes/depois, usuário e data; motorista e placa mascarados no diff (`[PersonalData]`).

## Regras de negócio e técnicas
- **Romaneio** (proposta): `production.shipments` (`number`, `project_id`, `party_id`, `delivery_address` (cópia do endereço), `carrier_party_id`, `vehicle_plate`, `driver_name` (`[PersonalData]`), `volumes`, `gross_weight`, `planned_date`, `status` Draft/Shipped/Cancelled/Reversed, `shipped_at`, `shipped_by`, `xmin`) e `shipment_lines` (`shipment_id`, `item_id`, `quantity`, `serial_ids` ou tabela `shipment_line_serials`, `location_id`). O dono é a Produção (R-PRD-8); a saída do estoque é do Inventory pelo evento.
- **Fase 5:** a NF-e de saída referencia o romaneio (`outgoing_invoices.shipment_id`); a regra de quem baixa o estoque (romaneio ou NF autorizada) é o Pontos em aberto 1.
- Guid v7; `xmin`; auditoria `IAuditable`; quantidades `numeric(18,4)`, peso `numeric(18,4)`.

## Permissões, eventos e tabelas
- **Permissões:** `production.view`, `production.create` (montar), `production.edit` (confirmar, cancelar), `production.approve` (estornar).
- **Eventos (`Production.Contracts`):** `ShipmentDispatched`, `ShipmentReversed`; consumidor no Inventory.
- **Contratos:** `IStockMovements`, `IStockSerials` (proposta, Inventory: séries disponíveis por projeto), `IProjects`, `IParties`.
- **Tabelas:** `production.shipments`, `production.shipment_lines` (propostas); dono de anexo `Shipment`.

## Telas e estados
- **Fábrica → Produção → Expedição** (`/app/producao/expedicao`, aba nova no `modules.ts`), **romaneio** (`/app/producao/expedicao/$id`, abas Itens, Documentos, Histórico de alterações), botão "Novo romaneio" no projeto.
- **Verbos:** "Salvar romaneio" / "Romaneio salvo"; "Confirmar expedição" / "Expedição confirmada"; "Cancelar romaneio" / "Romaneio cancelado"; "Estornar expedição" / "Expedição estornada".
- **Estados:** carregando; vazio ("Nada pronto para expedir. As OS concluídas aparecem aqui." + "Ver OS prontas para concluir"); erro; processamento assíncrono da saída; modo leitura do expedido.
- **Propositivo:** séries e endereço preenchidos; transportadora sugerida pela última usada com o cliente; próximos passos do CA 6.
- **400 px:** itens e séries em cartões; diálogo em tela cheia.
- **Ajuda:** `Ajuda/producao-expedicao.md`, `Ajuda/producao-romaneio.md` (abas `itens` padrão, `documentos`, `historico`); `<Dica>` em "Romaneio" ("Lista do que sai da fábrica; não é nota fiscal.").

## Plano de testes
- **Unidade:** séries disponíveis por projeto; regras de inclusão (CA 2).
- **Integração (Testcontainers, obrigatória: movimenta estoque):** montar com idempotência (CA 1); série repetida e de outro projeto (CA 2); PDF sem valores nem custo na imagem Alpine (CA 3); confirmar com evento e saída `Shipment` sem custo de projeto, idempotente (CA 4, 5); saldo insuficiente devolve a rascunho (CA 5); cancelar e estornar (CA 7, 8); três camadas e IDOR (CA 10); concorrência (CA 11); auditoria mascarada (CA 12).
- **E2E (Playwright):** concluir OS, montar romaneio, baixar PDF e confirmar a expedição; tela a 400 px.

## Fora do escopo
- NF-e de saída, DANFE e XML ao cliente: FIS-03 (Fase 5).
- Entrega técnica e aceite: COM-06 (já existe). Registro de entrega sem instalação: COM-04b (já existe).
- Expedição de venda avulsa (sem projeto): Fase 10.

## Pontos em aberto
1. **Quem baixa o estoque** (PO): o `04-fluxos` §2 faz a baixa do produto acabado na NF-e autorizada (`InvoiceAuthorized`, Fase 5). Sem NF-e na Fase 3, proposta: a confirmação do romaneio baixa o estoque (CA 5), e na Fase 5 a NF-e referencia o romaneio sem baixar de novo; alternativa: o romaneio só reserva e a baixa espera a NF-e (o estoque fica com o produto até a Fase 5).
2. **Numeração** (PO): proposta `ROM-AAAA-0001`.
3. **Quem monta o romaneio** (PO): proposta: `production.create` (Supervisão e Engenharia do seed); alternativa: Almoxarifado com `inventory.create`.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers, PDF na imagem Alpine) e E2E passando.
- [ ] Migration criada (não destrutiva), testada sobre cópia do banco do tenant `konebras`.
- [ ] Permissões só do `ModuleCatalog`; dono de anexo `Shipment` registrado.
- [ ] Telas com estados, inclusive a 400 px.
- [ ] Ajudas da lista e do romaneio; teste de cobertura passando.
- [ ] Confirmação com impacto e próximos passos aplicados.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` e `docs/04-fluxos-estados.md` (romaneio e a regra do ponto 1) atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `Shipment` e `ShipmentLine` (Domain); `ShipmentService` (montar, confirmar, cancelar, estornar).
- [ ] `ShipmentPdfDocument`; consumidor no Inventory (`Shipment` e séries).
- [ ] `IStockSerials` no Inventory; tela de expedição, romaneio e ajudas.

## Quality gates
- Agentes previstos: @dev, @qa.
- Gate: CA 4, 5, 8 e 11 obrigatórios.

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

- **Correções feitas na validação:** a baixa pela NF-e do `04-fluxos` não existe na Fase 3: ponto em aberto (1) com proposta, sem decidir; `Shipment` fora do custo do projeto (CA 5); PDF sem valores (CA 3).
- **Pré-condições para `Ready`:** ponto em aberto 1.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da PRD-06) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
