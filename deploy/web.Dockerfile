# Front (backoffice, PWA e painel da TV): build estático servido por nginx não-root. Contexto: raiz do repositório.
FROM node:24-alpine AS build
WORKDIR /web
RUN corepack enable
COPY src/Web/package.json src/Web/pnpm-lock.yaml src/Web/.npmrc ./
RUN pnpm install --frozen-lockfile
# Galeria de componentes e moldes (FND-09d): ligada só no build da homologação.
ARG VITE_GALERIA=false
ENV VITE_GALERIA=$VITE_GALERIA
COPY src/Web/ ./
# A ajuda da /fab/entrar vem de Ajuda/fab-entrar.md, embutida no app (PRD-03b): mesmo caminho relativo do repositório (/web/../Ajuda).
COPY Ajuda/ /Ajuda/
RUN pnpm build

# nginx-unprivileged: roda como uid 101, escuta na 8080 e grava o pid em /tmp.
FROM nginxinc/nginx-unprivileged:1.29-alpine
COPY deploy/web/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /web/dist /usr/share/nginx/html
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --retries=3 CMD wget -qO /dev/null http://127.0.0.1:8080/ || exit 1
