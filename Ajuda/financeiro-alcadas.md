---
titulo: Alçadas
rota: /app/financeiro/alcadas
aba:
padrao: true
modulo: finance
---

## Para que serve
Definir quem aprova cada ordem de pagamento e cada pedido de compra, por faixa de valor, por conta e em até três níveis.

## Como usar
- Escolha o tipo no alto da tela: **Ordem de pagamento** ou **Pedido de compra**. Cada tipo tem as próprias regras.
- A regra padrão de cada tipo é "Proprietário aprova tudo". Crie faixas para delegar quando houver outro aprovador.
- **Nova regra de alçada** (só o Proprietário): escolha o nível (1 a 3), a faixa de valor, a conta (opcional, com teto obrigatório) e o aprovador (um grupo ou um usuário com a permissão Aprovar do tipo).
- **Nível:** o documento passa pelo nível 1 e, se houver regra no nível 2 (ou 3) para o valor, por ele também, nessa ordem.
- **Regra mais específica:** a regra por conta vence a faixa quando o documento é todo dessa conta e o valor cabe no teto.
- O **simulador** mostra quem aprovaria um documento de um valor.

**Alçadas de pedido de compra**
- Quem aprova pedido precisa da permissão **Aprovar em Compras**. Quem só tem Aprovar em Caixa e pagamentos não aprova pedido.
- O valor de referência do pedido é o maior entre o total do pedido e a soma dos pedidos do mesmo fornecedor nos últimos 30 dias (aprovados ou aguardando), incluindo ele.
- Quem pediu não aprova o próprio pedido. A exceção é o Proprietário quando é o único com a permissão de aprovar: a aprovação ganha a marca Autoaprovado.
- Sem aprovador possível em algum nível, o envio do pedido é recusado com o motivo e o pedido fica em rascunho.

## Perguntas frequentes
**Por que o valor de referência é maior que o total?** Para evitar o fracionamento: na OP vale o maior entre o total, o maior título e a soma das OPs do mesmo fornecedor nos últimos 30 dias; no pedido, a soma dos pedidos do fornecedor nos últimos 30 dias.

**Mudar uma regra afeta o que já foi enviado?** Não. A OP e o pedido mantêm a alçada do envio. Reenviar, ou editar o pedido, recalcula.
