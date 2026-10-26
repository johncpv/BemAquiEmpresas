---
titulo: Painel da TV
rota: /tv
aba:
padrao: true
modulo:
---

## Para que serve
Painel da fábrica para a TV, sempre escuro e com letras grandes, aberto por um link próprio da tela, sem login. Mostra em que etapa está cada OS, quem está nela, há quanto tempo, o que está atrasado e o que vem a seguir.

## Como usar
- Abra no navegador da TV o link copiado em **Painel da TV** (no backoffice). O link vale para uma conexão só e some da barra de endereço assim que a TV conecta.
- Depois disso, a TV abre o painel sozinha, mesmo se reiniciar: basta abrir o endereço /tv.
- No canto, **Ao vivo** mostra que o painel está atualizado. Sem conexão, aparece **Reconectando…** e a hora da última atualização; os dados continuam na tela.
- **Tela cheia** esconde a barra do navegador. A tela fica acesa enquanto o painel está aberto, quando o navegador permite.

## As visões
- **Quadro por etapa**: uma coluna por etapa, com a letra da etapa e quantas OS estão nela. Cada cartão tem 4 linhas: o número da OS e o nome curto do projeto; quem está na etapa (primeiro nome e inicial, "+1" quando há mais gente) ou **Pausada: {motivo}**; o tempo na etapa e as horas apontadas de padrão; e a data prevista para terminar a etapa, ou **ATRASADA** ou **EM RISCO**. Cada coluna mostra até 3 cartões, primeiro os atrasados; o resto aparece como **+{n} OS**.
- **Fila e previsões**: os próximos projetos da fila com OS nesta linha, com a posição, o início previsto na linha, a previsão de entrega, a data prometida e a folga (**No prazo**, **Em risco** ou **Atrasado**). Sem previsão ainda, aparece **Previsão em cálculo**.
- **Atrasos e riscos**: as OS atrasadas e em risco desta linha, do maior atraso para o menor, com a etapa atual, quem está nela, quantos dias e a etapa que define a entrega. Sem nenhuma, a visão não aparece.
- O cabeçalho mostra a linha da tela, quantas OS estão em andamento e quantas estão atrasadas. O canto mostra a visão atual (por exemplo **1/3**).

## Cores
- Faixa vermelha no cartão: OS atrasada (a data prometida já passou). Faixa laranja: em risco (a previsão passa da data prometida).
- Horas acima do padrão ficam em vermelho no medidor.
- A letra da etapa fica em destaque no display de andar; o resto da tela usa as cores só para avisar.

## Rotação e atualização
- As visões trocam sozinhas no tempo escolhido para a tela (de 10 a 120 segundos). Com uma visão só, não há troca. Na TV menor, quando as colunas não cabem, elas rolam a cada troca.
- Um apontamento no app do operador muda o cartão na TV em menos de 2 segundos. Uma previsão nova chega em até 10 segundos.

## Por que o cliente não aparece
A TV fica à vista de todos na fábrica. Por isso ela nunca mostra o nome do cliente, o produto, valores nem dados pessoais: só o número da OS, o nome curto do projeto e o operador pelo primeiro nome e inicial.

## Avisos da tela
- **Conectando ao painel…**: a TV está buscando os dados.
- **Nenhuma OS em produção nesta linha.**: não há OS aberta nos centros desta linha agora.
- **Link inválido ou desligado.**: o link já foi usado ou a tela foi desligada. Peça um novo link a quem cuida do painel.
- **Esta tela precisa ser conectada de novo.**: a sessão venceu (30 dias sem uso ou 180 dias) ou um novo link foi gerado.
- **Esta tela foi desligada por {empresa}.**: a tela foi revogada.
- **Painel indisponível nesta versão do sistema.**: o Painel da TV não está liberado para a empresa.
- **Esta tela só funciona na rede da fábrica.**: a TV está fora das redes permitidas da tela.

## Perguntas frequentes
**A TV precisa de usuário?** Não. Cada TV usa um link próprio, que pode ser desligado a qualquer momento e não dá acesso a nenhuma outra tela do sistema.

**Como mudo o que a TV mostra?** No backoffice, em **Painel da TV**, use **Editar** na tela (linha, visões e rotação) e **Ver prévia** para conferir antes de ir até a TV.
