# Operação demo — como montar o tenant `demo` para a apresentação

Objetivo: deixar cada ato do [`roteiro-apresentacao.md`](roteiro-apresentacao.md) com o dado certo, no estado certo, no dia. Os valores abaixo são de exemplo, plausíveis para a Konebras, e fecham entre si (orçamento, BOM, custo-hora e margem).

**Data da apresentação = D.** As datas são relativas a D. Comece a montagem em **D-8** (dia útil): o supervisor só lança apontamento retroativo de até 7 dias, e a história precisa de horas reais.

**Nada aqui usa dado real de cliente.** Os CNPJs e CPFs abaixo são fictícios, com dígito verificador válido. Não use a consulta de CNPJ nesses números (ela pode trazer o nome de uma empresa real): digite os dados à mão. A exceção é o XML de compra do Ato 3 (ver etapa 14).

## Visão geral da carga

| Etapa | O quê | Quem faz | Quando | Depende de |
|---|---|---|---|---|
| 0 | Tenant, módulos, integrações | Equipe técnica | D-10 | — |
| 1 | Proprietário, empresa, parâmetros | Proprietário | D-8 | 0 |
| 2 | Grupos e usuários | Proprietário | D-8 | 1 |
| 3 | Financeiro base e implantação | Proprietário e Financeiro | D-8 | 1 |
| 4 | Terceiros | Comercial e Financeiro | D-8 | 1 |
| 5 | Centros de trabalho, calendário, modelo de instalação | Supervisor | D-8 | 1 |
| 6 | RH: sindicato, jornada, cargos, funcionários | RH | D-8 | 5 |
| 7 | Estoque: itens e saldo inicial | Almoxarifado e Proprietário | D-8 | 4 |
| 8 | Engenharia: roteiro do KB-6 | Engenharia | D-8 | 5, 7 |
| 9 | Fiscal e cobrança | Proprietário | D-8 | 0, 4 |
| 10 | Patrimônio, TV e tablet | Supervisor | D-7 | 5, 6 |
| 11 | Histórico financeiro (agosto a outubro) | Financeiro e Proprietário | D-7 | 3, 4 |
| 12 | Projeto Bela Vista (do orçamento à instalação) | Vários | D-7 a D-2 | 2 a 9 |
| 13 | Projeto Vale Norte (até a Montagem) | Vários | D-6 a D-1 | 12 |
| 14 | Compra com XML (Ato 3) | Compras e Proprietário | D-5 a D-2 | 7 |
| 15 | Orçamentos do funil e do Ato 1 | Comercial | D-3 | 8 |
| 16 | Ordem de pagamento, extrato, ponto e folha | Financeiro, RH, funcionários | D-3 a D-1 | 11 a 13 |
| 17 | Ensaio e refação | Quem apresenta | D-1 | tudo |

## O que é importado e o que é digitado

| Dado | Como entra no `demo` |
|---|---|
| Cadastros do Dolibarr (terceiros, contatos, itens, contas) | **Não use no `demo`.** A importação do Dolibarr é para o tenant `konebras`, roda só no servidor (CLI) e a tela `/app/configuracoes/importacao` é só de consulta. Para o `demo`, digite os cadastros abaixo |
| Saldo inicial do estoque | Digitado, com **Saldo inicial em lote** em Estoque → Saldos |
| Saldos das contas e títulos em aberto | Digitados na Implantação financeira, em grade (com **Duplicar** linha) |
| Itens parecidos | **Duplicar item** |
| Roteiro | **Duplicar** o "Roteiro padrão (exemplo)" que já vem no sistema |
| BOM | Montada uma vez e depois **Copiar de outra OS** |
| Nota de compra | **Importar XML** (ou PDF, ou **Digitar a nota**) em Fiscal → Notas de entrada |
| Extrato do banco | **Importar OFX** (modelo na etapa 16) ou API do Inter (sandbox) |
| Valores da folha | Lançados à mão ou importados do arquivo da contadora na folha lançada |

O sistema já traz prontos: plano de contas gerencial (1 Receitas a 5 Financiamentos e sócios), centros de custo (Administrativo, Comercial, Produção, Instalação), condições de pagamento (À vista, 30, 30/60, 30/60/90 dias), local ALM (Almoxarifado principal), o "Roteiro padrão (exemplo)", o modelo da OS de instalação, os grupos iniciais e a regra de alçada "Proprietário aprova tudo".

---

## Etapa 0 — Tenant, módulos e integrações (equipe técnica, D-10)

1. **Host:** escolher o endereço do `demo` (por exemplo `demo.bemaquiapp.com.br`, a definir), apontar o DNS para a VPS, criar o vhost no `stack-nginx` e o certificado no certbot.
2. **Provisionar** em `/plataforma/tenants` → **Provisionar tenant**: chave `demo`, razão social "Konebras Elevadores (DEMO)", CNPJ 47.225.005/0001-86 (o emitente de teste da homologação, ver `docs/operacao/emissor-fiscal.md`), o host e o e-mail do Proprietário de demonstração. Se já existir um `demo` com dados de teste, provisione outra chave (por exemplo `apresentacao`) e repita o passo 4 com ela.
3. **Módulos** em `/plataforma/tenants/demo` → **Módulos**: libere todos. (Para mostrar o cadeado ao vivo no Ato 8, deixe a aba da plataforma aberta no dia; ver o Ato 8.)
4. **Fiscal em homologação:** conferir `catalog.tenants.fiscal_environment_allowed = 'Homologation'` para a chave do `demo` (comando em `docs/operacao/emissor-fiscal.md`, seção "Ligar a homologação no `demo`").
5. **Emissor Fiscal:** tenant do Emissor, emitente e chave de API do `demo` (tabela da seção 1 de `docs/operacao/emissor-fiscal.md`).
6. **Banco Inter sandbox:** credencial de sandbox da Konebras (`docs/operacao/banco-inter.md`, P-08). Sem ela, o Ato 5 mostra a aba Boleto sem emitir.
7. **E-mail (SMTP):** precisa estar ativo para os convites. Sem SMTP, use `tenant invite-owner --key demo` para o link do Proprietário (`docs/operacao/provisionamento.md`).

## Etapa 1 — Proprietário, empresa e parâmetros (D-8)

| Tela | Rota | O que preencher | Ajuda |
|---|---|---|---|
| Convite e 2FA | `/convite` → `/entrar/configurar-2fa` | Senha do Proprietário e autenticador no celular de quem apresenta | [convite](../../Ajuda/convite.md) · [configurar-2fa](../../Ajuda/configurar-2fa.md) |
| Empresa | `/app/configuracoes/empresa` | Konebras Elevadores (DEMO), Caçapava/SP, logo | [configuracoes-empresa](../../Ajuda/configuracoes-empresa.md) |
| Numeração | `/app/configuracoes/numeracao` | Manter o padrão (ORC-2026-0001 etc.) | [configuracoes-numeracao](../../Ajuda/configuracoes-numeracao.md) |
| Parâmetros | `/app/configuracoes/parametros` | % de encargos **80,00%**; modo **Margem sobre o preço**; % padrão **27%**; validade 15 dias; prazo padrão de instalação **5 dias úteis**; metas de faturamento de nov/2026 **R$ 250.000,00** e dez/2026 **R$ 300.000,00**; prazo padrão de compra 15 dias | [configuracoes-parametros](../../Ajuda/configuracoes-parametros.md) |
| Segurança | `/app/configuracoes/seguranca` | Não exigir 2FA de todos (assim o Comercial e o Operador entram sem código) | [configuracoes-seguranca](../../Ajuda/configuracoes-seguranca.md) |
| IA (opcional) | `/app/configuracoes/ia` | Provedor e chave para o **Pergunte ao BemAqui** | [configuracoes-ia](../../Ajuda/configuracoes-ia.md) |

## Etapa 2 — Grupos e usuários (D-8)

1. Em `/app/acessos/grupos`, crie **Diretoria / Gerência**, **Compras** e **Contabilidade** pela matriz de [`grupos-e-permissoes.md`](grupos-e-permissoes.md). No **RH**, acrescente `hr.approve`.
2. Em `/app/acessos/usuarios`, convide um usuário por papel. Use caixas reais com sufixo, por exemplo `<caixa>+financeiro@<domínio>`, para receber todos os convites num lugar só.

| Nome na demo | Grupo(s) | Precisa de 2FA | Observação |
|---|---|---|---|
| João (dono) | Proprietário | sim | Janela 1 do projetor |
| Helena | Diretoria / Gerência | sim | Usada só para mostrar a alçada |
| Ana | Financeiro | sim | Janela 2 |
| Rafael | Comercial | não | Monta os orçamentos |
| Paulo | Engenharia | sim | Libera a BOM |
| Marcos | Supervisor de fábrica (Supervisão de produção) | sim | Janela 3. **Nunca aponte horas com o usuário dele** (ele decide a qualidade) |
| Carlos | Operadores | não | PIN no tablet |
| Diego | Operadores | não | Pedido de ajuste de ponto |
| Edson | Operadores | não | Apontamentos da Montagem |
| Sílvia | Compras | não | Solicitação, cotação e pedido |
| Bruno | Almoxarifado | não | Recebe pela nota |
| Lúcia | RH | sim | Folha e ponto |

3. Cadastre o autenticador de todos os usuários com 2FA no mesmo celular, com o nome do papel na conta do aplicativo.

## Etapa 3 — Financeiro base e implantação (D-8)

| Tela | Rota | O que fazer | Ajuda |
|---|---|---|---|
| Contas e caixas | `/app/financeiro/contas` | **Banco Inter — conta movimento** (banco 077, agência 0001, conta 1234567-8); **Caixa da fábrica** | [financeiro-contas](../../Ajuda/financeiro-contas.md) |
| Plano de contas e centros de custo | `/app/financeiro/plano-de-contas`, `/app/financeiro/centros-de-custo` | Manter os do sistema | [financeiro-plano-de-contas](../../Ajuda/financeiro-plano-de-contas.md) |
| Condições de pagamento | `/app/financeiro/condicoes-de-pagamento` | Manter as do sistema | [financeiro-condicoes-de-pagamento](../../Ajuda/financeiro-condicoes-de-pagamento.md) |
| Alçadas | `/app/financeiro/alcadas` | Criar as faixas sugeridas em [`grupos-e-permissoes.md`](grupos-e-permissoes.md) | [financeiro-alcadas](../../Ajuda/financeiro-alcadas.md) |
| Implantação financeira | `/app/financeiro/implantacao` | Data de corte **31/07/2026**; saldo do Inter **R$ 180.000,00**, caixa **R$ 2.000,00**; sem títulos em aberto; concluir | [financeiro-implantacao](../../Ajuda/financeiro-implantacao.md) |

Título lançado na implantação não entra no resultado nem na pontualidade do cliente. Por isso o histórico da etapa 11 é lançado como título normal.

## Etapa 4 — Terceiros (D-8)

Rota: `/app/cadastros/terceiros/novo` · Ajuda: [cadastros-terceiro-novo](../../Ajuda/cadastros-terceiro-novo.md)

| Nome | Tipo | CNPJ | Cidade | Detalhe |
|---|---|---|---|---|
| Condomínio Residencial Jardim das Acácias | Cliente | 11.222.333/0001-81 | São José dos Campos/SP | Endereço de obra com código IBGE; contato de cobrança |
| Construtora Vale Norte Ltda | Cliente | 22.333.444/0001-81 | Taubaté/SP | Idem |
| Condomínio Residencial Bela Vista | Cliente | 33.444.555/0001-81 | Caçapava/SP | Obra em Caçapava (a NFS-e é do município da obra) |
| Clínica Santa Luzia Ltda | Cliente | 44.555.666/0001-81 | Jacareí/SP | Será a cliente inadimplente |
| Aço Forte Distribuidora Ltda | Fornecedor | 55.666.777/0001-81 | São Paulo/SP | Chapas e guias |
| Tração Motores Industriais Ltda | Fornecedor | 66.777.888/0001-81 | Guarulhos/SP | Máquina de tração, quadro de comando, assistência técnica |
| Cabos & Polias Comércio Ltda | Fornecedor | 77.888.999/0001-81 | Campinas/SP | Cabos de aço |
| Vidros & Inox Cabinas Ltda | Fornecedor | 88.999.000/0001-98 | São Paulo/SP | Cabina e portas |
| Instala Vale Montagens Ltda | Instalador | 99.000.111/0001-65 | Caçapava/SP | Chave Pix e condição 30 dias |

Fornecedores e instalador: dados de pagamento (Pix ou conta) preenchidos. Na Cabos & Polias, troque a chave Pix em D-1: a ordem de pagamento do Ato 5 mostra o aviso âmbar "dados de pagamento mudaram há pouco".

## Etapa 5 — Fábrica: centros, calendário e instalação (D-8)

| Tela | Rota | O que preencher | Ajuda |
|---|---|---|---|
| Centros de trabalho | `/app/producao/centros-de-trabalho` | **Engenharia**, **Corte**, **Montagem**, **Qualidade**, todos na linha "Elevadores", turnos 07:00–11:00 e 12:00–16:48 (8,80 h/dia) | [producao-centros-de-trabalho](../../Ajuda/producao-centros-de-trabalho.md) |
| Calendário | `/app/producao/calendario` | Feriados de 20/11 e 25/12/2026 | [producao-calendario](../../Ajuda/producao-calendario.md) |
| Modelo da OS de instalação | `/app/producao/modelo-instalacao` | Manter o do sistema; conferir os itens do checklist de entrega técnica | [producao-modelo-instalacao](../../Ajuda/producao-modelo-instalacao.md) |

## Etapa 6 — RH (D-8)

| Tela | Rota | O que preencher | Ajuda |
|---|---|---|---|
| Sindicatos | `/app/rh/sindicatos` | Sindicato dos Metalúrgicos (fictício) | [rh-sindicatos](../../Ajuda/rh-sindicatos.md) |
| Jornadas | `/app/rh/jornadas` | 44 h semanais, 07:00–16:48, intervalo 1 h | [rh-jornadas](../../Ajuda/rh-jornadas.md) |
| Cargos | `/app/rh/cargos` | Tabela abaixo | [rh-cargos](../../Ajuda/rh-cargos.md) |
| Funcionários | `/app/rh/funcionarios/novo` | Tabela abaixo; ligar cada um ao usuário | [rh-funcionario-novo](../../Ajuda/rh-funcionario-novo.md) |

Cargos (encargos 80%, 220 h/mês; custo-hora = salário × 1,8 ÷ 220):

| Cargo | Centro padrão | Salário de referência | Custo-hora |
|---|---|---|---|
| Projetista | Engenharia | R$ 6.000,00 | R$ 49,09 |
| Caldeireiro | Corte | R$ 3.600,00 | R$ 29,45 |
| Montador | Montagem | R$ 3.300,00 | R$ 27,00 |
| Inspetor de qualidade | Qualidade | R$ 4.000,00 | R$ 32,73 |
| Supervisor de produção | Montagem | R$ 5.500,00 | R$ 45,00 |
| Assistente administrativo | — | R$ 3.200,00 | R$ 26,18 |

Funcionários (CPFs fictícios):

| Nome | CPF | Cargo | Usuário |
|---|---|---|---|
| Paulo Andrade | 111.444.777-35 | Projetista | Paulo |
| Carlos Souza | 222.555.888-46 | Montador | Carlos |
| Diego Lima | 333.666.999-57 | Montador | Diego |
| Edson Rocha | 444.777.000-83 | Montador | Edson |
| Fábio Nunes | 555.888.111-94 | Caldeireiro | — |
| Marcos Pereira | 666.999.222-03 | Supervisor de produção | Marcos |
| Ana Martins | 777.000.333-40 | Assistente administrativo | Ana |
| Lúcia Ferraz | 888.111.444-50 | Assistente administrativo | Lúcia |

Complete a conta para pagamento (banco ou Pix) de cada um: sem ela, a folha não fecha.

## Etapa 7 — Estoque (D-8)

Rota: `/app/estoque/itens/novo` · Ajuda: [estoque-item-novo](../../Ajuda/estoque-item-novo.md) · [estoque-saldos](../../Ajuda/estoque-saldos.md)

NCMs de exemplo: confirme com a contadora antes de usar fora do `demo`.

| Código | Item | Tipo | Unidade | NCM | Custo | Saldo inicial (ALM) |
|---|---|---|---|---|---|---|
| CP-MAQ6 | Máquina de tração 6 pessoas | Componente | UN | 8431.31.10 | R$ 18.500,00 | 2 |
| CP-QDC | Quadro de comando | Componente | UN | 8537.10.90 | R$ 9.800,00 | 2 |
| CP-CAB6 | Cabina inox 6 pessoas | Componente | UN | 8431.31.10 | R$ 14.200,00 | 2 |
| CP-PPAV | Porta de pavimento | Componente | UN | 8431.31.10 | R$ 1.600,00 | 10 |
| MP-GT90 | Guia T 90 mm | Matéria-prima (lote) | M | 8431.31.10 | R$ 95,00 | 80 |
| CP-CABO8 | Cabo de aço 8 mm | Componente | M | 7312.10.90 | R$ 22,00 | 240 |
| MP-CH03 | Chapa de aço 3 mm 1200×3000 | Matéria-prima (lote) | UN | 7208.39.90 | R$ 146,00 | 20 |
| PA-KB6 | Elevador de passageiros KB-6 (6 pessoas) | Produto acabado (série) | UN | 8428.10.00 | — | 0 |

1. Cadastre o primeiro item e use **Duplicar item** para os parecidos.
2. Em **Saldos**, **Saldo inicial em lote** com a última coluna. Lote das chapas e guias: `L-2026-10`.
3. **Concluir implantação do estoque** (Proprietário).

O saldo atende o Bela Vista e o Vale Norte. A BOM do Jardim das Acácias (Ato 2) fica sem máquina de tração e sem cabo: o chip **Falta** aparece e a solicitação de compra nasce sozinha.

BOM por unidade do KB-6 (montada na etapa 12 e depois copiada):

| Item | Quantidade por unidade | Custo |
|---|---|---|
| Máquina de tração 6 pessoas | 1 UN | R$ 18.500,00 |
| Quadro de comando | 1 UN | R$ 9.800,00 |
| Cabina inox 6 pessoas | 1 UN | R$ 14.200,00 |
| Porta de pavimento | 5 UN | R$ 8.000,00 |
| Guia T 90 mm | 40 M | R$ 3.800,00 |
| Cabo de aço 8 mm | 120 M | R$ 2.640,00 |
| Chapa de aço 3 mm | 10 UN | R$ 1.460,00 |
| **Total** | | **R$ 58.400,00** |

## Etapa 8 — Engenharia: roteiro do KB-6 (D-8)

Rota: `/app/engenharia/roteiros` → **Duplicar** o "Roteiro padrão (exemplo)" · Ajuda: [engenharia-roteiro-novo](../../Ajuda/engenharia-roteiro-novo.md)

Nome **Elevador KB-6**, produto PA-KB6. Quatro etapas (a embalagem fica no romaneio):

| Letra | Etapa | Centro | Horas padrão | Marcações |
|---|---|---|---|---|
| P | Projeto | Engenharia | 16 | Começa sem BOM |
| C | Corte | Corte | 24 | Checklist: conferir medidas, rebarbar |
| M | Montagem | Montagem | 80 | Checklist: fixar guias, montar cabina, passar cabos, ligar quadro, teste em vazio |
| Q | Qualidade | Qualidade | 6 | Etapa de qualidade; checklist: nivelamento, freio, portas, documentação |

## Etapa 9 — Fiscal e cobrança (D-8)

| Tela | Rota | O que fazer | Ajuda |
|---|---|---|---|
| Emissor Fiscal | `/app/configuracoes/integracoes/emissor-fiscal` | Chave do `demo`, **Testar conexão** | [configuracoes-integracoes-emissor-fiscal](../../Ajuda/configuracoes-integracoes-emissor-fiscal.md) |
| Fiscal | `/app/configuracoes/fiscal` | Parâmetros "a confirmar" revisados; série de homologação | [configuracoes-fiscal](../../Ajuda/configuracoes-fiscal.md) |
| Regras fiscais | `/app/fiscal/regras` | Venda de equipamento (NF-e) e prestação de serviço de instalação (NFS-e, ISS do município da obra), ativadas pelo Proprietário | [fiscal-regras](../../Ajuda/fiscal-regras.md) |
| Banco Inter | `/app/configuracoes/integracoes/inter` | Conta Inter em **Sandbox**, **Testar conexão** | [configuracoes-integracoes-inter](../../Ajuda/configuracoes-integracoes-inter.md) |
| Cobrança | `/app/configuracoes/cobranca` | Multa 2%, juros 1% ao mês, régua de cobrança padrão | [configuracoes-cobranca](../../Ajuda/configuracoes-cobranca.md) |

## Etapa 10 — Patrimônio, TV e tablet (D-7)

| Tela | Rota | O que fazer | Ajuda |
|---|---|---|---|
| Bens | `/app/patrimonio/bens/novo` | Dobradeira hidráulica (centro Corte, R$ 145.000,00), Guilhotina (Corte, R$ 98.000,00), Máquina de solda MIG (Montagem, R$ 12.000,00), Ponte rolante 2 t (Montagem, R$ 60.000,00) | [patrimonio-bem-novo](../../Ajuda/patrimonio-bem-novo.md) |
| Plano preventivo | aba **Manutenção** do bem | Dobradeira: mensal, vencendo em D+3, prestador Tração Motores | [patrimonio-bem-manutencao](../../Ajuda/patrimonio-bem-manutencao.md) |
| Etiquetas | `/app/patrimonio/bens` | Imprimir a etiqueta QR da dobradeira e colar num cartão para a sala | [patrimonio-etiquetas](../../Ajuda/patrimonio-etiquetas.md) |
| Painel da TV | `/app/painel-tv` | Tela "TV da Fábrica", linha Todas, as 3 visões, rotação 20 s; abrir o link na TV ou no segundo monitor | [painel-tv](../../Ajuda/painel-tv.md) |
| Tablet | `/fab` | Marcos entra com e-mail e senha → **Aparelho** → **Registrar este aparelho**. Depois Carlos, Diego e Edson entram com e-mail e senha e **Criar PIN** | [fab-entrar](../../Ajuda/fab-entrar.md) · [configuracoes-seguranca](../../Ajuda/configuracoes-seguranca.md) |

## Etapa 11 — Histórico financeiro de agosto a outubro (D-7)

Dá números ao DRE, ao fluxo de caixa e à inadimplência. Lance como títulos normais (não na implantação), com a emissão e o vencimento no mês certo.

| Lançamento | Onde | Valores por mês (ago, set, out) | Situação no dia D |
|---|---|---|---|
| Aluguel do galpão (3.2 Ocupação) | Recorrentes `/app/financeiro/recorrentes`, dia 5 | R$ 12.000,00 | Pagos; novembro em aberto |
| Energia (3.2 Ocupação) | Recorrentes, dia 15 | R$ 4.800,00 | Pagos; novembro em aberto |
| Honorários da contadora (3.3 Administrativas) | Recorrentes, dia 10 | R$ 2.500,00 | Pagos; novembro em aberto |
| Conservação mensal de elevadores (1.2 Serviços) | A receber `/app/financeiro/receber/novo`, um título por condomínio (Jardim das Acácias, Bela Vista, Vale Norte) | R$ 6.500,00 cada | Recebidos; Jardim das Acácias paga sempre 8 a 12 dias atrasado |
| Plataforma de acessibilidade, Clínica Santa Luzia (1.1 Venda de equipamentos) | A receber, emissão 05/09/2026 | R$ 54.000,00 em 3 parcelas (05/09, 05/10, 05/11) | 1ª recebida com 15 dias de atraso; 2ª **vencida** (inadimplência); 3ª em aberto |
| Compra de material para estoque (2.1 Materiais) | A pagar, Aço Forte | R$ 9.000,00 em set e R$ 11.000,00 em out | Pagos |

Os pagamentos de cada mês saem por uma ordem de pagamento por mês (Ana cria, João aprova, Ana paga com a data do mês e o motivo da baixa retroativa). Os recebimentos são baixados com a data real do mês. Ajuda: [financeiro-recorrentes](../../Ajuda/financeiro-recorrentes.md) · [financeiro-receber-novo](../../Ajuda/financeiro-receber-novo.md) · [financeiro-ordens-de-pagamento](../../Ajuda/financeiro-ordens-de-pagamento.md)

## Etapa 12 — Projeto Bela Vista, do orçamento à instalação (D-7 a D-2)

É o elevador "que já passou por tudo" e fecha a apresentação (Ato 9). Fazer este projeto inteiro é também o primeiro ensaio de ponta a ponta.

**Orçamento** (Rafael, `/app/orcamentos`): cliente Bela Vista, prazo de entrega 75 dias, marcos 30% na assinatura, 40% fabricação concluída + 10 dias, 20% entrega + 10 dias, 10% aceite técnico + 15 dias.

| Item | Tipo | Preço | Composição de custo |
|---|---|---|---|
| Elevador de passageiros KB-6, 5 paradas | Equipamento (produto PA-KB6) | R$ 105.000,00 | Material R$ 58.400,00 (as linhas da BOM); mão de obra: Engenharia 16 h (Projetista), Corte 24 h (Caldeireiro), Montagem 80 h (Montador), Qualidade 6 h (Inspetor) = R$ 3.848,62; frete R$ 2.200,00; impostos estimados R$ 9.450,00 |
| Instalação do elevador KB-6 | Serviço | R$ 20.000,00 | Instalação R$ 16.000,00; impostos estimados R$ 1.000,00 |
| **Total** | | **R$ 125.000,00** | Custo R$ 90.898,62 · **margem orçada 27,3%** |

O mesmo orçamento serve de molde para o Vale Norte e o Jardim das Acácias (use a cópia de orçamento).

**Sequência e estado esperado**

| Dia | Quem | Ação | Estado depois |
|---|---|---|---|
| D-7 | João | Aprovar e gerar contrato (assinatura em D-7) | Contrato ativo, parcelas a receber, projeto P, OS de fabricação |
| D-7 | Ana | Baixar o sinal de 30% (R$ 37.500,00) no Inter | Sinal recebido (fica como adiantamento no DRE até a expedição) |
| D-7 | Paulo | OS → aba BOM → criar com as linhas da etapa 7 → **Liberar BOM** | Material reservado |
| D-7 a D-4 | João (no lugar do supervisor) | Apontamentos retroativos por funcionário: Paulo 16 h no Projeto; Fábio 26 h no Corte; Carlos, Diego e Edson **104 h** na Montagem (24 h acima do padrão) | Horas e custo de mão de obra no projeto |
| D-6 | Bruno | Requisição de material da BOM para a OS | Material baixado: custo realizado |
| D-4 | João | Qualidade aprovada (João não apontou horas em nome próprio) | OS pronta para concluir |
| D-4 | Marcos | **Concluir OS** (série KB6-2026-001) | Projeto F Fabricado; parcela de 40% com vencimento |
| D-3 | Marcos | Romaneio → Bruno confirma a expedição | Produto expedido |
| D-3 | Ana | NF-e de saída emitida em homologação | Receita e imposto pela nota |
| D-3 | Paulo | OS de instalação a partir do projeto, executor Instala Vale, **valor combinado R$ 19.500,00** (R$ 3.500,00 acima do orçado) | Título previsto do instalador (comprometido) |
| D-3 | Ana | Título a pagar de frete R$ 2.900,00 confirmado com o projeto (R$ 700,00 acima) | Custo realizado de frete |
| D-2 | Marcos | **Iniciar instalação**; concluir as etapas; marcar todo o checklist de entrega técnica | Projeto I Instalando, aguardando o **aceite técnico** (feito ao vivo no Ato 4) |

Resultado esperado no Ato 9 (anote os números reais no ensaio, porque o imposto vem da regra fiscal): custo projetado cerca de R$ 95.700,00 e margem projetada perto de **23,4%**, cerca de **3,9 pp abaixo** da orçada; a natureza que mais desvia é a instalação, seguida da mão de obra da Montagem.

Ajuda: [comercial-orcamento-itens](../../Ajuda/comercial-orcamento-itens.md) · [comercial-orcamento-condicoes](../../Ajuda/comercial-orcamento-condicoes.md) · [producao-os-bom](../../Ajuda/producao-os-bom.md) · [producao-os-apontamentos](../../Ajuda/producao-os-apontamentos.md) · [estoque-requisicoes](../../Ajuda/estoque-requisicoes.md) · [producao-romaneio-novo](../../Ajuda/producao-romaneio-novo.md) · [producao-instalacoes](../../Ajuda/producao-instalacoes.md) · [projetos-projeto-custos](../../Ajuda/projetos-projeto-custos.md)

## Etapa 13 — Projeto Vale Norte, até a Montagem (D-6 a D-1)

| Dia | Quem | Ação | Estado no dia D |
|---|---|---|---|
| D-6 | João | Aprovar o orçamento copiado do Bela Vista, cliente Vale Norte, assinatura D-6 | Projeto e OS |
| D-6 | Ana | Baixar o sinal (R$ 37.500,00) | Recebido |
| D-5 | Paulo | BOM: **Copiar de outra OS** (a do Bela Vista vem **Sugerido**) → Liberar | Reservado |
| D-6 a D-2 | João | Apontamentos: Projeto 18 h (Paulo), Corte 26 h (Fábio), Montagem 70 h (Diego e Edson) | Horas acima do padrão no Projeto e no Corte (vermelho no Kanban) |
| D-4 | Bruno | Requisição de material da BOM | Baixado |
| D-1 | Carlos (tablet) | **Iniciar etapa** Montagem e marcar 4 dos 5 itens do checklist | Apontamento aberto; no Ato 2 ele marca o último item e finaliza |

Não deixe nenhum apontamento do Vale Norte no usuário do Marcos: ele decide a qualidade ao vivo.

## Etapa 14 — Compra com XML para o Ato 3 (D-5 a D-2)

A importação do XML confere a assinatura do fornecedor, o protocolo da SEFAZ e se a nota é **destinada ao CNPJ da empresa**. Um XML inventado não passa. Escolha um destes caminhos:

| Caminho | Como | Risco |
|---|---|---|
| A (recomendado) | Pegue um XML autorizado (`-procNFe.xml`) de uma compra real e recente da Konebras, destinada ao CNPJ 47.225.005/0001-86. Cadastre esse fornecedor real e os itens da nota no `demo` e crie o pedido com os mesmos itens e quantidades | Mostra nome de fornecedor real: combine com o dono antes. Confira no ensaio se a nota é aceita no `demo` |
| B | **Importar PDF** do DANFE dessa nota, ou **Digitar a nota**, e receber pela nota | Sem a conferência de assinatura; o resto do Ato 3 é igual |

Sequência (com os itens da nota escolhida):
1. Sílvia: solicitação manual → cotação com 2 fornecedores (uma resposta mais barata e outra mais rápida) → concluir → **Gerar pedidos de compra** → **Enviar para aprovação**.
2. João: **Aprovar pedido** (impacto no caixa) → Sílvia: **Enviar ao fornecedor**.
3. Estado no dia D: pedido **Enviado**, sem recebimento; XML (ou PDF) num pen drive ou na área de trabalho.

Para mostrar a solicitação que nasce sozinha, o Ato 2 libera a BOM do Jardim das Acácias ao vivo (falta de máquina de tração e de cabo).

Ajuda: [compras-solicitacao-nova](../../Ajuda/compras-solicitacao-nova.md) · [compras-cotacao-nova](../../Ajuda/compras-cotacao-nova.md) · [compras-pedido](../../Ajuda/compras-pedido.md) · [fiscal-entrada-importar](../../Ajuda/fiscal-entrada-importar.md) · [fiscal-entrada-importar-pdf](../../Ajuda/fiscal-entrada-importar-pdf.md)

## Etapa 15 — Orçamentos do funil e do Ato 1 (D-3)

| Orçamento | Cliente | Situação no dia D | Para quê |
|---|---|---|---|
| KB-6 Jardim das Acácias (cópia do Bela Vista) | Jardim das Acácias | **Enviado** (PDF gerado e enviado ao e-mail de teste) | Aprovado ao vivo no Ato 1 |
| Plataforma de acessibilidade | Clínica Santa Luzia | Perdido (motivo: preço) | Funil |
| Elevador de carga 1.000 kg | Construtora Vale Norte | Rascunho, vence em 5 dias | Funil e "vence em 7 dias" na pauta |
| 2 elevadores KB-6 (obra nova) | Construtora Vale Norte | Enviado | Funil com valor em aberto |

## Etapa 16 — Ordem de pagamento, extrato, ponto e folha (D-3 a D-1)

**Ordem de pagamento para o Ato 5** (D-1): Ana lança em A pagar (`/app/financeiro/pagar/novo`) o título da Cabos & Polias de **R$ 8.640,00** ("Cabos de aço 8 mm, compra de outubro", conta 2.1 Materiais, vencimento D+2), cria a ordem e envia; fica **Aguardando aprovação** do João. A troca da chave Pix do fornecedor (etapa 4) gera o aviso âmbar. Ajuda: [financeiro-pagar-novo](../../Ajuda/financeiro-pagar-novo.md)

**Extrato OFX para a conciliação** (Ana importa em D-1, João concilia ao vivo; quem importou não concilia). Monte o arquivo com as transações reais da conta Inter do `demo` nos últimos dias (os dois sinais e um pagamento) e uma tarifa sem par. Os campos `BANKID` e `ACCTID` precisam bater com a conta cadastrada:

```text
OFXHEADER:100
DATA:OFXSGML
VERSION:102
SECURITY:NONE
ENCODING:USASCII
CHARSET:1252
COMPRESSION:NONE
OLDFILEUID:NONE
NEWFILEUID:NONE

<OFX>
<SIGNONMSGSRSV1><SONRS><STATUS><CODE>0<SEVERITY>INFO</STATUS><DTSERVER>20261113<LANGUAGE>POR</SONRS></SIGNONMSGSRSV1>
<BANKMSGSRSV1><STMTTRNRS><TRNUID>1001<STATUS><CODE>0<SEVERITY>INFO</STATUS><STMTRS><CURDEF>BRL
<BANKACCTFROM><BANKID>077<BRANCHID>0001<ACCTID>12345678<ACCTTYPE>CHECKING</BANKACCTFROM>
<BANKTRANLIST><DTSTART>20261106120000[-3:BRT]<DTEND>20261112120000[-3:BRT]
<STMTTRN><TRNTYPE>CREDIT<DTPOSTED>20261106120000[-3:BRT]<TRNAMT>37500.00<FITID>DEMO-0001<MEMO>PIX RECEBIDO RESIDENCIAL BELA VISTA</STMTTRN>
<STMTTRN><TRNTYPE>CREDIT<DTPOSTED>20261107120000[-3:BRT]<TRNAMT>37500.00<FITID>DEMO-0002<MEMO>PIX RECEBIDO CONSTRUTORA VALE NORTE</STMTTRN>
<STMTTRN><TRNTYPE>DEBIT<DTPOSTED>20261110120000[-3:BRT]<TRNAMT>-2900.00<FITID>DEMO-0003<MEMO>PIX ENVIADO FRETE</STMTTRN>
<STMTTRN><TRNTYPE>DEBIT<DTPOSTED>20261112120000[-3:BRT]<TRNAMT>-14.90<FITID>DEMO-0004<MEMO>TARIFA PACOTE DE SERVICOS</STMTTRN>
</BANKTRANLIST>
</STMTRS></STMTTRNRS></BANKMSGSRSV1></OFX>
```

Troque as datas e os valores pelos lançamentos reais do `demo` (cada linha do banco precisa ter a transação de mesmo valor e data no sistema, menos a tarifa). Ajuda: [financeiro-contas-extrato-banco](../../Ajuda/financeiro-contas-extrato-banco.md) · [financeiro-conciliacao](../../Ajuda/financeiro-conciliacao.md)

**Ponto** (D-3 a D-1): Carlos, Diego e Edson marcam entrada, intervalo e saída pelo tablet (`/fab/ponto`). Diego "esquece" a saída de D-2 e pede o ajuste em **Meu ponto** (`/app/meu-ponto`) no celular: o pedido fica **Aguardando aprovação** para a Lúcia no Ato 6. Ajuda: [fab-ponto](../../Ajuda/fab-ponto.md) · [meu-ponto](../../Ajuda/meu-ponto.md)

**Folha de outubro** (D-2, Lúcia): Nova folha → Mensal → **Lançada** → lançar os valores (salário dos cargos; INSS e IRRF de exemplo) e os encargos → **Fechar folha** (2FA) → aba **Pagamentos** → **Criar ordem de pagamento dos salários**. No dia D, a ordem de salários fica aguardando o João (entra na pauta). Ajuda: [rh-folha](../../Ajuda/rh-folha.md) · [rh-folha-detalhe-pagamentos](../../Ajuda/rh-folha-detalhe-pagamentos.md)

**Conferência paralela (opcional):** só aparece com números se houver uma folha **calculada** da mesma competência, e ela exige o ponto do mês fechado. Sem isso, no Ato 6 mostre a tela e explique o critério de diferença zero.

## Estado esperado no dia D (conferência rápida)

| Onde | O que precisa aparecer |
|---|---|
| Painel `/app/painel` | Saldo em contas positivo; caixa projetado com os dois cenários diferentes; vencido da Clínica Santa Luzia; margem dos projetos com Bela Vista e Vale Norte; o alerta de decisões aguardando |
| Início `/app` (João) | Ordens de pagamento aguardando (fornecedor e salários), orçamento que vence em 7 dias |
| Orçamentos | Jardim das Acácias **Enviado**, funil com 4 situações |
| Kanban | Vale Norte na coluna **M**, com horas acima do padrão; nenhuma OS do Jardim das Acácias ainda |
| Tablet | Carlos com a etapa Montagem do Vale Norte em andamento, 4 de 5 itens marcados |
| TV | Conectada, **Ao vivo**, mostrando o Vale Norte |
| Compras | Pedido do Ato 3 **Enviado**, sem recebimento |
| Instalações | Bela Vista com o checklist completo, aguardando aceite |
| Ordens de pagamento | Uma do fornecedor aguardando aprovação, com o aviso âmbar |
| Conciliação | OFX importado pela Ana, pares sugeridos |
| RH → Ajustes | Pedido do Diego aguardando |
| Patrimônio → Manutenção | Preventiva da dobradeira vencendo em 3 dias; nenhuma corretiva aberta |

## Itens consumidos a cada ensaio

Um ensaio completo gasta estes itens. Refaça antes do dia.

| Item consumido | Como refazer |
|---|---|
| Orçamento Jardim das Acácias aprovado | Copiar o orçamento de novo (Enviado). Cancele o contrato do ensaio (Proprietário, com motivo): o projeto e as parcelas sem baixa são cancelados juntos |
| BOM do Jardim das Acácias e a solicitação gerada | Saem com o cancelamento do contrato e da OS; a solicitação é cancelada sozinha quando a falta some |
| Montagem do Vale Norte finalizada, qualidade aprovada, OS concluída | Não volta atrás com facilidade. Prepare um **segundo projeto igual** ao Vale Norte (por exemplo "Vale Norte, torre B") no mesmo estado, ou faça o ensaio do Ato 2 até a etapa 6 |
| Romaneio e NF-e do Vale Norte | Use o projeto reserva |
| Aceite técnico do Bela Vista | Prepare um **segundo projeto em instalação** (reserva) ou ensaie o Ato 4 sem confirmar o aceite |
| Recebimento pelo XML | O mesmo XML não entra duas vezes. Ensaie só até a conferência da nota, sem **Receber pela nota**, ou tenha um segundo XML |
| Ordem de pagamento aprovada e paga | Criar outra com outro título |
| Conciliação | Use **Desfazer conciliação** (com motivo) |
| Pedido de ajuste do Diego | Pedir de novo, para outro dia |
| OS de manutenção corretiva | Cancelar a do ensaio |

## Pendências do produto que afetam a demonstração

| Ponto | Efeito na apresentação | O que fazer |
|---|---|---|
| Boleto do Inter só provado contra o simulador; credencial de sandbox pendente (P-08) | Pode não emitir no `demo` | Sem sandbox, mostre a aba Boleto e explique; não prometa o tempo de baixa |
| NF-e: homologação SEFAZ dos cenários no `demo` é pré-condição externa | A emissão pode ser recusada | Use **Pré-visualizar** (DANFE sem valor fiscal) se a emissão falhar no ensaio |
| NFS-e de Caçapava: processo provisório pelo portal da prefeitura | Não há emissão automática | Mostre a área de copiar e o registro; no `demo`, não registre número de nota real |
| Importação do XML exige nota real destinada ao CNPJ | Ver etapa 14 | Caminho A ou B |
| Folha calculada de férias, 13º e rescisão (FLH-03, 04 e 05) | Em finalização | Não mostrar; falar como em [`README.md`](README.md) |
| Portal do cliente e WhatsApp | Próxima fase, depois do go-live | Não mostrar |

## Checklist pré-apresentação

**Na véspera (D-1)**
- [ ] Ensaio completo cronometrado e itens consumidos refeitos.
- [ ] Números do Ato 9 (margem orçada e projetada do Bela Vista) anotados no roteiro.
- [ ] Ordem de pagamento do Ato 5 aguardando; OFX importado pela Ana; pedido de ajuste do Diego aguardando.
- [ ] Carlos com a Montagem do Vale Norte em andamento no tablet.
- [ ] XML (ou PDF) do Ato 3 na área de trabalho do notebook.
- [ ] Etiqueta QR da dobradeira impressa.

**Ambiente**
- [ ] `demo` abre no host escolhido, com certificado válido.
- [ ] Emissor Fiscal: **Testar conexão** OK. Inter sandbox: **Testar conexão** OK (ou plano B combinado).
- [ ] Rede da sala testada no notebook, no tablet e na TV (o app do operador e o ponto precisam de rede).

**Logins (um perfil de navegador por usuário)**
- [ ] Janela 1: João (Proprietário), já no painel.
- [ ] Janela 2: Ana (Financeiro).
- [ ] Janela 3: Marcos (Supervisor), no Painel do supervisor.
- [ ] Opcional: Rafael (Comercial), para mostrar o botão que ele não tem.
- [ ] Celular com o autenticador de João, Ana, Marcos e Paulo, carregado e sem bloqueio de tela demorado.
- [ ] Sessões abertas há menos de 15 minutos no tablet (a sessão por PIN sai depois de 15 min sem toque).

**Navegador e telas**
- [ ] Chrome ou Edge atualizado, zoom em 100% (ou 110% no projetor), notificações do sistema operacional silenciadas.
- [ ] Tema claro no projetor (seletor de tema na barra de cima), a TV é sempre escura.
- [ ] Ajuda que abre sozinha na primeira visita já vista em cada tela usada (passe uma vez por todas no ensaio, com os mesmos usuários).
- [ ] TV em tela cheia, mostrando **Ao vivo**.
- [ ] Tablet com a tela sempre ligada, brilho alto, carregador.

**Plano B**
- [ ] Capturas de tela dos Atos 4 e 5 (NF-e e boleto) caso a integração externa falhe.
- [ ] Aba da plataforma (`/plataforma/tenants/demo`) aberta, se for mostrar o cadeado no Ato 8.
