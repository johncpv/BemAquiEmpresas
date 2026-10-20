# COM-04c — Contrato de implantação (contrato já em andamento)

| Campo | Valor |
|---|---|
| ID | COM-04c |
| Título | "Aprovar como contrato de implantação" para os contratos que já estavam em andamento antes do BemAqui: data de assinatura no passado, marcos com data no passado e a marca "Recebido antes da implantação" (sem gerar título), com os eventos já ocorridos registrados com a data real |
| Tamanho | P (nova, decisão D3 da revisão pré-Fase 2) |
| Status | Done |
| Requisitos | R-COML-7 (contrato e parcelas) · R-PRJ-1 · R-PRJ-6 · R-FIN-3 · R-PRP-3 (nada digitado duas vezes) · R-PRP-7 |
| Dependências | COM-04a (aprovação, contrato, data de assinatura editável), COM-04b (consumidor do `ContractSigned`, registro de gatilhos, registro manual de fabricação e de entrega), FIN-00 (data de corte) |
| Bloqueia | Uso real dos contratos em andamento na homologação (ADR 0020) |
| Ordem de execução | 14 (Marco 2.3 "Contrato ao caixa", logo depois da COM-04b) |

## User story
Como **proprietário**, quero **registrar no sistema os contratos que já estavam em andamento, com o que já foi recebido e o que ainda falta**, para **acompanhar esses projetos e cobrar as parcelas restantes sem gerar título do que já entrou no caixa**.

## Contexto e referências
- **D3 (PO, 20/10/2026):** "Contrato de implantação" na COM-04a ou numa COM-04c (P): data de assinatura ≤ hoje, marcos com data no passado e a marca "recebido antes da implantação" (título não gerado). A data de assinatura editável em toda aprovação ficou na COM-04a (CA 2).
- **Decisão do SM (20/10/2026, registrada no README):** story própria, porque a COM-04a já é G e o modo de implantação mexe também no consumidor do Finance (COM-04b), que só existe depois dela.
- Hoje um contrato em andamento não entra: o marco por data não pode estar no passado (COM-01b CA 2) e o sinal já recebido viraria título em aberto, duplicando o caixa (FIN-00 traz o saldo real das contas).
- ADR 0020 (dados reais na homologação a partir da FIN-00); ADR 0019 §1 (registro de gatilhos).

## Critérios de aceite
1. **Dado** `quotes.approve`, a revisão corrente `Sent` e a implantação financeira com data de corte definida (FIN-00), **quando** abro "Aprovar e gerar contrato" e marco "Contrato de implantação (já estava em andamento)", **então** o diálogo aceita a data de assinatura no passado (≤ hoje), permite editar a data dos marcos por data para o passado e mostra, em cada marco, a opção "Recebido antes da implantação"; sem data de corte, a opção não aparece ("Defina a data de corte na Implantação financeira antes.").
2. **Dado** um marco marcado "Recebido antes da implantação", **quando** a aprovação grava, **então** o marco fica com `received_before_onboarding = true` e o `ContractSigned` leva a marca; o consumidor do Finance **não gera título** para ele (o dinheiro já está no saldo de implantação das contas); os demais marcos seguem a COM-04b (data → `Open`, inclusive com vencimento no passado, que aparece como "Vencido"; evento → `Planned`).
3. **Dado** um marco "Recebido antes da implantação", **quando** a data do marco (ou a data de assinatura, para o sinal) é posterior à data de corte, **então** 422 no marco "Recebido depois da data de corte: lance a baixa no título." (o recebimento depois do corte tem de passar pelo caixa do sistema).
4. **Dado** um contrato de implantação cujo projeto já teve a fabricação concluída ou a entrega antes do sistema, **quando** registro "Registrar fabricação concluída" ou "Registrar entrega" (COM-04b CA 9 e 10) com a data real no passado, **então** o evento grava a data real, o registro de gatilhos guarda o evento e os títulos `Planned` desse gatilho são liberados com vencimento = data real + prazo (podendo já nascer vencidos); marcos "Recebido antes da implantação" com esse gatilho continuam sem título.
5. **Dado** o contrato de implantação, **quando** abro o contrato e o projeto, **então** aparecem a `Tag` "Implantação" e, em "Itens e marcos", cada marco recebido antes mostra "Recebido antes da implantação" no lugar do título; o contrato conta esses marcos como quitados para a conclusão (COM-04b CA 14).
6. **[Segurança]** **Dado** um usuário sem `quotes.approve`, **quando** tenta aprovar como implantação, **então** 403; a marca "Contrato de implantação" e cada marco recebido antes ficam na auditoria do contrato, com o usuário, e os membros do Proprietário são notificados ("Contrato de implantação {número} com {n} marcos recebidos antes ({total}), por {usuário}.").
7. **Dado** a implantação financeira concluída (FIN-00 CA 10) há mais de 90 dias, **quando** abro o diálogo de aprovação, **então** a opção "Contrato de implantação" continua disponível, com o aviso âmbar "A implantação foi concluída em {data}. Use só para contrato que já existia antes dela."
8. **Dado** o módulo `quotes` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`. `RequireModule` e `perm:`.

## Regras de negócio e técnicas
- **Marca no contrato:** `contracts.is_onboarding` (bool) e `contract_milestones.received_before_onboarding` (bool); o snapshot registra as duas.
- **Validação:** a regra "marco por data não pode estar no passado" (COM-01b CA 2) não vale na aprovação de implantação; a data de assinatura continua ≤ hoje (COM-04a CA 2).
- **Finance:** o `ContractSignedConsumer` (COM-04b) ignora os marcos com `receivedBeforeOnboarding`; nenhum título é criado para eles, e o `IReceivables.ForContractAsync` devolve esses marcos como "recebidos antes".
- Dinheiro `numeric(18,2)`; datas `date`; auditoria `IAuditable`.

## Permissões, eventos e tabelas
- **Permissões:** `quotes.approve` (aprovar como implantação), `projects.edit`/`projects.approve` (registros manuais com data real, COM-04b).
- **Eventos:** `ContractSigned` ganha `isOnboarding` e, por marco, `receivedBeforeOnboarding`.
- **Tabelas:** `commercial.contracts` (+ `is_onboarding`); `commercial.contract_milestones` (+ `received_before_onboarding`).

## Telas e estados
- **Diálogo "Aprovar e gerar contrato"** (COM-04a) com a opção "Contrato de implantação (já estava em andamento)" e, quando marcada, a tabela de marcos com data editável e a coluna "Recebido antes da implantação".
- **Contrato e projeto:** `Tag` "Implantação"; marcos recebidos antes com o texto no lugar do título.
- **Estados:** sem data de corte, a opção aparece desabilitada com o motivo e o link "Implantação financeira".
- **Propositivo:** os marcos com data anterior à data de corte vêm pré-marcados como "Recebido antes da implantação" (o usuário confirma ou desmarca).
- **400 px:** a tabela de marcos do diálogo vira cartões.
- **Ajuda:** a ajuda `comercial-orcamento.md` ganha "Contrato de implantação"; `<Dica>` em "Recebido antes da implantação" ("O valor já está no saldo das contas na data de corte; o sistema não gera título para ele.").

## Plano de testes
- **Unidade:** validações de datas e da marca (CA 1, 3).
- **Integração (Testcontainers, obrigatória: efeito financeiro):** aprovação de implantação com 3 marcos (um recebido antes, um por data no passado, um por evento): só dois títulos, um já vencido (CA 2); recebido depois da data de corte recusado (CA 3); fabricação registrada com data no passado libera o título vencido (CA 4); conclusão considerando os recebidos antes (CA 5); auditoria e notificação (CA 6); três camadas (CA 8).
- **E2E (Playwright):** cadastrar o orçamento de um contrato em andamento, aprovar como implantação e conferir as parcelas no financeiro.

## Fora do escopo
- Importação de contratos do Dolibarr (decisão do PO: só cadastros, ADR 0017).
- Baixa de recebimentos feitos depois da data de corte: pelo fluxo normal (FIN-01).

## Pontos em aberto
Nenhum. Decidido pelo PO em 20/10/2026 (D3); a forma (story própria) foi decidida pelo SM pelo tamanho da COM-04a.

## Definition of Done
- [x] Critérios de aceite atendidos.
- [x] Testes de unidade, integração (Testcontainers) e E2E passando.
- [x] Migration criada (não destrutiva).
- [x] Permissões só do `ModuleCatalog`.
- [x] Diálogo com estados.
- [x] Ajuda atualizada; teste de cobertura passando.
- [x] Padrão preenchido (marcos antes da data de corte) e confirmação com impacto.
- [x] Auditoria ligada.
- [x] OpenAPI e cliente Orval regenerados.
- [x] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Modo de implantação no `ApproveQuoteHandler` e no diálogo.
- [ ] Marca no evento e no `ContractSignedConsumer` do Finance.
- [ ] Ajuda.

## Quality gates
- Agentes previstos: @dev, @qa.
- Gate: CA 2, 3 e 4 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 20/10/2026.

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

- **Correções feitas na validação (20/10/2026):** a marca "Recebido antes da implantação" ficou presa à data de corte da FIN-00 (CA 3), para nenhum recebimento posterior ao corte escapar do caixa; o registro de fabricação e de entrega com data real reaproveita a COM-04b (CA 4); notificação ao Proprietário (CA 6).
- **Pré-condições para `Ready`:** nenhuma pendente.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 20/10/2026 | 0.1 | Story nova (D3 da revisão pré-Fase 2; quebra decidida pelo SM) | River (SM) |
| 20/10/2026 | 0.2 | Validação do PO: 10/10, `Ready` | Pax (PO) |
| 20/10/2026 | 1.0 | Implementada. Desvios: a data de corte vem do Finance por `IFinancialOpening` (contrato novo); "recebido antes" é conferido contra a data de corte só no marco por data e no marco na assinatura (os de outro evento não têm data na aprovação e são aceitos); a conclusão conta os marcos recebidos antes como quitados comparando o número de parcelas com o de marcos sem a marca; a notificação ao Proprietário sai na mesma transação da aprovação; o `IReceivables.ForContractAsync` não devolve os recebidos antes (o contrato mostra a marca pelo próprio marco) | @dev |
