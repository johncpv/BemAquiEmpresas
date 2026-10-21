# FIS-05 — NF-e de entrada por PDF (DANFE): chave, leitura assistida e conferência obrigatória

| Campo | Valor |
|---|---|
| ID | FIS-05 |
| Título | Quando o fornecedor manda só o PDF da DANFE: extração da chave de acesso (texto ou código de barras) e dos dados do cabeçalho e dos itens, isolada e com tempo limite; pedido do XML ao fornecedor; sem Distribuição DF-e no EmissorFiscal, leitura assistida com conferência manual campo a campo antes de seguir pelo mesmo fluxo da FIS-04a |
| Tamanho | M |
| Status | Draft |
| Requisitos | R-FIS-6 (NF-e de entrada por PDF: extração da chave e dados; tentativa de obter o XML pela chave se o EmissorFiscal suportar Distribuição DF-e; senão leitura assistida com conferência manual obrigatória) · R-PRP-3 · R-SEC-5 |
| Dependências | FIS-04a (nota de entrada, de-para, pedido e conferência), FIS-01 (análise: sem Distribuição DF-e, lacuna L-f), FND-07 (anexos e varredura) |
| Bloqueia | — |
| Ordem de execução | 14 (Marco 5.5 "Nota de entrada") |

## User story
Como **almoxarife**, quero **subir o PDF da nota quando o fornecedor não mandou o XML e ter os dados lidos para eu só conferir**, para **não digitar a nota inteira e não deixar de registrar a compra**.

## Contexto e referências
- PRD R-FIS-6; `02-arquitetura` §6.3 ("Leitura de PDF de NF de entrada: extração de texto (PdfPig); se não houver texto, OCR; parser da chave de 44 dígitos e campos do DANFE; conferência manual obrigatória antes de dar entrada").
- Revisão pré-Fase 0 §3: "leitura de PDF e OCR isoladas, com timeout".
- FIS-01, lacuna L-f: o EmissorFiscal **não** tem Distribuição DF-e nem consulta de NF-e de terceiros; a busca do XML pela chave não está disponível.
- `06-pendencias-riscos`, risco "Leitura de NF por PDF com erro de extração → conferência manual obrigatória; XML continua sendo o caminho preferencial".
- `03-modelo-dados` → `incoming_invoices.source` (XML/PDF/DFe), `pdf_path`.

## Critérios de aceite
1. **Dado** `fiscal.create` (ou a ação decidida na FIS-04a ponto 1), **quando** envio um PDF em "Importar PDF da DANFE" (até 5 MB, 1 a 10 páginas), **então** o arquivo passa pela varredura e a extração roda num processo isolado (proposta: job na fila `fiscal`, sem rede, com 30 s de tempo limite e memória limitada), lendo o texto com PdfPig e, sem texto, o OCR (Pontos em aberto 2); PDF com JavaScript, anexos embutidos ou criptografado é recusado.
2. **Dado** o texto ou a imagem, **quando** a chave de acesso de 44 dígitos é encontrada (no texto ou no código de barras CODE-128 da DANFE), **então** o dígito verificador é conferido (módulo 11), e da chave saem UF, ano e mês, CNPJ do emitente, modelo, série e número; chave com dígito errado é descartada com aviso.
3. **Dado** a chave válida, **quando** o CNPJ do emitente da chave é conferido, **então** o fornecedor é localizado como na FIS-04a; chave já importada (XML ou PDF) é recusada com 409 e o link (mesma regra da FIS-04a CA 3).
4. **Dado** a chave, **quando** a Distribuição DF-e não está disponível (situação atual, lacuna L-f), **então** a tela mostra "Não conseguimos buscar o XML desta nota automaticamente." com a ação "Pedir o XML ao fornecedor" (e-mail pela fila ao contato do fornecedor, com o número e a chave, R-PRP-3), e segue para a leitura assistida; se um dia o `IFiscalEmitter` oferecer `DownloadByKeyAsync` (DF-e), o sistema busca o XML e segue pela FIS-04a sem leitura assistida (o contrato já prevê `BaixarPorChave`, `02-arquitetura` §6.1).
5. **Dado** a leitura assistida, **quando** abre, **então** mostra lado a lado a página do PDF e os campos lidos (emissão, destinatário, totais, itens com código, descrição, NCM, quantidade, unidade, valor unitário e total), cada um com a marca de confiança (lido, incerto, não encontrado); **todo** campo precisa ser conferido (marcado ou corrigido) antes de "Conferir nota" ficar ativo; o CNPJ do destinatário lido tem de ser o do tenant.
6. **Dado** a conferência completa, **quando** salvo, **então** a nota nasce com `source = Pdf`, o PDF como anexo de sistema e a marca "Lida do PDF, sem XML" visível em todas as telas e no título a pagar; a partir daí segue a FIS-04a (de-para, pedido, divergências) e a FIS-04b (recebimento); soma dos itens diferente do total lido bloqueia com "A soma dos itens (R$ X) não bate com o total da nota (R$ Y)."
7. **Dado** uma nota lida do PDF, **quando** o XML chega depois, **então** "Substituir pelo XML" importa o XML pela FIS-04a, confere a mesma chave, compara os valores com os conferidos (diferenças destacadas) e troca a origem para `Xml`, mantendo o recebimento já feito (se os valores diferirem, avisa quem tem `fiscal.edit` e `finance.edit`).
8. **Dado** o módulo `fiscal` não liberado, **quando** chamo, **então** 403 `module_not_released`; importar e conferir sem a permissão: 403; `RequireModule` e `perm:`; IDOR em `/incoming-invoices/{id}/pdf-reading`.
9. **[Segurança]** **Dado** a extração, **quando** roda, **então** o log traz só o resultado, a duração e a chave mascarada, nunca o texto extraído; o texto e as imagens intermediárias ficam em `tmpfs` e são apagados no fim (sucesso, falha ou tempo limite).
10. **Dado** qualquer importação, conferência ou substituição, **quando** abro o histórico, **então** vejo o que foi lido, o que o usuário corrigiu e quem conferiu.

## Regras de negócio e técnicas
- **Extração** em job isolado (`PdfInvoiceReadingJob`, `TenantJob`), sem acesso de rede e com limites; o resultado fica num rascunho de leitura (`incoming_invoice_readings`, proposta) até a conferência.
- **Chave**: 44 dígitos com DV módulo 11; nunca confiar no CNPJ do texto se ele contradiz a chave.
- **XML continua preferencial**: a nota lida do PDF é marcada e pode ser substituída.
- Bibliotecas sem licença comercial (PdfPig MIT; OCR com Tesseract, Pontos em aberto 2).

## Permissões, eventos e tabelas
- **Permissões:** as da FIS-04a.
- **Eventos:** nenhum entre módulos.
- **Tabelas:** `fiscal.incoming_invoices.source = Pdf`; `fiscal.incoming_invoice_readings` (proposta: campos lidos com confiança, corrigido_por, conferido_em). Template de e-mail `incoming_invoice_xml_request`.

## Telas e estados
- **Notas de entrada → "Importar PDF da DANFE"** e a tela de **leitura assistida** (`/app/fiscal/entrada/leitura/$id`), em duas colunas (PDF e campos).
- **Verbos:** "Importar PDF" / "PDF enviado para leitura"; "Pedir o XML ao fornecedor" / "Pedido enviado"; "Conferir nota" / "Nota conferida"; "Substituir pelo XML" / "Nota substituída pelo XML".
- **Estados:** lendo (com o estado de demora depois de 30 s); leitura falhou ("Não conseguimos ler este PDF. Peça o XML ou digite a nota." + "Digitar a nota"); campos incertos em âmbar; tudo conferido.
- **Propositivo:** o pedido do XML ao fornecedor é o primeiro passo sugerido; campos preenchidos pela leitura.
- **400 px:** o PDF vira uma aba "Ver PDF" acima dos campos; campos em cartões com "Conferido" de 44 px.
- **Ajuda:** `Ajuda/fiscal-entrada-leitura.md`; `<Dica>` em "Lida do PDF, sem XML" e "Confiança da leitura".

## Plano de testes
- **Unidade:** chave de 44 dígitos e DV (CA 2); parser de campos da DANFE com PDFs de exemplo fictícios; soma dos itens (CA 6).
- **Integração (Testcontainers):** PDF com texto e escaneado, tempo limite e PDF malicioso recusado (CA 1); chave duplicada (CA 3); pedido do XML (CA 4); conferência obrigatória campo a campo (CA 5); nota `Pdf` seguindo a FIS-04a e a FIS-04b (CA 6); substituição pelo XML com diferença (CA 7); três camadas e IDOR (CA 8); log e temporários apagados (CA 9); auditoria (CA 10).
- **E2E (Playwright):** importar um PDF de exemplo, conferir os campos e seguir para o de-para; tela a 400 px.

## Fora do escopo
- Distribuição DF-e e manifestação do destinatário: lacuna L-f do EmissorFiscal; entram quando o `IFiscalEmitter` oferecer, sem mudar esta tela.
- Leitura de outros documentos (NFS-e tomada, CT-e).

## Pontos em aberto
1. **Distribuição DF-e** (PO): pedir a extensão do EmissorFiscal antes do go-live (resolveria também a captura automática das notas de entrada) ou deixar para depois do MVP (proposta).
2. **OCR** (arquiteto): Tesseract na imagem da API aumenta o tamanho e a superfície; alternativa: aceitar só PDF com texto no MVP e pedir o XML nos escaneados.
3. **Digitar a nota sem PDF legível** (PO): permitir a digitação completa com a mesma conferência (proposta) ou exigir o XML.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers) e E2E passando.
- [ ] Migration criada (não destrutiva), testada sobre cópia do banco do tenant `konebras`.
- [ ] Permissões só do `ModuleCatalog`; template de e-mail registrado.
- [ ] Telas com estados, inclusive a 400 px.
- [ ] Ajuda escrita; teste de cobertura passando.
- [ ] Conferência manual obrigatória antes de qualquer efeito (R-PRP-7).
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/03-modelo-dados.md` atualizado; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] `PdfInvoiceReadingJob` isolado (PdfPig, OCR conforme o ponto 2), `AccessKey` com DV, parser da DANFE.
- [ ] `IncomingInvoiceReading`; tela de leitura assistida; pedido do XML; substituição pelo XML.

## Quality gates
- Agentes previstos: @dev, cybersecurity (PDF malicioso, isolamento), @qa.
- Gate: CA 1, 2, 5 e 7 obrigatórios.

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

- **Correções feitas na validação:** a ausência de DF-e no EmissorFiscal (lacuna L-f) virou o caminho padrão, com o contrato pronto para quando existir (CA 4); conferência de todo campo (CA 5); substituição pelo XML (CA 7).
- **Pré-condições para `Ready`:** ponto em aberto 2.
- **Prontidão:** 10/10. **Veredito:** GO condicional. Status `Draft`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
