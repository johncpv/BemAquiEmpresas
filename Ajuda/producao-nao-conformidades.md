---
titulo: Produção — não conformidades
rota: /app/producao/nao-conformidades
aba:
padrao: true
modulo: production
---

## Para que serve
Acompanhar as não conformidades abertas nas reprovações da qualidade até o encerramento, com o responsável de cada uma.

## Como usar
- A lista abre em **Aguardando você**: as NC em que você é o responsável e que não foram encerradas. **Todas** mostra as de todos. O filtro de **Situação** e a página ficam no endereço.
- Cada linha mostra o número (NC-2026-0001), a OS, o projeto, a etapa de qualidade, o responsável, a situação e a idade em dias.
- Situações: **Aberta** (vermelho), **Em ação** (âmbar, causa e ação definidas) e **Encerrada** (verde, com o resultado).
- Toque no número para abrir a NC; na OS, a aba **Qualidade** mostra as tentativas e as NC daquela OS.

## Perguntas frequentes
**Quem é o responsável?** Por padrão, quem reprovou. Quem tem **Editar** na Produção troca o responsável na NC.

**A NC some da lista quando é encerrada?** Some de **Aguardando você**. Continua em **Todas**, com a situação **Encerrada**.

**No Kanban aparece algum aviso?** Sim: o cartão da OS mostra o chip **NC aberta** enquanto houver NC não encerrada.