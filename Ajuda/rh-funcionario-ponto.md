---
titulo: Ponto do funcionário
rota: /app/rh/funcionarios/$id
aba: ponto
padrao: false
modulo: hr
---

## Para que serve
Ver as marcações de ponto deste funcionário por dia e lançar os ajustes: a marcação que ele não conseguiu fazer no app, o dia inteiro pela jornada e o abono de uma ausência.

**Controle interno de jornada; não substitui o registro legal de ponto (REP-P, Portaria 671).**

## Como usar
- Escolha o período em **De** e **Até** (os últimos 7 dias vêm escolhidos).
- Cada dia mostra a hora, o tipo e a origem de cada marcação (**App**, **PIN no** tablet ou **Manual**). A marcação desconsiderada aparece riscada, com "Desconsiderada por {aprovador} em {data}: {motivo}".
- **Sem localização** marca a marcação feita com a localização ligada, mas sem a localização do aparelho.
- A localização só aparece em **Ver localização**, para quem tem a permissão de dados sensíveis do RH. Cada leitura fica registrada.


## Acordo de banco de horas e espelho
- **Acordo de banco de horas** mostra se o funcionário tem acordo e desde quando (só para quem tem a permissão de dados sensíveis do RH). **Alterar acordo** (com a permissão de editar) marca "Tem acordo de banco de horas" com a data do acordo: o banco vale a partir dela, só em período em aberto. Sem acordo, extras e faltas saem como totais do período para a folha. Confira o modelo do acordo com o jurídico.
- **Ver o espelho do mês** abre o espelho de ponto do funcionário (RH → Ponto → Espelho).
## Ajustes do ponto
- **Lançar marcação**: informe a data, a hora, o tipo e a justificativa (10 a 200 caracteres, sem CID nem diagnóstico) e clique em **Lançar marcação**. A mensagem diz "Marcação enviada para aprovação": o lançamento do RH também passa por aprovação.
- **Lançar o dia pela jornada**: escolha o dia (vem o último dia útil) e o diálogo já traz as quatro marcações da jornada do funcionário. Ajuste o que for diferente e escreva uma justificativa só; cada marcação vira um pedido.
- **Abonar ausência**: escolha o dia, o dia inteiro ou as horas, o motivo da lista de ausências (a mesma dos afastamentos) e a justificativa. O documento (atestado, declaração) é opcional e só aparece para quem tem a permissão **Ver dados de saúde**.
- Os pedidos aparecem abaixo com a situação: **Aguardando aprovação**, **Aprovado**, **Reprovado** ou **Cancelado**, e a marca **autoaprovado** quando o Proprietário único aprovou o próprio ajuste. **Ver pedido** abre o pedido em RH → Ponto → Ajustes para aprovar.
- Não dá para lançar no futuro nem antes do período mais antigo em aberto.

## Perguntas frequentes
**O funcionário não tem nenhuma marcação.** Confira se o usuário dele está ligado ao cadastro (Dados gerais) e se ele marca em "Meu ponto".

**Posso corrigir uma marcação aqui?** Não diretamente. A marcação não é editada nem apagada; o ajuste aprovado desconsidera a original (ela continua guardada, só deixa de contar) e cria outra.
