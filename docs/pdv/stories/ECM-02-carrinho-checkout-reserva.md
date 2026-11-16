# ECM-02 — Carrinho, checkout e reserva no estoque único: reserva com validade no mesmo estoque da loja física, frete por retirada ou faixa de CEP, dados mínimos do comprador e consentimento

| Campo | Valor |
|---|---|
| ID | ECM-02 |
| Título | Carrinho, checkout e reserva no estoque único: reserva com validade no mesmo estoque da loja física, frete por retirada ou faixa de CEP, dados mínimos do comprador e consentimento |
| Tamanho | G |
| Status | Draft (plano proposto em 15/11/2026; aguarda a aprovação do PO) |
| Fase | 14 — E-commerce (trilha seguinte à do balcão) |
| Requisitos | Pedido do PO de 15/11/2026 (item 1) · [Modelo de dados](../03-modelo-dados.md#11-storefront--schema-storefront-fase-14) · ADR 0018 e 0037 (LGPD) · PP-26 |
| Dependências | ECM-01, VND-01; PP-26 (finalidade nova para o dado do comprador) |
| Bloqueia | ECM-03 |
| Paridade com o mercado | Requisitos mínimos 35 do [benchmark](../00-benchmark-mercado.md#7-matriz-de-paridade-cada-requisito-mínimo-e-a-story-que-o-cobre) |
| Ordem de execução | 2 (Fase 14, trilha do e-commerce) |

## User story
Como **comprador**, quero **pôr os produtos no carrinho e fechar o pedido sabendo o frete**, e como **dono**, quero **que o que foi vendido online não seja vendido de novo no balcão**.

## Contexto e referências
- O Estoque já tem `reservations`. A reserva do carrinho usa o mesmo mecanismo, com validade.
- A venda só nasce quando o pedido é pago (ECM-03), pelo `ISaleIngestion` da VND-01.

## Critérios de aceite
1. **Dado** a loja, **quando** adiciono um produto ao carrinho, **então** ele guarda produto ou variação, quantidade e o preço daquele momento; o carrinho sobrevive a fechar o navegador.
2. **Dado** o carrinho, **quando** inicio o checkout, **então** o sistema reserva as quantidades no local de estoque da loja, com validade configurável (padrão: 30 minutos), e a disponibilidade no balcão e na vitrine passa a descontar a reserva.
3. **Dado** um item sem saldo disponível na hora da reserva, **quando** inicio o checkout, **então** vejo "Só há N unidades disponíveis." e posso ajustar; nada é reservado pela metade sem o comprador ver.
4. **Dado** uma reserva vencida sem pagamento, **quando** o prazo passa, **então** ela é liberada sozinha e o carrinho avisa na próxima visita.
5. **Dado** o preço alterado entre o carrinho e o checkout, **quando** avanço, **então** vejo o preço novo e confirmo antes de pagar.
6. **Dado** o checkout, **quando** informo o CEP, **então** vejo as opções de entrega da loja: retirada na loja (sem custo) e entrega própria por faixa de CEP, com valor e prazo; CEP fora das faixas mostra só a retirada.
7. **[Segurança]** **Dado** o checkout, **quando** preencho os dados, **então** só são pedidos os necessários ao pedido e à nota (nome, CPF ou CNPJ, e-mail, telefone, endereço), com o aviso de para que servem e o consentimento registrado; marketing é consentimento à parte, desmarcado por padrão.
8. **Dado** um comprador sem conta, **quando** fecho o pedido, **então** consigo comprar como convidado; criar conta é opcional.
9. **Dado** dois compradores disputando a última unidade, **quando** os dois iniciam o checkout ao mesmo tempo, **então** só um consegue a reserva (teste de concorrência).
10. **Dado** o pedido pronto para pagar, **quando** confirmo, **então** nasce um pedido online aguardando pagamento, com itens, frete, total e a reserva ligada.

## Regras de negócio e técnicas
- Estoque único: o mesmo saldo serve ao balcão e à loja virtual. A baixa de verdade é a da venda (VND-01), que consome a reserva.
- O PDV não é barrado por reserva online quando vende a mercadoria que está na prateleira; a falta que resultar disso cai na regra da PP-02.
- Dinheiro `decimal(18,2)`; quantidade `decimal(18,4)`.

## Permissões, eventos e tabelas
- **Permissões:** nenhuma nova no backoffice; rotas do comprador são da identidade da loja.
- **Endpoints:** `/api/v1/store/cart`, `/store/checkout`, `/store/shipping-options`.
- **Tabelas:** `storefront.carts`, `cart_lines`, `online_orders`, `shipping_methods`; reservas pelo contrato do Estoque.
- **Auditoria:** consentimento do comprador.

## Telas e estados
- **Carrinho** e **Checkout** (públicas). Vazio: "Seu carrinho está vazio." com "Ver produtos". Carregando e erro; aviso de reserva vencida.
- **Loja → Loja virtual → Entrega** (`/app/loja/loja-virtual/entrega`): formas e faixas de CEP.
- **Ajuda:** `Ajuda/loja-loja-virtual-entrega.md` (nova).

## Plano de testes
- **Integração (Testcontainers), obrigatória por reservar estoque:** reserva e liberação por validade (CA 2 e 4); concorrência na última unidade (CA 9); preço alterado (CA 5).
- **Unidade:** frete por faixa de CEP.
- **Vitest:** checkout como convidado; consentimentos.

## Fora do escopo
- Pagamento (ECM-03). Transportadora e etiqueta (ECM-04).
- Cupom de desconto (FID-01).

## Pontos em aberto
- PP-26: base legal e retenção dos dados do comprador.
- Prioridade entre reserva online e venda no balcão: a proposta é o balcão nunca ser barrado.

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
