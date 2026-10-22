# ADR 0038: Patrimônio: OS de manutenção e paradas na capacidade

- Status: Aceito (21/10/2026). O PO (John) aprovou a decisão D10 (manutenção como parada no `IWorkCenterCapacity`) da revisão pré-Fases 4 a 8 (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`), os pontos 40 a 58 da tabela 3.3 e o achado da seção 4.2 para a PAT-03b.
- Contexto: as stories de Patrimônio (PAT-01a a PAT-03b) deixavam em aberto quem é dono da OS de manutenção (o Production já tem "OS" de fabricação e de instalação), onde fica o local do bem (o `inventory.locations` é de estoque, não de setor) e como a manutenção reduz a capacidade que o planejamento usa (ADR 0029). A PAT-03b também mostrava o custo da manutenção com `projects.view_cost`, que é sigilo comercial de projeto, não de patrimônio. O `ModuleCatalog` já tem o módulo `assets` com `view`, `create`, `edit` e `delete` (sem `approve`), e o `DocumentOrigin` já tem `Maintenance`.
- Complementa as ADRs 0003 (adendo de 21/10/2026: ações do `assets`), 0006 (fronteiras, eventos), 0016 (`IWorkCenterCapacity`), 0019 (§7 anexos, §17 CP estimado), 0024 (`IPayables`) e 0029 (planejamento finito).

## Decisão

### 1. Dono da OS de manutenção
- A OS de manutenção é do **Assets**: `assets.maintenance_orders` (numeração `MAN`, dono de anexo `MaintenanceOrder`), com plano preventivo em `assets.maintenance_plans` e checklist em `assets.maintenance_order_checklist`. Ela **não** é uma OS do Production e não aparece no Kanban.
- Plano por data ou **por horas, com horímetro digitado**; o próximo vencimento conta a partir da **conclusão** da OS anterior.
- Abre a OS quem tem `assets.create`; executa e conclui quem tem `assets.edit`. Baixa de peças do estoque fica fora do MVP.

### 2. Local do bem
- Cadastro próprio e simples **`assets.sites`** (`name` único, `is_active`, `seed_key`; seed "Fábrica" e "Escritório"), com criação rápida no combobox. O bem tem `site_id` e, opcionalmente, o centro de trabalho (`work_center_id`) para a capacidade.

### 3. Custo e título a pagar
- O valor combinado com o prestador gera o título a pagar **previsto** pelo `IPayables` (origem `Maintenance`, padrão do CP estimado do instalador, ADR 0019 §17), confirmado na conclusão; contas do plano da manutenção são parâmetros "A confirmar com a contadora".
- **Custo da manutenção visível com `assets.edit`**, não com `projects.view_cost`. O valor de aquisição do bem também só com `assets.edit`.

### 4. Paradas na capacidade
- A parada é do **Production**: `production.downtimes` (+ `source_type`, `source_id`, centro, período, horas por dia), escrita **só** pelo consumidor do Production.
- O Assets publica **`MaintenanceScheduled`**, **`MaintenanceUnscheduled`** e **`MaintenanceCompleted`** (`Assets.Contracts`, só ids, centro, período e horas por dia, sem texto livre). As **horas por dia** da parada são informadas na OS.
- O consumidor **reconcilia a partir do evento mais recente da OS** (idempotente pela inbox; reentrega ou ordem trocada não duplicam a parada).
- **`IWorkCenterCapacity.GetAvailableHoursAsync`** passa a devolver as horas **já descontadas** das paradas, e o motor de agendamento (ADR 0029) não conhece manutenção: só lê a capacidade. Uma parada nova depois do plano dispara o recálculo e o crítico "Acima da capacidade" na ocupação.
- Outras paradas (falta de energia, reforma) entram no Production depois, pelo mesmo `downtimes`.

### 5. Etiqueta e leitura
- Etiqueta com **URL curta opaca** `/b/{etiqueta}`, leitura só com login; `returnUrl` só relativo; `assets.view` conferido antes de buscar a etiqueta.
- QR gerado no servidor com biblioteca MIT; leitura pelo `BarcodeDetector` do navegador; "Ler etiqueta" no `/fab`. Foto do bem sem EXIF/GPS.
- Modelo da impressora e da etiqueta: P-13 (pré-condição externa).

### 6. Permissões
- Seed patch **`assets-permissions`**: Supervisão de produção com `assets.view`, `assets.create` e `assets.edit`; Almoxarifado com `assets.view`; `assets.delete` só com o Proprietário. Sem ação nova no catálogo (adendo de 21/10/2026 à ADR 0003).

## Consequências
- **PAT-01a** cria `assets.assets`, `assets.sites` e o seed patch; **PAT-01b** a etiqueta e a leitura; **PAT-03a** os planos e a OS; **PAT-03b** o custo, o título e as paradas, com o consumidor no Production e a mudança no `IWorkCenterCapacity`.
- `docs/03-modelo-dados.md` registra `assets.sites`, `site_id` no lugar de `location_id`, `maintenance_order_costs`, `maintenance_order_checklist` e `production.downtimes` (+ origem); `docs/04-fluxos-estados.md` registra os três eventos.
- Depreciação (Fase 9), inventário do patrimônio e termo de responsabilidade ficam fora.
