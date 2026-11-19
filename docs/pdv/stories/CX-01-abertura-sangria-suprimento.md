# CX-01 — Abertura de caixa com fundo de troco, sangria e suprimento com motivo e comprovante, alerta e teto de dinheiro em caixa

| Campo | Valor |
|---|---|
| ID | CX-01 |
| Título | Abertura de caixa com fundo de troco, sangria e suprimento com motivo e comprovante, alerta e teto de dinheiro em caixa |
| Tamanho | M |
| Status | Ready for Review (19/11/2026) |
| Fase | 11 — PDV balcão com cupom não fiscal e offline (marco MVP) |
| Requisitos | Pedido do PO de 15/11/2026 (item 4) · [Fluxos](../04-fluxos-estados.md#2-caixa-e-turno) · [PRD do PDV](../01-PRD.md) seção 6.5 |
| Dependências | PDV-01, PDV-02; PP-22 (alerta e teto) |
| Bloqueia | CX-02, PDV-04 |
| Paridade com o mercado | Requisitos mínimos 5 do [benchmark](../00-benchmark-mercado.md#7-matriz-de-paridade-cada-requisito-mínimo-e-a-story-que-o-cobre) |
| Ordem de execução | 5 (Fase 11) |

## User story
Como **operador de caixa**, quero **abrir o caixa com o troco contado e registrar toda retirada e reforço**, para **o dinheiro da gaveta bater no fim do turno**.

## Contexto e referências
- `finance.bank_accounts.type` já tem `Cash`, e existem `bank_transfers`. O caixa do PDV é uma conta do tipo Caixa; sangria e suprimento são transferências.
- Nesta story a abertura exige rede; sem rede vem na OFF-03.

## Critérios de aceite
1. **Dado** um caixa sem sessão aberta e `pos.create`, **quando** aciono "Abrir caixa", **então** informo o fundo de troco (padrão: o da última abertura), confirmo com "Abrir caixa" e a sessão nasce `Open`, com o meu usuário como operador.
2. **Dado** um caixa com sessão `Open`, **quando** outro operador tenta abrir, **então** recebe "Este caixa já está aberto por Fulano desde 08:02.".
3. **Dado** nenhuma sessão aberta, **quando** tento lançar item, **então** a venda não começa (PDV-02 CA 17).
4. **Dado** `pos.cash_out`, **quando** registro uma sangria, **então** informo valor e motivo; a gaveta abre e sai o comprovante em duas vias, com linha para assinatura de quem retira. **A tela não recusa o valor por ser maior que o dinheiro calculado**, porque a recusa revelaria o saldo e furaria a conferência cega: sangria acima do calculado é aceita, fica marcada e leva o fechamento a `PendingReview`.
5. **Dado** `pos.cash_in`, **quando** registro um suprimento, **então** informo valor e motivo e sai o comprovante.
6. **Dado** um operador sem `pos.cash_out` ou `pos.cash_in`, **quando** tenta, **então** vê "Chamar supervisor" (PDV-04); até a PDV-04, recebe "Você não tem permissão para esta ação.".
7. **Dado** o dinheiro em caixa acima do limite de alerta da loja, **quando** concluo uma venda em dinheiro, **então** vejo o aviso "Dinheiro em caixa acima de R$ X. Faça uma sangria." com o botão "Fazer sangria".
8. **Dado** o dinheiro em caixa acima do teto da loja, **quando** tento receber mais dinheiro, **então** a forma Dinheiro fica bloqueada até a sangria; as outras formas continuam.
9. **Dado** a abertura, a sangria e o suprimento sincronizados, **quando** o Financeiro processa, **então** há transferência Cofre → Caixa do PDV (abertura), Caixa do PDV → Cofre (sangria) e Cofre → Caixa do PDV (suprimento), cada uma ligada ao movimento; reprocessar não duplica.
10. **Dado** o painel do caixa, **quando** abro, **então** vejo operador, hora da abertura, número de vendas e as sangrias e suprimentos; **não** vejo o total esperado em dinheiro (conferência cega, CX-02).

## Regras de negócio e técnicas
- Uma sessão `Open` por caixa.
- Fundo de troco zero é permitido, com confirmação.
- O "dinheiro em caixa calculado" é usado só para o alerta e o teto, que são limites fixos da loja e não revelam o saldo; nunca é mostrado ao operador nem usado para recusar uma sangria.
- Limite de alerta e teto são parâmetros da loja (PP-22).

## Permissões, eventos e tabelas
- **Permissões:** `pos.create`, `pos.cash_out`, `pos.cash_in`.
- **Eventos do aparelho:** `CashSessionOpened`, `CashOutRecorded`, `CashInRecorded`. **Evento de domínio:** `CashMovementRecorded`.
- **Tabelas:** `pos.cash_sessions`, `pos.cash_movements`; `finance.bank_transfers` (pelo contrato).
- **Auditoria:** abertura, sangria e suprimento.

## Telas e estados
- **Abrir caixa** (`/pdv/caixa/abrir`): campo do fundo de troco, com teclado numérico grande.
- **Caixa** (`/pdv/caixa`): painel do CA 10, com "Sangria", "Suprimento" e "Fechar caixa". Vazio: "Nenhuma movimentação neste turno.".
- Erro: "Não foi possível registrar. O movimento ficou salvo neste aparelho e será enviado." quando a rede falha depois da gravação local.
- **Ajuda:** `Ajuda/pdv-caixa.md` (nova). `<Dica>` em "Sangria" e "Suprimento".

## Plano de testes
- **Vitest:** validação da sangria; alerta e teto (CA 7 e 8).
- **Integração (Testcontainers):** uma sessão por caixa (CA 2); transferências no Financeiro, idempotentes (CA 9).

## Fora do escopo
- Fechamento (CX-02). Abrir sem rede (OFF-03).
- Abrir a gaveta sem venda (`pos.open_drawer`, IMP-02).

## Pontos em aberto
- PP-22: valores padrão do alerta e do teto.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade e de integração (Testcontainers) passando; Vitest no front.
- [ ] Migration criada; permissões só do `ModuleCatalog`.
- [ ] Telas com estados de carregando, vazio e erro; ajuda escrita ou atualizada em `Ajuda/` no mesmo commit.
- [ ] Padrões e próximo passo propositivos avaliados; auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados; story marcada no backlog; decisões em ADR.

## Implementação (19/11/2026)

Branch `feat/cx-01-abertura-sangria`. **Local-first** (decisão do PO para esta story, no lugar de "a abertura exige rede"): abrir, sangrar, suprir e fechar gravam no aparelho e põem o evento na fila de saída na mesma transação (`LocalStore.updateMeta`); a subida é da OFF-02, que chama a gravação no servidor (`CashSessionIngestion`). Nada aqui espera a rede.

| CA | Situação | Como |
|---|---|---|
| 1 | Feito | Sem turno, a tela "Abrir caixa" com teclado numérico grande (`MoneyPad`, os números entram pelos centavos); o fundo padrão é o da última abertura (`cash.lastOpening`); fundo zero pede confirmação. Grava a sessão `Open` com o operador e o evento `CashSessionOpened` (com o movimento do fundo). No servidor, `CashSessionIngestion.OpenAsync` grava `pos.cash_sessions` e o movimento `Opening` em `pos.cash_movements` |
| 2 | Feito | No aparelho: outro operador vê "Este caixa já está aberto por Fulano desde 08:02." e só pode sair. No servidor: índice único parcial `ix_cash_sessions_register_open` (`status = 'Open'`) e a mesma mensagem, com o nome do `IUserDirectory` e a hora de Brasília; reenviar a mesma abertura devolve a sessão sem duplicar |
| 3 | Feito | Sem sessão aberta a tela de venda não aparece (PDV-02 CA 17); o marcador provisório da PDV-02 (sem id) não vale como turno |
| 4 | Parcial | Sangria com valor e motivo (1 a 200 caracteres), o operador e o comprovante em duas vias com "Assinatura de quem retira" (imprime pelo navegador). **Nada compara o valor com o dinheiro calculado**: acima dele, a sangria é aceita e vai marcada (`aboveExpected`), sem mensagem. A gaveta abrir sozinha e a impressão direta são da IMP-01/IMP-02; o `PendingReview` do fechamento é da CX-02 |
| 5 | Feito | Suprimento com valor, motivo e comprovante (via única) |
| 6 | Feito | Sem `pos.cash_out` ou `pos.cash_in` (as ações vêm da sessão por PIN): "Você não tem permissão para esta ação."; o "Chamar supervisor" é da PDV-04 |
| 7 | Feito | Depois de uma venda com dinheiro, acima do aviso da loja (`cashDropAlertCents`, PP-22): "Dinheiro em caixa acima de R$ X. Faça uma sangria." com "Fazer sangria" (abre o painel já na sangria). X é o limite, nunca o saldo |
| 8 | Feito | Teto novo e opcional da loja (`pos.stores.cash_ceiling_amount`, nulo = sem teto, desce como `cashCeilingCents`): acima dele, a forma Dinheiro fica desabilitada no pagamento com "Faça uma sangria."; as outras continuam |
| 9 | Feito | Cada movimento publica `CashMovementRecorded` (outbox); o `PosCashMovementConsumer` do Financeiro grava a transferência Cofre → Caixa do PDV (abertura e suprimento) ou Caixa do PDV → Cofre (sangria), com as duas transações, ligada ao movimento por `finance.bank_transfers.pos_cash_movement_id` (único): reprocessar não duplica. Fundo zero não transfere. O Cofre é a conta `seed_key = cofre` (PERF-04) ou a conta Caixa ativa "Cofre"; sem ela, o consumidor cria "Cofre" |
| 10 | Feito | "Caixa" na tela de venda abre o painel: operador, hora da abertura, número de vendas e as sangrias e suprimentos (vazio: "Nenhuma movimentação neste turno."), com "Sangria", "Suprimento" e "Fechar caixa". **Nenhum total esperado** |

**Decisões e desvios:**
- **Rotas:** o painel e a abertura são telas dentro de `/pdv/` (o app do caixa tem uma rota só, por causa do escopo do service worker), não `/pdv/caixa` e `/pdv/caixa/abrir`.
- **Fechar caixa:** provisório até a CX-02: encerra o turno no aparelho com o evento `CashSessionClosed` (o servidor só tira a sessão de `Open`, `CloseAsync`), para o caixa poder abrir de novo. Fechar aplica a versão nova do app que estava esperando (`hasOpenCashSession` agora lê o turno; ponto de extensão da OFF-01).
- **Dinheiro calculado:** fica só no aparelho (`cashSalesCents` e os movimentos), para o aviso e o teto; não vai para a tela nem para o servidor como saldo. A venda concluída conta no turno depois de fechar (fora da transação da venda: uma queda nesse meio deixa o contador uma venda atrás, o que só afeta o aviso; a OFF-02 e a CX-02 recalculam pelo servidor). A venda concluída leva `cashSessionId` na fila.
- **Teto (PP-22):** a PP-22 decidiu o aviso (R$ 1.000,00) e não fixou teto; o teto nasce desligado e é editável por loja, junto com o aviso, em Loja → Lojas e caixas (backlog da OFF-01: "edição dos parâmetros da loja na CX-01"). A tolerância de quebra e o prazo de troca continuam sem tela (CX-02 e PDV-06).
- **Mensagem "Não foi possível registrar. O movimento ficou salvo neste aparelho e será enviado.":** sem subida nesta story, não há falha de rede depois da gravação local; a constante fica pronta (`SAVED_LOCALLY`) para a OFF-02.
- **Auditoria:** `CashSession` e `CashMovement` são `IAuditable` (módulo `sales`, como lojas e caixas).
- **Konebras (perfil fábrica):** nada novo aparece; tudo está no app do caixa (módulo `pos`) e na tela de lojas (módulo `sales`, desligado na fábrica). Banco só por acréscimo.
- **Migration** `20261119024155_PosCashSessionsCx01`: gerada pelo `dotnet ef` num contêiner (fontes copiadas com `docker cp`, sem montar o D:): `pos.cash_sessions`, `pos.cash_movements`, `pos.stores.cash_ceiling_amount` e `finance.bank_transfers.pos_cash_movement_id`, só acréscimo.
- O que o servidor deve validar quando a sessão subir foi registrado na [OFF-02, "Requisitos vindos da CX-01"](OFF-02-sincronizacao-pendencias.md#requisitos-vindos-da-cx-01-sessão-de-caixa).

**Testes:** Vitest `src/pdv/cash/cash-session.test.ts` (abertura com o evento na mesma transação e o fundo padrão, outro operador, validação da sangria sem revelar o saldo e a marca acima do calculado, aviso e teto, fechamento) e `src/pages/pdv/cash/CashArea.test.tsx` (painel sem o esperado, sem permissão, sangria com comprovante em duas vias, outro operador, Dinheiro bloqueado no teto com o Pix liberado); `PdvSale.test.tsx` e `PaymentScreen.test.tsx` ajustados à abertura nova. Integração (Testcontainers) `PosTests.Cash.cs`: uma sessão por caixa com a mensagem do CA 2, reenvio e fechamento idempotentes, auditoria; abertura, sangria e suprimento viram três transferências ligadas ao movimento com o Cofre, reenvio e reprocessamento sem duplicar, validações; fundo zero sem transferência. **A conferir no CI:** os testes .NET não rodaram nesta máquina (o Docker não monta o disco D:); a entrada do `openapi.json` foi escrita no formato do gerador e o cliente Orval regenerado dela. E2E do turno inteiro: no fechamento do marco (plano de testes).

Ajuda: `Ajuda/pdv-caixa.md` (nova), embutida na ajuda do app do caixa; `Ajuda/loja-lojas-caixas.md` atualizada (aviso e teto da loja).

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 15/11/2026 | 0.1 | Story escrita como parte do plano do PDV (proposta). Aguarda a aprovação do plano e as decisões citadas em "Pontos em aberto" para sair de `Draft`. | Claude (planejamento) |
| 15/11/2026 | 0.2 | Revista depois da revisão independente e das decisões do PO do mesmo dia ([o que mudou](../README.md#7-revisão-independente-de-15112026-achados-e-o-que-foi-aplicado)). | Claude (planejamento) |
| 19/11/2026 | 1.0 | Implementada (branch `feat/cx-01-abertura-sangria`), local-first: sessão de caixa e movimentos no aparelho com o evento na fila; `pos.cash_sessions` e `pos.cash_movements` com uma sessão aberta por caixa; transferências Cofre ↔ Caixa do PDV no Financeiro, ligadas ao movimento; aviso de sangria e teto de dinheiro por loja (teto novo, opcional); painel sem o esperado; requisitos da subida registrados na OFF-02. | Claude (dev) |
