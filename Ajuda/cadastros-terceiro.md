---
titulo: Terceiro — Dados
rota: /app/cadastros/terceiros/$id
aba: dados
padrao: true
modulo: parties
---

## Para que serve
Mostra e altera os dados do terceiro: documento, nomes, papéis, dados fiscais, e-mail, telefone e observações. Na barra de cima ficam as ações de inativar, excluir e salvar.

## Como usar
- Altere o que precisar e clique em **Salvar terceiro**.
- Para tirar o terceiro dos seletores sem perder o histórico, use **Inativar terceiro**. **Reativar** desfaz.
- **Excluir terceiro** só funciona quando ele não está em uso. Se estiver, o sistema oferece **Inativar**.
- Sem a permissão de editar, os campos ficam só para leitura.

## Condição de pagamento padrão
- Escolha a condição que costuma valer para este terceiro. Ela vem preenchida nos orçamentos, pedidos e títulos dele, e dá para trocar em cada documento.
- Só aparecem condições ativas. Se a condição escolhida for inativada depois, ela continua no terceiro, com o chip **Inativa**.

## Perguntas frequentes
**Qual é a diferença entre inativar e excluir?** Inativar esconde o terceiro dos seletores, mas ele continua nos documentos antigos e pode voltar com Reativar. Excluir é para cadastro feito por engano, que nunca foi usado.

**O que é o regime tributário?** É a forma como o terceiro paga impostos (Simples Nacional, Lucro Presumido etc.). Ele influencia notas e custos; na dúvida, confira com a contadora.

**Quem vê as alterações?** Toda alteração fica na aba **Histórico de alterações**, com quem fez e quando. Documento, e-mail e telefone aparecem mascarados no histórico.

**O que é "Importado do Dolibarr"?** O terceiro veio da importação. Ao lado aparece a referência de origem (ex.: llx_societe:42), para conferir no Dolibarr enquanto ele estiver no ar.
