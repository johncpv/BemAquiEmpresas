---
titulo: Retenção dos dados pessoais
rota: /app/configuracoes/privacidade/retencao
aba: privacidade-retencao
padrao: true
modulo: settings
---

## Para que serve
Dizer, para cada tipo de dado pessoal, por quanto tempo a empresa precisa guardá-lo antes de poder anonimizar a pedido do titular. É esta tabela que a anonimização consulta para decidir o que sai agora e o que fica **retido** por obrigação legal (fiscal, contábil, trabalhista e previdenciária). Só o Proprietário vê e altera.

## Como usar
- A tabela já vem **pré-configurada** pela regra da empresa (ADR 0018), separada por tipo de titular: terceiro pessoa física, contato, funcionário e usuário do sistema.
- Cada linha diz o grupo de dados, a regra e a **data base**:
  - **A qualquer momento:** telefone, e-mail pessoal, observações, localização do ponto e ciência da geolocalização, dependentes sem obrigação legal (fora do IR e do salário-família), nome e e-mail do usuário do sistema.
  - **Depois do último pagamento:** dados bancários e chave Pix (conta de salário e forma de pagamento dos títulos).
  - **5 anos depois do desligamento:** endereço do funcionário e os dados trabalhistas e previdenciários (nome, CPF, documentos, ponto, ajustes, afastamentos, holerites e anexos).
  - **5 anos depois do fim do exercício:** nome, CPF e endereço do terceiro nas notas, títulos e demais documentos com movimento (conta a partir de 31/12 do ano do último documento).
- **Editar:** escolha "A qualquer momento" ou "Depois de N anos" (0 a 30) e salve. A data base não muda: é a do grupo. Toda mudança fica no histórico, com o valor de antes e o de depois.
- **Marcar como conferido:** quando a contadora e o jurídico confirmarem a linha, clique em **Marcar como conferido**. Fica registrado quem conferiu e quando. Se a regra for alterada depois, a linha volta para "A confirmar".

## Perguntas frequentes
**Por que a linha aparece "A confirmar com a contadora"?** Os prazos vêm pré-configurados por uma regra geral. A contadora e o jurídico precisam confirmar se valem para a sua empresa (por exemplo, documentos que a contadora pede para guardar por mais tempo).

**O que acontece com um dado retido?** Ele continua no sistema até a data indicada na prévia da anonimização ("Retido até …"). Depois disso, um novo pedido do titular pode anonimizá-lo.

**Funcionário ainda ativo pode ser anonimizado?** Só no que não tem obrigação legal (telefone, e-mail, localização do ponto, dependentes fora do IR). Sem desligamento, nada trabalhista nem bancário é anonimizado.

**A anonimização acontece sozinha quando o prazo termina?** Não. Por enquanto ela só acontece a pedido do titular, em Configurações → Privacidade.
