# COM-01b — Orçamento: condições de pagamento por marco, validade, perda e cópia

| Campo | Valor |
|---|---|
| ID | COM-01b |
| Título | Condições de pagamento por marco (percentual, valor, gatilho por data ou por evento, prazo após o evento), preenchimento a partir da condição de pagamento, validade com padrão, rejeição com motivo e cópia de orçamento ou projeto anterior |
| Tamanho | M (quebra da COM-01) |
| Status | Done |
| Requisitos | R-COML-6 (condições por marco, valor, evento-gatilho ou data) · R-COML-4 (validade; o alerta, a expiração e a prorrogação ficam na COM-02) · R-COML-2 (copiar orçamento/itens de orçamento ou projeto anterior) · R-COML-9 (motivo de perda, base do funil) · R-PRP-1 · R-PRP-3 |
| Dependências | COM-01a; CAD-05b (`IPaymentTerms`, `PaymentTermSplitter`, condição padrão do terceiro); FND-09e (`RowsEditor`, erros por linha) |
| Bloqueia | COM-02, COM-04a (aprovação exige marcos somando 100%), COM-08 |
| Ordem de execução | 9 (Marco 2.2 "Orçamento") |

## User story
Como **comercial**, quero **definir como o cliente vai pagar (sinal, fabricação concluída, entrega técnica), até quando o preço vale e reaproveitar um orçamento parecido**, para **fechar a proposta rápido e já deixar pronto o que o financeiro vai cobrar**.

## Contexto e referências
- PRD R-COML-6 (ex.: 30% sinal, 40% na conclusão da fabricação, 30% na entrega técnica, "com valor e evento-gatilho ou data"), R-COML-2, R-COML-4.
- `04-fluxos-estados` §1 "Orçamento" (aprovar exige ao menos um marco e soma = 100%; `Rejected` com motivo obrigatório); §2 (marcos por data → recebível `Open`; por evento → `Planned`, vencimento = data do evento + prazo do marco).
- `03-modelo-dados` → `quotes.payment_term` (JSON por marco, substituído por tabela nesta story), `quotes.valid_until`, `lost_reason`; `contract_milestones` (`trigger` Date/Event, `trigger_event` Signature/FabricationDone/Delivered/TechnicalAcceptance, `percent`, `amount`, `due_date`).
- CAD-05b CA 14 (`IPaymentTerms.SplitAsync`) e CA 16 (condição padrão do terceiro).
- Revisão pré-Fase 2: seção 1.1 (itens 20 e 21), §2.1-M5 (valores dos marcos pelo `PaymentTermSplitter`), M6 (parâmetro do Commercial), M10 (`MilestoneTrigger` e `TriggerEvent` no SharedKernel), §2.2-M8 (% do marco revalidado no servidor), §2.3 ("Faltam 10,00%", % e R$ lado a lado, coluna Validade com "vence em X").

## Critérios de aceite
**Marcos**
1. **Dado** um orçamento `Draft`, **quando** abro a aba "Condições" e adiciono marcos no `RowsEditor` com descrição, percentual (`numeric(7,4)`) e gatilho, **então** cada marco mostra o % e o valor lado a lado (valores pelo `PaymentTermSplitter`: `round(preço total × % / 100, 2, AwayFromZero)`, com o último absorvendo a diferença de centavos), a soma dos percentuais aparece ao vivo em `aria-live` e, quando não fecha, o editor mostra o que falta ("Faltam 10,00%" ou "Passou 5,00%") com as ações "Dividir igualmente" e "Ajustar o último".
2. **Dado** o gatilho "Data", **quando** informo a data, **então** ela é gravada como `date` e não pode ser anterior a hoje (422 no campo; o contrato de implantação, COM-04c, aceita datas passadas na aprovação); dado o gatilho "Evento", **quando** escolho o evento (Assinatura do contrato, Fabricação concluída, Entrega, Aceite técnico) e o prazo em dias após o evento (inteiro ≥ 0, padrão 0), **então** o marco fica sem data.
3. **Dado** o exemplo do PRD (30% "Sinal" no evento Assinatura com prazo 0; 40% "Fabricação concluída" no evento Fabricação concluída com prazo 10; 30% "Entrega técnica" no evento Aceite técnico com prazo 10) e preço total R$ 100.000,00, **quando** salvo com "Salvar condições", **então** os valores são R$ 30.000,00, R$ 40.000,00 e R$ 30.000,00 e a mensagem de sucesso diz "Condições salvas".
4. **Dado** marcos que somam diferente de 100%, **quando** salvo, **então** a gravação é aceita como rascunho com o aviso âmbar "A soma dos marcos está em {soma}%. Para aprovar, precisa ser 100%."; a COM-04a recusa a aprovação nesse caso (`04-fluxos` §1).
5. **Dado** "Preencher pela condição de pagamento", **quando** escolho uma condição (padrão: a condição padrão do cliente), **então** cada parcela da condição vira um marco com o gatilho "Evento: Assinatura do contrato" e o prazo igual aos dias da parcela, com o mesmo percentual (ex.: "30/60/90 dias" → três marcos de 33,3333%, 33,3333% e 33,3334%, prazos 30, 60 e 90), e substitui os marcos atuais só depois de confirmar "Substituir marcos".
6. **Dado** o preço total mudando depois dos marcos (COM-01a), **quando** os itens mudam, **então** os valores dos marcos são recalculados pelos percentuais; os percentuais não mudam.
7. **[Segurança]** **Dado** um marco com % ≤ 0 ou > 100, ou prazo após o evento negativo, **quando** salvo, **então** o servidor responde 422 no campo da linha (`milestones[n].percent`), mesmo que o front tenha validado (§2.2-M8).

**Validade**
8. **Dado** um orçamento novo, **quando** é criado, **então** a validade vem com hoje + a validade padrão do tenant (parâmetro `quotes.validity_days`, seed 15 dias) e é editável no rascunho; validade anterior a hoje responde 422 "A validade não pode ser no passado."
9. **Dado** um orçamento `Draft` com a validade vencida, **quando** clico "Marcar como enviado" (COM-01a CA 16), **então** 422 "A validade venceu. Ajuste a data antes de enviar."
10. **Dado** a lista de orçamentos, **quando** abro, **então** a coluna Validade mostra a data e "vence em {n} dias" (ou "venceu", no rascunho vencido).

**Perda**
11. **Dado** `quotes.edit` e um orçamento `Sent`, **quando** clico "Registrar perda", escolho o motivo da lista (Preço, Prazo, Concorrente, Desistência do cliente, Sem retorno, Outro) e escrevo o detalhe (obrigatório em "Outro", até 500 caracteres), e confirmo "Registrar perda", **então** ele passa a `Rejected` com motivo, detalhe, usuário e data; a mensagem de sucesso diz "Perda registrada"; sem motivo, 422 "Informe o motivo da perda."
12. **Dado** um orçamento `Rejected`, **quando** tento editar ou enviar, **então** 409 "Orçamento perdido não é alterado."; "Copiar para novo orçamento" continua disponível.

**Cópia (nada digitado duas vezes)**
13. **Dado** `quotes.create`, **quando** clico "Copiar de…" e escolho um orçamento anterior (de qualquer cliente, qualquer situação), **então** nasce um novo orçamento `Draft` (número novo, revisão 0) com os itens, as composições de custo, os preços e os marcos copiados, e o cliente, o contato e a obra que eu escolher (padrão: os do original); a validade é recalculada (CA 8) e o original não muda.
14. **Dado** "Copiar itens de…", **quando** escolho itens de outro orçamento, **então** eles são acrescentados ao orçamento atual (`Draft`) com as composições; os preços seguem o modo e o % atuais quando o item copiado não tinha preço digitado.
15. **Dado** "Copiar de projeto", **quando** escolho um projeto (COM-04a), **então** a cópia usa o orçamento aprovado do contrato do projeto (snapshot), com os mesmos resultados do CA 13; antes da COM-04a, a opção não aparece.
16. **Dado** uma cópia com custo de mão de obra diferente do custo-hora atual do cargo, **quando** a cópia termina, **então** o novo orçamento mostra o aviso "Custos copiados de {número}. Confira com 'Atualizar custos'." (COM-01a CA 13), só para quem tem `quotes.view_cost`.

**Permissões e segurança**
17. **Dado** o módulo `quotes` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; sem `quotes.edit`, marcos, validade e perda respondem 403; sem `quotes.create`, cópia responde 403. A cópia de orçamento é permitida para quem tem `quotes.view` no orçamento de origem (mesmo tenant); a cópia leva a composição de custo no servidor, sem mostrá-la a quem não tem `quotes.view_cost`. `RequireModule` e `perm:`; IDOR em `/quotes/{id}/milestones/{milestoneId}`.
18. **Dado** qualquer alteração de marcos, validade ou perda, **quando** abro o histórico, **então** vejo antes/depois, usuário e data.

## Regras de negócio e técnicas
- **Marcos do orçamento:** tabela `commercial.quote_milestones` (`quote_id`, `sequence`, `description`, `percent` `numeric(7,4)`, `amount` `numeric(18,2)`, `trigger`, `trigger_event`, `due_date` `date`, `days_after_event` int) no lugar do JSON `payment_term` do 03, porque os marcos viram `contract_milestones` linha a linha (COM-04a) e precisam de auditoria por linha.
- **Valores dos marcos** pelo `PaymentTermSplitter` da CAD-05b (ADR 0019 §9); **vocabulário** `MilestoneTrigger` (Date/Event) e `TriggerEvent` (Signature/FabricationDone/Delivered/TechnicalAcceptance) no SharedKernel (ADR 0019 §14), usados também pelo Finance na COM-04b.
- **Soma 100%** exata (4 casas), conferida no servidor na aprovação.
- **Validade** `valid_until` (`date`); **perda** em `lost_reason` (enum) + `lost_detail` (texto).
- **Cópia** grava `copied_from_quote_id`; não é revisão (a revisão é a COM-02, com `parent_quote_id`).
- Auditoria `IAuditable` em marcos; Guid v7; concorrência pelo `xmin` do orçamento (ADR 0019 §2).

## Permissões, eventos e tabelas
- **Permissões (já no `ModuleCatalog`):** `quotes.view`, `quotes.create` (cópia), `quotes.edit` (marcos, validade, perda).
- **Parâmetro:** `quotes.validity_days` (seed 15) em `commercial.parameters` (ADR 0016 adendo M6), lido por `ICommercialParameters`, editado em Configurações → Parâmetros (seção Comercial, `settings.edit`).
- **Eventos:** `QuoteLost` (`{ quoteId, reason }`), sem consumidor nesta fase (funil COM-08 lê a tabela).
- **Tabelas:** `commercial.quote_milestones` (nova); `commercial.quotes` (+ `lost_detail`, `lost_at`, `lost_by`, `copied_from_quote_id`).

## Telas e estados
- **Orçamento → aba Condições** (`/app/orcamentos/$id?aba=condicoes`): editor de marcos com % e R$ lado a lado, o que falta para 100% com "Dividir igualmente" e "Ajustar o último", "Preencher pela condição de pagamento" e validade.
- **Diálogos:** "Registrar perda" / "Perda registrada"; "Substituir marcos" / "Marcos substituídos"; "Copiar de…" / "Orçamento copiado".
- **Lista:** situação ganha Perdido (neutro); filtro por motivo; coluna Validade com "vence em X".
- **Estados:** vazio de marcos ("Defina como o cliente vai pagar." + "Preencher pela condição de pagamento" e "Adicionar marco"); erro; modo leitura.
- **Propositivo:** condição padrão do cliente sugerida; validade padrão; cópia preenche tudo; depois de salvar os marcos com 100%, "Próximos passos": "Gerar PDF" (COM-03) ou "Marcar como enviado".
- **400 px:** cada marco vira um cartão (descrição, %, valor, gatilho); o total e o que falta ficam fixos no rodapé. E2E de 400 px e teclado do editor de marcos.
- **Ajuda:** `Ajuda/comercial-orcamento-condicoes.md` (aba `condicoes`); a ajuda `comercial-orcamentos.md` ganha "Copiar de outro orçamento" e "Registrar perda"; `<Dica>` em "Gatilho", "Prazo após o evento" e "Validade"; `Ajuda/configuracoes-parametros.md` ganha "Validade padrão do orçamento".

## Plano de testes
- **Unidade:** valores dos marcos e arredondamento pelo `PaymentTermSplitter` (CA 1, 3, 6); "Dividir igualmente" e "Ajustar o último" (CA 1); conversão da condição em marcos (CA 5); validade (CA 8, 9).
- **Integração (Testcontainers):** marcos com soma diferente de 100% gravados com aviso (CA 4); % e prazo revalidados no servidor (CA 7); preencher pela condição usando `IPaymentTerms` (CA 5); perda (CA 11, 12); cópia de orçamento e de itens com composições (CA 13, 14, 16); três camadas e IDOR (CA 17); auditoria (CA 18).
- **E2E (Playwright):** marcos do exemplo do PRD; copiar um orçamento perdido para um novo cliente; editor a 400 px.

## Fora do escopo
- Expiração automática, alerta de vencimento e prorrogação da validade: COM-02. Recebíveis gerados dos marcos: COM-04b. Marcos com data passada (contrato de implantação): COM-04c.
- Aprovação online pelo cliente: POR-02 (Fase 8).

## Pontos em aberto
Decididos pelo PO em 20/10/2026 (seção 1.1, itens 20 e 21, e D15):
1. Eventos-gatilho: os quatro do 03; "Entrega" e "Aceite técnico" disparam no aceite da OS de instalação (COM-06) ou no "Registrar entrega" manual para venda sem instalação (COM-04b, D15); "Fabricação concluída" é registrada à mão no projeto até a PRD-06.
2. Validade padrão de 15 dias (parâmetro do Commercial).
3. Lista fixa de motivos de perda, com "Outro" e detalhe obrigatório.
4. Um marco = um recebível; quem precisa parcelar cria vários marcos.
5. P-18 decidida (ADR 0021).

## Definition of Done
- [x] Critérios de aceite atendidos.
- [x] Testes de unidade, integração (Testcontainers) e E2E passando.
- [x] Migration criada (não destrutiva); parâmetro com seed idempotente.
- [x] Permissões só do `ModuleCatalog`.
- [x] Aba com estados de vazio, carregando e erro.
- [x] Ajuda da aba e da lista atualizada; teste de cobertura passando.
- [x] Padrões preenchidos e próximos passos aplicados.
- [x] Auditoria ligada.
- [x] OpenAPI e cliente Orval regenerados.
- [x] `docs/03-modelo-dados.md` atualizado (`quote_milestones` no lugar do JSON); story marcada no backlog.

## Tarefas técnicas sugeridas
- [x] Entidade `QuoteMilestone`; uso do `PaymentTermSplitter`; `MilestoneTrigger` e `TriggerEvent` no SharedKernel.
- [x] Casos de uso de perda e cópia (orçamento, itens, projeto).
- [x] Endpoints `/api/v1/quotes/{id}/milestones`, `/lose`, `/copy`; aba e ajuda.

## Quality gates
- Agentes previstos: @dev, @qa.
- Gate: CA 1, 3, 7 e 13 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 20/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fase 2).

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

- **Correções feitas na validação (19/10/2026):** prazo após o evento no marco; marcos em tabela própria em vez do JSON; soma ≠ 100% só bloqueia a aprovação; "Copiar de projeto" condicionado à COM-04a.
- **Correções da revalidação (20/10/2026):** valores pelo `PaymentTermSplitter` (M5) e o que falta para 100% com "Dividir igualmente" e "Ajustar o último" (§2.3, CA 1); % e prazo revalidados no servidor (§2.2-M8, CA 7); coluna Validade com "vence em X" (CA 10); `quotes.validity_days` como parâmetro do Commercial (M6); `MilestoneTrigger` e `TriggerEvent` no SharedKernel (M10); data passada só no contrato de implantação (COM-04c); entrega manual (D15) nos eventos-gatilho. CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma pendente.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 19/10/2026 | 0.1 | Story detalhada (quebra da COM-01) | River (SM) |
| 19/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 20/10/2026 | 0.3 | Decisões do PO (seção 1.1, D15) e correções da revisão pré-Fase 2 (§2.1-M5, M6, M10, §2.2-M8, §2.3); CAs renumerados | River (SM) |
| 20/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
| 20/10/2026 | 1.0 | Implementada. Desvios: marcos gravados inteiros com a validade (`PUT /quotes/{id}/conditions`), sem rota por marco; a divisão por percentual está no SharedKernel (`PercentSplit`, mesma regra do `PaymentTermSplitter`), porque o Commercial não referencia o domínio do Finance; a validade padrão (`quotes.validity_days`) vale 15 enquanto não gravada; "Copiar de projeto" (CA 15) entra com a COM-04a; o filtro por motivo da perda fica para o funil (COM-08), a lista filtra "Perdidos"; `QuoteLost` publicado no outbox | @dev |
