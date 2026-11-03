---
titulo: Funcionário
rota: /app/rh/funcionarios/$id
aba: geral
padrao: true
modulo: hr
---

## Para que serve
Ver e alterar os dados gerais do funcionário: nome, matrícula, admissão, jornada e sindicato, e o usuário do sistema ligado a ele.

## Como usar
- Altere o que precisar e clique em **Salvar dados gerais**. O botão fica preso no rodapé, logo acima do menu, e avisa **Alterações não salvas** enquanto houver algo para salvar.
- Para mudar o cargo, use **Trocar cargo** no topo, à direita do nome: a troca entra na linha do tempo, com a vigência e o motivo.
- **Registrar desligamento** muda a situação para Desligado. Nada é apagado.
- **Registrar afastamento** (com a permissão Ver dados sensíveis) deixa a situação Afastado do início ao fim previsto. O tipo e o motivo ficam na aba Linha do tempo, onde também dá para editar, encerrar antes e cancelar o afastamento. O tipo e o motivo só aparecem com a permissão Ver dados de saúde; sem ela, só as datas.
- Em **Usuário do sistema**, **Vincular usuário** liga um login ao funcionário (para o ponto e o apontamento). Exige a permissão de editar usuários.

## Imprimir e exportar
- Com a permissão **Ver dados sensíveis**, o bloco **Holerites** mostra os 6 últimos holerites do funcionário: competência, tipo (Mensal, Férias, 13º, Rescisão), líquido e a situação da folha. **Baixar holerite** baixa o PDF de cada um; holerite de folha ainda em lançamento sai com a marca d'água de rascunho.
- **Ver todos** abre as folhas de pagamento. Sem a permissão, o bloco não aparece.
- Sem holerite ainda, aparece "Nenhum holerite ainda. Os holerites aparecem quando a folha do mês é lançada.". Cada consulta e cada download ficam no histórico de acessos.

## Perguntas frequentes
**O que é a matrícula?** Um código do funcionário na empresa, se houver. É opcional e não se repete.

**Vincular o usuário muda as permissões dele?** Não. O vínculo só diz que aquele login é deste funcionário. Grupos e permissões continuam em Sistema → Usuários.

**Por que não consigo excluir?** Funcionário com linha do tempo, apontamento ou usuário vinculado não é excluído. Registre o desligamento.

**O que é o chip "Conta em quarentena" no topo?** A conta para pagamento foi trocada há menos de 30 dias e ainda não foi revisada. Veja a aba Pagamento e adicionais.
