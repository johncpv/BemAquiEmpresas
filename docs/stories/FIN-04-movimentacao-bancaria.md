# FIN-04 — Movimentação bancária: extrato interno, saldo, transferências e lançamento sem OP

| Campo | Valor |
|---|---|
| ID | FIN-04 |
| Título | Extrato interno por conta (saldo inicial + transações, saldo acumulado), saldo atual por conta e consolidado, transferência entre contas da empresa e estorno, e lançamento bancário sem OP (tarifa, IOF, juros e rendimento) com conta do plano, motivo e aviso ao Proprietário |
| Tamanho | M (re-estimada de P: recebeu o lançamento bancário sem OP, D4, e os controles de transferência da revisão pré-Fase 2) |
| Status | Ready |
| Requisitos | R-FIN-2 (contas bancárias e caixas, saldo inicial, transferências) · R-FIN-1 (conta do plano em todo lançamento) · R-FIN-10 (base do realizado) · R-COM-2 · R-PRP-7 · R-SEC-5 |
| Dependências | FIN-01 (`financial_transactions`, parâmetros do Finance), FIN-03a (pagamentos no extrato), FIN-05 (consulta de saldo atual), CAD-05b (contas, saldo inicial), FND-09e |
| Bloqueia | DSH-01 (saldo atual), BNK-03 (conciliação compara com este extrato) |
| Ordem de execução | 7 (Marco 2.1b "Pagar com OP") |

## User story
Como **financeiro**, quero **ver o extrato de cada conta com o saldo dia a dia, transferir dinheiro entre as contas da empresa e lançar as tarifas e rendimentos que o banco debita ou credita sozinho**, para **o saldo do sistema bater com o do banco e eu saber quanto tenho disponível em cada lugar**.

## Contexto e referências
- PRD R-FIN-2; `03-modelo-dados` → `financial_transactions` (`type`, `amount` com sinal), `bank_accounts` (`opening_balance`, `opening_date`).
- `04-fluxos-estados` §1: regra dura "nenhum pagamento é baixado sem OP aprovada", com a exceção "débitos automáticos e tarifas, marcados como 'sem OP' e listados para o dono". **D4 (PO, 20/10/2026):** essa exceção vem para esta story como lançamento manual, sem esperar a BNK-03 (Fase 5); sem ela, cada tarifa exigiria título, OP e aprovação, e o saldo não bateria com o banco.
- Revisão pré-Fase 2: D4, D8 (transferência acima do limite notifica o Proprietário; data retroativa), §2.2-M4 (conta nova notifica; transferência para conta nova exige aprovação), §2.1-A2 (idempotência), §2.3 (verbo "Transferir").
- CAD-05b CA 4 (conta sem saldo inicial com a `Tag` "Saldo inicial não informado"), CA 5 (saldo travado depois de transação).
- Padrões comuns da Fase 2 (`docs/stories/README.md`).

## Critérios de aceite
**Extrato e saldo**
1. **Dado** `finance.view` e a conta "Inter" com saldo inicial R$ 10.000,00 em 01/11/2026, um recebimento de R$ 425,00 em 05/11 e um pagamento de R$ 1.200,00 em 06/11, **quando** abro o extrato de 01/11 a 30/11, **então** vejo a linha "Saldo inicial" R$ 10.000,00, as duas transações em ordem de data (e de criação no mesmo dia) com descrição, documento de origem (link para o título, a OP, a transferência ou o lançamento), valor com sinal e saldo acumulado R$ 10.425,00 e R$ 9.225,00, e o saldo final do período R$ 9.225,00.
2. **Dado** um período que começa depois do saldo inicial, **quando** abro o extrato, **então** a primeira linha é "Saldo anterior" calculado (saldo inicial + transações antes do período) e bate com o saldo final do período anterior (teste com dataset fixo).
3. **Dado** uma conta sem saldo inicial, **quando** abro o extrato, **então** o saldo parte de zero, a `Tag` "Saldo inicial não informado" aparece no topo e o próximo passo "Informar saldo inicial" leva à Implantação financeira (FIN-00) ou ao painel da conta (CAD-05b), enquanto a conta não tiver transação.
4. **Dado** `finance.view`, **quando** abro "Contas e extrato", **então** cada conta ativa mostra o saldo atual (até hoje, pela consulta da FIN-05) e o rodapé mostra o saldo consolidado; transações com data futura não entram no saldo atual.

**Transferência**
5. **Dado** `finance.pay`, **quando** clico "Transferir", escolho origem e destino ativos e diferentes, valor, data e descrição e confirmo "Transferir", **então** na mesma transação são criadas duas transações `Transfer` ligadas por `transfer_id` (−valor na origem, +valor no destino), a mensagem de sucesso diz "Transferência registrada" e os dois extratos mostram a contrapartida com link; o mesmo envio repetido com o mesmo `Idempotency-Key` não cria outra transferência.
6. **Dado** origem igual ao destino, valor ≤ 0, conta inativa ou data anterior ao saldo inicial de qualquer das contas, **quando** confirmo, **então** 422 com mensagem PT-BR no campo; saldo da origem que fica negativo mostra o aviso âmbar "O saldo de {conta} fica negativo." e não bloqueia; o diálogo mostra o saldo das duas contas antes e depois.
7. **[Segurança]** **Dado** o parâmetro `finance.transfer_notify_amount` (padrão R$ 10.000,00), **quando** uma transferência passa dele, **então** os membros do Proprietário são notificados ("Transferência de {valor} de {origem} para {destino}, por {usuário}.").
8. **[Segurança]** **Dado** uma conta de destino criada há menos de 30 dias, **quando** transfiro para ela, **então** só quem tem `finance.approve` confirma: para os demais, o diálogo mostra a pré-condição "A conta {destino} foi criada há {n} dias: só quem aprova em Caixa e pagamentos pode transferir para ela." e a API responde 403 `transfer_to_new_account`.
9. **[Segurança]** **Dado** a criação de uma conta bancária ou caixa (CAD-05b), **quando** grava, **então** os membros do Proprietário são notificados ("Conta {nome} criada por {usuário}.") — mudança no caso de uso da CAD-05b feita nesta story.
10. **Dado** `finance.pay` e uma transferência, **quando** clico "Estornar transferência", informo o motivo e confirmo "Estornar transferência" (estilo `destructive`), **então** duas transações inversas são criadas, a transferência fica marcada como estornada e nada é apagado; os membros do Proprietário são notificados.

**Lançamento bancário sem OP (D4)**
11. **Dado** `finance.pay`, **quando** clico "Lançar tarifa ou rendimento" no extrato, escolho o tipo (Tarifa bancária, IOF, Juros pagos, Juros recebidos, Rendimento, Outro), a conta bancária, a data, o valor, a **conta do plano** (obrigatória e analítica: Despesa para saída, Receita para entrada; sugerida pelo tipo: tarifa, IOF e juros pagos → "3.5 Financeiras"; juros recebidos e rendimento → "1.3 Receitas financeiras"), o centro de custo (opcional) e o **motivo** (obrigatório, até 200 caracteres), e confirmo "Lançar", **então** é criada uma transação `BankEntry` com o sinal do tipo, ligada ao lançamento (`bank_entries`), e a mensagem de sucesso diz "Lançamento registrado".
12. **[Segurança]** **Dado** um lançamento sem OP gravado, **quando** grava, **então** os membros do Proprietário são notificados ("{tipo} de {valor} lançado sem OP em {conta} por {usuário}: {motivo}.") e ele aparece na lista "Movimentos sem OP" do extrato e no filtro correspondente, com `Tag` "Sem OP".
13. **Dado** `finance.pay` e um lançamento sem OP, **quando** clico "Estornar lançamento", informo o motivo e confirmo, **então** uma transação inversa é criada, o lançamento fica marcado como estornado e o Proprietário é notificado; nada é apagado.
14. **[Segurança]** **Dado** a data de uma transferência ou de um lançamento mais de `finance.backdate_days` (padrão 7) antes de hoje, **quando** confirmo, **então** o motivo da data retroativa é obrigatório e o Proprietário é notificado (como na FIN-01 CA 14).

**Regras e permissões**
15. **[Segurança]** **Dado** uma transação de recebimento ou pagamento, **quando** tento editá-la, apagá-la ou estorná-la pelo extrato, **então** não há rota: o estorno é feito no título (FIN-01) ou na OP (FIN-03a); o extrato só cria e estorna transferências e lançamentos sem OP; um teste de arquitetura garante que só o caso de uso de lançamento sem OP cria `BankEntry`.
16. **Dado** o módulo `finance` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; sem `finance.pay`, transferir, lançar e estornar respondem 403 e os botões somem. `RequireModule` e `perm:` em todos os endpoints; `/{accountId}/statement` com conta excluída ou de outro tipo responde 404.
17. **Dado** um extrato com 5 mil transações no período, **quando** abro, **então** a primeira página carrega em menos de 1 s (paginação server-side, saldo acumulado calculado no servidor por janela, índice `(bank_account_id, date)`).
18. **Dado** qualquer transferência ou lançamento, **quando** abro o histórico, **então** vejo criação e estorno com usuário, data e motivo.

## Regras de negócio e técnicas
- **Saldo** = `opening_balance` (ou 0) + Σ `amount` das transações com `date` ≤ dia; transações sempre com `date` ≥ `opening_date` (FIN-01 CA 24). Consulta `BankBalanceQuery` da FIN-05.
- **Transferência** (`bank_transfers`): `from_account_id`, `to_account_id`, `amount`, `date`, `description`, `backdate_reason`, `reversed_at`, `reversal_reason`, `client_request_id`; as duas transações levam `transfer_id`.
- **Lançamento sem OP** (`bank_entries`): `bank_account_id`, `kind` (`BankFee` | `Iof` | `InterestPaid` | `InterestReceived` | `Yield` | `Other`), `date`, `amount` (com sinal), `account_id` (plano), `cost_center_id`, `reason`, `backdate_reason`, `transaction_id`, `reversed_at`, `reversal_reason`, `client_request_id`. Tipo novo de transação `BankEntry` (o `Fee`/`Interest` do 03 passa a ser o `kind` do lançamento).
- **Parâmetro** `finance.transfer_notify_amount` em `finance.parameters` (ADR 0016 adendo M6).
- **Dinheiro** `numeric(18,2)`; **datas** `date`; Guid v7; auditoria `IAuditable` em `bank_transfers` e `bank_entries`; transações imutáveis; notificações síncronas.

## Permissões, eventos e tabelas
- **Permissões (já no `ModuleCatalog`):** `finance.view` (extrato, saldos), `finance.pay` (transferir, lançar sem OP, estornar), `finance.approve` (transferência para conta nova).
- **Eventos:** nenhum.
- **Tabelas:** `finance.financial_transactions` (+ `transfer_id`, `bank_entry_id`; tipo `BankEntry`); `finance.bank_transfers` (nova); `finance.bank_entries` (nova).

## Telas e estados
- **Financeiro → Contas e extrato** (lista da CAD-05b; grupo "Caixa e pagamentos") ganha a coluna "Saldo atual" e o rodapé consolidado; cada conta leva ao extrato.
- **Extrato da conta** (`/app/financeiro/contas/$id/extrato`): período na URL (padrão: mês corrente), DataTable com data, descrição, origem (link), entrada, saída, saldo (`tabular-nums`, negativo destacado) e `Tag` "Sem OP"; filtro "Movimentos sem OP".
- **Diálogos** (`size="lg"`, saldo antes e depois): "Transferir" / "Transferência registrada"; "Estornar transferência" / "Transferência estornada"; "Lançar tarifa ou rendimento" / "Lançamento registrado"; "Estornar lançamento" / "Lançamento estornado".
- **Estados:** carregando; vazio ("Nenhuma movimentação no período." + "Ver mês anterior"); erro; modo leitura.
- **Propositivo:** a conta de origem vem com a conta padrão; a data vem com hoje; a conta do plano é sugerida pelo tipo do lançamento; depois da transferência, "Ver extrato do destino".
- **400 px:** colunas sempre visíveis: data, descrição, valor e saldo; origem na linha expansível.
- **Ajuda:** `Ajuda/financeiro-extrato.md`; a ajuda `financeiro-contas.md` ganha "Saldo atual", "Transferências" e "Lançamento sem OP"; `Ajuda/configuracoes-parametros.md` ganha "Aviso de transferência"; `<Dica>` em "Saldo anterior" e "Sem OP".

## Plano de testes
- **Unidade:** saldo acumulado e saldo anterior (CA 1, 2); sinal e conta sugerida por tipo de lançamento (CA 11).
- **Integração (Testcontainers, obrigatória):** extrato com dataset fixo (CA 1, 2); conta sem saldo inicial (CA 3); saldo atual sem futuras (CA 4); transferência e estorno com as duas pernas na mesma transação e idempotência (CA 5, 10); validações (CA 6); notificação acima do limite e de conta nova (CA 7, 9); transferência para conta nova com e sem `finance.approve` (CA 8); lançamento sem OP, notificação, lista e estorno (CA 11–13); data retroativa (CA 14); sem rota de edição de transação (CA 15); três camadas (CA 16); desempenho com 5 mil transações (CA 17); auditoria (CA 18).
- **Arquitetura:** só o lançamento sem OP cria `BankEntry`; só a OP cria `Payment`.
- **E2E (Playwright):** transferir e conferir os dois extratos; lançar uma tarifa e vê-la em "Movimentos sem OP".

## Fora do escopo
- Conciliação com extrato do banco e OFX: BNK-03 (que passa a sugerir lançamentos sem OP a partir do extrato).
- Fluxo de caixa: FIN-05.

## Pontos em aberto
Decididos pelo PO em 20/10/2026 (seção 1.1, itens 10 e 11, e D4, D8):
1. Transferência com `finance.pay` (o Financeiro recebe a ação por seed patch, FIN-03a CA 1).
2. Lançamento sem OP nesta story (D4), com `finance.pay`, conta do plano, motivo, aviso ao Proprietário e a lista "Movimentos sem OP".
3. P-18 decidida (ADR 0021).

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers), arquitetura e E2E passando.
- [ ] Migration criada (não destrutiva); parâmetro com seed idempotente.
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Telas com estados de carregando, vazio e erro.
- [ ] Ajuda do extrato, da lista e dos parâmetros; teste de cobertura passando.
- [ ] Confirmação de efeito financeiro e próximos passos aplicados.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` e `docs/04-fluxos-estados.md` (exceção "sem OP") atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Consulta de extrato com saldo por janela (`SUM() OVER`), paginada.
- [ ] Entidades `BankTransfer` e `BankEntry` e os casos de uso de transferir, lançar e estornar.
- [ ] Notificação de conta nova no caso de uso da CAD-05b.
- [ ] Endpoints `/api/v1/finance/bank-accounts/{id}/statement`, `/api/v1/finance/transfers`, `/api/v1/finance/bank-entries`; telas e ajuda.

## Quality gates
- Agentes previstos: @dev, @qa, cybersecurity (lançamento sem OP e transferência).
- Gate: CA 1, 2, 5, 8, 11 e 12 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 20/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fase 2).

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

- **Correções feitas na validação (19/10/2026):** extrato sem lançamento avulso; transação de título só estornada na origem; desempenho do extrato com valor.
- **Correções da revalidação (20/10/2026):** re-estimada de P para M; lançamento bancário sem OP (D4, CA 11–13), que substitui o ponto em aberto 2 da versão anterior; transferência acima do limite notifica (D8, CA 7) e para conta nova exige `finance.approve` (§2.2-M4, CA 8); conta nova notifica o Proprietário (CA 9); data retroativa (CA 14); idempotência; verbo "Transferir"; saldo atual pela consulta da FIN-05; navegação (D12) e 400 px. CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma pendente.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 19/10/2026 | 0.1 | Story detalhada | River (SM) |
| 19/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 20/10/2026 | 0.3 | Decisões do PO (D4, D8, seção 1.1) e correções da revisão pré-Fase 2 (§2.1-A2, §2.2-M4, §2.3); re-estimada de P para M; CAs renumerados | River (SM) |
| 20/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
