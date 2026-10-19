---
titulo: Centros de trabalho
rota: /app/producao/centros-de-trabalho
aba: centros-de-trabalho
padrao: true
modulo: production
---

## Para que serve
Cadastrar os centros onde as etapas de fabricação são feitas, como Corte, Dobra e Montagem, com os turnos de cada um. As horas por dia de cada centro são a capacidade que o planejamento usa para calcular os prazos.

## Como usar
- Clique em **Novo centro de trabalho**, informe o nome e, se quiser, a linha de produção.
- Em **Turnos**, informe o início e o fim de cada turno. Use **Adicionar turno** para o turno da tarde, por exemplo. O total de horas por dia aparece enquanto você digita.
- Clique em **Salvar centro de trabalho**. O próximo centro já vem com os turnos do último.
- Depois do primeiro centro, cadastre o **Calendário** com os feriados e as paradas.

## Perguntas frequentes
**Como o sistema calcula as horas por dia?** Soma a duração dos turnos. Exemplo: 07:00 às 11:00 e 12:00 às 16:48 dão 8,80 horas.

**Posso cadastrar um turno que vira o dia, como 22:00 às 06:00?** Ainda não. Cadastre os turnos dentro do mesmo dia.

**O que acontece quando inativo um centro?** Ele some dos seletores e a capacidade dele passa a 0 no planejamento. Centro em uso por cargo, roteiro ou ordem de serviço não pode ser excluído, só inativado.
