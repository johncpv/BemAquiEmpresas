---
titulo: NFS-e da instalação
rota: /app/fiscal/saida/nfse/$id
aba: dados
padrao: true
modulo: fiscal
---

## Para que serve
Faturar o serviço de instalação quando o cliente aceita a entrega técnica. A NFS-e nasce da OS de instalação com o aceite técnico, já preenchida: o tomador (cliente do projeto), os serviços do contrato que ainda não saíram em nota, a discriminação, o município da obra e o ISS, as retenções e o IBS/CBS pela regra fiscal de prestação de serviço. A NF-e do equipamento do mesmo projeto fica vinculada.

## Como usar
1. Confira o tomador, o serviço e o ISS. Ajuste a discriminação, se precisar, e o envio ao cliente, e clique em **Salvar rascunho**. O imposto nunca é digitado: ele sai da regra fiscal.
2. **Com o Emissor Fiscal emitindo a NFS-e do município**: clique em **Emitir NFS-e**. O diálogo mostra o tomador, o valor, o ISS, o que entra no custo do projeto, o ambiente e o que falta resolver. Depois do envio, a tela mostra "NFS-e enviada. Aguardando a prefeitura…" e confere sozinha; nunca emita de novo enquanto processa.
3. **Processo provisório pelo portal da prefeitura** (enquanto o Emissor Fiscal não emite a NFS-e de Caçapava): siga a seção abaixo.
4. Autorizada ou registrada, a nota fica em Documentos e vai para o e-mail de cobrança do cliente (se "Enviar ao cliente" estiver marcado).

## Processo provisório pelo portal da prefeitura
Até o Emissor Fiscal emitir a NFS-e de Caçapava, a nota é emitida no portal da prefeitura e registrada aqui:
1. Abra o portal da prefeitura pelo link da tela e entre com o usuário da empresa.
2. Na área **Para copiar no portal da prefeitura**, use **Copiar** em cada campo (tomador, CPF ou CNPJ, discriminação, código do serviço, item da LC 116, município da prestação, valor, alíquota e ISS) e cole no portal.
3. Emita a nota no portal e baixe o PDF ou o XML.
4. Volte aqui e clique em **Registrar NFS-e emitida na prefeitura**. Informe o número, o código de verificação, a data de emissão, o valor do serviço e o ISS **como saíram na nota da prefeitura** e anexe o PDF ou o XML.
5. Confirme. A nota fica "Registrada (portal da prefeitura)" e conta no faturamento e no ISS do projeto pela data da nota. Se o ISS da nota for diferente do calculado, a tela avisa: confira com a contadora.

## Cancelamento
Enquanto o Emissor Fiscal não cancela NFS-e, cancele no portal da prefeitura e registre aqui com **Registrar cancelamento feito na prefeitura**: motivo, número do protocolo e o código do aplicativo autenticador. Só quem aprova no Fiscal registra. A nota fica cancelada, o imposto sai do custo do projeto e nada é apagado.

## Perguntas frequentes
**Por que o ISS é do município da obra?** A instalação (item 7.02 da LC 116) paga ISS onde o serviço é prestado. Por isso o projeto precisa do endereço da obra com o código IBGE.

**A NFS-e cria título a receber?** Não. A parcela do contrato é só cobrança; a receita do serviço conta pela data da nota.

**Antes da nota, o projeto fica sem ISS?** Não. Até a NFS-e, o projetado usa o ISS do orçado; com a nota, passa a ser o da nota.

**Onde ficam o usuário e a senha da prefeitura?** Só no Emissor Fiscal. O BemAqui não recebe nem guarda as credenciais da prefeitura.

**Registrei com o número errado.** Número repetido é recusado. Se a nota já foi registrada com dado errado, cancele no portal e registre o cancelamento; depois emita e registre a nota certa.
