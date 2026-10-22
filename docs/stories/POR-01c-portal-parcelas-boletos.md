# POR-01c — Portal do cliente: parcelas e boletos

| Campo | Valor |
|---|---|
| ID | POR-01c |
| Título | O cliente vê no portal as próprias parcelas dos últimos 12 meses (em aberto, vencidas, pagas e previstas "Após {marco}") por projeto, com vencimento e valor; para o boleto com Pix emitido, a linha digitável, o Pix copia e cola, o QR code e o PDF; segunda via pelo próprio cliente, com limite, só do boleto mais recente; a situação atualiza com a baixa pelo webhook; nunca títulos de outro cliente, descontos, observações nem dados internos |
| Tamanho | M (quebra da POR-01) |
| Status | Ready |
| Requisitos | PRD §5.15 ("boletos em aberto") · R-FIN-3 · R-FIN-7 (bolepix) · R-SEC-5 · PRD §6 LGPD |
| Dependências | POR-01a, POR-01b; FIN-01 (títulos a receber), COM-04b (parcelas do contrato); BNK-01b (boleto, segunda via e Configurações → Cobrança) e BNK-02 (baixa por webhook) |
| Bloqueia | POR-02, WPP-01 (o aviso leva ao financeiro do portal) |
| Ordem de execução | 3 (Marco 8.2 "Acompanhamento") |

## User story
Como **cliente**, quero **ver as minhas parcelas e pegar o boleto ou o Pix na hora, ou uma segunda via quando o boleto venceu**, para **pagar sem pedir nada ao financeiro**.

## Contexto e referências
- PRD §5.15; `03-modelo-dados` → `finance.receivables` (`party_id`, `project_id`, `due_date`, `amount`, `paid_amount`, `status`), `finance.billing_slips` (`digitable_line`, `pix_copy_paste`, `qr_code`, `pdf_path`, `status`).
- `04-fluxos` §1 (título: "Vencido" calculado, nunca gravado; boleto `Issued → Paid | Cancelled | Expired`).
- BNK-01b e BNK-02 (Fase 5): emissão, segunda via e baixa. **Inter em produção só na GOL-01b** (ADR 0034): até lá, o boleto no portal funciona no tenant `demo` com o sandbox; no `konebras`, vale o CA 7.
- **Revisão pré-Fases 4 a 8** (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`): D8 (limites de multa, juros e desconto), D13, D16; tabela 3.5, pontos 11 a 13; §4.3 POR-01a e transversal (rate limit por política nomeada); §4.4 padrões 5 e 6.
- **ADR 0040** (portal), **ADR 0032** (Banco Inter), **ADR 0042** (Configurações → Cobrança).

## Critérios de aceite
1. **Dado** um usuário do portal, **quando** abre `/portal/financeiro` ou a aba Financeiro do projeto, **então** vê as parcelas do seu cliente (pelo `IReceivables.ForPortalAsync(partyId)`) dos títulos `Open`, `PartiallyPaid` e `Paid` com vencimento nos **últimos 12 meses** ou no futuro, com projeto, descrição pública (o marco do contrato: "Sinal", "Conclusão da fabricação"), vencimento, valor, valor pago e situação com os chips da Fase 2 (Em aberto, Vence hoje, Vencido, Pago), e o total em aberto no topo.
2. **Dado** um título `Planned` cujo gatilho ainda não ocorreu, **quando** listado, **então** aparece como "Previsto: após {etapa em linguagem simples}" ("após a conclusão da fabricação"), com o valor e **sem data**.
3. **[Segurança]** **Dado** a resposta, **quando** montada, **então** não tem descrição interna, observação, conta contábil, centro de custo, motivo de cancelamento, desconto concedido, quem baixou, conta bancária da baixa nem títulos `Cancelled` (teste de arquitetura dos DTOs).
4. **Dado** uma parcela com boleto `Issued` (BNK-01b), **quando** abro, **então** vejo a linha digitável e o Pix copia e cola com "Copiar" (confirmação "Copiado"), o QR code e "Baixar boleto (PDF)"; o PDF passa pelas mesmas conferências de download da POR-01b CA 7 (do cliente da sessão, `Clean`, prefixo do tenant, auditado).
5. **Dado** a parcela em aberto cujo boleto **mais recente** está `Expired` ou vencido, **quando** clico "Gerar segunda via", **então** o pedido vai para a fila da BNK-01b (segunda via do boleto mais recente, com o novo vencimento em `finance.portal_reissue_due_days` dias úteis, pré-configurado: 3, e a multa e os juros de Configurações → Cobrança, nunca desconto), a tela mostra os estados do padrão 6 ("Gerando o boleto…", "Demorando mais que o normal" depois de 30 s, "Boleto pronto", "Não foi possível gerar: {motivo}. Fale com a {empresa}.") e o botão fica desligado enquanto processa; boleto antigo nunca é reemitido.
6. **[Segurança]** **Dado** a segunda via, **quando** o cliente pede, **então** vale o limite de `finance.portal_reissue_limits` (pré-configurado: uma por parcela a cada 24 h e até 3 por parcela), com rate limit pela política nomeada `portal-reissue` por usuário do portal e por tenant; acima do limite, "Você já pediu a segunda via desta parcela. Fale com a {empresa}." com o bloco de contato; o financeiro recebe a notificação "Segunda via pedida pelo portal: {cliente}, {título}" e cada pedido é auditado.
7. **Dado** o tenant sem a integração Inter ativa (BNK-01a) ou em sandbox fora do `demo`, **quando** abro uma parcela, **então** vejo só os dados da parcela e "Para pagar, use o boleto enviado por e-mail ou fale com a {empresa}." com o bloco de contato; "Gerar segunda via" não aparece.
8. **Dado** o pagamento baixado (webhook, BNK-02, ou baixa manual), **quando** o cliente abre o portal, **então** a parcela aparece "Pago em {data}" e o boleto some; o portal não guarda cópia, lê o estado atual.
9. **Dado** o módulo `portal` ou `finance` não liberado, **quando** chamo, **então** 403 `module_not_released` com a tela padrão; IDOR em `/api/v1/portal/receivables/{id}`, `/slip.pdf` e `/reissue` (404 uniforme para título de outro cliente, teste com dois clientes e dois tenants).
10. **Dado** a consulta, cada download de boleto e cada segunda via, **quando** consulto a auditoria do portal, **então** vejo quem, quando e o IP.

## Regras de negócio e técnicas
- **Contrato** `IReceivables.ForPortalAsync(partyId, from)`, `IBillingSlips.ForPortalAsync(partyId, receivableId)` e `IBillingSlips.RequestReissueFromPortalAsync(partyId, receivableId, portalUserId)` (Finance.Contracts), que filtram pelo cliente e devolvem só os campos públicos; a segunda via usa o caminho da BNK-01b (`IBankBillingProvider`, `TenantJob` com ids).
- **Parâmetros** `finance.portal_reissue_due_days` e `finance.portal_reissue_limits` em `finance.parameters`, editados na seção "Segunda via pelo portal" de **Configurações → Cobrança** (tela da BNK-01b, `settings.edit`, ADR 0042).
- "Vencido" e "Vence hoje" calculados por `BusinessTime.Today`; histórico de 12 meses pela data de vencimento.
- Valores em `numeric(18,2)` exibidos em `pt-BR` com `tabular-nums`.

## Permissões, eventos e tabelas
- **Permissões:** usuário do portal (sessão); `settings.edit` nos parâmetros.
- **Eventos:** nenhum novo (a segunda via usa os da BNK-01b).
- **Tabelas:** `finance.portal_reissue_requests` (`receivable_id`, `billing_slip_id`, `portal_user_id`, `requested_at`, `status`, `new_billing_slip_id`); parâmetros novos.

## Telas e estados
- **Padrões comuns das Fases 4 a 8:** 5 (Kit Portal: "Fale com a {empresa}" em todo erro e no boleto vencido, alvos de 44 px); 6 (segunda via como integração assíncrona, sem reenvio enquanto processa).
- **Portal:** `/portal/financeiro` (todas as parcelas, agrupadas por projeto, total em aberto no topo) e a aba Financeiro em `/portal/projetos/$id`.
- **Backoffice:** seção "Segunda via pelo portal" em Configurações → Cobrança (prazo e limites, estados de carregando e erro, auditoria antes/depois).
- **Estados:** carregando; vazio ("Nenhuma parcela em aberto."); erro; sem boleto (CA 7); vencido (CA 5).
- **Propositivo:** a parcela vencida já mostra "Gerar segunda via"; a vencer em até 3 dias aparece primeiro.
- **400 px:** cartões; "Copiar" com alvo grande e confirmação "Copiado".
- **Ajuda:** `Ajuda/portal-financeiro.md` e a aba `financeiro` da `Ajuda/portal-projeto.md` (previstas); atualizar `Ajuda/configuracoes-cobranca.md` ("Segunda via pelo portal").

## Plano de testes
- **Integração (Testcontainers):** parcelas só do cliente, 12 meses e previstas sem data (CA 1, 2); campos públicos (CA 3); boleto e PDF (CA 4); segunda via do mais recente com os estados (CA 5); limite, rate limit e notificação (CA 6); sem integração (CA 7); pago (CA 8); IDOR (CA 9); auditoria (CA 10).
- **Arquitetura:** DTOs públicos (CA 3).
- **E2E (Playwright):** copiar o Pix e pedir a segunda via no celular a 400 px, no tenant `demo` com o sandbox.

## Fora do escopo
- Renegociação, parcelamento e pagamento por cartão.
- Nota de débito, juros e multa calculados na tela: depois do MVP.

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fases 4 a 8, tabela 3.5, pontos 11–13):
1. Parcelas previstas sem data, como "Após {marco}" (em linguagem simples) (CA 2).
2. Segunda via pelo próprio cliente, com limite, só do boleto emitido mais recente (CA 5, 6).
3. Histórico de 12 meses (CA 1).

Decisões da revisão aplicadas: D8 (segunda via sem desconto; multa e juros de Configurações → Cobrança, CA 5); D13/ADR 0032 e ADR 0034 (Inter em produção só na GOL-01b; sandbox no `demo`, CA 7); D16/ADR 0040; §4.3 transversal (rate limit por política nomeada, CA 6); §4.4 padrões 5 e 6; ADR 0042 (parâmetros em Configurações → Cobrança).

**Pré-condição externa:** nenhuma (o boleto real depende da GOL-01b, já prevista).

## Definition of Done
- [ ] Critérios de aceite atendidos; testes de integração, arquitetura e E2E passando.
- [ ] Migration criada (não destrutiva); parâmetros com seed.
- [ ] Telas com estados; ajudas previstas escritas; teste de cobertura passando; auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados; `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Contratos `ForPortal*` e segunda via no Finance; endpoints `/api/v1/portal/receivables`; política `portal-reissue`.
- [ ] Seção em Configurações → Cobrança; telas e ajudas.

## Quality gates
- Agentes previstos: @dev, cybersecurity (IDOR, campos públicos, limite), @qa.
- Gate: CA 1, 3, 5, 6 e 9 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fases 4 a 8).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: BNK-01b pelo nome novo; POR-02 em "Bloqueia") |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** campos públicos por allowlist (CA 2); funciona sem a integração do Inter (CA 6).
- **Correções da revalidação (21/10/2026):** pontos 11 a 13 decididos (previstas "Após {marco}", CA 2; segunda via pelo cliente com limite e só do mais recente, CA 5, 6; 12 meses, CA 1); Inter em produção só na GOL-01b (CA 7); estados assíncronos (padrão 6); parâmetros em Configurações → Cobrança; dependências pelos nomes novos; CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma de decisão de story.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada (quebra da POR-01) | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (D8, D13, D16, tabela 3.5 pontos 11–13, §4.3 transversal, §4.4 padrões 5 e 6, ADR 0042); nova ordem (Marco 8.2, ordem 3, antes do go-live); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
