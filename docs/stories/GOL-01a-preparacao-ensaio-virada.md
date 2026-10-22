# GOL-01a — Go-live Konebras: critérios de entrada, ambiente de produção novo, runbook e ensaio completo da virada

| Campo | Valor |
|---|---|
| ID | GOL-01a |
| Título | Tudo o que precisa estar pronto e ensaiado antes do dia da virada: critérios de entrada do go-live (Fases 0 a 8 concluídas, trilha externa, Configurações → Fiscal conferida, 2FA de novo e estornos com aprovação, contagem física, folha pela RH-02c), ambiente de produção **novo** (ADR 0034: catálogo só com o `konebras`, key ring e certificado do Data Protection novos), inventário de segredos, runbook de virada e de rollback, scripts de revogação de sessões e de fechamento do Dolibarr, ensaio completo e rollback numa cópia restaurada (obrigatórios), treinamento, comunicação aos usuários e aos clientes, e o "Go/No-go" do PO |
| Tamanho | G (re-estimada de M: o ambiente de produção passou a ser novo, com key ring novo e recifração dos segredos; entram os scripts de revogação de sessões e do Dolibarr, o inventário de segredos completo e a lista de entrada da D7 inteira) |
| Status | Ready (pré-condição externa) |
| Requisitos | PRD §7 ("go-live e desligamento do Dolibarr") · PRD §1.2 (métricas) · PRD §6 Disponibilidade, Backup, Segurança · R-PLT-2 · R-TER-5 · R-SEC-3 · R-SEC-5 |
| Dependências | **Fases 0 a 8 concluídas** (todas as stories `Done`, a última sendo a WPP-01); PR de reforço da D19; FND-10b (homologação, backup, monitor); MIG-01a/b/c (cargas reexecutáveis); ADR 0034 aceita; trilha externa da D2 (pré-condição externa) |
| Bloqueia | GOL-01b |
| Ordem de execução | 1 (Marco final "Go-live") |

## User story
Como **PO**, quero **ensaiar a virada inteira numa cópia, num ambiente de produção novo, antes de fazê-la de verdade, com o caminho de volta escrito e testado**, para **trocar o Dolibarr pelo BemAqui num dia só, sem perder dado, sem levar chave nem sessão da homologação e sem improvisar**.

## Contexto e referências
- **ADR 0034** (promoção a produção: ambiente novo, segredos, sessões, Dolibarr, rollback; altera a ADR 0020 no ponto do key ring, que deixa de acompanhar o banco), ADR 0008 adendo de 21/10/2026 (D6), ADR 0027 (EmissorFiscal: rede `bqe-fiscal`, lacunas D3 com dono e prazo), ADR 0032 (Inter), ADR 0042 (Configurações → Fiscal e campos críticos conferidos).
- **Revisão pré-Fases 4 a 8** (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`): D1 (mudada pelo PO: tudo antes do go-live), D2, D3, D6, D7, D19; tabela 3.2, pontos 74 a 78; seção 2 (perguntas à contadora, ao Inter, ao jurídico e à Konebras).
- `05-backlog` GOL-01: promoção da homologação a produção, troca de domínio (`app.konebras.com.br` para o BemAqui e o Dolibarr para `adm.konebras.com.br`), segredos de produção, carga final do Dolibarr só com os cadastros alterados (MIG-01b, MIG-01c, ADR 0017), `DROP USER` do somente leitura, treinamento, checklist de virada e primeira semana.
- ADR 0020 (homologação com dado real promovida; nesta story, pelo ambiente novo da ADR 0034), ADR 0004 e 0005 D1 (Dolibarr em `adm.`; Konebras UI desativado junto), ADR 0015 (`deploy-prod.yml` desligado por `vars.PROD_DEPLOY`; compose em `/opt/projetos/bemaqui-empresas/`; vhost com `nginx -t`), ADR 0007 (key ring, certificado de proteção, recifração), ADR 0017 adendo 2 (a chave da API do Dolibarr deixa de ser usada e o secret é apagado), ADR 0018 (homologação com dado real segue regras de produção), ADR 0010 itens 13 e 14.
- `06-pendencias-riscos`: P-01, P-02, P-03, P-08, P-19 e P-20 (decididas pela D6), P-24.
- Runbooks: `docs/operacao/deploy.md`, `restauracao.md`, `segredos-e-chaves.md`, `migracao-dolibarr.md`, `monitoramento.md`, `provisionamento.md`.

## Critérios de aceite
**Critérios de entrada (go/no-go)**
1. **Dado** a lista de critérios de entrada em `docs/operacao/go-live.md`, **quando** o PO revisa, **então** cada item abaixo está marcado com evidência (link, data, responsável) e o PO registra "Go" ou "No-go" com data no runbook:
   - a. **Fases 0 a 8 concluídas**: todas as stories `Done` no backlog (a Fase 4 inteira, sem exceção; Fases 6, 7 e 8 inteiras) e o PR de reforço da D19 no ar.
   - b. **Trilha fiscal externa:** P-01 (regime), **P-02 (cartão CNPJ com o CNAE de fabricação; sem ele não há NF-e de produção própria)** e P-03 (IBS/CBS) respondidos pela contadora; **certificado A1 válido por mais de 60 dias no cofre do EmissorFiscal** (o BemAqui mostra só validade, titular e thumbprint, FIS-02a); EmissorFiscal implantado com as **lacunas da D3 entregues** (reconciliação com a SEFAZ e consulta forçada, DANFE em PDF, `transp` e volumes, IBS/CBS, `GET /me` e escopo `nfe:eventos`, timestamp no HMAC, senha do PFX em `[FromForm]`, CNPJ do certificado conferido, rede `bqe-fiscal`, banco sem superusuário, Swagger desligado, um tenant do Emissor por tenant, cópia offline da chave-mestra) e o **provider da NFS-e de Caçapava** (FIS-06).
   - c. **Configurações → Fiscal** sem nenhum campo crítico com o chip "A confirmar com a contadora" (regime, CRT, série exclusiva, CFOP/CST padrão, alíquotas, código de serviço da NFS-e, ISS e retenções, IBS/CBS; FIS-02a), e as regras fiscais detalhadas (FIS-02b) conferidas.
   - d. **Homologação:** SEFAZ dos 3 cenários (FIS-03a) e dos eventos (FIS-03b), NFS-e (FIS-06), fluxo completo do Inter no sandbox (BNK-01b, BNK-02, BNK-03a, BNK-03b), tudo no tenant `demo`.
   - e. **Inter:** conta PJ de produção com API (P-08) e credencial de produção só com cobrança, extrato e webhook; o **teste de conexão de produção** fica marcado para o D−1 (GOL-01b), e o "Go" é condicional a ele.
   - f. **D6 aplicada (P-19 e P-20):** 2FA de novo (`RequireRecentTwoFactor`) em pagar, estornar, cancelar ou inutilizar NF-e, trocar conta de salário, fechar a folha, anonimizar titular e salvar credenciais do Inter e do A1, e estorno de pagamento e de baixa bancária com `finance.approve`, conferidos pelo teste de arquitetura da lista da D6.
   - g. **Estoque:** contagem física feita e saldo inicial lançado na janela de implantação (D7 da Fase 3), com o relatório de diferenças aceito.
   - h. **Pessoas:** usuários do chão de fábrica com e-mail da empresa (P-24), todo usuário real com convite aceito e 2FA ativo quando exigido (ADR 0010); a **folha do mês anterior lançada pela RH-02c** (um título por funcionário e um por guia, ADR 0036) no BemAqui. A **virada da folha calculada** (FLH-06: 3 competências seguidas aceitas em paralelo com a lançada) **é operação e não segura o go-live**: no dia da virada a folha oficial é a lançada (RH-02a/02c), e a FLH-06 corre depois, com os datasets da contadora conferidos.
   - i. **Backup e volta:** backup diário verificado nos últimos 7 dias; **teste de restauração** do tenant `konebras` e **rollback** ensaiados numa cópia (CA 7 e 9), obrigatórios.
   - j. **Operação:** monitor externo do host de produção configurado; SPF/DKIM/DMARC do domínio de envio; inventário de segredos completo (CA 5); scripts de sessões e do Dolibarr ensaiados (CA 10 e 11).
   - k. **Métrica:** linha de base do "desvio da data prevista ≤ 15%" medida nos projetos em andamento (PRD §1.2), junto com as outras métricas do §1.2.
   - l. **Data:** virada no **início de um mês**, fora do horário comercial, com a janela aprovada pela Konebras.

**Runbook e ambiente de produção novo**
2. **Dado** o runbook `docs/operacao/go-live.md`, **quando** está pronto, **então** descreve, com comando, responsável, tempo estimado e verificação de cada passo: D−1 (teste de conexão do Inter de produção, backups extras), congelamento do Dolibarr, carga final, promoção do banco para o ambiente novo, recifração dos segredos, revogação de sessões, troca de host, vhosts, certificados, ligação do pipeline de produção, fiscal e Inter em produção, smoke, comunicação e o **rollback** (CA 9), com o ponto sem retorno marcado.
3. **Dado** a ADR 0034, **quando** o ambiente de produção é preparado, **então** é **novo** em `/opt/projetos/bemaqui-empresas/` (compose `bqe-prod-*`), com **catálogo novo só com o tenant `konebras`**, **key ring e certificado do Data Protection novos** e as cadeias de conexão cifradas de novo; segue o endurecimento da ADR 0015 (rede interna, não-root, `read_only`, limites), a rede dedicada `bqe-fiscal` com o EmissorFiscal (ADR 0027), passa no `check-compose.mjs`, e o vhost de produção (`bemaqui-empresas.conf`, log `bqe_masked`, WebSocket, `/webhooks/` fora da allowlist e com a máscara do token, FND-16 e BNK-02) passa no `nginx -t` num ensaio local.
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
12. **Dado** os grupos de usuários (Proprietário, Financeiro, Comercial, Engenharia, Supervisão, Operadores, Almoxarifado, RH), **quando** o treinamento é feito na homologação (tenant `demo`), **então** cada grupo faz o roteiro do seu dia a dia (por grupo, em `docs/operacao/go-live.md`, inclusive o 2FA de novo nas ações da D6) e a lista de presença e as dúvidas ficam registradas.
13. **Dado** a data da virada escolhida pelo PO, **quando** faltam 5 dias úteis, **então** estão aprovados pelo PO: o comunicado aos usuários (o que muda, data e hora do congelamento do Dolibarr, onde pedir ajuda) e o **e-mail aos clientes da Konebras** com a data da virada e o boleto novo (Inter pelo BemAqui) e a nota emitida pelo sistema novo, enviado na GOL-01b.

## Regras de negócio e técnicas
- **Nada de dado real em ambiente de teste fora do runbook** (ADR 0020, ADR 0034): o ensaio usa cópia restaurada, descartada depois, com o registro em `docs/operacao/restauracao.md`.
- **Ambiente novo (ADR 0034):** produção nunca herda chave, sessão nem segredo da homologação; o banco do `konebras` é o único dado que passa, recifrado.
- **Ponto sem retorno:** a primeira nota fiscal autorizada em produção ou o primeiro boleto real emitido pelo BemAqui.
- **Não há código de produto novo** nesta story: só infraestrutura, runbook, scripts de operação e evidências; qualquer correção de produto vira story própria.

## Permissões, eventos e tabelas
- **Permissões:** nenhuma nova. **Tabelas:** nenhuma nova (`catalog.tenant_domains`, `catalog.tenants.fiscal_production_allowed` e `catalog.tenants.bank_production_allowed` mudam na GOL-01b).
- **Arquivos:** `docs/operacao/go-live.md` (novo), compose e vhost de produção, scripts de acessos, do Dolibarr e de conferência, `deploy-prod.yml` revisado com environment protection (ainda desligado).

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

**Pré-condição externa:** a trilha externa da D2 com dono e data (contadora: P-01, P-02, P-03 e a conferência de Configurações → Fiscal; Konebras: certificado A1, conta Inter de produção, contagem física, e-mails dos operadores, janela da virada; trabalho no EmissorFiscal: lacunas da D3 e provider de Caçapava); não bloqueia o `Ready`, bloqueia o "Go".

## Definition of Done
- [ ] Critérios de entrada com evidência e "Go" do PO registrado (CA 1).
- [ ] Runbook de virada e rollback revisado pelo PO (CA 2, 9).
- [ ] Ambiente de produção novo com compose e vhost aprovados no CI (CA 3); recifração ensaiada (CA 4).
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

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da GOL-01) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (D1 nova, D2, D3, D6, D7, D14, D19, tabela 3.2 pontos 74–78); ambiente novo (ADR 0034); re-estimada para G; nova ordem (Marco final "Go-live", ordem 1); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
