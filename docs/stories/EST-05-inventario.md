# EST-05 — Inventário (contagem por local)

| Campo | Valor |
|---|---|
| ID | EST-05 |
| Título | Inventário por local: abertura com a foto do saldo, contagem cega por item e lote (inclusive no celular, com o kit chão de fábrica), recontagem das divergências por outra pessoa, e fechamento que gera um ajuste de estoque em modo Delta pelo fluxo de aprovação da EST-01b; ajuste e estorno manual bloqueados no local durante a contagem |
| Tamanho | M |
| Status | Ready |
| Requisitos | R-EST-9 (inventário: contagem por local, ajuste) · R-EST-6 (ajuste de inventário com motivo e aprovação) · R-EST-5 · R-PRP-7 · R-SEC-5 |
| Dependências | EST-01b (ajuste com aprovação), EST-02 (reservado) |
| Bloqueia | — |
| Ordem de execução | 23 (Marco 3.4 "Fechar e rastrear") |

## User story
Como **almoxarife**, quero **contar o estoque de um local sem ver o que o sistema diz, recontar o que deu diferente e mandar o resultado para aprovação**, e como **proprietário**, quero **aprovar a diferença de uma vez, com o valor**, para **o saldo do sistema voltar a bater com a prateleira**.

## Contexto e referências
- PRD §5.9 R-EST-9 e R-EST-6; backlog EST-05: "Contagem por local, divergências, ajuste aprovado".
- `03-modelo-dados` → `inventory_counts / inventory_count_lines` (`location_id`, `status`, `item_id`, `lot_id`, `system_qty`, `counted_qty`, `adjusted`).
- EST-01b: o ajuste nasce `Pending`, só muda o saldo na aprovação, todo ajuste exige `inventory.approve` de outra pessoa (ponto 14), o aprovador decide sobre o que foi enviado sem editar, e o saldo abaixo do reservado é recusado com a ação "Ver reservas do item" (ponto 15). As linhas do ajuste têm modo `Counted` ou `Delta`.
- EST-01a: saldos, lotes e o motor `IStockMovements` (já entregue).
- Revisão pré-Fase 3: `docs/revisoes/2026-10-21-revisao-pre-fase3.md` (D7, D15, tabela 1.1 itens 89 a 91, §2.1 "Outros: inventário gera o ajuste em modo Delta", §2.2 EST-05, §2.3 padrão 1).
- ADR 0023 §1 (efeito de estoque síncrono), §4 (movimento imutável), §5 (segregação nas manuais, `SoleOwnerRule`, nenhum custo no chão de fábrica) e §7 (numeração `INV-AAAA-0001`).

## Critérios de aceite
**Abrir**
1. **Dado** `inventory.adjust`, **quando** clico "Novo inventário", escolho o local e o escopo (todos os itens com saldo ou movimento no local, ou um filtro por categoria/tipo) e clico "Abrir inventário", **então** nasce o inventário `Counting` com número `INV-2026-0001` (D15), uma linha por item e lote com o **saldo do sistema na abertura** (`system_qty`) e o horário da foto (`snapshot_at`), com `Idempotency-Key`.
2. **Dado** um local com inventário `Counting`, **quando** tento abrir outro no mesmo local, **então** 409 "Já há um inventário aberto em {local}: {número}."

**Contar**
3. **Dado** `inventory.create` (contador), **quando** abro a contagem (no backoffice ou no celular, `/app/estoque/inventarios/$id/contagem`), **então** vejo os itens e lotes **sem o saldo do sistema** (contagem cega, ponto 90), digito a quantidade contada na unidade de estoque (ou convertida), posso incluir um item ou lote que não estava na lista ("Encontrei outro item") e cada linha grava quem contou e quando.
4. **Dado** a contagem pelo celular, **quando** abro a tela, **então** ela usa o kit chão de fábrica da PRD-03a (padrão 1): tela dedicada sem o dock, alvos de 56 × 56 px com 8 px entre eles, texto de 18 px, teclado numérico, confirmação em folha de tela cheia, região de mensagens própria e botão "Ajuda" no cabeçalho.
5. **Dado** movimentos no local durante a contagem (entradas, saídas, requisições, recebimentos e romaneios continuam permitidos, ponto 91), **quando** o inventário fecha, **então** a diferença de cada linha = contado − (saldo da abertura + movimentos do item e lote no local entre a foto e a contagem da linha, `movement_delta`), e a tela mostra "Teve movimento durante a contagem" na linha afetada.
6. **[Segurança]** **Dado** um local com inventário `Counting`, **quando** alguém tenta lançar ou aprovar um ajuste (EST-01b) ou estornar um movimento manual (EST-01a) nesse local, **então** 409 "Há um inventário em contagem em {local}: {número}. Ajustes e estornos manuais ficam bloqueados até fechar ou cancelar." com o link para o inventário (ponto 91).

**Divergências e recontagem**
7. **Dado** a contagem terminada, **quando** clico "Ver divergências" (`inventory.adjust`), **então** vejo as linhas com diferença (quantidade e valor pelo custo médio), ordenadas pelo valor absoluto, e posso marcar linhas para **recontagem**; a recontagem substitui a contagem anterior na diferença e mantém o histórico das duas.
8. **[Segurança]** **Dado** uma linha marcada para recontagem, **quando** o mesmo usuário que fez a primeira contagem tenta recontar, **então** o servidor recusa com 422 "A recontagem precisa ser feita por outra pessoa." (§2.2); a tela de recontagem só lista para o usuário as linhas que ele não contou.
9. **Dado** linhas não contadas, **quando** tento fechar, **então** o diálogo pergunta "Considerar {n} itens não contados como zero?" ou "Manter o saldo do sistema"; a escolha fica no inventário.

**Fechar e ajustar**
10. **Dado** `inventory.adjust`, **quando** clico "Fechar inventário" e confirmo no diálogo de impacto (sobras, faltas e valor líquido, antes → depois por linha; padrão 4), **então** o inventário vai a `Closed` e nasce **um ajuste** da EST-01b (`Pending`, motivo "Contagem", vínculo com o inventário) com uma linha em **modo Delta** por divergência (`delta_qty` = a diferença do CA 5), notificado aos aprovadores (§2.1, ADR 0023 §1); o saldo só muda quando o ajuste é aprovado, aplicando a diferença sobre o saldo do momento da aprovação, de modo que movimentos depois do fechamento não distorcem o resultado.
11. **[Segurança]** **Dado** o ajuste gerado, **quando** o aprovador é quem abriu o inventário ou contou ou recontou alguma linha, **então** a regra do autor da EST-01b vale para ele: não aprova, com a exceção do Proprietário único (`SoleOwnerRule`, marca "Autoaprovado" e notificação; ADR 0023 §5); o aprovador decide sobre o ajuste enviado, sem editar as linhas.
12. **Dado** o ajuste aprovado ou reprovado, **quando** abro o inventário, **então** a situação mostra "Ajuste {número} aprovado/reprovado" com link; reprovado, o inventário pode ser reaberto para recontagem ("Reabrir contagem"), voltando a bloquear o local (CA 6); aprovação recusada por saldo abaixo do reservado mostra a ação "Ver reservas do item" (EST-01b).
13. **Dado** um inventário `Counting`, **quando** clico "Cancelar inventário" com motivo, **então** fica `Cancelled`, sem ajuste, e o local é desbloqueado.

**Permissões, segurança e auditoria**
14. **Dado** o módulo `inventory` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; abrir, ver divergências, fechar e cancelar sem `inventory.adjust`; contar sem `inventory.create`: 403 e o botão some. `RequireModule` e `perm:`; IDOR em `/inventory-counts/{id}/lines/{lineId}`.
15. **[Segurança]** **Dado** quem só conta, **quando** recebe o DTO da contagem, **então** ele não tem `system_qty`, `movement_delta`, diferença nem custo (teste de contrato; contagem cega e nenhum custo na tela de chão de fábrica, ADR 0023 §5).
16. **[Segurança]** **Dado** dois contadores na mesma linha, **quando** salvam juntos, **então** só um grava (`xmin`) e o outro vê "Esta linha foi contada por {nome} agora há pouco." com "Recarregar".
17. **Dado** qualquer mudança, **quando** abro o histórico, **então** vejo cada contagem e recontagem com quem e quando, e a decisão do ajuste.

## Regras de negócio e técnicas
- **Tabelas** (03 + colunas desta story): `inventory_counts` (`number`, `location_id`, `scope` (jsonb), `status` Counting/Closed/Cancelled, `snapshot_at`, `opened_by`, `closed_by`, `closed_at`, `uncounted_policy`, `adjustment_id`, `xmin`); `inventory_count_lines` (`count_id`, `item_id`, `lot_id`, `system_qty`, `counted_qty`, `counted_by`, `counted_at`, `recount_of_line_id`, `movement_delta`).
- **Sem efeito direto no saldo:** todo efeito passa pelo ajuste da EST-01b, que chama o `IStockMovements` de forma síncrona na aprovação.
- **Contagem abaixo do reservado (decisão do PO de 23/10/2026, na EST-02):** contagem abaixo do reservado reduz primeiro as reservas das OS com a BOM liberada mais recentemente, gera a falta e avisa quem tem `inventory.approve`. (Desde a EST-02 o motor recusa ajuste negativo acima do disponível; o ajuste do inventário passa pelo `ReservationAllocator` para liberar essas reservas antes, na mesma transação.)
- **Ajuste em modo Delta:** a diferença é calculada no fechamento, com os movimentos do período (CA 5), e enviada como delta; a aprovação soma o delta ao saldo atual (nunca grava a quantidade contada por cima).
- **Bloqueio do local:** enquanto houver inventário `Counting` no local, a EST-01b e o estorno manual da EST-01a consultam o inventário e recusam (CA 6); os demais movimentos seguem.
- **Recontagem:** `counted_by` da recontagem ≠ `counted_by` da linha original, conferido no servidor (CA 8).
- Valor da divergência pelo custo médio do momento do fechamento; Guid v7; `xmin`; auditoria `IAuditable`; quantidades `numeric(18,4)`.

## Permissões, eventos e tabelas
- **Permissões:** `inventory.adjust` (abrir, fechar, cancelar, reabrir), `inventory.create` (contar), `inventory.approve` (aprova o ajuste gerado, EST-01b).
- **Eventos:** nenhum entre módulos.
- **Tabelas:** `inventory.inventory_counts`, `inventory.inventory_count_lines` (com as colunas acima); numeração `INV` no serviço de numeração (prefixo único, ADR 0023 §7).

## Telas e estados
- Padrões de tela da Fase 3: 1, 4, 9.
- **Fábrica → Estoque → Inventários** (`/app/estoque/inventarios`, aba do grupo "Operação"; detalhe `/app/estoque/inventarios/$id` com abas Contagem, Divergências, Histórico de alterações; contagem pelo celular `/app/estoque/inventarios/$id/contagem`, tela dedicada com o kit chão de fábrica).
- **Verbos:** "Abrir inventário" / "Inventário aberto"; "Salvar contagem" / "Contagem salva"; "Fechar inventário" / "Inventário fechado. Ajuste enviado para aprovação"; "Cancelar inventário" / "Inventário cancelado"; "Reabrir contagem" / "Contagem reaberta".
- **Estados:** carregando; vazio ("Nenhum inventário. Conte um local para acertar o saldo." + "Novo inventário"); progresso da contagem ("{x} de {y} itens contados"); erro; modo leitura do fechado; faixa no detalhe do local e nas telas de ajuste "Inventário {número} em contagem neste local" (CA 6).
- **Propositivo:** escopo padrão "itens com saldo no local"; ordem da lista pela posição/código; divergências ordenadas pelo valor; próximo passo "Ver ajuste" depois de fechar.
- **400 px:** contagem um item por cartão com teclado numérico; divergências com item, diferença e valor.
- **Ajuda:** `Ajuda/estoque-inventarios.md`, `Ajuda/estoque-inventario.md` (abas `contagem` padrão, `divergencias`, `historico`), `Ajuda/estoque-inventario-contagem.md`; `<Dica>` em "Contagem cega", "Movimento durante a contagem" e "Recontagem por outra pessoa".

## Plano de testes
- **Unidade:** diferença com movimentos no período (CA 5); política dos não contados (CA 9); recontador diferente (CA 8).
- **Integração (Testcontainers, obrigatória: gera ajuste de estoque):** abrir com foto e bloqueio de dois no mesmo local (CA 1, 2); contagem cega sem `system_qty` nem custo no DTO (CA 3, 15); movimentos durante a contagem (CA 5); ajuste e estorno manual recusados no local em contagem, e liberados depois de fechar ou cancelar (CA 6, 13); recontagem pela mesma pessoa recusada (CA 7, 8); fechar gera um ajuste `Pending` em modo Delta sem mudar o saldo, e a aprovação depois de um novo movimento aplica só a diferença (CA 10); segregação e Proprietário único (CA 11); reprovado reabre (CA 12); cancelar (CA 13); três camadas e IDOR (CA 14); concorrência na linha (CA 16); auditoria (CA 17).
- **E2E (Playwright):** abrir inventário, contar no celular (400 px, kit chão de fábrica), recontar com outro usuário, fechar e aprovar o ajuste com um terceiro usuário.

## Fora do escopo
- Inventário rotativo (contagem cíclica programada): fora do MVP.
- Coletor de dados e leitura de código de barras: fora do MVP.
- Bloqueio de entradas e saídas no local durante a contagem: não existe (ponto 91); só ajuste e estorno manual ficam bloqueados.

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fase 3, tabela 1.1, itens 89–91):
1. Numeração: `INV-AAAA-0001` (D15; CA 1).
2. Contagem cega: sim (CA 3, 15).
3. Movimentos durante a contagem: permitidos, com o ajuste gerado em modo Delta (CA 5, 10); ajuste e estorno manual no local bloqueados enquanto houver contagem (CA 6).

**Pré-condição externa:** nenhuma.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers) e E2E passando.
- [ ] Migration criada (não destrutiva), testada sobre cópia do banco do tenant `konebras`.
- [ ] Permissões só do `ModuleCatalog`.
- [ ] ADR 0023 aplicada (ajuste em modo Delta pelo motor, segregação da §5, numeração da §7).
- [ ] Telas com estados, inclusive a 400 px e a contagem com o kit chão de fábrica.
- [ ] Ajudas da lista, das abas e da contagem; teste de cobertura passando.
- [ ] Confirmação com impacto e próximo passo aplicados.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `InventoryCount` e `InventoryCountLine` (Domain); `CountDifferenceCalculator` puro.
- [ ] `InventoryCountService` (abrir, contar, recontar, fechar, cancelar, reabrir) gerando o ajuste Delta da EST-01b.
- [ ] `ILocationCountLock` interno do Inventory, consultado pelo ajuste (EST-01b) e pelo estorno manual (EST-01a).
- [ ] Endpoints `/api/v1/inventory/inventory-counts`; telas, contagem com o kit chão de fábrica e ajudas.

## Quality gates
- Agentes previstos: @dev, design (contagem no celular), @qa.
- Gate: CA 3, 5, 6, 8, 10, 11 e 15 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fase 3).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK (ajustado) |
| 5 | CAs de segurança e de permissão (3 camadas) | OK (ajustado: CA 6, 8, 15) |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: ordem 23) |
| 7 | Plano de testes cobre os CAs | OK (ajustado) |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK (ajustado: kit chão de fábrica) |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK |

- **Correções feitas na validação (21/10/2026, primeira validação):** o inventário não muda saldo por conta própria: gera um ajuste da EST-01b, com a segregação de lá; `system_qty` fora do DTO do contador.
- **Correções da revalidação (21/10/2026):** o ajuste gerado vai em modo Delta, sem recalcular pela quantidade contada (CA 10, §2.1); ajuste e estorno manual bloqueados no local durante a contagem (CA 6, ponto 91); recontador diferente do primeiro contador, conferido no servidor (CA 8, §2.2); DTO do contador sem custo (CA 15, D7); contagem pelo celular com o kit chão de fábrica (CA 4, padrão 1); `INV-AAAA-0001` e contagem cega definitivas; depende da EST-01b e da EST-02, Marco 3.4. CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma de decisão de story.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO (D7, D15, tabela 1.1 itens 89–91) e correções da revisão pré-Fase 3 (§2.1 ajuste em modo Delta, ADR 0023 §1, §5 e §7; §2.2 EST-05; §2.3 padrões 1, 4, 9); nova ordem (Marco 3.4, ordem 23) | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
