---
titulo: Etiquetas com QR code
rota: /app/patrimonio/bens
aba: etiquetas
padrao: false
modulo: assets
---

## Para que serve
Imprimir a etiqueta adesiva de cada bem, com o QR code e o número do patrimônio. Na fábrica, quem aponta o celular para a etiqueta vê a ficha da máquina e pode abrir a manutenção na hora, sem procurar em planilha.

## Como usar
- **Uma etiqueta:** abra o bem e clique em **Imprimir etiqueta**. O PDF sai no tamanho configurado em Configurações → Parâmetros → Patrimônio.
- **Várias de uma vez:** na lista de bens, marque os bens (ou filtre a lista) e clique em **Imprimir etiquetas**. Escolha **Os selecionados** ou **Todos do filtro** (até 500) e **Impressora de etiquetas** (uma por página) ou **Folha A4** (em grade, com linha de corte). Até 50 etiquetas o PDF baixa na hora; acima disso, ele é gerado em segundo plano e o sino avisa quando estiver pronto. O link do aviso vale por 7 dias.
- **Etiqueta danificada:** abra o bem e clique em **Reimprimir etiqueta** (precisa da permissão de editar o patrimônio). A etiqueta é a mesma de antes: o número do bem nunca muda.
- Cada impressão e reimpressão fica no **Histórico de alterações** do bem, com quem e quando.

## Perguntas frequentes
**O que vai no QR code?** Só o endereço curto do bem no sistema, por exemplo `https://app.konebras.com.br/b/PAT-00001`. Não vai valor, número de série, responsável nem nenhum código interno. Quem lê precisa entrar no sistema para ver a ficha.

**O que aparece impresso na etiqueta?** O QR code, o número do bem em letra grande, o nome do bem (até 2 linhas) e o nome da empresa.

**Qual o tamanho da etiqueta?** Começa em 50 × 25 mm. Quando a empresa tiver o modelo da impressora de etiquetas, ajuste em Configurações → Parâmetros → Patrimônio.

**Não tenho impressora de etiquetas. Posso imprimir mesmo assim?** Pode. Escolha **Folha A4** em **Imprimir etiquetas** e use papel adesivo comum.

**O aviso do sino diz que o arquivo expirou.** O PDF do lote fica guardado por 7 dias. Peça as etiquetas de novo na lista de bens.
