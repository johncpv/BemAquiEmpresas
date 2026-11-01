---
titulo: RH — Espelho de ponto do funcionário
rota: /app/rh/ponto/espelho/$employeeId
aba:
padrao: true
modulo: hr
---

## Para que serve
Conferir o espelho de um funcionário no período, dia a dia: o esperado pela jornada, as marcações válidas, o trabalhado, as extras, as faltas, os atrasos, os abonos e a situação do dia, com os totais e o banco de horas no topo.

**Controle interno de jornada. Este registro não substitui o registro legal de ponto (REP-P, Portaria 671).**

## Como usar
- Troque o mês em **Mês anterior** e **Mês seguinte**; **Voltar ao espelho geral** volta à lista do período.
- O topo mostra a jornada, o **período de apuração** e os totais: esperado, trabalhado, extras, faltas (em horas e dias), atrasos e abonado. Com acordo de banco, aparece também "Banco de horas: +3 h 20 min no período, +5 h 10 min acumulado".
- Cada dia mostra as marcações (hora local), o esperado, o trabalhado (a soma dos pares entrada e saída, menos os intervalos marcados), a extra, a falta, o atraso, o abono e a situação: **Normal**, **Extra**, **Atraso**, **Falta**, **Abonado**, **Compensado do banco**, **Afastado**, **Feriado**, **Folga**, **Inconsistente** ou **Ainda não chegou**.
- No celular, os dias aparecem em cartões, com o trabalhado e a situação sempre à vista.

## Dia inconsistente
- Fica com a faixa âmbar e o motivo: "Falta a saída", "Falta a entrada", "Marcação fora de sequência" ou "Intervalo de 40 min, a jornada prevê 60". O trabalhado conta só os pares fechados.
- Resolva pelo atalho do dia: **Lançar marcação** (abre a aba Ponto do funcionário, para lançar o que faltou) ou **Ver pedidos de ajuste**.
- Se o dia está certo assim (por exemplo, o funcionário saiu mais cedo combinado), use **Marcar como revisado** e escreva o comentário (5 a 200 caracteres, sem CID nem diagnóstico). O dia passa a "Dia revisado" e deixa de bloquear o fechamento. Precisa da permissão de editar do RH.

## Abonos, afastamentos e banco
- O abono aprovado (RH → Ponto → Ajustes) aparece no dia e conta como hora abonada.
- A **Folga compensada pelo banco de horas** não conta como hora abonada: com acordo de banco, o dia é descontado do saldo e aparece como "Compensado do banco"; sem acordo, vira falta.
- Afastamento e dia depois do desligamento não são falta. O motivo do abono e do afastamento só aparece para quem tem a permissão **Ver dados de saúde**; para os demais, "Ausência justificada" e "Afastado".

## Período fechado
- A faixa "Período fechado em {data} por {usuário}" mostra quem fechou e se o funcionário já confirmou. Os dias e os totais são os congelados no fechamento.
- **Baixar espelho em PDF** gera o PDF do funcionário (sem CPF, salário, motivo de saúde nem localização), com o campo de assinatura. Cada download fica registrado.

## Perguntas frequentes
**Por que o dia aparece como Atraso e não Normal?** A diferença do dia passou da tolerância (por padrão 5 minutos por marcação e 10 no dia). Acima dela, conta a diferença inteira.

**O funcionário tem acordo de banco, mas não vejo extras.** Com o banco, a diferença do dia vai para o saldo do banco de horas, e não para extras, faltas ou atrasos. O saldo aparece no topo.

**Mudei a jornada e o espelho fechado não mudou.** O espelho fechado guarda os dias e os totais como estavam no fechamento. Para recalcular, reabra o período.
