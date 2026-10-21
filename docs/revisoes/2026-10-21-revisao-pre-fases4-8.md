# Revisão pré-Fases 4 a 8: relatório consolidado

- **Data:** 21/10/2026.
- **Objeto:** as stories das Fases 4 a 8 (Fase 4: 15; Fase 5: 18; Fase 6: 14 + CAD-04c2; Fase 7: 7; Fase 8: 5), escritas pelo SM e validadas pelo PO nas branches `docs/fase4-stories`, `docs/fase5-stories` e `docs/fase6-stories`, juntas na `docs/fases4-8-stories` sobre a Fase 3 decidida.
- **Revisores:** quatro, todos somente leitura, conferindo as stories contra o código das Fases 0 a 2, as decisões da Fase 3 (`2026-10-21-revisao-pre-fase3.md`) e o projeto `D:\GIT\EmissorFiscal`:
  - AIOX architect;
  - AIOX po + pm;
  - design-squad, com `audit-design`;
  - cybersecurity, com `audit-app-security` (threat model STRIDE, conferido com o código).
- **Como usar:** a seção 1 lista o que depende do PO; a seção 2, o que depende de terceiros (perguntas prontas para levar); a seção 3, os pontos em aberto de cada fase com a recomendação consolidada. As seções 4 a 6 entram nas stories e nas ADRs depois da decisão, sem nova consulta. Nada foi aplicado ainda.
- **Achado transversal:** as Fases 4 a 8 foram escritas sobre os rascunhos da Fase 3 e citam ADRs só até a 0022. Vários pontos que elas tratam como abertos já foram decididos em 21/10 (D2, D4, D6, D8, D12, D14 e os pontos 6, 28, 38 a 53, 62, 65, 67, 76, 78, 79 da Fase 3), e algumas stories contradizem essas decisões (a NF-e ainda baixa estoque; a qualidade ainda usa `production.approve`; o recebimento por NF ainda usa evento e `purchasing.create`). A correção é mecânica e entra na seção 4, sem nova decisão.

## 0. Vereditos

| Fase | GO ou GO com ajustes | Ajustar antes de `Ready` | Bloqueadas (NO-GO) até a decisão |
|---|---|---|---|
| 4 | FIN-07, DSH-04, PLN-01b, PLN-02, PLN-03, PLN-04, PLN-05, DSH-03 | DSH-02 (fonte do comprometido e impostos), FIN-08 (receita, sinal no rótulo), DSH-01a e DSH-01b (módulo do painel, cache, componentes), PLN-01a (datas fora do documento), PNL-01a e PNL-01b (TV legível, sessão) | — |
| 5 | FND-16, BNK-01a, BNK-01b, BNK-02, BNK-03a, BNK-03b, FIS-02a, FIS-05, FIN-09, GOL-01b | FIS-02b (conteúdo da contadora), FIS-03b (D4), FIS-04a (assinatura do XML), GOL-01a (critérios de entrada) | **FIS-01** (premissa falsa: o EmissorFiscal não reconcilia nota pendente), **FIS-03a** (reconciliação, D4, venda mista, lacunas do Emissor), **FIS-04b** (contradiz a ADR 0024), **FIS-06** (sem provider de Caçapava; depois do go-live) |
| 6 | CAD-04c2 (com os CAs da conta), RH-01b, RH-01c, RH-02a, RH-03, PAT-01a, PAT-01b, PAT-02, PAT-03a, PAT-03b, LGPD-01b | RH-01a (uso legal do ponto, autoatendimento), LGPD-01a (identidade do titular) | **RH-02c** (favorecido do título de salário, ADR), **RH-02b** (proteção do holerite) |
| 7 | FLH-02b, FLH-04, FLH-05, FLH-06 | FLH-01, FLH-02a, FLH-03 | Nenhuma fica `Ready` antes dos dados da contadora |
| 8 | POR-01b, POR-01c, POR-02 | — | **POR-01a** (ADR do portal), **WPP-01** (provedor, opt-in, troca de número) |

## 1. Decisões do PO

| # | Decisão | Recomendação consolidada | Origem |
|---|---|---|---|
| D1 | **Escopo do go-live e ordem das fases.** A GOL-01a exige "Fases 0 a 4 concluídas", e a Fase 4 tem 15 stories (5 G) que não substituem nada do Dolibarr. | **Go-live = Fase 3 + FND-16, BNK-01a a BNK-03b, FIS-01 a FIS-03b, GOL-01a e GOL-01b.** FIN-07 e DSH-02 entram antes, porque não dependem de ninguém de fora e ocupam a espera. O resto da Fase 4, FIS-04a/04b, FIS-05, FIS-06 e FIN-09 vêm depois do go-live. Exceção formal à regra "não comece uma fase sem a anterior" (ponto 78 da Fase 5). **Fase 8 antes da Fase 7**; a Fase 7 só entra se a Konebras quiser internalizar a folha. Ordem completa na seção 6. | Produto C2/A7 |
| D2 | **Trilha externa do go-live, com dono e data.** P-01, **P-02** (sem CNAE de fabricação não há NF-e de produção própria; falta no CA 1 da GOL-01a), P-03, certificado A1, conta Inter com API (P-08) e o EmissorFiscal implantado, que ainda não autorizou nenhuma nota real. | Abrir já, em paralelo ao fim da Fase 3, com dono e data para cada item (seção 2). O EmissorFiscal entra em homologação agora (tenant `demo`); produção só na GOL-01b. | Produto C1 |
| D3 | **Lacunas do EmissorFiscal** (trabalho no repositório dele, planejar já). | Antes da FIS-03a: **job de reconciliação com a SEFAZ e consulta forçada** (hoje nota em `Processing`, `ContingencyPending` ou duplicidade 204 fica parada sem webhook); **DANFE em PDF** no Emissor; grupo `transp` e volumes; IBS/CBS (P-03); `GET /me` e escopo `nfe:eventos` (hoje cancelar e inutilizar exigem só `nfe:emitir`); timestamp no HMAC do webhook; senha do PFX em `[FromForm]`; CNPJ do certificado conferido sem aceitar falha; **rede dedicada** (`bqe-fiscal`; hoje API, banco e painel estão na `stack_stack` em HTTP, junto com o Dolibarr), banco sem superusuário, Swagger desligado; um tenant do Emissor por tenant do BemAqui, com chave sem escopo `admin`; cópia offline da chave-mestra do cofre. Depois do go-live: provider da NFS-e de Caçapava (FIS-06). ADR 0027. | Arquitetura C1/M9, Segurança |
| D4 | **Venda mista e receita** (a contadora decide antes do Marco 5.4). Muda o DRE, o "a faturar" e o card de faturamento. | Proposta para a contadora: o equipamento sai em NF-e na expedição; a instalação, em NFS-e no aceite técnico; a parcela do contrato é só cobrança (boleto), nunca faturamento; a receita do DRE é a data da nota; o sinal fica como adiantamento até a nota. A divisão vem do tipo do item do orçamento (`Service` já existe). | Produto C3 |
| D5 | **NFS-e no go-live.** É o CNAE principal, mas o provider de Caçapava não existe no Emissor. | Não bloqueia o go-live se a contadora confirmar o processo provisório (portal da prefeitura). FIS-06 é a primeira story fiscal depois do go-live; o provider de Caçapava entra no Emissor em paralelo. Até lá o projetado usa o ISS do orçado. | Produto A3, Arquitetura |
| D6 | **P-19 (2FA a cada ação) e P-20 (estorno com aprovação).** Foram adiadas "até o fim do planejamento atual", que termina aqui; o go-live põe dinheiro real no Inter. | Decidir antes da GOL-01a e pôr nos critérios de entrada: **2FA de novo** em pagar, estornar, cancelar ou inutilizar NF-e, trocar conta de salário, fechar a folha, anonimizar titular e salvar credenciais do Inter; **estorno de pagamento e de baixa bancária com `finance.approve`**. | Produto A4, Segurança M8 |
| D7 | **Critérios de entrada e checklist da GOL-01a/01b.** | Acrescentar: P-02; P-19 e P-20; contagem física do estoque com saldo inicial na janela de implantação (D7 da Fase 3); usuários do chão de fábrica (P-24); folha como CP recorrente até a RH-02c; **restauração e rollback ensaiados numa cópia, obrigatórios** (ponto 75); **ambiente de produção novo** (catálogo novo só com o `konebras`, key ring e certificado do Data Protection novos, conexão cifrada de novo; homologação com key ring novo e o `konebras` apagado depois do fechamento) — ADR 0034; **revogar todas as famílias de refresh e subir o security stamp de todos** (o refresh hoje não fica preso ao host, então o CA 3 da GOL-01b está errado), expirar convites, redefinições, PINs, aparelhos e quiosques; apagar usuários e convites de teste e conferir os super admins; trocar webhooks e Inter de sandbox para produção; **regenerar a `DOLAPIKEY`** no Dolibarr (é a mesma do Konebras UI) e desativar os usuários dela; Dolibarr em `adm.` só leitura, com allowlist de IP ou autenticação básica; `DROP USER` com revogação na rede; inventário de segredos completo (SSH, B2, `age`, GHCR, `ALTER ROLE`, environment protection no `deploy-prod.yml`); linha de base da métrica "desvio da data prevista ≤ 15%". Virada no início do mês, fora do horário comercial. | Produto A5/M4, Segurança C3/A18, Arquitetura |
| D8 | **Custo e imposto no projeto (adendo à ADR 0025).** A DSH-02 soma o comprometido duas vezes (títulos previstos e pedidos) e realiza imposto por guia, que é da empresa, não do projeto. | Fonte única nos títulos do Finance: comprometido de material = max(0, Σ títulos de compra de item estocável do projeto − Σ baixas líquidas para a OS). Imposto realizado só pela nota autorizada (fontes `InvoiceTax` e `InvoiceTaxReversal` na razão, com data de corte); até lá, o projetado usa o orçado. | Arquitetura A1/A3, Produto A8 |
| D9 | **Painel do dono: onde mora e o que é a página inicial.** | Módulo fino `Dashboard` (já no `ModuleCatalog`), só de composição: Application e Endpoints, referenciando apenas `*.Contracts`, sem schema; `revenue_targets` no Finance; o `Reporting` da ADR 0006 é revogado (ADR 0028). Cache de 60 s com chave `{tenant}:{usuário}:{versão de permissão}`, contador de geração por tenant e botão "Atualizar". **Início com a Minha pauta é a página inicial de todos**; o Proprietário com `dashboard.view` cai em `/app/painel`, cujo card "Aguardando sua decisão" é a pauta filtrada. "Resultado gerencial por projeto" sai do DRE e vai para o `Dashboard` (não fecha com o DRE mensal). | Arquitetura M1/M2, Design 17, Segurança |
| D10 | **Planejamento.** O recálculo de hora em hora reescreveria OS e projetos (versão muda, 409 contínuo, auditoria cheia). | Datas previstas só em tabelas do plano (`production.schedule_step_forecasts`, `schedule_allocations`, `commercial.project_forecasts`), um único escritor, fora da auditoria; a fila de prioridade é do Production (`production.planning_queue`); `commercial.projects.priority` fica sem uso, sem apagar a coluna. Reordenar a fila com `production.edit`, auditado. Ordem pela data prometida, depois a assinatura. Capacidade por centro (férias não reduzem); manutenção vira parada no `IWorkCenterCapacity`. ADR 0029. | Arquitetura C3/M5, Produto M2 |
| D11 | **Painel da TV.** | `/tv#token` trocado por cookie `__Host-bq_kiosk` (o fragmento não vai ao servidor; `replaceState`, `no-referrer`); sessão de 30 dias sem uso e 180 dias absolutos; `kiosk.delete` no catálogo e criar exige também `production.view`; aviso ao Proprietário; **leitura a distância**: mínimo de 32 px nos títulos, 28 px nas linhas e 48 px em contagens e relógio, cartão de 4 linhas sem o produto, até 3 cartões por coluna com "+{n} OS"; operador pelo primeiro nome e inicial; **cliente nunca aparece**, só o nome curto do projeto; prévia no backoffice (`/app/painel-tv/$id/previa`). ADR 0030. | Design C3/10, Segurança, Arquitetura M4 |
| D12 | **Fiscal: certificado, cancelamento e entrada.** | A1 **só no cofre do EmissorFiscal** (o BemAqui guarda só a validade). Cancelar e inutilizar com a ação `fiscal.approve`, nova no catálogo e reservada ao Proprietário (ADR 0008), em uma etapa, com "Pedir cancelamento" para os demais e 2FA (D6). `fiscal.import_incoming` para o Almoxarifado importar sem poder emitir. NF-e só referencia o romaneio (D4 da Fase 3); cancelar a nota não mexe no estoque. Entrada: recusar XML sem protocolo; exatamente um `infNFe` e uma assinatura, `Reference URI` igual ao `Id`, CNPJ do certificado igual ao do emitente; duplicatas pelo `CreateOpenInstallmentsAsync` na transação do recebimento (ADR 0024), com `purchasing.receive`. Série exclusiva obrigatória. Parser de PDF isolado; OCR fora do MVP. | Segurança, Arquitetura C2/A2, Produto |
| D13 | **Banco Inter.** | Webhook com token `?t=` na URL (o nginx já registra só `$uri`), reconsulta ao Inter por mTLS antes de qualquer baixa, deduplicação também pelo SHA-256 do corpo, `tenantKey` opaco. Credencial só com escopos de cobrança, extrato e webhook (recusar pagamento e envio de Pix). Pagamento com diferença vai para conferência humana. Desconto, multa e juros acima do limite da D8 exigem `finance.approve`. **OFX bloqueado em conta com API**, liberável por conta com `finance.approve` como contingência; linhas de OFX marcadas "não verificadas pelo banco". Conciliação: quem confirma não é quem importou; fechamento por período com `finance.approve`. Integração por conta. ADRs 0031 (webhooks, com adendo à 0007) e 0032 (Inter). | Segurança, Arquitetura, Produto |
| D14 | **Folha no financeiro e conta de salário** (os revisores divergem no favorecido). | Fechamento chama o `IPayables` na própria transação (padrão da ADR 0024). Favorecido: **um título "Folha {mês}" mais um por guia**, com o detalhe por funcionário no HR e o retrato das contas gravado no fechamento (menos dado pessoal no Finance; recomendação do produto). Alternativa do arquiteto: `payee_type`/`payee_id` no título, com os dados bancários lidos de um contrato sensível do HR só no pagamento. **Conta de salário:** trocar exige `hr.view_sensitive`, 2FA (D6) e segunda confirmação quando o titular ou a chave Pix não batem com o CPF; aviso ao funcionário no e-mail pessoal e ao Proprietário; **quarentena de 30 dias** que tira a conta nova do pagamento automático e exige revisão expressa. Quem fecha a folha (`hr.approve`) não paga (`finance.pay`). ADR 0036. | Segurança C2, Arquitetura A4, Produto M7 |
| D15 | **Ponto e autoatendimento do funcionário.** | O uso legal do ponto do app (CLT art. 74 §2, Portaria 671) é **pré-condição do Marco 6.1** (jurídico e contadora). Por isso a Fase 6 começa pela folha: CAD-04c2 → RH-02a → RH-02c, e o ponto depois. "Meu ponto" em rotas `/api/v1/me/hr/*`, liberadas pela camada 1 (módulo liberado) e pelo vínculo com funcionário ativo, sem ação no grupo (ADR 0035, com adendo à 0026); marcar no tablet encerra a sessão por PIN no servidor. Geolocalização desligada por padrão, com ciência registrada. Holerite: **"Meus holerites" autenticado é o canal**, e o e-mail só avisa, sem anexo. | Produto A6/M5, Arquitetura, Design 21, Segurança |
| D16 | **Portal do cliente** (os revisores divergem no host e no 2FA). | ADR 0040 antes da POR-01a: chave de assinatura própria, claim de tipo de token e validação estrita nos dois esquemas (hoje o `ValidateAudience` é falso e só confere o host), teste "token de um lado dá 401 no outro". **Host próprio** para o portal (o cookie `__Host-` com `Path=/` levaria o refresh do backoffice ao portal; recomendação da segurança contra a do arquiteto, `/portal` no mesmo host). 2FA opcional para ver; **código por e-mail obrigatório no aceite de orçamento**. Aceite só avisa o Comercial (mantém a D5 da Fase 2), com versão, hash do PDF congelado no envio, IP, user-agent e recibo; revisão nova invalida o aceite; sem link anônimo. Um contato vê um cliente só. Kit Portal com vocabulário leigo. | Segurança A15/A16, Arquitetura A5, Design 7/8 |
| D17 | **WhatsApp.** | API oficial da Meta por um parceiro (BSP), custo do tenant, atrás do `IWhatsAppSender`; opt-in pelo próprio contato e **trocar o número derruba o opt-in**; mensagem só com o aviso e o link do portal (sem valor, CPF ou link de boleto); dias úteis, das 8 h às 18 h; FIN-09 antes. ADR 0041. | Segurança, Produto, Design |
| D18 | **Componentes e protótipo antes dos marcos.** O protótipo não cobre Fiscal, Banco, RH, Patrimônio nem Portal, e faltam componentes. | Antes do 4.2: `ChartFrame`, `ColumnChart`, `Kpi`/`DashboardCard`, `TreeTable` com coluna fixa e navegação por célula, troca do `--chart-3` (hoje âmbar). Antes do 4.3: Gantt próprio em SVG, sem biblioteca. Antes do 4.4: kit TV. Antes do 5.4: protótipo de nota de saída (rascunho, emissão, rejeitada) e conciliação; `ConfirmDialog` com confirmação digitada, prazo e impacto em lista; `EnvironmentBanner` único. Antes da Fase 8: protótipo e kit Portal. | Design C1/C2 |
| D19 | **Reforço no código que já está no ar** (homologação com dado real). | PR pequeno já: `KnownProxies` só com o IP do `stack-nginx` (hoje a sub-rede inteira da `stack_stack`, inclusive o Dolibarr, pode forjar IP e host); apagar o modelo do e-mail de convite e de redefinição depois do envio (hoje o token fica em claro em `EmailLog.Model`); teste de arquitetura que reprova endpoint sem `RequirePermission`/`RequireModule` fora de uma lista versionada de rotas anônimas; idempotência com o usuário no escopo (D17 c da Fase 3) e expurgo de 30 dias da `idempotency_records`. | Segurança (código), Arquitetura M7 |

## 2. O que levar a cada parte (perguntas objetivas)

### Contadora
1. **P-01:** o regime é Lucro Presumido desde 01/01/2026? ICMS e IPI na entrada são recuperáveis? PIS/COFINS cumulativo?
2. **P-02:** quando sai o cartão CNPJ com o CNAE de fabricação? Até lá, com qual CFOP faturar o elevador?
3. **P-03:** IBS/CBS são obrigatórios em 2026 na NF-e e na NFS-e da Konebras? Quais CST e `cClassTrib`?
4. **Venda mista (D4):** como dividir equipamento e instalação; remessa para obra mais venda, ou só a venda; nota pelo total na expedição; duplicatas na nota; sinal como adiantamento.
5. Os 3 cenários de homologação da NF-e (venda a contribuinte em SP, a não contribuinte em SP, peça ou outro estado), com CFOP, CST/CSOSN, alíquotas e NCM, e a conferência dos XML.
6. Série exclusiva da NF-e e numeração do emissor atual.
7. **NFS-e de Caçapava:** item da LC 116 (14.06 ou 7.02?), código municipal, NBS, ISS no local da prestação, retenções. Quem emite até a FIS-06 (processo provisório)?
8. Grupo `transp` e volumes; prazo de cancelamento em SP (24 h?); devolução.
9. **DRE:** receita pela data da nota; contas de juros, multa e desconto; compra para estoque na conta 2.1 pela data do documento; quais tributos entram no custo do projeto (ICMS, PIS, COFINS, ISS, IBS/CBS; IPI fora?).
10. Multa e juros padrão do boleto (proposta 2% e 1% a.m.); confirmar os 80% de encargos (P-05); conta e natureza do título de compra (P-17).
11. **Fase 6:** tolerância de marcação, banco de horas, período de apuração, tipos de abono; rubricas e códigos, tipos de folha, guias e vencimentos; layouts de importação e exportação; conta da folha; máscara do CPF. **Ela já envia holerite hoje?**
12. Tabela de retenção de documentos (LGPD, com o jurídico) e contas da manutenção.
13. **Fase 7 (só se a Konebras quiser internalizar a folha):** tabelas e casos de teste, regras de férias, 13º e rescisão, critério de aceite da virada e quem assina.

### Banco Inter
1. Data da conta PJ com API (P-08), os escopos (só cobrança, extrato e webhook) e as credenciais de sandbox e de produção.
2. Formato do certificado (`.crt`/`.key` ou `.pfx`), validade e renovação.
3. Webhook: aceita query string na URL? Política de reenvio; id único do aviso; mTLS e IPs de origem.
4. Limite do "seu número"; multa, juros e desconto no bolepix; vencimento em dia não útil; regras de baixa e cancelamento.
5. Tarifas e limites de chamadas; extrato: período por consulta, id por lançamento, horário de disponibilização, OFX da mesma conta.
6. Pagamento de salários e Pix em lote pela API (para a D14).
7. É possível testar a credencial de produção antes do go-live?

### Jurídico
1. **Ponto do app como registro oficial** (CLT art. 74 §2; Portaria 671: REP-P registrado ou REP-A por acordo coletivo) e assinatura do espelho. É pré-condição do Marco 6.1 (D15).
2. Geolocalização no ponto; acordo de banco de horas.
3. Holerite: aviso por e-mail com acesso autenticado basta?
4. LGPD do titular: prazo de resposta (proposta 15 dias), dados de terceiros (dependentes), eliminação por anonimização, retenção (com a contadora).
5. Valor do aceite online de orçamento (código por e-mail, IP, hash do PDF; Lei 14.063).
6. Opt-in do WhatsApp para cobrança.
7. Matriz de rescisão (com a contadora).
8. Itens menores: nome do operador na TV; retenção de 90 dias do corpo dos webhooks.

### Konebras (operação)
1. Data e janela da virada; treinamento; usuários com convite e 2FA; e-mails de apelido para os operadores **e para todo funcionário que marcar ponto**.
2. Compra do certificado A1 (e-CNPJ) e responsável; contagem física do estoque antes do go-live.
3. Como emite a NFS-e hoje; vendas para fora de SP a não contribuinte (DIFAL).
4. Prazo padrão de instalação (proposta 5 dias úteis); quem recebe o aviso de risco; quem reordena a fila.
5. Política de multa, juros e desconto; envio automático do boleto; passos da régua (proposta −3, 0, +3, +10 dias); aviso aos clientes sobre o boleto novo.
6. Dolibarr: perfis só leitura e data de desligamento (proposta: depois do primeiro fechamento, em até 3 meses; backup por 5 anos); extratos do Inter para montar as regras de conciliação.
7. Pessoas: número de funcionários; método atual de ponto; sindicato e convenção; férias coletivas; quer internalizar a folha (Fase 7)?
8. TV e visões do painel; impressora de etiquetas; máquinas com plano preventivo e horímetro.
9. Metas de faturamento; interesse dos clientes no portal; provedor e custo do WhatsApp.
10. Numeração de série que já usa nos elevadores (campos `elev_*` do Dolibarr; pendência da Fase 3).

## 3. Pontos em aberto das stories

Numeração das seções de cada fase em `docs/stories/README.md`. **B** = bloqueia o `Ready`. **Fase 3** = já decidido em 21/10, só atualizar a story. Quem decide: PO, Arq (arquiteto), Cont (contadora), Inter, Jur (jurídico), Kon (operação da Konebras).

### 3.1 Fase 4
| # | Story | Recomendação consolidada | Quem |
|---|---|---|---|
| 1 | FIN-07 | (B) % de inadimplência = vencido há mais de 30 dias ÷ faturado em 12 meses (o atraso já desloca a data; evita oscilar com poucos títulos grandes) | PO |
| 2 | FIN-07 | Limite de 20%, como proposto | PO |
| 3 | FIN-07 | Dias corridos | PO |
| 4 | FIN-07 | (B) Vencido em aberto no ajustado em max(hoje, vencimento + atraso médio) | PO |
| 5 | DSH-02 | Fase 3 (D2) e D8: fonte única nos títulos, sem o `IPurchaseOrders` | — |
| 6 | DSH-02 | Fase 3 (D2): título previsto vai para "Comprometido" | — |
| 7 | DSH-02 | Imposto realizado só pela nota; até lá, o projetado usa o orçado (D8) | PO/Cont |
| 8 | DSH-02 | Margem do consolidado ponderada pela receita | PO |
| 9 | DSH-02 | Fase 3 (item 78): custo da NC informativo | — |
| 10 | FIN-08 | (B) Receita pela data da nota; antes do go-live, `issue_date` do título com rótulo "provisório"; sinal como adiantamento (D4) | Cont+PO |
| 11 | FIN-08 | Contas de juros, multa e desconto do CA 6 | Cont |
| 12 | FIN-08 | (B) "Resultado gerencial por projeto" vai para o `Dashboard`, com a nota de que não fecha com o DRE mensal (D9) | PO |
| 13 | FIN-08 | Sem fechamento de período; aviso "Mudou desde a última consulta" | PO |
| 14 | FIN-08 | (B) Compra para estoque na conta 2.1 pela data do documento; `<Dica>` explicando a diferença do resultado por projeto (por consumo) | Cont |
| 15 | DSH-04 | (B) Ordem da pauta: o que destrava outra pessoa ou dinheiro, depois o vencimento | PO |
| 16 | DSH-04 | Categorias e limites como propostos | PO |
| 17 | DSH-04 | (B) Pauta no Início, sem item no dock (D9) | PO |
| 18 | DSH-01a | (B) Módulo `Dashboard` só de composição; ADR 0028 (D9) | Arq |
| 19 | DSH-01a | (B) Meta de faturamento mensal por ano, editada com `settings.edit` | PO |
| 20 | DSH-01a | (B) Faturamento antes do go-live = títulos emitidos com o rótulo "provisório"; depois, as notas (D4) | PO/Cont |
| 21 | DSH-01a | Cache de 60 s, contador de geração e "Atualizar" (D9) | PO |
| 22 | DSH-01a | Primeira tela: Início; Proprietário com `dashboard.view` em `/app/painel` (D9) | PO |
| 23 | DSH-01b | (B) Carteira como proposta; "a faturar" = contratado − notas depois do go-live | PO |
| 24 | DSH-01b | (B) "Margem projetada (com custos reais)" nos em andamento e realizada nos encerrados, com a orçada como contexto | PO |
| 25 | PLN-01a | Ordem antes da fila: data prometida, depois a assinatura (D10) | PO |
| 26 | PLN-01a | (B) Etapa em andamento primeiro, como proposto | PO |
| 27 | PLN-01a | (B) Etapa seguinte no dia útil seguinte; recalibrar depois de 1 mês de dados | PO |
| 28 | PLN-01a | Retrabalho com as horas padrão inteiras | PO |
| 29 | PLN-01a | (B) Datas fora do documento, em tabelas do plano com `schedule_runs` (D10, ADR 0029) | Arq |
| 30 | PLN-01a | Fase 3 (D14) | — |
| 31 | PLN-01b | (B) Prazo de instalação na data prevista como o CA 5 (casos a, b e c) | PO |
| 32 | PLN-01b | (B) Prazo padrão de 5 dias úteis, a confirmar | Kon |
| 33 | PLN-01b | Até 10 s, com o pedido de recálculo agrupado (1 rodando, 1 pendente) | PO |
| 34 | PLN-01b | Aviso ao responsável e a `production.approve`, sem tolerância | PO |
| 35 | PLN-01b | Fase 3 (item 62), substituída aqui | — |
| 36 | PLN-02 | (B) Fila no Production; `priority` do projeto sem uso, sem apagar (D10) | PO/Arq |
| 37 | PLN-02 | (B) Reordenar com `production.edit`, auditado (D10) | PO |
| 38 | PLN-02 | Projeto novo entra pela data prometida | PO |
| 39 | PLN-02 | Fase 3 (item 28) | — |
| 40 | PLN-04 | (B) Ocupação até 100% no plano finito, com o quadro "Depois de"; crítico "Acima da capacidade" só quando a capacidade cair depois do plano | PO |
| 41 | PLN-04 | < 60% "Folga", 60–95% sem destaque, ≥ 95% "Lotado" | PO |
| 42 | PLN-03 | Gantt próprio em SVG, sem biblioteca nova (D18) | Arq |
| 43 | PLN-03 | OS de instalação como barra só de leitura | PO |
| 44 | PNL-01a | (B) `/tv#token` + cookie (D11) | PO/Arq |
| 45 | PNL-01a | (B) `kiosk.delete` no catálogo; criar exige `production.view` (D11) | PO |
| 46 | PNL-01a | 30 dias sem uso e 180 dias absolutos (D11) | PO |
| 47 | PNL-01a | Corrigir o `02-arquitetura` §2.1 e §4 junto | Arq |
| 48 | PNL-01b | (B) Primeiro nome e inicial do operador | PO |
| 49 | PNL-01b | (B) Cliente nunca aparece; só o nome curto do projeto (D11) | PO |
| 50 | PNL-01b | As 3 visões, pulando "Atrasos" quando vazia | PO/Kon |
| 51 | DSH-03 | Horas paradas e taxa de NC bastam no MVP | PO |
| 52 | DSH-03 | Faltas que bloqueiam nos próximos 5 dias úteis | PO |
| 53 | DSH-03 | Situação da compra pelo contrato do Purchasing, só leitura; "Gerar solicitação" com `purchasing.create` | PO |
| 54 | PLN-05 | Etapas sem roteiro na ordem das linhas do orçamento | PO |
| 55 | PLN-05 | (B) Simular com `quotes.view`, só datas | PO |
| 56 | PLN-05 | Fase 3 (item 6) | — |

### 3.2 Fase 5
| # | Story | Recomendação consolidada | Quem |
|---|---|---|---|
| 1 | FND-16 | Sem mTLS no nginx: token `?t=` (só hash guardado) e reconsulta por mTLS antes da baixa (D13) | Arq/Inter |
| 2 | FND-16 | (B) Host da plataforma, `/webhooks/*` fora da allowlist de IP e fora do middleware de tenant por host; o EmissorFiscal entra pela rede interna | PO |
| 3 | FND-16 | Rotação do segredo com 24 h de sobreposição e alerta de uso do antigo | PO |
| 4 | FND-16 | Corpo guardado 90 dias, nunca além do necessário | PO/Jur |
| 5 | FND-16 | (B) Adendo à ADR 0007: `WebhookSecret`, `FiscalApiKey`, `WhatsAppCredentials` | Arq |
| 6 | FIS-01 | (B) O PO implanta o EmissorFiscal já, em homologação; produção na GOL (D2) | PO |
| 7 | FIS-01 | (B) A1 só no cofre do EmissorFiscal; ajustar a R-FIS-2 (D12) | PO/Arq |
| 8 | FIS-01 | (B) Homologação SEFAZ no tenant `demo`; nada no `konebras` antes da GOL-01b | PO |
| 9 | FIS-01 | (B) IBS/CBS: assumir obrigatório e priorizar no Emissor (P-03) | Cont+PO |
| 10 | FIS-01 | (B) DANFE em PDF no EmissorFiscal; reserva: `IPdfRenderer` a partir do XML (fecha o XSS da DANFE em HTML) | PO/Arq |
| 11 | FIS-01 | `transp` e volumes antes do go-live; duplicatas conforme a contadora | Cont |
| 12 | FIS-01 | (B) `nfe-{invoiceId}-{tentativa}`, a tentativa muda só depois de uma rejeição; hash do payload gravado em `Processing` | Arq |
| 13 | BNK-01a | (B) Aceitar `.crt`/`.key` e `.pfx` | Inter |
| 14 | BNK-01a | Integração por conta | PO |
| 15 | BNK-01a | (B) Sem produção antes da GOL; teste de conexão de produção no D−1 | PO |
| 16 | BNK-01a | Alerta de vencimento ao Proprietário, como proposto | PO |
| 17 | BNK-01b | (B) Multa 2% e juros 1% a.m. no seed, editáveis | Kon+Cont |
| 18 | BNK-01b | "Seu número" curto e opaco (sequência por conta em base 36), conferido no sandbox | Inter |
| 19 | BNK-01b | (B) Vencido e dia não útil como proposto | PO |
| 20 | BNK-01b | Envio automático marcado por padrão, com o e-mail visível no diálogo | Kon |
| 21 | BNK-01b | 200 e-mails por dia | PO |
| 22 | BNK-01b | (B) Desconto, multa e juros contam no limite da D8; acima, `finance.approve` | PO |
| 23 | BNK-02 | (B) Confirmar no sandbox se o Inter aceita query; se não, token no caminho com máscara do último segmento | Inter |
| 24 | BNK-02 | (B) Pagamento com diferença vai para conferência humana | PO |
| 25 | BNK-02 | Estorno de baixa com `finance.approve` (fecha a P-20, D6) | PO |
| 26 | BNK-02 | (B) Chave composta (cobrança, situação, data) mais o SHA-256 do corpo; confirmar o id de entrega no sandbox | Inter |
| 27 | BNK-02 | Resumo por hora acima de 5 avisos | PO |
| 28 | BNK-03a | Importação às 06:30 | PO |
| 29 | BNK-03a | (B) `ofx` só no dono `BankStatementBatch` | Arq |
| 30 | BNK-03a | OFX bloqueado em conta com API; liberável por conta com `finance.approve` como contingência (D13) | PO |
| 31 | BNK-03a | Contraparte mascarada para quem só tem `finance.view` | PO |
| 32 | BNK-03b | Janela de 3 dias | PO |
| 33 | BNK-03b | (B) Fechamento por período com `finance.approve` | PO |
| 34 | BNK-03b | Regras de tipo montadas com extratos reais | Kon |
| 35 | FIS-02a | (B) `fiscal.approve` novo no catálogo, reservado ao Proprietário (D12) | PO |
| 36 | FIS-02a | (B) Seed: Financeiro com `fiscal.view/create/edit`; Almoxarifado com `fiscal.view` + `fiscal.import_incoming` | PO |
| 37 | FIS-02a | (B) Série exclusiva, obrigatória em produção | Cont |
| 38 | FIS-02a | Sem produção antes da GOL | PO |
| 39 | FIS-02a | (B) Confirmar Lucro Presumido (P-01) | Cont |
| 40 | FIS-02b | (B) Regras e casos de teste fornecidos pela contadora | Cont |
| 41 | FIS-02b | (B) Tributos no custo do projeto: ICMS, PIS, COFINS, ISS (e IBS/CBS); IPI fora, a confirmar | Cont+PO |
| 42 | FIS-02b | (B) Ver o ponto 9 | Cont |
| 43 | FIS-02b | DIFAL bloqueado; a Konebras informa a frequência | PO/Kon |
| 44 | FIS-02b | Segregação na ativação como proposta | PO |
| 45 | FIS-03a | Fase 3 (D4): o romaneio baixa; remover a flag dos dois caminhos | — |
| 46 | FIS-03a | (B) Remessa para obra e venda: definir | Cont |
| 47 | FIS-03a | Imposto nunca digitado à mão | PO |
| 48 | FIS-03a | (B) Nota × parcelas pela regra da venda mista (D4) | Cont+PO |
| 49 | FIS-03a | Ver o ponto 10 | — |
| 50 | FIS-03a | (B) Os 3 cenários: venda a contribuinte em SP, a não contribuinte em SP, peça ou outro estado | Cont |
| 51 | FIS-03a | (B, externo) Comprar o e-CNPJ A1 | Kon |
| 52 | FIS-03b | (B) Prazo de cancelamento de 24 h, com contagem no diálogo | Cont |
| 53 | FIS-03b | (B) Uma etapa para `fiscal.approve`, "Pedir cancelamento" para os demais (D12) | PO |
| 54 | FIS-03b | Devolução fica com a contadora no MVP | PO/Cont |
| 55 | FIS-03b | Carta de correção com `fiscal.edit` | PO |
| 56 | FIS-04a | (B) `fiscal.import_incoming` (D12) | PO |
| 57 | FIS-04a | Recusar XML sem protocolo | Cont |
| 58 | FIS-04a | Aviso "situação na SEFAZ não conferida", sem consulta | PO |
| 59 | FIS-04a | Item sem estoque como proposto | PO |
| 60 | FIS-04a | (B) Nota sem pedido exige `purchasing.approve` | PO |
| 61 | FIS-04b | Sem duplicatas na nota, vale a condição do pedido | PO |
| 62 | FIS-04b | Crédito na entrada pela P-01 | Cont |
| 63 | FIS-04b | Fase 3 (itens 38 a 53) e D12: chamada síncrona e `purchasing.receive` | — |
| 64 | FIS-05 | Distribuição DF-e depois do MVP | PO |
| 65 | FIS-05 | (B) Sem OCR: só PDF com texto | Arq |
| 66 | FIS-05 | Oferecer "Digitar a nota" | PO |
| 67 | FIN-09 | (B) Passos −3, 0, +3, +10 dias | Kon |
| 68 | FIN-09 | Títulos de implantação fora | PO |
| 69 | FIN-09 | (B) Conferência antes de enfileirar e de novo no envio (guarda por template); texto editável literal e escapado, com placeholders de lista fixa | Arq |
| 70 | FIS-06 | (B) Não bloqueia o go-live; processo provisório (D5) | PO+Cont |
| 71 | FIS-06 | (B) P-02 e venda mista (D4) | Cont |
| 72 | FIS-06 | (B) Provider de Caçapava no Emissor, planejado já (D3) | PO |
| 73 | FIS-06 | (B) Código do serviço: 14.06 ou 7.02 da LC 116? | Cont |
| 74 | GOL-01a | (B) Lista do D1 | PO |
| 75 | GOL-01a | Teste de restauração e rollback **obrigatórios** (D7) | PO |
| 76 | GOL-01a | (B) Ambiente de produção novo, ADR 0034 (D7) | PO/Arq |
| 77 | GOL-01a | Início de mês, fora do horário comercial | PO+Kon |
| 78 | GOL-01a | (B) Fase 4 depois do go-live, exceto FIN-07 e DSH-02 (D1) | PO |
| 79 | GOL-01b | Desligar o Dolibarr depois do 1º fechamento ou em até 3 meses; backup por 5 anos | PO |
| 80 | GOL-01b | (B) Dolibarr só leitura, com allowlist de IP e a `DOLAPIKEY` regenerada (D7) | Kon/PO |
| 81 | GOL-01b | (B) Aviso por e-mail aos clientes com a data da virada e o boleto novo | Kon |

### 3.3 Fase 6
| # | Story | Recomendação consolidada | Quem |
|---|---|---|---|
| 1 | RH-01a | (B) "Meu ponto" em `/api/v1/me/hr/*`, camada 1 + vínculo com funcionário ativo, sem ação no grupo; ADR 0035 (D15) | PO/Arq |
| 2 | RH-01a | Geolocalização desligada por padrão; ciência registrada; precisão reduzida; visível só com `hr.view_sensitive`; tratada como indício | PO+Jur |
| 3 | RH-01a | Sem marcação offline (coerente com a P-23) | PO |
| 4 | RH-01a | Intervalo mínimo de 1 min; "Minhas marcações" de 60 dias | PO |
| 5 | RH-01a | (B) Uso legal do ponto do app: pré-condição do Marco 6.1 (D15) | Jur+Cont |
| 6 | RH-01b | (B) Lançamento manual do RH também passa por aprovação | PO |
| 7 | RH-01b | (B) Tipos de abono unificados com os afastamentos da CAD-04c1 | PO+Cont |
| 8 | RH-01b | Proprietário único aprova as próprias marcações, com a marca | PO |
| 9 | RH-01b | Retenção do atestado igual à do funcionário; ação `hr.view_health` separando atestado de salário (ADR 0003) | Jur |
| 10 | RH-01c | (B) Tolerância de 5/10 min, salvo a convenção | Cont |
| 11 | RH-01c | (B) Banco de horas simples, só com acordo, sem vencimento automático | Cont+Jur |
| 12 | RH-01c | (B) Apuração pelo mês civil | Cont |
| 13 | RH-01c | Fecha o período quem tem `hr.edit` | PO |
| 14 | RH-01c | "Li e confirmo" no Meu espelho; PDF com campo de assinatura | Jur |
| 15 | RH-02a | (B) Rubricas com os códigos da contadora | Cont |
| 16 | RH-02a | Os 6 tipos de folha (vencimentos diferentes no caixa) | Cont |
| 17 | RH-02a | (B) Um título por guia, com vencimento padrão | Cont |
| 18 | RH-02a | (B) Layout de importação: o arquivo da contadora, se existir | Cont+PO |
| 19 | RH-02a | Alerta de variação do líquido de 10%; aviso "novos na folha / conta alterada" no fechamento | PO |
| 20 | RH-02b | (B) "Meus holerites" autenticado; e-mail só avisa, sem anexo (D15) | Jur+PO |
| 21 | RH-02b | Holerite em "Meus holerites" desde já (é o canal) | PO |
| 22 | RH-02b | CPF mascarado 3+2 | Cont |
| 23 | RH-02c | (B) Título "Folha {mês}" + um por guia, retrato das contas no fechamento; ADR 0036 (D14) | PO/Arq |
| 24 | RH-02c | (B) Conta e centro de custo pelo centro do cargo | Cont |
| 25 | RH-02c | (B) Fecha a folha quem tem `hr.approve`, que não paga (D14) | PO |
| 26 | RH-02c | Vencimento dos salários no 5º dia útil | Cont |
| 27 | RH-02c | Folha prevista de 3 meses, com 13º e férias (origem `PayrollForecast`) | PO |
| 28 | RH-03 | (B) Colunas do layout para a contadora | Cont |
| 29 | RH-03 | Não guardar o arquivo; baixado com `hr.view_sensitive` | PO |
| 30 | RH-03 | Só CSV no MVP; XLSX depois, com biblioteca MIT | Arq |
| 31 | LGPD-01a | Configurações → Privacidade, só o Proprietário | PO |
| 32 | LGPD-01a | (B) Prazo de 15 dias corridos | Jur |
| 33 | LGPD-01a | (B) Dependentes informados pelo titular; CPF de terceiros mascarado | Jur |
| 34 | LGPD-01a | Usuários do sistema incluídos como titulares | PO |
| 35 | LGPD-01a | Arquivo guardado 30 dias; download autenticado com expiração; identidade conferida e registrada antes de exportar | PO |
| 36 | LGPD-01b | (B) Tabela de retenção por campo | Cont+Jur |
| 37 | LGPD-01b | Eliminação atendida por anonimização | Jur |
| 38 | LGPD-01b | Anonimização automática depois do MVP | PO |
| 39 | LGPD-01b | Uma transação por módulo, etapas idempotentes e retomáveis | Arq |
| 40 | PAT-01a | (B) Permissões do `assets` como propostas | PO |
| 41 | PAT-01a | (B) Local do bem em cadastro próprio simples (`assets.sites`) | PO/Arq |
| 42 | PAT-01a | Valor de aquisição só com `assets.edit` | PO |
| 43 | PAT-01a | Depreciação não antecipada (Fase 9) | PO |
| 44 | PAT-01a | Inventário do patrimônio fora | PO |
| 45 | PAT-01b | (B, externo) Modelo da impressora e da etiqueta (P-13) | Kon |
| 46 | PAT-01b | URL curta `/b/{etiqueta}`, opaca, leitura com login | PO |
| 47 | PAT-01b | (B) QR no servidor com biblioteca MIT; leitura pelo `BarcodeDetector`; "Ler etiqueta" no `/fab` | Arq |
| 48 | PAT-02 | Venda do bem: `NextSteps` "Lançar título" | PO |
| 49 | PAT-02 | Termo de responsabilidade fora | PO |
| 50 | PAT-03a | (B) OS de manutenção no Assets (`assets.maintenance_orders`); ADR 0038 | Arq |
| 51 | PAT-03a | (B) Plano por horas com horímetro digitado | PO/Kon |
| 52 | PAT-03a | Próximo vencimento a partir da conclusão | PO |
| 53 | PAT-03a | Baixa de peças do estoque fora | PO |
| 54 | PAT-03a | Abre a OS quem tem `assets.create` | PO |
| 55 | PAT-03b | (B) Contas da manutenção | Cont |
| 56 | PAT-03b | (B) Custo de manutenção visível com `assets.edit` | PO |
| 57 | PAT-03b | (B) Horas por dia informadas na OS, como parada no `IWorkCenterCapacity` (D10) | PO/Kon |
| 58 | PAT-03b | Outras paradas na Produção, depois | PO |

### 3.4 Fase 7
Bloqueiam: 1, 2, 7, 8, 13, 14, 16, 17, 20, 22, 23 e 25.

| # | Story | Recomendação consolidada | Quem |
|---|---|---|---|
| 1 | FLH-01 | Valores iniciais e casos de teste (dados sintéticos, nunca holerites reais no Git) | Cont |
| 2 | FLH-01 | Formato da redução do IRRF: conferir a norma vigente; esquema por tipo de tabela versionado no código | Cont/Arq |
| 3 | FLH-01 | Encargos pela P-01 | Cont |
| 4 | FLH-01 | Quem cadastra não confere; exceção do Proprietário único só com dataset da contadora aprovado | PO |
| 5 | FLH-01 | A plataforma sugere a tabela, o tenant importa e a contadora confere | PO |
| 6 | FLH-01 | Arredondamento por regra parametrizada, testado centavo a centavo | Cont |
| 7 | FLH-02a | Calculada só como simulação até a virada (FLH-06) | PO+Cont |
| 8 | FLH-02a | Regras parametrizadas: proporcionalidade, base da insalubridade, acúmulo, valor da hora | Cont |
| 9 | FLH-02a | Afastamento por doença e acidente | Cont |
| 10 | FLH-02a | Elegibilidade do salário-família | Cont |
| 11 | FLH-02a | Pensão e consignado como lançamento avulso | PO |
| 12 | FLH-02a | Encargos pela P-01 | Cont |
| 13 | FLH-02b | % de horas extras e adicional noturno pela convenção | Cont |
| 14 | FLH-02b | Fórmula do DSR e perda por falta | Cont |
| 15 | FLH-02b | Banco de horas vencido pela RH-01c ponto 11 | Cont/PO |
| 16 | FLH-03 | Regras de férias | Cont |
| 17 | FLH-03 | INSS e IRRF das férias | Cont |
| 18 | FLH-03 | Férias coletivas fora, salvo se a Konebras usar | Kon |
| 19 | FLH-03 | Alerta de vencimento com 60 dias | PO |
| 20 | FLH-04 | Regras do 13º | Cont |
| 21 | FLH-04 | Previsão do 13º no caixa desde janeiro | PO |
| 22 | FLH-05 | Matriz de rescisão | Cont+Jur |
| 23 | FLH-05 | Saldo do FGTS digitado | Cont |
| 24 | FLH-05 | Termo só para conferência no MVP | PO |
| 25 | FLH-06 | Aceite: diferença zero no líquido e por rubrica | PO+Cont |
| 26 | FLH-06 | Assina a contadora responsável, registrado | PO+Cont |
| 27 | FLH-06 | 3 meses sem exigir evento; férias, 13º e rescisão só viram oficiais depois de 1 caso conferido | PO |
| 28 | Fase | eSocial, DCTFWeb e FGTS Digital fora do produto | PO |
| — | Todas | Determinismo: `calculator_version`, ids das tabelas e retrato das entradas por holerite; folha fechada nunca é recalculada; retificação nasce `Draft` e passa por nova conferência (ADR 0039) | Arq |

### 3.5 Fase 8
| # | Story | Recomendação consolidada | Quem |
|---|---|---|---|
| 1 | POR-01a | (B) ADR 0040 (D16) | Arq+PO |
| 2 | POR-01a | (B) Módulo `portal` no catálogo, com seed patch do Comercial | PO |
| 3 | POR-01a | (B) Host próprio (D16; o arquiteto preferia `/portal` no host do tenant) | PO |
| 4 | POR-01a | 2FA opcional para ver; código por e-mail obrigatório no aceite (D16) | PO |
| 5 | POR-01a | Um contato vê um cliente só | PO |
| 6 | POR-01b | (B) Data prometida; quando a prevista passar dela, "Nova previsão {data}" com o motivo em linguagem simples | PO/Kon |
| 7 | POR-01b | NF-e compartilhada por padrão | PO |
| 8 | POR-01b | Marca de compartilhar em `portal.shared_documents` | Arq |
| 9 | POR-01b | E-mail só nos marcos, com opt-out | Kon |
| 10 | POR-01b | Endereço da obra: só cidade e bairro | PO |
| 11 | POR-01c | Parcelas previstas sem data, como "Após {marco}" | PO |
| 12 | POR-01c | Segunda via pelo próprio cliente, com limite, só do boleto emitido mais recente | Kon |
| 13 | POR-01c | Histórico de 12 meses | PO |
| 14 | POR-02 | (B) O aceite só avisa o Comercial (mantém a D5 da Fase 2) | PO+Jur |
| 15 | POR-02 | Assinatura eletrônica com validade jurídica fora | Jur |
| 16 | POR-02 | Sem link de aceite sem login | PO |
| 17 | WPP-01 | (B) API oficial por BSP, custo do tenant (D17) | PO |
| 18 | WPP-01 | (B) Opt-in pelo próprio contato; troca de número derruba o opt-in | Jur |
| 19 | WPP-01 | Textos aprovados pela Konebras, com prévia | Kon |
| 20 | WPP-01 | Dias úteis, das 8 h às 18 h | PO |
| 21 | WPP-01 | FIN-09 antes | PO |

## 4. Correções sem decisão do PO

### 4.1 Atualização pelas decisões da Fase 3 (mecânica)
- Refazer a seção "Dependências da Fase 3" do README nas Fases 4, 5 e 6 (tudo já decidido) e tirar os textos "Fase 4 ainda sem stories" e "Fases 4/5 só no backlog".
- CAD-04c → CAD-04c1 (RH-01a/01b/01c) e CAD-04c2 (RH-02a); PRD-06a → PRD-06a1/06a2; incluir a CAD-04c2 no bloco da ordem 6.2.
- FIS-03a CA 11 e FIS-03b CA 4: retirar os dois caminhos de baixa; a nota só referencia o romaneio (D4 da Fase 3).
- FIS-04b: duplicatas pelo `CreateOpenInstallmentsAsync` na transação do recebimento, `purchasing.receive` (ADR 0024, D8 da Fase 3).
- DSH-02: `project_costs` nasce na PRD-04 como razão (`source`, `seq`, `is_backfill`); comprometido pela D8 desta revisão.
- DSH-03 e DSH-04: qualidade com `production.approve_quality` e a segregação do ponto 76 da Fase 3.
- PNL-01b, PLN-05 e PLN-02: fechar como decididos os pontos 65, 6 (ENG-01) e 28 (EST-02) da Fase 3.
- `02-arquitetura`: `/tv#token` no lugar de `/tv/{token}`, sem o índice de quiosque no catálogo (§2.1, §2.2, §4) e o webhook do Inter sem o tenant pelo nome na URL.
- FND-16 CA 14 cita `fiscal.approve`, que só passa a existir pela D12; CA 6 com `settings.edit`.

### 4.2 Arquitetura (entram nas ADRs e nas stories)
- **FND-16:** gravar o `webhook_events` e a mensagem do outbox na mesma transação (o handler do provedor vira consumidor com inbox) ou um varredor de eventos `Received` há mais de 5 min; `/webhooks/*` fora do middleware de tenant por host, com teste; payload em `bytea` ou `text`.
- **BNK-01a:** um `SocketsHttpHandler` por conta, em cache pelo thumbprint, com o certificado só em memória e descartado na rotação; saída de rede liberada só para os hosts do Inter; proibido `ServerCertificateCustomValidationCallback` permissivo fora dos testes (teste de arquitetura).
- **RH-01c:** `IBusinessCalendar` no `Production.Contracts`.
- **LGPD-01:** inventário de onde há dado pessoal (`idempotency_records`, `webhook_events`, `email_logs`, notificações, usuários do portal, payload do outbox, Hangfire, backups); cada um entra no `IPersonalDataProvider` ou ganha expurgo; o teste de dado pessoal (`PersonalDataTests`, hoje só Parties e HR) cobre as entidades novas; texto livre do RH marcado `[PersonalData]`. ADR 0037.
- **RH-01a:** "Meu ponto" no tablet exige os endpoints na lista `[PinSessionAllowed]` da ADR 0026; quem ganha `hr.view_sensitive` perde o PIN.
- **PAT-03b:** custo da manutenção com `assets.edit`, não `projects.view_cost`.
- **Riscos de migração:** apagar `commercial.projects.priority` só com ADR, na contração; `payables.party_id` aceitando nulo é expansão (código N-1 tolerante); expurgos e anonimização apagam dado de propósito, cobertos pelas ADRs 0031 e 0037; apagar o `konebras` da homologação, pela 0034; conferir se há linhas em `kiosk_tokens` antes de trocar a rota da TV.

### 4.3 Segurança (viram CAs)
- **Transversal:** jobs com `tenantId` e conferência do módulo liberado; grupos do SignalR derivados da claim, conexão fechada quando a permissão ou o security stamp mudam (o hub de hoje não revalida); rate limit por políticas nomeadas e não só em `/auth` (simulação da PLN-05, webhooks); anexos autorizados por registro, não só por módulo, e storage com prefixo de tenant.
- **FND-16:** teste com dois tenants; canário também no `error_log` do nginx.
- **BNK-01a:** certificado e `client_secret` só de escrita, com canário; URL base fixa (contra SSRF); testar a credencial nova antes de trocar.
- **BNK-03a/03b:** parser sem DTD, limite, antivírus, hash do arquivo e FITID únicos; aviso ao Proprietário a cada importação; teto para "lançar sem OP" a partir de linha de OFX; ignorar débito com `finance.approve`; desfazer com motivo.
- **FIS-03a:** saneamento pelos limites do esquema antes de reservar o número; campos livres escapados no XML e no DANFE.
- **FIS-05:** parser de PDF em worker isolado (`network_mode: none`, memória limitada, sistema de arquivos só leitura, kill no tempo limite).
- **PNL-01a:** pareamento de uso único ou teto de sessões por tela; 404 por linha em `/kiosk/board/orders/{id}`; `no-store`; o service worker não guarda `/tv` nem `/api/v1/kiosk`; o principal de quiosque recebe 403 fora do GET do painel.
- **FIN-08 e DSH-02:** drill-down de salários e custo-hora com nome exigem `hr.view_sensitive`.
- **DSH-01a/01b:** DTO por forma de permissão e `RequireModule` do módulo dono em cada card.
- **POR-01a:** resposta uniforme e rate limit no convite e na recuperação; e-mail único por tenant; refresh com detecção de reuso; trocar o e-mail do contato invalida o acesso; documentos só por endpoint autorizado.
- **POR-02:** decisões do cliente só de inclusão; recibo por e-mail com o hash; revisão diferente da aceita bloqueia a sugestão na COM-04a.
- **FLH-01/02:** parâmetros mudados com `settings.edit` aparecem como aviso no fechamento; `parallel_differences` marcado `[PersonalData]`.
- **PAT-01b:** `returnUrl` só relativo; `assets.view` conferido antes de buscar a etiqueta; tirar EXIF/GPS da foto do bem.
- **Baixos:** trigger do `audit_log` protegido também contra o usuário da aplicação; PFX com `EphemeralKeySet`; DANFE de nota cancelada com marca.

### 4.4 Design (padrões das Fases 4 a 8, entram no README)
1. Família de gráficos sobre `ChartFrame` (`Chart`, `ColumnChart`, Gantt): série nunca só por cor (traço e rótulo direto), referências em x e em y (zero, meta, capacidade), resumo textual acima e tabela equivalente.
2. `Kpi` e `DashboardCard`: todo número clicável até a origem, "Como calculamos", "Atualizado às", estados por card; faixa "Atenção" com até 3 alertas.
3. Tabelas analíticas (DRE, ocupação, espelho): coluna fixa, navegação por célula, drill-down em painel lateral com total igual ao da célula. No DRE, sinal no rótulo ("(−) Custos") e crítico só em resultado negativo, com a coluna "Δ mês anterior".
4. Kit TV (D11): tema escuro, 32/28/48 px, até 4 linhas por cartão e 3 cartões por coluna, "Ao vivo / Reconectando… / Atualizado às", prévia no backoffice; `StageIndicator` com `size="tv"` e `size="portal"`.
5. Kit Portal: celular primeiro, 16 px, alvos de 44 px, vocabulário leigo com teste de texto (sem OS, marco, romaneio, letras de etapa), etapa com nome e frase do que acontece e do que vem depois, bloco "Fale com a {empresa}" em todo erro e boleto vencido, "Preferências de aviso", mensagem própria de indisponibilidade.
6. Integração externa assíncrona (SEFAZ, Inter, WhatsApp): "Enviado / Aguardando / Demorando (30 s) / Confirmado / Recusado com motivo e ação", sem reenvio enquanto processa; `EnvironmentBanner` único.
7. Ações irreversíveis: confirmação digitada quando apaga dado (LGPD), contagem do prazo legal (cancelamento de NF-e), impacto em lista nas ações em lote.
8. Dado sensível do RH: bloco "Dados protegidos" padrão; autoatendimento ("Meu ponto", "Meu espelho", "Meus holerites") com o mesmo layout no `/fab` e no celular.
- **Achados pontuais:** conciliação com uma linha por par (também no desktop) e seções "Sem par no banco" e "No sistema, não no banco"; fila "A emitir" em Notas de saída e na pauta; Financeiro com abas agrupadas e "Mais" a 400 px ("Pagamentos a conferir" e "Números a inutilizar" viram filtros); campos incertos da FIS-05 com texto e foco no primeiro; prévia da mensagem do WhatsApp e lista de falhas reenviáveis; botões de `ErrorState`/`EmptyState` com 44 px a 400 px; FND-16 "Ver conteúdo" com JSON mascarado; FIS-02b "Simular" em painel lateral; FLH-06 diferença em R$ e % por rubrica.

## 5. ADRs antes de codar

| Nº | Título | Antes de |
|---|---|---|
| 0025 (adendo) | Comprometido por saldo com fonte única; fontes de imposto da nota (D8) | DSH-02 |
| 0027 | Integração com o EmissorFiscal: serviço HTTP, um tenant do Emissor por tenant, A1 no Emissor, rede `bqe-fiscal`, reconciliação com a SEFAZ como pré-condição, lacunas com dono e prazo (D3) | FIS-01 |
| 0028 | Composição do painel e dos relatórios: módulo `Dashboard`, `Reporting` revogado, cache (D9) | Marco 4.2 |
| 0029 | Planejamento finito: escritor único, datas fora do documento, fila no Production, `project_forecasts` (D10) | Marco 4.3 |
| 0030 | Quiosque e TV: `/tv#token`, esquema `Kiosk`, hub do quiosque, revogação; adendo à 0009 (D11) | Marco 4.4 |
| 0031 | Recepção de webhooks: atomicidade com o outbox, roteamento, retenção; adendo à 0007 (D13) | FND-16 |
| 0032 | Banco Inter: integração por conta, handler mTLS em memória, saída restrita, token na URL (D13) | BNK-01a |
| 0033 | Gantt próprio (D18) | PLN-03 |
| 0034 | Promoção a produção: ambiente novo, segredos, sessões, Dolibarr, rollback (D7) | GOL-01a |
| 0035 | Autoatendimento do funcionário; adendo à 0026 (D15) | Marco 6.1 |
| 0036 | Folha no financeiro: favorecido, chamada síncrona, previsão, conta de salário (D14) | Marco 6.2 |
| 0037 | Execução da LGPD: provedores, onde há dado pessoal, expurgos, backups | Marco 6.3 |
| 0038 | Patrimônio: dono da OS de manutenção, paradas na capacidade | Marco 6.4 |
| 0039 | Motor da folha calculada: vigência, determinismo, versão do motor, dados sintéticos no CI | Fase 7 |
| 0040 | Identidade do portal do cliente (D16) | POR-01a |
| 0041 | Provedor e consentimento do WhatsApp (D17) | WPP-01 |
| Adendos | 0003 (`fiscal.approve`, `fiscal.import_incoming`, `kiosk.delete`, `hr.view_health`); 0007 (segredos novos); 0008 (`fiscal.approve` reservado); 0009 (rotas anônimas do quiosque, webhook e portal); 0026 (PIN no "Meu ponto") | junto da story |

## 6. Ordem recomendada (D1)
1. **Agora:** a trilha externa da seção 2 e as lacunas do EmissorFiscal (D3), em paralelo ao fim da Fase 3; o PR de reforço do código atual (D19).
2. **Antes do go-live, sem dependência externa:** FIN-07 → DSH-02.
3. **Núcleo do go-live, em duas trilhas:** FND-16 → BNK-01a → BNK-01b → BNK-02 → BNK-03a → BNK-03b; e FIS-01 → FIS-02a → FIS-02b → FIS-03a → FIS-03b.
4. **Go-live:** GOL-01a → GOL-01b, no início de um mês.
5. **Logo depois:** FIN-08 (antes do primeiro fechamento, com a receita pela nota) → FIS-06 → FIN-09 → FIS-04a → FIS-04b.
6. **Resto da Fase 4:** DSH-04 → DSH-01a → PLN-01a → PLN-01b → PLN-02 → PLN-04 → PNL-01a → PNL-01b → DSH-01b → DSH-03 → PLN-03 → PLN-05; depois a FIS-05.
7. **Fase 6:** CAD-04c2 → RH-02a → RH-02c → RH-02b → RH-03 → (decisão legal) RH-01a → RH-01b → RH-01c → LGPD-01a → LGPD-01b; Patrimônio em paralelo: PAT-01a → PAT-01b → PAT-02 → PAT-03a → PAT-03b.
8. **Fase 8:** POR-01a → POR-01b → POR-01c → WPP-01 → POR-02.
9. **Fase 7:** só com caso de negócio da Konebras.
