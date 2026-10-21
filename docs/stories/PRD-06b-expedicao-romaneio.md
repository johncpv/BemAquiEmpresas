# PRD-06b — Expedição: romaneio e saída do produto acabado

| Campo | Valor |
|---|---|
| ID | PRD-06b |
| Título | Romaneio de expedição do projeto (`ROM-AAAA-0001`) com os produtos acabados e as séries, dados de transporte e PDF; a confirmação baixa o produto acabado do estoque de forma síncrona (série `Shipped`, 409 `insufficient_stock`); produto do estoque geral confirmado por outra pessoa com `inventory.approve`; na Fase 5 a NF-e referencia o romaneio e nunca baixa de novo |
| Tamanho | M |
| Status | Ready |
| Requisitos | R-PRD-8 (expedição: romaneio, vínculo com NF-e de saída, baixa do produto acabado) · R-EST-6 (saída por venda) · R-EST-8 (série) · R-COM-3 (PDF do romaneio) · R-COM-4 · R-PRP-2 · R-PRP-7 · R-SEC-5 |
| Dependências | PRD-06a2, COM-03 |
| Bloqueia | EST-04 |
| Ordem de execução | 20 (Marco 3.4 "Fechar e rastrear") |

## User story
Como **supervisor de expedição/almoxarife**, quero **montar o romaneio do que vai para a obra, com as séries, e confirmar a saída**, para **o estoque baixar o produto acabado e ficar registrado o que foi entregue a cada cliente**.

## Contexto e referências
- PRD §4 ("Expedição: nº de série ─► NF-e"); §5.7 R-PRD-8.
- `04-fluxos-estados` §2: hoje `InvoiceAuthorized` (Fiscal) → Inventory baixa produto acabado (`Shipment`). Pela **D4**, o romaneio confirmado baixa (chamada síncrona) e, na Fase 5, a NF-e de saída referencia o romaneio e nunca baixa de novo; o `04-fluxos` §2 é atualizado nesta story. §4: `ROM-AAAA-0001` (D15).
- `03-modelo-dados` → `stock_movements.type = Shipment`; `serials.status` (InStock/Shipped), `outgoing_invoices.shipment_id` (Fase 5). O romaneio entra no modelo nesta story.
- ADR 0022 (PDF); COM-03 (`IPdfRenderer`, `StoreGeneratedAsync`); ADR 0019 §7 (arquivo gerado pelo sistema).
- `04-fluxos-estados` §1 "Projeto": a expedição não muda o projeto (a entrega é a OS de instalação ou o registro manual).
- **P-04** (etapa "Expedição" do roteiro): pela D4 passa a ser "preparar e embalar"; ao finalizá-la, o próximo passo é "Concluir OS" e depois "Novo romaneio".
- **Revisão pré-Fase 3:** `docs/revisoes/2026-10-21-revisao-pre-fase3.md` (D4, D15, tabela 1.1 itens 84–86 e 93, §2.1 "Conclusão e romaneio", §2.2 ponto 86, §2.3 padrões 4, 5, 6, 8).
- **ADR 0023** §1 (efeito de estoque síncrono; 409 `insufficient_stock`), §4 (idempotência e estorno), §5 (`SoleOwnerRule`), §7 (D4, D15); **ADR 0025** §1 (o `Shipment` nunca entra no custo do projeto); **ADR 0019 §7, adendo de 21/10/2026** (dono de anexo `Shipment`).
- Dependências de cadastros já entregues: EST-01a (`IStockMovements`, tipo `Shipment`), COM-04a (`IProjects`, endereço da obra), CAD-01a (transportadora).

## Critérios de aceite
**Montar o romaneio**
1. **Dado** `production.create` (ponto 86) e um projeto com produto acabado em estoque (`serials` `InStock` com o `project_id` do projeto, ou produto sem série com saldo vindo das OS do projeto), **quando** clico "Novo romaneio" no projeto ou em "Fábrica → Produção → Expedição", **então** o formulário vem com as séries e quantidades disponíveis do projeto (padrão: todas), o endereço de entrega (o da obra do projeto, `IProjects` + `IParties.AddressesAsync`; editável), a transportadora (terceiro com papel Transportadora, opcional; a última usada com o cliente com a tag "Sugerido"), a placa e o motorista (texto, opcional, `[PersonalData]`), volumes e peso (opcionais) e a data prevista; ao salvar, nasce o romaneio `Draft` com número `ROM-2026-0001` (D15, ponto 85), com `Idempotency-Key`.
2. **Dado** uma série já em outro romaneio não cancelado, **quando** tento incluí-la, **então** 422 "A série {série} já está no romaneio {número}."; série ou produto de outro projeto é recusado (422), exceto pela opção "Incluir produto do estoque geral" (produto acabado sem projeto), que marca a linha "Estoque geral" e faz o romaneio exigir a confirmação do CA 6.
3. **Dado** o romaneio `Draft`, **quando** clico "Baixar PDF", **então** o PDF (`ShipmentPdfDocument`, `IPdfRenderer`) traz empresa, número, data, cliente, endereço de entrega, transportadora, itens com descrição, quantidade e séries, volumes e peso, e campos de assinatura de quem recebeu; sem valores (o romaneio não é documento fiscal) e sem custo (teste de extração de texto).

**Confirmar a expedição (D4)**
4. **Dado** `production.edit` e o romaneio `Draft` só com produto do projeto, **quando** clico "Confirmar expedição" e confirmo no diálogo de impacto (por item: saída do estoque, local, séries que passam a "Expedido"; saldo físico antes → depois), **então** na **mesma transação** (ADR 0023 §1; a Produção é dona da transação): o `IStockMovements.PostAsync` lança as saídas `Shipment` por linha (origem `Shipment`, chave `(Shipment, romaneioId, linha, Shipment)`, custo médio do momento), as séries vão a `Shipped`, o romaneio vai a `Shipped` com data e usuário, o PDF fica guardado como arquivo do sistema (dono `Shipment`) e o evento `ShipmentDispatched` (`{ shipmentId, projectId, lines: [{ itemId, serialIds, quantity, locationId }] }`) vai para o outbox só para avisos e painéis; a mensagem diz "Expedição confirmada".
5. **Dado** saldo insuficiente no momento da confirmação (alguém tirou o produto antes), **quando** confirmo, **então** 409 `insufficient_stock` com o item e o saldo atual, nada é gravado e o romaneio continua `Draft` com a mensagem dentro do diálogo.
6. **[Segurança]** **Dado** um romaneio com linha "Estoque geral" (ponto 86), **quando** quem montou pede a confirmação, **então** o romaneio fica "Aguardando confirmação do estoque" e só **outra pessoa** com `inventory.approve` confirma (403 "Quem montou o romaneio não confirma produto do estoque geral." para o autor), decidindo sobre o que foi montado, sem editar; exceção do Proprietário único pelo `SoleOwnerRule` (ADR 0023 §5), com a marca "Autoaprovado" e notificação; quem tem `inventory.approve` recebe a notificação "Romaneio {número} aguardando confirmação: produto do estoque geral." com link.
7. **Dado** a saída `Shipment`, **quando** o Projeto 360 calcula o custo, **então** o movimento **não** entra no custo do projeto (ADR 0025 §1: o custo do equipamento já entrou pelas baixas e pelas horas).
8. **Dado** o romaneio expedido, **quando** o `NextSteps` aparece, **então** oferece "Registrar entrega" (projeto sem OS de instalação, COM-04b, com `projects.approve`) ou "Ver OS de instalação" (COM-06), e "Emitir NF-e" aparece como "Disponível na Fase 5" (sem ação).

**Etapa Expedição do roteiro (D4, ponto 93)**
9. **Dado** a etapa "Expedição" do roteiro (preparar e embalar), **quando** ela é finalizada e a OS fica "Pronta para concluir", **então** o `NextSteps` oferece "Concluir OS" (PRD-06a1, com `production.approve`); **quando** a OS conclui com o produto no estoque (PRD-06a2), **então** o `NextSteps` oferece "Novo romaneio" já com o projeto e as séries da OS.

**Cancelar e estornar**
10. **Dado** um romaneio `Draft` ou "Aguardando confirmação do estoque", **quando** clico "Cancelar romaneio" com motivo, **então** fica `Cancelled` e as séries ficam livres.
11. **Dado** um romaneio `Shipped` que não saiu de fato (erro de lançamento) e sem NF-e (Fase 5), **quando** clico "Estornar expedição" (`production.approve`, motivo) e confirmo no diálogo `destructive`, **então** na mesma transação o Inventory lança as entradas inversas (`reverses_movement_id`, ADR 0023 §4), as séries voltam a `InStock` e o romaneio fica `Reversed`; o evento `ShipmentReversed` vai para o outbox; com NF-e autorizada (Fase 5), o estorno passa a exigir o cancelamento da nota.
12. **Dado** a Fase 5, **quando** a NF-e de saída for emitida, **então** ela referencia o romaneio (`outgoing_invoices.shipment_id`) e **nunca** baixa o estoque de novo (D4); esta story deixa a regra no `04-fluxos` §2 e no contrato do romaneio (`IShipments.ByIdAsync` com as linhas e as séries).

**Consulta, permissões e auditoria**
13. **Dado** `production.view`, **quando** abro "Expedição" (`/app/producao/expedicao`), **então** vejo a lista "Prontos para expedir" (projetos com produto acabado em estoque, com físico, reservado e disponível) e os romaneios com número, projeto, cliente, itens, data e situação (Rascunho neutro, Aguardando confirmação alerta, Expedido ok, Cancelado neutro, Estornado neutro); para quem tem `inventory.approve`, a lista de romaneios abre no filtro "Aguardando você"; o projeto mostra os romaneios na aba Itens e OS e na linha do tempo.
14. **Dado** o romaneio, **quando** anexo comprovantes (dono `Shipment`: pdf, png, jpg até 10 MB, ClamAV; leitura `production.view`, escrita `production.create`; limite de 30 envios por hora com 429; ADR 0019 §7, adendo), **então** ficam na aba Documentos com o PDF gerado.
15. **Dado** o módulo `production` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; criar sem `production.create`, confirmar e cancelar sem `production.edit`, confirmar estoque geral sem `inventory.approve`, estornar sem `production.approve`: 403. `RequireModule` e `perm:`; IDOR em `/shipments/{id}/lines/{lineId}`; projeto, endereço e transportadora validados pelos Contracts dos donos.
16. **[Segurança]** **Dado** duas confirmações simultâneas, ou a mesma série em dois romaneios confirmados ao mesmo tempo, **quando** chegam, **então** só um grava (`xmin` e índice único parcial da série em romaneio não cancelado), e a saída do estoque não duplica (chave de idempotência do movimento).
17. **Dado** qualquer mudança no romaneio, **quando** abro o histórico, **então** vejo antes/depois, usuário e data, quem montou e quem confirmou; motorista e placa mascarados no diff (`[PersonalData]`).

## Regras de negócio e técnicas
- **Romaneio:** `production.shipments` (`number`, `project_id`, `party_id`, `delivery_address` (cópia do endereço), `carrier_party_id`, `vehicle_plate`, `driver_name` (`[PersonalData]`), `volumes`, `gross_weight`, `planned_date`, `status` Draft/AwaitingStockApproval/Shipped/Cancelled/Reversed, `created_by`, `shipped_at`, `shipped_by`, `stock_approved_by`, `xmin`) e `shipment_lines` (`shipment_id`, `item_id`, `quantity`, `location_id`, `from_general_stock`) + `shipment_line_serials`. O dono é a Produção (R-PRD-8); a saída do estoque é do Inventory pela chamada síncrona (ADR 0023 §1).
- **Numeração** `ROM-AAAA-0001` pelo serviço de numeração, prefixo único no tenant (D15).
- **Fase 5:** a NF-e de saída referencia o romaneio e não baixa (D4).
- Guid v7; `xmin`; auditoria `IAuditable`; quantidades `numeric(18,4)`, peso `numeric(18,4)`.

## Permissões, eventos e tabelas
- **Permissões:** `production.view`, `production.create` (montar, anexar), `production.edit` (confirmar, cancelar), `inventory.approve` (confirmar produto do estoque geral, outra pessoa), `production.approve` (estornar). Nenhuma permissão nova.
- **Eventos (`Production.Contracts`):** `ShipmentDispatched`, `ShipmentReversed` (só avisos e projeção; nenhum consumidor mexe no estoque).
- **Contratos:** `IStockMovements.PostAsync`, `IStockSerials` (Inventory: séries disponíveis por projeto, `Shipped`/`InStock`), `IStockBalances`, `IProjects`, `IParties`; `IShipments` (Production, para a Fase 5 e a EST-04).
- **Tabelas:** `production.shipments`, `production.shipment_lines`, `production.shipment_line_serials`; dono de anexo `Shipment`.

## Telas e estados
- Padrões de tela da Fase 3: 4, 5, 6, 8.
- **Fábrica → Produção → Expedição** (`/app/producao/expedicao`, aba nova no `modules.ts`), **romaneio** (`/app/producao/expedicao/$id`, abas Itens, Documentos, Histórico de alterações), botão "Novo romaneio" no projeto.
- **Verbos:** "Salvar romaneio" / "Romaneio salvo"; "Confirmar expedição" / "Expedição confirmada"; "Confirmar estoque geral" / "Estoque geral confirmado"; "Cancelar romaneio" / "Romaneio cancelado"; "Estornar expedição" / "Expedição estornada".
- **Estados:** carregando; vazio ("Nada pronto para expedir. As OS concluídas aparecem aqui." + "Ver OS prontas para concluir"); erro (inclusive o 409 `insufficient_stock` dentro do diálogo); modo leitura do expedido.
- **Propositivo:** séries e endereço preenchidos; transportadora sugerida; próximos passos dos CA 8 e 9.
- **400 px:** itens e séries em cartões; na lista "Prontos para expedir", só o disponível, o resto na linha expansível; diálogo em tela cheia.
- **Ajuda:** `Ajuda/producao-expedicao.md`, `Ajuda/producao-romaneio.md` (abas `itens` padrão, `documentos`, `historico`); `<Dica>` em "Romaneio" ("Lista do que sai da fábrica; não é nota fiscal.") e em "Estoque geral" ("Produto acabado sem projeto: outra pessoa do estoque confirma a saída.").

## Plano de testes
- **Unidade:** séries disponíveis por projeto; regras de inclusão e estoque geral (CA 2, 6).
- **Integração (Testcontainers, obrigatória: movimenta estoque):** montar com numeração `ROM` e idempotência (CA 1); série repetida, de outro projeto e estoque geral (CA 2); PDF sem valores nem custo na imagem Alpine (CA 3); confirmar com saída `Shipment` síncrona, séries e evento na mesma transação (CA 4); 409 `insufficient_stock` sem gravar nada (CA 5); estoque geral com outra pessoa, autor recusado e Proprietário único (CA 6); fora do custo do projeto (CA 7); próximos passos (CA 8, 9); cancelar e estornar síncrono (CA 10, 11); anexos e limite de envios (CA 14); três camadas e IDOR (CA 15); concorrência (CA 16); auditoria mascarada (CA 17).
- **E2E (Playwright):** finalizar a etapa Expedição, concluir a OS, montar o romaneio pelo próximo passo, baixar o PDF e confirmar a expedição; tela a 400 px.

## Fora do escopo
- NF-e de saída, DANFE e XML ao cliente: FIS-03 (Fase 5), que referencia o romaneio.
- Entrega técnica e aceite: COM-06 (já existe). Registro de entrega sem instalação: COM-04b (já existe).
- Expedição de venda avulsa (sem projeto): Fase 10.

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fase 3, D4, D15 e tabela 1.1, itens 84–86 e 93):
1. O romaneio confirmado baixa o produto acabado, por chamada síncrona com 409 `insufficient_stock`; na Fase 5 a NF-e referencia o romaneio e nunca baixa de novo (D4; CA 4, 5, 12).
2. Numeração `ROM-AAAA-0001` (D15; CA 1).
3. `production.create` monta; com produto do estoque geral, outra pessoa com `inventory.approve` confirma (CA 1, 6).
4. A etapa Expedição do roteiro é "preparar e embalar": ao finalizá-la, o próximo passo é "Concluir OS" e depois "Novo romaneio" (D4; CA 9).

**Pré-condição externa:** nenhuma.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers, PDF na imagem Alpine) e E2E passando.
- [ ] Migration criada (não destrutiva), testada sobre cópia do banco do tenant `konebras`.
- [ ] ADR 0023 §1 aplicada (baixa síncrona) e D4 registrada.
- [ ] Permissões só do `ModuleCatalog`; dono de anexo `Shipment` registrado (ADR 0019 §7 adendo).
- [ ] Telas com estados, inclusive a 400 px.
- [ ] Ajudas da lista e do romaneio; teste de cobertura passando.
- [ ] Diálogo de impacto e próximos passos aplicados.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` (romaneio) e `docs/04-fluxos-estados.md` §2 e §4 (D4: romaneio baixa, NF-e referencia; `ROM`) atualizados; P-04 com a etapa Expedição "preparar e embalar"; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `Shipment` e `ShipmentLine` (Domain); `ShipmentService` (montar, confirmar, confirmar estoque geral, cancelar, estornar) com a chamada síncrona ao Inventory.
- [ ] `ShipmentPdfDocument`; `IStockSerials` no Inventory; `IShipments` no Production.
- [ ] Tela de expedição, romaneio, próximos passos e ajudas.

## Quality gates
- Agentes previstos: @dev, @qa.
- Gate: CA 4, 5, 6, 11 e 16 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fase 3).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK (renumerados) |
| 5 | CAs de segurança e de permissão (3 camadas) | OK (ajustado: CA 6) |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: PRD-06a2, Marco 3.4) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK |

- **Correções feitas na validação (21/10/2026, primeira validação):** a baixa pela NF-e do `04-fluxos` não existe na Fase 3: ponto em aberto (1) com proposta, sem decidir; `Shipment` fora do custo do projeto; PDF sem valores.
- **Correções da revalidação (21/10/2026):** a baixa passa de consumidor do evento a chamada síncrona na confirmação, com 409 `insufficient_stock` sem gravar nada (D4, ADR 0023 §1; sai a volta automática a rascunho); a NF-e da Fase 5 referencia o romaneio (CA 12); `ROM-AAAA-0001` (D15); estoque geral confirmado por outra pessoa com `inventory.approve`, com `SoleOwnerRule` (ponto 86, §2.2); próximos passos da etapa Expedição (ponto 93); dono de anexo `Shipment` pelo adendo da ADR 0019 §7; estorno síncrono; filtro "Aguardando você" e físico/reservado/disponível. CAs renumerados (17).
- **Pré-condições para `Ready`:** nenhuma de decisão de story.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da PRD-06) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO (D4, D15, tabela 1.1 itens 84–86 e 93) e correções da revisão pré-Fase 3 (§2.1 baixa síncrona, ADR 0023 §1; §2.2 ponto 86; §2.3 padrões 4, 5, 6, 8); nova ordem (Marco 3.4) | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
