# ADR 0029: Planejamento finito

- Status: Aceito (21/10/2026). O PO (John) aprovou a decisão D10 da revisão pré-Fases 4 a 8 (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`) e os pontos 25 a 41 e 57 das tabelas 3.1 e 3.3.
- Contexto: o motor de agendamento (PLN-01a) recalcula o plano de hora em hora e a cada evento (PLN-01b). Do jeito que as stories estavam, cada recálculo reescreveria `planned_start`/`planned_end` das OS e etapas e a data prevista do projeto: a versão (`xmin`) mudaria o tempo todo, quem estivesse editando uma OS levaria 409 contínuo, e a auditoria se encheria de mudanças que ninguém fez. A fila de prioridade (PLN-02) ainda estava dividida entre a `commercial.projects.priority` e o Production.
- Complementa as ADRs 0006 (jobs, fronteiras), 0016 (`IWorkCenterCapacity`, calendário), 0019 (§2 concorrência, §12 jobs idempotentes) e 0038 (paradas de manutenção na capacidade).

## Decisão

### 1. Datas previstas fora do documento
- O plano mora **só em tabelas do plano**, nunca nas colunas dos documentos:
  - `production.schedule_runs` (cada execução: início, fim, gatilho, situação, erro, contagens; marca de "pendente" por tenant);
  - `production.schedule_allocations` (horas por etapa, centro e dia da última execução bem-sucedida);
  - `production.schedule_step_forecasts` (início e fim previstos por etapa e por OS);
  - `commercial.project_forecasts` (data prevista do projeto, motivo, risco e `computed_at`), escrita pelo Commercial a partir do evento `ProjectForecastChanged` do Production.
- Só as **datas previstas de fabricação** saem do documento: o motor **não grava** `production_orders.planned_start/planned_end` nem `production_order_steps.planned_start/planned_end` das OS de fabricação. As colunas **continuam em uso** para as datas digitadas da OS de instalação (COM-06), que o motor nunca altera; nas OS de fabricação ficam sem uso, sem apagar (apagar só na contração, com ADR).
- As tabelas do plano ficam **fora da auditoria** (não são `IAuditable`): o registro é a própria `schedule_runs`. A tela da OS e do projeto lê a previsão pelo `IProductionSchedule` e pelo `IProjects`.

### 2. Escritor único
- **Um só escritor do plano por tenant:** advisory lock por tenant na execução; pedido que chega durante o cálculo marca "pendente" e roda **uma vez** depois (no máximo 1 rodando e 1 pendente). Recálculo pedido por evento é agrupado em até 10 s.
- A execução grava o plano inteiro numa transação; execução com erro mantém o plano anterior e registra o erro (alerta de job, FND-12).
- Job recorrente global de hora em hora, enfileirando por tenant (`TenantJob<T>`, ADR 0006).

### 3. Fila de prioridade no Production
- A fila é **do Production** e é **de projetos** (R-PLN-2): `production.planning_queue` (uma posição por projeto com OS de fabricação abertas, `project_id` único; dentro do projeto, as OS seguem a ordem dos itens e as etapas a sequência do roteiro) e `production.planning_queue_state` (versão da fila, quem e quando reordenou).
- **Ordem padrão:** data prometida ao cliente, depois a data de assinatura do contrato. Projeto novo entra pela data prometida.
- **Reordenar exige `production.edit`** e é auditado (a fila é decisão de alguém, ao contrário das datas calculadas).
- `commercial.projects.priority` **fica sem uso**, sem apagar a coluna (contração só com ADR).

### 4. Regras do motor
- Agendamento finito simples, para frente, por centro de trabalho, sobre o `IWorkCenterCapacity` e o calendário útil.
- **Etapa em andamento primeiro**, com as horas restantes = padrão − apontado, com **mínimo de 10% do padrão** enquanto a etapa está em andamento ou pausada (como o `04-fluxos` §3.4); a etapa seguinte começa no **dia útil seguinte** ao fim da anterior (recalibrar depois de 1 mês de dados); retrabalho com as horas padrão inteiras.
- **Capacidade por centro:** férias de funcionário não reduzem a capacidade (o centro é a unidade); manutenção do patrimônio vira **parada** no `IWorkCenterCapacity` (ADR 0038), que devolve as horas já descontadas.
- **Ocupação** até 100% no plano finito; o crítico "Acima da capacidade" só aparece quando a capacidade cai depois do plano (parada nova). Faixas: < 60% "Folga", 60 a 95% sem destaque, ≥ 95% "Lotado".
- **Instalação:** a data prevista do projeto soma o prazo de instalação (parâmetro `planning.installation_days`, padrão 5 dias úteis) à previsão da última OS de fabricação; OS de instalação aparece no Gantt como barra só de leitura.
- **Risco:** previsão depois da data prometida avisa o responsável do projeto e quem tem `production.approve`, sem tolerância.

## Consequências
- **PLN-01a** cria `schedule_runs`, `schedule_allocations`, `schedule_step_forecasts`, o motor puro e o lock; **PLN-01b** cria `commercial.project_forecasts`, o `ProjectForecastChanged` e os gatilhos por evento; **PLN-02** cria a fila; **PLN-03** e **PLN-04** só leem; **PLN-05** simula sem gravar.
- O `docs/03-modelo-dados.md` registra as tabelas novas e marca as colunas `planned_*` como sem uso nas OS de fabricação (continuam nas de instalação) e `projects.priority` como sem uso.
- Linha de base da métrica "desvio da data prevista ≤ 15%" medida a partir da `schedule_step_forecasts` (GOL-01a).
