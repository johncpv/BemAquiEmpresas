# IMP-01 — Cupom não fiscal em bobina de 58 e 80 mm: modo "só cupom não fiscal" ligado pelo Proprietário com aceite registrado, a porta `HardwareBridge` com a impressão pelo navegador, o assistente "Instalar impressora" e o relatório de vendas sem documento fiscal

| Campo | Valor |
|---|---|
| ID | IMP-01 |
| Título | Cupom não fiscal em bobina de 58 e 80 mm: modo "só cupom não fiscal" ligado pelo Proprietário com aceite registrado, a porta `HardwareBridge` com a impressão pelo navegador, o assistente "Instalar impressora" e o relatório de vendas sem documento fiscal |
| Tamanho | M |
| Status | Draft (plano proposto em 15/11/2026; aguarda a aprovação do PO) |
| Fase | 11 — PDV balcão com cupom não fiscal e offline (marco MVP) |
| Requisitos | Pedido do PO de 15/11/2026 (item 7: impressoras com NFC-e ou cupom não fiscal) · [Rascunho B](../adr/rascunho-B-impressao-perifericos.md) itens 1, 3, 4, 9 e 10 · ADR 0041 (WhatsApp) · ADR 0045 (PDF) |
| Dependências | PDV-03 (a PP-05 foi decidida pelo PO em 15/11/2026) |
| Bloqueia | IMP-02, CX-02 (comprovantes) |
| Paridade com o mercado | Requisitos mínimos 2 (cupom não fiscal) do [benchmark](../00-benchmark-mercado.md#7-matriz-de-paridade-cada-requisito-mínimo-e-a-story-que-o-cobre) |
| Ordem de execução | 6 (Fase 11) |

## User story
Como **operador de caixa**, quero **entregar um comprovante impresso ao cliente**, para **ele conferir o que comprou**, e como **dono**, quero **instalar a impressora sem chamar técnico**.

## Contexto e referências
- O MVP não emite NFC-e. O comprovante é um recibo que diz "Não é documento fiscal".
- **Decisão do PO de 15/11/2026 (PP-05):** o cupom não fiscal vale para qualquer empresa que decida, por conta própria, não emitir o cupom fiscal. A escolha é responsabilidade do cliente; o sistema registra o aceite, marca o comprovante e dá o relatório.
- O recibo sai de um modelo estruturado (linhas, colunas, QR, corte, gaveta). Nesta story o modelo vira HTML para a impressão do navegador; na IMP-02 o mesmo modelo vira bytes ESC/POS.

## Critérios de aceite
1. **Dado** a porta `HardwareBridge`, **quando** leio o código, **então** toda impressão do PDV passa por ela; o `BrowserPrintBridge` é o adaptador desta story.
2. **Dado** uma venda concluída, **quando** aciono "Imprimir cupom", **então** sai o recibo com: nome e CNPJ da empresa e da loja, número da venda, data e hora, operador, itens (quantidade, descrição, unitário, total), descontos, total, pagamentos por forma, troco, CPF do cliente quando informado e, em destaque, **"Não é documento fiscal"**.
3. **Dado** uma impressora de 58 mm e outra de 80 mm, **quando** imprimo, **então** o recibo respeita 32, 42 ou 48 colunas conforme o cadastro, sem cortar texto nem valor.
4. **Dado** uma venda já impressa, **quando** reimprimo, **então** o recibo sai com "2ª VIA" e a reimpressão fica registrada, com quem e quando.
5. **Dado** a loja configurada para "perguntar", **quando** concluo a venda, **então** escolho entre "Imprimir", "Não imprimir" e, com rede e contato do cliente, "Enviar por e-mail" ou "Enviar por WhatsApp".
6. **Dado** `pos.manage_devices`, **quando** abro "Instalar impressora", **então** sigo três passos: escolher a conexão; escolher a largura e o modelo, de uma lista curta; "Imprimir teste" com régua de colunas, acentos, QR Code e corte, e a pergunta "Saiu certo?".
7. **Dado** a configuração da impressora salva, **quando** outro aparelho do mesmo caixa sincroniza, **então** recebe a configuração.
8. **Dado** a impressora sem papel, desligada ou sem resposta, **quando** imprimo, **então** a venda **não** é afetada e vejo "Não foi possível imprimir." com "Tentar de novo", "Outra impressora" e "Seguir sem imprimir".
9. **Dado** a falta de rede, **quando** imprimo na impressora local, **então** funciona.
10. **[Segurança]** **Dado** uma loja nova, **quando** abro a configuração, **então** o modo "só cupom não fiscal" vem desligado e **só o Proprietário** pode ligá-lo; ao ligar, ele lê e aceita um texto que diz que a obrigação de emitir documento fiscal é da empresa, que o BemAqui não avalia se ela é dispensada e que a empresa deve se orientar com a contadora dela; o aceite fica no `audit_log` com quem, quando, a loja e a versão do texto. Sem o aceite, a loja não vende (até existir a NFC-e, na Fase 13).
11. **Dado** quem não é do grupo Proprietário, **quando** tenta ligar ou desligar o modo, **então** recebe 403, mesmo tendo acesso às configurações.
12. **Dado** `sales.view`, **quando** abro "Vendas sem documento fiscal", **então** vejo por período, loja e operador a quantidade e o valor das vendas sem documento fiscal, com exportação; o relatório inclui as vendas feitas com `pos.skip_fiscal` em loja que emite NFC-e (Fase 13).
13. **Dado** o texto do aceite alterado numa versão nova, **quando** o Proprietário entra, **então** o sistema pede novo aceite, e o anterior fica guardado.

## Regras de negócio e técnicas
- O texto "Não é documento fiscal" é fixo no modelo do recibo e não pode ser removido nem editado pelo tenant.
- O texto do aceite é versionado; a redação final passa pelo jurídico e usa a orientação da contadora (QC-13), mas isso não trava a story.
- O recibo é gerado no aparelho, do dado local; não depende do servidor.
- Reimpressão é evento registrado.
- Envio por e-mail e WhatsApp usa as filas existentes, com o consentimento da ADR 0041.

## Permissões, eventos e tabelas
- **Permissões:** `pos.create` (imprimir e reimprimir); `pos.manage_devices` (instalar impressora); ligar o modo não fiscal é reservado ao grupo Proprietário; `sales.view` para o relatório.
- **Endpoints:** `GET|POST|PUT /api/v1/pos/printers`; `POST /api/v1/pos/stores/{id}/non-fiscal-mode` (aceite); `GET /api/v1/sales/reports/non-fiscal`, `GET .../export`. A configuração desce pela sincronização.
- **Eventos do aparelho:** `ReceiptPrinted`, `ReceiptReprinted`.
- **Tabelas:** `pos.printers`, `pos.print_jobs`; `fiscal_mode` e o aceite em `pos.stores`.
- **Auditoria:** aceite do modo não fiscal (ligar, desligar, nova versão do texto).

## Telas e estados
- **Instalar impressora** (`/pdv/config/impressora`): assistente. Vazio: "Nenhuma impressora instalada neste caixa." com "Instalar impressora".
- **Diálogo de impressão** depois da venda; erro do CA 8.
- **Loja → Lojas e caixas → Documento fiscal:** escolha do modo, com o texto do aceite. **Loja → Vendas sem documento fiscal** (`/app/loja/vendas-sem-documento-fiscal`): relatório. Vazio: "Nenhuma venda sem documento fiscal no período.". Carregando e erro.
- **Ajuda:** `Ajuda/pdv-impressora.md` (nova), com a lista de modelos testados e o que fazer quando o cupom sai cortado; `Ajuda/loja-documento-fiscal.md` e `Ajuda/loja-vendas-sem-documento-fiscal.md` (novas).

## Plano de testes
- **Vitest:** modelo do recibo em 32, 42 e 48 colunas, com nomes longos e valores grandes (CA 3); "2ª VIA" (CA 4); presença obrigatória de "Não é documento fiscal".
- **Integração (Testcontainers):** só o Proprietário liga o modo (CA 10 e 11); aceite auditado e versionado (CA 13); relatório por período (CA 12).
- **Manual, com o kit de hardware:** CA 2, 3 e 6 nas impressoras do kit; resultado anotado na ajuda.
- **Integração:** configuração desce para o aparelho (CA 7).

## Fora do escopo
- ESC/POS direto, corte, gaveta e QR nativo (IMP-02).
- Ponte e impressora de rede (IMP-03).
- DANFE NFC-e (NFCE-02).

## Pontos em aberto
- A redação do texto do aceite (jurídico; QC-13 como orientação). Os insumos traziam duas redações para o comprovante; o PO fixou "Não é documento fiscal".
- A responsabilidade do fornecedor do software quando o cliente escolhe não emitir: a confirmar com o jurídico (RP-17).

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade e de integração (Testcontainers) passando; Vitest no front.
- [ ] Migration criada; permissões só do `ModuleCatalog`.
- [ ] Telas com estados de carregando, vazio e erro; ajuda escrita ou atualizada em `Ajuda/` no mesmo commit.
- [ ] Padrões e próximo passo propositivos avaliados; auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados; story marcada no backlog; decisões em ADR.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 15/11/2026 | 0.1 | Story escrita como parte do plano do PDV (proposta). Aguarda a aprovação do plano e as decisões citadas em "Pontos em aberto" para sair de `Draft`. | Claude (planejamento) |
| 15/11/2026 | 0.2 | Revista depois da revisão independente e das decisões do PO do mesmo dia ([o que mudou](../README.md#7-revisão-independente-de-15112026-achados-e-o-que-foi-aplicado)). | Claude (planejamento) |
