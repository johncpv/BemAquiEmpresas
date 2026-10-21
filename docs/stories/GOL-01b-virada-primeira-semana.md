# GOL-01b — Go-live Konebras: a virada, o corte do Dolibarr e a primeira semana

| Campo | Valor |
|---|---|
| ID | GOL-01b |
| Título | Execução da virada pelo runbook ensaiado: congelamento do Dolibarr, carga final só dos cadastros alterados, promoção do banco do tenant `konebras`, segredos de produção, `app.konebras.com.br` apontando para o BemAqui e o Dolibarr em `adm.konebras.com.br` só leitura, fiscal e Inter em produção, `DROP USER` e remoção dos segredos da migração, smoke, acompanhamento diário da primeira semana e o fechamento do go-live |
| Tamanho | M (quebra da GOL-01) |
| Status | Draft |
| Requisitos | PRD §7 Fase 5 ("go-live e desligamento do Dolibarr") · PRD §1.2 (métricas de sucesso) · R-PLT-2 · R-TER-5 · R-FIS-2 · R-FIN-7 · PRD §6 Disponibilidade, Backup, Observabilidade |
| Dependências | GOL-01a ("Go" do PO registrado) |
| Bloqueia | Fase 6 (entra com o sistema em produção, PRD §7) |
| Ordem de execução | 18 (Marco 5.7 "Go-live", última da fase) |

## User story
Como **proprietário da Konebras**, quero **começar a trabalhar no BemAqui em `app.konebras.com.br` num dia combinado, com tudo o que estava na homologação, e ter alguém olhando de perto na primeira semana**, para **desligar o Dolibarr com segurança**.

## Contexto e referências
- `05-backlog` GOL-01 e ADR 0020 (promoção), ADR 0004, 0005 D1 (Dolibarr em `adm.konebras.com.br`; Konebras UI desativado), ADR 0017 (carga final só de cadastros; `DROP USER bqe_migracao_ro` e remoção do secret; adendo 2: a chave da API do Dolibarr deixa de ser usada e o secret é apagado), ADR 0015, ADR 0007, ADR 0010 item 13.
- PRD §1.2: "100% dos pagamentos passando por ordem de pagamento aprovada", "Dolibarr desligado", "Fechamento financeiro mensal em menos de 1 dia".
- Runbook `docs/operacao/go-live.md` (GOL-01a).

## Checklist de virada (resumo; o detalhe é o runbook da GOL-01a)
1. **D−1:** comunicado lembrado; backup extra do Dolibarr (MariaDB e arquivos) e do BemAqui verificado; certificados de `app` e `adm` válidos.
2. **Corte do Dolibarr (hora H):** Dolibarr em modo só leitura (usuários sem permissão de escrita, proposta) e Konebras UI fora do ar; última hora de lançamento registrada.
3. **Carga final:** MIG-01b e MIG-01c (API REST do Dolibarr) só com os cadastros alterados depois da última carga, primeiro em `--dry-run`, relatório aceito pelo PO, depois a real; conflitos resolvidos à mão.
4. **Limpeza da migração:** `DROP USER bqe_migracao_ro` (se existir), secret da chave da API do Dolibarr e arquivo de-para apagados; staging expurgada (`--purge-staging --accepted`).
5. **Promoção:** backup final da homologação; banco do `konebras` promovido (forma decidida na GOL-01a ponto 3); migrator; segredos de produção; key ring e certificado de proteção acompanhando o banco.
6. **Domínio:** `app.konebras.com.br` → vhost do BemAqui; Dolibarr em `adm.konebras.com.br` (URL principal ajustada); `tenant_domains` com `app.konebras.com.br` primário; `homolog.konebras.com.br` deixa de servir o tenant `konebras`.
7. **Integrações em produção:** EmissorFiscal com o emitente em produção e a flag `fiscal_production_allowed` ligada (FIS-02a); série de produção; Inter de produção (credenciais, certificado e webhook registrado no host de produção, BNK-02); jobs recorrentes ativos; `deploy-prod.yml` ligado.
8. **Smoke (só leitura e um teste controlado):** `/health/ready`, login de um Proprietário com 2FA, saldo das contas igual ao extrato do dia, primeira nota fiscal real e primeiro boleto real feitos com o PO acompanhando (o ponto sem retorno).
9. **Rollback:** disponível até o prazo da GOL-01a (CA 8), pelo runbook.
10. **Primeira semana:** acompanhamento diário (CA 6 a 9); fechamento do go-live (CA 10).

## Critérios de aceite
1. **Dado** o "Go" da GOL-01a, **quando** a virada é feita, **então** cada passo do checklist é executado pelo runbook, marcado com hora, responsável e verificação no registro da virada (`docs/operacao/go-live.md`, seção "Execução"), e qualquer desvio fica descrito.
2. **[Segurança]** **Dado** o fim da carga final, **quando** a limpeza roda, **então** o usuário somente leitura do Dolibarr não existe mais (conferido por consulta), os secrets da migração não existem mais nos ambientes, o pacote de snapshot (se usado) foi apagado e a staging foi expurgada (ADR 0017, ADR 0018).
3. **Dado** a promoção, **quando** o primeiro acesso a `https://app.konebras.com.br` acontece, **então** o tenant `konebras` é resolvido pelo host novo, o login funciona com os mesmos usuários e 2FA, os tokens antigos de `homolog.konebras.com.br` não valem no host novo (o `aud` do JWT é o host primário, ADR 0008), e o relatório de conferência do GOL-01a CA 7 bate com os números do dia.
4. **[Segurança]** **Dado** a produção, **quando** os segredos são conferidos, **então** nenhum é o da homologação (exceto key ring e certificado de proteção), um segredo de cada tipo decifra, e o monitor externo e os alertas de backup, jobs e integrações (FND-12) apontam para a produção.
5. **Dado** a primeira nota fiscal real e o primeiro boleto real, **quando** são feitos com o PO, **então** a nota é autorizada em produção pela SEFAZ e o boleto aparece no Internet Banking do Inter; a baixa do boleto, quando pago, chega pelo webhook de produção.
6. **Dado** cada dia útil da primeira semana, **quando** o acompanhamento diário é feito (proposta: 30 min com o Financeiro e o PO), **então** são conferidos e registrados: saldo do sistema × banco por conta, boletos emitidos e baixados, notas autorizadas e rejeitadas, eventos de webhook com falha ou parados, jobs com falha, dead-letters, chamados dos usuários; problema vira item com dono e prazo.
7. **Dado** um problema de severidade alta na primeira semana (dado errado com efeito financeiro ou fiscal, indisponibilidade acima de 1 h), **quando** acontece, **então** o PO decide no mesmo dia entre corrigir, contornar ou acionar o rollback (dentro do prazo da GOL-01a), e a decisão fica registrada.
8. **Dado** o Dolibarr em `adm.konebras.com.br`, **quando** a primeira semana termina, **então** continua só leitura para consulta do histórico (ADR 0017: o que não migrou fica consultável lá), e a data de desligamento definitivo e o destino do backup final dele ficam definidos pelo PO (Pontos em aberto 1).
9. **Dado** a homologação, **quando** a produção está no ar, **então** volta a ser só de teste, sem o tenant `konebras` com dado real (ou com a decisão da GOL-01a ponto 3), e o deploy de homologação continua para os testes.
10. **Dado** o fim da primeira semana sem problema de severidade alta aberto, **quando** o PO revisa, **então** registra o fechamento do go-live (data), a linha de base das métricas do PRD §1.2 (pagamentos com OP, projetos com orçado × realizado, tempo do fechamento mensal) e a data da primeira medição de 3 meses.

## Regras de negócio e técnicas
- Nenhuma alteração de produto nesta story; correção de produto descoberta na virada vira story ou hotfix pelo fluxo normal (branch, PR, merge, deploy).
- Toda ação na produção pelo runbook; diagnóstico com dado real só em cópia restaurada (ADR 0020).

## Permissões, eventos e tabelas
- **Permissões:** nenhuma nova. **Tabelas:** `catalog.tenant_domains` (host primário), `catalog.tenants.fiscal_production_allowed` (ligada).

## Telas e estados
- A faixa de homologação da GOL-01a sai; nenhuma tela nova.

## Plano de testes
- **Smoke de produção** (só leitura + o teste controlado do CA 5), registrado.
- **Conferências** do CA 3, 4 e 6 registradas no runbook.

## Fora do escopo
- Fase 6 (RH e Patrimônio), que entra com o sistema em produção.

## Pontos em aberto
1. **Desligamento definitivo do Dolibarr** (PO): quando (proposta: depois do primeiro fechamento mensal no BemAqui ou em até 3 meses, a métrica "Dolibarr desligado" do PRD) e onde fica o backup final dele (proposta: cifrado, no mesmo bucket do backup, com retenção de 5 anos pela guarda fiscal e contábil).
2. **Modo só leitura do Dolibarr** (PO): como travar a escrita (perfis dos usuários ou vhost só leitura).
3. **Comunicação ao cliente final** (PO): avisar os clientes da Konebras sobre o boleto novo (Inter pelo BemAqui) e a nota emitida pelo sistema novo?

## Definition of Done
- [ ] Checklist de virada executado e registrado (CA 1).
- [ ] Limpeza da migração conferida (CA 2).
- [ ] Produção no host novo com conferência dos dados e dos segredos (CA 3, 4).
- [ ] Primeira nota e primeiro boleto reais (CA 5).
- [ ] Primeira semana acompanhada e registrada (CA 6, 7).
- [ ] Dolibarr só leitura com data de desligamento definida (CA 8); homologação de volta a teste (CA 9).
- [ ] Fechamento do go-live com a linha de base das métricas (CA 10); `docs/02-arquitetura.md` §7 e ADR 0020 atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Executar o runbook; registrar a execução; ajustar o runbook com o que mudou.
- [ ] Planilha (ou seção no runbook) do acompanhamento diário.

## Quality gates
- Agentes previstos: @devops, @qa, PO.
- Gate: CA 2, 3, 4 e 5 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026.

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK (sem endpoint novo; segredos, `aud` e limpeza) |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK (sem tela nova) |
| 10 | DoD ajustado; pontos em aberto registrados sem decidir | OK |

- **Correções feitas na validação:** o ponto sem retorno (primeira nota e primeiro boleto reais) ficou explícito e acompanhado pelo PO (CA 5); o Dolibarr fica só leitura, sem data de desligamento decidida (ponto 1); tokens do host antigo não valem no novo (CA 3).
- **Pré-condições para `Ready`:** GOL-01a com "Go"; pontos em aberto 2 e 3.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da GOL-01) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
