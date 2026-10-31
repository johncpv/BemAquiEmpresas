---
titulo: Pedido do titular
rota: /app/configuracoes/privacidade/$id
aba: privacidade-pedido
padrao: true
modulo: settings
---

## Para que serve
Atender um pedido do titular do começo ao fim: conferir quem é a pessoa, entregar os dados dela, abrir o cadastro para corrigir, registrar a resposta e concluir ou recusar. Tudo fica no histórico do pedido, com quem fez, quando e de qual IP.

## Como usar
- **Conferir identidade:** antes de entregar ou mexer em dados, confirme que quem pediu é mesmo o titular. Escolha como conferiu (documento apresentado, confirmação pelo e-mail do cadastro, pelo telefone do cadastro ou pessoalmente), a data e uma observação. Não copie o documento para o sistema. Sem a identidade conferida, **Exportar dados do titular** e **Abrir cadastro** ficam desabilitados.
- **Exportar dados do titular** (pedidos de Acesso e de Portabilidade): o sistema reúne o que cada módulo tem da pessoa (cadastro, RH, ponto, holerites, financeiro, comercial, e-mails, acessos) e monta um arquivo ZIP com o **dados.pdf** (para ler) e o **dados.json** (para levar a outro sistema), mais os documentos anexados na pasta **anexos/**. Leva alguns instantes: o aviso chega no sino quando estiver pronto. Dados de outras pessoas (o CPF de um dependente, o e-mail de uma cópia) saem mascarados.
- **Baixar exportação:** gera um link que vale **uma vez** e por **15 minutos**. Baixou de novo? Clique outra vez. O arquivo fica guardado por 30 dias depois que o pedido é concluído e então é apagado.
- **Abrir cadastro** (pedido de Correção): leva à tela do terceiro, do funcionário ou do usuário. Corrija lá; a alteração fica no histórico normal do cadastro e o pedido guarda o link do que foi aberto.
- **Concluir pedido:** informe a data da resposta, o que foi feito e se o arquivo foi enviado. O pedido fica **Concluído**.
- **Recusar:** informe o motivo (ex.: retenção legal, titular não identificado). O pedido fica **Recusado**.

## Perguntas frequentes
**Por que preciso conferir a identidade?** Para não entregar os dados de uma pessoa a outra. A conferência fica registrada com quem fez, quando e como.

**O arquivo traz a senha ou o código de verificação da pessoa?** Não. Senha, código do aplicativo autenticador, tokens e chaves nunca saem na exportação.

**A exportação falhou. E agora?** A tela mostra qual módulo não respondeu. Tente de novo em alguns minutos; se continuar, fale com o suporte.

**Quem vê o arquivo?** Só o Proprietário, pelo link de uso único. Cada exportação e cada download ficam no histórico do pedido.
