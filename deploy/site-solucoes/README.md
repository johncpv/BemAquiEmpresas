# Site institucional da BemAqui Soluções (solucoes.bemaquiapp.com.br)

Site estático (HTML + CSS, sem JavaScript), provisório até o institucional assumir `bemaquiapp.com.br`. Decisão dos sócios de 26/11/2026: cada subdomínio é uma aplicação, e cada produto tem página e políticas próprias aqui.

| Caminho | Conteúdo |
|---|---|
| `/` | Produtos do ecossistema e links das políticas de cada um |
| `/erp/` | Página do BemAqui ERP (é a URL informada aos marketplaces) |
| `/erp/privacidade`, `/erp/termos` | Política de privacidade e termos de uso do ERP (minuta para revisão jurídica) |

## Publicação

- Arquivos em `/opt/infra/nginx/static/solucoes/` na VPS. O `stack-nginx` monta `/opt/infra/nginx/static` em `/usr/share/nginx/static`.
- Vhost em `/opt/infra/nginx/conf.d/solucoes.conf`, com `try_files $uri $uri.html $uri/ =404`, para `/erp/privacidade` funcionar sem `.html`.
- Certificado por certbot webroot (`/opt/infra/nginx/certbot`, volume `stack_certbot-etc`), renovado pelo `/opt/infra/renovar-cert.sh`.
- Antes de cada reload: `docker exec stack-nginx nginx -t`.
- Para atualizar o conteúdo, basta copiar os arquivos de novo para a pasta; não precisa de reload.

## Pendências das minutas

Encarregado de dados (DPO), comarca, prazos (guarda de dados de comprador, suporte, disponibilidade, exportação), localização dos provedores e revisão jurídica.
