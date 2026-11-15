# CX-01 — Abertura de caixa com fundo de troco, sangria e suprimento com motivo e comprovante, alerta e teto de dinheiro em caixa

| Campo | Valor |
|---|---|
| ID | CX-01 |
| Título | Abertura de caixa com fundo de troco, sangria e suprimento com motivo e comprovante, alerta e teto de dinheiro em caixa |
| Tamanho | M |
| Status | Draft (plano proposto em 15/11/2026; aguarda a aprovação do PO) |
| Fase | 11 — PDV balcão com cupom não fiscal e offline (marco MVP) |
| Requisitos | Pedido do PO de 15/11/2026 (item 4) · [Fluxos](../04-fluxos-estados.md#2-caixa-e-turno) · [PRD do PDV](../01-PRD.md) seção 6.5 |
| Dependências | PDV-01, PDV-02 |
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
4. **Dado** `pos.cash_out`, **quando** registro uma sangria, **então** informo valor e motivo; o valor não pode passar do dinheiro em caixa calculado; a gaveta abre e sai o comprovante em duas vias, com linha para assinatura de quem retira.
5. **Dado** `pos.cash_in`, **quando** registro um suprimento, **então** informo valor e motivo e sai o comprovante.
6. **Dado** um operador sem `pos.cash_out` ou `pos.cash_in`, **quando** tenta, **então** vê "Chamar supervisor" (PDV-04); até a PDV-04, recebe "Você não tem permissão para esta ação.".
7. **Dado** o dinheiro em caixa acima do limite de alerta da loja, **quando** concluo uma venda em dinheiro, **então** vejo o aviso "Dinheiro em caixa acima de R$ X. Faça uma sangria." com o botão "Fazer sangria".
8. **Dado** o dinheiro em caixa acima do teto da loja, **quando** tento receber mais dinheiro, **então** a forma Dinheiro fica bloqueada até a sangria; as outras formas continuam.
9. **Dado** a abertura, a sangria e o suprimento sincronizados, **quando** o Financeiro processa, **então** há transferência Cofre → Caixa do PDV (abertura), Caixa do PDV → Cofre (sangria) e Cofre → Caixa do PDV (suprimento), cada uma ligada ao movimento; reprocessar não duplica.
10. **Dado** o painel do caixa, **quando** abro, **então** vejo operador, hora da abertura, número de vendas e as sangrias e suprimentos; **não** vejo o total esperado em dinheiro (conferência cega, CX-02).

## Regras de negócio e técnicas
- Uma sessão `Open` por caixa.
- Fundo de troco zero é permitido, com confirmação.
- O "dinheiro em caixa calculado" é usado só para o alerta, o teto e a validação da sangria; nunca é mostrado ao operador.
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

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 15/11/2026 | 0.1 | Story escrita como parte do plano do PDV (proposta). Aguarda a aprovação do plano e as decisões citadas em "Pontos em aberto" para sair de `Draft`. | Claude (planejamento) |
