# CX-02 — Fechamento de caixa com conferência cega, tolerância de quebra por forma de pagamento, recolhimento do dinheiro ao Cofre, conferência do Pix manual contra o extrato, revisão por quem tem `sales.approve`, relatório em bobina e em PDF e reabertura da última sessão

| Campo | Valor |
|---|---|
| ID | CX-02 |
| Título | Fechamento de caixa com conferência cega, tolerância de quebra por forma de pagamento, recolhimento do dinheiro ao Cofre, conferência do Pix manual contra o extrato, revisão por quem tem `sales.approve`, relatório em bobina e em PDF e reabertura da última sessão |
| Tamanho | G |
| Status | Ready for Review (19/11/2026, branch `feat/cx-02-fechamento`, sobre a OFF-02) |
| Fase | 11 — PDV balcão com cupom não fiscal e offline (marco MVP) |
| Requisitos | Pedido do PO de 15/11/2026 (item 4) · [Fluxos](../04-fluxos-estados.md#2-caixa-e-turno) · [PRD do PDV](../01-PRD.md) regra 5 e seção 6.5 · ADR 0045 (PDF) |
| Dependências | CX-01, PDV-03, IMP-01; PP-22 |
| Bloqueia | CX-03, OFF-03 |
| Paridade com o mercado | Requisitos mínimos 5 do [benchmark](../00-benchmark-mercado.md#7-matriz-de-paridade-cada-requisito-mínimo-e-a-story-que-o-cobre) |
| Ordem de execução | 8 (Fase 11) |

## User story
Como **dono**, quero **que o operador conte o caixa sem saber quanto deveria ter**, para **a diferença aparecer de verdade**, e como **gerente**, quero **aprovar as quebras acima do tolerável**.

## Contexto e referências
- Conferência cega é o controle que o benchmark aponta como "melhor do mercado" no caixa (Alterdata Spice) e o plano adota como padrão.

## Critérios de aceite
### Contagem
1. **Dado** uma sessão `Open` sem venda `Open` ou `InPayment`, **quando** o operador aciona "Fechar caixa", **então** a sessão passa a `Counting`; vendas em espera são listadas para retomar ou descartar antes.
2. **Dado** `Counting`, **quando** o operador conta, **então** informa o valor por forma de pagamento: dinheiro (com contagem por cédula e moeda, opcional) e o total dos comprovantes de cada forma de cartão e de Pix; **a tela não mostra o valor esperado, a diferença nem o total de vendas**.
3. **[Segurança]** **Dado** o operador sem `sales.approve`, **quando** chama qualquer endpoint ou lê o dado local da sessão durante a contagem, **então** o esperado por forma não está disponível: o aparelho não o calcula na tela de contagem e a API não o devolve.
4. **Dado** a contagem confirmada com "Confirmar contagem", **quando** concluo, **então** a sessão passa a `Counted` e a contagem não pode ser refeita pelo operador; recontar exige supervisor, com motivo, e fica registrado.

### Quebra
5. **Dado** `Counted` com a diferença de **cada forma de pagamento**, em módulo, até a tolerância da loja, **quando** o fechamento é processado, **então** a sessão passa a `Closed` e a quebra (sobra ou falta) fica registrada por forma. Sobra numa forma **não** compensa falta em outra.
6. **Dado** `Counted` com alguma forma acima da tolerância, ou sangria acima do dinheiro calculado (CX-01 CA 4), ou aprovações offline acima do limite da loja (OFF-03), **quando** processado, **então** a sessão passa a `PendingReview`, o operador vê só "Fechamento enviado para conferência." e o gerente é notificado.
7. **Dado** `PendingReview` e `sales.approve`, **quando** abro a sessão na retaguarda, **então** vejo esperado, contado e diferença por forma, as vendas, os cancelamentos, os descontos, as sangrias e os suprimentos; "Aprovar fechamento" exige motivo e leva a `Closed`.
8. **[Segurança]** **Dado** quem foi o operador da sessão, **quando** tenta aprovar o próprio fechamento, **então** recebe 403 "Quem operou o caixa não pode aprovar o próprio fechamento.".

### Relatório e reabertura
9. **Dado** a sessão `Closed`, **quando** imprimo o relatório, **então** ele sai em bobina e em PDF (padrão da ADR 0045) com: vendas por forma, quantidade de vendas, cancelamentos, descontos, sangrias, suprimentos, vendas feitas sem rede e a quebra. A via do operador **não** traz esperado nem diferença; a do gerente traz.
10. **[Segurança]** **Dado** a última sessão `Closed` do caixa e `sales.approve`, **quando** aciono "Reabrir caixa" com motivo, **então** ela volta a `Open`, os lançamentos do fechamento são desfeitos (CX-03), a reabertura fica auditada e **o Proprietário recebe um aviso**; **quem operou a sessão não pode reabri-la** (403), e sessão que não é a última não reabre.
11. **Dado** a sessão `Closed`, **quando** o operador volta à tela de venda, **então** vê "Abra o caixa para começar a vender.".

### Recolhimento e Pix
12. **Dado** a contagem do dinheiro, **quando** o operador a confirma, **então** informa também o **fundo de troco que fica** para o próximo turno (padrão: o da loja); o restante é o **recolhimento**, que sai com comprovante em duas vias para o Cofre. Sem isso, o fundo trazido do Cofre a cada abertura faria o saldo da conta Caixa crescer sem parar.
13. **Dado** pagamentos "Pix conferido pelo operador" na sessão, **quando** quem tem `sales.approve` abre o fechamento, **então** vê a lista um a um (hora, valor, operador) para conferir contra o extrato do banco, com "Conferido" e "Não localizado"; Pix não localizado vira quebra da forma Pix e entra no relatório por operador. Com o extrato do banco integrado, o sistema sugere o casamento por valor e hora.

## Regras de negócio e técnicas
- A tolerância é da loja (PP-22), aplicada **por forma de pagamento**, em módulo.
- O esperado é calculado no servidor a partir dos eventos sincronizados. Com eventos ainda na fila, o fechamento fica "Aguardando envio das vendas" antes de calcular a quebra.
- Uma só contagem por fechamento.
- O evento `CashSessionClosed` é publicado quando a sessão chega a `Closed`, não em `Counted`.

## Permissões, eventos e tabelas
- **Permissões:** `pos.create` (contar e fechar o próprio caixa); `sales.approve` (ver esperado, aprovar, reabrir).
- **Endpoints:** `GET /api/v1/pos/cash-sessions`, `GET .../{id}`, `POST .../{id}/approve`, `POST .../{id}/reopen`, `POST .../{id}/pix-check`, `GET .../{id}/report.pdf`, `GET /api/v1/pos/cash-sessions/export`. A sessão de caixa é do `Pos`; as rotas de retaguarda são autorizadas com `sales.*`.
- **Eventos do aparelho:** `CashCountStarted`, `CashCounted`, `CashDeposited`. **Eventos de domínio:** `CashSessionClosed`, `CashSessionReopened`.
- **Tabelas:** `pos.cash_sessions`, `pos.cash_counts`.
- **Auditoria:** contagem, recontagem, aprovação, reabertura.

## Telas e estados
- **Fechar caixa** (`/pdv/caixa/fechar`): contagem por forma, teclado grande, sem totais esperados.
- **Loja → Caixas** (`/app/loja/caixas`): lista de sessões com filtro por loja, caixa, operador e estado; chip "Em conferência". Vazio: "Nenhum caixa fechado no período.".
- **Sessão de caixa** (detalhe): o quadro do CA 7.
- Carregando e erro em todas.
- **Ajuda:** `Ajuda/pdv-fechar-caixa.md` e `Ajuda/loja-caixas.md` (novas). `<Dica>` em "Conferência cega" e "Quebra de caixa".

## Plano de testes
- **Unidade:** cálculo do esperado por forma (vendas, troco, sangria, suprimento, cancelamento); tolerância por forma, com sobra numa e falta em outra (CA 5); recolhimento = contado − fundo que fica (CA 12).
- **Integração (Testcontainers):** dentro e acima da tolerância (CA 5 e 6); o operador não aprova o próprio caixa (CA 8); reabrir só a última (CA 10); o esperado não sai para quem não tem `sales.approve` (CA 3).
- **Vitest:** tela de contagem sem valores esperados.

## Fora do escopo
- Lançamentos no Financeiro (CX-03).
- Fechar sem rede (OFF-03).

## Pontos em aberto
- PP-22: valor padrão da tolerância (sugestão: R$ 5,00).
- Extrato do banco integrado para sugerir o casamento do Pix: hoje só o Inter (ADR 0032); com outro banco, a conferência do CA 13 é manual.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade e de integração (Testcontainers) passando; Vitest no front.
- [ ] Migration criada; permissões só do `ModuleCatalog`.
- [ ] Telas com estados de carregando, vazio e erro; ajuda escrita ou atualizada em `Ajuda/` no mesmo commit.
- [ ] Padrões e próximo passo propositivos avaliados; auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados; story marcada no backlog; decisões em ADR.

## Implementação (19/11/2026)

Branch `feat/cx-02-fechamento`, sobre a OFF-02 (o fechamento sobe do aparelho pela subida de eventos). Local-first como a CX-01: o aparelho grava a contagem e põe o evento na fila; o servidor recalcula tudo.

| CA | Situação | Como |
|---|---|---|
| 1 | Feito | "Fechar caixa" (a folha da PDV-02 já lista as vendas na tela ou em espera para retomar ou descartar) grava `counting` no turno e o evento `CashCountStarted`; no servidor a sessão vai a `Counting` (`CashClosingService.StartCountAsync`). Depois disso, venda e movimento da sessão vão para a quarentena |
| 2 | Feito | Tela de contagem no `/pdv`: dinheiro (teclado grande ou contagem por cédula e moeda) e o total dos comprovantes de cada forma; nada de esperado, diferença ou total de vendas |
| 3 | Feito | O aparelho apaga o dinheiro calculado (`cashSalesCents`) no "Fechar caixa" e nunca recebe esperado (nem no aperto de mão). Na API, o esperado, a diferença, o total das vendas, a marca da sangria acima do calculado e a lista de Pix só saem para `sales.approve` (ou Proprietário), conferido no serviço pelo usuário do request, em todas as rotas (lista, detalhe, CSV e PDF) |
| 4 | Parcial | "Confirmar contagem" (com confirmação) gera `CashCounted` com o id da contagem; a sessão vai a `Counted` e a mesma contagem reenviada não muda nada. Outra contagem da sessão vai para a quarentena ("Recontar exige o supervisor"). **Recontar com o supervisor no caixa fica para a PDV-04** (PIN do supervisor); hoje o caminho é reabrir na retaguarda (CA 10) |
| 5 | Feito | Com todas as vendas da sessão no servidor (o aparelho informa quantas concluiu), o servidor calcula o esperado por forma pelo `ICashSessionSales` (novo contrato do `Sales`), fundo e movimentos, e aplica a tolerância da loja **em cada forma** (`CashClosing.WithinTolerance`); dentro: `Closed` e `CashSessionClosed` no outbox |
| 6 | Feito | Acima da tolerância em alguma forma, ou com sangria acima do calculado (refeita no servidor): `PendingReview`, aviso a quem tem `sales.approve` (menos quem operou). O operador vê só "Fechamento enviado para conferência." (sempre, porque o aparelho não sabe o resultado). Aprovações sem rede (OFF-03) ficam reservadas no motivo |
| 7 | Feito | Loja → Caixas (`/app/loja/caixas`): esperado, contado e diferença por forma, vendas, canceladas, descontos, sem rede, fundo, sangrias, suprimentos e recolhimento; "Aprovar fechamento" com motivo leva a `Closed` (aceita também `Counted` esperando vendas que não vão chegar) |
| 8 | Feito | Quem operou (ou contou) a sessão recebe 403 `own_cash_session` "Quem operou o caixa não pode aprovar o próprio fechamento." |
| 9 | Feito | PDF no modelo único (`CashSessionPdfDocument`): via do operador sem esperado, diferença nem total de vendas; via do gerente (`?copy=manager`, só `sales.approve`) com o quadro e a quebra. Bobina no aparelho (`buildClosingReceipt`): a via do operador e o comprovante do recolhimento, impressos pela porta da IMP-01 |
| 10 | Feito | "Reabrir caixa" com motivo: só a última sessão do caixa, só `Closed`, nunca por quem operou (403); volta a `Open`, o recolhimento volta à gaveta (movimento `DepositReturn`, Cofre → Caixa), `CashSessionReopened` no outbox (a CX-03 estorna), aviso ao Proprietário, auditado. O aparelho retoma a sessão reaberta no próximo aperto de mão (`reopenedCashSession`), uma vez por reabertura |
| 11 | Feito | Depois de "Concluir", a tela de abertura diz "Abra o caixa para começar a vender." |
| 12 | Feito | O operador informa o fundo que fica (padrão: o fundo da abertura, que passa a ser o padrão da próxima); o recolhimento (contado − fundo que fica) vira o movimento `Deposit` e a transferência Caixa do PDV → Cofre pelo consumidor da CX-01 (`AddPosCashTransfer`), com o comprovante em duas vias |
| 13 | Feito | Lista dos Pix manuais da sessão (hora, valor, operador, venda) com "Conferido" e "Não localizado" (`pos.cash_pix_checks`, auditado); o não localizado vira quebra da forma Pix (recalculada). Sem extrato integrado, a conferência é manual (sugestão de casamento pelo extrato do Inter fica para depois) |

**Decisões e desvios:**
- **Evento `CashDeposited`:** o recolhimento vai dentro do `CashCounted` (fundo que fica e id do movimento), num evento só: contagem e recolhimento nunca ficam um sem o outro, e o valor é recalculado no servidor.
- **Recolhimento no Financeiro já na CX-02** (a CX-03 CA 1 previa no `CashSessionClosed`): reusa o caminho da CX-01; a CX-03 fica com os resumos por forma, a quebra e o estorno na reabertura.
- **Fundo que fica e a próxima abertura:** o fundo que fica já está na conta do caixa; a abertura seguinte (`CashSessionIngestion.OpenAsync`, acréscimo mínimo) transfere do Cofre só a diferença para o fundo deixado, e a sobra volta ao Cofre. Sem isso, a conta Caixa do PDV cresceria o fundo a cada turno.
- **Padrão do fundo que fica:** o fundo da abertura da sessão (a loja não tem parâmetro de fundo padrão).
- **Vendas sem rede:** a venda que chegou ao servidor mais de 5 min depois de concluída no aparelho.
- **Tolerância por loja:** já existia no banco (OFF-01, padrão R$ 5,00, PP-22); agora é editável em Loja → Lojas e caixas.
- **Rotas no aparelho:** a contagem é uma tela dentro de `/pdv` (escopo do service worker), não `/pdv/caixa/fechar`.

**Testes:** unidade `CashClosingUnitTests` (esperado por forma com fundo, suprimento, sangria e recolhimento; tolerância por forma com sobra e falta que se anulam; recolhimento). Integração `PosTests.Closing.cs`: pela subida do aparelho, dentro da tolerância fecha (com o `CashSessionClosed` e a transferência do recolhimento), venda depois do "Fechar caixa" vai para a quarentena, acima da tolerância vai para conferência com aviso, recontagem vai para a quarentena; na retaguarda, o esperado só para `sales.approve` (detalhe e via do gerente), o operador não aprova nem reabre, só a última sessão reabre, reabertura devolve o recolhimento, avisa o Proprietário e publica `CashSessionReopened`. Vitest: `cash-session.test.ts` (contagem uma vez, dinheiro calculado apagado, evento sem esperado, retomada da reabertura) e `CashCountScreen.test.tsx` (tela sem esperado, recolhimento, "Fechamento enviado para conferência.", "Abra o caixa para começar a vender.").

Ajuda: `Ajuda/pdv-fechar-caixa.md` (nova, embutida no `/pdv`), `Ajuda/loja-caixas.md` (nova), `Ajuda/pdv-caixa.md` e `Ajuda/loja-lojas-caixas.md` atualizadas. `<Dica>` em "Conferência cega", "Quebra de caixa", "Fundo que fica" e "Tolerância de quebra por forma".

Migration `20261120024415_PosCashClosingCx02`: colunas novas em `pos.cash_sessions` e as tabelas `pos.cash_counts` e `pos.cash_pix_checks`, só acréscimo.

**Revisão de segurança (PR #238, 23/11/2026):**
- **A1:** o `CashSessionClosed` da CX-01 vindo do aparelho (fechar sem contagem, sem quebra e sem fundo que fica) vai para a quarentena com "use Fechar caixa (contagem)"; o `CashSessionIngestion.CloseAsync` saiu. O caixa só fecha pelo "Fechar caixa".
- **M1:** depois de uma reabertura, o número de vendas do aparelho (que recomeça do zero ao retomar a sessão) é comparado só com as vendas que chegaram depois da reabertura. Limite: venda contada antes da reabertura que só chega depois dela (aprovada como "contado esperando vendas") conta como nova.
- **M2:** conferir o Pix: 403 para quem operou ou contou o caixa e para quem atestou aquele Pix; no caixa `Closed`, 422 "Reabra o caixa para conferir o Pix." (a quebra já foi publicada; reabrir faz a CX-03 estornar e lançar de novo).
- **M3:** sem `sales.approve`, `PendingReview` e `Closed` aparecem iguais ("Enviado"); o filtro "em conferência" e o total dele não aparecem, nem quem aprovou.
- **B2:** Aprovar, Reabrir e Conferir Pix devolvem 409 no conflito de concorrência; a conferência do Pix grava junto com a sessão (a versão da sessão barra a corrida com Aprovar e Reabrir). Sem versão própria em `cash_counts` (exigiria migration; a da sessão cobre os três caminhos).
- **B3:** a exportação CSV da lista fica na auditoria (com os filtros), como o PDF.
- **B1 (limite do modo offline):** aceito, sem mudança de código.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 15/11/2026 | 0.1 | Story escrita como parte do plano do PDV (proposta). Aguarda a aprovação do plano e as decisões citadas em "Pontos em aberto" para sair de `Draft`. | Claude (planejamento) |
| 15/11/2026 | 0.2 | Revista depois da revisão independente e das decisões do PO do mesmo dia ([o que mudou](../README.md#7-revisão-independente-de-15112026-achados-e-o-que-foi-aplicado)). | Claude (planejamento) |
| 19/11/2026 | 1.0 | Implementada (branch `feat/cx-02-fechamento`): contagem cega no aparelho com recolhimento ao Cofre, quebra recalculada no servidor por forma com a tolerância da loja, `PendingReview` com aviso, Loja → Caixas com aprovação, reabertura da última sessão, conferência do Pix e relatório em PDF e em bobina. | Claude (dev) |
