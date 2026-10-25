---
titulo: Planejamento da produção
rota: /app/producao/planejamento
aba:
padrao: true
modulo: production
---

## Para que serve
Saber quando cada etapa de cada OS de fabricação vai começar e terminar, pela capacidade real da fábrica. Com isso a data de entrega dada ao cliente vem do plano, e não do chute.

## Como o plano calcula
- **Ordem:** os projetos entram pela **data prometida** ao cliente (a mais cedo primeiro). No empate, pela **data de assinatura** do contrato e depois pelo número do projeto. Projeto sem data prometida vai para o fim. Dentro do projeto, as OS seguem a ordem dos itens; dentro da OS, as etapas seguem a sequência do roteiro.
- **Quem já está na máquina continua:** a etapa em andamento ou pausada entra antes da fila, a partir de hoje, no centro dela.
- **Horas restantes:** etapa que não começou conta as horas padrão inteiras. Etapa em andamento conta as horas padrão menos as apontadas, e nunca menos de 10% do padrão.
- **Retrabalho inteiro:** a etapa que a qualidade devolveu volta com as horas padrão inteiras.
- **Capacidade:** cada centro de trabalho tem as horas por dia dos turnos. Fim de semana, feriado e parada do calendário do centro tiram horas do dia. As férias dos funcionários não mudam a capacidade.
- **Etapa seguinte no dia útil seguinte:** a próxima etapa da mesma OS começa no dia útil depois do fim da anterior. OS diferentes não esperam uma pela outra.
- A etapa ocupa as horas que sobram em cada dia e se divide em quantos dias precisar. Uma OS de menor prioridade pode usar a sobra de um dia sem atrasar a de maior prioridade.

## Como usar
- O cabeçalho mostra o último cálculo, por exemplo **Recalculado às 14:05 (de hora em hora), 12 OS em 230 ms**, e quantas OS mudaram de data.
- O plano se refaz sozinho **de hora em hora**. Quem edita na produção pode clicar em **Recalcular agora** depois de mudar etapas, horas ou o calendário. Enquanto calcula, o botão mostra **Recalculando…**.
- **OS sem previsão** lista as OS que o plano não conseguiu datar, com o motivo e o botão que resolve:
  - **Escolher roteiro:** a OS ainda não tem etapas.
  - **Informar centro:** uma etapa está sem centro de trabalho. Abra a aba **Etapas** da OS e escolha o centro.
  - **Revisar calendário:** o centro não tem horas nos próximos 2 anos (centro inativo ou calendário sem dias úteis).
- As datas previstas aparecem na aba **Etapas** de cada OS (**Previsto** de cada etapa e **Fim previsto** da OS) e nos cartões do **Kanban**.
- Se o último cálculo falhar, aparece **O último cálculo falhou às {hora}. As datas mostradas são do cálculo anterior.** Clique em **Recalcular agora**.

## Perguntas frequentes
**Posso mudar a data prevista à mão?** Não. A data prevista é calculada e muda sozinha a cada cálculo. Ela não entra no histórico da OS e não atrapalha quem está editando a OS.

**Por que a data mudou sem ninguém mexer?** Porque o plano é refeito de hora em hora: uma etapa que demorou mais, um feriado novo ou uma OS nova com data prometida mais cedo mudam as datas das outras.

**E a OS de instalação?** Ela fica fora do cálculo e mantém as datas digitadas.

**Posso mudar a ordem da fila?** A fila manual chega na próxima versão do Planejamento. Por enquanto a ordem é pela data prometida e pela assinatura.
