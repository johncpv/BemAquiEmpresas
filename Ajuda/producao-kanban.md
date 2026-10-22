---
titulo: Kanban da produção
rota: /app/producao/kanban
aba:
padrao: true
modulo: production
---

## Para que serve
Ver todas as OS de fabricação abertas por etapa, num quadro que se atualiza sozinho, e saber onde a fábrica está travando antes de atrasar a entrega.

## Como usar
- Cada coluna é uma **etapa do roteiro**, pela letra (a mesma do display de andar): P Projeto, C Corte, M Montagem, Q Qualidade, E Expedição no roteiro padrão. OS com roteiros diferentes dividem as colunas pela letra.
- No topo da coluna: quantas OS estão nela e as **horas restantes** (horas padrão das etapas atuais menos as apontadas, nunca abaixo de zero).
- **Sem roteiro** junta as OS que nasceram sem etapas: abra a OS e escolha o roteiro.
- **Pronta para concluir** junta as OS com todas as etapas concluídas: abra a OS para concluir.
- O cartão mostra a OS, o produto, o projeto e o cliente, a etapa atual, as horas apontadas contra o padrão (vermelho acima do padrão) e a data prometida.
- Faixa vermelha com **Atrasada {n} d**: a data prometida já passou. Faixa âmbar com **Em risco, previsto {data}**: a data prometida está a até 7 dias e a OS ainda não chegou à última etapa.

## Como a etapa avança
- O quadro **não arrasta**. A etapa segue o roteiro, em ordem: ela avança pelo apontamento do operador no app do chão de fábrica.
- Quem aprova na produção pode usar **Concluir etapa** no menu do cartão (os três pontos), quando a etapa terminou sem apontamento. Informe o motivo: fica registrado quem concluiu e por quê.
- A etapa de qualidade não aparece no menu: ela é decidida na inspeção.
- Quando a primeira etapa é concluída, a OS passa a **Em produção** e o projeto também.

## Filtros
- Clique em **{n} atrasadas** ou **{n} em risco** para ver só essas OS.
- Filtre por projeto, cliente, linha (do centro de trabalho da etapa atual) e roteiro. Os filtros ficam no endereço: dá para mandar o link.
- **Ver como lista** mostra a mesma informação em tabela.

## Tempo real
- O chip **Ao vivo** indica que o quadro se atualiza sozinho quando alguém mexe numa OS.
- Se a conexão cair, aparece **Atualização ao vivo pausada. Tentando reconectar…** e o quadro recarrega a cada minuto até voltar.

## No celular
Uma coluna por vez: escolha a etapa nas abas do topo. Os contadores de atraso e de risco continuam visíveis.

## Perguntas frequentes
**Por que não consigo arrastar o cartão?** Porque a etapa só anda em ordem, pelo apontamento ou pelo "Concluir etapa". Assim o histórico mostra quem fez cada etapa.

**Quem recebe o aviso "OS atrasada"?** Todo dia de manhã, o responsável do projeto e quem aprova na produção recebem um aviso por OS atrasada, uma vez para cada data prometida.
