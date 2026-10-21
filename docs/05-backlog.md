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

Stories detalhadas em [`docs/stories/`](stories/README.md#fase-3--engenharia-estoque-compras-e-produção), com critérios de aceite completos, ordem de execução, padrões comuns, validação do PO e os 92 pontos em aberto consolidados. Escritas pelo @sm e validadas pelo @po em 21/10/2026: **todas `Draft`** (4 com GO e 19 com GO condicional, à espera das decisões do PO marcadas como pré-condição). Aqui fica o resumo de cada uma.

**Ordem de execução:** **3.1 "Estoque confiável":** CAD-04c → EST-01a → EST-01b → **3.2 "Da engenharia à OS":** ENG-01 → PRD-01 → ENG-02 → EST-02 → **3.3 "Comprar o que falta":** CMP-01 → CMP-02 → CMP-03a → CMP-03b → CMP-04 → **3.4 "Fábrica rodando":** EST-03 → PRD-02 → PRD-03a → PRD-03b → PRD-04 → PRD-05 → **3.5 "Fechar a OS":** PRD-06a → PRD-06b → EST-04 → EST-05 → EST-06 (D).

**Quebras:** EST-01 → 01a/01b · CMP-03 → 03a/03b · PRD-03 → 03a/03b · PRD-06 → 06a/06b. **Re-estimadas:** PRD-01 (M → G), CMP-04 (M → G). **Revisada:** CAD-04c (coerência com o código atual). **ADR sugerida antes do Marco 3.3:** alçada do pedido de compra por `Finance.Contracts` e `IPayables` por parcela (CMP-03a).

**CAD-04c [ ] Funcionários: dependentes, banco/Pix, adicionais, vale-transporte, afastamentos e documentos (M)** — 3.1, ordem 1 — quebra D6 da CAD-04b, revisada em 21/10/2026, `Draft` até o PO decidir o formato do vale-transporte e os tipos de afastamento — [story](stories/CAD-04c-funcionarios-complemento.md)
- Requisitos: R-RH-1, R-RH-3, R-SEC-5, R-SEC-10, PRD §6 (LGPD).
- CA: afastamento visível como "Afastado" com `hr.view` (calculado na leitura) e tipo/motivo só com `hr.view_sensitive` (LGPD art. 11), motivo sem CID; `Status` no `IEmployees` para a PRD-03a; troca de banco/Pix notificada aos Proprietários; CPF de dependente só quando é de IR; documentos com `hr.view_sensitive`, `SensitiveRead` e regras do dono de anexo (ADR 0019 §7).

_MIG-01d (saldos iniciais de estoque) não entra aqui: cancelada pelo PO em 18/10/2026. O saldo inicial é digitado na EST-01a (`OpeningBalance`, ADR 0016)._

**EST-01a [ ] Saldos, movimentos e custo médio (G)** — 3.1, ordem 2 — quebra da EST-01 — [story](stories/EST-01a-saldos-movimentos-custo-medio.md)
- Requisitos: R-EST-4, R-EST-5, R-EST-6, R-EST-7, R-EST-2, R-EST-3, R-EST-8, R-COM-4, R-PRP-7, R-SEC-5.
- CA: saldo inicial `OpeningBalance` em D 23:59:59 (também em lote, atômico); entrada manual, saída avulsa e transferência com impacto no diálogo; negativo bloqueado, com lock do saldo e `CHECK`s (20 saídas paralelas da última unidade gravam uma); custo médio global confere com o cálculo de referência (dataset de 30 movimentos); movimento imutável (trigger) e estorno por movimento inverso, pelo documento de origem; `IStockMovements` idempotente por origem e `IStockBalances` em lote; Saldos com 50 mil linhas em menos de 1 s.

**EST-01b [ ] Ajuste de estoque com motivo e aprovação (M)** — 3.1, ordem 3 — quebra da EST-01 — [story](stories/EST-01b-ajuste-estoque-aprovacao.md)
- Requisitos: R-EST-6, R-EST-5, R-EST-7, R-COM-2, R-PRP-5, R-PRP-7, R-SEC-5, R-SEC-10.
- CA: ajuste lançado com `inventory.adjust` fica pendente sem mexer no saldo; aprovação com `inventory.approve` por outra pessoa (exceção do Proprietário único, D6) gera os movimentos `Adjustment`, recalculando sobre o saldo atual; reprovar e cancelar sem efeito; aprovação concorrente grava uma vez; estorno pelo documento.

**ENG-01 [ ] Templates de roteiro (M)** — 3.2, ordem 4 — [story](stories/ENG-01-templates-roteiro.md)
- Requisitos: R-ENG-1, R-ENG-2, R-PRP-1, R-PLN-1, R-SEC-5.
- CA: CRUD de roteiros com etapas (centro validado por `IWorkCenters`, horas padrão, qualidade, exige aprovação, checklist), sequência contígua e erros por linha; seed do roteiro de exemplo Projeto → Corte → Montagem → Qualidade → Expedição (P-04), inativo quando não há centro único; duplicar; excluir sem uso e inativar com uso; editar não muda OS já criadas; `IRoutingTemplates` para a Produção; seed patch de permissões (proposta).

**PRD-01 [ ] Geração da OS de fabricação (G)** — 3.2, ordem 5 — re-estimada de M — [story](stories/PRD-01-geracao-os-fabricacao.md)
- Requisitos: R-PRD-1, R-PRJ-2, R-ENG-1, R-ENG-6, R-PRP-1, R-PRP-2, R-COM-3, R-COM-4, R-SEC-5.
- CA: consumidor idempotente do `ProjectItemCreated` cria a OS `Open` com o roteiro sugerido copiado (ou "Sem roteiro" com aviso); "Gerar OS" para os itens dos projetos da Fase 2; etapas editáveis até a primeira iniciar; anexos técnicos; lista com atraso e filtros; cancelamento com `ProductionOrderCancelled`; `IProductionOrders`; migration N-1 que preserva a OS de instalação da COM-06.

**ENG-02 [ ] BOM da OS (M)** — 3.2, ordem 6 — [story](stories/ENG-02-bom-da-os.md)
- Requisitos: R-ENG-3, R-ENG-4, R-ENG-5, R-EST-3, R-PRP-2, R-PRP-3, R-SEC-5.
- CA: BOM na aba da OS, copiada da última OS do produto, com necessidade (qtd × qtd da OS × (1 + perda), na unidade de estoque) e falta ao vivo; liberar com `engineering.approve` publica `BomReleased` com a necessidade calculada; revisão com histórico e comparação publica `BomRevised`; liberação concorrente publica um evento só.

**EST-02 [ ] Reservas e disponível (M)** — 3.2, ordem 7 — [story](stories/EST-02-reservas-disponivel.md)
- Requisitos: R-EST-4, R-ENG-4, R-ENG-5, R-EST-5, R-CMP-1, R-PRP-5, R-SEC-5.
- CA: reservar acima do disponível reserva o possível e gera falta para compras (`StockShortageChanged`), inclusive com disponível zero; liberações concorrentes nunca reservam além do físico; revisão ajusta pela necessidade vigente; OS cancelada ou concluída libera; material que chega completa as faltas; disponível = físico − reservado em todas as telas e contratos; aba Material da OS e lista Faltas.

**CMP-01 [ ] Solicitação de compra (M)** — 3.3, ordem 8 — [story](stories/CMP-01-solicitacao-compra.md)
- Requisitos: R-CMP-1, R-CMP-6, R-COM-2, R-COM-4, R-PRP-2, R-PRP-4, R-PRP-5, R-SEC-5.
- CA: manual com destino projeto ou estoque geral; automática pela falta da BOM sem duplicar (atualiza e cancela a linha em aberto), com recomposição pelas faltas abertas; job diário do estoque mínimo idempotente, com teste de execução dupla e de dois tenants, e aviso consolidado; visão "Por item"; seed patch de Compras para o Almoxarifado (proposta).

**CMP-02 [ ] Cotação (M)** — 3.3, ordem 9 — [story](stories/CMP-02-cotacao.md)
- Requisitos: R-CMP-2, R-CMP-6, R-COM-4, R-PRP-1, R-PRP-3, R-SEC-5.
- CA: cotação a partir das linhas de SC com os últimos fornecedores sugeridos; respostas com preço (unidade do fornecedor convertida), frete, IPI, prazo e condição; comparativo com o menor custo total destacado e a última compra; vencedor por linha, com motivo quando não é o menor; concluída imutável; próximo passo "Gerar pedidos de compra".

**CMP-03a [ ] Pedido de compra: alçada e CP previsto (G)** — 3.3, ordem 10 — quebra da CMP-03 — [story](stories/CMP-03a-pedido-compra-alcada-cp.md)
- Requisitos: R-CMP-3, R-CMP-4, R-CMP-6, R-FIN-4, R-FIN-5, R-FIN-10, R-COM-2, R-COM-4, R-PRP-1, R-PRP-7, R-SEC-5.
- CA: pedido por fornecedor vencedor ou manual com padrões; alçada do motor da FIN-03b para `PurchaseOrder` com valor de referência contra fracionamento, alçada viável e segregação solicitante × aprovador; e-mail só com número, fornecedor, total e link; aprovação gera os títulos a pagar `Planned` estimados por parcela no fluxo de caixa, sem duplicar; compra para o projeto como "Comprometido", não como realizado (ponto em aberto); reaprovação ao editar; cancelamento cancela as previsões.

**CMP-03b [ ] PDF e envio do pedido ao fornecedor (P)** — 3.3, ordem 11 — quebra da CMP-03 — [story](stories/CMP-03b-pdf-envio-pedido.md)
- Requisitos: R-CMP-3, R-COM-1, R-COM-3, R-PRP-2, R-SEC-5.
- CA: PDF pelo `IPdfRenderer` (sem custo nem projeto, teste de extração de texto na imagem Alpine); envio pela fila com o PDF guardado como arquivo do sistema e enviado depois do `Clean`; pedido passa a `Sent`; reenvio e limite diário.

**CMP-04 [ ] Recebimento manual contra o pedido (G)** — 3.3, ordem 12 — re-estimada de M — [story](stories/CMP-04-recebimento-manual.md)
- Requisitos: R-CMP-5, R-EST-6, R-EST-7, R-EST-8, R-FIN-4, R-COM-4, R-PRP-2, R-PRP-7, R-SEC-5.
- CA: conferência contra o saldo do pedido (acima é recusado), com lote e local; entrada no estoque com custo = valor + rateio de frete e IPI pelo valor (P-16), conferido com dataset; faltas de OS completadas; o Finance confirma os títulos previstos pelo valor recebido e tira o "Estimado"; estorno do recebimento e encerramento do saldo; recebimentos concorrentes não duplicam a entrada. (Recebimento por NF-e na Fase 5.)

**EST-03 [ ] Requisição de material para a OS e devolução (M)** — 3.4, ordem 13 — [story](stories/EST-03-requisicao-material-os.md)
- Requisitos: R-PRD-5, R-EST-4, R-EST-6, R-EST-8, R-PRJ-5, R-PRJ-4, R-PRP-7, R-SEC-5.
- CA: baixa registra lote (sugerido pelo mais antigo) e consome a reserva; custo vai para o projeto (`StockIssued` → `commercial.project_costs` → realizado de Material no Projeto 360, dataset fixo); pedido do operador confirmado pelo almoxarife; devolução reverte o custo; OS cancelada devolve ou mantém como perda; baixas concorrentes nunca passam do reservado + disponível.

**PRD-02 [ ] Kanban da produção (M)** — 3.4, ordem 14 — [story](stories/PRD-02-kanban.md)
- Requisitos: R-PRD-2, PRD §5.14, PRD §6, R-PRP-5, R-SEC-5.
- CA: colunas por etapa com a letra no display de andar e horas restantes; cartões com projeto, cliente, horas × padrão, prazo, atraso (faixa vermelha) e risco (âmbar); filtros na URL; tempo real pelo `ProductionHub` (grupo do tenant, mensagem só com o id, push depois do commit, < 2 s) com reserva por polling; sem mudar etapa pelo arraste (ponto em aberto); 300 OS em menos de 1 s.

**PRD-03a [ ] App do operador: fila, apontamento e checklist (G)** — 3.4, ordem 15 — quebra da PRD-03 — [story](stories/PRD-03a-pwa-fila-apontamento.md)
- Requisitos: R-PRD-3, R-PRD-6, PRD §6 (Usabilidade), R-PRP-5, R-SEC-5.
- CA: `/fab` sem dock com a fila do operador; iniciar (OS e projeto passam a "Em produção"), pausar com motivo (falta de material avisa o almoxarifado), retomar e finalizar com checklist obrigatório e cronômetro; um apontamento aberto por funcionário, sempre o do usuário logado; afastado não aponta; supervisor aponta por funcionário, conclui e corrige; alvos ≥ 56 px no tablet de entrada e a 400 px.

**PRD-03b [ ] App do operador: PIN e funcionamento sem rede (M)** — 3.4, ordem 16 — quebra da PRD-03 — [story](stories/PRD-03b-pwa-pin-offline.md)
- Requisitos: R-PRD-3, PRD §6 (Usabilidade, Segurança), R-SEC-1, R-SEC-3, R-SEC-7, R-SEC-5.
- CA: PWA instalável; aparelho da fábrica registrado pelo supervisor (cookie `__Host-`); PIN opcional depois do login completo no aparelho, com bloqueio por tentativas, sessão curta limitada ao `shopfloor` e proibido para perfis com ações reservadas; apontamento offline sincroniza ao reconectar sem duplicar (reenvio duplo no teste), com conflitos avisados ao supervisor.

**PRD-04 [ ] Custo de mão de obra (P)** — 3.4, ordem 17 — [story](stories/PRD-04-custo-mao-de-obra.md)
- Requisitos: R-PRD-4, R-RH-2, R-PRJ-5, R-SEC-5, PRD §6 (LGPD).
- CA: apontamento fechado × custo-hora do cargo (snapshot) = custo da OS e do projeto, levado pelo `LaborEntryClosed` ao realizado de Mão de obra no Projeto 360 (dataset fixo); custo-hora alterado depois não muda o passado; cargo sem custo-hora avisado; correção por estorno e novo valor; nunca mostra salário.

**PRD-05 [ ] Qualidade e não conformidade (M)** — 3.4, ordem 18 — [story](stories/PRD-05-qualidade-nao-conformidade.md)
- Requisitos: R-PRD-6, R-COM-2, R-COM-3, R-COM-4, R-PRP-5, R-SEC-5, R-SEC-10.
- CA: aprovar com checklist libera a próxima etapa ou deixa a OS pronta para concluir; reprovar abre a NC (descrição, causa, ação, responsável) e devolve a OS à etapa escolhida, com nova tentativa na mesma etapa de qualidade; quem fez a etapa não aprova a qualidade dela (proposta); ciclo da NC até o encerramento; decisão concorrente grava uma vez.

**PRD-06a [ ] Conclusão da OS: produto acabado com série e custo (M)** — 3.5, ordem 19 — quebra da PRD-06 — [story](stories/PRD-06a-conclusao-os-produto-acabado.md)
- Requisitos: R-PRD-7, R-EST-8, R-EST-7, R-PRJ-6, R-PRJ-7, R-COM-4, R-PRP-2, R-PRP-7, R-SEC-5.
- CA: série por unidade e entrada do produto acabado pelo custo apurado da OS (material + horas), sem voltar a contar no projeto; lotes consumidos registrados; reservas restantes liberadas; a última OS do projeto passa o projeto a "Fabricado" e publica `ProjectFabricated`, que libera a parcela (registro de gatilhos); estorno da conclusão sem série expedida.

**PRD-06b [ ] Expedição: romaneio e saída do produto acabado (M)** — 3.5, ordem 20 — quebra da PRD-06 — [story](stories/PRD-06b-expedicao-romaneio.md)
- Requisitos: R-PRD-8, R-EST-6, R-EST-8, R-COM-3, R-COM-4, R-PRP-2, R-PRP-7, R-SEC-5.
- CA: romaneio do projeto com séries, endereço da obra e transporte; PDF sem valores; confirmar a expedição baixa o produto acabado (`Shipment`, sem custo de projeto) e marca as séries como expedidas (ponto em aberto: baixa no romaneio ou na NF-e da Fase 5); cancelar e estornar; série em dois romaneios recusada.

**EST-04 [ ] Lote, série e rastreabilidade (M)** — 3.5, ordem 21 — [story](stories/EST-04-lote-serie-rastreabilidade.md)
- Requisitos: R-EST-8, R-PRD-7, R-PRJ-4, R-SEC-5, PRD §6 (Desempenho).
- CA: série → OS, projeto, cliente, romaneio e lotes de MP consumidos (com fornecedor e recebimento); lote → OS, séries, projetos e clientes onde foi usado, conciliado com o recebido; exportação CSV segura e auditada; abas Lotes e Séries no item; lote em 200 OS em menos de 1 s.

**EST-05 [ ] Inventário (M)** — 3.5, ordem 22 — [story](stories/EST-05-inventario.md)
- Requisitos: R-EST-9, R-EST-6, R-EST-5, R-PRP-7, R-SEC-5.
- CA: inventário por local com a foto do saldo; contagem cega (também no celular) e recontagem das divergências; diferença corrigida pelos movimentos do período; fechar gera um ajuste da EST-01b, aprovado por outra pessoa; um inventário aberto por local.

**EST-06 [ ] Capacidade de produzir (P)** — D — 3.5, ordem 23 — [story](stories/EST-06-capacidade-de-produzir.md)
- Requisitos: R-EST-10, R-PRP-2, R-CMP-1.
- CA: para uma BOM, quantas unidades o disponível (já sem o reservado) permite, o item que limita e a falta para uma quantidade desejada; atalho para a solicitação de compra preenchida, sem gravar sem confirmação; 100 itens em menos de 1 s.

---

## Fase 4 — Planejamento, painéis e visão do dono

**PLN-01 [ ] Motor de agendamento (G)** — Algoritmo da seção 3.4 de 04-fluxos, recálculo por evento e por hora.
- CA: testes com cenários fixos (2 centros, 3 projetos, feriado) batendo datas esperadas; recálculo de 50 OS < 2 s.

**PLN-02 [ ] Fila de prioridade (M)** — Arrastar projetos para reordenar; mostra impacto nas datas.

**PLN-03 [ ] Gantt (G)** — Por projeto e por centro de trabalho; prevista × prometida.

**PLN-04 [ ] Ocupação da fábrica (M)** — Horas comprometidas / capacidade por semana e centro.

**PLN-05 [ ] Simulação de prazo do orçamento (M)** — D.

**PNL-01 [ ] Painel TV (G)** — Tokens de quiosque, configuração (linha/visões/rotação), SignalR, layout grande e escuro.
- CA: token revogado derruba a tela em < 1 min; atualização < 2 s após apontamento.

**DSH-01 [ ] Dashboard do proprietário (G)** — Todos os cards da seção 5.14 do PRD com drill-down.
- CA: carrega < 2 s; números conferem com relatórios de origem (teste com dataset fixo).

**DSH-02 [ ] Orçado × realizado e margem (M)** — Por projeto e consolidado, por tipo de custo.

**FIN-07 [ ] Projeção ajustada por histórico (M)** — Seção 3.5; base × ajustado; menor saldo.

**FIN-08 [ ] DRE gerencial (M)** — Mensal e por projeto.

**DSH-04 [ ] Minha pauta (M)** — Requisitos: PRP-4, PRP-5. Lista por usuário do que precisa dele hoje, montada pelos detectores dos módulos (OP para aprovar, títulos vencendo, OS atrasadas, orçamentos expirando, faltas), ordenada pelo que mais destrava, cada linha com o porquê e o link para resolver.
- CA: cada item some quando resolvido; o usuário só vê itens de módulos a que tem acesso; a pauta abre em menos de 1 s.

**DSH-03 [ ] Painel do supervisor (M)** — Kanban + capacidade + NC + faltas.

---

## Fase 5 — Integrações fiscais e bancárias (go-live)

**FIS-01 [ ] Análise do EmissorFiscal (M)** — Ler `D:\GIT\EmissorFiscal`, documentar capacidades e forma de integração em ADR, listar lacunas (Reforma Tributária, NFS-e, DF-e).
- CA: ADR aprovado pelo PO antes de FIS-02.

**FIS-02 [ ] Configuração fiscal e regras (M)** — Certificado A1, série, ambiente, regras por operação (conteúdo da contadora).
- CA: alerta 30 dias antes do vencimento do certificado.

**FIS-03 [ ] NF-e de saída (G)** — Da expedição/venda; autorização, rejeição tratada, cancelamento, CC-e, DANFE/XML por e-mail; baixa do produto acabado.
- CA: emitida e autorizada em homologação SEFAZ com 3 cenários definidos pela contadora.

**FIS-04 [ ] NF-e de entrada por XML (M)** — Upload, validação, de-para de itens do fornecedor (lembrado para a próxima vez), vínculo ao pedido, recebimento.
- CA: XML duplicado (mesma chave) é recusado; divergência de quantidade/preço com pedido é destacada.

**FIS-05 [ ] NF-e de entrada por PDF (M)** — Extração da chave e dados; busca do XML pela chave se suportado; senão leitura assistida com conferência obrigatória.

**FIS-06 [ ] NFS-e (M)** — D, condicionado à pendência P-02.

**BNK-01 [ ] Banco Inter: emissão de bolepix (G)** — Configuração por tenant (certificado + credenciais), emissão a partir do recebível, PDF/linha digitável/Pix copia-e-cola, envio por e-mail, cancelamento.
- CA: fluxo completo em sandbox; falha de emissão não perde o recebível.

**BNK-02 [ ] Webhook de pagamento (M)** — Recebe, valida, baixa título, cria transação, idempotente.
- CA: webhook repetido não baixa duas vezes; job de conferência diária consulta boletos em aberto (caso o webhook falhe).

**BNK-03 [ ] Conciliação (G)** — Extrato pela API Inter + importação OFX; sugestão de match por valor/data/documento; conciliação manual.

**FIN-09 [ ] Régua de cobrança (M)** — D.

**GOL-01 [ ] Go-live Konebras (M)** — **Promoção da homologação a produção** (ADR 0020): o banco do tenant `konebras` da homologação, que já tem os dados reais desde a FIN-00, passa a ser o de produção, com a troca de DNS e domínio (`app.konebras.com.br` para o BemAqui e o Dolibarr para `adm.konebras.com.br`), dos segredos de produção e das chaves de Data Protection; carga final do Dolibarr só com os cadastros alterados depois da última carga (MIG-01b e MIG-01c com a infraestrutura da MIG-01a; ADR 0017), `DROP USER` do usuário somente leitura e remoção do segredo, treinamento, checklist de virada, acompanhamento da primeira semana.

---

## Fase 6 — RH e Patrimônio (fim do MVP)

**RH-01 [ ] Ponto básico (G)** — Marcação no PWA (geolocalização opcional), lançamento manual, espelho mensal, banco de horas simples, ajustes com aprovação, fechamento do período.

**RH-02 [ ] Folha lançada e holerite (G)** — Lançar/importar rubricas do período, gerar holerites PDF, envio por e-mail, fechamento gera CP de salários e encargos.
- CA: holerite com layout padrão (proventos, descontos, bases INSS/FGTS/IRRF, líquido).

**RH-03 [ ] Exportação para contadora (M)** — D, layout a definir.

**LGPD-01 [ ] Exportação e anonimização a pedido do titular (M)** — ADR 0018. O Proprietário atende o pedido; exportação em JSON e PDF de terceiro PF, contato ou funcionário; anonimização que preserva os documentos com movimento; pedido e atendimento auditados.

**PAT-01 [ ] Cadastro de patrimônio e QR (M)** — Bens, etiquetas em lote (PDF para impressora de etiqueta), leitura pelo PWA.

**PAT-02 [ ] Movimentação de bens (P)**

**PAT-03 [ ] Manutenção preventiva/corretiva (M)** — Planos, OS de manutenção, custo, CP do prestador; integração com capacidade (PAT-5).

---

## Fase 7 — Folha calculada
**FLH-01 [ ]** Tabelas parametrizadas por vigência (INSS, IRRF, salário-família) · **FLH-02 [ ]** Cálculo mensal (salário, HE e DSR do ponto, adicionais, faltas, INSS, IRRF, FGTS) · **FLH-03 [ ]** Férias · **FLH-04 [ ]** 13º · **FLH-05 [ ]** Rescisão · **FLH-06 [ ]** Conferência paralela com a folha da contadora por 3 meses antes de virar a chave.

## Fase 8 — Cliente e comunicação
**POR-01 [ ]** Portal do cliente (projetos, etapa, previsão, documentos, boletos) · **POR-02 [ ]** Aprovação online de orçamento · **WPP-01 [ ]** Notificações por WhatsApp (cobrança, status do projeto).

## Fase 9 — Evolução
Ponto legal (Portaria 671) · Depreciação · Retalhos de corte · Etapas terceirizadas · SSO · Contratos de manutenção recorrente (se validado).
