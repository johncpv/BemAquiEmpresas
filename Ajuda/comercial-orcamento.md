---
titulo: Orçamento — dados
rota: /app/orcamentos/$id
aba: dados
padrao: true
modulo: quotes
---

## Para que serve
Guardar o cabeçalho do orçamento: contato, endereço da obra, prazo de entrega prometido e observações.

## Como usar
- O contato vem com o contato de cobrança do cliente, e a obra com o endereço de obra padrão. Troque se precisar.
- O prazo de entrega é em dias corridos depois da aprovação.
- Clique em **Salvar orçamento**.
- Quando o orçamento estiver pronto, clique em **Marcar como enviado**.
- Com o sim do cliente, clique em **Aprovar e gerar contrato**. O diálogo mostra o que falta (validade, item com preço, marcos que somam 100% e cliente ativo), com o caminho para corrigir. Confira a data de assinatura (padrão hoje, nunca no futuro) e a conta de receita das parcelas.
- **Contrato de implantação:** para o contrato que já estava em andamento antes do sistema, marque "Contrato de implantação (já estava em andamento)". Aí a data de assinatura e as datas dos marcos podem estar no passado, e cada marco pode ser marcado "Recebido antes da implantação" (vem marcado quando a data é até a data de corte da Implantação financeira). O marco recebido antes não gera título, porque o dinheiro já está no saldo das contas. Recebimento depois da data de corte entra como baixa no título.
- Depois da aprovação, os próximos passos levam ao projeto criado, ao contrato e ao anexo do contrato assinado.

## Perguntas frequentes
**Posso mudar um orçamento enviado?** Não. Enviado, ele fica só para consulta. Para mudar, crie uma **Nova revisão**. Para mudar só a data, use **Prorrogar validade**.

**Posso registrar um contrato que já estava rolando?** Sim, com "Contrato de implantação". Antes, defina a data de corte em Financeiro → Implantação financeira. Depois, registre a fabricação e a entrega com as datas reais no projeto.

**Posso mudar um orçamento aprovado?** Não. Ajustes no contrato são feitos por aditivo.

**Posso excluir?** Só o rascunho que nunca teve revisão. O número não volta para a sequência.
