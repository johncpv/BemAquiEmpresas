---
titulo: Séries do item
rota: /app/estoque/itens/$id
aba: series
padrao: false
modulo: inventory
---

## Para que serve
Ver cada unidade deste produto que já entrou no estoque com número de série: de qual OS e de qual projeto ela veio, onde está e a situação.

## Como usar
- Cada linha mostra a **série**, a **situação** (**No estoque**, **Expedida** ou **Cancelada**), a **OS** (clique para abrir a aba **Séries** dela), o **projeto**, o **local** e quando entrou. As mais novas aparecem primeiro.
- A 400 px ficam a série e a situação; toque na linha para ver a OS, o projeto e o local.
- As séries nascem quando a OS de fabricação do produto é concluída, no formato código do item, ano e sequência (`ELV-0001-2026-00001`).
- **Cancelada:** a conclusão da OS foi estornada. O número fica guardado e não volta a ser usado.

## Perguntas frequentes
**Por que diz "Este produto não controla série."?** O item está sem controle de série. A entrada do produto acabado é por quantidade. Marque o controle de série na aba **Dados** antes da próxima conclusão.

**Onde vejo de quais lotes uma série foi feita?** Na aba **Séries** da OS que produziu a unidade, em **Lotes de matéria-prima consumidos**.
