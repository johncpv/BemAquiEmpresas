# GOL-01b — Go-live Konebras: a virada, o corte do Dolibarr e a primeira semana

| Campo | Valor |
|---|---|
| ID | GOL-01b |
| Título | Execução da virada pelo runbook ensaiado: D−1 com o teste de conexão do Inter de produção, aviso por e-mail aos clientes, congelamento do Dolibarr, carga final só dos cadastros alterados, promoção do banco do tenant `konebras` para o ambiente de produção novo (ADR 0034), recifração e segredos novos, revogação de todas as sessões e do security stamp, `app.konebras.com.br` apontando para o BemAqui e o Dolibarr em `adm.konebras.com.br` só leitura com allowlist e `DOLAPIKEY` regenerada, SEFAZ e Inter em produção, `DROP USER` e remoção dos segredos da migração, smoke, acompanhamento diário da primeira semana, fechamento do go-live, limpeza da homologação e a data de desligamento do Dolibarr |
| Tamanho | M (quebra da GOL-01) |
| Status | Ready (pré-condição externa) |
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

## Checklist de virada (resumo; o detalhe é o runbook da GOL-01a)
1. **D−1:** **teste de conexão do Inter de produção** no ambiente novo (BNK-01a CA 12, leitura sem efeito; falhou = virada adiada); e-mail aos clientes lembrado; backup extra do Dolibarr (MariaDB e arquivos) e do BemAqui verificado; certificados de `app` e `adm` válidos; A1 válido no cofre do EmissorFiscal.
2. **Corte do Dolibarr (hora H):** Dolibarr em modo só leitura (perfis sem escrita) e Konebras UI fora do ar; última hora de lançamento registrada.
3. **Carga final:** MIG-01b e MIG-01c (API REST do Dolibarr) só com os cadastros alterados depois da última carga, primeiro em `--dry-run`, relatório aceito pelo PO, depois a real; conflitos resolvidos à mão.
4. **Limpeza da migração e Dolibarr:** script do Dolibarr da GOL-01a: `DOLAPIKEY` regenerada e usuários dela desativados; `adm.konebras.com.br` só leitura com allowlist de IP ou autenticação básica; `DROP USER bqe_migracao_ro` com revogação na rede; secret da chave da API do Dolibarr e arquivo de-para apagados; staging expurgada (`--purge-staging --accepted`).
5. **Promoção:** backup final da homologação; banco do `konebras` promovido ao ambiente de produção novo (catálogo novo só com ele, key ring e certificado do Data Protection novos, ADR 0034); recifração dos segredos por tenant; migrator; segredos de produção novos.
6. **Acessos:** script de acessos da GOL-01a: todas as famílias de refresh revogadas, security stamp de todos os usuários subido, convites, redefinições, PINs, aparelhos e quiosques expirados, usuários e convites de teste apagados, super admins conferidos pelo PO.
7. **Domínio:** `app.konebras.com.br` → vhost do BemAqui; Dolibarr em `adm.konebras.com.br`; `tenant_domains` com `app.konebras.com.br` primário; `homolog.konebras.com.br` deixa de servir o tenant `konebras`.
8. **Integrações em produção:** EmissorFiscal com o emitente em produção e a flag `fiscal_production_allowed` ligada (FIS-02a; exige os campos fiscais críticos conferidos); série de produção; Inter de produção com a flag `bank_production_allowed` ligada (BNK-01a), credenciais, certificado e webhook registrado no host de produção (BNK-02); `tenantKey`, segredos e tokens de webhook novos, cadastrados de novo no Inter e no EmissorFiscal de produção; webhooks e integrações de sandbox desligados; jobs recorrentes ativos; `deploy-prod.yml` ligado com environment protection.
9. **Smoke (só leitura e um teste controlado):** `/health/ready`, login de um Proprietário com 2FA, saldo das contas igual ao extrato do dia, primeira nota fiscal real e primeiro boleto real feitos com o PO acompanhando (o ponto sem retorno).
10. **Rollback:** disponível até o fim do primeiro dia útil, pelo runbook (GOL-01a CA 9).
11. **Primeira semana e fechamento:** acompanhamento diário (CA 7 a 9); fechamento do go-live (CA 12); limpeza da homologação (CA 11).

## Critérios de aceite
1. **Dado** o "Go" da GOL-01a, **quando** a virada é feita, **então** cada passo do checklist é executado pelo runbook, marcado com hora, responsável e verificação no registro da virada (`docs/operacao/go-live.md`, seção "Execução"), e qualquer desvio fica descrito; o teste de conexão do Inter de produção no D−1 é o primeiro registro, e a falha dele adia a virada.
2. **Dado** a data da virada, **quando** faltam 5 dias úteis e de novo no D−1, **então** o **e-mail aos clientes da Konebras** aprovado na GOL-01a (data da virada, boleto novo pelo Inter, nota pelo sistema novo, contato para dúvidas) sai pela fila do BemAqui ou pelo e-mail da Konebras, e o envio fica registrado (quantidade e falhas, sem a lista de destinatários no runbook).
3. **[Segurança]** **Dado** o fim da carga final, **quando** a limpeza e o script do Dolibarr rodam, **então** o usuário somente leitura do Dolibarr não existe mais e a rede não deixa mais o BemAqui chegar ao banco dele (conferido por consulta e por tentativa de conexão), a `DOLAPIKEY` antiga não funciona mais (conferido por chamada), o `adm.konebras.com.br` recusa acesso fora da allowlist ou sem a autenticação básica e não aceita escrita, os secrets da migração não existem mais nos ambientes, o pacote de snapshot (se usado) foi apagado e a staging foi expurgada (ADR 0017, ADR 0018).
4. **[Segurança]** **Dado** a promoção, **quando** o script de acessos roda e o primeiro acesso a `https://app.konebras.com.br` acontece, **então** **todas as famílias de refresh foram revogadas e o security stamp de todos os usuários subiu** (o refresh não fica preso ao host, então tokens e cookies de `homolog.konebras.com.br` não podem valer no host novo; correção do antigo CA 3, que confiava só no `aud` do JWT), um refresh e um JWT emitidos antes da virada dão 401 (conferido), convites, redefinições, PINs, aparelhos e quiosques antigos não valem, o tenant `konebras` é resolvido pelo host novo, o login funciona com os mesmos usuários e 2FA, e o relatório de conferência da GOL-01a CA 8 bate com os números do dia.
5. **[Segurança]** **Dado** a produção, **quando** os segredos são conferidos, **então** nenhum é o da homologação (nem o key ring nem o certificado de proteção, ADR 0034), um segredo de cada tipo decifra com o key ring novo e nenhum decifra com o antigo, e o monitor externo e os alertas de backup, jobs e integrações (FND-12) apontam para a produção.
6. **Dado** a primeira nota fiscal real e o primeiro boleto real, **quando** são feitos com o PO, **então** a nota é autorizada em produção pela SEFAZ e o boleto aparece no Internet Banking do Inter; a baixa do boleto, quando pago, chega pelo webhook de produção (com reconsulta por mTLS, BNK-02); o `EnvironmentBanner` some no `konebras`; o **envio de WhatsApp aos clientes é ligado** (Configurações → Integrações → WhatsApp em produção, WPP-01), só para contatos com opt-in, com a primeira mensagem real conferida pelo PO.
7. **Dado** cada dia útil da primeira semana, **quando** o acompanhamento diário é feito (30 min com o Financeiro e o PO), **então** são conferidos e registrados: saldo do sistema × banco por conta, boletos emitidos e baixados, pagamentos a conferir, notas autorizadas e rejeitadas, eventos de webhook com falha ou parados, jobs com falha, dead-letters, ações com 2FA de novo recusadas, chamados dos usuários; problema vira item com dono e prazo.
8. **Dado** um problema de severidade alta na primeira semana (dado errado com efeito financeiro ou fiscal, indisponibilidade acima de 1 h), **quando** acontece, **então** o PO decide no mesmo dia entre corrigir, contornar ou acionar o rollback (dentro do prazo da GOL-01a), e a decisão fica registrada.
9. **Dado** o Dolibarr em `adm.konebras.com.br`, **quando** a primeira semana termina, **então** continua **só leitura, com allowlist de IP ou autenticação básica**, para consulta do histórico (o que não migrou fica consultável lá, ADR 0017).
10. **Dado** o **primeiro fechamento mensal no BemAqui**, ou **3 meses** depois da virada (o que vier primeiro), **quando** o PO confirma, **então** o Dolibarr é desligado: backup final (MariaDB e arquivos) **cifrado**, no mesmo bucket do backup, com **retenção de 5 anos** (guarda fiscal e contábil), teste de restauração desse backup registrado, vhost `adm.` removido e o container parado; a data do desligamento fica no runbook e mede a métrica "Dolibarr desligado" do PRD §1.2.
11. **Dado** a produção no ar, **quando** a homologação é limpa, **então** ela recebe **key ring novo** logo depois da virada e, **depois do primeiro fechamento mensal em produção**, o **banco do `konebras` é apagado dela** (com o registro em `docs/operacao/restauracao.md`), o tenant `demo` e os de teste continuam, e o deploy de homologação segue para os testes (ADR 0034).
12. **Dado** o fim da primeira semana sem problema de severidade alta aberto, **quando** o PO revisa, **então** registra o fechamento do go-live (data), a linha de base das métricas do PRD §1.2 (pagamentos com OP, projetos com orçado × realizado, tempo do fechamento mensal, desvio da data prevista ≤ 15%, medido na GOL-01a) e a data da primeira medição de 3 meses.

## Regras de negócio e técnicas
- Nenhuma alteração de produto nesta story; correção de produto descoberta na virada vira story ou hotfix pelo fluxo normal (branch, PR, merge, deploy).
- Toda ação na produção pelo runbook e pelos scripts ensaiados na GOL-01a; diagnóstico com dado real só em cópia restaurada (ADR 0020, ADR 0034).
- **SEFAZ e Inter em produção só aqui** (D1): as flags `fiscal_production_allowed` e `bank_production_allowed` são ligadas pelo super admin da plataforma no passo 8, registradas no runbook.

## Permissões, eventos e tabelas
- **Permissões:** nenhuma nova. **Tabelas:** `catalog.tenant_domains` (host primário), `catalog.tenants.fiscal_production_allowed` e `catalog.tenants.bank_production_allowed` (ligadas), sessões e security stamps (pelo script de acessos).

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

**Pré-condição externa:** janela da virada no início de um mês, fora do horário comercial, e presença do Financeiro e do PO na primeira semana (Konebras); não bloqueia o `Ready`.

## Definition of Done
- [ ] Checklist de virada executado e registrado, com o D−1 do Inter (CA 1).
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

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da GOL-01) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (D1 nova, D7, tabela 3.2 pontos 79–81; ADR 0034); CA de sessões corrigido; nova ordem (Marco final "Go-live", ordem 2, última); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
