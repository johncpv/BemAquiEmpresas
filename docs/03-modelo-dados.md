# 03 — Modelo de dados (lógico)

Campos-base omitidos em todas as tabelas: `id`, auditoria (`created_*`, `updated_*`), `is_deleted` em cadastros. `→` indica FK. Nomes em inglês (código); descrição em português.

## catalog (banco de plataforma)
- **tenants** (FND-02a): key (identificador curto, único), legal_name, trade_name, document, status (Provisioning/Active/Suspended), plan_id, db_name, connection_secret (cifrado, FND-11), created_at — auditado em `audit.audit_log` do catálogo
- **tenant_domains** (FND-02a, ADR 0004): tenant_id, host (normalizado, **único**), is_primary, verified_at — o tenant é resolvido pelo host completo; o host da plataforma nunca entra aqui
- **plans**: name, limits (JSON: usuários, armazenamento), price
- **plan_modules** (FND-06): plan_id, module_code (único por plano). Módulos que o plano libera; plano novo nasce com todos (ADR 0010 item 4)
- **tenant_modules** (FND-06): tenant_id, module_code (único por tenant), enabled, is_exception, released_at, updated_at. Liberação efetiva por tenant: herda do plano; a mudança manual do super admin vira exceção e sobrevive à troca de plano. Trigger com NOTIFY invalida o cache do tenant
- **platform_admins** (FND-13, ADR 0012): email, normalized_email (único), password_hash, security_stamp, status (Invited/Active/Inactive), access_failed_count, lockout_end, last_login_at, totp_enabled, perm_version (FND-06c, cache versionado), totp_secret_cipher, totp_last_step, created_at
- **platform_admin_tokens** (FND-13): admin_id, token_hash (único), purpose (SetPassword/Verify/Setup), created_at, expires_at, attempts, used_at, invalidated_at
- **platform_recovery_codes** (FND-13): admin_id, code_hash, created_at, used_at
- **platform_sessions** (FND-13): admin_id, token_hash (único), host, created_at, last_seen_at, revoked_at, created_by_ip
- **audit.audit_log** do catálogo: auditoria da plataforma (`entity = "PlatformAuth"` para login do super admin), imutável
- **platform_alerts** (FND-12): key (tipo:tenant, único), last_sent_at, occurrences, last_summary — deduplicação de alertas
- **provisioning_logs** (FND-03a): tenant_id, step (Register/CreateRole/CreateDatabase/Migrate/Seed/CreateOwner/SendInvite/Activate), status (Started/Succeeded/Failed), message (nunca com segredo), at
- **tenants** recebe também `schema_version` (última migration aplicada no banco do tenant) e `migrated_at` (FND-03a)
- **security.data_protection_keys** (FND-11, ADR 0007): id, friendly_name, xml. É o key ring do Data Protection. Cada chave é gravada cifrada pelo certificado de proteção, que fica fora do banco.

## identity
- **users** (FND-04a, ADR 0011): name, email, normalized_email (único), password_hash (PBKDF2 do Identity), security_stamp, status (Invited/Active/Inactive), access_failed_count, lockout_end, last_login_at, totp_enabled, totp_secret_cipher (cifrado, FND-05), totp_last_step (anti-replay); employee_id (CAD-04b: opcional, único, gravado só por `IUserEmployeeLink`)
- **modules** (FND-06, seed do `ModuleCatalog`, igual em todos os tenants): code (`finance`, único), name, area (uma das 7 do dock), phase, order, description, approve_meaning
- **module_actions** (FND-06, ADR 0003): module_code, action (único por módulo), label, description, is_standard. Ações padrão e específicas (snake_case) aplicáveis ao módulo
- **groups** (FND-06b): name (único entre ativos), description, is_system (Proprietário), is_active, requires_2fa, seed_key (grupos do seed: o seed não recria), created_at; auditada
- **group_modules**: group_id, module_code, enabled
- **group_permissions**: group_id, module_code, action (padrão ou específica do `ModuleCatalog`; único por grupo e módulo). Só vale se o módulo estiver habilitado no grupo; o Proprietário não grava linhas
- **user_groups**: user_id, group_id (N:N)
- **user_invites**: user_id, email, group_ids (uuid[], validados pela delegação no envio e no aceite), token_hash, expires_at (72 h), accepted_at, canceled_at, invited_by, is_owner, created_at
- Permissão efetiva (calculada e em cache): ∪ group_permissions dos grupos ativos do usuário, com módulo habilitado, ∩ módulos liberados do tenant
- **refresh_tokens** (FND-04a): user_id, token_hash (SHA-256, único), family_id, created_at, expires_at, replaced_at, replaced_by_id, revoked_at, created_by_ip
- **password_resets** (FND-04): user_id, token_hash, expires_at (1 h), used_at, created_at
- **kiosk_tokens**: name, token_hash, scope (painel + filtros JSON), last_seen_at, revoked_at
- **user_preferences** (FND-09a, ADR 0009): user_id, key (único por usuário: `theme`; `help:<chave>` para ajuda já vista, FND-14/ADR 0013), value, updated_at
- **tenant_settings** (FND-05): key (única), value, updated_at, updated_by; auditada. Chaves: `security.require_2fa`; `payroll.charges_pct` (numeric(5,2), seed 80,00) e `pricing.mode` (`markup` | `margin`, seed `markup`) (CAD-06). A garantia padrão fica no Inventory (ADR 0016)
- **seed_patches** (CAD-01a, ADR 0016): key (única), applied_at. Patches versionados que só acrescentam concessões aos grupos com `seed_key` e incrementam o `perm_version`
- **ai_settings** (FND-15, ADR 0014): provider (`anthropic` | `gemini` | `openai`), model, api_key_cipher (cifrada com `SecretKind.AiApiKey`, nunca volta pela API), key_set_at, updated_at, updated_by; uma linha por tenant; auditada sem a chave
- **ai_usage_logs** (FND-15): at, user_id, provider, model, input_tokens, output_tokens, duration_ms, outcome (`answered` | `no_citation` | `failed` | `test_ok` | `test_failed`)
- **recovery_codes** (FND-05): user_id, code_hash (SHA-256), created_at, used_at. 10 por usuário, de uso único
- **two_factor_challenges** (FND-05, ADR 0011): user_id, token_hash (único), purpose (Verify/Setup), created_at, expires_at (5 min), attempts (máx. 5), completed_at, invalidated_at, created_by_ip

## settings (dono: Identity, ADR 0014 e 0016)
- **company_profile** (CAD-06): legal_name, trade_name, state_registration, municipal_registration, zip_code, street, number, complement, district, city, city_ibge, state, phone, email; uma linha por tenant; auditada. O logo é o anexo `TenantLogo` mais recente com verificação `Clean` (sem coluna `logo_attachment_id`: o anexo novo fica pendente enquanto o anterior continua valendo). Fonte da verdade de razão social e nome fantasia; o CNPJ vem do catálogo, só leitura

## shared
- **number_sequences** (schema `shared`, FND-07a): doc_type, year (0 = sem ano), last_value; único por (doc_type, year). Prefixo e dígitos vêm de `NumberFormats` (padrão de `04-fluxos` §4) mais as exceções de `number_formats`. "Próximo número" por `INSERT … ON CONFLICT DO UPDATE SET last_value = GREATEST(…)` (ADR 0016). Toda tabela de documento numerado tem índice único em `number`
- **number_formats** (schema `shared`, CAD-06): doc_type (único), prefix (1–6, A–Z/0–9, único entre os tipos), yearly (bloqueado depois do primeiro número), digits (3–8), updated_at; auditada (quem mudou fica no `audit_log`); só as exceções ao padrão, gravadas pelo `INumberFormatAdmin`
- **attachments** (schema `shared`, FND-07c): owner_type, owner_id, file_name (só exibição), content_type (detectado), size, sha256, storage_path (aleatório), scan_status (Pending/Clean/Infected/Failed), scanned_at, created_at, uploaded_by
- **audit_log** (schema `audit`, FND-07a): entity, entity_id, action (Created/Updated/Deleted e eventos de autenticação, até 64 caracteres), user_id, at, changes (jsonb, só campos alterados; `[PersonalData]` mascarado, `[ProtectedSecret]` só `changed`), ip, origin (MIG-01a: quem gravou sem usuário logado, ex.: "Importação Dolibarr · operador · execução"; também no catálogo). **Imutável**: trigger recusa UPDATE/DELETE/TRUNCATE. `action = "SensitiveRead"` registra leitura de dado sensível (CAD-04a, ADR 0016), visível só ao Proprietário
- **purge_log** (schema `audit`, ADR 0016): at, migration, rows, reason — registro do expurgo controlado de dado pessoal que tenha chegado à auditoria (nasce com o primeiro expurgo)
- **outbox_messages** (schema `shared`, FND-07b): id (v7), type, payload (jsonb), occurred_at, attempts, next_attempt_at, processed_at, dead_lettered_at, error, correlation_id
- **inbox_messages** (schema `shared`, FND-07b): message_id, handler (PK composta), processed_at — entrega idempotente por handler
- **notifications** (schema `shared`, FND-07c): user_id, type, title, link (obrigatório, rota relativa do app), created_at, read_at
- **email_logs** (schema `shared`, FND-08): to (`[PersonalData]`), template, subject, model (jsonb), status (Queued/Sent/Failed/Skipped), attempts, error, related_type, related_id, created_at, sent_at

## parties
- **parties** (CAD-01a): kind (PF/PJ), document (opcional; único parcial `WHERE NOT is_deleted AND document IS NOT NULL`; `[PersonalData]`), legal_name, trade_name, state_registration, municipal_registration, tax_regime, email, phone, notes, is_active (CAD-01a); default_payment_term_id (CAD-05b, só o id de `finance.payment_terms`); import_source (MIG-01b, ex.: `dolibarr:llx_societe:42`, gravado só pelo `IPartyImport`)
- **party_roles** (CAD-01a): party_id, role (Customer/Supplier/Installer/Carrier); único por terceiro e papel
- **party_addresses** (CAD-01a): party_id, type (Billing/Delivery/Site), zip, street, number, complement, district, city, city_ibge, state, is_default (CAD-01a; único por terceiro e tipo)
- **party_contacts** (CAD-01a): party_id, name, role, email, phone, receives_billing
- **cnpj_lookups** (CAD-01b): document (único), payload (jsonb, sem o quadro societário), looked_up_at. Cache da consulta de CNPJ por tenant, apagado aos 30 dias por um job diário
- **cnpj_lookups** (CAD-01b): document, payload (jsonb, sem QSA), looked_up_at; cache por tenant, expurgado aos 30 dias
- As condições de pagamento pertencem ao Finance (`finance.payment_terms`, ADR 0016)

## hr
- **job_positions** (CAD-04a): name, cbo, salary_min, salary_max, reference_salary (padrão na média da faixa), monthly_hours (semanais × 5, editável), hourly_cost (numeric(18,6), calculado/ajustável), hourly_cost_manual, work_center_id (padrão), is_active. Faixa e salário de referência são `[PersonalData]` e só saem em `/sensitive`
- **employees** (CAD-04b, base): name, registration (matrícula, texto opcional, única), cpf, rg, pis, ctps_number, ctps_series, voter_id, birth_date, zip, street, number, complement, district, city, state, phone, email, admission_date, termination_date, job_position_id, base_salary, work_schedule_id, union_id, status (Active/Terminated; OnLeave na CAD-04c). **CAD-04c:** bank_code, bank_agency, bank_account, bank_account_type, pix_key_type, pix_key, transport_voucher, insalubrity_pct (10/20/40), hazard_pay, cnh_number, cnh_category, cnh_expiry, military_certificate. Campos pessoais `[PersonalData]`, só em `/sensitive`
- **employee_dependents** (CAD-04c): employee_id, name, cpf (só quando ir_dependent), birth_date, kinship, ir_dependent, family_allowance
- **employee_history**: employee_id, type (Salary/Position na CAD-04b; Leave na CAD-04c), effective_date, old_value, new_value, reason (Leave: até 200 caracteres, sem CID)
- **work_schedules** (CAD-04a): name, weekly_hours, days (JSON com entradas/saídas), is_active, seed_key (seed "44 h — seg a sex 08:00–17:48")
- **unions** (CAD-04a): name, document (CNPJ), base_month, is_active
- **time_entries**: employee_id, at, type (In/Out/BreakStart/BreakEnd), source (PWA/Manual), lat, lng, justification, approved_by
- **timesheets**: employee_id, period (ano-mês), worked_hours, overtime_hours, absences, hour_bank_balance, status (Open/Closed)
- **payroll_runs**: period, type (Monthly/Vacation/13th/Termination), mode (Entered/Calculated), status (Draft/Closed/Paid)
- **payslips**: payroll_run_id, employee_id, gross, deductions, net, fgts_base, inss_base, irrf_base
- **payslip_lines**: payslip_id, code, description, reference, type (Earning/Deduction), amount
- **payroll_tax_tables** (fase 7): type (INSS/IRRF/SalarioFamilia), valid_from, brackets (JSON)

## commercial
- **quotes**: number, revision, party_id, contact_id, site_address_id, status (Draft/Sent/Approved/Rejected/Expired/Superseded), valid_until, delivery_days_promised, payment_term (JSON por marco), notes, total_cost, total_price, margin_pct, lost_reason, parent_quote_id (revisão anterior)
- **quote_items**: quote_id, item_kind (Equipment/Product/Service), product_id (opcional), description, quantity, unit_cost, unit_price, routing_template_id
- **quote_item_costs**: quote_item_id, cost_type (Material/Labor/Installation/Freight/Tax/Other), work_center_id, item_id, quantity, unit_cost, total
- **contracts**: number, quote_id, party_id, signed_at, total_value, status (Active/Completed/Cancelled), snapshot (jsonb do orçamento aprovado), file (attachment)
- **contract_milestones**: contract_id, sequence, description, percent, amount, trigger (Date/Event), trigger_event (Signature/FabricationDone/Delivered/TechnicalAcceptance), due_date, receivable_id
- **contract_amendments**: contract_id, number, description, value_delta, days_delta, approved_at
- **projects**: number, contract_id, party_id, name, site_address_id, promised_date, forecast_date, status (Planned/InProgress/Fabricated/Installing/Delivered/Closed/Cancelled), priority (int para fila), manager_user_id, warranty_until
- **project_items**: project_id, quote_item_id, description, quantity, product_id, routing_template_id
- **project_budget**: project_id, cost_type, budgeted_amount (do contrato) — base do orçado × realizado
- **project_costs** (view/tabela materializada): project_id, cost_type, source (StockIssue/Labor/Purchase/Payable/Payroll), source_id, amount, date

## engineering
- **routing_templates**: name, product_type, is_active
- **routing_template_steps**: template_id, sequence, name, work_center_id, standard_hours, requires_approval, is_quality_step, checklist (JSON)
- **boms**: production_order_id, revision, status (Draft/Released), released_at, released_by, copied_from_bom_id
- **bom_lines**: bom_id, item_id, quantity, unit_id, scrap_pct, notes

## production
- **work_centers** (CAD-03): name, line, hours_per_day (numeric(5,2), derivado dos turnos), shifts (jsonb `[{start,end}]` em "HH:mm", sem turno que vira o dia nem sobreposição), calendar_id, is_active, created_at (antes dela a capacidade é 0); soft delete; nome único sem diferenciar maiúsculas
- **calendars** (CAD-03): name, is_default (um padrão por tenant no MVP), working_days (jsonb com os nomes dos dias, ex.: `["Monday", …]`, enum `Weekday`)
- **calendar_exceptions** (CAD-03): calendar_id, date (único por calendário), type (Holiday/Stoppage), hours_available (numeric(5,2), 0 a 24), description; entidade `CalendarExceptionDay` (o analisador proíbe tipo terminado em Exception); excluída de verdade, com auditoria
- **production_orders**: number, type (Fabrication/Installation/Maintenance), project_id, project_item_id, product_id, quantity, status (Open/InProgress/Paused/Quality/Completed/Cancelled), priority, planned_start, planned_end, actual_start, actual_end, executor_party_id (instalação terceirizada), serial_numbers
- **production_order_steps**: order_id, sequence, name, work_center_id, standard_hours, status (Waiting/Ready/InProgress/Paused/Done/Rejected), planned_start, planned_end, actual_start, actual_end, checklist_result (JSON)
- **labor_entries**: order_step_id, employee_id, started_at, ended_at, paused_minutes, hourly_cost_snapshot, cost, notes
- **step_pauses**: labor_entry_id, reason (Material/Machine/Break/Other), started_at, ended_at
- **non_conformities**: number, order_id, order_step_id, description, cause, action, responsible_user_id, return_to_step_id, status (Open/InAction/Closed), cost
- **downtimes**: work_center_id, asset_id, reason, started_at, ended_at

## inventory
- **units** (CAD-02a, classe `MeasureUnit`): code (até 6, maiúsculas, único entre os não excluídos; seed UN, KG, M, M2, M3, L, PC), name, is_active, seed_key; soft delete; em uso por item não inativa nem exclui; import_source entra com a MIG-01c
- **item_categories** (CAD-02a): name (único entre as irmãs, sem acento nem caixa), parent_id (sem ciclo), is_active; soft delete; com itens ou filhas só inativa; import_source entra com a MIG-01c
- **items** (CAD-02a): code (digitado, único), name, type (`ItemType` em `Inventory.Contracts`: RawMaterial/Component/Finished/Consumable/Service), category_id, unit_id (estoque), ncm (obrigatório para item ativo de RawMaterial/Component/Finished), cest, origin, min_stock, tracks_lot, tracks_serial, warranty_months (CAD-02a), average_cost (numeric(18,6), só leitura, EST-01), is_active, search_text (código e nome sem acento, índice pg_trgm); código único entre os não excluídos; min_stock numeric(18,4); soft delete; import_source (MIG-01c)
- **warranty_defaults** (CAD-02a, ADR 0016): item_type (único), months (nulo = sem garantia); seed Finished 12, Component 3, Service 3
- **item_unit_conversions** (CAD-02b): item_id, from_unit_id, to_unit_id, factor (numeric(18,6); resultado com 4 casas, AwayFromZero)
- **item_supplier_codes** (CAD-02b): item_id, party_id, supplier_code, supplier_unit (texto livre), conversion_factor (de-para da NF de entrada); import_source (MIG-01c)
- **locations** (CAD-03): name, code, is_default (único), is_active, seed_key (seed "ALM — Almoxarifado principal"); código único entre os não excluídos; soft delete; import_source (MIG-01c)
- **lots**: item_id, lot_number, supplier_party_id, received_at, expiry
- **serials**: item_id, serial_number, production_order_id, status (InStock/Shipped), project_id
- **stock_balances**: item_id, location_id, lot_id, quantity_on_hand, quantity_reserved (disponível = on_hand − reserved)
- **reservations**: item_id, location_id, production_order_id, bom_line_id, quantity, quantity_issued, status (Active/Consumed/Released)
- **stock_movements**: number, type (Receipt/Issue/Transfer/Adjustment/Return/Production/Shipment; OpeningBalance "Saldo inicial" entra com a EST-01, ADR 0016), item_id, location_id, lot_id, serial_id, quantity (com sinal), unit_cost, total_cost, source_type, source_id, project_id, at, user_id — imutável
- **inventory_counts / inventory_count_lines**: location_id, status, item_id, lot_id, system_qty, counted_qty, adjusted

## purchasing
- **purchase_requests**: number, origin (Manual/BOMShortage/MinStock), project_id, production_order_id, status (Open/Quoting/Ordered/Cancelled)
- **purchase_request_lines**: request_id, item_id, quantity, needed_by
- **rfqs (cotações)**: number, request_id, status
- **rfq_suppliers / rfq_lines**: rfq_id, party_id, item_id, quantity, unit_price, lead_days, freight, selected
- **purchase_orders**: number, party_id, rfq_id, project_id, status (Draft/PendingApproval/Approved/Sent/PartiallyReceived/Received/Cancelled), payment_term_id, expected_date, total
- **purchase_order_lines**: order_id, item_id, quantity, unit_price, quantity_received
- **goods_receipts**: number, purchase_order_id, incoming_invoice_id, received_at, status
- **goods_receipt_lines**: receipt_id, po_line_id, item_id, quantity, lot_number, location_id, unit_cost

## finance
- **chart_of_accounts** (CAD-05a): code (máscara `9.9.99`), name, type (Revenue/Expense/Asset/Liability), parent_id, accepts_entries (derivado), dre_group, is_active, seed_key
- **cost_centers** (CAD-05a): code, name, parent_id, is_active, seed_key
- **bank_accounts** (CAD-05b): name, type (Bank/Cash), bank_code, agency, account, opening_balance (numeric(18,2), anulável), opening_date (date, anulável, junto com o saldo), is_active, is_default (único entre as ativas); import_source (MIG-01b, entra sem saldo); provider (Inter/OFX/None) e credentials_secret entram na BNK-01
- **payment_terms** (CAD-05b, dono Finance, ADR 0016): name, installments (jsonb: dias e percentual numeric(7,4), soma 100), is_active, seed_key (seed À vista, 30, 30/60 e 30/60/90 dias); import_source (MIG-01b). Exposta por `IPaymentTerms` em `Finance.Contracts`
- **receivables**: number, party_id, project_id, contract_milestone_id, account_id, cost_center_id, description, issue_date, due_date, amount, interest, fine, discount, paid_amount, status (Planned/Open/PartiallyPaid/Paid/Overdue/Cancelled), billing_slip_id
- **payables**: number, party_id, project_id, account_id, cost_center_id, origin (Purchase/Payroll/Installer/Recurring/Manual), origin_id, description, due_date, amount, paid_amount, status (Planned/Open/AwaitingApproval/Approved/Paid/Cancelled)
- **payment_orders**: number, payable_ids, total, requested_by, status (Pending/Approved/Rejected/Paid/Cancelled), approval_rule_id, scheduled_date
- **payment_order_approvals**: payment_order_id, approver_user_id, level, decision, comment, at
- **approval_rules**: document_type (PaymentOrder/PurchaseOrder), min_amount, max_amount, account_id (opcional), approver_role_id/user_id, level
- **recurring_expenses**: description, party_id, account_id, cost_center_id, amount, day_of_month, start, end, last_generated
- **financial_transactions**: bank_account_id, date, amount (com sinal), type (Receipt/Payment/Transfer/Fee/Interest), receivable_id, payable_id, payment_order_id, reconciled, statement_line_id
- **billing_slips** (boletos): receivable_id, provider, our_number, barcode, digitable_line, pix_copy_paste, qr_code, pdf_path, status (Issued/Paid/Cancelled/Expired), paid_at, paid_amount, provider_payload
- **bank_statement_lines**: bank_account_id, date, amount, description, external_id, source (API/OFX), matched_transaction_id
- **customer_payment_stats** (view): party_id, avg_delay_days, pct_overdue, last_12m_invoiced

## fiscal
- **fiscal_settings**: regime, ie, cnae, certificate_secret, certificate_expiry, environment, nfe_series, next_number
- **tax_rules**: operation (Sale/Return/Remittance/Service), cfop, cst_csosn, icms/ipi/pis/cofins/ibs/cbs params (JSON), conditions (UF destino, tipo de cliente, NCM)
- **outgoing_invoices**: number, series, access_key, type (NFe/NFSe), party_id, project_id, shipment_id, status (Draft/Processing/Authorized/Rejected/Cancelled), xml_path, pdf_path, protocol, totals (JSON), receivable_ids
- **outgoing_invoice_items**: invoice_id, item_id, serial_ids, ncm, cfop, quantity, unit_price, taxes (JSON)
- **incoming_invoices**: access_key, party_id, number, series, issue_date, total, source (XML/PDF/DFe), status (Imported/Checked/Received/Rejected), xml_path, pdf_path, purchase_order_id
- **incoming_invoice_items**: invoice_id, supplier_code, description, ncm, cfop, quantity, unit, unit_price, item_id (de-para), conversion_factor
- **fiscal_events**: invoice_id, type (Cancel/CCe), protocol, at, payload

## assets
- **assets**: tag (código do QR), name, type, brand, model, serial_number, acquisition_date, acquisition_value, invoice_ref, location_id, responsible_employee_id, work_center_id, status (Active/Maintenance/Disposed), photo
- **asset_movements**: asset_id, from/to location, from/to responsible, at, reason
- **maintenance_plans**: asset_id, description, frequency_type (Days/Hours), frequency_value, last_done_at, next_due_at, checklist (JSON)
- **maintenance_orders**: number, asset_id, plan_id, type (Preventive/Corrective), status, scheduled_at, done_at, cost, supplier_party_id, notes, payable_id

## migration (MIG-01a, ADR 0017; módulo `Migrations`, fora do `ModuleCatalog`)
- **import_runs**: started_at, finished_at, mode (DryRun/Real), entities, status (Running/Succeeded/Failed), summary (jsonb, totais por entidade), operator, snapshot_taken_at, failure, accepted_at, purged_at
- **import_items**: run_id, entity, source_table, source_id, payload (jsonb, colunas pessoais mascaradas), hash, outcome (Imported/Updated/Unchanged/Merged/Linked/Skipped/Conflict/Error), target_type, target_id, message, warning, differences (jsonb, os dois valores do conflito, mascarados) — expurgada após o aceite e em até 30 dias (ADR 0018)
- **external_refs**: source, source_table, source_id (únicos juntos), target_type, target_id, source_hash, target_xmin, imported_at, last_run_id

## Relacionamentos-chave
- quote 1—N quote_items 1—N quote_item_costs · quote 1—0..1 contract (a revisão aprovada)
- contract 1—N contract_milestones 1—1 receivable · contract 1—1 project
- project 1—N project_items 1—N production_orders (Fabrication) · project 1—N production_orders (Installation)
- production_order 1—N production_order_steps 1—N labor_entries · production_order 1—N boms (revisões) 1—N bom_lines 1—1 reservation
- stock_movements referenciam a origem (source_type/source_id) e o projeto → base do custo realizado
- purchase_order → goods_receipt → stock_movements (Receipt) + payable
- payables N—1 payment_order (uma OP pode agrupar vários títulos) → financial_transactions
