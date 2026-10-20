# FIN-04 — Movimentação bancária: extrato interno, saldo e transferências

| Campo | Valor |
|---|---|
| ID | FIN-04 |
| Título | Extrato interno por conta (saldo inicial + transações, saldo acumulado), saldo atual por conta e consolidado, transferência entre contas da empresa e estorno da transferência |
| Tamanho | P |
| Status | Draft |
| Requisitos | R-FIN-2 (contas bancárias e caixas, saldo inicial, transferências) · R-FIN-10 (base do realizado) · R-PRP-7 · R-SEC-5 |
| Dependências | FIN-01 (`financial_transactions`), FIN-03a (pagamentos), CAD-05b (contas, saldo inicial) |
| Bloqueia | FIN-05 (realizado e saldo atual), DSH-01 (saldo atual), BNK-03 (conciliação compara com este extrato) |
| Ordem de execução | 4 (Marco 2.1 — caixa e pagamentos) |

## User story
Como **financeiro**, quero **ver o extrato de cada conta com o saldo dia a dia e transferir dinheiro entre as contas da empresa**, para **conferir com o banco e saber quanto tenho disponível em cada lugar**.

## Contexto e referências
- PRD R-FIN-2; `03-modelo-dados` → `financial_transactions` (`type` Transfer, `amount` com sinal), `bank_accounts` (`opening_balance`, `opening_date`).
- CAD-05b CA 4 (conta sem saldo inicial com a `Tag` "Saldo inicial não informado"), CA 5 (saldo travado depois de transação).
- `04-fluxos-estados` §1: tarifa e débito automático "sem OP" só pela conciliação (BNK-03).
- Padrões comuns da Fase 2 (`docs/stories/README.md`).

## Critérios de aceite
1. **Dado** `finance.view` e a conta "Inter" com saldo inicial R$ 10.000,00 em 01/11/2026, um recebimento de R$ 425,00 em 05/11 e um pagamento de R$ 1.200,00 em 06/11, **quando** abro o extrato de 01/11 a 30/11, **então** vejo a linha "Saldo inicial" R$ 10.000,00, as duas transações em ordem de data (e de criação no mesmo dia) com descrição, documento de origem (link para o título ou a OP), valor com sinal e saldo acumulado R$ 10.425,00 e R$ 9.225,00, e o saldo final do período R$ 9.225,00.
2. **Dado** um período que começa depois do saldo inicial, **quando** abro o extrato, **então** a primeira linha é "Saldo anterior" calculado (saldo inicial + transações antes do período) e bate com o saldo final do período anterior (teste com dataset fixo).
3. **Dado** uma conta sem saldo inicial, **quando** abro o extrato, **então** o saldo parte de zero, a `Tag` "Saldo inicial não informado" aparece no topo e o próximo passo "Informar saldo inicial" leva ao painel da conta (CAD-05b), enquanto a conta não tiver transação.
4. **Dado** `finance.view`, **quando** abro "Contas e caixas", **então** cada conta ativa mostra o saldo atual (até hoje) e o rodapé mostra o saldo consolidado; transações com data futura não entram no saldo atual.
5. **Dado** `finance.pay` (ver Pontos em aberto 1), **quando** clico "Transferir entre contas", escolho origem e destino ativos e diferentes, valor, data e descrição e confirmo "Transferir", **então** na mesma transação são criadas duas transações `Transfer` ligadas por `transfer_id` (−valor na origem, +valor no destino), o toast diz "Transferência registrada" e os dois extratos mostram a contrapartida com link.
6. **Dado** origem igual ao destino, valor ≤ 0, conta inativa ou data anterior ao saldo inicial de qualquer das contas, **quando** confirmo, **então** 422 com mensagem PT-BR; saldo da origem que fica negativo mostra o aviso âmbar "O saldo de {conta} fica negativo." e não bloqueia.
7. **Dado** `finance.pay` e uma transferência, **quando** clico "Estornar transferência", informo o motivo e confirmo "Estornar transferência", **então** duas transações inversas são criadas, a transferência fica marcada como estornada e nada é apagado; os membros do Proprietário são notificados.
8. **[Segurança]** **Dado** uma transação de recebimento ou pagamento, **quando** tento editá-la, apagá-la ou estorná-la pelo extrato, **então** não há rota: o estorno é feito no título (FIN-01) ou na OP (FIN-03a); o extrato só cria e estorna transferências.
9. **Dado** o módulo `finance` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; sem `finance.pay`, transferir e estornar respondem 403 e o botão some. `RequireModule` e `perm:` em todos os endpoints; `/{accountId}/statement` com conta excluída ou de outro tipo responde 404.
10. **Dado** um extrato com 5 mil transações no período, **quando** abro, **então** a primeira página carrega em menos de 1 s (paginação server-side, saldo acumulado calculado no servidor por janela).

## Regras de negócio e técnicas
- **Saldo** = `opening_balance` (ou 0) + Σ `amount` das transações com `date` ≤ dia; transações sempre com `date` ≥ `opening_date` (FIN-01 CA 17).
- **Transferência** (`bank_transfers`, proposta): `from_account_id`, `to_account_id`, `amount`, `date`, `description`, `reversed_at`, `reversal_reason`; as duas transações levam `transfer_id`.
- **Dinheiro** `numeric(18,2)`; **datas** `date`; Guid v7; auditoria `IAuditable` em `bank_transfers`; transações imutáveis.
- **Sem lançamento avulso** de tarifa, juros ou rendimento nesta story (ver Pontos em aberto 2).

## Permissões, eventos e tabelas
- **Permissões (já no `ModuleCatalog`):** `finance.view` (extrato, saldos), `finance.pay` (transferir e estornar).
- **Eventos:** nenhum.
- **Tabelas:** `finance.financial_transactions` (+ `transfer_id`); `finance.bank_transfers` (**proposta**).

## Telas e estados
- **Financeiro → Contas e caixas** (CAD-05b) ganha a coluna "Saldo atual" e o rodapé consolidado; cada conta leva ao extrato.
- **Extrato da conta** (`/app/financeiro/contas/$id/extrato`): período na URL (padrão: mês corrente), DataTable com data, descrição, origem (link), entrada, saída, saldo (`tabular-nums`, negativo destacado).
- **Diálogos:** "Transferir entre contas" / "Transferência registrada"; "Estornar transferência" / "Transferência estornada".
- **Estados:** carregando; vazio ("Nenhuma movimentação no período." + "Ver mês anterior"); erro; modo leitura.
- **Propositivo:** conta de origem vem com a conta padrão; a data vem com hoje; depois da transferência, "Ver extrato do destino".
- **Ajuda:** `Ajuda/financeiro-extrato.md`; a ajuda `financeiro-contas.md` ganha "Saldo atual" e "Transferências"; `<Dica>` em "Saldo anterior".

## Plano de testes
- **Unidade:** saldo acumulado e saldo anterior (CA 1, 2).
- **Integração (Testcontainers, obrigatória):** extrato com dataset fixo (CA 1, 2); conta sem saldo inicial (CA 3); saldo atual sem futuras (CA 4); transferência e estorno com as duas pernas na mesma transação (CA 5, 7); validações (CA 6); sem rota de edição de transação (CA 8); três camadas (CA 9); desempenho com 5 mil transações (CA 10).
- **E2E (Playwright):** transferir e conferir os dois extratos.

## Fora do escopo
- Conciliação com extrato do banco e OFX: BNK-03. Tarifas e débitos automáticos "sem OP": BNK-03.
- Fluxo de caixa projetado: FIN-05.

## Pontos em aberto
1. **Permissão da transferência.** O catálogo não diz. **Recomendação:** `finance.pay`, porque mexe no caixa como um pagamento (ação reservada ao Proprietário pela ADR 0008, que a concede a quem paga; ver FIN-03a, ponto 4, sobre o Financeiro do seed).
2. **Lançamento avulso de tarifa, juros e rendimento antes da BNK-03.** A regra dura proíbe pagamento sem OP. **Recomendação:** não ter lançamento avulso no MVP antes da BNK-03: tarifa vira CP + OP; rendimento vira título a receber manual. A BNK-03 traz a exceção "sem OP" listada para o dono.
3. **P-18:** ver FIN-01, ponto 5.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers) e E2E passando.
- [ ] Migration criada (não destrutiva).
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Telas com estados de carregando, vazio e erro.
- [ ] Ajuda do extrato e da lista atualizada; teste de cobertura passando.
- [ ] Confirmação de efeito financeiro e próximos passos aplicados.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Consulta de extrato com saldo por janela (`SUM() OVER`), paginada.
- [ ] Entidade `BankTransfer` e casos de uso de transferir e estornar.
- [ ] Endpoints `/api/v1/finance/bank-accounts/{id}/statement`, `/api/v1/finance/transfers`; telas e ajuda.

## Quality gates
- Agentes previstos: @dev, @qa.
- Gate: CA 1, 2 e 5 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 19/10/2026.

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

- **Correções feitas na validação (19/10/2026):** extrato sem lançamento avulso (respeita a regra dura da OP); transação de título só estornada na origem (CA 8); desempenho do extrato com valor (CA 10).
- **Pré-condições para `Ready`:** Pontos em aberto 1 e 2 (com padrão recomendado) e P-18.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 19/10/2026 | 0.1 | Story detalhada | River (SM) |
| 19/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
