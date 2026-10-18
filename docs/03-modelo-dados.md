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
- **users** (FND-04a, ADR 0011): name, email, normalized_email (único), password_hash (PBKDF2 do Identity), security_stamp, status (Invited/Active/Inactive), access_failed_count, lockout_end, last_login_at, totp_enabled, totp_secret_cipher (cifrado, FND-05), totp_last_step (anti-replay); employee_id opcional entra com o RH
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
- **tenant_settings** (FND-05): key (única), value, updated_at, updated_by; auditada. Chaves: `security.require_2fa`; depois logo, dados da empresa, numeração
- **ai_settings** (FND-15, ADR 0014): provider (`anthropic` | `gemini` | `openai`), model, api_key_cipher (cifrada com `SecretKind.AiApiKey`, nunca volta pela API), key_set_at, updated_at, updated_by; uma linha por tenant; auditada sem a chave
- **ai_usage_logs** (FND-15): at, user_id, provider, model, input_tokens, output_tokens, duration_ms, outcome (`answered` | `no_citation` | `failed` | `test_ok` | `test_failed`)
- **recovery_codes** (FND-05): user_id, code_hash (SHA-256), created_at, used_at. 10 por usuário, de uso único
- **two_factor_challenges** (FND-05, ADR 0011): user_id, token_hash (único), purpose (Verify/Setup), created_at, expires_at (5 min), attempts (máx. 5), completed_at, invalidated_at, created_by_ip

## shared
- **number_sequences** (schema `shared`, FND-07a): doc_type, year (0 = sem ano), last_value; único por (doc_type, year). Prefixo e dígitos vêm de `NumberFormats` (padrão de `04-fluxos` §4, editável por tenant na CAD-06)
- **attachments** (schema `shared`, FND-07c): owner_type, owner_id, file_name (só exibição), content_type (detectado), size, sha256, storage_path (aleatório), scan_status (Pending/Clean/Infected/Failed), scanned_at, created_at, uploaded_by
- **audit_log** (schema `audit`, FND-07a): entity, entity_id, action (Created/Updated/Deleted e eventos de autenticação, até 64 caracteres), user_id, at, changes (jsonb, só campos alterados; `[PersonalData]` mascarado, `[ProtectedSecret]` só `changed`), ip. **Imutável**: trigger recusa UPDATE/DELETE/TRUNCATE
- **outbox_messages** (schema `shared`, FND-07b): id (v7), type, payload (jsonb), occurred_at, attempts, next_attempt_at, processed_at, dead_lettered_at, error, correlation_id
- **inbox_messages** (schema `shared`, FND-07b): message_id, handler (PK composta), processed_at — entrega idempotente por handler
- **notifications** (schema `shared`, FND-07c): user_id, type, title, link (obrigatório, rota relativa do app), created_at, read_at
- **email_logs** (schema `shared`, FND-08): to (`[PersonalData]`), template, subject, model (jsonb), status (Queued/Sent/Failed/Skipped), attempts, error, related_type, related_id, created_at, sent_at

## parties
- **parties**: kind (PF/PJ), document, legal_name, trade_name, state_registration, municipal_registration, tax_regime, email, phone, notes, default_payment_term_id
- **party_roles**: party_id, role (Customer/Supplier/Installer/Carrier)
- **party_addresses**: party_id, type (Billing/Delivery/Site), zip, street, number, complement, district, city, city_ibge, state
- **party_contacts**: party_id, name, role, email, phone, receives_billing
- **payment_terms**: name, installments (JSON: dias/percentual)

## hr
- **job_positions**: name, cbo, salary_min, salary_max, hourly_cost (calculado/ajustável), work_center_id (padrão)
- **employees**: name, cpf, rg, pis, birth_date, address, phone, email, bank/pix, admission_date, termination_date, job_position_id, base_salary, work_schedule_id, union_id, transport_voucher, insalubrity_pct, hazard_pay, status
- **employee_dependents**: employee_id, name, cpf, birth_date, kinship, ir_dependent, family_allowance
- **employee_history**: employee_id, type (Salary/Position/Leave), effective_date, old_value, new_value, reason
- **work_schedules**: name, weekly_hours, days (JSON com entradas/saídas)
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
- **work_centers**: name, line, hours_per_day, shifts (JSON), calendar_id, is_active
- **calendars / calendar_exceptions**: date, type (Holiday/Shutdown), hours_available
- **production_orders**: number, type (Fabrication/Installation/Maintenance), project_id, project_item_id, product_id, quantity, status (Open/InProgress/Paused/Quality/Completed/Cancelled), priority, planned_start, planned_end, actual_start, actual_end, executor_party_id (instalação terceirizada), serial_numbers
- **production_order_steps**: order_id, sequence, name, work_center_id, standard_hours, status (Waiting/Ready/InProgress/Paused/Done/Rejected), planned_start, planned_end, actual_start, actual_end, checklist_result (JSON)
- **labor_entries**: order_step_id, employee_id, started_at, ended_at, paused_minutes, hourly_cost_snapshot, cost, notes
- **step_pauses**: labor_entry_id, reason (Material/Machine/Break/Other), started_at, ended_at
- **non_conformities**: number, order_id, order_step_id, description, cause, action, responsible_user_id, return_to_step_id, status (Open/InAction/Closed), cost
- **downtimes**: work_center_id, asset_id, reason, started_at, ended_at

## inventory
- **units**: code (UN, KG, M, M2, M3, L, PC), name
- **item_categories**: name, parent_id
- **items**: code, name, type (RawMaterial/Component/Finished/Consumable/Service), category_id, unit_id (estoque), ncm, cest, origin, min_stock, tracks_lot, tracks_serial, average_cost, is_active
- **item_unit_conversions**: item_id, from_unit_id, to_unit_id, factor
- **item_supplier_codes**: item_id, party_id, supplier_code, supplier_unit, conversion_factor (de-para da NF de entrada)
- **locations**: name, code, is_default
- **lots**: item_id, lot_number, supplier_party_id, received_at, expiry
- **serials**: item_id, serial_number, production_order_id, status (InStock/Shipped), project_id
- **stock_balances**: item_id, location_id, lot_id, quantity_on_hand, quantity_reserved (disponível = on_hand − reserved)
- **reservations**: item_id, location_id, production_order_id, bom_line_id, quantity, quantity_issued, status (Active/Consumed/Released)
- **stock_movements**: number, type (Receipt/Issue/Transfer/Adjustment/Return/Production/Shipment), item_id, location_id, lot_id, serial_id, quantity (com sinal), unit_cost, total_cost, source_type, source_id, project_id, at, user_id — imutável
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
- **chart_of_accounts**: code, name, type (Revenue/Expense/Asset/Liability), parent_id, accepts_entries, dre_group
- **cost_centers**: code, name, parent_id
- **bank_accounts**: name, type (Bank/Cash), bank_code, agency, account, provider (Inter/OFX/None), credentials_secret, opening_balance, opening_date
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

## Relacionamentos-chave
- quote 1—N quote_items 1—N quote_item_costs · quote 1—0..1 contract (a revisão aprovada)
- contract 1—N contract_milestones 1—1 receivable · contract 1—1 project
- project 1—N project_items 1—N production_orders (Fabrication) · project 1—N production_orders (Installation)
- production_order 1—N production_order_steps 1—N labor_entries · production_order 1—N boms (revisões) 1—N bom_lines 1—1 reservation
- stock_movements referenciam a origem (source_type/source_id) e o projeto → base do custo realizado
- purchase_order → goods_receipt → stock_movements (Receipt) + payable
- payables N—1 payment_order (uma OP pode agrupar vários títulos) → financial_transactions
