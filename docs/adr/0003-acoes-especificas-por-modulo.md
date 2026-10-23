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

## Adendo 21/10/2026 (revisão pré-Fases 4 a 8, decisões D11, D12, D16 e D15 do PO)
- **`fiscal.approve`** (ação padrão nova no módulo `fiscal`, D12): significado "cancelar e inutilizar NF-e". Cancelar e inutilizar são feitos em uma etapa por quem tem a ação; os demais usam "Pedir cancelamento" (FIS-03b). Reservada ao Proprietário (ADR 0008, adendo de 21/10/2026); nenhum seed patch a concede. Também libera ver o corpo dos webhooks fiscais (ADR 0031 §5).
- **`fiscal.import_incoming`** (ação específica, D12, segregação de função): "Importar nota de entrada: trazer o XML ou o PDF da nota do fornecedor, sem poder emitir nota de saída." O Almoxarifado recebe `fiscal.view` + `fiscal.import_incoming` (FIS-04a, FIS-05).
- **Seed patch `fiscal-permissions`** (FIS-02a): Financeiro com `fiscal.view`, `fiscal.create` e `fiscal.edit`; Almoxarifado com `fiscal.view` e `fiscal.import_incoming`.
- **`kiosk.delete`** (ação padrão nova no módulo `kiosk`, D11): revogar uma tela do Painel da TV. Criar e editar tela (`kiosk.create`) passam a exigir também `production.view` (ADR 0030 §4). A Supervisão de produção recebe `kiosk.delete` pelo seed patch `kiosk-delete` (PNL-01a).
- **`hr.view_health`** (ação específica, LGPD, dado de saúde): "Ver atestados e dados de saúde: abrir atestado médico e o CID dos afastamentos." Separa o atestado (dado sensível de saúde, LGPD art. 11) do salário e dos documentos (`hr.view_sensitive`); uma não implica a outra (RH-01b, CAD-04c1). Reservada ao Proprietário como a `hr.view_sensitive` (ADR 0008, adendo de 21/10/2026). **Entrou no catálogo na CAD-04c1b** (decisão do PO de 22/10/2026), com o rótulo "Ver dados de saúde" e o seed patch `hr-view-health` no grupo RH; a regra de leitura e escrita está no adendo de 22/10/2026 à ADR 0016.
- **Módulo `portal`** (D16) no `ModuleCatalog`: "Portal do cliente", área Comercial, ações `view`, `create` (convidar acesso), `edit` e `delete`, sem ação específica; seed patch `portal-permissions` para o Comercial (`portal.view`, `portal.create`, `portal.edit`). Detalhes na ADR 0040.
- **Ações do `assets`** (Patrimônio): continuam as padrão já no catálogo, `view`, `create`, `edit` e `delete`, **sem `approve`** e sem ação específica. Custo da manutenção e valor de aquisição ficam visíveis com `assets.edit` (não com `projects.view_cost`). Seed patch `assets-permissions`: Supervisão de produção com `assets.view`, `assets.create` e `assets.edit`; Almoxarifado com `assets.view`; `assets.delete` só com o Proprietário (ADR 0038 §6).
- Cada ação entra no `ModuleCatalog` na story que a usa primeiro (FIS-02a, PNL-01a, RH-01b, POR-01a, PAT-01a); a concessão segue a delegação da ADR 0008.
