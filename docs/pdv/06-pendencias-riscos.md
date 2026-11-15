# 06 — Pendências, perguntas para a contadora e riscos do plano do PDV

Regra do repositório: item pendente não é implementado até ter decisão registrada (data + quem decidiu).

Os ids `PP-nn` são deste plano. Na aprovação, cada decisão entra no `docs/06-pendencias-riscos.md` como P-31 em diante. A coluna "Decisão" fica vazia até o PO decidir.

## 1. Decisões que destravam o início

Sete decisões seguram as Fases 10 e 11. Sem elas, nenhuma story de produto sai de `Draft`.

| # | Decisão | Recomendação do plano | Bloqueia |
|---|---|---|---|
| [PP-01](#pp-01--venda-sem-internet-em-loja-que-emite-nfc-e) | Venda sem internet em loja que emite NFC-e | **Sem recomendação única: as análises discordam.** PO + contadora | NFCE-04; a forma do agente (IMP-03); o discurso comercial do offline |
| [PP-02](#pp-02--venda-com-saldo-insuficiente) | Venda com saldo de estoque insuficiente | **Sem recomendação única: as análises discordam.** PO | PDV-05 |
| PP-03 | Módulo fora do perfil: oculto ou com cadeado | Oculto | PERF-01 |
| PP-07 | Hospedagem no Brasil, VPS dedicada e backup contínuo | Migrar antes da versão pública | INFRA-01; o MVP ir a cliente |
| PP-11 | PIN de supervisor no balcão no lugar do TOTP | Aceitar, como exceção nomeada | PDV-04, OFF-03 |
| PP-12 | Operador de caixa sem e-mail | Criar o "usuário de operação" | PDV-01 |
| PP-13 | NFC-e no roteiro do EmissorFiscal e a dívida E14 | Começar já | NFCE-00 e toda a Fase 13 |

Em seguida, por prazo longo: PP-06 (UF do primeiro cliente), PP-19 (kit de hardware), PP-18 (adquirentes e TEF) e PP-35 (lojista piloto).

## 2. Pendências (decisão do PO)

### PP-01 — Venda sem internet em loja que emite NFC-e

**O problema.** O certificado A1 fica só no cofre do EmissorFiscal (ADR 0027 §3). Com a loja sem internet, ninguém na loja consegue assinar. E a contingência offline da NFC-e exige assinatura no momento da venda: o QR Code do DANFE de contingência deriva do XML assinado [F]. Sem certificado ao alcance do caixa não existe NFC-e em contingência; existe só um recibo.

Este cenário é diferente de "internet ok, SEFAZ fora do ar". Nesse outro caso as duas análises concordam: o Emissor emite em contingência e devolve o XML assinado e o QR.

**As duas análises recomendam coisas opostas.** O plano não escolhe.

| | **Posição da arquitetura do PDV** ([rascunho E](adr/rascunho-E-acoplamento-fiscal.md)) | **Posição da análise do Emissor** ([07, seção 2.6](07-emissor-fiscal.md#26-quem-assina-offline-opções-e-recomendação)) |
|---|---|---|
| Recomendação | **4G de reserva** como orientação padrão de implantação **+ recibo não fiscal agora e NFC-e emitida na volta da rede**, como política por loja, com janela máxima e relatório "NFC-e emitidas depois da venda" | **Agente de assinatura na loja com um A1 dedicado**; o Emissor central vira receptor, transmissor e guardião |
| Como funciona | O caixa imprime um comprovante não fiscal e enfileira. O Emissor emite como nota normal quando a rede volta | Um serviço local (por loja, ou por caixa em loja pequena) guarda um certificado, monta, assina e numera a NFC-e `tpEmis=9`, imprime o DANFE de contingência e envia o XML ao Emissor |
| A favor | Simples. Nada novo no Emissor. O A1 não sai do cofre, como o PO pediu. O 4G reduz o cenário ao mínimo | É a contingência prevista na norma. Funciona sem internet. O consumidor sai com documento fiscal |
| Contra | **Não é contingência fiscal.** O consumidor sai sem documento fiscal. A nota emitida depois leva a data da reconexão, não a da venda, ou é rejeitada por atraso [F]. Na prática é venda sem documento no ato | Há certificado fora do cofre. É software a mais para distribuir, assinar e atualizar. Pede as lacunas 13 a 16 e 21 do Emissor (recepção de nota já assinada, série de contingência por caixa, prazo, rejeição tardia) |
| Ressalva legal | Entregar mercadoria sem documento fiscal **pode ser infração**; o risco de autuação é do cliente. Só com parecer escrito da contadora, por UF | Depende de a UF aceitar `tpEmis=9` e de qual o prazo de transmissão [F: a confirmar por UF; SP inclusive]. Nota offline rejeitada depois da venda tem procedimento fiscal a definir (QC-12) |
| Sobre o requisito "o PDV não guarda o A1" | Atendido ao pé da letra | Atendido "no sentido que importa", segundo a análise: o A1 do cofre não sai dele; o que vai para a loja é **outro** certificado, de alcance e revogação controlados. **Isso precisa do aceite explícito do PO** |
| Mitigações propostas | Janela máxima; relatório; ligar só depois de a contadora aprovar por UF | A1 adicional só para a loja, revogável sem afetar o central [F: emitir mais de um A1 por CNPJ é prática comum]; importação não exportável; série de contingência exclusiva |
| Efeito no mercado | Fica **igual** à Saipos, que admite não emitir nota nesse modo ([benchmark](00-benchmark-mercado.md), seção 5 item 2) | É o diferencial "emitir offline" que o benchmark aponta como espaço para superar |
| Se for reprovada | A escolha fica entre bloquear a venda e reabrir o assinador local | Sobra o recibo como modo degradado, desligado por padrão, habilitado por cliente com aceite formal |

Outras opções, que nenhuma das análises recomenda como desenho principal:

| Opção | Por que não |
|---|---|
| Bloquear a venda quando o documento é obrigatório | Seguro juridicamente; contraria o offline-first que o PO pediu |
| EPEC (`tpEmis=4`) | Precisa de internet; disponível em poucas UFs [F] |
| Navegador assinar | Navegador puro não assina offline |

**O que o PO decide:**
1. Aceita um certificado A1 dedicado na loja (posição do Emissor)? Se sim, por loja ou por caixa?
2. Se não, aceita por escrito o risco fiscal do "cupom agora, NFC-e depois", depois do parecer da contadora?
3. O 4G de reserva vira item obrigatório da implantação em loja fiscal? (Vale nas duas posições.)

**O que a contadora responde antes:** QC-01, QC-12 e QC-13.

**O que não depende desta decisão:** o MVP (balcão com cupom não fiscal) e a NFC-e online (NFCE-00 a NFCE-03).

| Decisão |
|---|
| _(em aberto)_ |

### PP-02 — Venda com saldo insuficiente

**O problema.** O motor de estoque proíbe saldo negativo (`CHECK quantity_on_hand >= 0`, 409 `insufficient_stock`, ADR 0023). Uma venda de balcão, e mais ainda uma venda feita sem rede, não pode ser recusada por saldo: o cliente já levou a mercadoria.

| | **Opção A: permitir saldo negativo em local de loja** (recomendada pela arquitetura, [rascunho F](adr/rascunho-F-escala-hospedagem.md)) | **Opção B: manter o `CHECK` e abrir pendência** (recomendada pela análise de produto) |
|---|---|---|
| Como funciona | Só em local marcado como loja e só para origem PDV. Relatório de divergências. O custo usa o médio vigente | A falta fica estacionada numa fila de pendência até o ajuste. Regularizada a pendência, a baixa é aplicada |
| A favor | O saldo errado fica **visível** em vez de escondido. Mais simples de explicar ao lojista | Preserva o motor e o custo médio. Nada muda no que a Konebras usa |
| Contra | Exige afrouxar o `CHECK`: migration com ADR. Pode distorcer o custo médio | O saldo na tela parece certo enquanto há falta pendente. Mais uma fila para o lojista acompanhar |
| Efeito na Konebras | Nenhum, se a marca for por local e a fábrica não tiver local de loja | Nenhum |

A story [PDV-05](stories/PDV-05-baixa-estoque-sem-bloquear.md) está escrita para valer nas duas: o que muda é onde a falta aparece.

| Decisão |
|---|
| _(em aberto)_ |

### Demais pendências

| ID | Decisão | Opções | Recomendação | Bloqueia | Decisão |
|---|---|---|---|---|---|
| PP-03 | Módulo fora do perfil | (a) oculto; (b) com cadeado, como hoje | (a). O cadeado fica só para "no perfil, fora do plano". Muda uma regra do `CLAUDE.md`, então pede ADR | PERF-01 | |
| PP-04 | Fronteiras dos módulos novos | Catálogo: (a) módulo próprio; (b) preço no `Sales` e código de barras no item do Estoque. Venda: (a) `Sales` + `Pos`; (b) um projeto só | Catálogo próprio: preço, código de barras e ficha técnica servem a PDV, restaurante, oficina e loja virtual, e o Estoque não deve conhecer preço de venda. `Sales` + `Pos`: o e-commerce usa a venda sem caixa | CAT-01, PDV-01 | |
| PP-05 | Cupom não fiscal em tenant obrigado a emitir documento fiscal | (a) o sistema oferece o modo, com aviso; (b) só para MEI e dispensados; (c) não oferece | (a): imprime o texto de "sem valor fiscal", permite converter em NFC-e depois e tem relatório de vendas sem nota; a responsabilidade é do tenant, com aviso claro na configuração e parâmetro que desliga o modo por loja. Texto validado com a contadora (QC-13) | IMP-01, NFCE-02 | |
| PP-06 | UF e regime do primeiro cliente; UFs do lançamento, em ordem | — | Piloto na mesma UF da Konebras (SP). Confirmar com a contadora o documento exigido hoje; não assumir. Cada UF a mais custa pesquisa, credenciamento e piloto | NFCE-00, NFCE-01 | |
| PP-07 | Hospedagem | (a) migrar API, PostgreSQL e EmissorFiscal para São Paulo, em VPS dedicada, com WAL contínuo, antes da versão pública do PDV; (b) manter a VPS atual até haver cliente pagante | (a). A VPS atual foi informada como nos EUA: a estimativa é de 110 a 180 ms de ida e volta (não medido), cada NFC-e cruza o hemisfério duas vezes, alguma SEFAZ pode restringir IP estrangeiro (a validar) e CPF fora do país pede base legal na LGPD. RPO de 24 h não serve ao varejo. Orçamento a definir | INFRA-01 | |
| PP-08 | Agente local no Windows | (a) aceitar um instalador assinado como parte do "simples"; (b) só navegador | (a), se o SPK-01 confirmar que a impressora USB no Windows e a impressora de rede não funcionam só pelo navegador. Exige comprar certificado de assinatura de código | IMP-03 | |
| PP-09 | Plataformas da primeira versão | — | Windows com Chrome ou Edge e Android com Chrome. iOS fora do caixa. Navegador puro não assina offline (liga com a PP-01) | OFF-01 | |
| PP-10 | Janela máxima sem sincronizar e limites sem rede | (a) 72 h (proposta da arquitetura); (b) 7 dias (piso de mercado, Hiper); (c) parâmetro por loja | (c), com teto de 7 dias e padrão a decidir. **72 h fixas ficam abaixo do mercado.** Quanto maior a janela, maior a exposição a aparelho perdido e a PIN quebrado | OFF-03 | |
| PP-11 | PIN de supervisor no balcão para cancelar venda e NFC-e | (a) PIN de supervisor + motivo + aviso ao Proprietário; (b) manter o TOTP | (a). A ADR 0008 exige TOTP recente para estornar e a ADR 0026 recusa PIN para quem tem `*.approve`; nenhuma das duas funciona no balcão. O TOTP continua valendo na retaguarda. Exceção nomeada, como a `production.approve_quality` | PDV-04, OFF-03, NFCE-03 | |
| PP-12 | Operador de caixa, garçom e cozinha sem e-mail | (a) "usuário de operação" sem e-mail, que só entra por PIN em aparelho registrado; (b) apelido de e-mail (solução da P-24) | (a). Apelido de e-mail não escala para o varejo. ADR nova, como a P-24 já previa | PDV-01 | |
| PP-13 | Prioridade do EmissorFiscal | (a) NFC-e entra já no roteiro de lá; (b) esperar o MVP | (a). É o caminho crítico. As lacunas 25 e 5 afetam a NF-e de hoje e não esperam nada. A **dívida E14** (painel preso ao tenant da WSP) bloqueia o segundo cliente e precisa ser resolvida antes; decidir por qual dos dois caminhos já descritos no `plano-finalizacao.md` de lá. Definir quem faz | NFCE-00 | |
| PP-14 | Caminho da venda fiscal | (a) PDV → BemAqui → Emissor (dois saltos, como na ADR 0027); (b) PDV → Emissor direto, com chave por loja | (a) na primeira versão: mantém o imposto calculado no servidor e uma só integração. Rever com a latência medida no SPK-02 | NFCE-02 | |
| PP-15 | Filiais | (a) um CNPJ com várias lojas; (b) CNPJ por loja. E o certificado: A1 por loja ou o da matriz (lacuna 22) | (a) na primeira versão. (b) quando houver cliente com filial: pede emitente por estabelecimento e adendo à ADR 0027 §2. A1 da matriz na filial depende de a SEFAZ aceitar [F] (QC-16) | NFCE-07 | |
| PP-16 | Ordem dos segmentos e o que é "lançamento" | — | Balcão não fiscal primeiro, **como piloto**; NFC-e antes de anunciar ao mercado; depois lanchonete com KDS, mesas e comandas, supermercado. E-commerce em trilha paralela. O MVP sozinho fica abaixo do piso de mercado em NFC-e, TEF e SmartPOS | Fases 12 a 17 | |
| PP-17 | Planos comerciais | Preço por caixa, por loja ou por pacote; teto de aparelhos por plano; KDS, totem e TEF inclusos ou à parte | Decidir antes do primeiro cliente pagante. O benchmark mostra cobrança por módulo como norma e "pacote sem adicionais" como argumento de venda. Tecnicamente, limite em `plans.limits` | PERF-02 (limites) | |
| PP-18 | TEF e SmartPOS: quais fornecedores abordar primeiro | TEF tradicional (PayGo, SiTef), SmartPOS (Stone, PagBank, Cielo, Getnet), API em nuvem | Iniciar o contato comercial já: a homologação é longa. Preferir quem não exija o agente local. A venda já grava o tipo de integração desde o MVP | PAG-03, PAG-04, PDV-13 | |
| PP-19 | Hardware homologado | — | Lista curta: 2 impressoras de 80 mm (USB e rede), 1 Bluetooth de 58 mm, 1 leitor USB, 1 balança, 1 gaveta. Comprar o kit antes do SPK-01. A lista é contrato de suporte e precisa de dono | SPK-01, IMP-01, IMP-02, IMP-05, IMP-06 | |
| PP-20 | Split por pessoa: documento fiscal | (a) uma NFC-e por venda, com vários pagamentos; (b) uma por pagante | (a) como padrão; (b) como opção quando pedirem CPF individual. Confirmar com a contadora (QC-14) | MESA-04 | |
| PP-21 | Gateway de cartão do e-commerce, marketplaces e plataformas | Gateway com antifraude; marketplace direto ou por hub; quais plataformas (Nuvemshop, Tray, Loja Integrada) | Pix primeiro; um gateway com antifraude para cartão; marketplace e plataformas por hub, se houver um que cubra os quatro. Sem isso os itens 34, 36 e 38 do piso não fecham | ECM-03, ECM-06, ECM-07, PAG-06 | |
| PP-22 | Parâmetros da loja | — | Por loja, com padrão no seed. Sugestão inicial a validar: R$ 5,00 de tolerância de quebra; 5% de desconto para o operador; 30 dias para troca; teto de dinheiro em caixa a definir | CX-01, CX-02, PDV-04, PDV-06 | |
| PP-23 | Restaurante sem internet | (a) degradar: mesas, comandas, KDS e painel exigem rede; o caixa segue vendendo no modo balcão e a cozinha recebe pela impressora; (b) servidor local na loja | (a). Recomendar 4G de reserva, que custa menos que o servidor local. Loja que usa só KDS, sem impressora de cozinha, não tem modo sem internet: isso vai no material comercial | MESA-02, KDS-02 | |
| PP-24 | Painel do cliente | (a) só a senha; (b) senha e primeiro nome | (b) como opção por loja; padrão (a) | KDS-03 | |
| PP-25 | Vocabulário por perfil | — | Teto de cerca de 30 termos; sem tradução livre pelo tenant na primeira versão | PERF-03 | |
| PP-26 | CPF do consumidor e LGPD | — | CPF só para o documento fiscal e para localizar devolução; cifrado no aparelho; entra na tabela de retenção existente (ADR 0018 e 0037). Dados de cliente fora do país: resolvido pela PP-07 | PDV-02 | |
| PP-27 | O cliente criar a própria conta | — | Fora deste plano. Provisionamento pelo super admin até cerca de 20 tenants | — | |
| PP-28 | Ações novas no Estoque (`transfer`, `write_off`) | — | Criar só se houver segregação real (a ADR 0003 exige necessidade). Começar com as ações que já existem | CPD-01, CPD-02 | |
| PP-29 | Emissor: licença bloqueada | A loja para de vender com nota? | Carência, como já se faz com cancelamento e consulta | NFCE-00 | |
| PP-30 | Emissor: responsável técnico | Qual CNPJ assina como software house; quem cadastra o CSRT em cada SEFAZ | Definir antes da homologação em cada UF | NFCE-00 | |
| PP-31 | Emissor: biblioteca do DANFE em PDF | `Zeus.Danfe.QuestPdf` (conferir licença) ou HTML próprio com Gotenberg | Decidir no SPK-02. No caixa a impressão é ESC/POS a partir de dados; o PDF é só para segunda via e e-mail | NFCE-00 | |
| PP-32 | Emissor: número rejeitado na NFC-e | Queimar e inutilizar, como hoje, ou reaproveitar quando a rejeição é conclusiva | Depende da contadora (QC-11) | NFCE-00 | |
| PP-33 | Meta de latência e de volume | Tempo máximo no caixa, vendas por minuto no pico, lojas no primeiro ano | Sem isso a fase de escala do Emissor é chute. Proposta de teto no caixa: 8 a 10 s, depois a venda segue pendente | NFCE-00, NFCE-02 | |
| PP-34 | **Arquivo de certificado no diretório do EmissorFiscal** | — | Ver a nota de segurança abaixo | — | |
| PP-35 | Lojista piloto | — | Não abrir a Fase 11 sem um lojista parceiro de balcão para teste em campo; um de e-commerce para a Fase 14 | Fase 11 | |
| PP-36 | Integrações de food com terceiros | iFood, 99Food e Keeta (Open Delivery), robô de WhatsApp, totem | Só com cliente de alimentação pedindo. Credenciamento e custo a levantar. Sem isso os itens 14, 18 e 20 do piso não fecham | DLV-04, DLV-05, PDV-13 | |
| PP-37 | PRD e `CLAUDE.md` | — | Atualizar os dois antes das stories: o produto deixa de ser só "ERP de indústria sob encomenda" | Todas | |

#### Nota de segurança (PP-34)

A análise do EmissorFiscal encontrou em `D:\GIT\EmissorFiscal\docs\Certificado\` um arquivo `.pfx` e um arquivo de texto com a senha, lado a lado, no diretório de trabalho.

- **Não estão versionados** (`git ls-files` vazio) e **não foram abertos** nesta análise.
- Mesmo fora do git, certificado e senha juntos numa pasta de projeto ficam ao alcance de backup, sincronização de nuvem, busca de arquivos e de qualquer ferramenta que leia o diretório. A regra do `CLAUDE.md` do Emissor pede que não fiquem no repositório.
- **Recomendação:** tirar os dois da pasta do projeto e guardar o certificado em cofre, com a senha em outro lugar; conferir se a pasta está no `.gitignore`; e, se houver dúvida sobre cópias, considerar revogar e reemitir o A1. A decisão é do PO.

## 3. Perguntas para a contadora

**Nada do que é fiscal neste plano foi confirmado.** As respostas valem por UF: a coluna "SP" é a do piloto proposto (PP-06); cada UF nova repete a coluna.

| ID | Pergunta | Varia por UF? | SP | Outras UFs | Liga com |
|---|---|---|---|---|---|
| QC-01 | A contingência offline da NFC-e (`tpEmis=9`) é aceita? Qual o prazo de transmissão? Exige duas vias? E a alternativa "recibo agora, NFC-e na volta da rede" é aceitável, com que limite? | Sim | | | PP-01 |
| QC-02 | Prazo de cancelamento da NFC-e (o Ajuste SINIEF 19/16 fala em 30 minutos, com variação) e se aceita cancelamento por substituição | Sim | | | NFCE-03 |
| QC-03 | Há obrigação de TEF integrado, ou de vínculo entre o comprovante do cartão e a NFC-e? Em que UFs o grupo de cartão (credenciadora, bandeira, autorização) é exigido mesmo sem TEF? | Sim | | | PDV-03, PAG-03; RP-03 |
| QC-04 | Simples e MEI: CSOSN por tipo de produto (102, 500 e os demais); CST de PIS/COFINS (49, 99 ou outro); IBS/CBS já se preenche na NFC-e, e desde quando (as fontes divergem entre 01/01 e 04/01/2027) | Em parte | | | NFCE-05 |
| QC-05 | CFOP usados no varejo de cada segmento (supermercado, padaria, restaurante, oficina); tratamento da taxa de serviço e da gorjeta no restaurante | Em parte | | | NFCE-01, MESA-03 |
| QC-06 | Oficina: a peça sai em NFC-e ou em NF-e? O serviço em NFS-e (o padrão Nacional nunca foi exercitado no Emissor; só Caçapava está em uso) | Sim (município) | | | OFI-04 |
| QC-07 | SP: situação e datas da troca do SAT pela NFC-e. Outras UFs: ainda há equipamento fiscal específico exigido (SC e CE têm histórico)? | Sim | | | PP-06 |
| QC-08 | Valor a partir do qual o consumidor deve ser identificado na NFC-e | Sim | | | PDV-02 |
| QC-09 | Código de benefício fiscal (`cBenef`): em que UFs e CST se aplica aos clientes-alvo | Sim | | | NFCE-00 |
| QC-10 | Tributos aproximados (Lei 12.741/2012): fonte da alíquota e texto no cupom | Não | | | NFCE-06 |
| QC-11 | A dispensa de inutilização do Ajuste SINIEF 40/2026 vale para a NFC-e? O que fazer com número rejeitado | Sim | | | PP-32 |
| QC-12 | Nota emitida offline e rejeitada depois da venda: qual o procedimento aceito | Sim | | | PP-01 |
| QC-13 | Cupom não fiscal em quem é obrigado a emitir: texto do aviso, se pode converter em NFC-e depois, e quem está dispensado (MEI) | Em parte | | | PP-05 |
| QC-14 | Conta dividida: uma NFC-e por venda com vários pagamentos, ou uma por pagante com CPF | Não | | | PP-20 |
| QC-15 | Devolução de venda feita em NFC-e: NF-e de devolução é o caminho? | Em parte | | | PDV-06, NFCE-03 |
| QC-16 | Filial pode usar o A1 da matriz (mesma raiz de CNPJ)? | Sim | | | PP-15 |

Do benchmark, a confirmar em fonte primária antes de virar requisito: a regra de contingência offline em SP e o prazo de 24 h (só fontes de fornecedores, com contradição); a data do Simples na reforma (Ato Conjunto RFB/CGIBS nº 4/2026); a data de descontinuação do MFE no Ceará.

## 4. Riscos

| ID | Risco | Impacto | Mitigação |
|---|---|---|---|
| RP-01 | A NFC-e não existe no EmissorFiscal, que a declara fora de escopo | Bloqueia o PDV fiscal | NFCE-00 começa já no repositório do Emissor; o MVP não depende dela; desenvolvimento contra o falso do `IFiscalEmitter`, como nas stories fiscais; SPK-02 |
| RP-02 | Sem internet não há como assinar NFC-e na loja | Legal | PP-01; contadora; 4G de reserva |
| RP-03 | Maquininha não integrada proibida em alguma UF | Legal e comercial | QC-03; antecipar o TEF nas UFs afetadas |
| RP-04 | Regras fiscais do varejo (Simples, ST, monofásico, IBS/CBS, exigência por UF) | Alto | Presets "A confirmar com a contadora" (ADR 0042); piloto em uma UF; diferenças por UF em tabela de configuração, não em `if` |
| RP-05 | Periféricos no Windows pelo navegador não funcionam de forma simples | Alto | SPK-01; ponte (IMP-03); lista curta de modelos homologados |
| RP-06 | Offline: duplicidade, perda de venda, relógio errado, conflito | Alto | Eventos imutáveis com `event_id` e cadeia de hash; teste de reenvio em paralelo; SPK-03; piloto em campo antes de vender |
| RP-07 | Vendas não sincronizadas perdidas com o aparelho | Alto | Sincronizar logo; contador visível; cópia do registro em disco pelo agente; alerta de fila antiga |
| RP-08 | PIN sem rede quebrável com acesso físico ao aparelho | Médio | Limites do que se faz sem rede; janela máxima (PP-10); revisão na sincronização |
| RP-09 | Hospedagem fora do Brasil: latência, SEFAZ, LGPD | Alto | INFRA-01 (PP-07) |
| RP-10 | VPS única, compartilhada, com RPO de 24 h | Alto para o varejo | VPS dedicada; WAL contínuo; restauração ensaiada |
| RP-11 | Disputa por numeração e custo médio sob volume de supermercado | Médio | Baixa agregada; número por caixa; teste de carga |
| RP-12 | Limites do Emissor não comportam supermercado (60 requisições por minuto por cliente; instância única; PDF por nota) | Médio | Lacunas 10, 31, 32 e 34; a meta sai da PP-33 |
| RP-13 | Homologação de TEF e SmartPOS demorada e cara | Prazo | Contato comercial já (PP-18); porta pronta desde o MVP; Pix integrado primeiro |
| RP-14 | Atualização do app no meio do turno; versões antigas enviando eventos | Médio | Atualizar só na troca de turno; `schema_version` |
| RP-15 | Escopo: 6 módulos novos, 8 segmentos e 87 stories com um PO | Alto | MVP estreito; fases 15 a 17 só com cliente real pedindo; cada segmento é seed + extensão, nunca um PDV novo |
| RP-16 | Regressão na Konebras, que está em produção | Alto | Perfil `factory` sem mudança; teste de regressão da navegação; migrations nunca destrutivas; mudanças no Estoque e no Financeiro só aditivas |
| RP-17 | Cupom não fiscal usado para sonegar | Médio (jurídico e de imagem) | PP-05; relatório de vendas sem nota; parâmetro que desliga o modo |
| RP-18 | Fraude interna no caixa | Médio | Controles do [PRD, seção 6.5](01-PRD.md#65-controles-antifraude); relatórios por operador desde o MVP |
| RP-19 | MVP abaixo do piso de mercado (sem NFC-e, TEF e SmartPOS) apresentado como produto pronto | Comercial e de imagem | Tratar o MVP como piloto (PP-16); só anunciar depois da Fase 13 |
| RP-20 | Itens do piso que dependem de terceiros (iFood, marketplaces, plataformas, WhatsApp) sem prazo controlável | Prazo | PP-21 e PP-36; preferir hub e padrão aberto (Open Delivery) |
| RP-21 | Suporte a hardware vira operação contínua; o lojista liga na hora do pico | Custo | Lista curta homologada; checklist de primeiros passos; ajuda contextual por tela; modo treinamento (PDV-11) |
| RP-22 | Sem piloto definido | Médio | PP-35 |
| RP-23 | Estimativas de esforço não reconciliadas entre as análises | Prazo | SPK-03 antes de assumir data; ver [05-backlog.md](05-backlog.md#esforço) |
| RP-24 | Regras fiscais marcadas [F] vêm de memória e podem estar desatualizadas em 11/2026 | Alto | Nenhuma vira regra de código sem a contadora ou fonte oficial |
| RP-25 | Certificado e senha numa pasta de projeto do Emissor | Segurança | PP-34 |

## 5. O que não foi verificado

- Nenhuma NFC-e foi montada, assinada ou transmitida. O suporte da biblioteca Unimake foi inferido dos nomes de símbolos na DLL.
- Latência e volume não foram medidos. A região da VPS foi assumida como informada.
- No benchmark, parte dos preços e das funcionalidades não pôde ser confirmada em fonte primária ([seção 6](00-benchmark-mercado.md#6-o-que-não-consegui-verificar)).
- Do lado do BemAqui não foram lidos por inteiro: `docs/04-fluxos-estados.md`, o PRD em detalhe, o `OutboxProcessor` e o adaptador `EmissorFiscalHttpEmitter.cs`.

## 6. Próximos passos sugeridos (sem código)

1. O PO decide as sete pendências da seção 1.
2. Levar QC-01 a QC-16 à contadora, começando por QC-01, QC-03 e QC-13.
3. Abrir a NFCE-00 no EmissorFiscal, começando pelas duas correções da NF-e e pelo spike da Unimake; decidir a E14.
4. Comprar o kit de hardware (PP-19) e rodar os três spikes.
5. Escolher um lojista piloto de balcão e um de e-commerce (PP-35).
6. Com os spikes concluídos: fechar os rascunhos A a F como ADRs, escrever a ADR dos perfis de negócio e integrar este plano aos documentos principais.
