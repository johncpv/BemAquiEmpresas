---
titulo: Caixa (PDV) — abertura, sangria e suprimento
rota: /pdv
aba: caixa
padrao: false
modulo: pos
---

## Para que serve
Abrir o turno com o troco contado e registrar toda retirada (**sangria**) e todo reforço (**suprimento**) de dinheiro da gaveta, para o dinheiro bater no fim do turno. Funciona igual com e sem internet: tudo fica gravado neste aparelho e sobe sozinho quando a conexão volta.

## Como usar
- **Abrir o caixa:** depois de entrar com o seu PIN, digite o **fundo de troco** (o dinheiro que está na gaveta para dar troco) e toque em **Abrir caixa**. O valor da última abertura já vem preenchido. Fundo de troco zero é permitido: o caixa pede para confirmar.
- **Painel do caixa:** na tela de venda, toque em **Caixa**. Aparecem quem abriu, a hora da abertura, o número de vendas e as sangrias e os suprimentos do turno.
- **Sangria:** no painel, toque em **Sangria**, digite o valor que vai sair da gaveta e o motivo (ex.: "Depósito no banco") e confirme. Sai o comprovante em duas vias, com linha para a assinatura de quem retira: uma fica com quem leva o dinheiro, a outra vai junto do dinheiro.
- **Suprimento:** no painel, toque em **Suprimento**, digite o valor que entra na gaveta e o motivo (ex.: "Troco") e confirme. Sai o comprovante.
- **Aviso de sangria:** quando o dinheiro na gaveta passa do limite da loja, aparece "Dinheiro em caixa acima de R$ X. Faça uma sangria." com o botão **Fazer sangria**.
- **Teto de dinheiro:** se a loja tiver teto e o dinheiro na gaveta passar dele, a forma **Dinheiro** fica bloqueada no pagamento até a sangria. Cartão, Pix e as outras formas continuam.
- **Fechar caixa:** encerra o turno com a contagem cega do dinheiro e dos comprovantes (veja "Caixa (PDV) — fechamento com conferência cega").

## Regras
- **Um caixa, uma sessão aberta.** Se outra pessoa abriu o caixa, aparece "Este caixa já está aberto por Fulano desde 08:02.": peça para quem abriu fechar o caixa.
- **Sem caixa aberto não se vende.**
- **Sangria e suprimento sempre com motivo** e com o seu nome. Precisam das permissões **Sangria** e **Suprimento** do seu grupo; sem elas aparece "Você não tem permissão para esta ação.".
- **O caixa não mostra quanto dinheiro deveria haver na gaveta**, nem recusa uma sangria por ser maior que esse valor. É a conferência cega: no fechamento você conta a gaveta antes de ver o esperado. Uma sangria acima do calculado é aceita e fica marcada para o supervisor revisar no fechamento.
- No sistema, a abertura e o suprimento viram uma transferência do **Cofre** para a conta do caixa, e a sangria uma transferência da conta do caixa para o **Cofre** (Financeiro → Contas).

## Perguntas frequentes
**Onde mudo o limite do aviso de sangria e o teto?** No sistema, em Loja → Lojas e caixas, na loja. O padrão do aviso é R$ 1.000,00; o teto começa desligado. O caixa recebe os valores na próxima sincronização.

**Apareceu "Não foi possível registrar. O movimento ficou salvo neste aparelho e será enviado."** O movimento está gravado no aparelho e sobe quando a conexão voltar. Não registre de novo.

**Errei o valor da sangria.** Não apague nem registre outra sangria para "compensar": avise o supervisor. A correção fica no fechamento do caixa.

**A gaveta não abriu sozinha.** A abertura automática da gaveta e a impressão direta chegam com a configuração da impressora do caixa. Até lá, imprima o comprovante pelo botão **Imprimir**.
