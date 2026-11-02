---
titulo: Rescisão do funcionário
rota: /app/rh/funcionarios/$id
aba: rescisao
padrao: false
modulo: hr
---

## Para que serve
Saber quanto custa um desligamento antes de decidir e, depois do desligamento registrado, calcular a rescisão com a memória de cada verba, o líquido, o FGTS, a multa do FGTS e o prazo do pagamento. As regras (tipos de desligamento e as verbas de cada um, o aviso prévio proporcional, a multa e o prazo) vêm pré-configuradas pela CLT, "A confirmar com a contadora" e com o jurídico, em Configurações → Parâmetros → RH. O termo oficial e as guias são da contadora: o sistema calcula para conferência.

## Como usar
- **Simular rescisão** (funcionário ativo): escolha o tipo do desligamento, a data prevista (vem com hoje) e o aviso prévio (vem com o mais comum do tipo). Informe o saldo do FGTS do extrato para ver a multa. A tela mostra as verbas e os descontos com a memória, o líquido, o FGTS, a multa e o **custo total para a empresa**. A simulação não grava nada.
- **Registrar desligamento** fica no topo da tela do funcionário. Depois dele, o próximo passo é **Calcular rescisão**.
- **Calcular rescisão** pede o espelho de ponto do mês do desligamento fechado e, nos tipos com multa, o **saldo do FGTS do extrato** com a data dele. Nasce a folha de rescisão do funcionário, com o saldo de salário, o aviso indenizado, as férias vencidas e proporcionais com 1/3, o 13º proporcional (menos o que já foi pago), o saldo do banco de horas, os descontos, o INSS e o IRRF.
- **Gerar termo para conferência** baixa o PDF com as verbas, os descontos e o líquido, com a marca "Documento de conferência interna: o termo oficial é emitido pela contadora.".
- **Ver folha de rescisão** abre a folha, com o holerite, a memória e o histórico do cálculo. Avulsos (adiantamentos, outros descontos) entram por lá, com o motivo, e a rescisão é recalculada.
- **Fechar** a folha de rescisão pede a permissão de aprovar a folha e o código do autenticador de novo. O diálogo mostra o líquido, as guias, a multa do FGTS e o prazo do pagamento; nascem o título do funcionário e as guias do FGTS rescisório e da multa com esse vencimento, e as do INSS e do IRRF no vencimento das guias.
- **Estornar desligamento**: o funcionário volta antes do pagamento. Com a folha de rescisão fechada, reabra a folha antes.

## Perguntas frequentes
**Quais verbas entram em cada tipo?** Sem justa causa: saldo, aviso, férias vencidas e proporcionais com 1/3, 13º proporcional e multa de 40%. Pedido de demissão: o mesmo sem o aviso pago e sem multa, com o desconto do aviso não cumprido. Justa causa: saldo e férias vencidas com 1/3. Acordo (art. 484-A): metade do aviso indenizado e multa de 20%. Término da experiência no prazo: saldo, férias proporcionais com 1/3 e 13º proporcional; antecipado pela empresa, mais a metade dos dias restantes e a multa de 40%; pelo empregado, o desconto de até a metade dos dias restantes.

**Como sai o aviso prévio?** 30 dias mais 3 por ano completo de casa, até 90 (Lei 12.506/2011). Indenizado, ele conta como tempo de serviço: os dias projetados entram nos avos de férias e de 13º.

**De onde vem a multa do FGTS?** Do saldo do extrato que o RH digita, mais os depósitos desta rescisão (o FGTS do mês e o rescisório), vezes o percentual da matriz. A multa é da empresa: não sai do líquido.

**Por que a rescisão não fecha?** Até a virada da folha calculada e o primeiro caso de rescisão conferido com a contadora, a rescisão calculada é simulação: "Rescisão calculada em conferência: feche a rescisão lançada." O termo sai com a marca "Simulação".

**E o prazo?** 10 dias corridos do desligamento (CLT art. 477 §6º). Fechar com o pagamento depois do prazo avisa "Pagamento depois do prazo legal: há multa. Confirme com a contadora.", sem bloquear.

**E o banco de horas?** O saldo positivo é pago como horas extras de dia útil; o negativo só é descontado quando o acordo de banco de horas permite (parâmetro "Banco de horas na rescisão").

**O que acontece depois de fechar?** Os períodos de férias e o banco de horas do funcionário ficam encerrados, e a folha mensal calculada da competência não inclui mais o funcionário.
