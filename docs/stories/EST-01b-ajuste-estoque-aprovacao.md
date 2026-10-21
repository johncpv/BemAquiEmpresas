# EST-01b — Ajuste de estoque com motivo e aprovação

| Campo | Valor |
|---|---|
| ID | EST-01b |
| Título | Ajuste de estoque (sobra ou falta sem documento de origem), num modo só por ajuste (quantidade contada ou diferença), com motivo obrigatório, lançado por quem tem `inventory.adjust` e aprovado por outra pessoa com `inventory.approve`, que decide sem editar; o saldo e o custo médio só mudam na aprovação; modo Delta pronto para o inventário (EST-05) |
| Tamanho | M (quebra da EST-01) |
| Status | Ready |
| Requisitos | R-EST-6 (ajuste de inventário com motivo e aprovação) · R-EST-5 · R-EST-7 · R-COM-2 · R-PRP-5 · R-PRP-7 · R-SEC-5 · R-SEC-10 (o que "aprovar" significa no Estoque) |
| Dependências | EST-01a (motor `IStockMovements`, posição, custo médio, `SoleOwnerRule`), EST-02 (reservado na posição, reservas do item) |
| Bloqueia | EST-05 |
| Ordem de execução | 22 (Marco 3.4 "Fechar e rastrear") |

## User story
Como **almoxarife**, quero **lançar a diferença entre o que o sistema diz e o que existe na prateleira, com o motivo**, e como **proprietário**, quero **que todo ajuste passe pela minha aprovação (ou de quem eu designar)**, para **o estoque não mudar sem documento e sem alguém responder por isso**.

## Contexto e referências
- PRD R-EST-6 ("ajuste de inventário (com motivo e aprovação)"); SEC-10 ("Cada módulo explica o que 'aprovar' significa nele"): no `ModuleCatalog`, `inventory.approve` = "ajuste de inventário" e a ação específica `inventory.adjust` = "Lançar ajuste de inventário, que muda o saldo sem documento de origem" (ADR 0003).
- **ADR 0023**: §1 (ajuste aprovado é chamada síncrona ao motor); §2 (reservado na posição); §5 (`SoleOwnerRule`, o aprovador decide sobre o que foi enviado, sem editar); §7 e §2.1 "Outros" da revisão (o inventário gera o ajuste em modo Delta).
- **ADR 0024 §4** (`IApprovalRouting` e o mapa tipo de documento → permissão do aprovador, no Finance).
- **Revisão pré-Fase 3** (`docs/revisoes/2026-10-21-revisao-pre-fase3.md`): D7, D15, tabela 1.1 itens 13 a 17, §2.1 "Outros" (ajuste em modo Delta), §2.2 (o aprovador do ajuste decide sobre o que foi enviado), §2.3 padrões 4, 5, 8 e 9 e o achado "um modo por ajuste".
- ADR 0008: `*.approve` é reservada ao Proprietário para conceder; `inventory.adjust` não está na lista de reservadas.
- `04-fluxos-estados` §3.1 (custo médio) e §3.6 (o solicitante não aprova o próprio pedido; exceção do Proprietário único da D6 de 20/10/2026; o parâmetro `finance.owner_can_approve_own` é do Finance e não vale aqui).
- `03-modelo-dados`: `stock_movements.type = Adjustment`; o documento do ajuste entra nesta story.
- Riscos (`06`): "Aprovador único vira gargalo".

## Critérios de aceite
**Permissões do seed**
1. **Dado** um tenant novo ou existente no próximo deploy, **quando** o seed patch `inventory-adjust-warehouse` roda, **então** o grupo Almoxarifado do seed ganha `inventory.adjust`, uma vez, com o `perm_version` incrementado; `inventory.approve` continua só no Proprietário (reservada, ADR 0008).

**Lançar o ajuste**
2. **Dado** `inventory.adjust`, **quando** clico "Lançar ajuste", escolho o local e **o modo do ajuste** ("Quantidade contada", padrão, ou "Diferença"), adiciono linhas (item, lote quando o item controla, e a quantidade contada ou a diferença com sinal, conforme o modo, na unidade de estoque ou convertida), informo o motivo (lista: Perda/avaria, Erro de lançamento, Sobra encontrada, Contagem, Outro; "Outro" exige descrição até 200) e clico "Enviar para aprovação", **então** nasce o ajuste `Pending` com número `AJ-AAAA-0001` (D15), cada linha mostrando físico, reservado e disponível do sistema, quantidade nova, diferença e o valor estimado (diferença × custo médio do momento), e **o saldo não muda**; o comando exige `Idempotency-Key`. O modo é do ajuste inteiro, não da linha: trocar o modo com linhas preenchidas pede confirmação e limpa as quantidades.
3. **Dado** um ajuste `Pending`, **quando** gravado, **então** os usuários que podem aprová-lo (com `inventory.approve` efetivo, diferentes do autor) recebem a notificação "Ajuste {número} de {local} aguardando aprovação: {n} itens, {valor} de diferença." com link direto para o ajuste (R-PRP-5), com `dedup_key` `inventory-adjustment:{id}`.
4. **Dado** uma linha que deixaria o físico do lote negativo ou o físico da posição **abaixo do reservado**, **quando** envio, **então** 422 na linha (`lines[n].quantity`) "O saldo ficaria {x}, abaixo do reservado ({y}) para OS." com a ação "Ver reservas do item" (as reservas daquele item e local, EST-02); o ajuste não transforma diferença em falta de reserva.

**Aprovar, reprovar, cancelar**
5. **Dado** `inventory.approve` e um ajuste `Pending` de outra pessoa, **quando** clico "Aprovar ajuste" e confirmo no diálogo grande com o impacto (por item: saldo antes → depois, valor da diferença; total de sobras e faltas em R$), **então** na mesma transação o ajuste fica `Approved` (com aprovador e data), cada linha vira um movimento `Adjustment` **síncrono** pelo `IStockMovements` (sobra = entrada pelo custo médio do momento; falta = saída pelo custo médio do momento; trava e ordem da EST-01a), o saldo muda e a mensagem diz "Ajuste aprovado"; o autor é notificado.
6. **[Segurança]** **Dado** o aprovador, **quando** decide, **então** decide **sobre o que foi enviado, sem editar**: o endpoint de decisão aceita só a decisão e o comentário (linha, quantidade, lote ou modo no corpo: 422); para mudar, ele reprova com comentário e o autor lança outro ajuste.
7. **[Segurança]** **Dado** o saldo mudou entre o envio e a aprovação, **quando** aprovo, **então** o servidor recalcula a diferença sobre o saldo **atual** no modo "Quantidade contada" e mantém a diferença no modo "Diferença"; a regra do CA 4 é conferida de novo; se alguma linha ficaria negativa ou abaixo do reservado, 409 `insufficient_stock` com a linha, e nada é gravado; o diálogo mostra "O saldo mudou desde o envio" com os valores novos antes de confirmar.
8. **[Segurança]** **Dado** o autor do ajuste, **quando** tenta aprová-lo, **então** 403 "Quem lança o ajuste não o aprova."; a exceção é a do Proprietário único, decidida pelo `SoleOwnerRule` (BuildingBlocks, EST-01a), que grava `self_approved = true`, mostra a marca "Autoaprovado" e notifica o Proprietário.
9. **Dado** `inventory.approve`, **quando** clico "Reprovar ajuste" com comentário obrigatório, **então** o ajuste fica `Rejected`, nenhum movimento nasce e o autor é notificado com o comentário.
10. **Dado** o autor e um ajuste `Pending`, **quando** clico "Cancelar ajuste" com motivo, **então** fica `Cancelled`, sem movimento.
11. **[Segurança]** **Dado** duas aprovações simultâneas do mesmo ajuste, **quando** chegam juntas, **então** só uma grava (transição pela entidade rastreada com `xmin`); os movimentos não duplicam (idempotência por `(Adjustment, ajusteId, linha, tipo)` no `IStockMovements`, ADR 0023 §4).
12. **Dado** um ajuste `Approved`, **quando** alguém tenta editar, **então** 409; a correção é outro ajuste, ou o estorno do ajuste pelo próprio documento ("Estornar ajuste", com `inventory.approve`, por quem não é o autor do ajuste, salvo o `SoleOwnerRule`; com motivo e o mesmo diálogo de impacto), que gera os movimentos inversos (EST-01a CA 23).

**Modo Delta para o inventário e alçada**
13. **Dado** o serviço interno do ajuste, **quando** a contagem de inventário (EST-05) pede um ajuste com origem `InventoryCount`, **então** o ajuste nasce no modo "Diferença" (Delta), com a referência da contagem, e segue o mesmo fluxo de aprovação deste documento (CA 3 a 12); nesta story o caminho é entregue e testado chamando o serviço direto (sem tela de inventário).
14. **Dado** a regra de alçada, **quando** um ajuste é enviado, **então** **todo ajuste** é aprovado por quem tem `inventory.approve`, sem faixa de valor; o tipo de documento `StockAdjustment` fica declarado no mapa tipo → permissão do aprovador do `IApprovalRouting` (`inventory.approve`, ADR 0024 §4), sem regra ativa e sem chamada ao motor de alçadas nesta story.

**Consulta e permissões**
15. **Dado** `inventory.view`, **quando** abro "Ajustes", **então** vejo número, local, modo, autor, data, situação (Aguardando aprovação info, Aprovado ok, Reprovado crítico, Cancelado neutro), itens e valor da diferença; para quem pode aprovar, o filtro padrão é **"Aguardando você"** (os que o usuário pode aprovar, com `inventory.approve`, não sendo o autor).
16. **Dado** o módulo `inventory` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; lançar sem `inventory.adjust`, aprovar/reprovar/estornar sem `inventory.approve`: 403 e o botão some. `RequireModule` e `perm:`; IDOR em `/adjustments/{id}/lines/{lineId}`.
17. **Dado** qualquer mudança do ajuste, **quando** abro "Histórico de alterações", **então** vejo antes/depois, usuário e data; a aba "Decisão" mostra autor, aprovador, comentário e a marca "Autoaprovado" quando houver.

**Bloqueio por inventário (EST-05)**
18. **[Segurança]** **Dado** o ponto de bloqueio `ILocationCountLock` (interno do Inventory), **quando** alguém lança, aprova ou estorna um ajuste manual, ou estorna um movimento manual (EST-01a CA 20), **então** o serviço consulta o bloqueio do local antes de gravar; com contagem aberta no local, 409 `location_counting` "Há um inventário em contagem em {local}: {número}. Ajustes e estornos manuais ficam bloqueados até fechar ou cancelar.", com o link para o inventário, e nada é gravado (ponto 91). Nesta story o bloqueio nasce com a implementação que nunca bloqueia e é testado com um dublê; a EST-05 liga a implementação real (EST-05 CA 6). O ajuste de origem `InventoryCount` (CA 13) não passa por esse bloqueio.

## Regras de negócio e técnicas
- **Documento do ajuste:** `inventory.stock_adjustments` (`number` AJ, `location_id`, `mode` Counted/Delta, `origin` Manual/InventoryCount, `origin_id`, `reason`, `reason_detail`, `status` Pending/Approved/Rejected/Cancelled, `requested_by`, `decided_by`, `decided_at`, `decision_comment`, `self_approved`, `reversed_at`, `reversed_by`, `reversal_reason`, `xmin`) e `inventory.stock_adjustment_lines` (`adjustment_id`, `item_id`, `lot_id`, `system_qty_at_request`, `counted_qty`, `delta_qty`, `unit_cost_estimate`).
- **Efeito só na aprovação:** o ajuste pendente não reserva nem bloqueia saldo; a aprovação recalcula (CA 7) e chama o motor de forma síncrona (ADR 0023 §1).
- **Custo:** sobra entra pelo custo médio do momento (não muda o custo médio); falta sai pelo custo médio; valor da diferença = Σ |Δ| × custo, 2 casas na linha.
- **Reservado:** conferido na posição (ADR 0023 §2); o ajuste nunca deixa o físico abaixo do reservado.
- **Notificação** síncrona pelo `INotificationService` (ADR 0019 §18) com `dedup_key`.
- **Sem efeito no custo do projeto:** ajuste não tem projeto (perda de material de OS é saída pela EST-03, com projeto).
- Guid v7; `xmin`; auditoria `IAuditable` em ajuste e linhas; quantidades `numeric(18,4)`, custos `numeric(18,6)`, valores `numeric(18,2)`.

## Permissões, eventos e tabelas
- **Permissões:** `inventory.adjust` (lançar e cancelar o próprio), `inventory.approve` (aprovar, reprovar, estornar), `inventory.view`. Seed patch `inventory-adjust-warehouse` (CA 1).
- **Eventos:** nenhum entre módulos (notificações dentro do módulo, síncronas).
- **Contratos:** nenhum novo; o mapa do `IApprovalRouting` ganha o tipo `StockAdjustment` (CA 14).
- **Tabelas:** `inventory.stock_adjustments`, `inventory.stock_adjustment_lines`; movimentos `Adjustment` em `inventory.stock_movements`.

## Telas e estados
Padrões de tela da Fase 3: 4, 5, 8, 9.
- **Fábrica → Estoque → Ajustes** (aba "Operação"; `/app/estoque/ajustes`, detalhe `/app/estoque/ajustes/$id`): lista com o filtro padrão "Aguardando você"; detalhe com cabeçalho (número, chip, local, modo, autor), "facts" (itens, sobras, faltas, valor), abas na URL: Itens, Decisão, Histórico de alterações.
- **Verbos:** "Enviar para aprovação" / "Ajuste enviado para aprovação"; "Aprovar ajuste" / "Ajuste aprovado"; "Reprovar ajuste" / "Ajuste reprovado"; "Cancelar ajuste" / "Ajuste cancelado"; "Estornar ajuste" / "Ajuste estornado".
- **Estados:** carregando; vazio ("Nenhum ajuste lançado." + "Lançar ajuste"); vazio de "Aguardando você" ("Nada aguardando sua aprovação."); erro; modo leitura com a faixa "Aprovado em {data} por {usuário}. Para corrigir, lance outro ajuste ou estorne este."
- **Propositivo:** saldo do sistema preenchido em cada linha; "Quantidade contada" como modo padrão (o usuário digita o que vê); valor estimado ao vivo no rodapé; o diálogo de aprovação lista o que mudou desde o envio (CA 7); linha recusada pelo reservado leva direto a "Ver reservas do item"; a partir do saldo com "Abaixo do mínimo", `NextSteps` "Gerar solicitação de compra" (CMP-01).
- **400 px:** linhas em cartões (item, disponível, contado, diferença; físico e reservado na linha expansível); diálogo de aprovação em tela cheia; lista com número, local, valor e situação.
- **Ajuda:** `Ajuda/estoque-ajustes.md`, `Ajuda/estoque-ajuste.md` (abas `itens` padrão, `decisao`, `historico`); `<Dica>` em "Quantidade contada × diferença" e "Autoaprovado".

## Plano de testes
- **Unidade:** cálculo da diferença nos dois modos e recálculo na aprovação (CA 2, 7); regra do reservado (CA 4); regra do autor e `SoleOwnerRule` (CA 8).
- **Integração (Testcontainers, obrigatória: movimenta estoque):** seed patch (CA 1); pendente sem efeito no saldo e modo único (CA 2); notificação aos aprovadores elegíveis sem o autor (CA 3); negativo e reservado com a ação (CA 4); aprovação síncrona gera movimentos (CA 5); decisão sem edição (CA 6); saldo mudado entre envio e aprovação (CA 7); autor não aprova e Proprietário único (CA 8); reprovar e cancelar (CA 9, 10); aprovação concorrente (CA 11); estorno do ajuste (CA 12); ajuste Delta de origem `InventoryCount` pelo serviço (CA 13); mapa do `IApprovalRouting` (CA 14); "Aguardando você" (CA 15); três camadas e IDOR (CA 16); auditoria (CA 17); bloqueio do local com dublê do `ILocationCountLock`: lançar, aprovar e estornar recusados com 409 e nada gravado, ajuste `InventoryCount` liberado (CA 18).
- **E2E (Playwright):** almoxarife lança ajuste, Proprietário aprova pelo link da notificação; tela a 400 px.

## Fora do escopo
- Contagem de inventário por local e a implementação real do bloqueio do local durante a contagem: EST-05 (gera ajustes Delta por este fluxo; o ponto de consulta é desta story, CA 18).
- Alçada por faixa de valor e aprovador por grupo: só se o PO pedir (o tipo já fica previsto no mapa, CA 14).

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fase 3, tabela 1.1, itens 13–17):
1. (13) Numeração `AJ-AAAA-0001` (D15).
2. (14) Sem faixa de valor: todo ajuste com `inventory.approve`; o tipo fica previsto no motor de alçadas (CA 14).
3. (15) Saldo abaixo do reservado: recusado, com a ação "Ver reservas do item" (CA 4).
4. (16) Autoaprovação do Proprietário: regra D6 de 20/10 (Proprietário único), no helper `SoleOwnerRule`, com notificação (CA 8).
5. (17) Seed `inventory-adjust-warehouse` aprovado (CA 1).

**Pré-condição externa:** nenhuma.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] ADR 0023 §1, §5 e o modo Delta (§7) aplicados.
- [ ] Testes de unidade, integração (Testcontainers) e E2E passando.
- [ ] Migration criada (não destrutiva), testada sobre cópia do banco do tenant `konebras`; seed patch de permissões.
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Telas com estados, inclusive a 400 px.
- [ ] Ajuda de cada tela e aba; teste de cobertura passando.
- [ ] Confirmação com impacto, notificação acionável e próximos passos avaliados.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado (documento do ajuste); story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `StockAdjustment` e `StockAdjustmentLine` (Domain) com a máquina de estados e o modo no documento.
- [ ] `StockAdjustmentService` (enviar, aprovar, reprovar, cancelar, estornar, criar Delta a partir da contagem) usando o `IStockMovements`, o `SoleOwnerRule` e o `ILocationCountLock` (implementação que nunca bloqueia até a EST-05).
- [ ] Tipo `StockAdjustment` no mapa do `IApprovalRouting`.
- [ ] Endpoints `/api/v1/inventory/adjustments`; telas, ajuda e seed patch.

## Quality gates
- Agentes previstos: @dev, cybersecurity (segregação de função, decisão sem edição e concorrência), @qa.
- Gate: CA 2, 4, 5, 6, 7, 8, 11, 13 e 18 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fase 3).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK (ajustado: renumerados) |
| 5 | CAs de segurança e de permissão (3 camadas) | OK (ajustado: §2.2) |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: Marco 3.4, depois da EST-02) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK |

- **Correções feitas na validação (21/10/2026, primeira validação):** saldo só muda na aprovação e é recalculado sobre o saldo atual; segregação autor × aprovador com a exceção da D6; estorno pelo documento; alçada por valor deixada como ponto em aberto, sem decidir.
- **Correções da revalidação (21/10/2026):** um modo por ajuste, no documento (achado de design, CA 2); o aprovador decide sobre o que foi enviado, sem editar (§2.2, CA 6); saldo abaixo do reservado da posição recusado com "Ver reservas do item" (CA 4); `SoleOwnerRule` com notificação (CA 8); modo Delta para a EST-05 (ADR 0023 §7, CA 13); todo ajuste com `inventory.approve` e o tipo previsto no motor de alçadas (CA 14); "Aguardando você" como filtro padrão (padrão 5, CA 15); aprovação síncrona pelo motor (CA 5); seed aprovado; pontos 13–17 decididos; dependência da EST-02 e ordem 22 no Marco 3.4; ponto de bloqueio do local por inventário (ponto 91, CA 18): lançar, aprovar e estornar recusados com 409 durante a contagem.
- **Pré-condições para `Ready`:** nenhuma de decisão de story.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da EST-01) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO (D7, D15, tabela 1.1 itens 13–17) e correções da revisão pré-Fase 3 (§2.1 ADR 0023 §1, §5, §7 e modo Delta; §2.2 EST-01b; §2.3 padrões 4, 5, 8, 9 e "um modo por ajuste"); CAs renumerados; nova ordem (Marco 3.4, ordem 22) | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
