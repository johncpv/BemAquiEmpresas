# PAT-02 — Patrimônio: movimentação de bens e baixa

| Campo | Valor |
|---|---|
| ID | PAT-02 |
| Título | Transferência de local, de responsável e de centro de trabalho com data, motivo e histórico imutável; transferência em lote; baixa do bem (venda, descarte, perda, doação) com motivo, data e valor, que tira o bem das listas ativas sem apagar nada |
| Tamanho | P |
| Status | Draft |
| Requisitos | R-PAT-3 (movimentação, transferência de local/responsável, com histórico) · R-PRP-1 · R-PRP-6 (D) · R-SEC-5 |
| Dependências | PAT-01a (bens), PAT-01b (atalho pela leitura) |
| Bloqueia | — |
| Ordem de execução | 12 (Marco 6.4 "Patrimônio") |

## User story
Como **supervisor**, quero **registrar quando um bem muda de lugar ou de responsável, e quando ele sai da empresa**, para **saber sempre onde está cada bem e com quem**.

## Contexto e referências
- PRD R-PAT-3; `03-modelo-dados` → `assets.asset_movements` (`asset_id`, `from/to location`, `from/to responsible`, `at`, `reason`); `assets.assets.status` (`Disposed`).
- `CLAUDE.md`: exclusão lógica em cadastro; histórico não é apagado.
- PAT-01a CA 4 (responsável desligado com "Troque o responsável").

## Critérios de aceite
1. **Dado** `assets.edit` e um bem `Active` ou `Maintenance`, **quando** clico "Registrar movimentação", informo o novo local, o novo responsável e/ou o novo centro de trabalho, a data (≤ hoje; padrão hoje) e o motivo (até 200 caracteres) e confirmo "Registrar movimentação", **então** nasce o movimento com os valores de antes e depois e o bem passa a mostrar os novos, na mesma transação; a mensagem de sucesso diz "Movimentação registrada".
2. **Dado** a movimentação, **quando** nada mudou, **então** 422 "Informe pelo menos um local, responsável ou centro diferente do atual."
3. **Dado** a aba Movimentações do bem, **quando** abro, **então** vejo os movimentos do mais recente ao mais antigo (data, de → para, motivo, quem registrou); movimento não é editado nem apagado (sem rota; teste de arquitetura).
4. **Dado** vários bens selecionados na lista, **quando** clico "Transferir selecionados" (D), escolho o destino e o motivo e confirmo, **então** um movimento por bem é gravado na mesma transação; um bem `Disposed` na seleção faz o comando falhar inteiro com a lista do que impede.
5. **Dado** um funcionário desligado que ainda é responsável por bens, **quando** abro Patrimônio → Bens com o filtro "Responsável desligado", **então** vejo esses bens e a ação "Transferir selecionados" já com o motivo sugerido "Responsável desligado" (propositivo).
6. **Dado** `assets.edit`, **quando** clico "Baixar bem", escolho o motivo (Venda, Descarte, Perda ou roubo, Doação, Outro), a data e, na venda, o valor (`numeric(18,2)`) e o comprador opcional (`IParties`), e confirmo "Baixar bem" (estilo `destructive`), **então** o bem passa a `Disposed`, some dos seletores e das listas padrão (filtro "Baixados"), o movimento de baixa fica no histórico, os planos de manutenção do bem são inativados (PAT-03a) e as OS de manutenção abertas impedem a baixa (409 "Conclua ou cancele as OS de manutenção abertas.").
7. **Dado** um bem baixado por engano, **quando** clico "Reativar bem" com `assets.edit` e informo o motivo, **então** ele volta a `Active` com um movimento de reativação; nada é apagado.
8. **Dado** o módulo `assets` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem `assets.edit`, 403 `forbidden` e as ações somem; `RequireModule` e `perm:`; IDOR em `/assets/{id}/movements/{movId}`.
9. **Dado** qualquer movimento, baixa ou reativação, **quando** abro o histórico, **então** vejo usuário, data e IP.

## Regras de negócio e técnicas
- **Tabela:** `assets.asset_movements` (03) + `kind` (`Transfer`/`Disposal`/`Reactivation`), `from_site_id`/`to_site_id`, `from_employee_id`/`to_employee_id`, `from_work_center_id`/`to_work_center_id`, `moved_on` (`date`), `reason`, `disposal_reason`, `disposal_value`, `buyer_party_id`, `created_by`.
- **Baixa sem efeito financeiro automático nesta story:** a venda do bem é registrada aqui; o título a receber, se houver, é lançado no Financeiro (Pontos em aberto 1).
- Guid v7; `IAuditable`; movimento imutável.

## Permissões, eventos e tabelas
- **Permissões:** `assets.edit`.
- **Eventos:** `AssetDisposed` (`{ assetId, disposedOn }`) para a Produção/Planejamento deixarem de considerar a máquina (sem consumidor obrigatório nesta fase).
- **Tabelas:** `assets.asset_movements` (extensão).

## Telas e estados
- Na ficha (PAT-01a): "Registrar movimentação", "Baixar bem", "Reativar bem"; aba Movimentações.
- Na lista: seleção com "Transferir selecionados"; filtros "Responsável desligado" e "Baixados".
- **Estados:** vazio da aba ("Nenhuma movimentação. O bem está onde foi cadastrado."); erro; modo leitura.
- **400 px:** movimentos em cartões.
- **Ajuda:** atualizar `Ajuda/patrimonio-bem.md` (aba `movimentacoes`) e `Ajuda/patrimonio-bens.md` ("Transferir em lote", "Baixar bem").

## Plano de testes
- **Unidade:** nada mudou (CA 2); baixa com OS aberta (CA 6).
- **Integração (Testcontainers):** movimento com antes e depois (CA 1); imutável (CA 3); lote atômico (CA 4); filtro de desligado (CA 5); baixa e reativação (CA 6, 7); três camadas e IDOR (CA 8).
- **E2E (Playwright):** transferir dois bens e baixar um.

## Fora do escopo
- Título a receber da venda do bem e baixa contábil do imobilizado: Financeiro e contadora.
- Termo de responsabilidade assinado pelo funcionário: fora do MVP.

## Pontos em aberto
1. **Venda do bem** (PO): só registrar (proposta) ou já oferecer "Lançar título a receber" no `NextSteps` com o valor e o comprador.
2. Termo de responsabilidade em PDF na troca de responsável (PO).

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers) e E2E passando.
- [ ] Migration criada (não destrutiva).
- [ ] Telas com estados; ajuda atualizada; teste de cobertura passando.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `AssetMovement` e as transições do bem; endpoints `/api/v1/assets/assets/{id}/movements`, `/dispose`, `/reactivate`, `/assets/transfer`.

## Quality gates
- Agentes previstos: @dev, @qa.
- Gate: CA 1, 3 e 6 obrigatórios.

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

- **Correções feitas na validação:** a baixa do bem entrou aqui (é o destino do "Excluir" de bem com histórico, PAT-01a CA 12), sem efeito financeiro automático.
- **Pré-condições para `Ready`:** nenhuma de decisão; PAT-01a concluída.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Draft` até a PAT-01a.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
