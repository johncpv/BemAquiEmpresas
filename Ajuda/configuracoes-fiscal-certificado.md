---
titulo: Configurações — Certificado digital
rota: /app/configuracoes/fiscal/certificado
aba: fiscal-certificado
padrao: true
modulo: fiscal
---

## Para que serve
Envia o **certificado digital A1** (arquivo .pfx com senha) que assina as notas da empresa. O arquivo e a senha vão direto ao cofre do **Emissor Fiscal** e não ficam guardados no BemAqui: aqui ficam só o titular, o CNPJ, a validade e o thumbprint, para avisar do vencimento.

## Como usar
1. Conecte o Emissor Fiscal antes (Configurações → Integrações → Emissor fiscal, com **Testar conexão** aprovado).
2. Clique em **Enviar certificado**, escolha o arquivo .pfx (até 50 KB), digite a senha e o código do aplicativo autenticador.
3. O sistema confere se o arquivo tem a chave privada, se é A1, se está dentro da validade e se o CNPJ do titular é o da empresa. Se algo não bater, o motivo aparece no próprio formulário e nada é enviado.
4. Aceito, a tela mostra **Válido**, **Vence em {n} dias** (a partir de 30 dias) ou **Vencido**.

## Vencimento
- O Proprietário recebe aviso 30, 15 e 7 dias antes do vencimento, e o item **Certificado digital vencendo** entra na pauta dele.
- Vencido, nenhuma nota é emitida até enviar o novo.

## Perguntas frequentes
**Trocaram o certificado direto no Emissor Fiscal.** A tela confere o Emissor a cada hora (ou em **Conferir no Emissor Fiscal**) e mostra "O certificado no Emissor Fiscal mudou em {data}", já com os dados novos.

**O certificado é de outro CNPJ.** Use o e-CNPJ A1 da própria empresa; o de outro CNPJ (mesmo de uma filial ou do sócio) é recusado.

**Quem pode enviar?** Só quem edita Configurações (o Proprietário), com o código do autenticador. Há um limite de 5 envios por hora.
