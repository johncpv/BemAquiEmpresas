# GOL-01a — Go-live Konebras: critérios de entrada, ambiente de produção novo, runbook e ensaio completo da virada

| Campo | Valor |
|---|---|
| ID | GOL-01a |
| Título | Tudo o que precisa estar pronto e ensaiado antes do dia da virada: critérios de entrada do go-live (Fases 0 a 7 concluídas, EmissorFiscal publicado, contagem física, folha pela RH-02c, backup B2 do ambiente novo), checklist **"Antes da primeira operação fiscal e bancária"** (contadora, Configurações → Fiscal, NFS-e de Caçapava, Inter de produção e as marcas de produção, feitos com o sistema já no ar), ambiente de produção **novo** (ADR 0034: catálogo só com o `konebras`, key ring e certificado do Data Protection novos), inventário de segredos, runbook de virada e de rollback, scripts de revogação de sessões e de fechamento do Dolibarr, ensaio completo e rollback numa cópia restaurada (obrigatórios), treinamento, comunicação aos usuários e aos clientes, e o "Go/No-go" do PO |
| Tamanho | G (re-estimada de M: o ambiente de produção passou a ser novo, com key ring novo e recifração dos segredos; entram os scripts de revogação de sessões e do Dolibarr, o inventário de segredos completo e a lista de entrada da D7 inteira) |
| Status | Done (02/11/2026, com os desvios aprovados pelo PO listados em "Desvios na entrega") |
| Requisitos | PRD §7 ("go-live e desligamento do Dolibarr") · PRD §1.2 (métricas) · PRD §6 Disponibilidade, Backup, Segurança · R-PLT-2 · R-TER-5 · R-SEC-3 · R-SEC-5 |
| Dependências | **Fases 0 a 7 concluídas** (todas as stories `Done`; a Fase 8, POR-* e WPP-01, não segura o Go, decisão do PO de 02/11/2026); PR de reforço da D19; FND-10b (homologação, backup, monitor); MIG-01a/b/c (cargas reexecutáveis); ADR 0034 aceita (adendo de 02/11/2026); EmissorFiscal publicado (02/11/2026, imagem `be1253e`). A trilha da contadora e o Inter de produção **não** são dependência do Go: vão para o checklist do CA 14 |
| Bloqueia | GOL-01b |
| Ordem de execução | 1 (Marco final "Go-live") |

## User story
Como **PO**, quero **ensaiar a virada inteira numa cópia, num ambiente de produção novo, antes de fazê-la de verdade, com o caminho de volta escrito e testado**, para **trocar o Dolibarr pelo BemAqui num dia só, sem perder dado, sem levar chave nem sessão da homologação e sem improvisar**.

## Contexto e referências
- **ADR 0034** (promoção a produção: ambiente novo, segredos, sessões, Dolibarr, rollback; altera a ADR 0020 no ponto do key ring, que deixa de acompanhar o banco; adendo de 02/11/2026 com os critérios revistos), ADR 0008 adendo de 21/10/2026 (D6, fora do Go desde 02/11/2026), ADR 0027 (EmissorFiscal: lacunas D3 com dono e prazo; adendo de 27/10/2026: **sem rede `bqe-fiscal`**, integração pela API pública `https://api.fiscal.bemaquiapp.com.br`), ADR 0032 (Inter), ADR 0042 (Configurações → Fiscal e campos críticos conferidos).
- **Runbook único:** `docs/operacao/go-live.md` (critérios de entrada, checklist antes da primeira operação fiscal e bancária, ambiente novo, segredos, passo a passo, rollback, ensaio, Dolibarr, treinamento, comunicados, métricas).
- **Revisão pré-Fases 4 a 8** (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`): D1 (mudada pelo PO: tudo antes do go-live), D2, D3, D6, D7, D19; tabela 3.2, pontos 74 a 78; seção 2 (perguntas à contadora, ao Inter, ao jurídico e à Konebras).
- `05-backlog` GOL-01: promoção da homologação a produção, troca de domínio (`app.konebras.com.br` para o BemAqui e o Dolibarr para `adm.konebras.com.br`), segredos de produção, carga final do Dolibarr só com os cadastros alterados (MIG-01b, MIG-01c, ADR 0017), `DROP USER` do somente leitura, treinamento, checklist de virada e primeira semana.
- ADR 0020 (homologação com dado real promovida; nesta story, pelo ambiente novo da ADR 0034), ADR 0004 e 0005 D1 (Dolibarr em `adm.`; Konebras UI desativado junto), ADR 0015 (`deploy-prod.yml` desligado por `vars.PROD_DEPLOY`; compose em `/opt/projetos/bemaqui-empresas/`; vhost com `nginx -t`), ADR 0007 (key ring, certificado de proteção, recifração), ADR 0017 adendo 2 (a chave da API do Dolibarr deixa de ser usada e o secret é apagado), ADR 0018 (homologação com dado real segue regras de produção), ADR 0010 itens 13 e 14.
- `06-pendencias-riscos`: P-01, P-02, P-03, P-08, P-19 e P-20 (decididas pela D6), P-24.
- Runbooks: `docs/operacao/deploy.md`, `restauracao.md`, `segredos-e-chaves.md`, `migracao-dolibarr.md`, `monitoramento.md`, `provisionamento.md`.

## Critérios de aceite
**Critérios de entrada (go/no-go)**
1. **Dado** a lista de critérios de entrada em `docs/operacao/go-live.md` §1, **quando** o PO revisa, **então** cada item abaixo está marcado com evidência, data, responsável e situação, e o PO registra "Go" ou "No-go" com data no campo "Go/No-go" do runbook (revisto pelo PO em 02/11/2026: **a contadora e o Inter não seguram o Go**):
   - a. **Fases 0 a 7 concluídas**: todas as stories `Done` no backlog (a Fase 4 inteira, sem exceção; Fases 6 e 7 inteiras) e o PR de reforço da D19 no ar. **A Fase 8 (POR-*, WPP-01) fica fora do Go** (02/11/2026).
   - b. **EmissorFiscal de produção pronto para o BemAqui:** versão publicada em 02/11/2026 (imagem `be1253e`) com as lacunas da tabela de `docs/operacao/emissor-fiscal.md` §7 conferidas na homologação (consulta forçada, DANFE em PDF, `GET /me` e `nfe:eventos`, timestamp no HMAC, XML do evento L-n, XML da inutilização, lacunas de numeração, bloco `transporte` L-d, exposição sem Swagger nem `/admin`, um tenant do Emissor por tenant, envio do A1 sem `admin`), **cópia offline da chave-mestra** do cofre com teste de restauração (L-k) e **certificado A1 válido por mais de 60 dias no cofre do Emissor** (já instalado, decisão de 31/10/2026; o BemAqui mostra só validade, titular e thumbprint, FIS-02a). A rede `bqe-fiscal` não existe mais (ADR 0027, adendo de 27/10/2026). Regime, CNAE, IBS/CBS e NFS-e de Caçapava vão para o CA 14.
   - c. _Movido para o CA 14 (02/11/2026):_ Configurações → Fiscal sem campo crítico "A confirmar com a contadora" e regras fiscais (FIS-02b) conferidas.
   - d. **Homologação fiscal no `demo`:** SEFAZ dos 3 cenários (FIS-03a) e dos eventos (FIS-03b) com o emitente de teste. A conferência dos cenários pela contadora, a NFS-e de Caçapava e o fluxo do Inter no sandbox vão para o CA 14.
   - e. _Movido para o CA 14 (02/11/2026):_ conta PJ do Inter com API (P-08), credencial de produção e teste de conexão. O teste do Inter **não adia mais a virada**.
   - f. **Informativo (02/11/2026):** a D6 (2FA de novo por ação com `RequireRecentTwoFactor` e estorno de pagamento e de baixa bancária com `finance.approve`, P-19 e P-20) **sai do Go** e volta à revisão depois do go-live, junto com os itens de segurança adiados em 20/10/2026. O runbook registra o que já está no ar (ex.: 2FA de novo ao salvar credenciais do Inter e do A1, ao pagar e estornar a OP), sem bloquear.
   - g. **Estoque:** contagem física feita e saldo inicial lançado na janela de implantação (D7 da Fase 3), com o relatório de diferenças aceito.
   - h. **Pessoas:** usuários do chão de fábrica com e-mail da empresa (P-24), todo usuário real com convite aceito e 2FA ativo quando exigido (ADR 0010); a **folha do mês anterior lançada pela RH-02c** (um título por funcionário e um por guia, ADR 0036) no BemAqui. A **virada da folha calculada** (FLH-06: 3 competências seguidas aceitas em paralelo com a lançada) **é operação e não segura o go-live**: no dia da virada a folha oficial é a lançada (RH-02a/02c), e a FLH-06 corre depois, com os datasets da contadora conferidos.
   - i. **Backup e volta:** backup diário **B2 + `age` ligado no ambiente de produção novo já nesta story** (02/11/2026) e verificado nos últimos 7 dias antes da data, com o da homologação (dado real) também verificado; **teste de restauração** do tenant `konebras` e **rollback** ensaiados numa cópia (CA 7 e 9), obrigatórios.
   - j. **Operação:** monitor externo do host de produção configurado; SPF/DKIM/DMARC do domínio de envio; inventário de segredos completo (CA 5); scripts de sessões, do Dolibarr e de conferência ensaiados (CA 8, 10 e 11).
   - k. **Métrica:** linha de base do "desvio da data prevista ≤ 15%" medida nos projetos em andamento (PRD §1.2), junto com as outras métricas do §1.2.
   - l. **Data:** virada no **início de um mês**, fora do horário comercial, com a janela aprovada pela Konebras. A data segue em aberto: **[data da virada, a definir pelo PO]** (recomendada 01/12/2026; alternativa 15–16/11/2026, que exige dispensar o "início do mês").
   - m. **Ambiente de produção novo** pronto (CA 3 a 5) e **treinamento e comunicados** aprovados (CA 12 e 13).

**Runbook e ambiente de produção novo**
2. **Dado** o runbook `docs/operacao/go-live.md`, **quando** está pronto, **então** descreve, com comando, responsável, tempo estimado e verificação de cada passo: D−1 (backups extras, certificados, imagem), hora H (Dolibarr congelado só leitura, Konebras UI fora), carga final (MIG-01b/c em `--dry-run`, relatório, real), promoção do banco para o ambiente novo, recifração dos segredos, migrator, script de acessos, troca de domínio (`tenant_domains`, vhosts `app.` e `adm.`, certbot, `nginx -t`), ligação do pipeline de produção, smoke, comunicação e o **rollback** (CA 9), com o ponto sem retorno marcado. Fiscal e Inter em produção ficam no checklist do CA 14, fora da noite da virada.
3. **Dado** a ADR 0034, **quando** o ambiente de produção é preparado, **então** é **novo** em `/opt/projetos/bemaqui-empresas/` (compose `bqe-prod-*`, arquivos `deploy/prod/docker-compose.yml`, `deploy/prod/deploy.sh`, `deploy/prod/nginx/bemaqui-empresas.conf` e `deploy/prod/.env.example`), com **catálogo novo só com o tenant `konebras`**, **key ring e certificado do Data Protection novos** e as cadeias de conexão cifradas de novo; segue o endurecimento da ADR 0015 (rede interna, não-root, `read_only`, limites) e o adendo de 27/10/2026 da ADR 0027 (sem rede `bqe-fiscal`, EmissorFiscal pela API pública em HTTPS), passa no `check-compose.mjs`, o **backup B2 + `age` fica ligado no cron já nesta story** (CA 1 i), e o vhost de produção (`bemaqui-empresas.conf`, log `bqe_masked`, WebSocket, `/webhooks/` fora da allowlist e com a máscara do token, FND-16 e BNK-02) passa no `nginx -t` num ensaio local.
4. **[Segurança]** **Dado** o banco do `konebras` promovido ao ambiente novo, **quando** os segredos por tenant que estavam cifrados com o key ring da homologação são tratados, **então** cada um é **recifrado** pelo procedimento da ADR 0034 (decifrado com o key ring antigo num passo isolado e cifrado com o novo) ou **recadastrado** (Inter e A1 de produção entram novos de qualquer forma); depois da promoção, nenhum dado do ambiente novo decifra com o key ring da homologação (teste no ensaio).
5. **[Segurança]** **Dado** os segredos de produção (assinatura do JWT, senha do Postgres e `ALTER ROLE` dos usuários do banco, SMTP, conexão do catálogo, chave do EmissorFiscal de produção sem escopo `admin`, credenciais e certificado do Inter de produção, segredos de webhook, chave SSH de deploy, B2, chave `age` do backup, token do GHCR, environment protection do `deploy-prod.yml`), **quando** são gerados, **então** são **todos novos** (nenhum reaproveitado da homologação, inclusive o key ring), ficam só em arquivos de secret 0400 e no cofre do PO, e o **inventário** com dono, local e data de rotação está no runbook, sem nenhum valor.
6. **Dado** o tenant `demo` e os demais tenants de teste da homologação, **quando** a promoção é feita, **então** ficam na homologação, que continua para testes; a produção nasce só com o catálogo do `konebras`; nenhum dado de teste entra na produção; o runbook prevê **key ring novo na homologação** depois da virada e **o banco do `konebras` apagado dela depois do primeiro fechamento mensal em produção** (GOL-01b, ADR 0034 §2).

**Ensaio completo**
7. **Dado** uma cópia restaurada do backup real do tenant `konebras` e do catálogo num ambiente separado (nunca a homologação em uso), **quando** o ensaio roda o runbook inteiro (carga final do Dolibarr em `--dry-run` pela MIG-01a, promoção para o ambiente novo, recifração, migrator, revogação de sessões, mudança do host primário em `tenant_domains` para um host de ensaio, decifração de um segredo de cada tipo e o smoke), **então** tudo passa, o tempo total é medido e registrado, e o que falhou volta ao runbook antes do dia da virada.
8. **Dado** o ensaio, **quando** os dados promovidos são conferidos pelo script de conferência (só leitura, saída sem dado pessoal), **então** o relatório mostra: saldo de cada conta bancária igual ao extrato do banco no dia do ensaio (FIN-00/BNK-03b, com a conciliação fechada), total a receber e a pagar em aberto, títulos da folha do mês, saldo do estoque igual ao da contagem, número de terceiros, itens, contratos e projetos, e **nenhuma nota fiscal de homologação nem boleto de sandbox** no tenant.
9. **Dado** o rollback, **quando** é ensaiado, **então** a volta do `app.konebras.com.br` para o Dolibarr (vhost anterior guardado, Dolibarr descongelado, `DOLAPIKEY` antiga não reativada) funciona em **menos de 30 min**, e o runbook diz o que fazer com o que foi lançado no BemAqui entre a virada e a volta (lista exportada para relançar no Dolibarr; nota fiscal autorizada não volta: segue no EmissorFiscal) e o prazo máximo para decidir voltar (até o fim do primeiro dia útil).

**Sessões, acessos e Dolibarr (scripts ensaiados aqui, executados na GOL-01b)**
10. **[Segurança]** **Dado** o script de acessos da virada, **quando** roda no ensaio, **então** **revoga todas as famílias de refresh** e **sobe o security stamp de todos os usuários** (o refresh hoje não fica preso ao host, então o `aud` sozinho não basta), expira convites, redefinições de senha, PINs, aparelhos registrados e quiosques/telas da TV, apaga os usuários e convites de teste e lista os super admins para conferência do PO; o ensaio confirma que um refresh e um JWT emitidos antes do script dão 401.
11. **[Segurança]** **Dado** o script do Dolibarr, **quando** roda no ensaio (numa cópia do Dolibarr), **então** regenera a **`DOLAPIKEY`** (é a mesma do Konebras UI) e desativa os usuários dela, deixa o Dolibarr em `adm.konebras.com.br` **só leitura** (perfis sem escrita) **com allowlist de IP ou autenticação básica** no vhost, e faz o `DROP USER bqe_migracao_ro` com a revogação de acesso na rede (regra do firewall/rede Docker), conferido por consulta.

**Treinamento e comunicação**
12. **Dado** os grupos de usuários (Proprietário, Financeiro, Comercial, Engenharia, Supervisão, Operadores, Almoxarifado, RH), **quando** o treinamento é feito na homologação (tenant `demo`), **então** cada grupo faz o roteiro do seu dia a dia (por grupo, em `docs/operacao/go-live.md` §9, com o material de `docs/apresentacao/`, inclusive o 2FA de novo nas ações em que ele já existe) e a lista de presença e as dúvidas ficam registradas.
13. **Dado** a data da virada escolhida pelo PO (**[data da virada, a definir pelo PO]**), **quando** faltam 5 dias úteis, **então** estão aprovados pelo PO os rascunhos de `docs/operacao/go-live.md` §10: o comunicado aos usuários (o que muda, data e hora do congelamento do Dolibarr, onde pedir ajuda) e o **e-mail aos clientes da Konebras** com a data da virada, o boleto novo (Inter pelo BemAqui) e a nota emitida pelo sistema novo, enviado na GOL-01b.

**Antes da primeira operação fiscal e bancária (decisão do PO de 02/11/2026)**
14. **Dado** o sistema já em produção depois da virada, **quando** a Konebras vai emitir a primeira NF-e, NFS-e ou boleto real, **então** o checklist "Antes da primeira operação fiscal e bancária" de `docs/operacao/go-live.md` §2 está fechado, com evidência, data e responsável de cada item:
   - a. **Fiscal:** P-01 (regime), P-02 (CNAE de fabricação no cartão CNPJ), P-03 (IBS/CBS) respondidos pela contadora; Configurações → Fiscal sem campo crítico "A confirmar com a contadora" e regras fiscais (FIS-02b) conferidas; os 3 cenários de homologação conferidos pela contadora; **NFS-e de Caçapava** com o usuário e os códigos de contribuinte da prefeitura cadastrados no emitente do Emissor, o escopo `nfse:emitir` na chave e Caçapava em `nfseMunicipios` do `GET /me`; emitente em produção no Emissor e integração fiscal do `konebras` testada; marca `fiscal_environment_allowed = 'Production'` (a "fiscal_production_allowed" das versões anteriores) ligada pelo super admin.
   - b. **Bancário:** conta PJ do Inter com API de produção (P-08); fluxo do sandbox no `demo` (BNK-01b, BNK-02, BNK-03a, BNK-03b), se ainda não feito; credencial de produção só com cobrança, extrato e webhook, com o **teste de conexão** aprovado; marca `bank_production_allowed` ligada pelo super admin; webhook registrado no host de produção; extrato e saldo conferidos.
   - c. **Primeira NF-e real e primeiro boleto real acompanhados pelo PO.** O **ponto sem retorno** continua sendo o primeiro dos dois.
   Até o checklist fechar, as marcas ficam desligadas (emitir nota, boleto, webhook e extrato do Inter respondem 409) e o resto do sistema opera normalmente.

## Regras de negócio e técnicas
- **Nada de dado real em ambiente de teste fora do runbook** (ADR 0020, ADR 0034): o ensaio usa cópia restaurada, descartada depois, com o registro em `docs/operacao/restauracao.md`.
- **Ambiente novo (ADR 0034):** produção nunca herda chave, sessão nem segredo da homologação; o banco do `konebras` é o único dado que passa, recifrado.
- **Ponto sem retorno:** a primeira nota fiscal autorizada em produção ou o primeiro boleto real emitido pelo BemAqui. Não acontece na noite da virada: acontece depois do checklist do CA 14.
- **A contadora e o Inter não seguram o Go** (02/11/2026): o que depende deles é configurado com o sistema no ar, antes das primeiras operações (CA 14).
- **Não há código de produto novo** nesta story: só infraestrutura, runbook, scripts de operação e evidências; qualquer correção de produto vira story própria.

## Permissões, eventos e tabelas
- **Permissões:** nenhuma nova. **Tabelas:** nenhuma nova (`catalog.tenant_domains` muda na GOL-01b; `catalog.tenants.fiscal_environment_allowed` e `catalog.tenants.bank_production_allowed` mudam no checklist do CA 14).
- **Arquivos:** `docs/operacao/go-live.md` (novo), `deploy/prod/` (compose, `deploy.sh`, vhost `nginx/bemaqui-empresas.conf`, `.env.example`), scripts `scripts/golive/acessos-virada.sql|sh`, `scripts/golive/dolibarr-fechamento.sh` e `scripts/golive/conferencia.sql`, comando `reencrypt-secrets` da CLI, `deploy-prod.yml` revisado com environment protection (ainda desligado). Os nomes estão como hipótese em `go-live.md` §13, a confirmar com a implementação.

## Telas e estados
- Nenhuma tela nova. A tela inicial ganha, só na homologação e até a virada, a faixa "Ambiente de homologação com dados reais. A troca para app.konebras.com.br será em dd/mm." pelo `EnvironmentBanner` (BNK-01a), com o texto aprovado pelo PO.
- **Ajuda:** `Ajuda/` sem mudança; o roteiro de treinamento fica em `docs/operacao/`.

## Plano de testes
- **Infra:** `check-compose.mjs` e `nginx -t` do compose e do vhost de produção (CA 3).
- **Ensaio (manual, registrado):** runbook completo numa cópia restaurada, com tempo, recifração, conferência dos dados e decifração dos segredos (CA 4, 7, 8); rollback (CA 9); script de acessos com refresh e JWT antigos dando 401 (CA 10); script do Dolibarr numa cópia (CA 11).
- **Segurança:** varredura dos arquivos de secret (permissão 0400, dono); conferência de que nenhum segredo nem o key ring da homologação foi reaproveitado (CA 4, 5).
- **E2E:** suíte E2E contra o ambiente de ensaio num tenant de teste (nunca o `konebras`).

## Fora do escopo
- A virada em si e a primeira semana: GOL-01b. Desligamento definitivo do Dolibarr: GOL-01b (data) e depois.

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fases 4 a 8, tabela 3.2, pontos 74–78, e D1 mudada pelo PO):
1. Stories obrigatórias: **Fases 0 a 8 inteiras** antes do go-live (D1 mudada; nenhuma exceção) e a lista da D7 nos critérios de entrada (CA 1).
2. Teste de restauração e rollback **obrigatórios** (CA 1 i, 7, 9).
3. Ambiente de produção novo, ADR 0034 (CA 3–6).
4. Início de mês, fora do horário comercial (CA 1 l).
5. Ponto 78: Fase 4 inteira antes do go-live, sem exceção (CA 1 a).

Decisões da revisão aplicadas: D1 (marco final "Go-live" depois da Fase 8; SEFAZ e Inter em produção só na GOL-01b); D2 (trilha externa com evidência, CA 1 b, e); D3 (lacunas do EmissorFiscal e provider de Caçapava, CA 1 b); D6 (P-19 e P-20 aplicados, CA 1 f); D7 (P-02, contagem física, P-24, restauração e rollback, ambiente novo, revogação de refresh e security stamp, `DOLAPIKEY`, Dolibarr só leitura com allowlist, `DROP USER` com revogação na rede, inventário de segredos, linha de base da métrica, início do mês, CA 1, 3–5, 10, 11); D14 (folha pela RH-02c, CA 1 h); D19 (PR de reforço, CA 1 a); novo requisito de Configurações (Configurações → Fiscal sem campo crítico "A confirmar com a contadora", CA 1 c).

Decididos pelo PO em 02/11/2026:
6. **Contadora e Inter fora do Go:** regime, CNAE, IBS/CBS, Configurações → Fiscal e regras fiscais, NFS-e de Caçapava, conta e credencial de produção do Inter e as marcas de produção passam para o checklist "Antes da primeira operação fiscal e bancária" (CA 14), feito com o sistema no ar; o ponto sem retorno continua a primeira NF-e ou o primeiro boleto reais (CA 1 b, c, d, e).
7. **Fase 8 fora do Go** (POR-*, WPP-01): CA 1 a passa a "Fases 0 a 7 concluídas".
8. **D6 fora do Go** (2FA de novo por ação e estorno com aprovação): CA 1 f informativo; revisão depois do go-live.
9. **Backup B2 ligado no ambiente novo já nesta story** (CA 1 i, CA 3).
10. **Data da virada em aberto:** "[data da virada, a definir pelo PO]"; recomendada 01/12/2026, alternativa 15–16/11/2026 (CA 1 l).
11. **EmissorFiscal publicado** em 02/11/2026 (imagem `be1253e`): XML do evento (L-n), XML da inutilização, lacunas de numeração, provider de Caçapava e bloco `transporte` (L-d); tabela de `docs/operacao/emissor-fiscal.md` §7 atualizada (CA 1 b).

**Pré-condição externa:** Konebras: contagem física, e-mails dos operadores, janela da virada; trabalho no EmissorFiscal: conferência das lacunas na homologação e cópia offline da chave-mestra. Não bloqueia o `Ready`, bloqueia o "Go". A trilha da contadora e o Inter (P-01, P-02, P-03, P-08) deixam de ser pré-condição do Go e passam a ser do CA 14.

## Definition of Done
- [ ] Critérios de entrada com evidência e "Go" do PO registrado (CA 1).
- [ ] Checklist "Antes da primeira operação fiscal e bancária" escrito no runbook, com dono de cada item (CA 14; a execução é depois da virada).
- [ ] Runbook de virada e rollback revisado pelo PO (CA 2, 9), com as hipóteses do §13 conferidas com a implementação.
- [ ] Ambiente de produção novo com compose e vhost aprovados no CI (CA 3), backup B2 rodando há 7 dias; recifração ensaiada (CA 4).
- [ ] Segredos de produção gerados e inventário completo sem valor (CA 5).
- [ ] Ensaio completo, conferência e rollback ensaiados, com tempos registrados (CA 7–9).
- [ ] Scripts de acessos e do Dolibarr ensaiados (CA 10, 11).
- [ ] Treinamento por grupo e comunicados (usuários e clientes) aprovados (CA 12, 13).
- [ ] `docs/02-arquitetura.md` §7 atualizado (produção, ambiente novo); story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `docs/operacao/go-live.md` (critérios de entrada, runbook, rollback, inventário de segredos, roteiros de treinamento).
- [ ] Compose `deploy/prod/` e vhost de produção; `deploy-prod.yml` revisado com environment protection.
- [ ] Procedimento de recifração dos segredos por tenant (ADR 0034).
- [ ] Scripts: acessos (refresh, security stamp, convites, redefinições, PINs, aparelhos, quiosques, usuários de teste, super admins), Dolibarr (`DOLAPIKEY`, só leitura, allowlist, `DROP USER`) e conferência dos dados (só leitura, sem dado pessoal).

## Quality gates
- Agentes previstos: @devops, @architect (ambiente novo, recifração), cybersecurity (segredos, sessões, vhost, Dolibarr, rollback), @qa, PO.
- Gate: CA 1, 3, 4, 5, 7, 9, 10 e 11 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fases 4 a 8).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK (ajustado: R-SEC-3 pelas sessões) |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK (critérios de entrada como lista com evidência) |
| 5 | CAs de segurança e de permissão (3 camadas) | OK (não há endpoint novo; segredos, recifração, sessões e Dolibarr) |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: marco final "Go-live", Fases 0 a 8 concluídas) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs | OK (ADR 0034, D7) |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK (faixa pelo `EnvironmentBanner`; sem tela nova) |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** a GOL-01 foi quebrada em preparação/ensaio (01a) e virada/primeira semana (01b), para o ensaio completo e o rollback testado virem antes do dia; segredos da homologação não reaproveitados, exceto o key ring; teste de restauração como proposta de critério obrigatório.
- **Correções da revalidação (21/10/2026):** pontos 74 a 78 decididos; D1 mudada pelo PO: a story passa ao marco final "Go-live" e exige **Fases 0 a 8 concluídas** (CA 1 a); lista da D7 inteira nos critérios de entrada, com P-02, P-19/P-20 aplicados, contagem física, P-24, folha pela RH-02c (no lugar do CP recorrente), Configurações → Fiscal conferida, A1 no cofre do EmissorFiscal, lacunas da D3 e provider de Caçapava, datasets da Fase 7, Inter de produção testado no D−1, linha de base da métrica e início do mês (CA 1); ambiente de produção novo com key ring novo e recifração (ADR 0034, CA 3, 4, 6), o que muda o CA de segredos (antes o key ring acompanhava o banco, CA 5); inventário de segredos completo (CA 5); scripts de revogação de sessões e do Dolibarr ensaiados (CA 10, 11); e-mail aos clientes (CA 13); re-estimada para G; CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma de decisão de story. Pré-condição externa explícita: a trilha externa da D2 e o trabalho no EmissorFiscal, que bloqueiam o "Go", não o `Ready`.
- **Conferência do fechamento (21/10/2026, amostragem do PO):** virada da folha calculada (FLH-06) como operação, fora dos critérios de entrada; WhatsApp ligado na GOL-01b; checklist refeita, sem mudança de resultado.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready (pré-condição externa)`.

## Desvios na entrega (decisão do PO de 02/11/2026)
A story foi fechada em 02/11/2026, com o "Go" do PO e a virada no mesmo dia (GOL-01b). Entregue: runbook `docs/operacao/go-live.md` (CA 2), checklist "Antes da primeira operação fiscal e bancária" escrito (CA 14, execução na GOL-01b), compose, vhosts e pipeline do ambiente novo (PR #175), recifração e scripts de acessos, do Dolibarr e de conferência (PR #176). Desvios aprovados pelo PO:
1. **A homologação virou a produção** (ADR 0034, adendo de 02/11/2026 à noite; volta à ADR 0020 original): sem ambiente novo, sem promoção, sem recifração e sem cópia de arquivos. Os **CA 3, 4 e 6** (ambiente novo, recifração, `demo` na homologação) e a parte do ambiente novo do **CA 5** passam para a **fase 2**, depois do go-live, junto com o B2. O `deploy/prod/` fica no repositório, sem uso.
2. **CA 5 (segredos):** na virada, só a **assinatura do JWT** foi trocada (a antiga guardada fora de `secrets/`); o key ring, o certificado do Data Protection e os demais segredos são os da homologação (risco aceito, ADR 0034 adendo).
3. **CA 1 i (backup e volta):** os 7 dias de backup B2 foram **substituídos** pela política de hoje (dump do `catalog` e do `tenant_konebras` antes de cada deploy, `scripts/deploy-homolog.sh`).
4. **CA 7 a 9 (ensaio e rollback numa cópia):** **substituídos** pela virada no próprio lugar, com os dumps de antes da virada e o vhost antigo do Dolibarr guardado (`go-live.md` §6 e §12). Sem ensaio cronometrado.
5. **CA 10 (script de acessos):** não ensaiado numa cópia; rodado direto na virada, com as contagens no `go-live.md` §12 (a conferência de refresh e JWT antigos com 401 não foi registrada).
6. **CA 11 (script do Dolibarr):** não usado: o Dolibarr foi **desligado e removido** da VPS no mesmo dia, com backup final e teste de restauração (GOL-01b).
7. **CA 1 l (data):** dispensado; virada em 02/11/2026 (segunda-feira, 17:16 de São Paulo, dentro do horário comercial), decidida pelo PO no mesmo dia, sem a janela combinada antes.
8. **CA 1 k, CA 12 e CA 13 (métricas, treinamento e comunicados):** ficam para **depois da virada**, na GOL-01b. O `demo` não existe na stack de produção: o treinamento não pode usá-la.
9. **CA 1 a a 1 h e 1 j:** sem evidência registrada nesta story nem no `go-live.md` §1; a virada foi feita por decisão do PO.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da GOL-01) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (D1 nova, D2, D3, D6, D7, D14, D19, tabela 3.2 pontos 74–78); ambiente novo (ADR 0034); re-estimada para G; nova ordem (Marco final "Go-live", ordem 1); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
| 02/11/2026 | 0.5 | Decisões do PO de 02/11/2026: contadora e Inter fora do Go, num checklist novo "Antes da primeira operação fiscal e bancária" (CA 14, com as marcas de produção, o teste de conexão do Inter, a NFS-e de Caçapava e a primeira NF-e e o primeiro boleto com o PO); Fase 8 fora do Go (CA 1 a: Fases 0 a 7); D6 fora do Go (CA 1 f informativo); backup B2 ligado no ambiente novo nesta story (CA 1 i, CA 3); data da virada em aberto (CA 1 l, 13); EmissorFiscal publicado (`be1253e`) e sem rede `bqe-fiscal` (CA 1 b, CA 3); runbook `docs/operacao/go-live.md` escrito (CA 2) | River (SM) |
| 02/11/2026 | 1.0 | `Done`. Decisão do PO à noite: a homologação virou a produção (ADR 0034, adendo; ADR 0020); ambiente novo, recifração e B2 para a fase 2; ensaio numa cópia trocado pela virada no lugar com dumps e rollback guardado; data, métricas, treinamento e comunicados dispensados ou para depois. Desvios em "Desvios na entrega"; execução em `go-live.md` §12 | @dev |
