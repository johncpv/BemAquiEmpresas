---
titulo: Planejamento — fila de prioridade
rota: /app/producao/planejamento
aba: fila
padrao: true
modulo: production
---

## Para que serve
Decidir a ordem em que a fábrica atende os projetos e ver, antes de confirmar, o que acontece com a data de entrega de cada um. O plano usa esta fila para datar as etapas: quem está mais acima ocupa os centros de trabalho primeiro.

## Como usar
- A fila mostra os projetos que têm **OS de fabricação aberta** (nem concluída nem cancelada), na ordem da fila, com: posição, número e nome do projeto, cliente, OS abertas, horas restantes, data **prometida**, entrega **prevista**, **folga** em dias e o prazo (**No prazo**, **Em risco**, **Atrasado** ou **Sem previsão**).
- **Folga** é a diferença, em dias corridos, entre a data prometida e a entrega prevista. Positiva, sobra prazo; negativa, o projeto vai atrasar esse tanto.
- **Projeto sem previsão:** a linha diz o motivo (OS sem roteiro, etapa sem centro, centro sem capacidade) e traz o botão que resolve.
- **Mudar a ordem:** arraste o projeto pela alça, à esquerda, ou use os botões **Mover para cima**, **Mover para baixo** e **Posição…** (escolha o número da posição e clique em **Mover**).
- **Pelo teclado:** vá com Tab até a alça do projeto, aperte **Espaço**, use as **setas para cima e para baixo** e aperte **Espaço** de novo para soltar (**Esc** cancela). Os botões de mover também funcionam com Tab e Enter. O leitor de tela anuncia "Projeto {número} movido para a posição {n}".
- Assim que você muda a ordem, aparece a faixa **Ordem alterada, ainda não salva.** e, em até 2 segundos, cada linha mostra a **prévia**: entrega prevista **antes → depois** (por exemplo, 05/12 → 09/12) e a folga nova. A prévia é só uma simulação: nada muda na fábrica até salvar. Enquanto calcula, aparece **Calculando o impacto…**.
- O projeto que passa a atrasar (ou atrasa mais) fica destacado. Ao clicar em **Salvar ordem**, o sistema lista **Estes projetos passam a atrasar: …**, com quantos dias depois do prometido. Escolha **Salvar mesmo assim** ou **Voltar**. Se ninguém piora, salva direto.
- Depois de salvar aparece **Ordem salva. Recalculando as datas…** e o plano se refaz sozinho. **Descartar** volta à ordem gravada.
- **No celular:** não há arraste. Cada linha tem **Subir** e **Descer**, com o número, a entrega prevista e o prazo; a prévia aparece abaixo de cada linha.
- **Mudanças na fila** mostra o histórico: quem mudou (ou **Sistema**), quando, o motivo e as posições antes → depois.

## Como o projeto novo entra
- O projeto entra sozinho quando ganha a **primeira OS de fabricação**, **pela data prometida**: logo antes do primeiro projeto da fila com data prometida depois da dele. No empate de data, fica depois dos que já estavam. Sem data prometida, vai para o fim.
- O projeto sai sozinho quando as OS de fabricação dele terminam ou são canceladas. As posições continuam 1, 2, 3…, sem buraco.
- A entrada e a saída aparecem em **Mudanças na fila**, com o motivo **Entrou pela data prometida**, **Saiu: OS concluídas** ou **Saiu: OS canceladas**.
- Na primeira vez, a fila é montada pela data prometida, depois pela data de assinatura do contrato e pelo número do projeto (**Fila montada pela data prometida**).

## Perguntas frequentes
**Quem pode mudar a ordem?** Quem tem permissão de editar a produção. Os outros veem a fila só para leitura, com a nota "Para mudar a ordem, peça a quem cuida da produção."

**Duas pessoas podem mexer ao mesmo tempo?** Podem abrir juntas, mas só a primeira salva. A segunda vê **A fila mudou desde que você abriu. Recarregue.** e reordena sobre a fila nova.

**Apareceu "A fila mudou. Recarregue e reordene de novo."** Um projeto entrou ou saiu da fila enquanto você reordenava. Recarregue a tela e mude a ordem de novo.

**Apareceu "Aguarde alguns segundos para simular de novo."** A prévia tem um limite de simulações por minuto. Espere um pouco e mexa de novo; salvar continua funcionando.

**A fila muda a ordem das faltas de material?** Não. As faltas seguem a OS que originou a compra e depois a BOM liberada há mais tempo.

**E as OS dentro do projeto?** Seguem a ordem dos itens do projeto; dentro da OS, as etapas seguem a sequência do roteiro.
