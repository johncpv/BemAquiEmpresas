---
titulo: Configurações — Cobrança
rota: /app/configuracoes/cobranca
aba: cobranca
padrao: true
modulo: settings
---

## Para que serve
Define a multa, os juros de mora, o desconto padrão e o envio do boleto com Pix emitido pelo Banco Inter. Os valores já vêm preenchidos (multa de 2,00%, juros de 1,00% ao mês, sem desconto, envio por e-mail ligado e até 200 e-mails por dia).

Na seção **Régua de cobrança**, configura os lembretes automáticos por e-mail ao cliente, antes e depois do vencimento.

## Como usar
- Ajuste os valores e clique em **Salvar cobrança**. A multa vai de 0 a 2,00% e os juros de 0 a 1,00% ao mês.
- Os valores com o selo **A confirmar com a contadora** já valem, mas precisam da conferência dela. Depois de conferir, clique em **Marcar como conferido**. Se mudar um valor conferido, o selo volta.
- **Limite diário de e-mails** soma orçamentos, pedidos de compra, boletos e lembretes da régua. Acima dele, o boleto sai no dia seguinte às 08:00 e o lembrete, no próximo dia útil.
- **Multa e juros do título vencido** contam do vencimento original do título (padrão) ou do novo vencimento do boleto.

### Régua de cobrança
- A régua vem **desligada**, com quatro passos sugeridos: 3 dias antes do vencimento, no dia, 3 dias depois e 10 dias depois. Cada passo tem um texto sugerido.
- Os dias de cada passo vão de 30 antes a 90 depois do vencimento, até 6 passos, sem repetir o dia. Passo que cai em sábado, domingo ou feriado sai no próximo dia útil.
- O texto é puro: sai como está, sem formatação. Use os **Campos do texto** (clique para colocar no texto): `{empresa}`, `{cliente}`, `{numero}`, `{valor}`, `{vencimento}`, `{linha_digitavel}` e `{dias_de_atraso}`. Outro campo é recusado.
- Clique em **Ver prévia** para ver o e-mail com um título de exemplo.
- Escolha o **Horário de envio** (de 07:00 a 20:00, horário de Brasília) e o e-mail que recebe as respostas do cliente. Vazio, vale o e-mail da empresa.
- Marque **Régua ligada** e clique em **Salvar régua**. A mensagem diz quantos títulos recebem lembrete no próximo dia de envio.
- Para parar a cobrança de um cliente ou de um título (por exemplo, numa negociação), use **Pausar cobrança** no título ou no cadastro do cliente.

## Perguntas frequentes
**Mudar a multa altera boletos já emitidos?** Não. Vale para os boletos novos; para mudar um boleto emitido, cancele e emita outro.

**Quem pode mudar?** Só quem edita Configurações. Quem só vê o Financeiro consulta em modo leitura.

**E o vencimento em sábado, domingo ou feriado?** Segue a regra do banco: o cliente paga no dia útil seguinte. O título não muda.

**A régua cobra quem já pagou?** Não. O sistema confere o título ao montar a lista e de novo na hora de enviar. Título pago, cancelado, com boleto pago aguardando conferência ou com baixa registrada no dia não recebe lembrete.

**Quais títulos entram na régua?** Os títulos a receber em aberto lançados à mão e as parcelas de contrato. Os títulos da implantação ficam fora.

**O e-mail leva o boleto?** Se o título tem boleto emitido, o e-mail leva a linha digitável e o PDF. Cada e-mail fala só de um título.

**Para quem vai o lembrete?** Para o contato do cliente marcado "recebe cobrança". Sem esse contato, o lembrete aparece como ignorado no título.
