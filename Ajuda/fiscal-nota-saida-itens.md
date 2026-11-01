---
titulo: Nota de saída — itens e tributos
rota: /app/fiscal/saida/$id
aba: itens
padrao: false
modulo: fiscal
---

## Para que serve
Ver e ajustar os itens da nota (os do romaneio) e os tributos de cada um: regra aplicada, CFOP, bases, alíquotas e valores.

## Como usar
1. A quantidade vai até o expedido no romaneio. O valor unitário vem do contrato.
2. A descrição tem até 120 caracteres e as informações do item até 500 (as séries já vêm preenchidas).
3. Em **Regra aplicada**, escolha outra entre as regras ativas que valem para o cliente e para o item, ou deixe a automática.
4. Quando a regra usa o IPI "pela TIPI do NCM", informe a alíquota da TIPI do NCM. Ela é lembrada para a próxima nota com o mesmo NCM.
5. Clique em **Salvar rascunho**: o cálculo refaz e os totais atualizam.

## Situações
- Item sem regra: a nota mostra qual operação, UF, tipo de cliente e NCM ficaram sem regra, com o link para as regras fiscais.
- **DIFAL**: venda a não contribuinte de outro estado; a emissão fica bloqueada até o Emissor Fiscal suportar.

## Perguntas frequentes
**Por que não aparece a instalação?** Serviço sai em NFS-e no aceite técnico (venda mista). A NF-e leva só o equipamento e as peças.

**O que é "Custo do projeto"?** Os tributos marcados como custo na regra (ICMS, PIS, COFINS e IBS/CBS por padrão). Com a nota autorizada em produção, eles entram no realizado do projeto.
