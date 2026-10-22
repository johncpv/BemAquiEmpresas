---
titulo: Produção — inspeção de qualidade
rota: /app/producao/qualidade/$stepId
aba:
padrao: true
modulo: production
---

## Para que serve
Inspecionar a OS na etapa de qualidade: marcar o checklist e aprovar, ou reprovar abrindo a não conformidade e devolvendo a OS para a etapa que será refeita.

## Como usar
- Em cada item do checklist toque em **Conforme**, **Não conforme** ou **Não se aplica**. Tocar de novo no mesmo botão desmarca. Cada marcação grava o seu nome e a hora.
- **Iniciar inspeção** (opcional) abre o seu apontamento na etapa: as horas da inspeção entram no custo da OS. A decisão fecha o apontamento.
- **Aprovar qualidade** só libera com todos os itens marcados e nenhum **Não conforme**. A confirmação mostra o que muda: "A OS vai para {próxima etapa}" ou "A OS fica pronta para concluir". A mensagem é **Qualidade aprovada**.
- **Reprovar qualidade** abre a folha da não conformidade. A descrição já vem com os itens **Não conforme**. Escolha a **etapa de retorno** (a sugerida é a anterior), o responsável (você, por padrão) e, se quiser, a causa, a ação e as fotos. A mensagem é **NC {número} aberta**.

## Depois da decisão
- Aprovada no meio do roteiro: a próxima etapa fica pronta para o operador.
- Aprovada na última etapa: quem tem **Aprovar** na Produção vê **Concluir OS**; quem só decide a qualidade vê "A OS está pronta para concluir. O supervisor conclui."
- Reprovada: a etapa de retorno fica pronta e as seguintes voltam a esperar. Quando o retrabalho chegar de novo, a mesma etapa de qualidade volta com o checklist zerado, numa nova **tentativa**.

## Perguntas frequentes
**Apareceu "Quem fez uma etapa desta tentativa não aprova a qualidade dela."** Você apontou alguma etapa desde o início da OS ou desde a última reprovação. Outra pessoa com a ação de qualidade aprova; você ainda pode reprovar.

**O que é Autoinspeção?** Aprovação feita por quem participou do trabalho, permitida só quando não há outro aprovador. A OS e a etapa ficam marcadas e o Proprietário recebe o aviso.

**Duas pessoas decidiram ao mesmo tempo. E agora?** Só uma decisão grava. A outra recebe o aviso de que a OS mudou: recarregue para ver como ficou.