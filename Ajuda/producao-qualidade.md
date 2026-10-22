---
titulo: Produção — qualidade
rota: /app/producao/qualidade
aba:
padrao: true
modulo: production
---

## Para que serve
Ver as OS que chegaram à etapa de qualidade e abrir a inspeção para aprovar ou reprovar. Nada sai da fábrica sem passar por aqui.

## Como usar
- A fila abre em **Aguardando você**: as OS que você pode decidir. **Todas** mostra também as que outra pessoa precisa decidir. O filtro fica no endereço da página.
- Cada linha mostra a OS, a etapa de qualidade, a **tentativa**, o projeto, o prometido e desde quando a OS espera. O chip **NC aberta** avisa que a OS já teve reprovação ainda não encerrada.
- Toque em **Inspecionar** para abrir o checklist e decidir. A mesma tela existe no tablet, em **/fab/qualidade**.
- A fila se atualiza sozinha quando uma etapa termina no chão de fábrica.

## Quem decide
- Só quem tem a ação **Aprovar ou reprovar a qualidade** (production.approve_quality) vê a fila e decide. Ter só **Aprovar** na Produção não basta: essa ação é a de concluir a OS.
- Quem apontou qualquer etapa da tentativa atual não aprova a qualidade dela. O chip **Você fez parte do trabalho** mostra essas OS em **Todas**.
- Se não houver outro aprovador possível, você aprova com a marca **Autoinspeção** (Aprovada por quem fez parte do trabalho, porque não havia outro aprovador. O Proprietário foi avisado.) e o Proprietário é avisado.

## Perguntas frequentes
**Por que a lista está vazia?** Nenhuma OS chegou à qualidade, ou as que chegaram tiveram uma etapa feita por você. Veja em **Todas**.

**Posso reprovar uma OS que eu mesmo fiz?** Pode. A segregação vale só para aprovar.

**Quem conclui a OS depois da qualidade?** O supervisor, com a ação **Aprovar** da Produção. Aprovar a qualidade da última etapa deixa a OS **Pronta para concluir**.