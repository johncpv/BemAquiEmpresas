# ADR 0043: Leitura do PDF da DANFE num worker isolado

- Status: Aceito (01/11/2026), na implementação da FIS-05. Aplica a decisão D12 da revisão pré-Fases 4 a 8 (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`, §4.3: "parser de PDF isolado; OCR fora do MVP") e os pontos 64 a 66 decididos pelo PO em 21/10/2026.
- Contexto: o PDF da DANFE vem do fornecedor e é superfície de ataque (JavaScript, anexos embutidos, formulários com ação, bombas de descompressão, falhas do parser). A API guarda segredos (certificados, chaves do Emissor e do Inter) e fala com o banco de todos os tenants: abrir o PDF no processo dela põe tudo isso ao alcance de uma falha do parser.
- Complementa as ADRs 0009 (anexos e varredura), 0015 (deploy na VPS compartilhada) e 0027 (EmissorFiscal sem Distribuição DF-e).

## Decisão

### 1. Onde o PDF é aberto
- **Só no worker `fiscal-pdf-reader`** (projeto `src/Workers/BemAqui.PdfReader`, sem referência ao Host nem aos módulos; só o .NET e o **PdfPig 0.1.16, licença Apache 2.0**). A API nunca abre o PDF: confere tamanho (5 MB), tipo pelos bytes, antivírus (falha fechada) e uma varredura de **nomes** nos bytes (`/JavaScript`, `/JS`, `/Launch`, `/SubmitForm`, `/ImportData`, `/EmbeddedFile(s)`, `/RichMedia`, `/XFA`, `/GoToR`, `/GoToE`, `/Encrypt`, inclusive escritos com `#xx`), sem interpretar o arquivo.
- O worker, depois de abrir, confere de novo **todos os objetos** já decodificados (inclusive os de dentro de object streams), os anexos embutidos e a criptografia; qualquer conteúdo ativo recusa o PDF inteiro ("Este PDF não pode ser lido com segurança. Peça o XML ao fornecedor."). Depois: de 1 a 10 páginas; menos de 20 letras = sem texto (**sem OCR**, ponto 65); e os campos pela posição dos rótulos da DANFE.
- Nada do PDF é executado nem gravado em disco: o texto só existe na memória da leitura e some com ela (sucesso, falha ou tempo limite). A resposta leva só os campos (chave, emissão, emitente, destinatário, totais e itens), nunca o texto.

### 2. Isolamento do container
- Serviço `fiscal-pdf-reader` no compose de homologação (que vira produção na GOL-01): a **mesma imagem da API** (o publish do worker vai para `/app/pdf-reader`), com outro `entrypoint`.
- `network_mode: none`; `read_only: true` com `tmpfs` só para o runtime (`/tmp`, 16 MB, `noexec`); usuário `1655:1654` (não-root, grupo da API); `cap_drop: [ALL]`; `no-new-privileges`; **256 MB** de memória (e o heap do .NET limitado a 150 MB), meia CPU e 64 processos.
- Fala com a API por **socket Unix** no volume `pdf-socket` (`/run/pdf-reader/reader.sock`, modo 0660), montado só no worker e na API. Protocolo: 4 bytes de tamanho e o conteúdo, uma leitura por conexão, uma leitura por vez.
- **Tempo limite de 30 s** por leitura: estourado, o worker responde `TimedOut` (se ainda der) e o processo sai com o código 75; o `restart: unless-stopped` sobe outro. A API espera 35 s; sem resposta, a leitura fica "falhou por tempo".
- O `scripts/check-compose.mjs` reprova o compose se o worker tiver rede, não for só leitura, tiver outro volume ou bind além do socket, rodar como root, tiver capabilities, ou não tiver limite de memória de até 256 MB; e se o volume do socket for montado em outro serviço além da API.

### 3. O que a leitura vale
- É **sugestão**. O resultado fica num rascunho (`fiscal.incoming_invoice_readings`), com a situação de cada campo em texto ("Lido", "Leitura incerta: confira", "Não encontrado"). A API confere a resposta do worker como dado de fora (só as chaves do formulário, formatos e tamanhos). A chave com DV errado é descartada; o CNPJ do texto que contradiz a chave é ignorado.
- **Todo campo é conferido** pelo usuário antes de a nota nascer; a nota nasce com `source = Pdf` (ou `Manual`, "Digitar a nota"), sem assinatura nem protocolo, marcada "Lida do PDF, sem XML" / "Digitada, sem XML" nas telas e nos títulos a pagar, e segue pela FIS-04a e pela FIS-04b. O XML continua preferencial: "Substituir pelo XML" faz toda a validação da FIS-04a e troca a origem.

## Consequências
- Mais um container na VPS (256 MB). O deploy (`deploy/homolog/deploy.sh`) passa a puxar e subir o worker junto com a API; o `deploy.sh` instalado na VPS precisa ser atualizado uma vez (ele não se atualiza sozinho).
- Sem o worker (desenvolvimento local sem o container), a leitura fica "Não conseguimos ler este PDF" e a tela oferece "Pedir o XML" e "Digitar a nota".
- Uma leitura por vez: um PDF lento segura os outros até o tempo limite. Volume esperado (dezenas por dia) não justifica paralelismo; se justificar, sobe-se outro worker com o próprio socket.
- Distribuição DF-e (XML pela chave) fica pronta no contrato `IIncomingXmlDownloader` (hoje `NoIncomingXmlDownloader`): quando o EmissorFiscal oferecer, troca-se o registro.
