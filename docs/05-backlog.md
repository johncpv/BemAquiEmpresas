# 05 — Backlog por fase

Formato: `ID — Como <perfil>, quero <ação> para <benefício>` + critérios de aceite (CA).
Tamanho relativo: P (até 1 sessão de trabalho), M (2–3), G (4+). Marque `[x]` ao concluir.
Regra: não comece uma fase sem a anterior concluída, exceto quando indicado.

---

## Fase 0 — Fundação

Stories detalhadas em [`docs/stories/`](stories/README.md), com critérios de aceite completos, ordem de execução, Definition of Ready e pontos em aberto. A linha `Requisitos:` usa o prefixo `R-` (ADR 0005 D9). Aqui fica o resumo de cada uma.

**Ordem de execução** (revisão pré-Fase 0 §2.3 + stories novas): FND-01 → FND-11 → FND-07 → FND-02 → FND-03a → FND-08 → FND-12 → FND-04 → FND-05 → FND-13 → FND-03b → FND-06 → FND-06b → FND-06c → FND-06d → FND-10 → FND-14 → FND-15. Em paralelo desde a FND-01: FND-09a → FND-09b e FND-09c.

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

**FND-09a [ ] Tokens, tema e componentes visuais base (M)** — pronta; aguarda validação visual do PO — quebra da FND-09 — [story](stories/FND-09a-tokens-tema.md)
- Requisitos: PRD §6 (Usabilidade).
- CA: tokens do protótipo mapeados para o shadcn, com as correções de contraste da revisão (§2.4); `--signal` separado de `--warn`; tema claro/escuro/automático salvo no perfil, sem flash; Archivo self-hosted; chips, indicador de etapa e `Money`; lint proíbe cor literal.

**FND-09b [ ] Shell do backoffice e navegação (M)** — pronta; aguarda validação visual do PO — quebra da FND-09 — [story](stories/FND-09b-shell-navegacao.md)
- Requisitos: PRD §6 (Usabilidade), R-COM-2.
- CA: dock com as 7 áreas, cada uma abrindo popover (desktop) ou sheet (celular) com os módulos (ADR 0005 D8); sub-telas em abas na topbar; Ctrl K abre a paleta; topbar a 400px com busca em ícone e tema no menu do usuário; `<nav>`, skip link, foco e título na troca de rota.

**FND-09c [x] DataTable, estados e formulário base (M)** — quebra da FND-09 — [story](stories/FND-09c-datatable-estados-formulario.md)
- Requisitos: PRD §6 (Desempenho), R-SEC-5, R-PRP-2.
- CA: tabela padrão com filtro, ordenação e paginação server-side; Skeleton, EmptyState, ErrorState e InlineError ligados ao TanStack Query; formulário RHF + Zod com mensagens PT-BR e erros do ProblemDetails; cliente Orval sem `fetch` à mão; histórico por registro.

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

Stories detalhadas em [`docs/stories/`](stories/README.md#fase-1--cadastros-e-migração), com critérios de aceite completos, ordem de execução, pontos em aberto e a validação do PO (18/10/2026, todas `Draft` com GO condicional). Aqui fica o resumo de cada uma.

**Ordem de execução:** CAD-06 → CAD-05a → CAD-05b → CAD-01a → CAD-01b → CAD-03 → CAD-02a → CAD-02b → CAD-04a → CAD-04b → MIG-01b → MIG-01c. A CAD-03 pode correr em paralelo com a CAD-01a/01b, e a MIG-01a em paralelo desde a CAD-05a. A MIG-01d roda depois do EST-01 e a MIG-01e depois da FIN-01/FIN-02 (dependem de tabelas dessas fases; decisão do PO sobre o fechamento da Fase 1).

**Quebras:** CAD-01 → 01a/01b · CAD-02 → 02a/02b · CAD-04 → 04a/04b · CAD-05 → 05a/05b · MIG-01 → 01a/01b/01c/01d/01e. Re-estimadas: CAD-03 e CAD-06 (P → M).

**CAD-06 [ ] Configurações da empresa (M)** — re-estimada — [story](stories/CAD-06-configuracoes-empresa.md)
- Requisitos: R-COM-4, R-COM-3, R-RH-2, R-PRJ-7, R-COML-1, R-SEC-5, R-AJD-1.
- CA: dados da empresa e logo (allowlist, ClamAV, rota pública só do logo); numeração editável por tipo sem reiniciar a sequência e com "próximo número" que só sobe; % de encargos com histórico e evento `PayrollChargesChanged`; garantia padrão por tipo de item; modo de precificação (P-14).

**CAD-05a [ ] Plano de contas gerencial e centros de custo (M)** — quebra da CAD-05 — [story](stories/CAD-05a-plano-contas-centros-custo.md)
- Requisitos: R-FIN-1, R-PLT-3, R-FIN-13, R-SEC-5, R-PRP-1.
- CA: plano de contas padrão por seed idempotente, em árvore; código sugerido e tipo herdado; só conta analítica recebe lançamento; conta com lançamento não pode ser excluída, só inativada; centros de custo com as mesmas regras.

**CAD-05b [ ] Contas bancárias, caixas e condições de pagamento (M)** — quebra da CAD-05 — [story](stories/CAD-05b-contas-bancarias-condicoes-pagamento.md)
- Requisitos: R-FIN-2, R-TER-2, R-PRP-1, R-SEC-5.
- CA: saldo inicial e data travados depois de movimentação; uma conta padrão; credencial bancária nunca volta na API; condições com parcelas somando 100% e divisão com a diferença na última parcela (`IPaymentTerms.Split`).

**CAD-01a [ ] Terceiros (G)** — quebra da CAD-01 — [story](stories/CAD-01a-terceiros.md)
- Requisitos: R-TER-1, R-TER-2, R-PRP-1, R-PRP-3, R-SEC-5, PRD §6 (Desempenho, LGPD).
- CA: CNPJ (inclusive alfanumérico) e CPF validados; mesmo documento não duplica e oferece adicionar o papel, também em gravação simultânea; vários endereços e contatos; dados pessoais de PF mascarados na auditoria; lista com 50 mil registros em menos de 1 s.

**CAD-01b [ ] Consulta de CNPJ (M)** — quebra da CAD-01 — [story](stories/CAD-01b-consulta-cnpj.md)
- Requisitos: R-TER-1, R-PRP-3, R-PRP-7.
- CA: consulta preenche razão social e endereço e só grava com confirmação; cache; provedor fora do ar não trava o cadastro e conta para o alerta de integração; rate limit; requisição só com o CNPJ e para URL fixa.

**CAD-03 [ ] Locais de estoque, centros de trabalho e calendário (M)** — re-estimada — [story](stories/CAD-03-locais-centros-trabalho-calendario.md)
- Requisitos: R-EST-2, R-PLN-1, R-SEC-5, R-PRP-1.
- CA: local padrão único; turnos definem as horas por dia; feriados nacionais sugeridos e confirmados; capacidade diária por centro (`IWorkCenterCapacity`) usada nos testes de planejamento, com a fixture reaproveitada pela PLN-01.

**CAD-02a [ ] Itens, unidades e categorias (M)** — quebra da CAD-02 — [story](stories/CAD-02a-itens-unidades-categorias.md)
- Requisitos: R-EST-1, R-EST-8, R-PLT-3, R-PRJ-7, R-PRP-1, R-PRP-3, R-SEC-5.
- CA: seed de unidades; NCM obrigatório fora de Serviço; lote e série sugeridos pelo tipo; garantia padrão do tipo; item com movimento não pode trocar a unidade de estoque; lista com 50 mil itens em menos de 1 s.

**CAD-02b [ ] Conversões de unidade e códigos do fornecedor (M)** — quebra da CAD-02 — [story](stories/CAD-02b-conversoes-codigos-fornecedor.md)
- Requisitos: R-EST-3, R-FIS-5, R-PRP-3, R-SEC-5.
- CA: conversão kg → m² por item pelo `IUnitConversion` que os movimentos vão usar; sem conversão, erro claro (nunca fator 1); código do fornecedor único por fornecedor e só para terceiro com papel Fornecedor.

**CAD-04a [ ] Cargos, jornadas, sindicatos e custo-hora (M)** — quebra da CAD-04 — [story](stories/CAD-04a-cargos-jornadas-custo-hora.md)
- Requisitos: R-RH-2, R-RH-1, R-PRD-4, R-SEC-5, PRD §6 (LGPD).
- CA: custo-hora = salário × (1 + % encargos) ÷ horas mensais, ou ajustado à mão; recálculo idempotente quando o % de encargos muda; faixa salarial só com `hr.view_sensitive`.

**CAD-04b [ ] Funcionários, dependentes, histórico e afastamentos (G)** — quebra da CAD-04 — [story](stories/CAD-04b-funcionarios-dependentes-historico.md)
- Requisitos: R-RH-1, R-RH-3, R-SEC-5, R-SEC-10, PRD §6 (LGPD).
- CA: dados pessoais só com `hr.view_sensitive` (ADR 0003), cortados na resposta da API; leitura de dado sensível registrada; histórico salarial e de cargo gravado a cada alteração; afastamentos e desligamento sem apagar nada; vínculo com o usuário pelo `Identity.Contracts`.

**MIG-01a [ ] Mapeamento e infraestrutura de importação do Dolibarr (G)** — quebra da MIG-01 — [story](stories/MIG-01a-infra-importacao-dolibarr.md)
- Requisitos: R-TER-5, PRD §6 (Segurança, LGPD), R-SEC-5.
- CA: mapeamento das tabelas `llx_*` aceito pelo PO; leitura somente leitura com snapshot consistente e credencial só em secret; staging e chaves externas; reexecução não duplica; simulação sem gravação; relatório de importação (importados, atualizados, sem alteração, ignorados, conflitos, erros).
- Nota: primeiro mapear as tabelas do Dolibarr com o PO (P-07).

**MIG-01b [ ] Carga de terceiros, contatos, condições e contas bancárias (M)** — quebra da MIG-01 — [story](stories/MIG-01b-carga-terceiros-financeiro.md)
- Requisitos: R-TER-5, R-TER-1, R-TER-2, R-FIN-2.
- CA: `llx_societe` com o mesmo documento vira um terceiro com vários papéis; documento inválido vai para o relatório; saldo bancário na data de corte; os 118 terceiros contados no relatório.

**MIG-01c [ ] Carga de itens, categorias, unidades, códigos do fornecedor e locais (M)** — quebra da MIG-01 — [story](stories/MIG-01c-carga-itens-locais.md)
- Requisitos: R-TER-5, R-EST-1, R-EST-2, R-EST-3, R-PRJ-7.
- CA: de-para de unidades e tipos; `elev_garantia` vira a garantia do item (P-11); códigos do fornecedor de `llx_product_fournisseur_price`; os 34 produtos contados no relatório.

**MIG-01d [ ] Saldos iniciais de estoque (P)** — quebra da MIG-01; depende do EST-01 — [story](stories/MIG-01d-saldos-iniciais-estoque.md)
- Requisitos: R-TER-5, R-EST-4, R-EST-5, R-EST-6, R-EST-7, R-EST-8, R-PRP-7.
- CA: saldos de estoque entram como movimento "Saldo inicial" com o custo de partida, só com confirmação explícita; reexecução não duplica nem ajusta sozinha.

**MIG-01e [ ] Títulos em aberto e histórico de pagamentos (M)** — quebra da MIG-01; depende da FIN-01 e da FIN-02 — [story](stories/MIG-01e-titulos-abertos-historico.md)
- Requisitos: R-TER-5, R-FIN-3, R-FIN-4, R-FIN-5, R-TER-4, R-FIN-11, R-PRP-7.
- CA: faturas em aberto viram CR e CP com o valor pago parcial; pagos de cliente dos últimos 12 meses entram como histórico de pontualidade; nenhum título a pagar é baixado sem OP; totais por terceiro batem com o Dolibarr.

---

## Fase 2 — Comercial, Projetos e Financeiro núcleo

**COM-01 [ ] Orçamento (G)** — Itens, composição de custo por item, margem, preço, condições por marco, validade, copiar de anterior.
- CA: preço = custo × (1 + markup) ou custo / (1 − margem) conforme configuração; totais recalculados ao vivo; soma de marcos = 100%.

**COM-02 [ ] Revisões e validade (M)** — Nova revisão congela a anterior; job diário expira.
- CA: só a revisão mais recente é editável/aprovável; histórico comparável lado a lado.

**COM-03 [ ] PDF e envio do orçamento (M)** — QuestPDF com layout do tenant, e-mail ao contato.
- CA: PDF anexado ao orçamento e ao log de e-mail.

**COM-04 [ ] Aprovação → Contrato + Projeto + Recebíveis (G)** — Fluxo da seção 2 de 04-fluxos.
- CA: aprovar gera contrato com snapshot, projeto com itens, orçamento do projeto e recebíveis (marcos por data = Open; por evento = Planned); tudo na mesma transação lógica (outbox); anexar contrato assinado.

**COM-05 [ ] Projeto 360 (M)** — Tela do projeto com abas: itens/OS, financeiro, custos, documentos, linha do tempo.

**COM-06 [ ] OS de Instalação (M)** — Criar a partir do projeto, executor terceirizado, etapas e checklist de entrega técnica; valor gera CP do instalador vinculado ao projeto.
- CA: aceite técnico dispara evento que libera o recebível vinculado.

**COM-07 [ ] Aditivo de contrato (M)** — D. Ajusta orçado e recebíveis futuros.

**COM-08 [ ] Funil comercial (P)** — D. Lista por status, conversão, motivos de perda.

**FIN-01 [ ] Contas a receber (M)** — CRUD, parcelas, baixa total/parcial, juros/multa/desconto.
- CA: baixa cria transação na conta; título vencido aparece como Overdue.

**FIN-02 [ ] Contas a pagar e recorrentes (M)** — CRUD, origens, despesas recorrentes por job mensal.
- CA: recorrente gera título no dia configurado sem duplicar.

**FIN-03 [ ] Ordem de pagamento e alçadas (G)** — Criar OP com 1+ títulos, regras de alçada, aprovação em tela e por link no e-mail (exige login), multi-nível, notificação.
- CA: pagamento sem OP aprovada é bloqueado na API; solicitante não aprova a própria OP; reprovação exige comentário; auditoria completa.

**FIN-04 [ ] Movimentação bancária e transferências (P)** — Extrato interno por conta, saldo.

**FIN-05 [ ] Fluxo de caixa base (M)** — Realizado + projetado, diário/semanal/mensal, por conta ou consolidado.
- CA: valores conferem com a soma dos títulos/recorrentes (teste com dataset fixo).

**FIN-06 [ ] Inadimplência e aging (P)** — Por cliente e projeto.

---

## Fase 3 — Engenharia, Estoque, Compras e Produção

**ENG-01 [ ] Templates de roteiro (M)** — CRUD de templates e etapas (centro, horas padrão, checklist, qualidade).

**ENG-02 [ ] BOM da OS (M)** — Criar na etapa Projeto, copiar de outra OS/item, liberar, revisar.
- CA: liberar dispara reservas; revisão ajusta reservas e mantém histórico.

**EST-01 [ ] Saldos e movimentos (G)** — Entrada manual, saída, transferência, ajuste com motivo/aprovação; custo médio; negativo bloqueado.
- CA: movimento imutável; estorno gera movimento inverso; custo médio confere com cálculo de referência (testes).

**EST-02 [ ] Reservas e disponível (M)** — Reserva por linha de BOM; disponível = físico − reservado em todas as telas.
- CA: tentativa de reservar acima do disponível reserva o possível e gera falta para compras.

**EST-03 [ ] Requisição de material para OS (M)** — Baixa das reservas, escolha de lote, apropriação ao projeto.
- CA: baixa registra lote; custo vai para o projeto; devolução reverte.

**EST-04 [ ] Lote e série + rastreabilidade (M)** — Consulta série → lotes de MP usados; lote → séries/projetos onde foi usado.

**EST-05 [ ] Inventário (M)** — Contagem por local, divergências, ajuste aprovado.

**EST-06 [ ] Capacidade de produzir (P)** — D.

**CMP-01 [ ] Solicitação de compra (M)** — Manual, por falta de BOM e por estoque mínimo (job diário).

**CMP-02 [ ] Cotação (M)** — Fornecedores, comparativo, seleção.

**CMP-03 [ ] Pedido de compra (M)** — Alçada, PDF, e-mail, previsão de CP.

**CMP-04 [ ] Recebimento manual (M)** — Conferência contra pedido, lote, local, entrada no estoque, confirmação do CP. (Recebimento por NF na Fase 5.)

**PRD-01 [ ] Geração de OS (M)** — A partir do item do projeto e template; etapas editáveis antes do início.

**PRD-02 [ ] Kanban (M)** — Colunas por etapa, cartões com projeto/cliente/prazo/atraso, filtros, tempo real.

**PRD-03 [ ] PWA do operador (G)** — Login rápido (PIN opcional após login completo no dispositivo), minha fila, iniciar/pausar (motivo)/finalizar, checklist da etapa, tolerância a queda de rede.
- CA: apontamento offline sincroniza ao reconectar sem duplicar; botões ≥ 56px; funciona em tablet Android de entrada.

**PRD-04 [ ] Custo de mão de obra (P)** — Apontamento fechado × custo-hora → custo da OS/projeto.

**PRD-05 [ ] Qualidade e NC (M)** — Checklist, aprovar/reprovar, NC com retorno a etapa escolhida.

**PRD-06 [ ] Conclusão e expedição (M)** — Série do produto acabado, entrada em estoque pelo custo da OS, romaneio.

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

**GOL-01 [ ] Go-live Konebras (M)** — Carga final do Dolibarr, treinamento, checklist de virada, acompanhamento da primeira semana.

---

## Fase 6 — RH e Patrimônio (fim do MVP)

**RH-01 [ ] Ponto básico (G)** — Marcação no PWA (geolocalização opcional), lançamento manual, espelho mensal, banco de horas simples, ajustes com aprovação, fechamento do período.

**RH-02 [ ] Folha lançada e holerite (G)** — Lançar/importar rubricas do período, gerar holerites PDF, envio por e-mail, fechamento gera CP de salários e encargos.
- CA: holerite com layout padrão (proventos, descontos, bases INSS/FGTS/IRRF, líquido).

**RH-03 [ ] Exportação para contadora (M)** — D, layout a definir.

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
