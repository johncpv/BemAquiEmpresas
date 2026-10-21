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

- Ver ADR 0019 (ações específicas `quotes.view_cost` e `projects.view_cost`, por sigilo comercial).

## Adendo 21/10/2026 (revisão pré-Fase 3, decisões D6, D8 e D13 do PO)
- **`engineering.delete`** (ação padrão, D13): o módulo `engineering` passa a declarar `delete`, para excluir roteiro sem uso (ENG-01) e descartar rascunho de BOM (ENG-02). Entra no seed do grupo Engenharia pelo patch `engineering-permissions`.
- **`purchasing.receive`** (ação específica, D8, segregação de função): "Registrar recebimento: dar entrada no estoque do que chegou de um pedido de compra aprovado." Separa quem pede (`purchasing.create`) de quem recebe; o Almoxarifado recebe `purchasing.view` + `purchasing.receive`, sem `purchasing.edit` (CMP-01, CMP-04; ADR 0024 §5).
- **`production.approve_quality`** (ação específica, D6): "Aprovar ou reprovar a qualidade: decidir a etapa de qualidade da OS, sem concluir a OS nem liberar parcela." É a única ação de aprovação usável numa sessão por PIN (ADR 0026 §2). A conclusão da OS e a liberação da parcela continuam com `production.approve` e login completo (PRD-05, PRD-06a1). Aprovar e reprovar a qualidade exigem esta ação (a `production.approve` sozinha não decide a qualidade); a Supervisão de produção do seed a recebe pelo seed patch `production-quality` (PRD-05).
- As três entram no `ModuleCatalog` na story que as usa primeiro (ENG-01, CMP-01 e PRD-05); a concessão segue a regra de delegação da ADR 0008 (ninguém concede o que não tem).
