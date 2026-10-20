# ADR 0020: Homologação promovida a produção

- Status: Aceito (20/10/2026). Decisão D1 do PO (John) na revisão pré-Fase 2, alternativa (b).
- Contexto: a homologação (`homolog.konebras.com.br`, ADR 0015) é separada da produção, que só liga na GOL-01 (Fase 5). Sem decisão, o PO lançaria títulos, saldos e contratos três vezes: no Dolibarr, na homologação e de novo na produção. A revisão recomendou duas saídas: (a) ligar a produção ao fim do Marco 2.1 só com Cadastros e Financeiro; (b) usar a homologação com os dados reais e promover o banco do tenant para produção na GOL-01. O PO escolheu a (b).
- Complementa as ADRs 0004, 0005 (D1), 0015 e 0018.

## Decisão
- **Os dados reais entram na homologação a partir da FIN-00** (implantação financeira): saldos das contas na data de corte, títulos em aberto, contratos em andamento (COM-04c) e o que vier depois.
- **Na GOL-01, o banco do tenant `konebras` da homologação vira o banco de produção.** Não há carga nova do financeiro nem dos documentos: só os cadastros que mudaram no Dolibarr depois da última carga (MIG-01b e MIG-01c reexecutáveis, ADR 0017).
- **`app.konebras.com.br` continua no Dolibarr até a GOL-01.** A ADR 0004 não muda nesse ponto, nem a ADR 0005 D1: a troca de host acontece na GOL-01.

## Consequências

### O tenant `konebras` da homologação passa a ter dado real de produção
- Valem as regras de acesso de produção da ADR 0018 ("Homologação com dados reais"): só usuários convidados, 2FA conforme a ADR 0010, sem usuário de teste compartilhado nem senha padrão, backup e restauração só pelo runbook, nenhum dump em claro fora da VPS.
- Nenhum acesso de desenvolvimento ao banco do tenant `konebras` fora do runbook. Diagnóstico com dado real só em cópia restaurada e descartada depois.

### Backup e restauração antes da primeira carga real
- **Pré-condição da FIN-00:** o backup diário cifrado da homologação (FND-10b) está no ar e verificado (último dump com tamanho e data esperados no bucket) e **um teste de restauração do tenant `konebras`** foi feito com o backup real, num ambiente separado, registrado em `docs/operacao/restauracao.md` (data, duração, conferência dos segredos).
- **Adendo de 20/10/2026 (PO):** a primeira carga real da FIN-00 está liberada sem esperar o teste de restauração. O backup diário continua obrigatório e o teste de restauração passa a ser recomendado (fazer assim que possível e depois mensalmente), sem bloquear a carga.
- Depois disso, a restauração é testada uma vez por mês (risco "Perda de até 24 h de dados", `06-pendencias-riscos.md`).

### Migrations
- Continua a regra: **nenhuma migration destrutiva sem ADR** (ADR 0006).
- Toda migration é testada **sobre uma cópia restaurada do banco do tenant `konebras`** antes do deploy na homologação (passo do runbook de deploy). Falha na cópia bloqueia o deploy.
- O padrão expand/contract (N-1) da ADR 0006 vale também para a homologação.

### Testes nunca escrevem no tenant `konebras`
- Testes manuais, E2E e smoke na homologação usam **outro tenant de teste** (ex.: `demo`, com host próprio de homologação), provisionado pela CLI, sem dado real.
- O smoke do deploy só lê (`/health/ready` e o `tenant-context`) no host da Konebras.
- O pipeline recusa rodar E2E contra o host do tenant `konebras`.

### GOL-01 passa a ser promoção
- A GOL-01 deixa de ser carga nova e passa a ser **promoção**: o banco do tenant `konebras` muda de ambiente (ou o ambiente de homologação vira o de produção) com:
  - DNS e domínio: `app.konebras.com.br` passa a apontar para o BemAqui; o Dolibarr vai para `adm.konebras.com.br` (ADR 0005 D1); o host primário do tenant em `catalog.tenant_domains` muda para `app.konebras.com.br`;
  - segredos de produção (assinatura do JWT, SMTP, conexão), trocados no momento da promoção;
  - chaves de Data Protection: o key ring da homologação acompanha o banco (os segredos do tenant são decifrados com ele), com a trilha de backup própria (ADR 0007);
  - o pipeline de produção (`deploy-prod.yml`) ligado.
- A carga final do Dolibarr na GOL-01 fica só com os cadastros alterados depois da última carga.
- O texto da GOL-01 no `05-backlog.md` foi ajustado.

### Riscos
- O ambiente de homologação recebe deploys frequentes com dado real: o risco é aceito com o teste da migration na cópia, o rollback automático do deploy (ADR 0015) e o backup diário.
- Um erro de teste no tenant real é mitigado pela regra "testes nunca escrevem no tenant `konebras`".
