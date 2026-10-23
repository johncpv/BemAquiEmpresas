---
titulo: Projeto — custos
rota: /app/projetos/$id
aba: custos
padrao: false
modulo: projects
---

## Para que serve
Comparar o que o orçamento aprovado previu gastar com o que já foi gasto, o que já está contratado e quanto o projeto deve custar no fim, em cada natureza de custo.

## Como usar
- Cada natureza (material, mão de obra, instalação, frete, impostos e outros) mostra **Orçado**, **Realizado**, **Comprometido**, **Projetado** e **Desvio**. No celular, **Comprometido** e **Desvio** ficam em **Comprometido e desvio**, dentro do cartão.
- **Realizado:** o que já foi gasto. Entram os títulos a pagar **confirmados** com o projeto (instalador, frete, outros), as **horas apontadas** na produção (horas × custo-hora do cargo) e o **material baixado** para as OS pelas requisições, menos as devoluções. Material comprado para o projeto só conta como realizado quando é baixado para a OS.
- **Comprometido:** o que já está contratado, mas ainda não foi gasto. Entram o título **previsto** (por exemplo, o previsto do instalador quando a OS de instalação nasce) e o material comprado para o projeto que ainda não foi baixado para a OS.
- **Projetado:** o realizado mais o maior entre o que ainda falta do orçado e o comprometido. Se o comprometido passa do que falta do orçado, o projetado já mostra o estouro.
- **Desvio:** projetado menos orçado. Positivo quer dizer que o custo vai passar do orçado.
- A margem projetada do Resumo usa o projetado.
- A marca **Calculado na implantação** aparece quando parte do realizado foi recomposta na implantação: as horas apontadas antes do cálculo de custo entrar no ar receberam o custo-hora do cargo daquele dia, e as saídas de material com o projeto lançadas antes da requisição de material entraram pelo valor da saída.
- A linha do tempo do projeto mostra **Material baixado para a {OS}: {valor}** e **Material devolvido da {OS}: {valor}**, para quem vê custo.

## Perguntas frequentes
**Por que o previsto do instalador saiu do realizado?** Porque ainda não foi gasto: o título previsto nasce com a OS de instalação, antes do serviço. Ele aparece em **Comprometido** até ser confirmado e, aí, passa ao **Realizado**. O projetado não muda com isso.

**De onde vem o custo do material?** Da requisição de material da OS: cada baixa entra pelo custo médio do item no momento, e a devolução sai pelo custo da baixa original. O estorno da requisição tira o valor de novo.

**De onde vem o custo das horas?** De cada apontamento fechado: horas líquidas (sem as pausas) × custo-hora do cargo de quem trabalhou, gravado no fechamento. O salário de cada pessoa não aparece.

**Por que não vejo esta aba?** O orçado é sigilo comercial. Só quem tem a permissão **Ver custo e margem** do projeto vê.