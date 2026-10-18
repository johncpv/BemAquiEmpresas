# MIG-01e — Títulos em aberto e histórico de pagamentos do Dolibarr

| Campo | Valor |
|---|---|
| ID | MIG-01e |
| Título | Importar faturas de cliente (`llx_facture`) e de fornecedor (`llx_facture_fourn`) em aberto como títulos a receber e a pagar, e as faturas pagas de cliente dos últimos 12 meses como histórico de pontualidade |
| Tamanho | M (quebra da MIG-01) |
| Status | Draft |
| Requisitos | R-TER-5 · R-FIN-3 · R-FIN-4 · R-FIN-5 (nenhum pagamento sem OP) · R-TER-4 e R-FIN-11 (histórico de atraso para a projeção ajustada) · R-PRP-7 |
| Dependências | MIG-01b (terceiros, condições, contas), CAD-05a (plano de contas); **FIN-01** (`receivables`) e **FIN-02** (`payables`), que são da Fase 2. **Pré-condição:** pontos em aberto 1 e 2 |
| Bloqueia | GOL-01 (carga final dos títulos), FIN-07 (projeção ajustada com histórico) |
| Ordem de execução | 15 (depois da FIN-01 e da FIN-02) |

## User story
Como **financeiro**, quero **que as faturas em aberto do Dolibarr entrem como títulos a receber e a pagar, e que os pagamentos dos clientes no último ano entrem como histórico**, para **cobrar e pagar pelo BemAqui desde a virada e ter a média de atraso de cada cliente na projeção de caixa**.

## Contexto e referências
- Backlog MIG-01: "títulos em aberto".
- Revisão pré-Fase 0 §3: a MIG-01 importa os títulos pagos que existirem no Dolibarr, para alimentar a pontualidade dos clientes.
- PRD R-FIN-3, R-FIN-4, R-FIN-5; `docs/04-fluxos-estados.md` §1 (estados de título a receber e a pagar; "nenhum pagamento é baixado sem OP aprovada"), §3.5 (média de atraso dos últimos 12 meses; sem histórico, média geral do tenant).
- `docs/03-modelo-dados.md` → `finance.receivables`, `finance.payables` (`origin` Purchase/Payroll/Installer/Recurring/Manual), `customer_payment_stats` (view).
- `docs/06-pendencias-riscos.md` P-07: 26 faturas de cliente e 164 faturas de fornecedor no Dolibarr.
- Numeração: CR-AAAA-00001 e CP-AAAA-00001 (04 §4).

## Critérios de aceite
**A receber**
1. **Dado** uma `llx_facture` validada e não paga (`fk_statut` = 1, `paye` = 0) de um cliente importado, **quando** rodo a carga com `--confirm-finance` e a data de corte, **então** nasce um título a receber com número CR do BemAqui, a referência do Dolibarr (`ref`) na descrição e na chave externa, emissão = `datef`, vencimento = `date_lim_reglement`, valor = `total_ttc` (`numeric(18,2)`) e valor pago = soma de `llx_paiement_facture` até a data de corte; o estado fica `Open` ou `PartiallyPaid`, e `Overdue` é calculado (04 §1).
2. **Dado** uma fatura paga (`paye` = 1) com pagamento nos 12 meses antes da data de corte, **quando** a carga roda, **então** nasce um título `Paid` marcado como histórico importado (com vencimento e data do último pagamento), usado só para a pontualidade (`customer_payment_stats`); ele não gera transação bancária nem entra no fluxo de caixa realizado.
3. **Dado** faturas em rascunho (`fk_statut` = 0) ou abandonadas (`fk_statut` = 3), **quando** a carga roda, **então** saem como "ignorado" com o motivo.
4. **Dado** notas de crédito (`type` = 2) e faturas de adiantamento (`type` = 3), **quando** a carga roda, **então** seguem a regra do ponto em aberto 3 (proposta: ignorar com aviso para conferência manual).

**A pagar**
5. **Dado** uma `llx_facture_fourn` validada e não paga de um fornecedor importado, **quando** a carga roda com `--confirm-finance`, **então** nasce um título a pagar com número CP, a referência do Dolibarr e a do fornecedor (`ref_supplier`), vencimento, valor = `total_ttc` e valor pago parcial, em estado `Open`.
6. **[Segurança]** **Dado** um título a pagar importado, **quando** alguém tenta baixá-lo, **então** vale a regra dura do 04 §1: só com ordem de pagamento aprovada (FIN-03); a importação nunca cria título `Paid` a pagar nem baixa pagamento.

**Classificação**
7. **Dado** um título importado, **quando** não há de-para da conta contábil (ponto em aberto 4), **então** ele entra na conta analítica "A classificar" (receita ou despesa, criada pela carga se não existir) e aparece no relatório para classificação; o centro de custo fica vazio.

**Confirmação, reexecução e conferência**
8. **Dado** a carga sem `--confirm-finance`, **quando** rodo, **então** nada é gravado e o relatório mostra os títulos que seriam criados, com a mensagem "Confirme com --confirm-finance para lançar os títulos." (R-PRP-7).
9. **Dado** a carga feita, **quando** rodo de novo, **então** nenhum título é duplicado; um título que já recebeu baixa, OP ou edição no BemAqui não é alterado e sai como conflito; um título ainda intocado é atualizado se o Dolibarr mudou (ex.: novo pagamento parcial antes da data de corte).
10. **Dado** o relatório, **quando** confiro, **então** vejo, por cliente e por fornecedor, a quantidade e o total em aberto no Dolibarr e no BemAqui, e os dois batem; vejo também a média de atraso calculada por cliente a partir do histórico importado.
11. **Dado** um título de um terceiro não importado (erro na MIG-01b), **quando** a carga roda, **então** sai como erro "terceiro não importado (id {fk_soc})" e entra quando o terceiro for corrigido e a carga reexecutada.

## Regras de negócio e técnicas
- Grava só por `IReceivableImport` e `IPayableImport` em `Finance.Contracts` (**propostas**), que aplicam as regras da FIN-01 e da FIN-02 e usam o `INumberSequence`.
- **Origem do título a pagar:** `Manual` com a chave externa (proposta) ou uma origem nova `Migration` (ponto em aberto 5).
- **Marca de histórico:** campo `is_imported_history` (**proposta**) no título a receber, para excluir do fluxo de caixa realizado e do aging.
- **Dinheiro** `numeric(18,2)`; **datas** de emissão e vencimento como `date`.
- **Data de corte:** pagamentos depois dela não entram (o título fica aberto no BemAqui e é baixado lá).

## Permissões, eventos e tabelas
- **Permissões:** nenhuma (CLI, MIG-01a).
- **Eventos:** os que a FIN-01 e a FIN-02 publicarem ao criar título; nenhum novo.
- **Tabelas:** `finance.receivables` (+ `is_imported_history`, **proposta**), `finance.payables` (FIN-01/FIN-02); `migration.*`.

## Telas e estados
- Sem tela. Os títulos aparecem em Caixa e pagamentos (FIN-01/FIN-02); os importados mostram a referência do Dolibarr.

## Plano de testes
- **Unidade:** estado a partir de `fk_statut`, `paye` e pagamentos (CA 1–3); valor pago parcial até a data de corte; média de atraso a partir do histórico (CA 10).
- **Integração (Testcontainers PostgreSQL + MariaDB, fixture anonimizada):** a receber aberto, parcial, vencido e pago no histórico (CA 1, 2); ignorados (CA 3, 4); a pagar aberto e bloqueio de baixa sem OP (CA 5, 6); conta "A classificar" (CA 7); sem confirmação (CA 8); reexecução com título intocado e com título baixado (CA 9); conferência dos totais (CA 10); terceiro ausente (CA 11).
- **Ensaio manual:** simulação contra a cópia da base real e conferência dos totais com o PO (26 faturas de cliente e 164 de fornecedor).

## Fora do escopo
- Lançamentos bancários individuais e conciliação: BNK-03.
- Contratos, projetos e orçamentos do Dolibarr (não migram).
- Boletos já emitidos no Dolibarr.

## Pontos em aberto
1. **Quando executar** (pré-condição): os títulos precisam das tabelas da FIN-01 e da FIN-02 (Fase 2). Proposta: rodar depois delas e de novo na GOL-01, com a data de corte da virada.
2. **Histórico de pagos** (pré-condição): importar os pagos de cliente dos últimos 12 meses (proposta, conforme a revisão §3) e como guardá-los (título `Paid` com marca de histórico, proposta, ou só uma tabela de estatística).
3. **Notas de crédito e adiantamentos:** ignorar com aviso (proposta) ou abater do título.
4. **Conta contábil dos títulos importados:** conta "A classificar" (proposta) ou de-para pelos códigos contábeis do Dolibarr (`accountancy_code_*`).
5. **Origem do título a pagar importado:** `Manual` (proposta) ou origem nova `Migration`.
6. **Faturas de fornecedor pagas:** importar como histórico também (sem uso previsto) ou só as abertas (proposta).
7. **Fechamento da Fase 1:** mesma decisão da MIG-01d (ponto em aberto 5): esta story fica na Fase 1 com execução depois da FIN-01 e da FIN-02 (proposta).

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade e integração (Testcontainers PostgreSQL + MariaDB) passando.
- [ ] `IReceivableImport` e `IPayableImport` no `Finance.Contracts`.
- [ ] Mapeamento atualizado em `docs/operacao/migracao-dolibarr.md`.
- [ ] Simulação contra a cópia da base real conferida com o PO.
- [ ] `docs/03-modelo-dados.md` atualizado (`is_imported_history`); story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `ReceivablesLoader` e `PayablesLoader` (`IDolibarrLoader`) com `--confirm-finance`.
- [ ] Interfaces de importação no `Finance.Contracts` sobre as regras da FIN-01/FIN-02.
- [ ] Relatório de conferência por terceiro e média de atraso.
- [ ] Fixture com aberto, parcial, pago, rascunho, abandonado e nota de crédito.

## Quality gates
- Agentes previstos: @dev, @data-engineer, @qa.
- Gate: CA 6 (sem baixa sem OP), CA 8 (confirmação) e CA 10 (totais batem) obrigatórios.

## Validação do PO
Checklist do @po (Pax), 18/10/2026.

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo (cabeçalho, user story, contexto, CAs, regras, permissões/tabelas, telas, testes, fora do escopo, pontos em aberto, DoD, tarefas) | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas do CLAUDE.md) que se aplicam | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK com ressalva: depende da FIN-01 e da FIN-02 (Fase 2); registrado nos pontos em aberto 1 e 7 |
| 7 | Plano de testes cobre os CAs (unidade, integração com Testcontainers, arquitetura, E2E quando cabe) | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas como proposta) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK (sem tela) |
| 10 | DoD do CLAUDE.md ajustado; pontos em aberto registrados sem decidir | OK |

- **Correções feitas na validação:** Acrescentado o ponto em aberto 7 (fechamento da Fase 1 × dependência da FIN-01/FIN-02).
- **Pré-condições para `Ready`:** Pontos em aberto 1 (quando executar), 2 (histórico de pagos) e 7 (fechamento da Fase 1).
- **Prontidão:** 7/10. **Veredito:** GO condicional (permanece `Draft` até as pré-condições serem decididas).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 18/10/2026 | 0.1 | Story detalhada (quebra da MIG-01) | River (SM) |
| 18/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
