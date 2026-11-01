---
titulo: Bem
rota: /app/patrimonio/bens/$id
aba: dados
padrao: true
modulo: assets
---

## Para que serve
Ver e alterar os dados do bem: identificação, aquisição, local, responsável e centro de trabalho.

## Como usar
- Altere o que precisar e clique em **Salvar bem**.
- **Imprimir etiqueta** baixa o PDF da etiqueta com o QR code, no tamanho de Configurações → Parâmetros → Patrimônio. **Reimprimir etiqueta** (para quem edita o patrimônio) serve para a etiqueta danificada: sai a mesma etiqueta. As duas ficam no histórico do bem.
- **Duplicar bem** abre um bem novo com os mesmos dados, menos a série e a etiqueta.
- **Excluir** só funciona para bem sem movimentação, manutenção nem custo. O bem com histórico é baixado.
- Sem a permissão de editar o patrimônio, a ficha abre em modo leitura.

## Perguntas frequentes
**Por que não consigo mudar a etiqueta?** A etiqueta é gerada ao salvar e nunca muda. É ela que identifica o bem no QR code.

**O aviso diz que o responsável foi desligado.** O funcionário saiu da empresa. Escolha outro responsável no campo Responsável e salve.

**O que vai no QR code da etiqueta?** Só o endereço curto do bem (`/b/PAT-00001`), sem valor, série nem responsável. Quem lê precisa entrar no sistema.

**Onde fica o valor de aquisição?** Na seção Aquisição, só para quem tem a permissão de editar o patrimônio.
