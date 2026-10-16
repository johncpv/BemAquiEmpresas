# ADR 0003 — Ações específicas por módulo no catálogo de permissões

- Status: Aceito (16/10/2026)
- Contexto: a CAD-04 precisa restringir dados pessoais de funcionários (LGPD) a quem tem permissão, o que não cabe nas 5 ações padrão (`view`, `create`, `edit`, `approve`, `delete`). A story citava `hr.employee.read-sensitive`, fora do padrão `modulo.acao`.
- Decisão:
  - O código de permissão continua sempre `modulo.acao`, com dois níveis. Não existe permissão por entidade (`modulo.entidade.acao`).
  - Cada módulo declara no `ModuleCatalog` as ações padrão que se aplicam a ele e, quando necessário, **ações específicas** com código em snake_case, rótulo e descrição em PT-BR. Exemplos: `hr.view_sensitive` (ver dados pessoais e salário), `inventory.adjust` (ajuste de inventário), `finance.pay` (baixar pagamento de OP aprovada).
  - Ações específicas aparecem na matriz de permissões do grupo como colunas extras daquele módulo, com a descrição em tooltip.
  - As regras da matriz valem para todas: marcar qualquer ação marca `view`; desmarcar `view` limpa o módulo.
  - Uma ação específica só entra no catálogo quando a necessidade é real: um requisito de segurança ou LGPD, ou uma separação de função exigida pelo negócio. Do contrário, usar as ações padrão.
- Motivo: o código de permissão fica uniforme (policy `perm:{modulo}.{acao}`, cache, `<Can>`), sem uma explosão de permissões por entidade, e ainda cobre os casos reais de dado sensível.
- Consequências: `identity.modules.applicable_actions` passa a guardar as ações padrão e também as específicas, com rótulo e descrição. A CAD-04 usa `hr.view_sensitive`. O grupo Proprietário recebe todas as ações, inclusive as específicas.
