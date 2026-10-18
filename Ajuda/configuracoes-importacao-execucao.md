---
titulo: Importação do Dolibarr — Execução
rota: /app/configuracoes/importacao/$id
aba: execucao
padrao: true
modulo: settings
---

## Para que serve
Ver uma execução da importação: situação, operador, quando o snapshot do Dolibarr foi extraído, os totais por entidade e as pendências.

## Como usar
- Os **totais por entidade** somam exatamente o que foi lido do Dolibarr.
- Em **Pendências**, filtre por entidade ou por tipo (erro, conflito, aviso). O filtro fica no endereço da página, para compartilhar.
- **Abrir registro** leva ao cadastro no BemAqui, quando ele existe.

## Perguntas frequentes
**O que faço com um conflito?** Compare os dois valores. Se o certo é o do BemAqui, não precisa fazer nada. Se é o do Dolibarr, corrija o cadastro no BemAqui.

**O que faço com um erro?** Corrija o dado no Dolibarr e peça uma nova execução, ou cadastre direto no BemAqui.

**Por que alguns valores aparecem com "***"?** CPF, e-mail e telefone aparecem mascarados (LGPD).
