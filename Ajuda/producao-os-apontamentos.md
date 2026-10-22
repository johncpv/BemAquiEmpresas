---
titulo: OS de fabricação — apontamentos
rota: /app/producao/os/$id
aba: apontamentos
padrao: false
modulo: production
---

## Para que serve
Ver as horas apontadas na OS, quem apontou e quando, e corrigir ou aprovar quando preciso.

## Como usar
- Cada linha mostra o funcionário, a etapa, o início, o fim, as pausas com o motivo, as **horas líquidas** (sem as pausas) e quem registrou.
- As marcas explicam o que aconteceu: **Apontado por {supervisor}**, **Encerrado por {nome}** (outro operador concluiu a etapa para todos), **Encerrado pelo supervisor**, **Corrigido** e **Autoaprovado**.
- O histórico da OS mostra cada correção com o antes e o depois.
- Com a permissão **Ver custo e margem** do projeto, a coluna **Custo** mostra o valor de cada apontamento e o custo-hora do cargo gravado no fechamento. **Histórico do custo** mostra cada versão: o custo-hora, as horas, o valor, a origem (Fechamento, Correção, Recálculo ou Implantação) e quem fez.
- **Sem custo-hora** quer dizer que o cargo estava sem custo-hora no fechamento; use **Abrir cargo** e **Recalcular apontamentos sem custo**. **Calculado na implantação** quer dizer que o custo veio da recomposição, com o custo-hora do cargo daquele dia.

## Apontar por funcionário
- Para quem não conseguiu apontar no tablet. Exige a ação de editar na produção e a de apontar do app do operador.
- Escolha o funcionário ativo, a etapa, o início e o fim (ou **Termina agora**) e as pausas.
- O sistema recusa início no futuro, fim antes do início, apontamento com mais de 7 dias e horário que cruza com outro apontamento do mesmo funcionário.

## Corrigir apontamento e aprovar etapa
- **Corrigir apontamento** (quem aprova na produção): ajuste início, fim e pausas e informe o motivo. Apontamento aberto fecha com a marca "Encerrado pelo supervisor".
- **Aprovar etapa** aparece quando uma etapa que exige aprovação foi finalizada.
- Quem fez o apontamento não corrige nem aprova a própria etapa. Se não houver outro aprovador na empresa, a ação passa com a marca **Autoaprovado** e o Proprietário recebe o aviso.

## Perguntas frequentes
**As horas já viram custo?** Sim. Cada apontamento fechado vira custo de mão de obra: horas líquidas × custo-hora do cargo, no fechamento. A correção refaz o valor com o mesmo custo-hora e as horas novas, e a diferença entra no custo do projeto.