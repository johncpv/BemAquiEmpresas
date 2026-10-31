---
titulo: Regras fiscais
rota: /app/fiscal/regras
aba: regras
padrao: true
modulo: fiscal
---

## Para que serve
Cada operação de saída (venda de produção própria, revenda de peça, prestação de serviço e as outras) tem uma **regra fiscal** com o CFOP, os CST, as alíquotas de ICMS, IPI, PIS, COFINS, ISS e IBS/CBS, as condições (UF de destino, tipo de cliente, NCM) e a vigência. A nota sai com a regra **ativa** que vale para o cliente e para o item, sem digitar CFOP nem alíquota a cada vez.

As regras já vêm **montadas com os valores propostos** de Configurações → Fiscal, em rascunho e com o selo **A confirmar com a contadora**: venda de produção própria a contribuinte (5101/6101), a não contribuinte (5101/6107, com DIFAL), revenda de peça (5102/6102) e a prestação de serviço da instalação (ISS de 5%, item 7.02).

## Como usar
1. Clique em **Simular**: o painel lateral monta uma nota com o cliente e os itens que você informar e mostra a regra de cada item, as bases, os impostos, o total e o que entra no custo do projeto. Nada é gravado.
2. Abra a regra, confira os valores com a contadora e ajuste o que ela pedir.
3. Quem tem permissão de ativar (o Proprietário) clica em **Ativar regra** e em **Marcar como conferida**. Quem editou a regra não a ativa: outra pessoa ativa.
4. Use os filtros **A confirmar** (regras ainda não conferidas) e **A revisar** (os valores padrão de Configurações → Fiscal mudaram depois da regra).
5. Para remessa e retorno de obra, devolução ou outra operação, clique em **Nova regra fiscal**.

## Situações
- **Rascunho**: ainda não vale para a nota. **Ativa**: vale para as próximas notas. **Inativa**: deixou de valer (o motivo fica na regra).
- **DIFAL bloqueado**: a venda a não contribuinte de outro estado mostra o DIFAL no cálculo, mas a emissão fica bloqueada até o Emissor Fiscal suportar.

## Perguntas frequentes
**E se não houver regra para a venda?** A nota não é emitida e a tela diz qual operação, UF, tipo de cliente e NCM ficaram sem regra. O sistema nunca usa um padrão escondido.

**Em homologação preciso conferir?** Não: a regra ativa não conferida emite com o aviso. Em produção, só regra ativa **e** conferida.

**Quem pode mudar?** Ver e simular: quem vê o Fiscal. Cadastrar e editar: quem edita o Fiscal. Ativar, inativar e marcar como conferida: o Proprietário.
