# CX-02 — Fechamento de caixa com conferência cega, tolerância de quebra por forma de pagamento, recolhimento do dinheiro ao Cofre, conferência do Pix manual contra o extrato, revisão por quem tem `sales.approve`, relatório em bobina e em PDF e reabertura da última sessão

| Campo | Valor |
|---|---|
| ID | CX-02 |
| Título | Fechamento de caixa com conferência cega, tolerância de quebra por forma de pagamento, recolhimento do dinheiro ao Cofre, conferência do Pix manual contra o extrato, revisão por quem tem `sales.approve`, relatório em bobina e em PDF e reabertura da última sessão |
| Tamanho | G |
| Status | Draft (plano proposto em 15/11/2026; aguarda a aprovação do PO) |
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

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 15/11/2026 | 0.1 | Story escrita como parte do plano do PDV (proposta). Aguarda a aprovação do plano e as decisões citadas em "Pontos em aberto" para sair de `Draft`. | Claude (planejamento) |
| 15/11/2026 | 0.2 | Revista depois da revisão independente e das decisões do PO do mesmo dia ([o que mudou](../README.md#7-revisão-independente-de-15112026-achados-e-o-que-foi-aplicado)). | Claude (planejamento) |
