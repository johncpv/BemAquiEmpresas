# ECM-03 — Pagamento online: Pix dinâmico com webhook e reconsulta e cartão por gateway com campos hospedados, o pedido pago virando venda de canal Online sem sessão de caixa

| Campo | Valor |
|---|---|
| ID | ECM-03 |
| Título | Pagamento online: Pix dinâmico com webhook e reconsulta e cartão por gateway com campos hospedados, o pedido pago virando venda de canal Online sem sessão de caixa |
| Tamanho | G |
| Status | Draft (plano proposto em 15/11/2026; aguarda a aprovação do PO) |
| Fase | 14 — E-commerce (trilha seguinte à do balcão) |
| Requisitos | Pedido do PO de 15/11/2026 (itens 1 e 6) · [Rascunho C](../adr/rascunho-C-pagamentos.md) · ADR 0031 e 0032 (webhook e reconsulta) · [PRD do PDV](../01-PRD.md) regra 14 (dados de cartão) |
| Dependências | ECM-02, VND-01, PAG-01, PAG-02 (porta `IPixChargeProvider`); PP-21 (gateway de cartão) decidida; adendo à ADR 0032 |
| Bloqueia | ECM-04 |
| Ordem de execução | 3 (Fase 14, trilha do e-commerce) |

## User story
Como **comprador**, quero **pagar por Pix ou cartão e saber na hora que deu certo**, e como **dono**, quero **que o pedido pago entre no estoque e no financeiro sem eu fazer nada**.

## Contexto e referências
- "O aviso nunca é verdade": o webhook só dispara a reconsulta; quem confirma é a consulta ao provedor (ADR 0031).
- O sistema nunca guarda número de cartão, código de segurança nem trilha. Na loja virtual isso se resolve com campos hospedados ou token do gateway: o número do cartão vai do navegador do comprador direto ao gateway.

## Critérios de aceite
### Pix
1. **Dado** um pedido aguardando pagamento, **quando** escolho Pix, **então** o sistema cria a cobrança pelo `IPixChargeProvider`, mostra o QR Code e o código para copiar, com o prazo de validade.
2. **Dado** o webhook do provedor, **quando** chega, **então** o sistema valida a assinatura, **reconsulta** a cobrança e só com a resposta "paga" marca o pagamento como `Captured`; webhook sem assinatura válida é recusado e registrado.
3. **Dado** o webhook que não chegou, **quando** o comprador fica na tela ou o job de reconsulta roda, **então** o pagamento é confirmado pela consulta periódica.
4. **Dado** a cobrança vencida sem pagamento, **quando** o prazo passa, **então** o pedido é cancelado e a reserva liberada.

### Cartão
5. **[Segurança]** **Dado** o checkout com cartão, **quando** o comprador digita os dados, **então** eles são digitados em campos hospedados pelo gateway (ou viram token no navegador); nenhuma requisição ao BemAqui leva número de cartão, validade ou código de segurança. Um teste confere que o contrato da API não tem esses campos.
6. **Dado** o pagamento autorizado e capturado no gateway, **quando** a confirmação chega (retorno + webhook com reconsulta), **então** o pagamento fica `Captured`, com bandeira, parcelas, NSU, código de autorização e a referência do gateway.
7. **Dado** o pagamento recusado, **quando** o gateway responde, **então** o comprador vê o motivo em linguagem simples e pode tentar outra forma; a reserva continua até vencer.
8. **Dado** uma resposta que não chegou (queda no meio), **quando** o estado é desconhecido, **então** o pagamento fica `Unknown`, o pedido não é cancelado nem confirmado, e a reconsulta resolve; se não resolver no prazo, vai para conferência humana.

### Venda e financeiro
9. **Dado** o pagamento `Captured`, **quando** o pedido é confirmado, **então** o `Storefront` entrega a venda ao `Sales` pelo `ISaleIngestion`, com canal `Online`, **sem sessão de caixa**; o pedido passa a "Pago", a baixa de estoque consome a reserva e o comprador recebe o e-mail de confirmação.
10. **Dado** a mesma confirmação recebida duas vezes, **quando** processo, **então** há uma venda só.
11. **Dado** as vendas online do dia, **quando** o Financeiro processa, **então** nasce o recebível por dia, canal, provedor, forma e vencimento, com a taxa e o prazo cadastrados na PAG-01 para o gateway (VND-01 CA 11).
12. **Dado** um pagamento capturado de um pedido que não pôde ser confirmado (reserva vencida e produto vendido), **quando** acontece, **então** o pedido vai para "Pago, sem estoque" com alerta ao lojista, que escolhe entre atender depois ou estornar; nada é estornado sozinho.

## Regras de negócio e técnicas
- O gateway é um adaptador atrás de uma porta, como o Pix; trocar de gateway não muda a venda.
- Segredos do gateway e do PSP ficam cifrados por tenant, write-only, como os demais.
- O webhook é rota anônima com assinatura validada, já prevista na lista do `CLAUDE.md`.

## Permissões, eventos e tabelas
- **Permissões:** `storefront.edit` para configurar o gateway (com 2FA de novo, como os outros segredos).
- **Endpoints:** `/api/v1/store/payments/pix`, `/store/payments/card`, `/api/v1/webhooks/gateway/{provider}`.
- **Tabelas:** `storefront.online_payments`; `finance.pix_charges`; as do `sales` pelo contrato.
- **Auditoria:** configuração do gateway.

## Telas e estados
- **Pagamento** e **Pedido confirmado** (públicas). Estados: aguardando Pix, pago, recusado, em conferência. Carregando e erro.
- **Loja → Loja virtual → Pagamento** (`/app/loja/loja-virtual/pagamento`): provedor de Pix, gateway, chip "A confirmar" nas taxas.
- **Ajuda:** `Ajuda/loja-loja-virtual-pagamento.md` (nova).

## Plano de testes
- **Integração (Testcontainers), obrigatória por movimentar financeiro e estoque:** webhook + reconsulta (CA 2 e 3); idempotência da confirmação (CA 10); venda sem sessão de caixa (CA 9); `Unknown` (CA 8); pago sem estoque (CA 12).
- **Contrato:** nenhum campo de cartão na API (CA 5).
- **Provedores falsos** para Pix e gateway, como nas stories bancárias.

## Fora do escopo
- Antifraude próprio (é do gateway). Assinatura e recorrência.
- Estorno e arrependimento (ECM-05).

## Pontos em aberto
- PP-21: qual gateway.
- Se a PAG-02 (Fase 12) ainda não existir quando a trilha abrir, a porta `IPixChargeProvider` nasce aqui.

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
