# BNK-03b — Conciliação bancária: sugestão de par, conciliação manual, lançamento a partir do extrato e fechamento do período

| Campo | Valor |
|---|---|
| ID | BNK-03b |
| Título | Tela de conciliação por conta e período, **uma linha por par** (linha do banco e transação do sistema lado a lado), com as seções "Sem par no banco" e "No sistema, não no banco": sugestão automática de par (valor, data em até 3 dias e documento), aceitar, recusar ou escolher à mão, conciliar 1×N, criar a partir da linha o lançamento sem OP (tarifa, IOF, juros, rendimento) ou a baixa de um título a receber, desfazer e fechar o período com `finance.approve`; quem importou o OFX não confirma a conciliação dele; nenhum pagamento sem OP aprovada |
| Tamanho | G (quebra da BNK-03) |
| Status | Ready (pré-condição externa) |
| Requisitos | R-FIN-8 (conciliação, sugestão automática de match) · R-FIN-2 · R-FIN-3 · R-FIN-5 (nenhum pagamento sem OP) · R-FIN-10 · R-PRP-1 · R-PRP-3 · R-PRP-6 · R-PRP-7 · R-SEC-5 |
| Dependências | BNK-03a (linhas do extrato, lote e quem importou), FIN-04 (transações, lançamento sem OP com `finance.pay`), FIN-01 (baixa de título a receber), FIN-03a (pagamento de OP), BNK-02 (transação do boleto e "aguardando conferência"), BNK-01b (`ConfirmDialog`), BNK-01a (`RequireRecentTwoFactor`) |
| Bloqueia | GOL-01a (critério de entrada: conciliação fechada no ensaio; a trilha fiscal corre em paralelo, sem dependência real) |
| Ordem de execução | 6 (Marco 5.3 "Conciliação") |

## User story
Como **financeiro**, quero **ver lado a lado o extrato do banco e o que o sistema registrou, com o par já sugerido**, para **fechar o mês em menos de um dia e achar na hora o que entrou ou saiu sem registro**.

## Contexto e referências
- PRD R-FIN-8; §1.2 ("Fechamento financeiro mensal em menos de 1 dia de trabalho"); `05-backlog` BNK-03 ("sugestão de match por valor/data/documento; conciliação manual").
- **ADR 0032** (Banco Inter; OFX de contingência), ADR 0008 adendo de 21/10/2026 (D6: 2FA de novo em pagar).
- **Revisão pré-Fases 4 a 8** (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`): D6, D13, D18; tabela 3.2, pontos 32 a 34; §4.3 (BNK-03a/03b); §4.4 (padrões 3 e 7; achado "conciliação com uma linha por par, também no desktop, e seções 'Sem par no banco' e 'No sistema, não no banco'").
- `04-fluxos-estados` §1: "Regra dura: nenhum pagamento é baixado sem OP aprovada. Exceção: tarifas, IOF, juros e rendimentos lançados no extrato como lançamento bancário sem OP (FIN-04, D4) … a conciliação (BNK-03) passa a sugerir esses lançamentos a partir do extrato do banco."
- `03-modelo-dados` → `financial_transactions.reconciled` e `statement_line_id`; `bank_statement_lines.matched_transaction_id`.
- FIN-04 (lançamento sem OP com `finance.pay`, conta do plano e motivo, aviso ao Proprietário, lista "Movimentos sem OP"); FIN-01 (baixa com `finance.edit`, limites de desconto).

## Critérios de aceite
**Sugestão**
1. **Dado** linhas `Pending` e transações não conciliadas da mesma conta, **quando** abro "Conciliação" da conta para um período, **então** o sistema sugere pares nesta ordem: (a) transação do boleto do Inter com o mesmo identificador do banco; (b) mesmo valor e mesma data; (c) mesmo valor com **até 3 dias** de diferença e documento ou contraparte parecidos (CPF/CNPJ do terceiro, número do título ou da OP na descrição); cada sugestão mostra o motivo ("Mesmo valor, 1 dia de diferença, CNPJ do fornecedor") e a confiança (Alta, Média); uma transação nunca é sugerida para duas linhas; linha "não verificada pelo banco" (OFX de contingência, BNK-03a CA 6) nunca tem confiança Alta.
2. **Dado** uma linha sem par, **quando** a descrição indica tarifa, IOF, juros ou rendimento (regras por palavra e tipo do banco em `bank_statement_rules`, editáveis por tenant com `finance.edit`; seed vazio, montado com extratos reais do Inter), **então** a sugestão é "Lançar como {tipo} sem OP" com a conta do plano mais usada para aquele tipo (R-PRP-1).
3. **Dado** uma linha de entrada sem par cujo pagador (documento) é cliente com título em aberto de mesmo valor, **quando** aparece, **então** a sugestão é "Baixar o título {número} de {cliente}".

**Conciliar**
4. **Dado** `finance.edit`, **quando** aceito uma sugestão ou escolho à mão uma ou mais transações para uma linha (1×N, ou N linhas para 1 transação, com a soma igual), **então** a linha fica `Reconciled` com os vínculos, as transações ficam `reconciled = true` com `statement_line_id`, e a mensagem diz "Conciliado"; soma diferente é recusada com "A soma das transações (R$ X) não bate com a linha (R$ Y)."; "Aceitar todas as de confiança alta" concilia em lote pelo `ConfirmDialog` com impacto em lista (pares, valores e total).
5. **[Segurança]** **Dado** uma linha vinda de **OFX**, **quando** quem importou o lote (BNK-03a) tenta conciliá-la, aceitar a sugestão, lançar sem OP ou baixar título a partir dela, **então** recebe 403 `segregation_required` "Quem importou o extrato não confirma a conciliação dele. Peça a outra pessoa do Financeiro." e o botão aparece desabilitado com esse motivo; outro usuário com `finance.edit` (Proprietário incluído) confirma; linhas da API do Inter, importadas pelo sistema, não têm essa restrição.
6. **[Segurança]** **Dado** a sugestão "Lançar sem OP", **quando** confirmo, **então** o diálogo é o mesmo da FIN-04 (conta do plano, motivo, data e valor **vindos da linha e travados**), exige `finance.pay` e, para saída, o **2FA de novo** (D6), gera o lançamento sem OP, o aviso ao Proprietário e a entrada em "Movimentos sem OP", e concilia a linha na mesma transação; a partir de linha **não verificada pelo banco** (OFX), valor acima do teto `finance.ofx_bank_entry_cap` (Configurações → Parâmetros, limites do financeiro; padrão R$ 500,00) exige `finance.approve`.
7. **Dado** a sugestão "Baixar o título", **quando** confirmo, **então** o diálogo de baixa da FIN-01 abre com o valor, a data e a conta da linha (juros, multa e desconto calculados pela diferença e editáveis, sujeitos aos limites da FIN-01), exige `finance.edit` e concilia na mesma transação.
8. **[Segurança]** **Dado** uma linha de **saída** sem transação correspondente que não é tarifa, IOF ou juros, **quando** tento conciliar, **então** o sistema **não** oferece criar pagamento: só "Vincular a uma OP paga" (a OP já tem a transação, FIN-03a) ou "Abrir OP para regularizar" (leva à criação da OP com o título, que segue aprovação e só então paga; a linha fica `Pending` até lá) — "nenhum pagamento sem OP aprovada" vale também aqui (teste de arquitetura: a conciliação não chama o serviço de pagamento).
9. **Dado** a transação "aguardando conferência" do boleto (BNK-02 CA 9), **quando** aparece na conciliação, **então** a linha do banco é casada com ela, mas a conferência do título continua no filtro "Pagamentos a conferir" da lista A receber.

**Desfazer e fechar**
10. **Dado** uma linha conciliada, **quando** quem tem `finance.edit` clica "Desfazer conciliação" com motivo, **então** os vínculos são removidos (a linha volta a `Pending` e as transações a `reconciled = false`), sem estornar nada; o lançamento sem OP ou a baixa criados pela conciliação continuam e só saem pelo estorno da FIN-04 ou da FIN-01.
11. **Dado** um período, **quando** todas as linhas estão conciliadas ou ignoradas e o saldo do banco no último dia bate com o do sistema, **então** a conta mostra "Conciliada até dd/mm/aaaa" e quem tem `finance.approve` pode "Fechar conciliação até dd/mm" no `ConfirmDialog` com o impacto em lista (linhas, totais e diferença zero); depois de fechado, desfazer conciliação, ignorar linha ou lançar transação com data no período exige `finance.approve` e motivo, e é notificado aos membros do Proprietário.
12. **Dado** transações do sistema sem linha do banco há mais de 5 dias (ex.: pagamento registrado que não saiu), **quando** abro a conciliação, **então** aparecem na seção "No sistema, não no banco" com o chip alerta e a ação "Ver origem"; linhas do banco sem sugestão aparecem em "Sem par no banco".

**Permissões, concorrência e auditoria**
13. **Dado** o módulo `finance` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; conciliar e desfazer com `finance.edit`, lançar sem OP com `finance.pay` (+ 2FA de novo na saída), fechar e mexer no período fechado com `finance.approve`; `RequireModule` e `perm:`; IDOR em `/bank-accounts/{id}/reconciliations/{rid}` (transação de outra conta: 422).
14. **[Segurança]** **Dado** dois usuários conciliando a mesma linha ou a mesma transação ao mesmo tempo, **quando** gravam, **então** só um passa (`xmin` na linha e na transação; 409 `concurrency_conflict` com "Recarregar").
15. **Dado** qualquer conciliação, lançamento, desfazer ou fechamento, **quando** abro o histórico da linha ou da transação, **então** vejo quem, quando, a sugestão aceita (ou "manual"), quem importou a linha e o motivo.

## Regras de negócio e técnicas
- **`bank_reconciliations`:** `bank_account_id`, `kind` (`Suggested`/`Manual`/`BankEntry`/`Settlement`), `confidence`, `reason`, `created_at`, `created_by`, `undone_at`, `undone_by`, `undo_reason`; **`bank_reconciliation_links`**: `reconciliation_id`, `statement_line_id`, `transaction_id`, `amount`.
- **Motor de sugestão** (Application, puro e testável): candidatos da mesma conta e janela de 3 dias, sem sugerir a mesma transação duas vezes, ordem do CA 1.
- **Regras de tipo** (`bank_statement_rules`): palavra ou tipo do banco → tipo do lançamento sem OP e conta do plano sugerida; seed vazio.
- **Fechamento** (`bank_reconciliation_closings`): conta, até a data, quem e quando; o serviço de transações da FIN-04 e o de baixa da FIN-01 conferem o fechamento antes de gravar data no período.
- **Segregação** (CA 5): `bank_statement_batches.imported_by` comparado com o usuário atual nas linhas de OFX.
- **Teto do OFX:** `finance.ofx_bank_entry_cap` em `finance.parameters` (padrão R$ 500,00, editável em Configurações → Parâmetros com `settings.edit`).
- A conciliação nunca cria pagamento; só vincula, chama a baixa de recebimento (FIN-01) ou o lançamento sem OP (FIN-04).

## Permissões, eventos e tabelas
- **Permissões:** `finance.view` (ver), `finance.edit` (conciliar, desfazer, baixar recebimento, editar regras), `finance.pay` (lançar sem OP, com 2FA de novo na saída), `finance.approve` (fechar, mexer no período fechado, lançar acima do teto a partir de OFX). Nenhuma ação nova.
- **Eventos:** `ReceivableSettled` (pela baixa, já existe).
- **Tabelas:** `finance.bank_reconciliations`, `finance.bank_reconciliation_links`, `finance.bank_statement_rules`, `finance.bank_reconciliation_closings` (novas); `finance.parameters.ofx_bank_entry_cap` (nova chave); `financial_transactions.reconciled` e `statement_line_id` passam a ser preenchidos.

## Telas e estados
- **Padrões comuns das Fases 4 a 8:** 3 (tabela analítica: coluna fixa da data, navegação por célula, drill-down em painel lateral), 7 (`ConfirmDialog` com impacto em lista no lote e no fechamento).
- **Financeiro → Caixa e pagamentos → Conciliação** (`/app/financeiro/conciliacao`, entrada nova no `modules.ts`, agrupada nas abas do Financeiro com "Mais" a 400 px; e a partir da conta): seletor de conta e período; **uma linha por par** (banco à esquerda, sistema à direita, motivo e confiança no meio), também no desktop, e as seções "Sem par no banco" e "No sistema, não no banco"; barra de totais (pendentes, conciliadas, diferença de saldo).
- **Verbos:** "Conciliar" / "Conciliado"; "Aceitar todas de confiança alta" / "{n} linhas conciliadas"; "Lançar sem OP" / "Lançamento registrado"; "Baixar título" / "Baixa registrada"; "Desfazer conciliação" / "Conciliação desfeita"; "Fechar conciliação" / "Conciliação fechada".
- **Estados:** carregando; vazio ("Nada a conciliar neste período." ou, sem extrato, "Importe o extrato para conciliar." + link para a aba "Extrato do banco"); erro dentro do diálogo; tudo conciliado ("Conta conciliada até dd/mm/aaaa" + "Fechar conciliação"); linha bloqueada pela segregação com o motivo.
- **Propositivo:** sugestões já prontas, conta do plano e motivo preenchidos pela regra; atalho de teclado para aceitar a sugestão; próximo período oferecido depois de fechar; "Criar regra a partir desta linha" quando o mesmo tipo se repete.
- **400 px:** os pares em cartões (linha do banco em cima, sugestão embaixo, "Conciliar" com 44 px); seções empilhadas; diálogos em tela cheia; E2E de 400 px e só pelo teclado.
- **Ajuda:** `Ajuda/financeiro-conciliacao.md` (prevista); `<Dica>` em "Confiança", "Lançar sem OP", "Não verificada pelo banco", "Quem importou não confirma" e "Fechar conciliação".

## Plano de testes
- **Unidade:** motor de sugestão com tabela de casos e janela de 3 dias (CA 1–3), sem dupla sugestão e sem Alta para linha não verificada; soma 1×N (CA 4).
- **Integração (Testcontainers, obrigatória: movimenta financeiro):** conciliar, lote e soma errada (CA 4); segregação de quem importou o OFX (CA 5); lançar sem OP com `finance.pay`, 2FA de novo, aviso e teto do OFX (CA 6); baixar título com os limites (CA 7); saída sem OP nunca vira pagamento e teste de arquitetura (CA 8); boleto aguardando conferência (CA 9); desfazer sem estornar (CA 10); fechar e período fechado, inclusive pela FIN-04 e FIN-01 (CA 11); seções (CA 12); três camadas e IDOR (CA 13); concorrência (CA 14); auditoria (CA 15); dataset fixo de um mês simulado fechando com diferença zero.
- **E2E (Playwright):** importar OFX com um usuário, conciliar com outro, lançar uma tarifa e fechar o mês; tela a 400 px e só pelo teclado.

## Fora do escopo
- Regras de conciliação aprendidas por IA; conciliação de cartão de crédito; contas de outros bancos por API.

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fases 4 a 8, tabela 3.2, pontos 32–34):
1. Janela de 3 dias (CA 1).
2. Fechamento por período com `finance.approve` (CA 11).
3. Regras de tipo montadas com extratos reais; seed vazio (CA 2; pré-condição externa).

Decisões da revisão aplicadas: D13 (quem confirma não é quem importou, fechamento com `finance.approve`, CA 5, 11); D6 (2FA de novo no lançamento sem OP de saída, CA 6); §4.3 (teto para "lançar sem OP" a partir de linha de OFX, CA 6); D18 (`ConfirmDialog` com impacto em lista, criado na BNK-01b, CA 4, 11); §4.4 (uma linha por par também no desktop e as duas seções, CA 12; padrões 3 e 7).

**Pré-condição externa:** extratos reais do Inter da Konebras para montar as regras de tipo (tarifa, IOF, rendimento); a story funciona com o seed vazio e a criação de regra pela tela, então não bloqueia o `Ready`.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers), arquitetura e E2E passando.
- [ ] Migration criada (não destrutiva), testada sobre cópia do banco do tenant `konebras`; parâmetro do teto com seed.
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Telas com estados, inclusive a 400 px.
- [ ] Ajuda escrita; teste de cobertura passando.
- [ ] Efeito financeiro com confirmação e próximos passos aplicados.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `ReconciliationSuggester` (puro), `BankReconciliation` e vínculos; regras de tipo com seed vazio e "Criar regra a partir desta linha".
- [ ] Endpoints `/api/v1/finance/reconciliation` (sugestões, conciliar, lote, desfazer, fechar) reaproveitando os serviços da FIN-01 e da FIN-04, com a segregação e o fechamento.
- [ ] Tela de uma linha por par, seções e a versão em cartões a 400 px; ajuda.

## Quality gates
- Agentes previstos: @dev, @ux-design-expert (tela de pares e 400 px), cybersecurity (regra "sem pagamento sem OP", segregação), @qa.
- Gate: CA 4, 5, 6, 8, 10, 11 e 14 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fases 4 a 8).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK (ajustado: segregação do importador, teto do OFX) |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: ordem 6; a GOL-01a passa ao marco final) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs | OK (teto padrão de R$ 500,00 registrado como parâmetro editável) |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK (ajustado: uma linha por par e as duas seções) |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** saída sem OP nunca vira pagamento pela conciliação; desfazer não estorna; fechamento do período como proposta.
- **Correções da revalidação (21/10/2026):** pontos 32 a 34 decididos (CA 1, 11, 2); quem importou o OFX não confirma a conciliação dele (CA 5); 2FA de novo no lançamento sem OP de saída e teto para linha de OFX com `finance.approve` (CA 6); linha não verificada nunca com confiança Alta (CA 1); fechamento no `ConfirmDialog` e período fechado protegido também na FIN-04 e na FIN-01 (CA 11); tela com uma linha por par também no desktop e as seções "Sem par no banco" e "No sistema, não no banco" (CA 12); "Pagamentos a conferir" como filtro (CA 9); CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma de decisão de story. Pré-condição externa explícita: extratos reais do Inter para as regras de tipo, sem bloquear.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready (pré-condição externa)`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da BNK-03) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (D6, D13, D18, tabela 3.2 pontos 32–34, §4.3, §4.4); nova ordem (Marco 5.3, ordem 6); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
