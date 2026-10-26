---
titulo: Painel da TV
rota: /app/painel-tv
aba:
padrao: true
modulo: kiosk
---

## Para que serve
Cadastrar cada TV da fábrica com um link próprio, que abre o painel de produção sem login e sem expor a senha de ninguém. Cada tela pode ser desligada a qualquer momento.

## Como usar
- Clique em **Nova tela**, dê um nome (por exemplo TV da Montagem), escolha a linha (ou **Todas**), as visões e o tempo de rotação, e clique em **Criar tela**. É preciso poder criar telas e ver a produção.
- O link aparece **uma única vez**. Clique em **Copiar link** e abra-o no navegador da TV. Ele vale para uma conexão só: depois de abrir, a TV fica conectada sozinha, mesmo se reiniciar.
- A lista mostra a situação de cada tela (Aguardando conexão, Ativa ou Revogada), o último acesso ("Visto há {n} min" ou "Nunca conectada") e as sessões ativas.
- **Editar** muda o nome, a linha, as visões, a rotação e as redes. O link continua o mesmo e a TV recebe a configuração nova em até 1 minuto.

## Gerar novo link
- Use quando trocar a TV, limpar o navegador dela ou quando a lista mostrar **Precisa de novo link**. O link anterior deixa de valer e a TV conectada sai do painel até abrir o novo.
- A sessão da TV vence com 30 dias sem uso ou 180 dias desde a conexão. Nos dois casos, gere um novo link.

## Revogar
- **Revogar** (com permissão de excluir no Painel da TV) desliga a TV na hora: a tela mostra "Esta tela foi desligada" em até 1 minuto. Não dá para desfazer; para voltar, crie uma tela nova.
- O Proprietário recebe um aviso quando uma tela é criada, alterada, revogada ou quando alguém tenta usar de novo um link já usado.

## Redes permitidas
- Opcional. Informe até 5 redes no formato 192.168.10.0/24 (ou um IP só). Fora delas, a TV mostra "Esta tela só funciona na rede da fábrica." Vazio: qualquer rede.

## Perguntas frequentes
**A TV vê custo ou nome de cliente?** Não. A TV só lê o painel da produção, sem dinheiro e sem dado pessoal, e não alcança nenhuma outra tela do sistema.

**Perdi o link antes de abrir na TV.** Clique em **Gerar novo link**: o link não aparece de novo.
