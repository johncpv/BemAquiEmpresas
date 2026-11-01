---
titulo: Pedido do titular
rota: /app/configuracoes/privacidade/$id
aba: privacidade-pedido
padrao: true
modulo: settings
---

## Para que serve
Atender um pedido do titular do começo ao fim: conferir quem é a pessoa, entregar os dados dela, abrir o cadastro para corrigir, anonimizar os dados (pedidos de anonimização e de eliminação), registrar a resposta e concluir ou recusar. Tudo fica no histórico do pedido, com quem fez, quando e de qual IP.

## Como usar
- **Conferir identidade:** antes de entregar ou mexer em dados, confirme que quem pediu é mesmo o titular. Escolha como conferiu (documento apresentado, confirmação pelo e-mail do cadastro, pelo telefone do cadastro ou pessoalmente), a data e uma observação. Não copie o documento para o sistema. Sem a identidade conferida, **Exportar dados do titular** e **Abrir cadastro** ficam desabilitados.
- **Exportar dados do titular** (pedidos de Acesso e de Portabilidade): o sistema reúne o que cada módulo tem da pessoa (cadastro, RH, ponto, holerites, financeiro, comercial, e-mails, acessos) e monta um arquivo ZIP com o **dados.pdf** (para ler) e o **dados.json** (para levar a outro sistema), mais os documentos anexados na pasta **anexos/**. Leva alguns instantes: o aviso chega no sino quando estiver pronto. Dados de outras pessoas (o CPF de um dependente, o e-mail de uma cópia) saem mascarados.
- **Baixar exportação:** gera um link que vale **uma vez** e por **15 minutos**. Baixou de novo? Clique outra vez. O arquivo fica guardado por 30 dias depois que o pedido é concluído e então é apagado.
- **Abrir cadastro** (pedido de Correção): leva à tela do terceiro, do funcionário ou do usuário. Corrija lá; a alteração fica no histórico normal do cadastro e o pedido guarda o link do que foi aberto.
- **Prever anonimização** (pedidos de Anonimização e de Eliminação, com a identidade conferida): cada módulo responde o que vai acontecer, em três blocos. **Será anonimizado**: o dado é trocado pelo **marcador** "Titular anonimizado" seguido do número do pedido (texto) ou apagado (datas, documentos, telefones, e-mails, localização). **Fica retido até…**: o que a lei obriga a guardar, com o motivo e a data a partir da qual pode sair (pela tabela de **Retenção**). **Documentos mantidos**: títulos, notas, folhas, marcações de ponto e romaneios continuam existindo, com valores, datas e números, só sem o dado pessoal.
- **Anonimizar:** digite o nome do titular exatamente como aparece no pedido, informe o código do aplicativo autenticador e confirme. A anonimização roda em segundo plano, um módulo de cada vez; o aviso chega no sino com "Dados anonimizados". O terceiro fica inativo e marcado "Anonimizado", o funcionário desligado fica marcado "Anonimizado" (some dos seletores) e o usuário do sistema fica bloqueado, com o e-mail trocado. Os arquivos anexados que não estão retidos (documentos, atestados, holerites em PDF) são apagados, e fica só a linha com o marcador.
- **Tentar de novo:** se algum módulo falhar, o pedido mostra "Anonimização parcial" com os módulos feitos e o que falhou. Clique em **Tentar de novo** (com o código do aplicativo de novo): só o que falta é refeito.
- **Concluir pedido:** informe a data da resposta, o que foi feito e se o arquivo foi enviado. O pedido fica **Concluído**. Depois de anonimizar, o sistema já sugere o texto da resposta.
- **Recusar:** informe o motivo (ex.: retenção legal, titular não identificado). O pedido fica **Recusado**.

## Perguntas frequentes
**Por que preciso conferir a identidade?** Para não entregar os dados de uma pessoa a outra. A conferência fica registrada com quem fez, quando e como.

**O arquivo traz a senha ou o código de verificação da pessoa?** Não. Senha, código do aplicativo autenticador, tokens e chaves nunca saem na exportação.

**A exportação falhou. E agora?** A tela mostra qual módulo não respondeu. Tente de novo em alguns minutos; se continuar, fale com o suporte.

**Quem vê o arquivo?** Só o Proprietário, pelo link de uso único. Cada exportação e cada download ficam no histórico do pedido.

**O titular pediu eliminação. Por que o sistema anonimiza em vez de apagar?** A eliminação é atendida por anonimização: documentos com movimento (estoque, financeiro, fiscal, folha) não podem ser apagados. O dado pessoal sai e o documento fica, com o marcador no lugar do nome.

**O que fica retido?** O que tem obrigação legal: nome e CPF nas notas e títulos até 5 anos depois do fim do exercício; dados trabalhistas e previdenciários até 5 anos depois do desligamento; dados bancários até o último pagamento. Os prazos estão em Configurações → Privacidade → Retenção.

**Dá para desfazer a anonimização?** Não. A anonimização é irreversível: o dado original não fica guardado em lugar nenhum, nem na auditoria (que registra só quais campos foram anonimizados).

**E os backups?** Os backups expiram em até 30 dias e não são restaurados com dado anonimizado sem refazer a anonimização: o passo está no roteiro de restauração.

**Quem pode anonimizar?** Só o Proprietário, com a verificação em duas etapas ativa. Ninguém anonimiza o próprio usuário, e o último membro ativo do Proprietário não pode ser anonimizado.
