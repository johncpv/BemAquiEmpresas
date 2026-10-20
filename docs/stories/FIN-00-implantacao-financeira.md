# FIN-00 — Implantação financeira

| Campo | Valor |
|---|---|
| ID | FIN-00 |
| Título | Data de corte única, saldo das contas na data de corte, lançamento rápido dos títulos a receber e a pagar em aberto pelo saldo (origem `Opening`) e relatório de conferência "saldo do sistema = saldo do banco"; primeira carga de dado financeiro real na homologação (ADR 0020) |
| Tamanho | M (nova, decisão D2 da revisão pré-Fase 2) |
| Status | Done |
| Requisitos | R-FIN-2 (saldo inicial das contas) · R-FIN-3 · R-FIN-4 · R-FIN-1 · R-PRP-1 · R-PRP-3 · R-SEC-5 |
| Dependências | FIN-01 (títulos a receber, parâmetros do Finance), FIN-02 (títulos a pagar, forma de pagamento), CAD-05b (contas bancárias e saldo inicial), FND-09e (`RowsEditor`, idempotência); **ADR 0020**; **pré-condição:** backup diário verificado e teste de restauração do tenant `konebras` (FND-10b) |
| Bloqueia | Uso real do financeiro na homologação: FIN-05 com dado real, FIN-06a, FIN-03a (pagar os títulos de implantação) |
| Ordem de execução | 3 (Marco 2.1a "Ver o caixa", entre a FIN-02 e a FIN-05) |

## User story
Como **financeiro**, quero **começar o BemAqui numa data de corte, com o saldo de cada conta e os títulos que estão em aberto nesse dia**, para **ter o caixa e o contas a receber e a pagar certos desde o primeiro dia, sem lançar o histórico do Dolibarr**.

## Contexto e referências
- Revisão pré-Fase 2, D2 ("data de corte única, lançamento rápido dos títulos em aberto pelo saldo com origem `Opening`, fora do DRE por competência e da pontualidade, e relatório de conferência"); volume pequeno (P-07: 26 faturas e 164 faturas de fornecedor no Dolibarr, das quais só as em aberto entram), sem importar CSV.
- ADR 0017 e decisão do PO de 18/10/2026: a migração traz só cadastros; títulos e saldos começam no BemAqui (MIG-01e cancelada).
- **ADR 0020:** os dados reais entram na homologação a partir desta story e o banco do tenant é promovido a produção na GOL-01. Pré-condições: backup diário verificado e teste de restauração; testes nunca escrevem no tenant `konebras`.
- ADR 0016 ("Data de corte e `OpeningBalance`": vale a posição no **fim** do dia D); CAD-05b CA 4 e 5 (saldo inicial anulável, travado depois da primeira transação).
- Padrões comuns da Fase 2 (`docs/stories/README.md`).

## Critérios de aceite
**Data de corte**
1. **Dado** `finance.approve` (na Fase 2, só o Proprietário), **quando** abro "Implantação financeira" e informo a data de corte (≤ hoje) e confirmo "Definir data de corte", **então** a data é gravada no parâmetro `finance.cutover_date` (`finance.parameters`, auditado) e a tela segue para os saldos; sem `finance.approve`, a tela fica em modo leitura com o aviso "Quem define a data de corte é o Proprietário.".
2. **Dado** a data de corte definida, **quando** já existe baixa, pagamento, transferência ou lançamento bancário em qualquer conta, **então** a data não muda mais (409 "A data de corte não muda depois do primeiro movimento.").

**Saldos das contas**
3. **Dado** `finance.edit` e o passo "Saldos das contas", **quando** informo, para cada conta ativa, o saldo no **fim do dia** da data de corte e confirmo "Salvar saldos", **então** cada conta grava `opening_balance` e `opening_date` = data de corte (pelo comando da CAD-05b, que continua travando o saldo depois da primeira transação), e a lista mostra "Saldo informado" ou a `Tag` "Saldo inicial não informado".
4. **Dado** uma conta com transação, **quando** tento mudar o saldo de implantação, **então** 409 "A conta já tem movimento. Corrija com um lançamento." (CAD-05b CA 5).

**Títulos em aberto**
5. **Dado** `finance.create` e o passo "A receber em aberto", **quando** preencho no `RowsEditor` uma linha por título (cliente, descrição, documento de origem opcional — ex.: o número da fatura no Dolibarr —, emissão opcional, vencimento, **saldo em aberto**, conta de receita — padrão "1.9 Receitas a classificar" —, centro de custo opcional) e clico "Lançar títulos a receber", **então** cada linha vira um título `Open`, origem `Opening`, com `amount` = saldo informado, número `CR` normal, `issue_date` = emissão informada ou a data de corte, na mesma transação e com um `Idempotency-Key` por lote; a mensagem de sucesso diz "{n} títulos a receber lançados ({total})".
6. **Dado** o passo "A pagar em aberto", **quando** preencho uma linha por título (fornecedor, descrição, documento de origem opcional, vencimento, **saldo líquido em aberto**, conta de despesa — padrão a última conta do fornecedor, senão "3.9 Despesas a classificar" —, centro opcional, forma de pagamento opcional) e clico "Lançar títulos a pagar", **então** cada linha vira um título `Open`, origem `Opening`, sem retenções (o saldo já é o líquido), que depois entra numa OP normalmente (FIN-03a).
7. **Dado** uma linha inválida (terceiro sem o papel, saldo ≤ 0, conta agrupadora), **quando** lanço o lote, **então** a API responde 422 com o erro na linha certa (`lines[3].amount`, FND-09e) e nada do lote é gravado; vencimento no passado é aceito (título vencido) e aparece como "Vencido" logo depois.
8. **Dado** um título de origem `Opening` ainda sem baixa, **quando** está errado, **então** é cancelado com motivo pelo fluxo normal (FIN-01 CA 17, FIN-02 CA 12), sem o limite de cancelamento enquanto a implantação não foi concluída, e lançado de novo.

**Conferência e conclusão**
9. **Dado** o passo "Conferência", **quando** abro, **então** vejo por conta: o saldo do sistema na data de corte, um campo "Saldo no extrato do banco" para digitar e a diferença (zero em ok, diferente em crítico); e os totais de títulos de implantação a receber e a pagar, por cliente e fornecedor, com a quantidade; o relatório pode ser baixado em CSV.
10. **Dado** `finance.approve` e a conferência sem diferença (ou com as diferenças justificadas num campo de observação), **quando** clico "Concluir implantação" e confirmo, **então** `finance.cutover_completed_at` é gravado com o usuário, a tela fica em modo leitura com o resumo, e novos títulos `Opening` são recusados (409 "A implantação foi concluída em {data}.").
11. **Dado** os títulos de origem `Opening`, **quando** outros relatórios os leem, **então** ficam **fora** do DRE por competência (a FIN-08 lê `origin`; a regra fica registrada no `03-modelo-dados.md`) e do cálculo de pontualidade (FIN-06b), e **dentro** do aging (FIN-06a) e do fluxo de caixa (FIN-05); as listas mostram a `Tag` "Implantação".

**Pré-condição da primeira carga real (ADR 0020)**
12. **Dado** o deploy desta story na homologação, **quando** o PO vai fazer a primeira carga real no tenant `konebras`, **então** o backup diário da homologação está no ar (o teste de restauração é recomendado e não bloqueia: adendo da ADR 0020, PO em 20/10/2026), e, quando feito, existe em `docs/operacao/restauracao.md` o registro do teste de restauração do tenant `konebras` feito com o backup real (data, duração, segredos decifrados), com data posterior ao início do backup diário, e o último dump do bucket tem data e tamanho esperados (passo manual do runbook, conferido pelo PO).
13. **Dado** os testes manuais, E2E e smoke desta story, **quando** rodam na homologação, **então** usam um tenant de teste (ex.: `demo`), nunca o `konebras` (ADR 0020); o pipeline recusa E2E contra o host da Konebras.

**Permissões e auditoria**
14. **Dado** o módulo `finance` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; sem `finance.create`, os passos de títulos ficam em modo leitura e a API responde 403; sem `finance.approve`, data de corte e conclusão respondem 403. `RequireModule` e `perm:`.
15. **Dado** qualquer passo, **quando** abro o histórico, **então** vejo quem definiu a data de corte, os saldos (antes e depois), os lotes lançados e a conclusão, com usuário e data.

## Regras de negócio e técnicas
- **Data de corte:** parâmetro `finance.cutover_date` (`date`) e `finance.cutover_completed_at` (`timestamptz`) em `finance.parameters` (ADR 0016 adendo M6). Vale a posição no fim do dia (ADR 0016).
- **Títulos `Opening`:** os mesmos `receivables` e `payables` da FIN-01 e FIN-02, com `origin = Opening` e `origin_id` nulo; `opening_reference` (texto, até 40, o documento de origem); idempotência por lote (`client_request_id` no lote, `finance.opening_batches`: id, tipo, quantidade, total, criado por).
- **Sem transação bancária:** o saldo das contas é o `opening_balance`; os títulos `Opening` não criam transação.
- **Sem importação de arquivo:** lançamento pelo editor de linhas, com "Duplicar linha" e navegação por teclado.
- Dinheiro `numeric(18,2)`; datas `date`; Guid v7; auditoria `IAuditable` em lotes e parâmetros.

## Permissões, eventos e tabelas
- **Permissões (já no `ModuleCatalog`):** `finance.approve` (data de corte, conclusão), `finance.edit` (saldos), `finance.create` (lotes de títulos), `finance.view` (conferência).
- **Eventos:** nenhum.
- **Tabelas:** `finance.opening_batches` (nova); `finance.receivables` e `finance.payables` (+ `opening_reference`); chaves `finance.cutover_date` e `finance.cutover_completed_at` em `finance.parameters`.

## Telas e estados
- **Financeiro → Implantação financeira** (`/app/financeiro/implantacao`; grupo "Configuração financeira"): página em passos (Data de corte → Saldos das contas → A receber em aberto → A pagar em aberto → Conferência), com o passo na URL e o progresso visível.
- **Verbos:** "Definir data de corte" / "Data de corte definida"; "Salvar saldos" / "Saldos salvos"; "Lançar títulos a receber" / "Títulos a receber lançados"; "Lançar títulos a pagar" / "Títulos a pagar lançados"; "Concluir implantação" / "Implantação concluída".
- **Estados:** carregando; vazio por passo ("Nenhum título a receber em aberto na data de corte? Siga para o próximo passo."); erro; modo leitura depois de concluída.
- **Propositivo:** a data de corte sugerida é o último dia do mês anterior; contas e condições já vêm da migração (MIG-01b); o cliente e o fornecedor sugerem a conta do último título; ao concluir, "Próximos passos": "Ver fluxo de caixa" (FIN-05) e "Ver A receber vencido".
- **400 px e teclado:** E2E do editor de títulos em cartões e só pelo teclado.
- **Ajuda:** `Ajuda/financeiro-implantacao.md` (abas `data-de-corte`, `saldos`, `receber`, `pagar`, `conferencia`); `<Dica>` em "Data de corte", "Saldo no fim do dia" e "Implantação".

## Plano de testes
- **Unidade:** regras da data de corte (CA 1, 2); validação das linhas (CA 7); conferência (CA 9).
- **Integração (Testcontainers, obrigatória: movimenta financeiro):** data de corte e trava depois do primeiro movimento (CA 1, 2); saldos pela CAD-05b e trava com transação (CA 3, 4); lotes de títulos com idempotência e rollback do lote inválido (CA 5–7); cancelamento antes da conclusão (CA 8); conclusão e recusa de novo `Opening` (CA 10); `origin = Opening` fora da pontualidade e dentro do aging, com dataset fixo (CA 11); três camadas (CA 14); auditoria (CA 15).
- **Manual:** CA 12 (registro do teste de restauração e do backup) e CA 13 (tenant de teste na homologação).
- **E2E (Playwright, tenant de teste):** implantação completa com 3 contas, 5 títulos a receber e 5 a pagar; editor a 400 px.

## Fora do escopo
- Importação de planilha ou do Dolibarr (decisão do PO, ADR 0017).
- Histórico de pagamentos anteriores à data de corte e pontualidade antiga (MIG-01e cancelada).
- DRE: FIN-08 (Fase 4) só aplica a regra do CA 11.

## Pontos em aberto
Nenhum. Decidido pelo PO em 20/10/2026 (D1 alternativa b, D2). Pré-condição externa, com dono definido: backup verificado e teste de restauração do tenant `konebras` (FND-10b, PO), antes da primeira carga real.

## Definition of Done
- [x] Critérios de aceite atendidos.
- [x] Testes de unidade, integração (Testcontainers) e E2E passando.
- [ ] Passos manuais do CA 12 e 13 conferidos pelo PO (antes da primeira carga real).
- [x] Migration criada (não destrutiva).
- [x] Permissões só do `ModuleCatalog`.
- [x] Tela com estados por passo.
- [x] Ajuda de cada passo; teste de cobertura passando.
- [x] Padrões preenchidos e próximos passos aplicados.
- [x] Auditoria ligada.
- [x] OpenAPI e cliente Orval regenerados.
- [x] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [x] `OpeningBatch` e os casos de uso de lote (receber e pagar) sobre as entidades da FIN-01 e FIN-02.
- [x] Parâmetros `finance.cutover_date` e `finance.cutover_completed_at`; conferência e CSV.
- [x] Endpoints `/api/v1/finance/opening` (+ `/cutover`, `/balances`, `/receivables`, `/payables`, `/reconciliation`, `/complete`); tela em passos e ajuda.

## Quality gates
- Agentes previstos: @dev, @qa, devops (pré-condição de backup e restauração).
- Gate: CA 5, 10, 11 e 12 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 20/10/2026.

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK (CA 12 e 13 por passo manual descrito, DoR item 1) |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto registrados sem decidir | OK |

- **Correções feitas na validação (20/10/2026):** a data de corte e a conclusão ficaram com `finance.approve` (decisão de uma vez, do Proprietário); títulos de implantação pelo saldo líquido, sem retenção; a pré-condição da ADR 0020 virou CA manual (CA 12) e a regra "testes nunca escrevem no tenant `konebras`" virou CA (CA 13).
- **Pré-condições para `Ready`:** nenhuma de decisão. Pré-condição externa explícita para a primeira carga real: backup verificado e teste de restauração (CA 12).
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 20/10/2026 | 0.1 | Story nova (D2 da revisão pré-Fase 2, ADR 0020) | River (SM) |
| 20/10/2026 | 0.2 | Validação do PO: 10/10, `Ready` com pré-condição externa | Pax (PO) |
| 20/10/2026 | 0.3 | PO liberou a primeira carga real sem esperar o teste de restauração (adendo da ADR 0020); CA 12 ajustado | River (SM) |
| 20/10/2026 | 1.0 | Implementada. Desvios: data de corte, conclusão (com o usuário) e observação ficam em `finance.opening_setup` (uma linha, com versão), porque `finance.parameters` só guarda número; idempotência do lote pelo filtro comum (`Idempotency-Key`, sem `client_request_id`); o lote vai de 1 a 500 linhas; a forma de pagamento do título a pagar de implantação é informada depois, no próprio título (a API aceita, o editor não mostra a coluna); o extrato digitado na conferência não é gravado (vai no CSV, gerado no navegador); o `RowsEditor` ganhou "Duplicar" e o `Combobox` ganhou `aria-label` | @dev |
