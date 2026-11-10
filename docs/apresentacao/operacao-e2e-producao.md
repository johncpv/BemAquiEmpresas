# Operação de ponta a ponta na produção (DEMO) — roteiro por papel

Roteiro para simular, na empresa real (`app.konebras.com.br`), a vida inteira de um pedido de elevador, do orçamento à margem real, com **um usuário por papel**. O dono faz o próprio papel; os outros papéis são personagens que você assume, um de cada vez. Em cada passo há um 📸: é o print que vai para a apresentação.

O fio da história é o mesmo da apresentação ([`roteiro-apresentacao.md`](roteiro-apresentacao.md)): **cada pessoa faz só a sua parte, e cada parte alimenta custo, margem e caixa com dado real.** Os valores são os da [`operacao-demo.md`](operacao-demo.md) e fecham entre si.

## Antes de tudo: regras para não sujar a Konebras real

A simulação roda na produção (decisão do PO de 10/11/2026). Documento com movimento não se apaga, só se cancela ou estorna. Por isso:

1. **Tudo o que for criado leva "DEMO"** no nome ou no código: clientes, fornecedores, itens, funcionários, usuários, o projeto. Assim a limpeza acha tudo por busca.
2. **Dinheiro só na conta "DEMO — Conta simulação"** (criada no passo 0.4). Nunca baixe nada na conta do Banco Inter real.
3. **Centro de custo "DEMO"** em todo título, para separar no DRE e no fluxo de caixa.
4. **Estoque só no local "DEMO"**.
5. **NF-e só em homologação** (é o que a Konebras tem liberado hoje): sai sem valor fiscal. Não libere a produção fiscal durante a simulação.
6. **Fica de fora** (mostre a tela e explique, sem gravar):
   - **Folha de pagamento:** cria a folha real da competência.
   - **Boleto do Inter:** a integração com o Inter ainda não está configurada.
   - **NFS-e da instalação:** o número é de nota real da prefeitura.
   - **Importação de XML de compra:** precisa de nota real destinada ao CNPJ da Konebras.
7. **Se o sistema recusar alguma ação** por causa da implantação financeira ou de estoque, que ainda não foram concluídas na Konebras, **pare e me chame**. Não conclua nenhuma implantação com números de simulação.
8. **No fim, faça a limpeza** (seção final), na ordem.

A numeração (ORC-2026-…, OS, pedidos) consumida pela simulação não volta. Os números reais continuam a partir dela.

## Os personagens

Use **um perfil do Chrome (ou do Edge) por personagem**: cada perfil guarda a sua sessão, e você troca de papel trocando de janela. Uma aba anônima não serve, porque compartilha a sessão entre abas.

Os e-mails usam o "+" do Gmail: tudo chega na sua caixa, mas o sistema vê um usuário por endereço.

| Personagem | Papel na história | Grupo(s) no sistema | E-mail do convite | 2FA |
|---|---|---|---|---|
| **John** (você) | Dono: aprova orçamento, compra e pagamento; acompanha margem e caixa | Proprietário | o seu | sim |
| **Rafael DEMO** | Comercial: monta e envia o orçamento | Comercial | `johncpv+comercial@gmail.com` | não |
| **Paulo DEMO** | Engenharia: roteiro, BOM, liberação de material, OS de instalação | Engenharia | `johncpv+engenharia@gmail.com` | sim (libera BOM) |
| **Marcos DEMO** | Supervisor de fábrica: planeja, acompanha, decide a qualidade, conclui a OS, romaneio e instalação | Supervisão de produção | `johncpv+supervisor@gmail.com` | sim (aprova) |
| **Bruno DEMO** | Almoxarifado: itens, saldo, requisição, recebimento e expedição | Almoxarifado | `johncpv+almox@gmail.com` | não |
| **Sílvia DEMO** | Compras: solicitação, cotação e pedido | **Compras** (criar, passo 0.1) | `johncpv+compras@gmail.com` | não |
| **Ana DEMO** | Financeiro: títulos, recebimentos, ordem de pagamento, pagamento, NF-e e conciliação | Financeiro | `johncpv+financeiro@gmail.com` | sim (paga) |
| **Lúcia DEMO** | RH: funcionários, jornada e ajuste de ponto | RH | `johncpv+rh@gmail.com` | sim |
| **Carlos DEMO** e **Diego DEMO** | Operadores: apontam as etapas e batem o ponto no tablet, por PIN | Operadores | `johncpv+carlos@gmail.com`, `johncpv+diego@gmail.com` | não (PIN) |

Os autenticadores de 2FA podem ficar todos no seu celular, com o nome do personagem em cada conta do aplicativo.

**Regras que a simulação vai mostrar** (bons slides):
- O Comercial monta o orçamento, mas **só o dono aprova** e gera o contrato.
- A Ana cria a ordem de pagamento, mas **não aprova**: quem aprova é você, com 2FA. Quem aprova não paga.
- O Marcos **não aprova a qualidade** de uma etapa em que ele mesmo apontou horas.
- Quem importa o extrato não concilia.
- O vendedor vê preço e margem, mas não vê o caixa.

---

## Ato 0 — Preparação (John, depois cada papel)

### 0.1 Grupo Compras (John)
`/app/acessos/grupos` → **Novo grupo** "Compras": Compras (ver, criar, editar), Cadastros (ver, criar, editar), Projetos (ver), Estoque (ver), Produção (ver). Aprovar pedido fica com você.
📸 A matriz do grupo com as ações marcadas: é o slide do "controle gerencial".

### 0.2 Usuários (John)
`/app/acessos/usuarios` → **Convidar** cada personagem da tabela, com o grupo dele. Cada um aceita o convite no próprio perfil do navegador (`/convite`) e, quem tem 2FA, configura o autenticador.
📸 A lista de usuários com os grupos.

> Se o convite não chegar no Gmail, a conta do MailerSend pode estar ainda no modo sandbox. Me avise.

### 0.3 Centro de custo DEMO (John)
`/app/financeiro/centros-de-custo` → **Novo**: "DEMO — Simulação".

### 0.4 Conta DEMO (Ana)
`/app/financeiro/contas` → **Nova conta**: "DEMO — Conta simulação", tipo caixa, saldo inicial R$ 0,00.

### 0.5 Terceiros (Rafael cria o cliente; Sílvia cria os fornecedores; Ana confere os dados de pagamento)
`/app/cadastros/terceiros/novo`. CNPJs fictícios com dígito válido. **Não use a consulta de CNPJ**: digite tudo à mão.

| Nome | Tipo | CNPJ | Cidade |
|---|---|---|---|
| DEMO Condomínio Residencial Bela Vista | Cliente | 33.444.555/0001-81 | Caçapava/SP |
| DEMO Tração Motores Industriais | Fornecedor | 66.777.888/0001-81 | Guarulhos/SP |
| DEMO Aço Forte Distribuidora | Fornecedor | 55.666.777/0001-81 | São Paulo/SP |
| DEMO Instala Vale Montagens | Instalador | 99.000.111/0001-65 | Caçapava/SP |

📸 A ficha do cliente com o endereço da obra.

### 0.6 Local e itens (Bruno)
1. `/app/estoque/locais` → **Novo local** "DEMO".
2. `/app/estoque/itens/novo`. Cadastre o primeiro item e use **Duplicar item** nos outros:

| Código | Item | Tipo | Unid. | Custo | Saldo no local DEMO |
|---|---|---|---|---|---|
| DEMO-MAQ6 | DEMO Máquina de tração 6 pessoas | Componente | UN | R$ 18.500,00 | **0** (vai faltar de propósito) |
| DEMO-QDC | DEMO Quadro de comando | Componente | UN | R$ 9.800,00 | 1 |
| DEMO-CAB6 | DEMO Cabina inox 6 pessoas | Componente | UN | R$ 14.200,00 | 1 |
| DEMO-PPAV | DEMO Porta de pavimento | Componente | UN | R$ 1.600,00 | 5 |
| DEMO-GT90 | DEMO Guia T 90 mm | Matéria-prima | M | R$ 95,00 | 40 |
| DEMO-CABO8 | DEMO Cabo de aço 8 mm | Componente | M | R$ 22,00 | 120 |
| DEMO-KB6 | DEMO Elevador KB-6 (6 pessoas) | Produto acabado (série) | UN | — | 0 |

3. Saldo: `/app/estoque/saldos`, pelo lançamento de entrada no local DEMO.
   - **Atenção:** a implantação do estoque da Konebras ainda não foi concluída. Se o sistema só oferecer "Saldo inicial", **pare e me chame antes de lançar**. Saldo inicial de simulação não pode entrar na implantação real.

📸 Saldos filtrados pelo local DEMO.

### 0.7 Roteiro (Paulo)
`/app/engenharia/roteiros` → **Duplicar** o "Roteiro padrão (exemplo)" → nome "DEMO Elevador KB-6", produto DEMO-KB6. Quatro etapas, nos centros de trabalho da Konebras:

| Letra | Etapa | Centro | Horas padrão | Checklist |
|---|---|---|---|---|
| P | Projeto | Desenhos e Projetos | 16 | — |
| C | Corte | Setor 1 - Corte | 24 | conferir medidas, rebarbar |
| M | Montagem | Setor 2 - Montagem | 80 | fixar guias, montar cabina, passar cabos, ligar quadro, teste em vazio |
| Q | Qualidade | Setor 3 - Inspeção e Qualidade | 6 | etapa de qualidade: nivelamento, freio, portas, documentação |

📸 O roteiro com as letras das etapas.

### 0.8 Funcionários dos operadores (Lúcia)
`/app/rh/funcionarios/novo`: **Carlos DEMO** (CPF 222.555.888-46) e **Diego DEMO** (CPF 333.666.999-57), cargo Montador, jornada padrão, cada um ligado ao seu usuário. **Não inclua os dois em nenhuma folha.**
📸 A ficha do funcionário mostrando o usuário ligado; os dados sensíveis aparecem mascarados.

### 0.9 Tablet e TV (Marcos)
1. **Tablet** (ou um celular fazendo o papel dele): abra `/fab`, entre como Marcos → **Aparelho** → **Registrar este aparelho**. Depois Carlos e Diego entram com e-mail e senha e criam o **PIN**.
2. **TV:** `/app/painel-tv` → nova tela "DEMO TV da Fábrica" → abra o link na TV ou num segundo monitor.

📸 A tela de PIN do tablet; a TV em "Ao vivo".

---

## Ato 1 — Comercial: do orçamento ao contrato

| # | Quem | Onde | Faça | Confira | 📸 |
|---|---|---|---|---|---|
| 1.1 | Rafael | `/app/orcamentos` → **Novo** | Cliente DEMO Bela Vista; item 1 "DEMO Elevador KB-6, 5 paradas", equipamento, produto DEMO-KB6, **R$ 105.000,00**, com composição de custo (material R$ 58.400,00; mão de obra pelas horas do roteiro; frete R$ 2.200,00); item 2 "Instalação", serviço, **R$ 20.000,00** (instalação R$ 16.000,00) | Total R$ 125.000,00; margem orçada perto de 27% | Itens com a margem |
| 1.2 | Rafael | aba **Condições** | Prazo de entrega 75 dias; marcos 30% na assinatura · 40% fabricação concluída + 10 dias · 20% entrega + 10 dias · 10% aceite técnico + 15 dias; centro de custo DEMO | Os 4 marcos somam 100% | Os marcos |
| 1.3 | Rafael | cabeçalho | **Baixar PDF** → **Enviar** ao cliente (e-mail para você mesmo) | Situação **Enviado** | O PDF do orçamento |
| 1.4 | Rafael | — | Repare: não há botão de aprovar | O vendedor não aprova | A tela sem o botão |
| 1.5 | John | o mesmo orçamento | **Aprovar e gerar contrato**, assinatura hoje | Contrato ativo, projeto na etapa **P**, OS de fabricação, parcelas a receber | O contrato gerado; o projeto no indicador de etapa |

## Ato 2 — Financeiro: o sinal

| # | Quem | Onde | Faça | Confira | 📸 |
|---|---|---|---|---|---|
| 2.1 | Ana | `/app/financeiro/receber` | Abra a parcela de 30% (R$ 37.500,00) → **Baixar** na conta **DEMO — Conta simulação** | Parcela recebida; saldo da conta DEMO sobe | A baixa |
| 2.2 | John | `/app/painel` | Veja o caixa projetado já com as próximas parcelas | — | O painel |

## Ato 3 — Engenharia: material da obra

| # | Quem | Onde | Faça | Confira | 📸 |
|---|---|---|---|---|---|
| 3.1 | Paulo | `/app/producao/os` → OS do DEMO Bela Vista → aba **BOM** | Crie a BOM: 1 máquina, 1 quadro, 1 cabina, 5 portas, 40 m de guia, 120 m de cabo (local DEMO) → **Liberar BOM** (2FA) | Material reservado; a máquina aparece com o chip **Falta** | A BOM com o chip Falta |
| 3.2 | Paulo | `/app/estoque/faltas` | Veja a falta da máquina | **A solicitação de compra nasceu sozinha** | A solicitação gerada |

## Ato 4 — Compras: cotar, aprovar e receber

| # | Quem | Onde | Faça | Confira | 📸 |
|---|---|---|---|---|---|
| 4.1 | Sílvia | `/app/compras/solicitacoes` | Abra a solicitação da máquina → **Nova cotação** com DEMO Tração Motores e DEMO Aço Forte | Cotação aberta | — |
| 4.2 | Sílvia | `/app/compras/cotacoes/$id` | **Baixar pedido de cotação** de cada fornecedor; lance as respostas: Tração R$ 18.200,00 em 10 dias; Aço Forte R$ 17.900,00 em 25 dias | Mapa comparativo com "Menor custo" | O PDF do mapa comparativo |
| 4.3 | Sílvia | mesma tela | Escolha a Tração (prazo) com o motivo → concluir → **Gerar pedidos de compra** → **Enviar para aprovação** | Pedido **Aguardando aprovação**; o PDF sai com "Rascunho" | O PDF do pedido em rascunho |
| 4.4 | John | `/app/compras/pedidos/$id` | **Aprovar pedido** (veja o impacto no caixa) | Aprovado | O impacto no caixa |
| 4.5 | Sílvia | mesmo pedido | **Enviar ao fornecedor** | Enviado | — |
| 4.6 | Bruno | `/app/compras/pedidos/$id/recebimento` | **Registrar recebimento** da máquina no local DEMO (sem nota) | Saldo da máquina 1; a falta some | O comprovante de recebimento (PDF sem custo) |
| 4.7 | Ana | `/app/financeiro/pagar` | Confira o título a pagar da Tração (R$ 18.200,00); centro de custo DEMO | Título previsto | — |

## Ato 5 — Fábrica: planejar, apontar, qualidade

| # | Quem | Onde | Faça | Confira | 📸 |
|---|---|---|---|---|---|
| 5.1 | Marcos | `/app/producao/supervisor` e `/app/producao/kanban` | Veja a OS do DEMO Bela Vista; imprima a **ficha da OS** (QR por etapa) | OS na coluna P | O Kanban; a ficha da OS |
| 5.2 | Bruno | `/app/estoque/requisicoes` | Requisição do material da BOM para a OS | Material baixado: vira custo realizado do projeto | A requisição |
| 5.3 | Carlos (tablet, PIN) | `/fab` | **Iniciar etapa** Projeto; minutos depois, **Finalizar** | Horas apontadas; a TV mostra a etapa em andamento | O tablet com o cronômetro; a TV |
| 5.4 | Carlos e Diego (tablet) | `/fab` | Corte e Montagem: iniciar, marcar o checklist item a item, finalizar | Etapa M concluída | O checklist da Montagem |
| 5.5 | Marcos | `/app/producao/apontamentos-abertos` | Lance horas retroativas para dar volume (ex.: Montagem +24 h acima do padrão) **em nome dos operadores, nunca no dele** | Horas acima do padrão em vermelho no Kanban | O Kanban em vermelho |
| 5.6 | Marcos | `/app/producao/qualidade` | **Aprovar** a qualidade (2FA) | Ele pode, porque não apontou | A inspeção aprovada |
| 5.7 | Marcos | OS | **Concluir OS** (número de série DEMO-KB6-001) | Projeto na etapa **F**; parcela de 40% com vencimento | O indicador de etapa em F |

## Ato 6 — Expedição e nota

| # | Quem | Onde | Faça | Confira | 📸 |
|---|---|---|---|---|---|
| 6.1 | Marcos | `/app/producao/expedicao/novo` | Romaneio do DEMO-KB6 | Romaneio criado | O PDF do romaneio |
| 6.2 | Bruno | romaneio | **Confirmar expedição** | Produto expedido | — |
| 6.3 | Ana | `/app/fiscal/saida` | **Emitir NF-e** do equipamento (homologação) | Autorizada **em homologação**, sem valor fiscal | A nota autorizada |

## Ato 7 — Instalação e aceite

| # | Quem | Onde | Faça | Confira | 📸 |
|---|---|---|---|---|---|
| 7.1 | Paulo | projeto → **OS de instalação** | Executor DEMO Instala Vale, **valor combinado R$ 19.500,00** (R$ 3.500,00 acima do orçado) | Título previsto do instalador | A OS de instalação |
| 7.2 | Marcos | `/app/producao/instalacoes/$id` | **Iniciar instalação** → marcar o checklist de entrega técnica → **Registrar aceite técnico** (2FA) | Projeto em I e depois **T**; parcela de 10% com vencimento | O termo de aceite em PDF |
| 7.3 | Ana | `/app/fiscal/saida` | NFS-e: **só mostre** a área de copiar para o portal da prefeitura; não registre número | — | A tela da NFS-e |

## Ato 8 — Pagar com controle

| # | Quem | Onde | Faça | Confira | 📸 |
|---|---|---|---|---|---|
| 8.1 | Ana | `/app/financeiro/ordens-de-pagamento/nova` | Ordem com o título da Tração e o do instalador → **Enviar para aprovação** | Aguardando aprovação; ela não tem o botão Aprovar | A ordem aguardando |
| 8.2 | John | `/app/financeiro/ordens-de-pagamento/$id` | **Aprovar** (2FA) | Aprovada | A aprovação; o PDF da ordem |
| 8.3 | Ana | mesma ordem | **Pagar** pela conta **DEMO — Conta simulação** (2FA) | Paga; saldo DEMO cai | — |
| 8.4 | Ana | `/app/financeiro/receber` | Baixe as parcelas de 40%, 20% e 10% na conta DEMO | Contrato 100% recebido | A lista A receber exportada em Excel |
| 8.5 (opcional) | Ana → John | `/app/financeiro/contas/$id/extrato` → `/app/financeiro/conciliacao` | Ana importa um OFX da conta DEMO; **você** concilia | Quem importou não concilia | A conciliação com os pares sugeridos |

## Ato 9 — Pessoas: ponto

| # | Quem | Onde | Faça | Confira | 📸 |
|---|---|---|---|---|---|
| 9.1 | Carlos e Diego | `/fab/ponto` | Entrada, intervalo e saída | Marcações no espelho | O ponto no tablet |
| 9.2 | Diego | `/app/meu-ponto` (celular) | Pede ajuste de uma saída "esquecida" | Aguardando aprovação | O pedido |
| 9.3 | John (ou Lúcia, se o grupo RH tiver Aprovar) | `/app/rh/ponto/ajustes` | **Aprovar** o ajuste | Espelho corrigido | O espelho |
| 9.4 | Lúcia | `/app/rh/folha` | **Só mostre** a tela da folha; não crie folha | — | — |

## Ato 10 — O dono fecha a conta

| # | Quem | Onde | Faça | 📸 |
|---|---|---|---|---|
| 10.1 | John | `/app/projetos/$id` → **Custos** | Margem orçada × realizada do DEMO Bela Vista (esperado: cerca de 27% para cerca de 23%; o maior desvio é a instalação, depois a mão de obra da Montagem) | **O slide final**: orçado × realizado |
| 10.2 | John | `/app/projetos/margens` e `/app/painel/resultado-por-projeto` | Margem do projeto na lista | A lista de margens |
| 10.3 | John | `/app/financeiro/dre` e `/app/financeiro/fluxo-de-caixa` | Filtre pelo centro de custo DEMO → **Exportar PDF** | O DRE em PDF |
| 10.4 | John | `/app/painel` | Pauta do dono: o que aguarda decisão | O painel |
| 10.5 | Rafael | `/app/financeiro` | Mostre que o vendedor não vê o Financeiro no menu | O dock do Comercial |

---

## Lista de prints para a apresentação

Nomeie os arquivos pela ordem: `01-grupo-compras.png`, `02-orcamento-itens.png`… Um por linha 📸, tirados no tema claro, com zoom em 100% e janela em tela cheia. A TV sai no tema escuro.

Prints essenciais (a apresentação curta):
1. Matriz do grupo (0.1)
2. Orçamento com margem (1.1)
3. Contrato e projeto no indicador de etapa (1.5)
4. BOM com o chip Falta (3.1)
5. Mapa comparativo da cotação (4.2)
6. Tablet com o checklist (5.4)
7. TV Ao vivo (5.3)
8. Kanban com as horas acima do padrão (5.5)
9. Ordem de pagamento aguardando a aprovação do dono (8.1)
10. Orçado × realizado do projeto (10.1)

## Limpeza (no fim, nesta ordem)

Faça logo depois dos prints. Se algum passo for recusado, **pare e me chame**: não force pelo banco.

1. **Ana:** estorne as baixas da conta DEMO (recebimentos e o pagamento da ordem), com o motivo "Simulação DEMO". Os títulos voltam a ficar em aberto.
2. **John:** cancele o contrato do DEMO Bela Vista, com o motivo. Projeto e parcelas sem baixa são cancelados juntos.
3. **Ana:** cancele os títulos a pagar DEMO que ainda estiverem abertos (Tração e instalador).
4. **Ana:** a NF-e de homologação fica como está: não tem valor fiscal.
5. **Bruno:** zere o local DEMO com um ajuste de saída, se sobrou algo; John aprova o ajuste.
6. **Lúcia:** inative os funcionários DEMO. Se pedir desligamento, me chame.
7. **John:**
   - inative os usuários DEMO em `/app/acessos/usuarios`;
   - revogue o aparelho e a TV DEMO;
   - inative os terceiros, os itens, o roteiro, o local, a conta e o centro de custo DEMO.
8. **Confira:** o DRE e o fluxo de caixa **sem** o filtro DEMO precisam mostrar os mesmos números de antes da simulação. Se não baterem, me chame antes da primeira operação real.
