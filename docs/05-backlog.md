# 05 — Backlog por fase

Formato: `ID — Como <perfil>, quero <ação> para <benefício>` + critérios de aceite (CA).
Tamanho relativo: P (até 1 sessão de trabalho), M (2–3), G (4+). Marque `[x]` ao concluir.
Regra: não comece uma fase sem a anterior concluída, exceto quando indicado.

---

## Fase 0 — Fundação

Stories detalhadas em [`docs/stories/`](stories/README.md), com critérios de aceite completos, ordem de execução, Definition of Ready e pontos em aberto. A linha `Requisitos:` usa o prefixo `R-` (ADR 0005 D9). Aqui fica o resumo de cada uma.

**Ordem de execução** (revisão pré-Fase 0 §2.3 + stories novas): FND-01 → FND-11 → FND-07 → FND-02 → FND-03a → FND-08 → FND-12 → FND-04 → FND-05 → FND-13 → FND-03b → FND-06 → FND-06b → FND-06c → FND-06d → FND-10 → FND-14 → FND-15. Em paralelo desde a FND-01: FND-09a → FND-09b e FND-09c. Antes da Fase 1: FND-09d (D7 da revisão pré-Fase 1). Antes da Fase 2: FND-09e (revisão pré-Fase 2 §3).

**Definition of Ready:** story sem CA testável não entra em execução.

**FND-01 [x] Estrutura da solução, CI e cadeia de suprimentos (M)** — [story](stories/FND-01-estrutura-solucao.md)
- Requisitos: PRD §6 (Segurança).
- CA: `dotnet test` e `pnpm build` passam no CI; NetArchTest impede módulo referenciar outro que não pelo `*.Contracts`; `dotnet list package --vulnerable` e `pnpm audit` no CI; actions fixadas por SHA; Dependabot ativo.

**FND-11 [x] Segredos e Data Protection (M)** — nova — [story](stories/FND-11-segredos-data-protection.md)
- Requisitos: R-PLT-1, PRD §6 (Segurança).
- CA: key ring protegido por certificado e com cópia offline; propósito por tenant e por tipo de segredo; segredos write-only e nunca logados; backup da chave em trilha separada (ADR 0007).

**FND-07 [x] Building blocks (G)** — [story](stories/FND-07-building-blocks.md)
- Requisitos: R-SEC-5, R-COM-2, R-COM-3, R-COM-4, R-PRP-5.
- CA: diff gravado na mesma transação, `audit_log` só INSERT, PII mascarada e segredos omitidos; outbox transacional com retry, dead-letter e inbox idempotente; numeração sem duplicidade com 50 chamadas paralelas; anexos com allowlist, magic bytes, ClamAV e permissão do documento dono; job de tenant suspenso não roda.

**FND-02 [x] Catálogo e resolução de tenant (G)** — [story](stories/FND-02-catalogo-resolucao-tenant.md)
- Requisitos: R-PLT-1, R-PLT-2.
- CA: dois tenants isolados; host inexistente → 404; tenant suspenso → 403 com página amigável, valendo em < 10 s; token de `a.localhost` rejeitado em `b.localhost` (REST e hub); Host/X-Forwarded-For forjados ignorados; credencial de A não conecta em B; sem CORS; HSTS, CSP e frame-ancestors; Swagger desligado em prod.

**FND-03a [x] Migrator multi-tenant e provisionamento por serviço/CLI (G)** — quebra da FND-03 — [story](stories/FND-03a-migrator-provisionamento.md)
- Requisitos: R-PLT-1, R-PLT-3.
- CA: provisionar tenant novo em < 1 min; falha em uma etapa registra log e permite retomar; migrations aplicadas a todos os tenants no deploy com relatório, advisory lock e 503 para schema defasado; role do Postgres por tenant com `REVOKE CONNECT FROM PUBLIC`; role dedicado ao provisionamento.

**FND-08 [x] E-mail (M)** — [story](stories/FND-08-email.md)
- Requisitos: R-COM-1.
- CA: falha de envio faz retry 3×, depois marca falha visível no log; templates do tenant só em Fluid sandboxed; nunca buscar URL fornecida pelo tenant; links com o host primário do catálogo.

**FND-12 [x] Observabilidade (M)** — nova — [story](stories/FND-12-observabilidade.md)
- Requisitos: PRD §6 (Observabilidade, Disponibilidade).
- CA: logs JSON com tenant, usuário e correlação, sem segredos; `/health/live` e `/health/ready`; alerta de job esgotado, dead-letter, falha de migração e de integração.

**FND-04 [x] Autenticação (G)** — [story](stories/FND-04-autenticacao.md)
- Requisitos: R-SEC-1, R-SEC-3, R-SEC-4, R-SEC-5, R-PLT-3.
- CA: access 15 min / refresh 7 dias; reuso de refresh revoga a família, com tolerância a refresh concorrente; 5 tentativas erradas bloqueiam 15 min; recuperação expira em 1 h, é de uso único, revoga as sessões e não desliga o 2FA; access token só em memória; anti-CSRF por Origin + header; respostas uniformes; desativar vale no próximo request; rate limit por IP real + tenant + usuário. Inclui as telas de login, recuperação e aceite de convite.

**FND-05 [x] 2FA TOTP (M)** — [story](stories/FND-05-2fa-totp.md)
- Requisitos: R-SEC-2.
- CA: login com 2FA pede código; código de recuperação usável uma vez; proprietário obriga 2FA e usuários sem 2FA são levados à configuração no próximo login; anti-replay; 5 tentativas por desafio; token intermediário só com escopo de 2FA. Inclui as telas de 2FA.

**FND-13 [x] Autenticação do super admin (M)** — nova — [story](stories/FND-13-autenticacao-super-admin.md)
- Requisitos: R-PLT-4.
- CA: login em `plataforma.bemaquiapp.com.br` com 2FA obrigatório e allowlist de IP (ADR 0005 D2); tokens da plataforma e do tenant não valem um no outro; painel da plataforma e `/hangfire` respondem 404 em host de tenant.

**FND-03b [x] Tela do super admin: tenants (M)** — quebra da FND-03 — [story](stories/FND-03b-tela-super-admin-tenants.md)
- Requisitos: R-PLT-4, R-PLT-3.
- CA: listar, provisionar com progresso por etapa, retomar, suspender (com motivo) e reativar tenants; ações auditadas; 404 em host de tenant.

**FND-06 [x] Catálogo de módulos e liberação por tenant (M)** — [story](stories/FND-06-catalogo-modulos-liberacao.md)
- Requisitos: R-SEC-11, R-SEC-10, R-SEC-12.
- CA: endpoint de módulo não liberado → 403 `module_not_released`; seed idempotente com `module_actions` (ADR 0003); liberar ou bloquear um módulo reflete no tenant no próximo request, sem deploy.

**FND-06b [x] Grupos de usuários e permissões (G)** — [story](stories/FND-06b-grupos-permissoes.md)
- Requisitos: R-SEC-9, R-SEC-10, R-SEC-12, R-SEC-5.
- CA: habilitar módulo marca "ver"; desmarcar "ver" limpa o módulo; marcar outra ação marca "ver"; módulos não liberados aparecem desabilitados com "Não liberado"; Proprietário não editável e nunca sem membro; ninguém concede ação ou grupo que não possui, e só o Proprietário concede `*.approve`, `*.pay`, `hr.view_sensitive` e o grupo Proprietário (ADR 0008); revogar vale no próximo request; serviço de permissão efetiva e policy `perm:{modulo}.{acao}`; alterações auditadas.

**FND-06c [x] Usuários, convite e permissão efetiva (M)** — [story](stories/FND-06c-usuarios-convite-permissao-efetiva.md)
- Requisitos: R-SEC-4, R-SEC-12, R-SEC-2.
- CA: usuário em dois grupos recebe a união; remover do grupo tira o acesso no próximo request; rota sem permissão → 403 `forbidden`; teste de integração cobrindo as três camadas; convite respeita a delegação; 2FA obrigatório para o Proprietário e para quem aprova ou paga; tela Configurações → Segurança (exigir 2FA para todos, `settings.edit`).

**FND-06d [x] Menu por permissão e tela de módulo não liberado (P)** — [story](stories/FND-06d-menu-permissao-modulo-nao-liberado.md)
- Requisitos: R-SEC-11, R-SEC-12.
- CA: acessar a URL de um módulo bloqueado mostra a tela padrão "Funcionalidade não liberada nessa versão do sistema.", nunca uma tela quebrada ou em branco; módulo sem permissão some do dock e da paleta.

**FND-09a [x] Tokens, tema e componentes visuais base (M)** — validada pelo PO em 18/10/2026 — quebra da FND-09 — [story](stories/FND-09a-tokens-tema.md)
- Requisitos: PRD §6 (Usabilidade).
- CA: tokens do protótipo mapeados para o shadcn, com as correções de contraste da revisão (§2.4); `--signal` separado de `--warn`; tema claro/escuro/automático salvo no perfil, sem flash; Archivo self-hosted; chips, indicador de etapa e `Money`; lint proíbe cor literal.

**FND-09b [x] Shell do backoffice e navegação (M)** — validada pelo PO em 18/10/2026 — quebra da FND-09 — [story](stories/FND-09b-shell-navegacao.md)
- Requisitos: PRD §6 (Usabilidade), R-COM-2.
- CA: dock com as 7 áreas, cada uma abrindo popover (desktop) ou sheet (celular) com os módulos (ADR 0005 D8); sub-telas em abas na topbar; Ctrl K abre a paleta; topbar a 400px com busca em ícone e tema no menu do usuário; `<nav>`, skip link, foco e título na troca de rota.

**FND-09c [x] DataTable, estados e formulário base (M)** — quebra da FND-09 — [story](stories/FND-09c-datatable-estados-formulario.md)
- Requisitos: PRD §6 (Desempenho), R-SEC-5, R-PRP-2.
- CA: tabela padrão com filtro, ordenação e paginação server-side; Skeleton, EmptyState, ErrorState e InlineError ligados ao TanStack Query; formulário RHF + Zod com mensagens PT-BR e erros do ProblemDetails; cliente Orval sem `fetch` à mão; histórico por registro.

**FND-09d [x] Componentes de formulário e moldes de tela (G)** — moldes aprovados pelo PO em 18/10/2026 — [story](stories/FND-09d-componentes-formulario.md)
- Requisitos: PRD §6 (Usabilidade), R-PRP-1, R-PRP-2, R-PRP-3, R-AJD-4.
- CA: pré-requisito "PO valida FND-09a/09b e aprova os moldes"; três moldes (cadastro em seções, detalhe com abas na URL, painel lateral); máscaras (CNPJ/CPF inclusive alfanumérico, CEP, telefone, NCM, CBO), números no padrão brasileiro, Combobox com criação rápida, chips acessíveis, ToggleGroup, Sheet, TreeTable `treegrid`, editor de linhas, data/hora, aviso `warning`, "obrigatório" com `aria-required`, faixa "Próximos passos", `ConfirmDialog` com `recovery`, `Tag` e tom neutro, filtros na URL, `Dica` de 24 px; CAs de teclado e 400 px.

**FND-09e [x] Componentes e convenções compartilhados da Fase 2 (M)** — nova (revisão pré-Fase 2 §3), antes da FIN-01 — [story](stories/FND-09e-componentes-convencoes-fase2.md)
- Requisitos: PRD §6 (Usabilidade, Acessibilidade), R-PRP-2, R-PRP-7, R-COM-2, R-SEC-5.
- CA: mensagens numa região `role="status"` no topo do conteúdo, erro dentro do diálogo e próximo passo só no `NextSteps` (ADR 0021, resolve a P-18); `ConfirmDialog` grande com formulário, pré-condições e erro por campo; `applyServerErrors` com caminhos aninhados; `RowsEditor` com modelo de colunas; `DataTable` com seleção, totais e linha expansível; tokens de gráfico e `Chart`; menu do Financeiro em dois grupos (D12); `xmin` com 409, `Idempotency-Key`, `dedup_key` nas notificações, regra Contracts × Contracts e teste de posse de tabela (ADR 0019). Sem o filtro de 2FA por ação (P-19).

**FND-10 [~] Deploy em homologação (G)** — FND-10a [x] (repositório) · FND-10b [ ] (VPS, aguarda o PO; ADR 0015) — [story](stories/FND-10-deploy-homologacao.md)
- Requisitos: PRD §6 (Backup, Disponibilidade, Segurança).
- CA: push na main publica na homologação (host sugerido `homolog.konebras.com.br`) atrás do `stack-nginx`; `app.konebras.com.br` continua com o Dolibarr até a GOL-01 (ADR 0005 D1); Postgres em rede interna, sem `ports:`; containers não-root, `read_only` e `cap_drop`; WebSocket com `proxy_read_timeout` ≥ 120 s; logs do nginx mascaram tokens; backup diário cifrado fora da VPS (RPO 24 h, D6), credencial sem delete; restauração de um tenant testada e documentada.

**FND-14 [x] Ajuda contextual e Dica (M)** — [story](stories/FND-14-ajuda-contextual-dica.md)
- Requisitos: R-AJD-1, R-AJD-2, R-AJD-3, R-AJD-4, R-AJD-6, R-AJD-7.
- CA: toda rota do front tem ajuda (o teste falha se faltar); a ajuda de um módulo sem permissão não aparece no índice; a primeira visita abre a ajuda uma vez por usuário e por tela, em qualquer dispositivo.

**FND-15 [x] Pergunte ao BemAqui (G)** — [story](stories/FND-15-pergunte-ao-bemaqui.md)
- Requisitos: R-AJD-5, R-AJD-8, R-AJD-6.
- CA: a resposta sempre cita a seção usada; sem provedor configurado, sem IA ou com citação inválida, devolve os trechos encontrados; trocar de provedor na tela vale na próxima pergunta, sem deploy; a chave nunca volta na API nem aparece em log; teste prova que nenhum dado de negócio do tenant entra no prompt.

---
## Fase 1 — Cadastros e migração

Stories detalhadas em [`docs/stories/`](stories/README.md#fase-1--cadastros-e-migração), com critérios de aceite completos, ordem de execução e a validação do PO. Decisões do PO de 18/10/2026 aplicadas (revisão pré-Fase 1, ADRs 0016, 0017 e 0018): as 13 stories da fase estão `Ready`. Aqui fica o resumo de cada uma.

**Escopo da migração (decisão do PO, 18/10/2026):** migra todos os cadastros do Dolibarr, inclusive condições de pagamento e contas bancárias e caixas sem saldo. Não migra lançamentos nem financeiro transacional (títulos, pagamentos, recebimentos, movimentos e saldos bancários, saldos de estoque), que começam no BemAqui (ADR 0017).

**Ordem de execução (D1):** FND-09d (Fase 0) → **Marco A, terceiros reais:** CAD-01a → MIG-01a → CAD-05b → MIG-01b → CAD-01b → **Marco B, catálogo real:** CAD-06 → CAD-03 → CAD-02a → CAD-02b → MIG-01c → **Marco C, base de custo:** CAD-05a → CAD-04a → CAD-04b. A CAD-04c vai para o início da Fase 3.

**Quebras:** CAD-01 → 01a/01b · CAD-02 → 02a/02b · CAD-04 → 04a/04b/04c (D6) · CAD-05 → 05a/05b · MIG-01 → 01a/01b/01c. **Canceladas pelo PO:** MIG-01d e MIG-01e. Re-estimadas: CAD-03 e CAD-06 (P → M), CAD-04b (G → M).

**CAD-01a [x] Terceiros (G)** — Marco A, ordem 1 — [story](stories/CAD-01a-terceiros.md)
- Requisitos: R-TER-1, R-TER-2, R-PRP-1, R-PRP-3, R-SEC-5, PRD §6 (Desempenho, LGPD).
- CA: `BrazilianDocuments` (CNPJ alfanumérico e CPF); duplicidade conferida antes de tudo e também em gravação simultânea, oferecendo adicionar o papel; documento opcional com "Sem documento"; situação Ativo/Inativo; endereço de cobrança na criação; CPF mascarado na lista e busca exata; Ctrl K; permissões padrão por seed patch; auditoria e log sem dado pessoal; lista com 50 mil registros em menos de 1 s.

**MIG-01a [x] Mapeamento e infraestrutura de importação do Dolibarr (G)** — Marco A, ordem 2 — [story](stories/MIG-01a-infra-importacao-dolibarr.md)
- Requisitos: R-TER-5, PRD §6 (Segurança, LGPD), R-SEC-5.
- CA: mapeamento só das tabelas de cadastro aceito pelo PO; snapshot cifrado com GRANT por coluna, sem conexão ao banco do Dolibarr; módulo `Migrations` com `I*Import`; idempotência por `xmin`; `--dry-run` desfeito no fim; proteção por `MAIN_INFO_SIREN`; relatório pelo total lido, CSV sem injeção de fórmula; tela só de leitura "Configurações → Importação do Dolibarr"; expurgo da staging em até 30 dias.
- Mapeamento aceito pelo PO em 18/10/2026 (CA 1). Origem padrão: API REST do Dolibarr com a chave do Konebras UI (ADR 0017, adendo 2), sem usuário no MariaDB de produção; o snapshot fica como alternativa. Painel da plataforma sem allowlist por decisão do PO (adendo da ADR 0018).

**CAD-05b [x] Contas bancárias, caixas e condições de pagamento (M)** — Marco A, ordem 3 — [story](stories/CAD-05b-contas-bancarias-condicoes-pagamento.md)
- Requisitos: R-FIN-2, R-TER-2, R-PRP-1, R-SEC-5.
- CA: saldo inicial opcional, travado depois de movimentação; conta padrão única; troca de dados bancários só com Proprietário ou `finance.approve`, notificada; condições do Finance com parcelas somando 100% e `IPaymentTerms.Split`; seed À vista, 30, 30/60 e 30/60/90 dias; condição padrão do terceiro.

**MIG-01b [x] Carga de terceiros, contatos, condições de pagamento e contas bancárias (M)** — Marco A, ordem 4 — [story](stories/MIG-01b-carga-terceiros-cadastros-financeiros.md)
- Requisitos: R-TER-5, R-TER-1, R-TER-2, R-FIN-2.
- CA: só depois da allowlist da plataforma fechada (ADR 0018); mesmo documento vira um terceiro com vários papéis; prospects como Cliente, encerrados inativos, instaladores pela lista do PO; condições por de-para com o seed; contas sem saldo e sem `opening_date`; nada de `birthday`, IBAN, BIC ou titular; relatório pelo total lido.

**CAD-01b [x] Consulta de CNPJ e de CEP (M)** — Marco A, ordem 5 — [story](stories/CAD-01b-consulta-cnpj.md)
- Requisitos: R-TER-1, R-PRP-3, R-PRP-7.
- CA: BrasilAPI para CNPJ e CEP, sem reserva automática; só grava com confirmação; cache de 30 dias por tenant, sem QSA; falha não trava o cadastro e conta para o alerta; regex, sem redirect, resposta até 64 KB, limite global e circuit breaker.

**CAD-06 [x] Configurações da empresa (M)** — Marco B, ordem 6 — re-estimada — [story](stories/CAD-06-configuracoes-empresa.md)
- Requisitos: R-COM-4, R-COM-3, R-RH-2, R-COML-1, R-SEC-5, R-AJD-1.
- CA: perfil da empresa como fonte da verdade, CNPJ só leitura; logo até 1 MB pela rota pública própria; numeração com "próximo número" que só sobe (inclusive em ano sem linha) e reinício anual bloqueado depois do primeiro número; % de encargos com 80,00%, histórico e `PayrollChargesChanged`; markup como modo padrão.

**CAD-03 [x] Locais de estoque, centros de trabalho e calendário (M)** — Marco B, ordem 7 — re-estimada — [story](stories/CAD-03-locais-centros-trabalho-calendario.md)
- Requisitos: R-EST-2, R-PLN-1, R-SEC-5, R-PRP-1.
- CA: `delete` em `inventory` e `production`; local padrão "ALM"; turnos definem as horas por dia; feriados nacionais sugeridos e confirmados; capacidade diária por centro (`IWorkCenterCapacity` por id) com a fixture reaproveitada pela PLN-01.

**CAD-02a [x] Itens, unidades, categorias e garantia padrão (M)** — Marco B, ordem 8 — [story](stories/CAD-02a-itens-unidades-categorias.md)
- Requisitos: R-EST-1, R-EST-8, R-PLT-3, R-PRJ-7, R-PRP-1, R-PRP-3, R-SEC-5.
- CA: seed de unidades; NCM obrigatório para item ativo de matéria-prima, componente e produto acabado; lote e série para qualquer tipo exceto Serviço; garantia padrão no Inventory (12/3/3 meses); item com movimento não troca a unidade; Ctrl K; lista com 50 mil itens em menos de 1 s.

**CAD-02b [x] Conversões de unidade e códigos do fornecedor (M)** — Marco B, ordem 9 — [story](stories/CAD-02b-conversoes-codigos-fornecedor.md)
- Requisitos: R-EST-3, R-FIS-5, R-PRP-3, R-SEC-5.
- CA: conversão kg → m² por item pelo `IUnitConversion`; sem conversão, erro claro (nunca fator 1); código do fornecedor único por fornecedor e só para terceiro com papel Fornecedor.

**MIG-01c [x] Carga de itens, categorias, unidades, códigos do fornecedor e locais (M)** — Marco B, ordem 10 — [story](stories/MIG-01c-carga-itens-locais.md)
- Requisitos: R-TER-5, R-EST-1, R-EST-2, R-EST-3, R-PRJ-7.
- CA: de-para de unidades e tipos; produto sem NCM entra inativo; `elev_garantia` vira a garantia do item (P-11); depósito único ligado ao ALM; sem saldo, custo médio nem preço; relatório pelo total lido.

**CAD-05a [x] Plano de contas gerencial e centros de custo (M)** — Marco C, ordem 11 — [story](stories/CAD-05a-plano-contas-centros-custo.md)
- Requisitos: R-FIN-1, R-PLT-3, R-FIN-13, R-SEC-5, R-PRP-1.
- CA: seed v1 editável (máscara `9.9.99`, grupos da DRE, contas "A classificar"), sem sobrescrever; só conta analítica recebe lançamento; excluir sem uso, inativar com uso; centros Administrativo, Comercial, Produção e Instalação.

**CAD-04a [x] Cargos, jornadas, sindicatos e custo-hora (M)** — Marco C, ordem 12 — [story](stories/CAD-04a-cargos-jornadas-custo-hora.md)
- Requisitos: R-RH-2, R-RH-1, R-PRD-4, R-SEC-5, R-SEC-10, PRD §6 (LGPD).
- CA: custo-hora = salário de referência × (1 + % encargos) ÷ horas mensais (semanais × 5), ou ajustado à mão; recálculo idempotente quando o % muda; faixa salarial só em `/sensitive` com `hr.view_sensitive`; custo-hora só por cargo.

**CAD-04b [x] Funcionários: cadastro base (M)** — Marco C, ordem 13 — quebra D6 — [story](stories/CAD-04b-funcionarios-base.md)
- Requisitos: R-RH-1, R-RH-3, R-SEC-5, R-SEC-10, PRD §6 (LGPD).
- CA: criação em página única; dados pessoais só em `/sensitive`, com `SensitiveRead`; comandos separados; reajuste de salário, troca de cargo e desligamento com diálogo e linha do tempo; vínculo com o usuário exige `hr.edit` e `identity.edit`.

~~**MIG-01d Saldos iniciais de estoque**~~ e ~~**MIG-01e Títulos em aberto e histórico de pagamentos**~~ — **canceladas pelo PO em 18/10/2026.** A migração não traz saldos nem títulos; o saldo inicial de estoque é digitado na EST-01 (`OpeningBalance`) e o das contas na CAD-05b.

---

## Fase 2 — Comercial, Projetos e Financeiro núcleo

Stories detalhadas em [`docs/stories/`](stories/README.md#fase-2--comercial-projetos-e-financeiro-núcleo), com critérios de aceite completos, ordem de execução, padrões comuns e a validação do PO. As stories foram escritas e validadas em 19/10/2026 e revistas em 20/10/2026 com as decisões do PO sobre a revisão pré-Fase 2 (`docs/revisoes/2026-10-20-revisao-pre-fase2.md`, ADRs 0019 a 0022, adendos às ADRs 0008 e 0016). **Todas estão `Ready`**; FIN-00, COM-03 e COM-06 têm pré-condição externa explícita. Adiadas pelo PO para depois do término do planejamento atual: 2FA a cada ação (P-19) e estorno de pagamento com `finance.approve` (P-20).

**Ordem de execução (seção 4 da revisão):** **Pré:** ADRs 0019, 0020, 0021 e 0022, adendos às ADRs 0008 e 0016 e a FND-09e (Fase 0) → **2.1a "Ver o caixa":** FIN-01 → FIN-02 → FIN-00 → FIN-05 → FIN-06a → **2.1b "Pagar com OP":** FIN-03a → FIN-04 → **2.2 Orçamento:** COM-01a → COM-01b → COM-02 → COM-03 → **2.3 Contrato ao caixa:** COM-04a → COM-04b → COM-04c → COM-05 → COM-06 → **2.4 Controles e D:** FIN-06b → COM-07 (D) → FIN-03b → COM-08 (D). Os dados financeiros reais entram na homologação a partir da FIN-00, e o banco do tenant da homologação é promovido a produção na GOL-01 (ADR 0020).

**Quebras e novas:** COM-01 → 01a/01b · COM-04 → 04a/04b/04c · FIN-03 → 03a/03b · FIN-06 → 06a/06b. **Novas:** FND-09e (Fase 0), FIN-00 (D2), COM-04c (D3). **Re-estimadas:** FIN-01 (M → G), COM-06 (M → G), FIN-04 (P → M, recebeu o lançamento sem OP).

**FIN-01 [x] Contas a receber (G)** — 2.1a, ordem 1 — re-estimada — [story](stories/FIN-01-contas-a-receber.md)
- Requisitos: R-FIN-3, R-FIN-1, R-FIN-2, R-COM-4, R-COM-2, R-PRP-1, R-PRP-7, R-SEC-5.
- CA: título manual e parcelado por `IPaymentTerms.SplitAsync`, com idempotência; baixa total ou parcial com juros, multa e desconto cria transação na conta, na mesma transação; "Quitar o saldo com desconto" sem transação; `paid_amount = Σ baixas` (teste de propriedade); "Vencido" calculado; limites de desconto, cancelamento e data retroativa (D8) e relatório de descontos e cancelamentos; vencimento da parcela do contrato editável com motivo (D14); título nunca apagado; baixa concorrente recusada com 409.

**FIN-02 [x] Contas a pagar e recorrentes (M)** — 2.1a, ordem 2 — [story](stories/FIN-02-contas-a-pagar-recorrentes.md)
- Requisitos: R-FIN-4, R-FIN-6, R-FIN-5, R-FIN-1, R-COM-4, R-PRP-1, R-SEC-5.
- CA: título manual e parcelado com retenções digitadas (P-15); forma de pagamento (Pix, boleto, TED) com aviso de titular diferente (D9); título travado enquanto está em OP; sem baixa fora da OP (teste de arquitetura); `IPayables` idempotente por origem, com data estimada; recorrente gera título com antecedência, sem duplicar, inclusive em paralelo e com dois tenants.

**FIN-00 [x] Implantação financeira (M)** — 2.1a, ordem 3 — nova (D2) — [story](stories/FIN-00-implantacao-financeira.md)
- Requisitos: R-FIN-2, R-FIN-3, R-FIN-4, R-FIN-1, R-PRP-1, R-PRP-3, R-SEC-5.
- CA: data de corte única; saldo das contas no fim do dia de corte; títulos a receber e a pagar em aberto lançados pelo saldo com origem `Opening` (fora do DRE por competência e da pontualidade); relatório de conferência "saldo do sistema = saldo do banco"; pré-condição da primeira carga real: backup verificado e teste de restauração (ADR 0020); testes nunca no tenant `konebras`.

**FIN-05 [x] Fluxo de caixa base (M)** — 2.1a, ordem 4 — [story](stories/FIN-05-fluxo-de-caixa-base.md)
- Requisitos: R-FIN-10, R-FIN-2, R-PRP-5, PRD §6 (Desempenho).
- CA: valores conferem com a soma dos títulos, das recorrentes e das transações (dataset fixo); saldo atual pela consulta desta story; realizado até hoje e projetado depois; CP estimado do instalador como estimativa; diário, semanal e mensal batendo entre si; dia de menor saldo e alerta de caixa negativo acionável; menos de 2 s com 50 mil títulos.

**FIN-06a [x] Inadimplência por cliente e aviso diário (P)** — 2.1a, ordem 5 — quebra da FIN-06 — [story](stories/FIN-06a-aging-aviso-diario.md)
- Requisitos: R-FIN-12 (parte), R-COM-2, R-PRP-5.
- CA: vencidos por cliente nas faixas 0–30, 31–60, 61–90 e 90+ (dataset fixo, com títulos de implantação); aviso diário consolidado sem repetir (`dedup_key`), com teste de dois tenants.

**FIN-03a [x] Ordem de pagamento: criar, aprovar e pagar (G)** — 2.1b, ordem 6 — quebra da FIN-03 — [story](stories/FIN-03a-ordem-de-pagamento.md)
- Requisitos: R-FIN-5, R-COM-2, R-COM-3, R-COM-4, R-PRP-2, R-PRP-5, R-PRP-7, R-SEC-2, R-SEC-5.
- CA: `finance.pay` para o Financeiro por seed patch (D5); OP com 1+ títulos e retrato do envio (fornecedor, líquido, forma de pagamento); pagamento limitado ao aprovado + tolerância (409 `exceeds_approved`); avisos de titular, fornecedor novo e dados alterados; regra do solicitante pela D6; pagamento sem OP aprovada bloqueado em todos os caminhos; aprovação e pagamento concorrentes gravam uma vez; 2FA no login de quem aprova ou paga; estorno com `finance.pay` (estado `Reversed`); comprovante só em OP paga; auditoria completa.

**FIN-04 [x] Movimentação bancária, transferências e lançamento sem OP (M)** — 2.1b, ordem 7 — re-estimada — [story](stories/FIN-04-movimentacao-bancaria.md)
- Requisitos: R-FIN-2, R-FIN-1, R-FIN-10, R-COM-2, R-PRP-7, R-SEC-5.
- CA: extrato com saldo inicial, saldo anterior e saldo acumulado conferidos com dataset fixo; transferência com as duas pernas na mesma transação, idempotente, aviso acima do limite e aprovação para conta nova; lançamento bancário sem OP (tarifa, IOF, juros, rendimento) com conta do plano, motivo, aviso ao Proprietário e a lista "Movimentos sem OP" (D4); sem edição de transação de título; 5 mil transações em menos de 1 s.

**COM-01a [x] Orçamento: itens, composição de custo, preço e margem (G)** — 2.2, ordem 8 — quebra da COM-01 — [story](stories/COM-01a-orcamento-itens-custo-preco.md)
- Requisitos: R-COML-1, R-COM-4, R-PRP-1, R-PRP-3, R-SEC-5, R-SEC-10.
- CA: `quotes.view_cost` e `projects.view_cost` com seed patch para Comercial e Financeiro e DTO sem custo para os demais (D7); preço = custo × (1 + markup) ou custo ÷ (1 − margem) conforme o modo do tenant, com % padrão; editor de custo por natureza; totais e margem ao vivo e gravados com as mesmas fórmulas, arredondando só na linha; mão de obra pelo custo-hora do cargo; imposto digitado enquanto a P-01 está aberta; índices de número com revisão na primeira migration; orçamento enviado não é alterado.

**COM-01b [x] Orçamento: marcos, validade, perda e cópia (M)** — 2.2, ordem 9 — quebra da COM-01 — [story](stories/COM-01b-orcamento-marcos-validade-copia.md)
- Requisitos: R-COML-6, R-COML-4, R-COML-2, R-COML-9, R-PRP-1, R-PRP-3.
- CA: marcos por data ou evento com prazo após o evento, valores pelo `PaymentTermSplitter` e "Faltam X%"; soma = 100% exigida para aprovar; % revalidado no servidor; "Preencher pela condição de pagamento"; validade padrão; perda com motivo da lista; copiar orçamento, itens ou projeto anterior.

**COM-02 [x] Revisões, validade e prorrogação (M)** — 2.2, ordem 10 — [story](stories/COM-02-revisoes-validade.md)
- Requisitos: R-COML-3, R-COML-4, R-COM-2, R-PRP-5.
- CA: nova revisão congela a anterior (`Superseded`) com o mesmo número; só a revisão mais recente é editável/aprovável; "Prorrogar validade" sem nova revisão (D16); histórico comparável lado a lado; job diário expira e avisa antes, sem repetir.

**COM-03 [x] PDF e envio do orçamento (M)** — 2.2, ordem 11 — pré-condição: SPF/DKIM/DMARC — [story](stories/COM-03-pdf-envio-orcamento.md)
- Requisitos: R-COML-5, R-COM-1, R-COM-3, R-PRP-2, R-SEC-5.
- CA: PDF pelo `IPdfRenderer` (QuestPDF, Archivo embutida, teste na imagem Alpine; ADR 0022) e sem custo nem margem (teste de extração de texto); PDF anexado como arquivo do sistema e ao log de e-mail; envio pela fila com anexo lido por id depois do `Clean`, cópia, Reply-To e limite diário; mensagem do usuário nunca vira template.

**COM-04a [x] Aprovação → contrato + projeto (G)** — 2.3, ordem 12 — quebra da COM-04 — [story](stories/COM-04a-aprovacao-contrato-projeto.md)
- Requisitos: R-COML-7, R-PRJ-1, R-PRJ-2, R-FIN-1, R-COM-3, R-COM-4, R-PRP-2, R-PRP-3, R-PRP-7, R-SEC-5.
- CA: aprovar gera, numa transação, contrato `Active` com data de assinatura editável (D3), snapshot sem dado pessoal, marcos, projeto com itens e orçamento do projeto por natureza, com `QuoteApproved` e `ContractSigned` (com a conta de receita validada) no outbox; aprovação dupla gera um contrato só; projeto e natureza do custo no título a pagar; próximo passo "Ver projeto criado".

**COM-04b [x] Recebíveis do contrato e liberação por marco (M)** — 2.3, ordem 13 — quebra da COM-04 — [story](stories/COM-04b-recebiveis-marcos-contrato.md)
- Requisitos: R-COML-7, R-PRJ-6, R-FIN-3, R-COM-2, R-PRP-5, R-PRP-7.
- CA: marcos por data = `Open`, por evento = `Planned`, criados pelo Finance ao receber `ContractSigned`, idempotente; registro de gatilhos para eventos fora de ordem; evento do marco libera a parcela com vencimento = data do evento + prazo; "fabricação concluída" e "entrega" (venda sem instalação, D15) registradas à mão; cancelar contrato cancela os recebíveis sem baixa; contrato concluído quando tudo é quitado e reaberto por estorno.

**COM-04c [x] Contrato de implantação (P)** — 2.3, ordem 14 — nova (D3) — [story](stories/COM-04c-contrato-de-implantacao.md)
- Requisitos: R-COML-7, R-PRJ-1, R-PRJ-6, R-FIN-3, R-PRP-3, R-PRP-7.
- CA: contrato já em andamento aprovado com data de assinatura e marcos no passado; marco "Recebido antes da implantação" não gera título e só vale até a data de corte; eventos já ocorridos registrados com a data real.

**COM-05 [x] Projeto 360 (M)** — 2.3, ordem 15 — [story](stories/COM-05-projeto-360.md)
- Requisitos: R-PRJ-1, R-PRJ-4, R-PRJ-2, R-PRJ-5 (parte), R-COM-3, R-PRP-2, R-PRP-5.
- CA: abas Resumo, Itens e OS, Financeiro, Custos, Documentos, Linha do tempo e Histórico na URL; Financeiro só com `finance.view`; Custos e margem só com `projects.view_cost`; orçado, realizado, projetado e desvio por natureza, com margem projetada pela fórmula do `04-fluxos` §3.3.

**COM-06 [x] OS de Instalação (G)** — 2.3, ordem 16 — re-estimada — [story](stories/COM-06-os-instalacao.md)
- Requisitos: R-PRJ-3, R-FIN-4, R-PRJ-6, R-COM-4, R-COM-3, R-PRP-2, R-PRP-7.
- CA: criar a partir do projeto com instalador e valor, que gera o CP previsto do instalador com data estimada, na mesma transação; etapas e checklist de entrega técnica (alvos de 56 px); aceite técnico confirma o CP e dispara o evento que entrega o projeto e libera o recebível vinculado; mudança de valor avisa o financeiro.

**FIN-06b [x] Inadimplência por projeto, pontualidade e histórico do terceiro (M)** — 2.4, ordem 17 — quebra da FIN-06 — [story](stories/FIN-06b-inadimplencia-projeto-pontualidade.md)
- Requisitos: R-FIN-12 (parte), R-TER-4, R-TER-3, R-PRP-5.
- CA: vencidos por projeto; atraso médio do cliente (sem títulos de implantação) para a FIN-07; aba "Comercial e financeiro" no terceiro, com cada bloco pela permissão do módulo dono.

**COM-07 [x] Aditivo de contrato (M)** — D — 2.4, ordem 18 — [story](stories/COM-07-aditivo-contrato.md)
- Requisitos: R-COML-8, R-PRJ-5, R-PRP-7, R-SEC-5.
- CA: aditivo em página própria com prévia "antes → depois", aprovado com `projects.approve`, ajusta o orçado por natureza, a data prometida e os recebíveis futuros (cria os do acréscimo, reduz os sem baixa), com revalidação no servidor e sem mudar o contrato original; autor não aprova o próprio aditivo (D6).

**FIN-03b [x] Alçadas multinível e aprovação pelo e-mail (M)** — 2.4, ordem 19 — quebra da FIN-03 — [story](stories/FIN-03b-alcadas-multinivel.md)
- Requisitos: R-FIN-5, R-COM-1, R-COM-2, R-PRP-5, R-SEC-5.
- CA: regras por faixa de valor e por conta (com teto), com aprovador por grupo ou usuário; a mais específica vence; valor de referência contra fracionamento; alçada viável; todas as aprovações do nível N antes do N+1; fora da alçada recusa mesmo com `finance.approve`; e-mail só com número, total e link, que exige login e não aprova sozinho.

**COM-08 [x] Funil comercial (P)** — D — 2.4, ordem 20 — [story](stories/COM-08-funil-comercial.md)
- Requisitos: R-COML-9, PRD §5.14 (Comercial).
- CA: orçamentos por situação no período (só a revisão corrente), conversão = aprovados ÷ decididos, motivos de perda, filtro por autor, drill-down e "Vencendo" com "Prorrogar validade".

_MIG-01e (títulos em aberto e histórico de pagamentos) não entra aqui: cancelada pelo PO em 18/10/2026. Os títulos em aberto entram pela FIN-00 (implantação financeira) e a pontualidade dos clientes (R-TER-4) começa com os títulos do BemAqui, sem os de implantação. O histórico comercial e financeiro do terceiro (R-TER-3) está na FIN-06b._

---

## Fase 3 — Engenharia, Estoque, Compras e Produção

Stories detalhadas em [`docs/stories/`](stories/README.md#fase-3--engenharia-estoque-compras-e-produção), com critérios de aceite completos, ordem de execução, padrões comuns, validação do PO e rastreabilidade da revisão. Escritas pelo @sm e validadas pelo @po em 21/10/2026; revistas no mesmo dia depois das decisões do PO sobre a revisão pré-Fase 3 (D1 a D17, os 95 pontos da tabela 1.1, §2.1 a §2.3, §3 e §4; ADRs 0023 a 0026): **todas `Ready`**, 10/10. Aqui fica o resumo de cada uma.

**Ordem de execução (D1):** **3.1 "Fábrica visível":** ENG-01 → PRD-01 → PRD-02 → CAD-04c1 → PRD-03a → *(ADR 0025)* PRD-04 → PRD-05 → PRD-06a1 → *(ADR 0026)* PRD-03b → **3.2 "Estoque e BOM":** *(ADR 0023)* EST-01a → ENG-02 → EST-02 → EST-03 → **3.3 "Comprar o que falta":** *(ADR 0024)* CMP-01 → CMP-03a → CMP-04 → CMP-02 → CMP-03b → **3.4 "Fechar e rastrear":** PRD-06a2 → PRD-06b → EST-04 → EST-01b → EST-05 → EST-06 (D). **O PO valida no fim do 3.1:** OS no Kanban, horas no Projeto 360 e a parcela "fabricação concluída" liberada sozinha.

**Quebras:** EST-01 → 01a/01b · CMP-03 → 03a/03b · PRD-03 → 03a/03b · PRD-06 → 06a1/06a2/06b · CAD-04c → 04c1/04c2 (D9). **Re-estimadas:** PRD-01 (M → G), CMP-04 (M → G), PRD-04 (P → M). **Adiado:** o funcionamento sem rede da PRD-03b, depois do piloto (P-23). **CAD-04c2 movida para a Fase 6 (D9)**, antes da RH-02.

**ENG-01 [ ] Templates de roteiro (M)** — 3.1, ordem 1 — [story](stories/ENG-01-templates-roteiro.md)
- Requisitos: R-ENG-1, R-ENG-2, R-PRP-1, R-PLN-1, R-SEC-5.
- CA: roteiros com etapas (centro, horas padrão, qualidade, exige aprovação, checklist) e campo "Letra" (D10); primeira etapa marcada "Começa sem BOM" (D12); produto único por roteiro (`routing_template_items`); seed do exemplo com a Expedição "Preparar e embalar para o romaneio" (D4); `engineering.delete` e seed patch `engineering-permissions` (D13); `StageIndicator` corrigido (D17 b).

**PRD-01 [ ] Geração da OS de fabricação (G)** — 3.1, ordem 2 — re-estimada de M — [story](stories/PRD-01-geracao-os-fabricacao.md)
- Requisitos: R-PRD-1, R-PRJ-2, R-ENG-1, R-ENG-6, R-PRP-1, R-PRP-2, R-COM-3, R-COM-4, R-SEC-5.
- CA: `Equipment` gera OS sozinho, `Product` pelo botão, `Service` e contrato de implantação nunca (D14); "Gerar OS" em lote; mesma `ProductionOrder` da COM-06 com `CHECK` por tipo e deploy em duas etapas; cancelar OS iniciada com `production.approve`; dwg/dxf de 25 MB e leitura pelo operador por endpoint que confere a fila (D16); `OrderHeader`/`FactsBar` e ordem das abas.

**PRD-02 [ ] Kanban da produção (M)** — 3.1, ordem 3 — [story](stories/PRD-02-kanban.md)
- Requisitos: R-PRD-2, R-COM-2, PRD §5.14, PRD §6, R-PRP-5, R-SEC-5.
- CA: colunas pela letra da etapa, sem arraste; "Concluir etapa" no menu do cartão com `production.approve` (D10); "Em risco" numa função só; coluna "Pronta para concluir" (D12); job diário "OS atrasada"; `ProductionHub` com `RequireModule` e permissão no `MapHub`, conexão fechada quando muda a permissão ou o security stamp, token fora dos logs; push depois do commit.

**CAD-04c1 [ ] Funcionários: afastamentos e situação (P)** — 3.1, ordem 4 — quebra da CAD-04c (D9) — [story](stories/CAD-04c1-afastamentos-situacao.md)
- Requisitos: R-RH-3, R-SEC-5, R-SEC-10, PRD §6 (LGPD).
- CA: afastamentos com seis tipos; situação "Afastado" calculada na leitura ("Desligado" prevalece); `IEmployees.Status` para a PRD-03a; job diário publica início e fim do afastamento com data futura, uma vez; afastamentos na exportação e anonimização (ADR 0018).

**PRD-03a [ ] App do operador: fila, apontamento e checklist (G)** — 3.1, ordem 5 — quebra da PRD-03 — [story](stories/PRD-03a-pwa-fila-apontamento.md)
- Requisitos: R-PRD-3, R-PRD-6, PRD §6 (Usabilidade), R-PRP-5, R-SEC-5.
- CA: fila com todas as etapas prontas, as do meu centro primeiro, e busca pelo número (D11); primeira etapa sem BOM (D12); pausa com motivos e aviso ao almoxarifado; "Finalizar etapa · {h} h apontadas"; supervisor aponta por funcionário sem sobreposição e com até 7 dias; quem apontou não corrige nem aprova, salvo sem outro aprovador elegível (`SoleOwnerRule`, marca e aviso ao Proprietário); kit "chão de fábrica"; DTO sem custo.

**PRD-04 [ ] Custo de mão de obra e custo real do projeto (M)** — 3.1, ordem 6 — re-estimada de P; ADR 0025 — [story](stories/PRD-04-custo-mao-de-obra.md)
- Requisitos: R-PRD-4, R-RH-2, R-PRJ-5, R-SEC-5, PRD §6 (LGPD).
- CA: snapshot do custo-hora no fechamento; nenhuma pausa entra no custo; cria `commercial.project_costs` como razão de diferenças; troca o cálculo da COM-05 (realizado, "Comprometido" e projetado, D2) com teste de regressão sobre cópia do `konebras` (D17 a); recompõe os apontamentos já fechados com "Calculado na implantação" (D3); aba Custos da OS.

**PRD-05 [ ] Qualidade e não conformidade (M)** — 3.1, ordem 7 — [story](stories/PRD-05-qualidade-nao-conformidade.md)
- Requisitos: R-PRD-6, R-COM-2, R-COM-3, R-COM-4, R-PRP-5, R-SEC-5, R-SEC-10.
- CA: ação `production.approve_quality`, usável por PIN, que só aprova ou reprova a qualidade (D6), com seed patch `production-quality` para a Supervisão; quem fez etapa da tentativa não aprova ("Autoinspeção" sem outro aprovador); NC com responsável padrão = quem reprovou; última etapa deixa a OS "Pronta para concluir", nunca `Completed`.

**PRD-06a1 [ ] Conclusão da OS e fabricação concluída do projeto (P)** — 3.1, ordem 8 — quebra da PRD-06a (D1) — [story](stories/PRD-06a1-conclusao-os.md)
- Requisitos: R-PRD-7, R-PRJ-6, R-COM-4, R-PRP-2, R-PRP-7, R-SEC-5.
- CA: estado `ReadyToComplete`; concluir com `production.approve` e login completo; projeto "Fabricado" e `ProjectFabricated` liberando a parcela, sem estoque; registro manual de fabricação bloqueado quando o projeto tem OS; sem conclusão parcial; estorno `Completed → InProgress` e `Fabricated → InProgress`, recusado com a parcela recebida.

**PRD-03b [ ] App do operador: aparelho registrado e PIN (M)** — 3.1, ordem 9 — ADR 0026 — [story](stories/PRD-03b-pwa-pin-aparelho.md)
- Requisitos: R-PRD-3, PRD §6 (Usabilidade, Segurança), R-SEC-1, R-SEC-3, R-SEC-7, R-SEC-5.
- CA: PIN de 6 dígitos só em aparelho registrado (segredo rotativo, 180 dias, revogado depois de 30 dias sem uso); sessão de 12 h com saída depois de 15 min sem toque; `amr=pin` e lista `[PinSessionAllowed]` com teste de arquitetura; perfil sensível apaga o PIN, exceto `production.approve_quality`; prazo absoluto de 30 dias também na sessão completa (D17 d). Sem rede: fora do escopo (P-23).

_MIG-01d (saldos iniciais de estoque) não entra aqui: cancelada pelo PO em 18/10/2026. O saldo inicial é digitado na EST-01a (`OpeningBalance`, ADR 0016)._

**EST-01a [ ] Saldos, movimentos e custo médio (G)** — 3.2, ordem 10 — quebra da EST-01; ADR 0023 — [story](stories/EST-01a-saldos-movimentos-custo-medio.md)
- Requisitos: R-EST-4, R-EST-5, R-EST-6, R-EST-7, R-EST-2, R-EST-3, R-EST-8, R-COM-4, R-PRP-7, R-SEC-5.
- CA: posição agregada com `CHECK`s e trava item → local → lote; `PostAsync` exige transação; movimento imutável (trigger) e estorno por movimento inverso; numeração `MOV`; janela de implantação com "Concluir implantação do estoque"; entrada e saída manual acima de R$ 1.000,00 ou com desvio acima de 20% aprovadas por outra pessoa (D7); `SoleOwnerRule`; `CostByOrderAsync`.

**ENG-02 [ ] BOM da OS (M)** — 3.2, ordem 11 — [story](stories/ENG-02-bom-da-os.md)
- Requisitos: R-ENG-3, R-ENG-4, R-ENG-5, R-EST-3, R-PRP-2, R-PRP-3, R-SEC-5.
- CA: BOM por unidade com a necessidade da OS; liberar com `engineering.approve` (seed patch `engineering-release-bom`, D13); revisão com `Superseded`; liga o bloqueio da D12 no início de etapa (PRD-03a) e na "Concluir etapa" (PRD-02) com 409 `bom_not_released`; filtro "OS que vão parar sem BOM"; `IBoms.ReleasedForOrderAsync`.

**EST-02 [ ] Reservas e disponível (M)** — 3.2, ordem 12 — [story](stories/EST-02-reservas-disponivel.md)
- Requisitos: R-EST-4, R-ENG-4, R-ENG-5, R-EST-5, R-CMP-1, R-PRP-5, R-SEC-5.
- CA: reserva na posição agregada, reconciliada a partir da fonte (BOM vigente e situação da OS; alvo 0 para OS cancelada ou concluída); `StockShortageChanged` só como aviso; material que chega completa as faltas; recomposição das BOMs já liberadas (D3); `ReleaseForOrderAsync` síncrono para a conclusão.

**EST-03 [ ] Requisição de material para a OS e devolução (M)** — 3.2, ordem 13 — [story](stories/EST-03-requisicao-material-os.md)
- Requisitos: R-PRD-5, R-EST-4, R-EST-6, R-EST-8, R-PRJ-5, R-PRJ-4, R-PRP-7, R-SEC-5.
- CA: baixa síncrona pelo motor com lote sugerido; numeração `REQ`; consumo acima da BOM acima de R$ 1.000,00 aprovado por outra pessoa; "Pedir material" no `/fab` (API e botão desta story), que nunca baixa; custo na razão `project_costs`; devolução pelo custo da baixa original; recomposição das saídas com projeto (D3).

**CMP-01 [ ] Solicitação de compra (M)** — 3.3, ordem 14 — [story](stories/CMP-01-solicitacao-compra.md)
- Requisitos: R-CMP-1, R-CMP-6, R-COM-2, R-COM-4, R-PRP-2, R-PRP-4, R-PRP-5, R-SEC-5.
- CA: manual ou pela falta da BOM, recomposta a partir de `OpenShortagesAsync` (ADR 0023 §6); job do mínimo descontando o que já está em compra (saldo a receber pelo `OpenQuantityByItemAsync` da CMP-03a); prazo de 15 dias; Serviço recusado; `purchasing.receive` e seed patch `purchasing-permissions` (Almoxarifado só `view` + `receive`, D8); job e consumidores conferem o módulo liberado.

**CMP-03a [ ] Pedido de compra: criação, aprovação por alçada e CP previsto (G)** — 3.3, ordem 15 — quebra da CMP-03; ADR 0024 — [story](stories/CMP-03a-pedido-compra-alcada-cp.md)
- Requisitos: R-CMP-3, R-CMP-4, R-CMP-6, R-FIN-4, R-FIN-5, R-FIN-10, R-COM-2, R-COM-4, R-PRP-1, R-PRP-7, R-SEC-5.
- CA: pedido manual, da SC ou pela API com `rfqId` (para a CMP-02); alçada pelo `IApprovalRouting` com valor de referência contra fracionamento e `SoleOwnerRule`; `finance.approve` sem `purchasing.approve` recebe 403; aprovação gera os títulos `Planned` por parcela na mesma transação; "Comprometido" pela D2; aviso "1 cotação"; `IPurchaseOrders.OpenQuantityByItemAsync` (saldo a receber por item).

**CMP-04 [ ] Recebimento manual contra o pedido (G)** — 3.3, ordem 16 — re-estimada de M — [story](stories/CMP-04-recebimento-manual.md)
- Requisitos: R-CMP-5, R-EST-6, R-EST-7, R-EST-8, R-FIN-4, R-COM-4, R-PRP-2, R-PRP-7, R-SEC-5.
- CA: registro com `purchasing.receive`, por quem não pediu nem aprovou (D8); entrada com custo rateado (P-16); cada recebimento gera os próprios títulos em aberto (`CreateOpenInstallmentsAsync`) e replaneja os previstos para o saldo; Σ recebido ≤ aprovado + tolerância; estorno cancela os títulos do recebimento (409 se em OP).

**CMP-02 [ ] Cotação (M)** — 3.3, ordem 17 — [story](stories/CMP-02-cotacao.md)
- Requisitos: R-CMP-2, R-CMP-6, R-COM-4, R-PRP-1, R-PRP-3, R-SEC-5.
- CA: comparativo com rádio por linha e tag "Sugerido"; IPI % por fornecedor; quem lançou cada resposta; anexo da proposta obrigatório acima de R$ 5.000,00 (D8); "Gerar pedidos de compra" pela API da CMP-03a e "Enviar todos para aprovação".

**CMP-03b [ ] PDF e envio do pedido de compra ao fornecedor (P)** — 3.3, ordem 18 — quebra da CMP-03 — [story](stories/CMP-03b-pdf-envio-pedido.md)
- Requisitos: R-CMP-3, R-COM-1, R-COM-3, R-PRP-2, R-SEC-5.
- CA: PDF pelo `IPdfRenderer` sem custo nem projeto; envio pela fila depois do `Clean`; confirmação de destinatário fora dos contatos do fornecedor; mensagem escapada, sem links; CR/LF recusado; pedido a `Sent`.

**PRD-06a2 [ ] Produto acabado: entrada no estoque, séries e lotes consumidos (M)** — 3.4, ordem 19 — quebra da PRD-06a (D1) — [story](stories/PRD-06a2-produto-acabado.md)
- Requisitos: R-PRD-7, R-EST-8, R-EST-7, R-COM-4, R-PRP-2, R-PRP-7, R-SEC-5.
- CA: na transação da conclusão, entrada `Production` pelo custo da OS (fontes síncronas, ADR 0025 §2), séries `{código}-{AAAA}-{seq}` (D15), lotes consumidos em `inventory.serial_components` e reservas liberadas; produto obrigatório; OS concluídas antes ficam sem entrada no estoque, documentado; estorno reverte a entrada e as séries não expedidas.

**PRD-06b [ ] Expedição: romaneio e saída do produto acabado (M)** — 3.4, ordem 20 — [story](stories/PRD-06b-expedicao-romaneio.md)
- Requisitos: R-PRD-8, R-EST-6, R-EST-8, R-COM-3, R-COM-4, R-PRP-2, R-PRP-7, R-SEC-5.
- CA: o romaneio confirmado baixa de forma síncrona, com 409 `insufficient_stock` sem gravar nada (D4); na Fase 5 a NF-e referencia o romaneio; numeração `ROM`; estoque geral confirmado por outra pessoa com `inventory.approve` (`SoleOwnerRule`); estorno síncrono; série em dois romaneios recusada.

**EST-04 [ ] Lote, série e rastreabilidade (M)** — 3.4, ordem 21 — [story](stories/EST-04-lote-serie-rastreabilidade.md)
- Requisitos: R-EST-8, R-PRD-7, R-PRJ-4, R-SEC-5, PRD §6 (Desempenho, LGPD).
- CA: série → OS, projeto, cliente, romaneio e lotes (a série herda os lotes da OS inteira); lote → OS, séries e clientes; cliente PF por iniciais para quem não tem `parties.view` (tela, DTO e CSV); OS concluída antes da PRD-06a2 aparece "Sem série"; lote em 200 OS em menos de 1 s.

**EST-01b [ ] Ajuste de estoque com motivo e aprovação (M)** — 3.4, ordem 22 — quebra da EST-01 — [story](stories/EST-01b-ajuste-estoque-aprovacao.md)
- Requisitos: R-EST-6, R-EST-5, R-EST-7, R-COM-2, R-PRP-5, R-PRP-7, R-SEC-5, R-SEC-10.
- CA: numeração `AJ`; um modo por ajuste; todo ajuste aprovado por outra pessoa com `inventory.approve`, que decide sem editar (`SoleOwnerRule`); saldo abaixo do reservado recusado; modo Delta de origem `InventoryCount` para a EST-05; ponto de bloqueio do local (`ILocationCountLock`): com contagem aberta, lançar, aprovar e estornar recebem 409; seed patch `inventory-adjust-warehouse`.

**EST-05 [ ] Inventário (contagem por local) (M)** — 3.4, ordem 23 — [story](stories/EST-05-inventario.md)
- Requisitos: R-EST-9, R-EST-6, R-EST-5, R-PRP-7, R-SEC-5.
- CA: numeração `INV`; contagem cega também no celular (kit chão de fábrica), sem custo no DTO do contador; recontador diferente do primeiro, conferido no servidor; ajuste Delta pela EST-01b sobre o saldo da aprovação; ajuste e estorno manual bloqueados no local durante a contagem (liga o bloqueio da EST-01b).

**EST-06 [ ] Capacidade de produzir (P)** — D, desejável; pode ir à Fase 4 — 3.4, ordem 24 — [story](stories/EST-06-capacidade-de-produzir.md)
- Requisitos: R-EST-10, R-PRP-2, R-CMP-1.
- CA: para uma BOM, quantas unidades o disponível permite, o item que limita e a falta; linha informativa "Com os pedidos em aberto: {n}" só com `purchasing.view` (pelo `OpenQuantityByItemAsync`), sem mudar o total; atalho para a solicitação preenchida, sem gravar sem confirmação.

_CAD-04c2 (dependentes, banco/Pix, adicionais, vale-transporte e documentos, M, `Ready`) movida para a Fase 6 (D9), antes da RH-02 — [story](stories/CAD-04c2-funcionarios-complemento.md)._

---

## Fase 4 — Planejamento, painéis e visão do dono

Stories detalhadas em [`docs/stories/`](stories/README.md#fase-4--planejamento-painéis-e-visão-do-dono), com critérios de aceite completos, ordem de execução, padrões comuns, validação do PO, as dependências da Fase 3 ainda em aberto e os 56 pontos em aberto consolidados. Escritas pelo @sm e validadas pelo @po em 21/10/2026: **todas `Draft`** (2 com GO e 13 com GO condicional, à espera das decisões do PO marcadas como pré-condição). A fase só começa com a Fase 3 concluída. Aqui fica o resumo de cada uma.

**Ordem de execução:** **4.1 "O dinheiro do jeito que ele vai acontecer":** FIN-07 → DSH-02 → FIN-08 → **4.2 "O painel do dono, parte do dinheiro":** DSH-04 → DSH-01a → **4.3 "Datas que a fábrica cumpre":** PLN-01a → PLN-01b → PLN-02 → PLN-04 → PLN-03 → **4.4 "A fábrica na parede":** PNL-01a → PNL-01b → DSH-03 → **4.5 "A visão completa do dono":** DSH-01b → PLN-05 (D).

**Quebras:** PLN-01 → 01a/01b · PNL-01 → 01a/01b · DSH-01 → 01a/01b. **Re-estimada:** FIN-08 (M → G). **ADRs sugeridas:** consultas do painel no Host × módulo `Reporting` (antes do 4.2); dono da fila de prioridade e datas previstas fora da auditoria (antes do 4.3); caminho `/tv#token` com cookie de sessão (antes do 4.4).

**FIN-07 [ ] Projeção de caixa ajustada pelo histórico do cliente (M)** — 4.1, ordem 1 — [story](stories/FIN-07-projecao-ajustada.md)
- Requisitos: R-FIN-11, R-FIN-10, R-TER-4, R-PRP-5, PRD §5.14 (Caixa), PRD §6 (Desempenho).
- CA: recebível deslocado pelo atraso médio do cliente (sem histórico, a média do tenant) e ponderado pela inadimplência acima do limite configurável; base × ajustado lado a lado com o menor saldo de cada um e a diferença causada pelos atrasos (dataset fixo nos dois cenários); detalhe do dia com o motivo de cada deslocamento; `ICashForecast` igual à tela para o painel; 50 mil títulos e 2 mil clientes em menos de 2 s, pontualidade em lote.

**DSH-02 [ ] Orçado × realizado e margem, por projeto e consolidado (M)** — 4.1, ordem 2 — [story](stories/DSH-02-orcado-realizado-margem.md)
- Requisitos: R-PRJ-5, PRD §5.14 (Margem), PRD §1.1 e §1.2, R-PRP-5, PRD §6.
- CA: um cálculo só (`IProjectMargins`) para o Projeto 360, o relatório e o painel; realizado por natureza pelas fontes do `04-fluxos` §3.3 (baixa para a OS, horas apontadas, títulos com projeto); coluna Comprometido; margens orçada, realizada e projetada; ranking dos piores; drill-down até o documento de origem com o total igual à célula; tudo só com `projects.view_cost`; 5 mil projetos e 200 mil custos em menos de 1 s.

**FIN-08 [ ] DRE gerencial mensal e por projeto (G)** — 4.1, ordem 3 — re-estimada de M — [story](stories/FIN-08-dre-gerencial.md)
- Requisitos: R-FIN-13, R-FIN-1, R-PRP-5, PRD §5.14 (Faturamento), PRD §6.
- CA: DRE pelos grupos do plano de contas, por competência (P-17): receita pela emissão do título, despesa pela competência ou pelo documento, lançamentos bancários pela data, juros e descontos das baixas em contas parametrizadas; `Opening`, `Planned` e transferências fora; dataset fixo; drill-down por célula; aba por projeto com `projects.view_cost`; `IRevenueSummary` para o painel; 24 meses em menos de 2 s.

**DSH-04 [ ] Minha pauta (M)** — 4.2, ordem 4 — [story](stories/DSH-04-minha-pauta.md)
- Requisitos: R-PRP-4, R-PRP-5, R-COM-2, PRD §5.14 (Pendências do dono), PRD §6.
- CA: detectores por módulo (`IAgendaSource`) para aprovações, pagamentos, vencimentos, atrasos, validades, qualidade, NC, faltas e ajustes, cada um com a sua permissão; ordem pelo que mais destrava; cada linha com o porquê e o link para a ação; item some quando resolvido (calculado na leitura); só itens de módulos liberados e habilitados; resposta parcial se uma fonte falhar; menos de 1 s.

**DSH-01a [ ] Painel do proprietário: estrutura, caixa, faturamento, inadimplência, comercial e pendências (G)** — 4.2, ordem 5 — quebra da DSH-01 — [story](stories/DSH-01a-painel-dono-financeiro-comercial.md)
- Requisitos: PRD §5.14, R-FIN-10, R-FIN-11, R-FIN-12, R-FIN-13, R-COML-9, R-PRP-4, R-PRP-5, PRD §6.
- CA: cards de saldo, caixa 30/60/90 base × ajustado, faturamento × meta, vencido com aging, comercial e pendências, cada um pela permissão do dono e com regra e drill-down; números iguais aos relatórios de origem (teste com dataset fixo); faixa "Atenção" acionável; metas de faturamento; cache por tenant de 60 s com invalidação por evento; painel em menos de 2 s.

**PLN-01a [ ] Motor de agendamento finito (G)** — 4.3, ordem 6 — quebra da PLN-01 — [story](stories/PLN-01a-motor-agendamento.md)
- Requisitos: R-PLN-3, R-PLN-1, R-SEC-5, PRD §6 (Desempenho).
- CA: algoritmo do `04-fluxos` §3.4 (fila, horas restantes com mínimo de 10%, alocação dia a dia na capacidade e no calendário, etapas sequenciais na OS); cenário fixo com 2 centros, 3 projetos e feriado batendo as datas esperadas; gravação atômica das datas e das alocações; uma execução por vez por tenant; recálculo manual e de hora em hora (`TenantJob`, dois tenants); `IProductionSchedule` com simulação sem gravar; 50 OS em menos de 2 s.

**PLN-01b [ ] Recálculo por evento, data prevista do projeto e risco (M)** — 4.3, ordem 7 — quebra da PLN-01 — [story](stories/PLN-01b-recalculo-previsao-risco.md)
- Requisitos: R-PLN-3, R-PLN-4, R-COM-2, R-PRP-5, PRD §1.2, PRD §6.
- CA: o plano se refaz a cada apontamento, etapa, OS, roteiro, qualidade, calendário, centro e aditivo, com pedidos agrupados; data prevista do projeto = fim da fabricação + prazo de instalação; `ProjectForecastChanged` com `computedAt` (fora de ordem não sobrescreve); entrega prevista no Projeto 360; prevista > prometida vira risco no Kanban, nas listas e numa notificação sem repetição.

**PLN-02 [ ] Fila de prioridade dos projetos (M)** — 4.3, ordem 8 — [story](stories/PLN-02-fila-prioridade.md)
- Requisitos: R-PLN-2, R-PLN-3, R-PLN-4, R-PRP-2, R-SEC-5.
- CA: arrastar (ou teclado) para reordenar; prévia "antes → depois" das datas e da folga pela simulação, sem gravar; aviso de quem passa a atrasar; salvar com versão (409), histórico e recálculo; permutação validada no servidor; prévia em menos de 2 s.

**PLN-04 [ ] Ocupação da fábrica por semana e centro (M)** — 4.3, ordem 9 — [story](stories/PLN-04-ocupacao-fabrica.md)
- Requisitos: R-PLN-6, R-PLN-1, PRD §5.14 (Produção), PRD §6.
- CA: horas comprometidas ÷ capacidade por semana e centro e na fábrica (números conferidos com o cenário da PLN-01a); carga além do horizonte; drill-down até as etapas; faixas de folga e lotado; gráfico de barras com tabela equivalente; `IFactoryLoad` para os painéis; menos de 1 s.

**PLN-03 [ ] Cronograma (Gantt) por projeto e por centro de trabalho (G)** — 4.3, ordem 10 — [story](stories/PLN-03-gantt.md)
- Requisitos: R-PLN-4, R-PLN-3, R-PRP-5, PRD §5.14 (Entregas), PRD §6.
- CA: visão por projeto (projeto → OS → etapa, prevista × prometida, real × previsto, "Define a entrega") e por centro (carga diária, feriados e paradas); escala dia/semana/mês; detalhe e abertura da OS pelo teclado; tabela equivalente; aba no Projeto 360; 300 OS utilizáveis em menos de 1,5 s; 400 px sem rolagem horizontal da página.

**PNL-01a [ ] Tokens de tela, sessão de quiosque e tempo real da TV (G)** — 4.4, ordem 11 — quebra da PNL-01 — [story](stories/PNL-01a-tokens-tela-sessao-quiosque.md)
- Requisitos: R-SEC-6, PRD §2, PRD §5.14 (Painel de produção), PRD §6 (Segurança), R-SEC-5.
- CA: token de 256 bits guardado como hash e mostrado uma vez; link `/tv#token` trocado por cookie de sessão (o token nunca chega ao log); principal de quiosque só em GET do painel; hub com grupos das claims; **token revogado derruba a tela em menos de 1 min**; rede permitida opcional; tenant suspenso e módulo não liberado desligam a TV; isolamento entre tenants.

**PNL-01b [ ] Visões do painel da TV (M)** — 4.4, ordem 12 — quebra da PNL-01 — [story](stories/PNL-01b-visoes-painel-tv.md)
- Requisitos: PRD §5.14 (Painel de produção), R-PRD-2, R-PLN-4, PRD §6.
- CA: quadro por etapa (OS, operador, tempo na etapa, real × padrão, previsto, atrasada e em risco), fila com previsão × prometido e atrasos, pela linha do token; rotação configurável; **atualização em menos de 2 s após o apontamento**; legível a 1080p e 768p, sempre escura; DTO sem valores nem dado pessoal além do primeiro nome do operador.

**DSH-03 [ ] Painel do supervisor (M)** — 4.4, ordem 13 — [story](stories/DSH-03-painel-supervisor.md)
- Requisitos: PRD §5.14 (Painel do supervisor), R-PRD-2, R-PRD-6, R-PLN-6, R-EST-4, R-CMP-1, R-PRP-4, R-PRP-5, PRD §6.
- CA: blocos Quadro (atrasadas e em risco, tempo real), Capacidade (semana atual e 4 seguintes), Qualidade (aprovar e NC abertas), Faltas de material (com a situação da compra e "Gerar solicitação") e Indicadores (horas paradas e taxa de NC por etapa); cada bloco pela permissão do dono; menos de 1,5 s.

**DSH-01b [ ] Painel do proprietário: margem, carteira, produção e entregas (M)** — 4.5, ordem 14 — quebra da DSH-01 — [story](stories/DSH-01b-painel-dono-margem-producao.md)
- Requisitos: PRD §5.14, R-PRJ-5, R-PLN-4, R-PLN-6, R-PRP-5, PRD §6.
- CA: margem orçada × projetada e piores projetos (só com `projects.view_cost`), projetos em andamento, carteira a produzir e a faturar, produção (OS atrasadas e ocupação) e entregas (previstas no mês e em risco), iguais às origens; projeto em risco na faixa "Atenção"; **painel completo em menos de 2 s; números conferem com os relatórios de origem (dataset fixo)**.

**PLN-05 [ ] Simulação de prazo do orçamento (M)** — D — 4.5, ordem 15 — [story](stories/PLN-05-simulacao-prazo-orcamento.md)
- Requisitos: R-PLN-5, R-COML-1, R-PRP-1, R-PRP-2.
- CA: projeto virtual montado das horas por centro do orçamento, simulado no fim ou numa posição da fila, sem gravar; fim da fabricação, entrega prevista e comparação com o prazo do orçamento; projetos que passariam a atrasar; "Usar este prazo" só preenche o formulário; resposta só com datas (sem custo); menos de 2 s.

---

## Fase 5 — Integrações fiscais e bancárias (go-live)

Stories detalhadas em [`docs/stories/`](stories/README.md#fase-5--integrações-fiscais-e-bancárias-go-live), com critérios de aceite completos, ordem por marcos, padrões comuns, checklist de go-live, dependências da Fase 3 e os 81 pontos em aberto numerados. Escritas e validadas em 21/10/2026 (checklist do @po, 10/10); **todas `Draft` (GO condicional)**: dependem da ADR 0023 (integração com o EmissorFiscal), da contadora (P-01, P-02, P-03), do certificado A1, da conta do Inter com API (P-08) e das stories da Fase 3 (PRD-06b, CMP-03a, CMP-04).

**Ordem de execução:** **5.1 Base:** FND-16 → FIS-01 → **5.2 Cobrança pelo Inter:** BNK-01a → BNK-01b → BNK-02 → **5.3 Conciliação:** BNK-03a → BNK-03b → **5.4 Nota de saída:** FIS-02a → FIS-02b → FIS-03a → FIS-03b → **5.5 Nota de entrada:** FIS-04a → FIS-04b → FIS-05 → **5.6 Desejáveis:** FIN-09 (D) → FIS-06 (D) → **5.7 Go-live:** GOL-01a → GOL-01b.

**Quebras e novas:** FIS-02 → 02a/02b · FIS-03 → 03a/03b · FIS-04 → 04a/04b · BNK-01 → 01a/01b · BNK-03 → 03a/03b · GOL-01 → 01a/01b. **Nova:** FND-16 (recepção de webhooks). **Re-estimadas:** FIS-01 (M → G), BNK-02 (M → G).

**FND-16 [ ] Recepção de webhooks (M)** — 5.1, ordem 1 — nova — [story](stories/FND-16-webhooks-de-entrada.md)
- Requisitos: R-FIN-7, R-FIS-3, PRD §6 Segurança e Observabilidade, R-SEC-5.
- CA: rota `POST /webhooks/{provider}/{tenantKey}` única anônima nova; HMAC sobre o corpo cru ou token na query (nunca no caminho logado), comparação em tempo constante; resposta igual para tenant inexistente e assinatura errada; `webhook_events` com deduplicação; 200 em até 2 s e processamento por `TenantJob`; dead-letter com "Reprocessar"; nada de corpo ou segredo em log.

**FIS-01 [ ] Análise do EmissorFiscal, ADR 0023 e adaptador `IFiscalEmitter` (G)** — 5.1, ordem 2 — re-estimada — [story](stories/FIS-01-emissorfiscal-adr-adaptador.md)
- Requisitos: R-FIS-1, R-FIS-8, R-FIS-6, R-FIS-7, PRD §6 Segurança, R-SEC-5.
- CA: ADR 0023 aprovada pelo PO antes do código e da FIS-02a (análise com o que o EmissorFiscal cobre e as lacunas L-a a L-k); `IFiscalEmitter` sem DTO do EmissorFiscal; reenvio com o mesmo `Idempotency-Key`, nunca reemissão em contingência; chave por tenant cifrada, sem escopo `admin`; webhook HMAC que só dispara reconsulta; XML guardado 5 anos; job de pendentes; testes de contrato com fake e com a homologação do EmissorFiscal; rede Docker dedicada.

**BNK-01a [ ] Inter: configuração, mTLS e rotação (M)** — 5.2, ordem 3 — quebra da BNK-01 — [story](stories/BNK-01a-inter-configuracao-mtls.md)
- Requisitos: R-FIN-7, R-FIN-8, R-FIN-2, PRD §6 Segurança, R-SEC-5.
- CA: credenciais e certificado por conta com `settings.edit`, validados e cifrados (`inter-credentials`, `inter-certificate`), nunca devolvidos; teste de conexão e escopos; token em cache; produção com confirmação; rotação testando o novo; alerta 30/15/7/1 dias.

**BNK-01b [ ] Bolepix a partir do título (G)** — 5.2, ordem 4 — quebra da BNK-01 — [story](stories/BNK-01b-bolepix-emissao-cancelamento.md)
- Requisitos: R-FIN-7, R-FIN-3, R-COM-1, R-COM-3, R-PRP-7.
- CA: boleto pelo saldo do título, assíncrono (`Requested → Issued`), PDF, linha digitável e Pix guardados; falha não muda o título e o reenvio consulta antes de criar; um boleto ativo por título; e-mail com o PDF; cancelamento manual e automático quando o título muda; fluxo completo no sandbox.

**BNK-02 [ ] Webhook de pagamento e conferência diária (G)** — 5.2, ordem 5 — re-estimada — [story](stories/BNK-02-webhook-pagamento-baixa.md)
- Requisitos: R-FIN-7, R-FIN-3, R-FIN-2, R-TER-4.
- CA: webhook com token na URL que só dispara reconsulta por mTLS; baixa pelo serviço da FIN-01 com transação, uma vez só (10 execuções paralelas); pagamento com diferença vai para conferência; conferência diária com execução dupla e dois tenants; efeitos no caixa, aging, pontualidade e contrato.

**BNK-03a [ ] Extrato pela API do Inter e por OFX (M)** — 5.3, ordem 6 — quebra da BNK-03 — [story](stories/BNK-03a-extrato-api-ofx.md)
- Requisitos: R-FIN-8, R-FIN-2, R-FIN-10.
- CA: importação diária e "Buscar agora" sem duplicar; OFX com parser seguro e conta conferida; saldo do banco × sistema com aviso; linhas imutáveis, com "Ignorar" e "Desfazer importação".

**BNK-03b [ ] Conciliação bancária (G)** — 5.3, ordem 7 — quebra da BNK-03 — [story](stories/BNK-03b-conciliacao-bancaria.md)
- Requisitos: R-FIN-8, R-FIN-5, R-FIN-3, R-PRP-6, R-PRP-7.
- CA: sugestão por valor, data e documento com motivo e confiança; 1×N; lançamento sem OP a partir da linha com `finance.pay`; baixa de recebimento; saída sem OP nunca vira pagamento; desfazer sem estornar; fechamento por período (proposta).

**FIS-02a [ ] Configuração fiscal do tenant (M)** — 5.4, ordem 8 — quebra da FIS-02 — [story](stories/FIS-02a-configuracao-fiscal-tenant.md)
- Requisitos: R-FIS-2, R-COM-2, R-SEC-10.
- CA: `fiscal.approve` no catálogo e seed patch `fiscal-permissions` (propostas); regime, CNAE, série e ambiente com `settings.edit`; produção só depois da GOL-01 (proposta); certificado conforme a ADR 0023, nunca em log; **alerta 30 dias antes do vencimento do certificado** (e 15, 7, 1).

**FIS-02b [ ] Regras fiscais e cálculo dos tributos (G)** — 5.4, ordem 9 — quebra da FIS-02 — [story](stories/FIS-02b-regras-fiscais-calculo.md)
- Requisitos: R-FIS-4, R-FIS-3, R-FIS-8, R-PRJ-5.
- CA: regras por operação com versão e ativação por outra pessoa; seed sem nenhuma regra (conteúdo da contadora); motor puro de ICMS, ST, IPI, PIS, COFINS e IBS/CBS batendo com os casos da contadora; sem regra, sem nota; DIFAL bloqueado enquanto o EmissorFiscal não suportar; marca dos tributos que entram no custo do projeto.

**FIS-03a [ ] NF-e de saída: emissão e autorização (G)** — 5.4, ordem 10 — quebra da FIS-03 — [story](stories/FIS-03a-nfe-saida-emissao.md)
- Requisitos: R-FIS-3, R-PRD-8, R-PRJ-5, R-FIS-4, R-COM-1, R-COM-3.
- CA: rascunho a partir do romaneio com tributos pela regra; prévia sem número; emissão idempotente com retorno assíncrono (autorizada, rejeitada com correção, denegada, contingência); XML e DANFE guardados e enviados; `InvoiceAuthorized` lança o imposto como custo do projeto; Inventory pela decisão da PRD-06b; nunca apagar; **emitida e autorizada em homologação SEFAZ com 3 cenários definidos pela contadora**.

**FIS-03b [ ] Cancelamento, CC-e e inutilização (M)** — 5.4, ordem 11 — quebra da FIS-03 — [story](stories/FIS-03b-nfe-cancelamento-cce-inutilizacao.md)
- Requisitos: R-FIS-3, R-SEC-10, R-PRJ-5.
- CA: cancelamento no prazo só com `fiscal.approve` (quem emite não cancela), reversos no custo e no estoque, aviso aos Proprietários; carta de correção até 20; inutilização de faixa com recusa local; documento nunca apagado.

**FIS-04a [ ] NF-e de entrada por XML (M)** — 5.5, ordem 12 — quebra da FIS-04 — [story](stories/FIS-04a-nfe-entrada-xml-importacao.md)
- Requisitos: R-FIS-5, R-CMP-5, R-PRP-3, R-EST-3.
- CA: XML sem DTD e entidade externa, XSD, assinatura, protocolo e destinatário conferidos; **chave duplicada recusada**; fornecedor pelo CNPJ; de-para lembrado; vínculo ao pedido e **divergência de quantidade e preço destacada**.

**FIS-04b [ ] Recebimento pela nota (M)** — 5.5, ordem 13 — quebra da FIS-04 — [story](stories/FIS-04b-recebimento-pela-nota.md)
- Requisitos: R-CMP-5, R-EST-7, R-FIN-4, R-FIN-10.
- CA: recebimento da CMP-04 preenchido pela nota (rateio de frete e IPI, P-16); crédito de impostos atrás de parâmetro até a P-01; título a pagar pelas duplicatas; uma vez só.

**FIS-05 [ ] NF-e de entrada por PDF (M)** — 5.5, ordem 14 — [story](stories/FIS-05-nfe-entrada-pdf.md)
- Requisitos: R-FIS-6, R-PRP-3.
- CA: extração isolada com tempo limite; chave com DV; sem DF-e no EmissorFiscal, pedido do XML ao fornecedor e leitura assistida com conferência de todo campo; nota marcada "Lida do PDF" e substituível pelo XML.

**FIN-09 [ ] Régua de cobrança (M)** — D — 5.6, ordem 15 — [story](stories/FIN-09-regua-de-cobranca.md)
- Requisitos: R-FIN-9, R-COM-1, R-FIN-12.
- CA: passos antes e depois do vencimento com o boleto; uma vez por passo; nunca para título pago (conferido no envio); pausa por cliente e título.

**FIS-06 [ ] NFS-e da instalação (M)** — D, condicionado à P-02 — 5.6, ordem 16 — [story](stories/FIS-06-nfse-instalacao.md)
- Requisitos: R-FIS-7, R-FIS-8, R-PRJ-3, R-PRJ-5.
- CA: NFS-e a partir do aceite técnico, com ISS pela regra e vínculo à NF-e do equipamento; ISS no custo do projeto; cancelamento registrado manualmente enquanto o EmissorFiscal não cancela NFS-e.

**GOL-01a [ ] Go-live: preparação e ensaio da virada (M)** — 5.7, ordem 17 — quebra da GOL-01 — [story](stories/GOL-01a-preparacao-ensaio-virada.md)
- Requisitos: PRD §7, §1.2, §6.
- CA: critérios de entrada com evidência e "Go" do PO; runbook de virada e de rollback; compose e vhost de produção; segredos de produção novos (o key ring acompanha o banco, ADR 0020); ensaio completo da **promoção da homologação a produção** numa cópia restaurada, com conferência dos dados; rollback ensaiado; treinamento por grupo.

**GOL-01b [ ] Go-live: virada e primeira semana (M)** — 5.7, ordem 18 — quebra da GOL-01 — [story](stories/GOL-01b-virada-primeira-semana.md)
- Requisitos: PRD §7, §1.2, R-PLT-2, R-TER-5.
- CA: Dolibarr congelado e carga final só dos cadastros alterados (MIG-01b e MIG-01c; ADR 0017); `DROP USER` do somente leitura e remoção dos segredos da migração; banco do tenant `konebras` promovido; `app.konebras.com.br` para o BemAqui e o Dolibarr em `adm.konebras.com.br` só leitura; fiscal e Inter em produção; primeira nota e primeiro boleto reais com o PO; acompanhamento diário da primeira semana; fechamento do go-live com a linha de base das métricas.

---

## Fase 6 — RH e Patrimônio (fim do MVP)

Stories detalhadas em [`docs/stories/`](stories/README.md#fase-6--rh-e-patrimônio-fim-do-mvp), escritas pelo @sm e validadas pelo @po em 21/10/2026 (checklist de 10 itens, 10/10 em todas). **Todas estão `Draft`**: PAT-02 com GO; as demais com GO condicional, até as decisões marcadas como pré-condição nos pontos em aberto (README da Fase 6, 58 pontos). Dependências abertas de outras fases: CAD-04c, PRD-03a/03b e PRD-04 (Fase 3, rascunho), PLN-01 e DSH-04 (Fase 4), FIS-04 (Fase 5) e a P-01.

**Ordem de execução:** **6.1 Ponto:** RH-01a → RH-01b → RH-01c → **6.2 Folha lançada:** RH-02a → RH-02c → RH-02b → RH-03 (D) → **6.3 LGPD:** LGPD-01a → LGPD-01b. **6.4 Patrimônio** em paralelo: PAT-01a → PAT-01b → PAT-02 → PAT-03a → PAT-03b.

**Quebras:** RH-01 → 01a/01b/01c · RH-02 → 02a/02b/02c · LGPD-01 → 01a/01b · PAT-01 → 01a/01b · PAT-03 → 03a/03b. Depreciação (R-PAT-6) continua na Fase 9 (ponto em aberto da PAT-01a).

**RH-01a [ ] Ponto: marcação pelo app, geolocalização opcional e consulta (M)** — 6.1, ordem 1 — quebra da RH-01 — [story](stories/RH-01a-marcacao-ponto.md)
- Requisitos: R-RH-4 (parte), PRD §6 (LGPD, Usabilidade), R-SEC-5, R-PRP-1, R-PRP-2.
- CA: "Meu ponto" no tablet (`/fab/ponto`, também por PIN) e no celular com a hora do servidor e a próxima marcação sugerida; funcionário sempre da sessão; duplo toque e `Idempotency-Key`; afastado ou desligado não marca; geolocalização por parâmetro, só em `/sensitive`; marcação nunca editada nem apagada. Pré-condição: regra do autoatendimento (ADR).

**RH-01b [ ] Ponto: lançamento manual, pedido de ajuste, abono e aprovação (M)** — 6.1, ordem 2 — quebra da RH-01 — [story](stories/RH-01b-ajustes-ponto-aprovacao.md)
- Requisitos: R-RH-4 (parte), PRD §6 LGPD, R-SEC-5, R-COM-2, R-COM-3, R-PRP-2, R-PRP-5, R-PRP-6 (D).
- CA: lançamento do RH e pedido do funcionário como ajuste `Pending`; abono com atestado como anexo sensível, sem CID; aprovação com `hr.approve` ("ajustes de ponto"), sem aprovar o próprio; "desconsiderar" no lugar de apagar; concorrência; período fechado bloqueia.

**RH-01c [ ] Ponto: espelho mensal, banco de horas e fechamento (G)** — 6.1, ordem 3 — quebra da RH-01 — [story](stories/RH-01c-espelho-banco-horas-fechamento.md)
- Requisitos: R-RH-4 (parte), R-RH-5, R-COM-3, PRD §6 (Desempenho, LGPD), R-SEC-5, R-PRP-2, R-PRP-5.
- CA: espelho por dia pela jornada e pelo calendário (dataset fixo minuto a minuto); tolerância e banco de horas como parâmetros; inconsistências acionáveis; fechamento com pré-condições e totais congelados; reabertura em cascata; PDF sem dado sensível; 50 funcionários em menos de 1 s.

**RH-02a [ ] Folha lançada: competência, rubricas, lançamento e importação (G)** — 6.2, ordem 4 — quebra da RH-02 — [story](stories/RH-02a-folha-lancada-rubricas.md)
- Requisitos: R-RH-5 (parte), R-RH-1, PRD §6 LGPD, R-SEC-5, R-SEC-10, R-PRP-1, R-PRP-3.
- CA: rubricas com seed; folha por competência e tipo, sem duplicar; holerite preenchido com o salário e com o mês anterior; bases e encargos da empresa digitados; importação CSV no layout padrão com prévia e proteção de entrada; valores só com `hr.view_sensitive`; nenhum cálculo legal.

**RH-02c [ ] Folha lançada: fechamento, CP de salários e encargos e folha prevista (M)** — 6.2, ordem 5 — quebra da RH-02 — [story](stories/RH-02c-fechamento-folha-cp-caixa.md)
- Requisitos: R-RH-5 (parte), R-FIN-4, R-FIN-10, R-FIN-13, R-PRP-2, R-PRP-7, R-SEC-5.
- CA: fechamento com impacto financeiro publica `PayrollClosed`; o Finance cria os títulos de origem `Payroll` pelo contrato do HR, idempotente; OP dos salários pré-selecionada; reabertura recusada com pagamento em andamento; folha prevista como estimativa no fluxo de caixa, substituída pela real. Pré-condição: favorecido do título de salário (ADR).

**RH-02b [ ] Folha lançada: holerite em PDF e envio (M)** — 6.2, ordem 6 — quebra da RH-02 — [story](stories/RH-02b-holerite-pdf-envio.md)
- Requisitos: R-RH-5 (parte), R-COM-1, R-COM-3, PRD §6 LGPD, R-SEC-5, R-PRP-2.
- CA: holerite com layout padrão (proventos, descontos, bases INSS/FGTS/IRRF, líquido) pelo `IPdfRenderer`, sem CPF completo nem dados bancários; lote em ZIP; envio só com a folha fechada, só ao e-mail pessoal, sem valor no corpo; situação do envio por holerite. Pré-condição: proteção do holerite no e-mail.

**RH-03 [ ] Exportação do RH para a contadora (M)** — D — 6.2, ordem 7 — [story](stories/RH-03-exportacao-contadora.md)
- Requisitos: R-RH-8, P-06, PRD §6 LGPD, R-SEC-5, R-PRP-3.
- CA: pacote CSV/XLSX no layout padrão documentado (movimentação, ponto fechado, folha); só período fechado; minimização; `SensitiveRead`; teste golden do layout.

**LGPD-01a [ ] Direitos do titular: pedido e exportação (M)** — 6.3, ordem 8 — quebra da LGPD-01 — [story](stories/LGPD-01a-pedido-titular-exportacao.md)
- Requisitos: PRD §6 LGPD, ADR 0018, R-SEC-5, R-COM-3.
- CA: pedido registrado e acompanhado só pelo Proprietário, com prazo e aviso; exportação JSON e PDF de terceiro PF, contato ou funcionário por `IPersonalDataProvider` em cada módulo; teste de arquitetura impede dado pessoal sem provedor; arquivo sensível com expurgo.

**LGPD-01b [ ] Direitos do titular: anonimização (M)** — 6.3, ordem 9 — quebra da LGPD-01 — [story](stories/LGPD-01b-anonimizacao.md)
- Requisitos: PRD §6 LGPD, ADR 0018, R-SEC-5, R-PRP-7.
- CA: prévia por módulo com o que fica retido por obrigação legal; anonimização irreversível com marcador, preservando documentos com movimento e a auditoria; parcial com retentativa; canário em todas as tabelas; arquivos não retidos apagados. Pré-condição: tabela de retenção por campo (contadora + jurídico).

**PAT-01a [ ] Patrimônio: cadastro de bens (M)** — 6.4, ordem 10 — quebra da PAT-01 — [story](stories/PAT-01a-cadastro-bens.md)
- Requisitos: R-PAT-1, R-COM-3, R-COM-4, R-SEC-5, R-PRP-1, R-PRP-3.
- CA: seed patch `assets-permissions` (proposta); bem com etiqueta `PAT-00001`, responsável pelo `IEmployees`, centro e local; foto e documentos por dono de anexo; lista com 5 mil bens em menos de 1 s; excluir × baixar; `IAssets`.

**PAT-01b [ ] Patrimônio: etiquetas QR em lote e leitura pelo app (M)** — 6.4, ordem 11 — quebra da PAT-01 — [story](stories/PAT-01b-etiqueta-qr-leitura.md)
- Requisitos: R-PAT-2, R-PRP-6 (D), R-COM-3, R-SEC-5.
- CA: PDF de etiqueta no tamanho da impressora (P-13) e em grade A4; QR só com a URL da etiqueta; leitura pela câmera ou pelo leitor do celular abre a ficha depois do login, com atalhos de manutenção e movimentação; 404 uniforme.

**PAT-02 [ ] Patrimônio: movimentação e baixa (P)** — 6.4, ordem 12 — [story](stories/PAT-02-movimentacao-bens.md)
- Requisitos: R-PAT-3, R-PRP-1, R-PRP-6 (D), R-SEC-5.
- CA: transferência de local, responsável e centro com histórico imutável; lote; baixa com motivo e reativação, sem apagar nada.

**PAT-03a [ ] Patrimônio: planos preventivos e OS de manutenção (M)** — 6.4, ordem 13 — quebra da PAT-03 — [story](stories/PAT-03a-planos-os-manutencao.md)
- Requisitos: R-PAT-4, R-COM-2, R-COM-3, R-COM-4, R-PRP-2, R-PRP-5, R-SEC-5.
- CA: plano por dias ou horas (horímetro); job diário que avisa e abre a OS uma vez; OS `MAN-2026-0001` preventiva e corretiva com checklist de 56 px; bem em manutenção enquanto parado.

**PAT-03b [ ] Patrimônio: custo, CP do prestador e capacidade (M)** — 6.4, ordem 14 — quebra da PAT-03 — [story](stories/PAT-03b-custo-cp-capacidade.md)
- Requisitos: R-PAT-4 (parte), R-PAT-5 (D), R-FIN-4, R-FIN-10, R-PRD-9 (D), R-PRP-7, R-SEC-5.
- CA: CP previsto do prestador (origem `Maintenance`) confirmado na conclusão; custo por bem e centro; `MaintenanceScheduled` vira parada no Production e reduz o `IWorkCenterCapacity` (efeito no planejamento com a PLN-01).

---

## Fase 7 — Folha calculada

Stories detalhadas em [`docs/stories/`](stories/README.md#fase-7--folha-calculada), validadas pelo @po em 21/10/2026 (10/10, todas `Draft` com GO condicional). Regras legais por vigência no banco, conferidas pela contadora, nunca no código; a calculada só substitui a lançada depois de 3 competências seguidas aceitas (FLH-06). eSocial fica fora do produto (PRD §3.3).

**Ordem de execução:** **7.1:** FLH-01 → FLH-02a → FLH-02b → **7.2:** FLH-03 → FLH-04 → FLH-05 → **7.3:** FLH-06 (começa depois da FLH-02b, em paralelo ao 7.2). **Quebra:** FLH-02 → 02a/02b.

**FLH-01 [ ] Tabelas legais por vigência (M)** — 7.1, ordem 1 — [story](stories/FLH-01-tabelas-legais-vigencia.md)
- Requisitos: R-RH-6, R-SEC-5.
- CA: INSS, IRRF (com dedução por dependente, desconto simplificado e redução adicional), salário-família, salário mínimo, FGTS, VT e encargos por vigência; conferência com segregação; versão ativa imutável e retificação explícita; escolha da versão pela competência.

**FLH-02a [ ] Cálculo mensal (G)** — 7.1, ordem 2 — quebra da FLH-02 — [story](stories/FLH-02a-calculo-mensal.md)
- Requisitos: R-RH-6, R-RH-5, R-FIN-10, PRD §6 LGPD, R-SEC-5, R-PRP-7.
- CA: salário proporcional, adicionais, faltas do ponto fechado, salário-família, VT, INSS, IRRF e FGTS pelo motor puro, centavo a centavo com o dataset da contadora; memória de cálculo; recálculo e avulsos; fechamento bloqueado até a virada.

**FLH-02b [ ] Horas extras, adicional noturno e DSR do ponto (M)** — 7.1, ordem 3 — quebra da FLH-02 — [story](stories/FLH-02b-horas-extras-dsr.md)
- Requisitos: R-RH-6, R-RH-4, R-SEC-5.
- CA: extras por tipo de dia com percentuais parametrizados, banco de horas pago ou descontado por decisão do RH, adicional noturno opcional, DSR e perda do DSR por parâmetro.

**FLH-03 [ ] Férias (G)** — 7.2, ordem 4 — [story](stories/FLH-03-ferias.md)
- Requisitos: R-RH-6, R-RH-4, R-RH-5, R-FIN-10, R-PRP-5, R-SEC-5.
- CA: períodos aquisitivos com alerta de vencimento; programação validada pelas regras parametrizadas; cálculo com 1/3, abono e médias; título antes do início; dias no espelho e no planejamento.

**FLH-04 [ ] 13º salário (M)** — 7.2, ordem 5 — [story](stories/FLH-04-decimo-terceiro.md)
- Requisitos: R-RH-6, R-RH-5, R-FIN-10, R-SEC-5.
- CA: 1ª e 2ª parcelas com avos e médias pela regra da contadora, adiantamento das férias abatido, previsão no fluxo de caixa.

**FLH-05 [ ] Rescisão (G)** — 7.2, ordem 6 — [story](stories/FLH-05-rescisao.md)
- Requisitos: R-RH-6, R-RH-3, R-RH-5, R-FIN-10, R-PRP-2, R-PRP-7, R-SEC-5.
- CA: simulação com custo total sem gravar; verbas pela matriz parametrizada por tipo de desligamento; multa do FGTS como encargo; termo de conferência; prazo de pagamento com aviso.

**FLH-06 [ ] Conferência paralela e virada (M)** — 7.3, ordem 7 — [story](stories/FLH-06-conferencia-paralela.md)
- Requisitos: R-RH-6, `06` Riscos, R-SEC-5, R-PRP-2.
- CA: lançada × calculada por funcionário e rubrica; diferenças classificadas; aceite da competência; virada pelo Proprietário só depois de 3 competências seguidas aceitas, com volta possível.

---

## Fase 8 — Cliente e comunicação

Stories detalhadas em [`docs/stories/`](stories/README.md#fase-8--cliente-e-comunicação), validadas pelo @po em 21/10/2026 (10/10, todas `Draft`; POR-01c com GO, as demais com GO condicional). Dependências abertas: PLN-01 (Fase 4), FIS-03, BNK-01, BNK-02 e FIN-09 (Fase 5).

**Ordem de execução:** **8.1:** POR-01a → **8.2:** POR-01b → POR-01c → **8.3:** POR-02 → **8.4:** WPP-01. **Quebra:** POR-01 → 01a/01b/01c.

**POR-01a [ ] Portal do cliente: acesso separado e isolamento (G)** — 8.1, ordem 1 — quebra da POR-01 — [story](stories/POR-01a-acesso-portal-cliente.md)
- Requisitos: PRD §5.15, R-SEC-1, R-SEC-2, R-SEC-3, R-SEC-5, R-SEC-11, PRD §6.
- CA: usuário do portal ligado a contato de cliente, convidado pelo backoffice; `aud`, claim e cookie próprios, token cruzado recusado nos dois sentidos; bloqueio e recuperação; `party_id` sempre da sessão com IDOR testado em todos os endpoints; DTOs por allowlist. Pré-condições: ADR do portal, módulo `portal` no catálogo, host.

**POR-01b [ ] Portal: projetos, etapa, entrega e documentos (M)** — 8.2, ordem 2 — quebra da POR-01 — [story](stories/POR-01b-portal-projetos-documentos.md)
- Requisitos: PRD §5.15, R-PRJ-4 (parte pública), R-COM-3, R-SEC-5, PRD §6 LGPD.
- CA: projetos do cliente com o `StageIndicator` e os marcos públicos, sem custo, margem nem dado interno; documentos só quando compartilhados; download conferido e auditado. Pré-condição: qual data de entrega mostrar.

**POR-01c [ ] Portal: parcelas e boletos (M)** — 8.2, ordem 3 — quebra da POR-01 — [story](stories/POR-01c-portal-parcelas-boletos.md)
- Requisitos: PRD §5.15, R-FIN-3, R-FIN-7, R-SEC-5, PRD §6 LGPD.
- CA: parcelas do cliente com campos públicos; boleto com linha digitável, Pix copia e cola e PDF (BNK-01); pago pelo webhook (BNK-02); funciona sem a BNK-01.

**POR-02 [ ] Aceite online do orçamento (M)** — 8.3, ordem 4 — [story](stories/POR-02-aprovacao-online-orcamento.md)
- Requisitos: R-COML-10, R-COML-4, R-COML-3, R-COM-2, R-PRP-2, R-PRP-7, R-SEC-5.
- CA: revisão corrente no portal com o PDF enviado; aceite com nome digitado, IP e hash do PDF; recusa com motivo; avisa o comercial e pré-preenche a aprovação interna (ADR 0005 D5 mantida). Pré-condição: valor do aceite.

**WPP-01 [ ] Notificações por WhatsApp (G)** — 8.4, ordem 5 — [story](stories/WPP-01-notificacoes-whatsapp.md)
- Requisitos: backlog WPP-01, R-FIN-9, PRD §3.2, PRD §6 (LGPD, Observabilidade), R-SEC-5.
- CA: provedor atrás de `IWhatsAppSender` com credenciais cifradas por tenant; só modelos aprovados; consentimento por contato e descadastro; régua de cobrança e etapa do projeto idempotentes; webhook com assinatura. Pré-condições: provedor e base legal do opt-in.

## Fase 9 — Evolução
Ponto legal (Portaria 671) · Depreciação · Retalhos de corte · Etapas terceirizadas · SSO · Contratos de manutenção recorrente (se validado).
