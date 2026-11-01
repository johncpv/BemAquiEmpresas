# PAT-02 — Patrimônio: movimentação de bens e baixa

| Campo | Valor |
|---|---|
| ID | PAT-02 |
| Título | Transferência de local, de responsável e de centro de trabalho com data, motivo e histórico imutável; transferência em lote; baixa do bem (venda, descarte, perda, doação) com motivo, data e valor, que tira o bem das listas ativas sem apagar nada; na venda, o próximo passo "Lançar título a receber" já preenchido |
| Tamanho | P |
| Status | Done |
| Requisitos | R-PAT-3 (movimentação, transferência de local/responsável, com histórico) · R-PRP-1 · R-PRP-2 · R-PRP-6 (D) · R-SEC-5 |
| Dependências | PAT-01a (bens), PAT-01b (atalho pela leitura), FIN-01 (título a receber manual) |
| Bloqueia | — |
| Ordem de execução | 13 (Marco 6.4 "Patrimônio") |

## User story
Como **supervisor**, quero **registrar quando um bem muda de lugar ou de responsável, e quando ele sai da empresa**, para **saber sempre onde está cada bem e com quem**.

## Contexto e referências
- PRD R-PAT-3; `03-modelo-dados` → `assets.asset_movements` (`asset_id`, `from/to location`, `from/to responsible`, `at`, `reason`); `assets.assets.status` (`Disposed`).
- `CLAUDE.md`: exclusão lógica em cadastro; histórico não é apagado.
- PAT-01a CA 4 (responsável desligado com "Troque o responsável").
- **Revisão pré-Fases 4 a 8** (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`): tabela 3.3 pontos 48 e 49; §4.4 padrão 7 (impacto em lista nas ações em lote). **ADR 0038** (`docs/adr/0038-patrimonio-manutencao-capacidade.md`).

## Critérios de aceite
1. **Dado** `assets.edit` e um bem `Active` ou `Maintenance`, **quando** clico "Registrar movimentação", informo o novo local, o novo responsável e/ou o novo centro de trabalho, a data (≤ hoje; padrão hoje) e o motivo (até 200 caracteres) e confirmo "Registrar movimentação", **então** nasce o movimento com os valores de antes e depois e o bem passa a mostrar os novos, na mesma transação; a mensagem de sucesso diz "Movimentação registrada".
2. **Dado** a movimentação, **quando** nada mudou, **então** 422 "Informe pelo menos um local, responsável ou centro diferente do atual."
3. **Dado** a aba Movimentações do bem, **quando** abro, **então** vejo os movimentos do mais recente ao mais antigo (data, de → para, motivo, quem registrou); movimento não é editado nem apagado (sem rota; teste de arquitetura).
4. **Dado** vários bens selecionados na lista, **quando** clico "Transferir selecionados" (D), escolho o destino e o motivo e confirmo no diálogo com o impacto em lista (cada bem, de → para), **então** um movimento por bem é gravado na mesma transação; um bem `Disposed` na seleção faz o comando falhar inteiro com a lista do que impede.
5. **Dado** um funcionário desligado que ainda é responsável por bens, **quando** abro Patrimônio → Bens com o filtro "Responsável desligado", **então** vejo esses bens e a ação "Transferir selecionados" já com o motivo sugerido "Responsável desligado" (propositivo).
6. **Dado** `assets.edit`, **quando** clico "Baixar bem", escolho o motivo (Venda, Descarte, Perda ou roubo, Doação, Outro), a data e, na venda, o valor (`numeric(18,2)`) e o comprador opcional (`IParties`), e confirmo "Baixar bem" (estilo `destructive`), **então** o bem passa a `Disposed`, some dos seletores e das listas padrão (filtro "Baixados") e o movimento de baixa fica no histórico; a recusa por OS de manutenção aberta e a inativação dos planos entram com a PAT-03a (CA 12 de lá).
7. **Dado** a baixa por venda, **quando** termina, **então** o `NextSteps` oferece "Lançar título a receber" para quem tem `finance.create`, que abre o título a receber manual da FIN-01 já com o comprador, o valor, a data e a descrição "Venda do bem {etiqueta} — {nome}" (nada é gravado no Financeiro sem a confirmação de lá); sem `finance.create`, o passo não aparece.
8. **Dado** um bem baixado por engano, **quando** clico "Reativar bem" com `assets.edit` e informo o motivo, **então** ele volta a `Active` com um movimento de reativação; nada é apagado; se já houver título da venda, a tela avisa "Cancele o título da venda no Financeiro, se for o caso."
9. **Dado** o módulo `assets` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; sem `assets.edit`, 403 e as ações somem; `RequireModule` e `perm:`; IDOR em `/assets/{id}/movements/{movId}`.
10. **Dado** qualquer movimento, baixa ou reativação, **quando** abro o histórico, **então** vejo usuário, data e IP.

## Regras de negócio e técnicas
- **Tabela:** `assets.asset_movements` (03) + `kind` (`Transfer`/`Disposal`/`Reactivation`), `from_site_id`/`to_site_id`, `from_employee_id`/`to_employee_id`, `from_work_center_id`/`to_work_center_id`, `moved_on` (`date`), `reason`, `disposal_reason`, `disposal_value`, `buyer_party_id`, `created_by`.
- **Baixa sem efeito financeiro automático:** a venda é registrada aqui; o título a receber só nasce no Financeiro, pelo próximo passo e com a confirmação de lá (CA 7, R-PRP-7).
- Guid v7; `IAuditable`; movimento imutável.

## Permissões, eventos e tabelas
- **Permissões:** `assets.edit`; `finance.create` para o próximo passo da venda.
- **Eventos:** `AssetDisposed` (`{ assetId, disposedOn }`), sem texto livre, para a Produção e o Planejamento deixarem de considerar a máquina pelo `IAssets` (sem consumidor obrigatório nesta story).
- **Tabelas:** `assets.asset_movements` (extensão).

## Telas e estados
- **Padrões comuns das Fases 4 a 8:** 7 (impacto em lista na transferência em lote; `destructive` na baixa).
- Na ficha (PAT-01a): "Registrar movimentação", "Baixar bem", "Reativar bem"; aba Movimentações.
- Na lista: seleção com "Transferir selecionados"; filtros "Responsável desligado" e "Baixados".
- **Estados:** vazio da aba ("Nenhuma movimentação. O bem está onde foi cadastrado."); erro; modo leitura.
- **Propositivo:** motivo sugerido para responsável desligado; "Lançar título a receber" depois da venda.
- **400 px:** movimentos em cartões; botões de `ErrorState`/`EmptyState` com 44 px.
- **Ajuda:** atualizar `Ajuda/patrimonio-bem.md` (aba `movimentacoes`) e `Ajuda/patrimonio-bens.md` ("Transferir em lote", "Baixar bem", "Venda do bem").

## Plano de testes
- **Unidade:** nada mudou (CA 2).
- **Integração (Testcontainers):** movimento com antes e depois (CA 1); imutável (CA 3); lote atômico (CA 4); filtro de desligado (CA 5); baixa (CA 6); próximo passo da venda só com `finance.create` e sem gravar no Financeiro (CA 7); reativação (CA 8); três camadas e IDOR (CA 9); histórico (CA 10).
- **E2E (Playwright):** transferir dois bens, vender um e abrir o título a receber pré-preenchido.

## Fora do escopo
- Baixa contábil do imobilizado: contadora.
- Termo de responsabilidade assinado pelo funcionário: fora do MVP.

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fases 4 a 8, tabela 3.3, pontos 48–49):
1. **Venda do bem:** `NextSteps` "Lançar título a receber", pré-preenchido, com a confirmação no Financeiro (CA 7).
2. **Termo de responsabilidade:** fora do MVP.

Decisões da revisão aplicadas: §4.4 padrão 7 (CA 4, 6).

**Pré-condição externa:** nenhuma.

## Definition of Done
- [x] Critérios de aceite atendidos.
- [x] Testes de unidade e integração (Testcontainers) e Vitest passando. E2E não escrito nem rodado nesta story (regra do PO de 31/10: fica para o fechamento da feature Patrimônio).
- [x] Migration criada (não destrutiva: `AssetMovementsPat02`, só a tabela nova).
- [x] Telas com estados; ajuda atualizada; teste de cobertura passando.
- [x] Próximo passo da venda aplicado.
- [x] Auditoria ligada.
- [x] OpenAPI e cliente Orval regenerados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [x] `AssetMovement` e as transições do bem; endpoints `/api/v1/assets/assets/{id}/movements`, `/dispose`, `/reactivate`, `/assets/transfer`; próximo passo com o link pré-preenchido da FIN-01.

## Quality gates
- Agentes previstos: @dev, @qa.
- Gate: CA 1, 3, 6 e 7 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fases 4 a 8).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: a regra da OS aberta foi para a PAT-03a, que vem depois) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** a baixa do bem entrou aqui (é o destino do "Excluir" de bem com histórico, PAT-01a CA 12), sem efeito financeiro automático.
- **Correções da revalidação (21/10/2026):** pontos 48 e 49 decididos: "Lançar título a receber" pré-preenchido depois da venda (CA 7, novo) e termo fora; impacto em lista no lote (padrão 7, CA 4); a recusa da baixa com OS de manutenção aberta e a inativação dos planos passaram para a PAT-03a, que vem depois na ordem; ordem 13 do Marco 6.4; CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma de decisão de story.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (tabela 3.3 pontos 48–49, ADR 0038, §4.4 padrão 7); nova ordem (Marco 6.4, ordem 13); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
| 01/11/2026 | 1.0 | Implementada: `assets.asset_movements` (migration `AssetMovementsPat02`, só a tabela nova), `AssetMovement` imutável (fábricas `Transfer`/`Disposal`/`Reactivation`, sem setter nem construtor público, sem rota de edição ou exclusão, conferido em `AssetMovementRulesTests`), `AssetMovementService` e as rotas `GET/POST /api/v1/assets/assets/{id}/movements`, `GET .../movements/{movementId}` (pela rota do bem, IDOR), `POST .../dispose`, `POST .../reactivate`, `POST /api/v1/assets/assets/transfer/preview` e `/transfer`, `GET /api/v1/assets/choices/buyers`; lista com `responsibleTerminated` e a padrão sem os baixados; evento `AssetDisposed { assetId, disposedOn }` no outbox (sem consumidor); `IAssets.ByWorkCenterAsync` e a sugestão do centro sem os baixados; `AssetMovementUsage` (bem com movimento não é excluído); usos de terceiro, funcionário e centro também pelos movimentos; `AssetsPersonalDataProvider` (o motivo é `[PersonalData]`). Front: aba Movimentações em cartões, "Registrar movimentação", "Baixar bem" (`destructive`), "Reativar bem", "Transferir selecionados" com o impacto em lista pela prévia, filtros "Responsável desligado" e "Baixados", `NextSteps` "Lançar título a receber" que abre o título a receber manual da FIN-01 pré-preenchido pela URL (`cliente`, `clienteNome`, `valor`, `emissao`, `descricao`), atalho da leitura da etiqueta abrindo o diálogo (`?acao=movimentar`); ajuda atualizada. **Recomendações aceitas (PO 24/10), pela opção mais conservadora:** (1) na aba Dados, local, responsável e centro só mudam por "Registrar movimentação" (PUT com outro valor: 422 `asset_placement_by_movement`), para o histórico ficar completo; o bem baixado não é editado (409 `asset_disposed`) nem movimentado até ser reativado; (2) na movimentação individual o destino é o estado completo (o formulário vem com o atual; vazio tira o responsável), no lote o vazio é "Manter o de cada bem"; (3) a data vai até hoje e não pode ser anterior ao último movimento do bem; (4) no lote, além do baixado, o bem já no destino ou com movimento posterior à data também impede (com o motivo), até 500 bens; a prévia do servidor monta o impacto em lista; (5) o bem em manutenção pode ser baixado até a PAT-03a trazer a recusa por OS aberta; valor e comprador só na venda (fora dela são ignorados); o comprador é um cliente ativo (`IParties`, papel Cliente), para casar com o título a receber; "Outro" exige a observação; o valor da venda segue a regra do valor de aquisição (fora do JSON e do histórico sem `assets.edit`); (6) a reativação avisa "Cancele o título da venda no Financeiro, se for o caso." sempre que a baixa desfeita foi uma venda (o Assets não lê o Financeiro, ADR 0006); (7) CA 10: usuário, data e IP de cada movimento e da mudança no bem ficam na auditoria (`AuditEntry`, conferido por teste com `X-Forwarded-For`); a aba mostra quem registrou e quando, e o IP não é exibido na tela (o histórico comum da FND-09c não expõe IP; é dado pessoal de quem registrou). E2E não escrito (regra do PO de 31/10). Pendência externa: nenhuma. Status `Done` | Dex (dev) |
