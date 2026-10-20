# Revisão pré-Fase 2: relatório consolidado

- **Data:** 20/10/2026.
- **Objeto:** as 17 stories da Fase 2 (FIN-01 a FIN-06, COM-01a a COM-08), escritas pelo SM e validadas pelo PO no branch `docs/fase2-stories` (commit `c2c629a`).
- **Revisores:** quatro, todos somente leitura e conferindo as stories contra o código das Fases 0 e 1:
  - AIOX architect (Aria);
  - AIOX po + pm (Pax e Morgan);
  - design-squad, com `audit-design`;
  - cybersecurity, com `audit-app-security` (threat model).
- **Como usar:** a seção 1 lista o que depende do PO. As seções 2 e 3 entram nas stories e na ADR 0019 depois da decisão, sem nova consulta. Nada deste relatório foi aplicado ainda.

## 0. Vereditos

| Story | Arquitetura | Produto | Design | Segurança | Consolidado |
|---|---|---|---|---|---|
| FIN-01 Contas a receber | GO c/ ajustes | GO c/ ajustes | GO c/ ajustes | GO cond. | **GO com ajustes** |
| FIN-02 Contas a pagar | GO c/ ajustes | GO | GO c/ ajustes | **Não pronta** | **Ajustar antes (trava do título em OP)** |
| FIN-03a Ordem de pagamento | GO c/ ajustes | GO após D5 | GO c/ ajustes | **Não pronta** | **Ajustar antes (pago = aprovado, 2FA por ação)** |
| FIN-04 Movimentação bancária | GO c/ ajustes | GO c/ ajustes | GO | GO cond. | **GO com ajustes** |
| FIN-05 Fluxo de caixa | GO c/ ajustes | GO | GO c/ ajustes | GO | **GO com ajustes** |
| FIN-06 Inadimplência | GO c/ ajustes | Quebrar | GO | GO | **Quebrar em 06a/06b** |
| FIN-03b Alçadas | GO c/ ajustes | GO, baixa prioridade | Ajustar | **Não pronta** | **Ajustar antes (fracionamento, teto, viabilidade)** |
| COM-01a Orçamento: itens e custo | GO c/ ajustes | GO | Ajustar | GO cond. | **Ajustar antes (editor de custo, D7)** |
| COM-01b Marcos e validade | GO | GO | GO c/ ajustes | GO | **GO com ajustes** |
| COM-02 Revisões | GO c/ ajustes | GO | GO c/ ajustes | GO | **GO com ajustes** |
| COM-03 PDF e envio | **NO-GO** | GO | GO | GO cond. | **NO-GO até a ADR de PDF (D17) e anexo/e-mail** |
| COM-04a Aprovação → contrato | GO c/ ajustes | GO c/ ajustes | Ajustar | GO cond. | **Ajustar antes** |
| COM-04b Recebíveis por marco | **NO-GO** | GO c/ ajustes | GO c/ ajustes | GO | **NO-GO até o registro de gatilhos (2.1-C1)** |
| COM-05 Projeto 360 | GO c/ ajustes | GO | Ajustar | GO cond. | **Ajustar antes** |
| COM-06 OS de instalação | GO c/ ajustes | GO cond. | Ajustar | GO | **Ajustar antes** |
| COM-07 Aditivo (D) | GO c/ ajustes | GO | Ajustar | GO cond. | **Ajustar antes** |
| COM-08 Funil (D) | GO | GO | GO | GO | **GO** |

Os dois NO-GOs são de infraestrutura que ainda não existe (PDF, registro de eventos de gatilho). Não pedem mudança de escopo.

## 1. Decisões do PO

Cada decisão traz uma recomendação, que pode ser aprovada como está.

| # | Decisão | Recomendação | Origem |
|---|---|---|---|
| D1 | **Onde fica o dado financeiro real antes do go-live.** A homologação é separada da produção, que só liga na GOL-01 (Fase 5). Sem decisão, o PO lança títulos e saldos três vezes (Dolibarr, homologação, produção) | (a) **Go-live antecipado de Cadastros + Financeiro**: a produção liga ao fim do Marco 2.1 só com `parties` e `finance` (e o que já está pronto), e o resto dos módulos fica "não liberado" até cada um ficar pronto. Alternativa (b): promover o banco do tenant de homologação para produção na GOL-01, com as migrations testadas sobre ele. Registrar em ADR antes da FIN-01 | Produto C1 |
| D2 | **Implantação financeira (story nova FIN-00, P/M).** Data de corte única, lançamento rápido dos títulos em aberto pelo saldo com origem `Opening` (fora do DRE por competência e da pontualidade) e relatório de conferência "saldo do sistema = saldo do banco na data de corte" | Criar, entre a FIN-02 e a FIN-05. Volume pequeno (26 faturas e 164 de fornecedor), sem importar CSV | Produto C2 |
| D3 | **Contratos já em andamento.** Hoje não entram: assinatura sempre na data do clique, marco por data não pode estar no passado, sinal já recebido vira título em aberto | "Contrato de implantação" na COM-04a (ou COM-04c, P): data de assinatura ≤ hoje, marcos com data no passado e a marca "recebido antes da implantação" (título não gerado). A data de assinatura fica editável em toda aprovação (padrão hoje) | Produto A1, M6 |
| D4 | **Tarifas, IOF, juros e rendimentos.** Hoje só na BNK-03 (Fase 5); até lá cada tarifa exigiria CP + OP + 2FA e o saldo não bateria com o banco | Trazer para a FIN-04 o "lançamento bancário sem OP" que o 04-fluxos §1 já prevê: `finance.pay`, conta do plano obrigatória, motivo, aviso ao Proprietário e a lista "Movimentos sem OP" no extrato | Produto A2 |
| D5 | **Quem paga e quem aprova.** O grupo Financeiro do seed não tem `finance.pay`; a FIN-03b e o protótipo deixam o Financeiro aprovar até R$ 5.000, contra a ADR 0008 | Seed patch dando `finance.pay` ao Financeiro (adendo à ADR 0008). `finance.approve` continua só no Proprietário na Fase 2. A FIN-03b vai para o fim da fase, e a delegação de aprovação a outro grupo fica para quando houver mais de um aprovador | Produto M5, B5; Design 23 |
| D6 | **Proprietário aprovando a própria OP** | Permitido quando o Proprietário tem um único membro, com a marca "Aprovada pelo próprio solicitante" e o filtro correspondente. Com mais de um membro, desligado por padrão; a troca do parâmetro é auditada e notifica os Proprietários | Segurança M5 |
| D7 | **Quem vê custo e margem.** Pela recomendação das stories, todo `quotes.view` e `projects.view` vê; no seed, Engenharia e Supervisão veriam a margem no Projeto 360 | Ações novas `quotes.view_cost` e `projects.view_cost` (ADR 0003: sigilo comercial), no seed para Proprietário, Comercial e Financeiro. DTOs sem custo para os demais, com teste de arquitetura. O PDF nunca mostra custo | Segurança M3 |
| D8 | **Limites para desconto, cancelamento, transferência e data retroativa** | Parâmetros com padrão: desconto acima de 5% ou de R$ 500,00 e cancelamento de título com saldo acima de R$ 1.000,00 exigem `finance.approve`; transferência acima de R$ 10.000,00 notifica o Proprietário; baixa ou pagamento com data mais de 7 dias no passado exige motivo e notifica | Segurança A5, M4, B2 |
| D9 | **Forma de pagamento no título a pagar** (Pix, boleto, TED), para o aprovador ver para onde o dinheiro vai | Entrar na FIN-02 (chave Pix e titular, linha digitável validada, ou banco/agência/conta e documento do titular), congelada na OP, mostrada ao aprovar e ao pagar; titular diferente do CNPJ do fornecedor gera alerta | Segurança A4 |
| D10 | **Estorno de pagamento** | Exige `finance.approve`. O título estornado aparece marcado "pago e estornado em {data}" em nova OP | Segurança M6 |
| D11 | **P-18 (mensagens)** | Sucesso numa região `role="status"` no topo do conteúdo, sem bloquear, por no mínimo 5 s; erro sempre dentro do diálogo ou formulário, com foco; próximo passo sempre no `NextSteps`, nunca no toast (tirar `nextStep` do `toastDone` e do `ConfirmDialog`). ADR antes da FIN-01 | Design 1 |
| D12 | **Navegação do Financeiro** | Dois grupos: "Caixa e pagamentos" (A receber, A pagar, Ordens de pagamento, Contas e extrato, Fluxo de caixa, Inadimplência) e "Configuração financeira" (Plano de contas, Centros de custo, Condições, Recorrentes, Alçadas). Contratos e Instalações entram no menu nas suas stories | Design 6 |
| D13 | **Ordem da fase** | Ver seção 4 | Produto (2) |
| D14 | **Vencimento de parcela do contrato** | Editável no título em aberto, com motivo e auditoria (`finance.edit`); o valor continua mudando só pelo contrato ou pelo aditivo | Produto A4 |
| D15 | **Venda sem instalação** | "Registrar entrega" manual no projeto (como a fabricação concluída), com `projects.approve` e confirmação do impacto, liberado só sem OS de instalação aberta | Produto A3; Arquitetura B5 |
| D16 | **Prorrogar validade de orçamento enviado** | "Prorrogar validade" em `Sent` muda só a data, auditada, sem nova revisão | Produto M1 |
| D17 | **PDF (QuestPDF).** A licença Community só vale abaixo de um limite de receita anual | Confirmar a elegibilidade da Community (ou comprar a licença) e registrar em ADR antes da COM-03, junto com `IPdfRenderer`, fonte Archivo embutida e teste na imagem Alpine | Arquitetura A5 |

### 1.1 Pontos em aberto das stories

Os 29 pontos do README da Fase 2 ficam com a recomendação do SM, com estes ajustes:
- **4:** com D14 e com a exceção de origem cancelada (2.1-A3).
- **6:** somar a sugestão "Gerar título do recolhimento" ao pagar título com retenção, sem decidir (Produto M3).
- **11:** rejeitado na parte do lançamento avulso; substituído por D4.
- **14:** excluir títulos de origem `Opening` do cálculo.
- **19:** substituído por D7.
- **20:** com D15.
- **22:** com D16.
- **24:** com a data de assinatura editável (D3).
- **25:** a conta de receita é escolhida e validada na aprovação, sugerida "1.1", e vai no evento (2.1-A4).
- **26:** `cost_type` criado na COM-04a, junto com o campo de projeto no título a pagar, com o enum no SharedKernel.
- **27:** validar o checklist padrão da OS de instalação com a Konebras antes da COM-06.

Os demais (1, 2, 3, 5, 7 a 10, 12, 13, 15 a 18, 21, 23, 28, 29) ficam como recomendado.

## 2. Correções sem decisão do PO

### 2.1 Arquitetura (entram na ADR 0019, "Decisões técnicas da Fase 2")

- **C1. Eventos de gatilho fora de ordem (COM-04b, COM-06, COM-07).** O outbox não garante ordem: se o `ContractSigned` vai para o backoff, o `ProjectFabricated` ou o `TechnicalAcceptance` chegam antes, não acham título, e a liberação se perde. O Finance grava `finance.project_trigger_events (project_id, event, occurred_on)` com chave única: título criado depois de um evento já registrado nasce `Open`; evento sem título fica gravado e é aplicado quando o título nascer. Teste com `ContractSigned` atrasado e com aditivo depois do evento.
- **C2. Concorrência, transições e auditoria (FIN-01, FIN-03a, COM-02, COM-04a, COM-06).** Não existe `xmin` como token em nenhuma entidade, nem conversão de conflito em 409. Convenção: `xmin` em toda entidade de documento, versão enviada pela API (campo `version` ou If-Match), conflito como 409 ProblemDetails, transição pela entidade rastreada (status + `xmin`), nunca por `UPDATE` cru (que fura o interceptor de auditoria); se o SQL cru for inevitável, auditoria explícita por `IAuditWriter`.
- **A2. Idempotência de comando sem documento-pai** (transferência, parcelamento, envio de orçamento, aditivo, criação de OP): cabeçalho `Idempotency-Key` com índice único (`client_request_id`), devolvendo o resultado original no reenvio.
- **A3. Título órfão de origem cancelada.** Quando o contrato ou a OS de origem está `Cancelled` (consultado pelo Contracts do dono), o Finance libera o cancelamento do título com `finance.edit`.
- **A4. Parcelas do contrato.** A conta de receita é validada na aprovação, de forma síncrona, e vai no evento. Toast "Aprovado: contrato e projeto criados. Gerando as parcelas…", com estado de demora depois de cerca de 30 s. Mensagem em dead-letter alerta `finance.edit` no tenant, com ação de reprocessar.
- **A1 (FIN-03b). Alçada viável.** Recusar o mesmo grupo ou usuário em níveis com faixas que se sobrepõem; no envio, cada nível precisa de um aprovador elegível, distinto do solicitante e dos outros níveis, senão 422 com o motivo. Teste "um Proprietário só".
- **M1. Anexos e e-mail nos BuildingBlocks.** `ReadAction`/`WriteAction` por dono de anexo; `OwnerExists` obrigatório (com teste de arquitetura) e `CanAttach(estado)` opcional; allowlist de tipo e tamanho por dono (comprovante pdf/png/jpg até 10 MB, contrato só pdf, OS imagem/pdf); `StoreGeneratedAsync` interno, sem rota, na transação do chamador, para o PDF gerado; `IEmailSender.Enqueue` com anexos e cópia; espera do anexo `Clean` com reagendamento próprio, sem gastar as tentativas do `SendEmailJob`.
- **M2 e M3. Título a pagar.** `paid_amount` em relação ao líquido; transição condicional do status do título ao entrar na OP, no lugar do índice único parcial (que dependeria de outra tabela).
- **M4.** "Abater saldo" como ação própria: baixa com total 0 e sem transação.
- **M5. Arredondamento.** Só no total do item (custo unitário com 6 casas × quantidade → 2 casas); orçamento do projeto distribuído pelo maior resto; `margin_pct numeric(9,4)` ou nulo com preço 0; marcos pelo `PaymentTermSplitter`.
- **M6. Parâmetros.** Cada módulo é dono dos próprios parâmetros (adendo à ADR 0016); Configurações só monta a tela.
- **M7. Numeração com revisão.** Adendo à ADR 0016 antes da COM-01a: único em `(number, revision)` e único parcial em `number WHERE is_current`, criados certos na primeira migration.
- **M8. Jobs e notificações.** `dedup_key` com índice único `(user_id, dedup_key)` em `shared.notifications`; todo job idempotente, com teste de execução dupla e de dois tenants; recorrentes no `RecurringJobsRegistrar` com o fuso de São Paulo.
- **M9. Listas grandes.** `IParties.FindManyAsync(ids)` e `IProjects` em lote; filtro pelo id escolhido no seletor e nomes resolvidos por página.
- **M10. Contracts × Contracts.** Vocabulário compartilhado (natureza do custo, gatilho, origem) no SharedKernel; regra de arquitetura "Contracts não referencia Contracts".
- **M11.** Implementar o teste de posse de tabela da ADR 0016 antes dos consumidores cruzados da COM-04b.
- **M12.** Conclusão do contrato avaliada também no `ProjectDelivered`; `ReceivableSettled` com `settlementId` e `isReversal`; estorno depois de `Completed` reabre o contrato.
- **M13.** CP `Planned` do instalador com data estimada (`planned_end` + condição do instalador), marcada como estimativa, para entrar no fluxo de caixa.
- **M14.** Atualizar o 04-fluxos §2 (contrato e projeto na mesma transação) e o 03 (`origin`/`origin_id` nos títulos); notificação dentro do próprio módulo é síncrona, pelo `INotificationService`.
- **Baixos:** OP estornada no estado `Reversed`; sobreposição de faixas com `EXCLUDE USING gist`; atributo de auditoria "só criado" para o snapshot do contrato; aditivo único em `(contract_id, number)`; `ContractCancelled` sem texto livre; índices parciais por `(status, due_date)` e `(bank_account_id, date)`.

### 2.2 Segurança (viram CAs)

- **C1. Pago = aprovado (FIN-02, FIN-03a).** Título em OP não finalizada recusa mudar fornecedor, valor, retenções, conta, centro ou projeto (409 `payable_in_payment_order`). A OP grava no envio o snapshot de fornecedor, valor líquido e forma de pagamento por item. No pagamento, principal + juros + multa − desconto acima do aprovado + tolerância (parâmetro, padrão R$ 0,00) responde 409 `exceeds_approved` e pede nova aprovação.
- **A1. 2FA por ação.** Filtro `RequireTwoFactor` em aprovar, pagar, estornar, transferir e editar alçada: confere `TotpEnabled` e `amr` com `otp`, senão 403 `two_factor_required`. Conceder ação reservada (inclusive por seed patch) invalida o security stamp dos afetados. TOTP recente (até 15 min) para pagar, estornar e editar alçada.
- **A2. Fracionamento.** A faixa da alçada é resolvida pelo maior valor original entre os títulos da OP, somando as OPs do mesmo fornecedor em 30 dias; o aprovador vê "possível fracionamento". Teste 4 × R$ 5.000,00.
- **A3. Regra por conta com teto obrigatório;** acima do teto vale a regra por valor; troca de conta de título com saldo acima do limite notifica o Proprietário.
- **A4.** Ver D9; avisos "fornecedor criado há menos de 30 dias" e "dados bancários alterados há {n} dias" na OP.
- **A5.** Ver D8; relatório "descontos e cancelamentos do período"; o Proprietário é notificado em todo cancelamento.
- **M2. E-mail do orçamento.** Destinatário e cópia como endereço puro (sem nome, vírgula nem CR/LF); assunto e nome fantasia sem CR/LF; nome de exibição "{nome fantasia} via BemAqui"; Reply-To do usuário; a mensagem do usuário entra como valor do modelo, nunca como template (teste com `{{ }}` e `{% %}`); limite de 50 envios por dia por tenant; SPF, DKIM e DMARC no domínio de envio.
- **M4.** Criação de conta bancária notifica o Proprietário; transferência para conta criada há menos de 30 dias exige `finance.approve`.
- **M8. Valores do cliente revalidados no servidor.** Cada título da redução do aditivo pertence ao contrato, está sem baixa e 0 ≤ novo ≤ saldo (ao salvar e no consumidor); % do marco > 0 e ≤ 100; cada retenção ≥ 0.
- **Baixos:** e-mail ao aprovador só com número, total e link; diálogo de aprovação destaca "preço abaixo do custo" e "preço digitado à mão"; alteração do valor da OS de instalação notifica `finance.edit` e a OP mostra "combinado × orçado"; SQL do fluxo e do aging só parametrizado, com allowlist de ordenação; pontualidade de cliente PF só com `finance.view` e `no-store`; limite de itens e de tamanho das observações no PDF; argumentos de job só com ids.

### 2.3 Design (padrões da Fase 2, entram no README)

1. **Ação com efeito financeiro:** diálogo grande (`size="lg"`, cerca de 640 px, tela cheia a 400 px) com o impacto (valores, conta, títulos afetados, saldo antes e depois); botão com o mesmo verbo; próximo passo no `NextSteps`; `recovery` quando o servidor recusa; estilo `destructive` só para estornar e cancelar.
2. **Pré-condições antes de confirmar:** o diálogo lista o que falta, com link para corrigir e o botão desabilitado com o motivo; o 422 é só defesa.
3. **Erros:** 422 sempre no campo, inclusive em linhas de editor e dentro de diálogos (`applyServerErrors` com caminhos aninhados); contador por aba; 409 de concorrência com "Recarregar".
4. **Valores:** todo % com o rótulo do que é ("Markup 40,00% · Margem 28,57%"); valor digitado à mão com Tag e "Voltar ao sugerido"; margem negativa no chip crítico "Preço abaixo do custo"; totais ao vivo num rodapé fixo, anunciados com espera de cerca de 500 ms.
5. **Somas que precisam fechar:** mostrar o que falta ("Faltam 10,00%"), com "Dividir igualmente" e "Ajustar o último".
6. **Situação:** o mesmo mapa de chips em todas as listas (Previsto e Cancelado neutros, Em aberto info, Vence hoje alerta, Vencido crítico, Pago e Aprovado ok); faixa lateral vermelha para atrasado e âmbar para risco.
7. **Etapa:** `StageIndicator` com letra explícita por etapa e rótulo visível; a lista de etapas do projeto é definida na COM-04a.
8. **Detalhe de documento:** cabeçalho com número, revisão, chip e contraparte; resumo em "facts" (4 → 2 colunas a 400 px); abas na URL; faixa de modo leitura que explica por quê e oferece a saída ("Nova revisão").
9. **Estados:** skeleton por aba; vazio com verbo e ação; processamento assíncrono com tempo limite; sem permissão financeira, a aba some.
10. **Gráficos:** Recharts com tokens `--chart-*` claros e escuros; realizado e projetado distinguidos pelo traço (contínuo × tracejado); tabela equivalente acessível por teclado.
11. **400 px:** cada story declara as colunas secundárias (número, contraparte, vencimento, valor e situação ficam sempre); editores de linha viram cartões; checklist de obra com alvos de 56 px; E2E de 400 px e teclado em toda tela com editor ou diálogo financeiro.
12. **2FA no momento da decisão:** estado único dentro do diálogo ("Para aprovar, ative a verificação em duas etapas." com "Ativar agora", ou o pedido do código), sem perder o preenchido.
13. **Navegação:** D12; cada story declara a sua entrada no `modules.ts`.

**Ajustes de tela por story:**
- **COM-01a:** editor de custo por natureza (seções com subtotal, chave estável, foco depois de remover) e resumo compacto a 400 px.
- **COM-01b:** coluna Validade com "vence em X"; % e R$ lado a lado.
- **COM-02:** comparação empilhada a 400 px.
- **COM-05:** nomes alinhados ao protótipo (Orçado, Realizado, Projetado, Desvio) e colunas da lista (Etapa, Entrega, Contratado, Margem, Recebido, atraso).
- **COM-06:** texto do aceite: "Confirma o título a pagar de {valor} ao instalador (vence em {data}, segue para ordem de pagamento) e libera a parcela {marco} de {valor} do cliente."
- **COM-07:** editor em página própria, com prévia "antes → depois".
- **FIN-01:** "Quitar o saldo com desconto" no lugar de "Baixar".
- **FIN-04:** verbo "Transferir" no gatilho e no botão.
- **FIN-06:** vazio com "Ver a receber".
- **COM-03:** "Escolher arquivo" além do arrastar.

## 3. Pré-requisitos compartilhados

Uma story técnica curta antes da FIN-01 (**FND-09e**, M), para nenhuma tela improvisar a sua versão:
- P-18 aplicada (D11) e `ConfirmDialog` grande com formulário e erro por campo;
- `applyServerErrors` com caminhos aninhados;
- `RowsEditor` com modelo de colunas, chave estável e erro por linha;
- `DataTable` com seleção, rodapé de totais e linha expansível;
- tokens de gráfico e Recharts;
- convenção de concorrência e idempotência (2.1-C2, A2);
- filtro `RequireTwoFactor` (2.2-A1).

As extensões de anexo e e-mail (2.1-M1) podem ficar para a primeira story que precisar delas (FIN-03a e COM-03).

## 4. Ordem recomendada

Começar pelo financeiro: não depende da P-01, usa os cadastros reais e entrega o resultado da fase ("OP aprovada e caixa projetado").

```
Pré:  ADR 0019 + ADR da P-18 + adendos às ADRs 0008 e 0016 + decisão D1 + FND-09e
2.1a "Ver o caixa":     FIN-01 → FIN-02 → FIN-00 (implantação) → FIN-05 → FIN-06a (aging + aviso)
2.1b "Pagar com OP":    FIN-03a → FIN-04 (+ lançamento sem OP)
2.2  Orçamento:         COM-01a → COM-01b → COM-02 → COM-03
2.3  Contrato ao caixa: COM-04a (+ implantação de contrato, cost_type) → COM-04b (+ entrega manual) → COM-05 → COM-06
2.4  Controles e D:     FIN-06b → COM-07 (D) → FIN-03b → COM-08 (D)
```

Com D1 (a), o go-live de Cadastros + Financeiro acontece ao fim do 2.1b.
