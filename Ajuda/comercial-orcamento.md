---
titulo: Orçamento — dados
rota: /app/orcamentos/$id
aba: dados
padrao: true
modulo: quotes
---

## Para que serve
Guardar o cabeçalho do orçamento: contato, endereço da obra, prazo de entrega prometido e observações. E simular, antes de mandar, em que data a fábrica consegue entregar.

## Como usar
- O contato vem com o contato de cobrança do cliente, e a obra com o endereço de obra padrão. Troque se precisar.
- O prazo de entrega é em dias corridos depois da aprovação.
- Clique em **Salvar orçamento**.
- Quando o orçamento estiver pronto, clique em **Marcar como enviado**.
- Com o sim do cliente, clique em **Aprovar e gerar contrato**. O diálogo mostra o que falta (validade, item com preço, marcos que somam 100% e cliente ativo), com o caminho para corrigir. Confira a data de assinatura (padrão hoje, nunca no futuro) e a conta de receita das parcelas.
- **Contrato de implantação:** para o contrato que já estava em andamento antes do sistema, marque "Contrato de implantação (já estava em andamento)". Aí a data de assinatura e as datas dos marcos podem estar no passado, e cada marco pode ser marcado "Recebido antes da implantação" (vem marcado quando a data é até a data de corte da Implantação financeira). O marco recebido antes não gera título, porque o dinheiro já está no saldo das contas. Recebimento depois da data de corte entra como baixa no título.
- Depois da aprovação, os próximos passos levam ao projeto criado, ao contrato e ao anexo do contrato assinado.

## Simular prazo
- Clique em **Simular prazo** (no rascunho ou no enviado) para saber, sem gravar nada, em que data a fábrica entrega se o cliente aprovar hoje.
- A simulação usa as horas por centro de trabalho das linhas de **Mão de obra** da composição de custo. Cada item que vira OS (Equipamento e Produto; Serviço nunca) vira uma OS, com uma etapa por centro: as horas do centro vezes a quantidade do item.
- **Ordem das etapas:** a do roteiro do item ou do roteiro sugerido para o produto. Sem roteiro, vale a ordem das linhas de Mão de obra na composição. Centro que não está no roteiro entra no fim.
- Por padrão o orçamento entra **no fim da fila**. Em **Na posição**, escolha de 1 até o tamanho da fila + 1 e clique em **Simular de novo** para ver o prazo se ele passar na frente.
- O resultado diz "Se aprovado hoje, a fabricação termina em … e a entrega fica em …". Com custo de instalação no orçamento, a entrega soma o prazo de instalação (Configurações do Planejamento). Também mostra o prazo prometido no orçamento, com "Cabe no prazo" ou quantos dias passa, e, fora do fim da fila, quantos projetos passam a atrasar (os números e nomes só para quem vê projetos).
- A simulação **não considera o prazo de compra do material**. Para ver se o estoque dá, use **Ver capacidade de produzir**.
- No rascunho, **Usar este prazo** coloca os dias corridos até a entrega no campo "Prazo de entrega prometido". Nada é gravado até você clicar em **Salvar orçamento**. No orçamento enviado, o botão é **Criar nova revisão com este prazo**: nasce a revisão em rascunho, já com o prazo no campo, para você salvar.
- Sem horas por centro, aparece "Este orçamento não tem horas por centro de trabalho. Informe as horas na composição de custo para simular."
- Muitas simulações seguidas (mais de 20 por minuto) pedem "Aguarde alguns segundos para simular de novo."

## Perguntas frequentes
**Posso mudar um orçamento enviado?** Não. Enviado, ele fica só para consulta. Para mudar, crie uma **Nova revisão**. Para mudar só a data, use **Prorrogar validade**.

**Posso registrar um contrato que já estava rolando?** Sim, com "Contrato de implantação". Antes, defina a data de corte em Financeiro → Implantação financeira. Depois, registre a fabricação e a entrega com as datas reais no projeto.

**A simulação reserva lugar na fábrica?** Não. Ela só calcula; a fila e o plano só mudam quando o orçamento é aprovado e as OS são geradas.

**Por que o botão Simular prazo não aparece?** Ele depende do módulo de Produção liberado na versão do sistema, e o orçamento precisa ser a revisão mais recente, em rascunho ou enviado.

**Posso mudar um orçamento aprovado?** Não. Ajustes no contrato são feitos por aditivo.

**Posso excluir?** Só o rascunho que nunca teve revisão. O número não volta para a sequência.
