# ADR 0032: Integração com o Banco Inter

- Status: Aceito (21/10/2026). O PO (John) aprovou a decisão D13 da revisão pré-Fases 4 a 8 (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`), os pontos 13 a 34 da tabela 3.2, os achados das seções 4.2 e 4.3 (BNK-01a, BNK-03a/03b) e a decisão D1 nova (Inter em produção só na GOL-01b).
- Contexto: as stories BNK-01a a BNK-03b ligam o BemAqui à API de Cobrança (bolepix), aos webhooks e ao extrato do Inter, com certificado mTLS. Faltava decidir se a integração é por tenant ou por conta, como o certificado é carregado (um `HttpClient` com certificado lido de disco a cada chamada ou guardado num handler global vazaria entre tenants), para onde a API pode sair, como o webhook prova a origem sem mTLS no nginx, quais escopos a credencial pode ter e o que fazer com o OFX quando a conta já tem API.
- Complementa as ADRs 0007 (segredos, adendo de 21/10/2026), 0008 (2FA de novo, adendo de 21/10/2026), 0031 (recepção de webhooks), 0034 (promoção a produção) e 0042 (Configurações → Integrações → Banco Inter).

## Decisão

### 1. Integração por conta bancária
- `finance.bank_integrations`, **uma por conta** (`finance.bank_accounts.provider = Inter`): client id, client secret e certificado mTLS (`.crt` + `.key` ou `.pfx`) cifrados (`inter-credentials` e `inter-certificate` com o id da integração, ADR 0007), conta, ambiente (`Sandbox`/`Production`), escopos recebidos, validade e thumbprint do certificado, último teste.
- Configurada em **Configurações → Integrações → Banco Inter** (ADR 0042, BNK-01a), com `settings.edit` e **2FA de novo** ao salvar credenciais ou certificado (ADR 0008, adendo).
- Credenciais e certificado são **só de escrita**, com canário no log; a credencial nova é **testada antes de substituir** a atual ("Testar conexão" com a nova; falha mantém a anterior).
- Alerta de vencimento do certificado ao Proprietário (30, 15 e 7 dias).

### 2. Handler mTLS em memória
- **Um `SocketsHttpHandler` por integração**, em cache pelo **thumbprint** do certificado, com o certificado carregado **só em memória** (`X509Certificate2` com `EphemeralKeySet`, nunca gravado em disco nem no store do sistema).
- Na rotação, o handler antigo é descartado depois das chamadas em curso; o cache nunca mistura integrações (a chave é o thumbprint mais o id da integração; teste com duas contas e dois tenants).
- Token OAuth do Inter em cache por integração, até o vencimento menos 60 s.
- **Proibido `ServerCertificateCustomValidationCallback` permissivo** (que aceite qualquer certificado do servidor) fora dos projetos de teste: teste de arquitetura que reprova a atribuição em qualquer assembly de produção.

### 3. Saída de rede restrita
- **URL base fixa** por ambiente (sandbox e produção do Inter), na configuração do ambiente, nunca informada pela tela (contra SSRF).
- **Saída liberada só para os hosts do Inter**: o `HttpClient` da integração recusa outro host (handler de allowlist), e a regra de saída do container fica documentada no runbook da VPS.

### 4. Escopos
- A credencial só pode ter os escopos de **cobrança, extrato e webhook**. Credencial com escopo de **pagamento ou envio de Pix** é **recusada** no teste de conexão ("Esta credencial pode pagar ou enviar Pix. Gere uma credencial só com cobrança, extrato e webhook."). O BemAqui não paga pelo Inter no MVP.

### 5. Webhook
- Provedor `inter` da ADR 0031: **token `?t=`** na URL (só o hash guardado; mostrado uma vez na tela para o cadastro no Inter), `tenantKey` opaco, deduplicação também pelo SHA-256 do corpo.
- Se o sandbox mostrar que o Inter não aceita query na URL, o token vai no último segmento do caminho, com máscara desse segmento no log do nginx (BNK-02).
- **Reconsulta por mTLS antes de qualquer baixa:** o corpo do aviso só identifica a cobrança; a situação, o valor e a data vêm da API.
- **Pagamento com diferença** (valor pago diferente do esperado, fora de multa e juros calculados) vai para **conferência humana** (`PaidPendingReview`), sem baixa automática.
- Resumo por hora quando chegam mais de 5 avisos.

### 6. Cobrança
- "Seu número" curto e opaco (sequência por conta em base 36), conferido no sandbox.
- Multa 2% e juros 1% a.m. no seed, editáveis (Configurações → Cobrança, ADR 0042); **desconto, multa e juros acima do limite da D8 exigem `finance.approve`** no título.
- **Estorno de baixa bancária exige `finance.approve`** e 2FA de novo (ADR 0008, adendo, D6).

### 7. Extrato e OFX
- Importação diária do extrato pela API às 06:30; o arquivo OFX bruto só no dono `BankStatementBatch`.
- **OFX bloqueado em conta com API**; liberável **por conta**, como contingência, com `finance.approve`. Linhas de OFX ficam marcadas **"não verificadas pelo banco"**.
- Parser de OFX sem DTD, com limite de tamanho, antivírus, hash do arquivo e FITID únicos; aviso ao Proprietário a cada importação; teto para "lançar sem OP" a partir de linha de OFX; ignorar débito exige `finance.approve`; desfazer exige motivo.
- **Conciliação:** quem confirma o par não é quem importou; fechamento por período com `finance.approve`.

### 8. Ambientes
- **Sandbox no tenant `demo`**; nada bancário real no `konebras` antes da GOL-01b.
- Produção só na **GOL-01b**; o teste de conexão com a credencial de produção é feito no **D−1** (ADR 0034).

## Consequências
- **BNK-01a** implementa as seções 1 a 4; **BNK-01b** a seção 6 e a tela de Cobrança; **BNK-02** a seção 5; **BNK-03a/03b** a seção 7.
- `docs/03-modelo-dados.md` registra `finance.bank_integrations` e `bank_accounts.provider`; `docs/02-arquitetura.md` (integrações) atualizado.

## Alternativas consideradas
- **Integração por tenant**, com uma credencial para todas as contas: rejeitada; o Inter emite credencial e certificado por conta, e uma credencial comprometida não deve alcançar as outras contas.
- **mTLS no nginx para o webhook:** rejeitada no MVP; o `stack-nginx` é compartilhado e não faz mTLS por location. O token na URL com reconsulta dá a mesma garantia de efeito.
