# EST-01b — Ajuste de estoque com motivo e aprovação

| Campo | Valor |
|---|---|
| ID | EST-01b |
| Título | Ajuste de estoque (sobra ou falta sem documento de origem) com motivo obrigatório, lançado por quem tem `inventory.adjust` e aprovado por outra pessoa com `inventory.approve`; o saldo e o custo médio só mudam na aprovação; reprovação e cancelamento sem efeito |
| Tamanho | M (quebra da EST-01) |
| Status | Draft |
| Requisitos | R-EST-6 (ajuste de inventário com motivo e aprovação) · R-EST-5 · R-EST-7 · R-COM-2 · R-PRP-5 · R-PRP-7 · R-SEC-5 · R-SEC-10 (o que "aprovar" significa no Estoque) |
| Dependências | EST-01a (motor `IStockMovements`, saldo, custo médio), FND-06b/06c (grupos, permissão efetiva), FND-09e |
| Bloqueia | EST-05 (o inventário gera ajustes por este fluxo) |
| Ordem de execução | 3 (Marco 3.1 "Estoque confiável") |

## User story
Como **almoxarife**, quero **lançar a diferença entre o que o sistema diz e o que existe na prateleira, com o motivo**, e como **proprietário**, quero **que todo ajuste passe pela minha aprovação (ou de quem eu designar)**, para **o estoque não mudar sem documento e sem alguém responder por isso**.

## Contexto e referências
- PRD R-EST-6 ("ajuste de inventário (com motivo e aprovação)"); SEC-10 ("Cada módulo explica o que 'aprovar' significa nele"): no `ModuleCatalog`, `inventory.approve` = "ajuste de inventário" e a ação específica `inventory.adjust` = "Lançar ajuste de inventário, que muda o saldo sem documento de origem" (ADR 0003).
- ADR 0008: `*.approve` é reservada ao Proprietário para conceder; `inventory.adjust` não está na lista de reservadas.
- `04-fluxos-estados` §3.1 (custo médio) e §3.6 (regra do solicitante que não aprova o próprio pedido, D6 de 20/10/2026: exceção do Proprietário único e o parâmetro `finance.owner_can_approve_own`, que é do Finance).
- `03-modelo-dados`: `stock_movements.type = Adjustment`; o documento do ajuste não está no modelo (proposta abaixo).
- Riscos (`06`): "Aprovador único vira gargalo".

## Critérios de aceite
**Permissões do seed**
1. **Dado** um tenant novo ou existente no próximo deploy, **quando** o seed patch `inventory-adjust-warehouse` (proposta) roda, **então** o grupo Almoxarifado do seed ganha `inventory.adjust`, uma vez, com o `perm_version` incrementado; `inventory.approve` continua só no Proprietário (reservada, ADR 0008).

**Lançar o ajuste**
2. **Dado** `inventory.adjust`, **quando** clico "Lançar ajuste", escolho o local e adiciono linhas (item, lote quando o item controla, quantidade contada **ou** diferença com sinal, na unidade de estoque ou convertida), informo o motivo (lista: Perda/avaria, Erro de lançamento, Sobra encontrada, Contagem, Outro; "Outro" exige descrição até 200) e clico "Enviar para aprovação", **então** nasce o ajuste `Pending` com número (proposta `AJ-AAAA-0001`, Pontos em aberto 1), cada linha mostrando saldo do sistema, quantidade nova, diferença e o valor estimado (diferença × custo médio do momento), e **o saldo não muda**; o comando exige `Idempotency-Key`.
3. **Dado** um ajuste `Pending`, **quando** gravado, **então** os usuários que podem aprová-lo (com `inventory.approve` efetivo, diferentes do autor) recebem a notificação "Ajuste {número} de {local} aguardando aprovação: {n} itens, {valor} de diferença." com link direto para o ajuste (R-PRP-5), com `dedup_key` `inventory-adjustment:{id}`.
4. **Dado** uma linha que deixaria o saldo do lote negativo ou abaixo do reservado, **quando** envio, **então** 422 na linha (`lines[n].quantity`) "O saldo ficaria {x}, abaixo do reservado ({y}) para OS." (ver Pontos em aberto 3).

**Aprovar, reprovar, cancelar**
5. **Dado** `inventory.approve` e um ajuste `Pending` de outra pessoa, **quando** clico "Aprovar ajuste" e confirmo no diálogo grande com o impacto (por item: saldo antes → depois, valor da diferença; total de sobras e faltas em R$), **então** na mesma transação o ajuste fica `Approved` (com aprovador e data), cada linha vira um movimento `Adjustment` pelo `IStockMovements` (sobra = entrada pelo custo médio do momento; falta = saída pelo custo médio do momento), o saldo e o custo médio mudam e a mensagem diz "Ajuste aprovado"; o autor é notificado.
6. **[Segurança]** **Dado** o saldo mudou entre o envio e a aprovação, **quando** aprovo, **então** o servidor recalcula a diferença sobre o saldo **atual** quando a linha foi lançada como "quantidade contada" e mantém a diferença quando foi lançada como "diferença"; se alguma linha ficaria negativa, 409 `insufficient_stock` com a linha, e nada é gravado; o diálogo mostra "O saldo mudou desde o envio" com os valores novos antes de confirmar.
7. **[Segurança]** **Dado** o autor do ajuste, **quando** tenta aprová-lo, **então** 403 "Quem lança o ajuste não o aprova."; exceção só para o Proprietário quando o grupo Proprietário tem um único membro (mesma regra da D6, `04-fluxos` §3.6), gravando `self_approved = true` e notificando o próprio Proprietário com a marca "Autoaprovado".
8. **Dado** `inventory.approve`, **quando** clico "Reprovar ajuste" com comentário obrigatório, **então** o ajuste fica `Rejected`, nenhum movimento nasce e o autor é notificado com o comentário.
9. **Dado** o autor e um ajuste `Pending`, **quando** clico "Cancelar ajuste" com motivo, **então** fica `Cancelled`, sem movimento.
10. **[Segurança]** **Dado** duas aprovações simultâneas do mesmo ajuste, **quando** chegam juntas, **então** só uma grava (transição pela entidade rastreada com `xmin`); os movimentos não duplicam (idempotência por `(Adjustment, ajusteId, linha)` no `IStockMovements`).
11. **Dado** um ajuste `Approved`, **quando** alguém tenta editar, **então** 409; a correção é outro ajuste, ou o estorno do ajuste pelo próprio documento ("Estornar ajuste", com `inventory.approve`, motivo e o mesmo fluxo de impacto), que gera os movimentos inversos (EST-01a CA 13).

**Consulta e permissões**
12. **Dado** `inventory.view`, **quando** abro "Ajustes", **então** vejo número, local, autor, data, situação (Aguardando aprovação info, Aprovado ok, Reprovado crítico, Cancelado neutro), itens e valor da diferença; "Aguardando você" mostra só os que o usuário pode aprovar (com `inventory.approve`, não sendo o autor).
13. **Dado** o módulo `inventory` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; lançar sem `inventory.adjust`, aprovar/reprovar/estornar sem `inventory.approve`: 403 e o botão some. `RequireModule` e `perm:`; IDOR em `/adjustments/{id}/lines/{lineId}`.
14. **Dado** qualquer mudança do ajuste, **quando** abro "Histórico de alterações", **então** vejo antes/depois, usuário e data; a aba "Decisão" mostra autor, aprovador, comentário e a marca "Autoaprovado" quando houver.

## Regras de negócio e técnicas
- **Documento do ajuste** (proposta): `inventory.stock_adjustments` (`number`, `location_id`, `reason`, `reason_detail`, `status` Pending/Approved/Rejected/Cancelled, `requested_by`, `decided_by`, `decided_at`, `decision_comment`, `self_approved`, `reversed_at`, `reversed_by`, `reversal_reason`, `xmin`) e `inventory.stock_adjustment_lines` (`adjustment_id`, `item_id`, `lot_id`, `mode` Counted/Delta, `system_qty_at_request`, `counted_qty`, `delta_qty`, `unit_cost_estimate`).
- **Efeito só na aprovação:** o ajuste pendente não reserva nem bloqueia saldo; a aprovação recalcula (CA 6).
- **Custo:** sobra entra pelo custo médio do momento (não muda o custo médio); falta sai pelo custo médio; valor da diferença = Σ |Δ| × custo, 2 casas na linha.
- **Notificação** síncrona pelo `INotificationService` (ADR 0019 §18) com `dedup_key`.
- **Sem efeito no custo do projeto:** ajuste não tem projeto (perda de material de OS é saída pela EST-03, com projeto).
- Guid v7; `xmin`; auditoria `IAuditable` em ajuste e linhas; quantidades `numeric(18,4)`, custos `numeric(18,6)`, valores `numeric(18,2)`.

## Permissões, eventos e tabelas
- **Permissões:** `inventory.adjust` (lançar e cancelar o próprio), `inventory.approve` (aprovar, reprovar, estornar), `inventory.view`. Seed patch **proposto** `inventory-adjust-warehouse` (CA 1).
- **Eventos:** nenhum entre módulos (notificações dentro do módulo, síncronas).
- **Tabelas:** `inventory.stock_adjustments`, `inventory.stock_adjustment_lines` (propostas); movimentos `Adjustment` em `inventory.stock_movements`.

## Telas e estados
- **Fábrica → Estoque → Ajustes** (`/app/estoque/ajustes`, detalhe `/app/estoque/ajustes/$id`, aba nova no `modules.ts`): lista com "Aguardando você" no topo; detalhe com cabeçalho (número, chip, local, autor), "facts" (itens, sobras, faltas, valor), abas na URL: Itens, Decisão, Histórico de alterações.
- **Verbos:** "Enviar para aprovação" / "Ajuste enviado para aprovação"; "Aprovar ajuste" / "Ajuste aprovado"; "Reprovar ajuste" / "Ajuste reprovado"; "Cancelar ajuste" / "Ajuste cancelado"; "Estornar ajuste" / "Ajuste estornado".
- **Estados:** carregando; vazio ("Nenhum ajuste lançado." + "Lançar ajuste"); erro; modo leitura com a faixa "Aprovado em {data} por {usuário}. Para corrigir, lance outro ajuste ou estorne este."
- **Propositivo:** saldo do sistema preenchido em cada linha; "quantidade contada" como modo padrão (o usuário digita o que vê); valor estimado ao vivo no rodapé; o diálogo de aprovação lista o que mudou desde o envio (CA 6); a partir do saldo com "Abaixo do mínimo", `NextSteps` "Gerar solicitação de compra" (a partir da CMP-01).
- **400 px:** linhas em cartões (item, sistema, contado, diferença); diálogo de aprovação em tela cheia; lista com número, local, valor e situação.
- **Ajuda:** `Ajuda/estoque-ajustes.md`, `Ajuda/estoque-ajuste.md` (abas `itens` padrão, `decisao`, `historico`); `<Dica>` em "Quantidade contada × diferença" e "Autoaprovado".

## Plano de testes
- **Unidade:** cálculo da diferença nos dois modos e recálculo na aprovação (CA 2, 6); regra do autor e exceção do Proprietário único (CA 7).
- **Integração (Testcontainers, obrigatória: movimenta estoque):** seed patch (CA 1); pendente sem efeito no saldo (CA 2); notificação aos aprovadores elegíveis sem o autor (CA 3, 12); negativo e reservado (CA 4); aprovação gera movimentos e custo médio, com saldo mudado entre envio e aprovação (CA 5, 6); autor não aprova e Proprietário único (CA 7); reprovar e cancelar (CA 8, 9); aprovação concorrente (CA 10); estorno do ajuste (CA 11); três camadas e IDOR (CA 13); auditoria (CA 14).
- **E2E (Playwright):** almoxarife lança ajuste, Proprietário aprova pelo link da notificação; tela a 400 px.

## Fora do escopo
- Contagem de inventário por local: EST-05 (gera ajustes por este fluxo).
- Alçada por valor no ajuste e aprovador por grupo: Pontos em aberto 2.

## Pontos em aberto
1. **Numeração do ajuste** (PO): o `04-fluxos` §4 não tem o tipo. Proposta: `AJ-AAAA-0001`.
2. **Alçada por valor** (PO): R-EST-6 pede aprovação, sem faixa. Proposta: todo ajuste é aprovado por quem tem `inventory.approve` (hoje só o Proprietário); faixas por valor ou o motor de alçadas da FIN-03b (`approval_rules` com outro `document_type`) só se o PO pedir.
3. **Ajuste que deixa o saldo abaixo do reservado** (PO): proposta: recusar (CA 4) e orientar a liberar a reserva ou registrar a falta pela OS; alternativa: aceitar e transformar a diferença em falta de reserva (EST-02).
4. **Autoaprovação do Proprietário** (PO): proposta: a mesma exceção da D6 (grupo Proprietário com um único membro), sem parâmetro novo; o parâmetro `finance.owner_can_approve_own` é do Finance e não vale aqui.
5. **Permissão do seed** (PO): seed patch proposto do CA 1.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers) e E2E passando.
- [ ] Migration criada (não destrutiva), testada sobre cópia do banco do tenant `konebras`; seed patch de permissões.
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Telas com estados, inclusive a 400 px.
- [ ] Ajuda de cada tela e aba; teste de cobertura passando.
- [ ] Confirmação com impacto, notificação acionável e próximos passos avaliados.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado (tabelas propostas); story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `StockAdjustment` e `StockAdjustmentLine` (Domain) com a máquina de estados.
- [ ] `StockAdjustmentService` (enviar, aprovar, reprovar, cancelar, estornar) usando o `IStockMovements`.
- [ ] Regra do autor (reaproveitar a lógica do Proprietário único do Identity por `IUserDirectory.OwnerIdsAsync`).
- [ ] Endpoints `/api/v1/inventory/adjustments`; telas, ajuda e seed patch.

## Quality gates
- Agentes previstos: @dev, cybersecurity (segregação de função e concorrência), @qa.
- Gate: CA 2, 5, 6, 7 e 10 obrigatórios.

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

- **Correções feitas na validação:** saldo só muda na aprovação e é recalculado sobre o saldo atual (CA 6); segregação autor × aprovador com a exceção da D6 (CA 7); estorno pelo documento (CA 11); alçada por valor deixada como ponto em aberto, sem decidir.
- **Pré-condições para `Ready`:** pontos em aberto 1, 3 e 5.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da EST-01) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
