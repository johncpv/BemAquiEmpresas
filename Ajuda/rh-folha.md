---
titulo: Folha
rota: /app/rh/folha
aba: folha
padrao: true
modulo: hr
---

## Para que serve
Ter a folha do mês no sistema: os proventos e descontos que a contadora calculou, lançados à mão ou importados, conferidos antes de virar holerite e pagamento. Há dois modos: a **folha lançada** (os valores vêm da contadora; o sistema soma e confere) e a **folha calculada** (o sistema calcula a Mensal pelo cadastro, pelo ponto fechado e pelas tabelas legais da competência, com a memória de cada valor). Até a virada, a calculada é só **simulação**: roda em paralelo com a lançada, que continua a oficial.

## Como usar
- Clique em **Nova folha**, escolha a **competência** (mês e ano) e o **tipo**: Mensal, Adiantamento, Férias, 13º 1ª parcela, 13º 2ª parcela ou Rescisão. Em Férias e Rescisão, marque os funcionários.
- Na Mensal e no 13º, escolha o **modo**: **Lançada** (valores da contadora) ou **Calculada pelo sistema**. Férias, rescisão e adiantamento por enquanto são só lançados.
- **13º salário:** escolha "13º 1ª parcela" ou "13º 2ª parcela" e o modo **Calculada**. A competência vem preenchida com o mês da data limite (novembro e dezembro) e a **prévia dos avos** mostra, por funcionário, quantos meses contam no ano e quais não contam. Na 2ª parcela sem a 1ª fechada, marque "Calcular a 2ª parcela sem adiantamento".
- Clique em **Criar folha**. Ela nasce "Em lançamento", com um holerite por funcionário ativo na competência (admitido até o último dia do mês e não desligado antes do primeiro; o afastado entra).
- Na Mensal, cada holerite já vem com a linha **Salário** (o salário vigente no mês) e a referência "30 dias".
- Abra a folha para lançar ou importar os valores, informar os encargos da empresa e conferir. Na **folha calculada**, abra e clique em **Calcular** (o ponto do mês precisa estar fechado).
- A lista marca a calculada com **calculada** e o chip **Simulação**, e a retificação com **retificação**.

## Perguntas frequentes
**Posso criar duas folhas do mesmo tipo no mesmo mês?** Não, no mesmo modo. O sistema avisa "Já existe a folha {tipo} de {mês}." Abra a que já existe. A calculada (simulação) convive com a lançada do mesmo mês, para comparar.

**O que é a simulação?** Até a virada (a conferência paralela de três meses com a contadora), a folha calculada não fecha, não gera título e o holerite sai só como prévia, com a marca "Simulação: não é o holerite oficial". A folha lançada continua a oficial.

**Por que não vejo os valores?** Os valores da folha são dados pessoais (LGPD). Sem a permissão "Ver dados sensíveis" do RH, a lista mostra só a competência, o tipo, a situação e o número de holerites.

**Como o sistema calcula o 13º?** Cada mês com 15 dias ou mais trabalhados no ano é um **avo** (1/12); faltas injustificadas do espelho fechado e dias pagos pela previdência saem dos dias trabalhados. A **1ª parcela** (até 30/11) é a metade da remuneração × avos do ano ÷ 12 (os meses seguintes à parcela contam como trabalhados: quem está no ano inteiro recebe a metade da remuneração), sem INSS e IRRF e com FGTS, menos o adiantamento já pago com as férias. A **2ª parcela** (até 20/12) é a remuneração de dezembro × avos ÷ 12, mais a média das horas extras, do adicional noturno e do DSR das folhas Mensais fechadas, menos o adiantamento; o INSS e o IRRF são calculados sobre o 13º inteiro, separados do salário do mês. As regras são parâmetros "A confirmar com a contadora" em Configurações → Parâmetros → RH.

**O 13º calculado já é oficial?** Não. Até a virada e o primeiro 13º conferido com a contadora, é simulação: "Fechar" avisa "13º calculado em conferência: feche a folha de 13º lançada."

**E as horas extras, faltas e atrasos?** Entram na referência das rubricas (ex.: "10:00 h"), lançadas à mão ou pelo arquivo. Quando o ponto existir no sistema, o espelho fechado aparece ao lado com "Usar horas do espelho".
