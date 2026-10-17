# Runbook: provisionamento de tenant e migrações (FND-03a)

O executável da API também funciona como CLI (ADR 0006). Na VPS, ele roda pela mesma imagem, como `docker compose run --rm api <comando>`. No desenvolvimento, use `dotnet run --project src/Host/BemAqui.Host -- <comando>`.

## Pré-requisitos

- **Role de provisionamento** no PostgreSQL. Ele tem `CREATEDB` e `CREATEROLE`, mas **não** é superuser e é diferente do role da aplicação:
  ```sql
  CREATE ROLE bemaqui_provisioner LOGIN CREATEDB CREATEROLE NOSUPERUSER PASSWORD '<senha forte>';
  ```
  - Configuração: `ConnectionStrings__Provisioner=Host=...;Database=postgres;Username=bemaqui_provisioner;Password=...`.
  - Guarde essa senha num secret, nunca no repositório.
- **Catálogo e key ring** configurados, conforme `docs/operacao/segredos-e-chaves.md`.

## Provisionar um tenant

```bash
tenant provision --key konebras --legal-name "WSP Equipamentos por Elevação Ltda" \
  --document 47.225.005/0001-86 --host homolog.konebras.com.br \
  --owner-email dono@konebras.com.br --plan Piloto
```

O comando executa estas etapas, nesta ordem:

| Etapa | O que faz |
|---|---|
| Register | Grava o tenant como `Provisioning` no catálogo, com host e plano. A conexão vai cifrada. |
| CreateRole | Cria o role `tenant_<key>`: login, sem superuser, sem CREATEDB e sem CREATEROLE. A senha tem 32 bytes aleatórios. |
| CreateDatabase | Cria o banco `tenant_<key>` com esse role como dono e aplica `REVOKE CONNECT FROM PUBLIC`. |
| Migrate | Aplica as migrations do TenantDbContext e registra a versão no catálogo. |
| Seed | Roda os dados iniciais dos módulos. Todos os seeds são idempotentes. |
| CreateOwner / SendInvite | Cria o usuário proprietário e envia o convite. **Ainda provisórias:** passam a funcionar de verdade com a FND-04/06c e a FND-08. |
| Activate | Muda o tenant para `Active`, e ele passa a responder pelo host. |

- **Logs:** cada etapa grava `Started`, `Succeeded` ou `Failed` em `catalog.provisioning_logs`. Nenhum segredo vai para o log nem para a saída do comando.
- **Saída:** uma linha por etapa e, no fim, o próximo passo. Exemplo: "Convite enviado para … Acesse https://… para concluir o cadastro."
- **Recusas:** o comando recusa uma chave ou um host já usados antes de criar qualquer recurso.

### Se uma etapa falhar

O tenant fica em `Provisioning`, e a saída mostra a etapa e o erro. Corrija a causa e retome:

```bash
tenant provision --key konebras --resume
```

A retomada pula as etapas já concluídas e continua a partir da que falhou. Rodar a retomada de novo, com tudo concluído, não refaz nada.

## Migrar no deploy

```bash
migrate
```

1. Migra o catálogo e o key ring.
2. Migra o banco de cada tenant ativo ou suspenso. Cada banco é protegido por advisory lock, então dois migrators em paralelo não migram o mesmo banco ao mesmo tempo.
3. Imprime um relatório por tenant: versão anterior, versão nova e duração.

**Código de saída:** `0` quando tudo deu certo, `2` quando algum tenant falhou. Uma falha num tenant não impede os outros.

**Quando o banco de um tenant fica abaixo da versão do build,** a API responde `503 tenant_schema_outdated` para esse tenant até a migração terminar. Um schema **mais novo** que o build é aceito, porque o padrão expand/contract mantém a versão anterior da API compatível.

**Ordem no deploy:** primeiro `migrate`, depois `docker compose up -d api`. Em homologação e produção a API não migra sozinha (`Catalog:AutoMigrate` e `DataProtection:AutoMigrate` ficam `false`).

**Migrations destrutivas:** remover ou renomear tabela, coluna ou schema exige uma ADR citada no arquivo da migration. Um teste de arquitetura reprova o CI se ela faltar.

## Desenvolvimento local

Com `docker compose up -d postgres`, o usuário `bemaqui` do compose também serve como provisionador local. Para criar um tenant que responde em `localhost`:

```bash
ConnectionStrings__Provisioner="Host=localhost;Database=postgres;Username=bemaqui;Password=bemaqui_dev" \
ASPNETCORE_ENVIRONMENT=Development \
dotnet run --project src/Host/BemAqui.Host -- tenant provision --key dev --legal-name "Empresa Dev" \
  --host localhost --owner-email voce@exemplo.com
```
