---
titulo: Projeto — itens
rota: /app/projetos/$id
aba: itens
padrao: true
modulo: projects
---

## Para que serve
Acompanhar o que a fábrica vai entregar do contrato. O projeto nasce na aprovação do orçamento, com os itens e o orçado por natureza de custo.

## Como usar
- O cabeçalho mostra a etapa: **P** Planejado, **E** Em produção, **F** Fabricado, **I** Instalando, **T** Entregue e **C** Encerrado.
- A data prometida é a data de assinatura mais o prazo de entrega do orçamento, em dias corridos.
- O responsável é quem fez o orçamento.
- **Registrar fabricação concluída:** informe a data (até hoje). O projeto passa a fabricado e a parcela do marco de fabricação ganha vencimento. O diálogo mostra qual parcela é liberada.
- **Registrar entrega** (Proprietário): para venda sem instalação. Informe a data; o projeto passa a entregue e as parcelas de entrega e de aceite técnico são liberadas. Se o contrato tem marco de fabricação, registre a fabricação antes.
- A aba **Itens** lista o que foi vendido. Quem vê custo tem também a aba **Orçado**, com o valor por natureza (material, mão de obra, instalação, frete, impostos e outros).

## Perguntas frequentes
**Quando usar "Registrar entrega"?** Quando não há instalação. Com OS de instalação, a entrega vem do aceite técnico.

**Posso mudar o nome, o responsável ou a data?** Numa próxima versão, na tela completa do projeto.

**De onde vem o orçado?** Da composição de custo dos itens do orçamento aprovado. A soma das naturezas fecha no custo total do orçamento.
