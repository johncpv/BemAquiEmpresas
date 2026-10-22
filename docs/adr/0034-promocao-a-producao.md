# ADR 0034: Promoção a produção e escopo do go-live

- Status: Aceito (21/10/2026). O PO (John) aprovou a decisão D7 da revisão pré-Fases 4 a 8 (`docs/revisoes/2026-10-21-revisao-pre-fases4-8.md`) inteira, com os pontos 74 a 81 da tabela 3.2, e **mudou a D1**: as Fases 4 a 8 inteiras entram **antes** do go-live, e a GOL-01a e a GOL-01b passam a ser as últimas stories, num marco final próprio, "Go-live".
- Contexto: a ADR 0020 fez da GOL-01 uma promoção: o banco do tenant `konebras` da homologação, que já tem dado real, vira o de produção, e o key ring acompanha o banco. A revisão mostrou que isso leva para a produção tudo o que a homologação acumulou: catálogo com o `demo` e outros tenants de teste, key ring e certificado do Data Protection que passaram por meses de deploys frequentes, sessões e refresh que não ficam presos ao host (o CA 3 da GOL-01b supunha o contrário), convites, PINs, aparelhos e quiosques de teste, a `DOLAPIKEY` do Dolibarr (a mesma do Konebras UI) e segredos espalhados sem inventário. A D1 original separava um go-live mínimo (Fase 3 + Fiscal + Banco) do resto; o PO preferiu entregar tudo antes da virada.
- **Revisa a ADR 0020** no ponto "o key ring da homologação acompanha o banco" e na forma da promoção (§2 abaixo); o resto da 0020 continua valendo. Complementa as ADRs 0004, 0005 (D1), 0007, 0015, 0017 (adendo 2), 0018, 0026, 0027 e 0032.

## Decisão

### 1. Escopo e ordem do go-live (D1 nova)
- **Go-live = Fases 0 a 8 concluídas.** Todas as stories das Fases 4 a 8 são executadas **antes** do go-live, na ordem de cada fase (Fase 4 inteira, FIS-04a/04b, FIS-05, FIS-06, FIN-09, Fases 6, 7 e 8). Não há exceção à regra "não comece uma fase sem a anterior" (o ponto 78 da Fase 5 fica decidido assim).
- **Marco final "Go-live"**, depois da Fase 8, com duas stories: **GOL-01a** (preparação, runbook e ensaio; exige "Fases 0 a 8 concluídas"; bloqueia a GOL-01b) e **GOL-01b** (virada e primeira semana; a última).
- **Continua valendo:** SEFAZ e Inter em produção **só na GOL-01b**. Até lá, homologação SEFAZ e sandbox do Inter no tenant `demo`; nada fiscal ou bancário real no `konebras` (ADRs 0027 e 0032). O rótulo "provisório" do faturamento por título continua até existirem notas reais.

### 2. Ambiente de produção novo
- A produção nasce num **ambiente novo** (`/opt/projetos/bemaqui-empresas/`, nomes `bqe-prod-*`, endurecimento da ADR 0015, rede `bqe-fiscal` com o EmissorFiscal de produção):
  - **catálogo novo, só com o tenant `konebras`** (o `demo` e os outros tenants de teste ficam na homologação);
  - **key ring e certificado do Data Protection novos**; os segredos do tenant (conexão, SMTP, TOTP, chave de IA) são **recifrados** do key ring da homologação para o novo por ferramenta do runbook (a recifração da FND-10b), no ensaio e na virada; os segredos de sandbox (Inter, EmissorFiscal de homologação, webhooks) **não** são migrados, são trocados;
  - **conexão do tenant cifrada de novo**, com usuário e senha do PostgreSQL novos;
  - o banco do `konebras` é restaurado do backup da homologação no ambiente novo (promoção pelo dado, não pelo ambiente).
- **Homologação depois da virada:** continua para testes com **key ring novo**, e o banco do `konebras` da homologação é **apagado** depois do primeiro fechamento mensal em produção (apagar dado de propósito, coberto por esta ADR), com registro em `docs/operacao/restauracao.md`.

### 3. Sessões e credenciais na virada
- **Revogar todas as famílias de refresh e subir o security stamp de todos os usuários** do `konebras` (o refresh não fica preso ao host: sem isso, uma sessão de homologação continuaria valendo em produção).
- Expirar **convites, redefinições de senha, PINs, aparelhos do chão de fábrica e telas de quiosque**; cada um é refeito em produção.
- Apagar usuários e convites de teste e **conferir a lista de super admins** da plataforma.
- **Webhooks e Inter de sandbox para produção:** `tenantKey`, segredos e tokens novos, cadastro de novo no Inter e no EmissorFiscal de produção; teste de conexão de produção do Inter no **D−1** (ADR 0032).

### 4. Dolibarr
- **Regenerar a `DOLAPIKEY`** no Dolibarr (é a mesma do Konebras UI) e **desativar os usuários** que a usavam; apagar o secret da importação (ADR 0017, adendo 2).
- Dolibarr em `adm.konebras.com.br` **só leitura**, com **allowlist de IP ou autenticação básica** no vhost.
- `DROP USER` do usuário somente leitura do banco do Dolibarr, com a **revogação na rede** (regra do firewall ou da rede Docker).
- Desligamento depois do primeiro fechamento mensal ou em até 3 meses, o que vier primeiro; backup do Dolibarr guardado por 5 anos.

### 5. Inventário de segredos
- Lista completa, com dono, local e data de rotação, sem nenhum valor, em `docs/operacao/segredos-e-chaves.md`: SSH da VPS, B2 (backup), chave `age`, GHCR, senhas do PostgreSQL (`ALTER ROLE` de cada usuário), assinatura do JWT, SMTP, Data Protection, EmissorFiscal (chave e chave-mestra do cofre, ADR 0027), Inter, WhatsApp, webhooks; **environment protection** no `deploy-prod.yml` (aprovação do PO para cada deploy de produção).

### 6. Critérios de entrada da GOL-01a
- Fases 0 a 8 `Done`.
- Trilha externa fechada (D2): P-01, **P-02** (CNAE de fabricação no cartão CNPJ, pré-condição da NF-e de produção própria), P-03, certificado A1 válido por mais de 60 dias no cofre do Emissor, conta Inter com API de produção (P-08), EmissorFiscal de produção com as lacunas da ADR 0027 fechadas.
- Campos fiscais críticos **conferidos** em Configurações → Fiscal (ADR 0042).
- P-19 e P-20 implementadas (2FA de novo e estorno com `finance.approve`, ADR 0008, adendo de 21/10/2026).
- **Contagem física do estoque** com saldo inicial na janela de implantação (D7 da Fase 3).
- Usuários do chão de fábrica com e-mail de apelido (P-24) e todo funcionário que marca ponto com e-mail.
- **Restauração e rollback ensaiados numa cópia, obrigatórios** (deixa de ser recomendação da ADR 0020).
- Linha de base da métrica "desvio da data prevista ≤ 15%" (ADR 0029).
- **Virada no início do mês, fora do horário comercial.**

## Consequências
- **GOL-01a** escreve o runbook com as seções 2 a 5, ensaia numa cópia restaurada (promoção, recifração, rollback) e confere os critérios da seção 6; **GOL-01b** executa a virada, a seção 3 e a primeira semana. O CA da GOL-01b que supunha o refresh preso ao host é corrigido.
- O `05-backlog` ganha o marco final "Go-live"; as stories das Fases 4 a 8 trocam "depois do go-live" por "antes do go-live, na ordem da fase" (salvo SEFAZ e Inter em produção e o desligamento do Dolibarr).
- `docs/operacao/go-live.md` (novo) e `segredos-e-chaves.md` atualizados na GOL-01a.

## Alternativas consideradas
- **Transformar a homologação em produção** (ADR 0020, forma original): rejeitada; leva catálogo, key ring, sessões e tenants de teste para a produção.
- **Go-live mínimo** (D1 original: Fase 3 + FND-16 + Banco + FIS-01 a FIS-03b, resto depois): rejeitada pelo PO, que prefere virar uma vez só com o produto completo.
