# Site público do BemAqui Empresas

Site estático (HTML + CSS, sem JavaScript) para https://empresas.bemaquiapp.com.br. Exigido nos cadastros de desenvolvedor dos marketplaces (Amazon SP-API, Shopee, Magalu). Público, sem login.

Arquivos: `index.html`, `privacidade.html`, `termos.html`, `style.css`.

## DNS
Registro **A** `empresas.bemaquiapp.com.br` apontando para `2.25.82.127` (VPS). **Sem proxy** (nuvem cinza, se o DNS estiver na Cloudflare), para o certbot validar.

## Publicação (stack-nginx existente)
1. Copiar a pasta para a VPS, por exemplo `/srv/sites/empresas/` (somente os 4 arquivos do site).
2. Criar o server block no `stack-nginx`, como os demais vhosts:

```nginx
server {
    listen 80;
    server_name empresas.bemaquiapp.com.br;
    location /.well-known/acme-challenge/ { root /var/www/certbot; }
    location / { return 301 https://$host$request_uri; }
}
server {
    listen 443 ssl;
    http2 on;
    server_name empresas.bemaquiapp.com.br;
    ssl_certificate     /etc/letsencrypt/live/empresas.bemaquiapp.com.br/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/empresas.bemaquiapp.com.br/privkey.pem;
    root /srv/sites/empresas;
    index index.html;
    location / { try_files $uri $uri.html $uri/ =404; }   # /privacidade e /termos sem .html
    add_header X-Content-Type-Options nosniff;
    add_header Referrer-Policy strict-origin-when-cross-origin;
}
```
3. Montar o diretório no container do nginx (volume somente leitura).
4. Emitir o certificado (webroot, como nos outros vhosts) antes de ativar o bloco 443:
   `certbot certonly --webroot -w /var/www/certbot -d empresas.bemaquiapp.com.br`
5. `nginx -t` e recarregar. Conferir `https://empresas.bemaquiapp.com.br`, `/privacidade` e `/termos` (sem aviso de segurança, sem login).

Os caminhos do nginx e do certbot acima são exemplos; ajuste ao padrão dos vhosts atuais (`D:\GIT\Konebras\CLAUDE.md`).

## Placeholders a preencher
- `index.html`: e-mail `contato@bemaquiapp.com.br` (confirmar que existe e é monitorado; marcado em comentário).
- `privacidade.html`: `[nome e e-mail do encarregado]`; `[prazo a confirmar]` (dados de comprador de marketplace e dados da conta); `[confirmar localização dos provedores]` (transferência internacional); `[revisão jurídica das bases por finalidade]`; data de atualização.
- `termos.html`: `[comarca]`; `[metas de disponibilidade (SLA) a definir]`; `[horários e prazos a definir]` (suporte); `[prazo de exportação a definir]`; `[revisão jurídica]` (limitação de responsabilidade); data de atualização.
- Remover o aviso "Minuta para revisão jurídica" das duas páginas legais só após a revisão do advogado.
- Conferir a razão social completa no cartão CNPJ (rodapé).
