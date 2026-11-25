# 06 — Pendências, perguntas para a contadora e riscos do plano do PDV

Regra do repositório: item pendente não é implementado até ter decisão registrada (data + quem decidiu).

Os ids `PP-nn` são deste plano. Na aprovação, cada decisão entra no `docs/06-pendencias-riscos.md` como P-31 em diante. A coluna "Decisão" fica vazia até o PO decidir. As decisões de 15/11/2026 já estão registradas, com a data.

## 1. Perguntas ao PO em primeiro lugar

As cinco perguntas que a revisão independente de 15/11/2026 pediu para fazer antes de qualquer outra coisa, com o que o PO respondeu no mesmo dia.

| # | Pergunta | Resposta do PO (15/11/2026) | Situação |
|---|---|---|---|
| 1 | O primeiro cliente pagante é e-commerce ou balcão? Se e-commerce, ele já tem loja (Nuvemshop, Tray) ou quer a loja própria? | **Balcão.** A trilha do balcão vem antes do e-commerce | Respondida (PP-16). A segunda metade volta a valer quando a trilha do e-commerce abrir |
| 2 | Quem faz o trabalho do EmissorFiscal, e ele pode começar antes do go-live? | **O próprio PO, em outra frente de trabalho, e pode começar antes do go-live da Konebras.** Exceção só para o Emissor | Respondida (PP-13, PP-41) |
| 3 | PP-01: aceita um certificado na loja sabendo que ele vale para todo o CNPJ? Se não, aceita por escrito o "cupom agora, NFC-e depois" após o parecer da contadora? | **Sim: contingência offline de verdade, com o certificado instalado no computador da loja e as proteções** (um servidor por loja, chave não exportável, certificado separado do cofre, série própria com alerta, inventário) | **Respondida e decidida** (PP-01). Ficam só as perguntas à contadora por UF e a consulta à certificadora |
| 4 | O piloto não fiscal será com MEI ou dispensado? Em que UF? | **O cupom não fiscal vale para qualquer empresa que decida, por conta própria, não emitir.** As duas opções (NFC-e e cupom não fiscal) existem e a escolha é configurável por tenant. A responsabilidade é do cliente | Respondida (PP-05, PP-35). A UF do piloto é definida com o lojista (PP-06) |
| 5 | "CPD" é o depósito que abastece as lojas ou a retaguarda do PDV? Há orçamento para a hospedagem no Brasil antes do piloto? | **CPD é a retaguarda** (Centro de Processamento de Dados no sentido do varejo). **Hospedagem: ficar na VPS atual por enquanto**, sem migrar antes do piloto | Respondida (PP-40, PP-07) |

## 2. Decisões que destravam as Fases 10 e 11

A primeira versão falava em "sete decisões". A revisão mostrou que eram mais: toda pendência citada como dependência de uma story das Fases 10 e 11 trava essa story. Em 15/11/2026 o PO decidiu cinco delas; a tabela separa o que foi decidido do que **continua de fato pendente**.

**Continua pendente:**

| # | Decisão | Recomendação do plano | Trava |
|---|---|---|---|
| PP-08 | Ponte (agente local) no Windows | Aceitar. Com a PP-01 decidida, o agente passa a ser necessário de qualquer forma para a assinatura na loja; o que resta decidir é se ele entra já no MVP, para a impressão | IMP-02; a IMP-03 no MVP |
| PP-19 | Kit de hardware | Comprar antes do SPK-01 | SPK-01, IMP-01, IMP-02 |

**Decidido em 15/11/2026 (John):**

| # | Decisão | O que ficou |
|---|---|---|
| PP-01 | Venda sem internet em loja fiscal | Assinatura na loja, com as proteções. Não travava as Fases 10 e 11; destrava a NFCE-04 e a fase 3 do Emissor |
| PP-05 | Cupom fiscal × não fiscal | As duas opções existem; configurável por tenant, com aceite do Proprietário. O padrão é da empresa e cada loja pode ter exceção (`stores.fiscal_mode`) |
| PP-38 | Limite de desconto | **Por loja** (parâmetro do `Pos`); o grupo dá só a permissão `pos.discount`. Aceita faixa por papel dentro da loja |
| PP-39 | PIN sem rede | **Operador usa o mesmo PIN; supervisor usa um código de aprovação offline separado** |
| PP-07 | Onde hospedar | Ficar na VPS atual. A INFRA-01 saiu do caminho do MVP |
| PP-13 | Dono e início do EmissorFiscal | O PO, em outra frente, antes do go-live. Falta só o caminho da E14 |
| PP-16 | Ordem das trilhas | Balcão primeiro |
| PP-40 | Sentido de "CPD" | Retaguarda |
| PP-41 | Exceção ao go-live | **Revista em 16/11/2026:** a construção do PDV começa já, na `main`, em PRs pequenos, com os módulos novos desligados para a Konebras; correções do go-live têm prioridade sobre qualquer story do PDV |
| PP-03 | Módulo fora do perfil | **Oculto** (16/11/2026). O cadeado fica para "no perfil, fora do plano" |
| PP-04 | Fronteiras dos módulos novos | **Catálogo próprio; `Sales` + `Pos`**, com a tabela de donos do [02, seção 4.1](02-arquitetura.md#41-fronteira-sales--pos-quem-é-dono-de-quê) (16/11/2026) |
| PP-25 | Vocabulário por perfil | **Teto de cerca de 30 termos**, sem tradução livre pelo tenant na primeira versão (16/11/2026) |

**Decidido em 18/11/2026 (John):**

| # | Decisão | O que ficou |
|---|---|---|
| PP-35 | Lojista piloto de balcão | **A Fase 11 abre agora, sem lojista piloto.** A validação é na Loja Demo (tenant de teste com o perfil Comércio). Quando houver piloto, ajusta-se com ele antes de ir à loja real |
| PP-11 | PIN de supervisor no balcão no lugar do TOTP | **Aceito**, como exceção nomeada à ADR 0008 e à ADR 0026: PIN do supervisor, motivo e aviso ao Proprietário, tudo auditado. Vale **só** para as ações de balcão da story (cancelar venda e NFC-e, desconto acima do limite). O TOTP continua valendo na retaguarda. Registrado na [ADR 0048](../adr/0048-aparelho-do-pdv-e-esquema-device.md) e nos adendos das ADRs 0008 e 0026 |
| PP-12 | Operador de caixa sem e-mail | **Criar o "usuário de operação"**: sem e-mail, criado pelo gerente, entra só por PIN em aparelho registrado da loja e não acessa a retaguarda ([ADR 0049](../adr/0049-usuario-de-operacao.md)) |
| PP-09 | Plataformas da primeira versão | **Windows com Chrome ou Edge e Android com Chrome. iOS fica fora do caixa** |
| PP-10 | Janela máxima sem sincronizar | **Prática de mercado (Hiper): até 7 dias sem sincronizar.** Parâmetro por loja, padrão e teto de 7 dias; a loja pode reduzir. O caixa avisa enquanto está sem conexão e para de vender ao estourar a janela. O prazo legal de 24 h para transmitir a NFC-e em contingência é outra regra (stories NFCE) |
| PP-02 | Venda com saldo insuficiente | **Configurável nas Configurações da empresa; padrão: não vender sem saldo.** Ligada, a venda sai e a falta vai para a pendência da retaguarda (opção B: o `CHECK` do estoque não muda). Sem rede, o caixa confere pelo último saldo recebido; venda concluída nunca é recusada na sincronização |
| PP-22 | Parâmetros da loja | **Padrões no seed, editáveis por loja:** tolerância de quebra de R$ 5,00 por forma de pagamento; 30 dias para troca; aviso de sangria a partir de R$ 1.000,00 em dinheiro no caixa |
| PP-26 | CPF do consumidor e LGPD | **Segue a recomendação do plano:** no MVP, o CPF só serve à nota e a localizar a devolução (fiado, fidelidade e loja virtual pedem base legal e consentimento próprios quando abrirem); a loja é controladora e o BemAqui é operador (contrato e política de privacidade); CPF cifrado no aparelho, fora de log, URL, outbox e do registro imutável, apagável, com busca por índice cego; base legal registrada antes do piloto |

**Não travam as Fases 10 e 11**, mas têm prazo longo e convém decidir cedo: PP-06 (UF do piloto), PP-18 (adquirentes e TEF), o caminho da E14 (PP-13) e as perguntas QC-01 e QC-12 à contadora, que agora seguram a NFCE-04.

## 3. Pendências (decisão do PO)

### PP-01 — Venda sem internet em loja que emite NFC-e

**O problema.** O certificado A1 fica só no cofre do EmissorFiscal (ADR 0027 §3). Com a loja sem internet, ninguém na loja consegue assinar. E a contingência offline da NFC-e exige assinatura no momento da venda: o QR Code do DANFE de contingência deriva do XML assinado [F]. Sem certificado ao alcance do caixa não existe NFC-e em contingência; existe só um recibo.

Este cenário é diferente de "internet ok, SEFAZ fora do ar". Nesse outro caso o Emissor, que tem o certificado, emite em contingência e devolve o XML assinado e o QR. A primeira versão do plano dava isso como resolvido; a revisão mostrou que faltava a lacuna correspondente no Emissor. Agora é a **lacuna 41**, com a story NFCE-08.

**Decidido pelo PO em 15/11/2026: opção (b), assinatura na loja.** O PO quer contingência offline de verdade: vender e emitir sem internet e transmitir à base nacional quando a rede voltar. Ele confirmou o certificado instalado no computador da loja, com as proteções abaixo, e **um servidor de assinatura por loja, não por caixa**. A opção (a) fica só como modo degradado, desligado por padrão. **O que continua aberto não é mais decisão do PO:** as perguntas à contadora por UF (se a UF aceita `tpEmis=9` e o prazo de transmissão: QC-01 e QC-12) e a consulta à certificadora sobre os tipos de certificado.

As duas análises tinham recomendado coisas opostas. O quadro fica registrado:

| | **(a) Cupom agora, NFC-e depois** (arquitetura do PDV, [rascunho E](adr/rascunho-E-acoplamento-fiscal.md)) | **(b) Assinatura na loja** (análise do Emissor, [07, seção 2.6](07-emissor-fiscal.md#26-quem-assina-offline-opções-e-recomendação)) |
|---|---|---|
| Como funciona | O caixa imprime um comprovante não fiscal e enfileira. O Emissor emite como nota normal quando a rede volta. Com 4G de reserva como orientação de implantação | Um agente local (por loja, ou por caixa em loja pequena) tem um certificado, monta, assina e numera a NFC-e `tpEmis=9`, imprime o DANFE de contingência e envia o XML ao Emissor quando a rede volta |
| A favor | Simples. Nada novo no Emissor. Nenhum certificado sai do cofre | É a contingência prevista na norma. Funciona sem internet. O consumidor sai com documento fiscal. É o que o PO pediu |
| Contra | **Não é contingência fiscal.** O consumidor sai sem documento fiscal. A nota emitida depois leva a data da reconexão, não a da venda, ou é rejeitada por atraso [F] | **Passa a existir um certificado do CNPJ dentro da loja** (ver abaixo). É software a mais para distribuir, assinar e atualizar (a ponte, IMP-03). Pede as lacunas 13 a 16 e 21 do Emissor |
| Ressalva legal | Entregar mercadoria sem documento fiscal **pode ser infração**; o risco de autuação é do cliente | Depende de a UF aceitar `tpEmis=9` e de qual o prazo de transmissão [F: a confirmar por UF; SP inclusive]. Nota offline rejeitada depois da venda tem procedimento fiscal a definir (QC-12) |
| Efeito no mercado | Fica igual à Saipos, que admite não emitir nota nesse modo | É o diferencial "emitir offline" que o benchmark aponta |
| Situação | Modo degradado, desligado por padrão, habilitado por cliente com aceite formal | **Decidida pelo PO em 15/11/2026** |

#### O certificado na loja: o que foi decidido e o que isso significa

A primeira versão deste plano falava em "A1 dedicado, de alcance e revogação controlados". **Essa frase induzia a erro e foi retirada** (achado B3 da revisão).

- **Um certificado A1 e-CNPJ não tem escopo técnico.** Quem copia o arquivo e a senha assina **qualquer documento em nome do CNPJ**: nota de qualquer modelo e série, procuração eletrônica, acesso a portais do governo. Não existe "certificado só para a série de contingência".
- Isto é **conhecimento geral, não conferido com uma Autoridade Certificadora**. A consulta à certificadora continua pendente: se existe tipo de certificado restrito a documento fiscal eletrônico, quanto custa, e se um CNPJ pode ter mais de um A1 ativo. A resposta pode reduzir o risco; não muda a decisão.
- O requisito original ("o PDV não guarda o A1") fica atendido só em parte: o A1 do cofre do Emissor não sai de lá, mas passa a existir **outro certificado do mesmo CNPJ dentro da loja**. É preciso adendo à ADR 0027 §3.

O PO decidiu pela opção (b) com estas cinco proteções, todas obrigatórias:

| # | Proteção | O que evita | O que não evita |
|---|---|---|---|
| 1 | **Chave não exportável:** certificado instalado no TPM do equipamento ou no repositório de certificados do Windows, com a chave marcada como não exportável. O `.pfx` e a senha nunca ficam em disco na loja; o agente nunca os lê | Cópia do arquivo por funcionário, técnico ou programa malicioso comum | Uso do certificado por quem controla aquele computador enquanto ele está ligado |
| 2 | **Certificado próprio da loja**, separado do que está no cofre central e revogável sem parar o resto [F: emitir mais de um A1 por CNPJ é prática comum; confirmar com a AC] | Revogar o da loja derrubar a emissão central, e o contrário | — |
| 3 | **Inventário por loja**, com **um servidor de assinatura por loja, não por caixa**: qual certificado (número de série, validade) está em qual equipamento, quem instalou e quando; conferido a cada sincronização do agente | Certificado esquecido em máquina vendida, trocada ou roubada; e um certificado em cada caixa, que multiplicaria a exposição | — |
| 4 | **Procedimento e custo de revogação escritos:** quem revoga, em quanto tempo, quanto custa reemitir e o que a loja faz enquanto isso (cai no modo degradado) | Demora e improviso no dia do incidente | — |
| 5 | **Série própria de contingência, com alerta de uso fora dela:** o Emissor avisa quando chega nota assinada por certificado de loja fora da série de contingência daquela loja, ou de modelo diferente de 65 | Uso indevido **que passe pelo Emissor** | Uso do certificado fora do sistema, que não é detectável |

Consequências do "um servidor por loja": os caixas da loja pedem a assinatura ao servidor pela rede local, então **a rede interna da loja precisa estar de pé** mesmo sem internet; se o servidor da loja cair, os caixas ficam no modo degradado. Em Android não há repositório do Windows nem TPM equivalente acessível ao navegador: **a assinatura depende do agente em Windows**. Trailer com um celular só não assina offline; fica no modo degradado ou no 4G.

Outras opções, que nenhuma das análises recomenda como desenho principal:

| Opção | Por que não |
|---|---|
| Bloquear a venda quando o documento é obrigatório | Seguro juridicamente; contraria o offline que o PO pediu |
| EPEC (`tpEmis=4`) | Precisa de internet; disponível em poucas UFs [F] |
| Navegador assinar | Navegador puro não assina offline |

**O que ainda falta, fora do PO:**
1. Contadora, por UF: a UF aceita `tpEmis=9`? Qual o prazo de transmissão? O que fazer com a nota offline rejeitada depois da venda? (QC-01, QC-12)
2. Certificadora: tipos de certificado, mais de um A1 por CNPJ, custo e prazo de revogação e reemissão.
3. Implantação: o 4G de reserva continua recomendado em loja fiscal, para reduzir o tempo em contingência.

**O que passa a ser escopo firme:** o agente de assinatura por loja (NFCE-04 e IMP-03), as lacunas 13 a 16 e 21 do Emissor (fase 3 de lá) e o adendo à ADR 0027 §3.

**O que não depende desta pendência:** o MVP (balcão com cupom não fiscal), a NFC-e online (NFCE-00 a NFCE-03) e a contingência com a SEFAZ fora (NFCE-08).

| Decisão |
|---|
| 15/11/2026 (John): **(b), contingência offline de verdade, com assinatura na loja.** Certificado instalado no computador da loja, com as proteções: um servidor por loja e não por caixa; chave não exportável; certificado separado do que fica no cofre central, revogável sem parar o resto; série própria de contingência, com alerta de uso fora dela; inventário por loja. A opção (a) permanece apenas como modo degradado, desligado por padrão. Ficam abertas só as perguntas à contadora por UF (prazo de transmissão; se a UF aceita `tpEmis=9`) e a confirmação com a certificadora sobre os tipos de certificado. |

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
| **18/11/2026 (John):** opção nas Configurações da empresa, padrão **não vender** sem saldo. Ligada, vale a opção B (pendência na retaguarda; o motor e o `CHECK` não mudam, nada muda na Konebras). Sem rede, o caixa usa o último saldo recebido e a venda concluída nunca é recusada depois |

### Demais pendências

| ID | Decisão | Opções | Recomendação | Bloqueia | Decisão |
|---|---|---|---|---|---|
| PP-03 | Módulo fora do perfil | (a) oculto; (b) com cadeado, como hoje | (a). O cadeado fica só para "no perfil, fora do plano". Muda uma regra do `CLAUDE.md`, então pede ADR | PERF-01 | |
| PP-04 | Fronteiras dos módulos novos | Catálogo: (a) módulo próprio; (b) preço no `Sales` e código de barras no item do Estoque. Venda: (a) `Sales` + `Pos`; (b) um projeto só | Catálogo próprio: preço, código de barras e ficha técnica servem a PDV, restaurante, oficina e loja virtual, e o Estoque não deve conhecer preço de venda. `Sales` + `Pos`, com o dono de cada tabela e rota e o contrato `ISaleIngestion` como estão em [02, seção 4.1](02-arquitetura.md#41-fronteira-sales--pos-quem-é-dono-de-quê) | CAT-01, VND-01, PDV-01 | |
| PP-05 | Cupom fiscal × cupom não fiscal | (a) o sistema oferece o modo não fiscal a qualquer empresa; (b) só para MEI e dispensados; (c) não oferece | A revisão recomendava deixar desligado até a resposta da contadora. O PO decidiu diferente (ao lado) | IMP-01, NFCE-02 | 15/11/2026 (John): **(a). As duas opções existem ("NFC-e" e "só cupom não fiscal") e a escolha é configurável por tenant**, loja a loja. Vale para qualquer empresa que decida, por conta própria, não emitir o cupom fiscal; a escolha é responsabilidade do cliente. Condições: a opção é ligada pelo **Proprietário** do tenant, com **aceite explícito registrado em auditoria** (o texto diz que a obrigação fiscal é da empresa e que o BemAqui não avalia se ela é dispensada); o comprovante sai com **"Não é documento fiscal"**; há **relatório de vendas sem documento fiscal por período**. A pergunta à contadora (QC-13) não trava o produto: é orientação que o cliente deve buscar. O risco continua (RP-17) |
| PP-06 | UF e regime do primeiro cliente; UFs do lançamento, em ordem | — | Piloto na mesma UF da Konebras (SP). Confirmar com a contadora o documento exigido hoje; não assumir. Cada UF a mais custa pesquisa, credenciamento e piloto | NFCE-00, NFCE-01 | |
| PP-07 | Hospedagem | (a) migrar API, PostgreSQL e EmissorFiscal para São Paulo, em VPS dedicada, com WAL contínuo, antes de o MVP ir a cliente; (b) ficar na VPS atual por enquanto | As análises recomendavam (a). O PO decidiu (b); ver o quadro "PP-07: o que foi aceito" abaixo | INFRA-01 (condicionada), INFRA-02 | 15/11/2026 (John): **(b). Ficar na VPS atual neste momento; não migrar para o Brasil antes do piloto.** A INFRA-01 sai do caminho crítico do MVP e fica condicionada aos gatilhos |
| PP-08 | Agente local no Windows | (a) aceitar um instalador assinado como parte do "simples"; (b) só navegador | (a). Com a PP-01 decidida pela assinatura na loja, o agente em Windows **passa a existir de qualquer forma** em loja fiscal. O que resta decidir é se ele entra já no MVP, para a impressão, o que depende do SPK-01. Exige comprar certificado de assinatura de código | IMP-03 | |
| PP-09 | Plataformas da primeira versão | — | Windows com Chrome ou Edge e Android com Chrome. iOS fora do caixa. Navegador puro não assina offline (liga com a PP-01) | OFF-01 | **18/11/2026 (John): Windows com Chrome ou Edge e Android com Chrome; iOS fica fora do caixa** |
| PP-10 | Janela máxima sem sincronizar e limites sem rede | (a) 72 h (proposta da arquitetura); (b) 7 dias (piso de mercado, Hiper); (c) parâmetro por loja | (c), com teto de 7 dias e padrão a decidir. **72 h fixas ficam abaixo do mercado.** Quanto maior a janela, maior a exposição a aparelho perdido e a PIN quebrado | OFF-03 | **18/11/2026 (John): seguir o mercado.** (c) com padrão e teto de 7 dias; a loja pode reduzir |
| PP-11 | PIN de supervisor no balcão para cancelar venda e NFC-e | (a) PIN de supervisor + motivo + aviso ao Proprietário; (b) manter o TOTP | (a). A ADR 0008 exige TOTP recente para estornar e a ADR 0026 recusa PIN para quem tem `*.approve`; nenhuma das duas funciona no balcão. O TOTP continua valendo na retaguarda. Exceção nomeada, como a `production.approve_quality` | PDV-04, OFF-03, NFCE-03 | **18/11/2026 (John): (a)**, exceção nomeada à ADR 0008 e à ADR 0026: PIN, motivo e aviso ao Proprietário, tudo auditado; só para as ações de balcão (cancelar venda e NFC-e, desconto acima do limite) |
| PP-12 | Operador de caixa, garçom e cozinha sem e-mail | (a) "usuário de operação" sem e-mail, que só entra por PIN em aparelho registrado; (b) apelido de e-mail (solução da P-24) | (a). Apelido de e-mail não escala para o varejo. ADR nova, como a P-24 já previa | PDV-01 | **18/11/2026 (John): (a).** Sem e-mail, criado pelo gerente, entra só por PIN em aparelho registrado da loja e não acessa a retaguarda (ADR 0049) |
| PP-13 | Prioridade e dono do EmissorFiscal | (a) NFC-e entra já no roteiro de lá; (b) esperar o MVP | (a). É o caminho crítico. As lacunas 25 e 5 afetam a NF-e de hoje e não esperam nada. A **dívida E14** (painel preso ao tenant da WSP) bloqueia o segundo cliente, de balcão ou de loja virtual | NFCE-00 | 15/11/2026 (John): **(a). O dono é o próprio PO, em outra sessão de trabalho, e o trabalho pode começar antes do go-live** (PP-41). **Falta decidir** por qual dos dois caminhos do `plano-finalizacao.md` de lá a E14 será resolvida |
| PP-14 | Caminho da venda fiscal | (a) PDV → BemAqui → Emissor (dois saltos, como na ADR 0027); (b) PDV → Emissor direto, com chave por loja | (a) na primeira versão: mantém o imposto calculado no servidor e uma só integração. Rever com a latência medida no SPK-02 | NFCE-02 | |
| PP-15 | Filiais | (a) um CNPJ com várias lojas; (b) CNPJ por loja. E o certificado: A1 por loja ou o da matriz (lacuna 22) | (a) na primeira versão. (b) quando houver cliente com filial: pede emitente por estabelecimento e adendo à ADR 0027 §2. A1 da matriz na filial depende de a SEFAZ aceitar [F] (QC-16) | NFCE-07 | |
| PP-16 | Ordem das trilhas e o que é "versão pública" | Trilha A, **balcão primeiro**: Fases 10 → 11 → 12 e 13, e o e-commerce depois. Serve se o primeiro cliente é de balcão. Trilha B, **e-commerce primeiro**: Fase 10 → Fase 14 (loja própria, ECM-01 a ECM-04, ou a ECM-07 reduzida se o cliente já tem loja em Nuvemshop ou Tray) e o balcão depois. É a mais curta até o primeiro faturamento, porque não precisa de caixa, de offline nem de NFC-e, mas depende da dívida E14 e da lacuna 25 do Emissor | O núcleo de vendas (VND-01) fica na Fase 10 nas duas trilhas, e as stories ECM-01 a ECM-04 já estão detalhadas, para a ordem poder mudar sem replanejar. O MVP de balcão é piloto; a versão pública é com NFC-e (Fase 13) | Fases 12 a 17 | 15/11/2026 (John): **trilha A, balcão primeiro**; o primeiro cliente é de balcão. O e-commerce é a trilha seguinte. Quando ela abrir, decidir entre loja própria e ECM-07 reduzida, conforme o cliente já tenha loja |
| PP-17 | Planos comerciais | Preço por caixa, por loja ou por pacote; teto de aparelhos por plano; KDS, totem e TEF inclusos ou à parte | Decidir antes do primeiro cliente pagante. O benchmark mostra cobrança por módulo como norma e "pacote sem adicionais" como argumento de venda. Tecnicamente, limite em `plans.limits` | PERF-02 (limites) | |
| PP-18 | TEF e SmartPOS: quais fornecedores abordar primeiro | TEF tradicional (PayGo, SiTef), SmartPOS (Stone, PagBank, Cielo, Getnet), API em nuvem | Iniciar o contato comercial já: a homologação é longa. Preferir quem não exija o agente local. A venda já grava o tipo de integração desde o MVP | PAG-03, PAG-04, PDV-13 | |
| PP-19 | Hardware homologado | — | Lista curta: 2 impressoras de 80 mm (USB e rede), 1 Bluetooth de 58 mm, 1 leitor USB, 1 balança, 1 gaveta. Comprar o kit antes do SPK-01. A lista é contrato de suporte e precisa de dono | SPK-01, IMP-01, IMP-02, IMP-05, IMP-06 | |
| PP-20 | Split por pessoa: documento fiscal | (a) uma NFC-e por venda, com vários pagamentos; (b) uma por pagante | (a) como padrão; (b) como opção quando pedirem CPF individual. Confirmar com a contadora (QC-14) | MESA-04 | |
| PP-21 | Gateway de cartão do e-commerce, marketplaces e plataformas | Gateway com antifraude; marketplace direto ou por hub; quais plataformas (Nuvemshop, Tray, Loja Integrada) | Pix primeiro; um gateway com antifraude para cartão; marketplace e plataformas por hub, se houver um que cubra os quatro. Sem isso os itens 34, 36 e 38 do piso não fecham | ECM-03, ECM-06, ECM-07, PAG-06 | |
| PP-22 | Parâmetros da loja | — | Por loja, com padrão no seed. Sugestão inicial a validar: R$ 5,00 de tolerância de quebra **por forma de pagamento**; 30 dias para troca; teto de dinheiro em caixa a definir. O limite de desconto está na PP-38 | CX-01, CX-02, PDV-04, PDV-06 | **18/11/2026 (John): aceitos os padrões.** R$ 5,00 por forma; 30 dias para troca; aviso de sangria a partir de R$ 1.000,00. Tudo editável por loja |
| PP-23 | Restaurante sem internet | (a) degradar: mesas, comandas, KDS e painel exigem rede; o caixa segue vendendo no modo balcão e a cozinha recebe pela impressora; (b) servidor local na loja | (a). Recomendar 4G de reserva, que custa menos que o servidor local. Loja que usa só KDS, sem impressora de cozinha, não tem modo sem internet: isso vai no material comercial | MESA-02, KDS-02 | |
| PP-24 | Painel do cliente | (a) só a senha; (b) senha e primeiro nome | (b) como opção por loja; padrão (a) | KDS-03 | |
| PP-25 | Vocabulário por perfil | — | Teto de cerca de 30 termos; sem tradução livre pelo tenant na primeira versão | PERF-03 | |
| PP-26 | CPF do consumidor e LGPD | — | (1) **Finalidades, uma a uma:** documento fiscal e localizar devolução no MVP; fiado (PDV-08), fidelidade (FID-02) e loja virtual (ECM) são finalidades novas, cada uma com base legal e consentimento próprios quando for aberta. (2) **Papéis:** o lojista é o controlador dos dados dos clientes dele; o BemAqui é o operador. Isso entra no contrato e na política de privacidade. (3) **Onde fica:** cifrado no aparelho; fora de log, de URL e do outbox; fora do registro imutável de eventos, em registro apagável; busca por índice cego. Entra na tabela de retenção (ADR 0018 e 0037). (4) **Dados fora do país:** a PP-07 resolve só depois da migração. Durante o piloto na VPS atual, e sempre no aparelho da loja, o dado continua onde está: registrar a base legal antes do piloto | PDV-02, PDV-06, OFF-02; PDV-08, FID-02, ECM-02 | **18/11/2026 (John): seguir a recomendação**, os quatro pontos |
| PP-27 | O cliente criar a própria conta | — | Fora deste plano. Provisionamento pelo super admin até cerca de 20 tenants | — | |
| PP-28 | Ações novas no Estoque (`transfer`, `write_off`) | — | Criar só se houver segregação real (a ADR 0003 exige necessidade). Começar com as ações que já existem | CPD-02, CPD-06 | |
| PP-29 | Emissor: licença bloqueada | A loja para de vender com nota? | Carência, como já se faz com cancelamento e consulta | NFCE-00 | |
| PP-30 | Emissor: responsável técnico | Qual CNPJ assina como software house; quem cadastra o CSRT em cada SEFAZ | Definir antes da homologação em cada UF | NFCE-00 | |
| PP-31 | Emissor: biblioteca do DANFE em PDF | `Zeus.Danfe.QuestPdf` (conferir licença) ou HTML próprio com Gotenberg | Decidir no SPK-02. No caixa a impressão é ESC/POS a partir de dados; o PDF é só para segunda via e e-mail | NFCE-00 | |
| PP-32 | Emissor: número rejeitado na NFC-e | Queimar e inutilizar, como hoje, ou reaproveitar quando a rejeição é conclusiva | Depende da contadora (QC-11) | NFCE-00 | |
| PP-33 | Meta de latência e de volume | Tempo máximo no caixa, vendas por minuto no pico, lojas no primeiro ano | Sem isso a fase de escala do Emissor é chute. Proposta de teto no caixa: 8 a 10 s, depois a venda segue pendente | NFCE-00, NFCE-02 | |
| PP-34 | **Arquivo de certificado no diretório do EmissorFiscal** | — | Ver a nota de segurança abaixo | — | |
| PP-35 | Lojista piloto | — | Não abrir a Fase 11 sem um lojista parceiro de balcão para teste em campo. Pela decisão da PP-05, o piloto não precisa ser MEI nem dispensado: basta o Proprietário ligar o modo "só cupom não fiscal" com o aceite registrado. Recomendação que continua valendo: orientar o lojista a consultar a contadora dele antes, e preferir quem tenha emissor próprio em paralelo durante o piloto. **A UF segue aberta** (PP-06) | Fase 11 | **18/11/2026 (John): a Fase 11 abre agora, sem lojista piloto.** Validação na Loja Demo (tenant de teste, perfil Comércio); com o piloto, ajusta-se antes de ir à loja real |
| PP-36 | Integrações de food com terceiros | iFood, 99Food e Keeta (Open Delivery), robô de WhatsApp, totem | Só com cliente de alimentação pedindo. Credenciamento e custo a levantar. Sem isso os itens 14, 18 e 20 do piso não fecham | DLV-04, DLV-05, PDV-13 | |
| PP-37 | PRD e `CLAUDE.md` | — | Atualizar os dois antes das stories: o produto deixa de ser só "ERP de indústria sob encomenda" | Todas | |
| PP-38 | Limite de desconto: onde mora | (a) por grupo de usuários (um campo no grupo, no Identity); (b) por loja (parâmetro do `Pos`), com o grupo dando só a permissão `pos.discount` | (b). O limite é regra de PDV e não deveria virar campo do Identity; a primeira versão do plano dizia as duas coisas em lugares diferentes. Se o lojista precisar de limites diferentes por papel, (b) aceita uma faixa por papel dentro da loja | PDV-04 | **Decidida em 15/11/2026: (b)** |
| PP-39 | PIN offline: o mesmo PIN do online ou um separado | (a) o mesmo PIN, com limites de valor e de quantidade para aprovações offline; (b) um código de aprovação offline separado para supervisor; (c) operador com o mesmo PIN e supervisor com código separado | (c). O PIN de 6 dígitos guardado no aparelho é quebrável com acesso físico; se for o mesmo do online, quebrar o de um supervisor rende uma credencial de aprovação que vale com rede. Com código separado, o estrago fica nos limites offline. Nas três opções valem os limites e a revisão na sincronização | OFF-03 | **Decidida em 15/11/2026: (c)** |
| PP-40 | Sentido de "CPD" | (a) central de distribuição que abastece as lojas; (b) retaguarda do varejo | — | Fase 12 | 15/11/2026 (John): **(b). CPD é o Centro de Processamento de Dados no sentido do varejo: a retaguarda**, onde se faz o controle geral (lançar as notas de entrada, precificar, cadastrar, entre outras). Não é centro de distribuição. O depósito central e a transferência entre lojas continuam como capacidade de multiloja (CPD-06) |
| PP-41 | Exceção à regra "construção depois do go-live" | (a) nenhuma; (b) só o EmissorFiscal; (c) também os spikes deste repositório | — | SPK-02, NFCE-00 | 15/11/2026 (John): **(b). O trabalho no EmissorFiscal pode começar antes do go-live da Konebras**, em outra frente. Para este repositório a regra continua: construção depois do go-live, SPK-01 e SPK-03 incluídos, salvo nova exceção. **16/11/2026 (John): nova exceção — a construção começa já, na `main`, com os módulos novos desligados por tenant; o go-live tem prioridade** |
| PP-42 | Acesso por loja | (a) quem tem o módulo vê todas as lojas, como hoje; (b) restringir usuário a lojas | Decidir com o piloto. O modelo de permissões é `modulo.acao`, sem escopo por loja; (b) pede ADR. Sem ela, um gerente vê as vendas e os caixas das outras lojas, o que enfraquece o item 27 do piso (multiloja) | PDV-07, CPD-05 | |

#### PP-07: o que foi aceito ao ficar na VPS atual

Decisão do PO de 15/11/2026: não migrar para o Brasil antes do piloto.

| | |
|---|---|
| **Consequências aceitas** | Latência maior na NFC-e online, no Pix e no painel do cliente (estimativa de 110 a 180 ms de ida e volta, não medida). Serviços públicos brasileiros que não respondem a IP de fora do país (**já aconteceu com o IBGE**). Dado pessoal fora do país, que pede base legal registrada na LGPD (PP-26). RPO de 24 h enquanto a INFRA-02 não reduzir |
| **Mitigações que valem sem migrar** | Tudo o que o caixa precisa roda local e offline: a venda, a impressão e o caixa não tocam o servidor. Tabelas públicas (municípios, NCM e afins) embutidas no sistema, em vez de consultadas em serviço externo. O SPK-02 mede, **a partir da VPS atual**, a latência da emissão e a eventual restrição de IP da SEFAZ. Backup mais frequente (INFRA-02) |
| **Gatilhos para reabrir a decisão** | (1) Alguma SEFAZ recusar IP estrangeiro. (2) Latência medida acima da meta (PP-33). (3) Primeiro cliente de volume. (4) Exigência de LGPD, de cliente, de contrato ou da autoridade |
| **Como os gatilhos são vigiados** | Conferidos no SPK-02 e no fechamento de cada fase, com registro nesta pendência |
| **Efeito no plano** | A INFRA-01 deixa de ser pré-condição do MVP e vira story condicionada. O risco RP-09 continua aberto, aceito pelo PO |

#### Nota de segurança (PP-34)

A análise do EmissorFiscal encontrou em `D:\GIT\EmissorFiscal\docs\Certificado\` um arquivo `.pfx` e um arquivo de texto com a senha, lado a lado, no diretório de trabalho.

- **Não estão versionados** (`git ls-files` vazio) e **não foram abertos** nesta análise.
- Mesmo fora do git, certificado e senha juntos numa pasta de projeto ficam ao alcance de backup, sincronização de nuvem, busca de arquivos e de qualquer ferramenta que leia o diretório. A regra do `CLAUDE.md` do Emissor pede que não fiquem no repositório.
- **Recomendação:** tirar os dois da pasta do projeto e guardar o certificado em cofre, com a senha em outro lugar; conferir se a pasta está no `.gitignore`; e, se houver dúvida sobre cópias, considerar revogar e reemitir o A1. A decisão é do PO.

#### Em aberto na PDV-04 (24/11/2026; PP-22 decidida pelo PO no mesmo dia)

| # | O que falta | Como ficou na PDV-04 | Quem decide |
|---|---|---|---|
| PP-22 (desconto) | **Decidida pelo PO em 24/11/2026:** o padrão de **0%** fica | Campo `pos.stores.discount_limit_percent` editável na tela da loja, com **0 como padrão** (todo desconto pede `pos.discount` ou o supervisor) | Decidido |
| PP-22 (alerta) | **Decidida pelo PO em 24/11/2026:** X = **3% das vendas do turno** | **Fora** da PDV-04: entra na **PDV-07** (indicador por caixa no turno). Até lá, o relatório de aprovações mostra os cancelamentos por operador, supervisor e tipo, sem o alerta | Decidido |
| Aprovação sem rede | A aprovação do supervisor sem conexão | Revisão de segurança, achado 2 (recomendação aceita pelo PO em 24/11/2026): ação que pede supervisor sem `approvalId` (autorizada por outra pessoa) vai para a **quarentena**, nunca só para a divergência; a liberação é do Proprietário. A aprovação sem rede fica para a **OFF-03** | OFF-03 |
| PP-38 (faixa por papel) | Limite diferente por papel dentro da loja (a PP-38 "aceita", não exige) | Fora: um limite por loja | PO, se o lojista pedir |
| PP-11 (motivos) | A lista de motivos de desconto, troca de preço e cancelamento | Lista fixa no caixa com "Outro" e texto (CA 1); vira configuração se o lojista pedir | PO |

#### Riscos aceitos na PDV-04 (revisão de segurança de 24/11/2026)

| Achado | Risco | Por que fica | Mitigação |
|---|---|---|---|
| 5 | O estorno de cartão no cancelamento é confirmado pelo próprio aparelho, e o NSU do estorno é texto livre (a maquininha externa não conversa com o sistema) | Sem TEF integrado não há como o servidor conferir o estorno | Cancelamento sempre com a aprovação de um supervisor diferente do operador, o NSU na trilha `pos.supervisor_approvals` e o aviso ao Proprietário (CA 9); a conciliação do cartão aponta o estorno que não houve |
| 8 | O relatório "Aprovações de supervisor" tem o escopo do tenant (quem tem `sales.view` vê todas as lojas), sem escopo por loja | A retaguarda inteira já é por tenant, sem recorte por loja; o recorte por loja seria uma regra nova só aqui | Filtro por loja na tela; o acesso continua pelas três camadas (`sales.view`) |

#### Decidido e em aberto na OFF-03 (24/11/2026)

| # | O que | Como ficou na OFF-03 | Quem decide |
|---|---|---|---|
| PP-22 (limites sem rede) | **Recomendação aceita (PO 24/11/2026)**, conservadora e editável por loja em Lojas e caixas | **R$ 50,00 por aprovação** sem rede (a diferença entre antes e depois: o desconto, o item ou a venda cancelados); **5 aprovações** e **R$ 150,00 somados por turno**; acima de **R$ 100,00** aprovados sem rede no turno, o fechamento vai para `PendingReview` (CX-02 CA 6); **cancelamento de venda sem rede só da venda paga toda em dinheiro** (pode ser desligado); **troca de preço só com rede** (rascunho A, item 11); zero no teto desliga a aprovação sem rede. Por quê: o código sem internet é quebrável com o aparelho na mão (RP-08); com esses tetos, o estrago máximo de um aparelho perdido num turno fica em R$ 150,00 e tudo é reconferido na subida | Decidido (recomendação aceita) |
| PP-10 (padrão) | Já decidido (18/11/2026): padrão e teto de 7 dias | "Tempo máximo sem internet" em horas (1 a 168) na loja; a janela conta do último horário do servidor, com piso contra relógio atrasado | Decidido |
| PP-39 | Já decidido (15/11/2026): operador com o mesmo PIN; supervisor com o código sem internet separado | O código é criado em Meu perfil → Segurança, depois do PIN; nunca vale com rede | Decidido |
| Abertura sem rede fora da janela | O servidor não recusa a abertura que chegou depois da janela (só o aparelho bloqueia) | Risco aceito até a revisão de segurança: o relógio do aparelho é a única prova do instante; a sequência e o hash da OFF-02 mostram a ordem | Revisão de segurança |
| PIN trocado com eventos na fila | Quem trocou o PIN com rede depois de entrar sem rede, antes de a fila subir, tem a entrada marcada e as vendas na quarentena | Fica assim: o servidor não guarda o PIN antigo para conferir a prova; o Proprietário libera em Pendências do PDV | PO, se incomodar no piloto |
| Quem recebe verificador e aprova sem rede | Igual à regra com rede (PDV-01/PDV-04, `PosStoreOperators` e lista de supervisores): usuário de operação só da loja dele; usuário normal ativo vale em qualquer loja da empresa | Mantido (revisão de segurança 24/11/2026, achado 5): espelha a regra com rede | PO, se quiser restringir usuário normal por loja |
| Bloqueio e contadores locais (risco aceito, achado 8) | O bloqueio por 5 erros e os contadores de aprovações do turno ficam no armazenamento do aparelho: quem controla o aparelho pode apagá-los | Risco aceito: os limites por aprovação, por turno e a prova são reconferidos no servidor na subida; o bloqueio é só freio local | Revisão de segurança |
| Marca `offline` e instante vindos do aparelho (risco aceito, achado 9) | O `"offline": true` e o `at` dos eventos são declarados pelo aparelho | Risco aceito: servem para relatório e conferência; a sequência e o hash da OFF-02, a tolerância de relógio e a prova assinada limitam o abuso | Revisão de segurança |

## 4. Perguntas para a contadora

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
| QC-13 | Cupom não fiscal em quem é obrigado a emitir: o que a empresa arrisca, se pode converter em NFC-e depois, e quem está dispensado. **Não trava o produto** (decisão da PP-05): é orientação que cada cliente deve buscar com a contadora dele; aqui serve para escrever o texto do aceite | Em parte | | | PP-05 |
| QC-14 | Conta dividida: uma NFC-e por venda com vários pagamentos, ou uma por pagante com CPF | Não | | | PP-20 |
| QC-15 | Devolução de venda feita em NFC-e: NF-e de devolução é o caminho? | Em parte | | | PDV-06, NFCE-03 |
| QC-16 | Filial pode usar o A1 da matriz (mesma raiz de CNPJ)? | Sim | | | PP-15 |
| QC-17 | Data de emissão × data da venda: numa venda feita sem internet e emitida depois, ou sincronizada no dia seguinte, qual data vai no documento fiscal e qual vale para a apuração? A arquitetura propõe "a data fiscal é a do servidor"; isso é proposta, não regra | Em parte | | | NFCE-02, NFCE-04, OFF-02 |

Do benchmark, a confirmar em fonte primária antes de virar requisito: a regra de contingência offline em SP e o prazo de 24 h (só fontes de fornecedores, com contradição); a data do Simples na reforma (Ato Conjunto RFB/CGIBS nº 4/2026); a data de descontinuação do MFE no Ceará.

Fora da contadora: perguntar a uma **Autoridade Certificadora** se existe certificado restrito a documento fiscal eletrônico e se um CNPJ pode ter mais de um A1 ativo (PP-01); e ao **jurídico**, qual a responsabilidade do fornecedor do software quando o cliente escolhe não emitir documento fiscal (PP-05, RP-17).

## 5. Riscos

| ID | Risco | Impacto | Mitigação |
|---|---|---|---|
| RP-01 | A NFC-e não existe no EmissorFiscal, que a declara fora de escopo | Bloqueia o PDV fiscal | NFCE-00 começa já no repositório do Emissor; o MVP não depende dela; desenvolvimento contra o falso do `IFiscalEmitter`, como nas stories fiscais; SPK-02 |
| RP-02 | Sem internet não há como assinar NFC-e na loja | Legal | Decidido na PP-01: assinatura na loja. Resta a UF aceitar `tpEmis=9` (QC-01); onde não aceitar, sobra o modo degradado e o 4G de reserva |
| RP-03 | Maquininha não integrada proibida em alguma UF | Legal e comercial | QC-03; antecipar o TEF nas UFs afetadas |
| RP-04 | Regras fiscais do varejo (Simples, ST, monofásico, IBS/CBS, exigência por UF) | Alto | Presets "A confirmar com a contadora" (ADR 0042); piloto em uma UF; diferenças por UF em tabela de configuração, não em `if` |
| RP-05 | Periféricos no Windows pelo navegador não funcionam de forma simples | Alto | SPK-01; ponte (IMP-03); lista curta de modelos homologados |
| RP-06 | Offline: duplicidade, perda de venda, relógio errado, conflito | Alto | Eventos imutáveis com `event_id` e cadeia de hash; teste de reenvio em paralelo; SPK-03; piloto em campo antes de vender |
| RP-07 | Vendas não sincronizadas perdidas com o aparelho | Alto | Sincronizar logo; contador visível; cópia do registro em disco pelo agente; alerta de fila antiga |
| RP-08 | PIN sem rede quebrável com acesso físico ao aparelho | Médio | Limites do que se faz sem rede; janela máxima (PP-10); revisão na sincronização |
| RP-09 | Hospedagem fora do Brasil: latência, SEFAZ e serviços públicos que recusam IP de fora, LGPD | Alto | **Risco aceito pelo PO em 15/11/2026 (PP-07).** Mitigações sem migrar e quatro gatilhos para reabrir; INFRA-01 pronta para executar |
| RP-10 | VPS única, compartilhada, com RPO de 24 h | Alto para o varejo | Backup mais frequente na VPS atual (INFRA-02), com a perda máxima medida; o PDV local-first guarda no aparelho o que ainda não subiu; migração condicionada (INFRA-01) |
| RP-11 | Disputa por numeração e custo médio sob volume de supermercado | Médio | Baixa agregada; número por caixa; teste de carga |
| RP-12 | Limites do Emissor não comportam supermercado (60 requisições por minuto por cliente; instância única; PDF por nota) | Médio | Lacunas 10, 31, 32 e 34; a meta sai da PP-33 |
| RP-13 | Homologação de TEF e SmartPOS demorada e cara | Prazo | Contato comercial já (PP-18); porta pronta desde o MVP; Pix dinâmico antes (PAG-02, Fase 12), por qualquer PSP |
| RP-14 | Atualização do app no meio do turno; versões antigas enviando eventos | Médio | Atualizar só na troca de turno; `schema_version` |
| RP-15 | Escopo: 6 módulos novos, 8 segmentos e 93 stories (seis delas épicos) com um PO | Alto | MVP estreito; fases 15 a 17 só com cliente real pedindo; cada segmento é seed + extensão, nunca um PDV novo |
| RP-16 | Regressão na Konebras, que está em produção | Alto | Perfil `factory` sem mudança; teste de regressão da navegação; migrations nunca destrutivas; mudanças no Estoque e no Financeiro só aditivas |
| RP-17 | Cupom não fiscal usado por empresa obrigada a emitir: autuação do cliente e **responsabilidade do fornecedor do software, a confirmar com o jurídico** | Médio a alto (jurídico e de imagem) | Decisão do PO na PP-05: aceite do Proprietário registrado em auditoria, "Não é documento fiscal" no comprovante, relatório de vendas sem documento fiscal, modo por loja. O risco não some com isso |
| RP-18 | Fraude interna no caixa | Médio | Controles do [PRD, seção 6.5](01-PRD.md#65-controles-antifraude); relatórios por operador desde o MVP |
| RP-19 | MVP abaixo do piso de mercado (sem NFC-e, TEF e SmartPOS) apresentado como produto pronto | Comercial e de imagem | Tratar o MVP como piloto (PP-16); só anunciar depois da Fase 13 |
| RP-20 | Itens do piso que dependem de terceiros (iFood, marketplaces, plataformas, WhatsApp) sem prazo controlável | Prazo | PP-21 e PP-36; preferir hub e padrão aberto (Open Delivery) |
| RP-21 | Suporte a hardware vira operação contínua; o lojista liga na hora do pico | Custo | Lista curta homologada; checklist de primeiros passos; ajuda contextual por tela; modo treinamento (PDV-11) |
| RP-22 | Sem piloto definido | Médio | PP-35 (decidida em 18/11/2026: validação na Loja Demo até haver piloto) |
| RP-23 | Estimativas de esforço não reconciliadas entre as análises | Prazo | SPK-03 antes de assumir data; ver [05-backlog.md](05-backlog.md#esforço) |
| RP-24 | Regras fiscais marcadas [F] vêm de memória e podem estar desatualizadas em 11/2026 | Alto | Nenhuma vira regra de código sem a contadora ou fonte oficial |
| RP-25 | Certificado e senha numa pasta de projeto do Emissor | Segurança | PP-34 |
| RP-26 | Certificado do CNPJ instalado na loja (PP-01, decidida): quem o usa assina qualquer documento da empresa | Alto | **Risco aceito pelo PO em 15/11/2026**, com as cinco proteções da PP-01; consultar a certificadora sobre certificado restrito; o cliente também precisa aceitar, na implantação |
| RP-27 | Dado de cartão digitado em campo livre (NSU, observação) ou passando pelo servidor na loja virtual | Alto | Regra 14 do PRD; validação dos campos livres; campos hospedados ou token do gateway (ECM-03) |
| RP-28 | O PO é o revisor de todas as frentes e o dono do EmissorFiscal ao mesmo tempo | Prazo | Cenários de prazo explícitos no [backlog](05-backlog.md#esforço); o Emissor começa antes do go-live (PP-41) |
| RP-29 | Pix conferido pelo operador aceito com comprovante falso, até o Pix dinâmico existir | Médio | Conferência contra o extrato no fechamento; relatório por operador; PAG-02 antecipada para a Fase 12 |

## 6. O que não foi verificado

- Nenhuma NFC-e foi montada, assinada ou transmitida. O suporte da biblioteca Unimake foi inferido dos nomes de símbolos na DLL.
- Latência e volume não foram medidos. A região da VPS foi assumida como informada.
- No benchmark, parte dos preços e das funcionalidades não pôde ser confirmada em fonte primária ([seção 6](00-benchmark-mercado.md#6-o-que-não-consegui-verificar)).
- Do lado do BemAqui não foram lidos por inteiro: `docs/04-fluxos-estados.md`, o PRD em detalhe, o `OutboxProcessor` e o adaptador `EmissorFiscalHttpEmitter.cs`.

## 7. Próximos passos sugeridos

1. O PO decide as pendências que continuam abertas na seção 2, começando pela PP-02.
2. Com o lojista piloto escolhido (PP-35), definir a UF (PP-06).
3. Levar QC-01 a QC-17 à contadora, começando por QC-01 e QC-12 (seguram a assinatura na loja), QC-03 e QC-17; consultar a certificadora (PP-01) e o jurídico (RP-17).
4. **Já pode começar** (PP-41): no EmissorFiscal, as duas correções da NF-e, o SPK-02 e a decisão da E14.
5. Sem código: comprar o kit de hardware (PP-19), contatar adquirentes (PP-18) e escolher o lojista piloto de balcão (PP-35).
6. Depois do go-live da Konebras: SPK-01 e SPK-03, a INFRA-02 e a Fase 10.
7. Com os spikes concluídos: fechar os rascunhos A a F como ADRs, escrever a ADR dos perfis de negócio e a de fronteiras, e integrar este plano aos documentos principais.
