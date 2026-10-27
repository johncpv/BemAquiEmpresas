---
titulo: Extrato do banco
rota: /app/financeiro/contas/$id/extrato
aba: extrato-banco
padrao: false
modulo: finance
---

## Para que serve
Ver, dentro do sistema, o extrato que o banco informou: as linhas do Inter chegam sozinhas todo dia pela API, e as dos outros bancos entram por arquivo OFX. É a base para conferir o banco com o que o sistema registrou.

## Como usar
- **Buscar agora** (contas do Inter) traz o extrato na hora. A busca automática roda todo dia às 06:30. Dá para buscar de novo depois de 5 minutos.
- **Importar OFX** (contas sem integração): exporte o extrato no formato OFX pelo internet banking e envie o arquivo (até 5 MB). O arquivo passa pelo antivírus, a conta do arquivo é conferida com a escolhida e os lançamentos já importados são ignorados.
- O resumo mostra quantas linhas são novas, quantas já estavam importadas e quantas foram recusadas, com o motivo.
- A faixa **Saldo no banco · no sistema · diferença** compara o saldo que o banco informou no fim do dia com o saldo do sistema no mesmo dia.
- Filtre por período (De e Até) e por situação: Pendente, Conciliada ou Ignorada.
- **Ignorar linha** tira uma linha errada da conciliação, com motivo. Linha de saída só pode ser ignorada por quem aprova em Caixa e pagamentos.
- **Desfazer importação** (em Importações) tira do extrato todas as linhas de um lote, com motivo, enquanto nenhuma estiver conciliada. Depois, o mesmo arquivo pode ser importado de novo.

## Perguntas frequentes
**Por que não consigo importar OFX numa conta do Inter?** O extrato dessa conta vem direto do banco. Se a API ficar fora do ar, quem aprova em Caixa e pagamentos pode **Liberar OFX nesta conta** por até 30 dias, com motivo. As linhas desse arquivo ficam marcadas "Não verificada pelo banco".

**Por que o arquivo foi recusado?** Os motivos mais comuns: o arquivo é de outra conta, já foi importado, repete o id dos lançamentos (FITID), não é um OFX ou o antivírus encontrou um problema. A mensagem diz qual foi.

**Por que vejo a contraparte com asteriscos?** Nome e documento inteiros aparecem só para quem edita o financeiro.

**Quem fica sabendo?** O Proprietário é avisado de toda importação de OFX, de toda liberação de OFX e de toda importação desfeita. Quem edita o financeiro é avisado quando o saldo do banco difere do sistema há mais de 2 dias.

**Posso apagar ou corrigir uma linha?** Não. A linha do banco nunca é editada nem apagada: marque como Ignorada ou desfaça a importação inteira.
