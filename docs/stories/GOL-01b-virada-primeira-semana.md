# GOL-01b — Go-live Konebras: a virada, o corte do Dolibarr e a primeira semana

| Campo | Valor |
|---|---|
| ID | GOL-01b |
| Título | Execução da virada pelo runbook ensaiado (`docs/operacao/go-live.md`): D−1, aviso por e-mail aos clientes, congelamento do Dolibarr, carga final só dos cadastros alterados, promoção do banco do tenant `konebras` para o ambiente de produção novo (ADR 0034), recifração e segredos novos, revogação de todas as sessões e do security stamp, `app.konebras.com.br` apontando para o BemAqui e o Dolibarr em `adm.konebras.com.br` só leitura com allowlist e `DOLAPIKEY` regenerada, `DROP USER` e remoção dos segredos da migração, smoke; depois, com o sistema no ar, o checklist **"Antes da primeira operação fiscal e bancária"** (SEFAZ e Inter em produção, GOL-01a CA 14); acompanhamento diário da primeira semana, fechamento do go-live, limpeza da homologação e a data de desligamento do Dolibarr |
| Tamanho | M (quebra da GOL-01) |
| Status | InProgress (virada feita em 02/11/2026; faltam a primeira semana, o checklist "Antes da primeira operação fiscal e bancária", a cópia do Dolibarr fora da VPS e o DNS) |
| Requisitos | PRD §7 ("go-live e desligamento do Dolibarr") · PRD §1.2 (métricas de sucesso) · R-PLT-2 · R-TER-5 · R-FIS-2 · R-FIN-7 · R-SEC-3 · PRD §6 Disponibilidade, Backup, Observabilidade |
| Dependências | GOL-01a ("Go" do PO registrado, com os scripts ensaiados) |
| Bloqueia | — (última story do planejamento atual) |
| Ordem de execução | 2 (Marco final "Go-live", última) |

## User story
Como **proprietário da Konebras**, quero **começar a trabalhar no BemAqui em `app.konebras.com.br` num dia combinado, com tudo o que estava na homologação, e ter alguém olhando de perto na primeira semana**, para **desligar o Dolibarr com segurança**.

## Contexto e referências
- **ADR 0034** (promoção a produção: ambiente novo, segredos, sessões, Dolibarr, rollback), ADR 0008 adendo de 21/10/2026 (D6), ADR 0032 (Inter de produção), ADR 0027 (EmissorFiscal de produção).
- **Revisão pré-Fases 4 a 8** (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`): D1 (SEFAZ e Inter em produção só aqui), D7; tabela 3.2, pontos 79 a 81.
- `05-backlog` GOL-01 e ADR 0020 (promoção, agora pelo ambiente novo da ADR 0034), ADR 0004, 0005 D1 (Dolibarr em `adm.konebras.com.br`; Konebras UI desativado), ADR 0017 (carga final só de cadastros; `DROP USER bqe_migracao_ro` e remoção do secret; adendo 2: a chave da API do Dolibarr deixa de ser usada e o secret é apagado), ADR 0015, ADR 0007, ADR 0010 item 13.
- PRD §1.2: "100% dos pagamentos passando por ordem de pagamento aprovada", "Dolibarr desligado", "Fechamento financeiro mensal em menos de 1 dia", "desvio da data prevista ≤ 15%".
- Runbook `docs/operacao/go-live.md` (GOL-01a).

## Checklist de virada (resumo; o detalhe é o runbook da GOL-01a, `docs/operacao/go-live.md` §5)
1. **D−1:** e-mail aos clientes lembrado; backup extra do Dolibarr (MariaDB e arquivos) e do BemAqui verificado; certificados de `app`, `adm` e `plataforma` válidos; A1 válido no cofre do EmissorFiscal; imagem de produção escolhida. O teste de conexão do Inter de produção **não adia mais a virada** (decisão do PO de 02/11/2026): passa para o checklist do item 8.
2. **Corte do Dolibarr (hora H):** Dolibarr em modo só leitura (perfis sem escrita) e Konebras UI fora do ar; última hora de lançamento registrada.
3. **Carga final:** MIG-01b e MIG-01c (API REST do Dolibarr) só com os cadastros alterados depois da última carga, primeiro em `--dry-run`, relatório aceito pelo PO, depois a real; conflitos resolvidos à mão.
4. **Limpeza da migração e Dolibarr:** script do Dolibarr da GOL-01a: `DOLAPIKEY` regenerada e usuários dela desativados; `adm.konebras.com.br` só leitura com allowlist de IP ou autenticação básica; `DROP USER bqe_migracao_ro` com revogação na rede; secret da chave da API do Dolibarr e arquivo de-para apagados; staging expurgada (`--purge-staging --accepted`).
5. **Promoção:** backup final da homologação; banco do `konebras` promovido ao ambiente de produção novo (catálogo novo só com ele, key ring e certificado do Data Protection novos, ADR 0034); recifração dos segredos por tenant; migrator; segredos de produção novos.
6. **Acessos:** script de acessos da GOL-01a: todas as famílias de refresh revogadas, security stamp de todos os usuários subido, convites, redefinições, PINs, aparelhos e quiosques expirados, usuários e convites de teste apagados, super admins conferidos pelo PO.
7. **Domínio:** `app.konebras.com.br` → vhost do BemAqui; Dolibarr em `adm.konebras.com.br`; `tenant_domains` com `app.konebras.com.br` primário; `homolog.konebras.com.br` deixa de servir o tenant `konebras`.
8. **Pipeline e integrações:** na noite da virada, `deploy-prod.yml` ligado com environment protection, jobs recorrentes ativos, `tenantKey` novo, webhooks e integrações de sandbox desligados. **Depois, com o sistema no ar e antes da primeira operação** (checklist "Antes da primeira operação fiscal e bancária", GOL-01a CA 14, `go-live.md` §2): trilha da contadora e Configurações → Fiscal conferidas; NFS-e de Caçapava; EmissorFiscal com o emitente em produção e a marca `fiscal_environment_allowed = 'Production'` (a antiga "fiscal_production_allowed"; FIS-02a); série de produção; Inter de produção com o teste de conexão aprovado, a marca `bank_production_allowed` ligada (BNK-01a), credenciais, certificado e webhook registrado no host de produção (BNK-02); segredos e tokens de webhook novos, cadastrados no Inter e no EmissorFiscal de produção.
9. **Smoke (só leitura):** `/health/ready`, login de um Proprietário com 2FA, saldo das contas igual ao extrato do dia, conferência dos números. A **primeira nota fiscal real e o primeiro boleto real** (o ponto sem retorno) são feitos com o PO acompanhando, no fim do checklist do item 8, não necessariamente na noite da virada.
10. **Rollback:** disponível até o fim do primeiro dia útil, pelo runbook (GOL-01a CA 9).
11. **Primeira semana e fechamento:** acompanhamento diário (CA 7 a 9); fechamento do go-live (CA 12); limpeza da homologação (CA 11).

## Critérios de aceite
1. **Dado** o "Go" da GOL-01a, **quando** a virada é feita, **então** cada passo do checklist é executado pelo runbook, marcado com hora, responsável e verificação no registro da virada (`docs/operacao/go-live.md`, seção "Execução"), e qualquer desvio fica descrito. O teste de conexão do Inter de produção sai do D−1 e entra no checklist "Antes da primeira operação fiscal e bancária" (GOL-01a CA 14); a falha dele **não adia a virada**, só a primeira cobrança pelo Inter (decisão do PO de 02/11/2026).
2. **Dado** a data da virada, **quando** faltam 5 dias úteis e de novo no D−1, **então** o **e-mail aos clientes da Konebras** aprovado na GOL-01a (data da virada, boleto novo pelo Inter, nota pelo sistema novo, contato para dúvidas) sai pela fila do BemAqui ou pelo e-mail da Konebras, e o envio fica registrado (quantidade e falhas, sem a lista de destinatários no runbook).
3. **[Segurança]** **Dado** o fim da carga final, **quando** a limpeza e o script do Dolibarr rodam, **então** o usuário somente leitura do Dolibarr não existe mais e a rede não deixa mais o BemAqui chegar ao banco dele (conferido por consulta e por tentativa de conexão), a `DOLAPIKEY` antiga não funciona mais (conferido por chamada), o `adm.konebras.com.br` recusa acesso fora da allowlist ou sem a autenticação básica e não aceita escrita, os secrets da migração não existem mais nos ambientes, o pacote de snapshot (se usado) foi apagado e a staging foi expurgada (ADR 0017, ADR 0018).
4. **[Segurança]** **Dado** a promoção, **quando** o script de acessos roda e o primeiro acesso a `https://app.konebras.com.br` acontece, **então** **todas as famílias de refresh foram revogadas e o security stamp de todos os usuários subiu** (o refresh não fica preso ao host, então tokens e cookies de `homolog.konebras.com.br` não podem valer no host novo; correção do antigo CA 3, que confiava só no `aud` do JWT), um refresh e um JWT emitidos antes da virada dão 401 (conferido), convites, redefinições, PINs, aparelhos e quiosques antigos não valem, o tenant `konebras` é resolvido pelo host novo, o login funciona com os mesmos usuários e 2FA, e o relatório de conferência da GOL-01a CA 8 bate com os números do dia.
5. **[Segurança]** **Dado** a produção, **quando** os segredos são conferidos, **então** nenhum é o da homologação (nem o key ring nem o certificado de proteção, ADR 0034), um segredo de cada tipo decifra com o key ring novo e nenhum decifra com o antigo, e o monitor externo e os alertas de backup, jobs e integrações (FND-12) apontam para a produção.
6. **Dado** o checklist "Antes da primeira operação fiscal e bancária" fechado (GOL-01a CA 14), **quando** a primeira nota fiscal real e o primeiro boleto real são feitos com o PO, **então** a nota é autorizada em produção pela SEFAZ e o boleto aparece no Internet Banking do Inter; a baixa do boleto, quando pago, chega pelo webhook de produção (com reconsulta por mTLS, BNK-02); o `EnvironmentBanner` some no `konebras`. O envio de WhatsApp (WPP-01) **sai do go-live** com a Fase 8 (decisão do PO de 02/11/2026) e liga quando a story for entregue.
7. **Dado** cada dia útil da primeira semana, **quando** o acompanhamento diário é feito (30 min com o Financeiro e o PO), **então** são conferidos e registrados: saldo do sistema × banco por conta, boletos emitidos e baixados, pagamentos a conferir, notas autorizadas e rejeitadas, eventos de webhook com falha ou parados, jobs com falha, dead-letters, ações com 2FA de novo recusadas, chamados dos usuários; problema vira item com dono e prazo.
8. **Dado** um problema de severidade alta na primeira semana (dado errado com efeito financeiro ou fiscal, indisponibilidade acima de 1 h), **quando** acontece, **então** o PO decide no mesmo dia entre corrigir, contornar ou acionar o rollback (dentro do prazo da GOL-01a), e a decisão fica registrada.
9. **Dado** o Dolibarr em `adm.konebras.com.br`, **quando** a primeira semana termina, **então** continua **só leitura, com allowlist de IP ou autenticação básica**, para consulta do histórico (o que não migrou fica consultável lá, ADR 0017).
10. **Dado** o **primeiro fechamento mensal no BemAqui**, ou **3 meses** depois da virada (o que vier primeiro), **quando** o PO confirma, **então** o Dolibarr é desligado: backup final (MariaDB e arquivos) **cifrado**, no mesmo bucket do backup, com **retenção de 5 anos** (guarda fiscal e contábil), teste de restauração desse backup registrado, vhost `adm.` removido e o container parado; a data do desligamento fica no runbook e mede a métrica "Dolibarr desligado" do PRD §1.2.
11. **Dado** a produção no ar, **quando** a homologação é limpa, **então** ela recebe **key ring novo** logo depois da virada e, **depois do primeiro fechamento mensal em produção**, o **banco do `konebras` é apagado dela** (com o registro em `docs/operacao/restauracao.md`), o tenant `demo` e os de teste continuam, e o deploy de homologação segue para os testes (ADR 0034).
12. **Dado** o fim da primeira semana sem problema de severidade alta aberto, **quando** o PO revisa, **então** registra o fechamento do go-live (data), a linha de base das métricas do PRD §1.2 (pagamentos com OP, projetos com orçado × realizado, tempo do fechamento mensal, desvio da data prevista ≤ 15%, medido na GOL-01a) e a data da primeira medição de 3 meses.

## Regras de negócio e técnicas
- Nenhuma alteração de produto nesta story; correção de produto descoberta na virada vira story ou hotfix pelo fluxo normal (branch, PR, merge, deploy).
- Toda ação na produção pelo runbook e pelos scripts ensaiados na GOL-01a; diagnóstico com dado real só em cópia restaurada (ADR 0020, ADR 0034).
- **SEFAZ e Inter em produção só aqui** (D1), **depois da virada e antes da primeira operação** (02/11/2026): as marcas `fiscal_environment_allowed = 'Production'` e `bank_production_allowed` são ligadas pelo super admin da plataforma no checklist do passo 8 (GOL-01a CA 14), registradas no runbook. A contadora e o Inter não seguram a virada.

## Permissões, eventos e tabelas
- **Permissões:** nenhuma nova. **Tabelas:** `catalog.tenant_domains` (host primário), `catalog.tenants.fiscal_environment_allowed` (`Production`) e `catalog.tenants.bank_production_allowed` (ligada), sessões e security stamps (pelo script de acessos).

## Telas e estados
- A faixa de homologação da GOL-01a e o `EnvironmentBanner` de sandbox saem no `konebras`; nenhuma tela nova.

## Plano de testes
- **Smoke de produção** (só leitura + o teste controlado do CA 6), registrado.
- **Conferências** dos CA 3, 4, 5 e 7 registradas no runbook (refresh e JWT antigos com 401; `DOLAPIKEY` antiga recusada; acesso ao banco do Dolibarr recusado; segredos decifrando só com o key ring novo).
- **Desligamento do Dolibarr:** restauração do backup final de 5 anos testada (CA 10).

## Fora do escopo
- Melhorias e stories pós-MVP (Fase 9), que entram com o sistema em produção.

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fases 4 a 8, tabela 3.2, pontos 79–81):
1. Desligar o Dolibarr depois do 1º fechamento ou em até 3 meses; backup cifrado por 5 anos (CA 10).
2. Dolibarr só leitura, com allowlist de IP ou autenticação básica e a `DOLAPIKEY` regenerada (D7; CA 3, 9).
3. Aviso por e-mail aos clientes com a data da virada e o boleto novo (CA 2).

Decisões da revisão aplicadas: D1 (marco final "Go-live", última story; SEFAZ e Inter em produção só aqui, CA 6); D7 (revogar todas as famílias de refresh e subir o security stamp de todos, corrigindo o antigo CA 3; expirar convites, redefinições, PINs, aparelhos e quiosques; usuários de teste e super admins; webhooks e Inter de sandbox para produção; `DROP USER` com revogação na rede; linha de base da métrica; CA 3, 4, 12); ADR 0034 (ambiente novo, key ring novo, homologação com key ring novo e o `konebras` apagado depois do fechamento, CA 5, 11); Inter de produção testado no D−1 (CA 1).

Decididos pelo PO em 02/11/2026:
4. Contadora e Inter não seguram a virada; SEFAZ e Inter em produção pelo checklist "Antes da primeira operação fiscal e bancária" (GOL-01a CA 14), depois da virada (checklist itens 1, 8 e 9; CA 1 e 6).
5. Fase 8 fora do go-live: WhatsApp sai do CA 6.
6. Data da virada em aberto: **[data da virada, a definir pelo PO]** (recomendada 01/12/2026; alternativa 15–16/11/2026).

**Pré-condição externa:** janela da virada no início de um mês, fora do horário comercial, e presença do Financeiro e do PO na primeira semana (Konebras); não bloqueia o `Ready`.

## Definition of Done
- [ ] Checklist de virada executado e registrado (CA 1).
- [ ] Checklist "Antes da primeira operação fiscal e bancária" fechado antes da primeira NF-e e do primeiro boleto (GOL-01a CA 14).
- [ ] E-mail aos clientes enviado e registrado (CA 2).
- [ ] Limpeza da migração e fechamento do Dolibarr conferidos (CA 3).
- [ ] Sessões revogadas e security stamp subido, com a conferência dos tokens antigos (CA 4).
- [ ] Produção no host novo com conferência dos dados e dos segredos (CA 4, 5).
- [ ] Primeira nota e primeiro boleto reais (CA 6).
- [ ] Primeira semana acompanhada e registrada (CA 7, 8).
- [ ] Dolibarr só leitura (CA 9) e desligado no prazo, com o backup de 5 anos testado (CA 10).
- [ ] Homologação com key ring novo e, depois do primeiro fechamento mensal, sem o `konebras` (CA 11).
- [ ] Fechamento do go-live com a linha de base das métricas (CA 12); `docs/02-arquitetura.md` §7 e ADRs 0020/0034 atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Executar o runbook e os scripts ensaiados; registrar a execução; ajustar o runbook com o que mudou.
- [ ] Planilha (ou seção no runbook) do acompanhamento diário.
- [ ] Backup final do Dolibarr cifrado, com retenção de 5 anos, e o teste de restauração dele.

## Quality gates
- Agentes previstos: @devops, cybersecurity (sessões, Dolibarr, segredos), @qa, PO.
- Gate: CA 1, 3, 4, 5 e 6 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fases 4 a 8).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK (ajustado: R-SEC-3) |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK (sem endpoint novo; sessões, segredos, Dolibarr e limpeza) |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: marco final "Go-live", última) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs | OK (ADR 0034, D7) |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK (sem tela nova) |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** o ponto sem retorno (primeira nota e primeiro boleto reais) ficou explícito e acompanhado pelo PO; o Dolibarr fica só leitura; tokens do host antigo não valem no novo.
- **Correções da revalidação (21/10/2026):** pontos 79 a 81 decididos (CA 10, 3, 9, 2); o antigo CA 3 estava errado (o refresh não fica preso ao host): agora todas as famílias de refresh são revogadas e o security stamp de todos sobe, com a conferência de que refresh e JWT antigos dão 401 (CA 4); ambiente de produção novo com key ring novo (ADR 0034, CA 5); `DOLAPIKEY` regenerada, Dolibarr só leitura com allowlist e `DROP USER` com revogação na rede (CA 3, 9); D−1 com o teste do Inter de produção (CA 1); limpeza da homologação depois do fechamento (CA 11); linha de base com o desvio da data prevista (CA 12); passa a ser a última story, no marco final "Go-live" (antes bloqueava a Fase 6, que agora vem antes); CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma de decisão de story. Pré-condição externa explícita: janela da virada e presença da Konebras na primeira semana, sem bloquear.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready (pré-condição externa)`.

## Execução (02/11/2026)
A virada foi feita em **02/11/2026, das 20:16 às 20:20 UTC**, pela decisão do PO do mesmo dia: **a homologação virou a produção** (ADR 0034, adendo de 02/11/2026 à noite; ADR 0020). Registro completo em `docs/operacao/go-live.md` §12.

| Item | Situação | Como ficou |
|---|---|---|
| Checklist 1 (D−1) e CA 2 (e-mail aos clientes) | Para depois | Sem D−1; comunicados depois da virada |
| Checklist 2 e 3 (corte e carga final) | Feito | Carga MIG-01b/c em simulação: nada novo nem alterado. Para o PO cadastrar à mão: 3 contas bancárias sem código do banco (BB-CC, CAIXA, INTER; o BemAqui tem 1 conta e o PO mantém assim) e 2 terceiros com CNPJ inválido no Dolibarr (ids 10 e 14) |
| Checklist 4, CA 3 e CA 9 (Dolibarr) | Trocado | No lugar do "só leitura em `adm.`": Dolibarr e Konebras UI **desligados e removidos** da VPS (~20:30 UTC), com backup final em `/opt/backups/dolibarr-final-20261102T202934Z` e restauração testada (118 terceiros, 34 produtos, 26 faturas, 16 propostas, iguais ao sistema no ar); vhosts `erp.` e `adm.`, containers, imagens, cron e pastas removidos; volumes `stack_dolidb`, `stack_dolidocs` e `konebras-ui_konebras_ui_db` mantidos |
| Checklist 5 e CA 5 (promoção, segredos) | Trocado | Sem promoção nem recifração: a stack da homologação é a produção. Só a assinatura do JWT foi trocada; key ring e demais segredos herdados (fase 2) |
| Checklist 6 e CA 4 (acessos) | Feito | `acessos-virada.sql`: 2 famílias / 12 refresh tokens revogados, security stamp de 2 usuários, 1 convite expirado, 1 aparelho revogado, 0 usuários de teste; `acessos-virada-catalogo.sql` aplicado; login do PO em `app.konebras.com.br` com sucesso. A conferência de refresh e JWT antigos com 401 não foi registrada |
| Checklist 7 (domínio) | Feito | `tenant_domains` só com `app.konebras.com.br` (primário); `homolog.konebras.com.br` com 301 para o `app.`; `plataforma-homolog.bemaquiapp.com.br` sem mudança, com `/webhooks/` fora da allowlist (corrigido no mesmo dia); `default.conf` do Dolibarr guardado fora do `conf.d` |
| Checklist 8 (pipeline e integrações) | Em aberto | `deploy-prod.yml` não se aplica (fase 2); o deploy é o `scripts/deploy-homolog.sh`. Marcas `fiscal_environment_allowed = 'None'` e `bank_production_allowed = false` até o checklist "Antes da primeira operação fiscal e bancária"; até lá, NF-e fora do sistema e boletos como antes |
| Checklist 9 e CA 6 (smoke, primeira nota e boleto) | Parcial | Smoke e login ok; primeira NF-e e primeiro boleto pelo sistema só depois do checklist do §2 |
| Checklist 10 (rollback) | Disponível | Pelos dumps de antes da virada e pelo `default.conf` guardado; voltar ao Dolibarr exige restaurar o backup final |
| Checklist 11, CA 7, 8 e 12 (primeira semana e fechamento) | Em andamento | Acompanhamento diário no `go-live.md` §12 |
| CA 10 (desligamento do Dolibarr) | Antecipado, falta 1 item | **Cópia de 5 anos fora da VPS** pendente (PO); só depois os volumes mantidos podem sair. O PO tira os DNS de `erp.` e `adm.` |
| CA 11 (limpeza da homologação) | Não se aplica | A homologação é a produção; key ring novo e ambiente de teste ficam para a fase 2 |

**Falta para `Done`:** a primeira semana (CA 7, 8) e o fechamento (CA 12); o checklist "Antes da primeira operação fiscal e bancária" com a primeira NF-e e o primeiro boleto (CA 6); a cópia do Dolibarr fora da VPS (CA 10) e a retirada dos DNS de `erp.` e `adm.`; comunicados aos usuários e aos clientes (CA 2). `cliente.konebras.com.br` fica reservado para o portal do cliente (Fase 8).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da GOL-01) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (D1 nova, D7, tabela 3.2 pontos 79–81; ADR 0034); CA de sessões corrigido; nova ordem (Marco final "Go-live", ordem 2, última); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
| 02/11/2026 | 0.5 | Decisões do PO de 02/11/2026: o teste do Inter sai do D−1 e não adia a virada (CA 1); SEFAZ e Inter em produção pelo checklist "Antes da primeira operação fiscal e bancária" da GOL-01a (CA 14), depois da virada (checklist itens 8 e 9, CA 6); WhatsApp fora com a Fase 8 (CA 6); marca fiscal pelo nome real `fiscal_environment_allowed`; data da virada em aberto | River (SM) |
| 02/11/2026 | 0.6 | `InProgress`. Virada executada em 02/11/2026 (20:16–20:20 UTC) com a homologação virando a produção (ADR 0034, adendo; ADR 0020): dumps, carga final em simulação, JWT trocado, acessos, `tenant_domains`, vhosts e login do PO. Dolibarr e Konebras UI desligados e removidos (~20:30 UTC) com backup final e restauração testada, no lugar do "só leitura em `adm.`" (CA 9 trocado, CA 10 antecipado menos a cópia de 5 anos fora da VPS). Ver "Execução" | @dev |
