# ADR 0001 — Banco de dados separado por tenant

- Status: Aceito (16/10/2026). **Alterada pela ADR 0004 (resolução pelo host completo, `catalog.tenant_domains`, em vez de subdomínio).**
- Decisão: um banco PostgreSQL por tenant + banco de catálogo, tenant resolvido por subdomínio.
- Motivo: isolamento físico dos dados, backup/restauração por cliente, desejo do PO de SaaS com banco separado.
- Custo: +15–25% no esforço da Fase 0 (≈ +3–5% no MVP). Migrar depois seria muito mais caro.
- Consequências: migrator multi-banco, provisionamento automatizado, jobs/webhooks sempre com tenantId. Ver docs/02-arquitetura.md §2.
