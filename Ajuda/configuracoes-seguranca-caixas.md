---
titulo: Segurança — caixas da loja
rota: /app/configuracoes/seguranca
aba: caixas
padrao: false
modulo: settings
recurso: settings.pos_devices
---

## Para que serve
Ver e bloquear os aparelhos que vendem nos caixas da loja, ao lado dos aparelhos da fábrica.

## Como usar
- A lista de aparelhos ganha a coluna **Tipo** (Fábrica ou Caixa), com a loja e o número do caixa, e o filtro **Loja**.
- O caixa é registrado no próprio aparelho, em **/pdv**, por quem pode registrar aparelhos, com o código do autenticador.
- **Revogar aparelho** num caixa bloqueia o aparelho na hora: ele para de receber o catálogo e pede um novo registro. O que ele já tinha vendido ainda pode ser enviado e fica para revisão.
- O limite de 10 aparelhos ativos vale só para os da fábrica; cada caixa tem um aparelho por vez.

## Perguntas frequentes
**Um caixa foi roubado. O que faço?** Clique em **Revogar aparelho** na linha dele. Ele deixa de vender por aquele caixa, e o Proprietário recebe o aviso.
