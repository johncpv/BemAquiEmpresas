---
titulo: Orçamentos
rota: /app/orcamentos
aba:
padrao: true
modulo: quotes
---

## Para que serve
Montar a pré-proposta para o cliente, item a item, com o custo estimado e o preço. O orçamento não lança nada no financeiro nem no estoque.

## Como usar
- Clique em **Novo orçamento**, escolha o cliente e clique em **Criar orçamento**.
- A lista mostra o número, o cliente, a situação, a validade e o preço. Quem tem a permissão **Ver custo e margem** vê também o custo e a margem.
- **Copiar de…** cria um orçamento novo a partir de outro, com itens, custos, preços e marcos. Nada é digitado de novo.
- No orçamento enviado, **Registrar perda** guarda o motivo (preço, prazo, concorrente, desistência, sem retorno ou outro).
- Filtre por situação (rascunho, enviado, perdido, expirado ou aprovado) e busque pelo número ou pelo cliente.
- A aba **Funil** mostra os orçamentos por situação, a taxa de conversão, os motivos de perda e os que vencem nos próximos 7 dias. Ao clicar numa situação no funil, a lista abre filtrada pelo período e pelo autor; **Limpar** tira esse filtro.

## Imprimir e exportar
- Clique em **Exportar** → **Excel (.xlsx)** para baixar a lista com a busca, a situação e o filtro do funil atuais, com todas as páginas.
- A planilha traz número, revisão, cliente, situação, validade e preço. **Custo** e **Margem** só saem para quem tem a permissão **Ver custo e margem**; sem ela, as colunas não aparecem no arquivo.
- A lista não tem PDF. O limite é de 10.000 linhas; acima disso, o sistema pede para refinar os filtros. Com a lista vazia, **Exportar** fica desabilitado.
- Toda exportação fica registrada na auditoria (quem, quando, quantas linhas e os filtros).

## Perguntas frequentes
**Por que não vejo o custo?** Custo e margem são sigilo comercial. Só quem tem a permissão **Ver custo e margem** no grupo vê.

**Como o orçamento vira contrato?** No orçamento enviado, clique em **Aprovar e gerar contrato** (na lista ou no orçamento). Só quem tem a permissão de aprovar vê o botão. O sistema cria o contrato, o projeto e as parcelas a receber.
