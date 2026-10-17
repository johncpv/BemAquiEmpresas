# Runbook: monitoramento, logs e alertas (FND-12)

## Health checks
| Endpoint | O que verifica | Uso |
|---|---|---|
| `/health/live` | Só se o processo responde. Não consulta dependência nenhuma. | Healthcheck do Docker |
| `/health/ready` | Catálogo (PostgreSQL), ClamAV, escrita no storage de arquivos e servidor do Hangfire | Monitor externo e deploy |

- Os dois são anônimos e devolvem só `Healthy` ou `Unhealthy`, sem detalhar cada componente. O detalhe por componente aparece apenas no host da plataforma, para super admin (FND-13).
- Para desligar uma verificação: `Health__ClamAv=false`, `Health__Storage=false` ou `Health__Hangfire=false`. Use só em ambiente sem esse componente.

## Monitor externo (fora da VPS)
O monitor fica no UptimeRobot, plano gratuito (ADR 0010, item 14), fora da VPS: se a VPS cair, o aviso chega mesmo assim.

1. Crie um monitor **HTTP(s)** para `https://<host>/health/ready`, com intervalo de 5 min.
2. Configure o alerta para o e-mail do PO depois de **2 checagens seguidas** com falha (CA 10).
3. Repita para cada ambiente: homologação e produção.

## Logs
- **Formato:** fora de Development, a saída é JSON (Compact) no console e, se `Logging__File__Path` estiver configurado, também em arquivo. Exemplo: `/data/logs/api-.json`.
- **Rotação do arquivo:** diária, ou quando passa de `Logging__File__FileSizeLimitBytes` (padrão 100 MB). Os arquivos ficam por `Logging__File__RetainedDays` dias (padrão 30) e depois são apagados.
- **Campos de cada request:**
  - `TenantId`, `UserId` e `CorrelationId` (o TraceId);
  - `RequestPath`, `StatusCode` e `Elapsed`.
- **Campos extras por origem:**
  - jobs: `JobId` e `JobType`;
  - outbox: `MessageId` e `Handler`.
- **Máscara:** todo `Secret` e toda propriedade com nome de segredo (password, senha, token, authorization, cookie, secret, apikey, connectionstring) aparecem como `***`, inclusive dentro de objetos. Cabeçalhos e corpo de request nunca são logados.

## Alertas
- **Destinatários:** `Alerts__Recipients__0=...`, `Alerts__Recipients__1=...`. São os super admins até a FND-13 trazer o cadastro deles.
- **Quando disparam:**
  - job que esgota as tentativas;
  - mensagem de outbox que vai para dead-letter;
  - e-mail que fica como `Failed`;
  - migrator que falha em algum tenant (o relatório vai junto);
  - integração externa que falha `Alerts__IntegrationFailureThreshold` vezes seguidas (padrão 5).
- **Deduplicação:** no máximo um e-mail por tipo e tenant a cada 30 min (`Alerts__DedupWindow`). As ocorrências são contadas em `catalog.platform_alerts`.
- **Link para o Hangfire:** `Alerts__PlatformBaseUrl=https://plataforma.bemaquiapp.com.br` faz o alerta de job incluir o link para o painel.
- **Log:** todo alerta também entra no log, como `ALERTA ...` com nível Error.
