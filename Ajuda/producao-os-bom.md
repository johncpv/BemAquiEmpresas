---
titulo: OS de fabricação — BOM
rota: /app/producao/os/$id
aba: bom
padrao: false
modulo: engineering
---

## Para que serve
Montar a lista de materiais (BOM) desta OS e liberá-la para o estoque reservar o material e o comprador saber o que falta. A BOM é da OS: cada OS tem a sua, com revisões.

## Como usar
- **Criar BOM** abre a revisão 0 em rascunho, vazia. **Copiar de outra OS** traz as linhas de uma BOM já liberada; a última OS do mesmo produto vem com a tag **Sugerido**.
- Em cada linha: o item (só materiais ativos; serviço e o próprio produto da OS não entram), a **quantidade por unidade do produto**, a unidade (a de estoque vem preenchida; outra só com a conversão cadastrada no item), a **perda %** e uma observação.
- A coluna **Necessidade da OS** mostra a conta ao vivo: quantidade por unidade × quantidade da OS × (1 + perda %), na unidade de estoque, com o disponível do estoque e o chip **Falta** quando a necessidade passa dele. Quem vê custo enxerga também o custo previsto (necessidade × custo médio), que não é gravado.
- **Salvar BOM** grava o rascunho sem mexer no estoque. Mude à vontade.
- **Liberar BOM** abre o resumo do impacto por item (necessidade, disponível e falta; quanto vai ser reservado aparece numa próxima versão). Ao confirmar, a BOM fica liberada e o estoque começa a reservar o material. BOM sem nenhuma linha também pode ser liberada: é a OS só de mão de obra.
- Depois de liberada, a BOM não é editada. Para mudar, clique em **Revisar BOM**: nasce a revisão seguinte em rascunho, e a liberada continua valendo até a nova ser liberada em **Liberar revisão**. A anterior fica **Substituída**.
- **Descartar rascunho** apaga a revisão em rascunho. A liberada, se houver, continua valendo.
- **Revisões** lista todas as revisões e compara duas lado a lado. **Histórico de alterações** mostra quem mudou o quê, inclusive o rascunho descartado.

## Perguntas frequentes
**A OS pode começar sem BOM?** A primeira etapa, sim: a BOM nasce na etapa Projeto. Da segunda etapa em diante, a etapa só começa (e só é concluída pelo supervisor) com a BOM liberada. Sem ela, a fábrica vê "A BOM desta OS ainda não foi liberada. Fale com a Engenharia."

**O que quer dizer "Sem item de estoque"?** A OS veio de um item do orçamento que não tem o produto cadastrado no estoque. A BOM pode ser montada e liberada normalmente; para concluir a OS, cadastre o item e ligue à OS.

**Por que não aparece quanto vai ser reservado?** O físico, o reservado, o disponível e a falta já vêm do estoque. A reserva do material para a OS e o que já foi baixado aparecem numa próxima versão.

**Quem pode liberar?** Quem tem a ação "Aprovar" da Engenharia. Quem libera a BOM precisa da verificação em duas etapas no login.

**A revisão diminuiu um material que já foi baixado. E agora?** O resumo da liberação avisa quanto já foi baixado. A sobra volta ao estoque pela devolução da requisição.
