# EST-05 — Inventário (contagem por local)

| Campo | Valor |
|---|---|
| ID | EST-05 |
| Título | Inventário por local: abertura com a foto do saldo, contagem cega por item e lote (inclusive no celular), recontagem das divergências, e fechamento que gera um ajuste de estoque pelo fluxo de aprovação da EST-01b |
| Tamanho | M |
| Status | Draft |
| Requisitos | R-EST-9 (inventário: contagem por local, ajuste) · R-EST-6 (ajuste de inventário com motivo e aprovação) · R-EST-5 · R-PRP-7 · R-SEC-5 |
| Dependências | EST-01b (ajuste com aprovação), EST-01a (saldos, lotes), EST-02 (reservado) |
| Bloqueia | — |
| Ordem de execução | 22 (Marco 3.5 "Fechar a OS") |

## User story
Como **almoxarife**, quero **contar o estoque de um local sem ver o que o sistema diz, recontar o que deu diferente e mandar o resultado para aprovação**, e como **proprietário**, quero **aprovar a diferença de uma vez, com o valor**, para **o saldo do sistema voltar a bater com a prateleira**.

## Contexto e referências
- PRD §5.9 R-EST-9 e R-EST-6; backlog EST-05: "Contagem por local, divergências, ajuste aprovado".
- `03-modelo-dados` → `inventory_counts / inventory_count_lines` (`location_id`, `status`, `item_id`, `lot_id`, `system_qty`, `counted_qty`, `adjusted`).
- EST-01b: o ajuste nasce `Pending` e só muda o saldo na aprovação, com segregação autor × aprovador e recálculo sobre o saldo atual.

## Critérios de aceite
**Abrir**
1. **Dado** `inventory.adjust`, **quando** clico "Novo inventário", escolho o local e o escopo (todos os itens com saldo ou movimento no local, ou um filtro por categoria/tipo) e clico "Abrir inventário", **então** nasce o inventário `Counting` com número proposto `INV-AAAA-0001` (Pontos em aberto 1), uma linha por item e lote com o **saldo do sistema na abertura** (`system_qty`) e o horário da foto, com `Idempotency-Key`.
2. **Dado** um local com inventário `Counting`, **quando** tento abrir outro no mesmo local, **então** 409 "Já há um inventário aberto em {local}: {número}."

**Contar**
3. **Dado** `inventory.create` (contador), **quando** abro a contagem (no backoffice ou no celular, `/app/estoque/inventarios/$id/contagem`), **então** vejo os itens e lotes **sem o saldo do sistema** (contagem cega, proposta; Pontos em aberto 2), digito a quantidade contada na unidade de estoque (ou convertida), posso incluir um item ou lote que não estava na lista ("Encontrei outro item") e cada linha grava quem contou e quando; alvos de 44 px no celular.
4. **Dado** movimentos no local durante a contagem, **quando** o inventário fecha, **então** a diferença de cada linha = contado − (saldo da abertura + movimentos do item e lote no local entre a foto e a contagem da linha), e a tela mostra "Teve movimento durante a contagem" na linha afetada (proposta, Pontos em aberto 3).

**Divergências e recontagem**
5. **Dado** a contagem terminada, **quando** clico "Ver divergências" (`inventory.adjust`), **então** vejo as linhas com diferença (quantidade e valor pelo custo médio), ordenadas pelo valor absoluto, e posso marcar linhas para **recontagem**; a recontagem é feita por outro contador quando houver (proposta) e substitui a contagem anterior, mantendo o histórico.
6. **Dado** linhas não contadas, **quando** tento fechar, **então** o diálogo pergunta "Considerar {n} itens não contados como zero?" ou "Manter o saldo do sistema"; a escolha fica no inventário.

**Fechar e ajustar**
7. **Dado** `inventory.adjust`, **quando** clico "Fechar inventário" e confirmo no diálogo com o impacto (sobras, faltas e valor líquido), **então** o inventário vai a `Closed` e nasce **um ajuste** da EST-01b (`Pending`, motivo "Contagem", modo "quantidade contada" por linha com divergência, vínculo com o inventário), notificado aos aprovadores; o saldo só muda quando o ajuste é aprovado (EST-01b CA 5), com o recálculo sobre o saldo atual (EST-01b CA 6) considerando os movimentos posteriores ao fechamento.
8. **[Segurança]** **Dado** o ajuste gerado, **quando** o aprovador é quem abriu o inventário ou contou alguma linha, **então** a regra do autor da EST-01b vale para ele (não aprova; exceção do Proprietário único).
9. **Dado** o ajuste aprovado ou reprovado, **quando** abro o inventário, **então** a situação mostra "Ajuste {número} aprovado/reprovado" com link; reprovado, o inventário pode ser reaberto para recontagem ("Reabrir contagem").
10. **Dado** um inventário `Counting`, **quando** clico "Cancelar inventário" com motivo, **então** fica `Cancelled`, sem ajuste.

**Permissões, segurança e auditoria**
11. **Dado** o módulo `inventory` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; abrir, ver divergências, fechar e cancelar sem `inventory.adjust`; contar sem `inventory.create`: 403 e o botão some. `RequireModule` e `perm:`; IDOR em `/inventory-counts/{id}/lines/{lineId}`; o DTO da contagem não tem `system_qty` para quem só conta (teste de contrato).
12. **[Segurança]** **Dado** dois contadores na mesma linha, **quando** salvam juntos, **então** só um grava (`xmin`) e o outro vê "Esta linha foi contada por {nome} agora há pouco." com "Recarregar".
13. **Dado** qualquer mudança, **quando** abro o histórico, **então** vejo cada contagem e recontagem com quem e quando, e a decisão do ajuste.

## Regras de negócio e técnicas
- **Tabelas** (03 + proposta): `inventory_counts` (`number`, `location_id`, `scope` (jsonb), `status` Counting/Closed/Cancelled, `snapshot_at`, `opened_by`, `closed_by`, `closed_at`, `uncounted_policy`, `adjustment_id`, `xmin`); `inventory_count_lines` (`count_id`, `item_id`, `lot_id`, `system_qty`, `counted_qty`, `counted_by`, `counted_at`, `recount_of_line_id`, `movement_delta`).
- **Sem efeito direto no saldo:** todo efeito passa pelo ajuste da EST-01b (um único caminho de mudança de saldo sem documento).
- **Diferença** com os movimentos do período (CA 4); valor pelo custo médio do momento do fechamento.
- Guid v7; `xmin`; auditoria `IAuditable`; quantidades `numeric(18,4)`.

## Permissões, eventos e tabelas
- **Permissões:** `inventory.adjust` (abrir, fechar, cancelar), `inventory.create` (contar), `inventory.approve` (aprova o ajuste gerado, EST-01b).
- **Eventos:** nenhum entre módulos.
- **Tabelas:** `inventory.inventory_counts`, `inventory.inventory_count_lines` (com as colunas propostas).

## Telas e estados
- **Fábrica → Estoque → Inventários** (`/app/estoque/inventarios`, detalhe `/app/estoque/inventarios/$id` com abas Contagem, Divergências, Histórico de alterações; contagem móvel `/app/estoque/inventarios/$id/contagem`).
- **Verbos:** "Abrir inventário" / "Inventário aberto"; "Salvar contagem" / "Contagem salva"; "Fechar inventário" / "Inventário fechado. Ajuste enviado para aprovação"; "Cancelar inventário" / "Inventário cancelado".
- **Estados:** carregando; vazio ("Nenhum inventário. Conte um local para acertar o saldo." + "Novo inventário"); progresso da contagem ("{x} de {y} itens contados"); erro; modo leitura do fechado.
- **Propositivo:** escopo padrão "itens com saldo no local"; ordem da lista pela posição/código; divergências ordenadas pelo valor; próximo passo "Ver ajuste" depois de fechar.
- **400 px:** contagem um item por cartão com teclado numérico; divergências com item, diferença e valor.
- **Ajuda:** `Ajuda/estoque-inventarios.md`, `Ajuda/estoque-inventario.md` (abas `contagem` padrão, `divergencias`, `historico`), `Ajuda/estoque-inventario-contagem.md`; `<Dica>` em "Contagem cega" e "Movimento durante a contagem".

## Plano de testes
- **Unidade:** diferença com movimentos no período (CA 4); política dos não contados (CA 6).
- **Integração (Testcontainers, obrigatória: gera ajuste de estoque):** abrir com foto e bloqueio de dois no mesmo local (CA 1, 2); contagem cega sem `system_qty` no DTO (CA 3, 11); movimentos durante a contagem (CA 4); recontagem (CA 5); fechar gera um ajuste `Pending` sem mudar o saldo e aprovação muda (CA 7); segregação (CA 8); reprovado reabre (CA 9); cancelar (CA 10); três camadas e IDOR (CA 11); concorrência na linha (CA 12); auditoria (CA 13).
- **E2E (Playwright):** abrir inventário, contar no celular (400 px), fechar e aprovar o ajuste com outro usuário.

## Fora do escopo
- Inventário rotativo (contagem cíclica programada): fora do MVP.
- Coletor de dados e leitura de código de barras: fora do MVP.
- Bloqueio de movimentos no local durante a contagem: Pontos em aberto 3.

## Pontos em aberto
1. **Numeração** (PO): proposta `INV-AAAA-0001`.
2. **Contagem cega** (PO): proposta: o contador não vê o saldo do sistema; alternativa: mostrar o saldo.
3. **Movimentos durante a contagem** (PO): proposta: permitidos, com a diferença corrigida pelos movimentos do período (CA 4); alternativa: bloquear entradas e saídas no local até fechar.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers) e E2E passando.
- [ ] Migration criada (não destrutiva), testada sobre cópia do banco do tenant `konebras`.
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Telas com estados, inclusive a 400 px.
- [ ] Ajudas da lista, das abas e da contagem; teste de cobertura passando.
- [ ] Confirmação com impacto e próximo passo aplicados.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `InventoryCount` e `InventoryCountLine` (Domain); `CountDifferenceCalculator` puro.
- [ ] `InventoryCountService` (abrir, contar, recontar, fechar, cancelar, reabrir) gerando o ajuste da EST-01b.
- [ ] Endpoints `/api/v1/inventory/inventory-counts`; telas, contagem móvel e ajudas.

## Quality gates
- Agentes previstos: @dev, @qa.
- Gate: CA 3, 4, 7 e 8 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026.

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto registrados sem decidir | OK |

- **Correções feitas na validação:** o inventário não muda saldo por conta própria: gera um ajuste da EST-01b (CA 7), com a segregação de lá (CA 8); `system_qty` fora do DTO do contador (CA 11).
- **Pré-condições para `Ready`:** ponto em aberto 3.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
