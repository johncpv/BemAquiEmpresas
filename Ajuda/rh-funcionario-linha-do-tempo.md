---
titulo: Linha do tempo do funcionário
rota: /app/rh/funcionarios/$id
aba: linha-do-tempo
padrao: false
modulo: hr
---

## Para que serve
Ver os afastamentos, as trocas de cargo e os reajustes de salário do funcionário, do mais recente ao mais antigo.

## Como usar
- Cada troca de cargo e cada reajuste mostra o antes e o depois, a vigência, o motivo e quando foi registrado.
- As entradas nascem das ações **Trocar cargo**, **Reajustar salário** e **Registrar afastamento**, no topo do funcionário.

## Afastamentos
- **Registrar afastamento** pede o tipo, o início (já vem com a data de hoje), o fim previsto (o último dia afastado) e um motivo curto, opcional.
- Os tipos são: Doença, Acidente de trabalho, Maternidade, Paternidade, Serviço militar e Outros. Férias não são afastamento: ficam para a folha.
- A situação **Afastado** é calculada todo dia: aparece do início ao fim previsto e volta a **Ativo** sozinha no dia seguinte, que é a volta prevista. Desligado prevalece sobre Afastado.
- Enquanto está afastado, o funcionário não aponta horas na fábrica.
- Dois afastamentos do mesmo funcionário não podem ter dias em comum.
- **Não informe CID nem diagnóstico no motivo.** Dado de saúde é dado sensível pela LGPD. Escreva só o necessário para o RH se lembrar do caso, como "Repouso médico" ou "Licença INSS".

## Perguntas frequentes
**Por que não vejo os reajustes nem os afastamentos?** Reajustes mostram o salário e afastamentos mostram o tipo e as datas. Os dois só aparecem para quem tem a permissão Ver dados sensíveis do RH; os demais veem só a situação Afastado. As trocas de cargo aparecem para todos que veem o RH.

**O afastamento acabou antes ou vai durar mais. E agora?** Nesta versão não dá para alterar o afastamento registrado. Fale com o administrador.

**A linha do tempo substitui o histórico de alterações?** Não. A linha do tempo conta a carreira do funcionário; o histórico de alterações registra cada mudança no cadastro.