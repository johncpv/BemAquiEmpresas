# 03 — Modelo de dados do PDV (proposta)

Entidades novas por módulo e as ligações com Estoque, Financeiro e Fiscal. É um esboço para orientar as stories: nomes e campos fecham na story de cada tabela e nos rascunhos de ADR.

## Convenções (as do `CLAUDE.md`)

| Tema | Regra |
|---|---|
| Dinheiro | `decimal(18,2)` |
| Quantidade | `decimal(18,4)` |
| Custo unitário | `decimal(18,6)` |
| Percentual | `decimal(9,4)` (taxa da adquirente, desconto, perda da ficha técnica). Proposta deste plano: o `CLAUDE.md` não fixa |
| Datas | `timestamptz` em UTC; data de negócio sem hora em `date` |
| Ids | Guid v7. **No PDV, o id da venda e dos eventos nasce no aparelho** |
| Exclusão | Soft delete em cadastro; documento com movimento só é cancelado ou estornado |
| Entre módulos | Sem FK para tabela de outro módulo: guarda-se o id e consulta-se pelo contrato |
| No aparelho | Dinheiro em **centavos inteiros** no TypeScript; a conversão para `decimal(18,2)` é do servidor |

Nomes de tabela e coluna em inglês; `tenant` não aparece como coluna porque o banco é separado por tenant.

## 1. Platform (banco de catálogo)

| Tabela | Campos-chave | Observação |
|---|---|---|
| `catalog.tenant_profiles` | `tenant_id`, `profile_code` (`factory`, `retail`, `services`, `ecommerce`), `segment` (nulo ou `counter`, `supermarket`, `bakery`, `restaurant`, `snack_bar`, `food_truck`, `workshop`), `applied_at`, `applied_by`, `removed_at` | Um ou mais perfis por tenant. Backfill `factory` para os tenants existentes. Único por (`tenant_id`, `profile_code`, `segment`) entre os não removidos |

`tenant_modules` não muda de forma: continua sendo a única tabela lida pela camada 1. O perfil só a grava, sem marcar exceção manual.

## 2. Identity

| Tabela | Mudança | Observação |
|---|---|---|
| `identity.shopfloor_devices` → aparelhos | + `kind` (`Factory`, `Register`; `Kds`, `Waiter`, `PrintStation` nas Fases 12 e 15), `store_id`, `register_id`, `register_epoch`, `public_key` (ECDSA P-256 do aparelho, SubjectPublicKeyInfo), `last_sync_at` (`revoked_at` já existia) | **Feito na PDV-01** ([ADR 0048](../adr/0048-aparelho-do-pdv-e-esquema-device.md)): a tabela **manteve o nome**; `kind` com padrão `Factory` (os aparelhos de antes ficam Fábrica). O teto é por tipo (os 10 da fábrica contam só `Factory`); o teto por plano é a PP-17 |
| `identity.users` | + `is_operation_user` (sem e-mail, só entra por PIN em aparelho registrado), `operation_store_ids` (`uuid[]`, lojas onde trabalha) | **Feito na PDV-01, parte 2** ([ADR 0049](../adr/0049-usuario-de-operacao.md), adendo de 18/11/2026): a coluna `email` continua obrigatória e única; o usuário de operação guarda um e-mail técnico `operacao.{id}@operacao.invalid` (RFC 2606), nunca exibido nem usado, como a anonimização |
| `identity.pos_pins` (nova) | `user_id` (único), `pin_hash` (PBKDF2), `must_change`, `created_by`, contadores de erro e bloqueio, `created_at`, `last_used_at` | **Feito na PDV-01, parte 2** (CA 11): um PIN do caixa por usuário, válido em todo caixa registrado das lojas dele; mesmas regras de erro da ADR 0026 §2. `identity.refresh_tokens.session_kind` ganha `PosPin` |
| `identity.device_pin_verifiers` (nova) | `device_id`, `user_id`, `verifier` (derivado no servidor), `issued_at`, `offline_limits` | Baixado só para os operadores e supervisores da loja do aparelho. No aparelho fica cifrado com uma **chave de cifra não extraível própria** (AES ou ECDH do WebCrypto); a chave ECDSA do aparelho só assina e não serve para isso (OFF-03) |

## 3. Catalog — schema `catalog_products`

| Tabela | Campos-chave | Observação |
|---|---|---|
| `categories` | `id`, `name`, `parent_id`, `default_margin_percent`, `default_markup_percent` (`numeric(9,4)`, um dos dois), `sort_order`, `is_active`, `seed_key` | **Feita na CAT-01.** Categoria de produto: organiza a grade do modo Toque, a importação, os relatórios e a margem padrão usada na precificação (CPD-01). A "Geral" (`seed_key = geral`) nasce no primeiro uso do módulo; com produto, só desativa |
| `products` | `id`, `item_id` (Estoque), `category_id`, `sale_name`, `search_text`, `sellable_pos`, `sellable_online`, `is_weighable`, `plu`, `variant_of_product_id`, `variant_attributes` (jsonb: cor, tamanho), `has_variants`, `is_active`, soft delete, `xmin`. Fase 15: `kitchen_station_id`, `prep_minutes`; regra 10: `fiscal_ready` | **Feita na CAT-01** (sem as colunas das Fases 15 e da regra 10). Um produto por item (índice único entre os não excluídos). **Variação (grade):** o pai tem item e agrupa; cada variação é um produto com item, código de barras, preço e saldo próprios e `variant_of_product_id` apontando para o pai; `has_variants` marca o pai, que não é vendido. A foto é anexo do dono `ProductPhoto` no produto raiz (a variação usa a do pai), no lugar de `photo_file_id` |
| `product_barcodes` | `product_id`, `gtin`, `pack_quantity` `decimal(18,4)` | **Feita na CAT-01.** Vários por produto. `gtin` único no tenant, com dígito verificador validado. `unit_id` ficou de fora: a embalagem é a quantidade na unidade do item |
| `price_lists` | `id`, `name`, `store_id` (nulo = todas), `channel` (`Pos`, `Online`, `Delivery`), `valid_from`, `valid_to`, `version`, `is_default`, `seed_key` | **Feita na CAT-01**, só com a "Balcão" (`seed_key = balcao`, criada no primeiro uso). Versão incrementada a cada mudança, com a linha travada; o aparelho informa a versão em que vendeu |
| `price_list_items` | `price_list_id`, `product_id`, `price` `decimal(18,2)`, `min_quantity` `decimal(18,4)` | **Feita na CAT-01** (`min_quantity` = 1). `min_quantity` atende preço de atacado por faixa (Fase 12) |
| `price_changes` | `price_list_id`, `product_id`, `previous_price`, `new_price` `decimal(18,2)`, `list_version`, `changed_at`, `changed_by` | **Feita na CAT-01.** Histórico do preço, só inclusão |
| `import_runs` | `id`, `file_name`, `status` (`Previewed`, `Running`, `Completed`, `Failed`), `total_rows`, `to_create`, `to_update`, `error_rows`, `stock_rows`, `prices_ignored`, `rows` e `errors` (jsonb), `confirmed_by`, `apply_prices`, `launch_stock`, `stock_location_id`, `in_background`, `processed`, `created`, `updated`, `failed`, `stock_posted`, `stock_already_posted`, `completed_at` | **Feita na CAT-02.** Importação por planilha: a prévia guarda as linhas válidas e as com erro (confirmar sem reenviar, baixar as com erro); o resultado e o progresso do job. Não é `IAuditable` (as linhas não vão para a auditoria): a confirmação grava `Imported` com os totais. O saldo inicial é movimento `OpeningBalance` com origem `CatalogImport` e o produto como `source_id` (reimportar não lança de novo) |
| `promotions` | `id`, `kind` (leve X pague Y, preço por faixa, por horário), `rule` (jsonb), `valid_from`, `valid_to`, `store_id` | Fase 12 |
| `modifier_groups`, `modifiers` | grupo: `min_select`, `max_select`; complemento: `name`, `price` `decimal(18,2)`, `item_id` opcional | Fase 15 |
| `combos`, `combo_items` | `combo_product_id`, `product_id`, `quantity`, `swap_group` | Fase 15 |
| `recipes`, `recipe_lines` | receita: `product_id`, `mode` (`ConsumeOnSale`, `ProduceToStock`), `yield_quantity`; linha: `ingredient_item_id`, `quantity` `decimal(18,4)`, `loss_percent` | Fase 15. A BOM da Engenharia é por OS (`production_order_id`) e não serve à ficha técnica de alimento |
| `label_templates`, `label_queue` | modelo por tamanho; fila gerada ao mudar preço | Fase 12. Reusa a base de etiqueta do Patrimônio |

Ligações: `products.item_id` → `inventory.items` (por `IItems`; o item novo nasce pelo `IItemRegistration`, CAT-01); custo para a margem por `IItemCosts`; disponibilidade para a loja virtual por `IStockBalances`. Os outros módulos leem o catálogo por `ISellableProducts` e `ICatalogLookup` ([ADR 0047](../adr/0047-catalogo-de-produtos.md)).

## 4. Pos — schema `pos`

Dono das tabelas abaixo e das rotas `/api/v1/pos/…`. Não escreve tabela do `Sales`: entrega a venda pelo contrato `ISaleIngestion` ([02, seção 4.1](02-arquitetura.md#41-fronteira-sales--pos-quem-é-dono-de-quê)).

| Tabela | Campos-chave | Observação |
|---|---|---|
| `stores` | **Feita na PDV-01** com `id`, `code` (2 a 6 letras ou números, único), `name`, `stock_location_id`, `default_price_list_id`, `is_active`, `seed_key` (`loja`), `xmin`; **OFF-01** acrescentou os parâmetros `offline_window_hours` (int, padrão e teto 168, PP-10), `cash_break_tolerance` (`numeric(18,2)`, padrão 5,00), `exchange_days` (int, padrão 30) e `cash_drop_alert_amount` (`numeric(18,2)`, padrão 1.000,00) (PP-22), com `CHECK`; **CX-01** acrescentou `cash_ceiling_amount` (`numeric(18,2)`, nulo = sem teto, CHECK > 0) e a edição do aviso e do teto na tela; **PDV-04** acrescentou `discount_limit_percent` (`numeric(5,2)`, 0 a 100, CHECK; padrão 0 até a PP-22 decidir o valor sugerido); ainda sem tela de edição; o resto chega com a story que usa. Proposta: `id`, `code` (curto, entra no número da venda), `name`, `stock_location_id` (Estoque), `default_price_list_id`, `fiscal_mode` (`Nfce`, `NonFiscalOnly`), `non_fiscal_accepted_by`, `non_fiscal_accepted_at`, `establishment_id` (nulo enquanto houver um CNPJ só), parâmetros da loja | Parâmetros: tolerância de quebra por forma, teto de dinheiro em caixa, prazo de devolução, janela máxima sem sincronizar, limites de aprovação offline (PP-10, PP-22, PP-39). O limite de desconto fica aqui (PP-38 decidida: por loja). `non_fiscal_accepted_*` guarda o aceite do Proprietário (regra 13 do PRD) **PDV-07:** `cancel_alert_percent` (`numeric(5,2)`, padrão 3; zero desliga): alerta de cancelamentos do turno no card "Caixas abertos" (PP-22). |
| `registers` | `id`, `store_id`, `number`, `cash_account_id` (Finance, tipo `Cash`), `fiscal_series`, `active_device_id`, `device_epoch`, `ui_mode` (`Touch`, `Keyboard`) | **Feita na PDV-01** (sem `fiscal_series`, da NFCE): número de 1 a 99 único na loja, conta Caixa única entre os caixas, `active_device_id` único entre os não nulos, `is_active`, `xmin`. Um aparelho ativo por caixa. `device_epoch` sobe a cada troca de aparelho; o `seq` recomeça em 1 em cada época, então nada do aparelho antigo colide com o novo |
| `cash_sessions` | `id`, `register_id`, `operator_user_id`, `status`, `opened_at`, `opening_amount` `decimal(18,2)`, `closed_at`, `expected_total`, `counted_total`, `difference`, `deposited_amount` (recolhido ao Cofre no fechamento), `reviewed_by`, `review_reason`, `opened_offline`, `closed_offline` | Uma `Open` por caixa (índice único parcial). Estados em [04](04-fluxos-estados.md#2-caixa-e-turno) | **Feita na CX-01** com `id` (do aparelho), `store_id`, `register_id` (FK), `device_epoch`, `operator_id`, `opening_amount`, `opened_at`, `status` (`Open`, `Closed`), `closed_at`, `created_at`, `xmin`; índice único parcial `ix_cash_sessions_register_open`; **CX-02** acrescentou `status` `Counting`, `Counted` e `PendingReview`, `count_started_at`, `count_id`, `counted_by`, `counted_at`, `device_sales_count`, `kept_amount`, `deposit_amount`, `deposit_movement_id`, `review_reasons` (flags), `closed_by`, `approved_by/at`, `approval_reason`, `reopen_count`, `reopened_by/at`, `reopen_reason`; o esperado não fica aqui (fica nas linhas de `cash_counts`) |
| `cash_movements` | `id`, `cash_session_id`, `kind` (`Opening`, `CashOut` sangria, `CashIn` suprimento, `Deposit` recolhimento no fechamento), `amount` `decimal(18,2)`, `reason`, `counterpart_account_id`, `approved_by`, `finance_transfer_id` Vira transferência no Finance. **Feita na CX-01** com `id` (do aparelho), `session_id` (FK), `kind` (`Opening`, `CashOut`, `CashIn`), `amount` (CHECK: > 0 fora da abertura), `reason` (obrigatório fora da abertura), `operator_id`, `occurred_at`, `above_expected` (marca do aparelho, recalculada na OFF-02), `created_at`; a transferência aponta para o movimento (`finance.bank_transfers.pos_cash_movement_id`, único) |
| `cash_counts` | `cash_session_id`, `payment_method_id`, `counted_amount`, `expected_amount`, `difference`, `denominations` (jsonb, opcional) | `expected_amount` só é exposto a quem tem `sales.approve`. A tolerância é aplicada por linha (por forma). **Feita na CX-02** com `session_id`, `count_id` (contagem do aparelho; a reabertura gera outra), `payment_method_id` (nulo = dinheiro sem forma), `method_type`, `counted`, `expected`, `pix_not_found`, `difference`, `denominations`. Nova `pos.cash_pix_checks` (`session_id`, `payment_id` único na sessão, `sale_id`, `payment_method_id`, `amount`, `status` `Found`/`NotFound`, `checked_by/at`). `cash_movements.kind` ganhou `Deposit` (recolhimento) e `DepositReturn` (reabertura) |
| `device_events` | `event_id` (PK, do aparelho), `device_id`, `register_id`, `device_epoch`, `seq`, `schema_version`, `type`, `payload` (jsonb, **sem dado pessoal**), `device_time`, `received_at`, `prev_hash`, `hash`, `outcome` (`Accepted`, `AcceptedWithDivergence`, `Quarantined`) | **Feita na OFF-02** (ADR 0050) com `kind`, `occurred_at`, `clock_skew_ms`, `chain_ok`, `device_revoked` e `payload` em `text` (o hash continua conferível); índice não único em (`register_id`, `device_epoch`, `seq`): o seq repetido também é guardado, em quarentena. **Imutável por trigger**, como `stock_movements`. Único por (`register_id`, `device_epoch`, `seq`) na proposta. `[NotAudited]`: o próprio registro é a trilha. O CPF do cliente **não entra no payload**: sobe ao lado do lote e vai para `sales.sale_customer_documents`, que pode ser apagado sem quebrar a cadeia de hash |
| `sync_divergences` | `id`, `event_id`, `sale_id`, `kind` (preço vencido, relógio, saldo, aparelho revogado, lacuna de sequência), `status` (`Open`, `Resolved`), `resolved_by`, `resolution` | Alimenta a tela "Pendências do PDV". **Feita na OFF-02** com `kind` (`PriceOutdated`, `DeviceClock`, `BrokenSequence`, `ReplacedDevice`, `SchemaVersion`, `Validation`, `Payment`, `CashSession`, `FiscalDocument`), `quarantined`, loja, caixa, aparelho, época, `seq`, `sale_number`, `operator_id`, `summary`, `details` (jsonb), `resolution` (`AcceptAsIs`, `ReleaseQuarantine`, `KeepOut`), `resolution_reason`, `resolved_at`, `xmin`; auditada. Também na OFF-02: `operator_incidents` (exceções por operador: remoção, redução, descarte, recusa) e `session_sales` (dinheiro de cada venda na sessão, para recalcular a sangria acima do esperado) |
| `catalog_items` | **Feita na OFF-01:** PK (`store_id`, `kind` (`Product`, `PaymentMethod`), `entity_id`), `payload` (jsonb: no produto, nome, unidade, pesável, PLU, GTINs com a quantidade da embalagem, preço da tabela da loja em centavos e a versão da tabela; na forma, nome, tipo, troco, `tPag` e conferida), `price_list_version`, `deleted`, `change_seq` (sequência `pos.catalog_change_seq`), `updated_at`; índice (`store_id`, `change_seq`) | **Modelo de leitura** do próprio módulo, uma linha por loja: alimentado por `ProductChanged`, `PriceChanged` e `PaymentMethodChanged`, que releem a fonte pelos contratos `IPosCatalogFeed` (Catálogo) e `IPaymentMethods` (Financeiro); idempotente (o `change_seq` só muda quando o conteúdo muda). Gravações em série pela trava `pg_advisory_xact_lock('pos.catalog')`, então o cursor não pula mudança. **Sem custo, margem nem taxa.** Removido = `deleted` (desce no delta). Operadores (pelo `IPosStoreOperators` do Identity) e impressoras não ficam aqui: descem inteiros a cada sincronização. O saldo por loja (PP-02) ficou para a PDV-05 |
| `catalog_store_builds` | **Feita na OFF-01:** `store_id` (PK), `price_list_id`, `built_at` | Carga inicial (backfill) do catálogo de cada loja, feita na primeira sincronização e refeita quando a loja troca de tabela de preço |
| `supervisor_approvals` | **Feita na PDV-04** com `id`, `store_id`, `register_id`, `device_id`, `action` (`Discount`, `ChangePrice`, `CancelItem`, `CancelSale`), `operator_id`, `supervisor_id`, `reason`, `sale_id`, `line_id`, `before_amount` e `after_amount` `decimal(18,2)`, `online`, `approved_at`, `used_at`, `used_by_event_id`, `refund_reference` (NSU do estorno); auditada | `supervisor_id` ≠ `operator_id` (regra + `CHECK`); uso único (a subida confere ação, venda, operador, supervisor e caixa) |
| `printers` | `id`, `store_id`, `register_id`, `name`, `connection` (IMP-01: `Browser`, `Usb`, `Network`, `Bluetooth`; só `Browser` aceito até a IMP-02/03), `paper_width_mm` (58, 80), `columns` (32, 42, 48; CHECK com a largura), `model` (lista curta), `is_default` (uma padrão ativa por caixa, índice único parcial), `is_active`, `test_confirmed_at`, `created_at`. A IMP-02 acrescenta `has_cutter`, `has_drawer`; a cozinha, `kitchen_station_id` | Configuração no servidor, por caixa; desce na sincronização (IMP-01) |
| `print_records` | `id` (do aparelho, idempotente), `store_id`, `register_id`, `printer_id`, `kind` (`Receipt`, `Reprint`, `CashReceipt`, `Test`; DANFE NFC-e e comanda depois), `sale_id`, `sale_number`, `copy` (1 = original; 2 = 2ª via), `operator_id`, `printed_at`, `received_at` | Reimpressão é evento registrado (IMP-01); sem CPF nem conteúdo do cupom |
| `stores` (IMP-01) | `fiscal_mode` (`Nfce` padrão, `NonFiscal`), `non_fiscal_terms_version`, `non_fiscal_accepted_at`, `non_fiscal_accepted_by`, `receipt_print_mode` (`Ask`, `Always`, `Never`) | Só o Proprietário muda o modo; o aceite fica no histórico de auditoria da loja |

## 5. Sales — schema `sales`

Dono das tabelas abaixo e das rotas `/api/v1/sales/…`. O núcleo (as quatro primeiras tabelas e a baixa de estoque) nasce na Fase 10, na VND-01, e não depende de caixa.

| Tabela | Campos-chave | Observação |
|---|---|---|
| `sales` | `id` (do aparelho, no PDV), `number` (`{loja}-{caixa}.{época}-{seq}` no PDV; numeração do canal nos demais), `store_id`, `register_id` (nulo fora do PDV), `cash_session_id` (**nulo em canal sem caixa**), `channel` (`Pos`, `Online`, `Delivery`, `Table`, `ServiceOrder`), `status`, `fiscal_status`, `stock_status`, `customer_id` (nulo = consumidor final), `seller_user_id`, `subtotal`, `discount_total`, `service_fee`, `total` (todos `decimal(18,2)`), `price_list_version`, `completed_at_device`, `received_at`, `invoice_id` (Fiscal) | Qual data vale para o documento fiscal (a da venda ou a da emissão) é pergunta para a contadora: QC-17. O instante do aparelho é sempre guardado |
| `sale_customer_documents` | `sale_id`, `document_encrypted` (CPF/CNPJ, `[PersonalData]`), `document_blind_index`, `purpose` (fiscal, devolução) | Fora do registro imutável de eventos. Cifrado; apagável pela retenção. A busca por CPF usa o índice cego (HMAC), nunca o CPF em claro |
| `sale_lines` | `id`, `sale_id`, `product_id`, `item_id`, `description`, `quantity` `decimal(18,4)`, `unit_price` `decimal(18,2)`, `discount` `decimal(18,2)`, `total` `decimal(18,2)`, `unit_cost` `decimal(18,6)`, `modifiers` (jsonb), `note`, `seat`; **PDV-04**: `list_unit_price` (preço da tabela quando trocado), `discount_reason`, `price_reason` (o desconto no total chega rateado nas linhas) | Preço e desconto gravados na linha; `unit_cost` preenchido na baixa, para a margem |
| `sale_parts` | `id`, `sale_id`, `label` (pessoa ou assento), `kind` (`Equal`, `Amount`, `Items`), `amount` `decimal(18,2)`, `status` (`Pending`, `Paid`) | Split. Venda sem split tem uma parte só |
| `sale_part_lines` | `sale_part_id`, `sale_line_id`, `quantity` `decimal(18,4)` | Só no split por itens |
| `sale_payments` (`tenders`) | `id`, `sale_id`, `sale_part_id`, `payment_method_id` (Finance), `amount` `decimal(18,2)`, `change_amount` `decimal(18,2)`, `status` (`Created`, `Pending`, `Captured`, `Voided`, `Refunded`, `Failed`, `Unknown`), `integration` (`Manual`, `Tef`, `SmartPos`, `PixApi`, `Gateway`), `installments`, `provider`, `acquirer_party_id`, `card_brand`, `nsu`, `authorization_code`, `txid`, `end_to_end_id`, `confirmed_by`, `statement_checked_at` | Os campos de TEF e Pix existem desde o início, vazios no modo manual. **Nunca número de cartão, CVV ou trilha.** `statement_checked_at` marca o Pix conferido pelo operador que já foi conferido contra o extrato |
| `sale_returns`, `sale_return_lines` | devolução: `sale_id`, `status` (`Requested`, `Approved`, `Completed`), `refund_kind` (`Cash`, `StoreCredit`, `CardReversal`), `exchange_sale_id`, `approved_by`; linha: `sale_line_id`, `quantity`, `restock` (volta ao estoque ou perda) | Troca = devolução + venda nova ligadas. **Feita na PDV-06**: `number` (DEV), `total`, `store_id`, `register_id`, `device_id`, `cash_session_id`, `operator_id`, `late`, `deadline`, `card_payment_id`, `card_nsu`, `is_exchange`, `store_credit_id`, `supervisor_approval_id`; linha com `destination` (`Restock`, `Damaged`), `reason`, `amount` e os movimentos de estoque (entrada e perda) |
| `store_credits` | `id`, `code`, `customer_id`, `amount`, `balance` (`decimal(18,2)`), `expires_at`, `origin_return_id` | Vale-troca (passivo). Não funciona sem rede. **Feita na PDV-06**: `code_hash` (SHA-256, único) e `code_last4` no lugar do código; `store_credit_movements` (um uso por pagamento do caixa, `tender_id` único, desfeito com `reversed_at`); validade por loja em `pos.stores.store_credit_validity_days` (padrão 90, a confirmar com o PO) |
| `stock_postings` | `id`, `sale_id`, `item_id`, `location_id`, `quantity` `decimal(18,4)`, `shortfall_quantity` `decimal(18,4)`, `unit_cost` `decimal(18,6)`, `stock_movement_id`, `reversal_movement_id`, `status` (`Posted`, `Shortfall`, `Blocked`, `Reversed`) | Baixa **por venda** e item (SPK-03, item 7; PDV-05): sai o que há em saldo e o restante (`shortfall_quantity`) fica em `inventory.stock_shortfalls` |
| `orders` (VND-02a) | `id`, `number` (PV-AAAA-NNNN), `status` (`Draft`, `Open`, `AwaitingPayment`, `Paid`, `Cancelled`), `origin` (`Backoffice`, `Conecta`, `Marketplace`, `Api`, `Storefront`), `source_app_id`, `source_app_name`, `external_id`, `external_hash`, `created_by`, `customer_id`, `store_id`, `stock_location_id`, `sale_id`, `receivable_id`, `payment_kind`, `subtotal`, `order_discount`, `discount_total`, `total` (`decimal(18,2)`), `hold_until`, `discount_needs_approval`, `paid_after_cancel`, `cancel_reason`, `xmin` | Pedido de venda até o pagamento; pago, vira a venda `sale_id`. Único: `number`; `(origin, source_app_id, external_id)` com nulos iguais. Nunca apagado |
| `order_lines`, `order_transitions`, `order_parameters` (VND-02a) | linhas (`product_id`, `item_id`, `quantity` `decimal(18,4)`, `unit_price`, `discount`, `total`, `price_list_version`); linha do tempo (de, para, ação, quem, origem, motivo, quando); parâmetros (limites de desconto, prazos, limites do aplicativo, forma do boleto) | `sale_payments` ganha `receivable_id` (pagamento que já é título: não gera outro recebível) |

## 6. Finance (extensões)

| Tabela | Campos-chave | Observação |
|---|---|---|
| `finance.payment_methods` (nova) | `id`, `name`, `kind` (`Cash`, `Pix`, `Debit`, `Credit`, `CreditInstallments`, `Voucher`, `StoreCredit`, `OnAccount` fiado), `destination_account_id`, `acquirer_party_id`, `nfe_payment_code` (o `tPag`), `allows_change`, `is_active`, `seed_key`, `confirmed_at`, `confirmed_by`, soft delete | `confirmed_at` nulo = chip "A confirmar" (só nas formas sugeridas; PAG-01) |
| `finance.acquirer_fees` (nova) | `id`, `acquirer_party_id`, `card_brand`, `payment_method_id`, `installments`, `fee_percent` `decimal(9,4)`, `settlement_days`, `valid_from`, `valid_to` (exclusivo), `created_at`, `created_by` | Histórico por vigência; nunca se edita a taxa passada; `installments` = "a partir de N parcelas" (PAG-01) |
| `finance.receivables` | + origem `Sale` e `CardSettlement`; + `sale_channel`, `cash_session_id` (**nulo em canal sem caixa**), `settlement_date`, `gross_amount`, `expected_fee`, `net_amount` | Título contra a **adquirente** ou o gateway (cartão) ou contra o cliente (fiado). No PDV, um por adquirente, bandeira, forma e vencimento, por sessão. Em canal sem caixa, o mesmo agrupamento por **dia e canal** (VND-01) |
| `finance.receivables` (VND-02a) | + origem `SalesOrder` (um título ativo por pedido, índice único parcial), `refunded_at`, `refunded_by` | Cobrança do pedido por boleto com Pix; fora da régua e da receita por título. `finance.payment_methods` ganha o tipo `BankSlip` |
| `finance.bank_accounts` | + `seed_key` (único entre as não nulas; PERF-04) | Contas "Caixa PDV 01", "Cofre" e "Quebra de caixa" do tipo `Cash` entram pelo pacote do Comércio (PERF-04); "Conta bancária principal" só quando o tenant não tem conta do tipo Banco |
| `parties.parties` | + `seed_key` (único entre as não nulas; PERF-04) | Adquirente "Maquininha" do pacote do Comércio |
| `finance.pix_charges` (nova, Fase 16) | `id`, `txid`, `amount`, `status`, `sale_payment_id`, `expires_at`, `end_to_end_id` | Atrás do `IPixChargeProvider` |

## 7. Inventory (extensões)

| Tabela | Mudança | Observação |
|---|---|---|
| `inventory.items` | + valor `Merchandise` em `ItemType` (mercadoria para revenda) | Aditivo |
| `inventory.stock_movements` | + origens `PosSale` (ou `PosStockBatch`, se agregado), `SaleReturn`, `Loss` | Aditivo |
| `inventory.locations` | + `store_id` (opcional). A opção A da PP-02 (`allow_negative`) **não** foi escolhida (18/11/2026) | O `CHECK quantity_on_hand >= 0` não muda |
| `inventory.stock_shortfalls` (nova, opção B da PP-02, PDV-05) | `id`, `item_id`, `location_id`, `quantity` `decimal(18,4)`, `source_type`, `source_id`, `source_number`, `occurred_at`, `status` (`Open`, `Applied`, `Cancelled`), `movement_id`, `reversal_movement_id`, `resolved_at`, `resolved_by` | Uma por origem, item e local. "Aplicar baixa" lança pelo motor com a origem `StockShortfall`; o cancelamento da venda fecha a falta aberta sem movimento. Auditada |
| `inventory.reservations` (VND-02a) | + `source_type`, `source_id`, `expires_at`; `production_order_id` anulável; `CHECK` de origem única; índice parcial da OS e índice único parcial do documento | Reserva genérica do pedido de venda (`ISalesReservations`); a baixa da venda consome a reserva pelo `StockPosting.ReservationSource*` |

## 8. Fiscal (extensões)

| Tabela | Mudança | Observação |
|---|---|---|
| `fiscal.outgoing_invoices` | + `InvoiceType.NFCe`; + `sale_id`, `register_id`, `emission_type` (normal, contingência central, contingência na loja), `contingency_deadline`, `qr_code`, `consult_url`, `print_payload` (jsonb do bloco `impressao`) | A numeração e a série são do Emissor, salvo a série de contingência da loja (NFCE-04). A NF-e modelo 55 do balcão (PDV-14) usa a tabela como já é hoje, com `sale_id` |
| `fiscal.fiscal_settings` | + CSC e idCSC **não ficam aqui**: ficam no Emissor, por emitente e ambiente (lacuna 8). Aqui só o estado "CSC cadastrado" e a série por caixa | O A1 continua só no cofre do Emissor (ADR 0027) |
| `fiscal.tax_rules` | + regras do Simples e do MEI por NCM (ST, monofásico) com "A confirmar com a contadora" | Fase 13 |

## 9. Restaurant — schema `restaurant` (Fase 15)

| Tabela | Campos-chave |
|---|---|
| `dining_areas`, `tables` | área: `store_id`, `name`; mesa: `number`, `seats`, `status` (`Free`, `Occupied`, `BillRequested`, `Paying`, `Reserved`, `Blocked`), posição no mapa |
| `tabs` (comandas) | `id`, `store_id`, `table_id` (nulo em comanda por cartão ou por nome), `card_number`, `customer_name`, `status` (`Open`, `BillRequested`, `PartiallyPaid`, `Closed`, `Cancelled`), `opened_by_device_id`, `sale_id` |
| `tab_items` | `tab_id`, `product_id`, `quantity`, `unit_price`, `modifiers`, `note`, `seat`, `round`, `sent_at`, `cancelled_by` |
| `kitchen_stations` | `store_id`, `name` (Chapa, Fritadeira, Bar, Expedição), `printer_id` |
| `kitchen_tickets`, `kitchen_ticket_items` | tíquete: `order_ref` (comanda ou venda), `station_id`, `status` (`Queued`, `InPreparation`, `Ready`, `Delivered`, `Cancelled`), tempos |
| `order_calls` | `store_id`, `business_date`, `call_number` (senha), `display_name`, `status` | Senha curta por loja e dia; sem rede, cada caixa usa faixa própria |
| `service_fee_rules`, `couvert_rules` | `store_id`, `percent` ou `amount`, `optional` |

## 10. ServiceOrders — schema `service_orders` (Fase 17)

| Tabela | Campos-chave |
|---|---|
| `service_orders` | `id`, `number`, `customer_id`, `status`, `object_kind` (veículo ou equipamento), `vehicle_id`, `entry_km`, `sale_id` |
| `vehicles` | `customer_id`, `plate`, `model`, `year`, `next_service_at` |
| `checklist_entries` | `service_order_id`, `item`, `result`, `photo_file_id`, `visible_to_customer` |
| `service_lines`, `part_lines` | serviço: `technician_user_id`, `hours`, `price`; peça: `item_id`, `quantity`, `price`, `reservation_id` |
| `quote_approvals` | `service_order_id`, `token`, `approved_at`, `approver_name` |

## 11. Storefront — schema `storefront` (Fase 14)

| Tabela | Campos-chave |
|---|---|
| `store_settings` | tema, políticas; o host fica em `catalog.tenant_domains` com `kind = Store` (padrão da ADR 0040) |
| `collections`, `collection_products` | vitrine |
| `shoppers` | identidade do comprador, separada do backoffice |
| `carts`, `cart_lines` | com `reservation_id` do Estoque e validade |
| `online_orders` | `id`, `number`, `shopper_id`, `status` (`Paid`, `Picking`, `Invoiced`, `Shipped`, `Delivered`, `Cancelled`), `shipping_method_id`, `sale_id` |
| `shipping_methods` | retirada, entrega própria por faixa de CEP, transportadora |
| `online_payments` | `online_order_id`, `provider`, `status`, `provider_ref` |

## 12. Ligações entre os módulos

```
catalog_products.products ──item_id──► inventory.items ──► stock_positions (por local)
        │                                                        ▲
        │ ProductChanged / PriceChanged                           │ baixa agregada (contrato IStockMovements)
        ▼                                                        │
pos.catalog_items ──foto + delta──► aparelho ──eventos──► pos.device_events ──projeta──► sales.sales
                                                                                           │
pos.cash_sessions ◄──cash_session_id────────────────────────────────────────────────────────┤
        │ CashSessionClosed                                                                 │ SaleCompleted
        ▼                                                                                   ▼
finance.receivables (adquirente ou cliente)                                   fiscal.outgoing_invoices (NFC-e)
finance.bank_transfers (sangria, suprimento, depósito, quebra; pos_cash_session_id)               │
conta Caixa "Quebra de caixa" (Pix não localizado e quebra)                                 ▼
        ▲                                                                              EmissorFiscal
        └── conciliação bancária existente (BNK-03b) casa o depósito da adquirente
```

| De | Para | Como |
|---|---|---|
| Venda | Estoque | Evento `SaleCompleted` → consumidor idempotente → `IStockMovements` no local da loja. Nunca na transação do caixa |
| Sessão de caixa | Financeiro | Evento `CashSessionClosed` → resumos (dinheiro recolhido ao Cofre por transferência, ficando na conta Caixa só o fundo; cartão em título contra a adquirente; Pix manual no banco, a conferir; quebra em `bank_entries`). Idempotente por sessão |
| Venda de canal sem caixa | Financeiro | Evento `SaleCompleted` → recebível por dia e canal, sem sessão de caixa (VND-01) |
| PDV | Venda | O `Pos` chama `ISaleIngestion` na transação do lote; não escreve tabela do `Sales` |
| Sangria e suprimento | Financeiro | Evento `CashMovementRecorded` → `bank_transfers` entre o Caixa do PDV e o Cofre |
| Venda | Fiscal | Pedido de emissão com `nfce-{saleId}-{tentativa}`; a venda segue com `fiscal_status = Pending` |
| Fiscal | Venda | `InvoiceAuthorized`, `InvoiceRejected`, `InvoiceCancelled` |
| Comanda, OS, pedido online | Venda | `IOrderPayments`: cada um vira uma venda com o `channel` correspondente |

## 13. Volume

Estimativas da análise de arquitetura, não medidas:
- Loja grande de supermercado: cerca de 400 mil linhas de venda por dia. Por isso a baixa de estoque é agregada e o financeiro recebe resumos.
- `pos.device_events` e `sales.sale_lines` são as tabelas que crescem. Guid v7 já dá localidade no índice. Particionar por mês só se o teste de carga pedir.
- No Emissor, cada NFC-e guarda o payload, o XML assinado e o autorizado: dezenas de GB por loja de supermercado por ano (lacuna 34).
