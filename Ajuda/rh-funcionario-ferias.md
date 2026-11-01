---
titulo: Férias do funcionário
rota: /app/rh/funcionarios/$id
aba: ferias
padrao: false
modulo: hr
---

## Para que serve
Ver os períodos aquisitivos do funcionário com o saldo de dias, programar as férias com o aviso, o fracionamento e o abono pecuniário, e calcular a folha de férias com o recibo. As regras vêm pré-configuradas pela CLT, "A confirmar com a contadora", em Configurações → Parâmetros → RH.

## Como usar
- **Períodos aquisitivos** mostra, desde a admissão, cada período de 12 meses com os dias de direito, os gozados, os vendidos, o saldo e o fim do período concessivo. O chip diz **Em aquisição**, **A gozar**, **Vence em {data}** (60 dias antes do fim do concessivo) ou **Vencido**.
- Os dias de direito seguem as faltas injustificadas dos espelhos fechados: 30 dias até 5 faltas, 24 de 6 a 14, 18 de 15 a 23, 12 de 24 a 32 e nenhum acima de 32.
- Afastamento pela previdência por mais de 6 meses (contínuos ou não) ou licença remunerada por mais de 30 dias encerram o período, e um novo começa no retorno. A memória do período diz "Período reiniciado em {data}: {motivo}".
- Clique em **Programar férias**. O diálogo já vem com o período, o início sugerido (o primeiro dia depois do aviso de 30 dias que não fica nos 2 dias antes de feriado ou domingo) e os dias com o saldo. Informe o **abono pecuniário** (até 1/3 dos dias de direito) e marque o **adiantamento do 13º** se o funcionário pediu. O diálogo mostra o fim, o retorno e até quando o pagamento vence.
- O sistema recusa o que fere o fracionamento (até 3 períodos, um com pelo menos 14 dias e os demais com pelo menos 5), o abono acima de 1/3 e o início nos 2 dias antes de feriado ou do repouso semanal, com a mensagem e o número do parâmetro.
- Programadas, as férias aparecem como **Férias** no espelho de ponto e no "Meu ponto" do funcionário. O próximo passo é **Gerar aviso de férias** (o PDF para o funcionário assinar). Se o início fica a menos de 30 dias, a tela avisa.
- **Calcular férias** (com a permissão de dados sensíveis) gera a folha de férias calculada: remuneração dos dias (salário e adicionais fixos), a média das variáveis dos 12 meses do período, o 1/3, o abono e o seu 1/3, o INSS (rateado pelos meses do gozo) e o IRRF (em separado no pagamento). **Baixar prévia do recibo** baixa o recibo em PDF.
- **Cancelar programação** pede o motivo: o saldo volta e os dias saem do espelho. Com a folha de férias fechada, reabra a folha antes.

## Perguntas frequentes
**O que é o abono pecuniário?** É a venda de até 1/3 dos dias de férias: o funcionário trabalha esses dias e recebe o valor deles com o 1/3. O abono e o seu 1/3 não têm INSS, FGTS nem IRRF.

**Por que a folha de férias não fecha?** Até a virada da folha calculada e o primeiro caso de férias conferido com a contadora, a folha de férias calculada é simulação: "Férias calculadas em conferência: feche a folha de férias lançada." A prévia do recibo sai com a marca "Simulação".

**Quando vence o pagamento?** Até 2 dias antes do início do gozo (o dia não útil antecipa). Fechar a folha de férias depois do prazo avisa "Pagamento depois do prazo legal: confirme com a contadora.", sem bloquear.

**E a folha mensal do mês das férias?** Os dias de férias não entram como salário de novo. A parte das férias do mês entra na base do INSS e do FGTS junto com o salário, e o INSS já descontado no recibo de férias é compensado.

**E o adiantamento do 13º?** Fica marcado na programação e entra na folha do 13º (1ª parcela).
