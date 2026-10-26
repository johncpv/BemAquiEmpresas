---
titulo: DRE gerencial
rota: /app/financeiro/dre
aba:
padrao: true
modulo: finance
---

## Para que serve
Ver, mês a mês, quanto a empresa vendeu, quanto custou produzir, quanto gastou para funcionar e quanto sobrou. É o resultado gerencial, por competência, para fechar o mês sem esperar a contadora. Não substitui a contabilidade.

## Como usar
- Escolha o **período** (3, 6, 12 ou 24 meses) e **até o mês**. O padrão são os 12 meses até o mês corrente.
- As linhas seguem a ordem do DRE: **Receita bruta**, **(−) Deduções**, **= Receita líquida**, **(−) Custos**, **= Lucro bruto** (com a margem bruta), **(−) Despesas operacionais**, **= Resultado operacional**, **(±) Resultado financeiro**, **(±) Outros resultados** e **= Resultado do período**. Cada grupo se abre nas contas do plano de contas.
- O **sinal fica no rótulo**: "(−) Custos" mostra o valor positivo. Só os resultados (Lucro bruto, Resultado operacional e Resultado do período) ficam em vermelho quando negativos.
- **Δ mês anterior** é o último mês do período menos o mês anterior, em valor e em percentual. **% da receita líquida** mostra quanto cada linha pesa sobre a receita líquida do período.
- **Clique numa célula** de conta ou de grupo para ver os documentos que somam o valor: títulos, lançamentos bancários, baixas e o sinal reconhecido, cada um com o link para o documento. O total do painel é igual ao da célula.
- **Pelo teclado:** as setas movem a célula ativa; **Home** e **End** vão ao início e ao fim da linha; na coluna de nomes, a seta para a direita abre o grupo e a seta para a esquerda fecha; **Enter** abre os documentos da célula.
- **No celular**, escolha o mês no seletor: a tabela mostra o mês, o Δ e o %. Os grupos começam fechados.

## Regras
- **Competência:** a receita entra no mês da venda e a despesa no mês do documento, ou na competência da conta recorrente. Não importa quando o dinheiro entrou ou saiu.
- **Provisório:** até a emissão de notas no sistema, a receita é a data de emissão dos títulos a receber. Com as notas, passa a ser a data da nota, e só os meses de antes continuam com o chip **Provisório**.
- **Sinal como adiantamento:** o sinal do contrato não entra na receita na assinatura. Ele fica na linha **Adiantamentos de clientes (sinal ainda sem nota)**, fora do resultado, e entra na receita na expedição do projeto ou, sem expedição, junto com o marco seguinte. O painel mostra "Sinal reconhecido na expedição de {data}" ou "Sinal reconhecido com o marco {nome}".
- **Compra para estoque:** entra em Custos (2.1 Materiais) no mês da nota do fornecedor, mesmo antes do material ser usado. No resultado por projeto, o material entra quando é baixado para a OS, por isso os dois não fecham.
- **Juros, multas e descontos** das baixas entram nas contas de Configurações → Parâmetros → Financeiro: contas do DRE, no mês da baixa. Baixa estornada não entra.
- **Não entram:** títulos cancelados, títulos previstos, títulos da implantação financeira, lançamentos estornados e transferências entre contas.
- **Mudanças retroativas:** o DRE é recalculado a cada abertura, sem fechamento de mês. Se, desde a sua última consulta, alguém lançou, cancelou ou estornou um documento com data já passada, aparece "Mudou desde a sua última consulta". Clique em **Ver o que mudou**. O mês afetado ganha o ícone de histórico.
- **A classificar:** valores nas contas 1.9 e 3.9 aparecem num aviso, com o link para os títulos. Abra o título e troque a conta.

## Perguntas frequentes
**Por que o DRE é diferente do fluxo de caixa?** O fluxo mostra quando o dinheiro entra e sai. O DRE mostra a que mês a venda e o gasto pertencem.

**Por que aparece "A confirmar com a contadora"?** As contas de juros e descontos e a regra do sinal vêm com a proposta enviada à contadora. Depois de conferir, quem edita as configurações clica em **Marcar como conferido**.

**Por que a folha aparece como uma linha só?** Os nomes e os valores de cada funcionário são dados protegidos. Só quem vê dados sensíveis do RH vê o detalhe.

**O DRE fecha com o resultado por projeto?** Não. O projeto soma o material pelo consumo; o DRE, pela data do documento.
