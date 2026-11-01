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
| 01/11/2026 | 1.0 | **Implementação.** Worker **`fiscal-pdf-reader`** (`src/Workers/BemAqui.PdfReader`, ADR 0043 nova), sem referência ao Host nem aos módulos; biblioteca **PdfPig 0.1.16, licença Apache 2.0** (a story dizia MIT: o PdfPig é Apache 2.0, também permissiva; já estava no `Directory.Packages.props` dos testes). Socket Unix com quadros de 4 bytes de tamanho, uma leitura por vez, **30 s** por leitura: estourado, o worker responde `TimedOut` e o processo sai (código 75), o Docker reinicia; a API espera 35 s. No worker: cabeçalho `%PDF-`, criptografia, anexos embutidos e **todos os objetos decodificados** (inclusive dentro de object stream e nomes com `#xx`) contra JavaScript, `Launch`, `SubmitForm`, `ImportData`, `GoToR/E`, `RichMedia`, XFA; 1 a 10 páginas; menos de 20 letras = `NoText` (sem OCR); campos pela posição dos rótulos da DANFE (`DanfeParser`: chave em blocos de 4 com DV, emissão, emitente pelo canhoto, CNPJ do emitente e do destinatário, os 11 totais do quadro "Cálculo do imposto" e os itens pelas colunas do cabeçalho, com a soma e quantidade × preço marcando "incerto"); log só com o resultado, a duração, as páginas e a chave mascarada; nada gravado em disco (o texto fica na memória da leitura). Na API, `PdfActiveContentScan` (nomes nos bytes, sem abrir o PDF), dono de anexo próprio `IncomingInvoicePdf` (só PDF, até 5 MB, anexo de sistema com o prefixo do tenant, gravado só pela importação), `SocketPdfInvoiceReader`, `PdfInvoiceReadingJob` (`TenantJob`, sem nova tentativa), `IncomingReadingSanitizer` (a resposta do worker é dado de fora: só as chaves do formulário, formato e tamanho de cada valor, DV, CNPJ do texto contra a chave, destinatário diferente do tenant fica incerto), `IncomingReadingForm` (conferência de todo campo, chave com DV/modelo 55/CNPJ, emissão no mês da chave e não futura, destinatário = tenant, itens, soma dos itens = total dos produtos), `IncomingInvoiceReading` (migration **`IncomingReadingsFis05`**, tabela `fiscal.incoming_invoice_readings` e as colunas `xml_differences`, `replaced_by_xml_at`, `replaced_by_xml_by` em `incoming_invoices`; não destrutiva), `IIncomingXmlDownloader` com `NoIncomingXmlDownloader` (sem DF-e no MVP). Endpoints `/api/v1/fiscal/incoming-invoices/pdf` (política **`fiscal-pdf-reading`**, 30 por hora por usuário), `/manual`, `/{id}/pdf-reading` (+ `/pdf`, `/manual`, `/xml-request`, `/check`), `/{id}/pdf` e `/{id}/replace-xml`, todos com `fiscal.import_incoming` (o PDF da nota com `fiscal.view`); template `incoming-invoice-xml-request`; a marca vai para os títulos a pagar pelo `IncomingInvoiceForReceipt.SourceMark` (FIS-04b). Compose de homologação com o serviço, o volume `pdf-socket` e a API montando o socket; `check-compose.mjs` reprova rede, escrita, outro volume, root, capabilities, memória ausente ou acima de 256 MB e o volume do socket em outro serviço; `api.Dockerfile` publica o worker na mesma imagem; `deploy.sh` puxa e sobe o worker. Telas: "Importar PDF da DANFE" (`/app/fiscal/entrada/importar-pdf`), leitura assistida (`/app/fiscal/entrada/leitura/$id`: lendo com a demora depois de 30 s, falhou com "Pedir o XML ao fornecedor" e "Digitar a nota", duas colunas com o PDF e os campos, aba "Ver PDF" a 400 px, situação em texto, foco no primeiro incerto, "Conferido" de 44 px, contador fixo com `aria-live`, soma dos itens), "Digitar a nota" na lista, leituras a conferir no topo da lista, marca nas notas, "Pedir o XML ao fornecedor" e "Substituir pelo XML" com as diferenças em texto na nota, PDF nos Documentos e a leitura no Histórico; ajudas `fiscal-entrada-leitura` e `fiscal-entrada-importar-pdf` (e as da lista e da nota atualizadas). **Recomendações aceitas (PO 24/10), pela opção mais conservadora:** o PDF é conferido duas vezes (bytes na API antes de aceitar, objetos no worker depois de abrir) e o que não terminou a conferência de segurança (recusado, inválido, que falhou ou estourou o tempo) **não é mostrado** na tela nem fica de anexo da nota; a leitura não guarda texto nem arquivo intermediário em disco (o `tmpfs` do worker fica só para o runtime); o rascunho da leitura tem id próprio e a nota nasce com outro id, ligada pelo `invoice_id` (o histórico da nota mostra a leitura por ele); emitente pessoa física (CPF na chave) é recusado, como na FIS-04a; a emissão tem de ser do mês da chave; só a soma dos itens contra o total dos produtos bloqueia (a fórmula do total da nota fica como "incerta" na leitura, porque FCP-ST e imposto de importação mudam a conta); duplicatas e impostos por item não são lidos do PDF (a nota segue com a condição do pedido e sem crédito de imposto por item, o custo fica com o imposto); "Substituir pelo XML" mantém os valores conferidos (o recebimento e os títulos já feitos continuam valendo) e grava as diferenças, avisando no sino quem tem `fiscal.import_incoming` **e** `finance.edit`; o pedido do XML vai ao contato de cobrança do fornecedor (senão ao primeiro com e-mail), um por leitura a cada 24 horas, e não entra no limite diário dos envios de documento (o e-mail não leva anexo); template com o nome em kebab-case (`incoming-invoice-xml-request`), como os outros; uma leitura por vez no worker. **Pendências:** o `deploy.sh` instalado na VPS precisa ser trocado uma vez pelo novo (ele não se atualiza sozinho) antes do primeiro deploy com o worker; o compose de produção é o de homologação promovido na GOL-01 (não há outro arquivo); sem o container do worker no ambiente local (`docker-compose.yml` de desenvolvimento), a leitura fica "Não conseguimos ler este PDF"; Distribuição DF-e quando o EmissorFiscal oferecer (ponto 64); E2E (Playwright) fora desta story (regra do PO de 31/10): provado por unidade, integração (Testcontainers com o worker de verdade num socket) e Vitest. | Dex (dev) |
