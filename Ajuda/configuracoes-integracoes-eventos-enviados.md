---
titulo: Avisos enviados ao Conecta
rota: /app/configuracoes/integracoes/eventos
aba: integracoes-eventos-enviados
padrao: false
modulo: conecta
---

## Para que serve
Mostrar os avisos que o BemAqui manda ao Conecta quando um pedido muda, um boleto é emitido, pago, cancelado ou vence, ou uma nota fiscal sai. O Conecta usa o aviso para mandar a mensagem ao cliente no WhatsApp, sem ninguém da equipe lembrar. A lista mostra o tipo, a situação, a hora, as tentativas e o erro.

## Como usar
- Filtre pelo **Tipo**, pela **Situação** e pelo **Período**. Com algum aviso **Parado**, a tela já abre no filtro **Parados**.
- Clique no tipo para abrir o painel do aviso: o conteúdo enviado e cada tentativa (hora, código HTTP, duração e o erro resumido).
- **Reenviar** (com permissão de editar o Conecta): o aviso entregue ou parado sai de novo, com o mesmo id e o mesmo conteúdo. Aparece "Reenviado".
- **Enviar teste**: sai um aviso de teste pela mesma fila e o resultado aparece na lista em alguns segundos. Aparece "Teste enviado".
- **Trocar segredo**: gera um segredo de assinatura novo. O Conecta recebe o novo sozinho na próxima renovação do acesso (até 15 minutos), e o anterior continua valendo por 24 horas. Marque **Invalidar o anterior agora** só se houver suspeita de vazamento: até o Conecta renovar, os avisos falham e tentam de novo. São até 5 trocas por dia.

## Situações
- **Na fila**: esperando o envio.
- **Entregue**: o Conecta respondeu que recebeu.
- **Tentando de novo**: o Conecta não respondeu ou recusou. O sistema tenta de novo em 1 min, 5 min, 30 min, 2 h, 6 h, 12 h e 24 h.
- **Parado**: falhou 8 vezes (cerca de 2 dias). Os Proprietários recebem um aviso com o link para **Reenviar**.
- **Retido**: a conexão com o Conecta está suspensa. O aviso espera e não sai.
- **Ignorado**: o aviso não saiu porque a conexão acabou, foi substituída ou o Conecta perdeu a permissão de ver aquele dado (por exemplo, "Sem permissão para ver notas"). O motivo aparece na linha.

## Para desenvolvedores
- **Formato:** `POST` com JSON UTF-8 `{"id","type","contract":1,"occurred_at","tenant","connection_id","data":{...},"resource"}`. O `data` leva só ids, número, situação, valor e datas; nunca nome, documento, telefone, e-mail, endereço, custo, imposto ou margem. O `tenant` é o **identificador da empresa** (slug, estável), não o endereço do site.
- **`resource`:** sempre uma rota da API v1: `/api/v1/integration/orders/{id}` (pedidos), `/api/v1/integration/charges/{id do título}` (boletos: o `data.id` continua o do boleto), `/api/v1/integration/invoices/{id}` (notas) e `/api/v1/integration/connection` (teste e desconexão). Boleto cancelado, nota cancelada e nota rejeitada trazem `party_id` (nulo quando a nota foi rejeitada antes de ter destinatário).
- **Cabeçalhos:** `BemAqui-Event-Id`, `BemAqui-Event-Type`, `BemAqui-Timestamp`, `BemAqui-Signature: v1=<hex>` e `BemAqui-Connection: <connection_id>` (fora da assinatura: use para achar o segredo antes de ler o corpo); `User-Agent: BemAqui-Webhooks/1`.
- **Assinatura:** HMAC-SHA256 de `"{timestamp}.{corpo}"` com o segredo como chave (bytes UTF-8). Na troca, vão duas: `v1=<nova>,v1=<antiga>`; aceite se qualquer uma bater. Compare em tempo constante e recuse timestamp com mais de 5 minutos de diferença.
- **Use o id e consulte de novo:** o mesmo aviso pode chegar mais de uma vez e fora de ordem. Guarde o `id` para não processar duas vezes e consulte o `resource` para saber o estado atual.
- **Resposta:** responda 2xx em até 10 segundos. Redirecionamento não é seguido. Com 429 ou 503, o `Retry-After` é respeitado até 1 hora.
- **Segredo:** sai só na resposta do `/oauth/token`, nunca numa tela. Enquanto o Conecta não renovar o acesso com o `refresh_token` da resposta que levou o segredo, toda resposta leva o segredo de novo: uma resposta perdida não perde o segredo. Perdeu mesmo assim? Clique em **Trocar segredo**.

## Perguntas frequentes
**O cliente recebe a mensagem em dobro se eu reenviar?** Não. O aviso sai com o mesmo id, e o Conecta reconhece o id.

**Por quanto tempo o conteúdo fica guardado?** Por 90 dias depois de o aviso terminar. Depois fica só o registro (tipo, datas, situação e código) e o **Reenviar** fica desabilitado.
