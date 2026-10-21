# BNK-03b — Conciliação bancária: sugestão de match, conciliação manual e lançamento a partir do extrato

| Campo | Valor |
|---|---|
| ID | BNK-03b |
| Título | Tela de conciliação por conta e período: sugestão automática de par entre a linha do banco e a transação do sistema (por valor, data e documento), aceitar, recusar ou escolher à mão, conciliar 1×N, criar a partir da linha o lançamento sem OP (tarifa, IOF, juros, rendimento) ou a baixa de um título a receber, e desfazer a conciliação; nenhum pagamento sem OP aprovada |
| Tamanho | G (quebra da BNK-03) |
| Status | Draft |
| Requisitos | R-FIN-8 (conciliação, sugestão automática de match) · R-FIN-2 · R-FIN-3 · R-FIN-5 (nenhum pagamento sem OP) · R-FIN-10 · R-PRP-1 · R-PRP-3 · R-PRP-6 · R-PRP-7 · R-SEC-5 |
| Dependências | BNK-03a (linhas do extrato), FIN-04 (transações, lançamento sem OP com `finance.pay`), FIN-01 (baixa de título a receber), FIN-03a (pagamento de OP), BNK-02 (transação do boleto e "aguardando conferência") |
| Bloqueia | GOL-01a (fechamento mensal em menos de 1 dia, PRD §1.2) |
| Ordem de execução | 7 (Marco 5.3 "Conciliação") |

## User story
Como **financeiro**, quero **ver lado a lado o extrato do banco e o que o sistema registrou, com o par já sugerido**, para **fechar o mês em menos de um dia e achar na hora o que entrou ou saiu sem registro**.

## Contexto e referências
- PRD R-FIN-8; §1.2 ("Fechamento financeiro mensal em menos de 1 dia de trabalho"); `05-backlog` BNK-03 ("sugestão de match por valor/data/documento; conciliação manual").
- `04-fluxos-estados` §1: "Regra dura: nenhum pagamento é baixado sem OP aprovada. Exceção: tarifas, IOF, juros e rendimentos lançados no extrato como lançamento bancário sem OP (FIN-04, D4) … a conciliação (BNK-03) passa a sugerir esses lançamentos a partir do extrato do banco."
- `03-modelo-dados` → `financial_transactions.reconciled` e `statement_line_id` ("BNK-03"); `bank_statement_lines.matched_transaction_id`.
- FIN-04 (lançamento sem OP com `finance.pay`, conta do plano e motivo, aviso ao Proprietário, lista "Movimentos sem OP"); FIN-01 (baixa com `finance.edit`).

## Critérios de aceite
**Sugestão**
1. **Dado** linhas `Pending` e transações não conciliadas da mesma conta, **quando** abro "Conciliação" da conta para um período, **então** o sistema sugere pares nesta ordem: (a) transação do boleto do Inter com o mesmo identificador do banco; (b) mesmo valor e mesma data; (c) mesmo valor com até 3 dias de diferença (proposta, Pontos em aberto 1) e documento ou contraparte parecidos (CPF/CNPJ do terceiro, número do título ou da OP na descrição); cada sugestão mostra o motivo ("Mesmo valor, 1 dia de diferença, CNPJ do fornecedor") e a confiança (Alta, Média); uma transação nunca é sugerida para duas linhas.
2. **Dado** uma linha sem par, **quando** a descrição indica tarifa, IOF, juros ou rendimento (regras por palavra e tipo do banco, editáveis por tenant, proposta), **então** a sugestão é "Lançar como {tipo} sem OP" com a conta do plano mais usada para aquele tipo (R-PRP-1).
3. **Dado** uma linha de entrada sem par cujo pagador (documento) é cliente com título em aberto de mesmo valor, **quando** aparece, **então** a sugestão é "Baixar o título {número} de {cliente}".

**Conciliar**
4. **Dado** `finance.edit`, **quando** aceito uma sugestão ou escolho à mão uma ou mais transações para uma linha (1×N, ou N linhas para 1 transação, com a soma igual), **então** a linha fica `Reconciled` com os vínculos, as transações ficam `reconciled = true` com `statement_line_id`, e a mensagem diz "Conciliado"; soma diferente é recusada com "A soma das transações (R$ X) não bate com a linha (R$ Y)."; "Aceitar todas as de confiança alta" concilia em lote, com o diálogo listando o que será feito.
5. **Dado** a sugestão "Lançar sem OP", **quando** confirmo, **então** o diálogo é o mesmo da FIN-04 (conta do plano, motivo, data e valor **vindos da linha e travados**), exige `finance.pay`, gera o lançamento sem OP, o aviso ao Proprietário e a entrada em "Movimentos sem OP", e concilia a linha na mesma transação.
6. **Dado** a sugestão "Baixar o título", **quando** confirmo, **então** o diálogo de baixa da FIN-01 abre com o valor, a data e a conta da linha (juros, multa e desconto calculados pela diferença e editáveis, sujeitos aos limites da D8), exige `finance.edit` e concilia na mesma transação.
7. **[Segurança]** **Dado** uma linha de **saída** sem transação correspondente que não é tarifa, IOF ou juros, **quando** tento conciliar, **então** o sistema **não** oferece criar pagamento: só "Vincular a uma OP paga" (a OP já tem a transação, FIN-03a) ou "Abrir OP para regularizar" (leva à criação da OP com o título, que segue aprovação e só então paga; a linha fica `Pending` até lá) — a regra "nenhum pagamento sem OP aprovada" vale também aqui (teste de arquitetura: a conciliação não chama o serviço de pagamento).
8. **Dado** a transação "aguardando conferência" do boleto (BNK-02 CA 6), **quando** aparece na conciliação, **então** a linha do banco é casada com ela, mas a conferência do título continua na tela "Pagamentos a conferir".

**Desfazer e fechar**
9. **Dado** uma linha conciliada, **quando** quem tem `finance.edit` clica "Desfazer conciliação" com motivo, **então** os vínculos são removidos (a linha volta a `Pending` e as transações a `reconciled = false`), sem estornar nada; o lançamento sem OP ou a baixa criados pela conciliação continuam e só saem pelo estorno da FIN-04 ou da FIN-01.
10. **Dado** um período, **quando** todas as linhas estão conciliadas ou ignoradas e o saldo do banco no último dia bate com o do sistema, **então** a conta mostra "Conciliada até dd/mm/aaaa" e quem tem `finance.approve` pode "Fechar conciliação até dd/mm" (proposta, Pontos em aberto 2); depois de fechado, desfazer conciliação ou lançar transação com data no período exige `finance.approve` e motivo.
11. **Dado** transações do sistema sem linha do banco há mais de 5 dias (ex.: pagamento registrado que não saiu), **quando** abro a conciliação, **então** aparecem em "No sistema, não no banco" com o chip alerta e a ação "Ver origem".

**Permissões, concorrência e auditoria**
12. **Dado** o módulo `finance` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; conciliar e desfazer com `finance.edit`, lançar sem OP com `finance.pay`, fechar e mexer no período fechado com `finance.approve`; `RequireModule` e `perm:`; IDOR em `/bank-accounts/{id}/reconciliations/{rid}` (transação de outra conta: 422).
13. **[Segurança]** **Dado** dois usuários conciliando a mesma linha ou a mesma transação ao mesmo tempo, **quando** gravam, **então** só um passa (`xmin` na linha e na transação; 409 `concurrency_conflict` com "Recarregar").
14. **Dado** qualquer conciliação, lançamento, desfazer ou fechamento, **quando** abro o histórico da linha ou da transação, **então** vejo quem, quando, a sugestão aceita (ou "manual") e o motivo.

## Regras de negócio e técnicas
- **`bank_reconciliations`** (proposta): `bank_account_id`, `kind` (`Suggested`/`Manual`/`BankEntry`/`Settlement`), `confidence`, `reason`, `created_at`, `created_by`, `undone_at`, `undone_by`, `undo_reason`; **`bank_reconciliation_links`**: `reconciliation_id`, `statement_line_id`, `transaction_id`, `amount`.
- **Motor de sugestão** (Application, puro e testável): candidatos da mesma conta e janela de datas, sem sugerir a mesma transação duas vezes, ordem do CA 1.
- **Regras de tipo** (`bank_statement_rules`, proposta): palavra ou tipo do banco → tipo do lançamento sem OP e conta do plano sugerida.
- **Fechamento** (`bank_reconciliation_closings`, proposta): conta, até a data, quem e quando.
- A conciliação nunca cria pagamento; só vincula, chama a baixa de recebimento (FIN-01) ou o lançamento sem OP (FIN-04).

## Permissões, eventos e tabelas
- **Permissões:** `finance.view` (ver), `finance.edit` (conciliar, desfazer, baixar recebimento), `finance.pay` (lançar sem OP), `finance.approve` (fechar e mexer no período fechado). Nenhuma ação nova.
- **Eventos:** `ReceivableSettled` (pela baixa, já existe).
- **Tabelas:** `finance.bank_reconciliations`, `finance.bank_reconciliation_links`, `finance.bank_statement_rules`, `finance.bank_reconciliation_closings` (propostas); `financial_transactions.reconciled` e `statement_line_id` passam a ser preenchidos.

## Telas e estados
- **Financeiro → Caixa e pagamentos → Conciliação** (`/app/financeiro/conciliacao`, entrada nova no `modules.ts`; e a partir da conta): seletor de conta e período; duas colunas "No banco" e "No sistema" com as sugestões ligando as linhas; barra de totais (pendentes, conciliadas, diferença de saldo).
- **Verbos:** "Conciliar" / "Conciliado"; "Aceitar todas de confiança alta" / "{n} linhas conciliadas"; "Lançar sem OP" / "Lançamento registrado"; "Baixar título" / "Baixa registrada"; "Desfazer conciliação" / "Conciliação desfeita"; "Fechar conciliação" / "Conciliação fechada".
- **Estados:** carregando; vazio ("Nada a conciliar neste período." ou, sem extrato, "Importe o extrato para conciliar." + link para BNK-03a); erro dentro do diálogo; tudo conciliado ("Conta conciliada até dd/mm/aaaa").
- **Propositivo:** sugestões já prontas, conta do plano e motivo preenchidos pela regra; atalho de teclado para aceitar a sugestão; próximo período oferecido depois de fechar.
- **400 px:** as duas colunas viram uma lista de pares em cartões (linha do banco em cima, sugestão embaixo, "Conciliar" com 44 px); diálogos em tela cheia; E2E de 400 px e só pelo teclado.
- **Ajuda:** `Ajuda/financeiro-conciliacao.md`; `<Dica>` em "Confiança", "Lançar sem OP" e "Fechar conciliação".

## Plano de testes
- **Unidade:** motor de sugestão com tabela de casos (CA 1–3), sem dupla sugestão; soma 1×N (CA 4).
- **Integração (Testcontainers, obrigatória: movimenta financeiro):** conciliar, lote e soma errada (CA 4); lançar sem OP com `finance.pay` e aviso (CA 5); baixar título com limite D8 (CA 6); saída sem OP nunca vira pagamento e teste de arquitetura (CA 7); boleto aguardando conferência (CA 8); desfazer sem estornar (CA 9); fechar e período fechado (CA 10); "no sistema, não no banco" (CA 11); três camadas e IDOR (CA 12); concorrência (CA 13); auditoria (CA 14); dataset fixo de um mês real simulado fechando com diferença zero.
- **E2E (Playwright):** importar OFX, aceitar sugestões, lançar uma tarifa e fechar o mês; tela a 400 px e só pelo teclado.

## Fora do escopo
- Regras de conciliação aprendidas por IA; conciliação de cartão de crédito; contas de outros bancos por API.

## Pontos em aberto
1. **Janela de datas da sugestão** (PO): proposta 3 dias.
2. **Fechamento da conciliação** (PO/contadora): ter ou não o fechamento por período com `finance.approve`? Proposta: sim.
3. **Regras de tipo padrão** (PO): lista inicial de palavras do extrato do Inter para tarifa, IOF e rendimento, a montar com extratos reais na homologação.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers), arquitetura e E2E passando.
- [ ] Migration criada (não destrutiva), testada sobre cópia do banco do tenant `konebras`.
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Telas com estados, inclusive a 400 px.
- [ ] Ajuda escrita; teste de cobertura passando.
- [ ] Efeito financeiro com confirmação e próximos passos aplicados.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `ReconciliationSuggester` (puro), `BankReconciliation` e vínculos; regras de tipo com seed vazio.
- [ ] Endpoints `/api/v1/finance/reconciliation` (sugestões, conciliar, lote, desfazer, fechar) reaproveitando os serviços da FIN-01 e da FIN-04.
- [ ] Tela de duas colunas e a versão em cartões a 400 px; ajuda.

## Quality gates
- Agentes previstos: @dev, @ux-design-expert (tela de duas colunas e 400 px), cybersecurity (regra "sem pagamento sem OP"), @qa.
- Gate: CA 4, 5, 7, 9 e 13 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026.

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto registrados sem decidir | OK |

- **Correções feitas na validação:** saída sem OP nunca vira pagamento pela conciliação (CA 7); desfazer não estorna (CA 9); fechamento do período como proposta, não decidido (ponto 2).
- **Pré-condições para `Ready`:** ponto em aberto 2.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da BNK-03) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
