---
titulo: Novo terceiro
rota: /app/cadastros/terceiros/novo
aba:
padrao: true
modulo: parties
---

## Para que serve
Cadastra um cliente, fornecedor, instalador ou transportadora, já com o endereço de cobrança e o contato principal, se você tiver.

## Como usar
- Comece pelo **CPF ou CNPJ**. O sistema descobre se é pessoa física ou jurídica e, ao sair do campo, confere se o documento já está cadastrado.
- Se já estiver, use **Adicionar papel** (por exemplo, tornar um cliente também fornecedor) ou **Abrir cadastro**. Se o cadastro tiver sido excluído, use **Restaurar cadastro**.
- Marque os **papéis**. Pelo menos um é obrigatório.
- O **endereço de cobrança** e o **contato principal** são opcionais; se começar a preencher, complete os campos pedidos.
- Clique em **Salvar terceiro**. Depois de salvar, o sistema sugere os próximos passos: endereço de entrega e outros contatos.

## Consulta de CNPJ
- Ao sair do campo de um CNPJ que ainda não está cadastrado, o sistema consulta a base pública da Receita e mostra o quadro **Dados da Receita**. Também dá para clicar em **Consultar CNPJ**.
- Confira e clique em **Usar estes dados**: razão social, nome fantasia, e-mail, telefone e endereço de cobrança preenchem só os campos vazios. Nada é gravado até **Salvar terceiro**.
- CNPJ com situação diferente de Ativa mostra o aviso **Situação na Receita**, mas o cadastro continua.
- CPF não é consultado.

## Consulta de CEP
- Ao completar o CEP, logradouro, bairro, cidade e UF são preenchidos nos campos vazios. Confira o número.

## Perguntas frequentes
**Posso salvar sem CPF ou CNPJ?** Sim. O terceiro fica com o aviso "Sem documento" e não recebe nota fiscal nem boleto até o documento ser informado.

**O CNPJ com letras é aceito?** Sim. O sistema já aceita o CNPJ alfanumérico da Receita Federal.

**Por que não aparece o nome fantasia?** Pessoa física não tem nome fantasia: o campo só aparece para pessoa jurídica.

**Por que a consulta não achou meu CNPJ?** A base pública pode não ter o CNPJ (por exemplo, empresa aberta há pouco ou CNPJ com letras) ou estar fora do ar. Preencha os dados à mão: o cadastro segue normalmente.
