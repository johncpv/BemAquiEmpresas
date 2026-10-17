# PRD — BemAqui Empresas

| | |
|---|---|
| Versão | 1.0 — 16/10/2026 |
| PO | John |
| Tenant piloto | Konebras Elevadores (WSP Equipamentos por Elevação LTDA - ME), Caçapava/SP |
| Execução | Claude Code |
| Status | Aprovado para Fase 0; pendências em `06-pendencias-riscos.md` |

## 1. Visão

Um sistema web, vendido como SaaS, que organiza toda a operação de uma pequena indústria sob encomenda — do orçamento à entrega instalada — com um único objetivo de fundo: **o dono saber, a qualquer momento, quanto cada projeto custa, quanto rende e como estará o caixa nas próximas semanas.**

O sistema substitui o Dolibarr na Konebras, que hoje cobre muita coisa de forma genérica e confusa. A aposta é o oposto: menos telas, fluxo único e encadeado, cada ação operacional gerando automaticamente seu reflexo financeiro.

### 1.1 Problemas que resolve
1. O dono não sabe a margem real de cada projeto (orçado × realizado).
2. Não existe previsão confiável de caixa, nem visão de quem atrasa pagamento.
3. O chão de fábrica não tem fila clara, nem previsão de entrega baseada em capacidade.
4. Estoque não reflete o que já está comprometido com OS abertas; compras são reativas.
5. Pagamentos saem sem aprovação formal.
6. Informação espalhada entre ERP genérico, planilhas e WhatsApp.

### 1.2 Métricas de sucesso (após 3 meses em produção na Konebras)
- 100% dos projetos com orçado × realizado visível em tempo real.
- 100% dos pagamentos passando por ordem de pagamento aprovada.
- Desvio da data de entrega prevista ≤ 15% nos projetos iniciados após o go-live.
- Dolibarr desligado.
- Fechamento financeiro mensal em menos de 1 dia de trabalho.

## 2. Personas e perfis de acesso

| Perfil | Quem | O que faz no sistema |
|---|---|---|
| Proprietário | Sócio/dono | Dashboard geral, aprova ordens de pagamento e contratos, vê tudo |
| Financeiro | Administrativo | CR/CP, cobrança, conciliação, fluxo de caixa |
| Comercial | Vendedor/dono | Orçamentos, contratos, carteira de clientes |
| Engenharia | Projetista | BOM, roteiro e etapa de projeto das OS |
| Supervisor de produção | Encarregado | Sequência da fila, distribuição, qualidade |
| Operador | Chão de fábrica | Apontamento de início/fim de etapa no tablet/celular (PWA) |
| Almoxarife | Estoque/compras | Requisições, recebimentos, inventário, compras |
| RH | Administrativo | Funcionários, ponto, folha |
| Cliente (portal) | Cliente final | Acompanha projeto, documentos e boletos (fase posterior) |
| Tela de quiosque | TV da fábrica | Painel de produção sem login (token de tela) |

Esses perfis viram **grupos de usuários** pré-cadastrados e editáveis. O usuário pertence a um ou mais grupos; cada grupo habilita módulos e marca as ações permitidas em cada um (ver, criar, editar, aprovar, excluir). A permissão efetiva é a soma dos grupos, limitada aos módulos liberados na versão contratada pelo tenant. Detalhes em SEC-4 a SEC-12.

## 3. Escopo

### 3.1 MVP (Fases 0 a 6)
Fundação SaaS, cadastros, comercial, financeiro, estoque, compras, produção, planejamento/painéis, integrações fiscal e bancária, RH (ponto básico + folha lançada) e patrimônio.

### 3.2 Pós-MVP
Folha calculada completa · Portal do cliente e aprovação online de orçamento · WhatsApp · Ponto legal (Portaria 671) · Depreciação · Retalhos de corte · Etapas terceirizadas · SSO Google/Microsoft · Contratos de manutenção recorrente (a validar).

### 3.3 Fora de escopo
Contabilidade completa (SPED contábil, balanço) — a contadora recebe exportações. eSocial direto pelo sistema (avaliar após folha calculada).

## 4. Fluxo principal (espinha dorsal)

```
Orçamento (validade, revisões)
   └─ aprovado internamente ─► Contrato (proposta firmada)
                                  ├─► Contas a Receber (parcelas por marco)
                                  └─► Projeto
                                        ├─► Itens do projeto (equipamentos)
                                        │      └─► OS de Fabricação (roteiro de etapas, Kanban)
                                        │             ├─ Etapa Projeto: BOM ─► Reserva de estoque ─► falta ─► Solicitação de Compra
                                        │             ├─ Etapas produtivas: requisição (baixa) + apontamento de horas (custo)
                                        │             ├─ Qualidade: checklist / não conformidade
                                        │             └─ Expedição: nº de série ─► NF-e
                                        └─► OS de Instalação (executor terceirizado) ─► CP do instalador
Todo custo (material, horas, terceiros, despesas) é apropriado ao Projeto ─► Orçado × Realizado ─► Dashboard
```

## 5. Módulos e requisitos

Prioridade: **M** = obrigatório no MVP · **D** = desejável no MVP · **P** = pós-MVP.

### 5.1 Plataforma SaaS e Segurança
| ID | Requisito | Pri |
|---|---|---|
| PLT-1 | Banco PostgreSQL separado por tenant + banco de catálogo (tenants, planos, status, conexões criptografadas) | M |
| PLT-2 | Tenant identificado pelo host da requisição (domínio próprio, ex.: `app.konebras.com.br`, ou subdomínio da plataforma) — ADR 0004 | M |
| PLT-3 | Provisionamento de tenant: cria banco, aplica migrations, cria admin, carrega seeds (plano de contas padrão, perfis, unidades) | M |
| PLT-4 | Painel de administração da plataforma (super admin): listar, suspender, provisionar tenants | M |
| SEC-1 | Login e-mail/senha, política de senha, bloqueio após tentativas, recuperação por e-mail | M |
| SEC-2 | 2FA opcional por app autenticador (TOTP) + códigos de recuperação; proprietário pode tornar obrigatório para o tenant | M |
| SEC-3 | JWT de curta duração + refresh token rotativo em cookie httpOnly | M |
| SEC-4 | **Cadastro de usuários**: nome, e-mail, grupos (um ou mais), situação (ativo, convite pendente, inativo), 2FA, último acesso; convite por e-mail; desativar sem apagar histórico | M |
| SEC-9 | **Grupos de usuários**: criar, editar, duplicar e inativar grupos; descrição; membros. Grupos iniciais por seed: Proprietário, Financeiro, Comercial, Engenharia, Supervisão de produção, Operadores, Almoxarifado, RH | M |
| SEC-10 | **Habilitar funcionalidades por grupo**: para cada módulo, chave "habilitado" e, por ação aplicável (ver, criar, editar, aprovar, excluir), caixa de marcação. Habilitar um módulo marca "ver"; desmarcar "ver" limpa o módulo. Cada módulo explica o que "aprovar" significa nele (ex.: Caixa = aprovar ordem de pagamento dentro da alçada) | M |
| SEC-11 | **Módulos liberados por versão/plano do tenant** (controlado pelo super admin no catálogo). Módulo não liberado aparece no menu com cadeado e abre a tela "Funcionalidade não liberada nessa versão do sistema."; na matriz de permissões aparece desabilitado | M |
| SEC-12 | Permissão efetiva = união dos grupos do usuário ∩ módulos liberados. Módulos sem permissão somem do menu. Grupo Proprietário é do sistema: acesso total, não editável, sempre com ao menos um membro. Alterações valem no próximo request do usuário | M |
| SEC-5 | Log de auditoria de toda alteração (usuário, data, antes/depois), consultável por registro | M |
| SEC-6 | Token de quiosque por tela (revogável, somente leitura, escopo de painel) | M |
| SEC-7 | Sessões ativas visíveis e revogáveis | D |
| SEC-8 | SSO Google/Microsoft | P |
| COM-1 | Envio de e-mail transacional (SMTP/SES) por fila, com templates e histórico | M |
| COM-2 | Notificações internas no sistema (sino) por evento: OP aguardando aprovação, OS atrasada, título vencido, estoque mínimo | M |
| COM-3 | Anexos em qualquer documento (orçamento, contrato, OS, NF, patrimônio) | M |
| COM-4 | Numeração sequencial configurável por tipo de documento | M |

### 5.2 Cadastros base (Terceiros)
Um único cadastro de **Terceiro** com papéis (cliente, fornecedor, instalador/prestador, transportadora); um mesmo CNPJ pode ter vários papéis.

| ID | Requisito | Pri |
|---|---|---|
| TER-1 | PF/PJ, CNPJ/CPF validado, consulta automática de dados por CNPJ (API pública) | M |
| TER-2 | Múltiplos endereços (cobrança, entrega, obra), contatos, IE, regime, condições padrão de pagamento | M |
| TER-3 | Histórico do terceiro: orçamentos, contratos, títulos, compras, pontualidade de pagamento | M |
| TER-4 | Indicador de pontualidade do cliente (média de dias de atraso), alimenta a projeção ajustada | M |
| TER-5 | Importação do Dolibarr (clientes, fornecedores, produtos) por CSV/consulta ao banco | M |

### 5.3 RH
| ID | Requisito | Pri |
|---|---|---|
| RH-1 | Cadastro completo do funcionário estruturado para folha: dados pessoais, documentos, dependentes (IR e salário-família), dados bancários/Pix, admissão, cargo, CBO, salário, jornada, sindicato, vale-transporte, adicionais (insalubridade, periculosidade) | M |
| RH-2 | Cargos/funções com faixa salarial e **custo-hora** (salário + encargos estimados) usado no apontamento de produção | M |
| RH-3 | Histórico salarial, de cargo e afastamentos | M |
| RH-4 | Ponto básico: marcação pelo PWA (com geolocalização opcional) ou lançamento manual pelo RH, espelho mensal, banco de horas simples, ajustes com justificativa e aprovação | M |
| RH-5 | Folha **lançada**: importar/lançar proventos e descontos vindos da contadora, gerar holerite PDF, enviar por e-mail, gerar CP de salários | M |
| RH-6 | Folha **calculada**: INSS, IRRF, FGTS, horas extras e DSR a partir do ponto, férias, 13º, rescisão; tabelas parametrizadas por vigência | P (fase 7) |
| RH-7 | Ponto legal Portaria 671 (REP-P, AFD, AEJ, comprovante) | P |
| RH-8 | Exportação para contadora (layout a definir) | D |

### 5.4 Comercial
**Definições (validadas com o PO):**
- **Orçamento** = pré-proposta com **validade**. Pode ter revisões (Rev. 0, 1, 2...). Não gera nenhum efeito financeiro.
- **Contrato** (a "proposta" no vocabulário da empresa) = proposta firmada. Nasce de um orçamento aprovado e é imutável depois de assinado (alterações só por aditivo).

| ID | Requisito | Pri |
|---|---|---|
| COML-1 | Orçamento com itens (equipamento/produto/serviço), composição de custo estimada por item: materiais (BOM estimada), horas por centro de trabalho, instalação terceirizada, frete, impostos estimados, margem/markup → preço de venda | M |
| COML-2 | Copiar orçamento/itens de um orçamento ou projeto anterior | M |
| COML-3 | Revisões versionadas com histórico e comparação | M |
| COML-4 | Validade com alerta de vencimento e status automático "expirado" | M |
| COML-5 | PDF do orçamento com layout do tenant (logo, condições, prazo) e envio por e-mail | M |
| COML-6 | Condições de pagamento por marco (ex.: 30% sinal, 40% na conclusão da fabricação, 30% na entrega técnica), com valor e evento-gatilho ou data | M |
| COML-7 | Aprovação interna gera Contrato (cópia congelada do orçamento aprovado) + Projeto + parcelas de Contas a Receber | M |
| COML-8 | Aditivo de contrato (valor/escopo/prazo) recalcula orçado e recebíveis futuros | D |
| COML-9 | Funil comercial: orçamentos por status, taxa de conversão, motivo de perda | D |
| COML-10 | Aprovação online pelo cliente via link/portal | P |

### 5.5 Projetos
| ID | Requisito | Pri |
|---|---|---|
| PRJ-1 | Projeto vinculado a um contrato e cliente, com endereço da obra, data prometida, responsável | M |
| PRJ-2 | Itens do projeto (cada equipamento/componente), cada um gera uma ou mais OS de fabricação | M |
| PRJ-3 | OS de Instalação por item/projeto, com executor terceirizado (fornecedor do tipo instalador), etapas de instalação e checklist de entrega técnica | M |
| PRJ-4 | Visão 360 do projeto: status das OS, recebíveis, custos lançados, documentos, linha do tempo | M |
| PRJ-5 | **Orçado × realizado** por natureza de custo (material, mão de obra, instalação, frete, impostos, outros) e margem atual vs prevista | M |
| PRJ-6 | Marcos do projeto disparam liberação de parcelas de recebimento (ex.: "fabricação concluída" libera a parcela 2 para faturamento) | M |
| PRJ-7 | Garantia: data de início e fim de garantia por item entregue | D |

### 5.6 Engenharia
| ID | Requisito | Pri |
|---|---|---|
| ENG-1 | Templates de roteiro por tipo de produto: sequência de etapas, centro de trabalho, horas padrão, checklist da etapa, se exige aprovação | M |
| ENG-2 | Etapas de exemplo (definitivas virão do cliente): Projeto → Corte → Montagem → Qualidade → Expedição | M |
| ENG-3 | BOM criada na etapa de Projeto da OS, com opção de copiar de item/OS anterior; itens, quantidades, unidade, perda % | M |
| ENG-4 | Liberação da BOM pela engenharia dispara reserva de estoque e cálculo de faltas | M |
| ENG-5 | Revisão de BOM após liberação: ajusta reservas e registra histórico | M |
| ENG-6 | Anexos técnicos (desenhos, PDFs) por OS e por item | M |

### 5.7 Produção (chão de fábrica)
| ID | Requisito | Pri |
|---|---|---|
| PRD-1 | OS de fabricação gerada a partir do item do projeto e do template de roteiro (editável na OS) | M |
| PRD-2 | Kanban por etapa (colunas = etapas do roteiro), filtros por projeto, cliente, linha, atraso | M |
| PRD-3 | Operador no PWA: vê sua fila, inicia/pausa/finaliza etapa; apontamento registra funcionário, início, fim, pausas e motivo | M |
| PRD-4 | Horas apontadas × custo-hora do cargo = custo de mão de obra apropriado à OS e ao projeto | M |
| PRD-5 | Requisição de material da OS (baixa das reservas) pelo almoxarife ou pelo operador com confirmação | M |
| PRD-6 | Qualidade: checklist por etapa; reprovação abre **Não Conformidade** (descrição, causa, ação, responsável) e devolve a OS a uma etapa anterior escolhida | M |
| PRD-7 | Conclusão da OS gera produto acabado em estoque com **número de série** e registra lotes de matéria-prima consumidos (rastreabilidade) | M |
| PRD-8 | Expedição: romaneio, vínculo com NF-e de saída, baixa do produto acabado | M |
| PRD-9 | Paradas de máquina/centro (manutenção, falta de material) registradas e refletidas na capacidade | D |
| PRD-10 | Etapas terceirizadas (envio/retorno) | P |

### 5.8 Planejamento e capacidade
| ID | Requisito | Pri |
|---|---|---|
| PLN-1 | Centros de trabalho com capacidade em horas/dia, turnos e calendário (feriados, paradas) | M |
| PLN-2 | Fila de projetos com **prioridade manual** (arrastar para ordenar) | M |
| PLN-3 | Sistema calcula datas previstas de início/fim de cada etapa de cada OS com base em prioridade, horas padrão (ou restantes) e capacidade (agendamento finito simples, para frente) | M |
| PLN-4 | Cronograma (Gantt) por projeto e por centro de trabalho; data prevista de entrega × data prometida com alerta de risco | M |
| PLN-5 | Simulação: "se eu aceitar este orçamento agora, quando entrego?" a partir das horas estimadas do orçamento | D |
| PLN-6 | Ocupação da fábrica por semana (horas comprometidas / capacidade) | M |

### 5.9 Estoque (Almoxarifado)
| ID | Requisito | Pri |
|---|---|---|
| EST-1 | Itens: matéria-prima, componente, produto acabado, consumo, serviço; NCM, unidade, estoque mínimo, controle por lote e/ou série | M |
| EST-2 | Múltiplos locais de estoque | M |
| EST-3 | Conversão de unidades por item (ex.: compra em kg, consumo em m², fator por item) | M |
| EST-4 | Saldo físico, **reservado** (OS abertas) e **disponível**; reserva na liberação da BOM, baixa na requisição | M |
| EST-5 | Estoque negativo bloqueado | M |
| EST-6 | Movimentos: entrada (compra/NF), saída (requisição, venda), transferência, ajuste de inventário (com motivo e aprovação), devolução de OS | M |
| EST-7 | Custo médio ponderado móvel, recalculado a cada entrada | M |
| EST-8 | Lote na matéria-prima e série no produto acabado, com consulta de rastreabilidade nos dois sentidos | M |
| EST-9 | Inventário (contagem por local, ajuste) | M |
| EST-10 | "Capacidade de produzir": para um item/BOM, quantas unidades o disponível atual permite e o que falta | D |
| EST-11 | Retalhos de corte | P |

### 5.10 Compras
| ID | Requisito | Pri |
|---|---|---|
| CMP-1 | Solicitação de compra manual ou **automática** pela falta de material na liberação da BOM ou pelo estoque mínimo | M |
| CMP-2 | Cotação com 1+ fornecedores, comparativo e escolha | M |
| CMP-3 | Pedido de compra com aprovação por alçada, envio por e-mail em PDF | M |
| CMP-4 | Pedido gera previsão de CP (compromisso) no fluxo de caixa | M |
| CMP-5 | Recebimento: por NF-e (XML/PDF) ou manual, conferência contra o pedido, entrada no estoque com lote, efetivação do CP | M |
| CMP-6 | Compra vinculada a projeto (custo direto) ou estoque geral | M |

### 5.11 Financeiro (núcleo do produto)
| ID | Requisito | Pri |
|---|---|---|
| FIN-1 | Plano de contas gerencial (seed padrão editável), centros de custo, vínculo opcional a projeto em todo lançamento | M |
| FIN-2 | Contas bancárias e caixas, saldo inicial, transferências | M |
| FIN-3 | Contas a receber: títulos gerados pelo contrato, venda avulsa ou manual; parcelas, juros/multa, descontos, baixa parcial | M |
| FIN-4 | Contas a pagar: originadas de compras, folha, instaladores, despesas fixas recorrentes (aluguel, energia...) ou manual | M |
| FIN-5 | **Ordem de pagamento**: todo pagamento exige OP aprovada; alçadas configuráveis por valor/categoria (padrão: proprietário aprova tudo); aprovação/reprovação com comentário, no sistema e por link no e-mail (autenticado) | M |
| FIN-6 | Despesas recorrentes geradas automaticamente por regra (mensal, dia X) | M |
| FIN-7 | Cobrança via **Banco Inter**: emissão de boleto com Pix (bolepix), envio ao cliente por e-mail, baixa automática por webhook | M |
| FIN-8 | Conciliação bancária: extrato via API do Inter e importação OFX para outros bancos; sugestão automática de match | M |
| FIN-9 | Régua de cobrança: lembrete antes do vencimento e após atraso, por e-mail | D |
| FIN-10 | **Fluxo de caixa realizado + projetado** (diário/semanal/mensal): base = títulos em aberto + recorrentes + pedidos de compra + folha prevista | M |
| FIN-11 | **Cenário ajustado**: desloca cada recebível pela média histórica de atraso do cliente (e % de inadimplência), mostrando base × ajustado e o dia de menor saldo | M |
| FIN-12 | Inadimplência: títulos vencidos por cliente e por projeto, aging (0-30, 31-60, 61-90, 90+) | M |
| FIN-13 | DRE gerencial por mês e por projeto | M |
| FIN-14 | Exportação para contadora (lançamentos, títulos, NF) | D |

### 5.12 Fiscal
| ID | Requisito | Pri |
|---|---|---|
| FIS-1 | Integração com o projeto existente **EmissorFiscal** via adaptador (`IFiscalEmitter`); análise do projeto é a primeira tarefa do módulo | M |
| FIS-2 | Cadastro fiscal do tenant: certificado A1 (criptografado), regime, IE, série, ambiente (homologação/produção) | M |
| FIS-3 | NF-e de saída a partir da expedição/venda: itens, NCM, CFOP, tributos por regra fiscal; autorização, cancelamento, carta de correção, DANFE PDF e XML por e-mail | M |
| FIS-4 | Regras fiscais por operação (CFOP, CST/CSOSN, alíquotas) parametrizáveis — conteúdo definido com a contadora | M |
| FIS-5 | NF-e de entrada: importação de **XML** (preferencial) com de-para de itens do fornecedor | M |
| FIS-6 | NF-e de entrada por **PDF (DANFE)**: extração da chave de acesso e dados; tentativa de obter o XML pela chave (se o EmissorFiscal suportar Distribuição DF-e); senão, leitura assistida com conferência manual obrigatória | M |
| FIS-7 | NFS-e para serviços de instalação/manutenção (CNAE principal da empresa) | D — ver pendência P-02 |
| FIS-8 | Adequação à Reforma Tributária (campos IBS/CBS) conforme exigência vigente | M — ver pendência P-03 |

### 5.13 Patrimônio
| ID | Requisito | Pri |
|---|---|---|
| PAT-1 | Cadastro de bens: tipo (máquina, ferramenta, mobiliário, veículo, TI), marca, modelo, nº de série, data e valor de aquisição, NF, local, responsável, foto | M |
| PAT-2 | Etiqueta com **QR code** (impressão em lote); leitura pelo PWA abre a ficha do bem | M |
| PAT-3 | Movimentação (transferência de local/responsável) com histórico | M |
| PAT-4 | Plano de manutenção preventiva por bem (periodicidade por tempo ou horas), OS de manutenção, histórico e custo | M |
| PAT-5 | Máquina vinculada a centro de trabalho: manutenção programada reduz a capacidade no planejamento | D |
| PAT-6 | Depreciação | P |

### 5.14 Dashboards e painéis
**Dashboard do proprietário** (uma tela, visão simples e geral, todos os KPIs pedidos):
- Caixa: saldo atual, fluxo projetado 30/60/90 dias (base × ajustado), dia de menor saldo
- Faturamento do mês e acumulado do ano × meta
- Margem: média prevista × realizada; ranking de projetos por margem (piores primeiro)
- Carteira: valor contratado a produzir e a faturar
- Inadimplência: total vencido, aging, top clientes
- Produção: projetos em andamento, OS atrasadas, ocupação da fábrica (semana atual e próximas 4)
- Entregas: previstas no mês, em risco (prevista > prometida)
- Comercial: orçamentos em aberto, taxa de conversão, orçamentos vencendo
- Pendências do dono: ordens de pagamento e aditivos aguardando aprovação

Cada card leva ao detalhe (drill-down).

**Painel de produção (TV / quiosque):** tempo real via SignalR; uma tela por linha ou geral (filtro na configuração do token); mostra OS por etapa, operador atual, tempo na etapa, previsto × real, atrasos destacados, próximos projetos da fila e previsão de entrega; rotação automática entre visões; modo escuro e fonte grande.

**Painel do supervisor:** Kanban + capacidade + não conformidades abertas + faltas de material.

### 5.15a Ajuda no produto e experiência propositiva
Mecanismo igual ao do NSHome (`D:\GIT\NSHome`): **ajuda contextual** por tela, **dicas** em campos e **assistente de perguntas com IA (RAG)** que responde só com base nos textos de ajuda, citando a fonte.

| ID | Requisito | Pri |
|---|---|---|
| AJD-1 | **Ajuda contextual:** toda tela, e cada aba nas telas com abas, tem um texto de ajuda em Markdown versionado com o código (`Ajuda/<chave>.md`, cabeçalho com `titulo`, `rota`, `aba`, `padrao`, `modulo`). Seções padrão: Para que serve · Como usar · Perguntas frequentes | M |
| AJD-2 | **Botão "Ajuda" (?)** na barra superior abre um painel lateral com a ajuda da tela atual (resolvida pela rota e pela aba), o índice de todas as ajudas e a busca | M |
| AJD-3 | **Primeira visita:** na primeira vez que o usuário abre uma tela, a ajuda abre sozinha uma vez. O registro de "já vista" fica no perfil do usuário | M |
| AJD-4 | **Dica (`<Dica>`):** um "?" ao lado de termo, campo ou botão que confunde; explica ao passar o mouse, ao focar com o teclado ou ao tocar | M |
| AJD-5 | **"Pergunte ao BemAqui":** o usuário pergunta em linguagem natural e o sistema busca as seções de ajuda mais próximas (priorizando a tela atual) e pede à IA uma resposta **só com elas**, sempre citando a fonte (link para a seção). Sem IA disponível ou sem citação válida, devolve os trechos encontrados. Nunca envia dado de negócio do tenant à IA; só textos de ajuda | M |
| AJD-6 | A ajuda respeita o acesso: o usuário só vê, e a IA só usa, ajudas dos módulos liberados e habilitados para ele | M |
| AJD-7 | **Cobertura obrigatória:** um teste automatizado falha se alguma rota do front não tiver ajuda. Mudou a tela, muda a ajuda no mesmo commit | M |
| AJD-8 | **Provedor de IA configurável em tela** (Configurações → Inteligência artificial, permissão `settings.edit`): provedor (Anthropic Claude, Google Gemini, OpenAI, …), modelo, chave de API (cifrada por tenant, write-only, nunca em código ou log) e botão "Testar conexão". Sem configuração, o "Pergunte ao BemAqui" funciona só por busca (trechos). Implementação atrás de `IAiProvider`, com um adaptador por provedor; uso registrado por tenant | M |

**Princípio propositivo (vale para todo módulo):** o sistema antecipa o próximo passo e evita ação desnecessária.

| ID | Requisito | Pri |
|---|---|---|
| PRP-1 | **Padrões inteligentes:** formulários vêm preenchidos com o mais provável (condição de pagamento e endereço padrão do terceiro, último fornecedor e preço do item, conta e centro de custo mais usados, roteiro do tipo de produto). O usuário só confirma ou ajusta | M |
| PRP-2 | **Próximo passo sugerido:** ao concluir uma ação, a tela oferece a continuação natural. Exemplos: orçamento aprovado → "Ver projeto criado"; BOM liberada com falta → "Gerar solicitação de compra"; recebimento → "Conferir título a pagar" | M |
| PRP-3 | **Nada digitado duas vezes:** dado que já existe no sistema ou em fonte pública é reaproveitado (CNPJ preenche cadastro, XML preenche recebimento, orçamento copia de anterior, contrato copia do orçamento) | M |
| PRP-4 | **Minha pauta:** uma lista por usuário com "o que precisa de você hoje" (OP para aprovar, títulos vencendo, OS atrasadas, orçamentos expirando, faltas de material), em ordem do que mais destrava, com link direto para resolver | M |
| PRP-5 | **Alertas acionáveis:** toda notificação ou alerta leva direto à tela e à ação que resolve, nunca só a uma lista genérica | M |
| PRP-6 | **Ações em lote** onde o volume justifica: aprovar várias OP, baixar vários títulos, imprimir várias etiquetas | D |
| PRP-7 | O sistema **sugere, mas não decide**: ação com efeito financeiro, fiscal ou de estoque sempre exige confirmação explícita do usuário | M |

### 5.15 Portal do cliente (pós-MVP, Fase 8)
Acesso do cliente ao andamento do(s) projeto(s) (etapa atual, previsão de entrega), documentos (contrato, NF, manuais), boletos em aberto e, futuramente, aprovação online de orçamento.

## 6. Requisitos não funcionais
| Tema | Requisito |
|---|---|
| Desempenho | Telas de listagem < 1 s com 50 mil registros; painel TV atualiza em < 2 s após apontamento |
| Disponibilidade | 99,5% mensal; janela de manutenção fora do horário comercial |
| Backup | Dump diário por banco de tenant, retenção 30 dias, cópia fora da VPS; teste de restauração mensal |
| Segurança | HTTPS obrigatório, senhas com hash Identity (PBKDF2), segredos (certificados, chaves Inter) criptografados por tenant, rate limit no login, OWASP Top 10 |
| LGPD | Dados pessoais de funcionários com acesso restrito ao RH; exportação e anonimização sob pedido |
| Usabilidade | Responsivo; PWA do chão de fábrica com botões grandes, uso com luva, funciona em tablet de entrada |
| Idioma/moeda | PT-BR, BRL, fuso America/Sao_Paulo |
| Observabilidade | Logs estruturados com tenant e usuário; health checks; alerta de falha em jobs e integrações |

## 7. Roadmap de fases (detalhe no backlog)
| Fase | Entrega | Resultado para o cliente |
|---|---|---|
| 0 | Fundação SaaS, auth/2FA, RBAC, auditoria, e-mail, deploy | Ambiente no ar com login seguro |
| 1 | Cadastros (terceiros, itens, funcionários base, plano de contas, bancos) + migração Dolibarr | Dados migrados |
| 2 | Comercial + Projetos + Financeiro núcleo (CR/CP, OP com aprovação, fluxo de caixa base) | Primeiro ganho financeiro: OP aprovada e caixa projetado |
| 3 | Engenharia + Estoque + Compras + Produção (OS, Kanban, apontamento, qualidade, série/lote) | Fábrica rodando no sistema |
| 4 | Planejamento/capacidade + Painel TV + Dashboard do proprietário + projeção ajustada + orçado×realizado | Visão completa do dono |
| 5 | Fiscal (EmissorFiscal, NF-e saída/entrada) + Banco Inter (bolepix, webhook, conciliação) | Faturamento e cobrança automáticos — **go-live e desligamento do Dolibarr** |
| 6 | RH (ponto básico, folha lançada, holerite) + Patrimônio | Fim do MVP |
| 7 | Folha calculada completa | |
| 8 | Portal do cliente, aprovação online, WhatsApp | |
| 9 | Ponto legal, depreciação, retalhos, terceirização de etapas, SSO | |

Observação: o go-live operacional pode acontecer ao fim da Fase 5; a Fase 6 entra com o sistema já em produção.
