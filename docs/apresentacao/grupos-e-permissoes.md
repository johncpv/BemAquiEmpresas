# Grupos de usuários e permissões — recomendação para a Konebras

Este documento vira o slide de **controle gerencial** da apresentação e é o roteiro para configurar os acessos em **Sistema → Usuários e acessos → Grupos** (`/app/acessos/grupos`).

Os códigos `modulo.acao` são os do catálogo real do sistema (`ModuleCatalog`). Os grupos marcados "do sistema" já nascem em toda empresa nova; os marcados "criar" a Konebras cria (o caminho mais rápido é **Novo grupo** a partir de um grupo parecido).

## As 3 portas de acesso (em linguagem de cliente)

| Porta | Pergunta | Quem decide | O que a pessoa vê quando a porta está fechada |
|---|---|---|---|
| 1. Versão contratada | "A Konebras contratou este módulo?" | O contrato com o BemAqui | O módulo aparece no menu **com cadeado** e abre a tela "Funcionalidade não liberada nessa versão do sistema." |
| 2. Módulo no grupo | "O grupo desta pessoa usa este módulo?" | O Proprietário, nos grupos | O módulo **não aparece** no menu dela |
| 3. Ação no grupo | "O que ela pode fazer lá dentro?" | O Proprietário, nos grupos | O botão não aparece ou vem desabilitado, com o motivo |

Regras que valem sempre:
- A pessoa pode estar em **mais de um grupo**; vale a **soma** dos grupos, sempre limitada ao que a versão contratada libera.
- Não existe permissão solta no usuário: tudo passa pelo grupo.
- **Ninguém concede o que não tem.** E as ações sensíveis só um membro do Proprietário concede: qualquer **Aprovar** (`*.approve`), **Pagar** (`finance.pay`), **Ver custo e margem** (`*.view_cost`), **Ver dados sensíveis** (`hr.view_sensitive`), **Ver dados de saúde** (`hr.view_health`), **Editar** em Usuários e acessos (`identity.edit`) e em Configurações (`settings.edit`).
- Ninguém altera um grupo do qual faz parte, a não ser o Proprietário.
- **Verificação em duas etapas (2FA) obrigatória** para o Proprietário e para quem tem qualquer **Aprovar** ou **Pagar**. A empresa pode exigir de todos em Configurações → Segurança.
- Mudou o grupo, a permissão vale no próximo acesso da pessoa.

Frase para o slide:
> "Três portas: o que a Konebras contratou, o que o grupo usa e o que a pessoa pode fazer. Aprovar, pagar, ver custo e ver salário, só o dono concede."

## Mapa do dock (onde cada módulo aparece)

| Área do dock | Módulos (código) |
|---|---|
| Visão geral | Início (todos) · Painel do proprietário (`dashboard`) |
| Comercial | Orçamentos (`quotes`) · Projetos e contratos (`projects`) · Cadastros (`parties`) |
| Fábrica | Engenharia (`engineering`) · Produção (`production`) · Estoque (`inventory`) · Compras (`purchasing`) · Patrimônio (`assets`) |
| Financeiro | Caixa e pagamentos (`finance`) · Fiscal (`fiscal`) |
| Pessoas | RH (`hr`) · Meu ponto e Meus holerites (todo funcionário com usuário ligado) |
| Telas dedicadas | Painel da TV (`kiosk`) · App do operador (`shopfloor`, abre `/fab`) |
| Sistema | Usuários e acessos (`identity`) · Configurações (`settings`) |

## Matriz recomendada

Legenda das ações: V = `view` (Ver), C = `create` (Criar), E = `edit` (Editar), A = `approve` (Aprovar), X = `delete` (Excluir). Ações específicas pelo nome.

| Módulo | Proprietário | Diretoria / Gerência | Financeiro | Comercial | Engenharia | Supervisor de fábrica | Operador | Compras | Almoxarifado | RH | Contabilidade |
|---|---|---|---|---|---|---|---|---|---|---|---|
| `dashboard` | tudo | V | V | — | — | — | — | — | — | — | — |
| `quotes` | tudo | V, A, `view_cost` | V, `view_cost` | V, C, E, `view_cost` | — | — | — | — | — | — | — |
| `projects` | tudo | V, A, `view_cost` | V, `view_cost` | V, `view_cost` | V, E | V | — | V | — | — | — |
| `parties` | tudo | V | V, C, E | V, C, E | V | — | — | V, C, E | V, C, E | — | V |
| `engineering` | tudo | V | — | — | V, C, E, X, A | V | — | — | — | — | — |
| `production` | tudo | V | — | — | V, C, E | V, C, E, A, `approve_quality` | — | V | V | — | — |
| `inventory` | tudo | V, A | V | — | V | V, C | — | V | V, C, E, `adjust` | — | — |
| `purchasing` | tudo | V, A | V | — | — | — | — | V, C, E | V, `receive` | — | — |
| `assets` | tudo | V | — | — | — | V, C, E | — | — | V | — | — |
| `finance` | tudo | V, A | V, C, E, `pay` | — | — | — | — | — | — | — | V |
| `fiscal` | tudo | V | V, C, E | — | — | — | — | — | V, `import_incoming` | — | V, E |
| `hr` | tudo | V | — | — | — | — | — | — | — | V, C, E, A, `view_sensitive`, `view_health` | — |
| `kiosk` | tudo | — | — | — | — | V, C, X | — | — | — | — | — |
| `shopfloor` | tudo | — | — | — | — | V, C | V, C | — | — | — | — |
| `identity` | tudo | — | — | — | — | — | — | — | — | — | — |
| `settings` | tudo | V | — | — | — | — | — | — | — | — | — |

**Diferença em relação ao que o sistema já traz pronto:**

| Grupo | Situação | O que ajustar |
|---|---|---|
| Proprietário | do sistema | Nada. Não é editável e precisa ter ao menos um membro |
| Financeiro | do sistema ("Financeiro") | Nada |
| Comercial | do sistema ("Comercial") | Nada |
| Engenharia | do sistema ("Engenharia") | Nada |
| Supervisor de fábrica | do sistema ("Supervisão de produção") | Nada |
| Operador | do sistema ("Operadores") | Nada |
| Almoxarifado | do sistema ("Almoxarifado") | Nada |
| RH | do sistema ("RH") | Acrescentar **Aprovar** (`hr.approve`) só se o RH for aprovar ajuste de ponto e fechar a folha. Sem ela, isso fica com o Proprietário |
| Diretoria / Gerência | **criar** | Montar pela matriz. Usar só se houver alguém, além do dono, que aprova |
| Compras | **criar** | Montar pela matriz. Por padrão, criar, editar e aprovar pedido de compra ficam só com o Proprietário |
| Contabilidade | **criar** | Montar pela matriz, só se a contadora for ter usuário |

## Os grupos, um a um

### Proprietário (do sistema)
- **Perfil:** sócio e dono. Entra direto no Painel do proprietário.
- **Módulos e ações:** tudo o que a versão contratada libera.
- **Vê no dock:** todas as áreas.
- **Só ele faz (por padrão):** aprovar ordem de pagamento e pedido de compra (regra padrão "Proprietário aprova tudo"), aprovar o orçamento e gerar o contrato, aprovar aditivo, cancelar ou inutilizar nota fiscal e ativar regra fiscal (`fiscal.approve`), aprovar ajuste de estoque, definir a data de corte da implantação financeira, aceitar a competência da conferência paralela da folha e virar para a folha calculada, conceder ações sensíveis, cuidar da privacidade (LGPD).
- **Regras:** 2FA obrigatório. Grupo não editável, nunca fica sem membro.

### Diretoria / Gerência (criar)
- **Perfil:** gerente geral ou sócio que divide as aprovações com o dono.
- **Módulos e ações:** ver quase tudo; aprovar orçamento (`quotes.approve`), aditivo (`projects.approve`), ordem de pagamento dentro da alçada (`finance.approve`), pedido de compra dentro da alçada (`purchasing.approve`) e ajuste de estoque (`inventory.approve`); ver custo e margem.
- **Vê no dock:** Visão geral, Comercial, Fábrica, Financeiro, Pessoas (sem dado sensível) e Configurações só para ler.
- **Não vê / não faz:** não paga (`finance.pay`): quem aprova não paga. Não cancela nota fiscal (fica com o Proprietário). Não vê salário nem dado de saúde. Não mexe em usuários.
- **Regras:** 2FA obrigatório (tem Aprovar). Cada aprovação respeita a faixa de valor em Alçadas.

### Financeiro (do sistema)
- **Perfil:** administrativo financeiro.
- **Módulos e ações:** Caixa e pagamentos para ver, criar, editar e **pagar** (`finance.pay`); Fiscal para operar as notas (`fiscal.view`, `fiscal.create`, `fiscal.edit`); custo e margem de orçamentos e projetos; cadastros; ver estoque e compras; painel.
- **Vê no dock:** Visão geral, Comercial (orçamentos e projetos só para ler, cadastros), Fábrica (estoque e compras só para ler), Financeiro.
- **Não vê / não faz:** não aprova a própria ordem de pagamento nem nenhuma outra (não tem `finance.approve`); não cancela nota (pede o cancelamento); não concilia o extrato que ele mesmo importou; não paga os salários de uma folha que ele fechou. Sem RH: não vê salário.
- **Regras:** 2FA obrigatório (tem Pagar). O sistema pede o código de novo para pagar e para lançar saída sem ordem na conciliação.

### Comercial (do sistema)
- **Perfil:** vendedor, orçamentista.
- **Módulos e ações:** Orçamentos para ver, criar e editar, com **Ver custo e margem**; Projetos para acompanhar, com custo e margem; Cadastros de clientes.
- **Vê no dock:** Comercial.
- **Não vê / não faz:** não aprova o orçamento nem gera o contrato (sem `quotes.approve`); não aprova aditivo; não vê financeiro, fábrica nem RH.
- **Opção:** se a Konebras não quiser que o vendedor veja a margem, tire `quotes.view_cost` e `projects.view_cost` do grupo. Ele continua vendo o preço.

### Engenharia (do sistema)
- **Perfil:** projetista.
- **Módulos e ações:** Engenharia completa, inclusive **liberar a BOM** (`engineering.approve`); Produção para criar e editar OS (inclusive a OS de instalação a partir do projeto); Projetos para ver e editar; ver estoque e cadastros.
- **Vê no dock:** Comercial (projetos e cadastros), Fábrica (Engenharia, Produção, Estoque).
- **Não vê / não faz:** não conclui OS nem decide qualidade; não vê custo e margem; não vê financeiro.
- **Regras:** 2FA obrigatório (tem Aprovar para liberar a BOM).

### Supervisor de fábrica (do sistema: "Supervisão de produção")
- **Perfil:** encarregado da fábrica.
- **Módulos e ações:** Produção completa, com **Aprovar** (`production.approve`: concluir OS, concluir etapa, registrar aceite técnico) e **Aprovar ou reprovar a qualidade** (`production.approve_quality`); estoque para ver e lançar; Painel da TV (criar e revogar telas); App do operador; Patrimônio para cadastrar bens e abrir manutenção; ver roteiros e projetos.
- **Vê no dock:** Comercial (projetos), Fábrica, Telas dedicadas.
- **Não vê / não faz:** não aprova a qualidade de uma OS em que apontou etapa; não vê custo e margem; não vê financeiro nem RH.
- **Regras:** 2FA obrigatório (tem Aprovar). Por ter Aprovar, entra sempre com e-mail e senha, nunca por PIN.

### Operador (do sistema: "Operadores")
- **Perfil:** chão de fábrica.
- **Módulos e ações:** App do operador (`shopfloor.view`, `shopfloor.create` = apontar).
- **Vê:** só o app `/fab` (fila, etapa, checklist, pedir material, Meu ponto). Fora do tablet, no celular: Meu ponto e Meus holerites.
- **Não vê / não faz:** nenhum valor, custo ou cliente além do necessário para a etapa; não aprova nada.
- **Regras:** entra por PIN de 6 dígitos no tablet registrado. O usuário precisa estar ligado a um funcionário ativo.

### Compras (criar)
- **Perfil:** comprador.
- **Módulos e ações:** Compras para ver, criar e editar (`purchasing.view`, `purchasing.create`, `purchasing.edit`): solicitação, cotação e pedido; cadastros de fornecedores; ver estoque e projetos.
- **Vê no dock:** Comercial (cadastros, projetos), Fábrica (Compras, Estoque).
- **Não vê / não faz:** não aprova o próprio pedido; não registra o recebimento do pedido que pediu (o sistema bloqueia); não paga.

### Almoxarifado (do sistema)
- **Perfil:** almoxarife.
- **Módulos e ações:** Estoque para ver, lançar e **ajustar** (`inventory.adjust`); **registrar recebimento** (`purchasing.receive`); importar a nota de entrada (`fiscal.import_incoming`); cadastros; ver produção e patrimônio.
- **Vê no dock:** Comercial (cadastros), Fábrica (Estoque, Compras, Produção, Patrimônio), Financeiro (Fiscal: notas de entrada).
- **Não vê / não faz:** não aprova o próprio ajuste nem lançamento manual acima de R$ 1.000,00 (vai para outra pessoa); não pede nem aprova compra.
- **Se a mesma pessoa compra e recebe:** coloque-a nos dois grupos. O sistema bloqueia o recebimento do pedido que ela mesma pediu ou aprovou: esse recebimento fica com outra pessoa do almoxarifado ou com o Proprietário.

### RH (do sistema)
- **Perfil:** departamento pessoal.
- **Módulos e ações:** RH para ver, criar e editar, com **Ver dados sensíveis** (`hr.view_sensitive`: salário, documentos) e **Ver dados de saúde** (`hr.view_health`: atestado, afastamento por doença). Recomendado: **Aprovar** (`hr.approve`) para decidir ajustes de ponto e fechar a folha.
- **Vê no dock:** Pessoas.
- **Não vê / não faz:** não aprova ajuste do próprio ponto; quem fecha a folha não paga os salários dela; não vê financeiro.
- **Regras:** com `hr.approve`, 2FA obrigatório e o sistema pede o código de novo para fechar a folha.

### Contabilidade (criar, opcional)
- **Perfil:** contadora externa.
- **Módulos e ações:** Fiscal para ver e editar regras em rascunho (`fiscal.view`, `fiscal.edit`); Caixa e pagamentos só para ver (`finance.view`: DRE, títulos, extratos); cadastros para ver.
- **Vê no dock:** Comercial (cadastros), Financeiro.
- **Não vê / não faz:** não ativa regra fiscal (o Proprietário ativa, com `fiscal.approve`); não paga, não aprova; não entra no RH: o pacote mensal da folha é gerado pelo RH em Exportar para a contadora.
- **Atenção:** `finance.view` mostra todos os títulos e extratos. Se a Konebras preferir, não crie o usuário: o RH manda o pacote da folha e o Financeiro exporta o que a contadora pedir.

## Quem aprova o quê (alçadas e segregação)

| Decisão | Ação no catálogo | Quem decide (recomendado) | Regra de segregação | 2FA |
|---|---|---|---|---|
| Aprovar orçamento e gerar contrato | `quotes.approve` | Proprietário ou Diretoria | — | sim |
| Aprovar aditivo de contrato | `projects.approve` | Proprietário ou Diretoria | — | sim |
| Liberar a BOM da OS | `engineering.approve` | Engenharia | — | sim |
| Decidir a qualidade | `production.approve_quality` | Supervisor | Quem apontou etapa da tentativa não aprova (sem outro aprovador: marca **Autoinspeção** e aviso ao Proprietário) | não |
| Concluir OS, concluir etapa sem apontamento, aceite técnico | `production.approve` | Supervisor | — | sim |
| Aprovar pedido de compra | `purchasing.approve` | Diretoria até a faixa; Proprietário acima | Quem pediu não aprova; valor de referência soma os pedidos do fornecedor em 30 dias | sim |
| Registrar recebimento | `purchasing.receive` | Almoxarifado | Quem pediu ou aprovou o pedido não recebe | não |
| Aprovar ajuste de estoque e lançamento manual acima de R$ 1.000,00 ou com custo 20% fora do médio | `inventory.approve` | Proprietário ou Diretoria | Outra pessoa, nunca quem lançou | sim |
| Aprovar ordem de pagamento | `finance.approve` | Diretoria até a faixa; Proprietário acima | Quem pediu não aprova (exceção: Proprietário único, com marca) | sim |
| Pagar ordem aprovada | `finance.pay` | Financeiro | Quem fechou a folha não paga os salários dela | sim, e de novo no pagamento |
| Estornar pagamento, desconto ou cancelamento acima do limite, fechar conciliação | `finance.approve` | Proprietário | Quem importou o OFX não concilia | sim |
| Cancelar ou inutilizar NF-e, ativar regra fiscal | `fiscal.approve` | Proprietário | Os outros usam **Pedir cancelamento** | sim, e de novo no cancelamento |
| Aprovar ajuste de ponto, fechar a folha | `hr.approve` | RH (responsável) ou Proprietário | Ninguém aprova ajuste do próprio ponto | sim, e de novo no fechamento |
| Aceitar competência da conferência paralela e virar a folha | Proprietário | Proprietário | — | sim |

**Alçadas sugeridas** (Financeiro → Alçadas, `/app/financeiro/alcadas`; valide os valores com o dono):

| Tipo | Nível | Faixa | Aprovador |
|---|---|---|---|
| Ordem de pagamento | 1 | até R$ 20.000,00 | Grupo Diretoria / Gerência |
| Ordem de pagamento | 1 | acima de R$ 20.000,00 | Grupo Proprietário |
| Ordem de pagamento | 2 | acima de R$ 50.000,00 | Grupo Proprietário (depois da Diretoria) |
| Pedido de compra | 1 | até R$ 15.000,00 | Grupo Diretoria / Gerência |
| Pedido de compra | 1 | acima de R$ 15.000,00 | Grupo Proprietário |

Sem Diretoria, mantenha a regra padrão "Proprietário aprova tudo".

## O que a auditoria mostra

- Todo documento tem a aba **Histórico**: quem criou, mudou, aprovou, pagou ou cancelou, quando, e o antes e o depois.
- Documento com movimento (estoque, financeiro, fiscal) nunca é apagado: é cancelado ou estornado, com motivo.
- O Proprietário é avisado de eventos sensíveis: autoaprovação, autoinspeção, tela de TV criada ou revogada, lançamento sem ordem de pagamento, importação de extrato.

Ajuda das telas: [acessos-grupos](../../Ajuda/acessos-grupos.md) · [acessos-usuarios](../../Ajuda/acessos-usuarios.md) · [financeiro-alcadas](../../Ajuda/financeiro-alcadas.md) · [configuracoes-seguranca](../../Ajuda/configuracoes-seguranca.md) · [modulo-nao-liberado](../../Ajuda/modulo-nao-liberado.md)
