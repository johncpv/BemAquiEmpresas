---
titulo: Banco Inter
rota: /app/configuracoes/integracoes/inter
aba: integracoes-inter
padrao: true
modulo: settings
---

## Para que serve
Ligar cada conta do Banco Inter ao sistema, uma vez, para emitir boletos e ler o extrato sem digitar nada no banco. A lista mostra as contas cadastradas com o banco 077 e a situação de cada uma.

## Como usar
- Toque na conta para abrir o detalhe e **Configurar integração**.
- Ainda não tem a conta aqui? Cadastre em Financeiro → **Contas e caixas**, com o banco **077 - Banco Inter**. Ela aparece nesta lista.
- Conta com o certificado perto de vencer abre direto em **Trocar credenciais**.

## Situações
- **Não conectada**: a conta existe, mas a integração ainda não foi configurada (ou foi desligada).
- **Sandbox**: ligada ao ambiente de teste do Inter. Nenhum boleto é cobrança real.
- **Produção**: ligada ao Inter de verdade. A cobrança real só começa na virada do sistema.
- **Certificado vence em breve**: faltam 30 dias ou menos. Troque as credenciais antes de vencer.
- **Certificado vencido**: a emissão de boletos para até as credenciais serem trocadas.

## Perguntas frequentes
**Quem pode configurar?** Só os membros do Proprietário, com a confirmação do código da verificação em duas etapas. Quem vê o Financeiro ou as Configurações enxerga a situação, sem nenhum segredo.

**Posso ligar mais de uma conta do Inter?** Pode. Cada conta tem o seu client id, o seu client secret e o seu certificado.
