---
titulo: Aditivo do contrato
rota: /app/contratos/$id/aditivos/$aditivoId
aba:
padrao: true
modulo: projects
---

## Para que serve
Mudar valor e prazo do contrato com aprovação, sem mexer no contrato original.

## Como usar
- Escreva a descrição e escolha o tipo: **Acréscimo**, **Redução** ou **Só prazo**. A variação de prazo em dias soma na data prometida.
- **Acréscimo:** defina os marcos de pagamento do valor a mais, somando 100%. Cada marco vira uma parcela a receber na aprovação.
- **Redução:** o sistema propõe tirar dos títulos futuros sem baixa, do último para o primeiro. Dá para ajustar o valor novo de cada um; a soma tem de fechar com a redução.
- A prévia mostra o contratado e a data prometida antes e depois.
- **Salvar aditivo**, depois **Enviar para aprovação**. Quem aprova vê o impacto e clica em **Aprovar aditivo** ou **Reprovar aditivo** (com o motivo).

## Perguntas frequentes
**O que muda no orçado do projeto?** A variação é distribuída pelas naturezas na proporção do orçado atual.

**Posso mudar um aditivo aprovado?** Não. Registre outro aditivo.
