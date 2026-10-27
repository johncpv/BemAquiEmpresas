---
titulo: Emissor fiscal
rota: /app/configuracoes/integracoes/emissor-fiscal
aba: integracoes-emissor-fiscal
padrao: true
modulo: fiscal
---

## Para que serve
Ligar o BemAqui ao **Emissor Fiscal**, o serviço que monta, assina e transmite as notas à SEFAZ. O BemAqui calcula os impostos e manda a nota pronta; o Emissor guarda o certificado A1, numera, transmite e avisa quando a SEFAZ responde. Cada empresa do BemAqui tem a própria conta no Emissor, com uma chave só dela.

## Como usar
1. Peça ao administrador do Emissor Fiscal uma **chave de API sem o escopo admin**, com os escopos emitir NF-e, cancelar e corrigir NF-e e consultar.
2. Peça também que ele cadastre o **aviso (webhook)** no painel do Emissor com o **Endereço do aviso** que esta tela mostra depois de salvar. O Emissor mostra o **segredo do aviso** uma única vez: copie.
3. Clique em **Configurar**, informe o endereço interno do Emissor (normalmente `http://emissor-fiscal:8080`), a chave e o segredo e clique em **Salvar integração fiscal**. O sistema pede o código do aplicativo autenticador. Aparece "Integração fiscal salva".
4. Clique em **Testar conexão**. O sistema confere a chave no Emissor e mostra o emitente, o ambiente e os escopos. Aprovado, aparece "Conexão testada" e a integração fica **Ativa**. O próximo passo é **Configurar o fiscal da empresa**.

Só o Proprietário salva e testa. Quem só vê as Configurações enxerga a tela sem os botões.

## Segurança
- A chave e o segredo são guardados cifrados e **nunca são mostrados de novo**: a tela diz só a data em que foram configurados. O histórico registra que mudaram, sem o valor.
- O endereço só aceita o serviço do Emissor na rede interna. Endereço de outro serviço, IP ou `localhost` é recusado.
- Para **trocar a chave**, cole a nova em "Trocar a chave": ela é testada antes de substituir a atual. Recusada, a atual continua valendo. Aceita, a antiga deixa de ser usada na hora, e os membros do Proprietário recebem um aviso.
- Ao **trocar o segredo do aviso**, o anterior continua valendo por 24 horas, o mesmo tempo que o Emissor assina com os dois.

## Situações
- **Não configurada**: falta a chave e o segredo.
- **Aguardando o teste**: salva, mas ainda sem o teste aprovado. Nenhuma nota sai.
- **Recusada no teste**: o motivo aparece em vermelho, com o que fazer.
- **Ativa**: a chave foi conferida e as notas podem ser emitidas.

## Perguntas frequentes
**Apareceu "Use uma chave sem o escopo admin."** A chave tem poder de administrador no Emissor. Peça uma chave nova só com emitir, eventos e consultar e troque aqui.

**Apareceu "Faltam os escopos ... nesta chave."** Peça ao administrador do Emissor que inclua os escopos listados.

**Apareceu "O emitente do Emissor Fiscal não é o CNPJ desta empresa."** A chave é de outra empresa. Confira com o administrador do Emissor.

**Apareceu "A emissão desta empresa só liga na virada para a produção."** O ambiente fiscal desta empresa ainda não foi liberado. A homologação fica na empresa de demonstração, e a produção liga na virada do sistema.

**O que é a faixa "Ambiente de homologação da SEFAZ"?** O emitente está no ambiente de teste da SEFAZ: as notas saem, mas não têm valor fiscal.

**Uma nota está "esperando a SEFAZ". Preciso emitir de novo?** Não. O sistema confere a nota sozinho a cada 10 minutos e nunca emite de novo; se a espera passar de 2 horas, quem edita o Fiscal recebe um aviso.
