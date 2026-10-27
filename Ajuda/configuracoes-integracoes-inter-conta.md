---
titulo: Conta do Banco Inter
rota: /app/configuracoes/integracoes/inter/$contaId
aba: integracoes-inter-conta
padrao: true
modulo: settings
---

## Para que serve
Configurar, testar, trocar e desligar a integração de uma conta do Inter. As credenciais e o certificado ficam guardados cifrados e **nunca aparecem de novo**: a tela mostra só "configurado em … por …", o fim do client id, a validade, o titular e a impressão digital do certificado.

## Antes de começar
No Internet Banking do Inter, em **Soluções para sua empresa → Nova integração**, crie uma integração **só com cobrança, extrato e webhook**. O Inter gera o client id, o client secret e o certificado (os arquivos `.crt` e `.key`, ou um `.pfx` com senha). Credencial que pode fazer pagamentos ou enviar Pix é recusada.

## Como usar
- **Configurar integração**: escolha o **Ambiente** (comece pelo **Sandbox**), informe o client id, o client secret e a conta corrente (já vem a da conta cadastrada) e envie o certificado. Clique em **Salvar integração bancária** e confirme o código do aplicativo autenticador. O sistema confere o certificado e a credencial no Inter antes de gravar.
- **Testar conexão**: pede um acesso ao Inter e lê o saldo, sem nenhum efeito. A tela mostra "Enviado", "Aguardando" e, depois de 30 segundos, "Demorando"; no fim, "Conexão com o Inter funcionando" com os escopos, ou o motivo da recusa e o que fazer. Não precisa clicar de novo enquanto testa.
- **Trocar credenciais**: na renovação anual do certificado ou num client secret novo. Envie só o que mudou. As credenciais novas são testadas antes; se falharem, as atuais continuam valendo. O Proprietário é avisado da troca.
- **Desligar integração**: apaga as credenciais e o certificado. Os boletos já emitidos continuam no banco e são conferidos pelo extrato ou pela baixa manual.
- **Histórico de alterações**: quem mudou, quando e o quê (ambiente, conta corrente, "credenciais alteradas", certificado trocado), sem os valores.

## Ambiente
- **Sandbox**: ambiente de teste do Inter. A faixa "Ambiente de teste do banco" aparece no topo.
- **Produção**: o Inter de verdade. Antes da virada do sistema, a credencial de produção pode ser salva e testada, mas boleto, aviso de pagamento e extrato ficam bloqueados ("A cobrança real será ligada na virada do sistema."). Depois da virada, ligar a produção pede a confirmação **Ligar a cobrança real**.

## Perguntas frequentes
**Por que pede o código de novo?** Salvar, trocar e desligar mexem com a chave da conta da empresa. O sistema pede o código do aplicativo autenticador, e a confirmação vale por 15 minutos.

**O certificado vai vencer. O que faço?** O Proprietário recebe o aviso 30, 15, 7 e 1 dia antes, com o link direto para **Trocar credenciais**. Gere o certificado novo no Inter e envie aqui.

**"Faltam escopos na credencial"?** Libere os escopos que faltam na integração do Internet Banking do Inter e teste de novo.
