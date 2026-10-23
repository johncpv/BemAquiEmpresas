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
- Cada afastamento mostra a situação dele: **Previsto** (ainda não começou), **Vigente**, **Encerrado** ou **Cancelado**.

## Corrigir, encerrar antes ou cancelar
- **Editar** corrige o tipo, as datas e o motivo de um afastamento previsto ou vigente. No vigente, o início fica hoje ou antes e o fim previsto, hoje ou depois. Confirme em **Salvar afastamento**: o diálogo mostra o antes e o depois.
- **Encerrar afastamento** serve para quando o funcionário voltou antes do fim previsto. Informe a data da volta, que já vem com hoje: ela fica depois do início, até o fim previsto e nunca depois de hoje. O último dia afastado passa a ser a véspera da volta e a situação volta a **Ativo** na hora.
- **Cancelar afastamento** vale só para o afastamento que ainda não começou, como um lançamento em duplicidade. Informe o motivo. O afastamento continua na linha do tempo como **Cancelado** e deixa de contar na situação. Afastamento que já começou não se cancela: encerre.
- Afastamento encerrado ou cancelado não muda mais.
- Cada mudança fica embaixo do afastamento, com o antes e o depois e quando foi feita. O histórico de alterações também registra, sem mostrar o tipo nem o motivo.
- Se outra pessoa mudou o funcionário ao mesmo tempo, o sistema avisa e oferece **Recarregar**.

## Doença e acidente de trabalho
- Doença e Acidente de trabalho são dado de saúde. O tipo e o motivo desses afastamentos só aparecem para quem tem a permissão **Ver dados de saúde** do RH.
- Quem não tem a permissão vê só **Afastado** e as datas. Pode encerrar e cancelar, mas não registra nem edita afastamento de saúde, e esses tipos não aparecem na lista.

## Perguntas frequentes
**Por que não vejo os reajustes nem os afastamentos?** Reajustes mostram o salário e afastamentos mostram o tipo e as datas. Os dois só aparecem para quem tem a permissão Ver dados sensíveis do RH; os demais veem só a situação Afastado. O tipo Doença ou Acidente de trabalho pede também a permissão Ver dados de saúde. As trocas de cargo aparecem para todos que veem o RH.

**O afastamento acabou antes ou vai durar mais. E agora?** Se acabou antes, use **Encerrar afastamento** com a data da volta. Se vai durar mais, use **Editar** e mude o fim previsto.

**Lancei um afastamento errado. Posso excluir?** Se ele ainda não começou, use **Cancelar afastamento**, com o motivo. Se já começou, corrija com **Editar** ou encerre com **Encerrar afastamento**.

**A linha do tempo substitui o histórico de alterações?** Não. A linha do tempo conta a carreira do funcionário; o histórico de alterações registra cada mudança no cadastro.