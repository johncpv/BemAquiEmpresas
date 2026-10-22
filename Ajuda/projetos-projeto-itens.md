---
titulo: Projeto — itens e OS
rota: /app/projetos/$id
aba: itens
padrao: false
modulo: projects
---

## Para que serve
Ver o que foi vendido e as ordens de serviço de cada item: a OS de fabricação e as OS de instalação.

## Como usar
- Cada item mostra a descrição, a quantidade e a OS de fabricação com a etapa atual.
- O equipamento ganha a OS de fabricação sozinho na aprovação do orçamento. O componente fabricado, e o equipamento de projeto aprovado antes da OS automática, mostram **Gerar OS de fabricação** (quem cria na produção).
- Serviço e contrato de implantação não geram OS de fabricação.
- Abaixo, as OS de fabricação e as OS de instalação do projeto, com a etapa e a situação.
- **Nova OS de instalação** (quem cria na produção): escolha os itens (os que ainda não têm OS vêm marcados), o instalador, o valor combinado e as datas previstas. O valor vem sugerido pelo orçado de instalação do projeto, para quem vê custo.

## Perguntas frequentes
**De onde vêm os itens?** Do orçamento aprovado. Todo item do orçamento vira item do projeto.

**Por que o botão Gerar OS não aparece?** O item já tem OS, é serviço, o contrato é de implantação ou o projeto já foi fabricado.