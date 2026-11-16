# ECM-04 — Gestão de pedidos online: separação, NF-e modelo 55 automática pelo fluxo existente, frete e etiqueta de envio, expedição e aviso ao comprador a cada estado

| Campo | Valor |
|---|---|
| ID | ECM-04 |
| Título | Gestão de pedidos online: separação, NF-e modelo 55 automática pelo fluxo existente, frete e etiqueta de envio, expedição e aviso ao comprador a cada estado |
| Tamanho | M |
| Status | Draft (plano proposto em 15/11/2026; aguarda a aprovação do PO) |
| Fase | 14 — E-commerce (trilha seguinte à do balcão) |
| Requisitos | Pedido do PO de 15/11/2026 (item 1) · FIS-03a (emissão de NF-e, existente) · [07-emissor-fiscal.md](../07-emissor-fiscal.md) seção 0 (lacuna 25) e lacuna 38 (dívida E14) |
| Dependências | ECM-03; **lacuna 25 corrigida e dívida E14 resolvida no EmissorFiscal** |
| Bloqueia | ECM-05 |
| Paridade com o mercado | Requisitos mínimos 37 do [benchmark](../00-benchmark-mercado.md#7-matriz-de-paridade-cada-requisito-mínimo-e-a-story-que-o-cobre) |
| Ordem de execução | 4 (Fase 14, trilha do e-commerce) |

## User story
Como **dono de loja virtual**, quero **ver os pedidos pagos, separar, emitir a nota e despachar numa tela só**, para **não perder pedido nem prazo**.

## Contexto e referências
- A loja virtual emite **NF-e modelo 55**, que o sistema já emite pelo EmissorFiscal. Não depende da NFC-e.
- **Depende do Emissor em dois pontos:** a lacuna 25 (PIS/COFINS com CST 49 e 99, comum no Simples) reprova a nota de cliente do Simples; e a dívida E14 impede o segundo cliente no Emissor. Sem as duas, esta story não entra em produção para um cliente novo.

## Critérios de aceite
1. **Dado** `storefront.view`, **quando** abro "Pedidos online", **então** vejo os pedidos por estado (Pago, Em separação, Faturado, Enviado, Entregue, Cancelado), com data, comprador, total, forma de entrega e há quanto tempo estão no estado.
2. **Dado** um pedido "Pago", **quando** aciono "Separar", **então** vejo a lista de separação com produto, variação, quantidade e local, posso imprimi-la, e o pedido passa a "Em separação".
3. **Dado** um item que não foi encontrado na separação, **quando** marco a falta, **então** o pedido fica "Em separação, com falta" e o sistema oferece avisar o comprador, enviar parcial ou cancelar o item (com estorno parcial na ECM-05).
4. **Dado** um pedido separado, **quando** aciono "Emitir nota", **então** a NF-e modelo 55 é emitida pelo fluxo existente, com os dados do comprador, os itens da venda e o frete, **com confirmação** (o sistema sugere, não decide); o pedido passa a "Faturado" e o comprador recebe o DANFE por e-mail.
5. **Dado** a loja configurada para "emitir a nota ao separar", **quando** concluo a separação, **então** a prévia da nota já vem pronta e a confirmação é um clique.
6. **Dado** a nota rejeitada, **quando** acontece, **então** o pedido fica em "Faturamento com erro", com o motivo e o caminho para corrigir; não segue para envio.
7. **Dado** um pedido "Faturado", **quando** informo o envio, **então** registro a forma (retirada, entrega própria, transportadora), o código de rastreio quando houver, imprimo a etiqueta de envio, e o pedido passa a "Enviado" ou "Pronto para retirada".
8. **Dado** cada mudança de estado, **quando** acontece, **então** o comprador recebe um e-mail (pela fila existente) e vê o estado na página do pedido.
9. **Dado** "Entregue" ou "Retirado", **quando** confirmo, **então** o pedido é concluído.
10. **Dado** pedidos parados além do prazo configurado em um estado, **quando** abro a tela, **então** eles vêm destacados e contam num card do Início.

## Regras de negócio e técnicas
- A venda e o recebível já nasceram no pagamento (ECM-03); esta story não mexe em financeiro.
- A NF-e usa as regras fiscais do tenant (FIS-02b). Venda para outro estado a não contribuinte pode cair em regra que o tenant ainda não conferiu: a emissão respeita os bloqueios que já existem.
- Etiqueta de envio reusa a base de etiqueta do produto.

## Permissões, eventos e tabelas
- **Permissões:** `storefront.view`, `storefront.edit`; emitir a nota exige as permissões do `fiscal` que já existem.
- **Endpoints:** `GET /api/v1/storefront/orders`, `GET .../{id}`, `POST .../{id}/pick`, `POST .../{id}/ship`, `POST .../{id}/deliver`, `GET /api/v1/storefront/orders/export`.
- **Eventos:** `OnlineOrderStatusChanged`.
- **Tabelas:** `storefront.online_orders` (estado, rastreio); nota pelo contrato do Fiscal.
- **Auditoria:** mudanças de estado.

## Telas e estados
- **Loja → Pedidos online** (`/app/loja/pedidos-online`): lista por estado. Vazio: "Nenhum pedido ainda.". Carregando e erro.
- **Pedido online** (detalhe): itens, pagamento, nota, envio e histórico, com o próximo passo em destaque.
- **Página do pedido** (pública, do comprador).
- **Ajuda:** `Ajuda/loja-pedidos-online.md` e `Ajuda/loja-pedido-online.md` (novas).

## Plano de testes
- **Unidade:** máquina de estados do pedido.
- **Integração (Testcontainers), com o emissor falso:** pedido pago → separação → nota autorizada → enviado → entregue (CA 2, 4, 7 e 9); nota rejeitada (CA 6); falta na separação (CA 3).
- **Vitest:** lista por estado e destaque de atraso.

## Fora do escopo
- Cotação de frete com transportadora por API e integração com os Correios (entra depois, com a PP-21).
- Cancelamento, devolução e estorno (ECM-05).
- Marketplaces e plataformas (ECM-06 e ECM-07).

## Pontos em aberto
- Regras fiscais de venda interestadual a consumidor final (DIFAL): hoje a emissão bloqueia; confirmar com a contadora do cliente.

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
