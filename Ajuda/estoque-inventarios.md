---
titulo: Inventários
rota: /app/estoque/inventarios
aba: inventarios
padrao: true
modulo: inventory
---

## Para que serve
Contar o estoque de um local para o saldo do sistema voltar a bater com a prateleira. A contagem é cega: quem conta não vê o saldo do sistema. A diferença vira um ajuste de estoque que outra pessoa aprova.

## Como usar
- A lista mostra o número (INV-AAAA-0001), o local, a foto do saldo, quantos itens já foram contados, o ajuste gerado e a situação. O inventário em contagem tem a faixa âmbar na lateral.
- **Novo inventário** (com a ação **Ajustar estoque** no Estoque): escolha o local (o padrão vem sugerido) e os itens. O padrão é **Itens com saldo no local**; marque **Também os zerados que já tiveram movimento** para contar o que deveria estar vazio. Categoria e tipo filtram a lista.
- Ao clicar **Abrir inventário**, o sistema tira a foto do saldo de cada item e lote e abre o inventário na aba **Contagem**.
- Enquanto o inventário está **Em contagem**, ajustes e estornos manuais no local ficam bloqueados. Entradas, saídas, requisições, recebimentos e romaneios continuam.

## Perguntas frequentes
**Apareceu "Já há um inventário aberto em …".** Cada local tem um inventário em contagem por vez. Abra o que já existe pelo número ou feche-o antes.

**Posso contar dois locais ao mesmo tempo?** Pode: abra um inventário para cada local.

**O inventário muda o saldo?** Não. Ao fechar, as diferenças viram um ajuste aguardando aprovação. O saldo só muda quando outra pessoa com a ação **Aprovar** no Estoque aprovar o ajuste.
