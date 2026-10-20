# ADR 0005 — Decisões do PO após a revisão pré-Fase 0

- Status: Aceito (16/10/2026) — PO (John)
- Origem: `docs/revisoes/2026-10-16-revisao-pre-fase0.md` (itens D1–D10)

| # | Decisão |
|---|---|
| D1 | `app.konebras.com.br` continua servindo o Dolibarr até o go-live, na mesma VPS. No go-live, troca só o conteúdo: o BemAqui assume o host e o Dolibarr vai para `adm.konebras.com.br`. Durante o desenvolvimento, a validação usa um host de homologação na mesma VPS (sugestão: `homolog.konebras.com.br`, DNS pelo PO). **Altera o ADR 0004 §5 e a FND-10:** a troca de host sai da Fase 0 e vai para a GOL-01. |
| D2 | Host da plataforma: `plataforma.bemaquiapp.com.br` (super admin, painel Hangfire, webhooks), fora de `tenant_domains`, com 2FA obrigatório e allowlist de IP. |
| D3 | Fases mantidas. Go-live único, quando o PO considerar seguro, sem prazo fixo. |
| D4 | OS de serviço/manutenção, contratos recorrentes e venda avulsa entram como **Fase 10**, planejada depois de todo o restante executado. |
| D5 | Aprovação do orçamento = assinatura do contrato, num único evento. Aprovar gera o contrato `Active` (`signed_at` = data da aprovação), o projeto, o orçamento do projeto e os recebíveis. O anexo do contrato assinado é opcional e pode ser incluído depois. Os eventos `QuoteApproved` e `ContractSigned` são publicados no mesmo comando. |
| D6 | Backup diário, ou seja, perda máxima aceita de 24 h. O dump é cifrado, guardado fora da VPS e testado na restauração. |
| D7 | Risco da VPS compartilhada aceito, com as mitigações da revisão (rede interna própria para o Postgres, containers não-root, limites de recurso, monitor externo). |
| D8 | Navegação: o dock mostra as **7 áreas**. Cada área abre um menu (popover no desktop, sheet no celular) com os módulos. Sub-telas ficam em abas na topbar. Ctrl K abre a paleta de comandos. A identidade visual do protótipo é mantida. |
| D9 | Requisitos do PRD passam a ter o prefixo `R-` (ex.: R-FIN-11). Cada story do backlog lista os requisitos que atende numa linha `Requisitos:`. |
| D10 | Pendências registradas com prazo em `06-pendencias-riscos.md`. |

- Ver ADR 0020 (D1 mantida: a troca de host acontece na GOL-01, agora como promoção do banco da homologação).
