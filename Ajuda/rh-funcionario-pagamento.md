---
titulo: Pagamento e adicionais do funcionário
rota: /app/rh/funcionarios/$id
aba: pagamento
padrao: false
modulo: hr
---

## Para que serve
Guardar a conta onde o funcionário recebe o salário (banco e Pix), os adicionais de insalubridade e periculosidade e se ele usa vale-transporte.

## Como usar
- Clique em **Registrar conta para pagamento** (ou **Trocar conta**, se já existe uma).
- O titular já vem com o nome e o CPF do funcionário, e a chave Pix como o CPF dele. Informe o banco (código COMPE), a agência, a conta e o tipo, ou só a chave Pix.
- Clique em **Salvar conta para pagamento** e confirme com o código do aplicativo autenticador (2FA de novo).
- Em **Adicionais e vale-transporte**, escolha o grau da insalubridade, marque a periculosidade e diga se ele **Usa** ou **Não usa** vale-transporte. Clique em **Salvar adicionais**.

## Proteções da conta
- **Segunda confirmação:** se a chave Pix ou o titular não é o CPF do funcionário, o sistema mostra o que não bate e pede para marcar "Conferi com o funcionário" e informar o motivo.
- **Aviso:** toda troca avisa os membros do Proprietário pelo sino e manda um e-mail ao funcionário (no e-mail pessoal e, se ele mudou há menos de 30 dias, também no anterior), sem banco, conta nem chave. Sem e-mail pessoal, o Proprietário é avisado para falar com ele pessoalmente.
- **Quarentena:** a conta trocada fica 30 dias fora do pagamento automático da folha. O chip "Conta em quarentena até..." aparece aqui e no topo do funcionário. A primeira conta não entra em quarentena: aparece como "Novo na folha".
- **Revisar conta:** quem não fez a troca, do Proprietário ou com a permissão de aprovar no RH, clica em **Revisar conta**, confere os dados com o funcionário, informa como conferiu e clica em **Liberar conta** com o 2FA. Sem revisão, a quarentena termina sozinha no fim do prazo.

## Perguntas frequentes
**Por que os dados aparecem com asteriscos?** Agência, conta, titular e chave Pix ficam mascarados até você clicar em **Mostrar**. Cada leitura fica registrada.

**Não consigo revisar a troca que eu fiz.** Quem fez a troca não revisa. A exceção é o Proprietário único da empresa, e a revisão fica marcada como "autorrevisado".

**Pediu o código e eu não tenho 2FA.** Trocar a conta de salário exige o 2FA. Ative em Minha conta, Segurança.

**Onde fica o valor do vale-transporte?** Na folha. O cadastro guarda só se ele usa ou não.
