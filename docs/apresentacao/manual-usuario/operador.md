# Manual do Operador

**Grupo:** Operadores (do sistema). **Ações:** `shopfloor.view`, `shopfloor.create` (apontar). **Onde:** no tablet da fábrica, no endereço `/fab`. No celular, com o seu login: **Meu ponto** e **Meus holerites**.

## Primeiro acesso

1. O supervisor registra o tablet.
2. Você entra uma vez com e-mail e senha e toca em **Criar PIN para entrar mais rápido** (6 dígitos).
3. Das próximas vezes, toque no seu nome e digite o PIN. Depois de 15 minutos sem tocar, o tablet sai sozinho.

Ajuda: [fab-entrar](../../../Ajuda/fab-entrar.md)

## Rotina do dia

| Momento | O que fazer | Onde | Ajuda |
|---|---|---|---|
| Chegada | Marcar a **Entrada** | Fila → **Meu ponto** (`/fab/ponto`) | [fab-ponto](../../../Ajuda/fab-ponto.md) |
| Começar o trabalho | Tocar no cartão da etapa → **Iniciar etapa** | Fila `/fab` | [fab](../../../Ajuda/fab.md) |
| Durante a etapa | Marcar os itens do **checklist**; abrir os desenhos | Etapa `/fab/etapa/$stepId` | [fab-etapa](../../../Ajuda/fab-etapa.md) |
| Faltou material | **Pausar** → **Falta de material**, e depois **Pedir material** | Etapa | [fab-pedir-material](../../../Ajuda/fab-pedir-material.md) |
| Intervalo | **Pausar** → Intervalo; depois marcar a saída e a volta do intervalo em **Meu ponto** | Etapa e Meu ponto | [fab-ponto](../../../Ajuda/fab-ponto.md) |
| Acabou a etapa | **Finalizar etapa** → confirmar na tela cheia | Etapa | [fab-etapa](../../../Ajuda/fab-etapa.md) |
| Fim do turno | **Pausar** → Fim do turno (ou finalizar); marcar a **Saída** | Etapa e Meu ponto | [fab-ponto](../../../Ajuda/fab-ponto.md) |

## No celular, com o seu login

| Tarefa | Onde | Ajuda |
|---|---|---|
| Ver as suas marcações dos últimos 60 dias | Pessoas → Meu ponto `/app/meu-ponto` → **Minhas marcações** | [meu-ponto](../../../Ajuda/meu-ponto.md) |
| Pedir ajuste de marcação esquecida ou abono de ausência (com atestado) | Meu ponto → **Incluir marcação esquecida** ou **Abonar ausência** | [meu-ponto](../../../Ajuda/meu-ponto.md) |
| Ver e baixar o holerite | Pessoas → Meus holerites `/app/meus-holerites` | [meus-holerites](../../../Ajuda/meus-holerites.md) |

O holerite e as marcações antigas não abrem no tablet, porque ele é usado por todos.

## Entre as telas

- **Cada hora apontada vira prazo e custo do projeto.** Por isso o tempo de pausa não conta, e por isso é importante pausar com o motivo certo.
- **Falta de material avisa o almoxarifado sozinha.** Você vê na etapa **Material separado** ou **Recusado: motivo**.
- **Mais de um na etapa:** **Sair da etapa (só eu)** fecha só o seu apontamento; **Concluir etapa para todos** pede o checklist e fecha o de todos.
- **Qualidade:** a etapa de qualidade aparece como "Aguardando qualidade". Quem decide é o inspetor ou o supervisor. Se a OS voltar para uma etapa que você já fez, a qualidade reprovou: refaça e finalize normalmente.
- **O ponto do tablet é controle interno de jornada**, não o registro legal (REP-P, Portaria 671). Ele alimenta o espelho, o banco de horas e a folha.

## Os 5 erros mais comuns

1. **Esquecer de finalizar ou pausar no fim do turno.** As horas continuam contando. Se esqueceu, avise o supervisor no dia seguinte.
2. **Apontar com o PIN de outro colega.** As horas e o checklist ficam no nome dele. Sempre o seu nome, o seu PIN.
3. **Pausar sem o motivo certo.** "Falta de material" e "Máquina parada" mostram ao supervisor onde a fábrica está travando. "Outro" esconde o problema.
4. **Marcar o checklist só no fim, de uma vez.** Cada marcação grava o seu nome e a hora. Marque conforme faz.
5. **Marcar o ponto duas vezes achando que não gravou.** Só a primeira vale; a tela mostra "Você já registrou entrada às …". Esqueceu uma marcação? Peça o ajuste em **Meu ponto**, no celular.
