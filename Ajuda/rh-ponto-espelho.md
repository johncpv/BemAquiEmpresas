---
titulo: RH — Ponto (espelho do período)
rota: /app/rh/ponto/espelho
aba:
padrao: true
modulo: hr
---

## Para que serve
Ver o espelho de ponto de todos os funcionários no período, conferir, **fechar o período** e mandar horas conferidas para a contadora e para a folha, com o banco de horas de cada um em dia.

**Controle interno de jornada. Este registro não substitui o registro legal de ponto (REP-P, Portaria 671).**

## Como usar
- Escolha o mês em **Mês anterior** e **Mês seguinte**; o mês fica no endereço da página (`?mes=2026-10`). Ao lado aparece o **período de apuração** (por padrão o mês civil; a empresa pode usar outro corte, como de 21 a 20, em Configurações → Parâmetros → RH).
- Cada linha é um funcionário: o **trabalhado** contra o esperado pela jornada, as **extras**, as **faltas**, os **atrasos**, o **banco de horas** (no período e acumulado, só de quem tem acordo), se o funcionário já confirmou e o chip de situação (**Em dia**, pedidos pendentes, dias inconsistentes ou faltas).
- Clique no nome para abrir o espelho do funcionário, dia a dia.

## Fechar o período
- **Fechar o período {mês}** (permissão de editar do RH) abre a lista do que falta, por funcionário:
  - **Pedidos de ajuste pendentes**: bloqueiam. O atalho **Ver pedidos de ajuste** leva direto aos pedidos do funcionário.
  - **Dias inconsistentes** (marcação ímpar, fora de sequência ou intervalo menor que o da jornada): bloqueiam. O atalho **Abrir o espelho** leva ao dia; lance a marcação que faltou ou use **Marcar como revisado**, com comentário.
  - O período só fecha depois do último dia, e na ordem: feche antes o mês anterior (o saldo do banco depende dele).
- Com tudo resolvido, confirme **Fechar período**. Os totais de cada funcionário ficam congelados (esperado, trabalhado, extras, faltas, atrasos, abonado e o banco no período e acumulado) e a mensagem diz "Período {mês} fechado".
- Depois de fechar, os próximos passos oferecem **Baixar espelhos em PDF**, **Lançar a folha de {mês}** e **Gerar pacote para a contadora**.
- Com o período fechado, ninguém lança, pede nem decide ajuste nele: a resposta é "O período está fechado. Peça a reabertura ao RH." A faixa "Período fechado em {data} por {usuário}" aparece no topo.
- Duas pessoas fechando ao mesmo tempo: só uma grava; a outra vê o aviso para recarregar.

## Reabrir o período
- **Reabrir período** (permissão de aprovar do RH) pede o motivo. Os espelhos do mês voltam a aberto, junto com os meses seguintes já fechados (o saldo acumulado depende deles), as confirmações dos funcionários ficam sem efeito e os membros do Proprietário recebem o aviso.
- Se a folha do mês já estava fechada, ela mostra "O ponto deste mês foi reaberto depois do fechamento da folha."

## Espelhos em PDF
- **Baixar espelhos em PDF** (período fechado) gera um PDF por funcionário num ZIP. Com até 20 funcionários sai na hora; acima disso, o sistema avisa no sino quando terminar.
- O PDF traz o logo da empresa, nome, matrícula, cargo, o período, os dias, os totais, o banco de horas, o texto "Controle interno de jornada; não substitui o registro legal de ponto." e o campo de assinatura do funcionário. Nunca traz CPF, salário, motivo de saúde nem localização. Cada download fica registrado.

## Perguntas frequentes
**De onde vem o esperado?** Da jornada do funcionário no dia da semana. Feriado ou parada do calendário padrão da produção vale zero. O afastamento e os dias fora do vínculo (antes da admissão, depois do desligamento) não contam como falta.

**O que é a tolerância?** A variação que não conta como extra nem atraso: por padrão 5 minutos por marcação e 10 no dia. Dentro dela, o dia é Normal; acima, conta a diferença inteira. O valor está "A confirmar com a contadora" em Configurações → Parâmetros → RH.

**Como funciona o banco de horas?** Só para quem tem acordo (na aba Ponto do funcionário), a partir da data do acordo e com o banco ligado nos parâmetros. Nesses dias, a diferença do dia vai para o saldo: saldo do período = trabalhado + abonado − esperado; acumulado = o do fechamento anterior + o do período. Nenhum saldo vence sozinho: a decisão de pagar fica na folha. Sem acordo, extras e faltas saem como totais do período, para a folha.

**E a confirmação do funcionário?** No período fechado, o funcionário clica **Li e confirmo** no "Meu espelho". A coluna mostra "Confirmado em {data}" ou "Aguardando o funcionário". É uma ciência: não bloqueia nada e não é assinatura legal.
