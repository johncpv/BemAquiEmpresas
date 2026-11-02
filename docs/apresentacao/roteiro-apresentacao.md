# Roteiro de apresentação — "A vida de um elevador, do orçamento à margem real"

Duração: **35 a 40 minutos** (versão curta de 15 minutos no fim). Público: sócios e gestores da Konebras.
Ambiente: tenant `demo` montado conforme [`operacao-demo.md`](operacao-demo.md).

## A história

Um elevador de passageiros **KB-6** (6 pessoas, 5 paradas) vendido por **R$ 125.000,00** (R$ 105.000,00 do equipamento e R$ 20.000,00 da instalação), com margem orçada de **27,3%**. Um elevador leva cerca de 60 dias da assinatura ao aceite técnico, então o `demo` tem quatro pedidos iguais, cada um num momento da vida:

| Pedido | Cliente | Momento no dia da apresentação | Usado nos atos |
|---|---|---|---|
| Orçamento ORC Jardim das Acácias | Condomínio Residencial Jardim das Acácias | Orçamento **enviado**, esperando o "sim" | 1 e 2 |
| Projeto Vale Norte | Construtora Vale Norte Ltda | **Em produção**, OS na etapa Montagem | 2, 4 e 5 |
| Pedido de compra do Ato 3 | Fornecedor da nota usada (ver etapa 14 da operação demo) | Pedido **aprovado e enviado**, material chegou com a nota | 3 |
| Projeto Bela Vista | Residencial Bela Vista | **Instalando**, checklist da obra completo, esperando o aceite | 4 e 9 |

Frase para abrir a história:
> "Vamos seguir um elevador KB-6. Como ele leva 60 dias para ficar pronto, deixamos quatro pedidos iguais, cada um num momento da vida. É a mesma Konebras, o mesmo produto e o mesmo processo."

**Fio condutor, repita em todo ato:** cada ação de alguém da equipe vira custo, margem ou caixa na hora, e o dono controla quem pode fazer o quê.

## Como montar a sala

| Tela | O que mostra | Quem está logado |
|---|---|---|
| Notebook no projetor, janela 1 | Backoffice | **Proprietário** (João, dono) |
| Notebook, janela 2 (outro perfil do navegador) | Backoffice | **Financeiro** (Ana) |
| Notebook, janela 3 (outro perfil) | Backoffice | **Supervisor de fábrica** (Marcos) |
| Tablet | App do operador, em `/fab` | **Operador** (Carlos), entra por PIN |
| TV ou segundo monitor | Painel da TV, em `/tv` | Sem login (link de tela) |
| Celular de quem apresenta | Aplicativo autenticador (2FA) dos usuários de demonstração | — |

Os nomes são da operação demo. Troque pelos nomes reais da equipe se o cliente preferir.

Os passos de Comercial, Engenharia, Compras e Almoxarifado são feitos na janela do Proprietário (ele tem tudo), para não trocar de login a todo momento. Diga em voz alta quem faria aquilo na Konebras. Troque de janela só onde a segregação é o ponto: o pagamento (Financeiro), a qualidade (Supervisor) e o botão que o Comercial não tem.

## Tempo por ato

| Ato | Tema | Tempo |
|---|---|---|
| 0 | Abertura: a visão do dono | 3 min |
| 1 | Comercial: orçamento → contrato → projeto | 5 min |
| 2 | Engenharia e Produção: BOM, OS, Kanban, app do operador, qualidade, TV | 8 min |
| 3 | Compras e Estoque: da falta ao recebimento pela nota | 4 min |
| 4 | Fiscal: NF-e do equipamento e NFS-e da instalação | 4 min |
| 5 | Financeiro: receber, pagar com aprovação, conciliar, caixa, DRE | 6 min |
| 6 | Pessoas: ponto e folha | 3 min |
| 7 | Patrimônio: manutenção que mexe no prazo | 2 min |
| 8 | Controle gerencial: grupos, alçadas, histórico | 3 min |
| 9 | Fechamento: a margem real do projeto contra o orçamento | 2 min |
| | **Total** | **40 min** |

Para caber em 35 min, encurte o Ato 2 (pule a TV) e o Ato 6 (só o espelho de ponto).

---

## Ato 0 — Abertura: a visão do dono (3 min)

| | |
|---|---|
| **Quem age** | Proprietário |
| **Tela** | Dock → **Visão geral** → Painel do proprietário · `/app/painel` |
| **Controle mostrado** | O Proprietário cai direto no painel; cada card só aparece para quem tem acesso ao módulo de origem |

**O que clicar**
1. Entre como Proprietário (e-mail, senha e código do autenticador). O sistema abre no painel.
2. Mostre a faixa **Atenção**: o projeto que vai atrasar e as decisões que esperam você.
3. Passe pelos cards: **Saldo em contas**, **Caixa projetado, próximas 12 semanas** (cenário **Os dois**), **A receber vencido**, **Margem dos projetos**.
4. Clique no **?** de um card ("Como calculamos") e depois em **Ver origem** do caixa projetado: abre o fluxo de caixa.
5. Volte. Clique em **Início** (`/app`) e mostre a **Minha pauta**.

**O que falar**
> "Esta é a primeira tela do dono: quanto tem no banco, como fica o caixa nas próximas 12 semanas e quanto cada projeto está rendendo de verdade. Nenhum número aqui é digitado: ele vem do que a fábrica, o comercial e o financeiro fizeram hoje."

> "Na Minha pauta, cada pessoa vê só o que depende dela, na ordem do que destrava os outros."

**Momento uau:** o caixa em dois cenários. A linha **Ajustado** desloca cada recebimento pelo atraso médio do cliente: "o sistema já sabe quem paga atrasado".

Ajuda: [painel](../../Ajuda/painel.md) · [inicio](../../Ajuda/inicio.md)

---

## Ato 1 — Comercial: orçamento → contrato → projeto (5 min)

| | |
|---|---|
| **Quem age** | Comercial monta (já pronto), **Proprietário aprova** |
| **Telas** | Dock → **Comercial** → Orçamentos · `/app/orcamentos` · Funil `/app/orcamentos/funil` · Orçamento `/app/orcamentos/$id` · Contrato `/app/contratos/$id` · Projeto `/app/projetos/$id` |
| **Controle mostrado** | Custo e margem são sigilo (ação **Ver custo e margem**); o PDF nunca mostra custo; aprovar e gerar o contrato é do Proprietário |

**O que clicar**
1. Abra o **Funil** e mostre os orçamentos dos últimos 90 dias.
2. Abra o orçamento do **Jardim das Acácias** (situação Enviado).
3. Aba **Itens** → **Composição de custo** do elevador: material, mão de obra (horas por centro × custo-hora do cargo), instalação, frete, impostos. Mostre a margem.
4. Clique em **Simular prazo**: "Se aprovado hoje, a fabricação termina em … e a entrega fica em …", com **Cabe no prazo**.
5. Aba **Documentos**: abra o PDF. Mostre que não tem custo nem margem.
6. Clique em **Aprovar e gerar contrato**. Confira a data de assinatura e confirme.
7. Siga o próximo passo até o **contrato** ("Gerando parcelas no financeiro…") e depois até o **projeto**: etapa **P**, data prometida, entrega prevista e folga.
8. No projeto, mostre que a OS de fabricação já nasceu (link para as OS).

**O que falar**
> "O vendedor monta o custo uma vez, com o custo-hora real de cada cargo. O cliente recebe o PDF sem custo. Quando o cliente diz sim, um clique cria o contrato, as parcelas a receber de cada marco, o projeto e a ordem de fabricação."

> "A simulação de prazo usa a fila real da fábrica: o vendedor promete uma data que a fábrica consegue cumprir."

**Momento uau:** um clique e aparecem contrato, parcelas no financeiro, projeto e OS, sem digitar nada de novo.

**Controle gerencial:** abra a janela do Comercial (se estiver aberta) e mostre que ele não tem o botão **Aprovar e gerar contrato**. "Vender é do comercial; fechar o contrato é de quem você autorizar."

Ajuda: [comercial-funil](../../Ajuda/comercial-funil.md) · [comercial-orcamento](../../Ajuda/comercial-orcamento.md) · [comercial-orcamento-itens](../../Ajuda/comercial-orcamento-itens.md) · [comercial-orcamento-documentos](../../Ajuda/comercial-orcamento-documentos.md) · [comercial-contrato](../../Ajuda/comercial-contrato.md) · [projetos-projeto](../../Ajuda/projetos-projeto.md)

---

## Ato 2 — Engenharia e Produção (8 min)

| | |
|---|---|
| **Quem age** | Engenharia (BOM), Operador (tablet), Supervisor (qualidade e conclusão) |
| **Telas** | Dock → **Fábrica** → Engenharia → BOMs `/app/engenharia/boms` · OS `/app/producao/os/$id` (aba BOM) · Kanban `/app/producao/kanban` · App do operador `/fab` · Painel do supervisor `/app/producao/supervisor` · TV `/tv` |
| **Controle mostrado** | BOM só liberada pela Engenharia (2FA); o Kanban não arrasta (a etapa anda pelo apontamento, com histórico); quem fez o trabalho não aprova a qualidade dele |

**O que clicar**

*Parte A — a OS que acabou de nascer (Jardim das Acácias)*
1. Em **BOMs**, abra a OS do Jardim das Acácias → aba **BOM** → **Criar BOM** → **Copiar de outra OS** (a última OS do mesmo produto vem com a marca **Sugerido**).
2. Mostre a coluna **Necessidade da OS** e o chip **Falta** em um item.
3. **Liberar BOM**. O estoque reserva o material e a falta vira solicitação de compra sozinha (ela aparece no Ato 3).

*Parte B — a OS em produção (Vale Norte)*
4. Abra o **Kanban**: colunas pela letra da etapa (P, C, M, Q, E), chip **Ao vivo**, faixa vermelha (atrasada) e âmbar (em risco).
5. No **tablet**, o operador Carlos entra pelo PIN, abre a etapa **Montagem** da OS do Vale Norte (já iniciada), marca os itens do checklist e toca **Finalizar etapa** → confirma.
6. Mostre a **TV** e o **Kanban**: o cartão mudou de coluna em segundos, sem ninguém atualizar.
7. Na janela do **Supervisor**, abra o **Painel do supervisor**: bloco **Qualidade** → **Aprovar** → checklist → confirme.
8. A OS fica **Pronta para concluir**. Abra a OS → **Concluir OS**. Informe a série do elevador e confirme.
9. Mostre o projeto Vale Norte: passou a **F Fabricado**, e a parcela "fabricação concluída" ganhou vencimento.

**O que falar**
> "A engenharia monta a lista de materiais copiando a do último elevador igual. Ao liberar, o estoque reserva e o que falta vira pedido de compra sozinho."

> "O operador aponta no tablet com poucos toques. Cada hora apontada é custo real do projeto e mexe na previsão de entrega."

> "Quem montou não aprova a própria qualidade. E quando a última OS é concluída, o financeiro já sabe que pode cobrar a parcela da fabricação."

**Momento uau:** o operador finaliza no tablet e, em menos de 2 segundos, a TV e o Kanban mudam. Depois, a conclusão da OS libera a parcela no financeiro.

**Controle gerencial:** na fila de qualidade do próprio operador aparece "Aguardando qualidade", sem botão de aprovar. Se a mesma pessoa que apontou tentar aprovar sem outro aprovador, a decisão leva a marca **Autoinspeção** e o Proprietário é avisado.

Ajuda: [engenharia-boms](../../Ajuda/engenharia-boms.md) · [producao-os-bom](../../Ajuda/producao-os-bom.md) · [producao-kanban](../../Ajuda/producao-kanban.md) · [fab](../../Ajuda/fab.md) · [fab-etapa](../../Ajuda/fab-etapa.md) · [producao-supervisor](../../Ajuda/producao-supervisor.md) · [producao-qualidade](../../Ajuda/producao-qualidade.md) · [tv](../../Ajuda/tv.md)

---

## Ato 3 — Compras e Estoque: da falta ao recebimento pela nota (4 min)

| | |
|---|---|
| **Quem age** | Compras (solicitação e cotação), Proprietário ou Diretoria (aprova o pedido), Almoxarifado (recebe) |
| **Telas** | Dock → **Fábrica** → Compras → Solicitações `/app/compras/solicitacoes` · Cotação `/app/compras/cotacoes/$id` · Pedido `/app/compras/pedidos/$id` · Dock → **Financeiro** → Fiscal → Importar XML `/app/fiscal/entrada/importar` · Estoque → Saldos `/app/estoque/saldos` |
| **Controle mostrado** | Pedido aprovado por alçada (valor de referência soma 30 dias do fornecedor, contra fracionamento); quem pediu ou aprovou não registra o recebimento; XML conferido (assinatura, protocolo, destinatário) |

**O que clicar**
1. Em **Solicitações**, mostre a solicitação que nasceu da BOM liberada no Ato 2 (origem: falta da BOM).
2. Abra a cotação pronta → **Comparativo**: o chip **Menor custo** e o motivo da escolha.
3. Abra o pedido gerado (aprovado e enviado ao fornecedor): mostre a aba **Decisões** (quem aprovou e quando) e o impacto no caixa.
4. Em **Fiscal → Notas de entrada → Importar XML**, arraste o XML do fornecedor → **Importar XML**. (Sem XML: **Importar PDF** ou **Digitar a nota**, ver etapa 14 da operação demo.)
5. Abra a nota: ligue ao pedido e confira os itens → **Receber pela nota** → confirme. O diálogo mostra o custo médio antes e depois e um título a pagar por duplicata.
6. Siga **Conferir títulos a pagar**: os títulos estão lá, com o vencimento da nota.

**O que falar**
> "A falta de material vira solicitação sozinha. O pedido passa pela alçada que você definiu e já entra no caixa como previsão."

> "Quando o material chega, o almoxarife importa o XML da nota: estoque, custo médio e contas a pagar saem juntos, sem digitar."

**Momento uau:** um XML importado vira entrada no estoque e títulos a pagar com os vencimentos da nota.

Ajuda: [compras-solicitacoes](../../Ajuda/compras-solicitacoes.md) · [compras-cotacao-comparativo](../../Ajuda/compras-cotacao-comparativo.md) · [compras-pedido](../../Ajuda/compras-pedido.md) · [fiscal-entrada-importar](../../Ajuda/fiscal-entrada-importar.md) · [fiscal-nota-entrada-receber](../../Ajuda/fiscal-nota-entrada-receber.md)

---

## Ato 4 — Fiscal: NF-e do equipamento e NFS-e da instalação (4 min)

| | |
|---|---|
| **Quem age** | Supervisor ou Almoxarifado (romaneio), Financeiro (NF-e), Supervisor (aceite técnico), Proprietário (cancelamento) |
| **Telas** | Dock → **Fábrica** → Produção → Expedição `/app/producao/expedicao` · Novo romaneio `/app/producao/expedicao/novo` · Dock → **Financeiro** → Fiscal → saída `/app/fiscal/saida` · NF-e `/app/fiscal/saida/$id` · Instalação `/app/producao/instalacoes/$id` · NFS-e `/app/fiscal/saida/nfse/$id` |
| **Controle mostrado** | Imposto nunca é digitado (sai da regra fiscal); só o Proprietário cancela nota, com 2FA; quem não pode cancelar tem **Pedir cancelamento** |

**O que clicar**
1. Em **Expedição → Prontos para expedir**, o Vale Norte (fabricado no Ato 2) já aparece. **Novo romaneio** → confira série e endereço da obra → confirme.
2. O rascunho da NF-e nasce do romaneio. Abra em **Fiscal → saída** → **Pré-visualizar** (DANFE com "Prévia, sem valor fiscal") → **Emitir NF-e** (ambiente de homologação).
3. Abra a **OS de instalação do Bela Vista**: checklist completo. **Registrar aceite técnico** → data e quem aceitou pelo cliente.
4. A NFS-e nasce preenchida. Mostre a área **Para copiar no portal da prefeitura** e explique o registro.

**O que falar**
> "A nota sai do romaneio, com o imposto pela regra que a contadora conferiu. Ninguém digita imposto."

> "No aceite técnico da obra, três coisas acontecem juntas: a parcela final do cliente é liberada, o pagamento do instalador é confirmado e a nota de serviço fica pronta."

**Momento uau:** o aceite técnico libera a parcela do cliente, confirma o título do instalador e prepara a NFS-e, num clique.

**Controle gerencial:** na janela do Financeiro, abra uma nota autorizada: aparece "Só o Proprietário cancela notas fiscais." e o botão **Pedir cancelamento**.

Ajuda: [producao-expedicao](../../Ajuda/producao-expedicao.md) · [fiscal-nota-saida](../../Ajuda/fiscal-nota-saida.md) · [producao-instalacao](../../Ajuda/producao-instalacao.md) · [fiscal-nfse](../../Ajuda/fiscal-nfse.md)

---

## Ato 5 — Financeiro: receber, pagar com aprovação, conciliar, caixa e DRE (6 min)

| | |
|---|---|
| **Quem age** | Financeiro (cobra, monta a ordem, paga, importa o extrato), Proprietário (aprova e concilia) |
| **Telas** | Dock → **Financeiro** → Caixa e pagamentos → A receber `/app/financeiro/receber` · Título `/app/financeiro/receber/$id` · Ordens de pagamento `/app/financeiro/ordens-de-pagamento` · Conciliação `/app/financeiro/conciliacao` · Fluxo de caixa `/app/financeiro/fluxo-de-caixa` · DRE `/app/financeiro/dre` · Inadimplência `/app/financeiro/inadimplencia` |
| **Controle mostrado** | Nenhum dinheiro sai sem ordem aprovada; quem pede não aprova; quem aprova não paga; 2FA para aprovar e pagar; quem importou o extrato não concilia |

**O que clicar**
1. **A receber**: abra a parcela "fabricação concluída" do Vale Norte (liberada no Ato 2) → aba **Boleto** → **Emitir boleto** (sandbox do Inter): linha digitável, Pix copia-e-cola e QR Code. Mostre a aba **Lembretes** (régua de cobrança).
2. **Ordens de pagamento**, filtro **Aguardando você**: abra a ordem do fornecedor de cabos. Mostre os avisos em âmbar (dados de pagamento mudaram há pouco, por exemplo) → **Aprovar** → código do autenticador.
3. Na janela do **Financeiro**: a mesma ordem, agora **Pagar** → conta e data → confirme com o autenticador.
4. **Conciliação** (na janela do Proprietário): o extrato OFX que o Financeiro importou aparece em pares, com a confiança → **Aceitar todas de confiança alta**. Uma tarifa sem par → **Lançar sem OP**.
5. **Fluxo de caixa**: cenário **Os dois**, o menor saldo previsto e a "diferença causada por atrasos".
6. **DRE**: clique numa célula (por exemplo, 2.1 Materiais do mês) → os documentos que somam o valor.

**O que falar**
> "Toda saída de dinheiro passa por uma ordem de pagamento. Quem monta não aprova, quem aprova não paga, e os dois confirmam com o código do celular."

> "O extrato do banco entra e o sistema sugere o par de cada linha. O que saiu do banco sem registro aparece na hora."

> "O DRE gerencial fecha o mês sem esperar a contadora, e cada número abre os documentos de onde veio."

**Momento uau:** a mesma ordem passa por duas pessoas, em duas janelas, com o código do celular, e o pagamento já aparece no extrato para conciliar.

Ajuda: [financeiro-receber-titulo-boleto](../../Ajuda/financeiro-receber-titulo-boleto.md) · [financeiro-ordens-de-pagamento](../../Ajuda/financeiro-ordens-de-pagamento.md) · [financeiro-ordem-de-pagamento](../../Ajuda/financeiro-ordem-de-pagamento.md) · [financeiro-conciliacao](../../Ajuda/financeiro-conciliacao.md) · [financeiro-fluxo-de-caixa](../../Ajuda/financeiro-fluxo-de-caixa.md) · [financeiro-dre](../../Ajuda/financeiro-dre.md)

---

## Ato 6 — Pessoas: ponto e folha (3 min)

| | |
|---|---|
| **Quem age** | Funcionário (marca o ponto), RH (espelho, ajustes, folha), Proprietário (conferência paralela) |
| **Telas** | Tablet `/fab/ponto` · Dock → **Pessoas** → RH → Espelho `/app/rh/ponto/espelho` · Ajustes `/app/rh/ponto/ajustes` · Folha `/app/rh/folha` · Conferência da folha `/app/rh/conferencia-folha` · Exportar para a contadora `/app/rh/exportar-contadora` |
| **Controle mostrado** | LGPD: salário e dados de saúde só para quem tem **Ver dados sensíveis** e **Ver dados de saúde**; ninguém aprova ajuste do próprio ponto; quem fechou a folha não paga os salários |

**O que clicar**
1. No tablet, Carlos toca **Meu ponto** → marca a saída para o intervalo. Mostre o comprovante "Comprovante de controle interno".
2. **Espelho de ponto**: extras, faltas e banco de horas de cada um.
3. **Ajustes**: o pedido de marcação esquecida, com o dia antes e depois → **Aprovar ajuste**.
4. **Folha** de outubro: fechada, com os títulos de salário e guias e a ordem de pagamento dos salários.
5. **Conferência da folha**: a folha calculada pelo sistema lado a lado com a da contadora, com diferença zero como critério.

**O que falar**
> "O ponto do tablet alimenta o espelho, o banco de horas e a folha. A folha fechada vira contas a pagar e entra no caixa, sem planilha."

> "Antes de o sistema calcular a folha sozinho, ele roda três meses em paralelo com a contadora, centavo por centavo."

**Diga com clareza:** "O cálculo de férias, 13º e rescisão está em finalização. Até lá, esses valores vêm da contadora e o sistema confere, gera holerite e paga."

Ajuda: [fab-ponto](../../Ajuda/fab-ponto.md) · [rh-ponto-espelho](../../Ajuda/rh-ponto-espelho.md) · [rh-ponto-ajustes](../../Ajuda/rh-ponto-ajustes.md) · [rh-folha](../../Ajuda/rh-folha.md) · [rh-conferencia-folha](../../Ajuda/rh-conferencia-folha.md) · [rh-exportar-contadora](../../Ajuda/rh-exportar-contadora.md)

---

## Ato 7 — Patrimônio: manutenção que mexe no prazo (2 min)

| | |
|---|---|
| **Quem age** | Supervisor de fábrica |
| **Telas** | Dock → **Fábrica** → Patrimônio → Manutenção `/app/patrimonio/manutencao` · Bem `/app/patrimonio/bens/$id` · Tablet: **Ler etiqueta** em `/fab` |
| **Controle mostrado** | Custo da manutenção com título a pagar do prestador; exclusão de bem só pelo Proprietário |

**O que clicar**
1. No tablet (login completo do supervisor), **Ler etiqueta** da dobradeira: abre a ficha do bem.
2. No backoffice, **Nova OS de manutenção** → corretiva, marque **Máquina parada** → **Abrir OS de manutenção**.
3. Abra **Planejamento** ou o projeto Vale Norte: a capacidade do centro caiu e a previsão foi recalculada.

**O que falar**
> "Máquina parada não é só um aviso: ela tira horas da capacidade e o sistema refaz a previsão de entrega de todos os projetos que passam por ela."

> "O custo da manutenção, com o prestador e as peças, fica na ficha da máquina."

Ajuda: [patrimonio-manutencao](../../Ajuda/patrimonio-manutencao.md) · [patrimonio-manutencao-os-custo](../../Ajuda/patrimonio-manutencao-os-custo.md) · [fab-bem-etiqueta](../../Ajuda/fab-bem-etiqueta.md)

---

## Ato 8 — Controle gerencial (3 min)

Use também o slide de [`grupos-e-permissoes.md`](grupos-e-permissoes.md).

| | |
|---|---|
| **Quem age** | Proprietário |
| **Telas** | Dock → **Sistema** → Usuários e acessos → Grupos `/app/acessos/grupos` · Usuários `/app/acessos/usuarios` · Dock → **Financeiro** → Alçadas `/app/financeiro/alcadas` · aba **Histórico** de qualquer documento |
| **Controle mostrado** | As 3 camadas de acesso; delegação ("ninguém concede o que não tem"); alçadas; 2FA; auditoria |

**O que clicar**
1. **Grupos**: abra o grupo **Financeiro**. Mostre a matriz por módulo (Habilitado, Ver, Criar, Editar, Aprovar e as ações específicas). Passe o mouse em **Aprovar** em Caixa e pagamentos.
2. Abra o grupo **RH**: **Ver dados sensíveis** e **Ver dados de saúde**.
3. **Alçadas**: as faixas de valor e o **simulador** ("quem aprovaria uma ordem de R$ 35.000,00?").
4. Abra a aba **Histórico** da ordem paga no Ato 5: quem pediu, quem aprovou, quem pagou, quando e o antes e depois.
5. Opcional (precisa da aba da plataforma aberta, com o IP liberado): em `/plataforma/tenants/demo` → **Módulos**, desligue o Patrimônio e salve. Recarregue o backoffice: o Patrimônio aparece com **cadeado** no dock e abre a tela **"Funcionalidade não liberada nessa versão do sistema."** Ligue de novo e salve. Confira no ensaio quanto tempo a mudança leva para aparecer. Sem a plataforma, mostre a tela pela ajuda ([modulo-nao-liberado](../../Ajuda/modulo-nao-liberado.md)) e explique.

**O que falar**
> "São três portas. A primeira é o que a Konebras contratou. A segunda é se o grupo da pessoa usa aquele módulo. A terceira é o que ela pode fazer lá dentro: ver, criar, editar, aprovar, excluir."

> "Aprovar, pagar, ver custo e ver salário só o grupo Proprietário concede. Ninguém dá a outro uma permissão que não tem."

> "Tudo o que muda fica registrado: quem, quando, e o antes e o depois."

---

## Ato 9 — Fechamento: a margem real contra o orçamento (2 min)

| | |
|---|---|
| **Quem age** | Proprietário |
| **Telas** | Dock → **Comercial** → Projetos → Orçado × realizado `/app/projetos/margens` · Projeto Bela Vista, aba **Custos** `/app/projetos/$id` · Resultado por projeto `/app/painel/resultado-por-projeto/$id` · Painel `/app/painel` |

**O que clicar**
1. Em **Orçado × realizado**, encontre o **Bela Vista** (entregue no Ato 4): margem orçada → projetada e o desvio em pontos percentuais.
2. Abra a aba **Custos** do projeto: a natureza que mais desviou (instalação, pelo valor combinado com o instalador) e a mão de obra (24 h a mais na Montagem). Clique no **Realizado** da mão de obra e mostre os apontamentos que somam o valor.
3. Volte ao **Painel**: os números mudaram com o que foi feito na apresentação (parcela liberada, pagamento feito, entrega registrada).

**O que falar**
> "Este é o mesmo elevador KB-6 do orçamento. Orçamos 27,3% de margem; o instalador cobrou mais, a montagem levou mais horas e a margem real ficou perto de 23%. Você sabe disso hoje, projeto por projeto, e não no fim do ano."

Use os números reais anotados no ensaio (o imposto vem das notas e pode mudar a margem em alguns décimos).

> "Tudo o que vimos, o orçamento, as horas do operador, a nota do fornecedor, a manutenção, virou custo, margem e caixa sem ninguém digitar duas vezes."

**Frase final:**
> "O BemAqui não é um sistema para preencher. É o painel de controle da Konebras, alimentado pelo trabalho de cada um."

---

## Versão curta (15 min)

| Min | Ato | O que mostrar |
|---|---|---|
| 0–2 | 0 | Painel do proprietário: caixa projetado em dois cenários e margem dos projetos |
| 2–5 | 1 | Aprovar e gerar contrato do Jardim das Acácias: contrato, parcelas, projeto e OS num clique |
| 5–8 | 2 | Tablet finaliza a Montagem do Vale Norte; Kanban e TV mudam ao vivo; supervisor aprova a qualidade |
| 8–11 | 5 | Ordem de pagamento: Proprietário aprova com 2FA, Financeiro paga na outra janela |
| 11–13 | 8 | Grupos e alçadas: as 3 portas, em uma tela |
| 13–15 | 9 | Orçado × realizado do Bela Vista: margem orçada contra a real |

---

## Perguntas prováveis do cliente

**Os dados do Dolibarr vêm para o sistema?**
Os cadastros sim: clientes, fornecedores, contatos, condições de pagamento, contas bancárias, itens, categorias, unidades e locais. A carga é feita pela equipe técnica e pode rodar várias vezes sem duplicar. Os títulos em aberto e o saldo das contas entram pela Implantação financeira, na data de corte. Orçamentos, pedidos e histórico antigo ficam para consulta no Dolibarr até o desligamento.

**E se a internet cair na fábrica?**
O app do operador e o ponto precisam de rede (o ponto usa a hora do servidor). Sem rede, a tela avisa e nada é gravado pela metade. A TV mantém os dados na tela e reconecta sozinha.

**O operador precisa de e-mail e senha?**
Não no dia a dia. No tablet registrado da fábrica, ele toca no nome e digita um PIN de 6 dígitos. Quem aprova, paga ou vê salário entra sempre com e-mail, senha e código do celular.

**Quem vê o salário das pessoas?**
Só quem tem a permissão **Ver dados sensíveis** do RH. Atestado e dado de saúde, só com **Ver dados de saúde**. Os outros veem "Dados protegidos". O custo-hora usado nos orçamentos vem do cargo, nunca do salário de uma pessoa.

**O vendedor vê a margem?**
Só se o grupo dele tiver **Ver custo e margem**. O PDF do orçamento nunca mostra custo nem margem, para ninguém.

**Posso aprovar pagamentos pelo celular?**
Sim. O sistema funciona no navegador do celular, e a ordem de pagamento também pode ser aprovada pelo link do e-mail, com o código do autenticador.

**Dá para limitar quem aprova por valor?**
Sim, em **Alçadas**: por faixa de valor, por conta e em até três níveis, para ordens de pagamento e pedidos de compra. O sistema soma os pedidos do mesmo fornecedor nos últimos 30 dias para impedir o fracionamento.

**A nota fiscal sai pelo sistema?**
A NF-e do equipamento sai pelo sistema, a partir do romaneio, pelo emissor fiscal integrado. A NFS-e da instalação nasce preenchida no aceite técnico; enquanto a prefeitura de Caçapava não está no emissor, ela é emitida no portal da prefeitura e registrada no sistema.

**O boleto é registrado no banco?**
Sim, pelo Banco Inter, com Pix no mesmo boleto. O pagamento baixa o título sozinho pelo aviso do banco, e o extrato entra para conciliação.

**O sistema calcula a folha?**
A folha mensal já é calculada e roda em paralelo com a contadora por três meses, com diferença zero, antes de virar a oficial. Férias, 13º e rescisão calculados estão em finalização; até lá, esses valores vêm da contadora.

**O ponto vale como ponto oficial?**
Não. É controle interno de jornada e não substitui o registro legal (REP-P, Portaria 671). Ele alimenta o espelho, o banco de horas e a folha.

**O cliente final acompanha a obra?**
Na próxima fase, depois do go-live, pelo Portal do cliente: projeto, etapa, documentos, boletos e aceite online do orçamento. O aviso por WhatsApp também entra nessa fase.

**E se alguém errar um lançamento?**
Documento com movimento (estoque, financeiro, fiscal) nunca é apagado: é cancelado ou estornado, com motivo, e fica no histórico. Cadastros são inativados, não apagados.

**Onde fica a ajuda?**
Em toda tela, no botão **Ajuda** da barra de cima. Ela abre sozinha na primeira visita. O **Pergunte ao BemAqui** responde com os textos da ajuda e mostra a fonte.

**Os meus dados ficam separados das outras empresas?**
Sim. Cada empresa tem o próprio banco de dados, e cada acesso passa pelas três camadas de permissão.

**Quanto tempo leva para começar?**
O roteiro de virada prevê: carga dos cadastros do Dolibarr, data de corte do financeiro, saldo inicial do estoque, grupos e usuários, e um ensaio completo antes do dia da virada.
