---
titulo: Modelo de etapas — itens
rota: /app/projetos/modelos/$id
aba: itens
padrao: false
modulo: projects
recurso: projects.service_execution
---

## Para que serve
Ligar itens de serviço ao modelo, para o orçamento sugerir este modelo quando o item for vendido.

## Como usar
- **Vincular item de serviço** busca os itens de serviço ativos do Estoque. Só item de serviço pode ter modelo de etapas.
- Cada item tem um modelo só. Se o item já usa outro, a mensagem oferece **Abrir modelo**.
- **Desvincular** faz o orçamento voltar a sugerir o padrão. Os projetos já criados não mudam.
- A ficha do item mostra o modelo ligado a ele, só para consulta.

## Perguntas frequentes
**Por que não acho um produto para vincular?** Só itens do tipo Serviço, ativos, podem ter modelo de etapas. Produtos e componentes seguem o roteiro da Engenharia.
