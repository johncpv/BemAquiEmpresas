---
titulo: Importação do Dolibarr
rota: /app/configuracoes/importacao
aba: importacao
padrao: true
modulo: settings
---

## Para que serve
Acompanhar a importação dos cadastros do Dolibarr: cada execução, o que entrou, o que ficou de fora e por quê. A tela é só de consulta. A carga é feita pela equipe técnica, no servidor, com o passo a passo do runbook da migração.

## Como usar
- Cada linha é uma execução. **Simulação** roda tudo e desfaz no fim, sem gravar nada nos cadastros. **Real** grava.
- Clique na data para ver os totais por entidade e as pendências.
- A importação pode rodar de novo quantas vezes for preciso: o que já entrou não é duplicado.

## Como ler o relatório
- **Lidos:** linhas lidas do Dolibarr. A soma das outras colunas, sem contar os avisos, fecha com esse total.
- **Importados:** cadastros novos no BemAqui. **Atualizados:** mudaram no Dolibarr e ninguém mexeu neles no BemAqui.
- **Sem alteração:** já tinham entrado e não mudaram. **Vinculados** e **Mesclados:** ligados a um cadastro que já existia, sem criar outro.
- **Ignorados:** ficaram de fora de propósito, com o motivo (ex.: outra empresa do Dolibarr).
- **Conflitos:** o cadastro foi editado no BemAqui depois da importação e também mudou no Dolibarr. O BemAqui não é sobrescrito; a pendência mostra os dois valores para você decidir.
- **Erros:** o registro não entrou (ex.: CNPJ inválido). Corrija no Dolibarr e rode de novo, ou cadastre direto no BemAqui.

## O que não migra
- Lançamentos e financeiro: títulos a receber e a pagar, pagamentos, recebimentos, movimentos e saldos bancários, saldos e movimentos de estoque.
- Orçamentos, pedidos, OS, projetos, usuários, plano contábil, preços, aniversário dos contatos e anexos.
- Tudo isso continua para consulta no Dolibarr (adm.konebras.com.br) até o desligamento.

## Perguntas frequentes
**Por que não tem botão para importar?** Por segurança, a carga só roda no servidor, por quem tem acesso a ele. Aqui você confere o resultado.

**Por que as pendências sumiram de uma execução?** Os dados lidos do Dolibarr ficam guardados só até o aceite da carga ou por 30 dias (LGPD). Depois disso ficam os totais.
