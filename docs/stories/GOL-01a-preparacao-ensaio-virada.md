# GOL-01a — Go-live Konebras: preparação, runbook e ensaio completo da virada

| Campo | Valor |
|---|---|
| ID | GOL-01a |
| Título | Tudo o que precisa estar pronto e ensaiado antes do dia da virada: critérios de entrada do go-live, runbook de virada e de rollback, compose e vhost de produção, segredos de produção, fiscal e Inter de produção, ensaio completo da promoção numa cópia restaurada do banco do tenant `konebras`, treinamento por grupo e o "go/no-go" do PO |
| Tamanho | M (quebra da GOL-01) |
| Status | Draft |
| Requisitos | PRD §7 Fase 5 ("go-live e desligamento do Dolibarr") · PRD §1.2 (métricas) · PRD §6 Disponibilidade, Backup, Segurança · R-PLT-2 · R-TER-5 |
| Dependências | Fases 0 a 4 concluídas; na Fase 5: FND-16, FIS-01, FIS-02a, FIS-02b, FIS-03a, FIS-03b, FIS-04a, FIS-04b, BNK-01a, BNK-01b, BNK-02, BNK-03a, BNK-03b (FIS-05, FIS-06 e FIN-09 conforme o ponto em aberto 1); FND-10b (homologação, backup, monitor); MIG-01a/b/c (cargas reexecutáveis); ADR 0020 |
| Bloqueia | GOL-01b |
| Ordem de execução | 17 (Marco 5.7 "Go-live") |

## User story
Como **PO**, quero **ensaiar a virada inteira numa cópia antes de fazê-la de verdade, com o caminho de volta escrito e testado**, para **trocar o Dolibarr pelo BemAqui num dia só, sem perder dado e sem improvisar**.

## Contexto e referências
- `05-backlog` GOL-01: promoção da homologação a produção (ADR 0020), troca de DNS e domínio (`app.konebras.com.br` para o BemAqui e o Dolibarr para `adm.konebras.com.br`), segredos de produção e chaves de Data Protection; carga final do Dolibarr só com os cadastros alterados depois da última carga (MIG-01b, MIG-01c, ADR 0017); `DROP USER` do somente leitura e remoção do segredo; treinamento; checklist de virada; acompanhamento da primeira semana.
- ADR 0020 (a GOL-01 é promoção: "o banco do tenant `konebras` muda de ambiente (ou o ambiente de homologação vira o de produção)"; host primário em `tenant_domains` muda; segredos de produção; key ring acompanha o banco; `deploy-prod.yml` ligado).
- ADR 0004 e 0005 D1 (Dolibarr em `adm.konebras.com.br` até o desligamento; Konebras UI desativado junto), ADR 0015 (`deploy-prod.yml` desligado por `vars.PROD_DEPLOY`; compose em `/opt/projetos/bemaqui-empresas/`; vhost instalado à mão com `nginx -t`), ADR 0007 (key ring, certificado de proteção, recifração antes de trocar certificado), ADR 0017 adendo 2 (leitura pela API REST do Dolibarr: na GOL-01 a chave deixa de ser usada e o secret é apagado), ADR 0018 (homologação com dado real segue regras de produção), ADR 0010 item 13 e 14 (produção desligada até a GOL-01; UptimeRobot).
- Runbooks: `docs/operacao/deploy.md`, `restauracao.md`, `segredos-e-chaves.md`, `migracao-dolibarr.md`, `monitoramento.md`, `provisionamento.md`.

## Critérios de aceite
**Critérios de entrada (go/no-go)**
1. **Dado** a lista de critérios de entrada do go-live (proposta, em `docs/operacao/go-live.md`): stories obrigatórias `Done` (Dependências), homologação SEFAZ dos 3 cenários (FIS-03a CA 19) e dos eventos (FIS-03b), fluxo completo do Inter no sandbox (BNK-01b, BNK-02), certificado A1 válido por mais de 60 dias, conta Inter de produção com API habilitada (P-08), backup diário verificado nos últimos 7 dias, **teste de restauração do tenant `konebras` feito** (recomendado pela ADR 0020; aqui passa a ser **obrigatório**, Pontos em aberto 2), monitor externo do host de produção configurado, SPF/DKIM/DMARC do domínio de envio, e nenhuma pendência fiscal aberta que bloqueie a emissão (P-01, P-03), **quando** o PO revisa, **então** cada item está marcado com evidência (link, data, responsável), e o PO registra "Go" ou "No-go" com data no runbook.

**Runbook e infraestrutura de produção**
2. **Dado** o runbook `docs/operacao/go-live.md`, **quando** está pronto, **então** descreve, com comando, responsável, tempo estimado e verificação de cada passo: congelamento do Dolibarr, carga final, promoção do banco, troca de segredos, troca de host, vhosts, certificados, ligação do pipeline de produção, fiscal e Inter em produção, smoke de leitura, comunicação aos usuários e o **rollback** (CA 8), com o ponto sem retorno marcado.
3. **Dado** a decisão do ponto em aberto 3 (promover o banco para um ambiente de produção novo em `/opt/projetos/bemaqui-empresas/` ou transformar a homologação em produção), **quando** o compose de produção é preparado, **então** segue o mesmo endurecimento da ADR 0015 (nomes `bqe-prod-*`, rede interna, não-root, `read_only`, limites, rede dedicada com o EmissorFiscal da FIS-01 CA 14), passa no `check-compose.mjs`, e o vhost de produção (`bemaqui-empresas.conf`, log `bqe_masked`, WebSocket, `/webhooks/`) passa no `nginx -t` num ensaio local.
4. **[Segurança]** **Dado** os segredos de produção (assinatura do JWT, senha do Postgres, SMTP, conexão do catálogo, chave do EmissorFiscal de produção, credenciais e certificado do Inter de produção), **quando** são gerados, **então** são novos (nenhum reaproveitado da homologação, exceto o key ring e o certificado de proteção do Data Protection, que acompanham o banco, ADR 0020), ficam só em arquivos de secret 0400 e no cofre do PO, e a lista deles com dono e data de rotação está no runbook, sem nenhum valor.
5. **Dado** o tenant de teste `demo` e os demais tenants de teste da homologação, **quando** a promoção for feita, **então** o runbook diz o que acontece com eles (proposta: ficam na homologação, que continua existindo para testes; a produção nasce só com o catálogo do `konebras`), e nenhum dado de teste entra na produção.

**Ensaio completo**
6. **Dado** uma cópia restaurada do backup real do tenant `konebras` e do catálogo num ambiente separado (nunca a homologação em uso), **quando** o ensaio roda o runbook inteiro (inclusive a carga final do Dolibarr em `--dry-run` pela MIG-01a, a troca de segredos, o migrator, a mudança do host primário em `tenant_domains` para um host de ensaio, a decifração de um segredo de cada tipo e o smoke), **então** tudo passa, o tempo total é medido e registrado, e o que falhou volta ao runbook antes do dia da virada.
7. **Dado** o ensaio, **quando** os dados promovidos são conferidos, **então** o relatório de conferência mostra: saldo de cada conta bancária igual ao extrato do banco no dia do ensaio (FIN-00/BNK-03b), total a receber e a pagar em aberto, número de terceiros, itens, contratos e projetos, e nenhuma nota fiscal de ambiente de homologação no tenant (ou a regra decidida na FIS-01 ponto 3).
8. **Dado** o rollback, **quando** é ensaiado, **então** a volta do `app.konebras.com.br` para o Dolibarr (vhost anterior guardado, Dolibarr descongelado) funciona em menos de 30 min (proposta) e o runbook diz o que fazer com o que foi lançado no BemAqui entre a virada e a volta (proposta: lista exportada para relançar no Dolibarr; nota fiscal autorizada não volta: segue no EmissorFiscal), e qual é o prazo máximo para decidir voltar (proposta: até o fim do primeiro dia útil).

**Treinamento e comunicação**
9. **Dado** os grupos de usuários (Proprietário, Financeiro, Comercial, Engenharia, Supervisão, Operadores, Almoxarifado), **quando** o treinamento é feito na homologação (tenant `demo`), **então** cada grupo faz o roteiro do seu dia a dia (proposta, por grupo, em `docs/operacao/go-live.md`) e a lista de presença e as dúvidas ficam registradas; cada usuário real da Konebras tem convite aceito e 2FA ativo quando exigido (ADR 0010).
10. **Dado** a data da virada escolhida pelo PO, **quando** faltam 5 dias úteis, **então** o comunicado aos usuários (o que muda, data e hora do congelamento do Dolibarr, onde pedir ajuda) e ao cliente final, se houver mudança de boleto ou de nota, está aprovado pelo PO.

## Regras de negócio e técnicas
- **Nada de dado real em ambiente de teste fora do runbook** (ADR 0020): o ensaio usa cópia restaurada, descartada depois, com o registro em `docs/operacao/restauracao.md`.
- **Ponto sem retorno:** a primeira nota fiscal autorizada em produção ou o primeiro boleto real emitido pelo BemAqui.
- **Não há código de produto novo** nesta story: só infraestrutura, runbook, scripts e evidências; qualquer correção de produto vira story própria.

## Permissões, eventos e tabelas
- **Permissões:** nenhuma nova. **Tabelas:** nenhuma nova (`catalog.tenant_domains` muda na GOL-01b; `catalog.tenants.fiscal_production_allowed` da FIS-02a é ligada lá).
- **Arquivos:** `docs/operacao/go-live.md` (novo), compose e vhost de produção, `deploy-prod.yml` revisado (ainda desligado).

## Telas e estados
- Nenhuma tela nova. A tela inicial ganha, só na homologação e até a virada, a faixa "Ambiente de homologação com dados reais. A troca para app.konebras.com.br será em dd/mm." (proposta, texto aprovado pelo PO).
- **Ajuda:** `Ajuda/` sem mudança; o roteiro de treinamento fica em `docs/operacao/`.

## Plano de testes
- **Infra:** `check-compose.mjs` e `nginx -t` do compose e do vhost de produção (CA 3).
- **Ensaio (manual, registrado):** runbook completo numa cópia restaurada, com tempo, conferência dos dados e decifração dos segredos (CA 6, 7); rollback (CA 8).
- **Segurança:** varredura dos arquivos de secret (permissão 0400, dono), conferência de que nenhum segredo da homologação foi reaproveitado (CA 4).
- **E2E:** suíte E2E contra o ambiente de ensaio num tenant de teste (nunca o `konebras`).

## Fora do escopo
- A virada em si e a primeira semana: GOL-01b. Desligamento definitivo do Dolibarr: GOL-01b (data) e depois.

## Pontos em aberto
1. **Stories obrigatórias para o go-live** (PO): proposta: FIS-05, FIS-06 e FIN-09 não bloqueiam (FIS-06 e FIN-09 são D); mas sem a FIS-06 a NFS-e da instalação fica fora do sistema (FIS-06 ponto 1).
2. **Teste de restauração obrigatório antes da virada** (PO): a ADR 0020 deixou recomendado; proposta: obrigatório para o go-live.
3. **Forma da promoção** (PO/arquiteto, ADR 0020): ambiente de produção novo com o banco promovido (proposta, separa homologação e produção daqui para frente) ou a homologação vira produção.
4. **Data da virada e janela** (PO): fora do horário comercial (PRD §6 Disponibilidade), em início de mês para o fechamento ficar inteiro num sistema só (proposta).
5. **Fase 4** (PO): o PRD prevê o go-live ao fim da Fase 5 com a Fase 4 concluída (regra do backlog "não comece uma fase sem a anterior"); confirmar se algum item da Fase 4 pode ficar para depois do go-live.

## Definition of Done
- [ ] Critérios de entrada com evidência e "Go" do PO registrado (CA 1).
- [ ] Runbook de virada e rollback revisado pelo PO (CA 2, 8).
- [ ] Compose e vhost de produção aprovados no CI (CA 3).
- [ ] Segredos de produção gerados e listados sem valor (CA 4).
- [ ] Ensaio completo e rollback ensaiado, com tempos e conferência registrados (CA 6–8).
- [ ] Treinamento por grupo e comunicado aprovados (CA 9, 10).
- [ ] `docs/02-arquitetura.md` §7 atualizado (produção); story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `docs/operacao/go-live.md` (critérios de entrada, runbook, rollback, roteiros de treinamento).
- [ ] Compose `deploy/prod/` e vhost de produção; `deploy-prod.yml` revisado.
- [ ] Script de conferência dos dados promovidos (só leitura, saída sem dado pessoal).

## Quality gates
- Agentes previstos: @devops, @architect, cybersecurity (segredos, vhost, rollback), @qa, PO.
- Gate: CA 1, 4, 6 e 8 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026.

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK (não há endpoint novo; segurança dos segredos e da virada) |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK (faixa de aviso; sem tela nova) |
| 10 | DoD ajustado; pontos em aberto registrados sem decidir | OK |

- **Correções feitas na validação:** a GOL-01 foi quebrada em preparação/ensaio (01a) e virada/primeira semana (01b), para o ensaio completo e o rollback testado virem antes do dia; segredos da homologação não reaproveitados, exceto o key ring (CA 4); teste de restauração como proposta de critério obrigatório (ponto 2).
- **Pré-condições para `Ready`:** pontos em aberto 1, 3 e 5.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da GOL-01) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
