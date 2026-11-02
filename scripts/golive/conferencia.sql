-- Conferência dos dados promovidos (GOL-01a CA 8, GOL-01b CA 4). Roda no BANCO DO TENANT, só leitura, e a saída não tem dado pessoal:
-- nenhum nome de terceiro ou funcionário, documento, e-mail, conta ou agência; só nomes de contas e locais, quantidades e valores.
--
-- Parâmetro opcional: bqe.mes = 'AAAA-MM' (mês dos títulos da folha; padrão, o mês corrente em America/Sao_Paulo).
-- Uso: psql "$TENANT_DB" -v ON_ERROR_STOP=1 [-c "SET bqe.mes = '2026-12'"] -f scripts/golive/conferencia.sql
-- Saídas, nesta ordem: 1. saldo por conta; 2. títulos em aberto; 3. títulos da folha do mês; 4. estoque por local; 5. contagens;
-- 6. asserção "nenhuma nota de homologação e nenhum boleto de sandbox" (coluna resultado = OK).
-- Dinheiro em numeric(18,2); quantidade em numeric(18,4).

BEGIN TRANSACTION READ ONLY;

-- 1. Saldo de cada conta = saldo inicial + soma das transações (FIN-00, FIN-04). Comparar com o extrato do banco do dia.
SELECT a.name AS conta,
       a.kind AS tipo,
       a.is_active AS ativa,
       (coalesce(a.opening_balance, 0) + coalesce(sum(t.amount), 0))::numeric(18,2) AS saldo,
       count(t.id) AS transacoes,
       count(t.id) FILTER (WHERE NOT t.reconciled) AS nao_conciliadas
FROM finance.bank_accounts a
LEFT JOIN finance.financial_transactions t ON t.bank_account_id = a.id
WHERE NOT a.is_deleted
GROUP BY a.id, a.name, a.kind, a.is_active, a.opening_balance
ORDER BY a.name;

-- 2. Títulos em aberto (a receber: aberto e parcial; a pagar: aberto, parcial, em OP aguardando aprovação ou aprovada).
WITH hoje AS (SELECT (now() AT TIME ZONE 'America/Sao_Paulo')::date AS d)
SELECT 'A receber' AS tipo,
       count(*) AS quantidade,
       coalesce(sum(r.amount - r.paid_amount), 0)::numeric(18,2) AS saldo_em_aberto,
       count(*) FILTER (WHERE r.due_date < (SELECT d FROM hoje)) AS vencidos,
       coalesce(sum(r.amount - r.paid_amount) FILTER (WHERE r.due_date < (SELECT d FROM hoje)), 0)::numeric(18,2) AS saldo_vencido
FROM finance.receivables r
WHERE r.status IN ('Open', 'PartiallyPaid')
UNION ALL
SELECT 'A pagar',
       count(*),
       coalesce(sum(p.amount - p.retention_iss - p.retention_inss - p.retention_ir - p.retention_pcc - p.paid_amount), 0)::numeric(18,2),
       count(*) FILTER (WHERE p.due_date < (SELECT d FROM hoje)),
       coalesce(sum(p.amount - p.retention_iss - p.retention_inss - p.retention_ir - p.retention_pcc - p.paid_amount)
                FILTER (WHERE p.due_date < (SELECT d FROM hoje)), 0)::numeric(18,2)
FROM finance.payables p
WHERE p.status IN ('Open', 'PartiallyPaid', 'AwaitingApproval', 'Approved');

-- 3. Títulos da folha (RH-02c, origem Payroll) com vencimento no mês: salários (favorecido funcionário) e guias (favorecido terceiro).
WITH mes AS (
    SELECT to_date(coalesce(nullif(current_setting('bqe.mes', true), ''), to_char(now() AT TIME ZONE 'America/Sao_Paulo', 'YYYY-MM')) || '-01', 'YYYY-MM-DD') AS inicio
)
SELECT to_char((SELECT inicio FROM mes), 'MM/YYYY') AS mes,
       CASE p.payee_type WHEN 'Employee' THEN 'Salários' ELSE 'Guias e encargos' END AS grupo,
       p.status AS situacao,
       count(*) AS quantidade,
       sum(p.amount)::numeric(18,2) AS valor,
       sum(p.amount - p.retention_iss - p.retention_inss - p.retention_ir - p.retention_pcc - p.paid_amount)::numeric(18,2) AS saldo_em_aberto
FROM finance.payables p
WHERE p.origin = 'Payroll'
  AND p.status <> 'Cancelled'
  AND p.due_date >= (SELECT inicio FROM mes)
  AND p.due_date < (SELECT inicio FROM mes) + interval '1 month'
GROUP BY p.payee_type, p.status
ORDER BY grupo, situacao;

-- 4. Estoque por local: físico e valor ao custo médio do item (comparar com a contagem física, D7 da Fase 3).
SELECT l.code AS local_codigo,
       l.name AS local,
       count(sp.id) FILTER (WHERE sp.quantity_on_hand <> 0) AS itens_com_saldo,
       coalesce(sum(sp.quantity_on_hand), 0)::numeric(18,4) AS quantidade,
       coalesce(sum(round(sp.quantity_on_hand * i.average_cost, 2)), 0)::numeric(18,2) AS valor
FROM inventory.locations l
LEFT JOIN inventory.stock_positions sp ON sp.location_id = l.id
LEFT JOIN inventory.items i ON i.id = sp.item_id
WHERE NOT l.is_deleted
GROUP BY l.id, l.code, l.name
ORDER BY l.code;

-- 5. Contagens (cadastros ativos e documentos).
SELECT (SELECT count(*) FROM parties.parties WHERE NOT is_deleted) AS terceiros,
       (SELECT count(*) FROM inventory.items WHERE NOT is_deleted) AS itens,
       (SELECT count(*) FROM commercial.contracts) AS contratos,
       (SELECT count(*) FROM commercial.contracts WHERE status <> 'Cancelled') AS contratos_nao_cancelados,
       (SELECT count(*) FROM commercial.projects) AS projetos;

-- 6. Asserção: nenhuma nota fiscal de homologação (enviada à SEFAZ de homologação) e nenhum boleto de sandbox (conta sem integração
--    de produção) no tenant. Rascunho de nota não é nota; aparece só para conferência.
WITH n AS (
    SELECT (SELECT count(*) FROM fiscal.outgoing_invoices WHERE environment = 'Homologation' AND status <> 'Draft') AS notas_homologacao,
           (SELECT count(*) FROM fiscal.outgoing_invoices WHERE status = 'Draft') AS notas_rascunho,
           (SELECT count(*) FROM finance.billing_slips s
            LEFT JOIN finance.bank_integrations bi ON bi.bank_account_id = s.bank_account_id
            WHERE bi.id IS NULL OR bi.environment <> 'Production') AS boletos_sandbox,
           (SELECT count(*) FROM finance.bank_integrations WHERE environment = 'Sandbox' AND status = 'Active') AS integracoes_bancarias_sandbox,
           (SELECT count(*) FROM fiscal.integration_settings WHERE environment = 'Homologation') AS emissor_em_homologacao
)
SELECT notas_homologacao, notas_rascunho, boletos_sandbox, integracoes_bancarias_sandbox, emissor_em_homologacao,
       CASE WHEN notas_homologacao = 0 AND boletos_sandbox = 0 THEN 'OK' ELSE 'FALHOU' END AS resultado
FROM n;

ROLLBACK;
