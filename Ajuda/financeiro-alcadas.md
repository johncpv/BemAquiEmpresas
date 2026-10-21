---
titulo: Alçadas
rota: /app/financeiro/alcadas
aba:
padrao: true
modulo: finance
---

## Para que serve
Definir quem aprova cada ordem de pagamento, por faixa de valor, por conta e em até três níveis.

## Como usar
- A regra padrão é "Proprietário aprova tudo". Crie faixas para delegar quando houver outro aprovador.
- **Nova regra de alçada** (só o Proprietário): escolha o nível (1 a 3), a faixa de valor, a conta (opcional, com teto obrigatório) e o aprovador (um grupo ou um usuário com a permissão Aprovar).
- **Nível:** a OP passa pelo nível 1 e, se houver regra no nível 2 (ou 3) para o valor, por ele também, nessa ordem.
- **Regra mais específica:** a regra por conta vence a faixa quando todos os títulos da OP são da mesma conta e o valor cabe no teto.
- O **simulador** mostra quem aprovaria uma OP de um valor.

## Perguntas frequentes
**Por que o valor de referência é maior que o total da OP?** Para evitar o fracionamento: vale o maior entre o total, o maior título e a soma das OPs do mesmo fornecedor nos últimos 30 dias.

**Mudar uma regra afeta as OPs já enviadas?** Não. Elas mantêm a alçada do envio. "Reenviar para aprovação" recalcula.
