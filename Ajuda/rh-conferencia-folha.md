---
titulo: Conferência da folha
rota: /app/rh/conferencia-folha
aba: conferencia-folha
padrao: true
modulo: hr
---

## Para que serve
Comparar, mês a mês, a folha que o sistema calcula com a folha da contadora, antes de trocar. Até a virada, quem vale é a folha lançada (a da contadora): é ela que gera os títulos a pagar e o caixa. A folha calculada é só simulação. Depois de 3 competências seguidas aceitas, com diferença zero e o termo assinado pela contadora, o Proprietário pode virar para a folha calculada.

## Como usar
- **Comparar**: com a folha calculada da competência e a folha lançada (importada da contadora), clique em **Comparar**. O sistema confere cada funcionário e cada rubrica pelo código, com tolerância zero: um centavo já é diferença.
- A tabela mostra, por funcionário, o **valor da contadora**, o **valor do sistema**, a **diferença em R$** e a **diferença %** (sobre o valor da contadora; "—" quando ele é zero), e o líquido de cada lado. **Só diferenças** esconde as linhas iguais. Clique na diferença para ver a memória de cálculo do sistema.
- No topo, as rubricas com mais diferença aparecem primeiro: é por elas que vale começar.
- **Classificar** cada diferença: "Erro do sistema", "Erro da contadora", "Regra diferente: ajustar parâmetro" ou "Arredondamento", com a justificativa. "Erro do sistema" gera o caso de correção anonimizado (sem nome, CPF, matrícula nem data de nascimento): **Baixar casos de correção** entrega o arquivo para a equipe virar caso de teste. "Regra diferente" leva a Configurações → Parâmetros → RH.
- A justificativa não aceita a competência. Corrija a causa (importe a folha corrigida da contadora ou ajuste o parâmetro e recalcule) e clique em **Comparar de novo**: só a diferença zero libera o aceite.
- **Aceitar competência**: sem diferença, baixe o **Termo de conferência**, imprima, peça para a contadora assinar, anexe o termo assinado, informe o nome e o CRC dela e clique em **Aceitar competência {mês}**. Só membros do Proprietário aceitam.
- **De-para de rubricas**: quando a contadora usa um código diferente do sistema para a mesma rubrica, cadastre a correspondência (código da contadora → rubrica do sistema) e compare de novo.
- **Virar para a folha calculada**: com 3 competências seguidas aceitas, o Proprietário vê o botão, confere o impacto em lista e confirma. A partir da competência seguinte, a folha calculada Mensal fecha como oficial; a lançada continua disponível. Férias, 13º e rescisão viram cada um depois de um caso conferido do mesmo tipo.
- **Voltar para a folha lançada**: se a contadora apontar uma divergência grave depois da virada, o Proprietário volta com o motivo (só um tipo ou todos). A contagem das 3 competências recomeça.
- **Histórico** mostra quem comparou, classificou, aceitou, virou e voltou, e quando. A justificativa só aparece para quem vê dados sensíveis do RH.

## Perguntas frequentes
**Por que a competência ficou "Reprovada"?** Porque há pelo menos uma diferença em alguma rubrica ou no líquido. Classifique, corrija a causa e compare de novo.

**Classifiquei como "Arredondamento". Posso aceitar?** Não. O aceite exige diferença zero no líquido e em cada rubrica. Ajuste o arredondamento da tabela ou peça a folha corrigida, recalcule e compare de novo.

**A virada precisa de um mês com férias ou 13º?** Não. Bastam 3 competências Mensais seguidas aceitas. Férias, 13º e rescisão continuam pela folha lançada até um caso de cada tipo ser conferido e aceito.

**O que vai para a contadora durante a conferência?** O pacote de "Exportar para a contadora" leva também o folha-calculada.csv, no mesmo layout da folha lançada, para ela comparar do lado dela.

**Quem vê os valores?** Só quem tem a permissão de ver dados sensíveis do RH. Aceitar, virar e voltar são do Proprietário.
