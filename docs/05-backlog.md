# 05 — Backlog por fase

Formato: `ID — Como <perfil>, quero <ação> para <benefício>` + critérios de aceite (CA).
Tamanho relativo: P (até 1 sessão de trabalho), M (2–3), G (4+). Marque `[x]` ao concluir.
Regra: não comece uma fase sem a anterior concluída, exceto quando indicado.

---

## Fase 0 — Fundação

**FND-01 [ ] Estrutura da solução (M)** — Criar a solução conforme CLAUDE.md (Host, BuildingBlocks, módulos vazios, Web, tests), Docker Compose local, CI de build/teste.
- CA: `dotnet test` e `pnpm build` passam no CI; teste de arquitetura (NetArchTest) impede módulo referenciar Infrastructure de outro módulo.

**FND-02 [ ] Catálogo e resolução de tenant (G)** — Banco `catalog`, `tenant_domains`, middleware por host (ADR 0004), `TenantDbContext` com conexão dinâmica, cache.
- CA: dois tenants de teste com dados isolados; requisição a host inexistente → 404; tenant suspenso → 403 com página amigável; teste de integração prova que um tenant não lê dados do outro.

**FND-03 [ ] Migrator multi-tenant e provisionamento (G)** — `TenantMigrator`, comando de provisionamento (cria banco, migra, semeia, cria proprietário, envia convite) e tela do super admin.
- CA: provisionar tenant novo em < 1 min; falha em uma etapa registra log e permite retomar; migrations aplicadas a todos os tenants no deploy com relatório.

**FND-04 [ ] Autenticação (G)** — Identity por tenant, login, refresh rotativo em cookie, recuperação de senha, bloqueio, convite de usuário.
- CA: access 15 min / refresh 7 dias; reuso de refresh revoga a família; 5 tentativas erradas bloqueiam 15 min; e-mail de recuperação expira em 1 h.

**FND-05 [ ] 2FA TOTP (M)** — Ativar com QR code, códigos de recuperação, exigência por tenant/perfil.
- CA: login com 2FA pede código; código de recuperação usável uma vez; proprietário obriga 2FA e usuários sem 2FA são levados à configuração no próximo login.

**FND-06 [ ] Catálogo de módulos e liberação por tenant (M)** — `ModuleCatalog` (seed com módulos, ações aplicáveis e significado de "aprovar"), `plan_modules`/`tenant_modules` no catálogo, tela do super admin para liberar módulos.
- CA: endpoint de módulo não liberado → 403 `module_not_released`; seed idempotente; liberar um módulo reflete no tenant sem deploy.

**FND-06b [ ] Grupos de usuários e permissões (G)** — Grupos (CRUD, duplicar, inativar), matriz módulo × ação com chave "habilitado" por módulo, grupos iniciais por seed, grupo Proprietário de sistema. Tela conforme protótipo (Acessos → Grupos).
- CA: habilitar módulo marca "ver"; desmarcar "ver" limpa o módulo; marcar outra ação marca "ver"; módulos não liberados aparecem desabilitados com "Não liberado"; Proprietário não editável e nunca sem membro; alterações auditadas.

**FND-06c [ ] Usuários, convite e permissão efetiva (M)** — Cadastro de usuário com vários grupos, convite por e-mail (escolhe senha, oferece 2FA), desativação, policy dinâmica `perm:{modulo}.{acao}`, cache de permissões, endpoint `/me/permissions`, `<Can>` no front.
- CA: usuário em dois grupos recebe a união; remover do grupo tira o acesso no próximo request; rota sem permissão → 403 `forbidden`; teste de integração cobrindo as três camadas.

**FND-06d [ ] Menu por permissão e tela de módulo não liberado (P)** — Dock monta os botões a partir de `/me/permissions`: não liberado = cadeado e tela padrão "Funcionalidade não liberada nessa versão do sistema."; sem permissão = oculto.
- CA: acessar a URL de um módulo bloqueado mostra a tela padrão, nunca uma tela quebrada ou em branco.

**FND-07 [ ] Building blocks (M)** — Result/ProblemDetails, paginação, numeração, auditoria (interceptor + tela de histórico por registro), soft delete, outbox + dispatcher, anexos, notificações.
- CA: alteração em entidade auditável grava diff; evento publicado na mesma transação do comando e processado por job; numeração sem duplicidade sob concorrência (teste com 50 chamadas paralelas).

**FND-08 [ ] E-mail (P)** — `IEmailSender` SMTP/SES, templates (Razor/Fluid), fila Hangfire, log de envio.
- CA: falha de envio faz retry 3×, depois marca falha visível no log.

**FND-09 [ ] Shell do front (G)** — Login, 2FA, layout conforme protótipo (`docs/prototipo/`): dock inferior por área, barra superior com marca, trilha, busca, seletor de tema (claro/escuro/automático salvo no perfil) e usuário; tokens de design; componentes base (chips de status, indicador de etapa, tabela padrão, painéis); cliente Orval.
- CA: telas comparadas com o protótipo nos dois temas e em 400px de largura; tabela padrão com filtro, ordenação e paginação server-side; estados de carregando, vazio e erro em todos os componentes de lista.

**FND-14 [ ] Ajuda contextual e Dica (M)** — Requisitos: AJD-1, AJD-2, AJD-3, AJD-4, AJD-6, AJD-7. Catálogo de ajudas em Markdown embutido no assembly, endpoints `/ajuda` (índice filtrado por permissão) e `/ajuda/{chave}`, painel lateral, abertura na primeira visita (registrada no perfil), componente `<Dica>`.
- CA: toda rota do front tem ajuda (o teste falha se faltar); a ajuda de um módulo sem permissão não aparece no índice; a primeira visita abre a ajuda uma vez por usuário e por tela, em qualquer dispositivo.

**FND-15 [ ] Pergunte ao BemAqui (M)** — Requisitos: AJD-5, AJD-8. Busca das seções relevantes, resposta da IA restrita aos trechos com citação obrigatória, fallback por regra, `IAiProvider` configurável na plataforma, registro de uso e custo por tenant.
- CA: a resposta sempre cita a seção usada; sem IA ou com citação inválida, devolve os trechos encontrados; teste prova que nenhum dado de negócio do tenant entra no prompt; chave de IA nunca aparece em log.

**FND-10 [ ] Deploy (M)** — Compose de produção em `/opt/projetos/bemaqui-empresas/` na VPS compartilhada, vhost `app.konebras.com.br` no `stack-nginx` + certbot (ADR 0004), Dolibarr movido para `adm.konebras.com.br`, CI/CD por SSH, backup diário por banco, health check.
- CA: push na main publica em homolog; tag publica em prod; restauração de backup de um tenant testada e documentada.

---

## Fase 1 — Cadastros e migração

**CAD-01 [ ] Terceiros (M)** — CRUD com papéis, endereços, contatos, consulta CNPJ.
- CA: CNPJ/CPF validados; consulta preenche razão social e endereço; mesmo documento não duplica (adiciona papel).

**CAD-02 [ ] Itens e unidades (M)** — Itens, categorias, unidades, conversões, NCM, lote/série, estoque mínimo.
- CA: conversão kg→m² por item aplicada nos movimentos; item com movimento não pode trocar a unidade de estoque.

**CAD-03 [ ] Locais de estoque e centros de trabalho (P)** — CRUD; calendário e feriados.
- CA: capacidade diária por centro usada nos testes de planejamento.

**CAD-04 [ ] Funcionários e cargos (M)** — Cadastro completo para folha (RH-1, RH-2, RH-3), custo-hora do cargo.
- CA: dados pessoais visíveis só com permissão `hr.view_sensitive` (ADR 0003); histórico salarial registrado a cada alteração.

**CAD-05 [ ] Financeiro base (M)** — Plano de contas padrão (seed), centros de custo, contas bancárias/caixa, condições de pagamento.
- CA: plano de contas em árvore; conta com lançamento não pode ser excluída, só inativada.

**CAD-06 [ ] Configurações da empresa (P)** — Dados do tenant, logo, numeração, %encargos, parâmetros.

**MIG-01 [ ] Migração do Dolibarr (G)** — Script/tela de importação: terceiros, produtos, saldos de estoque iniciais, títulos em aberto.
- CA: relatório de importação (importados, ignorados, erros); reexecução não duplica; saldos de estoque entram como movimento "Saldo inicial".
- Nota: primeiro mapear tabelas do Dolibarr (`llx_societe`, `llx_product`, `llx_product_stock`, `llx_facture`...) com o PO.

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
