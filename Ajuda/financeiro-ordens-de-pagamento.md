---
titulo: Ordens de pagamento
rota: /app/financeiro/ordens-de-pagamento
aba:
padrao: true
modulo: finance
---

## Para que serve
Garantir que nenhum dinheiro saia da empresa sem uma ordem aprovada. Toda saída para fornecedor passa por aqui.

## Como usar
- Em **A pagar**, marque os títulos (ou use **Selecionar os que vencem até…**) e clique em **Criar ordem de pagamento**.
- Use o filtro **Aguardando você** para ver o que espera a sua aprovação.
- O filtro **Aprovadas pelo próprio solicitante** mostra as ordens que quem pediu também aprovou. O selo aparece na lista, no cabeçalho da ordem, na linha da decisão, no PDF e na exportação.

## Imprimir e exportar
- Clique em **Exportar** e escolha **Excel (.xlsx)** ou **PDF**. Valem a situação e o "Mostrar" da tela, com todas as páginas.
- Saem número, situação, data prevista, total, quantidade de títulos, solicitante, data e hora do envio e se foi autoaprovada. A última linha soma o total do filtro.
- O PDF de cada ordem continua no botão **Baixar PDF** da própria ordem.
- O limite é de 10.000 linhas no Excel e 2.000 no PDF. Acima disso, o sistema avisa e pede para refinar os filtros (período, situação ou busca).
- Toda exportação fica registrada na auditoria (quem, quando, formato, quantas linhas e os filtros). Com a lista vazia, **Exportar** fica desabilitado.

## Perguntas frequentes
**Quem aprova?** Quem a alçada indicar, em Financeiro → **Alçadas**: por faixa de valor, por conta e em até três níveis, sempre com a permissão de aprovar no Financeiro. A regra padrão é "Proprietário aprova tudo". A ordem guarda a alçada do envio; reenviar recalcula.

**Quem paga?** O Financeiro, depois da aprovação, registrando a saída da conta.

**Posso aprovar a ordem que eu pedi?** Depende da regra **Quem solicitou a ordem pode aprová-la?**, em Configurações → Parâmetros → Limites do financeiro: **Nunca** (só o Proprietário único aprova a própria), **Só o Proprietário** ou **Qualquer aprovador (só no 1º nível)**. Quando pode, o diálogo **Aprovar** avisa que a aprovação fica registrada como feita pelo próprio solicitante. Quando não pode, peça a outra pessoa; se você é do Proprietário, o atalho **Permitir que eu aprove minhas ordens** leva aos parâmetros. Pagar continua pedindo a verificação em duas etapas, e numa alçada de dois níveis quem aprovou o 1º nunca aprova o 2º.

**Reenviei ou estornei uma ordem autoaprovada. O selo some?** A decisão antiga continua no histórico com o selo. No reenvio, a ordem perde o selo até a nova decisão; no estorno, ela o mantém.

**Salários: conta lida no pagamento.** O título de salário paga na conta do funcionário, que fica no RH. A ordem guarda só o final da conta. No "Pagar", o bloco **Para onde vai o dinheiro** lê os dados na hora, só para quem paga. Se a conta mudou depois do envio, a ordem precisa de nova aprovação (**Reenviar para aprovação**). Conta em quarentena não entra na ordem.

**Quem fechou a folha pode pagar os salários?** Não. A exceção é o Proprietário único, com a marca de autopagamento.
