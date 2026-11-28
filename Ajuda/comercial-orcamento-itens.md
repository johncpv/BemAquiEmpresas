---
titulo: Orçamento — itens
rota: /app/orcamentos/$id
aba: itens
padrao: false
modulo: quotes
---

## Para que serve
Listar o que o cliente vai comprar, com a quantidade e o preço, e montar o custo de cada item.

## Como usar
- Em **Adicionar item**, busque o **item cadastrado** pelo código, pelo nome ou pelo código de barras. Cada resultado mostra o tipo, a unidade e, para quem vê custo, o custo médio.
- Escolhido o item, a descrição vem preenchida (dá para ajustar) e a linha fica ligada ao item, com a unidade dele. O tipo vem do cadastro: serviço entra como **Serviço**, produto acabado como **Equipamento** e o resto como **Produto**.
- O item ainda não está cadastrado? Use **Digitar à mão**: escolha o tipo, escreva a descrição e a quantidade. A linha fica sem ligação com o estoque.
- Abra a **Composição de custo** do item e preencha as seções: material (item do estoque e custo unitário), mão de obra (centro, cargo e horas), instalação, frete, impostos estimados e outros.
- O **custo unitário** do item é a soma da composição para uma unidade. O preço sugerido sai do % (markup ou margem) do item ou do padrão da empresa.
- Digite outro preço para fixar à mão. A marca **Digitado à mão** aparece, com **Voltar ao sugerido**.
- **Atualizar custos** traz o custo-hora atual dos cargos, com o antes e o depois.

## Perguntas frequentes
**Por que o item que procuro não aparece?** A busca mostra só itens ativos, até 20 por vez. Digite mais do código ou do nome. Item inativo não pode ser escolhido; a linha que já estava ligada a ele continua editável.

**Por que não vejo o custo médio na busca?** Ele aparece só para quem tem a permissão de ver custo do orçamento.

**De onde vem o custo da mão de obra?** Do cargo, pelo salário e pelos encargos. Nunca do salário de um funcionário.

**Por que o custo do material já vem preenchido?** É o último custo usado para o mesmo item em outro orçamento. Confira e ajuste.

**O que acontece se o preço ficar abaixo do custo?** O rodapé mostra **Preço abaixo do custo**. O sistema avisa, mas não bloqueia.
