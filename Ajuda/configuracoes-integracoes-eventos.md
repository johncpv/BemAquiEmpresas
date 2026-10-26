---
titulo: Eventos recebidos
rota: /app/configuracoes/integracoes/eventos
aba: integracoes-eventos
padrao: true
modulo: settings
---

## Para que serve
Mostrar os avisos que os sistemas externos mandam ao BemAqui: o retorno da nota fiscal, o pagamento de um boleto no banco e, mais tarde, a situação das mensagens do WhatsApp. Cada aviso é conferido (assinatura ou endereço secreto), guardado uma vez só e processado em segundo plano. A lista mostra o provedor, o tipo, quando chegou, a situação, as tentativas e o erro. O conteúdo do aviso não aparece na lista.

## Como usar
- Filtre pelo **Provedor**, pela **Situação** e pelo **Período**. Os filtros ficam no endereço da página, então dá para mandar o link para alguém.
- Se algum aviso estiver **Parado**, a tela já abre no filtro **Parados**.
- A 400 px, toque na seta da linha para ver as tentativas e o erro.
- **Reprocessar** (para o aviso parado, com permissão de editar o módulo dono, como o Financeiro ou o Fiscal): corrija a causa e confirme. Aparece "Evento reenviado para processamento" e o aviso volta para a fila. Aviso que ainda está sendo processado não pode ser reenviado.
- **Ver conteúdo** (com permissão de aprovar no módulo dono; no Fiscal, por enquanto, só o Proprietário): abre o aviso num painel ao lado, com CPF, CNPJ, nome, e-mail, telefone, endereço e chave Pix mascarados. Valores, ids e datas aparecem. Toda abertura fica registrada no histórico.

## Situações
- **Recebido**: chegou, foi conferido e espera o processamento.
- **Processado**: o efeito foi gravado (por exemplo, a nota autorizada ou o pagamento conferido no banco).
- **Ignorado**: não havia nada a fazer, ou o módulo dono não está liberado. O motivo aparece no erro.
- **Falhou**: deu erro e o sistema tenta de novo sozinho, com intervalos cada vez maiores.
- **Parado**: falhou 10 vezes. Quem edita o módulo dono recebe um aviso com o caminho para **Reprocessar**.

## Perguntas frequentes
**Um aviso falso pode baixar um pagamento?** Não. O aviso só é aceito com a assinatura ou o endereço secreto certos, e o sistema confere a informação na fonte (o banco ou o emissor da nota) antes de qualquer efeito. Aviso recusado não é gravado.

**O mesmo aviso chegou duas vezes. Vai baixar em dobro?** Não. O aviso repetido é reconhecido pelo número da entrega e pelo próprio conteúdo, e é guardado uma vez só.

**Por quanto tempo o conteúdo fica guardado?** Por 90 dias. Depois o conteúdo é apagado e fica só o registro (provedor, tipo, datas e situação). Avisos que falharam ou estão parados não são apagados.

**Por que não vejo o botão "Ver conteúdo"?** Porque ele exige a permissão de aprovar no módulo dono do aviso. Peça ao administrador, se precisar.
