# FIS-05 — NF-e de entrada por PDF (DANFE): chave, leitura assistida em worker isolado, "Digitar a nota" e conferência obrigatória

| Campo | Valor |
|---|---|
| ID | FIS-05 |
| Título | Quando o fornecedor manda só o PDF da DANFE: extração da chave de acesso e dos dados do cabeçalho e dos itens, só de PDF com texto (sem OCR), num worker isolado (`network_mode: none`, memória limitada, sistema de arquivos só leitura, morto no tempo limite); pedido do XML ao fornecedor; leitura assistida com campos incertos marcados com texto e foco no primeiro; "Digitar a nota" quando a leitura não serve; conferência manual campo a campo antes de seguir pelo mesmo fluxo da FIS-04a |
| Tamanho | M |
| Status | Ready |
| Requisitos | R-FIS-6 (NF-e de entrada por PDF: extração da chave e dados; XML pela chave se o EmissorFiscal suportar Distribuição DF-e; senão leitura assistida com conferência manual obrigatória) · R-PRP-3 · R-SEC-5 |
| Dependências | FIS-04a (nota de entrada, de-para, pedido, conferência, `fiscal.import_incoming`), FIS-04b (recebimento pela nota), FIS-01 (sem Distribuição DF-e), FND-07 (anexos e varredura) |
| Bloqueia | — |
| Ordem de execução | 14 (Marco 5.5 "Nota de entrada") |

## User story
Como **almoxarife**, quero **subir o PDF da nota quando o fornecedor não mandou o XML e ter os dados lidos para eu só conferir, ou digitar a nota quando a leitura não der**, para **não digitar tudo à toa e não deixar de registrar a compra**.

## Contexto e referências
- PRD R-FIS-6; `02-arquitetura` §6.3 ("Leitura de PDF de NF de entrada: extração de texto (PdfPig); se não houver texto, OCR; parser da chave de 44 dígitos e campos do DANFE; conferência manual obrigatória antes de dar entrada"): **o OCR sai do MVP** (ponto 65; o `02` é corrigido).
- **Revisão pré-Fases 4 a 8** ([2026-10-21-revisao-pre-fases4-8.md](../revisoes/2026-10-21-revisao-pre-fases4-8.md)): D12 ("parser de PDF isolado; OCR fora do MVP"); tabela 3.2, pontos 64 a 66; §4.3 (worker isolado: `network_mode: none`, memória limitada, sistema de arquivos só leitura, kill no tempo limite); §4.4 achado "campos incertos da FIS-05 com texto e foco no primeiro".
- Revisão pré-Fase 0 §3: "leitura de PDF e OCR isoladas, com timeout".
- **ADR 0027** ([0027-integracao-emissorfiscal.md](../adr/0027-integracao-emissorfiscal.md)): o Emissor não tem Distribuição DF-e nem consulta de NF-e de terceiros.
- `06-pendencias-riscos`, risco "Leitura de NF por PDF com erro de extração → conferência manual obrigatória; XML continua sendo o caminho preferencial".
- `03-modelo-dados` → `incoming_invoices.source` (XML/PDF/DFe), `pdf_path`.

## Critérios de aceite
**Enviar e ler com isolamento**
1. **Dado** `fiscal.import_incoming`, **quando** envio um PDF em "Importar PDF da DANFE" (até 5 MB, 1 a 10 páginas), **então** o arquivo passa pela varredura e é recusado se tiver JavaScript, anexos embutidos, formulário com ação ou criptografia ("Este PDF não pode ser lido com segurança. Peça o XML ao fornecedor."); aceito, vai para a leitura no worker isolado (CA 2) e a tela mostra "Lendo o PDF…" com o estado de demora depois de 30 s; política de rate limit nomeada `fiscal-pdf-reading` (30 por hora por usuário).
2. **[Segurança]** **Dado** a leitura, **quando** roda, **então** acontece no worker **`fiscal-pdf-reader`**, container próprio com `network_mode: none`, memória limitada (256 MB), `read_only: true` com `tmpfs` só para o trabalho, usuário sem root e sem capabilities; a API entrega o PDF e recebe o resultado por um socket Unix num volume compartilhado só entre os dois; cada leitura tem **tempo limite de 30 s** e, estourado, o processo é **morto** (o container reinicia) e a leitura fica "falhou por tempo"; o `check-compose.mjs` falha se o worker tiver rede, escrita no sistema de arquivos ou não tiver limite de memória.
3. **Dado** um PDF **sem texto** (escaneado, só imagem), **quando** é lido, **então** **não há OCR** (ponto 65): a tela mostra "Este PDF não tem texto para ler. Peça o XML ao fornecedor ou digite a nota." com "Pedir o XML ao fornecedor" e "Digitar a nota" (CA 9).

**Chave e fornecedor**
4. **Dado** o texto, **quando** a chave de acesso de 44 dígitos é encontrada, **então** o dígito verificador é conferido (módulo 11), e da chave saem UF, ano e mês, CNPJ do emitente, modelo, série e número; chave com dígito errado é descartada com aviso; o CNPJ do texto que contradiz a chave é ignorado.
5. **Dado** a chave válida, **quando** o CNPJ do emitente é conferido, **então** o fornecedor é localizado como na FIS-04a; chave já importada (XML, PDF ou digitada) é recusada com 409 e o link (FIS-04a CA 4).
6. **Dado** a chave, **quando** a Distribuição DF-e não está disponível (situação no MVP, ponto 64), **então** a tela mostra "Não conseguimos buscar o XML desta nota automaticamente." com "Pedir o XML ao fornecedor" (e-mail `incoming_invoice_xml_request` pela fila ao contato do fornecedor, com o número e a chave, R-PRP-3) e segue para a leitura assistida; quando um dia o `IFiscalEmitter` oferecer `DownloadByKeyAsync`, o sistema busca o XML e segue pela FIS-04a sem leitura assistida.

**Leitura assistida e conferência**
7. **Dado** a leitura assistida, **quando** abre, **então** mostra lado a lado a página do PDF e os campos lidos (emissão, destinatário, totais, itens com código, descrição, NCM, quantidade, unidade, valor unitário e total), cada um com a situação **em texto** ("Lido", "Leitura incerta: confira", "Não encontrado") além da cor, e o **foco vai para o primeiro campo incerto ou não encontrado**; o contador "{n} campos a conferir" fica fixo no rodapé (`aria-live`); **todo** campo precisa ser conferido (marcado ou corrigido) antes de "Conferir nota" ficar ativo; o CNPJ do destinatário tem de ser o do tenant.
8. **Dado** a conferência completa, **quando** salvo, **então** a nota nasce com `source = Pdf`, o PDF como anexo de sistema e a marca "Lida do PDF, sem XML" visível em todas as telas e nos títulos a pagar; a partir daí segue a FIS-04a (de-para, pedido, divergências) e a FIS-04b (recebimento); soma dos itens diferente do total lido bloqueia com "A soma dos itens (R$ X) não bate com o total da nota (R$ Y)."
9. **Dado** a leitura que falhou, o PDF sem texto ou a nota sem PDF legível, **quando** clico **"Digitar a nota"** (ponto 66), **então** abre o mesmo formulário da leitura assistida vazio, com a chave obrigatória (44 dígitos com DV, CA 4), e a mesma conferência e soma do CA 8; a nota nasce com `source = Manual` e a marca "Digitada, sem XML".
10. **Dado** uma nota lida do PDF ou digitada, **quando** o XML chega depois, **então** "Substituir pelo XML" importa o XML pela FIS-04a (com toda a validação da assinatura), confere a mesma chave, compara os valores com os conferidos (diferenças destacadas em texto) e troca a origem para `Xml`, mantendo o recebimento já feito; se os valores diferirem, avisa quem tem `fiscal.import_incoming` e `finance.edit`.

**Permissões, privacidade e auditoria**
11. **Dado** o módulo `fiscal` não liberado, **quando** chamo, **então** 403 `module_not_released`; sem grupo, 403 `forbidden`; importar, digitar, conferir e substituir sem `fiscal.import_incoming`: 403 e os botões somem; `RequireModule` e `perm:`; IDOR em `/incoming-invoices/{id}/pdf-reading`; o PDF só por endpoint autorizado (anexo por registro, prefixo do tenant no storage).
12. **[Segurança]** **Dado** a extração, **quando** roda, **então** o log traz só o resultado, a duração e a chave mascarada, nunca o texto extraído; o texto e os arquivos intermediários ficam no `tmpfs` do worker e são apagados no fim (sucesso, falha ou tempo limite).
13. **Dado** qualquer importação, digitação, conferência ou substituição, **quando** abro o histórico, **então** vejo o que foi lido, o que o usuário corrigiu e quem conferiu.

## Regras de negócio e técnicas
- **Extração** no worker `fiscal-pdf-reader` (PdfPig, MIT), chamado pelo job `PdfInvoiceReadingJob` (`TenantJob` com `tenantId`); o resultado fica num rascunho de leitura (`incoming_invoice_readings`) até a conferência.
- **Sem OCR no MVP:** PDF sem texto vai para "Pedir o XML" ou "Digitar a nota".
- **Chave:** 44 dígitos com DV módulo 11; nunca confiar no CNPJ do texto se ele contradiz a chave.
- **XML continua preferencial:** a nota lida do PDF ou digitada é marcada e pode ser substituída.

## Permissões, eventos e tabelas
- **Permissões:** `fiscal.import_incoming` (importar, digitar, conferir, substituir), `fiscal.view` (ver).
- **Eventos:** nenhum entre módulos.
- **Tabelas:** `fiscal.incoming_invoices.source` (`Xml`/`Pdf`/`Manual`); `fiscal.incoming_invoice_readings` (nova: campos lidos com a situação, `corrected_by`, `checked_at`, `duration_ms`, `result`). Template de e-mail `incoming_invoice_xml_request`.
- **Infra:** serviço `fiscal-pdf-reader` no compose de homologação e de produção, conferido pelo `check-compose.mjs`.

## Telas e estados
- **Padrões comuns das Fases 4 a 8:** achado pontual "campos incertos da FIS-05 com texto e foco no primeiro"; botões de `EmptyState`/`ErrorState` com 44 px a 400 px.
- **Notas de entrada → "Importar PDF da DANFE"** e **"Digitar a nota"**; tela de **leitura assistida** (`/app/fiscal/entrada/leitura/$id`), em duas colunas (PDF e campos).
- **Verbos:** "Importar PDF" / "PDF enviado para leitura"; "Pedir o XML ao fornecedor" / "Pedido enviado"; "Digitar a nota" / —; "Conferir nota" / "Nota conferida"; "Substituir pelo XML" / "Nota substituída pelo XML".
- **Estados:** lendo (com o estado de demora depois de 30 s); leitura falhou ou falhou por tempo ("Não conseguimos ler este PDF. Peça o XML ou digite a nota." + "Pedir o XML ao fornecedor" e "Digitar a nota"); PDF sem texto (CA 3); campos incertos com texto e foco no primeiro; tudo conferido.
- **Propositivo:** o pedido do XML ao fornecedor é o primeiro passo sugerido; campos preenchidos pela leitura; foco no primeiro campo a conferir; depois de conferir, o `NextSteps` oferece "Receber pela nota" (FIS-04b).
- **400 px:** o PDF vira uma aba "Ver PDF" acima dos campos; campos em cartões com "Conferido" de 44 px.
- **Ajuda:** `Ajuda/fiscal-entrada-leitura.md` (prevista); `<Dica>` em "Lida do PDF, sem XML", "Digitada, sem XML" e "Leitura incerta".

## Plano de testes
- **Unidade:** chave de 44 dígitos e DV (CA 4); parser de campos da DANFE com PDFs de exemplo fictícios; soma dos itens (CA 8); foco no primeiro campo incerto (Vitest, CA 7).
- **Integração (Testcontainers):** PDF com texto, PDF sem texto sem OCR, tempo limite com o processo morto e PDF malicioso recusado (CA 1 a 3); chave duplicada (CA 5); pedido do XML (CA 6); conferência obrigatória campo a campo (CA 7); nota `Pdf` seguindo a FIS-04a e a FIS-04b (CA 8); "Digitar a nota" com a chave obrigatória (CA 9); substituição pelo XML com diferença (CA 10); três camadas, IDOR e anexo por registro (CA 11); log e temporários apagados (CA 12); auditoria (CA 13).
- **Infra:** `check-compose.mjs` com o worker sem rede, só leitura e com limite de memória (CA 2).
- **E2E (Playwright):** importar um PDF de exemplo, ver o foco no primeiro campo incerto, conferir os campos e seguir para o de-para; digitar uma nota; tela a 400 px.

## Fora do escopo
- Distribuição DF-e e manifestação do destinatário: depois do MVP (ponto 64); entram quando o `IFiscalEmitter` oferecer, sem mudar esta tela.
- OCR de PDF escaneado: fora do MVP (ponto 65). Leitura de outros documentos (NFS-e tomada, CT-e).

## Pontos em aberto
Decididos pelo PO em 21/10/2026 (revisão pré-Fases 4 a 8, tabela 3.2, pontos 64–66):
1. Distribuição DF-e depois do MVP (CA 6).
2. Sem OCR: só PDF com texto (CA 3).
3. Oferecer "Digitar a nota" com a mesma conferência (CA 9).

Decisões da revisão aplicadas: D12 (parser isolado, OCR fora); §4.3 (worker com `network_mode: none`, memória limitada, sistema de arquivos só leitura e morto no tempo limite, CA 2; rate limit nomeado, CA 1; anexo por registro, CA 11); §4.4 (campos incertos com texto e foco no primeiro, CA 7); `fiscal.import_incoming` (FIS-04a); antes do go-live, na ordem da fase.

**Pré-condição externa:** nenhuma.

## Definition of Done
- [ ] Critérios de aceite atendidos.
- [ ] Testes de unidade, integração (Testcontainers) e E2E passando; `check-compose.mjs` com o worker isolado.
- [ ] Migration criada (não destrutiva), testada sobre cópia do banco do tenant `konebras`.
- [ ] Permissões só do `ModuleCatalog`; template de e-mail registrado.
- [ ] Telas com estados, inclusive a 400 px.
- [ ] Ajuda escrita; teste de cobertura passando.
- [ ] Conferência manual obrigatória antes de qualquer efeito (R-PRP-7).
- [ ] Auditoria ligada.
- [ ] OpenAPI e cliente Orval regenerados.
- [ ] `docs/02-arquitetura.md` §6.3 (sem OCR, worker isolado) e `docs/03-modelo-dados.md` atualizados; story marcada no backlog.

## Tarefas técnicas sugeridas
- [ ] Worker `fiscal-pdf-reader` (PdfPig) com o socket Unix e o tempo limite; `PdfInvoiceReadingJob`; `AccessKey` com DV; parser da DANFE.
- [ ] `IncomingInvoiceReading`; tela de leitura assistida com foco no primeiro campo incerto; "Digitar a nota"; pedido do XML; substituição pelo XML.
- [ ] Compose e `check-compose.mjs`.

## Quality gates
- Agentes previstos: @dev, @devops (worker isolado no compose), cybersecurity (PDF malicioso, isolamento), @qa.
- Gate: CA 1, 2, 3, 4, 7, 9 e 10 obrigatórios.

## Validação do PO
Checklist do @po (Pax), 21/10/2026 (revalidação depois das decisões do PO e da revisão pré-Fases 4 a 8).

| # | Item | Resultado |
|---|---|---|
| 1 | Template completo | OK |
| 2 | User story no formato "Como / quero / para" | OK (ajustado) |
| 3 | `Requisitos:` com IDs `R-` do PRD e rastreáveis | OK |
| 4 | CAs numerados, testáveis, em Dado/Quando/Então | OK |
| 5 | CAs de segurança e de permissão (3 camadas) | OK |
| 6 | Dependências e sequência coerentes com a ordem de execução | OK (ajustado: depois da FIS-04b) |
| 7 | Plano de testes cobre os CAs | OK |
| 8 | Fontes citadas; nada inventado contra os docs (propostas marcadas) | OK |
| 9 | Telas com estados, ajuda e comportamento propositivo | OK |
| 10 | DoD ajustado; pontos em aberto decididos e registrados | OK (ajustado) |

- **Correções feitas na validação (21/10/2026, primeira validação):** a ausência de DF-e no EmissorFiscal virou o caminho padrão, com o contrato pronto para quando existir; conferência de todo campo; substituição pelo XML.
- **Correções da revalidação (21/10/2026):** pontos 64 a 66 decididos; OCR fora (CA 3); worker isolado com `network_mode: none`, memória limitada, sistema de arquivos só leitura e morto no tempo limite, conferido no compose (§4.3; CA 2); "Digitar a nota" com `source = Manual` (CA 9); campos incertos com texto e foco no primeiro (§4.4; CA 7); `fiscal.import_incoming` no lugar de `fiscal.create` (CA 1, 11); rate limit e anexo por registro; antes do go-live, na ordem da fase; CAs renumerados.
- **Pré-condições para `Ready`:** nenhuma de decisão de story.
- **Prontidão:** 10/10. **Veredito:** GO. Status `Ready`.

## Change Log
| Data | Versão | Descrição | Autor |
|---|---|---|---|
| 21/10/2026 | 0.1 | Story detalhada | River (SM) |
| 21/10/2026 | 0.2 | Validação do PO (checklist) e correções | Pax (PO) |
| 21/10/2026 | 0.3 | Decisões do PO e correções da revisão pré-Fases 4 a 8 (D12, tabela 3.2 pontos 64–66, §4.3, §4.4); sem OCR, worker isolado, "Digitar a nota"; antes do go-live, na ordem da fase (14, Marco 5.5); CAs renumerados | River (SM) |
| 21/10/2026 | 0.4 | Revalidação do PO: 10/10, `Ready` | Pax (PO) |
