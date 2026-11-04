---
titulo: Funcionários
rota: /app/rh/funcionarios
aba: funcionarios
padrao: true
modulo: hr
---

## Para que serve
Ver quem trabalha na empresa, em qual cargo e centro de trabalho, e abrir o cadastro de cada funcionário. É a base do ponto, do apontamento de horas e da folha.

## Como usar
- Busque pelo nome ou pela matrícula. Filtre por situação, cargo ou centro de trabalho.
- Clique no nome para abrir o funcionário.
- Clique em **Novo funcionário** para cadastrar. O botão só aparece para quem pode ver dados sensíveis, porque o CPF e o salário são obrigatórios.

## Imprimir e exportar
- **Exportar → Excel (.xlsx)**, na barra da lista, baixa os funcionários com a busca e os filtros da tela (todas as páginas): matrícula, nome, CPF, cargo, admissão, situação, jornada, sindicato e salário. O arquivo se chama funcionarios-{data}.xlsx, sem nome nem CPF de ninguém.
- **CPF mascarado:** sem a permissão Ver dados sensíveis, o CPF sai como 123.***.***-45 (os 3 primeiros e os 2 últimos dígitos, como no holerite) e o salário sai como ***. O menu avisa: "CPF e salário saem mascarados." Com a permissão, saem completos.
- A situação sai como na tela (Ativo, Afastado, Desligado). Com Ver dados sensíveis, o afastado sai com as datas; o tipo e o motivo do afastamento só saem para quem tem também Ver dados de saúde.
- Toda exportação fica na auditoria (quem, quando, quantas linhas e os filtros), marcando quando o arquivo levou CPF ou salário completos.

## Perguntas frequentes
**Por que a lista não mostra CPF nem salário?** São dados pessoais protegidos pela LGPD. Eles ficam nas abas Dados pessoais e Contrato e salário, só para quem tem a permissão Ver dados sensíveis.

**Dá para buscar pelo CPF?** Sim, com a permissão Ver dados sensíveis: digite o CPF completo, com 11 dígitos. Parte do CPF não encontra.

**Onde estão os desligados?** Filtre a situação por Desligado. Eles continuam no cadastro, com todo o histórico.

**O que é a situação Afastado?** O funcionário tem um afastamento vigente hoje (doença, maternidade e outros). A situação volta a Ativo sozinha depois do fim previsto. O tipo e o motivo só aparecem para quem tem a permissão Ver dados sensíveis.
