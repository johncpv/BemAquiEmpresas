---
titulo: OS de fabricação — custos
rota: /app/producao/os/$id
aba: custos
padrao: false
modulo: production
---

## Para que serve
Ver quanto o material e a mão de obra estão custando nesta OS: o material baixado pelas requisições e as horas apontadas de cada etapa multiplicadas pelo custo-hora do cargo de quem trabalhou.

## Como usar
- A linha **Material** (com o Estoque liberado) mostra o custo baixado para a OS por item e o total: cada baixa pelo custo médio do momento, menos as devoluções (pelo custo da baixa original) e os estornos.
- A linha **Mão de obra** mostra o total de horas e o custo da OS. Abaixo, cada etapa com as horas e o custo.
- A aba mostra só os totais (por etapa e da OS). O custo-hora e o custo de cada apontamento aparecem na aba **Apontamentos** quando a empresa liga **Exibir custo-hora em cada apontamento** em Configurações → Parâmetros → Produção (começa desligado).
- O custo de cada apontamento usa o **custo-hora do cargo** gravado no fechamento do apontamento. Se o custo-hora do cargo mudar depois, os apontamentos antigos não mudam; só os próximos usam o valor novo.
- As pausas (falta de material, máquina parada, intervalo, fim do turno e outras) nunca entram no custo.
- **Sem custo-hora:** o cargo do funcionário estava sem custo-hora no fechamento. As horas contam, mas o custo fica zero. Clique em **Abrir cargo**, defina o custo-hora e depois em **Recalcular apontamentos sem custo** (quem aprova na produção). O diálogo mostra quantos apontamentos mudam e o total novo.
- **Calculado na implantação:** o apontamento foi fechado antes do cálculo de custo entrar no ar e recebeu o custo-hora do cargo daquele dia.

## Perguntas frequentes
**Por que não vejo esta aba?** O custo é sigilo comercial. Só quem tem a permissão **Ver custo e margem** do projeto vê; os outros veem só as horas na aba **Apontamentos**.

**De onde vem o custo do material?** Das requisições da OS (aba **Material**): o custo médio do item no momento da baixa. A devolução sai pelo custo da baixa original, então o total é o que ficou na OS.