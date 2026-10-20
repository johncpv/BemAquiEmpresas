---
titulo: Contrato — resumo
rota: /app/contratos/$id
aba: resumo
padrao: true
modulo: projects
---

## Para que serve
Ver os dados do contrato: cliente, valor, data de assinatura, conta de receita das parcelas, orçamento de origem e projeto.

## Como usar
- **Ver projeto** abre o projeto criado na aprovação.
- O link do orçamento de origem abre a revisão que foi aprovada.
- Logo depois da aprovação aparece **Gerando parcelas no financeiro…**. As parcelas a receber nascem dos marcos de pagamento. Se demorar, o aviso muda e o sistema avisa quando ficarem prontas.
- **Parcelas e marcos:** o marco por data ou na assinatura já nasce em aberto, com vencimento. O marco por evento (fabricação concluída, entrega ou aceite técnico) nasce **previsto** e ganha vencimento quando o evento acontece: data do evento mais o prazo do marco.
- **Cancelar contrato** (Proprietário): informe o motivo. O projeto é cancelado junto. As parcelas sem baixa são canceladas no financeiro; as que já têm baixa ficam para o financeiro estornar ou manter. Projeto entregue não cancela: use um aditivo.
- O contrato fica **concluído** sozinho quando o projeto foi entregue e todas as parcelas estão pagas ou canceladas. Se uma baixa for estornada, ele volta a ativo.

## Perguntas frequentes
**Por que vejo o CPF ou CNPJ do cliente aqui e não no histórico?** O dado do cliente vem do cadastro na hora de mostrar. O contrato guarda só o id e o nome.

**A data de assinatura pode ser corrigida?** Não depois de aprovado. Ela é escolhida no diálogo da aprovação.
