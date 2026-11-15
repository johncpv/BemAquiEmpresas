# IMP-01 — Cupom não fiscal em bobina de 58 e 80 mm, a porta `HardwareBridge` com a impressão pelo navegador e o assistente "Instalar impressora" com página de teste

| Campo | Valor |
|---|---|
| ID | IMP-01 |
| Título | Cupom não fiscal em bobina de 58 e 80 mm, a porta `HardwareBridge` com a impressão pelo navegador e o assistente "Instalar impressora" com página de teste |
| Tamanho | M |
| Status | Draft (plano proposto em 15/11/2026; aguarda a aprovação do PO) |
| Fase | 11 — PDV balcão com cupom não fiscal e offline (marco MVP) |
| Requisitos | Pedido do PO de 15/11/2026 (item 7: impressoras com NFC-e ou cupom não fiscal) · [Rascunho B](../adr/rascunho-B-impressao-perifericos.md) itens 1, 3, 4, 9 e 10 · ADR 0041 (WhatsApp) · ADR 0045 (PDF) |
| Dependências | PDV-03; PP-05 decidida |
| Bloqueia | IMP-02, CX-02 (comprovantes) |
| Paridade com o mercado | Requisitos mínimos 2 (cupom não fiscal) do [benchmark](../00-benchmark-mercado.md#7-matriz-de-paridade-cada-requisito-mínimo-e-a-story-que-o-cobre) |
| Ordem de execução | 6 (Fase 11) |

## User story
Como **operador de caixa**, quero **entregar um comprovante impresso ao cliente**, para **ele conferir o que comprou**, e como **dono**, quero **instalar a impressora sem chamar técnico**.

## Contexto e referências
- O MVP não emite NFC-e. O comprovante é um recibo que diz que não tem valor fiscal.
- O recibo sai de um modelo estruturado (linhas, colunas, QR, corte, gaveta). Nesta story o modelo vira HTML para a impressão do navegador; na IMP-02 o mesmo modelo vira bytes ESC/POS.

## Critérios de aceite
1. **Dado** a porta `HardwareBridge`, **quando** leio o código, **então** toda impressão do PDV passa por ela; o `BrowserPrintBridge` é o adaptador desta story.
2. **Dado** uma venda concluída, **quando** aciono "Imprimir cupom", **então** sai o recibo com: nome e CNPJ da empresa e da loja, número da venda, data e hora, operador, itens (quantidade, descrição, unitário, total), descontos, total, pagamentos por forma, troco, CPF do cliente quando informado e, em destaque, o texto de que não é documento fiscal.
3. **Dado** uma impressora de 58 mm e outra de 80 mm, **quando** imprimo, **então** o recibo respeita 32, 42 ou 48 colunas conforme o cadastro, sem cortar texto nem valor.
4. **Dado** uma venda já impressa, **quando** reimprimo, **então** o recibo sai com "2ª VIA" e a reimpressão fica registrada, com quem e quando.
5. **Dado** a loja configurada para "perguntar", **quando** concluo a venda, **então** escolho entre "Imprimir", "Não imprimir" e, com rede e contato do cliente, "Enviar por e-mail" ou "Enviar por WhatsApp".
6. **Dado** `pos.manage_devices`, **quando** abro "Instalar impressora", **então** sigo três passos: escolher a conexão; escolher a largura e o modelo, de uma lista curta; "Imprimir teste" com régua de colunas, acentos, QR Code e corte, e a pergunta "Saiu certo?".
7. **Dado** a configuração da impressora salva, **quando** outro aparelho do mesmo caixa sincroniza, **então** recebe a configuração.
8. **Dado** a impressora sem papel, desligada ou sem resposta, **quando** imprimo, **então** a venda **não** é afetada e vejo "Não foi possível imprimir." com "Tentar de novo", "Outra impressora" e "Seguir sem imprimir".
9. **Dado** a falta de rede, **quando** imprimo na impressora local, **então** funciona.
10. **Dado** a configuração da loja, **quando** o tenant tem o modo não fiscal ligado, **então** a tela de configuração mostra o aviso de responsabilidade aprovado e existe o parâmetro que desliga o modo por loja.

## Regras de negócio e técnicas
- O texto de "sem valor fiscal" é um só, definido em configuração, e não pode ser removido do modelo. O texto final é validado com a contadora (QC-13).
- O recibo é gerado no aparelho, do dado local; não depende do servidor.
- Reimpressão é evento registrado.
- Envio por e-mail e WhatsApp usa as filas existentes, com o consentimento da ADR 0041.

## Permissões, eventos e tabelas
- **Permissões:** `pos.create` (imprimir e reimprimir); `pos.manage_devices` (instalar impressora).
- **Endpoints:** `GET|POST|PUT /api/v1/pos/printers`; a configuração desce pela sincronização.
- **Eventos do aparelho:** `ReceiptPrinted`, `ReceiptReprinted`.
- **Tabelas:** `pos.printers`, `pos.print_jobs`.

## Telas e estados
- **Instalar impressora** (`/pdv/config/impressora`): assistente. Vazio: "Nenhuma impressora instalada neste caixa." com "Instalar impressora".
- **Diálogo de impressão** depois da venda; erro do CA 8.
- **Ajuda:** `Ajuda/pdv-impressora.md` (nova), com a lista de modelos testados e o que fazer quando o cupom sai cortado.

## Plano de testes
- **Vitest:** modelo do recibo em 32, 42 e 48 colunas, com nomes longos e valores grandes (CA 3); "2ª VIA" (CA 4); presença obrigatória do texto de "sem valor fiscal".
- **Manual, com o kit de hardware:** CA 2, 3 e 6 nas impressoras do kit; resultado anotado na ajuda.
- **Integração:** configuração desce para o aparelho (CA 7).

## Fora do escopo
- ESC/POS direto, corte, gaveta e QR nativo (IMP-02).
- Ponte e impressora de rede (IMP-03).
- DANFE NFC-e (NFCE-02).

## Pontos em aberto
- PP-05 e QC-13: se o cupom não fiscal pode ser oferecido a quem é obrigado a emitir, e com que texto.
- Os insumos trazem duas redações ("SEM VALOR FISCAL" e "Não é documento fiscal"); a escolha é da contadora.

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
