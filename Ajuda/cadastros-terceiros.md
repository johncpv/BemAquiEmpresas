---
titulo: Terceiros
rota: /app/cadastros/terceiros
aba: terceiros
padrao: true
modulo: parties
---

## Para que serve
Reúne num cadastro só os clientes, fornecedores, instaladores e transportadoras. Um mesmo CNPJ ou CPF pode ter vários papéis, então a empresa é cadastrada uma vez e usada em orçamentos, compras e financeiro.

## Como usar
- Busque pelo nome (com ou sem acento), por parte do CNPJ ou pelo CPF completo.
- Filtre pelo **Papel** e marque **Mostrar inativos** para ver também os que não aparecem mais nos seletores.
- Clique no nome para abrir o cadastro.
- Clique em **Novo terceiro** para cadastrar. Se o documento já existir, o sistema avisa e oferece **Adicionar papel**.

## Perguntas frequentes
**Por que o CPF aparece com asteriscos?** Por proteção de dados (LGPD): na lista e na busca o CPF aparece só em parte. Ele aparece completo dentro do cadastro.

**Por que a busca por parte do CPF não encontra nada?** Para evitar a busca de pessoas por pedaços do documento, o CPF só é encontrado inteiro. O CNPJ, que é público, aceita busca por parte.

**O que significa "Sem documento"?** O terceiro foi salvo sem CPF ou CNPJ. Ele pode ser usado normalmente, mas não recebe nota fiscal nem boleto até o documento ser informado.

**Os clientes do Dolibarr já estão aqui?** Depois da importação, sim: clientes e fornecedores do Dolibarr aparecem nesta lista, com a marca **Importado do Dolibarr** no detalhe. Registros do Dolibarr com o mesmo CNPJ viram um terceiro só, com todos os papéis. O que não entrou, e por quê, fica em Configurações → Importação do Dolibarr.
