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
- Em **Adicionar item**, escolha o tipo, escreva a descrição e a quantidade.
- Abra a **Composição de custo** do item e preencha as seções: material (item do estoque e custo unitário), mão de obra (centro, cargo e horas), instalação, frete, impostos estimados e outros.
- O **custo unitário** do item é a soma da composição para uma unidade. O preço sugerido sai do % (markup ou margem) do item ou do padrão da empresa.
- Digite outro preço para fixar à mão. A marca **Digitado à mão** aparece, com **Voltar ao sugerido**.
- **Atualizar custos** traz o custo-hora atual dos cargos, com o antes e o depois.

## Perguntas frequentes
**De onde vem o custo da mão de obra?** Do cargo, pelo salário e pelos encargos. Nunca do salário de um funcionário.

**Por que o custo do material já vem preenchido?** É o último custo usado para o mesmo item em outro orçamento. Confira e ajuste.

**O que acontece se o preço ficar abaixo do custo?** O rodapé mostra **Preço abaixo do custo**. O sistema avisa, mas não bloqueia.
