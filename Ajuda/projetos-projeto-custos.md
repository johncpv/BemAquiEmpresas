---
titulo: Projeto — custos
rota: /app/projetos/$id
aba: custos
padrao: false
modulo: projects
---

## Para que serve
Comparar o que o orçamento aprovado previu gastar com o que já foi gasto, o que já está contratado e quanto o projeto deve custar no fim, em cada natureza de custo. Os números são os mesmos do relatório **Orçado × realizado**.

## Como usar
- No topo ficam a receita contratada (contrato mais aditivos aprovados), a margem orçada, a margem realizada (receita menos o que já foi gasto) e a **margem orçada → projetada**, com a diferença em pontos percentuais (pp).
- Se o projeto não tem nenhum custo orçado, a margem orçada vira **Sem orçamento de custo**, a projetada aparece sozinha (só com o custo já lançado) e um aviso no topo leva ao contrato e ao orçamento de origem.
- **Editar custo orçado:** quem tem a permissão **Editar custo orçado** informa ou corrige o orçado de cada natureza, com o motivo. O diálogo mostra o custo orçado total e a margem orçada antes e depois; só as naturezas que você alterar mudam. A margem orçada e o desvio passam a valer na hora aqui, no **Orçado × realizado** e no painel. Projeto encerrado ou cancelado não é editado.
- **Edições do custo orçado**, no fim da aba, mostra cada edição: quem fez, quando, o motivo e, em cada natureza, o valor anterior e o novo.
- Cada natureza (material, mão de obra, instalação, frete, impostos e outros) mostra **Orçado**, **Realizado**, **Comprometido**, **Projetado** e **Desvio**. No celular, **Comprometido** e **Desvio** ficam em **Comprometido e desvio**, dentro do cartão.
- Clique em um valor de **Orçado**, **Realizado** ou **Comprometido** para abrir o painel com os lançamentos que somam esse valor: baixa para OS, devolução, apontamento, correção de apontamento, título a pagar, título de compra, imposto da nota ou estorno de imposto, com o documento, a data e o valor com sinal. O documento tem link quando você tem acesso ao módulo dele (Estoque, Produção, Financeiro, Compras, Fiscal). No orçado, o painel mostra o orçamento do contrato, cada aditivo aprovado e cada edição do custo orçado (pela diferença, com o motivo).
- **Realizado:** o que já foi gasto. Entram os títulos a pagar **confirmados** com o projeto (instalador, frete, outros e material entregue direto na obra), as **horas apontadas** na produção (horas × custo-hora do cargo), o **material baixado** para as OS pelas requisições, menos as devoluções, e o **imposto das notas** autorizadas do projeto. Material comprado para o projeto só conta como realizado quando é baixado para a OS.
- **Comprometido:** o que já está contratado, mas ainda não foi gasto. Entram o título **previsto** (por exemplo, o previsto do instalador quando a OS de instalação nasce) e o material comprado para o projeto que ainda não foi baixado para a OS, **por saldo**: títulos de compra menos baixas, nunca abaixo de zero. O painel do comprometido de material mostra essa conta no topo.
- **Projetado:** o realizado mais o maior entre o que ainda falta do orçado e o comprometido. Se o comprometido passa do que falta do orçado, o projetado já mostra o estouro.
- **Impostos:** até a primeira nota fiscal do projeto, o projetado é o imposto do orçamento (marca **Imposto orçado**). Com nota, é o imposto das notas mais o orçado da parte ainda não faturada (marca **Imposto das notas + orçado no restante**).
- **Desvio:** projetado menos orçado. Positivo quer dizer que o custo vai passar do orçado.
- Quando o projeto tem guia de imposto, aparece a nota "Guias de imposto são da empresa: entram no DRE, não no projeto."
- Quando o projeto tem não conformidade, aparece a quantidade e o custo estimado só como informação: o retrabalho já está nas horas e no material, e não soma em coluna nenhuma.
- Vindo do chip **Margem abaixo** do relatório, a natureza que mais desviou fica destacada.
- A marca **Calculado na implantação** aparece quando parte do realizado foi recomposta na implantação: as horas apontadas antes do cálculo de custo entrar no ar receberam o custo-hora do cargo daquele dia, e as saídas de material com o projeto lançadas antes da requisição de material entraram pelo valor da saída.
- A linha do tempo do projeto mostra **Material baixado para a {OS}: {valor}** e **Material devolvido da {OS}: {valor}**, para quem vê custo.

## Perguntas frequentes
**Por que o previsto do instalador saiu do realizado?** Porque ainda não foi gasto: o título previsto nasce com a OS de instalação, antes do serviço. Ele aparece em **Comprometido** até ser confirmado e, aí, passa ao **Realizado**. O projetado não muda com isso.

**De onde vem o custo do material?** Da requisição de material da OS: cada baixa entra pelo custo médio do item no momento, e a devolução sai pelo custo da baixa original. O estorno da requisição tira o valor de novo.

**De onde vem o custo das horas?** De cada apontamento fechado: horas líquidas (sem as pausas) × custo-hora do cargo de quem trabalhou, gravado no fechamento. No painel, as horas aparecem por OS e centro de trabalho; o nome de quem trabalhou e o custo-hora individual só aparecem para quem tem **Ver dados sensíveis** do RH (para os demais, o bloco **Dados protegidos**).

**Por que a guia de imposto não entra?** DARF, GARE e DAS são impostos da empresa, não de um projeto: entram no DRE.

**O que é "Sem orçamento de custo"?** O projeto tem receita contratada, mas nenhum custo orçado em nenhuma natureza. Sem o orçado não existe margem orçada nem desvio: mostrar 100% faria um projeto sem orçamento parecer um ótimo negócio. Por isso ele fica fora da margem do painel e do consolidado do Orçado × realizado, e aparece contado à parte. O custo orçado vem da composição de custo do orçamento aprovado (custo por natureza em cada item); confira o orçamento de origem pelo contrato. Como o contrato já foi aprovado, quem tem a permissão informa o orçado em **Editar custo orçado**.

**Por que não vejo "Editar custo orçado"?** A edição muda a margem orçada do projeto, então tem permissão própria (**Editar custo orçado**, em Usuários e acessos → Grupos), além de **Ver custo e margem**. Ela nasce só com o Proprietário. O botão também some em projeto encerrado ou cancelado.

**Editar o custo orçado ou fazer um aditivo?** Use o aditivo quando o contrato mudou (valor, prazo ou escopo): ele soma a variação no orçado e na receita. Use **Editar custo orçado** para informar um orçado que faltou ou corrigir um valor errado; a receita não muda.

**O que acontece se outra pessoa mexer no projeto enquanto edito?** O sistema avisa que o projeto mudou e oferece **Recarregar**: confira os valores e salve de novo.

**Por que não vejo esta aba?** O orçado é sigilo comercial. Só quem tem a permissão **Ver custo e margem** do projeto vê.
