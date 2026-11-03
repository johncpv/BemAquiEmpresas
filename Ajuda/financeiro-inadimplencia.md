---
titulo: Inadimplência
rota: /app/financeiro/inadimplencia
aba: clientes
padrao: true
modulo: finance
---

## Para que serve
Ver quem está devendo e há quanto tempo, para cobrar primeiro quem pesa mais.

## Como usar
- Cada cliente mostra também a pontualidade dos últimos 12 meses: o **atraso médio** (dias entre o vencimento e a baixa que quitou, pesando o valor; pagar antes conta como zero) e o **% pago com atraso**. Cliente sem título quitado aparece como "Sem histórico".
- Os cartões mostram o saldo vencido por faixa de atraso: até 30 dias, 31 a 60, 61 a 90 e mais de 90.
- A tabela lista os clientes do maior saldo vencido para o menor. Clique no cliente para ver os títulos vencidos dele.
- Todo dia às 8h, quem edita o Financeiro recebe um aviso com os títulos que venceram no dia anterior.

## Imprimir e exportar
- Clique em **Exportar**, na barra da lista, e escolha **Excel (.xlsx)** ou **PDF**. Vale a ordem da tela.
- Saem o cliente, as quatro faixas (até 30, 31 a 60, 61 a 90 e mais de 90 dias), o total vencido, o maior atraso, a quantidade de títulos, o atraso médio e o % pago com atraso ("Sem histórico" quando não há). A última linha é o total, igual aos cartões.
- O CPF/CNPJ do cliente não vai para o arquivo.
- O limite é de 10.000 linhas no Excel e 2.000 no PDF. Acima disso, o sistema avisa e pede para refinar os filtros (período, situação ou busca).
- Toda exportação fica registrada na auditoria (quem, quando, formato, quantas linhas e os filtros). Com a lista vazia, **Exportar** fica desabilitado.

## Perguntas frequentes
**Como são contados os dias de atraso?** Da data de vencimento até hoje. O "maior atraso" é o do título mais antigo do cliente.

**Título pago em parte entra?** Entra, pelo que falta receber.

**Os títulos da implantação entram?** Entram, como os demais. Títulos cancelados e parcelas sem data não entram.
