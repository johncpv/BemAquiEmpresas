# COM-01b — Orçamento: condições de pagamento por marco, validade, perda e cópia

| Campo | Valor |
|---|---|
| ID | COM-01b |
| Título | Condições de pagamento por marco (percentual, valor, gatilho por data ou por evento, prazo após o evento), preenchimento a partir da condição de pagamento, validade com padrão, rejeição com motivo e cópia de orçamento ou projeto anterior |
| Tamanho | M (quebra da COM-01) |
| Status | Draft |
| Requisitos | R-COML-6 (condições por marco, valor, evento-gatilho ou data) · R-COML-4 (validade; o alerta e a expiração ficam na COM-02) · R-COML-2 (copiar orçamento/itens de orçamento ou projeto anterior) · R-COML-9 (motivo de perda, base do funil) · R-PRP-1 · R-PRP-3 |
| Dependências | COM-01a; CAD-05b (`IPaymentTerms`, condição padrão do terceiro) |
| Bloqueia | COM-02, COM-04a (aprovação exige marcos somando 100%), COM-08 |
| Ordem de execução | 7 (Marco 2.2 — orçamento real) |

## User story
Como **comercial**, quero **definir como o cliente vai pagar (sinal, fabricação concluída, entrega técnica), até quando o preço vale e reaproveitar um orçamento parecido**, para **fechar a proposta rápido e já deixar pronto o que o financeiro vai cobrar**.

## Contexto e referências
- PRD R-COML-6 (ex.: 30% sinal, 40% na conclusão da fabricação, 30% na entrega técnica, "com valor e evento-gatilho ou data"), R-COML-2, R-COML-4.
- `04-fluxos-estados` §1 "Orçamento" (aprovar exige ao menos um marco e soma = 100%; `Rejected` com motivo obrigatório); §2 (marcos por data → recebível `Open`; por evento → `Planned`, vencimento = data do evento + prazo do marco).
- `03-modelo-dados` → `quotes.payment_term` (JSON por marco), `quotes.valid_until`, `lost_reason`; `contract_milestones` (`trigger` Date/Event, `trigger_event` Signature/FabricationDone/Delivered/TechnicalAcceptance, `percent`, `amount`, `due_date`).
- CAD-05b CA 14 (`IPaymentTerms.SplitAsync`) e CA 16 (condição padrão do terceiro).

## Critérios de aceite
**Marcos**
1. **Dado** um orçamento `Draft`, **quando** abro a aba "Condições" e adiciono marcos no `RowsEditor` com descrição, percentual (`numeric(7,4)`) e gatilho, **então** cada marco mostra o valor (`round(preço total × % / 100, 2, AwayFromZero)`, com o último absorvendo a diferença de centavos) e a soma dos percentuais aparece ao vivo em `aria-live`.
2. **Dado** o gatilho "Data", **quando** informo a data, **então** ela é gravada como `date` e não pode ser anterior a hoje (422); dado o gatilho "Evento", **quando** escolho o evento (Assinatura do contrato, Fabricação concluída, Entrega, Aceite técnico — ver Pontos em aberto 1) e o prazo em dias após o evento (inteiro ≥ 0, padrão 0), **então** o marco fica sem data.
3. **Dado** o exemplo do PRD (30% "Sinal" no evento Assinatura com prazo 0; 40% "Fabricação concluída" no evento Fabricação concluída com prazo 10; 30% "Entrega técnica" no evento Aceite técnico com prazo 10) e preço total R$ 100.000,00, **quando** salvo com "Salvar condições", **então** os valores são R$ 30.000,00, R$ 40.000,00 e R$ 30.000,00 e o toast diz "Condições salvas".
4. **Dado** marcos que somam diferente de 100%, **quando** salvo, **então** a gravação é aceita como rascunho com o aviso âmbar "A soma dos marcos está em {soma}%. Para aprovar, precisa ser 100%."; a COM-04a recusa a aprovação nesse caso (`04-fluxos` §1).
5. **Dado** "Preencher pela condição de pagamento", **quando** escolho uma condição (padrão: a condição padrão do cliente), **então** cada parcela da condição vira um marco com o gatilho "Evento: Assinatura do contrato" e o prazo igual aos dias da parcela, com o mesmo percentual (ex.: "30/60/90 dias" → três marcos de 33,3333%, 33,3333% e 33,3334%, prazos 30, 60 e 90), e substitui os marcos atuais só depois de confirmar "Substituir marcos".
6. **Dado** o preço total mudando depois dos marcos (COM-01a), **quando** os itens mudam, **então** os valores dos marcos são recalculados pelos percentuais; os percentuais não mudam.

**Validade**
7. **Dado** um orçamento novo, **quando** é criado, **então** a validade vem com hoje + a validade padrão do tenant (parâmetro `quotes.validity_days`, proposta, ver Pontos em aberto 2) e é editável; validade anterior a hoje responde 422 "A validade não pode ser no passado."
8. **Dado** um orçamento `Draft` com a validade vencida, **quando** clico "Marcar como enviado" (COM-01a CA 13), **então** 422 "A validade venceu. Ajuste a data antes de enviar."

**Perda**
9. **Dado** `quotes.edit` e um orçamento `Sent`, **quando** clico "Registrar perda", escolho o motivo da lista (Preço, Prazo, Concorrente, Desistência do cliente, Sem retorno, Outro — ver Pontos em aberto 3) e escrevo o detalhe (obrigatório em "Outro", até 500 caracteres), e confirmo "Registrar perda", **então** ele passa a `Rejected` com motivo, detalhe, usuário e data; o toast diz "Perda registrada"; sem motivo, 422 "Informe o motivo da perda."
10. **Dado** um orçamento `Rejected`, **quando** tento editar ou enviar, **então** 409 "Orçamento perdido não é alterado."; "Copiar para novo orçamento" continua disponível.

**Cópia (nada digitado duas vezes)**
11. **Dado** `quotes.create`, **quando** clico "Copiar de…" e escolho um orçamento anterior (de qualquer cliente, qualquer situação), **então** nasce um novo orçamento `Draft` (número novo, revisão 0) com os itens, as composições de custo, os preços e os marcos copiados, e o cliente, o contato e a obra que eu escolher (padrão: os do original); a validade é recalculada (CA 7) e o original não muda.
12. **Dado** "Copiar itens de…", **quando** escolho itens de outro orçamento, **então** eles são acrescentados ao orçamento atual (`Draft`) com as composições; os preços seguem o modo e o % atuais quando o item copiado não tinha preço digitado.
13. **Dado** "Copiar de projeto", **quando** escolho um projeto (COM-04a), **então** a cópia usa o orçamento aprovado do contrato do projeto (snapshot), com os mesmos resultados do CA 11; antes da COM-04a, a opção não aparece.
14. **Dado** uma cópia com custo de mão de obra diferente do custo-hora atual do cargo, **quando** a cópia termina, **então** o novo orçamento mostra o aviso "Custos copiados de {número}. Confira com 'Atualizar custos'." (COM-01a CA 12).

**Permissões e segurança**
15. **Dado** o módulo `quotes` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; sem `quotes.edit`, marcos, validade e perda respondem 403; sem `quotes.create`, cópia responde 403. A cópia de orçamento é permitida para quem tem `quotes.view` no orçamento de origem (mesmo tenant). `RequireModule` e `perm:`; IDOR em `/quotes/{id}/milestones/{milestoneId}`.
16. **Dado** qualquer alteração de marcos, validade ou perda, **quando** abro o histórico, **então** vejo antes/depois, usuário e data.

## Regras de negócio e técnicas
- **Marcos do orçamento** (proposta): tabela `commercial.quote_milestones` (`quote_id`, `sequence`, `description`, `percent` `numeric(7,4)`, `amount` `numeric(18,2)`, `trigger` Date/Event, `trigger_event`, `due_date` `date`, `days_after_event` int) no lugar do JSON `payment_term` do 03, porque os marcos viram `contract_milestones` linha a linha (COM-04a) e precisam de auditoria por linha.
- **Soma 100%** exata (4 casas), conferida no servidor na aprovação.
- **Validade** `valid_until` (`date`); **perda** em `lost_reason` (enum, proposta) + `lost_detail` (texto).
- **Cópia** grava `copied_from_quote_id` (proposta); não é revisão (a revisão é a COM-02, com `parent_quote_id`).
- Auditoria `IAuditable` em marcos; Guid v7.

## Permissões, eventos e tabelas
- **Permissões (já no `ModuleCatalog`):** `quotes.view`, `quotes.create` (cópia), `quotes.edit` (marcos, validade, perda).
- **Parâmetro (proposta):** `quotes.validity_days` em `identity.tenant_settings` (via `ITenantParameters`), editado em Configurações → Parâmetros.
- **Eventos:** `QuoteLost` (`{ quoteId, reason }`), sem consumidor nesta fase (funil COM-08 lê a tabela).
- **Tabelas:** `commercial.quote_milestones` (**proposta**); `commercial.quotes` (+ `lost_detail`, `lost_at`, `lost_by`, `copied_from_quote_id`).

## Telas e estados
- **Orçamento → aba Condições** (`/app/orcamentos/$id?aba=condicoes`): editor de marcos, total ao vivo, "Preencher pela condição de pagamento" e validade.
- **Diálogos:** "Registrar perda" / "Perda registrada"; "Substituir marcos" / "Marcos substituídos"; "Copiar de…" / "Orçamento copiado".
- **Lista:** situação ganha Perdido (neutro); filtro por motivo.
- **Estados:** vazio de marcos ("Defina como o cliente vai pagar." + "Preencher pela condição de pagamento" e "Adicionar marco"); erro; modo leitura.
- **Propositivo:** condição padrão do cliente sugerida; validade padrão; cópia preenche tudo; depois de salvar os marcos com 100%, "Próximos passos": "Gerar PDF" (COM-03) ou "Marcar como enviado".
- **400 px e teclado:** E2E do editor de marcos.
- **Ajuda:** `Ajuda/comercial-orcamento-condicoes.md` (aba `condicoes`); a ajuda `comercial-orcamentos.md` ganha "Copiar de outro orçamento" e "Registrar perda"; `<Dica>` em "Gatilho", "Prazo após o evento" e "Validade"; `Ajuda/configuracoes-parametros.md` ganha "Validade padrão do orçamento".

## Plano de testes
- **Unidade:** valores dos marcos e arredondamento (CA 1, 3, 6); conversão da condição em marcos (CA 5); validade (CA 7, 8).
- **Integração (Testcontainers):** marcos com soma diferente de 100% gravados com aviso (CA 4); preencher pela condição usando `IPaymentTerms` (CA 5); perda (CA 9, 10); cópia de orçamento e de itens com composições (CA 11, 12, 14); três camadas e IDOR (CA 15); auditoria (CA 16).
- **E2E (Playwright):** marcos do exemplo do PRD; copiar um orçamento perdido para um novo cliente; editor a 400 px.

## Fora do escopo
- Expiração automática e alerta de vencimento: COM-02. Recebíveis gerados dos marcos: COM-04b.
- Aprovação online pelo cliente: POR-02 (Fase 8).

## Pontos em aberto
1. **Eventos-gatilho na Fase 2.** "Fabricação concluída" depende da Produção (Fase 3) e "Entrega" e "Aceite técnico" se confundem no `04-fluxos` (o projeto vira `Delivered` quando a entrega técnica é aceita). **Recomendação:** manter os quatro eventos do 03; "Entrega" e "Aceite técnico" disparam juntos no aceite da OS de instalação (COM-06); "Fabricação concluída" é registrada à mão no projeto até a PRD-06 (COM-04b, ponto 1).
2. **Validade padrão.** **Recomendação:** parâmetro `quotes.validity_days` com seed 15 dias, editável.
3. **Lista de motivos de perda.** **Recomendação:** a lista do CA 9, fixa no sistema, para o funil (COM-08) agrupar; "Outro" com detalhe obrigatório.
4. **Marco parcelado.** Um marco pode querer virar várias parcelas (ex.: "70% em 3× após a entrega"). **Recomendação:** um marco = um recebível; quem precisa parcelar cria vários marcos (o CA 5 já faz isso a partir da condição).
5. **P-18:** ver FIN-01, ponto 5.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers) e E2E passando.
- [ ] Migration criada (não destrutiva); parâmetro com seed idempotente.
- [ ] Permissões só do `ModuleCatalog`.
- [ ] Aba com estados de vazio, carregando e erro.
- [ ] Ajuda da aba e da lista atualizada; teste de cobertura passando.
- [ ] Padrões preenchidos e próximos passos aplicados.
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado (`quote_milestones` no lugar do JSON); story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Entidade `QuoteMilestone` e `MilestoneSplitter` (Domain, mesmo arredondamento do `PaymentTermSplitter`).
- [ ] Casos de uso de perda e cópia (orçamento, itens, projeto).
- [ ] Endpoints `/api/v1/quotes/{id}/milestones`, `/lose`, `/copy`; aba e ajuda.

## Quality gates
- Agentes previstos: @dev, @qa.
- Gate: CA 1, 3 e 11 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 19/10/2026.

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

- **Correções feitas na validação (19/10/2026):** prazo após o evento no marco (o `04-fluxos` §2 usa "data do evento + prazo do marco", que não tinha coluna); marcos em tabela própria (proposta) em vez do JSON; soma ≠ 100% só bloqueia a aprovação; "Copiar de projeto" condicionado à COM-04a.
- **Pré-condições para `Ready`:** Pontos em aberto 1 a 4 (com padrão recomendado) e P-18.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 19/10/2026 | 0.1 | Story detalhada (quebra da COM-01) | River (SM) |
| 19/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
