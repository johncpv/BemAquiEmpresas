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

## Apontar por funcionário
- Para quem não conseguiu apontar no tablet. Exige a ação de editar na produção e a de apontar do app do operador.
- Escolha o funcionário ativo, a etapa, o início e o fim (ou **Termina agora**) e as pausas.
- O sistema recusa início no futuro, fim antes do início, apontamento com mais de 7 dias e horário que cruza com outro apontamento do mesmo funcionário.

## Corrigir apontamento e aprovar etapa
- **Corrigir apontamento** (quem aprova na produção): ajuste início, fim e pausas e informe o motivo. Apontamento aberto fecha com a marca "Encerrado pelo supervisor".
- **Aprovar etapa** aparece quando uma etapa que exige aprovação foi finalizada.
- Quem fez o apontamento não corrige nem aprova a própria etapa. Se não houver outro aprovador na empresa, a ação passa com a marca **Autoaprovado** e o Proprietário recebe o aviso.

## Perguntas frequentes
**As horas já viram custo?** O custo das horas entra no projeto numa próxima entrega; aqui ficam as horas.