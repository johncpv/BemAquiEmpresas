---
titulo: Caixa (PDV) — vender sem conexão
rota: /pdv
aba: sem-conexao
padrao: false
modulo: pos
---

## Para que serve
Explicar o que o caixa faz quando a internet cai e o que quer dizer a faixa colorida no topo da tela. O caixa guarda neste aparelho os produtos, os preços e as formas de pagamento da loja, para abrir e achar os produtos mesmo sem internet. O que é feito sem rede fica guardado aqui e sobe sozinho quando a rede volta.

## Como usar
- **Primeira abertura:** precisa de internet. O caixa mostra "Preparando o caixa: baixando produtos" com o progresso. Depois disso ele abre mesmo sem rede.
- **"A primeira abertura precisa de internet.":** o aparelho ainda não baixou os produtos. Ligue o Wi-Fi ou o cabo e toque em **Tentar de novo**.
- **Faixa verde, "Sincronizado às 14:05":** está tudo em dia; a hora é a da última vez que o caixa conversou com o servidor.
- **Faixa amarela, "Sem conexão":** a rede caiu. O caixa continua aberto com os dados guardados e mostra a hora da última sincronização.
- **Faixa vermelha, "Sem conexão: 3 vendas a enviar":** há vendas feitas sem rede esperando para subir, e a faixa diz há quanto tempo está a mais antiga. Elas sobem sozinhas quando a rede volta; não feche o navegador nem apague os dados do site.
- **Faixa amarela com rede, "3 vendas a enviar":** as vendas estão subindo; o caixa envia a fila a cada poucos segundos e tira dela o que o servidor confirmou. Se a rede cair no meio, ele reenvia a partir da primeira venda não confirmada, sem duplicar nada.
- **"As vendas não sobem: acerte o relógio do aparelho":** a data e a hora do aparelho estão erradas e o servidor recusou o envio. Acerte o relógio; as vendas continuam guardadas e sobem sozinhas depois.
- **"Vende sem conexão até 25/11 às 14:05":** o prazo que a loja deu para o caixa ficar sem sincronizar (o **Tempo máximo sem internet** da loja, até 7 dias). A conta parte do último horário que o caixa recebeu do servidor: mexer no relógio do aparelho não estende o prazo.
- **"Conecte à internet até amanhã às 14h.":** já passou 80% do prazo. Reconecte o aparelho antes disso.
- **"Este caixa está há N dias sem enviar as vendas. Conecte à internet para abrir o caixa.":** o prazo venceu. O turno aberto pode ser fechado, mas um turno novo só abre depois de conectar.
- **"O relógio deste aparelho está atrasado…":** o relógio voltou para antes do último horário conhecido. Acerte a data e a hora e conecte; até lá, nenhum turno novo abre, e o servidor registra a divergência.
- **"O navegador pode apagar dados deste caixa. Instale o aplicativo.":** o navegador não garantiu guardar os dados. Instale o caixa como aplicativo (no Chrome ou no Edge, menu ⋮ → **Instalar aplicativo**) e abra por ele.
- **"O caixa já está aberto em outra janela.":** só uma janela do caixa vende por vez. Use a outra; se fechar a outra, esta assume sozinha.
- **"Atualização disponível. Será aplicada na troca de turno.":** chegou uma versão nova do caixa. Ela não entra no meio do turno; é aplicada quando o turno for fechado.

## Entrar e vender sem conexão
- **Entrar:** sem rede, o caixa mostra "Sem conexão: toque no seu nome e digite o seu PIN". O PIN é o mesmo de sempre e é conferido no próprio aparelho. A sessão fica marcada como feita sem rede e continua assim até você tocar em **Sair**, mesmo que a rede volte.
- **"Este caixa ainda não recebeu o seu cadastro. Conecte à internet uma vez.":** você foi cadastrado, ganhou o PIN ou entrou na loja depois da última sincronização deste aparelho (ou ainda precisa trocar o PIN que o gerente deu). Conecte o aparelho uma vez e tente de novo. Use **Não achei o meu nome** se o seu nome não aparece.
- **5 PINs errados:** a sua entrada fica bloqueada **neste aparelho** até ele conectar à internet, e o bloqueio é avisado ao servidor.
- **Abrir, sangria, suprimento e fechar:** funcionam sem rede e vão marcados. Quando tudo sobe, o servidor calcula o esperado e a quebra, e o fechamento segue para **Fechado** ou **Em conferência**, como sempre. O relatório do fechamento lista as vendas feitas sem rede.
- **Supervisor sem rede:** o supervisor digita o **código sem internet** dele (não o PIN), criado em Meu perfil → Segurança. Vale para desconto, cancelamento de item e cancelamento da venda paga toda em dinheiro, dentro dos limites da loja (por padrão, até R$ 50,00 cada, 5 aprovações e R$ 150,00 por turno). Acima disso: "Acima do limite sem internet. Conecte para aprovar.". A troca de preço sempre precisa de internet. Se o turno passar de R$ 100,00 aprovados sem rede, o fechamento vai para a conferência.
- **O que só funciona com internet:** fiado, vale-troca, devolução, busca de cliente cadastrado, consulta de saldo, registro de aparelho e instalação de impressora nova. Eles aparecem desabilitados com "Disponível com internet." (toque em **Só com internet** no caixa para ver a lista).
- **Quando a rede volta:** a fila sobe sozinha, o segredo do aparelho é trocado e a faixa volta ao normal, sem você fazer nada. O servidor confere de novo cada entrada e cada aprovação: PIN trocado, pessoa desativada ou aprovação acima do limite vão para **Pendências do PDV** (tipo "Acesso sem rede"), e as vendas daquela entrada esperam o Proprietário liberar.

## Perguntas frequentes
**Quais aparelhos servem de caixa?** Windows com Google Chrome ou Microsoft Edge, e Android com Google Chrome. Em outros navegadores a tela diz "Este navegador não é suportado no caixa.".

**Perco as vendas se a energia cair?** Cada venda concluída é gravada no aparelho na hora. Ao religar, abra o caixa de novo: as vendas guardadas continuam na fila e sobem quando houver rede.

**Posso limpar os dados do navegador?** Não no aparelho do caixa. Limpar os dados do site apaga as vendas que ainda não subiram e o registro do aparelho.

**O caixa foi bloqueado ou trocado. E as vendas que ele tinha?** Elas continuam subindo, mesmo com o aparelho bloqueado, e ficam em **Pendências do PDV** para o gerente conferir antes de entrar.

**O preço mudou na retaguarda. Quando chega ao caixa?** Na próxima sincronização, alguns minutos depois, se houver rede. Sem rede, o caixa usa o último preço que baixou.

**Alguém que pegar o aparelho consegue descobrir o meu PIN?** O PIN nunca fica no aparelho. Fica só uma conferência cifrada, que dá muito trabalho para quebrar e só serve sem rede, com limites; por isso o código do supervisor é separado do PIN e tudo é conferido de novo quando as vendas sobem.
