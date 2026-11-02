# Manual da Contadora (Fiscal e Contabilidade)

**Grupo:** Contabilidade (criar no sistema, opcional). **Ações recomendadas:** `fiscal.view`, `fiscal.edit` (regras em rascunho); `finance.view` (DRE, títulos, extratos); `parties.view`.

O BemAqui faz a gestão da Konebras; a contabilidade oficial continua com você. O sistema entrega os números e os arquivos para você conferir, sem retrabalho. Se a Konebras preferir não criar o seu usuário, o RH e o Financeiro enviam os arquivos abaixo.

## O que a Konebras precisa de você

### Uma vez, na implantação

| Tarefa | Tela | Ajuda |
|---|---|---|
| Conferir os parâmetros fiscais pré-configurados "a confirmar" (regime, série, crédito de impostos na entrada) | Sistema → Configurações → Fiscal `/app/configuracoes/fiscal` | [configuracoes-fiscal](../../../Ajuda/configuracoes-fiscal.md) |
| Conferir as regras fiscais por operação (venda de equipamento, instalação, NFS-e e ISS do município da obra) | Financeiro → Fiscal → Regras fiscais `/app/fiscal/regras` | [fiscal-regras](../../../Ajuda/fiscal-regras.md) · [fiscal-regra](../../../Ajuda/fiscal-regra.md) |
| Confirmar o % de encargos sobre a folha (usado no custo-hora) | Configurações → Parâmetros `/app/configuracoes/parametros` | [configuracoes-parametros](../../../Ajuda/configuracoes-parametros.md) |
| Conferir as colunas do pacote mensal do RH ("Layout do pacote para a contadora") | Pacote gerado pelo RH | [rh-exportar-contadora](../../../Ajuda/rh-exportar-contadora.md) |
| Revisar o plano de contas gerencial e as contas do DRE (juros, multas, descontos) | Financeiro → Plano de contas `/app/financeiro/plano-de-contas`; Parâmetros → Financeiro | [financeiro-plano-de-contas](../../../Ajuda/financeiro-plano-de-contas.md) |

A regra que você ajustar fica em rascunho. Quem ativa é o Proprietário (`fiscal.approve`).

### Todo mês

| Tarefa | Tela | Ajuda |
|---|---|---|
| Receber o pacote do RH (movimentação, folha e ponto fechados) e calcular a folha | ZIP `contadora-aaaa-mm-v2.zip`, enviado pelo RH | [rh-exportar-contadora](../../../Ajuda/rh-exportar-contadora.md) |
| Devolver a folha calculada para o RH importar | Folha lançada, importação dos valores | [rh-folha-detalhe](../../../Ajuda/rh-folha-detalhe.md) |
| Até a virada: assinar o **Termo de conferência** de cada competência aceita | Conferência da folha `/app/rh/conferencia-folha` (o Proprietário baixa o termo) | [rh-conferencia-folha](../../../Ajuda/rh-conferencia-folha.md) |
| Conferir as notas de saída (NF-e) e de serviço (NFS-e) do mês | Fiscal → saída `/app/fiscal/saida` | [fiscal-saida-emitidas](../../../Ajuda/fiscal-saida-emitidas.md) · [fiscal-nfse](../../../Ajuda/fiscal-nfse.md) |
| Conferir as notas de entrada | Fiscal → Notas de entrada `/app/fiscal/entrada` | [fiscal-entrada](../../../Ajuda/fiscal-entrada.md) |
| Ler o DRE gerencial e cruzar com a contabilidade | Financeiro → DRE `/app/financeiro/dre` | [financeiro-dre](../../../Ajuda/financeiro-dre.md) |
| Conferir extratos e conciliação | Financeiro → Contas e caixas; Conciliação | [financeiro-extrato](../../../Ajuda/financeiro-extrato.md) · [financeiro-conciliacao](../../../Ajuda/financeiro-conciliacao.md) |

## Entre as telas

- **O DRE é gerencial, por competência.** A receita entra pela data da nota; a compra para estoque entra em Custos no mês da nota do fornecedor; guias de imposto (DARF, GARE, DAS) entram no DRE, não no projeto. O sinal do contrato fica como adiantamento até a expedição.
- **Clique na célula do DRE** para ver os documentos que somam o valor: títulos, lançamentos, baixas.
- **Imposto nunca é digitado na nota:** sai da regra fiscal. Se o ISS de uma NFS-e registrada pelo portal da prefeitura vier diferente do calculado, a tela avisa para conferir com você.
- **NFS-e de Caçapava:** enquanto o emissor fiscal não emite a NFS-e do município, a nota é emitida no portal da prefeitura e registrada no BemAqui com número, código de verificação e ISS como saíram na nota.
- **Folha:** até a virada, a oficial é a sua (lançada). O sistema calcula em paralelo e só vira depois de 3 competências seguidas com diferença zero e o termo assinado por você. Férias, 13º e rescisão calculados estão em finalização; até lá, os valores são os seus.

## Os 5 erros mais comuns

1. **Comparar o DRE com o resultado por projeto.** Não fecham por regra: o projeto soma pelo consumo (material baixado na OS), o DRE pela data do documento.
2. **Deixar parâmetro fiscal "a confirmar" sem revisar.** Ele vale como está. Confira e registre a confirmação.
3. **Mudar o layout do pacote do RH por e-mail, sem registrar.** Se precisar de outra coluna, peça uma versão nova do layout: a planilha antiga continua valendo até lá.
4. **Classificar diferença da conferência paralela como "Arredondamento" para aceitar.** O aceite exige diferença zero em cada rubrica. Corrija a causa e compare de novo.
5. **Esperar o SPED pelo sistema.** O BemAqui não gera SPED contábil nem balanço: isso continua com você, a partir das exportações.
