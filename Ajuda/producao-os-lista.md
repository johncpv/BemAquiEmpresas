---
titulo: OS de fabricação
rota: /app/producao/os
aba:
padrao: true
modulo: production
---

## Para que serve
Ver a fila do que a fábrica tem para produzir: cada equipamento vendido vira uma OS de fabricação, com as etapas do roteiro do produto.

## Como usar
- A lista mostra o número da OS, o projeto e o cliente, o produto e a quantidade, a etapa atual (a letra do roteiro), a data prometida ao cliente e a situação.
- A OS atrasada (prometido no passado e OS não concluída) tem a faixa vermelha e o texto **Atrasada {n} d**.
- Filtre por situação, linha (do centro de trabalho) e **Atrasadas**. Clique no projeto ou no cliente de uma linha para ver só as OS dele.
- O chip **Sem roteiro** marca a OS que nasceu sem etapas: abra a OS e escolha o roteiro.
- Quem aprova na produção abre a lista em **Aguardando você**: as OS **Prontas para concluir**, com todas as etapas e a qualidade feitas. Abra a OS e clique em **Concluir OS**. Para ver as outras, escolha **Todas** em Situação.
- **Itens de projeto sem OS** mostra os itens que ainda podem gerar OS. Marque os itens e clique em **Gerar OS**: o diálogo diz quantas OS vão nascer.

## Quais itens geram OS
- **Equipamento**: a OS nasce sozinha quando o orçamento é aprovado.
- **Componente fabricado**: pelo botão **Gerar OS de fabricação** (no projeto) ou pelo filtro **Itens de projeto sem OS**.
- **Serviço** e **contrato de implantação** nunca geram OS. No contrato de implantação, a fabricação aconteceu antes do sistema.
- Projeto já fabricado, em instalação, entregue ou cancelado não gera OS nova.
- Cada item gera uma OS, com a quantidade do item.

## Perguntas frequentes
**A lista abriu vazia em "Aguardando você". E agora?** Nenhuma OS está pronta para concluir. Acompanhe a fábrica pelo Kanban ou escolha **Todas** em Situação.

**Os projetos aprovados antes da OS automática têm OS?** Não sozinhos. Use **Itens de projeto sem OS** para gerar as OS desses itens.

**Por que a OS veio com estas etapas?** O sistema sugere o roteiro nesta ordem: o roteiro do item do projeto, o roteiro vinculado ao produto, o último roteiro usado com o mesmo produto e, se só houver um roteiro ativo, ele.