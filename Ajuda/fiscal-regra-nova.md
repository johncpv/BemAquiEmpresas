---
titulo: Nova regra fiscal
rota: /app/fiscal/regras/nova
aba:
padrao: true
modulo: fiscal
---

## Para que serve
Cadastrar a regra de uma operação que ainda não tem, como **remessa para obra**, **retorno de obra**, **devolução de compra** ou outra saída, ou uma regra mais específica para um NCM.

## Como usar
1. Escolha a **operação**. Na prestação de serviço somem o CFOP, o ICMS e o IPI e aparece o ISS.
2. Preencha a descrição, a natureza da operação (o texto da nota), a vigência e os CFOP. O de fora do estado é sugerido a partir do de dentro.
3. Em **Condições**, limite por UF de destino, tipo de cliente ou NCM. Uma regra com NCM ganha da sem NCM; duas regras ativas para o mesmo caso são recusadas ("Esta regra se sobrepõe à regra…").
4. Ajuste os tributos e os que entram no custo do projeto e clique em **Salvar regra fiscal**. A regra nasce em rascunho, "A confirmar com a contadora", e outra pessoa com permissão a ativa depois de simular.

## Perguntas frequentes
**Por que a regra não vale logo depois de salvar?** Ela nasce em rascunho. Outra pessoa com permissão de ativar abre a regra, simula e clica em **Ativar regra**; quem cadastrou não ativa a própria regra.

**Posso ter uma regra só para um produto?** Sim: informe o NCM ou o prefixo em **Condições**. Ela ganha da regra sem NCM da mesma operação.
