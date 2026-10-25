---
titulo: Inventário — contagem
rota: /app/estoque/inventarios/$id
aba: contagem
padrao: true
modulo: inventory
---

## Para que serve
Contar os itens e lotes do local, sem ver o saldo do sistema, e acompanhar o inventário até o ajuste.

## Como usar
- No topo ficam o número, a situação, quem abriu e, nos fatos, o progresso ("{x} de {y} itens contados"), a foto do saldo, o escopo e as recontagens pendentes.
- Em **Contagem**, cada cartão é um item (e lote). Digite a **Quantidade contada** na unidade de estoque (ou em outra unidade cadastrada para o item) e clique **Salvar contagem**. O cartão mostra quem contou e quando.
- **Encontrei outro item**: inclua um item ou lote que não estava na lista, já com a quantidade contada.
- **Contar no celular** abre a contagem em tela cheia, com botões grandes e teclado numérico.
- **Fechar inventário** (ação **Ajustar estoque**): o diálogo mostra as sobras, as faltas, o valor líquido e o antes → depois de cada linha. Se faltou contar algum item, escolha **Considerar como zero** ou **Manter o saldo do sistema**; a escolha fica gravada. Fechado, nasce um ajuste em modo **Diferença** para aprovação, e o próximo passo leva a **Ver ajuste**.
- **Cancelar inventário**: com o motivo; nenhum ajuste nasce e o local é desbloqueado.
- Depois de fechar, a faixa mostra "Ajuste … aguardando aprovação", "aprovado" ou "reprovado", com o link. Reprovado, **Reabrir contagem** volta o inventário para a contagem, com as contagens de antes.

## Perguntas frequentes
**Apareceu "Esta linha foi contada por … agora há pouco".** Outra pessoa salvou a mesma linha ao mesmo tempo. Clique **Recarregar** para ver o que foi contado.

**Por que não vejo o saldo do sistema?** É a contagem cega: assim o número digitado é o da prateleira, sem influência do que o sistema diz.

**Por que um item sumiu da minha lista?** Ele foi para a recontagem e você contou a primeira vez. A recontagem precisa ser feita por outra pessoa.
