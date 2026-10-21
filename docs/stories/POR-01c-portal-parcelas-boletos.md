# POR-01c — Portal do cliente: parcelas e boletos

| Campo | Valor |
|---|---|
| ID | POR-01c |
| Título | O cliente vê no portal as próprias parcelas (em aberto, vencidas, pagas) por projeto, com vencimento e valor, e, para o boleto com Pix emitido, a linha digitável, o Pix copia e cola, o QR code e o PDF; a situação atualiza com a baixa pelo webhook; nunca títulos de outro cliente, juros internos, descontos negociados de outros ou observações internas |
| Tamanho | M (quebra da POR-01) |
| Status | Draft |
| Requisitos | PRD §5.15 ("boletos em aberto") · R-FIN-3 · R-FIN-7 (bolepix) · R-SEC-5 · PRD §6 LGPD |
| Dependências | POR-01a, POR-01b; FIN-01 (títulos a receber), COM-04b (parcelas do contrato); Fase 5 BNK-01 (boleto) e BNK-02 (baixa por webhook) |
| Bloqueia | WPP-01 (link para o boleto) |
| Ordem de execução | 3 (Marco 8.2 "Acompanhamento") |

## User story
Como **cliente**, quero **ver as minhas parcelas e pegar o boleto ou o Pix na hora**, para **pagar sem pedir a segunda via ao financeiro**.

## Contexto e referências
- PRD §5.15; `03-modelo-dados` → `finance.receivables` (`party_id`, `project_id`, `due_date`, `amount`, `paid_amount`, `status`), `finance.billing_slips` (`digitable_line`, `pix_copy_paste`, `qr_code`, `pdf_path`, `status`).
- `04-fluxos` §1 (título: "Vencido" calculado, nunca gravado; boleto `Issued → Paid | Cancelled | Expired`).
- BNK-01/BNK-02 (Fase 5): sem eles, o portal mostra só as parcelas.

## Critérios de aceite
1. **Dado** um usuário do portal, **quando** abre `/portal/financeiro` ou a aba Financeiro do projeto, **então** vê as parcelas do seu cliente (pelo `IReceivables.ForPortalAsync(partyId)`, proposta) dos títulos `Open`, `PartiallyPaid` e `Paid` dos últimos 12 meses (proposta), com projeto, descrição pública (o marco do contrato: "Sinal", "Conclusão da fabricação"), vencimento, valor, valor pago e situação com os chips da Fase 2 (Em aberto, Vence hoje, Vencido, Pago); títulos `Planned` aparecem como "Previsto: {marco}" sem data quando o gatilho não ocorreu (proposta, Pontos em aberto 1).
2. **[Segurança]** **Dado** a resposta, **quando** montada, **então** não tem descrição interna, observação, conta contábil, centro de custo, motivo de cancelamento, desconto concedido, quem baixou, conta bancária da baixa nem títulos `Cancelled` (teste de arquitetura dos DTOs).
3. **Dado** uma parcela com boleto `Issued` (BNK-01), **quando** abro, **então** vejo a linha digitável e o Pix copia e cola com "Copiar", o QR code e "Baixar boleto (PDF)"; o PDF passa pelas mesmas conferências de download da POR-01b CA 6 (do cliente da sessão, `Clean`, auditado).
4. **Dado** um boleto `Expired` ou `Cancelled`, **quando** abro a parcela em aberto, **então** vejo "Boleto vencido. Peça um novo à {empresa}." com o contato de cobrança do tenant; a emissão de segunda via pelo próprio cliente fica para o ponto em aberto 2.
5. **Dado** o pagamento baixado (webhook, BNK-02, ou baixa manual), **quando** o cliente abre o portal, **então** a parcela aparece "Pago em {data}" e o boleto some; o portal não guarda cópia, lê o estado atual.
6. **Dado** o portal sem BNK-01 em produção, **quando** abro uma parcela, **então** vejo só os dados da parcela e a mensagem "Para pagar, use o boleto enviado por e-mail ou fale com a {empresa}."
7. **Dado** o módulo `portal` ou `finance` não liberado, **quando** chamo, **então** 403 `module_not_released` com a tela padrão; IDOR em `/portal/receivables/{id}` e `/portal/receivables/{id}/slip.pdf` (404 uniforme para título de outro cliente, teste com dois clientes e dois tenants).
8. **Dado** a consulta e cada download de boleto, **quando** consulto a auditoria do portal, **então** vejo quem, quando e o IP.

## Regras de negócio e técnicas
- **Contrato** `IReceivables.ForPortalAsync(partyId, from)` e `IBillingSlips.ForPortalAsync(partyId, receivableId)` (Finance.Contracts, propostas), que filtram pelo cliente e devolvem só os campos públicos.
- "Vencido" e "Vence hoje" calculados por `BusinessTime.Today`.
- Valores em `numeric(18,2)` exibidos em `pt-BR` com `tabular-nums`.

## Permissões, eventos e tabelas
- **Permissões:** usuário do portal (sessão).
- **Eventos:** nenhum.
- **Tabelas:** nenhuma nova.

## Telas e estados
- **Portal:** `/portal/financeiro` (todas as parcelas, agrupadas por projeto, total em aberto no topo) e a aba Financeiro em `/portal/projetos/$id`.
- **Estados:** carregando; vazio ("Nenhuma parcela em aberto."); erro; sem boleto (CA 4, 6).
- **400 px:** cartões; "Copiar" com alvo grande e confirmação "Copiado".
- **Ajuda:** `Ajuda/portal-financeiro.md` e a aba `financeiro` da `Ajuda/portal-projeto.md`.

## Plano de testes
- **Integração (Testcontainers):** parcelas só do cliente e campos públicos (CA 1, 2); boleto e PDF (CA 3); vencido e sem BNK (CA 4, 6); pago (CA 5); IDOR (CA 7); auditoria (CA 8).
- **Arquitetura:** DTOs públicos (CA 2).
- **E2E (Playwright):** copiar o Pix no celular a 400 px.

## Fora do escopo
- Segunda via pelo cliente, renegociação e pagamento por cartão.
- Nota de débito, juros e multa calculados na tela: depois do MVP.

## Pontos em aberto
1. **Parcelas previstas** (PO): mostrar os marcos futuros sem data (proposta) ou só os títulos com vencimento.
2. **Segunda via pelo cliente** (PO + financeiro): o cliente pede e o boleto novo é emitido automaticamente (BNK-01) ou o financeiro aprova.
3. Período do histórico (12 meses, proposta).

## Definition of Done
- [ ] Critérios de aceite atendidos; testes de integração, arquitetura e E2E passando.
- [ ] Telas com estados; ajuda; teste de cobertura passando; auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Contratos `ForPortal*` no Finance; endpoints `/api/v1/portal/receivables`; telas e ajuda.

## Quality gates
- Agentes previstos: @dev, cybersecurity (IDOR, campos públicos), @qa.
- Gate: CA 1, 2 e 7 obrigatórios.

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

- **Correções feitas na validação:** campos públicos por allowlist (CA 2); funciona sem a BNK-01 (CA 6).
- **Pré-condições para `Ready`:** nenhuma de decisão; POR-01a e POR-01b concluídas; BNK-01 e BNK-02 para os CAs 3 a 5.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Draft` até as dependências.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da POR-01) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
