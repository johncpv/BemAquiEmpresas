---
titulo: Bem
rota: /app/patrimonio/bens/$id
aba: dados
padrao: true
modulo: assets
---

## Para que serve
Ver e alterar os dados do bem: identificação e aquisição. Mostrar onde ele está, quem responde por ele e em que centro de trabalho, e registrar quando muda de lugar ou sai da empresa.

## Como usar
- Altere o que precisar e clique em **Salvar bem**.
- O local, o responsável e o centro mudam por **Registrar movimentação** (no topo da ficha ou na seção Onde está), para ficar no histórico do bem.
- **Baixar bem** tira o bem da empresa (venda, descarte, perda ou roubo, doação ou outro motivo), com a data e, na venda, o valor e o comprador. O bem some das listas e dos seletores, mas nada é apagado.
- **Reativar bem** aparece no bem baixado: ele volta a Ativo, com o motivo, e a baixa continua no histórico.
- **Imprimir etiqueta** baixa o PDF da etiqueta com o QR code, no tamanho de Configurações → Parâmetros → Patrimônio. **Reimprimir etiqueta** (para quem edita o patrimônio) serve para a etiqueta danificada: sai a mesma etiqueta. As duas ficam no histórico do bem.
- **Duplicar bem** abre um bem novo com os mesmos dados, menos a série e a etiqueta.
- **Excluir** só funciona para bem sem movimentação, manutenção nem custo. O bem com histórico é baixado: a recusa oferece **Baixar bem**.
- Sem a permissão de editar o patrimônio, a ficha abre em modo leitura. O bem baixado também fica em modo leitura até ser reativado.

## Perguntas frequentes
**Por que não consigo mudar a etiqueta?** A etiqueta é gerada ao salvar e nunca muda. É ela que identifica o bem no QR code.

**O aviso diz que o responsável foi desligado.** O funcionário saiu da empresa. Clique em **Troque o responsável**: a movimentação abre com o motivo "Responsável desligado".

**Vendi o bem. Como lanço o recebimento?** Ao baixar por **Venda**, aparece o próximo passo **Lançar título a receber** (para quem lança no Financeiro). Ele abre o título a receber já com o comprador, o valor, a data e a descrição "Venda do bem PAT-00001 — nome". Nada é gravado no Financeiro até você salvar o título lá.

**Baixei por engano um bem vendido.** Use **Reativar bem**. Se o título da venda já foi lançado, cancele o título no Financeiro, se for o caso: a reativação não mexe no Financeiro.

**O que vai no QR code da etiqueta?** Só o endereço curto do bem (`/b/PAT-00001`), sem valor, série nem responsável. Quem lê precisa entrar no sistema.

**Onde fica o valor de aquisição?** Na seção Aquisição, só para quem tem a permissão de editar o patrimônio. O valor da venda segue a mesma regra.
